import sqlite3
import os
from datetime import datetime

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
DB_PATH = os.path.join(BASE_DIR, "eventos_pos.db")
IMAGES_DIR = os.path.join(BASE_DIR, "static", "img", "productos")


def get_connection():
    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    conn.execute("PRAGMA foreign_keys = ON")
    return conn


DEFAULT_MENU_PRODUCTS = [
    # ALMUERZO
    (
        "Almuerzo Completo",
        "Almuerzo",
        30000,
        1,
        100,
        1,
        "Entrada (Berenjena/Rusa) + Milanesa Napolitana + Café y Brownie",
        "almuerzo.png",
    ),
    # PRINCIPALES
    ("Porción de Muzza", "Principales", 1500, 0, 0, 0, "Porción individual", "muzza.png"),
    ("Empanada de Carne", "Principales", 1500, 0, 0, 0, "Unidad", "emp_carne.png"),
    ("Empanada Jamón y Queso", "Principales", 1500, 0, 0, 0, "Unidad", "emp_jyq.png"),
    ("Empanada de Pollo", "Principales", 1500, 0, 0, 0, "Unidad", "emp_pollo.png"),
    ("Empanada de Roquefort", "Principales", 1500, 0, 0, 0, "Unidad", "emp_roquefort.png"),
    ("Empanada Caprese", "Principales", 1500, 0, 0, 0, "Unidad", "emp_caprese.png"),
    ("Pancho", "Principales", 2000, 0, 0, 0, "Unidad", "pancho.png"),
    ("Sandwich J&Q", "Principales", 3000, 0, 0, 0, "Jamón y Queso", "sandwich.png"),
    # BEBIDAS
    ("Gaseosa 500cc Pepsi", "Bebidas", 2000, 0, 0, 0, "Botella 500cc", "pepsi.png"),
    ("Paso de los Toros Pomelo", "Bebidas", 2000, 0, 0, 0, "Botella 500cc", "pomelo.png"),
    ("Agua Mineral 500cc", "Bebidas", 1000, 0, 0, 0, "Botella 500cc", "agua.png"),
    ("Jugo Baggio 220cc", "Bebidas", 800, 0, 0, 0, "Caja 220cc", "baggio.png"),
    ("Agua Saborizada Manzana", "Bebidas", 800, 0, 0, 0, "Vaso 200ml", "sab_manzana.png"),
    ("Agua Saborizada Pomelo", "Bebidas", 800, 0, 0, 0, "Vaso 200ml", "sab_pomelo.png"),
    ("Café", "Bebidas", 500, 0, 0, 0, "Pocillo / Vaso", "cafe.png"),
    # PARA LLEVAR
    ("PrePizza x3", "Para Llevar", 4500, 0, 0, 0, "Pack de 3 prepizzas", "prepizza.png"),
    (
        "Docena de Empanadas",
        "Para Llevar",
        15000,
        0,
        0,
        1,
        "12 unidades a elección",
        "docena_emp.png",
    ),
    # POSTRES
    ("Tarta de Ricota", "Postres", 15000, 0, 0, 0, "Tarta entera", "tarta_ricota.png"),
    ("Pastafrola de Membrillo", "Postres", 15000, 0, 0, 0, "Pastafrola entera", "pastafrola.png"),
    ("Brownie", "Postres", 2500, 0, 0, 0, "Porción individual", "brownie.png"),
    ("ChocoArroz", "Postres", 1500, 0, 0, 0, "Unidad", "chocoarroz.png"),
]


