import json
import mimetypes
import os
import socket
from datetime import datetime
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse

from database import (
    get_connection,
    get_settings,
    init_db,
    reset_menu_to_araucana,
    update_settings,
)
from printer import (
    build_escpos_bytes,
    list_windows_printers,
    send_to_network_printer,
    send_to_windows_printer,
)

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
PORT = int(os.environ.get("POS_PORT", 8080))


def get_local_ip() -> str:
    try:
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as s:
            s.connect(("8.8.8.8", 80))
            return s.getsockname()[0]
    except Exception:
        return "127.0.0.1"


def fetch_all_products(include_inactive=True):
    conn = get_connection()
    query = "SELECT * FROM products ORDER BY id ASC"
    if not include_inactive:
        query = "SELECT * FROM products WHERE active = 1 ORDER BY id ASC"
    rows = [dict(r) for r in conn.execute(query).fetchall()]
    conn.close()
    return rows


def fetch_order_with_items(order_id: int):
    conn = get_connection()
    order_row = conn.execute("SELECT * FROM orders WHERE id = ?", (order_id,)).fetchone()
    if not order_row:
        conn.close()
        return None
    order = dict(order_row)
    items = conn.execute("SELECT * FROM order_items WHERE order_id = ?", (order_id,)).fetchall()
    order["items"] = [dict(i) for i in items]
    conn.close()
    return order


def fetch_recent_orders(limit=50):
    conn = get_connection()
    orders = [
        dict(r)
        for r in conn.execute(
            "SELECT * FROM orders ORDER BY id DESC LIMIT ?", (limit,)
        ).fetchall()
    ]
    for o in orders:
        items = conn.execute(
            "SELECT * FROM order_items WHERE order_id = ?", (o["id"],)
        ).fetchall()
        o["items"] = [dict(i) for i in items]
    conn.close()
    return orders


def fetch_kitchen_board():
    conn = get_connection()
    active_orders = [
        dict(r)
        for r in conn.execute("""
            SELECT * FROM orders
            WHERE status = 'completado' AND kitchen_status IN ('pendiente', 'preparando')
            ORDER BY id ASC
        """).fetchall()
    ]
    for o in active_orders:
        items = conn.execute("SELECT * FROM order_items WHERE order_id = ?", (o["id"],)).fetchall()
        o["items"] = [dict(i) for i in items]

    finished_orders = [
        dict(r)
        for r in conn.execute("""
            SELECT * FROM orders
            WHERE status = 'completado' AND kitchen_status = 'finalizado'
            ORDER BY id DESC LIMIT 20
        """).fetchall()
    ]
    for o in finished_orders:
        items = conn.execute("SELECT * FROM order_items WHERE order_id = ?", (o["id"],)).fetchall()
        o["items"] = [dict(i) for i in items]

    conn.close()
    return {
        "active": active_orders,
        "finished": finished_orders,
    }


def fetch_summary():
    conn = get_connection()
    totals = conn.execute("""
        SELECT
            COUNT(*) as count_orders,
            COALESCE(SUM(total), 0) as total_revenue,
            COALESCE(SUM(CASE WHEN payment_method = 'efectivo' THEN total ELSE 0 END), 0) as cash_total,
            COALESCE(SUM(CASE WHEN payment_method = 'transferencia' THEN total ELSE 0 END), 0) as transfer_total,
            COALESCE(SUM(CASE WHEN payment_method = 'tarjeta' THEN total ELSE 0 END), 0) as card_total,
            COALESCE(SUM(CASE WHEN kitchen_status IN ('pendiente', 'preparando') THEN 1 ELSE 0 END), 0) as pending_kitchen,
            COALESCE(SUM(CASE WHEN kitchen_status = 'finalizado' THEN 1 ELSE 0 END), 0) as finished_kitchen
        FROM orders
        WHERE status = 'completado'
    """).fetchone()
    conn.close()
    return dict(totals)


