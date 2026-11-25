from datetime import datetime
import tkinter as tk
from tkinter import filedialog, messagebox
import customtkinter as ctk

from database import (
    close_cash_session,
    get_active_cash_session,
    get_connection,
    get_session_live_metrics,
    get_settings,
    init_db,
    open_new_cash_session,
    reset_menu_to_araucana,
    update_settings,
)
from image_helper import import_custom_product_image, load_ctk_product_image
from printer import (
    detect_preferred_thermal_printer,
    format_cash_closure_text,
    format_double_ticket_text,
    list_windows_printers,
    print_cash_closure_direct,
    print_test_ticket_direct,
    print_ticket_direct,
)

ctk.set_appearance_mode("dark")
ctk.set_default_color_theme("blue")


def fmt_money(val: float) -> str:
    return f"${float(val or 0):,.0f}".replace(",", ".")


CATEGORY_COLORS = {
    "Almuerzo": "#f59e0b",
    "Principales": "#ef4444",
    "Bebidas": "#3b82f6",
    "Para Llevar": "#8b5cf6",
    "Postres": "#ec4899",
}


class ProgramaCajaApp(ctk.CTk):
    def __init__(self):
        super().__init__()
        init_db()

        self.settings = get_settings()
        detected = detect_preferred_thermal_printer()
        if detected and not self.settings.get("printer_name"):
            self.settings = update_settings({"printer_name": detected, "print_mode": "windows_raw"})

        self.title("CAJA POS — Asociación Juvenil Araucana")
        self.geometry("1380x840")
        self.minsize(1150, 720)
        self.configure(fg_color="#0b1120")

        # Estado de la aplicación
        self.sidebar_expanded = True
        self.current_section = "pos"
        self.active_session = get_active_cash_session()
        self.products = []
        self.image_cache = {}
        self.cart = []  # [{product_id, name, category, price, quantity, stock_enabled, stock_qty}]
        self.selected_category = "Todos"
        self.search_var = tk.StringVar(value="")
        self.payment_method = "efectivo"
        self.cash_received_var = tk.StringVar(value="")
        self.notes_var = tk.StringVar(value="")
        self.last_order = None

        self.bind("<F12>", lambda e: self.submit_order())

        self._build_layout()
        self.refresh_all()
        self._schedule_background_sync()

    # =========================================================================
    # ESTRUCTURA GENERAL: MENÚ LATERAL DESPLEGABLE (IZQUIERDA) + CONTENIDO
    # =========================================================================
    def _build_layout(self):
        # 1. Menú Lateral Izquierdo Desplegable
        self.sidebar = ctk.CTkFrame(
            self,
            width=245,
            corner_radius=0,
            fg_color="#111827",
            border_width=1,
            border_color="#1f2937",
        )
        self.sidebar.pack(side="left", fill="y")
        self.sidebar.pack_propagate(False)

        # Cabecera del Sidebar con botón ☰ Desplegar/Contraer
        sb_top = ctk.CTkFrame(self.sidebar, fg_color="transparent")
        sb_top.pack(fill="x", padx=12, pady=(14, 10))

        self.btn_toggle_sb = ctk.CTkButton(
            sb_top,
            text="☰",
            width=42,
            height=38,
            corner_radius=8,
            fg_color="#1f2937",
            hover_color="#374151",
            font=ctk.CTkFont(size=18, weight="bold"),
            command=self.toggle_sidebar,
        )
        self.btn_toggle_sb.pack(side="left")

        self.lbl_brand = ctk.CTkLabel(
            sb_top,
            text="  ARAUCANA POS",
            font=ctk.CTkFont(size=15, weight="bold"),
            text_color="#10b981",
        )
        self.lbl_brand.pack(side="left", padx=4)

        # Botones de navegación del menú lateral
        self.nav_buttons = {}
        nav_items = [
            ("pos", "🛒", "Punto de Venta (Caja)"),
            ("menu", "🍽️", "Platos, Fotos y Stock"),
            ("orders", "🧾", "Tickets Emitidos"),
            ("closure", "🔐", "Cierre / Nueva Caja"),
            ("settings", "⚙️", "Ticketera y Ajustes"),
        ]

        self.nav_container = ctk.CTkFrame(self.sidebar, fg_color="transparent")
        self.nav_container.pack(fill="x", padx=10, pady=6)

        for key, icon, label in nav_items:
            btn = ctk.CTkButton(
                self.nav_container,
                text=f" {icon}   {label}",
                anchor="w",
                height=44,
                corner_radius=10,
                font=ctk.CTkFont(size=13, weight="bold"),
                fg_color="transparent",
                text_color="#cbd5e1",
                hover_color="#1f2937",
                command=lambda k=key: self.show_section(k),
            )
            btn.pack(fill="x", pady=4)
            self.nav_buttons[key] = (btn, icon, label)

        # Panel inferior del Sidebar: Estado de Caja Abierta/Cerrada + Acción Rápida
        self.sb_footer = ctk.CTkFrame(self.sidebar, fg_color="#1e293b", corner_radius=12)
        self.sb_footer.pack(side="bottom", fill="x", padx=10, pady=14)

        self.lbl_sb_session_status = ctk.CTkLabel(
            self.sb_footer,
            text="🟢 CAJA ABIERTA",
            font=ctk.CTkFont(size=12, weight="bold"),
            text_color="#10b981",
        )
        self.lbl_sb_session_status.pack(padx=10, pady=(10, 2))

        self.lbl_sb_session_total = ctk.CTkLabel(
            self.sb_footer,
            text="Recaudado: $0",
            font=ctk.CTkFont(size=12),
            text_color="#cbd5e1",
        )
        self.lbl_sb_session_total.pack(padx=10, pady=(0, 8))

        self.btn_sb_quick_caja = ctk.CTkButton(
            self.sb_footer,
            text="🔒 Cerrar / Arqueo Caja",
            height=34,
            corner_radius=8,
            fg_color="#f59e0b",
            hover_color="#d97706",
            text_color="#0f172a",
            font=ctk.CTkFont(size=12, weight="bold"),
            command=lambda: self.show_section("closure"),
        )
        self.btn_sb_quick_caja.pack(fill="x", padx=10, pady=(0, 10))

        # 2. Contenedor Principal Derecho (Barra superior de estado + Vistas)
        self.right_wrapper = ctk.CTkFrame(self, fg_color="#0b1120", corner_radius=0)
        self.right_wrapper.pack(side="left", fill="both", expand=True)

        # Barra superior informativa
        self.topbar = ctk.CTkFrame(
            self.right_wrapper,
            height=60,
            corner_radius=0,
            fg_color="#111827",
            border_width=1,
            border_color="#1f2937",
        )
        self.topbar.pack(fill="x")

        tb_left = ctk.CTkFrame(self.topbar, fg_color="transparent")
        tb_left.pack(side="left", padx=16, pady=10)

        self.lbl_header_event = ctk.CTkLabel(
            tb_left,
            text="Asociación Juvenil Araucana",
            font=ctk.CTkFont(size=17, weight="bold"),
            text_color="#f8fafc",
        )
        self.lbl_header_event.pack(side="left")

        tb_right = ctk.CTkFrame(self.topbar, fg_color="transparent")
        tb_right.pack(side="right", padx=16, pady=10)

        self.btn_new_event_top = ctk.CTkButton(
            tb_right,
            text="✨ Iniciar Nueva Caja / Evento",
            height=34,
            corner_radius=8,
            fg_color="#1f2937",
            hover_color="#374151",
            text_color="#38bdf8",
            font=ctk.CTkFont(size=12, weight="bold"),
            command=self.open_new_session_dialog,
        )
        self.btn_new_event_top.pack(side="left", padx=6)

        self.lbl_kitchen_badge = ctk.CTkLabel(
            tb_right,
            text="👨‍🍳 Cocina: 0 pendientes",
            fg_color="#1e293b",
            corner_radius=8,
            padx=12,
            pady=5,
            font=ctk.CTkFont(size=12, weight="bold"),
            text_color="#fbbf24",
        )
        self.lbl_kitchen_badge.pack(side="left", padx=6)

        self.lbl_next_ticket_badge = ctk.CTkLabel(
            tb_right,
            text="PRÓXIMO TICKET: #001",
            fg_color="#2563eb",
            corner_radius=8,
            padx=14,
            pady=5,
            font=ctk.CTkFont(size=13, weight="bold"),
            text_color="#ffffff",
        )
        self.lbl_next_ticket_badge.pack(side="left", padx=(6, 0))

        # Banner de notificación rápida (cuando se cobra un ticket)
        self.toast_banner = ctk.CTkFrame(self.right_wrapper, fg_color="#065f46", corner_radius=8, height=40)
        self.lbl_toast = ctk.CTkLabel(
            self.toast_banner,
            text="",
            font=ctk.CTkFont(size=13, weight="bold"),
            text_color="#ecfdf5",
        )
        self.lbl_toast.pack(side="left", padx=14, pady=6)
        self.btn_toast_reprint = ctk.CTkButton(
            self.toast_banner,
            text="🖨️ Reimprimir último ticket",
            height=28,
            corner_radius=6,
            fg_color="#10b981",
            text_color="#042f2e",
            font=ctk.CTkFont(size=12, weight="bold"),
            command=self.reprint_last_order,
        )
        self.btn_toast_reprint.pack(side="right", padx=12, pady=6)

        # Contenedor donde se alternan las 5 pantallas
        self.content_area = ctk.CTkFrame(self.right_wrapper, fg_color="#0b1120", corner_radius=0)
        self.content_area.pack(fill="both", expand=True, padx=14, pady=12)

        self.sections = {
            "pos": ctk.CTkFrame(self.content_area, fg_color="transparent"),
            "menu": ctk.CTkFrame(self.content_area, fg_color="transparent"),
            "orders": ctk.CTkFrame(self.content_area, fg_color="transparent"),
            "closure": ctk.CTkFrame(self.content_area, fg_color="transparent"),
            "settings": ctk.CTkFrame(self.content_area, fg_color="transparent"),
        }

        self._build_section_pos(self.sections["pos"])
        self._build_section_menu(self.sections["menu"])
        self._build_section_orders(self.sections["orders"])
        self._build_section_closure(self.sections["closure"])
        self._build_section_settings(self.sections["settings"])

        self.show_section("pos")

    def toggle_sidebar(self):
        self.sidebar_expanded = not self.sidebar_expanded
        if self.sidebar_expanded:
            self.sidebar.configure(width=245)
            self.lbl_brand.pack(side="left", padx=4)
            self.sb_footer.pack(side="bottom", fill="x", padx=10, pady=14)
            for key, (btn, icon, label) in self.nav_buttons.items():
                btn.configure(text=f" {icon}   {label}", anchor="w")
        else:
            self.sidebar.configure(width=74)
            self.lbl_brand.pack_forget()
            self.sb_footer.pack_forget()
            for key, (btn, icon, label) in self.nav_buttons.items():
                btn.configure(text=icon, anchor="center")

    def show_section(self, key: str):
        self.current_section = key
        for k, frame in self.sections.items():
            frame.pack_forget()
        self.sections[key].pack(fill="both", expand=True)

        for k, (btn, icon, label) in self.nav_buttons.items():
            if k == key:
                btn.configure(fg_color="#10b981", text_color="#042f2e", hover_color="#059669")
            else:
                btn.configure(fg_color="transparent", text_color="#cbd5e1", hover_color="#1f2937")

        if key == "menu":
            self.render_menu_admin_cards()
        elif key == "orders":
            self.render_orders_list()
        elif key == "closure":
            self.render_closure_panel()
        elif key == "settings":
            self.load_settings_into_form()

    def show_notification(self, msg: str, is_error: bool = False):
        self.toast_banner.configure(fg_color="#7f1d1d" if is_error else "#065f46")
        self.lbl_toast.configure(text=msg)
        self.toast_banner.pack(fill="x", padx=14, pady=(8, 0), before=self.content_area)

    # =========================================================================
    # SECCIÓN 1: PUNTO DE VENTA (CON FOTOS EN CADA PLATO + CARRITO INTUITIVO)
    # =========================================================================
    def _build_section_pos(self, parent):
        # Izquierda: Buscador + Categorías + Grilla de Platos con Foto
        left_col = ctk.CTkFrame(parent, fg_color="transparent")
        left_col.pack(side="left", fill="both", expand=True, padx=(0, 12))

        # Barra de Búsqueda + Botón rápido para agregar o editar fotos
        filter_top = ctk.CTkFrame(left_col, fg_color="#111827", corner_radius=12)
        filter_top.pack(fill="x", pady=(0, 8))

        ent_search = ctk.CTkEntry(
            filter_top,
            textvariable=self.search_var,
            placeholder_text="🔍 Buscar plato, bebida o postre...",
            height=38,
            corner_radius=8,
            fg_color="#1e293b",
            border_color="#334155",
            font=ctk.CTkFont(size=13),
        )
        ent_search.pack(side="left", fill="x", expand=True, padx=10, pady=8)
        self.search_var.trace_add("write", lambda *_: self.render_pos_product_grid())

        ctk.CTkButton(
            filter_top,
            text="Limpiar",
            width=70,
            height=36,
            corner_radius=8,
            fg_color="#1f2937",
            hover_color="#374151",
            command=lambda: self.search_var.set(""),
        ).pack(side="left", padx=(0, 6))

        ctk.CTkButton(
            filter_top,
            text="📷 Editar Platos / Fotos",
            width=155,
            height=36,
            corner_radius=8,
            fg_color="#2563eb",
            hover_color="#1d4ed8",
            font=ctk.CTkFont(size=12, weight="bold"),
            command=lambda: self.show_section("menu"),
        ).pack(side="right", padx=(0, 10))

        # Botones grandes de Categorías
        self.cat_pills_frame = ctk.CTkFrame(left_col, fg_color="transparent")
        self.cat_pills_frame.pack(fill="x", pady=(0, 8))

        # Grilla scrollable de Platos con Foto
        self.pos_scroll_grid = ctk.CTkScrollableFrame(
            left_col,
            fg_color="#111827",
            corner_radius=12,
            border_width=1,
            border_color="#1f2937",
        )
        self.pos_scroll_grid.pack(fill="both", expand=True)

        # Derecha: Panel de Pedido Actual y Cobro
        right_col = ctk.CTkFrame(
            parent,
            width=450,
            fg_color="#111827",
            corner_radius=14,
            border_width=1,
            border_color="#1f2937",
        )
        right_col.pack(side="right", fill="y")
        right_col.pack_propagate(False)

        c_head = ctk.CTkFrame(right_col, fg_color="transparent")
        c_head.pack(fill="x", padx=14, pady=(12, 6))

        ctk.CTkLabel(
            c_head,
            text="🛒 Pedido Actual",
            font=ctk.CTkFont(size=16, weight="bold"),
            text_color="#f8fafc",
        ).pack(side="left")

        ctk.CTkButton(
            c_head,
            text="🗑️ Vaciar",
            width=80,
            height=30,
            corner_radius=8,
            fg_color="#374151",
            hover_color="#ef4444",
            font=ctk.CTkFont(size=12, weight="bold"),
            command=self.clear_cart,
        ).pack(side="right")

        # Lista scrollable de ítems del carrito
        self.cart_scroll = ctk.CTkScrollableFrame(
            right_col,
            height=220,
            fg_color="#0b1120",
            corner_radius=10,
            border_width=1,
            border_color="#1e293b",
        )
        self.cart_scroll.pack(fill="both", expand=True, padx=12, pady=4)

        # Nota para Cocina
        self.ent_notes = ctk.CTkEntry(
            right_col,
            textvariable=self.notes_var,
            placeholder_text="📝 Nota para cocina (ej. gustos de empanadas / sin sal)...",
            height=36,
            corner_radius=8,
            fg_color="#0b1120",
            border_color="#334155",
        )
        self.ent_notes.pack(fill="x", padx=12, pady=(6, 8))

        # Selector de Medio de Pago
        pay_row = ctk.CTkFrame(right_col, fg_color="transparent")
        pay_row.pack(fill="x", padx=12, pady=(0, 6))

        self.pay_btns = {}
        for code, label in [
            ("efectivo", "💵 Efectivo"),
            ("transferencia", "📱 Mercado Pago"),
            ("tarjeta", "💳 Tarjeta"),
        ]:
            b = ctk.CTkButton(
                pay_row,
                text=label,
                height=36,
                corner_radius=8,
                font=ctk.CTkFont(size=12, weight="bold"),
                command=lambda m=code: self.select_payment_method(m),
            )
            b.pack(side="left", fill="x", expand=True, padx=3)
            self.pay_btns[code] = b

        # Panel dinámico de Efectivo / Vuelto o Alias Mercado Pago
        self.pay_context = ctk.CTkFrame(right_col, fg_color="#0b1120", corner_radius=10)
        self.pay_context.pack(fill="x", padx=12, pady=(0, 8))

        # Sub-panel Efectivo
        self.cash_box = ctk.CTkFrame(self.pay_context, fg_color="transparent")
        r1 = ctk.CTkFrame(self.cash_box, fg_color="transparent")
        r1.pack(fill="x", padx=10, pady=(8, 4))

        ctk.CTkLabel(r1, text="Paga con $:", font=ctk.CTkFont(size=13, weight="bold")).pack(side="left")
        self.ent_cash = ctk.CTkEntry(
            r1,
            textvariable=self.cash_received_var,
            width=110,
            height=34,
            font=ctk.CTkFont(size=14, weight="bold"),
            justify="right",
        )
        self.ent_cash.pack(side="left", padx=8)
        self.cash_received_var.trace_add("write", lambda *_: self.update_totals_ui())

        self.lbl_change = ctk.CTkLabel(
            r1,
            text="VUELTO: $0",
            font=ctk.CTkFont(size=14, weight="bold"),
            text_color="#10b981",
        )
        self.lbl_change.pack(side="right")

        r2 = ctk.CTkFrame(self.cash_box, fg_color="transparent")
        r2.pack(fill="x", padx=8, pady=(2, 8))
        for txt, val in [
            ("Exacto", "exact"),
            ("$5.000", 5000),
            ("$10.000", 10000),
            ("$20.000", 20000),
            ("$30.000", 30000),
            ("$50.000", 50000),
        ]:
            ctk.CTkButton(
                r2,
                text=txt,
                width=58,
                height=28,
                corner_radius=6,
                fg_color="#1e293b",
                hover_color="#334155",
                font=ctk.CTkFont(size=11, weight="bold"),
                command=lambda v=val: self.set_quick_cash(v),
            ).pack(side="left", expand=True, fill="x", padx=2)

        # Sub-panel Mercado Pago
        self.mp_box = ctk.CTkFrame(self.pay_context, fg_color="transparent")
        self.lbl_mp_alias = ctk.CTkLabel(
            self.mp_box,
            text="Alias Mercado Pago:  araucana24",
            font=ctk.CTkFont(size=15, weight="bold"),
            text_color="#38bdf8",
        )
        self.lbl_mp_alias.pack(pady=12)

        # Sub-panel Tarjeta
        self.card_box = ctk.CTkFrame(self.pay_context, fg_color="transparent")
        ctk.CTkLabel(
            self.card_box,
            text="Cobro con Tarjeta Débito / Crédito",
            font=ctk.CTkFont(size=13),
            text_color="#cbd5e1",
        ).pack(pady=12)

        # Barra Total a Cobrar
        tot_frame = ctk.CTkFrame(right_col, fg_color="#064e3b", corner_radius=10)
        tot_frame.pack(fill="x", padx=12, pady=(0, 8))

        ctk.CTkLabel(
            tot_frame,
            text="TOTAL A COBRAR",
            font=ctk.CTkFont(size=13, weight="bold"),
            text_color="#a7f3d0",
        ).pack(side="left", padx=14, pady=10)

        self.lbl_total = ctk.CTkLabel(
            tot_frame,
            text="$0",
            font=ctk.CTkFont(size=24, weight="bold"),
            text_color="#ffffff",
        )
        self.lbl_total.pack(side="right", padx=14, pady=10)

        self.btn_charge = ctk.CTkButton(
            right_col,
            text="🖨️  COBRAR E IMPRIMIR TICKET (F12)",
            height=50,
            corner_radius=10,
            fg_color="#10b981",
            hover_color="#059669",
            text_color="#042f2e",
            font=ctk.CTkFont(size=15, weight="bold"),
            command=self.submit_order,
        )
        self.btn_charge.pack(fill="x", padx=12, pady=(0, 12))

        self.select_payment_method("efectivo")

    def render_category_pills(self):
        for w in self.cat_pills_frame.winfo_children():
            w.destroy()

        cats = ["Todos", "Almuerzo", "Principales", "Bebidas", "Para Llevar", "Postres"]
        labels = {
            "Todos": "⭐ Todos",
            "Almuerzo": "🍽️ Almuerzo",
            "Principales": "🍕 Principales",
            "Bebidas": "🥤 Bebidas",
            "Para Llevar": "🥡 Para Llevar",
            "Postres": "🍰 Postres",
        }
        for c in cats:
            active = self.selected_category == c
            ctk.CTkButton(
                self.cat_pills_frame,
                text=labels.get(c, c),
                height=36,
                corner_radius=8,
                fg_color="#2563eb" if active else "#1e293b",
                hover_color="#1d4ed8" if active else "#334155",
                text_color="#ffffff" if active else "#cbd5e1",
                font=ctk.CTkFont(size=12, weight="bold"),
                command=lambda cat=c: self._on_select_category(cat),
            ).pack(side="left", fill="x", expand=True, padx=3)

    def _on_select_category(self, cat: str):
        self.selected_category = cat
        self.render_category_pills()
        self.render_pos_product_grid()

    def render_pos_product_grid(self):
        for w in self.pos_scroll_grid.winfo_children():
            w.destroy()

        q = self.search_var.get().strip().lower()
        filtered = []
        for p in self.products:
            if not p["active"]:
                continue
            if self.selected_category != "Todos" and p["category"] != self.selected_category:
                continue
            if q and q not in p["name"].lower() and q not in p["category"].lower():
                continue
            filtered.append(p)

        cols = 3
        for c in range(cols):
            self.pos_scroll_grid.columnconfigure(c, weight=1, uniform="pos_card")

        for idx, p in enumerate(filtered):
            r = idx // cols
            c = idx % cols
            out_of_stock = bool(p["stock_enabled"]) and int(p["stock_qty"]) <= 0
            accent = CATEGORY_COLORS.get(p["category"], "#10b981")

            card = ctk.CTkFrame(
                self.pos_scroll_grid,
                fg_color="#1e293b",
                corner_radius=12,
                border_width=2,
                border_color="#334155" if not out_of_stock else "#7f1d1d",
            )
            card.grid(row=r, column=c, sticky="nsew", padx=6, pady=6)

            # Foto del plato
            cache_key = (p["id"], p.get("image_path", ""))
            if cache_key not in self.image_cache:
                self.image_cache[cache_key] = load_ctk_product_image(
                    p.get("image_path", ""), p["name"], p["category"], size=(210, 110)
                )
            ctk_img = self.image_cache[cache_key]

            img_btn = ctk.CTkButton(
                card,
                text="",
                image=ctk_img,
                fg_color="transparent",
                hover_color="#334155",
                corner_radius=8,
                command=lambda prod=p: self.add_to_cart(prod),
            )
            img_btn.pack(fill="x", padx=6, pady=(6, 4))

            # Nombre del plato
            lbl_name = ctk.CTkLabel(
                card,
                text=p["name"],
                font=ctk.CTkFont(size=13, weight="bold"),
                text_color="#f8fafc" if not out_of_stock else "#94a3b8",
                wraplength=200,
                justify="left",
            )
            lbl_name.pack(anchor="w", padx=10)

            # Stock / Categoría
            stock_txt = (
                f"AGOTADO (0 u.)"
                if out_of_stock
                else (f"Stock: {p['stock_qty']} u." if p["stock_enabled"] else p["category"])
            )
            ctk.CTkLabel(
                card,
                text=stock_txt,
                font=ctk.CTkFont(size=11),
                text_color="#ef4444" if out_of_stock else accent,
            ).pack(anchor="w", padx=10, pady=(0, 4))

            # Pie de tarjeta: Precio + Botón Agregar + Botón Cambiar Foto rápido
            foot = ctk.CTkFrame(card, fg_color="transparent")
            foot.pack(fill="x", padx=8, pady=(2, 8))

            ctk.CTkLabel(
                foot,
                text=fmt_money(p["price"]),
                font=ctk.CTkFont(size=16, weight="bold"),
                text_color="#10b981",
            ).pack(side="left")

            ctk.CTkButton(
                foot,
                text="📷",
                width=30,
                height=30,
                corner_radius=6,
                fg_color="#334155",
                hover_color="#475569",
                command=lambda prod=p: self.pick_photo_for_product(prod),
            ).pack(side="right", padx=(4, 0))

            ctk.CTkButton(
                foot,
                text="+ Agregar" if not out_of_stock else "Sin Stock",
                width=85,
                height=30,
                corner_radius=6,
                state="normal" if not out_of_stock else "disabled",
                fg_color="#10b981" if not out_of_stock else "#475569",
                hover_color="#059669",
                text_color="#042f2e" if not out_of_stock else "#cbd5e1",
                font=ctk.CTkFont(size=12, weight="bold"),
                command=lambda prod=p: self.add_to_cart(prod),
            ).pack(side="right")

    # =========================================================================
    # CARRITO Y COBRO
    # =========================================================================
    def add_to_cart(self, prod: dict):
        if not self.active_session:
            if messagebox.askyesno(
                "Caja Cerrada",
                "La caja actual está cerrada.\n¿Querés abrir una Nueva Caja ahora para empezar a cobrar?",
            ):
                self.open_new_session_dialog()
            return

        if prod["stock_enabled"] and int(prod["stock_qty"]) <= 0:
            messagebox.showwarning("Sin stock", f"'{prod['name']}' está agotado.")
            return

        for item in self.cart:
            if item["product_id"] == prod["id"]:
                if prod["stock_enabled"] and item["quantity"] + 1 > int(prod["stock_qty"]):
                    messagebox.showwarning(
                        "Límite de stock", f"Solo quedan {prod['stock_qty']} unidades de '{prod['name']}'."
                    )
                    return
                item["quantity"] += 1
                self.render_cart()
                return

        self.cart.append(
            {
                "product_id": prod["id"],
                "name": prod["name"],
                "category": prod["category"],
                "price": float(prod["price"]),
                "quantity": 1,
                "stock_enabled": bool(prod["stock_enabled"]),
                "stock_qty": int(prod["stock_qty"]),
            }
        )
        self.render_cart()

    def change_cart_qty(self, product_id: int, delta: int):
        for idx, item in enumerate(self.cart):
            if item["product_id"] == product_id:
                new_q = item["quantity"] + delta
                if new_q <= 0:
                    self.cart.pop(idx)
                elif item["stock_enabled"] and new_q > item["stock_qty"]:
                    messagebox.showwarning("Límite de stock", f"Solo hay {item['stock_qty']} u. disponibles.")
                    return
                else:
                    item["quantity"] = new_q
                break
        self.render_cart()

    def clear_cart(self):
        self.cart = []
        self.render_cart()

    def get_cart_total(self) -> float:
        return sum(i["price"] * i["quantity"] for i in self.cart)

    def render_cart(self):
        for w in self.cart_scroll.winfo_children():
            w.destroy()

        if not self.cart:
            ctk.CTkLabel(
                self.cart_scroll,
                text="Tocá cualquier plato con foto de la izquierda\npara agregarlo al pedido.",
                font=ctk.CTkFont(size=12),
                text_color="#64748b",
            ).pack(pady=55)
        else:
            for item in self.cart:
                row = ctk.CTkFrame(self.cart_scroll, fg_color="#1e293b", corner_radius=8)
                row.pack(fill="x", pady=3, padx=2)

                ctk.CTkButton(
                    row,
                    text="−",
                    width=28,
                    height=28,
                    corner_radius=6,
                    fg_color="#334155",
                    hover_color="#ef4444",
                    font=ctk.CTkFont(size=14, weight="bold"),
                    command=lambda pid=item["product_id"]: self.change_cart_qty(pid, -1),
                ).pack(side="left", padx=(6, 2), pady=6)

                ctk.CTkLabel(
                    row,
                    text=str(item["quantity"]),
                    width=26,
                    font=ctk.CTkFont(size=13, weight="bold"),
                    text_color="#10b981",
                ).pack(side="left")

                ctk.CTkButton(
                    row,
                    text="+",
                    width=28,
                    height=28,
                    corner_radius=6,
                    fg_color="#334155",
                    hover_color="#10b981",
                    font=ctk.CTkFont(size=14, weight="bold"),
                    command=lambda pid=item["product_id"]: self.change_cart_qty(pid, 1),
                ).pack(side="left", padx=(2, 8), pady=6)

                ctk.CTkLabel(
                    row,
                    text=item["name"],
                    font=ctk.CTkFont(size=12, weight="bold"),
                    text_color="#f8fafc",
                    anchor="w",
                ).pack(side="left", fill="x", expand=True)

                sub = item["price"] * item["quantity"]
                ctk.CTkLabel(
                    row,
                    text=fmt_money(sub),
                    font=ctk.CTkFont(size=13, weight="bold"),
                    text_color="#10b981",
                ).pack(side="right", padx=10)

        self.update_totals_ui()

    def select_payment_method(self, method: str):
        self.payment_method = method
        for code, btn in self.pay_btns.items():
            if code == method:
                btn.configure(fg_color="#10b981", text_color="#042f2e", hover_color="#059669")
            else:
                btn.configure(fg_color="#1e293b", text_color="#cbd5e1", hover_color="#334155")

        self.cash_box.pack_forget()
        self.mp_box.pack_forget()
        self.card_box.pack_forget()

        if method == "efectivo":
            self.cash_box.pack(fill="x")
        elif method == "transferencia":
            self.lbl_mp_alias.configure(text=f"Alias Mercado Pago:  {self.settings.get('mp_alias', 'araucana24')}")
            self.mp_box.pack(fill="x")
        else:
            self.card_box.pack(fill="x")

        self.update_totals_ui()

    def set_quick_cash(self, val):
        tot = self.get_cart_total()
        self.cash_received_var.set(str(int(tot)) if val == "exact" else str(val))

    def update_totals_ui(self):
        tot = self.get_cart_total()
        self.lbl_total.configure(text=fmt_money(tot))

        try:
            paid = float(self.cash_received_var.get() or 0)
        except ValueError:
            paid = 0

        if paid <= 0 or tot <= 0:
            self.lbl_change.configure(text="VUELTO: $0", text_color="#10b981")
        elif paid < tot:
            self.lbl_change.configure(text=f"FALTAN {fmt_money(tot - paid)}", text_color="#ef4444")
        else:
            self.lbl_change.configure(text=f"VUELTO: {fmt_money(paid - tot)}", text_color="#10b981")

    def submit_order(self):
        if not self.active_session:
            messagebox.showwarning("Caja Cerrada", "Debés abrir una caja antes de registrar ventas.")
            self.open_new_session_dialog()
            return

        if not self.cart:
            messagebox.showwarning("Pedido vacío", "Agregá al menos un plato o bebida al pedido.")
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
        event_name = self.active_session.get("event_name", "Asociación Juvenil Araucana")
        now_str = datetime.now().strftime("%d/%m/%Y %H:%M:%S")

        cur.execute(
            """
            INSERT INTO orders (
                session_id, ticket_number, event_name, total, payment_method,
                amount_received, change_amount, notes, status,
                kitchen_status, kitchen_printed, created_at
            )
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, 'completado', 'pendiente', 0, ?)
        """,
            (
                self.active_session["id"],
                ticket_num,
                event_name,
                total,
                self.payment_method,
                amount_received,
                change_amount,
                notes,
                now_str,
            ),
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
            if item["stock_enabled"]:
                cur.execute(
                    "UPDATE products SET stock_qty = MAX(0, stock_qty - ?) WHERE id = ?",
                    (item["quantity"], item["product_id"]),
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

        self.last_order = order_row
        self.cart = []
        self.cash_received_var.set("")
        self.notes_var.set("")
        self.refresh_all()

        try:
            printer_used = print_ticket_direct(order_row, self.settings, only_kitchen=False)
            self.show_notification(
                f"✅ Ticket #{order_row['ticket_number']:03d} cobrado ({fmt_money(total)}) — Impreso en {printer_used} y enviado a Cocina"
            )
        except Exception as e:
            self.show_notification(
                f"⚠️ Ticket #{order_row['ticket_number']:03d} guardado y enviado a Cocina (Impresora: {e})",
                is_error=True,
            )

    def reprint_last_order(self):
        if not self.last_order:
            return
        try:
            p = print_ticket_direct(self.last_order, self.settings, only_kitchen=False)
            self.show_notification(f"🖨️ Ticket #{self.last_order['ticket_number']:03d} reimpreso en {p}")
        except Exception as e:
            messagebox.showerror("Error de impresión", str(e))

    # =========================================================================
    # SECCIÓN 2: GESTIÓN DE PLATOS, FOTOS Y STOCK
    # =========================================================================
    def _build_section_menu(self, parent):
        head = ctk.CTkFrame(parent, fg_color="#111827", corner_radius=12)
        head.pack(fill="x", pady=(0, 10))

        ctk.CTkLabel(
            head,
            text="🍽️ Gestión de Platos, Fotos, Precios y Stock",
            font=ctk.CTkFont(size=18, weight="bold"),
        ).pack(side="left", padx=16, pady=12)

        ctk.CTkButton(
            head,
            text="+ Agregar Nuevo Plato / Bebida",
            height=38,
            corner_radius=8,
            fg_color="#10b981",
            hover_color="#059669",
            text_color="#042f2e",
            font=ctk.CTkFont(size=13, weight="bold"),
            command=lambda: self.open_product_editor_modal(None),
        ).pack(side="right", padx=12, pady=10)

        ctk.CTkButton(
            head,
            text="🔄 Restaurar Menú Original del PDF",
            height=38,
            corner_radius=8,
            fg_color="#1f2937",
            hover_color="#374151",
            font=ctk.CTkFont(size=12, weight="bold"),
            command=self.restore_original_menu,
        ).pack(side="right", padx=4, pady=10)

        self.menu_admin_scroll = ctk.CTkScrollableFrame(
            parent,
            fg_color="#111827",
            corner_radius=12,
            border_width=1,
            border_color="#1f2937",
        )
        self.menu_admin_scroll.pack(fill="both", expand=True)

    def render_menu_admin_cards(self):
        for w in self.menu_admin_scroll.winfo_children():
            w.destroy()

        conn = get_connection()
        all_prods = [dict(r) for r in conn.execute("SELECT * FROM products ORDER BY id ASC").fetchall()]
        conn.close()

        for p in all_prods:
            row = ctk.CTkFrame(self.menu_admin_scroll, fg_color="#1e293b", corner_radius=10)
            row.pack(fill="x", padx=10, pady=5)

            cache_key = (p["id"], p.get("image_path", ""))
            if cache_key not in self.image_cache:
                self.image_cache[cache_key] = load_ctk_product_image(
                    p.get("image_path", ""), p["name"], p["category"], size=(120, 68)
                )

            ctk.CTkLabel(row, text="", image=self.image_cache[cache_key]).pack(side="left", padx=10, pady=8)

            info = ctk.CTkFrame(row, fg_color="transparent")
            info.pack(side="left", fill="x", expand=True, padx=6)

            status_badge = "🟢 Activo" if p["active"] else "🔴 Pausado / Oculto"
            ctk.CTkLabel(
                info,
                text=f"{p['name']}   ({p['category']})   —   {status_badge}",
                font=ctk.CTkFont(size=14, weight="bold"),
                text_color="#f8fafc" if p["active"] else "#94a3b8",
            ).pack(anchor="w")

            stock_info = f"Control de Stock: {p['stock_qty']} u. disponibles" if p["stock_enabled"] else "Stock libre"
            ctk.CTkLabel(
                info,
                text=f"Precio: {fmt_money(p['price'])}   |   {stock_info}   |   {p.get('description', '')}",
                font=ctk.CTkFont(size=12),
                text_color="#10b981",
            ).pack(anchor="w", pady=(2, 0))

            actions = ctk.CTkFrame(row, fg_color="transparent")
            actions.pack(side="right", padx=10)

            ctk.CTkButton(
                actions,
                text="📷 Cambiar Foto",
                width=120,
                height=34,
                corner_radius=8,
                fg_color="#2563eb",
                hover_color="#1d4ed8",
                font=ctk.CTkFont(size=12, weight="bold"),
                command=lambda prod=p: self.pick_photo_for_product(prod),
            ).pack(side="left", padx=4)

            ctk.CTkButton(
                actions,
                text="✏️ Editar Plato",
                width=110,
                height=34,
                corner_radius=8,
                fg_color="#334155",
                hover_color="#475569",
                font=ctk.CTkFont(size=12, weight="bold"),
                command=lambda prod=p: self.open_product_editor_modal(prod),
            ).pack(side="left", padx=4)

            ctk.CTkButton(
                actions,
                text="🗑️",
                width=38,
                height=34,
                corner_radius=8,
                fg_color="#7f1d1d",
                hover_color="#ef4444",
                command=lambda prod=p: self.delete_product(prod),
            ).pack(side="left", padx=4)

    def pick_photo_for_product(self, prod: dict):
        file_path = filedialog.askopenfilename(
            title=f"Elegir foto para {prod['name']}",
            filetypes=[("Imágenes", "*.png *.jpg *.jpeg *.webp *.bmp")],
        )
        if not file_path:
            return
        try:
            new_filename = import_custom_product_image(file_path, prod["name"])
            conn = get_connection()
            conn.execute("UPDATE products SET image_path = ? WHERE id = ?", (new_filename, prod["id"]))
            conn.commit()
            conn.close()
            self.image_cache.clear()
            self.refresh_all()
            if self.current_section == "menu":
                self.render_menu_admin_cards()
            self.show_notification(f"📷 Foto actualizada para '{prod['name']}'")
        except Exception as e:
            messagebox.showerror("Error al cargar imagen", str(e))

    def open_product_editor_modal(self, prod: dict = None):
        win = ctk.CTkToplevel(self)
        win.title("Editar Plato" if prod else "Nuevo Plato / Bebida")
        win.geometry("480x520")
        win.grab_set()

        ctk.CTkLabel(
            win,
            text="Editar Plato o Bebida" if prod else "Crear Nuevo Plato o Bebida",
            font=ctk.CTkFont(size=18, weight="bold"),
        ).pack(pady=(16, 10))

        form = ctk.CTkFrame(win, fg_color="transparent")
        form.pack(fill="both", expand=True, padx=24)

        ctk.CTkLabel(form, text="Nombre del Plato:").pack(anchor="w", pady=(6, 2))
        e_name = ctk.CTkEntry(form, height=36)
        e_name.pack(fill="x")
        if prod:
            e_name.insert(0, prod["name"])

        ctk.CTkLabel(form, text="Categoría:").pack(anchor="w", pady=(10, 2))
        e_cat = ctk.CTkComboBox(
            form, values=["Almuerzo", "Principales", "Bebidas", "Para Llevar", "Postres"], height=36
        )
        e_cat.pack(fill="x")
        e_cat.set(prod["category"] if prod else "Principales")

        ctk.CTkLabel(form, text="Precio ($):").pack(anchor="w", pady=(10, 2))
        e_price = ctk.CTkEntry(form, height=36)
        e_price.pack(fill="x")
        if prod:
            e_price.insert(0, str(int(prod["price"])))

        ctk.CTkLabel(form, text="Descripción corta (opcional):").pack(anchor="w", pady=(10, 2))
        e_desc = ctk.CTkEntry(form, height=36)
        e_desc.pack(fill="x")
        if prod and prod.get("description"):
            e_desc.insert(0, prod["description"])

        stock_var = tk.BooleanVar(value=bool(prod["stock_enabled"]) if prod else False)
        ctk.CTkCheckBox(form, text="Controlar límite de unidades disponibles (Stock)", variable=stock_var).pack(
            anchor="w", pady=(14, 6)
        )

        ctk.CTkLabel(form, text="Cantidad de unidades disponibles:").pack(anchor="w", pady=(2, 2))
        e_stock = ctk.CTkEntry(form, height=36)
        e_stock.pack(fill="x")
        e_stock.insert(0, str(prod["stock_qty"] if prod else 50))

        active_var = tk.BooleanVar(value=bool(prod["active"]) if prod else True)
        ctk.CTkCheckBox(form, text="Plato visible y disponible para la venta", variable=active_var).pack(
            anchor="w", pady=(12, 6)
        )

        def save():
            name = e_name.get().strip()
            cat = e_cat.get().strip() or "Principales"
            try:
                price = float(e_price.get() or 0)
                sq = int(e_stock.get() or 0)
            except ValueError:
                messagebox.showerror("Dato inválido", "Revisá el precio y el stock.")
                return
            if not name:
                return

            conn = get_connection()
            if prod:
                conn.execute(
                    """
                    UPDATE products
                    SET name=?, category=?, price=?, description=?, stock_enabled=?, stock_qty=?, active=?
                    WHERE id=?
                """,
                    (
                        name,
                        cat,
                        price,
                        e_desc.get().strip(),
                        1 if stock_var.get() else 0,
                        sq,
                        1 if active_var.get() else 0,
                        prod["id"],
                    ),
                )
            else:
                conn.execute(
                    """
                    INSERT INTO products (name, category, price, description, stock_enabled, stock_qty, active)
                    VALUES (?, ?, ?, ?, ?, ?, ?)
                """,
                    (
                        name,
                        cat,
                        price,
                        e_desc.get().strip(),
                        1 if stock_var.get() else 0,
                        sq,
                        1 if active_var.get() else 0,
                    ),
                )
            conn.commit()
            conn.close()
            self.image_cache.clear()
            self.refresh_all()
            self.render_menu_admin_cards()
            win.destroy()

        ctk.CTkButton(
            win,
            text="💾 Guardar Plato",
            height=42,
            corner_radius=10,
            fg_color="#10b981",
            text_color="#042f2e",
            font=ctk.CTkFont(size=14, weight="bold"),
            command=save,
        ).pack(fill="x", padx=24, pady=16)

    def delete_product(self, prod: dict):
        if not messagebox.askyesno("Eliminar Plato", f"¿Seguro que querés eliminar '{prod['name']}'?"):
            return
        conn = get_connection()
        conn.execute("DELETE FROM products WHERE id = ?", (prod["id"],))
        conn.commit()
        conn.close()
        self.refresh_all()
        self.render_menu_admin_cards()

    def restore_original_menu(self):
        if not messagebox.askyesno(
            "Restaurar Menú", "¿Querés recargar los 22 platos originales del PDF de Asociación Juvenil Araucana?"
        ):
            return
        reset_menu_to_araucana()
        self.image_cache.clear()
        self.refresh_all()
        self.render_menu_admin_cards()

    # =========================================================================
    # SECCIÓN 3: TICKETS EMITIDOS Y ESTADO EN COCINA
    # =========================================================================
    def _build_section_orders(self, parent):
        head = ctk.CTkFrame(parent, fg_color="#111827", corner_radius=12)
        head.pack(fill="x", pady=(0, 10))

        ctk.CTkLabel(
            head,
            text="🧾 Tickets Emitidos en la Caja Actual y Estado en Cocina",
            font=ctk.CTkFont(size=18, weight="bold"),
        ).pack(side="left", padx=16, pady=12)

        self.orders_scroll = ctk.CTkScrollableFrame(
            parent,
            fg_color="#111827",
            corner_radius=12,
            border_width=1,
            border_color="#1f2937",
        )
        self.orders_scroll.pack(fill="both", expand=True)

    def render_orders_list(self):
        for w in self.orders_scroll.winfo_children():
            w.destroy()

        conn = get_connection()
        sess_id = self.active_session["id"] if self.active_session else -1
        orders = [
            dict(r)
            for r in conn.execute("SELECT * FROM orders WHERE session_id = ? ORDER BY id DESC", (sess_id,)).fetchall()
        ]
        for o in orders:
            o["items"] = [
                dict(i) for i in conn.execute("SELECT * FROM order_items WHERE order_id = ?", (o["id"],)).fetchall()
            ]
        conn.close()

        if not orders:
            ctk.CTkLabel(
                self.orders_scroll,
                text="Todavía no hay tickets emitidos en esta sesión de caja.",
                font=ctk.CTkFont(size=14),
                text_color="#64748b",
            ).pack(pady=50)
            return

        for o in orders:
            card = ctk.CTkFrame(self.orders_scroll, fg_color="#1e293b", corner_radius=10)
            card.pack(fill="x", padx=10, pady=5)

            items_txt = ", ".join(f"{i['quantity']}x {i['product_name']}" for i in o["items"])
            is_canc = o["status"] == "anulado"

            if is_canc:
                k_badge = "❌ ANULADO"
                k_col = "#ef4444"
            elif o["kitchen_status"] == "finalizado":
                k_badge = f"✅ ENTREGADO ({o['finished_at'] or 'Listo'})"
                k_col = "#10b981"
            elif o["kitchen_status"] == "preparando":
                k_badge = "👨‍🍳 EN PREPARACIÓN"
                k_col = "#38bdf8"
            else:
                k_badge = "🔔 EN COCINA (Pendiente)"
                k_col = "#fbbf24"

            pay_str = "MERCADO PAGO" if o["payment_method"] == "transferencia" else o["payment_method"].upper()

            left = ctk.CTkFrame(card, fg_color="transparent")
            left.pack(side="left", fill="x", expand=True, padx=14, pady=10)

            ctk.CTkLabel(
                left,
                text=f"TICKET #{o['ticket_number']:03d}   ·   {o['created_at']}   ·   {pay_str}   ·   {fmt_money(o['total'])}",
                font=ctk.CTkFont(size=14, weight="bold"),
                text_color="#f8fafc" if not is_canc else "#64748b",
            ).pack(anchor="w")

            ctk.CTkLabel(
                left,
                text=f"Platos: {items_txt}" + (f"   |   Nota: {o['notes']}" if o.get("notes") else ""),
                font=ctk.CTkFont(size=12),
                text_color="#cbd5e1",
            ).pack(anchor="w", pady=(2, 0))

            right = ctk.CTkFrame(card, fg_color="transparent")
            right.pack(side="right", padx=12)

            ctk.CTkLabel(
                right,
                text=k_badge,
                font=ctk.CTkFont(size=12, weight="bold"),
                text_color=k_col,
            ).pack(side="left", padx=10)

            ctk.CTkButton(
                right,
                text="🖨️ Reimprimir",
                width=110,
                height=32,
                corner_radius=8,
                fg_color="#10b981",
                text_color="#042f2e",
                font=ctk.CTkFont(size=12, weight="bold"),
                command=lambda ord_obj=o: self.reprint_specific_order(ord_obj),
            ).pack(side="left", padx=4)

            if not is_canc:
                ctk.CTkButton(
                    right,
                    text="❌ Anular",
                    width=85,
                    height=32,
                    corner_radius=8,
                    fg_color="#7f1d1d",
                    hover_color="#ef4444",
                    font=ctk.CTkFont(size=12, weight="bold"),
                    command=lambda oid=o["id"]: self.cancel_order_by_id(oid),
                ).pack(side="left", padx=4)

    def reprint_specific_order(self, order: dict):
        try:
            p = print_ticket_direct(order, self.settings, only_kitchen=False)
            self.show_notification(f"🖨️ Ticket #{order['ticket_number']:03d} reimpreso en {p}")
        except Exception as e:
            messagebox.showerror("Error al imprimir", str(e))

    def cancel_order_by_id(self, order_id: int):
        if not messagebox.askyesno("Anular Ticket", "¿Anular este ticket y devolver el stock de sus productos?"):
            return
        conn = get_connection()
        items = conn.execute("SELECT * FROM order_items WHERE order_id = ?", (order_id,)).fetchall()
        for it in items:
            if it["product_id"]:
                conn.execute(
                    "UPDATE products SET stock_qty = stock_qty + ? WHERE id = ? AND stock_enabled = 1",
                    (it["quantity"], it["product_id"]),
                )
        conn.execute("UPDATE orders SET status = 'anulado' WHERE id = ?", (order_id,))
        conn.commit()
        conn.close()
        self.refresh_all()
        self.render_orders_list()

    # =========================================================================
    # SECCIÓN 4: CIERRE DE CAJA, ARQUEO E INICIAR NUEVA CAJA / EVENTO
    # =========================================================================
    def _build_section_closure(self, parent):
        self.closure_container = ctk.CTkScrollableFrame(
            parent,
            fg_color="#111827",
            corner_radius=12,
            border_width=1,
            border_color="#1f2937",
        )
        self.closure_container.pack(fill="both", expand=True)

    def render_closure_panel(self):
        for w in self.closure_container.winfo_children():
            w.destroy()

        self.active_session = get_active_cash_session()

        # Barra superior de acciones de Caja
        top_bar = ctk.CTkFrame(self.closure_container, fg_color="#1e293b", corner_radius=12)
        top_bar.pack(fill="x", padx=14, pady=12)

        if self.active_session:
            metrics = get_session_live_metrics(self.active_session["id"])
            ctk.CTkLabel(
                top_bar,
                text=f"🟢 CAJA ABIERTA #{metrics['id']:03d} — {metrics['event_name']} (Iniciada: {metrics['opened_at']})",
                font=ctk.CTkFont(size=16, weight="bold"),
                text_color="#10b981",
            ).pack(side="left", padx=16, pady=14)
        else:
            metrics = None
            ctk.CTkLabel(
                top_bar,
                text="🔴 LA CAJA ACTUAL ESTÁ CERRADA",
                font=ctk.CTkFont(size=16, weight="bold"),
                text_color="#ef4444",
            ).pack(side="left", padx=16, pady=14)

        ctk.CTkButton(
            top_bar,
            text="🔓 Abrir Nueva Caja / Nuevo Evento (Reiniciar a #001)",
            height=40,
            corner_radius=10,
            fg_color="#2563eb",
            hover_color="#1d4ed8",
            font=ctk.CTkFont(size=13, weight="bold"),
            command=self.open_new_session_dialog,
        ).pack(side="right", padx=14, pady=10)

        if metrics:
            # Tarjetas de Resumen Financiero
            cards_row = ctk.CTkFrame(self.closure_container, fg_color="transparent")
            cards_row.pack(fill="x", padx=14, pady=6)

            for title, val, col in [
                ("TOTAL RECAUDADO", fmt_money(metrics["total_sales"]), "#10b981"),
                ("PEDIDOS COBRADOS", str(metrics["orders_count"]), "#38bdf8"),
                ("VENTAS EN EFECTIVO", fmt_money(metrics["cash_sales"]), "#fbbf24"),
                ("MERCADO PAGO", fmt_money(metrics["mp_sales"]), "#60a5fa"),
                ("TARJETA", fmt_money(metrics["card_sales"]), "#a78bfa"),
            ]:
                c = ctk.CTkFrame(cards_row, fg_color="#1e293b", corner_radius=10)
                c.pack(side="left", fill="x", expand=True, padx=4)
                ctk.CTkLabel(c, text=title, font=ctk.CTkFont(size=11, weight="bold"), text_color="#94a3b8").pack(
                    anchor="w", padx=12, pady=(10, 2)
                )
                ctk.CTkLabel(c, text=val, font=ctk.CTkFont(size=18, weight="bold"), text_color=col).pack(
                    anchor="w", padx=12, pady=(0, 10)
                )

            # Panel de Arqueo y Cierre de Caja
            mid = ctk.CTkFrame(self.closure_container, fg_color="transparent")
            mid.pack(fill="x", padx=14, pady=10)

            arqueo_card = ctk.CTkFrame(mid, fg_color="#1e293b", corner_radius=12)
            arqueo_card.pack(side="left", fill="both", expand=True, padx=(0, 8))

            ctk.CTkLabel(
                arqueo_card,
                text="🔐 Arqueo de Efectivo y Cierre de Caja",
                font=ctk.CTkFont(size=16, weight="bold"),
            ).pack(anchor="w", padx=16, pady=(14, 8))

            info_txt = (
                f"• Fondo Inicial de Cambio en Caja:   {fmt_money(metrics['opening_cash'])}\n"
                f"• (+) Ventas Cobradas en Efectivo:   {fmt_money(metrics['cash_sales'])}\n"
                f"• (=) EFECTIVO ESPERADO EN CAJÓN:    {fmt_money(metrics['expected_cash'])}"
            )
            ctk.CTkLabel(
                arqueo_card,
                text=info_txt,
                font=ctk.CTkFont(size=14, weight="bold"),
                text_color="#f8fafc",
                justify="left",
            ).pack(anchor="w", padx=16, pady=6)

            cnt_row = ctk.CTkFrame(arqueo_card, fg_color="transparent")
            cnt_row.pack(fill="x", padx=16, pady=10)

            ctk.CTkLabel(
                cnt_row,
                text="Efectivo Real Contado en Cajón ($):",
                font=ctk.CTkFont(size=13, weight="bold"),
            ).pack(side="left")

            counted_var = tk.StringVar(value=str(int(metrics["expected_cash"])))
            ent_counted = ctk.CTkEntry(cnt_row, textvariable=counted_var, width=140, height=36)
            ent_counted.pack(side="left", padx=10)

            lbl_diff = ctk.CTkLabel(
                cnt_row,
                text="Caja Exacta ($0)",
                font=ctk.CTkFont(size=13, weight="bold"),
                text_color="#10b981",
            )
            lbl_diff.pack(side="left", padx=8)

            def update_diff(*_):
                try:
                    c_val = float(counted_var.get() or 0)
                except ValueError:
                    c_val = 0
                d = c_val - float(metrics["expected_cash"])
                if abs(d) < 1:
                    lbl_diff.configure(text="✓ Caja Exacta ($0)", text_color="#10b981")
                elif d > 0:
                    lbl_diff.configure(text=f"Sobrante: +{fmt_money(d)}", text_color="#38bdf8")
                else:
                    lbl_diff.configure(text=f"Faltante: {fmt_money(d)}", text_color="#ef4444")

            counted_var.trace_add("write", update_diff)

            btn_row = ctk.CTkFrame(arqueo_card, fg_color="transparent")
            btn_row.pack(fill="x", padx=16, pady=(8, 16))

            def do_print_report():
                try:
                    c_val = float(counted_var.get() or 0)
                except ValueError:
                    c_val = float(metrics["expected_cash"])
                m_copy = dict(metrics)
                m_copy["counted_cash"] = c_val
                try:
                    p = print_cash_closure_direct(m_copy, self.settings)
                    self.show_notification(f"🖨️ Reporte de Arqueo / Cierre impreso en {p}")
                except Exception as e:
                    messagebox.showerror("Error al imprimir reporte", str(e))

            def do_close_register():
                if not messagebox.askyesno(
                    "Confirmar Cierre de Caja",
                    "¿Querés CERRAR formalmente esta caja y guardar e imprimir el Ticket Z de Cierre?",
                ):
                    return
                try:
                    c_val = float(counted_var.get() or 0)
                except ValueError:
                    c_val = float(metrics["expected_cash"])
                closed_data = close_cash_session(metrics["id"], c_val)
                try:
                    print_cash_closure_direct(closed_data, self.settings)
                except Exception:
                    pass
                self.refresh_all()
                self.render_closure_panel()
                self.show_notification(f"🔒 Caja #{metrics['id']:03d} cerrada correctamente.")

            ctk.CTkButton(
                btn_row,
                text="🖨️ Imprimir Ticket de Arqueo / Cierre",
                height=42,
                corner_radius=10,
                fg_color="#334155",
                hover_color="#475569",
                font=ctk.CTkFont(size=13, weight="bold"),
                command=do_print_report,
            ).pack(side="left", fill="x", expand=True, padx=(0, 6))

            ctk.CTkButton(
                btn_row,
                text="🔒 CERRAR CAJA DEFINITIVAMENTE",
                height=42,
                corner_radius=10,
                fg_color="#ef4444",
                hover_color="#dc2626",
                font=ctk.CTkFont(size=13, weight="bold"),
                command=do_close_register,
            ).pack(side="left", fill="x", expand=True, padx=(6, 0))

            # Ranking de Platos Vendidos a la derecha
            rank_card = ctk.CTkFrame(mid, fg_color="#1e293b", corner_radius=12, width=420)
            rank_card.pack(side="right", fill="both", expand=True)

            ctk.CTkLabel(
                rank_card,
                text="📊 Cantidades Vendidas por Plato",
                font=ctk.CTkFont(size=16, weight="bold"),
            ).pack(anchor="w", padx=16, pady=(14, 8))

            if not metrics["ranking"]:
                ctk.CTkLabel(rank_card, text="Sin ventas registradas aún.", text_color="#64748b").pack(
                    padx=16, pady=20
                )
            else:
                for r in metrics["ranking"]:
                    r_line = ctk.CTkFrame(rank_card, fg_color="#0b1120", corner_radius=6)
                    r_line.pack(fill="x", padx=14, pady=3)
                    ctk.CTkLabel(
                        r_line,
                        text=f"{r['total_qty']}x  {r['product_name']}",
                        font=ctk.CTkFont(size=13, weight="bold"),
                    ).pack(side="left", padx=10, pady=6)
                    ctk.CTkLabel(
                        r_line,
                        text=fmt_money(r["total_amount"]),
                        font=ctk.CTkFont(size=13, weight="bold"),
                        text_color="#10b981",
                    ).pack(side="right", padx=10, pady=6)

        # Historial de Cierres de Caja Anteriores
        conn = get_connection()
        past_sessions = [
            dict(r)
            for r in conn.execute(
                "SELECT * FROM cash_sessions WHERE status = 'cerrada' ORDER BY id DESC LIMIT 15"
            ).fetchall()
        ]
        conn.close()

        if past_sessions:
            ctk.CTkLabel(
                self.closure_container,
                text="📁 Historial de Cajas Cerradas Anteriormente",
                font=ctk.CTkFont(size=15, weight="bold"),
            ).pack(anchor="w", padx=16, pady=(16, 6))

            for ps in past_sessions:
                p_row = ctk.CTkFrame(self.closure_container, fg_color="#1e293b", corner_radius=8)
                p_row.pack(fill="x", padx=14, pady=4)
                ctk.CTkLabel(
                    p_row,
                    text=(
                        f"Caja #{ps['id']:03d} ({ps['event_name']})  |  "
                        f"Apertura: {ps['opened_at']}  →  Cierre: {ps['closed_at']}  |  "
                        f"Pedidos: {ps['orders_count']}  |  Total: {fmt_money(ps['total_sales'])}"
                    ),
                    font=ctk.CTkFont(size=12, weight="bold"),
                ).pack(side="left", padx=14, pady=10)

                ctk.CTkButton(
                    p_row,
                    text="🖨️ Reimprimir Cierre",
                    width=140,
                    height=30,
                    corner_radius=6,
                    fg_color="#334155",
                    command=lambda sid=ps["id"]: print_cash_closure_direct(
                        get_session_live_metrics(sid), self.settings
                    ),
                ).pack(side="right", padx=12, pady=8)

    def open_new_session_dialog(self):
        win = ctk.CTkToplevel(self)
        win.title("Iniciar Nueva Caja / Nuevo Evento")
        win.geometry("460x380")
        win.grab_set()

        ctk.CTkLabel(
            win,
            text="🔓 Iniciar Nueva Caja / Evento",
            font=ctk.CTkFont(size=18, weight="bold"),
        ).pack(pady=(18, 8))

        ctk.CTkLabel(
            win,
            text="Esto abrirá un turno de caja limpio y reiniciará los tickets a #001.",
            font=ctk.CTkFont(size=12),
            text_color="#94a3b8",
        ).pack(pady=(0, 12))

        form = ctk.CTkFrame(win, fg_color="transparent")
        form.pack(fill="x", padx=24)

        ctk.CTkLabel(form, text="Nombre del Evento / Jornada:").pack(anchor="w", pady=(6, 2))
        e_ev = ctk.CTkEntry(form, height=38)
        e_ev.pack(fill="x")
        e_ev.insert(0, self.settings.get("event_name", "Asociación Juvenil Araucana"))

        ctk.CTkLabel(form, text="Fondo Inicial de Cambio en Efectivo ($):").pack(anchor="w", pady=(12, 2))
        e_cash = ctk.CTkEntry(form, height=38)
        e_cash.pack(fill="x")
        e_cash.insert(0, "0")

        reset_seq_var = tk.BooleanVar(value=True)
        ctk.CTkCheckBox(
            form,
            text="Reiniciar numeración de tickets a #001",
            variable=reset_seq_var,
        ).pack(anchor="w", pady=14)

        def start_caja():
            ev_name = e_ev.get().strip() or "Asociación Juvenil Araucana"
            try:
                open_cash = float(e_cash.get() or 0)
            except ValueError:
                open_cash = 0.0
            self.active_session = open_new_cash_session(ev_name, open_cash, reset_seq_var.get())
            self.cart = []
            self.refresh_all()
            if self.current_section == "closure":
                self.render_closure_panel()
            win.destroy()
            self.show_notification(f"🔓 Nueva Caja iniciada para '{ev_name}' (Próximo ticket #001)")

        ctk.CTkButton(
            win,
            text="✅ ABRIR NUEVA CAJA AHORA",
            height=44,
            corner_radius=10,
            fg_color="#10b981",
            text_color="#042f2e",
            font=ctk.CTkFont(size=14, weight="bold"),
            command=start_caja,
        ).pack(fill="x", padx=24, pady=14)

    # =========================================================================
    # SECCIÓN 5: CONFIGURACIÓN Y TICKETERA (XPRINTER XP-80)
    # =========================================================================
    def _build_section_settings(self, parent):
        card = ctk.CTkFrame(parent, fg_color="#111827", corner_radius=14, border_width=1, border_color="#1f2937")
        card.pack(fill="both", expand=True, padx=10, pady=10)

        ctk.CTkLabel(
            card,
            text="⚙️ Configuración de Ticketera Térmica y Datos del Evento",
            font=ctk.CTkFont(size=18, weight="bold"),
        ).pack(anchor="w", padx=24, pady=(20, 14))

        form = ctk.CTkFrame(card, fg_color="transparent")
        form.pack(fill="x", padx=24)

        ctk.CTkLabel(form, text="Ticketera Conectada en Windows:").pack(anchor="w", pady=(6, 2))
        printers = list_windows_printers()
        self.cmb_set_printer = ctk.CTkComboBox(form, values=printers, width=380, height=38)
        self.cmb_set_printer.pack(anchor="w")

        ctk.CTkLabel(form, text="Ancho del Rollo Térmico:").pack(anchor="w", pady=(12, 2))
        self.cmb_set_width = ctk.CTkComboBox(
            form, values=["80 (Estándar grande 48 columnas)", "58 (Compacto 32 columnas)"], width=380, height=38
        )
        self.cmb_set_width.pack(anchor="w")

        self.chk_double_var = tk.BooleanVar(value=True)
        ctk.CTkCheckBox(
            form,
            text="Imprimir Doble Talón con corte automático (Talón Cliente + Talón Comanda Cocina)",
            variable=self.chk_double_var,
        ).pack(anchor="w", pady=14)

        ctk.CTkLabel(form, text="Alias de Mercado Pago:").pack(anchor="w", pady=(6, 2))
        self.ent_set_alias = ctk.CTkEntry(form, width=380, height=38)
        self.ent_set_alias.pack(anchor="w")

        ctk.CTkLabel(form, text="Mensaje al pie del ticket:").pack(anchor="w", pady=(12, 2))
        self.ent_set_footer = ctk.CTkEntry(form, width=480, height=38)
        self.ent_set_footer.pack(anchor="w")

        btns = ctk.CTkFrame(card, fg_color="transparent")
        btns.pack(anchor="w", padx=24, pady=24)

        ctk.CTkButton(
            btns,
            text="💾 Guardar Configuración",
            height=42,
            corner_radius=10,
            fg_color="#10b981",
            text_color="#042f2e",
            font=ctk.CTkFont(size=14, weight="bold"),
            command=self.save_settings_from_form,
        ).pack(side="left", padx=(0, 12))

        ctk.CTkButton(
            btns,
            text="🖨️ Imprimir Ticket de Prueba (80mm Ancho Completo)",
            height=42,
            corner_radius=10,
            fg_color="#2563eb",
            hover_color="#1d4ed8",
            font=ctk.CTkFont(size=13, weight="bold"),
            command=self.run_printer_test,
        ).pack(side="left")

    def load_settings_into_form(self):
        self.settings = get_settings()
        curr_p = self.settings.get("printer_name") or detect_preferred_thermal_printer()
        if curr_p:
            self.cmb_set_printer.set(curr_p)
        w = self.settings.get("paper_width", "80")
        self.cmb_set_width.set("58 (Compacto 32 columnas)" if w == "58" else "80 (Estándar grande 48 columnas)")
        self.chk_double_var.set(str(self.settings.get("print_double_ticket", "1")) == "1")

        self.ent_set_alias.delete(0, "end")
        self.ent_set_alias.insert(0, self.settings.get("mp_alias", "araucana24"))

        self.ent_set_footer.delete(0, "end")
        self.ent_set_footer.insert(0, self.settings.get("ticket_footer", ""))

    def save_settings_from_form(self):
        w_val = "58" if self.cmb_set_width.get().startswith("58") else "80"
        self.settings = update_settings(
            {
                "printer_name": self.cmb_set_printer.get().strip(),
                "paper_width": w_val,
                "print_double_ticket": "1" if self.chk_double_var.get() else "0",
                "mp_alias": self.ent_set_alias.get().strip() or "araucana24",
                "ticket_footer": self.ent_set_footer.get().strip(),
            }
        )
        self.refresh_all()
        self.show_notification("⚙️ Configuración de ticketera guardada correctamente.")

    def run_printer_test(self):
        self.save_settings_from_form()
        try:
            p = print_test_ticket_direct(self.settings)
            self.show_notification(f"🖨️ Ticket de prueba (48 columnas) enviado a {p}")
        except Exception as e:
            messagebox.showerror("Error en Ticketera", str(e))

    # =========================================================================
    # SINCRONIZACIÓN GENERAL
    # =========================================================================
    def refresh_all(self):
        conn = get_connection()
        self.products = [dict(r) for r in conn.execute("SELECT * FROM products ORDER BY id ASC").fetchall()]
        conn.close()
        self.settings = get_settings()
        self.active_session = get_active_cash_session()

        seq = int(self.settings.get("ticket_Sequence", 1))
        self.lbl_next_ticket_badge.configure(text=f"PRÓXIMO TICKET: #{seq:03d}")

        if self.active_session:
            metrics = get_session_live_metrics(self.active_session["id"])
            self.lbl_header_event.configure(text=f"{self.active_session['event_name']} (Caja #{metrics['id']:03d})")
            self.lbl_sb_session_status.configure(text=f"🟢 CAJA #{metrics['id']:03d} ABIERTA", text_color="#10b981")
            self.lbl_sb_session_total.configure(text=f"Recaudado: {fmt_money(metrics['total_sales'])}")
        else:
            self.lbl_header_event.configure(text="CAJA CERRADA — Iniciá una Nueva Caja para vender")
            self.lbl_sb_session_status.configure(text="🔴 CAJA CERRADA", text_color="#ef4444")
            self.lbl_sb_session_total.configure(text="Sin turno activo")

        self.render_category_pills()
        self.render_pos_product_grid()
        self.render_cart()
        self.refresh_kitchen_counter()

    def refresh_kitchen_counter(self):
        conn = get_connection()
        row = conn.execute(
            """
            SELECT
                COALESCE(SUM(CASE WHEN kitchen_status IN ('pendiente', 'preparando') THEN 1 ELSE 0 END), 0) as pend,
                COALESCE(SUM(CASE WHEN kitchen_status = 'finalizado' THEN 1 ELSE 0 END), 0) as done
            FROM orders
            WHERE status = 'completado'
        """
        ).fetchone()
        conn.close()
        self.lbl_kitchen_badge.configure(text=f"👨‍🍳 Cocina: {row['pend']} en curso | {row['done']} entregados")

    def _schedule_background_sync(self):
        try:
            self.refresh_kitchen_counter()
            if self.current_section == "orders":
                self.render_orders_list()
        except Exception:
            pass
        self.after(2500, self._schedule_background_sync)


if __name__ == "__main__":
    app = ProgramaCajaApp()
    app.mainloop()
