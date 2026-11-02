const state = {
  settings: {},
  products: [],
  orders: [],
  summary: {},
  windowsPrinters: [],
  selectedCategory: "Todos",
  searchQuery: "",
  cart: [], // { product_id, name, price, quantity, stock_enabled, stock_qty }
  paymentMethod: "efectivo",
  currentTicketOrder: null,
};

function fmtMoney(val) {
  const num = Number(val || 0);
  return "$" + num.toLocaleString("es-AR", { maximumFractionDigits: 0 });
}

function padTicket(num) {
  return "#" + String(num || 1).padStart(3, "0");
}

function showToast(msg, isError = false) {
  const container = document.getElementById("toast-container");
  const el = document.createElement("div");
  el.className = "toast" + (isError ? " error" : "");
  el.textContent = msg;
  container.appendChild(el);
  setTimeout(() => el.remove(), 3500);
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

// ==========================================
// INICIALIZACIÓN Y CARGA DE DATOS
// ==========================================
async function loadBootstrap() {
  try {
    const data = await apiGet("/api/bootstrap");
    if (!data.ok) return;
    state.settings = data.settings || {};
    state.products = data.products || [];
    state.orders = data.orders || [];
    state.summary = data.summary || {};
    state.windowsPrinters = data.windows_printers || [];

    const lanUrl = `http://${data.local_ip}:${data.port}`;
    document.getElementById("header-lan-url").textContent = lanUrl;

    applySettingsToUI();
    renderAll();
  } catch (err) {
    showToast("Error al conectar con el servidor local", true);
  }
}

function applySettingsToUI() {
  const s = state.settings;
  document.getElementById("header-event-name").textContent = s.event_name || "Evento General";
  document.getElementById("header-next-ticket").textContent = padTicket(s.ticket_Sequence || 1);

  // Configurar ancho CSS para @media print (58mm -> 54mm área útil, 80mm -> 74mm área útil)
  const widthMm = s.paper_width === "58" ? "54mm" : "74mm";
  document.documentElement.style.setProperty("--print-width", widthMm);

  // Formulario de configuración
  document.getElementById("set-event-name").value = s.event_name || "";
  document.getElementById("set-paper-width").value = s.paper_width || "80";
  document.getElementById("set-double-ticket").value = s.print_double_ticket ?? "1";
  document.getElementById("set-print-mode").value = s.print_mode || "browser";
  document.getElementById("set-printer-ip").value = s.printer_ip || "192.168.1.200";
  document.getElementById("set-printer-port").value = s.printer_port || "9100";
  document.getElementById("set-ticket-footer").value = s.ticket_footer || "";

  // Lista de impresoras de Windows
  const selPrinter = document.getElementById("set-printer-name");
  selPrinter.innerHTML = `<option value="">-- Impresora predeterminada de Windows --</option>`;
  state.windowsPrinters.forEach((pName) => {
    const opt = document.createElement("option");
    opt.value = pName;
    opt.textContent = pName;
    if (s.printer_name === pName) opt.selected = true;
    selPrinter.appendChild(opt);
  });

  togglePrinterModeFields();
}

function togglePrinterModeFields() {
  const mode = document.getElementById("set-print-mode").value;
  document.getElementById("network-printer-fields").classList.toggle("hidden", mode !== "network_escpos");
  document.getElementById("windows-printer-fields").classList.toggle("hidden", mode !== "windows_raw");
}

function renderAll() {
  renderCategories();
  renderPOSProducts();
  renderCart();
  renderCatalogTable();
  renderOrdersAndStats();
}

// ==========================================
// 1. VISTA POS: CATÁLOGO Y CARRITO
// ==========================================
function renderCategories() {
  const activeProds = state.products.filter((p) => p.active);
  const cats = ["Todos", ...new Set(activeProds.map((p) => p.category))];
  const container = document.getElementById("category-pills");
  container.innerHTML = "";

  cats.forEach((cat) => {
    const btn = document.createElement("button");
    btn.className = "cat-pill" + (state.selectedCategory === cat ? " active" : "");
    btn.textContent = cat;
    btn.onclick = () => {
      state.selectedCategory = cat;
      renderCategories();
      renderPOSProducts();
    };
    container.appendChild(btn);
  });
}

function renderPOSProducts() {
  const grid = document.getElementById("pos-products-grid");
  grid.innerHTML = "";

  const q = state.searchQuery.toLowerCase().trim();
  const filtered = state.products.filter((p) => {
    if (!p.active) return false;
    if (state.selectedCategory !== "Todos" && p.category !== state.selectedCategory) return false;
    if (q && !p.name.toLowerCase().includes(q) && !p.category.toLowerCase().includes(q)) return false;
    return true;
  });

  if (filtered.length === 0) {
    grid.innerHTML = `<div class="muted" style="grid-column: 1/-1; padding: 2rem; text-align:center;">No hay productos en esta categoría o búsqueda.</div>`;
    return;
  }

  filtered.forEach((p) => {
    const outOfStock = p.stock_enabled && p.stock_qty <= 0;
    const card = document.createElement("div");
    card.className = "product-card" + (outOfStock ? " out-of-stock" : "");

    let stockLabel = "Stock libre";
    let stockClass = "prod-stock";
    if (p.stock_enabled) {
      if (p.stock_qty <= 0) {
        stockLabel = "SIN STOCK";
        stockClass += " zero";
      } else if (p.stock_qty <= 10) {
        stockLabel = `Quedan ${p.stock_qty} u.`;
        stockClass += " low";
      } else {
        stockLabel = `Disp: ${p.stock_qty} u.`;
      }
    }

    card.innerHTML = `
      <div>
        <div class="prod-badges">
          <span class="badge-cat">${p.category}</span>
          ${p.is_combo ? `<span class="badge-combo">★ COMBO</span>` : ""}
        </div>
        <div class="prod-title" style="margin-top:0.45rem;">${p.name}</div>
        ${p.description ? `<div class="prod-desc">${p.description}</div>` : ""}
      </div>
      <div class="prod-footer">
        <span class="prod-price">${fmtMoney(p.price)}</span>
        <span class="${stockClass}">${stockLabel}</span>
      </div>
    `;

    card.onclick = () => {
      if (outOfStock) {
        showToast(`'${p.name}' está sin stock disponible.`, true);
        return;
      }
      addToCart(p);
    };

    grid.appendChild(card);
  });
}

function addToCart(product) {
  const existing = state.cart.find((item) => item.product_id === product.id);
  if (existing) {
    if (product.stock_enabled && existing.quantity + 1 > product.stock_qty) {
      showToast(`Stock máximo alcanzado para '${product.name}' (${product.stock_qty} u.)`, true);
      return;
    }
    existing.quantity += 1;
  } else {
    state.cart.push({
      product_id: product.id,
      name: product.name,
      price: Number(product.price),
      quantity: 1,
      stock_enabled: Boolean(product.stock_enabled),
      stock_qty: Number(product.stock_qty),
    });
  }
  renderCart();
}

function changeCartQty(productId, delta) {
  const idx = state.cart.findIndex((i) => i.product_id === productId);
  if (idx === -1) return;
  const item = state.cart[idx];
  const nextQty = item.quantity + delta;

  if (nextQty <= 0) {
    state.cart.splice(idx, 1);
  } else if (item.stock_enabled && nextQty > item.stock_qty) {
    showToast(`Solo quedan ${item.stock_qty} unidades de '${item.name}'`, true);
    return;
  } else {
    item.quantity = nextQty;
  }
  renderCart();
}

function getCartTotal() {
  return state.cart.reduce((acc, item) => acc + item.price * item.quantity, 0);
}

function renderCart() {
  const container = document.getElementById("cart-items");
  const total = getCartTotal();
  document.getElementById("cart-total").textContent = fmtMoney(total);
  document.getElementById("btn-submit-order").disabled = state.cart.length === 0;

  if (state.cart.length === 0) {
    container.innerHTML = `<div class="empty-cart">Seleccioná productos del catálogo para armar el pedido.</div>`;
    updateCashChange();
    return;
  }

  container.innerHTML = "";
  state.cart.forEach((item) => {
    const row = document.createElement("div");
    row.className = "cart-item";
    row.innerHTML = `
      <div class="cart-item-info">
        <div class="cart-item-title">${item.name}</div>
        <div class="cart-item-sub">${fmtMoney(item.price)} c/u · Sub: <strong>${fmtMoney(item.price * item.quantity)}</strong></div>
      </div>
      <div class="qty-controls">
        <button type="button" class="qty-btn" data-act="dec">−</button>
        <strong>${item.quantity}</strong>
        <button type="button" class="qty-btn" data-act="inc">+</button>
      </div>
    `;
    row.querySelector('[data-act="dec"]').onclick = () => changeCartQty(item.product_id, -1);
    row.querySelector('[data-act="inc"]').onclick = () => changeCartQty(item.product_id, 1);
    container.appendChild(row);
  });

  updateCashChange();
}

function updateCashChange() {
  const total = getCartTotal();
  const inputVal = Number(document.getElementById("cash-received").value || 0);
  const changeEl = document.getElementById("cash-change");

  if (total === 0 || inputVal <= 0) {
    changeEl.textContent = "$0";
    changeEl.style.color = "#38bdf8";
    return;
  }

  const diff = inputVal - total;
  if (diff < 0) {
    changeEl.textContent = "Faltan " + fmtMoney(Math.abs(diff));
    changeEl.style.color = "#ef4444";
  } else {
    changeEl.textContent = fmtMoney(diff);
    changeEl.style.color = "#10b981";
  }
}

// ==========================================
// COBRAR Y GENERAR TICKET
// ==========================================
async function submitOrder() {
  if (state.cart.length === 0) return;

  const total = getCartTotal();
  const cashInput = Number(document.getElementById("cash-received").value || 0);
  if (state.paymentMethod === "efectivo" && cashInput > 0 && cashInput < total) {
    showToast("El monto recibido en efectivo es menor al total a cobrar.", true);
    return;
  }

  const btn = document.getElementById("btn-submit-order");
  btn.disabled = true;
  btn.textContent = "Procesando...";

  try {
    const payload = {
      items: state.cart.map((i) => ({ product_id: i.product_id, quantity: i.quantity })),
      payment_method: state.paymentMethod,
      amount_received: state.paymentMethod === "efectivo" ? (cashInput || total) : total,
      notes: document.getElementById("order-notes").value,
    };

    const res = await apiPost("/api/orders", payload);
    if (!res.ok) {
      showToast(res.error || "No se pudo registrar el pedido", true);
      btn.disabled = false;
      btn.textContent = "🖨️ COBRAR E IMPRIMIR TICKET";
      return;
    }

    // Actualizar estado local con respuesta
    state.products = res.products;
    state.orders = res.orders;
    state.summary = res.summary;
    state.settings = res.settings;
    state.cart = [];
    document.getElementById("cash-received").value = "";
    document.getElementById("order-notes").value = "";

    applySettingsToUI();
    renderAll();

    // Mostrar y preparar el ticket para impresión
    openTicketModal(res.order, res.raw_print_status);
  } catch (err) {
    showToast("Error de red al procesar el pedido", true);
  } finally {
    btn.textContent = "🖨️ COBRAR E IMPRIMIR TICKET";
    renderCart();
  }
}

// ==========================================
// GENERADOR HTML DEL TICKET (58mm / 80mm + DOBLE TALÓN)
// ==========================================
function buildThermalTicketHTML(order) {
  const s = state.settings;
  const isDouble = String(s.print_double_ticket ?? "1") === "1";

  const itemsRows = (order.items || [])
    .map(
      (it) => `
      <div class="t-row">
        <span>${it.quantity}x ${it.product_name}</span>
        <strong>${fmtMoney(it.subtotal)}</strong>
      </div>`
    )
    .join("");

  const kitchenItemsRows = (order.items || [])
    .map(
      (it) => `
      <div style="margin: 4px 0; font-size: 15px; font-weight: 800;">
        [ ${it.quantity} ] ${it.product_name.toUpperCase()}
        <div style="font-size: 11px; font-weight: 400;">Sector: ${it.category || "General"}</div>
      </div>`
    )
    .join("");

  let cashDetails = "";
  if (order.payment_method === "efectivo") {
    cashDetails = `
      <div class="t-row"><span>Paga con:</span><span>${fmtMoney(order.amount_received)}</span></div>
      <div class="t-row"><span>Su vuelto:</span><strong>${fmtMoney(order.change_amount)}</strong></div>
    `;
  } else if (order.payment_method === "transferencia") {
    cashDetails = `
      <div class="t-row"><span>Alias MP:</span><strong>${s.mp_alias || "araucana24"}</strong></div>
    `;
  }

  const clientStub = `
    <div class="thermal-receipt ${isDouble ? "thermal-stub-break" : ""}">
      <div class="t-center">
        <div class="t-event">${s.event_name || order.event_name || "EVENTO"}</div>
        <div>COMPROBANTE DE CLIENTE</div>
        <div class="t-number">PEDIDO ${padTicket(order.ticket_number)}</div>
        <div style="font-size:11px;">${order.created_at}</div>
      </div>
      <div class="t-divider"></div>
      ${itemsRows}
      <div class="t-divider"></div>
      <div class="t-row t-total">
        <span>TOTAL:</span>
        <span>${fmtMoney(order.total)}</span>
      </div>
      <div class="t-row">
        <span>Medio de pago:</span>
        <strong style="text-transform:uppercase;">${order.payment_method}</strong>
      </div>
      ${cashDetails}
      ${order.notes ? `<div class="t-divider"></div><div><strong>Nota:</strong> ${order.notes}</div>` : ""}
      <div class="t-divider"></div>
      <div class="t-center" style="font-size:12px;">${s.ticket_footer || "¡Gracias por tu compra!"}</div>
    </div>
  `;

  if (!isDouble) {
    return clientStub;
  }

  const kitchenStub = `
    <div class="t-cut">✂ --- CORTAR AQUÍ (TALÓN BARRA / COCINA) --- ✂</div>
    <div class="thermal-receipt">
      <div class="t-center">
        <div class="t-event">*** BARRA / COCINA ***</div>
        <div class="t-number">PEDIDO ${padTicket(order.ticket_number)}</div>
        <div style="font-size:11px;">${order.created_at}</div>
      </div>
      <div class="t-divider"></div>
      ${kitchenItemsRows}
      ${order.notes ? `<div class="t-divider"></div><div style="font-size:14px;font-weight:800;">NOTA: ${order.notes}</div>` : ""}
      <div class="t-divider"></div>
    </div>
  `;

  return clientStub + kitchenStub;
}

function openTicketModal(order, rawStatus = null) {
  state.currentTicketOrder = order;
  const html = buildThermalTicketHTML(order);
  document.getElementById("ticket-preview-box").innerHTML = html;
  document.getElementById("thermal-print-area").innerHTML = html;
  document.getElementById("ticket-modal").classList.remove("hidden");

  if (rawStatus === "ok") {
    showToast(`Ticket ${padTicket(order.ticket_number)} enviado directo a la ticketera`);
  } else if (rawStatus && rawStatus.startsWith("error:")) {
    showToast(`Aviso ticketera directa: ${rawStatus}. Podés imprimir desde el navegador.`, true);
  } else if (state.settings.print_mode === "browser") {
    // Disparo automático del diálogo de impresión del navegador al cobrar
    setTimeout(() => window.print(), 200);
  }
}

async function triggerPrintCurrentOrder() {
  if (!state.currentTicketOrder) return;
  if (state.settings.print_mode === "browser") {
    window.print();
  } else {
    const res = await apiPost(`/api/orders/${state.currentTicketOrder.id}/print-raw`);
    if (res.ok) {
      showToast("Ticket enviado a la ticketera ESC/POS");
    } else {
      showToast(res.error || "Error en ticketera directa; abriendo impresión de navegador...", true);
      window.print();
    }
  }
}

// ==========================================
// 2. VISTA CATÁLOGO Y CONTROL DE STOCK
// ==========================================
function renderCatalogTable() {
  const tbody = document.getElementById("catalog-table-body");
  tbody.innerHTML = "";

  state.products.forEach((p) => {
    const tr = document.createElement("tr");
    const stockBadge = p.stock_enabled
      ? `<strong>${p.stock_qty} u.</strong>
         <button class="btn btn-ghost btn-sm" data-plus10="${p.id}" style="margin-left:6px;">+10</button>`
      : `<span class="muted">Sin límite</span>`;

    tr.innerHTML = `
      <td>
        <strong>${p.name}</strong>
        ${p.is_combo ? `<span class="badge-combo" style="margin-left:6px;">COMBO</span>` : ""}
        ${p.description ? `<div class="muted">${p.description}</div>` : ""}
      </td>
      <td><span class="badge-cat">${p.category}</span></td>
      <td><strong style="color:var(--primary);">${fmtMoney(p.price)}</strong></td>
      <td>${stockBadge}</td>
      <td>${p.active ? `<span style="color:var(--primary);">Activo</span>` : `<span class="muted">Oculto</span>`}</td>
      <td>
        <button class="btn btn-ghost btn-sm" data-edit="${p.id}">Editar</button>
        <button class="btn btn-ghost btn-sm" style="color:var(--danger);" data-del="${p.id}">Eliminar</button>
      </td>
    `;

    const plusBtn = tr.querySelector(`[data-plus10="${p.id}"]`);
    if (plusBtn) {
      plusBtn.onclick = async () => {
        const updated = { ...p, stock_qty: Number(p.stock_qty) + 10 };
        const res = await apiPost("/api/products", updated);
        if (res.ok) {
          state.products = res.products;
          renderAll();
          showToast(`+10 unidades agregadas a '${p.name}'`);
        }
      };
    }

    tr.querySelector(`[data-edit="${p.id}"]`).onclick = () => openProductModal(p);
    tr.querySelector(`[data-del="${p.id}"]`).onclick = async () => {
      if (!confirm(`¿Eliminar '${p.name}' del catálogo?`)) return;
      const res = await apiPost(`/api/products/${p.id}/delete`);
      if (res.ok) {
        state.products = res.products;
        renderAll();
        showToast("Producto eliminado");
      }
    };

    tbody.appendChild(tr);
  });
}

function openProductModal(product = null) {
  document.getElementById("product-modal-title").textContent = product ? "Editar Producto / Combo" : "Nuevo Producto / Combo";
  document.getElementById("prod-id").value = product ? product.id : "";
  document.getElementById("prod-name").value = product ? product.name : "";
  document.getElementById("prod-category").value = product ? product.category : "Comida";
  document.getElementById("prod-price").value = product ? product.price : "";
  document.getElementById("prod-desc").value = product ? product.description : "";
  document.getElementById("prod-is-combo").checked = product ? Boolean(product.is_combo) : false;
  document.getElementById("prod-active").checked = product ? Boolean(product.active) : true;
  document.getElementById("prod-stock-enabled").checked = product ? Boolean(product.stock_enabled) : true;
  document.getElementById("prod-stock-qty").value = product ? product.stock_qty : 50;

  document.getElementById("product-modal").classList.remove("hidden");
}

// ==========================================
// 3. VISTA HISTORIAL DE TICKETS Y CAJA
// ==========================================
function renderOrdersAndStats() {
  const sum = state.summary;
  document.getElementById("stat-total-revenue").textContent = fmtMoney(sum.total_revenue);
  document.getElementById("stat-count-orders").textContent = sum.count_orders || 0;
  document.getElementById("stat-cash").textContent = fmtMoney(sum.cash_total);
  document.getElementById("stat-digital").textContent = fmtMoney(
    Number(sum.transfer_total || 0) + Number(sum.card_total || 0)
  );

  const tbody = document.getElementById("orders-table-body");
  tbody.innerHTML = "";

  if (state.orders.length === 0) {
    tbody.innerHTML = `<tr><td colspan="7" class="muted" style="text-align:center;padding:1.5rem;">Todavía no hay ventas registradas en este evento.</td></tr>`;
    return;
  }

  state.orders.forEach((o) => {
    const tr = document.createElement("tr");
    const summaryItems = (o.items || []).map((i) => `${i.quantity}x ${i.product_name}`).join(", ");
    const isCancelled = o.status === "anulado";

    let statusBadge = `<span style="color:var(--primary);">Completado</span>`;
    if (isCancelled) {
      statusBadge = `<span style="color:var(--danger);font-weight:700;">ANULADO</span>`;
    } else if (o.kitchen_status === "finalizado") {
      statusBadge = `<span style="color:var(--primary);font-weight:700;">✅ Finalizado (${o.finished_at || "Listo"})</span>`;
    } else if (o.kitchen_status === "preparando") {
      statusBadge = `<span style="color:#60a5fa;font-weight:700;">👨‍🍳 En Preparación</span>`;
    } else {
      statusBadge = `<span style="color:#fbbf24;font-weight:700;">🔔 En Cocina (Pendiente)</span>`;
    }

    tr.innerHTML = `
      <td><strong>${padTicket(o.ticket_number)}</strong></td>
      <td class="muted">${o.created_at}</td>
      <td>
        <div>${summaryItems}</div>
        ${o.notes ? `<div class="muted">Nota: ${o.notes}</div>` : ""}
      </td>
      <td style="text-transform:uppercase;">${o.payment_method}</td>
      <td><strong style="color:${isCancelled ? "var(--muted)" : "var(--primary)"};">${fmtMoney(o.total)}</strong></td>
      <td>${statusBadge}</td>
      <td>
        <button class="btn btn-ghost btn-sm" data-reprint="${o.id}">🖨️ Ver / Imprimir</button>
        ${!isCancelled ? `<button class="btn btn-ghost btn-sm" style="color:var(--danger);" data-cancel="${o.id}">Anular</button>` : ""}
      </td>
    `;

    tr.querySelector(`[data-reprint="${o.id}"]`).onclick = () => openTicketModal(o);
    const cancelBtn = tr.querySelector(`[data-cancel="${o.id}"]`);
    if (cancelBtn) {
      cancelBtn.onclick = async () => {
        if (!confirm(`¿Anular el Ticket ${padTicket(o.ticket_number)} y devolver el stock al catálogo?`)) return;
        const res = await apiPost(`/api/orders/${o.id}/cancel`);
        if (res.ok) {
          state.products = res.products;
          state.orders = res.orders;
          state.summary = res.summary;
          renderAll();
          showToast(`Ticket ${padTicket(o.ticket_number)} anulado y stock restaurado.`);
        } else {
          showToast(res.error || "Error al anular", true);
        }
      };
    }

    tbody.appendChild(tr);
  });
}

// ==========================================
// EVENTOS DE INTERFAZ
// ==========================================
document.addEventListener("DOMContentLoaded", () => {
  loadBootstrap();

  // Navegación por pestañas
  document.querySelectorAll(".tab-btn").forEach((btn) => {
    btn.addEventListener("click", () => {
      document.querySelectorAll(".tab-btn").forEach((b) => b.classList.remove("active"));
      document.querySelectorAll(".tab-view").forEach((v) => v.classList.remove("active"));
      btn.classList.add("active");
      document.getElementById(`tab-${btn.dataset.tab}`).classList.add("active");
    });
  });

  // Búsqueda en vivo
  document.getElementById("pos-search").addEventListener("input", (e) => {
    state.searchQuery = e.target.value;
    renderPOSProducts();
  });

  // Vaciar carrito
  document.getElementById("btn-clear-cart").addEventListener("click", () => {
    state.cart = [];
    renderCart();
  });

  // Selección de medio de pago
  document.querySelectorAll(".pay-btn").forEach((btn) => {
    btn.addEventListener("click", () => {
      document.querySelectorAll(".pay-btn").forEach((b) => b.classList.remove("active"));
      btn.classList.add("active");
      state.paymentMethod = btn.dataset.method;
      document.getElementById("cash-box").classList.toggle("hidden", state.paymentMethod !== "efectivo");
      const mpBox = document.getElementById("mp-box");
      if (mpBox) {
        mpBox.classList.toggle("hidden", state.paymentMethod !== "transferencia");
      }
    });
  });

  // Sincronización periódica con Cocina (para ver en Caja cuando Cocina finaliza un pedido)
  setInterval(async () => {
    try {
      const data = await apiGet("/api/bootstrap");
      if (data.ok) {
        state.orders = data.orders || [];
        state.summary = data.summary || {};
        renderOrdersAndStats();
      }
    } catch (e) {}
  }, 3500);

  // Calculadora de efectivo y botones rápidos
  document.getElementById("cash-received").addEventListener("input", updateCashChange);
  document.querySelectorAll(".chip-btn").forEach((btn) => {
    btn.addEventListener("click", () => {
      const val = btn.dataset.cash;
      const total = getCartTotal();
      document.getElementById("cash-received").value = val === "exact" ? total : Number(val);
      updateCashChange();
    });
  });

  // Botón principal de cobro
  document.getElementById("btn-submit-order").addEventListener("click", submitOrder);

  // Modal de Producto
  document.getElementById("btn-new-product").addEventListener("click", () => openProductModal(null));
  document.getElementById("btn-close-product-modal").addEventListener("click", () => {
    document.getElementById("product-modal").classList.add("hidden");
  });
  document.getElementById("btn-cancel-product").addEventListener("click", () => {
    document.getElementById("product-modal").classList.add("hidden");
  });

  document.getElementById("product-form").addEventListener("submit", async (e) => {
    e.preventDefault();
    const payload = {
      id: document.getElementById("prod-id").value || null,
      name: document.getElementById("prod-name").value,
      category: document.getElementById("prod-category").value,
      price: Number(document.getElementById("prod-price").value),
      description: document.getElementById("prod-desc").value,
      is_combo: document.getElementById("prod-is-combo").checked,
      active: document.getElementById("prod-active").checked,
      stock_enabled: document.getElementById("prod-stock-enabled").checked,
      stock_qty: Number(document.getElementById("prod-stock-qty").value),
    };
    const res = await apiPost("/api/products", payload);
    if (res.ok) {
      state.products = res.products;
      document.getElementById("product-modal").classList.add("hidden");
      renderAll();
      showToast("Producto guardado en el catálogo");
    } else {
      showToast(res.error || "Error al guardar producto", true);
    }
  });

  // Modal de Ticket
  const closeTicketModal = () => document.getElementById("ticket-modal").classList.add("hidden");
  document.getElementById("btn-close-ticket-modal").addEventListener("click", closeTicketModal);
  document.getElementById("btn-modal-new-order").addEventListener("click", closeTicketModal);
  document.getElementById("btn-modal-print").addEventListener("click", triggerPrintCurrentOrder);

  // Configuración de ticketera y evento
  document.getElementById("set-print-mode").addEventListener("change", togglePrinterModeFields);
  document.getElementById("settings-form").addEventListener("submit", async (e) => {
    e.preventDefault();
    const payload = {
      event_name: document.getElementById("set-event-name").value,
      paper_width: document.getElementById("set-paper-width").value,
      print_double_ticket: document.getElementById("set-double-ticket").value,
      print_mode: document.getElementById("set-print-mode").value,
      printer_ip: document.getElementById("set-printer-ip").value,
      printer_port: document.getElementById("set-printer-port").value,
      printer_name: document.getElementById("set-printer-name").value,
      ticket_footer: document.getElementById("set-ticket-footer").value,
    };
    const res = await apiPost("/api/settings", payload);
    if (res.ok) {
      state.settings = res.settings;
      applySettingsToUI();
      showToast("Configuración guardada correctamente");
    }
  });

  // Reinicio de numeración o nuevo evento
  document.getElementById("btn-reset-seq").addEventListener("click", async () => {
    if (!confirm("¿Reiniciar el contador del próximo ticket a #001?")) return;
    const res = await apiPost("/api/event/reset", { clear_orders: false });
    if (res.ok) {
      state.settings = res.settings;
      applySettingsToUI();
      showToast("Numeración de tickets reiniciada a #001");
    }
  });

  document.getElementById("btn-reset-all").addEventListener("click", async () => {
    if (!confirm("¿Estás seguro? Se borrarán los pedidos del evento actual y el contador volverá a #001.")) return;
    const res = await apiPost("/api/event/reset", { clear_orders: true });
    if (res.ok) {
      state.settings = res.settings;
      state.orders = res.orders;
      state.summary = res.summary;
      applySettingsToUI();
      renderAll();
      showToast("Nuevo evento iniciado desde #001");
    }
  });
});
