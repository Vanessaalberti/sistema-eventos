import sqlite3
import os

DB_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "eventos_pos.db")


def get_connection():
    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    conn.execute("PRAGMA foreign_keys = ON")
    return conn


DEFAULT_MENU_PRODUCTS = [
    # ALMUERZO
    (
        "Almuerzo Completo (11:30 a 14:30)",
        "Almuerzo",
        30000,
        1,
        100,
        1,
        "Entrada (Berenjena/Ensalada rusa) + Milanesa de Cerdo Napolitana + Café y Brownie",
    ),
    # PRINCIPALES
    ("Porción de Muzza", "Principales", 1500, 0, 0, 0, "Porción individual"),
    ("Empanada de Carne", "Principales", 1500, 0, 0, 0, "Unidad"),
    ("Empanada de Jamón y Queso", "Principales", 1500, 0, 0, 0, "Unidad"),
    ("Empanada de Pollo", "Principales", 1500, 0, 0, 0, "Unidad"),
    ("Empanada de Roquefort", "Principales", 1500, 0, 0, 0, "Unidad"),
    ("Empanada Caprese", "Principales", 1500, 0, 0, 0, "Unidad"),
    ("Pancho", "Principales", 2000, 0, 0, 0, "Unidad"),
    ("Sandwich J&Q", "Principales", 3000, 0, 0, 0, "Jamón y Queso"),
    # BEBIDAS
    ("Gaseosa 500cc - Pepsi", "Bebidas", 2000, 0, 0, 0, "Botella 500cc"),
    ("Gaseosa 500cc - Paso de los Toros Pomelo", "Bebidas", 2000, 0, 0, 0, "Botella 500cc"),
    ("Agua Mineral 500cc", "Bebidas", 1000, 0, 0, 0, "Botella 500cc"),
    ("Jugo Baggio 220cc", "Bebidas", 800, 0, 0, 0, "Caja 220cc"),
    ("Vaso 200ml Agua Saborizada Manzana", "Bebidas", 800, 0, 0, 0, "Vaso 200ml"),
    ("Vaso 200ml Agua Saborizada Pomelo", "Bebidas", 800, 0, 0, 0, "Vaso 200ml"),
    ("Café", "Bebidas", 500, 0, 0, 0, "Pocillo / Vaso"),
    # PARA LLEVAR
    ("PrePizza x3", "Para Llevar", 4500, 0, 0, 0, "Pack de 3 prepizzas"),
    (
        "Docena de Empanadas",
        "Para Llevar",
        15000,
        0,
        0,
        1,
        "12 u. a elección: Carne, J&Q, Pollo, Roquefort, Caprese",
    ),
    # POSTRES
    ("Tarta de Ricota", "Postres", 15000, 0, 0, 0, "Tarta entera"),
    ("Pastafrola de Membrillo", "Postres", 15000, 0, 0, 0, "Pastafrola entera"),
    ("Brownie", "Postres", 2500, 0, 0, 0, "Porción individual"),
    ("ChocoArroz", "Postres", 1500, 0, 0, 0, "Unidad"),
]


