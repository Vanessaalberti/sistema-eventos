import os
import time
from PIL import Image, ImageDraw, ImageFont, ImageOps
import customtkinter as ctk

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
IMAGES_DIR = os.path.join(BASE_DIR, "static", "img", "productos")

CATEGORY_PALETTES = {
    "Almuerzo": ("#78350f", "#f59e0b", "ALMUERZO"),
    "Principales": ("#7f1d1d", "#ef4444", "PRINCIPAL"),
    "Bebidas": ("#1e3a8a", "#3b82f6", "BEBIDA"),
    "Para Llevar": ("#4c1d95", "#8b5cf6", "PARA LLEVAR"),
    "Postres": ("#831843", "#ec4899", "POSTRE"),
}


def ensure_default_product_image(filename: str, product_name: str, category: str) -> str:
    """
    Si el archivo de imagen no existe en static/img/productos/, genera una miniatura
    visual limpia con Pillow para que cada plato tenga imagen desde el primer momento.
    """
    os.makedirs(IMAGES_DIR, exist_ok=True)
    if not filename:
        safe = "".join(c if c.isalnum() else "_" for c in product_name.lower())[:20]
        filename = f"{safe}.png"

    full_path = os.path.join(IMAGES_DIR, filename)
    if os.path.isfile(full_path):
        return full_path

    bg_dark, accent, tag = CATEGORY_PALETTES.get(category, ("#064e3b", "#10b981", category.upper()))
    w, h = 320, 180
    img = Image.new("RGB", (w, h), bg_dark)
    draw = ImageDraw.Draw(img)

    # Banda decorativa superior e ícono circular central
    draw.rectangle([0, 0, w, 10], fill=accent)
    draw.ellipse([w // 2 - 42, 24, w // 2 + 42, 108], fill=accent)

    # Iniciales del plato en el círculo
    words = [w for w in product_name.split() if len(w) > 2] or product_name.split()
    initials = "".join(w[0].upper() for w in words[:2]) or "P"

    try:
        font_big = ImageFont.truetype("segoeui.ttf", 32)
        font_tag = ImageFont.truetype("segoeuib.ttf", 14)
    except Exception:
        font_big = ImageFont.load_default()
        font_tag = ImageFont.load_default()

    bbox = draw.textbbox((0, 0), initials, font=font_big)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    draw.text(((w - tw) / 2, 66 - th / 2 - 4), initials, fill="#ffffff", font=font_big)

    # Etiqueta inferior con nombre corto
    short_title = product_name if len(product_name) <= 24 else product_name[:22] + "..."
    tb = draw.textbbox((0, 0), short_title, font=font_tag)
    ttw = tb[2] - tb[0]
    draw.text(((w - ttw) / 2, 128), short_title, fill="#f8fafc", font=font_tag)

    img.save(full_path, "PNG")
    return full_path


def import_custom_product_image(source_file: str, product_name: str) -> str:
    """
    Copia, recorta proporcionalmente (320x180) y optimiza una foto JPG/PNG/WEBP elegida
    por el usuario desde su PC, guardándola en static/img/productos/.
    """
    os.makedirs(IMAGES_DIR, exist_ok=True)
    safe = "".join(c if c.isalnum() else "_" for c in product_name.lower())[:18]
    filename = f"custom_{safe}_{int(time.time())}.png"
    dest_path = os.path.join(IMAGES_DIR, filename)

    with Image.open(source_file) as im:
        im_rgb = im.convert("RGB")
        fitted = ImageOps.fit(im_rgb, (320, 180), method=Image.Resampling.LANCZOS)
        fitted.save(dest_path, "PNG")

    return filename


def load_ctk_product_image(image_filename: str, product_name: str, category: str, size=(145, 82)) -> ctk.CTkImage:
    """Carga un CTkImage listo para mostrar en las tarjetas del POS."""
    full_path = ensure_default_product_image(image_filename, product_name, category)
    try:
        pil_img = Image.open(full_path)
    except Exception:
        full_path = ensure_default_product_image("", product_name, category)
        pil_img = Image.open(full_path)
    return ctk.CTkImage(light_image=pil_img, dark_image=pil_img, size=size)
