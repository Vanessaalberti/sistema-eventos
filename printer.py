import ctypes
from ctypes import wintypes
import os
import socket
import subprocess
import unicodedata

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
TICKETS_DIR = os.path.join(BASE_DIR, "tickets_emitidos")

# Ancho real para Xprinter XP-80 (Font A estándar en 80mm = 48 caracteres por línea)
COLS_80MM = 48
COLS_58MM = 32


def clean_ascii(text: str) -> str:
    """Normaliza tildes y caracteres especiales para impresión térmica limpia."""
    if not text:
        return ""
    replacements = {
        "á": "a", "é": "e", "í": "i", "ó": "o", "ú": "u",
        "Á": "A", "É": "E", "Í": "I", "Ó": "O", "Ú": "U",
        "ñ": "n", "Ñ": "N", "¡": "", "¿": "",
    }
    out = str(text)
    for k, v in replacements.items():
        out = out.replace(k, v)
    nfkd = unicodedata.normalize("NFKD", out)
    return "".join([c for c in nfkd if not unicodedata.combining(c)])


def format_currency(value: float) -> str:
    return f"${value:,.0f}".replace(",", ".")


def _two_cols(left: str, right: str, cols: int) -> str:
    left_c = clean_ascii(left)
    right_c = clean_ascii(right)
    max_left = max(1, cols - len(right_c) - 2)
    if len(left_c) > max_left:
        left_c = left_c[:max_left]
    spaces = max(1, cols - len(left_c) - len(right_c))
    return left_c + (" " * spaces) + right_c


def _wrap_text(text: str, cols: int) -> list:
    words = clean_ascii(text).split()
    if not words:
        return [""]
    lines = []
    current = words[0]
    for w in words[1:]:
        if len(current) + 1 + len(w) <= cols:
            current += " " + w
        else:
            lines.append(current)
            current = w
    lines.append(current)
    return lines


def get_cols(settings: dict) -> int:
    return COLS_58MM if str(settings.get("paper_width", "80")) == "58" else COLS_80MM


def format_client_ticket_text(order: dict, settings: dict) -> str:
    """Genera texto de 48 columnas (80mm completo) o 32 columnas (58mm)."""
    cols = get_cols(settings)
    sep_eq = "=" * cols
    sep_dash = "-" * cols

    lines = [
        sep_eq,
        clean_ascii(settings.get("event_name", "ASOCIACION JUVENIL ARAUCANA")).upper().center(cols),
        "COMPROBANTE DE CLIENTE".center(cols),
        sep_eq,
        f"PEDIDO #{order['ticket_number']:03d}".center(cols),
        str(order.get("created_at", "")).center(cols),
        sep_dash,
        _two_cols("CANT  DESCRIPCION", "IMPORTE", cols),
        sep_dash,
    ]

    for item in order.get("items", []):
        qty_prefix = f"{item['quantity']}x "
        name_clean = clean_ascii(item["product_name"])
        sub_str = format_currency(item["subtotal"])
        lines.append(_two_cols(qty_prefix + name_clean, sub_str, cols))

    lines.append(sep_dash)
    lines.append(_two_cols("TOTAL A PAGAR:", format_currency(order["total"]), cols))

    pay_label = str(order.get("payment_method", "efectivo")).upper()
    if pay_label == "TRANSFERENCIA":
        pay_label = "MERCADO PAGO"
    lines.append(_two_cols("MEDIO DE PAGO:", pay_label, cols))

    if order.get("payment_method") == "transferencia":
        lines.append(_two_cols("ALIAS MP:", settings.get("mp_alias", "araucana24"), cols))
    elif order.get("payment_method") == "efectivo" and order.get("amount_received", 0) > 0:
        lines.append(_two_cols("PAGA CON:", format_currency(order["amount_received"]), cols))
        lines.append(_two_cols("SU VUELTO:", format_currency(order["change_amount"]), cols))

    if order.get("notes"):
        lines.append(sep_dash)
        for nl in _wrap_text(f"NOTA: {order['notes']}", cols):
            lines.append(nl)

    lines.append(sep_eq)
    footer = clean_ascii(settings.get("ticket_footer", "Gracias por colaborar!"))
    for fl in _wrap_text(footer, cols):
        lines.append(fl.center(cols))
    lines.append(sep_eq)
    return "\n".join(lines)