def init_db():
    os.makedirs(IMAGES_DIR, exist_ok=True)
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
            image_path TEXT DEFAULT '',
            active INTEGER NOT NULL DEFAULT 1
        )
    """)

    prod_cols = [r["name"] for r in cur.execute("PRAGMA table_info(products)").fetchall()]
    if "image_path" not in prod_cols:
        cur.execute("ALTER TABLE products ADD COLUMN image_path TEXT DEFAULT ''")

    # Tabla de Sesiones / Cierres de Caja
    cur.execute("""
        CREATE TABLE IF NOT EXISTS cash_sessions (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            event_name TEXT NOT NULL,
            opened_at TEXT NOT NULL,
            closed_at TEXT DEFAULT '',
            opening_cash REAL NOT NULL DEFAULT 0,
            cash_sales REAL NOT NULL DEFAULT 0,
            mp_sales REAL NOT NULL DEFAULT 0,
            card_sales REAL NOT NULL DEFAULT 0,
            total_sales REAL NOT NULL DEFAULT 0,
            expected_cash REAL NOT NULL DEFAULT 0,
            counted_cash REAL NOT NULL DEFAULT 0,
            difference REAL NOT NULL DEFAULT 0,
            orders_count INTEGER NOT NULL DEFAULT 0,
            status TEXT NOT NULL DEFAULT 'abierta',
            notes TEXT DEFAULT ''
        )
    """)

    cur.execute("""
        CREATE TABLE IF NOT EXISTS orders (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            session_id INTEGER DEFAULT 1,
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

    order_cols = [r["name"] for r in cur.execute("PRAGMA table_info(orders)").fetchall()]
    if "session_id" not in order_cols:
        cur.execute("ALTER TABLE orders ADD COLUMN session_id INTEGER DEFAULT 1")
    if "kitchen_status" not in order_cols:
        cur.execute("ALTER TABLE orders ADD COLUMN kitchen_status TEXT NOT NULL DEFAULT 'pendiente'")
    if "kitchen_printed" not in order_cols:
        cur.execute("ALTER TABLE orders ADD COLUMN kitchen_printed INTEGER NOT NULL DEFAULT 0")
    if "finished_at" not in order_cols:
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
        "paper_width": "80",
        "print_double_ticket": "1",
        "print_mode": "windows_raw",
        "printer_ip": "192.168.1.200",
        "printer_port": "9100",
        "printer_name": "Xprinter XP-80",
        "kitchen_auto_print": "1",
        "ticket_footer": "Mercado Pago Alias: araucana24 - ¡Gracias por colaborar!",
    }
    for k, v in default_settings.items():
        cur.execute("INSERT OR IGNORE INTO settings (key, value) VALUES (?, ?)", (k, v))

    # Crear productos iniciales si no hay
    cur.execute("SELECT COUNT(*) as total FROM products")
    if cur.fetchone()["total"] == 0:
        cur.executemany(
            """
            INSERT INTO products (name, category, price, stock_enabled, stock_qty, is_combo, description, image_path)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
        """,
            DEFAULT_MENU_PRODUCTS,
        )
    else:
        # Asignar imagen por defecto si algún producto existente no tiene image_path
        for name, cat, price, se, sq, ic, desc, img_file in DEFAULT_MENU_PRODUCTS:
            cur.execute(
                "UPDATE products SET image_path = ? WHERE (image_path IS NULL OR image_path = '') AND name LIKE ?",
                (img_file, f"%{name[:14]}%"),
            )

    # Asegurar que exista una sesión de caja abierta por defecto si nunca se abrió una
    cur.execute("SELECT COUNT(*) as c FROM cash_sessions")
    if cur.fetchone()["c"] == 0:
        now_str = datetime.now().strftime("%d/%m/%Y %H:%M:%S")
        cur.execute(
            """
            INSERT INTO cash_sessions (event_name, opened_at, opening_cash, status)
            VALUES ('Asociación Juvenil Araucana', ?, 0, 'abierta')
        """,
            (now_str,),
        )

    conn.commit()
    conn.close()


def get_active_cash_session():
    """Devuelve la sesión de caja actualmente abierta o None si la caja está cerrada."""
    conn = get_connection()
    row = conn.execute(
        "SELECT * FROM cash_sessions WHERE status = 'abierta' ORDER BY id DESC LIMIT 1"
    ).fetchone()
    conn.close()
    return dict(row) if row else None


