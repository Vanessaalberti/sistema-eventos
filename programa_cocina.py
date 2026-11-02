from datetime import datetime
import tkinter as tk
from tkinter import ttk, messagebox
import winsound

from database import get_connection, get_settings, init_db
from printer import print_ticket_direct, save_ticket_file


class ProgramaCocinaApp(tk.Tk):
    def __init__(self):
        super().__init__()
        init_db()

        self.title("COCINA — Pantalla de Pedidos en Vivo (Asociación Juvenil Araucana)")
        self.geometry("1320x800")
        self.minsize(1020, 660)
        self.configure(bg="#0f172a")

        self.settings = get_settings()
        self.auto_print_var = tk.BooleanVar(value=False)
        self.known_order_ids = set()
        self.first_load_done = False
        self.last_signature = None

        self._build_topbar()
        self._build_board()
        self.poll_orders()

    def _build_topbar(self):
        top = tk.Frame(self, bg="#1e293b", padx=20, pady=12, highlightthickness=1, highlightbackground="#334155")
        top.pack(fill="x")

        left = tk.Frame(top, bg="#1e293b")
        left.pack(side="left")

        tk.Label(
            left,
            text="COCINA",
            bg="#f59e0b",
            fg="#0f172a",
            font=("Segoe UI", 12, "bold"),
            padx=10,
            pady=4,
        ).pack(side="left", padx=(0, 12))

        tk.Label(
            left,
            text="Pedidos en Curso — Asociación Juvenil Araucana",
            bg="#1e293b",
            fg="#f8fafc",
            font=("Segoe UI", 14, "bold"),
        ).pack(side="left")

        right = tk.Frame(top, bg="#1e293b")
        right.pack(side="right")

        self.lbl_pending = tk.Label(
            right,
            text="🔔 PENDIENTES: 0",
            bg="#451a03",
            fg="#fbbf24",
            font=("Segoe UI", 11, "bold"),
            padx=14,
            pady=6,
        )
        self.lbl_pending.pack(side="left", padx=5)

        self.lbl_prep = tk.Label(
            right,
            text="👨‍🍳 PREPARANDO: 0",
            bg="#1e3a8a",
            fg="#93c5fd",
            font=("Segoe UI", 11, "bold"),
            padx=14,
            pady=6,
        )
        self.lbl_prep.pack(side="left", padx=5)

        self.btn_done_history = tk.Button(
            right,
            text="✅ ENTREGADOS: 0 (Ver)",
            command=self.open_finished_history_modal,
            bg="#064e3b",
            fg="#a7f3d0",
            font=("Segoe UI", 10, "bold"),
            relief="flat",
            padx=14,
            pady=6,
            cursor="hand2",
        )
        self.btn_done_history.pack(side="left", padx=5)

        tk.Checkbutton(
            right,
            text="🖨️ Auto-imprimir comanda al llegar",
            variable=self.auto_print_var,
            bg="#1e293b",
            fg="#cbd5e1",
            selectcolor="#0f172a",
            activebackground="#1e293b",
            font=("Segoe UI", 9, "bold"),
        ).pack(side="left", padx=(10, 0))

    def _build_board(self):
        wrap = tk.Frame(self, bg="#0f172a", padx=16, pady=14)
        wrap.pack(fill="both", expand=True)

        self.canvas = tk.Canvas(wrap, bg="#0f172a", highlightthickness=0)
        scroll_y = ttk.Scrollbar(wrap, orient="vertical", command=self.canvas.yview)
        self.grid_frame = tk.Frame(self.canvas, bg="#0f172a")

        self.grid_frame.bind("<Configure>", lambda e: self.canvas.configure(scrollregion=self.canvas.bbox("all")))
        self.canvas_win = self.canvas.create_window((0, 0), window=self.grid_frame, anchor="nw")
        self.canvas.bind("<Configure>", lambda e: self.canvas.itemconfig(self.canvas_win, width=e.width))
        self.canvas.configure(yscrollcommand=scroll_y.set)

        self.canvas.pack(side="left", fill="both", expand=True)
        scroll_y.pack(side="right", fill="y")

    def _sound_bell(self):
        try:
            winsound.Beep(784, 150)
            winsound.Beep(1046, 230)
        except Exception:
            try:
                winsound.MessageBeep()
            except Exception:
                pass

    def print_kitchen_comanda(self, order: dict, silent: bool = False):
        try:
            used_printer = print_ticket_direct(order, self.settings, only_kitchen=True)
            if not silent:
                messagebox.showinfo(
                    "Comanda impresa",
                    f"Comanda del Pedido #{order['ticket_number']:03d} impresa en {used_printer}.",
                )
        except Exception as e:
            if not silent:
                messagebox.showerror("Error de impresora", str(e))

    def set_order_status(self, order_id: int, new_status: str):
        finished_at = datetime.now().strftime("%H:%M:%S") if new_status == "finalizado" else ""
        conn = get_connection()
        conn.execute(
            "UPDATE orders SET kitchen_status = ?, finished_at = ?, kitchen_printed = 1 WHERE id = ?",
            (new_status, finished_at, order_id),
        )
        conn.commit()
        conn.close()
        self.last_signature = None
        self.poll_orders(reschedule=False)

    def poll_orders(self, reschedule: bool = True):
        try:
            conn = get_connection()
            self.settings = get_settings()

            active = [
                dict(r)
                for r in conn.execute(
                    """
                SELECT * FROM orders
                WHERE status = 'completado' AND kitchen_status IN ('pendiente', 'preparando')
                ORDER BY id ASC
            """
                ).fetchall()
            ]
            for o in active:
                o["items"] = [
                    dict(i)
                    for i in conn.execute("SELECT * FROM order_items WHERE order_id = ?", (o["id"],)).fetchall()
                ]

            done_count = conn.execute(
                "SELECT COUNT(*) as c FROM orders WHERE status = 'completado' AND kitchen_status = 'finalizado'"
            ).fetchone()["c"]

            for o in active:
                if o["id"] not in self.known_order_ids:
                    self.known_order_ids.add(o["id"])
                    save_ticket_file(o, self.settings, kind="cocina")
                    if self.first_load_done:
                        self._sound_bell()
                    if int(o.get("kitchen_printed", 0)) == 0:
                        conn.execute("UPDATE orders SET kitchen_printed = 1 WHERE id = ?", (o["id"],))
                        conn.commit()
                        if self.auto_print_var.get():
                            self.print_kitchen_comanda(o, silent=True)

            conn.close()
            self.first_load_done = True

            pending_cnt = sum(1 for o in active if o["kitchen_status"] == "pendiente")
            prep_cnt = sum(1 for o in active if o["kitchen_status"] == "preparando")

            self.lbl_pending.config(text=f"🔔 PENDIENTES: {pending_cnt}")
            self.lbl_prep.config(text=f"👨‍🍳 PREPARANDO: {prep_cnt}")
            self.btn_done_history.config(text=f"✅ ENTREGADOS: {done_count} (Ver)")

            sig = tuple((o["id"], o["kitchen_status"]) for o in active)
            if sig != self.last_signature:
                self.last_signature = sig
                self.render_active_cards(active)
        except Exception:
            pass

        if reschedule:
            self.after(1200, self.poll_orders)

    def render_active_cards(self, active: list):
        for w in self.grid_frame.winfo_children():
            w.destroy()

        if not active:
            empty_box = tk.Frame(self.grid_frame, bg="#1e293b", padx=40, pady=60, highlightthickness=1, highlightbackground="#334155")
            empty_box.pack(fill="x", pady=30)
            tk.Label(
                empty_box,
                text="✨ No hay pedidos pendientes en Cocina",
                bg="#1e293b",
                fg="#f8fafc",
                font=("Segoe UI", 16, "bold"),
            ).pack()
            tk.Label(
                empty_box,
                text="En cuanto Caja cobre un nuevo pedido, aparecerá aquí con alerta sonora.",
                bg="#1e293b",
                fg="#94a3b8",
                font=("Segoe UI", 11),
                pady=8,
            ).pack()
            return

        cols = 3
        for c in range(cols):
            self.grid_frame.columnconfigure(c, weight=1, uniform="kcard")

        for idx, o in enumerate(active):
            r = idx // cols
            c = idx % cols
            is_prep = o["kitchen_status"] == "preparando"

            border_color = "#3b82f6" if is_prep else "#f59e0b"
            header_bg = "#1e3a8a" if is_prep else "#78350f"
            badge_text = "👨‍🍳 EN PREPARACIÓN" if is_prep else "🔔 NUEVO PEDIDO"

            card = tk.Frame(
                self.grid_frame,
                bg="#1e293b",
                highlightthickness=2,
                highlightbackground=border_color,
            )
            card.grid(row=r, column=c, sticky="nsew", padx=8, pady=8)

            # Cabecera de color de la comanda
            head = tk.Frame(card, bg=header_bg, padx=14, pady=10)
            head.pack(fill="x")

            tk.Label(
                head,
                text=f"#{o['ticket_number']:03d}",
                bg=header_bg,
                fg="#ffffff",
                font=("Segoe UI", 22, "bold"),
            ).pack(side="left")

            right_head = tk.Frame(head, bg=header_bg)
            right_head.pack(side="right")
            tk.Label(
                right_head,
                text=badge_text,
                bg=header_bg,
                fg="#ffffff",
                font=("Segoe UI", 9, "bold"),
            ).pack(anchor="e")
            tk.Label(
                right_head,
                text=o["created_at"].split()[-1] if o.get("created_at") else "",
                bg=header_bg,
                fg="#cbd5e1",
                font=("Segoe UI", 9),
            ).pack(anchor="e")

            # Lista grande y legible de platos a preparar
            body = tk.Frame(card, bg="#1e293b", padx=14, pady=12)
            body.pack(fill="both", expand=True)

            for it in o["items"]:
                item_row = tk.Frame(body, bg="#0f172a", padx=10, pady=8)
                item_row.pack(fill="x", pady=4)

                tk.Label(
                    item_row,
                    text=f" {it['quantity']}x ",
                    bg="#10b981",
                    fg="#042f2e",
                    font=("Segoe UI", 13, "bold"),
                ).pack(side="left", padx=(0, 10))

                tk.Label(
                    item_row,
                    text=it["product_name"],
                    bg="#0f172a",
                    fg="#f8fafc",
                    font=("Segoe UI", 12, "bold"),
                    wraplength=250,
                    justify="left",
                    anchor="w",
                ).pack(side="left", fill="x", expand=True)

            if o.get("notes"):
                note_box = tk.Frame(body, bg="#451a03", padx=10, pady=8)
                note_box.pack(fill="x", pady=(6, 2))
                tk.Label(
                    note_box,
                    text=f"📝 NOTA: {o['notes']}",
                    bg="#451a03",
                    fg="#fde68a",
                    font=("Segoe UI", 10, "bold"),
                    wraplength=310,
                    justify="left",
                    anchor="w",
                ).pack(fill="x")

            # Botonera inferior clara
            foot = tk.Frame(card, bg="#1e293b", padx=14, pady=12)
            foot.pack(fill="x", side="bottom")

            tk.Button(
                foot,
                text="🖨️",
                title="Imprimir comanda en ticketera",
                command=lambda ord_obj=o: self.print_kitchen_comanda(ord_obj, silent=False),
                bg="#334155",
                fg="#f8fafc",
                font=("Segoe UI", 11, "bold"),
                relief="flat",
                padx=10,
                pady=8,
                cursor="hand2",
            ).pack(side="left", padx=(0, 6))

            if not is_prep:
                tk.Button(
                    foot,
                    text="👨‍🍳 Preparar",
                    command=lambda oid=o["id"]: self.set_order_status(oid, "preparando"),
                    bg="#2563eb",
                    fg="#ffffff",
                    font=("Segoe UI", 10, "bold"),
                    relief="flat",
                    padx=12,
                    pady=8,
                    cursor="hand2",
                ).pack(side="left", padx=(0, 6))

            tk.Button(
                foot,
                text="✅ LISTO / ENTREGADO",
                command=lambda oid=o["id"]: self.set_order_status(oid, "finalizado"),
                bg="#10b981",
                fg="#042f2e",
                font=("Segoe UI", 11, "bold"),
                relief="flat",
                padx=12,
                pady=8,
                cursor="hand2",
            ).pack(side="right", expand=True, fill="x")

    def open_finished_history_modal(self):
        win = tk.Toplevel(self)
        win.title("Últimos Pedidos Entregados")
        win.geometry("760x460")
        win.configure(bg="#1e293b")
        win.grab_set()

        head = tk.Frame(win, bg="#1e293b", padx=16, pady=12)
        head.pack(fill="x")
        tk.Label(
            head,
            text="Últimos 30 Pedidos Marcados como Entregados",
            bg="#1e293b",
            fg="#f8fafc",
            font=("Segoe UI", 12, "bold"),
        ).pack(side="left")

        cols = ("id", "ticket", "finished", "items")
        tree = ttk.Treeview(win, columns=cols, show="headings", height=12)
        tree.heading("id", text="ID")
        tree.heading("ticket", text="Ticket")
        tree.heading("finished", text="Hora Entregado")
        tree.heading("items", text="Productos")
        tree.column("id", width=50, anchor="center")
        tree.column("ticket", width=90, anchor="center")
        tree.column("finished", width=120, anchor="center")
        tree.column("items", width=460, anchor="w")
        tree.pack(fill="both", expand=True, padx=16, pady=6)

        conn = get_connection()
        rows = [
            dict(r)
            for r in conn.execute(
                "SELECT * FROM orders WHERE status='completado' AND kitchen_status='finalizado' ORDER BY id DESC LIMIT 30"
            ).fetchall()
        ]
        for o in rows:
            its = conn.execute("SELECT * FROM order_items WHERE order_id=?", (o["id"],)).fetchall()
            summary = ", ".join(f"{i['quantity']}x {i['product_name']}" for i in its)
            tree.insert("", "end", iid=str(o["id"]), values=(o["id"], f"#{o['ticket_number']:03d}", o["finished_at"], summary))
        conn.close()

        def reopen():
            sel = tree.selection()
            if not sel:
                return
            oid = int(sel[0])
            self.set_order_status(oid, "pendiente")
            win.destroy()

        tk.Button(
            win,
            text="↩ Devolver Pedido Seleccionado a la Pantalla de Cocina",
            command=reopen,
            bg="#f59e0b",
            fg="#0f172a",
            font=("Segoe UI", 10, "bold"),
            relief="flat",
            padx=16,
            pady=8,
        ).pack(pady=12)


if __name__ == "__main__":
    app = ProgramaCocinaApp()
    app.mainloop()