def format_kitchen_ticket_text(order: dict, settings: dict) -> str:
    cols = get_cols(settings)
    sep_eq = "=" * cols
    sep_dash = "-" * cols

    lines = [
        sep_eq,
        "COMANDA DE COCINA".center(cols),
        f"PEDIDO #{order['ticket_number']:03d}".center(cols),
        str(order.get("created_at", "")).center(cols),
        sep_eq,
    ]

    for item in order.get("items", []):
        lines.append(f" [ {item['quantity']} ]  {clean_ascii(item['product_name']).upper()}")

    if order.get("notes"):
        lines.append(sep_dash)
        for nl in _wrap_text(f"NOTA: {order['notes'].upper()}", cols):
            lines.append(nl)

    lines.append(sep_eq)
    return "\n".join(lines)


def format_double_ticket_text(order: dict, settings: dict) -> str:
    cols = get_cols(settings)
    client_txt = format_client_ticket_text(order, settings)
    if str(settings.get("print_double_ticket", "1")) != "1":
        return client_txt
    cut_line = "\n\n" + ("- - - - - CORTAR AQUI - - - - -".center(cols)) + "\n\n"
    return client_txt + cut_line + format_kitchen_ticket_text(order, settings)


def save_ticket_file(order: dict, settings: dict, kind: str = "doble") -> str:
    os.makedirs(TICKETS_DIR, exist_ok=True)
    num = int(order.get("ticket_number", 1))
    if kind == "cocina":
        content = format_kitchen_ticket_text(order, settings)
        filename = f"Ticket_{num:03d}_Cocina.txt"
    elif kind == "cliente":
        content = format_client_ticket_text(order, settings)
        filename = f"Ticket_{num:03d}_Cliente.txt"
    else:
        content = format_double_ticket_text(order, settings)
        filename = f"Ticket_{num:03d}_Completo.txt"

    filepath = os.path.join(TICKETS_DIR, filename)
    with open(filepath, "w", encoding="utf-8") as f:
        f.write(content + "\n")
    return filepath


