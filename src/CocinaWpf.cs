using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace EventosPos
{
    public static class KTheme
    {
        public static readonly SolidColorBrush Bg = BrushFromHex("#F6F7F9");
        public static readonly SolidColorBrush Card = BrushFromHex("#FFFFFF");
        public static readonly SolidColorBrush Border = BrushFromHex("#EAECF0");
        public static readonly SolidColorBrush BorderStrong = BrushFromHex("#D0D5DD");
        public static readonly SolidColorBrush Hover = BrushFromHex("#F3F4F6");
        public static readonly SolidColorBrush Text = BrushFromHex("#111827");
        public static readonly SolidColorBrush Muted = BrushFromHex("#6B7280");
        public static readonly SolidColorBrush Brand = BrushFromHex("#3B76F6");
        public static readonly SolidColorBrush BrandDark = BrushFromHex("#2563EB");
        public static readonly SolidColorBrush BrandBg = BrushFromHex("#EFF6FF");
        public static readonly SolidColorBrush Ok = BrushFromHex("#16A34A");
        public static readonly SolidColorBrush OkBg = BrushFromHex("#DCFCE7");
        public static readonly SolidColorBrush Warn = BrushFromHex("#CA8A04");
        public static readonly SolidColorBrush WarnBg = BrushFromHex("#FEF9C3");
        public static readonly SolidColorBrush Err = BrushFromHex("#DC2626");
        public static readonly SolidColorBrush ErrBg = BrushFromHex("#FEE2E2");

        public static readonly CornerRadius BtnRadius = new CornerRadius(7);
        public static readonly CornerRadius CardRadius = new CornerRadius(10);

        public static SolidColorBrush BrushFromHex(string hex)
        {
            var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
            b.Freeze();
            return b;
        }

        public static DropShadowEffect CardShadow()
        {
            return new DropShadowEffect
            {
                Color = Colors.Black,
                Direction = 270,
                ShadowDepth = 2,
                BlurRadius = 8,
                Opacity = 0.04
            };
        }
    }

    public class CocinaMainWindow : Window
    {
        private PosDatabasePayload _db;
        private readonly HashSet<int> _knownOrderIds = new HashSet<int>();
        private bool _firstLoadDone = false;
        private string _lastRenderHash = "";
        private string _activeTab = "active";

        // Estado de conexión de Red entre PCs
        private volatile string _discoveredCajaIp = "";
        private string _manualCajaIp = "";
        private string _activeCajaIp = ""; // IP remota conectada actualmente (vacío si lee local en la misma PC)
        private string _localKitchenPrinter = "";
        private string _currentConnLabel = "Buscando PC de Caja...";
        private bool _isConnectedToCaja = false;
        private bool _autoPrintEnabled = true;
        private bool _isListeningUdp = true;
        private readonly string _ipConfigPath = Path.Combine(PosStore.DataDir, "cocina_ip.txt");
        private readonly string _printerConfigPath = Path.Combine(PosStore.DataDir, "cocina_printer.txt");
        private readonly string _autoPrintConfigPath = Path.Combine(PosStore.DataDir, "cocina_autoprint.txt");

        private TextBlock _headerSubtitleText;
        private TextBlock _pendingCountText;
        private TextBlock _prepCountText;
        private TextBlock _doneCountText;
        private WrapPanel _activeWrap;
        private StackPanel _finishedStack;
        private ScrollViewer _activeScroll;
        private ScrollViewer _finishedScroll;
        private Border _btnTabActive;
        private Border _btnTabFinished;

        private DispatcherTimer _pollTimer;

        public CocinaMainWindow()
        {
            Directory.CreateDirectory(PosStore.DataDir);
            if (File.Exists(_ipConfigPath))
            {
                _manualCajaIp = File.ReadAllText(_ipConfigPath, Encoding.UTF8).Trim();
            }
            if (File.Exists(_printerConfigPath))
            {
                _localKitchenPrinter = File.ReadAllText(_printerConfigPath, Encoding.UTF8).Trim();
            }
            if (string.IsNullOrWhiteSpace(_localKitchenPrinter))
            {
                _localKitchenPrinter = EscPosPrinter.DetectPreferredPrinter();
            }
            if (File.Exists(_autoPrintConfigPath))
            {
                string ap = File.ReadAllText(_autoPrintConfigPath, Encoding.UTF8).Trim();
                _autoPrintEnabled = ap != "0";
            }

            _db = new PosDatabasePayload();

            Title = "COCINA POS — Monitor de Comandas en Tiempo Real (Portable)";
            Width = 1340;
            Height = 820;
            MinWidth = 1020;
            MinHeight = 660;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = KTheme.Bg;
            FontFamily = new FontFamily("Segoe UI");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            Closed += (s, e) => _isListeningUdp = false;

            StartUdpAutoDiscoveryListener();
            BuildLayout();
            PollOrders();

            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            _pollTimer.Tick += (s, e) => PollOrders();
            _pollTimer.Start();

            // Verificar automáticamente actualizaciones en GitHub al abrir (en segundo plano)
            Loaded += (s, e) => GitHubUpdater.CheckForUpdatesAsync(this, "Cocina_POS.exe", false);
        }

        private void StartUdpAutoDiscoveryListener()
        {
            var th = new Thread(() =>
            {
                try
                {
                    using (var udp = new UdpClient(PosNetworkSync.UdpDiscoveryPort))
                    {
                        var ep = new IPEndPoint(IPAddress.Any, 0);
                        while (_isListeningUdp)
                        {
                            try
                            {
                                byte[] data = udp.Receive(ref ep);
                                string msg = Encoding.UTF8.GetString(data);
                                if (msg.StartsWith("ARAUCANA_CAJA_IP|"))
                                {
                                    string[] parts = msg.Split('|');
                                    if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]))
                                    {
                                        _discoveredCajaIp = parts[1].Trim();
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            });
            th.IsBackground = true;
            th.Start();
        }

        private void BuildLayout()
        {
            var rootDock = new DockPanel { LastChildFill = true };

            var topBar = new Border
            {
                Background = KTheme.Card,
                BorderBrush = KTheme.Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(22, 12, 22, 12)
            };
            DockPanel.SetDock(topBar, Dock.Top);

            var topGrid = new Grid();
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var leftStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var logoBadge = new Border
            {
                Background = KTheme.WarnBg,
                BorderBrush = KTheme.Warn,
                BorderThickness = new Thickness(1),
                CornerRadius = KTheme.BtnRadius,
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 12, 0)
            };
            logoBadge.Child = LucideIcons.IconText("chef-hat", "COCINA", 16, KTheme.Warn, 13, FontWeights.Bold);
            leftStack.Children.Add(logoBadge);

            var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) };
            titleStack.Children.Add(new TextBlock
            {
                Text = "Monitor de Cocina en Tiempo Real",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = KTheme.Text
            });
            _headerSubtitleText = new TextBlock
            {
                Text = "Sincronizado por Red Local / Wi-Fi con Caja POS",
                FontSize = 11,
                Foreground = KTheme.Muted
            };
            titleStack.Children.Add(_headerSubtitleText);
            leftStack.Children.Add(titleStack);

            _btnTabActive = CreateTabButton("En Preparación", "flame", () => SwitchTab("active"));
            _btnTabActive.Margin = new Thickness(0, 0, 6, 0);
            _btnTabFinished = CreateTabButton("Pedidos Listos", "check", () => SwitchTab("finished"));
            leftStack.Children.Add(_btnTabActive);
            leftStack.Children.Add(_btnTabFinished);

            Grid.SetColumn(leftStack, 0);
            topGrid.Children.Add(leftStack);

            var rightStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var btnSettings = CreateSecondaryButton("Configuración", () => OpenKitchenSettingsModal(), "settings");
            btnSettings.Padding = new Thickness(12, 6, 12, 6);
            btnSettings.Margin = new Thickness(0, 0, 12, 0);
            rightStack.Children.Add(btnSettings);

            var pillPend = new Border { Background = KTheme.WarnBg, BorderBrush = KTheme.Warn, BorderThickness = new Thickness(1), CornerRadius = KTheme.BtnRadius, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 6, 0) };
            _pendingCountText = new TextBlock { Text = "Pendientes: 0", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = KTheme.Warn };
            pillPend.Child = _pendingCountText;
            rightStack.Children.Add(pillPend);

            var pillPrep = new Border { Background = KTheme.BrandBg, BorderBrush = KTheme.Brand, BorderThickness = new Thickness(1), CornerRadius = KTheme.BtnRadius, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 6, 0) };
            _prepCountText = new TextBlock { Text = "Preparando: 0", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = KTheme.BrandDark };
            pillPrep.Child = _prepCountText;
            rightStack.Children.Add(pillPrep);

            var pillDone = new Border { Background = KTheme.OkBg, BorderBrush = KTheme.Ok, BorderThickness = new Thickness(1), CornerRadius = KTheme.BtnRadius, Padding = new Thickness(10, 6, 10, 6) };
            _doneCountText = new TextBlock { Text = "Listos: 0", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = KTheme.Ok };
            pillDone.Child = _doneCountText;
            rightStack.Children.Add(pillDone);

            Grid.SetColumn(rightStack, 1);
            topGrid.Children.Add(rightStack);
            topBar.Child = topGrid;
            rootDock.Children.Add(topBar);

            var hostGrid = new Grid { Margin = new Thickness(22, 16, 22, 18) };

            _activeScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            _activeWrap = new WrapPanel { Orientation = Orientation.Horizontal };
            _activeScroll.Content = _activeWrap;

            _finishedScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Visibility = Visibility.Collapsed
            };
            _finishedStack = new StackPanel();
            _finishedScroll.Content = _finishedStack;

            hostGrid.Children.Add(_activeScroll);
            hostGrid.Children.Add(_finishedScroll);
            rootDock.Children.Add(hostGrid);

            Content = rootDock;
            SwitchTab("active");
        }

        private void SwitchTab(string tab)
        {
            _activeTab = tab;
            _activeScroll.Visibility = tab == "active" ? Visibility.Visible : Visibility.Collapsed;
            _finishedScroll.Visibility = tab == "finished" ? Visibility.Visible : Visibility.Collapsed;

            _btnTabActive.Background = tab == "active" ? KTheme.BrandBg : KTheme.Bg;
            _btnTabActive.BorderBrush = tab == "active" ? KTheme.Brand : KTheme.Border;
            _btnTabActive.Child = LucideIcons.IconText("flame", "En Preparación", 14, tab == "active" ? KTheme.BrandDark : KTheme.Muted, 13, FontWeights.Bold);

            _btnTabFinished.Background = tab == "finished" ? KTheme.BrandBg : KTheme.Bg;
            _btnTabFinished.BorderBrush = tab == "finished" ? KTheme.Brand : KTheme.Border;
            _btnTabFinished.Child = LucideIcons.IconText("check", "Pedidos Listos", 14, tab == "finished" ? KTheme.BrandDark : KTheme.Muted, 13, FontWeights.Bold);
        }

        private bool TryLoadStateFromCaja(out PosDatabasePayload loadedDb, out string connectionLabel, out bool isRemote)
        {
            loadedDb = null;
            connectionLabel = "";
            isRemote = false;

            // 1. Si el usuario configuró una IP manual de otra PC, probar esa primero
            if (!string.IsNullOrWhiteSpace(_manualCajaIp))
            {
                try
                {
                    loadedDb = PosNetworkSync.FetchStateFromCajaIp(_manualCajaIp);
                    if (loadedDb != null)
                    {
                        _activeCajaIp = _manualCajaIp;
                        connectionLabel = "Caja Red (" + _manualCajaIp + ")";
                        isRemote = true;
                        return true;
                    }
                }
                catch { }
            }

            // 2. Si se auto-descubrió una PC de Caja por Wi-Fi/UDP, conectarse a ella
            if (!string.IsNullOrWhiteSpace(_discoveredCajaIp))
            {
                try
                {
                    loadedDb = PosNetworkSync.FetchStateFromCajaIp(_discoveredCajaIp);
                    if (loadedDb != null)
                    {
                        _activeCajaIp = _discoveredCajaIp;
                        connectionLabel = "Caja Wi-Fi (" + _discoveredCajaIp + ")";
                        isRemote = true;
                        return true;
                    }
                }
                catch { }
            }

            // 3. Probar localhost TCP (si Caja_POS.exe está abierto en esta misma PC)
            try
            {
                loadedDb = PosNetworkSync.FetchStateFromCajaIp("127.0.0.1");
                if (loadedDb != null)
                {
                    _activeCajaIp = "127.0.0.1";
                    connectionLabel = "Caja Conectada (Esta PC)";
                    isRemote = true;
                    return true;
                }
            }
            catch { }

            // 4. Si comparten carpeta local en esta PC (aunque Caja_POS.exe esté cerrado)
            if (File.Exists(PosStore.DbFilePath))
            {
                loadedDb = PosStore.Load();
                _activeCajaIp = "";
                connectionLabel = "Modo Local";
                isRemote = false;
                return true;
            }

            return false;
        }

        private void PollOrders()
        {
            try
            {
                PosDatabasePayload newDb;
                string connLabel;
                bool isRemote;

                if (!TryLoadStateFromCaja(out newDb, out connLabel, out isRemote))
                {
                    _isConnectedToCaja = false;
                    _currentConnLabel = "Buscando PC de Caja en la red...";
                    if (_headerSubtitleText != null)
                    {
                        _headerSubtitleText.Text = "● Buscando PC de Caja en la red...";
                        _headerSubtitleText.Foreground = KTheme.Warn;
                    }
                    return;
                }

                _db = newDb;
                _isConnectedToCaja = true;
                _currentConnLabel = connLabel;
                if (_headerSubtitleText != null)
                {
                    _headerSubtitleText.Text = "● " + connLabel + (_autoPrintEnabled ? " · Auto-impresión activa" : "");
                    _headerSubtitleText.Foreground = KTheme.Muted;
                }

                bool separateBar = _db.Settings != null && _db.Settings.EnableBarSeparation;
                var activeOrders = _db.Orders
                    .Where(o => o.Status == "completado"
                                && (o.KitchenStatus == "pendiente" || o.KitchenStatus == "preparando")
                                && (!separateBar || GetKitchenItems(o).Count > 0))
                    .OrderBy(o => o.Id)
                    .ToList();

                var finishedOrders = _db.Orders
                    .Where(o => o.Status == "completado"
                                && (o.KitchenStatus == "finalizado" || o.KitchenStatus == "entregado")
                                && (!separateBar || GetKitchenItems(o).Count > 0))
                    .OrderByDescending(o => o.Id)
                    .Take(30)
                    .ToList();

                bool localChanged = false;
                foreach (var ord in activeOrders)
                {
                    if (!_knownOrderIds.Contains(ord.Id))
                    {
                        _knownOrderIds.Add(ord.Id);
                        if (_firstLoadDone)
                        {
                            try { SystemSounds.Exclamation.Play(); } catch { }
                        }
                        if (!ord.KitchenPrinted)
                        {
                            ord.KitchenPrinted = true;
                            if (isRemote && !string.IsNullOrEmpty(_activeCajaIp))
                                PosNetworkSync.SendMarkPrintedToCajaIp(_activeCajaIp, ord.Id);
                            else
                                localChanged = true;

                            if (_autoPrintEnabled)
                            {
                                try { PrintComandaWithKitchenPrinter(ord); } catch { }
                            }
                        }
                    }
                }
                if (localChanged && !isRemote) PosStore.Save(_db);
                _firstLoadDone = true;

                int pendCount = activeOrders.Count(o => o.KitchenStatus == "pendiente");
                int prepCount = activeOrders.Count(o => o.KitchenStatus == "preparando");
                int doneTotal = _db.Orders.Count(o => o.Status == "completado"
                    && (o.KitchenStatus == "finalizado" || o.KitchenStatus == "entregado")
                    && (!separateBar || GetKitchenItems(o).Count > 0));

                _pendingCountText.Text = "Pendientes: " + pendCount;
                _prepCountText.Text = "Preparando: " + prepCount;
                _doneCountText.Text = "Listos: " + doneTotal;

                string hash = (separateBar ? "SEP|" : "ALL|")
                              + string.Join("|", activeOrders.Select(o => o.Id + ":" + o.KitchenStatus))
                              + "##" + string.Join("|", finishedOrders.Select(o => o.Id));
                if (hash != _lastRenderHash)
                {
                    _lastRenderHash = hash;
                    RenderActiveCards(activeOrders);
                    RenderFinishedList(finishedOrders);
                }
            }
            catch { }
        }

        private List<OrderLineItem> GetKitchenItems(OrderRecord order)
        {
            if (order == null || order.Items == null) return new List<OrderLineItem>();
            if (_db != null && _db.Settings != null && _db.Settings.EnableBarSeparation)
                return order.Items.Where(i => !string.Equals(i.PrepStation, "Barra", StringComparison.OrdinalIgnoreCase)).ToList();
            return order.Items;
        }

        private void PrintComandaWithKitchenPrinter(OrderRecord order)
        {
            var printSettings = new PosSettings
            {
                EventName = _db.Settings != null ? _db.Settings.EventName : "Cocina",
                PaperWidthMm = _db.Settings != null ? _db.Settings.PaperWidthMm : 80,
                PrinterName = string.IsNullOrWhiteSpace(_localKitchenPrinter) ? EscPosPrinter.DetectPreferredPrinter() : _localKitchenPrinter,
                PrintDoubleTicket = false,
                EnableBarSeparation = _db.Settings != null && _db.Settings.EnableBarSeparation
            };
            EscPosPrinter.PrintOrder(order, printSettings, true);
        }

        private void OpenKitchenSettingsModal()
        {
            var win = new Window
            {
                Title = "Configuración de Cocina",
                Width = 490,
                Height = 510,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = KTheme.Card,
                ResizeMode = ResizeMode.NoResize,
                FontFamily = new FontFamily("Segoe UI")
            };

            var stack = new StackPanel { Margin = new Thickness(22) };
            stack.Children.Add(new TextBlock
            {
                Text = "Configuración de Cocina",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = KTheme.Text,
                Margin = new Thickness(0, 0, 0, 14)
            });

            // 1. Bloque de Red Local / Conexión con Caja
            var netCard = new Border
            {
                Background = KTheme.Bg,
                BorderBrush = KTheme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = KTheme.CardRadius,
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 14)
            };
            var netStack = new StackPanel();
            netStack.Children.Add(new TextBlock
            {
                Text = "Conexión con PC de Caja (Red Local / Wi-Fi)",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = KTheme.Text,
                Margin = new Thickness(0, 0, 0, 6)
            });

            var statusPill = new Border
            {
                Background = _isConnectedToCaja ? KTheme.OkBg : KTheme.WarnBg,
                BorderBrush = _isConnectedToCaja ? KTheme.Ok : KTheme.Warn,
                BorderThickness = new Thickness(1),
                CornerRadius = KTheme.BtnRadius,
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            statusPill.Child = new TextBlock
            {
                Text = _currentConnLabel,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = _isConnectedToCaja ? KTheme.Ok : KTheme.Warn
            };
            netStack.Children.Add(statusPill);

            netStack.Children.Add(new TextBlock
            {
                Text = "IP de la PC de Caja (dejá vacío para detección automática por Wi-Fi):",
                FontSize = 11,
                Foreground = KTheme.Muted,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var ipRow = new DockPanel();
            var ipBox = new TextBox
            {
                Padding = new Thickness(10, 6, 10, 6),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Background = KTheme.Card,
                BorderBrush = KTheme.BorderStrong,
                BorderThickness = new Thickness(1),
                Text = string.IsNullOrWhiteSpace(_manualCajaIp) ? _discoveredCajaIp : _manualCajaIp
            };
            var btnClearIp = CreateSecondaryButton("Detectar Auto", () =>
            {
                ipBox.Text = "";
                _manualCajaIp = "";
                if (File.Exists(_ipConfigPath)) File.Delete(_ipConfigPath);
            });
            btnClearIp.Padding = new Thickness(10, 6, 10, 6);
            btnClearIp.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(btnClearIp, Dock.Right);
            ipRow.Children.Add(btnClearIp);
            ipRow.Children.Add(ipBox);
            netStack.Children.Add(ipRow);
            netCard.Child = netStack;
            stack.Children.Add(netCard);

            // 2. Bloque de Impresora y Auto-impresión
            var prCard = new Border
            {
                Background = KTheme.Bg,
                BorderBrush = KTheme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = KTheme.CardRadius,
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 16)
            };
            var prStack = new StackPanel();
            prStack.Children.Add(new TextBlock
            {
                Text = "Impresora de Comandas",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = KTheme.Text,
                Margin = new Thickness(0, 0, 0, 6)
            });

            var printerCombo = new ComboBox { Padding = new Thickness(10, 7, 10, 7), FontSize = 13, Margin = new Thickness(0, 0, 0, 12) };
            foreach (var p in EscPosPrinter.ListInstalledPrinters())
                printerCombo.Items.Add(p);

            printerCombo.SelectedItem = _localKitchenPrinter;
            if (printerCombo.SelectedItem == null && printerCombo.Items.Count > 0)
                printerCombo.SelectedIndex = 0;
            prStack.Children.Add(printerCombo);

            var chkAutoPrint = new CheckBox
            {
                Content = "Imprimir comanda automáticamente al recibir cada pedido",
                IsChecked = _autoPrintEnabled,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = KTheme.Text
            };
            prStack.Children.Add(chkAutoPrint);
            prCard.Child = prStack;
            stack.Children.Add(prCard);

            // Botones finales
            var btnsRow = new UniformGrid { Columns = 2 };
            var btnTest = CreateSecondaryButton("Imprimir Prueba", () =>
            {
                if (printerCombo.SelectedItem != null)
                {
                    _localKitchenPrinter = printerCombo.SelectedItem.ToString();
                    File.WriteAllText(_printerConfigPath, _localKitchenPrinter, Encoding.UTF8);
                }
                try
                {
                    var testSettings = new PosSettings { PrinterName = _localKitchenPrinter, PaperWidthMm = 80, PrintTicketLogo = false };
                    EscPosPrinter.PrintTestTicket(testSettings);
                    MessageBox.Show(win, "Prueba enviada a " + _localKitchenPrinter, "Ticketera de Cocina", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(win, ex.Message, "Error en Ticketera", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
            btnTest.Margin = new Thickness(0, 0, 8, 0);
            btnsRow.Children.Add(btnTest);

            var btnSave = CreateSuccessButton("Guardar Configuración", () =>
            {
                _manualCajaIp = ipBox.Text.Trim();
                if (string.IsNullOrEmpty(_manualCajaIp))
                {
                    if (File.Exists(_ipConfigPath)) File.Delete(_ipConfigPath);
                }
                else
                {
                    File.WriteAllText(_ipConfigPath, _manualCajaIp, Encoding.UTF8);
                }

                if (printerCombo.SelectedItem != null)
                {
                    _localKitchenPrinter = printerCombo.SelectedItem.ToString();
                    File.WriteAllText(_printerConfigPath, _localKitchenPrinter, Encoding.UTF8);
                }

                _autoPrintEnabled = chkAutoPrint.IsChecked == true;
                File.WriteAllText(_autoPrintConfigPath, _autoPrintEnabled ? "1" : "0", Encoding.UTF8);

                _lastRenderHash = "";
                PollOrders();
                win.Close();
            });
            btnsRow.Children.Add(btnSave);
            stack.Children.Add(btnsRow);

            win.Content = stack;
            win.ShowDialog();
        }

        private void RenderActiveCards(List<OrderRecord> activeOrders)
        {
            _activeWrap.Children.Clear();

            if (activeOrders.Count == 0)
            {
                var emptyCard = new Border
                {
                    Width = 540,
                    Background = KTheme.Card,
                    BorderBrush = KTheme.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = KTheme.CardRadius,
                    Padding = new Thickness(32),
                    Margin = new Thickness(10),
                    Effect = KTheme.CardShadow()
                };
                var st = new StackPanel();
                st.Children.Add(new TextBlock
                {
                    Text = "No hay pedidos pendientes en Cocina",
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    Foreground = KTheme.Text
                });
                st.Children.Add(new TextBlock
                {
                    Text = "Cuando la PC de Caja cobre un nuevo pedido, aparecerá aquí instantáneamente con alerta sonora.",
                    FontSize = 13,
                    Foreground = KTheme.Muted,
                    Margin = new Thickness(0, 6, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
                emptyCard.Child = st;
                _activeWrap.Children.Add(emptyCard);
                return;
            }

            foreach (var ord in activeOrders)
            {
                bool isPrep = ord.KitchenStatus == "preparando";

                var card = new Border
                {
                    Width = 380,
                    Background = KTheme.Card,
                    BorderBrush = isPrep ? KTheme.Brand : KTheme.Warn,
                    BorderThickness = new Thickness(2),
                    CornerRadius = KTheme.CardRadius,
                    Margin = new Thickness(0, 0, 14, 14),
                    Padding = new Thickness(16),
                    Effect = KTheme.CardShadow()
                };

                var stack = new StackPanel();

                var headDock = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
                var statusBadge = new Border
                {
                    Background = isPrep ? KTheme.BrandBg : KTheme.WarnBg,
                    BorderBrush = isPrep ? KTheme.Brand : KTheme.Warn,
                    BorderThickness = new Thickness(1),
                    CornerRadius = KTheme.BtnRadius,
                    Padding = new Thickness(10, 4, 10, 4),
                    VerticalAlignment = VerticalAlignment.Center
                };
                statusBadge.Child = new TextBlock
                {
                    Text = isPrep ? "EN PREPARACIÓN" : "NUEVO PEDIDO",
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = isPrep ? KTheme.BrandDark : KTheme.Warn
                };
                DockPanel.SetDock(statusBadge, Dock.Right);
                headDock.Children.Add(statusBadge);

                headDock.Children.Add(new TextBlock
                {
                    Text = string.Format("PEDIDO #{0:000}", ord.TicketNumber),
                    FontSize = 22,
                    FontWeight = FontWeights.Bold,
                    Foreground = KTheme.Text,
                    VerticalAlignment = VerticalAlignment.Center
                });
                stack.Children.Add(headDock);

                string createdShort = (ord.CreatedAt ?? "").Length >= 16 ? ord.CreatedAt.Substring(0, 16) : (ord.CreatedAt ?? "");
                stack.Children.Add(new TextBlock
                {
                    Text = "Hora de pedido: " + createdShort,
                    FontSize = 12,
                    Foreground = KTheme.Muted,
                    Margin = new Thickness(0, 0, 0, 10)
                });

                var itemsBorder = new Border
                {
                    Background = KTheme.Bg,
                    BorderBrush = KTheme.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = KTheme.BtnRadius,
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 0, 0, 10)
                };
                var itemsStack = new StackPanel();
                foreach (var it in GetKitchenItems(ord))
                {
                    var itemRow = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                    var qtyBadge = new Border
                    {
                        Background = KTheme.Brand,
                        CornerRadius = KTheme.BtnRadius,
                        Padding = new Thickness(8, 3, 8, 3),
                        Margin = new Thickness(0, 0, 10, 0)
                    };
                    qtyBadge.Child = new TextBlock
                    {
                        Text = it.Quantity + "x",
                        FontSize = 13,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White
                    };
                    DockPanel.SetDock(qtyBadge, Dock.Left);
                    itemRow.Children.Add(qtyBadge);

                    var itemTextStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    itemTextStack.Children.Add(new TextBlock
                    {
                        Text = it.ProductName,
                        FontSize = 14,
                        FontWeight = FontWeights.Bold,
                        Foreground = KTheme.Text,
                        TextWrapping = TextWrapping.Wrap
                    });
                    if (it.PackChoices != null && it.PackChoices.Count > 0)
                    {
                        foreach (var pc in it.PackChoices)
                        {
                            itemTextStack.Children.Add(new TextBlock
                            {
                                Text = "   • " + pc.Quantity + "x " + pc.ProductName,
                                FontSize = 13,
                                FontWeight = FontWeights.SemiBold,
                                Foreground = KTheme.Text,
                                TextWrapping = TextWrapping.Wrap,
                                Margin = new Thickness(0, 1, 0, 0)
                            });
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(it.Note))
                    {
                        itemTextStack.Children.Add(new TextBlock
                        {
                            Text = "" + it.Note,
                            FontSize = 12,
                            FontWeight = FontWeights.Bold,
                            Foreground = KTheme.Brand,
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 2, 0, 0)
                        });
                    }
                    itemRow.Children.Add(itemTextStack);
                    itemsStack.Children.Add(itemRow);
                }
                itemsBorder.Child = itemsStack;
                stack.Children.Add(itemsBorder);

                if (!string.IsNullOrWhiteSpace(ord.Notes))
                {
                    var noteBorder = new Border
                    {
                        Background = KTheme.WarnBg,
                        BorderBrush = KTheme.Warn,
                        BorderThickness = new Thickness(1),
                        CornerRadius = KTheme.BtnRadius,
                        Padding = new Thickness(10, 8, 10, 8),
                        Margin = new Thickness(0, 0, 0, 10)
                    };
                    noteBorder.Child = new TextBlock
                    {
                        Text = "NOTA: " + ord.Notes,
                        FontSize = 13,
                        FontWeight = FontWeights.Bold,
                        Foreground = KTheme.Warn,
                        TextWrapping = TextWrapping.Wrap
                    };
                    stack.Children.Add(noteBorder);
                }

                var actionsDock = new DockPanel();
                var ordCopy = ord;

                var btnPrint = CreateSecondaryButton("", () =>
                {
                    try { PrintComandaWithKitchenPrinter(ordCopy); }
                    catch (Exception ex) { MessageBox.Show(ex.Message, "Ticketera", MessageBoxButton.OK, MessageBoxImage.Error); }
                }, "printer");
                btnPrint.Margin = new Thickness(0, 0, 6, 0);
                DockPanel.SetDock(btnPrint, Dock.Left);
                actionsDock.Children.Add(btnPrint);

                if (!isPrep)
                {
                    var btnPrep = CreateSecondaryButton("Preparar", () => SetOrderStatus(ordCopy.Id, "preparando"), "chef-hat");
                    btnPrep.Margin = new Thickness(0, 0, 6, 0);
                    DockPanel.SetDock(btnPrep, Dock.Left);
                    actionsDock.Children.Add(btnPrep);
                }

                var btnDone = CreateSuccessButton("PEDIDO LISTO", () => SetOrderStatus(ordCopy.Id, "finalizado"), "check");
                actionsDock.Children.Add(btnDone);

                stack.Children.Add(actionsDock);
                card.Child = stack;
                _activeWrap.Children.Add(card);
            }
        }

        private void RenderFinishedList(List<OrderRecord> finishedOrders)
        {
            _finishedStack.Children.Clear();
            if (finishedOrders.Count == 0)
            {
                _finishedStack.Children.Add(new TextBlock
                {
                    Text = "Todavía no hay pedidos marcados como listos en cocina.",
                    FontSize = 14,
                    Foreground = KTheme.Muted,
                    Margin = new Thickness(10, 20, 10, 10)
                });
                return;
            }

            foreach (var ord in finishedOrders)
            {
                var rowCard = new Border
                {
                    Background = KTheme.Card,
                    BorderBrush = KTheme.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = KTheme.CardRadius,
                    Padding = new Thickness(16, 12, 16, 12),
                    Margin = new Thickness(0, 0, 0, 8)
                };

                var dock = new DockPanel();
                var ordCopy = ord;
                var btnReopen = CreateSecondaryButton("Volver a Pendiente", () => SetOrderStatus(ordCopy.Id, "pendiente"), "rotate-ccw");
                DockPanel.SetDock(btnReopen, Dock.Right);
                dock.Children.Add(btnReopen);

                string finTime = (ord.FinishedAt ?? "").Length >= 5 ? ord.FinishedAt.Substring(0, 5) : (ord.FinishedAt ?? "");
                string stateLabel = ord.KitchenStatus == "entregado" ? "Entregado por Caja" : ("Listo en Cocina " + finTime).Trim();
                string summary = string.Join(", ", GetKitchenItems(ord).Select(i => i.Quantity + "x " + i.ProductName));
                dock.Children.Add(new TextBlock
                {
                    Text = string.Format("PEDIDO #{0:000}   ·   {1}   ·   {2}",
                        ord.TicketNumber, stateLabel, summary),
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = KTheme.Text,
                    VerticalAlignment = VerticalAlignment.Center
                });

                rowCard.Child = dock;
                _finishedStack.Children.Add(rowCard);
            }
        }

        private void SetOrderStatus(int orderId, string newStatus)
        {
            try
            {
                if (!string.IsNullOrEmpty(_activeCajaIp))
                {
                    _db = PosNetworkSync.SendKitchenStatusToCajaIp(_activeCajaIp, orderId, newStatus);
                }
                else
                {
                    _db = PosStore.Load();
                    var target = _db.Orders.FirstOrDefault(o => o.Id == orderId);
                    if (target != null)
                    {
                        target.KitchenStatus = newStatus;
                        target.FinishedAt = newStatus == "finalizado" ? DateTime.Now.ToString("HH:mm") : "";
                        target.KitchenPrinted = true;
                        PosStore.Save(_db);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo comunicar el cambio a la PC de Caja: " + ex.Message, "Red Local", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            _lastRenderHash = "";
            PollOrders();
        }

        private Border CreateTabButton(string text, string lucideIcon, Action onClick)
        {
            var b = new Border
            {
                Background = KTheme.Bg,
                BorderBrush = KTheme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = KTheme.BtnRadius,
                Padding = new Thickness(14, 7, 14, 7),
                Cursor = Cursors.Hand
            };
            b.Child = LucideIcons.IconText(lucideIcon, text, 14, KTheme.Muted, 13, FontWeights.Bold);
            b.MouseLeftButtonUp += (s, e) => onClick();
            return b;
        }

        private Border CreateSecondaryButton(string text, Action onClick, string lucideIcon = null)
        {
            var b = new Border
            {
                Background = KTheme.Card,
                BorderBrush = KTheme.BorderStrong,
                BorderThickness = new Thickness(1),
                CornerRadius = KTheme.BtnRadius,
                Padding = new Thickness(12, 9, 12, 9),
                Cursor = Cursors.Hand
            };
            if (!string.IsNullOrEmpty(lucideIcon))
                b.Child = LucideIcons.IconText(lucideIcon, text, 14, KTheme.Text, 12, FontWeights.Bold);
            else
                b.Child = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = KTheme.Text, HorizontalAlignment = HorizontalAlignment.Center };
            b.MouseEnter += (s, e) => b.Background = KTheme.Hover;
            b.MouseLeave += (s, e) => b.Background = KTheme.Card;
            b.MouseLeftButtonUp += (s, e) => onClick();
            return b;
        }

        private Border CreateSuccessButton(string text, Action onClick, string lucideIcon = null)
        {
            var b = new Border
            {
                Background = KTheme.Ok,
                CornerRadius = KTheme.BtnRadius,
                Padding = new Thickness(14, 9, 14, 9),
                Cursor = Cursors.Hand
            };
            if (!string.IsNullOrEmpty(lucideIcon))
                b.Child = LucideIcons.IconText(lucideIcon, text, 15, Brushes.White, 13, FontWeights.Bold);
            else
                b.Child = new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center };
            b.MouseEnter += (s, e) => b.Opacity = 0.9;
            b.MouseLeave += (s, e) => b.Opacity = 1.0;
            b.MouseLeftButtonUp += (s, e) => onClick();
            return b;
        }
    }

    public static class CocinaProgram
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        public static void Main()
        {
            try { SetProcessDPIAware(); } catch { }
            var app = new Application();
            app.Run(new CocinaMainWindow());
        }
    }
}
