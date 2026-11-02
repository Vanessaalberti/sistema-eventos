const kState = {
  settings: {},
  active: [],
  finished: [],
  knownOrderIds: new Set(),
  firstLoadDone: false,
  isPrinting: false,
};

function padTicket(num) {
  return "#" + String(num || 1).padStart(3, "0");
}

function showToast(msg, isError = false) {
  const container = document.getElementById("toast-container");
  const el = document.createElement("div");
  el.className = "toast" + (isError ? " error" : "");
  el.textContent = msg;
  container.appendChild(el);
  setTimeout(() => el.remove(), 4000);
}

// Sonido de campana de cocina (Web Audio API sin archivos externos)
function playKitchenBell() {
  try {
    const ctx = new (window.AudioContext || window.webkitAudioContext)();
    const now = ctx.currentTime;
    [587.33, 880].forEach((freq, idx) => {
      const osc = ctx.createOscillator();
      const gain = ctx.createGain();
      osc.type = "triangle";
      osc.frequency.setValueAtTime(freq, now + idx * 0.15);
      gain.gain.setValueAtTime(0.25, now + idx * 0.15);
      gain.gain.exponentialRampToValueAtTime(0.001, now + idx * 0.15 + 0.45);
      osc.connect(gain);
      gain.connect(ctx.destination);
      osc.start(now + idx * 0.15);
      osc.stop(now + idx * 0.15 + 0.45);
    });
  } catch (e) {}
}

async function apiGet(url) {
  const res = await fetch(url);
  return res.json();
}

async function apiPost(url, data = {}) {
  const res = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(data),
  });
  return res.json();
}

function buildKitchenComandaHTML(order) {
  const s = kState.settings;
  const rows = (order.items || [])
    .map(
      (it) => `
      <div style="margin: 6px 0; border-bottom: 1px dotted #000; padding-bottom: 4px;">
        <div style="font-size: 16px; font-weight: 900;">[ ${it.quantity} ] ${it.product_name.toUpperCase()}</div>
        <div style="font-size: 11px;">Sector: ${it.category || "Cocina"}</div>
      </div>`
    )
    .join("");

  return `
    <div class="thermal-receipt">
      <div class="t-center">
        <div class="t-event">${s.event_name || "ASOCIACIÓN JUVENIL ARAUCANA"}</div>
        <div style="font-weight:800; font-size:14px;">*** COMANDA DE COCINA ***</div>
        <div class="t-number">PEDIDO ${padTicket(order.ticket_number)}</div>
        <div style="font-size:11px;">Hora: ${order.created_at}</div>
      </div>
      <div class="t-divider"></div>
      ${rows}
      ${
        order.notes
          ? `<div class="t-divider"></div><div style="font-size:14px; font-weight:900;">NOTA: ${order.notes}</div>`
          : ""
      }
      <div class="t-divider"></div>
      <div class="t-center" style="font-size:11px;">Entregar al cliente contra Ticket ${padTicket(order.ticket_number)}</div>
    </div>
  `;
}

async function printKitchenTicket(order, isAuto = false) {
  const widthMm = kState.settings.paper_width === "58" ? "54mm" : "74mm";
  document.documentElement.style.setProperty("--print-width", widthMm);

  document.getElementById("thermal-print-area").innerHTML = buildKitchenComandaHTML(order);

  // Marcar como impreso en base de datos para no repetir en el próximo ciclo
  await apiPost(`/api/kitchen/orders/${order.id}/printed`);

  if (kState.settings.print_mode in { network_escpos: 1, windows_raw: 1 } && !isAuto) {
    const res = await apiPost(`/api/orders/${order.id}/print-raw`);
    if (res.ok) {
      showToast(`Comanda ${padTicket(order.ticket_number)} enviada a la ticketera`);
      return;
    }
  }

  window.print();
}

async function setKitchenStatus(orderId, status, ticketNum) {
  const res = await apiPost(`/api/kitchen/orders/${orderId}/status`, { kitchen_status: status });
  if (res.ok) {
    kState.active = res.active || [];
    kState.finished = res.finished || [];
    renderKitchenBoard();
    if (status === "finalizado") {
      showToast(`✅ Pedido ${padTicket(ticketNum)} marcado como FINALIZADO`);
    } else if (status === "preparando") {
      showToast(`👨‍🍳 Pedido ${padTicket(ticketNum)} en preparación`);
    } else {
      showToast(`↩ Pedido ${padTicket(ticketNum)} devuelto a pendientes`);
    }
  }
}

