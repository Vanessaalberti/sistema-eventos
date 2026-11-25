using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace EventosPos
{
    public class MenuGroup
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
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
        public string KitchenStatus { get; set; } // "pendiente", "preparando", "finalizado"
        public bool KitchenPrinted { get; set; }
        public string CreatedAt { get; set; }
        public string FinishedAt { get; set; }
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
        public bool PrintDoubleTicket { get; set; }
        public string PrinterName { get; set; }
        public string TicketFooter { get; set; }

        public PosSettings()
        {
            EventName = "Asociación Juvenil Araucana";
            ActiveMenuGroupId = 1;
            MpAlias = "araucana24";
            NextTicketSequence = 1;
            PaperWidthMm = 80;
            PrintDoubleTicket = true;
            PrinterName = "Xprinter XP-80";
            TicketFooter = "Mercado Pago Alias: araucana24 - ¡Gracias por colaborar!";
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
                            if (payload.MenuGroups == null || payload.MenuGroups.Count == 0)
                                payload.MenuGroups = GetDefaultMenuGroups();
                            if (payload.Products == null || payload.Products.Count == 0)
                                payload.Products = GetDefaultProducts();

                            foreach (var p in payload.Products)
                            {
                                if (p.GroupId <= 0) p.GroupId = 1;
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
                PrintDoubleTicket = true,
                PrinterName = EscPosPrinter.DetectPreferredPrinter(),
                TicketFooter = "Mercado Pago Alias: araucana24 - ¡Gracias por colaborar!"
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
                new ProductItem { Id = 3, GroupId = 1, Name = "Empanada de Carne", Category = "Principales", Price = 1500, StockEnabled = false, StockQty = 0, Description = "Unidad al horno", ImagePath = "emp_carne.png", Active = true },
                new ProductItem { Id = 4, GroupId = 1, Name = "Empanada Jamón y Queso", Category = "Principales", Price = 1500, StockEnabled = false, StockQty = 0, Description = "Unidad al horno", ImagePath = "emp_jyq.png", Active = true },
                new ProductItem { Id = 5, GroupId = 1, Name = "Empanada de Pollo", Category = "Principales", Price = 1500, StockEnabled = false, StockQty = 0, Description = "Unidad al horno", ImagePath = "emp_pollo.png", Active = true },
                new ProductItem { Id = 6, GroupId = 1, Name = "Empanada de Roquefort", Category = "Principales", Price = 1500, StockEnabled = false, StockQty = 0, Description = "Unidad al horno", ImagePath = "emp_roquefort.png", Active = true },
                new ProductItem { Id = 7, GroupId = 1, Name = "Empanada Caprese", Category = "Principales", Price = 1500, StockEnabled = false, StockQty = 0, Description = "Unidad al horno", ImagePath = "emp_caprese.png", Active = true },
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
                new ProductItem { Id = 18, GroupId = 1, Name = "Docena de Empanadas", Category = "Para Llevar", Price = 15000, StockEnabled = false, StockQty = 0, IsCombo = true, Description = "12 unidades a elección", ImagePath = "docena_emp.png", Active = true },
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
            session.MpSales = validOrders.Where(o => o.PaymentMethod == "Mercado Pago").Sum(o => o.Total);
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
                                        ord.FinishedAt = newStatus == "finalizado" ? DateTime.Now.ToString("HH:mm:ss") : "";
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
            }

            sb.AppendLine(sepDash);
            sb.AppendLine(TwoCols("TOTAL:", PosStore.FormatMoney(order.Total), cols));
            sb.AppendLine(TwoCols("MEDIO DE PAGO:", order.PaymentMethod.ToUpperInvariant(), cols));

            if (order.PaymentMethod == "Mercado Pago")
            {
                sb.AppendLine(TwoCols("ALIAS MP:", settings.MpAlias, cols));
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

            if (!onlyKitchen)
            {
                write(ALIGN_CENTER);
                write(BOLD_ON); write(SIZE_TALL);
                line((order.EventName ?? settings.EventName).ToUpperInvariant());
                write(SIZE_NORMAL);
                line("COMPROBANTE DE CLIENTE");
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
                }

                line(sepDash);
                write(BOLD_ON); write(SIZE_TALL);
                line(TwoCols("TOTAL:", PosStore.FormatMoney(order.Total), cols));
                write(SIZE_NORMAL); write(BOLD_OFF);

                line(TwoCols("MEDIO DE PAGO:", order.PaymentMethod.ToUpperInvariant(), cols));
                if (order.PaymentMethod == "Mercado Pago")
                {
                    line(TwoCols("ALIAS MP:", settings.MpAlias, cols));
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
                    write(SIZE_NORMAL); write(BOLD_OFF);
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
            }

            string printer = string.IsNullOrWhiteSpace(settings.PrinterName) ? DetectPreferredPrinter() : settings.PrinterName;
            SendRawBytes(printer, ms.ToArray());
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
            line(TwoCols("MERCADO PAGO:", PosStore.FormatMoney(session.MpSales), cols));
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
}