def build_escpos_bytes(order: dict, settings: dict, only_kitchen: bool = False) -> bytes:
    """
    Construye comandos nativos ESC/POS para Xprinter XP-80 (48 columnas completas en 80mm).
    Resetea márgenes de impresora, usa Font A estándar a ancho completo y corta el papel.
    """
    cols = get_cols(settings)
    sep_eq = "=" * cols
    sep_dash = "-" * cols

    ESC = b"\x1b"
    GS = b"\x1d"

    INIT = ESC + b"@"
    FONT_A = ESC + b"M\x00"              # Fuente A estándar (48 cols en 80mm)
    LEFT_MARGIN_ZERO = GS + b"L\x00\x00" # Margen izquierdo = 0
    PRINT_WIDTH_FULL = GS + b"W\x40\x02" # Ancho área impresión completo (576 dots = 72mm en rollo 80mm)
    ALIGN_LEFT = ESC + b"a\x00"
    ALIGN_CENTER = ESC + b"a\x01"
    BOLD_ON = ESC + b"E\x01"
    BOLD_OFF = ESC + b"E\x00"
    SIZE_NORMAL = GS + b"!\x00"
    SIZE_LARGE = GS + b"!\x11"           # Doble ancho + Doble alto para el N° de Pedido
    SIZE_TALL = GS + b"!\x01"            # Doble alto para totales e ítems de cocina
    CUT_PAPER = GS + b"V\x41\x03"        # Avance y corte automático de papel

    def raw_line(txt: str = "") -> bytes:
        return (clean_ascii(txt) + "\n").encode("ascii", errors="ignore")

    buf = bytearray()
    buf += INIT + FONT_A + LEFT_MARGIN_ZERO + PRINT_WIDTH_FULL

    # =========================================================================
    # 1. TALÓN CLIENTE (USA LAS 48 COLUMNAS COMPLETAS DEL ROLLO 80MM)
    # =========================================================================
    if not only_kitchen:
        buf += ALIGN_CENTER
        buf += BOLD_ON + SIZE_TALL
        buf += raw_line(settings.get("event_name", "ASOCIACION JUVENIL ARAUCANA").upper())
        buf += SIZE_NORMAL
        buf += raw_line("COMPROBANTE DE CLIENTE")
        buf += BOLD_OFF
        buf += raw_line(sep_eq)

        buf += BOLD_ON + SIZE_LARGE
        buf += raw_line(f"PEDIDO #{order['ticket_number']:03d}")
        buf += SIZE_NORMAL + BOLD_OFF
        buf += raw_line(str(order.get("created_at", "")))
        buf += raw_line(sep_dash)

        buf += ALIGN_LEFT
        buf += BOLD_ON + raw_line(_two_cols("CANT  DESCRIPCION", "IMPORTE", cols)) + BOLD_OFF
        buf += raw_line(sep_dash)

        for item in order.get("items", []):
            qty_name = f"{item['quantity']}x {item['product_name']}"
            sub_str = format_currency(item["subtotal"])
            buf += raw_line(_two_cols(qty_name, sub_str, cols))

        buf += raw_line(sep_dash)
        buf += BOLD_ON + SIZE_TALL
        buf += raw_line(_two_cols("TOTAL:", format_currency(order["total"]), cols))
        buf += SIZE_NORMAL + BOLD_OFF

        pay_label = str(order.get("payment_method", "efectivo")).upper()
        if pay_label == "TRANSFERENCIA":
            pay_label = "MERCADO PAGO"
        buf += raw_line(_two_cols("MEDIO DE PAGO:", pay_label, cols))

        if order.get("payment_method") == "transferencia":
            buf += raw_line(_two_cols("ALIAS MP:", settings.get("mp_alias", "araucana24"), cols))
        elif order.get("payment_method") == "efectivo" and order.get("amount_received", 0) > 0:
            buf += raw_line(_two_cols("PAGA CON:", format_currency(order["amount_received"]), cols))
            buf += BOLD_ON + raw_line(_two_cols("SU VUELTO:", format_currency(order["change_amount"]), cols)) + BOLD_OFF

        if order.get("notes"):
            buf += raw_line(sep_dash)
            for nl in _wrap_text(f"Nota: {order['notes']}", cols):
                buf += raw_line(nl)

        buf += raw_line(sep_eq)
        buf += ALIGN_CENTER
        for fl in _wrap_text(settings.get("ticket_footer", "Gracias por colaborar!"), cols):
            buf += raw_line(fl)
        buf += b"\n"
        buf += CUT_PAPER

    # =========================================================================
    # 2. TALÓN COMANDA DE COCINA (CON CORTE AUTOMÁTICO SEPARADO)
    # =========================================================================
    if only_kitchen or str(settings.get("print_double_ticket", "1")) == "1":
        buf += INIT + FONT_A + LEFT_MARGIN_ZERO + PRINT_WIDTH_FULL
        buf += ALIGN_CENTER
        buf += BOLD_ON + raw_line("*** COMANDA DE COCINA ***")
        buf += SIZE_LARGE + raw_line(f"PEDIDO #{order['ticket_number']:03d}") + SIZE_NORMAL + BOLD_OFF
        buf += raw_line(str(order.get("created_at", "")))
        buf += raw_line(sep_eq)

        buf += ALIGN_LEFT
        for item in order.get("items", []):
            buf += BOLD_ON + SIZE_TALL
            buf += raw_line(f"[{item['quantity']}x] {item['product_name'].upper()}")
            buf += SIZE_NORMAL + BOLD_OFF

        if order.get("notes"):
            buf += raw_line(sep_dash)
            buf += BOLD_ON
            for nl in _wrap_text(f"NOTA: {order['notes'].upper()}", cols):
                buf += raw_line(nl)
            buf += BOLD_OFF

        buf += raw_line(sep_eq)
        buf += b"\n"
        buf += CUT_PAPER

    return bytes(buf)


class DOC_INFO_1W(ctypes.Structure):
    _fields_ = [
        ("pDocName", wintypes.LPWSTR),
        ("pOutputFile", wintypes.LPWSTR),
        ("pDatatype", wintypes.LPWSTR),
    ]


def get_default_windows_printer() -> str:
    try:
        winspool = ctypes.WinDLL("winspool.drv", use_last_error=True)
        GetDefaultPrinterW = winspool.GetDefaultPrinterW
        size = wintypes.DWORD(0)
        GetDefaultPrinterW(None, ctypes.byref(size))
        if size.value > 0:
            buf = ctypes.create_unicode_buffer(size.value)
            if GetDefaultPrinterW(buf, ctypes.byref(size)):
                return buf.value
    except Exception:
        pass
    return ""


def detect_preferred_thermal_printer() -> str:
    """Detecta automáticamente la ticketera térmica (ej. Xprinter XP-80) si está instalada."""
    printers = list_windows_printers()
    keywords = ("xprinter", "xp-80", "xp-58", "pos", "thermal", "ticket", "epson", "bixolon", "hasar")
    for p in printers:
        low = p.lower()
        if any(k in low for k in keywords):
            return p
    return get_default_windows_printer() or (printers[0] if printers else "")