function renderKitchenBoard() {
  document.getElementById("k-count-pending").textContent = kState.active.length;
  document.getElementById("k-count-finished").textContent = kState.finished.length;

  const grid = document.getElementById("kitchen-active-grid");
  grid.innerHTML = "";

  if (kState.active.length === 0) {
    grid.innerHTML = `
      <div class="empty-kitchen-board">
        <h3>No hay pedidos pendientes en cocina</h3>
        <p class="muted">En cuanto Caja cobre un nuevo pedido, aparecerá aquí automáticamente y sonará la alerta.</p>
      </div>
    `;
  } else {
    kState.active.forEach((order) => {
      const isPrep = order.kitchen_status === "preparando";
      const card = document.createElement("div");
      card.className = "k-order-card " + (isPrep ? "status-prep" : "status-pending");

      const itemsList = (order.items || [])
        .map(
          (it) => `
          <div class="k-item-row">
            <span class="k-qty">${it.quantity}x</span>
            <div class="k-item-text">
              <strong>${it.product_name}</strong>
              <small>${it.category}</small>
            </div>
          </div>
        `
        )
        .join("");

      card.innerHTML = `
        <div class="k-card-header">
          <span class="k-ticket-num">${padTicket(order.ticket_number)}</span>
          <span class="k-badge ${isPrep ? "prep" : "new"}">
            ${isPrep ? "👨‍🍳 EN PREPARACIÓN" : "🔔 NUEVO PEDIDO"}
          </span>
        </div>
        <div class="k-card-time">Recibido: ${order.created_at}</div>
        <div class="k-card-items">${itemsList}</div>
        ${order.notes ? `<div class="k-card-note">📝 <strong>Nota:</strong> ${order.notes}</div>` : ""}
        <div class="k-card-actions">
          ${
            !isPrep
              ? `<button class="btn btn-outline btn-sm" data-prep="${order.id}">👨‍🍳 Preparando</button>`
              : `<span class="muted" style="font-size:0.8rem;">Preparando...</span>`
          }
          <button class="btn btn-ghost btn-sm" data-print="${order.id}">🖨️ Ticket</button>
          <button class="btn btn-primary k-btn-done" data-done="${order.id}">✅ FINALIZADO</button>
        </div>
      `;

      const prepBtn = card.querySelector(`[data-prep="${order.id}"]`);
      if (prepBtn) {
        prepBtn.onclick = () => setKitchenStatus(order.id, "preparando", order.ticket_number);
      }
      card.querySelector(`[data-print="${order.id}"]`).onclick = () => printKitchenTicket(order, false);
      card.querySelector(`[data-done="${order.id}"]`).onclick = () =>
        setKitchenStatus(order.id, "finalizado", order.ticket_number);

      grid.appendChild(card);
    });
  }

  // Tabla de finalizados recientes
  const tbody = document.getElementById("kitchen-finished-tbody");
  tbody.innerHTML = "";
  if (kState.finished.length === 0) {
    tbody.innerHTML = `<tr><td colspan="5" class="muted" style="text-align:center; padding:1.25rem;">Aún no hay pedidos finalizados.</td></tr>`;
    return;
  }

  kState.finished.forEach((o) => {
    const tr = document.createElement("tr");
    const summary = (o.items || []).map((i) => `${i.quantity}x ${i.product_name}`).join(", ");
    tr.innerHTML = `
      <td><strong>${padTicket(o.ticket_number)}</strong></td>
      <td class="muted">${o.created_at}</td>
      <td><strong style="color:var(--primary);">${o.finished_at || "Listo"}</strong></td>
      <td>${summary} ${o.notes ? `<span class="muted">(${o.notes})</span>` : ""}</td>
      <td>
        <button class="btn btn-ghost btn-sm" data-reprint="${o.id}">🖨️ Ticket</button>
        <button class="btn btn-ghost btn-sm" data-reopen="${o.id}">↩ Volver a Pendiente</button>
      </td>
    `;
    tr.querySelector(`[data-reprint="${o.id}"]`).onclick = () => printKitchenTicket(o, false);
    tr.querySelector(`[data-reopen="${o.id}"]`).onclick = () =>
      setKitchenStatus(o.id, "pendiente", o.ticket_number);
    tbody.appendChild(tr);
  });
}

async function pollKitchen() {
  try {
    const data = await apiGet("/api/kitchen/poll");
    if (!data.ok) return;

    kState.settings = data.settings || {};
    kState.active = data.active || [];
    kState.finished = data.finished || [];

    const syncEl = document.getElementById("sync-indicator");
    syncEl.textContent = "● Conectado en tiempo real";
    syncEl.style.color = "#10b981";

    // Detectar pedidos nuevos recién cobrados en Caja
    const autoPrintEnabled = document.getElementById("chk-auto-print").checked;
    for (const order of kState.active) {
      if (!kState.knownOrderIds.has(order.id)) {
        kState.knownOrderIds.add(order.id);
        if (kState.firstLoadDone) {
          playKitchenBell();
          showToast(`🔔 ¡Nuevo Pedido ${padTicket(order.ticket_number)} recibido desde Caja!`);
        }
        // Si todavía no se imprimió en cocina y está activo el auto-print, generar ticket automáticamente
        if (Number(order.kitchen_printed) === 0 && autoPrintEnabled) {
          await printKitchenTicket(order, true);
        }
      }
    }

    kState.firstLoadDone = true;
    renderKitchenBoard();
  } catch (err) {
    const syncEl = document.getElementById("sync-indicator");
    syncEl.textContent = "● Reconectando con Caja...";
    syncEl.style.color = "#ef4444";
  }
}

document.addEventListener("DOMContentLoaded", () => {
  pollKitchen();
  setInterval(pollKitchen, 1500);
});
