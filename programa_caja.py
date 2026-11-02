from datetime import datetime
import tkinter as tk
from tkinter import ttk, messagebox

from database import (
    get_connection,
    get_settings,
    init_db,
    reset_menu_to_araucana,
    update_settings,
)
from printer import (
    detect_preferred_thermal_printer,
    format_double_ticket_text,
    list_windows_printers,
    print_ticket_direct,
    save_ticket_file,
)


def fmt_money(val: float) -> str:
    return f"${val:,.0f}".replace(",", ".")


CATEGORY_COLORS = {
    "Almuerzo": "#f59e0b",     # Ámbar
    "Principales": "#ef4444",  # Rojo cálido
    "Bebidas": "#3b82f6",      # Azul
    "Para Llevar": "#8b5cf6",  # Violeta
    "Postres": "#ec4899",      # Rosa
}


class ProgramaCajaApp(tk.Tk):
    def __init__(self):
        super().__init__()
        init_db()

        self.settings = get_settings()
        detected_printer = detect_preferred_thermal_printer()
        if detected_printer and not self.settings.get("printer_name"):
            self.settings = update_settings({"printer_name": detected_printer, "print_mode": "windows_raw"})

        self.title("CAJA — Asociación Juvenil Araucana")
        self.geometry("1340x820")
        self.minsize(1120, 700)
        self.configure(bg="#0f172a")

        self.products = []
        self.cart = []  # [{product_id, name, category, price, quantity}]
        self.selected_category = "Todos"
        self.payment_method = "efectivo"
        self.cash_received_var = tk.StringVar(value="")
        self.notes_var = tk.StringVar(value="")
        self.last_created_order = None

        # Atajo de teclado F12 para cobrar rápido
        self.bind("<F12>", lambda e: self.submit_order())

        self._setup_ttk_styles()
        self._build_topbar()
        self._build_feedback_banner()

        # Contenedor de vistas (Vista 1: Caja POS | Vista 2: Historial y Administración)
        self.main_body = tk.Frame(self, bg="#0f172a")
        self.main_body.pack(fill="both", expand=True, padx=16, pady=(6, 14))

        self.view_pos = tk.Frame(self.main_body, bg="#0f172a")
        self.view_admin = tk.Frame(self.main_body, bg="#0f172a")

        self._build_pos_view()
        self._build_admin_view()

        self.switch_view("pos")
        self.refresh_all_data()
        self._schedule_live_sync()

    def _setup_ttk_styles(self):
        style = ttk.Style(self)
        style.theme_use("clam")
        style.configure(
            "Treeview",
            background="#1e293b",
            foreground="#f8fafc",
            fieldbackground="#1e293b",
            rowheight=34,
            font=("Segoe UI", 10),
        )
        style.configure(
            "Treeview.Heading",
            background="#334155",
            foreground="#f8fafc",
            font=("Segoe UI", 10, "bold"),
        )
        style.map("Treeview", background=[("selected", "#2563eb")], foreground=[("selected", "#ffffff")])

    # =========================================================================
    # BARRA SUPERIOR CLARA Y SIN RUIDO VISUAL
    # =========================================================================
    def _build_topbar(self):
        top = tk.Frame(self, bg="#1e293b", padx=18, pady=12, highlightthickness=1, highlightbackground="#334155")
        top.pack(fill="x")

        # Izquierda: Identidad + Selector de las 2 Vistas
        left = tk.Frame(top, bg="#1e293b")
        left.pack(side="left")

        tk.Label(
            left,
            text="CAJA",
            bg="#10b981",
            fg="#042f2e",
            font=("Segoe UI", 12, "bold"),
            padx=10,
            pady=4,
        ).pack(side="left", padx=(0, 12))

        tk.Label(
            left,
            text="Asociación Juvenil Araucana",
            bg="#1e293b",
            fg="#f8fafc",
            font=("Segoe UI", 14, "bold"),
        ).pack(side="left", padx=(0, 24))

        self.btn_nav_pos = tk.Button(
            left,
            text="🛒  1. TOMAR PEDIDO",
            command=lambda: self.switch_view("pos"),
            font=("Segoe UI", 10, "bold"),
            relief="flat",
            padx=16,
            pady=7,
            cursor="hand2",
        )
        self.btn_nav_pos.pack(side="left", padx=4)

        self.btn_nav_admin = tk.Button(
            left,
            text="📊  2. CAJA, HISTORIAL Y MENÚ",
            command=lambda: self.switch_view("admin"),
            font=("Segoe UI", 10, "bold"),
            relief="flat",
            padx=16,
            pady=7,
            cursor="hand2",
        )
        self.btn_nav_admin.pack(side="left", padx=4)

        # Derecha: Indicadores claros (Impresora + Cocina + Próximo Ticket)
        right = tk.Frame(top, bg="#1e293b")
        right.pack(side="right")

        printer_label = self.settings.get("printer_name") or detect_preferred_thermal_printer() or "Sin impresora"
        self.btn_printer_pill = tk.Button(
            right,
            text=f"🖨️ {printer_label}",
            command=self.open_printer_selector_modal,
            bg="#0f172a",
            fg="#cbd5e1",
            font=("Segoe UI", 9, "bold"),
            relief="flat",
            padx=12,
            pady=7,
            cursor="hand2",
        )
        self.btn_printer_pill.pack(side="left", padx=6)

        self.lbl_kitchen_pill = tk.Label(
            right,
            text="👨‍🍳 Cocina: 0 en curso",
            bg="#0f172a",
            fg="#fbbf24",
            font=("Segoe UI", 10, "bold"),
            padx=12,
            pady=7,
        )
        self.lbl_kitchen_pill.pack(side="left", padx=6)

        self.lbl_next_ticket = tk.Label(
            right,
            text="PRÓXIMO: #001",
            bg="#2563eb",
            fg="#ffffff",
            font=("Segoe UI", 11, "bold"),
            padx=14,
            pady=7,
        )
        self.lbl_next_ticket.pack(side="left", padx=(6, 0))

    def _build_feedback_banner(self):
        self.banner_frame = tk.Frame(self, bg="#065f46", padx=18, pady=8)
        self.lbl_banner_msg = tk.Label(
            self.banner_frame,
            text="",
            bg="#065f46",
            fg="#ecfdf5",
            font=("Segoe UI", 10, "bold"),
        )
        self.lbl_banner_msg.pack(side="left")

        self.btn_reprint_last = tk.Button(
            self.banner_frame,
            text="🖨️ Volver a imprimir este ticket",
            command=self.reprint_last_order,
            bg="#10b981",
            fg="#042f2e",
            font=("Segoe UI", 9, "bold"),
            relief="flat",
            padx=10,
            pady=3,
            cursor="hand2",
        )
        self.btn_reprint_last.pack(side="right")

    def show_banner(self, text: str, is_error: bool = False):
        bg = "#7f1d1d" if is_error else "#065f46"
        self.banner_frame.configure(bg=bg)
        self.lbl_banner_msg.configure(text=text, bg=bg)
        self.banner_frame.pack(fill="x", before=self.main_body)

    def switch_view(self, view_name: str):
        if view_name == "pos":
            self.view_admin.pack_forget()
            self.view_pos.pack(fill="both", expand=True)
            self.btn_nav_pos.configure(bg="#10b981", fg="#042f2e")
            self.btn_nav_admin.configure(bg="#334155", fg="#cbd5e1")
        else:
            self.view_pos.pack_forget()
            self.view_admin.pack(fill="both", expand=True)
            self.btn_nav_admin.configure(bg="#10b981", fg="#042f2e")
            self.btn_nav_pos.configure(bg="#334155", fg="#cbd5e1")
            self.refresh_admin_tables()

    # =========================================================================
    # VISTA 1: TOMAR PEDIDO Y COBRAR (DISEÑO UX DE ALTA VELOCIDAD)
    # =========================================================================
    def _build_pos_view(self):
        # Columna Izquierda (Menú por Categorías)
        left_col = tk.Frame(self.view_pos, bg="#0f172a")
        left_col.pack(side="left", fill="both", expand=True, padx=(0, 14))

        # Botones grandes de Categoría
        self.cat_bar = tk.Frame(left_col, bg="#0f172a")
        self.cat_bar.pack(fill="x", pady=(0, 10))

        # Contenedor scrollable de tarjetas de productos
        grid_wrap = tk.Frame(left_col, bg="#1e293b", highlightthickness=1, highlightbackground="#334155")
        grid_wrap.pack(fill="both", expand=True)

        self.prod_canvas = tk.Canvas(grid_wrap, bg="#0f172a", highlightthickness=0)
        scroll_y = ttk.Scrollbar(grid_wrap, orient="vertical", command=self.prod_canvas.yview)
        self.prod_grid = tk.Frame(self.prod_canvas, bg="#0f172a", padx=8, pady=8)

        self.prod_grid.bind(
            "<Configure>", lambda e: self.prod_canvas.configure(scrollregion=self.prod_canvas.bbox("all"))
        )
        self.prod_win = self.prod_canvas.create_window((0, 0), window=self.prod_grid, anchor="nw")
        self.prod_canvas.bind("<Configure>", lambda e: self.prod_canvas.itemconfig(self.prod_win, width=e.width))
        self.prod_canvas.configure(yscrollcommand=scroll_y.set)

        self.prod_canvas.pack(side="left", fill="both", expand=True)
        scroll_y.pack(side="right", fill="y")

        # Columna Derecha (Pedido Actual + Pago + Cobro en 1 Clic)
        right_col = tk.Frame(
            self.view_pos,
            bg="#1e293b",
            width=470,
            padx=16,
            pady=14,
            highlightthickness=1,
            highlightbackground="#334155",
        )
        right_col.pack(side="right", fill="y")
        right_col.pack_propagate(False)

        # Cabecera del Pedido
        cart_head = tk.Frame(right_col, bg="#1e293b")
        cart_head.pack(fill="x", pady=(0, 8))

        tk.Label(
            cart_head,
            text="1. Detalle del Pedido",
            bg="#1e293b",
            fg="#f8fafc",
            font=("Segoe UI", 12, "bold"),
        ).pack(side="left")

        tk.Button(
            cart_head,
            text="Vaciar todo",
            command=self.clear_cart,
            bg="#334155",
            fg="#cbd5e1",
            font=("Segoe UI", 9, "bold"),
            relief="flat",
            padx=10,
            pady=3,
            cursor="hand2",
        ).pack(side="right")

        # Lista interactiva de ítems (Cada fila tiene su propio [-] [+] y subtotal)
        cart_box = tk.Frame(right_col, bg="#0f172a", highlightthickness=1, highlightbackground="#334155")
        cart_box.pack(fill="both", expand=True, pady=(0, 10))

        self.cart_canvas = tk.Canvas(cart_box, bg="#0f172a", highlightthickness=0, height=210)
        cart_scroll = ttk.Scrollbar(cart_box, orient="vertical", command=self.cart_canvas.yview)
        self.cart_rows_frame = tk.Frame(self.cart_canvas, bg="#0f172a", padx=6, pady=6)

        self.cart_rows_frame.bind(
            "<Configure>", lambda e: self.cart_canvas.configure(scrollregion=self.cart_canvas.bbox("all"))
        )
        self.cart_win = self.cart_canvas.create_window((0, 0), window=self.cart_rows_frame, anchor="nw")
        self.cart_canvas.bind("<Configure>", lambda e: self.cart_canvas.itemconfig(self.cart_win, width=e.width))
        self.cart_canvas.configure(yscrollcommand=cart_scroll.set)

        self.cart_canvas.pack(side="left", fill="both", expand=True)
        cart_scroll.pack(side="right", fill="y")

        # Nota opcional para Cocina
        note_frame = tk.Frame(right_col, bg="#1e293b")
        note_frame.pack(fill="x", pady=(0, 10))
        tk.Label(
            note_frame,
            text="Nota para Cocina (opcional):",
            bg="#1e293b",
            fg="#94a3b8",
            font=("Segoe UI", 9, "bold"),
        ).pack(anchor="w")
        tk.Entry(
            note_frame,
            textvariable=self.notes_var,
            bg="#0f172a",
            fg="#f8fafc",
            insertbackground="#f8fafc",
            font=("Segoe UI", 10),
            relief="flat",
        ).pack(fill="x", ipady=6, pady=(3, 0))

        # Paso 2: Medio de Pago (3 Botones Grandes Tipo Toggle)
        tk.Label(
            right_col,
            text="2. Medio de Pago",
            bg="#1e293b",
            fg="#f8fafc",
            font=("Segoe UI", 11, "bold"),
        ).pack(anchor="w", pady=(0, 6))

        pay_btns_row = tk.Frame(right_col, bg="#1e293b")
        pay_btns_row.pack(fill="x", pady=(0, 8))

        self.pay_buttons = {}
        for code, text in [
            ("efectivo", "💵 EFECTIVO"),
            ("transferencia", "📱 MERCADO PAGO"),
            ("tarjeta", "💳 TARJETA"),
        ]:
            b = tk.Button(
                pay_btns_row,
                text=text,
                command=lambda m=code: self.select_payment_method(m),
                font=("Segoe UI", 9, "bold"),
                relief="flat",
                pady=8,
                cursor="hand2",
            )
            b.pack(side="left", expand=True, fill="x", padx=2)
            self.pay_buttons[code] = b

        # Caja contextual según medio de pago
        self.payment_context_box = tk.Frame(right_col, bg="#0f172a", padx=12, pady=10)
        self.payment_context_box.pack(fill="x", pady=(0, 10))

        # Sub-panel Efectivo
        self.cash_panel = tk.Frame(self.payment_context_box, bg="#0f172a")
        top_cash = tk.Frame(self.cash_panel, bg="#0f172a")
        top_cash.pack(fill="x")

        tk.Label(
            top_cash, text="¿Con cuánto paga? $", bg="#0f172a", fg="#cbd5e1", font=("Segoe UI", 10, "bold")
        ).pack(side="left")
        cash_inp = tk.Entry(
            top_cash,
            textvariable=self.cash_received_var,
            bg="#1e293b",
            fg="#ffffff",
            insertbackground="#ffffff",
            font=("Segoe UI", 12, "bold"),
            width=10,
            relief="flat",
            justify="right",
        )
        cash_inp.pack(side="left", padx=8, ipady=4)
        self.cash_received_var.trace_add("write", lambda *_: self.update_totals_ui())

        self.lbl_vuelto = tk.Label(
            top_cash, text="VUELTO: $0", bg="#0f172a", fg="#10b981", font=("Segoe UI", 12, "bold")
        )
        self.lbl_vuelto.pack(side="right")

        bills_row = tk.Frame(self.cash_panel, bg="#0f172a")
        bills_row.pack(fill="x", pady=(8, 0))
        for lbl, val in [
            ("Justo", "exact"),
            ("$5.000", 5000),
            ("$10.000", 10000),
            ("$20.000", 20000),
            ("$30.000", 30000),
            ("$50.000", 50000),
        ]:
            tk.Button(
                bills_row,
                text=lbl,
                command=lambda v=val: self.set_quick_cash(v),
                bg="#334155",
                fg="#f8fafc",
                font=("Segoe UI", 8, "bold"),
                relief="flat",
                pady=4,
                cursor="hand2",
            ).pack(side="left", expand=True, fill="x", padx=2)

        # Sub-panel Mercado Pago
        self.mp_panel = tk.Frame(self.payment_context_box, bg="#0f172a")
        tk.Label(
            self.mp_panel,
            text="Alias Mercado Pago:   araucana24",
            bg="#0f172a",
            fg="#38bdf8",
            font=("Segoe UI", 13, "bold"),
            pady=8,
        ).pack()

        # Sub-panel Tarjeta
        self.card_panel = tk.Frame(self.payment_context_box, bg="#0f172a")
        tk.Label(
            self.card_panel,
            text="Cobro con Tarjeta por Posnet",
            bg="#0f172a",
            fg="#cbd5e1",
            font=("Segoe UI", 11),
            pady=8,
        ).pack()

        # Paso 3: Total + Botón Gigante de Cobro e Impresión Directa
        total_banner = tk.Frame(right_col, bg="#064e3b", padx=14, pady=10)
        total_banner.pack(fill="x", pady=(0, 8))
        tk.Label(
            total_banner,
            text="TOTAL A COBRAR",
            bg="#064e3b",
            fg="#a7f3d0",
            font=("Segoe UI", 11, "bold"),
        ).pack(side="left")
        self.lbl_total_amount = tk.Label(
            total_banner,
            text="$0",
            bg="#064e3b",
            fg="#ffffff",
            font=("Segoe UI", 20, "bold"),
        )
        self.lbl_total_amount.pack(side="right")

        self.btn_submit = tk.Button(
            right_col,
            text="🖨️  COBRAR E IMPRIMIR TICKET (F12)",
            command=self.submit_order,
            bg="#10b981",
            fg="#042f2e",
            font=("Segoe UI", 12, "bold"),
            relief="flat",
            pady=13,
            cursor="hand2",
        )
        self.btn_submit.pack(fill="x", side="bottom")

        self.select_payment_method("efectivo")

    # =========================================================================
    # VISTA 2: HISTORIAL DE CAJA Y GESTIÓN DE MENÚ
    # =========================================================================
    def _build_admin_view(self):
        # Tarjetas de resumen de caja arriba
        stats_row = tk.Frame(self.view_admin, bg="#0f172a")
        stats_row.pack(fill="x", pady=(0, 12))

        self.stat_total = self._create_stat_card(stats_row, "TOTAL RECAUDADO HOY", "$0", "#10b981")
        self.stat_orders = self._create_stat_card(stats_row, "PEDIDOS COBRADOS", "0", "#38bdf8")
        self.stat_cash = self._create_stat_card(stats_row, "💵 EFECTIVO EN CAJA", "$0", "#fbbf24")
        self.stat_digital = self._create_stat_card(stats_row, "📱 MERCADO PAGO / TARJETA", "$0", "#a78bfa")

        # Dos columnas abajo: Historial de Tickets (Izquierda) y Menú (Derecha)
        split = tk.Frame(self.view_admin, bg="#0f172a")
        split.pack(fill="both", expand=True)

        # Panel Historial de Tickets
        orders_card = tk.Frame(
            split, bg="#1e293b", padx=14, pady=12, highlightthickness=1, highlightbackground="#334155"
        )
        orders_card.pack(side="left", fill="both", expand=True, padx=(0, 8))

        o_head = tk.Frame(orders_card, bg="#1e293b")
        o_head.pack(fill="x", pady=(0, 8))
        tk.Label(
            o_head,
            text="Tickets Emitidos y Estado en Cocina",
            bg="#1e293b",
            fg="#f8fafc",
            font=("Segoe UI", 12, "bold"),
        ).pack(side="left")

        tk.Button(
            o_head,
            text="🔄 Reiniciar Contador a #001",
            command=self.reset_ticket_counter,
            bg="#334155",
            fg="#cbd5e1",
            font=("Segoe UI", 9, "bold"),
            relief="flat",
            padx=10,
            pady=5,
        ).pack(side="right", padx=4)

        tk.Button(
            o_head,
            text="❌ Anular Seleccionado",
            command=self.cancel_selected_order,
            bg="#ef4444",
            fg="#ffffff",
            font=("Segoe UI", 9, "bold"),
            relief="flat",
            padx=10,
            pady=5,
        ).pack(side="right", padx=4)

        tk.Button(
            o_head,
            text="🖨️ Reimprimir Ticket Seleccionado",
            command=self.reprint_selected_order,
            bg="#10b981",
            fg="#042f2e",
            font=("Segoe UI", 9, "bold"),
            relief="flat",
            padx=10,
            pady=5,
        ).pack(side="right", padx=4)

        cols = ("id", "ticket", "time", "items", "payment", "total", "kstatus")
        self.orders_tree = ttk.Treeview(orders_card, columns=cols, show="headings")
        self.orders_tree.heading("id", text="ID")
        self.orders_tree.heading("ticket", text="Ticket")
        self.orders_tree.heading("time", text="Hora")
        self.orders_tree.heading("items", text="Productos")
        self.orders_tree.heading("payment", text="Pago")
        self.orders_tree.heading("total", text="Total")
        self.orders_tree.heading("kstatus", text="Estado Cocina")

        self.orders_tree.column("id", width=40, anchor="center")
        self.orders_tree.column("ticket", width=70, anchor="center")
        self.orders_tree.column("time", width=125, anchor="center")
        self.orders_tree.column("items", width=260, anchor="w")
        self.orders_tree.column("payment", width=105, anchor="center")
        self.orders_tree.column("total", width=90, anchor="e")
        self.orders_tree.column("kstatus", width=155, anchor="center")
        self.orders_tree.pack(fill="both", expand=True)

        # Panel Menú Rápido (Derecha)
        menu_card = tk.Frame(
            split, bg="#1e293b", width=430, padx=14, pady=12, highlightthickness=1, highlightbackground="#334155"
        )
        menu_card.pack(side="right", fill="y")
        menu_card.pack_propagate(False)

        m_head = tk.Frame(menu_card, bg="#1e293b")
        m_head.pack(fill="x", pady=(0, 8))
        tk.Label(m_head, text="Precios del Menú", bg="#1e293b", fg="#f8fafc", font=("Segoe UI", 12, "bold")).pack(
            side="left"
        )
        tk.Button(
            m_head,
            text="+ Agregar Plato",
            command=self.open_add_product_modal,
            bg="#2563eb",
            fg="#ffffff",
            font=("Segoe UI", 9, "bold"),
            relief="flat",
            padx=10,
            pady=4,
        ).pack(side="right")

        mcols = ("id", "name", "price")
        self.menu_tree = ttk.Treeview(menu_card, columns=mcols, show="headings")
        self.menu_tree.heading("id", text="ID")
        self.menu_tree.heading("name", text="Plato / Bebida")
        self.menu_tree.heading("price", text="Precio")
        self.menu_tree.column("id", width=40, anchor="center")
        self.menu_tree.column("name", width=250, anchor="w")
        self.menu_tree.column("price", width=95, anchor="e")
        self.menu_tree.pack(fill="both", expand=True)

        m_foot = tk.Frame(menu_card, bg="#1e293b")
        m_foot.pack(fill="x", pady=(8, 0))
        tk.Button(
            m_foot,
            text="🗑️ Eliminar Seleccionado",
            command=self.delete_selected_product,
            bg="#475569",
            fg="#ffffff",
            font=("Segoe UI", 9),
            relief="flat",
            padx=8,
            pady=4,
        ).pack(side="left")
        tk.Button(
            m_foot,
            text="🔄 Restaurar Menú Original",
            command=self.restore_araucana_menu,
            bg="#334155",
            fg="#cbd5e1",
            font=("Segoe UI", 9),
            relief="flat",
            padx=8,
            pady=4,
        ).pack(side="right")

    def _create_stat_card(self, parent, label, value, color):
        card = tk.Frame(parent, bg="#1e293b", padx=16, pady=12, highlightthickness=1, highlightbackground="#334155")
        card.pack(side="left", fill="x", expand=True, padx=4)
        tk.Label(card, text=label, bg="#1e293b", fg="#94a3b8", font=("Segoe UI", 9, "bold")).pack(anchor="w")
        val_lbl = tk.Label(card, text=value, bg="#1e293b", fg=color, font=("Segoe UI", 18, "bold"))
        val_lbl.pack(anchor="w", pady=(4, 0))
        return val_lbl

    # =========================================================================
    # RENDERIZADO DEL MENÚ Y CARRITO INTERACTIVO
    # =========================================================================
    def refresh_all_data(self):
        conn = get_connection()
        self.products = [
            dict(r) for r in conn.execute("SELECT * FROM products WHERE active = 1 ORDER BY id ASC").fetchall()
        ]
        conn.close()
        self.settings = get_settings()

        seq = int(self.settings.get("ticket_Sequence", 1))
        self.lbl_next_ticket.config(text=f"PRÓXIMO: #{seq:03d}")

        printer_label = self.settings.get("printer_name") or detect_preferred_thermal_printer() or "Sin impresora"
        self.btn_printer_pill.config(text=f"🖨️ {printer_label}")

        self.render_category_bar()
        self.render_product_cards()
        self.render_cart_rows()
        self.refresh_admin_tables()

    def render_category_bar(self):
        for w in self.cat_bar.winfo_children():
            w.destroy()

        categories = ["Todos", "Almuerzo", "Principales", "Bebidas", "Para Llevar", "Postres"]
        icons = {
            "Todos": "⭐ TODOS",
            "Almuerzo": "🍽️ ALMUERZO",
            "Principales": "🍕 PRINCIPALES",
            "Bebidas": "🥤 BEBIDAS",
            "Para Llevar": "🥡 PARA LLEVAR",
            "Postres": "🍰 POSTRES",
        }

        for cat in categories:
            is_active = self.selected_category == cat
            bg = "#2563eb" if is_active else "#1e293b"
            fg = "#ffffff" if is_active else "#cbd5e1"
            btn = tk.Button(
                self.cat_bar,
                text=icons.get(cat, cat.upper()),
                command=lambda c=cat: self.select_category(c),
                bg=bg,
                fg=fg,
                font=("Segoe UI", 10, "bold"),
                relief="flat",
                padx=14,
                pady=8,
                cursor="hand2",
            )
            btn.pack(side="left", expand=True, fill="x", padx=3)

    def select_category(self, category: str):
        self.selected_category = category
        self.render_category_bar()
        self.render_product_cards()

    def render_product_cards(self):
        for w in self.prod_grid.winfo_children():
            w.destroy()

        filtered = [
            p for p in self.products if self.selected_category == "Todos" or p["category"] == self.selected_category
        ]

        cols = 3
        for c in range(cols):
            self.prod_grid.columnconfigure(c, weight=1, uniform="menu_col")

        for idx, p in enumerate(filtered):
            r = idx // cols
            c = idx % cols
            accent = CATEGORY_COLORS.get(p["category"], "#10b981")

            card = tk.Frame(
                self.prod_grid,
                bg="#1e293b",
                highlightthickness=1,
                highlightbackground="#334155",
                cursor="hand2",
            )
            card.grid(row=r, column=c, sticky="nsew", padx=5, pady=5)

            # Barra superior de color de categoría
            stripe = tk.Frame(card, bg=accent, height=5)
            stripe.pack(fill="x")

            body = tk.Frame(card, bg="#1e293b", padx=12, pady=10)
            body.pack(fill="both", expand=True)

            name_lbl = tk.Label(
                body,
                text=p["name"],
                bg="#1e293b",
                fg="#f8fafc",
                font=("Segoe UI", 11, "bold"),
                wraplength=220,
                justify="left",
                anchor="w",
            )
            name_lbl.pack(fill="x", pady=(0, 6))

            foot = tk.Frame(body, bg="#1e293b")
            foot.pack(fill="x", side="bottom")

            price_lbl = tk.Label(
                foot,
                text=fmt_money(p["price"]),
                bg="#1e293b",
                fg="#10b981",
                font=("Segoe UI", 13, "bold"),
            )
            price_lbl.pack(side="left")

            add_pill = tk.Label(
                foot,
                text=" + AGREGAR ",
                bg="#334155",
                fg="#f8fafc",
                font=("Segoe UI", 8, "bold"),
                padx=6,
                pady=2,
            )
            add_pill.pack(side="right")

            for widget in (card, stripe, body, name_lbl, foot, price_lbl, add_pill):
                widget.bind("<Button-1>", lambda e, prod=p: self.add_to_cart(prod))

    def add_to_cart(self, prod: dict):
        for item in self.cart:
            if item["product_id"] == prod["id"]:
                item["quantity"] += 1
                self.render_cart_rows()
                return
        self.cart.append(
            {
                "product_id": prod["id"],
                "name": prod["name"],
                "category": prod["category"],
                "price": float(prod["price"]),
                "quantity": 1,
            }
        )
        self.render_cart_rows()

    def change_item_qty(self, product_id: int, delta: int):
        for idx, item in enumerate(self.cart):
            if item["product_id"] == product_id:
                item["quantity"] += delta
                if item["quantity"] <= 0:
                    self.cart.pop(idx)
                break
        self.render_cart_rows()

    def clear_cart(self):
        self.cart = []
        self.render_cart_rows()

    def get_cart_total(self) -> float:
        return sum(i["price"] * i["quantity"] for i in self.cart)

    def render_cart_rows(self):
        for w in self.cart_rows_frame.winfo_children():
            w.destroy()

        if not self.cart:
            tk.Label(
                self.cart_rows_frame,
                text="Tocá los productos de la izquierda\npara armar el pedido.",
                bg="#0f172a",
                fg="#64748b",
                font=("Segoe UI", 10),
                pady=45,
            ).pack(fill="x")
        else:
            for item in self.cart:
                row = tk.Frame(
                    self.cart_rows_frame,
                    bg="#1e293b",
                    padx=8,
                    pady=6,
                    highlightthickness=1,
                    highlightbackground="#334155",
                )
                row.pack(fill="x", pady=3)

                # Controles [-] Cant [+] directos en la fila
                ctrl = tk.Frame(row, bg="#1e293b")
                ctrl.pack(side="left", padx=(0, 8))

                tk.Button(
                    ctrl,
                    text="−",
                    command=lambda pid=item["product_id"]: self.change_item_qty(pid, -1),
                    bg="#334155",
                    fg="#ffffff",
                    font=("Segoe UI", 10, "bold"),
                    width=2,
                    relief="flat",
                    cursor="hand2",
                ).pack(side="left")

                tk.Label(
                    ctrl,
                    text=f" {item['quantity']} ",
                    bg="#1e293b",
                    fg="#10b981",
                    font=("Segoe UI", 11, "bold"),
                    width=3,
                ).pack(side="left")

                tk.Button(
                    ctrl,
                    text="+",
                    command=lambda pid=item["product_id"]: self.change_item_qty(pid, 1),
                    bg="#334155",
                    fg="#ffffff",
                    font=("Segoe UI", 10, "bold"),
                    width=2,
                    relief="flat",
                    cursor="hand2",
                ).pack(side="left")

                tk.Label(
                    row,
                    text=item["name"],
                    bg="#1e293b",
                    fg="#f8fafc",
                    font=("Segoe UI", 10, "bold"),
                    anchor="w",
                ).pack(side="left", fill="x", expand=True)

                sub = item["price"] * item["quantity"]
                tk.Label(
                    row,
                    text=fmt_money(sub),
                    bg="#1e293b",
                    fg="#f8fafc",
                    font=("Segoe UI", 11, "bold"),
                ).pack(side="right", padx=(6, 2))

        self.update_totals_ui()

    def select_payment_method(self, method: str):
        self.payment_method = method
        for code, btn in self.pay_buttons.items():
            if code == method:
                btn.configure(bg="#10b981", fg="#042f2e")
            else:
                btn.configure(bg="#0f172a", fg="#94a3b8")

        self.cash_panel.pack_forget()
        self.mp_panel.pack_forget()
        self.card_panel.pack_forget()

        if method == "efectivo":
            self.cash_panel.pack(fill="x")
        elif method == "transferencia":
            self.mp_panel.pack(fill="x")
        else:
            self.card_panel.pack(fill="x")

        self.update_totals_ui()

    def set_quick_cash(self, val):
        total = self.get_cart_total()
        if val == "exact":
            self.cash_received_var.set(str(int(total)))
        else:
            self.cash_received_var.set(str(val))

    def update_totals_ui(self):
        total = self.get_cart_total()
        self.lbl_total_amount.config(text=fmt_money(total))

        try:
            paid = float(self.cash_received_var.get() or 0)
        except ValueError:
            paid = 0

        if paid <= 0 or total <= 0:
            self.lbl_vuelto.config(text="VUELTO: $0", fg="#10b981")
        elif paid < total:
            self.lbl_vuelto.config(text=f"FALTAN {fmt_money(total - paid)}", fg="#ef4444")
        else:
            self.lbl_vuelto.config(text=f"VUELTO: {fmt_money(paid - total)}", fg="#10b981")

    # =========================================================================
    # COBRAR, IMPRIMIR EN XPRINTER XP-80 A ANCHO COMPLETO Y ENVIAR A COCINA
    # =========================================================================
    def submit_order(self):
        if not self.cart:
            messagebox.showwarning("Pedido vacío", "Agregá al menos un producto antes de cobrar.")
            return

        total = self.get_cart_total()
        notes = self.notes_var.get().strip()
        amount_received = total
        change_amount = 0.0

        if self.payment_method == "efectivo":
            try:
                raw_paid = float(self.cash_received_var.get() or 0)
            except ValueError:
                raw_paid = 0
            if raw_paid > 0 and raw_paid < total:
                messagebox.showerror("Efectivo insuficiente", "El monto recibido es menor al total a cobrar.")
                return
            if raw_paid > 0:
                amount_received = raw_paid
                change_amount = raw_paid - total

        conn = get_connection()
        cur = conn.cursor()
        seq_row = cur.execute("SELECT value FROM settings WHERE key = 'ticket_Sequence'").fetchone()
        ticket_num = int(seq_row["value"]) if seq_row else 1
        event_name = self.settings.get("event_name", "Asociación Juvenil Araucana")
        now_str = datetime.now().strftime("%d/%m/%Y %H:%M:%S")

        cur.execute(
            """
            INSERT INTO orders (
                ticket_number, event_name, total, payment_method,
                amount_received, change_amount, notes, status,
                kitchen_status, kitchen_printed, created_at
            )
            VALUES (?, ?, ?, ?, ?, ?, ?, 'completado', 'pendiente', 0, ?)
        """,
            (ticket_num, event_name, total, self.payment_method, amount_received, change_amount, notes, now_str),
        )
        order_id = cur.lastrowid

        for item in self.cart:
            sub = item["price"] * item["quantity"]
            cur.execute(
                """
                INSERT INTO order_items (order_id, product_id, product_name, category, quantity, unit_price, subtotal)
                VALUES (?, ?, ?, ?, ?, ?, ?)
            """,
                (order_id, item["product_id"], item["name"], item["category"], item["quantity"], item["price"], sub),
            )

        cur.execute(
            "INSERT OR REPLACE INTO settings (key, value) VALUES ('ticket_Sequence', ?)", (str(ticket_num + 1),)
        )
        conn.commit()

        order_row = dict(cur.execute("SELECT * FROM orders WHERE id = ?", (order_id,)).fetchone())
        order_row["items"] = [
            dict(r) for r in cur.execute("SELECT * FROM order_items WHERE order_id = ?", (order_id,)).fetchall()
        ]
        conn.close()

        self.last_created_order = order_row

        # Limpiar pedido inmediatamente para el siguiente cliente
        self.cart = []
        self.cash_received_var.set("")
        self.notes_var.set("")
        self.refresh_all_data()

        # Imprimir directo en la Xprinter XP-80 (48 columnas ancho completo + corte automático)
        try:
            used_printer = print_ticket_direct(order_row, self.settings, only_kitchen=False)
            self.show_banner(
                f"✅ PEDIDO #{order_row['ticket_number']:03d} COBRADO ({fmt_money(total)})  —  Impreso en {used_printer} y enviado a Cocina"
            )
        except Exception as e:
            self.show_banner(
                f"⚠️ Pedido #{order_row['ticket_number']:03d} enviado a Cocina, pero no se pudo imprimir: {e}",
                is_error=True,
            )

    def reprint_last_order(self):
        if not self.last_created_order:
            return
        try:
            used_printer = print_ticket_direct(self.last_created_order, self.settings, only_kitchen=False)
            self.show_banner(
                f"🖨️ Ticket #{self.last_created_order['ticket_number']:03d} reimpreso en {used_printer}"
            )
        except Exception as e:
            messagebox.showerror("Error de impresión", str(e))

    # =========================================================================
    # ADMINISTRACIÓN, HISTORIAL Y SINCRONIZACIÓN CON COCINA
    # =========================================================================
    def refresh_admin_tables(self):
        conn = get_connection()
        orders = [dict(r) for r in conn.execute("SELECT * FROM orders ORDER BY id DESC LIMIT 80").fetchall()]
        for o in orders:
            o["items"] = [
                dict(i) for i in conn.execute("SELECT * FROM order_items WHERE order_id = ?", (o["id"],)).fetchall()
            ]

        totals = dict(
            conn.execute(
                """
            SELECT
                COUNT(*) as count_orders,
                COALESCE(SUM(total), 0) as total_revenue,
                COALESCE(SUM(CASE WHEN payment_method = 'efectivo' THEN total ELSE 0 END), 0) as cash_total,
                COALESCE(SUM(CASE WHEN payment_method != 'efectivo' THEN total ELSE 0 END), 0) as digital_total,
                COALESCE(SUM(CASE WHEN kitchen_status IN ('pendiente', 'preparando') THEN 1 ELSE 0 END), 0) as k_pending,
                COALESCE(SUM(CASE WHEN kitchen_status = 'finalizado' THEN 1 ELSE 0 END), 0) as k_done
            FROM orders
            WHERE status = 'completado'
        """
            ).fetchone()
        )
        conn.close()

        self.lbl_kitchen_pill.config(
            text=f"👨‍🍳 Cocina: {totals['k_pending']} en curso | {totals['k_done']} listos"
        )
        self.stat_total.config(text=fmt_money(totals["total_revenue"]))
        self.stat_orders.config(text=str(totals["count_orders"]))
        self.stat_cash.config(text=fmt_money(totals["cash_total"]))
        self.stat_digital.config(text=fmt_money(totals["digital_total"]))

        sel_order = self.orders_tree.selection()
        keep_id = sel_order[0] if sel_order else None

        for r in self.orders_tree.get_children():
            self.orders_tree.delete(r)

        for o in orders:
            items_str = ", ".join(f"{i['quantity']}x {i['product_name']}" for i in o["items"])
            if o["status"] == "anulado":
                k_txt = "❌ ANULADO"
            elif o["kitchen_status"] == "finalizado":
                k_txt = f"✅ ENTREGADO ({o['finished_at'] or 'Listo'})"
            elif o["kitchen_status"] == "preparando":
                k_txt = "👨‍🍳 PREPARANDO"
            else:
                k_txt = "🔔 EN COCINA"

            pay_str = "MERCADO PAGO" if o["payment_method"] == "transferencia" else o["payment_method"].upper()
            self.orders_tree.insert(
                "",
                "end",
                iid=str(o["id"]),
                values=(
                    o["id"],
                    f"#{o['ticket_number']:03d}",
                    o["created_at"],
                    items_str,
                    pay_str,
                    fmt_money(o["total"]),
                    k_txt,
                ),
            )

        if keep_id and self.orders_tree.exists(keep_id):
            self.orders_tree.selection_set(keep_id)

        for r in self.menu_tree.get_children():
            self.menu_tree.delete(r)
        for p in self.products:
            self.menu_tree.insert("", "end", iid=str(p["id"]), values=(p["id"], p["name"], fmt_money(p["price"])))

    def _schedule_live_sync(self):
        try:
            self.refresh_admin_tables()
        except Exception:
            pass
        self.after(2000, self._schedule_live_sync)

    def reprint_selected_order(self):
        sel = self.orders_tree.selection()
        if not sel:
            messagebox.showinfo("Seleccioná un pedido", "Hacé clic en un pedido de la tabla para reimprimir su ticket.")
            return
        oid = int(sel[0])
        conn = get_connection()
        order = dict(conn.execute("SELECT * FROM orders WHERE id = ?", (oid,)).fetchone())
        order["items"] = [
            dict(r) for r in conn.execute("SELECT * FROM order_items WHERE order_id = ?", (oid,)).fetchall()
        ]
        conn.close()
        try:
            used_printer = print_ticket_direct(order, self.settings, only_kitchen=False)
            messagebox.showinfo("Reimpreso", f"Ticket #{order['ticket_number']:03d} enviado a {used_printer}.")
        except Exception as e:
            messagebox.showerror("Error al imprimir", str(e))

    def cancel_selected_order(self):
        sel = self.orders_tree.selection()
        if not sel:
            return
        oid = int(sel[0])
        if not messagebox.askyesno("Anular pedido", "¿Seguro que querés anular este pedido?"):
            return
        conn = get_connection()
        conn.execute("UPDATE orders SET status = 'anulado' WHERE id = ?", (oid,))
        conn.commit()
        conn.close()
        self.refresh_all_data()

    def reset_ticket_counter(self):
        if not messagebox.askyesno("Reiniciar numeración", "¿Volver el próximo ticket a #001?"):
            return
        update_settings({"ticket_Sequence": "1"})
        self.refresh_all_data()

    def open_printer_selector_modal(self):
        win = tk.Toplevel(self)
        win.title("Seleccionar Ticketera")
        win.geometry("420x250")
        win.configure(bg="#1e293b")
        win.grab_set()

        tk.Label(
            win,
            text="Impresora Térmica Conectada:",
            bg="#1e293b",
            fg="#f8fafc",
            font=("Segoe UI", 11, "bold"),
        ).pack(anchor="w", padx=20, pady=(18, 6))

        printers = list_windows_printers()
        cmb = ttk.Combobox(win, values=printers, width=42, font=("Segoe UI", 10))
        curr = self.settings.get("printer_name") or detect_preferred_thermal_printer()
        if curr in printers:
            cmb.set(curr)
        elif printers:
            cmb.set(printers[0])
        cmb.pack(padx=20, pady=4)

        dbl_var = tk.BooleanVar(value=str(self.settings.get("print_double_ticket", "1")) == "1")
        tk.Checkbutton(
            win,
            text="Imprimir doble talón (Cliente + Comanda de Cocina)",
            variable=dbl_var,
            bg="#1e293b",
            fg="#cbd5e1",
            selectcolor="#0f172a",
            font=("Segoe UI", 10),
        ).pack(anchor="w", padx=20, pady=12)

        def save():
            update_settings(
                {
                    "printer_name": cmb.get().strip(),
                    "print_double_ticket": "1" if dbl_var.get() else "0",
                    "print_mode": "windows_raw",
                }
            )
            self.refresh_all_data()
            win.destroy()

        tk.Button(
            win,
            text="Guardar Ticketera",
            command=save,
            bg="#10b981",
            fg="#042f2e",
            font=("Segoe UI", 10, "bold"),
            relief="flat",
            padx=16,
            pady=8,
        ).pack(pady=10)

    def open_add_product_modal(self):
        win = tk.Toplevel(self)
        win.title("Nuevo Plato o Bebida")
        win.geometry("380x290")
        win.configure(bg="#1e293b")
        win.grab_set()

        tk.Label(win, text="Nombre del Plato/Bebida:", bg="#1e293b", fg="#cbd5e1").pack(anchor="w", padx=18, pady=(14, 2))
        e_name = tk.Entry(win, bg="#0f172a", fg="#ffffff", width=36)
        e_name.pack(padx=18, ipady=4)

        tk.Label(win, text="Categoría:", bg="#1e293b", fg="#cbd5e1").pack(anchor="w", padx=18, pady=(10, 2))
        e_cat = ttk.Combobox(
            win, values=["Almuerzo", "Principales", "Bebidas", "Para Llevar", "Postres"], width=33, state="readonly"
        )
        e_cat.set("Principales")
        e_cat.pack(padx=18)

        tk.Label(win, text="Precio ($):", bg="#1e293b", fg="#cbd5e1").pack(anchor="w", padx=18, pady=(10, 2))
        e_price = tk.Entry(win, bg="#0f172a", fg="#ffffff", width=36)
        e_price.pack(padx=18, ipady=4)

        def save():
            name = e_name.get().strip()
            cat = e_cat.get().strip() or "Principales"
            try:
                price = float(e_price.get() or 0)
            except ValueError:
                return
            if not name:
                return
            conn = get_connection()
            conn.execute(
                "INSERT INTO products (name, category, price, stock_enabled, stock_qty, is_combo, description) VALUES (?, ?, ?, 0, 0, 0, '')",
                (name, cat, price),
            )
            conn.commit()
            conn.close()
            win.destroy()
            self.refresh_all_data()

        tk.Button(
            win,
            text="Guardar en el Menú",
            command=save,
            bg="#10b981",
            fg="#042f2e",
            font=("Segoe UI", 10, "bold"),
            relief="flat",
            padx=14,
            pady=8,
        ).pack(pady=16)

    def delete_selected_product(self):
        sel = self.menu_tree.selection()
        if not sel:
            return
        pid = int(sel[0])
        if not messagebox.askyesno("Eliminar", "¿Quitar este producto del menú?"):
            return
        conn = get_connection()
        conn.execute("DELETE FROM products WHERE id = ?", (pid,))
        conn.commit()
        conn.close()
        self.refresh_all_data()

    def restore_araucana_menu(self):
        if not messagebox.askyesno("Restaurar Menú", "¿Volver a cargar el menú original del PDF?"):
            return
        reset_menu_to_araucana()
        self.refresh_all_data()


if __name__ == "__main__":
    app = ProgramaCajaApp()
    app.mainloop()
