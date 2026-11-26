using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;

namespace EventosPos
{
    public class MenuGroup
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
    }

    public class PackFlavorChoice
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; }
        public int Quantity { get; set; }
    }

    public class ProductItem
    {
        public int Id { get; set; }
        public int GroupId { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public decimal Price { get; set; }
        public bool StockEnabled { get; set; }
        public int StockQty { get; set; }
        public bool IsCombo { get; set; }
        public int PackRequiredQty { get; set; }       // 0 = normal, 6 = Media Docena, 12 = Docena
        public string PackSourceFilter { get; set; }   // ej: "Empanada" para tomar todos los sabores de empanada
        public string Description { get; set; }
        public string ImagePath { get; set; }
        public bool Active { get; set; }
    }

    public class OrderLineItem
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; }
        public string Category { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Subtotal { get; set; }
        public string Note { get; set; }
        public List<PackFlavorChoice> PackChoices { get; set; }

        public OrderLineItem()
        {
            PackChoices = new List<PackFlavorChoice>();
        }
    }

    public class OrderRecord
    {
        public int Id { get; set; }
        public int SessionId { get; set; }
        public int TicketNumber { get; set; }
        public string EventName { get; set; }
        public decimal Total { get; set; }
        public string PaymentMethod { get; set; } // "Efectivo", "Mercado Pago", "Tarjeta"
        public decimal AmountReceived { get; set; }
        public decimal ChangeAmount { get; set; }
        public string Notes { get; set; }
        public string Status { get; set; } // "completado", "anulado"
        public string KitchenStatus { get; set; } // "pendiente", "preparando", "finalizado" (Listo en cocina), "entregado" (Despachado en Caja)
        public bool KitchenPrinted { get; set; }
        public string CreatedAt { get; set; }
        public string FinishedAt { get; set; }
        public string DeliveredAt { get; set; }
        public List<OrderLineItem> Items { get; set; }

        public OrderRecord()
        {
            Items = new List<OrderLineItem>();
        }
    }

    public class CashSession
    {
        public int Id { get; set; }
        public int MenuGroupId { get; set; }
        public string MenuGroupName { get; set; }
        public string EventName { get; set; }
        public string OpenedAt { get; set; }
        public string ClosedAt { get; set; }
        public decimal OpeningCash { get; set; }
        public decimal CashSales { get; set; }
        public decimal MpSales { get; set; }
        public decimal CardSales { get; set; }
        public decimal TotalSales { get; set; }
        public decimal ExpectedCash { get; set; }
        public decimal CountedCash { get; set; }
        public decimal Difference { get; set; }
        public int OrdersCount { get; set; }
        public string Status { get; set; } // "abierta", "cerrada"
        public string Notes { get; set; }
    }

    public class PosSettings
    {
        public string EventName { get; set; }
        public int ActiveMenuGroupId { get; set; }
        public string MpAlias { get; set; }
        public int NextTicketSequence { get; set; }
        public int PaperWidthMm { get; set; }
        public int ClientTicketCopies { get; set; }     // 1 a 4 copias iguales del ticket de cliente
        public bool PrintDoubleTicket { get; set; }     // Desactivado por defecto (Cocina_POS imprime las suyas)
        public string PrinterName { get; set; }
        public string KitchenPrinterName { get; set; }  // Vacío = misma ticketera que Caja
        public string TicketFooter { get; set; }

        // Métodos de pago configurables
        public bool EnableCash { get; set; }
        public bool EnableTransfer { get; set; }
        public bool EnableCard { get; set; }

        // Personalización del Logo en el Ticket
        public string TicketLogoPath { get; set; }
        public bool PrintTicketLogo { get; set; }
        public int TicketLogoWidthPx { get; set; }

        public PosSettings()
        {
            EventName = "Asociación Juvenil Araucana";
            ActiveMenuGroupId = 1;
            MpAlias = "araucana24";
            NextTicketSequence = 1;
            PaperWidthMm = 80;
            ClientTicketCopies = 1;
            PrintDoubleTicket = false; // Desactivado por defecto porque Cocina_POS imprime sus comandas
            PrinterName = "Xprinter XP-80";
            KitchenPrinterName = "";
            TicketFooter = "Alias Transferencia: araucana24 - ¡Gracias por colaborar!";
            EnableCash = true;
            EnableTransfer = true;
            EnableCard = false; // Desactivada por defecto según solicitud
            TicketLogoPath = "";
            PrintTicketLogo = true;
            TicketLogoWidthPx = 280;
        }
    }

    public class PosDatabasePayload
    {
        public PosSettings Settings { get; set; }
        public List<MenuGroup> MenuGroups { get; set; }
        public List<ProductItem> Products { get; set; }
        public List<CashSession> Sessions { get; set; }
        public List<OrderRecord> Orders { get; set; }

        public PosDatabasePayload()
        {
            Settings = new PosSettings();
            MenuGroups = new List<MenuGroup>();
            Products = new List<ProductItem>();
            Sessions = new List<CashSession>();
            Orders = new List<OrderRecord>();
        }
    }

    public static class PosStore
    {
        public static readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
        public static readonly string DataDir = Path.Combine(BaseDir, "data");
        public static readonly string ImagesDir = Path.Combine(BaseDir, "static", "img", "productos");
        public static readonly string TicketsDir = Path.Combine(BaseDir, "tickets_emitidos");
        public static readonly string DbFilePath = Path.Combine(DataDir, "pos_state.json");

        private static readonly object FileLock = new object();

        public static string FormatMoney(decimal val)
        {
            return "$" + val.ToString("N0", new CultureInfo("es-AR"));
        }

        public static PosDatabasePayload Load()
        {
            lock (FileLock)
            {
                Directory.CreateDirectory(DataDir);
                Directory.CreateDirectory(ImagesDir);
                Directory.CreateDirectory(TicketsDir);

                if (!File.Exists(DbFilePath))
                {
                    var initial = CreateDefaultDatabase();
                    Save(initial);
                    return initial;
                }

                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        string json = File.ReadAllText(DbFilePath, Encoding.UTF8);
                        var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                        var payload = ser.Deserialize<PosDatabasePayload>(json);
                        if (payload != null)
                        {
                            if (payload.Settings == null) payload.Settings = new PosSettings();
                            if (!payload.Settings.EnableCash && !payload.Settings.EnableTransfer && !payload.Settings.EnableCard)
                            {
                                payload.Settings.EnableCash = true;
                                payload.Settings.EnableTransfer = true;
                                payload.Settings.EnableCard = false;
                            }
                            if (payload.Settings.TicketLogoWidthPx <= 0)
                                payload.Settings.TicketLogoWidthPx = 280;
                            if (payload.Settings.ClientTicketCopies < 1 || payload.Settings.ClientTicketCopies > 4)
                                payload.Settings.ClientTicketCopies = 1;
                            if (payload.MenuGroups == null || payload.MenuGroups.Count == 0)
                                payload.MenuGroups = GetDefaultMenuGroups();
                            if (payload.Products == null || payload.Products.Count == 0)
                                payload.Products = GetDefaultProducts();

                            foreach (var p in payload.Products)
                            {
                                if (p.GroupId <= 0) p.GroupId = 1;
                                // Auto-configurar packs de empanadas existentes si vienen de una versión anterior
                                string lowName = (p.Name ?? "").ToLowerInvariant();
                                if (p.PackRequiredQty == 0 && lowName.Contains("docena") && lowName.Contains("empanada"))
                                {
                                    p.PackRequiredQty = lowName.Contains("media") ? 6 : 12;
                                    p.PackSourceFilter = "Empanada";
                                    p.IsCombo = true;
                                }
                            }

                            // Asegurar que exista Media Docena de Empanadas en el Grupo 1 si aún no existía
                            if (payload.Products.Any(p => p.GroupId == 1 && (p.Name ?? "").ToLowerInvariant().Contains("docena de empanadas"))
                                && !payload.Products.Any(p => p.GroupId == 1 && (p.Name ?? "").ToLowerInvariant().Contains("media docena")))
                            {
                                int nextPid = payload.Products.Max(p => p.Id) + 1;
                                payload.Products.Add(new ProductItem
                                {
                                    Id = nextPid,
                                    GroupId = 1,
                                    Name = "Media Docena de Empanadas",
                                    Category = "Para Llevar",
                                    Price = 8000,
                                    StockEnabled = false,
                                    StockQty = 0,
                                    IsCombo = true,
                                    PackRequiredQty = 6,
                                    PackSourceFilter = "Empanada",
                                    Description = "6 unidades a elección (descuenta stock por sabor)",
                                    ImagePath = "docena_emp.png",
                                    Active = true
                                });
                            }

                            if (payload.Settings.ActiveMenuGroupId <= 0 || !payload.MenuGroups.Any(g => g.Id == payload.Settings.ActiveMenuGroupId))
                            {
                                payload.Settings.ActiveMenuGroupId = payload.MenuGroups[0].Id;
                            }

                            if (payload.Sessions == null || payload.Sessions.Count == 0)
                            {
                                var grp = payload.MenuGroups.FirstOrDefault(g => g.Id == payload.Settings.ActiveMenuGroupId) ?? payload.MenuGroups[0];
                                payload.Sessions = new List<CashSession>
                                {
                                    new CashSession
                                    {
                                        Id = 1,
                                        MenuGroupId = grp.Id,
                                        MenuGroupName = grp.Name,
                                        EventName = payload.Settings.EventName,
                                        OpenedAt = DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
                                        OpeningCash = 0,
                                        Status = "abierta"
                                    }
                                };
                            }
                            else
                            {
                                foreach (var s in payload.Sessions)
                                {
                                    if (s.MenuGroupId <= 0)
                                    {
                                        s.MenuGroupId = payload.MenuGroups[0].Id;
                                        s.MenuGroupName = payload.MenuGroups[0].Name;
                                    }
                                }
                            }

                            if (payload.Orders == null) payload.Orders = new List<OrderRecord>();
                            return payload;
                        }
                    }
                    catch
                    {
                        Thread.Sleep(40);
                    }
                }
                return CreateDefaultDatabase();
            }
        }

        public static void Save(PosDatabasePayload payload)
        {
            lock (FileLock)
            {
                Directory.CreateDirectory(DataDir);
                var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                string json = ser.Serialize(payload);
                string tmp = DbFilePath + ".tmp";
                File.WriteAllText(tmp, json, Encoding.UTF8);
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        if (File.Exists(DbFilePath))
                            File.Delete(DbFilePath);
                        File.Move(tmp, DbFilePath);
                        break;
                    }
                    catch
                    {
                        Thread.Sleep(35);
                    }
                }
            }
        }

        public static List<MenuGroup> GetDefaultMenuGroups()
        {
            return new List<MenuGroup>
            {
                new MenuGroup { Id = 1, Name = "Almuerzo y Buffet Araucana", Description = "Menú completo de almuerzo, empanadas, bebidas, para llevar y postres" },
                new MenuGroup { Id = 2, Name = "Kiosco / Cantina Rápida", Description = "Menú reducido para eventos cortos, bebidas, panchos y café" }
            };
        }

        public static PosDatabasePayload CreateDefaultDatabase()
        {
            var groups = GetDefaultMenuGroups();
            var settings = new PosSettings
            {
                EventName = "Asociación Juvenil Araucana",
                ActiveMenuGroupId = 1,
                MpAlias = "araucana24",
                NextTicketSequence = 1,
                PaperWidthMm = 80,
                ClientTicketCopies = 1,
                PrintDoubleTicket = false,
                PrinterName = EscPosPrinter.DetectPreferredPrinter(),
                KitchenPrinterName = "",
                TicketFooter = "Alias Transferencia: araucana24 - ¡Gracias por colaborar!"
            };

            var db = new PosDatabasePayload
            {
                Settings = settings,
                MenuGroups = groups,
                Products = GetDefaultProducts(),
                Sessions = new List<CashSession>
                {
                    new CashSession
                    {
                        Id = 1,
                        MenuGroupId = 1,
                        MenuGroupName = groups[0].Name,
                        EventName = settings.EventName,
                        OpenedAt = DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
                        OpeningCash = 0,
                        Status = "abierta"
                    }
                },
                Orders = new List<OrderRecord>()
            };
            return db;
        }

        public static List<ProductItem> GetDefaultProducts()
        {
            return new List<ProductItem>
            {
                // Grupo 1: Almuerzo y Buffet Araucana
                new ProductItem { Id = 1, GroupId = 1, Name = "Almuerzo Completo", Category = "Almuerzo", Price = 30000, StockEnabled = true, StockQty = 100, IsCombo = true, Description = "Entrada (Berenjena/Rusa) + Milanesa Napolitana + Café y Brownie", ImagePath = "almuerzo.png", Active = true },
                new ProductItem { Id = 2, GroupId = 1, Name = "Porción de Muzza", Category = "Principales", Price = 1500, StockEnabled = false, StockQty = 0, Description = "Porción individual", ImagePath = "muzza.png", Active = true },
                new ProductItem { Id = 3, GroupId = 1, Name = "Empanada de Carne", Category = "Empanadas", Price = 1500, StockEnabled = false, StockQty = 0, Description = "Unidad al horno", ImagePath = "emp_carne.png", Active = true },
                new ProductItem { Id = 4, GroupId = 1, Name = "Empanada Jamón y Queso", Category = "Empanadas", Price = 1500, StockEnabled = false, StockQty = 0, Description = "Unidad al horno", ImagePath = "emp_jyq.png", Active = true },
                new ProductItem { Id = 5, GroupId = 1, Name = "Empanada de Pollo", Category = "Empanadas", Price = 1500, StockEnabled = false, StockQty = 0, Description = "Unidad al horno", ImagePath = "emp_pollo.png", Active = true },
                new ProductItem { Id = 6, GroupId = 1, Name = "Empanada de Roquefort", Category = "Empanadas", Price = 1500, StockEnabled = false, StockQty = 0, Description = "Unidad al horno", ImagePath = "emp_roquefort.png", Active = true },
                new ProductItem { Id = 7, GroupId = 1, Name = "Empanada Caprese", Category = "Empanadas", Price = 1500, StockEnabled = false, StockQty = 0, Description = "Unidad al horno", ImagePath = "emp_caprese.png", Active = true },
                new ProductItem { Id = 8, GroupId = 1, Name = "Pancho", Category = "Principales", Price = 2000, StockEnabled = false, StockQty = 0, Description = "Con aderezos a elección", ImagePath = "pancho.png", Active = true },
                new ProductItem { Id = 9, GroupId = 1, Name = "Sandwich J&Q", Category = "Principales", Price = 3000, StockEnabled = false, StockQty = 0, Description = "Jamón y Queso", ImagePath = "sandwich.png", Active = true },
                new ProductItem { Id = 10, GroupId = 1, Name = "Gaseosa 500cc Pepsi", Category = "Bebidas", Price = 2000, StockEnabled = false, StockQty = 0, Description = "Botella 500cc", ImagePath = "pepsi.png", Active = true },
                new ProductItem { Id = 11, GroupId = 1, Name = "Paso de los Toros Pomelo", Category = "Bebidas", Price = 2000, StockEnabled = false, StockQty = 0, Description = "Botella 500cc", ImagePath = "pomelo.png", Active = true },
                new ProductItem { Id = 12, GroupId = 1, Name = "Agua Mineral 500cc", Category = "Bebidas", Price = 1000, StockEnabled = false, StockQty = 0, Description = "Botella 500cc", ImagePath = "agua.png", Active = true },
                new ProductItem { Id = 13, GroupId = 1, Name = "Jugo Baggio 220cc", Category = "Bebidas", Price = 800, StockEnabled = false, StockQty = 0, Description = "Caja individual 220cc", ImagePath = "baggio.png", Active = true },
                new ProductItem { Id = 14, GroupId = 1, Name = "Agua Saborizada Manzana", Category = "Bebidas", Price = 800, StockEnabled = false, StockQty = 0, Description = "Vaso 200ml", ImagePath = "sab_manzana.png", Active = true },
                new ProductItem { Id = 15, GroupId = 1, Name = "Agua Saborizada Pomelo", Category = "Bebidas", Price = 800, StockEnabled = false, StockQty = 0, Description = "Vaso 200ml", ImagePath = "sab_pomelo.png", Active = true },
                new ProductItem { Id = 16, GroupId = 1, Name = "Café", Category = "Bebidas", Price = 500, StockEnabled = false, StockQty = 0, Description = "Pocillo / Vaso", ImagePath = "cafe.png", Active = true },
                new ProductItem { Id = 17, GroupId = 1, Name = "PrePizza x3", Category = "Para Llevar", Price = 4500, StockEnabled = false, StockQty = 0, Description = "Pack de 3 prepizzas", ImagePath = "prepizza.png", Active = true },
                new ProductItem { Id = 18, GroupId = 1, Name = "Docena de Empanadas", Category = "Para Llevar", Price = 15000, StockEnabled = false, StockQty = 0, IsCombo = true, PackRequiredQty = 12, PackSourceFilter = "Empanadas", Description = "12 unidades a elección (enlazado a stock de Empanadas)", ImagePath = "docena_emp.png", Active = true },
                new ProductItem { Id = 29, GroupId = 1, Name = "Media Docena de Empanadas", Category = "Para Llevar", Price = 8000, StockEnabled = false, StockQty = 0, IsCombo = true, PackRequiredQty = 6, PackSourceFilter = "Empanadas", Description = "6 unidades a elección (enlazado a stock de Empanadas)", ImagePath = "docena_emp.png", Active = true },
                new ProductItem { Id = 19, GroupId = 1, Name = "Tarta de Ricota", Category = "Postres", Price = 15000, StockEnabled = false, StockQty = 0, Description = "Tarta entera", ImagePath = "tarta_ricota.png", Active = true },
                new ProductItem { Id = 20, GroupId = 1, Name = "Pastafrola de Membrillo", Category = "Postres", Price = 15000, StockEnabled = false, StockQty = 0, Description = "Pastafrola entera", ImagePath = "pastafrola.png", Active = true },
                new ProductItem { Id = 21, GroupId = 1, Name = "Brownie", Category = "Postres", Price = 2500, StockEnabled = false, StockQty = 0, Description = "Porción individual", ImagePath = "brownie.png", Active = true },
                new ProductItem { Id = 22, GroupId = 1, Name = "ChocoArroz", Category = "Postres", Price = 1500, StockEnabled = false, StockQty = 0, Description = "Unidad", ImagePath = "chocoarroz.png", Active = true },

                // Grupo 2: Kiosco / Cantina Rápida (ejemplo inicial editable)
                new ProductItem { Id = 23, GroupId = 2, Name = "Pancho", Category = "Principales", Price = 2000, StockEnabled = false, StockQty = 0, Description = "Con aderezos", ImagePath = "pancho.png", Active = true },
                new ProductItem { Id = 24, GroupId = 2, Name = "Sandwich J&Q", Category = "Principales", Price = 3000, StockEnabled = false, StockQty = 0, Description = "Jamón y Queso", ImagePath = "sandwich.png", Active = true },
                new ProductItem { Id = 25, GroupId = 2, Name = "Gaseosa 500cc Pepsi", Category = "Bebidas", Price = 2000, StockEnabled = false, StockQty = 0, Description = "Botella 500cc", ImagePath = "pepsi.png", Active = true },
                new ProductItem { Id = 26, GroupId = 2, Name = "Agua Mineral 500cc", Category = "Bebidas", Price = 1000, StockEnabled = false, StockQty = 0, Description = "Botella 500cc", ImagePath = "agua.png", Active = true },
                new ProductItem { Id = 27, GroupId = 2, Name = "Café", Category = "Bebidas", Price = 500, StockEnabled = false, StockQty = 0, Description = "Vaso", ImagePath = "cafe.png", Active = true },
                new ProductItem { Id = 28, GroupId = 2, Name = "Brownie", Category = "Postres", Price = 2500, StockEnabled = false, StockQty = 0, Description = "Porción", ImagePath = "brownie.png", Active = true }
            };
        }

        public static CashSession GetActiveSession(PosDatabasePayload db)
        {
            return db.Sessions.LastOrDefault(s => s.Status == "abierta");
        }

        public static void RecalculateSessionMetrics(PosDatabasePayload db, CashSession session)
        {
            if (session == null) return;
            var validOrders = db.Orders.Where(o => o.SessionId == session.Id && o.Status == "completado").ToList();
            session.OrdersCount = validOrders.Count;
            session.CashSales = validOrders.Where(o => o.PaymentMethod == "Efectivo").Sum(o => o.Total);
            session.MpSales = validOrders.Where(o => o.PaymentMethod == "Transferencia" || o.PaymentMethod == "Mercado Pago").Sum(o => o.Total);
            session.CardSales = validOrders.Where(o => o.PaymentMethod == "Tarjeta").Sum(o => o.Total);
            session.TotalSales = validOrders.Sum(o => o.Total);
            session.ExpectedCash = session.OpeningCash + session.CashSales;
        }
    }

    // =========================================================================
    // MOTOR DE RED LOCAL (LAN / WI-FI) ENTRE DISTINTAS PCs (SIN SERVIDOR EXTERNO)
    // - Caja_POS.exe levanta automáticamente un servidor TCP (puerto 54545) y
    //   un faro de auto-descubrimiento UDP (puerto 54546).
    // - Cocina_POS.exe detecta automáticamente la IP de la PC de Caja en la red
    //   (o permite escribir la IP manualmente) para sincronizar pedidos en vivo.
    // =========================================================================
    public static class PosNetworkSync
    {
        public const int TcpPort = 54545;
        public const int UdpDiscoveryPort = 54546;

        private static TcpListener _tcpServer;
        private static Thread _serverThread;
        private static Thread _beaconThread;
        private static volatile bool _running;

        public static string GetLocalLanIp()
        {
            try
            {
                using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    s.Connect("8.8.8.8", 65530);
                    var endPoint = s.LocalEndPoint as IPEndPoint;
                    if (endPoint != null) return endPoint.Address.ToString();
                }
            }
            catch { }

            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ua.Address))
                            return ua.Address.ToString();
                    }
                }
            }
            catch { }
            return "127.0.0.1";
        }

        public static void StartCajaServer()
        {
            if (_running) return;
            _running = true;

            // 1. Servidor TCP que responde a Cocina_POS.exe desde cualquier PC de la red
            _serverThread = new Thread(() =>
            {
                try
                {
                    _tcpServer = new TcpListener(IPAddress.Any, TcpPort);
                    _tcpServer.Start();
                    while (_running)
                    {
                        try
                        {
                            var client = _tcpServer.AcceptTcpClient();
                            ThreadPool.QueueUserWorkItem(_ => HandleCajaClient(client));
                        }
                        catch
                        {
                            if (!_running) break;
                        }
                    }
                }
                catch { }
            });
            _serverThread.IsBackground = true;
            _serverThread.Start();

            // 2. Faro UDP Broadcast cada 2 segundos para que Cocina_POS.exe descubra la IP de Caja solo
            _beaconThread = new Thread(() =>
            {
                while (_running)
                {
                    try
                    {
                        string localIp = GetLocalLanIp();
                        using (var udp = new UdpClient())
                        {
                            udp.EnableBroadcast = true;
                            byte[] msg = Encoding.UTF8.GetBytes("ARAUCANA_CAJA_IP|" + localIp + "|" + TcpPort);
                            udp.Send(msg, msg.Length, new IPEndPoint(IPAddress.Broadcast, UdpDiscoveryPort));
                        }
                    }
                    catch { }
                    Thread.Sleep(2000);
                }
            });
            _beaconThread.IsBackground = true;
            _beaconThread.Start();
        }

        public static void StopCajaServer()
        {
            _running = false;
            try { if (_tcpServer != null) _tcpServer.Stop(); } catch { }
        }

        private static void HandleCajaClient(TcpClient client)
        {
            using (client)
            {
                client.ReceiveTimeout = 3000;
                client.SendTimeout = 3000;
                try
                {
                    using (var stream = client.GetStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true })
                    {
                        string cmdLine = reader.ReadLine();
                        if (string.IsNullOrEmpty(cmdLine)) return;

                        if (cmdLine == "GET_STATE")
                        {
                            var db = PosStore.Load();
                            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                            writer.WriteLine(ser.Serialize(db));
                        }
                        else if (cmdLine.StartsWith("SET_KITCHEN_STATUS|"))
                        {
                            // Formato: SET_KITCHEN_STATUS|<orderId>|<newStatus>
                            string[] parts = cmdLine.Split('|');
                            if (parts.Length >= 3)
                            {
                                int orderId;
                                if (int.TryParse(parts[1], out orderId))
                                {
                                    string newStatus = parts[2];
                                    var db = PosStore.Load();
                                    var ord = db.Orders.FirstOrDefault(o => o.Id == orderId);
                                    if (ord != null)
                                    {
                                        ord.KitchenStatus = newStatus;
                                        if (newStatus == "finalizado")
                                            ord.FinishedAt = DateTime.Now.ToString("HH:mm");
                                        else if (newStatus == "entregado")
                                            ord.DeliveredAt = DateTime.Now.ToString("HH:mm");
                                        else
                                            ord.FinishedAt = "";
                                        ord.KitchenPrinted = true;
                                        PosStore.Save(db);
                                    }
                                    var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                                    writer.WriteLine(ser.Serialize(db));
                                }
                            }
                        }
                        else if (cmdLine.StartsWith("MARK_PRINTED|"))
                        {
                            string[] parts = cmdLine.Split('|');
                            int orderId;
                            if (parts.Length >= 2 && int.TryParse(parts[1], out orderId))
                            {
                                var db = PosStore.Load();
                                var ord = db.Orders.FirstOrDefault(o => o.Id == orderId);
                                if (ord != null)
                                {
                                    ord.KitchenPrinted = true;
                                    PosStore.Save(db);
                                }
                                writer.WriteLine("OK");
                            }
                        }
                    }
                }
                catch { }
            }
        }

        // Métodos usados por Cocina_POS.exe para conectarse con la PC de Caja
        public static PosDatabasePayload FetchStateFromCajaIp(string hostIp)
        {
            using (var client = new TcpClient())
            {
                var result = client.BeginConnect(hostIp, TcpPort, null, null);
                bool success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(900));
                if (!success || !client.Connected)
                    throw new Exception("No se pudo conectar con Caja en " + hostIp);

                client.EndConnect(result);
                client.ReceiveTimeout = 2500;
                client.SendTimeout = 2500;

                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true })
                {
                    writer.WriteLine("GET_STATE");
                    string json = reader.ReadLine();
                    if (string.IsNullOrEmpty(json))
                        throw new Exception("Respuesta vacía de Caja");
                    var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                    return ser.Deserialize<PosDatabasePayload>(json);
                }
            }
        }

        public static PosDatabasePayload SendKitchenStatusToCajaIp(string hostIp, int orderId, string newStatus)
        {
            using (var client = new TcpClient())
            {
                var result = client.BeginConnect(hostIp, TcpPort, null, null);
                bool success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(1200));
                if (!success || !client.Connected)
                    throw new Exception("No se pudo enviar estado a Caja en " + hostIp);

                client.EndConnect(result);
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true })
                {
                    writer.WriteLine(string.Format("SET_KITCHEN_STATUS|{0}|{1}", orderId, newStatus));
                    string json = reader.ReadLine();
                    var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                    return ser.Deserialize<PosDatabasePayload>(json);
                }
            }
        }

        public static void SendMarkPrintedToCajaIp(string hostIp, int orderId)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var result = client.BeginConnect(hostIp, TcpPort, null, null);
                    if (result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(800)) && client.Connected)
                    {
                        client.EndConnect(result);
                        using (var stream = client.GetStream())
                        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true })
                        {
                            writer.WriteLine("MARK_PRINTED|" + orderId);
                        }
                    }
                }
            }
            catch { }
        }
    }

    public static class EscPosPrinter
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public class DOCINFOA
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string pDocName;
            [MarshalAs(UnmanagedType.LPWStr)] public string pOutputFile;
            [MarshalAs(UnmanagedType.LPWStr)] public string pDataType;
        }

        [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool OpenPrinter(string szPrinter, out IntPtr hPrinter, IntPtr pd);

        [DllImport("winspool.drv", EntryPoint = "ClosePrinter", SetLastError = true)]
        public static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int StartDocPrinter(IntPtr hPrinter, int level, [In] DOCINFOA di);

        [DllImport("winspool.drv", EntryPoint = "EndDocPrinter", SetLastError = true)]
        public static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", EntryPoint = "StartPagePrinter", SetLastError = true)]
        public static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", EntryPoint = "EndPagePrinter", SetLastError = true)]
        public static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", EntryPoint = "WritePrinter", SetLastError = true)]
        public static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

        public static List<string> ListInstalledPrinters()
        {
            var list = new List<string>();
            try
            {
                foreach (string p in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
                {
                    list.Add(p);
                }
            }
            catch { }
            return list;
        }

        public static string DetectPreferredPrinter()
        {
            var printers = ListInstalledPrinters();
            string[] keywords = { "xprinter", "xp-80", "xp-58", "pos", "thermal", "ticket", "epson", "bixolon" };
            foreach (var p in printers)
            {
                string low = p.ToLowerInvariant();
                if (keywords.Any(k => low.Contains(k)))
                    return p;
            }
            return printers.FirstOrDefault() ?? "Xprinter XP-80";
        }

        public static string CleanAscii(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            string s = input
                .Replace("á", "a").Replace("é", "e").Replace("í", "i").Replace("ó", "o").Replace("ú", "u")
                .Replace("Á", "A").Replace("É", "E").Replace("Í", "I").Replace("Ó", "O").Replace("Ú", "U")
                .Replace("ñ", "n").Replace("Ñ", "N").Replace("¡", "").Replace("¿", "");
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c >= 32 && c <= 126) sb.Append(c);
            }
            return sb.ToString();
        }

        public static string TwoCols(string left, string right, int cols)
        {
            string l = CleanAscii(left);
            string r = CleanAscii(right);
            int maxLeft = Math.Max(1, cols - r.Length - 2);
            if (l.Length > maxLeft) l = l.Substring(0, maxLeft);
            int spaces = Math.Max(1, cols - l.Length - r.Length);
            return l + new string(' ', spaces) + r;
        }

        public static string CenterText(string text, int cols)
        {
            string t = CleanAscii(text);
            if (t.Length >= cols) return t.Substring(0, cols);
            int pad = (cols - t.Length) / 2;
            return new string(' ', pad) + t;
        }

        public static string FormatOrderPreviewText(OrderRecord order, PosSettings settings, bool includeKitchen)
        {
            int cols = settings.PaperWidthMm == 58 ? 32 : 48;
            string sepEq = new string('=', cols);
            string sepDash = new string('-', cols);
            var sb = new StringBuilder();

            sb.AppendLine(sepEq);
            sb.AppendLine(CenterText((order.EventName ?? settings.EventName).ToUpperInvariant(), cols));
            sb.AppendLine(CenterText("COMPROBANTE DE CLIENTE", cols));
            sb.AppendLine(sepEq);
            sb.AppendLine(CenterText(string.Format("PEDIDO #{0:000}", order.TicketNumber), cols));
            sb.AppendLine(CenterText(order.CreatedAt, cols));
            sb.AppendLine(sepDash);
            sb.AppendLine(TwoCols("CANT  DESCRIPCION", "IMPORTE", cols));
            sb.AppendLine(sepDash);

            foreach (var it in order.Items)
            {
                sb.AppendLine(TwoCols(string.Format("{0}x {1}", it.Quantity, it.ProductName), PosStore.FormatMoney(it.Subtotal), cols));
                if (it.PackChoices != null && it.PackChoices.Count > 0)
                {
                    foreach (var pc in it.PackChoices)
                        sb.AppendLine(string.Format("   - {0}x {1}", pc.Quantity, CleanAscii(pc.ProductName)));
                }
                if (!string.IsNullOrWhiteSpace(it.Note))
                    sb.AppendLine("   * " + CleanAscii(it.Note));
            }

            sb.AppendLine(sepDash);
            sb.AppendLine(TwoCols("TOTAL:", PosStore.FormatMoney(order.Total), cols));
            sb.AppendLine(TwoCols("MEDIO DE PAGO:", order.PaymentMethod.ToUpperInvariant(), cols));

            if (order.PaymentMethod == "Transferencia" || order.PaymentMethod == "Mercado Pago")
            {
                sb.AppendLine(TwoCols("ALIAS TRANSF.:", settings.MpAlias, cols));
            }
            else if (order.PaymentMethod == "Efectivo" && order.AmountReceived > 0)
            {
                sb.AppendLine(TwoCols("PAGA CON:", PosStore.FormatMoney(order.AmountReceived), cols));
                sb.AppendLine(TwoCols("SU VUELTO:", PosStore.FormatMoney(order.ChangeAmount), cols));
            }

            if (!string.IsNullOrWhiteSpace(order.Notes))
            {
                sb.AppendLine(sepDash);
                sb.AppendLine("Nota: " + CleanAscii(order.Notes));
            }

            sb.AppendLine(sepEq);
            if (!string.IsNullOrWhiteSpace(settings.TicketFooter))
                sb.AppendLine(CenterText(settings.TicketFooter, cols));
            sb.AppendLine(sepEq);

            if (includeKitchen && settings.PrintDoubleTicket)
            {
                sb.AppendLine();
                sb.AppendLine(CenterText("- - - - CORTAR AQUI - - - -", cols));
                sb.AppendLine();
                sb.AppendLine(sepEq);
                sb.AppendLine(CenterText("*** COMANDA DE COCINA ***", cols));
                sb.AppendLine(CenterText(string.Format("PEDIDO #{0:000}", order.TicketNumber), cols));
                sb.AppendLine(CenterText(order.CreatedAt, cols));
                sb.AppendLine(sepEq);
                foreach (var it in order.Items)
                {
                    sb.AppendLine(string.Format("[{0}x] {1}", it.Quantity, CleanAscii(it.ProductName).ToUpperInvariant()));
                    if (it.PackChoices != null && it.PackChoices.Count > 0)
                    {
                        foreach (var pc in it.PackChoices)
                            sb.AppendLine(string.Format("   - {0}x {1}", pc.Quantity, CleanAscii(pc.ProductName).ToUpperInvariant()));
                    }
                    if (!string.IsNullOrWhiteSpace(it.Note))
                        sb.AppendLine("   >> " + CleanAscii(it.Note).ToUpperInvariant());
                }
                if (!string.IsNullOrWhiteSpace(order.Notes))
                {
                    sb.AppendLine(sepDash);
                    sb.AppendLine("NOTA: " + CleanAscii(order.Notes).ToUpperInvariant());
                }
                sb.AppendLine(sepEq);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Convierte una imagen (PNG/JPG/BMP) en comandos ESC/POS raster (GS v 0) optimizados
        /// para impresoras térmicas de 80mm (máx 576 dots) o 58mm (máx 384 dots).
        /// Compone sobre fondo blanco para soportar PNGs con transparencia sin manchas negras.
        /// </summary>
        public static byte[] BuildLogoEscPosRasterBytes(PosSettings settings)
        {
            if (settings == null || !settings.PrintTicketLogo || string.IsNullOrWhiteSpace(settings.TicketLogoPath))
                return null;

            string fullPath = Path.IsPathRooted(settings.TicketLogoPath)
                ? settings.TicketLogoPath
                : Path.Combine(PosStore.ImagesDir, settings.TicketLogoPath);

            if (!File.Exists(fullPath))
                return null;

            try
            {
                using (var src = new Bitmap(fullPath))
                {
                    int maxDots = settings.PaperWidthMm == 58 ? 320 : 440;
                    int desiredW = settings.TicketLogoWidthPx > 0 ? settings.TicketLogoWidthPx : 280;
                    int targetW = Math.Min(maxDots, Math.Max(120, desiredW));
                    // Redondear ancho a múltiplo exacto de 8 píxeles para GS v 0
                    targetW = (targetW / 8) * 8;
                    if (targetW < 8) targetW = 8;

                    double ratio = (double)src.Height / Math.Max(1, src.Width);
                    int targetH = (int)Math.Round(targetW * ratio);
                    // Limitar altura máxima para que el ticket quede prolijo y no desperdicie papel
                    if (targetH > 200)
                    {
                        targetH = 200;
                        targetW = ((int)Math.Round(targetH / ratio) / 8) * 8;
                        if (targetW < 8) targetW = 8;
                    }

                    using (var resized = new Bitmap(targetW, targetH, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
                    {
                        using (var g = Graphics.FromImage(resized))
                        {
                            g.Clear(Color.White);
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.SmoothingMode = SmoothingMode.HighQuality;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.DrawImage(src, 0, 0, targetW, targetH);
                        }

                        int widthBytes = targetW / 8;
                        var ms = new MemoryStream();
                        // Centrar imagen
                        ms.Write(new byte[] { 0x1B, 0x61, 0x01 }, 0, 3);
                        // Comando ESC/POS GS v 0 (m = 0 normal)
                        ms.Write(new byte[]
                        {
                            0x1D, 0x76, 0x30, 0x00,
                            (byte)(widthBytes & 0xFF), (byte)((widthBytes >> 8) & 0xFF),
                            (byte)(targetH & 0xFF), (byte)((targetH >> 8) & 0xFF)
                        }, 0, 8);

                        for (int y = 0; y < targetH; y++)
                        {
                            for (int bx = 0; bx < widthBytes; bx++)
                            {
                                byte slice = 0;
                                for (int bit = 0; bit < 8; bit++)
                                {
                                    int x = bx * 8 + bit;
                                    Color c = resized.GetPixel(x, y);
                                    double luma = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
                                    if (luma < 165)
                                    {
                                        slice |= (byte)(1 << (7 - bit));
                                    }
                                }
                                ms.WriteByte(slice);
                            }
                        }
                        ms.WriteByte(0x0A); // Pequeño salto de línea debajo del logo
                        return ms.ToArray();
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        public static void PrintOrder(OrderRecord order, PosSettings settings, bool onlyKitchen = false)
        {
            Directory.CreateDirectory(PosStore.TicketsDir);
            string txt = FormatOrderPreviewText(order, settings, !onlyKitchen);
            string file = Path.Combine(PosStore.TicketsDir, string.Format("Ticket_{0:000}.txt", order.TicketNumber));
            File.WriteAllText(file, txt, Encoding.UTF8);

            int cols = settings.PaperWidthMm == 58 ? 32 : 48;
            string sepEq = new string('=', cols);
            string sepDash = new string('-', cols);

            var ms = new MemoryStream();
            Action<byte[]> write = b => ms.Write(b, 0, b.Length);
            Action<string> line = s =>
            {
                byte[] bytes = Encoding.ASCII.GetBytes(CleanAscii(s) + "\n");
                ms.Write(bytes, 0, bytes.Length);
            };

            byte[] INIT = { 0x1B, 0x40 };
            byte[] FONT_A = { 0x1B, 0x4D, 0x00 };
            byte[] MARGIN_ZERO = { 0x1D, 0x4C, 0x00, 0x00 };
            byte[] WIDTH_FULL = { 0x1D, 0x57, 0x40, 0x02 };
            byte[] ALIGN_LEFT = { 0x1B, 0x61, 0x00 };
            byte[] ALIGN_CENTER = { 0x1B, 0x61, 0x01 };
            byte[] BOLD_ON = { 0x1B, 0x45, 0x01 };
            byte[] BOLD_OFF = { 0x1B, 0x45, 0x00 };
            byte[] SIZE_NORMAL = { 0x1D, 0x21, 0x00 };
            byte[] SIZE_TALL = { 0x1D, 0x21, 0x01 };
            byte[] SIZE_LARGE = { 0x1D, 0x21, 0x11 };
            byte[] CUT = { 0x1D, 0x56, 0x41, 0x03 };

            write(INIT); write(FONT_A); write(MARGIN_ZERO); write(WIDTH_FULL);

            string cajaPrinter = string.IsNullOrWhiteSpace(settings.PrinterName) ? DetectPreferredPrinter() : settings.PrinterName;
            string kitchenPrinter = string.IsNullOrWhiteSpace(settings.KitchenPrinterName) ? cajaPrinter : settings.KitchenPrinterName;
            int copies = Math.Max(1, Math.Min(4, settings.ClientTicketCopies));

            if (!onlyKitchen)
            {
                // Construir 1 copia del ticket de cliente
                byte[] logoBytes = BuildLogoEscPosRasterBytes(settings);
                for (int copyIdx = 0; copyIdx < copies; copyIdx++)
                {
                    write(INIT); write(FONT_A); write(MARGIN_ZERO); write(WIDTH_FULL);
                    if (logoBytes != null)
                        write(logoBytes);

                    write(ALIGN_CENTER);
                    write(BOLD_ON); write(SIZE_TALL);
                    line((order.EventName ?? settings.EventName).ToUpperInvariant());
                    write(SIZE_NORMAL);
                    line(copies > 1 ? string.Format("COMPROBANTE ({0}/{1})", copyIdx + 1, copies) : "COMPROBANTE DE CLIENTE");
                    write(BOLD_OFF);
                    line(sepEq);

                    write(BOLD_ON); write(SIZE_LARGE);
                    line(string.Format("PEDIDO #{0:000}", order.TicketNumber));
                    write(SIZE_NORMAL); write(BOLD_OFF);
                    line(order.CreatedAt);
                    line(sepDash);

                    write(ALIGN_LEFT);
                    write(BOLD_ON);
                    line(TwoCols("CANT  DESCRIPCION", "IMPORTE", cols));
                    write(BOLD_OFF);
                    line(sepDash);

                    foreach (var it in order.Items)
                    {
                        line(TwoCols(string.Format("{0}x {1}", it.Quantity, it.ProductName), PosStore.FormatMoney(it.Subtotal), cols));
                        if (it.PackChoices != null && it.PackChoices.Count > 0)
                        {
                            foreach (var pc in it.PackChoices)
                                line(string.Format("   - {0}x {1}", pc.Quantity, pc.ProductName));
                        }
                        if (!string.IsNullOrWhiteSpace(it.Note))
                            line("   * " + it.Note);
                    }

                    line(sepDash);
                    write(BOLD_ON); write(SIZE_TALL);
                    line(TwoCols("TOTAL:", PosStore.FormatMoney(order.Total), cols));
                    write(SIZE_NORMAL); write(BOLD_OFF);

                    line(TwoCols("MEDIO DE PAGO:", order.PaymentMethod.ToUpperInvariant(), cols));
                    if (order.PaymentMethod == "Transferencia" || order.PaymentMethod == "Mercado Pago")
                    {
                        line(TwoCols("ALIAS TRANSF.:", settings.MpAlias, cols));
                    }
                    else if (order.PaymentMethod == "Efectivo" && order.AmountReceived > 0)
                    {
                        line(TwoCols("PAGA CON:", PosStore.FormatMoney(order.AmountReceived), cols));
                        write(BOLD_ON);
                        line(TwoCols("SU VUELTO:", PosStore.FormatMoney(order.ChangeAmount), cols));
                        write(BOLD_OFF);
                    }

                    if (!string.IsNullOrWhiteSpace(order.Notes))
                    {
                        line(sepDash);
                        line("Nota: " + order.Notes);
                    }

                    line(sepEq);
                    write(ALIGN_CENTER);
                    if (!string.IsNullOrWhiteSpace(settings.TicketFooter))
                        line(settings.TicketFooter);
                    line("");
                    write(CUT);
                }

                // Enviar las copias del ticket de cliente a la ticketera de Caja
                SendRawBytes(cajaPrinter, ms.ToArray());
                ms = new MemoryStream();
            }

            if (onlyKitchen || settings.PrintDoubleTicket)
            {
                write(INIT); write(FONT_A); write(MARGIN_ZERO); write(WIDTH_FULL);
                write(ALIGN_CENTER);
                write(BOLD_ON);
                line("*** COMANDA DE COCINA ***");
                write(SIZE_LARGE);
                line(string.Format("PEDIDO #{0:000}", order.TicketNumber));
                write(SIZE_NORMAL); write(BOLD_OFF);
                line(order.CreatedAt);
                line(sepEq);

                write(ALIGN_LEFT);
                foreach (var it in order.Items)
                {
                    write(BOLD_ON); write(SIZE_TALL);
                    line(string.Format("[{0}x] {1}", it.Quantity, it.ProductName.ToUpperInvariant()));
                    write(SIZE_NORMAL);
                    if (it.PackChoices != null && it.PackChoices.Count > 0)
                    {
                        foreach (var pc in it.PackChoices)
                            line(string.Format("   - {0}x {1}", pc.Quantity, pc.ProductName.ToUpperInvariant()));
                    }
                    if (!string.IsNullOrWhiteSpace(it.Note))
                        line("   >> OBS: " + it.Note.ToUpperInvariant());
                    write(BOLD_OFF);
                }

                if (!string.IsNullOrWhiteSpace(order.Notes))
                {
                    line(sepDash);
                    write(BOLD_ON);
                    line("NOTA: " + order.Notes.ToUpperInvariant());
                    write(BOLD_OFF);
                }
                line(sepEq);
                line("");
                write(CUT);

                string targetKitchenPrinter = onlyKitchen ? cajaPrinter : kitchenPrinter;
                SendRawBytes(targetKitchenPrinter, ms.ToArray());
            }
        }

        public static void PrintCashClosure(CashSession session, List<OrderRecord> sessionOrders, PosSettings settings)
        {
            int cols = settings.PaperWidthMm == 58 ? 32 : 48;
            string sepEq = new string('=', cols);
            string sepDash = new string('-', cols);

            var ms = new MemoryStream();
            Action<byte[]> write = b => ms.Write(b, 0, b.Length);
            Action<string> line = s =>
            {
                byte[] bytes = Encoding.ASCII.GetBytes(CleanAscii(s) + "\n");
                ms.Write(bytes, 0, bytes.Length);
            };

            byte[] INIT = { 0x1B, 0x40 };
            byte[] FONT_A = { 0x1B, 0x4D, 0x00 };
            byte[] MARGIN_ZERO = { 0x1D, 0x4C, 0x00, 0x00 };
            byte[] WIDTH_FULL = { 0x1D, 0x57, 0x40, 0x02 };
            byte[] ALIGN_LEFT = { 0x1B, 0x61, 0x00 };
            byte[] ALIGN_CENTER = { 0x1B, 0x61, 0x01 };
            byte[] BOLD_ON = { 0x1B, 0x45, 0x01 };
            byte[] BOLD_OFF = { 0x1B, 0x45, 0x00 };
            byte[] CUT = { 0x1D, 0x56, 0x41, 0x03 };

            write(INIT); write(FONT_A); write(MARGIN_ZERO); write(WIDTH_FULL);
            write(ALIGN_CENTER); write(BOLD_ON);
            line((session.EventName ?? settings.EventName).ToUpperInvariant());
            line("CIERRE Y ARQUEO DE CAJA (TICKET Z)");
            write(BOLD_OFF);
            line(sepEq);

            write(ALIGN_LEFT);
            line(TwoCols("CAJA NRO:", string.Format("#{0:000}", session.Id), cols));
            if (!string.IsNullOrWhiteSpace(session.MenuGroupName))
                line(TwoCols("TIPO EVENTO/MENU:", session.MenuGroupName, cols));
            line(TwoCols("APERTURA:", session.OpenedAt, cols));
            line(TwoCols("CIERRE:", string.IsNullOrEmpty(session.ClosedAt) ? DateTime.Now.ToString("dd/MM/yyyy HH:mm") : session.ClosedAt, cols));
            line(TwoCols("PEDIDOS COBRADOS:", session.OrdersCount.ToString(), cols));
            line(sepDash);
            line(TwoCols("VENTAS EFECTIVO:", PosStore.FormatMoney(session.CashSales), cols));
            line(TwoCols("TRANSFERENCIA:", PosStore.FormatMoney(session.MpSales), cols));
            if (settings.EnableCard || session.CardSales > 0)
                line(TwoCols("TARJETA:", PosStore.FormatMoney(session.CardSales), cols));
            line(sepDash);
            write(BOLD_ON);
            line(TwoCols("TOTAL RECAUDADO:", PosStore.FormatMoney(session.TotalSales), cols));
            write(BOLD_OFF);
            line(sepEq);
            line(TwoCols("FONDO INICIAL:", PosStore.FormatMoney(session.OpeningCash), cols));
            line(TwoCols("+ VENTAS EFECTIVO:", PosStore.FormatMoney(session.CashSales), cols));
            write(BOLD_ON);
            line(TwoCols("= EFECTIVO ESPERADO:", PosStore.FormatMoney(session.ExpectedCash), cols));
            write(BOLD_OFF);
            line(TwoCols("EFECTIVO CONTADO:", PosStore.FormatMoney(session.CountedCash), cols));
            decimal diff = session.CountedCash - session.ExpectedCash;
            line(TwoCols("DIFERENCIA:", (diff > 0 ? "+" : "") + PosStore.FormatMoney(diff), cols));

            var ranking = sessionOrders
                .Where(o => o.Status == "completado")
                .SelectMany(o => o.Items)
                .GroupBy(i => i.ProductName)
                .Select(g => new { Name = g.Key, Qty = g.Sum(x => x.Quantity), Amt = g.Sum(x => x.Subtotal) })
                .OrderByDescending(x => x.Qty)
                .ToList();

            if (ranking.Count > 0)
            {
                line(sepEq);
                write(BOLD_ON);
                line("PLATOS VENDIDOS:");
                write(BOLD_OFF);
                line(sepDash);
                foreach (var r in ranking)
                {
                    line(TwoCols(string.Format("{0}x {1}", r.Qty, r.Name), PosStore.FormatMoney(r.Amt), cols));
                }
            }

            line(sepEq);
            line("");
            write(CUT);

            string printer = string.IsNullOrWhiteSpace(settings.PrinterName) ? DetectPreferredPrinter() : settings.PrinterName;
            SendRawBytes(printer, ms.ToArray());
        }

        public static void PrintTestTicket(PosSettings settings)
        {
            int cols = settings.PaperWidthMm == 58 ? 32 : 48;
            var ms = new MemoryStream();
            Action<byte[]> write = b => ms.Write(b, 0, b.Length);
            Action<string> line = s =>
            {
                byte[] bytes = Encoding.ASCII.GetBytes(CleanAscii(s) + "\n");
                ms.Write(bytes, 0, bytes.Length);
            };

            write(new byte[] { 0x1B, 0x40, 0x1B, 0x4D, 0x00, 0x1D, 0x4C, 0x00, 0x00, 0x1D, 0x57, 0x40, 0x02 });
            byte[] logoBytes = BuildLogoEscPosRasterBytes(settings);
            if (logoBytes != null)
                write(logoBytes);
            write(new byte[] { 0x1B, 0x61, 0x01, 0x1B, 0x45, 0x01, 0x1D, 0x21, 0x11 });
            line("PRUEBA TICKETERA");
            write(new byte[] { 0x1D, 0x21, 0x00, 0x1B, 0x45, 0x00 });
            line(new string('=', cols));
            line(string.Format("ANCHO COMPLETO CONFIGURADO ({0} COLUMNAS)", cols));
            line("123456789012345678901234567890123456789012345678".Substring(0, cols));
            line(new string('=', cols));
            line("");
            write(new byte[] { 0x1D, 0x56, 0x41, 0x03 });

            string printer = string.IsNullOrWhiteSpace(settings.PrinterName) ? DetectPreferredPrinter() : settings.PrinterName;
            SendRawBytes(printer, ms.ToArray());
        }

        public static void SendRawBytes(string printerName, byte[] bytes)
        {
            IntPtr hPrinter;
            if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
                throw new Exception("No se pudo abrir la ticketera '" + printerName + "'. Verificá que esté encendida y conectada.");

            try
            {
                var di = new DOCINFOA { pDocName = "Ticket POS Evento", pDataType = "RAW" };
                if (StartDocPrinter(hPrinter, 1, di) == 0)
                    throw new Exception("No se pudo iniciar cola de impresión RAW en '" + printerName + "'.");

                try
                {
                    StartPagePrinter(hPrinter);
                    IntPtr pUnmanagedBytes = Marshal.AllocCoTaskMem(bytes.Length);
                    Marshal.Copy(bytes, 0, pUnmanagedBytes, bytes.Length);
                    try
                    {
                        int written;
                        if (!WritePrinter(hPrinter, pUnmanagedBytes, bytes.Length, out written))
                            throw new Exception("Error al enviar bytes ESC/POS a la ticketera.");
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(pUnmanagedBytes);
                    }
                    EndPagePrinter(hPrinter);
                }
                finally
                {
                    EndDocPrinter(hPrinter);
                }
            }
            finally
            {
                ClosePrinter(hPrinter);
            }
        }
    }

    // =========================================================================
    // AUTO-ACTUALIZADOR DIRECTO DESDE GITHUB (ESTILO PROCESADORFICHADAS)
    // Verifica version.json en main de Vanessaalberti/sistema-eventos al abrir.
    // Si hay nueva versión, descarga el .exe (~120 KB), lo reemplaza y reinicia.
    // =========================================================================
    public class RemoteVersionInfo
    {
        public string version { get; set; }
        public string notes { get; set; }
    }

    public static class GitHubUpdater
    {
        public const string AppVersion = "1.0.5";
        public const string GitHubRepo = "Vanessaalberti/sistema-eventos";

        private static Version ParseVersionSafe(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new Version(0, 0, 0);
            string cleaned = raw.Trim().TrimStart('v', 'V');
            string[] parts = cleaned.Split('.');
            int maj = 0, min = 0, build = 0;
            if (parts.Length > 0) int.TryParse(parts[0], out maj);
            if (parts.Length > 1) int.TryParse(parts[1], out min);
            if (parts.Length > 2) int.TryParse(parts[2], out build);
            return new Version(maj, min, build);
        }

        public static void CheckForUpdatesAsync(Window ownerWindow, string exeFileName, bool notifyIfUpToDate)
        {
            var th = new Thread(() =>
            {
                try
                {
                    // Habilitar TLS 1.2 para conexiones HTTPS con GitHub en .NET Framework 4
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | SecurityProtocolType.Tls12;
                    long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    string rawBase = "https://raw.githubusercontent.com/" + GitHubRepo + "/main";
                    string versionUrl = rawBase + "/version.json?t=" + ts;

                    string jsonText = DownloadStringWithTimeout(versionUrl, 5000);
                    if (string.IsNullOrWhiteSpace(jsonText))
                    {
                        if (notifyIfUpToDate)
                            ownerWindow.Dispatcher.BeginInvoke(new Action(() =>
                                MessageBox.Show(ownerWindow, "No se pudo consultar GitHub (verificá tu conexión a internet).", "Actualizaciones", MessageBoxButton.OK, MessageBoxImage.Information)));
                        return;
                    }

                    var ser = new JavaScriptSerializer();
                    var remote = ser.Deserialize<RemoteVersionInfo>(jsonText);
                    if (remote == null || string.IsNullOrWhiteSpace(remote.version))
                        return;

                    Version vLocal = ParseVersionSafe(AppVersion);
                    Version vRemote = ParseVersionSafe(remote.version);

                    if (vRemote <= vLocal)
                    {
                        if (notifyIfUpToDate)
                            ownerWindow.Dispatcher.BeginInvoke(new Action(() =>
                                MessageBox.Show(ownerWindow,
                                    "Ya tenés la última versión instalada (v" + AppVersion + ").",
                                    "Sistema Actualizado",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Information)));
                        return;
                    }

                    string downloadUrl = rawBase + "/" + exeFileName + "?t=" + ts;
                    string notesText = string.IsNullOrWhiteSpace(remote.notes) ? "Mejoras y correcciones generales." : remote.notes;

                    ownerWindow.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var res = MessageBox.Show(
                            ownerWindow,
                            string.Format(
                                "Hay una nueva actualización disponible en GitHub (v{0}).\n" +
                                "Versión actual: v{1}\n\n" +
                                "Novedades:\n• {2}\n\n" +
                                "La actualización es instantánea (~120 KB) y no borra tus datos ni tus cajas.\n" +
                                "¿Querés actualizar y reiniciar el programa ahora?",
                                remote.version, AppVersion, notesText),
                            "Actualización Disponible",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Information);

                        if (res == MessageBoxResult.Yes)
                        {
                            DownloadAndSwapExeAsync(ownerWindow, downloadUrl);
                        }
                    }));
                }
                catch
                {
                    if (notifyIfUpToDate)
                    {
                        try
                        {
                            ownerWindow.Dispatcher.BeginInvoke(new Action(() =>
                                MessageBox.Show(ownerWindow, "No se pudo conectar con GitHub en este momento (modo sin internet).", "Actualizaciones", MessageBoxButton.OK, MessageBoxImage.Information)));
                        }
                        catch { }
                    }
                }
            });
            th.IsBackground = true;
            th.Start();
        }

        private static string DownloadStringWithTimeout(string url, int timeoutMs)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.UserAgent = "EventosPOS-Updater/1.0";
            req.Headers.Add("Cache-Control", "no-cache");
            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                if (resp.StatusCode != HttpStatusCode.OK) return null;
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return sr.ReadToEnd();
            }
        }

        private static void DownloadAndSwapExeAsync(Window ownerWindow, string downloadUrl)
        {
            var th = new Thread(() =>
            {
                try
                {
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | SecurityProtocolType.Tls12;
                    string currentExe = Assembly.GetEntryAssembly().Location;
                    string exeDir = Path.GetDirectoryName(currentExe);
                    string tempExe = Path.Combine(exeDir, "update_temp.exe");
                    string updaterBat = Path.Combine(exeDir, "updater_pos.bat");

                    var req = (HttpWebRequest)WebRequest.Create(downloadUrl);
                    req.Timeout = 15000;
                    req.UserAgent = "EventosPOS-Updater/1.0";
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var netStream = resp.GetResponseStream())
                    using (var fs = new FileStream(tempExe, FileMode.Create, FileAccess.Write))
                    {
                        netStream.CopyTo(fs);
                    }

                    // Validar cabecera PE ('MZ') y tamaño mínimo para seguridad
                    byte[] header = new byte[2];
                    using (var fs = File.OpenRead(tempExe))
                    {
                        if (fs.Length < 20000 || fs.Read(header, 0, 2) < 2 || header[0] != 0x4D || header[1] != 0x5A)
                            throw new Exception("El archivo descargado no es un ejecutable válido.");
                    }

                    // Script temporal oculto que reemplaza el .exe y lo vuelve a abrir automáticamente
                    string batContent =
                        "@echo off\r\n" +
                        "timeout /t 2 /nobreak > NUL\r\n" +
                        "move /y \"" + tempExe + "\" \"" + currentExe + "\" > NUL\r\n" +
                        "start \"\" \"" + currentExe + "\"\r\n" +
                        "del \"%~f0\"\r\n";

                    File.WriteAllText(updaterBat, batContent, Encoding.Default);

                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c \"" + updaterBat + "\"",
                        WorkingDirectory = exeDir,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    Process.Start(psi);

                    ownerWindow.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        Application.Current.Shutdown();
                    }));
                }
                catch (Exception ex)
                {
                    ownerWindow.Dispatcher.BeginInvoke(new Action(() =>
                        MessageBox.Show(ownerWindow, "No se pudo aplicar la actualización:\n" + ex.Message, "Error al actualizar", MessageBoxButton.OK, MessageBoxImage.Error)));
                }
            });
            th.IsBackground = true;
            th.Start();
        }
    }
}