def open_new_cash_session(event_name: str, opening_cash: float = 0.0, reset_sequence: bool = True) -> dict:
    """Cierra cualquier sesión previa abierta y abre una nueva caja limpia."""
    conn = get_connection()
    cur = conn.cursor()
    now_str = datetime.now().strftime("%d/%m/%Y %H:%M:%S")

    cur.execute("UPDATE cash_sessions SET status = 'cerrada', closed_at = ? WHERE status = 'abierta'", (now_str,))
    cur.execute(
        """
        INSERT INTO cash_sessions (event_name, opened_at, opening_cash, status)
        VALUES (?, ?, ?, 'abierta')
    """,
        (event_name, now_str, float(opening_cash)),
    )
    session_id = cur.lastrowid

    cur.execute("INSERT OR REPLACE INTO settings (key, value) VALUES ('event_name', ?)", (event_name,))
    if reset_sequence:
        cur.execute("INSERT OR REPLACE INTO settings (key, value) VALUES ('ticket_Sequence', '1')")

    conn.commit()
    row = cur.execute("SELECT * FROM cash_sessions WHERE id = ?", (session_id,)).fetchone()
    conn.close()
    return dict(row)


def get_session_live_metrics(session_id: int) -> dict:
    """Calcula en vivo todos los totales y el ranking de productos vendidos en la sesión."""
    conn = get_connection()
    sess = conn.execute("SELECT * FROM cash_sessions WHERE id = ?", (session_id,)).fetchone()
    if not sess:
        conn.close()
        return {}
    sess_dict = dict(sess)

    totals = dict(
        conn.execute(
            """
        SELECT
            COUNT(*) as orders_count,
            COALESCE(SUM(total), 0) as total_sales,
            COALESCE(SUM(CASE WHEN payment_method = 'efectivo' THEN total ELSE 0 END), 0) as cash_sales,
            COALESCE(SUM(CASE WHEN payment_method = 'transferencia' THEN total ELSE 0 END), 0) as mp_sales,
            COALESCE(SUM(CASE WHEN payment_method = 'tarjeta' THEN total ELSE 0 END), 0) as card_sales
        FROM orders
        WHERE session_id = ? AND status = 'completado'
    """,
            (session_id,),
        ).fetchone()
    )

    ranking = [
        dict(r)
        for r in conn.execute(
            """
        SELECT
            oi.product_name,
            oi.category,
            SUM(oi.quantity) as total_qty,
            SUM(oi.subtotal) as total_amount
        FROM order_items oi
        JOIN orders o ON o.id = oi.order_id
        WHERE o.session_id = ? AND o.status = 'completado'
        GROUP BY oi.product_name, oi.category
        ORDER BY total_qty DESC, total_amount DESC
    """,
            (session_id,),
        ).fetchall()
    ]
    conn.close()

    opening_cash = float(sess_dict.get("opening_cash", 0))
    cash_sales = float(totals.get("cash_sales", 0))
    expected_cash = opening_cash + cash_sales

    sess_dict.update(totals)
    sess_dict["expected_cash"] = expected_cash
    sess_dict["ranking"] = ranking
    return sess_dict


def close_cash_session(session_id: int, counted_cash: float, notes: str = "") -> dict:
    """Registra el arqueo y cierra formalmente la sesión de caja."""
    metrics = get_session_live_metrics(session_id)
    if not metrics:
        raise ValueError("Sesión de caja no encontrada.")

    now_str = datetime.now().strftime("%d/%m/%Y %H:%M:%S")
    expected = float(metrics["expected_cash"])
    diff = float(counted_cash) - expected

    conn = get_connection()
    conn.execute(
        """
        UPDATE cash_sessions
        SET closed_at = ?,
            cash_sales = ?,
            mp_sales = ?,
            card_sales = ?,
            total_sales = ?,
            expected_cash = ?,
            counted_cash = ?,
            difference = ?,
            orders_count = ?,
            status = 'cerrada',
            notes = ?
        WHERE id = ?
    """,
        (
            now_str,
            metrics["cash_sales"],
            metrics["mp_sales"],
            metrics["card_sales"],
            metrics["total_sales"],
            expected,
            float(counted_cash),
            diff,
            metrics["orders_count"],
            notes,
            session_id,
        ),
    )
    conn.commit()
    conn.close()
    return get_session_live_metrics(session_id)


def reset_menu_to_araucana():
    conn = get_connection()
    cur = conn.cursor()
    cur.execute("DELETE FROM products")
    cur.executemany(
        """
        INSERT INTO products (name, category, price, stock_enabled, stock_qty, is_combo, description, image_path)
        VALUES (?, ?, ?, ?, ?, ?, ?, ?)
    """,
        DEFAULT_MENU_PRODUCTS,
    )
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
