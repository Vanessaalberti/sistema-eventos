# Sistema POS para Eventos — Asociación Juvenil Araucana (Caja + Cocina)

Sistema compuesto por **2 programas sincronizados en tiempo real** para la toma de pedidos, cobro, emisión de tickets térmicos y gestión de comandas de cocina.

---

## 🖥️ Los 2 Programas Incluidos

1. **Programa 1 — CAJA (`iniciar_caja.bat`)**:
   - Se abre como ventana de programa de escritorio independiente.
   - Incluye cargado todo el menú de la **Asociación Juvenil Araucana** (`Almuerzo`, `Principales`, `Bebidas`, `Para Llevar`, `Postres`) y el **Alias de Mercado Pago: `araucana24`**.
   - Permite armar el pedido, calcular el vuelto en efectivo, cobrar y **generar el ticket del cliente** mientras envía el pedido automáticamente a la Cocina.
   - En la pestaña **Historial y Caja** muestra en vivo si cada pedido está *En Cocina (Pendiente)*, *En Preparación* o *Finalizado*.

2. **Programa 2 — COCINA (`iniciar_cocina.bat`)**:
   - Se abre como ventana de programa independiente (puede usarse en un segundo monitor en la misma PC o en otra PC/tablet conectada al mismo Wi-Fi).
   - Apenas la Caja cobra un pedido:
     - Suena una **campana de alerta** en Cocina.
     - Aparece la tarjeta del pedido con el número grande (`#001`, `#002`...), los productos y las notas.
     - **Se genera e imprime automáticamente el ticket de comanda** para la cocina (si la casilla *Auto-imprimir ticket* está activa).
   - Los cocineros pueden marcar cada pedido como **👨‍🍳 En Preparación** y luego **✅ FINALIZADO** cuando lo entregan.

---

## 🖨️ ¿Cómo se generan e imprimen los tickets?

En la pestaña **Ticketera y Evento** (dentro de Caja) podés elegir entre 3 modos según cómo conectes la ticketera:
1. **Directo por USB en Windows (`windows_raw`)**: Envía el comando térmico ESC/POS directo al puerto USB de la ticketera en Windows (usando la API nativa `winspool.drv` de Windows, **sin abrir ninguna ventana de diálogo**).
2. **Directo por Red / Wi-Fi (`network_escpos`)**: Envía el ticket directo por IP al puerto `9100` de la ticketera sin diálogo.
3. **Impresión desde Ventana / PDF (`browser`)**: Abre la vista previa térmica de **58mm u 80mm** lista para imprimir en cualquier impresora de Windows o guardar como PDF.