def send_to_windows_printer(raw_bytes: bytes, printer_name: str = ""):
    """Envía bytes ESC/POS RAW directo al spooler USB de Windows (sin márgenes de página)."""
    if not printer_name:
        printer_name = detect_preferred_thermal_printer()
    if not printer_name:
        raise RuntimeError("No se encontró ninguna impresora conectada en Windows.")

    winspool = ctypes.WinDLL("winspool.drv", use_last_error=True)

    OpenPrinterW = winspool.OpenPrinterW
    OpenPrinterW.argtypes = [wintypes.LPWSTR, ctypes.POINTER(wintypes.HANDLE), wintypes.LPVOID]
    OpenPrinterW.restype = wintypes.BOOL

    StartDocPrinterW = winspool.StartDocPrinterW
    StartDocPrinterW.argtypes = [wintypes.HANDLE, wintypes.DWORD, ctypes.POINTER(DOC_INFO_1W)]
    StartDocPrinterW.restype = wintypes.DWORD

    StartPagePrinter = winspool.StartPagePrinter
    StartPagePrinter.argtypes = [wintypes.HANDLE]
    StartPagePrinter.restype = wintypes.BOOL

    WritePrinter = winspool.WritePrinter
    WritePrinter.argtypes = [wintypes.HANDLE, wintypes.LPVOID, wintypes.DWORD, ctypes.POINTER(wintypes.DWORD)]
    WritePrinter.restype = wintypes.BOOL

    EndPagePrinter = winspool.EndPagePrinter
    EndPagePrinter.argtypes = [wintypes.HANDLE]
    EndPagePrinter.restype = wintypes.BOOL

    EndDocPrinter = winspool.EndDocPrinter
    EndDocPrinter.argtypes = [wintypes.HANDLE]
    EndDocPrinter.restype = wintypes.BOOL

    ClosePrinter = winspool.ClosePrinter
    ClosePrinter.argtypes = [wintypes.HANDLE]
    ClosePrinter.restype = wintypes.BOOL

    hprinter = wintypes.HANDLE()
    if not OpenPrinterW(printer_name, ctypes.byref(hprinter), None):
        err = ctypes.get_last_error()
        raise RuntimeError(f"No se pudo abrir la ticketera '{printer_name}' (Código: {err})")

    try:
        doc_info = DOC_INFO_1W("Ticket POS", None, "RAW")
        job_id = StartDocPrinterW(hprinter, 1, ctypes.byref(doc_info))
        if job_id == 0:
            err = ctypes.get_last_error()
            raise RuntimeError(f"No se pudo iniciar impresión RAW en '{printer_name}' (Código: {err})")
        try:
            StartPagePrinter(hprinter)
            written = wintypes.DWORD(0)
            buf = ctypes.create_string_buffer(raw_bytes)
            ok = WritePrinter(hprinter, buf, len(raw_bytes), ctypes.byref(written))
            EndPagePrinter(hprinter)
            if not ok:
                err = ctypes.get_last_error()
                raise RuntimeError(f"Error al enviar datos a la ticketera (Código: {err})")
        finally:
            EndDocPrinter(hprinter)
    finally:
        ClosePrinter(hprinter)


def send_to_network_printer(raw_bytes: bytes, ip: str, port: int = 9100, timeout: float = 4.0):
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.settimeout(timeout)
        sock.connect((ip, int(port)))
        sock.sendall(raw_bytes)


def print_ticket_direct(order: dict, settings: dict, only_kitchen: bool = False):
    """
    Función unificada: envía SIEMPRE en formato térmico ESC/POS RAW a ancho completo (80mm/48 cols)
    a la ticketera detectada (ej. Xprinter XP-80).
    """
    kind = "cocina" if only_kitchen else "doble"
    save_ticket_file(order, settings, kind=kind)
    printer_name = settings.get("printer_name", "").strip() or detect_preferred_thermal_printer()
    raw_bytes = build_escpos_bytes(order, settings, only_kitchen=only_kitchen)
    send_to_windows_printer(raw_bytes, printer_name)
    return printer_name


def list_windows_printers() -> list:
    try:
        res = subprocess.run(
            ["powershell", "-NoProfile", "-WindowStyle", "Hidden", "-Command", "Get-Printer | Select-Object -ExpandProperty Name"],
            capture_output=True,
            text=True,
            timeout=4,
        )
        if res.returncode == 0:
            return [line.strip() for line in res.stdout.splitlines() if line.strip()]
    except Exception:
        pass
    default_p = get_default_windows_printer()
    return [default_p] if default_p else []