def init_db():
    conn = get_connection()
    cur = conn.cursor()

    cur.execute("""
        CREATE TABLE IF NOT EXISTS settings (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        )
    """)

    cur.execute("""
        CREATE TABLE IF NOT EXISTS products (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL,
            category TEXT NOT NULL DEFAULT 'General',
            price REAL NOT NULL CHECK(price >= 0),
            stock_enabled INTEGER NOT NULL DEFAULT 0,
            stock_qty INTEGER NOT NULL DEFAULT 0,
            is_combo INTEGER NOT NULL DEFAULT 0,
            description TEXT DEFAULT '',
            active INTEGER NOT NULL DEFAULT 1
        )
    """)

    cur.execute("""
        CREATE TABLE IF NOT EXISTS orders (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            ticket_number INTEGER NOT NULL,
            event_name TEXT NOT NULL,
            total REAL NOT NULL,
            payment_method TEXT NOT NULL,
            amount_received REAL NOT NULL DEFAULT 0,
            change_amount REAL NOT NULL DEFAULT 0,
            notes TEXT DEFAULT '',
            status TEXT NOT NULL DEFAULT 'completado',
            kitchen_status TEXT NOT NULL DEFAULT 'pendiente',
            kitchen_printed INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL,
            finished_at TEXT DEFAULT ''
        )
    """)

    # Asegurar columnas nuevas si la base ya existía
    existing_cols = [r["name"] for r in cur.execute("PRAGMA table_info(orders)").fetchall()]
    if "kitchen_status" not in existing_cols:
        cur.execute("ALTER TABLE orders ADD COLUMN kitchen_status TEXT NOT NULL DEFAULT 'pendiente'")
    if "kitchen_printed" not in existing_cols:
        cur.execute("ALTER TABLE orders ADD COLUMN kitchen_printed INTEGER NOT NULL DEFAULT 0")
    if "finished_at" not in existing_cols:
        cur.execute("ALTER TABLE orders ADD COLUMN finished_at TEXT DEFAULT ''")

    cur.execute("""
        CREATE TABLE IF NOT EXISTS order_items (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            order_id INTEGER NOT NULL,
            product_id INTEGER,
            product_name TEXT NOT NULL,
            category TEXT NOT NULL DEFAULT 'General',
            quantity INTEGER NOT NULL,
            unit_price REAL NOT NULL,
            subtotal REAL NOT NULL,
            FOREIGN KEY(order_id) REFERENCES orders(id) ON DELETE CASCADE
        )
    """)

    default_settings = {
        "event_name": "Asociación Juvenil Araucana",
        "mp_alias": "araucana24",
        "ticket_Sequence": "1",
        "paper_width": "80",            # "58" o "80" (mm)
        "print_double_ticket": "1",     # 1 = Imprime Talón Cliente + Talón Cocina
        "print_mode": "browser",        # "browser", "windows_raw", "network_escpos"
        "printer_ip": "192.168.1.200",
        "printer_port": "9100",
        "printer_name": "",
        "kitchen_auto_print": "1",      # 1 = Cocina genera/imprime comanda automáticamente al llegar pedido
        "ticket_footer": "Mercado Pago Alias: araucana24 - ¡Gracias por colaborar!"
    }
    for k, v in default_settings.items():
        cur.execute("INSERT OR IGNORE INTO settings (key, value) VALUES (?, ?)", (k, v))

    cur.execute("SELECT COUNT(*) as total FROM products")
    if cur.fetchone()["total"] == 0:
        cur.executemany("""
            INSERT INTO products (name, category, price, stock_enabled, stock_qty, is_combo, description)
            VALUES (?, ?, ?, ?, ?, ?, ?)
        """, DEFAULT_MENU_PRODUCTS)

    conn.commit()
    conn.close()


def reset_menu_to_araucana():
    """Recarga el menú oficial de Asociación Juvenil Araucana."""
    conn = get_connection()
    cur = conn.cursor()
    cur.execute("DELETE FROM products")
    cur.executemany("""
        INSERT INTO products (name, category, price, stock_enabled, stock_qty, is_combo, description)
        VALUES (?, ?, ?, ?, ?, ?, ?)
    """, DEFAULT_MENU_PRODUCTS)
    cur.execute("INSERT OR REPLACE INTO settings (key, value) VALUES ('event_name', 'Asociación Juvenil Araucana')")
    cur.execute("INSERT OR REPLACE INTO settings (key, value) VALUES ('mp_alias', 'araucana24')")
    cur.execute("INSERT OR REPLACE INTO settings (key, value) VALUES ('ticket_footer', 'Mercado Pago Alias: araucana24 - ¡Gracias por colaborar!')")
    conn.commit()
    conn.close()


def get_settings():
    conn = get_connection()
    rows = conn.execute("SELECT key, value FROM settings").fetchall()
    conn.close()
    return {r["key"]: r["value"] for r in rows}


def update_settings(new_settings: dict):
    conn = get_connection()
    cur = conn.cursor()
    for k, v in new_settings.items():
        cur.execute("INSERT OR REPLACE INTO settings (key, value) VALUES (?, ?)", (str(k), str(v)))
    conn.commit()
    conn.close()
    return get_settings()