class POSRequestHandler(BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        pass

    def _send_json(self, data: dict, status: int = 200):
        payload = json.dumps(data, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(payload)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(payload)

    def _read_json(self) -> dict:
        length = int(self.headers.get("Content-Length", 0))
        if length == 0:
            return {}
        raw = self.rfile.read(length).decode("utf-8")
        return json.loads(raw)

    def _serve_file(self, filepath: str):
        if not os.path.isfile(filepath):
            self.send_error(404, "Archivo no encontrado")
            return
        mime, _ = mimetypes.guess_type(filepath)
        mime = mime or "application/octet-stream"
        with open(filepath, "rb") as f:
            content = f.read()
        self.send_response(200)
        self.send_header(
            "Content-Type",
            f"{mime}; charset=utf-8"
            if mime.startswith("text/") or mime == "application/javascript"
            else mime,
        )
        self.send_header("Content-Length", str(len(content)))
        self.end_headers()
        self.wfile.write(content)

    def do_GET(self):
        parsed = urlparse(self.path)
        path = parsed.path

        # Programa 1: CAJA
        if path in ("/", "/index.html", "/caja"):
            return self._serve_file(os.path.join(BASE_DIR, "templates", "index.html"))

        # Programa 2: COCINA
        if path in ("/cocina", "/cocina.html"):
            return self._serve_file(os.path.join(BASE_DIR, "templates", "cocina.html"))

        if path.startswith("/static/"):
            rel = path.lstrip("/").replace("/", os.sep)
            full_path = os.path.abspath(os.path.join(BASE_DIR, rel))
            if not full_path.startswith(os.path.join(BASE_DIR, "static")):
                self.send_error(403)
                return
            return self._serve_file(full_path)

        if path == "/api/bootstrap":
            return self._send_json({
                "ok": True,
                "settings": get_settings(),
                "products": fetch_all_products(include_inactive=True),
                "orders": fetch_recent_orders(40),
                "summary": fetch_summary(),
                "windows_printers": list_windows_printers(),
                "local_ip": get_local_ip(),
                "port": PORT,
            })

        if path == "/api/kitchen/poll":
            board = fetch_kitchen_board()
            return self._send_json({
                "ok": True,
                "settings": get_settings(),
                "active": board["active"],
                "finished": board["finished"],
                "summary": fetch_summary(),
                "local_ip": get_local_ip(),
                "port": PORT,
            })

        self.send_error(404, "Ruta no encontrada")

    def do_POST(self):
        parsed = urlparse(self.path)
        path = parsed.path

        try:
            body = self._read_json()
        except Exception as e:
            return self._send_json({"ok": False, "error": f"JSON inválido: {e}"}, 400)

        # 1. Crear pedido desde CAJA -> envía automáticamente a COCINA y genera ticket
        if path == "/api/orders":
            return self._handle_create_order(body)

        # 2. Actualizar estado en COCINA ('pendiente', 'preparando', 'finalizado')
        if path.startswith("/api/kitchen/orders/") and path.endswith("/status"):
            parts = path.strip("/").split("/")
            order_id = int(parts[3])
            return self._handle_kitchen_status(order_id, body)

        # 3. Marcar comanda como ya impresa en COCINA
        if path.startswith("/api/kitchen/orders/") and path.endswith("/printed"):
            parts = path.strip("/").split("/")
            order_id = int(parts[3])
            conn = get_connection()
            conn.execute("UPDATE orders SET kitchen_printed = 1 WHERE id = ?", (order_id,))
            conn.commit()
            conn.close()
            return self._send_json({"ok": True})

        # 4. Anular pedido y devolver stock
        if path.startswith("/api/orders/") and path.endswith("/cancel"):
            parts = path.strip("/").split("/")
            order_id = int(parts[2])
            return self._handle_cancel_order(order_id)

        # 5. Reimprimir en ticketera directa (Red / USB RAW)
        if path.startswith("/api/orders/") and path.endswith("/print-raw"):
            parts = path.strip("/").split("/")
            order_id = int(parts[2])
            return self._handle_raw_print(order_id)

        # 6. Crear o editar producto / combo / stock
        if path == "/api/products":
            return self._handle_save_product(body)

        # 7. Eliminar producto
        if path.startswith("/api/products/") and path.endswith("/delete"):
            parts = path.strip("/").split("/")
            prod_id = int(parts[2])
            conn = get_connection()
            conn.execute("DELETE FROM products WHERE id = ?", (prod_id,))
            conn.commit()
            conn.close()
            return self._send_json({"ok": True, "products": fetch_all_products()})

        # 8. Restaurar menú Asociación Juvenil Araucana
        if path == "/api/products/reset-menu":
            reset_menu_to_araucana()
            return self._send_json({
                "ok": True,
                "products": fetch_all_products(),
                "settings": get_settings(),
            })

        # 9. Guardar configuración del evento / ticketera
        if path == "/api/settings":
            updated = update_settings(body)
            return self._send_json({"ok": True, "settings": updated})

        # 10. Reiniciar numeración de tickets / nuevo evento
        if path == "/api/event/reset":
            clear_orders = bool(body.get("clear_orders", False))
            conn = get_connection()
            conn.execute("INSERT OR REPLACE INTO settings (key, value) VALUES ('ticket_Sequence', '1')")
            if clear_orders:
                conn.execute("DELETE FROM order_items")
                conn.execute("DELETE FROM orders")
            conn.commit()
            conn.close()
            return self._send_json({
                "ok": True,
                "settings": get_settings(),
                "orders": fetch_recent_orders(40),
                "summary": fetch_summary(),
            })

        self._send_json({"ok": False, "error": "Endpoint no encontrado"}, 404)

    def _handle_kitchen_status(self, order_id: int, body: dict):
        new_status = body.get("kitchen_status", "finalizado")
        if new_status not in ("pendiente", "preparando", "finalizado"):
            return self._send_json({"ok": False, "error": "Estado de cocina inválido"}, 400)

        finished_at = datetime.now().strftime("%H:%M:%S") if new_status == "finalizado" else ""
        conn = get_connection()
        conn.execute(
            "UPDATE orders SET kitchen_status = ?, finished_at = ?, kitchen_printed = 1 WHERE id = ?",
            (new_status, finished_at, order_id),
        )
        conn.commit()
        conn.close()

        board = fetch_kitchen_board()
        return self._send_json({
            "ok": True,
            "active": board["active"],
            "finished": board["finished"],
            "summary": fetch_summary(),
        })

    def _handle_create_order(self, body: dict):
        items = body.get("items", [])
        payment_method = body.get("payment_method", "efectivo")
        amount_received = float(body.get("amount_received", 0) or 0)
        notes = str(body.get("notes", "")).strip()

        if not items:
            return self._send_json({"ok": False, "error": "El pedido está vacío."}, 400)

        conn = get_connection()
        cur = conn.cursor()

        try:
            order_total = 0.0
            resolved_items = []

            for item in items:
                prod_id = int(item["product_id"])
                qty = int(item["quantity"])
                if qty <= 0:
                    continue

                prod = cur.execute("SELECT * FROM products WHERE id = ?", (prod_id,)).fetchone()
                if not prod:
                    conn.close()
                    return self._send_json({"ok": False, "error": f"Producto ID {prod_id} no existe."}, 400)

                if prod["stock_enabled"] and prod["stock_qty"] < qty:
                    conn.close()
                    return self._send_json({
                        "ok": False,
                        "error": f"Stock insuficiente para '{prod['name']}'. Disponible: {prod['stock_qty']} u."
                    }, 400)

                subtotal = float(prod["price"]) * qty
                order_total += subtotal
                resolved_items.append({
                    "product_id": prod["id"],
                    "product_name": prod["name"],
                    "category": prod["category"],
                    "quantity": qty,
                    "unit_price": float(prod["price"]),
                    "subtotal": subtotal,
                    "stock_enabled": int(prod["stock_enabled"]),
                })

            if payment_method == "efectivo":
                if amount_received <= 0:
                    amount_received = order_total
                if amount_received < order_total:
                    conn.close()
                    return self._send_json({
                        "ok": False,
                        "error": "El monto recibido en efectivo es menor al total a cobrar."
                    }, 400)
                change_amount = round(amount_received - order_total, 2)
            else:
                amount_received = order_total
                change_amount = 0.0

            seq_row = cur.execute("SELECT value FROM settings WHERE key = 'ticket_Sequence'").fetchone()
            ticket_num = int(seq_row["value"]) if seq_row else 1
            ev_row = cur.execute("SELECT value FROM settings WHERE key = 'event_name'").fetchone()
            event_name = ev_row["value"] if ev_row else "Asociación Juvenil Araucana"

            now_str = datetime.now().strftime("%d/%m/%Y %H:%M:%S")

            cur.execute("""
                INSERT INTO orders (
                    ticket_number, event_name, total, payment_method,
                    amount_received, change_amount, notes, status,
                    kitchen_status, kitchen_printed, created_at
                )
                VALUES (?, ?, ?, ?, ?, ?, ?, 'completado', 'pendiente', 0, ?)
            """, (ticket_num, event_name, order_total, payment_method, amount_received, change_amount, notes, now_str))
            order_id = cur.lastrowid

            for ri in resolved_items:
                cur.execute("""
                    INSERT INTO order_items (order_id, product_id, product_name, category, quantity, unit_price, subtotal)
                    VALUES (?, ?, ?, ?, ?, ?, ?)
                """, (order_id, ri["product_id"], ri["product_name"], ri["category"], ri["quantity"], ri["unit_price"], ri["subtotal"]))

                if ri["stock_enabled"]:
                    cur.execute("""
                        UPDATE products SET stock_qty = stock_qty - ? WHERE id = ?
                    """, (ri["quantity"], ri["product_id"]))

            cur.execute("INSERT OR REPLACE INTO settings (key, value) VALUES ('ticket_Sequence', ?)", (str(ticket_num + 1),))
            conn.commit()
        except Exception as e:
            conn.rollback()
            conn.close()
            return self._send_json({"ok": False, "error": str(e)}, 500)

        conn.close()

        created_order = fetch_order_with_items(order_id)
        settings = get_settings()

        raw_print_status = None
        if settings.get("print_mode") in ("network_escpos", "windows_raw"):
            try:
                raw_bytes = build_escpos_bytes(created_order, settings)
                if settings["print_mode"] == "network_escpos":
                    send_to_network_printer(
                        raw_bytes,
                        settings.get("printer_ip", "192.168.1.200"),
                        int(settings.get("printer_port", 9100) or 9100),
                    )
                else:
                    send_to_windows_printer(raw_bytes, settings.get("printer_name", ""))
                raw_print_status = "ok"
            except Exception as pe:
                raw_print_status = f"error: {pe}"

        return self._send_json({
            "ok": True,
            "order": created_order,
            "products": fetch_all_products(),
            "orders": fetch_recent_orders(40),
            "summary": fetch_summary(),
            "settings": settings,
            "raw_print_status": raw_print_status,
        })

    def _handle_cancel_order(self, order_id: int):
        conn = get_connection()
        cur = conn.cursor()
        order = cur.execute("SELECT * FROM orders WHERE id = ?", (order_id,)).fetchone()
        if not order:
            conn.close()
            return self._send_json({"ok": False, "error": "Pedido no encontrado."}, 404)
        if order["status"] == "anulado":
            conn.close()
            return self._send_json({"ok": False, "error": "El pedido ya está anulado."}, 400)

        items = cur.execute("SELECT * FROM order_items WHERE order_id = ?", (order_id,)).fetchall()
        for it in items:
            if it["product_id"]:
                cur.execute(
                    "UPDATE products SET stock_qty = stock_qty + ? WHERE id = ? AND stock_enabled = 1",
                    (it["quantity"], it["product_id"]),
                )
        cur.execute("UPDATE orders SET status = 'anulado' WHERE id = ?", (order_id,))
        conn.commit()
        conn.close()

        return self._send_json({
            "ok": True,
            "products": fetch_all_products(),
            "orders": fetch_recent_orders(40),
            "summary": fetch_summary(),
        })

    def _handle_raw_print(self, order_id: int):
        order = fetch_order_with_items(order_id)
        if not order:
            return self._send_json({"ok": False, "error": "Pedido no encontrado."}, 404)
        settings = get_settings()
        try:
            raw_bytes = build_escpos_bytes(order, settings)
            if settings.get("print_mode") == "network_escpos":
                send_to_network_printer(
                    raw_bytes,
                    settings.get("printer_ip", "192.168.1.200"),
                    int(settings.get("printer_port", 9100) or 9100),
                )
            elif settings.get("print_mode") == "windows_raw":
                send_to_windows_printer(raw_bytes, settings.get("printer_name", ""))
            else:
                return self._send_json({
                    "ok": False,
                    "error": "El modo actual es 'Ventana de Impresión'. Se abrirá la vista de ticket."
                }, 400)
            return self._send_json({"ok": True})
        except Exception as e:
            return self._send_json({"ok": False, "error": str(e)}, 500)

    def _handle_save_product(self, body: dict):
        prod_id = body.get("id")
        name = str(body.get("name", "")).strip()
        category = str(body.get("category", "General")).strip() or "General"
        price = float(body.get("price", 0) or 0)
        stock_enabled = 1 if body.get("stock_enabled", False) else 0
        stock_qty = int(body.get("stock_qty", 0) or 0)
        is_combo = 1 if body.get("is_combo", False) else 0
        description = str(body.get("description", "")).strip()
        active = 1 if body.get("active", True) else 0

        if not name:
            return self._send_json({"ok": False, "error": "El nombre del producto es obligatorio."}, 400)

        conn = get_connection()
        cur = conn.cursor()
        if prod_id:
            cur.execute("""
                UPDATE products
                SET name=?, category=?, price=?, stock_enabled=?, stock_qty=?, is_combo=?, description=?, active=?
                WHERE id=?
            """, (name, category, price, stock_enabled, stock_qty, is_combo, description, active, int(prod_id)))
        else:
            cur.execute("""
                INSERT INTO products (name, category, price, stock_enabled, stock_qty, is_combo, description, active)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            """, (name, category, price, stock_enabled, stock_qty, is_combo, description, active))
        conn.commit()
        conn.close()
        return self._send_json({"ok": True, "products": fetch_all_products()})


def main():
    init_db()
    local_ip = get_local_ip()
    server = ThreadingHTTPServer(("0.0.0.0", PORT), POSRequestHandler)
    print("=" * 68)
    print("  SISTEMA DE EVENTOS - ASOCIACIÓN JUVENIL ARAUCANA")
    print("=" * 68)
    print(f"  [PROGRAMA 1 - CAJA]    http://localhost:{PORT}/caja")
    print(f"  [PROGRAMA 2 - COCINA]  http://localhost:{PORT}/cocina")
    print("-" * 68)
    print(f"  En red local (otra PC/Tablet del evento):")
    print(f"  -> Caja:   http://{local_ip}:{PORT}/caja")
    print(f"  -> Cocina: http://{local_ip}:{PORT}/cocina")
    print("=" * 68)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nServidor detenido.")
        server.server_close()


if __name__ == "__main__":
    main()
