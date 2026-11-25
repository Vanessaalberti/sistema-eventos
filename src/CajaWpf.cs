using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace EventosPos
{
    public static class Theme
    {
        public static readonly SolidColorBrush Bg = BrushFromHex("#F5F6F8");
        public static readonly SolidColorBrush Card = BrushFromHex("#FFFFFF");
        public static readonly SolidColorBrush Border = BrushFromHex("#E5E7EB");
        public static readonly SolidColorBrush BorderStrong = BrushFromHex("#D1D5DB");
        public static readonly SolidColorBrush Hover = BrushFromHex("#F3F4F6");
        public static readonly SolidColorBrush BrandHover = BrushFromHex("#F0FBFD");
        public static readonly SolidColorBrush Text = BrushFromHex("#1F2937");
        public static readonly SolidColorBrush Muted = BrushFromHex("#6B7280");
        public static readonly SolidColorBrush Faint = BrushFromHex("#9CA3AF");
        public static readonly SolidColorBrush Brand = BrushFromHex("#0891B2");
        public static readonly SolidColorBrush BrandDark = BrushFromHex("#0E7490");
        public static readonly SolidColorBrush BrandBg = BrushFromHex("#E0F7FA");
        public static readonly SolidColorBrush BrandSoft = BrushFromHex("#CFFAFE");
        public static readonly SolidColorBrush Ok = BrushFromHex("#16A34A");
        public static readonly SolidColorBrush OkBg = BrushFromHex("#DCFCE7");
        public static readonly SolidColorBrush Warn = BrushFromHex("#CA8A04");
        public static readonly SolidColorBrush WarnBg = BrushFromHex("#FEF9C3");
        public static readonly SolidColorBrush Err = BrushFromHex("#DC2626");
        public static readonly SolidColorBrush ErrBg = BrushFromHex("#FEE2E2");

        // Radio uniforme rectangular redondeado (sin bordes ovalados)
        public static readonly CornerRadius BtnRadius = new CornerRadius(6);
        public static readonly CornerRadius CardRadius = new CornerRadius(8);

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

        private static readonly Dictionary<string, BitmapImage> ImgCache = new Dictionary<string, BitmapImage>();

        public static BitmapImage LoadImageCached(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename)) return null;
            string fullPath = Path.IsPathRooted(filename) ? filename : Path.Combine(PosStore.ImagesDir, filename);
            if (!File.Exists(fullPath)) return null;

            string key = fullPath + "_" + File.GetLastWriteTimeUtc(fullPath).Ticks;
            BitmapImage bmp;
            if (ImgCache.TryGetValue(key, out bmp))
                return bmp;

            try
            {
                bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(fullPath, UriKind.Absolute);
                bmp.DecodePixelWidth = 260;
                bmp.EndInit();
                bmp.Freeze();
                ImgCache[key] = bmp;
                return bmp;
            }
            catch
            {
                return null;
            }
        }
    }

    public class CartEntry
    {
        public ProductItem Product { get; set; }
        public int Quantity { get; set; }
        public decimal Subtotal { get { return Product.Price * Quantity; } }
    }

    public class CajaMainWindow : Window
    {
        private PosDatabasePayload _db;
        private readonly List<CartEntry> _cart = new List<CartEntry>();
        private string _selectedCategory = "Todos";
        private string _searchQuery = "";
        private string _paymentMethod = "Efectivo";
        private string _activeSection = "caja_actual"; // Pantalla inicial al abrir: Caja Actual
        private bool _sidebarExpanded = true;
        private int _editingGroupId = 1;
        private OrderRecord _lastCreatedOrder;

        // Controles del Shell
        private ColumnDefinition _sidebarCol;
        private TextBlock _brandTitle;
        private StackPanel _sidebarFooter;
        private TextBlock _sbSessionStatus;
        private TextBlock _sbSessionTotal;
        private readonly Dictionary<string, Border> _navButtons = new Dictionary<string, Border>();
        private readonly Dictionary<string, TextBlock> _navLabels = new Dictionary<string, TextBlock>();

        // Barra superior
        private TextBlock _headerTitle;
        private TextBlock _headerSub;
        private TextBlock _kitchenBadgeText;
        private TextBlock _nextTicketText;

        // Banner de notificación
        private Border _toastBanner;
        private TextBlock _toastText;

        // Vistas principales
        private Grid _viewHost;
        private UIElement _cajaActualView;
        private UIElement _posView;
        private UIElement _menuView;
        private UIElement _ordersView;
        private UIElement _settingsView;

        // Controles de Vista Punto de Venta
        private TextBlock _posActiveMenuTitle;
        private StackPanel _catButtonsPanel;
        private WrapPanel _productsWrap;
        private StackPanel _cartListPanel;
        private TextBox _notesBox;
        private TextBox _cashInputBox;
        private TextBlock _vueltoLabel;
        private TextBlock _totalAmountLabel;
        private StackPanel _cashContextPanel;
        private Border _mpContextPanel;
        private TextBlock _mpAliasLabel;
        private readonly Dictionary<string, Border> _payMethodCards = new Dictionary<string, Border>();

        // Controles de Vista Menú (Grupos de Evento + Platos del Grupo)
        private StackPanel _groupsListStack;
        private TextBlock _selectedGroupHeaderTitle;
        private TextBlock _selectedGroupHeaderSub;
        private StackPanel _groupDishesStack;

        // Controles de Tickets y Caja Actual
        private StackPanel _ordersListStack;
        private StackPanel _cajaActualHostStack;

        // Controles de Ajustes
        private ComboBox _printerCombo;
        private ComboBox _paperWidthCombo;
        private CheckBox _doubleTicketCheck;
        private TextBox _mpAliasSettingBox;
        private TextBox _footerSettingBox;

        private DispatcherTimer _syncTimer;

        public CajaMainWindow()
        {
            _db = PosStore.Load();
            var activeSess = PosStore.GetActiveSession(_db);
            _editingGroupId = activeSess != null ? activeSess.MenuGroupId : _db.Settings.ActiveMenuGroupId;

            Title = "CAJA POS — Asociación Juvenil Araucana";
            Width = 1380;
            Height = 840;
            MinWidth = 1100;
            MinHeight = 700;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Theme.Bg;
            FontFamily = new FontFamily("Segoe UI");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            PreviewKeyDown += OnWindowKeyDown;
            Closed += (s, e) => PosNetworkSync.StopCajaServer();

            // Levantar servidor TCP + Faro UDP de red local para que Cocina_POS.exe en otra PC se conecte solo
            PosNetworkSync.StartCajaServer();

            BuildShellLayout();
            RefreshAllUI();

            _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _syncTimer.Tick += (s, e) => BackgroundSync();
            _syncTimer.Start();
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F12 && _activeSection == "pos")
            {
                SubmitOrder();
                e.Handled = true;
            }
        }

        // =====================================================================
        // SHELL PRINCIPAL (MENÚ LATERAL DESPLEGABLE + CABECERA + VISTAS)
        // =====================================================================
        private void BuildShellLayout()
        {
            var rootGrid = new Grid();
            _sidebarCol = new ColumnDefinition { Width = new GridLength(235) };
            rootGrid.ColumnDefinitions.Add(_sidebarCol);
            rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // 1. SIDEBAR IZQUIERDO DESPLEGABLE
            var sidebarBorder = new Border
            {
                Background = Theme.Card,
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(0, 0, 1, 0)
            };
            Grid.SetColumn(sidebarBorder, 0);

            var sbDock = new DockPanel { LastChildFill = true };

            var sbHeader = new DockPanel { Margin = new Thickness(14, 16, 14, 12) };
            DockPanel.SetDock(sbHeader, Dock.Top);

            var toggleBtn = CreateIconButton("☰", "Desplegar / Contraer menú lateral", ToggleSidebar);
            DockPanel.SetDock(toggleBtn, Dock.Left);
            sbHeader.Children.Add(toggleBtn);

            _brandTitle = new TextBlock
            {
                Text = "ARAUCANA POS",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.BrandDark,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            sbHeader.Children.Add(_brandTitle);
            sbDock.Children.Add(sbHeader);

            // Footer del Sidebar: Resumen de Caja Actual
            _sidebarFooter = new StackPanel { Margin = new Thickness(12, 8, 12, 16) };
            DockPanel.SetDock(_sidebarFooter, Dock.Bottom);

            var statusCard = new Border
            {
                Background = Theme.Bg,
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.CardRadius,
                Padding = new Thickness(12, 10, 12, 10)
            };
            var scStack = new StackPanel();
            _sbSessionStatus = new TextBlock
            {
                Text = "● CAJA ABIERTA",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Ok
            };
            _sbSessionTotal = new TextBlock
            {
                Text = "Recaudado: $0",
                FontSize = 12,
                Foreground = Theme.Muted,
                Margin = new Thickness(0, 2, 0, 8)
            };
            var btnGoToPos = CreatePrimaryButton("🛒 Ir a Tomar Pedido", () => SwitchSection("pos"), true);
            scStack.Children.Add(_sbSessionStatus);
            scStack.Children.Add(_sbSessionTotal);
            scStack.Children.Add(btnGoToPos);
            statusCard.Child = scStack;
            _sidebarFooter.Children.Add(statusCard);
            sbDock.Children.Add(_sidebarFooter);

            // Botones del Menú Lateral (con "Caja Actual" primero y "Menú" limpio)
            var navStack = new StackPanel { Margin = new Thickness(10, 4, 10, 0) };
            AddNavItem(navStack, "caja_actual", "🏠", "Caja Actual");
            AddNavItem(navStack, "pos", "🛒", "Tomar Pedido");
            AddNavItem(navStack, "menu", "🍽️", "Menú");
            AddNavItem(navStack, "orders", "🧾", "Tickets Emitidos");
            AddNavItem(navStack, "settings", "⚙️", "Ticketera y Ajustes");
            sbDock.Children.Add(navStack);

            sidebarBorder.Child = sbDock;
            rootGrid.Children.Add(sidebarBorder);

            // 2. CONTENEDOR DERECHO
            var rightDock = new DockPanel { LastChildFill = true };
            Grid.SetColumn(rightDock, 1);

            var topBar = new Border
            {
                Background = Theme.Card,
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(22, 12, 22, 12)
            };
            DockPanel.SetDock(topBar, Dock.Top);

            var topGrid = new Grid();
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _headerTitle = new TextBlock
            {
                Text = "Caja Actual — Asociación Juvenil Araucana",
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Text
            };
            _headerSub = new TextBlock
            {
                Text = "Menú activo: Almuerzo y Buffet Araucana",
                FontSize = 12,
                Foreground = Theme.Muted
            };
            titleStack.Children.Add(_headerTitle);
            titleStack.Children.Add(_headerSub);
            Grid.SetColumn(titleStack, 0);
            topGrid.Children.Add(titleStack);

            var rightBadges = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var lanBadge = new Border
            {
                Background = Theme.Bg,
                BorderBrush = Theme.BorderStrong,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.BtnRadius,
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Las PCs de Cocina en la misma red Wi-Fi se conectan automáticamente a esta IP"
            };
            lanBadge.Child = new TextBlock
            {
                Text = "📡 IP Caja: " + PosNetworkSync.GetLocalLanIp(),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Text
            };
            rightBadges.Children.Add(lanBadge);

            var btnNewSessionTop = CreateGhostButton("🔓 Nueva Caja / Cambiar Evento", OpenNewSessionModal);
            btnNewSessionTop.Margin = new Thickness(0, 0, 8, 0);
            rightBadges.Children.Add(btnNewSessionTop);

            var kitchenBadge = new Border
            {
                Background = Theme.WarnBg,
                BorderBrush = Theme.Warn,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.BtnRadius,
                Padding = new Thickness(12, 7, 12, 7),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _kitchenBadgeText = new TextBlock
            {
                Text = "👨‍🍳 Cocina: 0 en curso",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Warn
            };
            kitchenBadge.Child = _kitchenBadgeText;
            rightBadges.Children.Add(kitchenBadge);

            var nextTicketBadge = new Border
            {
                Background = Theme.BrandBg,
                BorderBrush = Theme.Brand,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.BtnRadius,
                Padding = new Thickness(12, 7, 12, 7),
                VerticalAlignment = VerticalAlignment.Center
            };
            _nextTicketText = new TextBlock
            {
                Text = "PRÓXIMO: #001",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.BrandDark
            };
            nextTicketBadge.Child = _nextTicketText;
            rightBadges.Children.Add(nextTicketBadge);

            Grid.SetColumn(rightBadges, 1);
            topGrid.Children.Add(rightBadges);
            topBar.Child = topGrid;
            rightDock.Children.Add(topBar);

            // Banner de avisos
            _toastBanner = new Border
            {
                Background = Theme.OkBg,
                BorderBrush = Theme.Ok,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.BtnRadius,
                Margin = new Thickness(20, 12, 20, 0),
                Padding = new Thickness(14, 8, 14, 8),
                Visibility = Visibility.Collapsed
            };
            DockPanel.SetDock(_toastBanner, Dock.Top);

            var toastDock = new DockPanel();
            var btnReprintLast = CreateGhostButton("🖨️ Reimprimir último ticket", ReprintLastOrder);
            DockPanel.SetDock(btnReprintLast, Dock.Right);
            toastDock.Children.Add(btnReprintLast);

            _toastText = new TextBlock
            {
                Text = "",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Ok,
                VerticalAlignment = VerticalAlignment.Center
            };
            toastDock.Children.Add(_toastText);
            _toastBanner.Child = toastDock;
            rightDock.Children.Add(_toastBanner);

            // Host de las 5 vistas
            _viewHost = new Grid { Margin = new Thickness(20, 14, 20, 18) };
            _cajaActualView = BuildCajaActualView();
            _posView = BuildPosView();
            _menuView = BuildMenuView();
            _ordersView = BuildOrdersView();
            _settingsView = BuildSettingsView();

            _viewHost.Children.Add(_cajaActualView);
            _viewHost.Children.Add(_posView);
            _viewHost.Children.Add(_menuView);
            _viewHost.Children.Add(_ordersView);
            _viewHost.Children.Add(_settingsView);

            rightDock.Children.Add(_viewHost);
            rootGrid.Children.Add(rightDock);
            Content = rootGrid;

            // Al abrir el programa, lo primero que aparece es "Caja Actual"
            SwitchSection("caja_actual");
        }

        private void AddNavItem(StackPanel parent, string key, string icon, string label)
        {
            var border = new Border
            {
                CornerRadius = Theme.BtnRadius,
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 2, 0, 2),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var ic = new TextBlock
            {
                Text = icon,
                FontSize = 15,
                Width = 24,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var lbl = new TextBlock
            {
                Text = label,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Text,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            row.Children.Add(ic);
            row.Children.Add(lbl);
            border.Child = row;

            border.MouseEnter += (s, e) =>
            {
                if (_activeSection != key) border.Background = Theme.Hover;
            };
            border.MouseLeave += (s, e) =>
            {
                if (_activeSection != key) border.Background = Brushes.Transparent;
            };
            border.MouseLeftButtonUp += (s, e) => SwitchSection(key);

            _navButtons[key] = border;
            _navLabels[key] = lbl;
            parent.Children.Add(border);
        }

        private void ToggleSidebar()
        {
            _sidebarExpanded = !_sidebarExpanded;
            _sidebarCol.Width = new GridLength(_sidebarExpanded ? 235 : 68);
            _brandTitle.Visibility = _sidebarExpanded ? Visibility.Visible : Visibility.Collapsed;
            _sidebarFooter.Visibility = _sidebarExpanded ? Visibility.Visible : Visibility.Collapsed;
            foreach (var kv in _navLabels)
            {
                kv.Value.Visibility = _sidebarExpanded ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void SwitchSection(string key)
        {
            _activeSection = key;
            _cajaActualView.Visibility = key == "caja_actual" ? Visibility.Visible : Visibility.Collapsed;
            _posView.Visibility = key == "pos" ? Visibility.Visible : Visibility.Collapsed;
            _menuView.Visibility = key == "menu" ? Visibility.Visible : Visibility.Collapsed;
            _ordersView.Visibility = key == "orders" ? Visibility.Visible : Visibility.Collapsed;
            _settingsView.Visibility = key == "settings" ? Visibility.Visible : Visibility.Collapsed;

            foreach (var kv in _navButtons)
            {
                bool active = kv.Key == key;
                kv.Value.Background = active ? Theme.BrandBg : Brushes.Transparent;
                _navLabels[kv.Key].Foreground = active ? Theme.BrandDark : Theme.Text;
                _navLabels[kv.Key].FontWeight = active ? FontWeights.Bold : FontWeights.SemiBold;
            }

            if (key == "caja_actual") RenderCajaActualView();
            else if (key == "pos")
            {
                RenderCategoryButtons();
                RenderProductCards();
            }
            else if (key == "menu") RenderMenuSection();
            else if (key == "orders") RenderOrdersHistoryList();
            else if (key == "settings") LoadSettingsForm();
        }

        private void ShowToast(string message, bool isError = false)
        {
            _toastBanner.Background = isError ? Theme.ErrBg : Theme.OkBg;
            _toastBanner.BorderBrush = isError ? Theme.Err : Theme.Ok;
            _toastText.Foreground = isError ? Theme.Err : Theme.Ok;
            _toastText.Text = message;
            _toastBanner.Visibility = Visibility.Visible;
        }

        private int GetCurrentActiveGroupId()
        {
            var sess = PosStore.GetActiveSession(_db);
            if (sess != null && sess.MenuGroupId > 0 && _db.MenuGroups.Any(g => g.Id == sess.MenuGroupId))
                return sess.MenuGroupId;
            if (_db.Settings.ActiveMenuGroupId > 0 && _db.MenuGroups.Any(g => g.Id == _db.Settings.ActiveMenuGroupId))
                return _db.Settings.ActiveMenuGroupId;
            return _db.MenuGroups.First().Id;
        }

        private MenuGroup GetCurrentActiveGroup()
        {
            int gid = GetCurrentActiveGroupId();
            return _db.MenuGroups.FirstOrDefault(g => g.Id == gid) ?? _db.MenuGroups.First();
        }

        // =====================================================================
        // VISTA 1 (INICIAL AL ABRIR): CAJA ACTUAL EN LA QUE SE ESTÁ
        // =====================================================================
        private UIElement BuildCajaActualView()
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _cajaActualHostStack = new StackPanel();
            scroll.Content = _cajaActualHostStack;
            return scroll;
        }

        private void RenderCajaActualView()
        {
            _cajaActualHostStack.Children.Clear();
            _db = PosStore.Load();
            var activeSess = PosStore.GetActiveSession(_db);
            if (activeSess != null)
                PosStore.RecalculateSessionMetrics(_db, activeSess);

            var activeGroup = GetCurrentActiveGroup();

            // Tarjeta Principal de la Caja Actual
            var heroCard = new Border
            {
                Background = Theme.Card,
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.CardRadius,
                Padding = new Thickness(20, 18, 20, 18),
                Margin = new Thickness(0, 0, 0, 14),
                Effect = Theme.CardShadow()
            };

            var heroDock = new DockPanel();
            var heroActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(heroActions, Dock.Right);

            if (activeSess != null)
            {
                var btnGoPos = CreatePrimaryButton("🛒 Ir a Tomar Pedidos", () => SwitchSection("pos"), false);
                btnGoPos.Margin = new Thickness(0, 0, 8, 0);
                heroActions.Children.Add(btnGoPos);
            }

            var btnNewCaja = CreateGhostButton("🔓 Iniciar Nueva Caja / Cambiar Tipo de Evento", OpenNewSessionModal);
            heroActions.Children.Add(btnNewCaja);
            heroDock.Children.Add(heroActions);

            var heroInfo = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };

            var statusBadge = new Border
            {
                Background = activeSess != null ? Theme.OkBg : Theme.ErrBg,
                BorderBrush = activeSess != null ? Theme.Ok : Theme.Err,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.BtnRadius,
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(0, 0, 10, 0)
            };
            statusBadge.Child = new TextBlock
            {
                Text = activeSess != null ? string.Format("CAJA #{0:000} EN CURSO", activeSess.Id) : "CAJA CERRADA",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = activeSess != null ? Theme.Ok : Theme.Err
            };
            statusRow.Children.Add(statusBadge);

            var groupBadge = new Border
            {
                Background = Theme.BrandBg,
                BorderBrush = Theme.Brand,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.BtnRadius,
                Padding = new Thickness(10, 3, 10, 3)
            };
            groupBadge.Child = new TextBlock
            {
                Text = "MENÚ ACTIVO: " + activeGroup.Name.ToUpperInvariant(),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.BrandDark
            };
            statusRow.Children.Add(groupBadge);
            heroInfo.Children.Add(statusRow);

            heroInfo.Children.Add(new TextBlock
            {
                Text = activeSess != null ? activeSess.EventName : "No hay ninguna caja abierta en este momento",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Text,
                Margin = new Thickness(0, 2, 0, 2)
            });

            heroInfo.Children.Add(new TextBlock
            {
                Text = activeSess != null
                    ? string.Format("Abierta el {0}   ·   Fondo inicial en efectivo: {1}   ·   Próximo ticket: #{2:000}",
                        activeSess.OpenedAt, PosStore.FormatMoney(activeSess.OpeningCash), _db.Settings.NextTicketSequence)
                    : "Hacé clic en 'Iniciar Nueva Caja / Cambiar Tipo de Evento' para elegir el menú del evento y empezar a cobrar.",
                FontSize = 13,
                Foreground = Theme.Muted
            });

            heroDock.Children.Add(heroInfo);
            heroCard.Child = heroDock;
            _cajaActualHostStack.Children.Add(heroCard);

            if (activeSess != null)
            {
                // Selector rápido para cambiar el Menú (Grupo de Evento) de la Caja Actual sin cerrarla
                var switchMenuCard = new Border
                {
                    Background = Theme.Card,
                    BorderBrush = Theme.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = Theme.CardRadius,
                    Padding = new Thickness(16, 10, 16, 10),
                    Margin = new Thickness(0, 0, 0, 14)
                };
                var smDock = new DockPanel();
                var btnEditMenus = CreateGhostButton("🍽️ Configurar Grupos y Menús", () => SwitchSection("menu"));
                DockPanel.SetDock(btnEditMenus, Dock.Right);
                smDock.Children.Add(btnEditMenus);

                var smLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                smLeft.Children.Add(new TextBlock
                {
                    Text = "Tipo de Evento / Menú de esta Caja:",
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Theme.Text,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                });

                var grpCombo = new ComboBox { Width = 280, Padding = new Thickness(8, 5, 8, 5), FontSize = 13 };
                foreach (var g in _db.MenuGroups)
                {
                    grpCombo.Items.Add(g.Name);
                }
                grpCombo.SelectedItem = activeGroup.Name;
                grpCombo.SelectionChanged += (s, e) =>
                {
                    if (grpCombo.SelectedItem == null) return;
                    string chosenName = grpCombo.SelectedItem.ToString();
                    var chosenGrp = _db.MenuGroups.FirstOrDefault(g => g.Name == chosenName);
                    if (chosenGrp != null && chosenGrp.Id != activeSess.MenuGroupId)
                    {
                        activeSess.MenuGroupId = chosenGrp.Id;
                        activeSess.MenuGroupName = chosenGrp.Name;
                        _db.Settings.ActiveMenuGroupId = chosenGrp.Id;
                        PosStore.Save(_db);
                        _cart.Clear();
                        RefreshAllUI();
                        RenderCajaActualView();
                        ShowToast("Menú de la caja actual cambiado a: " + chosenGrp.Name);
                    }
                };
                smLeft.Children.Add(grpCombo);
                smDock.Children.Add(smLeft);
                switchMenuCard.Child = smDock;
                _cajaActualHostStack.Children.Add(switchMenuCard);

                // 5 Tarjetas de Resumen de la Caja Actual
                var metricsGrid = new UniformGrid { Columns = 5, Margin = new Thickness(0, 0, 0, 14) };
                metricsGrid.Children.Add(CreateStatCard("TOTAL RECAUDADO", PosStore.FormatMoney(activeSess.TotalSales), Theme.BrandDark));
                metricsGrid.Children.Add(CreateStatCard("PEDIDOS COBRADOS", activeSess.OrdersCount.ToString(), Theme.Text));
                metricsGrid.Children.Add(CreateStatCard("💵 EFECTIVO", PosStore.FormatMoney(activeSess.CashSales), Theme.Ok));
                metricsGrid.Children.Add(CreateStatCard("📱 MERCADO PAGO", PosStore.FormatMoney(activeSess.MpSales), Theme.Brand));
                metricsGrid.Children.Add(CreateStatCard("💳 TARJETA", PosStore.FormatMoney(activeSess.CardSales), Theme.Muted));
                _cajaActualHostStack.Children.Add(metricsGrid);

                // Dos columnas: Arqueo y Cierre de la Caja Actual + Ranking de Platos Vendidos
                var splitGrid = new Grid { Margin = new Thickness(0, 0, 0, 16) };
                splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
                splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
                splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var arqueoCard = new Border
                {
                    Background = Theme.Card,
                    BorderBrush = Theme.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = Theme.CardRadius,
                    Padding = new Thickness(18)
                };
                var aStack = new StackPanel();
                aStack.Children.Add(new TextBlock
                {
                    Text = "🔐 Arqueo y Cierre de esta Caja",
                    FontSize = 15,
                    FontWeight = FontWeights.Bold,
                    Foreground = Theme.Text,
                    Margin = new Thickness(0, 0, 0, 10)
                });

                aStack.Children.Add(new TextBlock
                {
                    Text = string.Format("• Fondo Inicial de Cambio:   {0}\n• (+) Ventas en Efectivo:     {1}\n• (=) EFECTIVO ESPERADO EN CAJÓN:   {2}",
                        PosStore.FormatMoney(activeSess.OpeningCash),
                        PosStore.FormatMoney(activeSess.CashSales),
                        PosStore.FormatMoney(activeSess.ExpectedCash)),
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Theme.Text,
                    LineHeight = 22,
                    Margin = new Thickness(0, 0, 0, 12)
                });

                aStack.Children.Add(new TextBlock
                {
                    Text = "Efectivo Real Contado en Cajón ($):",
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = Theme.Muted,
                    Margin = new Thickness(0, 0, 0, 4)
                });

                var countedRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
                var countedBox = CreateStyledTextBox("");
                countedBox.Width = 150;
                countedBox.Text = activeSess.ExpectedCash.ToString("0");

                var diffLabel = new TextBlock
                {
                    Text = "✓ Caja Exacta ($0)",
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = Theme.Ok,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0)
                };

                countedBox.TextChanged += (s, e) =>
                {
                    decimal cVal = 0;
                    decimal.TryParse(countedBox.Text.Trim(), out cVal);
                    decimal diff = cVal - activeSess.ExpectedCash;
                    if (Math.Abs(diff) < 1)
                    {
                        diffLabel.Text = "✓ Caja Exacta ($0)";
                        diffLabel.Foreground = Theme.Ok;
                    }
                    else if (diff > 0)
                    {
                        diffLabel.Text = "Sobrante: +" + PosStore.FormatMoney(diff);
                        diffLabel.Foreground = Theme.BrandDark;
                    }
                    else
                    {
                        diffLabel.Text = "Faltante: " + PosStore.FormatMoney(diff);
                        diffLabel.Foreground = Theme.Err;
                    }
                };

                countedRow.Children.Add(countedBox);
                countedRow.Children.Add(diffLabel);
                aStack.Children.Add(countedRow);

                var sessionOrders = _db.Orders.Where(o => o.SessionId == activeSess.Id).ToList();

                var btnsClosure = new UniformGrid { Columns = 2 };
                var btnPrintZ = CreateGhostButton("🖨️ Imprimir Reporte (Ticket Z)", () =>
                {
                    decimal cVal = activeSess.ExpectedCash;
                    decimal.TryParse(countedBox.Text.Trim(), out cVal);
                    activeSess.CountedCash = cVal;
                    try
                    {
                        EscPosPrinter.PrintCashClosure(activeSess, sessionOrders, _db.Settings);
                        ShowToast("🖨️ Reporte de Arqueo impreso en " + _db.Settings.PrinterName);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, "Error al imprimir", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                });
                btnPrintZ.Margin = new Thickness(0, 0, 6, 0);
                btnsClosure.Children.Add(btnPrintZ);

                var btnCloseRegister = CreatePrimaryButton("🔒 CERRAR CAJA ACTUAL", () =>
                {
                    if (MessageBox.Show("¿Querés CERRAR formalmente la caja actual e imprimir el Ticket Z de Cierre?",
                        "Confirmar Cierre de Caja", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        decimal cVal = activeSess.ExpectedCash;
                        decimal.TryParse(countedBox.Text.Trim(), out cVal);
                        activeSess.CountedCash = cVal;
                        activeSess.Difference = cVal - activeSess.ExpectedCash;
                        activeSess.ClosedAt = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
                        activeSess.Status = "cerrada";
                        PosStore.Save(_db);

                        try
                        {
                            EscPosPrinter.PrintCashClosure(activeSess, sessionOrders, _db.Settings);
                        }
                        catch { }

                        RefreshAllUI();
                        RenderCajaActualView();
                        ShowToast(string.Format("🔒 Caja #{0:000} cerrada correctamente.", activeSess.Id));
                    }
                }, false);
                btnCloseRegister.Background = Theme.Err;
                btnsClosure.Children.Add(btnCloseRegister);

                aStack.Children.Add(btnsClosure);
                arqueoCard.Child = aStack;
                Grid.SetColumn(arqueoCard, 0);
                splitGrid.Children.Add(arqueoCard);

                // Tarjeta de Ranking de Platos Vendidos en la Caja Actual
                var rankCard = new Border
                {
                    Background = Theme.Card,
                    BorderBrush = Theme.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = Theme.CardRadius,
                    Padding = new Thickness(18)
                };
                var rStack = new StackPanel();
                rStack.Children.Add(new TextBlock
                {
                    Text = "📊 Platos Vendidos en esta Caja",
                    FontSize = 15,
                    FontWeight = FontWeights.Bold,
                    Foreground = Theme.Text,
                    Margin = new Thickness(0, 0, 0, 10)
                });

                var ranking = sessionOrders
                    .Where(o => o.Status == "completado")
                    .SelectMany(o => o.Items)
                    .GroupBy(i => i.ProductName)
                    .Select(g => new { Name = g.Key, Qty = g.Sum(x => x.Quantity), Amt = g.Sum(x => x.Subtotal) })
                    .OrderByDescending(x => x.Qty)
                    .ToList();

                if (ranking.Count == 0)
                {
                    rStack.Children.Add(new TextBlock { Text = "Aún no hay ventas registradas en esta caja.", Foreground = Theme.Muted, FontSize = 12 });
                }
                else
                {
                    foreach (var r in ranking)
                    {
                        var rLine = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                        var amtTxt = new TextBlock { Text = PosStore.FormatMoney(r.Amt), FontWeight = FontWeights.Bold, Foreground = Theme.BrandDark };
                        DockPanel.SetDock(amtTxt, Dock.Right);
                        rLine.Children.Add(amtTxt);
                        rLine.Children.Add(new TextBlock { Text = r.Qty + "x  " + r.Name, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text });
                        rStack.Children.Add(rLine);
                    }
                }

                rankCard.Child = rStack;
                Grid.SetColumn(rankCard, 2);
                splitGrid.Children.Add(rankCard);
                _cajaActualHostStack.Children.Add(splitGrid);
            }

            // Historial de Cajas Anteriores
            var closedSessions = _db.Sessions.Where(s => s.Status == "cerrada").OrderByDescending(s => s.Id).ToList();
            if (closedSessions.Count > 0)
            {
                _cajaActualHostStack.Children.Add(new TextBlock
                {
                    Text = "Historial de Cajas Cerradas Anteriormente",
                    FontSize = 15,
                    FontWeight = FontWeights.Bold,
                    Foreground = Theme.Text,
                    Margin = new Thickness(0, 4, 0, 8)
                });

                foreach (var cs in closedSessions)
                {
                    var csCard = new Border
                    {
                        Background = Theme.Card,
                        BorderBrush = Theme.Border,
                        BorderThickness = new Thickness(1),
                        CornerRadius = Theme.CardRadius,
                        Padding = new Thickness(14, 10, 14, 10),
                        Margin = new Thickness(0, 0, 0, 6)
                    };
                    var csDock = new DockPanel();
                    var csCopy = cs;
                    var btnRep = CreateGhostButton("🖨️ Reimprimir Cierre", () =>
                    {
                        var sOrders = _db.Orders.Where(o => o.SessionId == csCopy.Id).ToList();
                        try
                        {
                            EscPosPrinter.PrintCashClosure(csCopy, sOrders, _db.Settings);
                            ShowToast("🖨️ Cierre de Caja #" + csCopy.Id.ToString("000") + " reimpreso.");
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(ex.Message, "Error al imprimir", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    });
                    DockPanel.SetDock(btnRep, Dock.Right);
                    csDock.Children.Add(btnRep);

                    csDock.Children.Add(new TextBlock
                    {
                        Text = string.Format("Caja #{0:000} ({1} - Menú: {2})   ·   {3} → {4}   ·   Pedidos: {5}   ·   Total: {6}",
                            cs.Id, cs.EventName, cs.MenuGroupName ?? "General", cs.OpenedAt, cs.ClosedAt, cs.OrdersCount, PosStore.FormatMoney(cs.TotalSales)),
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = Theme.Text,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    csCard.Child = csDock;
                    _cajaActualHostStack.Children.Add(csCard);
                }
            }
        }

        // =====================================================================
        // VISTA 2: TOMAR PEDIDO (USA EL MENÚ DEL GRUPO/TIPO DE EVENTO ACTIVO)
        // =====================================================================
        private UIElement BuildPosView()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(430) });

            var leftDock = new DockPanel { LastChildFill = true };
            Grid.SetColumn(leftDock, 0);

            var searchCard = new Border
            {
                Background = Theme.Card,
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.CardRadius,
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 10),
                Effect = Theme.CardShadow()
            };
            DockPanel.SetDock(searchCard, Dock.Top);

            var searchStack = new StackPanel();
            var searchTopRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };

            var btnManageMenu = CreateGhostButton("🍽️ Editar Menú", () => SwitchSection("menu"));
            DockPanel.SetDock(btnManageMenu, Dock.Right);
            searchTopRow.Children.Add(btnManageMenu);

            _posActiveMenuTitle = new TextBlock
            {
                Text = "Menú: Almuerzo y Buffet Araucana",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.BrandDark,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            DockPanel.SetDock(_posActiveMenuTitle, Dock.Left);
            searchTopRow.Children.Add(_posActiveMenuTitle);

            var searchBoxBorder = new Border
            {
                Background = Theme.Bg,
                BorderBrush = Theme.BorderStrong,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.BtnRadius,
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 10, 0)
            };
            var searchDock = new DockPanel();
            searchDock.Children.Add(new TextBlock
            {
                Text = "🔍",
                FontSize = 13,
                Foreground = Theme.Muted,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });
            var searchInput = new TextBox
            {
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                FontSize = 13,
                Foreground = Theme.Text,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            searchInput.TextChanged += (s, e) =>
            {
                _searchQuery = searchInput.Text.Trim();
                RenderProductCards();
            };
            searchDock.Children.Add(searchInput);
            searchBoxBorder.Child = searchDock;
            searchTopRow.Children.Add(searchBoxBorder);
            searchStack.Children.Add(searchTopRow);

            // Botones de categoría con bordes rectangulares redondeados uniformes (sin óvalos)
            _catButtonsPanel = new StackPanel { Orientation = Orientation.Horizontal };
            searchStack.Children.Add(_catButtonsPanel);
            searchCard.Child = searchStack;
            leftDock.Children.Add(searchCard);

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                PanningMode = PanningMode.VerticalOnly
            };
            _productsWrap = new WrapPanel { Orientation = Orientation.Horizontal };
            scrollViewer.Content = _productsWrap;
            leftDock.Children.Add(scrollViewer);
            grid.Children.Add(leftDock);

            // DERECHA: Carrito y Cobro
            var cartCard = new Border
            {
                Background = Theme.Card,
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.CardRadius,
                Padding = new Thickness(16),
                Effect = Theme.CardShadow()
            };
            Grid.SetColumn(cartCard, 2);

            var cartDock = new DockPanel { LastChildFill = true };

            var cartHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            DockPanel.SetDock(cartHeader, Dock.Top);

            var btnClearCart = CreateGhostButton("Vaciar", () =>
            {
                _cart.Clear();
                RenderCart();
            });
            DockPanel.SetDock(btnClearCart, Dock.Right);
            cartHeader.Children.Add(btnClearCart);

            cartHeader.Children.Add(new TextBlock
            {
                Text = "Pedido Actual",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Text,
                VerticalAlignment = VerticalAlignment.Center
            });
            cartDock.Children.Add(cartHeader);

            var checkoutStack = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            DockPanel.SetDock(checkoutStack, Dock.Bottom);

            checkoutStack.Children.Add(new TextBlock
            {
                Text = "NOTA PARA COCINA (OPCIONAL)",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Muted,
                Margin = new Thickness(0, 0, 0, 4)
            });
            _notesBox = CreateStyledTextBox("");
            _notesBox.Margin = new Thickness(0, 0, 0, 10);
            checkoutStack.Children.Add(_notesBox);

            checkoutStack.Children.Add(new TextBlock
            {
                Text = "MEDIO DE PAGO",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Muted,
                Margin = new Thickness(0, 0, 0, 6)
            });

            var payGrid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 10) };
            foreach (var pm in new[] { "Efectivo", "Mercado Pago", "Tarjeta" })
            {
                string icon = pm == "Efectivo" ? "💵 " : (pm == "Mercado Pago" ? "📱 " : "💳 ");
                var card = new Border
                {
                    CornerRadius = Theme.BtnRadius,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8, 8, 8, 8),
                    Margin = new Thickness(2, 0, 2, 0),
                    Cursor = Cursors.Hand
                };
                var lbl = new TextBlock
                {
                    Text = icon + pm,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                card.Child = lbl;
                string methodCopy = pm;
                card.MouseLeftButtonUp += (s, e) => SelectPaymentMethod(methodCopy);
                _payMethodCards[pm] = card;
                payGrid.Children.Add(card);
            }
            checkoutStack.Children.Add(payGrid);

            _cashContextPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            var cashBoxBorder = new Border
            {
                Background = Theme.Bg,
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.BtnRadius,
                Padding = new Thickness(10, 8, 10, 8)
            };
            var cashInner = new StackPanel();
            var cashRow1 = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };

            _vueltoLabel = new TextBlock
            {
                Text = "Vuelto: $0",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Ok,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(_vueltoLabel, Dock.Right);
            cashRow1.Children.Add(_vueltoLabel);

            cashRow1.Children.Add(new TextBlock
            {
                Text = "Paga con $:",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Text,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });

            _cashInputBox = new TextBox
            {
                Width = 95,
                Padding = new Thickness(6, 4, 6, 4),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                BorderBrush = Theme.BorderStrong,
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            _cashInputBox.TextChanged += (s, e) => UpdateTotalsAndChange();
            cashRow1.Children.Add(_cashInputBox);
            cashInner.Children.Add(cashRow1);

            var quickBillsGrid = new UniformGrid { Columns = 6 };
            foreach (var qb in new[] { Tuple.Create("Exacto", 0m), Tuple.Create("$5k", 5000m), Tuple.Create("$10k", 10000m), Tuple.Create("$20k", 20000m), Tuple.Create("$30k", 30000m), Tuple.Create("$50k", 50000m) })
            {
                decimal val = qb.Item2;
                var chip = CreateGhostButton(qb.Item1, () =>
                {
                    decimal total = _cart.Sum(c => c.Subtotal);
                    _cashInputBox.Text = (val == 0 ? total : val).ToString("0");
                });
                chip.Padding = new Thickness(4, 4, 4, 4);
                chip.Margin = new Thickness(1);
                ((TextBlock)chip.Child).FontSize = 11;
                quickBillsGrid.Children.Add(chip);
            }
            cashInner.Children.Add(quickBillsGrid);
            cashBoxBorder.Child = cashInner;
            _cashContextPanel.Children.Add(cashBoxBorder);
            checkoutStack.Children.Add(_cashContextPanel);

            _mpContextPanel = new Border
            {
                Background = Theme.BrandBg,
                BorderBrush = Theme.BrandSoft,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.BtnRadius,
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 10),
                Visibility = Visibility.Collapsed
            };
            _mpAliasLabel = new TextBlock
            {
                Text = "Alias Mercado Pago:  araucana24",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.BrandDark,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _mpContextPanel.Child = _mpAliasLabel;
            checkoutStack.Children.Add(_mpContextPanel);

            var totalBorder = new Border
            {
                Background = Theme.BrandBg,
                BorderBrush = Theme.Brand,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.BtnRadius,
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var totDock = new DockPanel();
            _totalAmountLabel = new TextBlock
            {
                Text = "$0",
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.BrandDark,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(_totalAmountLabel, Dock.Right);
            totDock.Children.Add(_totalAmountLabel);
            totDock.Children.Add(new TextBlock
            {
                Text = "TOTAL A COBRAR",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.BrandDark,
                VerticalAlignment = VerticalAlignment.Center
            });
            totalBorder.Child = totDock;
            checkoutStack.Children.Add(totalBorder);

            var btnCharge = CreatePrimaryButton("🖨️  COBRAR E IMPRIMIR TICKET (F12)", SubmitOrder, false);
            btnCharge.Padding = new Thickness(14, 13, 14, 13);
            checkoutStack.Children.Add(btnCharge);

            cartDock.Children.Add(checkoutStack);

            var cartScrollBorder = new Border
            {
                Background = Theme.Bg,
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.BtnRadius,
                Padding = new Thickness(6)
            };
            var cartScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _cartListPanel = new StackPanel();
            cartScroll.Content = _cartListPanel;
            cartScrollBorder.Child = cartScroll;
            cartDock.Children.Add(cartScrollBorder);

            cartCard.Child = cartDock;
            grid.Children.Add(cartCard);

            SelectPaymentMethod("Efectivo");
            return grid;
        }

        private void RenderCategoryButtons()
        {
            _catButtonsPanel.Children.Clear();
            int activeGroupId = GetCurrentActiveGroupId();
            var groupProds = _db.Products.Where(p => p.GroupId == activeGroupId && p.Active).ToList();

            var cats = new List<string> { "Todos" };
            foreach (var c in groupProds.Select(p => p.Category).Distinct())
            {
                if (!cats.Contains(c)) cats.Add(c);
            }
            if (!cats.Contains(_selectedCategory)) _selectedCategory = "Todos";

            foreach (var c in cats)
            {
                bool active = _selectedCategory == c;
                var btn = new Border
                {
                    Background = active ? Theme.Brand : Theme.Bg,
                    BorderBrush = active ? Theme.Brand : Theme.BorderStrong,
                    BorderThickness = new Thickness(1),
                    CornerRadius = Theme.BtnRadius, // Mismo borde redondeado rectangular, no ovalado
                    Padding = new Thickness(14, 6, 14, 6),
                    Margin = new Thickness(0, 0, 6, 0),
                    Cursor = Cursors.Hand
                };
                var txt = new TextBlock
                {
                    Text = c,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = active ? Brushes.White : Theme.Text
                };
                btn.Child = txt;
                string catCopy = c;
                btn.MouseLeftButtonUp += (s, e) =>
                {
                    _selectedCategory = catCopy;
                    RenderCategoryButtons();
                    RenderProductCards();
                };
                _catButtonsPanel.Children.Add(btn);
            }
        }

        private void RenderProductCards()
        {
            _productsWrap.Children.Clear();
            int activeGroupId = GetCurrentActiveGroupId();
            var activeGroup = GetCurrentActiveGroup();
            if (_posActiveMenuTitle != null)
                _posActiveMenuTitle.Text = "Menú: " + activeGroup.Name;

            string q = (_searchQuery ?? "").ToLowerInvariant();

            var filtered = _db.Products.Where(p =>
                p.GroupId == activeGroupId &&
                p.Active &&
                (_selectedCategory == "Todos" || p.Category == _selectedCategory) &&
                (string.IsNullOrEmpty(q) || p.Name.ToLowerInvariant().Contains(q) || p.Category.ToLowerInvariant().Contains(q))
            ).ToList();

            if (filtered.Count == 0)
            {
                _productsWrap.Children.Add(new TextBlock
                {
                    Text = "No hay platos cargados en esta categoría o grupo de menú.",
                    FontSize = 13,
                    Foreground = Theme.Muted,
                    Margin = new Thickness(16, 24, 16, 16)
                });
                return;
            }

            foreach (var prod in filtered)
            {
                bool outOfStock = prod.StockEnabled && prod.StockQty <= 0;

                var card = new Border
                {
                    Width = 215,
                    Background = Theme.Card,
                    BorderBrush = outOfStock ? Theme.Err : Theme.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = Theme.CardRadius,
                    Margin = new Thickness(0, 0, 10, 10),
                    Padding = new Thickness(10),
                    Cursor = outOfStock ? Cursors.No : Cursors.Hand,
                    Opacity = outOfStock ? 0.65 : 1.0,
                    Effect = Theme.CardShadow()
                };

                card.MouseEnter += (s, e) =>
                {
                    if (!outOfStock)
                    {
                        card.BorderBrush = Theme.Brand;
                        card.Background = Theme.BrandHover;
                    }
                };
                card.MouseLeave += (s, e) =>
                {
                    if (!outOfStock)
                    {
                        card.BorderBrush = Theme.Border;
                        card.Background = Theme.Card;
                    }
                };

                var stack = new StackPanel();

                var imgHost = new Border
                {
                    Height = 105,
                    CornerRadius = Theme.BtnRadius,
                    Background = Theme.Bg,
                    ClipToBounds = true,
                    Margin = new Thickness(0, 0, 0, 8)
                };

                var bmp = Theme.LoadImageCached(prod.ImagePath);
                if (bmp != null)
                {
                    imgHost.Child = new Image
                    {
                        Source = bmp,
                        Stretch = Stretch.UniformToFill,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                }
                else
                {
                    imgHost.Child = new TextBlock
                    {
                        Text = "🍽️",
                        FontSize = 32,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                }
                stack.Children.Add(imgHost);

                var metaDock = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                if (prod.StockEnabled)
                {
                    var stBadge = new Border
                    {
                        Background = outOfStock ? Theme.ErrBg : Theme.OkBg,
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(6, 2, 6, 2)
                    };
                    stBadge.Child = new TextBlock
                    {
                        Text = outOfStock ? "AGOTADO" : ("Stock: " + prod.StockQty),
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        Foreground = outOfStock ? Theme.Err : Theme.Ok
                    };
                    DockPanel.SetDock(stBadge, Dock.Right);
                    metaDock.Children.Add(stBadge);
                }

                metaDock.Children.Add(new TextBlock
                {
                    Text = prod.Category.ToUpperInvariant(),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = Theme.Muted,
                    VerticalAlignment = VerticalAlignment.Center
                });
                stack.Children.Add(metaDock);

                stack.Children.Add(new TextBlock
                {
                    Text = prod.Name,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = Theme.Text,
                    TextWrapping = TextWrapping.Wrap,
                    Height = 36
                });

                var footDock = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
                var btnPhoto = CreateIconButton("📷", "Cambiar foto de este plato", () => PickPhotoForProduct(prod));
                btnPhoto.Width = 28;
                btnPhoto.Height = 28;
                DockPanel.SetDock(btnPhoto, Dock.Right);
                footDock.Children.Add(btnPhoto);

                footDock.Children.Add(new TextBlock
                {
                    Text = PosStore.FormatMoney(prod.Price),
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = Theme.BrandDark,
                    VerticalAlignment = VerticalAlignment.Center
                });
                stack.Children.Add(footDock);

                card.Child = stack;

                var prodCopy = prod;
                card.MouseLeftButtonUp += (s, e) =>
                {
                    if (e.OriginalSource is TextBlock && ((TextBlock)e.OriginalSource).Text == "📷")
                        return;
                    AddToCart(prodCopy);
                };

                _productsWrap.Children.Add(card);
            }
        }

        // =====================================================================
        // VISTA 3: "MENÚ" (SEPARADO POR GRUPOS / TIPOS DE EVENTO)
        // =====================================================================
        private UIElement BuildMenuView()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(310) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // COLUMNA IZQUIERDA: Grupos / Tipos de Evento
            var leftCard = new Border
            {
                Background = Theme.Card,
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.CardRadius,
                Padding = new Thickness(14),
                Effect = Theme.CardShadow()
            };
            Grid.SetColumn(leftCard, 0);

            var leftDock = new DockPanel { LastChildFill = true };
            var leftHeader = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            DockPanel.SetDock(leftHeader, Dock.Top);

            leftHeader.Children.Add(new TextBlock
            {
                Text = "Tipos de Evento (Grupos)",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Text
            });
            leftHeader.Children.Add(new TextBlock
            {
                Text = "Creá distintos tipos de evento y armá el menú propio de cada uno.",
                FontSize = 11,
                Foreground = Theme.Muted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 10)
            });

            var btnNewGroup = CreatePrimaryButton("+ Nuevo Tipo de Evento / Grupo", () => OpenGroupEditorModal(null), true);
            leftHeader.Children.Add(btnNewGroup);
            leftDock.Children.Add(leftHeader);

            var groupsScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _groupsListStack = new StackPanel();
            groupsScroll.Content = _groupsListStack;
            leftDock.Children.Add(groupsScroll);
            leftCard.Child = leftDock;
            grid.Children.Add(leftCard);

            // COLUMNA DERECHA: Platos del Grupo / Tipo de Evento Seleccionado
            var rightDock = new DockPanel { LastChildFill = true };
            Grid.SetColumn(rightDock, 2);

            var rightTopCard = new Border
            {
                Background = Theme.Card,
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.CardRadius,
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 12),
                Effect = Theme.CardShadow()
            };
            DockPanel.SetDock(rightTopCard, Dock.Top);

            var rtDock = new DockPanel();
            var rtButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(rtButtons, Dock.Right);

            var btnUseGroupInCaja = CreateGhostButton("✓ Activar este Menú en la Caja Actual", () =>
            {
                var grp = _db.MenuGroups.FirstOrDefault(g => g.Id == _editingGroupId);
                if (grp == null) return;
                _db.Settings.ActiveMenuGroupId = grp.Id;
                var activeSess = PosStore.GetActiveSession(_db);
                if (activeSess != null)
                {
                    activeSess.MenuGroupId = grp.Id;
                    activeSess.MenuGroupName = grp.Name;
                }
                PosStore.Save(_db);
                _cart.Clear();
                RefreshAllUI();
                RenderMenuSection();
                ShowToast("✓ El menú '" + grp.Name + "' quedó activo en la Caja Actual.");
            });
            btnUseGroupInCaja.Margin = new Thickness(0, 0, 8, 0);
            rtButtons.Children.Add(btnUseGroupInCaja);

            var btnAddDishToGroup = CreatePrimaryButton("+ Nuevo Plato en este Menú", () => OpenProductEditorModal(null), false);
            rtButtons.Children.Add(btnAddDishToGroup);
            rtDock.Children.Add(rtButtons);

            var rtTitles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _selectedGroupHeaderTitle = new TextBlock
            {
                Text = "Menú del Evento",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Text
            };
            _selectedGroupHeaderSub = new TextBlock
            {
                Text = "Platos, precios, fotos y stock asignados a este tipo de evento",
                FontSize = 12,
                Foreground = Theme.Muted
            };
            rtTitles.Children.Add(_selectedGroupHeaderTitle);
            rtTitles.Children.Add(_selectedGroupHeaderSub);
            rtDock.Children.Add(rtTitles);

            rightTopCard.Child = rtDock;
            rightDock.Children.Add(rightTopCard);

            var dishesScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _groupDishesStack = new StackPanel();
            dishesScroll.Content = _groupDishesStack;
            rightDock.Children.Add(dishesScroll);

            grid.Children.Add(rightDock);
            return grid;
        }

        private void RenderMenuSection()
        {
            if (!_db.MenuGroups.Any(g => g.Id == _editingGroupId))
                _editingGroupId = _db.MenuGroups.First().Id;

            int activeCajaGroupId = GetCurrentActiveGroupId();

            // 1. Renderizar lista de Grupos / Tipos de Evento a la izquierda
            _groupsListStack.Children.Clear();
            foreach (var grp in _db.MenuGroups)
            {
                bool isSelected = grp.Id == _editingGroupId;
                bool isInUse = grp.Id == activeCajaGroupId;
                int dishesCount = _db.Products.Count(p => p.GroupId == grp.Id);

                var card = new Border
                {
                    Background = isSelected ? Theme.BrandBg : Theme.Bg,
                    BorderBrush = isSelected ? Theme.Brand : Theme.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = Theme.CardRadius,
                    Padding = new Thickness(12, 10, 12, 10),
                    Margin = new Thickness(0, 0, 0, 8),
                    Cursor = Cursors.Hand
                };

                var st = new StackPanel();
                var headRow = new DockPanel();

                var grpCopy = grp;
                var actBtns = new StackPanel { Orientation = Orientation.Horizontal };
                DockPanel.SetDock(actBtns, Dock.Right);

                var btnEditG = CreateIconButton("✏️", "Editar nombre del tipo de evento", () => OpenGroupEditorModal(grpCopy));
                btnEditG.Width = 26; btnEditG.Height = 26;
                btnEditG.Margin = new Thickness(0, 0, 4, 0);
                actBtns.Children.Add(btnEditG);

                if (_db.MenuGroups.Count > 1)
                {
                    var btnDelG = CreateIconButton("🗑️", "Eliminar este tipo de evento", () =>
                    {
                        if (MessageBox.Show("¿Eliminar el grupo '" + grpCopy.Name + "' y todos sus platos?",
                            "Eliminar Tipo de Evento", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                        {
                            _db.Products.RemoveAll(p => p.GroupId == grpCopy.Id);
                            _db.MenuGroups.RemoveAll(g => g.Id == grpCopy.Id);
                            _editingGroupId = _db.MenuGroups.First().Id;
                            if (_db.Settings.ActiveMenuGroupId == grpCopy.Id)
                                _db.Settings.ActiveMenuGroupId = _editingGroupId;
                            PosStore.Save(_db);
                            RefreshAllUI();
                            RenderMenuSection();
                        }
                    });
                    btnDelG.Width = 26; btnDelG.Height = 26;
                    actBtns.Children.Add(btnDelG);
                }
                headRow.Children.Add(actBtns);

                headRow.Children.Add(new TextBlock
                {
                    Text = grp.Name,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = isSelected ? Theme.BrandDark : Theme.Text,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                });
                st.Children.Add(headRow);

                var metaRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                metaRow.Children.Add(new TextBlock
                {
                    Text = dishesCount + " platos en el menú",
                    FontSize = 11,
                    Foreground = Theme.Muted,
                    VerticalAlignment = VerticalAlignment.Center
                });

                if (isInUse)
                {
                    var activeTag = new Border
                    {
                        Background = Theme.OkBg,
                        BorderBrush = Theme.Ok,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(6, 1, 6, 1),
                        Margin = new Thickness(8, 0, 0, 0)
                    };
                    activeTag.Child = new TextBlock
                    {
                        Text = "ACTIVO EN CAJA",
                        FontSize = 9,
                        FontWeight = FontWeights.Bold,
                        Foreground = Theme.Ok
                    };
                    metaRow.Children.Add(activeTag);
                }
                st.Children.Add(metaRow);

                card.Child = st;
                card.MouseLeftButtonUp += (s, e) =>
                {
                    _editingGroupId = grpCopy.Id;
                    RenderMenuSection();
                };

                _groupsListStack.Children.Add(card);
            }

            // 2. Renderizar platos del Grupo seleccionado a la derecha
            var currentGrp = _db.MenuGroups.First(g => g.Id == _editingGroupId);
            _selectedGroupHeaderTitle.Text = "Menú: " + currentGrp.Name;
            _selectedGroupHeaderSub.Text = string.IsNullOrWhiteSpace(currentGrp.Description)
                ? "Administrá los platos, fotos y precios de este tipo de evento"
                : currentGrp.Description;

            _groupDishesStack.Children.Clear();
            var groupProducts = _db.Products.Where(p => p.GroupId == _editingGroupId).ToList();

            if (groupProducts.Count == 0)
            {
                var emptyCard = new Border
                {
                    Background = Theme.Card,
                    BorderBrush = Theme.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = Theme.CardRadius,
                    Padding = new Thickness(24)
                };
                emptyCard.Child = new TextBlock
                {
                    Text = "Este tipo de evento todavía no tiene platos cargados. Hacé clic en '+ Nuevo Plato en este Menú' arriba a la derecha.",
                    FontSize = 13,
                    Foreground = Theme.Muted
                };
                _groupDishesStack.Children.Add(emptyCard);
                return;
            }

            foreach (var prod in groupProducts)
            {
                var rowCard = new Border
                {
                    Background = Theme.Card,
                    BorderBrush = Theme.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = Theme.CardRadius,
                    Padding = new Thickness(12, 10, 12, 10),
                    Margin = new Thickness(0, 0, 0, 8)
                };

                var dock = new DockPanel();
                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(actions, Dock.Right);

                var prodCopy = prod;
                var btnPhoto = CreateGhostButton("📷 Foto", () => PickPhotoForProduct(prodCopy));
                btnPhoto.Margin = new Thickness(0, 0, 6, 0);
                actions.Children.Add(btnPhoto);

                var btnEdit = CreateGhostButton("✏️ Editar", () => OpenProductEditorModal(prodCopy));
                btnEdit.Margin = new Thickness(0, 0, 6, 0);
                actions.Children.Add(btnEdit);

                var btnDel = CreateGhostButton("🗑️", () =>
                {
                    if (MessageBox.Show("¿Eliminar '" + prodCopy.Name + "' de este menú?", "Eliminar Plato", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                    {
                        _db.Products.RemoveAll(p => p.Id == prodCopy.Id);
                        PosStore.Save(_db);
                        RefreshAllUI();
                        RenderMenuSection();
                    }
                });
                ((TextBlock)btnDel.Child).Foreground = Theme.Err;
                actions.Children.Add(btnDel);
                dock.Children.Add(actions);

                var thumbBorder = new Border
                {
                    Width = 92,
                    Height = 54,
                    CornerRadius = Theme.BtnRadius,
                    Background = Theme.Bg,
                    ClipToBounds = true,
                    Margin = new Thickness(0, 0, 14, 0)
                };
                var bmp = Theme.LoadImageCached(prod.ImagePath);
                if (bmp != null)
                    thumbBorder.Child = new Image { Source = bmp, Stretch = Stretch.UniformToFill };
                DockPanel.SetDock(thumbBorder, Dock.Left);
                dock.Children.Add(thumbBorder);

                var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
                titleRow.Children.Add(new TextBlock
                {
                    Text = prod.Name,
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = prod.Active ? Theme.Text : Theme.Faint
                });

                var catBadge = new Border
                {
                    Background = Theme.BrandBg,
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                catBadge.Child = new TextBlock { Text = prod.Category, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Theme.BrandDark };
                titleRow.Children.Add(catBadge);
                info.Children.Add(titleRow);

                string stockInfo = prod.StockEnabled ? ("Stock controlado: " + prod.StockQty + " u.") : "Stock libre";
                info.Children.Add(new TextBlock
                {
                    Text = string.Format("Precio: {0}   ·   {1}   ·   {2}", PosStore.FormatMoney(prod.Price), stockInfo, prod.Description),
                    FontSize = 12,
                    Foreground = Theme.Muted,
                    Margin = new Thickness(0, 2, 0, 0)
                });
                dock.Children.Add(info);

                rowCard.Child = dock;
                _groupDishesStack.Children.Add(rowCard);
            }
        }

        private void OpenGroupEditorModal(MenuGroup existing)
        {
            var win = new Window
            {
                Title = existing == null ? "Nuevo Tipo de Evento / Grupo de Menú" : "Editar Tipo de Evento",
                Width = 440,
                Height = 320,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = Theme.Card,
                ResizeMode = ResizeMode.NoResize,
                FontFamily = new FontFamily("Segoe UI")
            };

            var stack = new StackPanel { Margin = new Thickness(22) };
            stack.Children.Add(new TextBlock
            {
                Text = existing == null ? "Crear Nuevo Tipo de Evento" : "Editar Tipo de Evento",
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Text,
                Margin = new Thickness(0, 0, 0, 12)
            });

            stack.Children.Add(new TextBlock { Text = "Nombre del Tipo de Evento (ej: Peña Folklórica, Cena Fin de Año):", FontSize = 12, Foreground = Theme.Muted, Margin = new Thickness(0, 0, 0, 4) });
            var nameBox = CreateStyledTextBox("");
            nameBox.Text = existing != null ? existing.Name : "";
            stack.Children.Add(nameBox);

            stack.Children.Add(new TextBlock { Text = "Descripción (opcional):", FontSize = 12, Foreground = Theme.Muted, Margin = new Thickness(0, 10, 0, 4) });
            var descBox = CreateStyledTextBox("");
            descBox.Text = existing != null ? existing.Description : "";
            stack.Children.Add(descBox);

            CheckBox chkCopyFromCurrent = null;
            if (existing == null)
            {
                chkCopyFromCurrent = new CheckBox
                {
                    Content = "Copiar platos actuales como base para editarlos rápido",
                    IsChecked = false,
                    Margin = new Thickness(0, 12, 0, 4),
                    FontSize = 12
                };
                stack.Children.Add(chkCopyFromCurrent);
            }

            var btnSave = CreatePrimaryButton("💾 Guardar Tipo de Evento", () =>
            {
                if (string.IsNullOrWhiteSpace(nameBox.Text)) return;
                if (existing != null)
                {
                    existing.Name = nameBox.Text.Trim();
                    existing.Description = descBox.Text.Trim();
                }
                else
                {
                    int newGroupId = _db.MenuGroups.Count == 0 ? 1 : _db.MenuGroups.Max(g => g.Id) + 1;
                    var newGrp = new MenuGroup
                    {
                        Id = newGroupId,
                        Name = nameBox.Text.Trim(),
                        Description = descBox.Text.Trim()
                    };
                    _db.MenuGroups.Add(newGrp);

                    if (chkCopyFromCurrent != null && chkCopyFromCurrent.IsChecked == true)
                    {
                        int nextProdId = _db.Products.Count == 0 ? 1 : _db.Products.Max(p => p.Id) + 1;
                        var sourceProds = _db.Products.Where(p => p.GroupId == _editingGroupId).ToList();
                        foreach (var sp in sourceProds)
                        {
                            _db.Products.Add(new ProductItem
                            {
                                Id = nextProdId++,
                                GroupId = newGroupId,
                                Name = sp.Name,
                                Category = sp.Category,
                                Price = sp.Price,
                                StockEnabled = sp.StockEnabled,
                                StockQty = sp.StockQty,
                                IsCombo = sp.IsCombo,
                                Description = sp.Description,
                                ImagePath = sp.ImagePath,
                                Active = sp.Active
                            });
                        }
                    }

                    _editingGroupId = newGroupId;
                }

                PosStore.Save(_db);
                RefreshAllUI();
                RenderMenuSection();
                win.Close();
            }, false);
            btnSave.Margin = new Thickness(0, 16, 0, 0);
            stack.Children.Add(btnSave);

            win.Content = stack;
            win.ShowDialog();
        }

        // =====================================================================
        // CARRITO, COBRO Y EDICIÓN DE PLATOS
        // =====================================================================
        private void AddToCart(ProductItem prod)
        {
            var activeSession = PosStore.GetActiveSession(_db);
            if (activeSession == null)
            {
                if (MessageBox.Show(
                    "La caja actual está cerrada.\n¿Querés abrir una Nueva Caja ahora para empezar a cobrar?",
                    "Caja Cerrada",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    OpenNewSessionModal();
                }
                return;
            }

            if (prod.StockEnabled && prod.StockQty <= 0)
            {
                MessageBox.Show("'" + prod.Name + "' está sin stock disponible.", "Sin Stock", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var existing = _cart.FirstOrDefault(c => c.Product.Id == prod.Id);
            if (existing != null)
            {
                if (prod.StockEnabled && existing.Quantity + 1 > prod.StockQty)
                {
                    MessageBox.Show("Stock máximo disponible alcanzado (" + prod.StockQty + " u.).", "Límite de Stock", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                existing.Quantity++;
            }
            else
            {
                _cart.Add(new CartEntry { Product = prod, Quantity = 1 });
            }

            RenderCart();
        }

        private void RenderCart()
        {
            _cartListPanel.Children.Clear();
            if (_cart.Count == 0)
            {
                _cartListPanel.Children.Add(new TextBlock
                {
                    Text = "Hacé clic en los platos de la izquierda para armar el pedido.",
                    FontSize = 12,
                    Foreground = Theme.Faint,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(20, 45, 20, 20)
                });
            }
            else
            {
                foreach (var entry in _cart.ToList())
                {
                    var rowBorder = new Border
                    {
                        Background = Theme.Card,
                        BorderBrush = Theme.Border,
                        BorderThickness = new Thickness(1),
                        CornerRadius = Theme.BtnRadius,
                        Padding = new Thickness(8, 6, 8, 6),
                        Margin = new Thickness(0, 0, 0, 5)
                    };

                    var dock = new DockPanel();

                    var subTxt = new TextBlock
                    {
                        Text = PosStore.FormatMoney(entry.Subtotal),
                        FontSize = 13,
                        FontWeight = FontWeights.Bold,
                        Foreground = Theme.Text,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0, 2, 0)
                    };
                    DockPanel.SetDock(subTxt, Dock.Right);
                    dock.Children.Add(subTxt);

                    var qtyStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0) };
                    DockPanel.SetDock(qtyStack, Dock.Left);

                    var entryCopy = entry;
                    var btnDec = CreateIconButton("−", "Restar 1", () =>
                    {
                        entryCopy.Quantity--;
                        if (entryCopy.Quantity <= 0) _cart.Remove(entryCopy);
                        RenderCart();
                    });
                    btnDec.Width = 26; btnDec.Height = 26;

                    var lblQty = new TextBlock
                    {
                        Text = entry.Quantity.ToString(),
                        Width = 24,
                        TextAlignment = TextAlignment.Center,
                        FontSize = 13,
                        FontWeight = FontWeights.Bold,
                        Foreground = Theme.BrandDark,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    var btnInc = CreateIconButton("+", "Sumar 1", () =>
                    {
                        if (entryCopy.Product.StockEnabled && entryCopy.Quantity + 1 > entryCopy.Product.StockQty)
                        {
                            MessageBox.Show("Solo hay " + entryCopy.Product.StockQty + " u. disponibles.", "Stock", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                        entryCopy.Quantity++;
                        RenderCart();
                    });
                    btnInc.Width = 26; btnInc.Height = 26;

                    qtyStack.Children.Add(btnDec);
                    qtyStack.Children.Add(lblQty);
                    qtyStack.Children.Add(btnInc);
                    dock.Children.Add(qtyStack);

                    dock.Children.Add(new TextBlock
                    {
                        Text = entry.Product.Name,
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = Theme.Text,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });

                    rowBorder.Child = dock;
                    _cartListPanel.Children.Add(rowBorder);
                }
            }

            UpdateTotalsAndChange();
        }

        private void SelectPaymentMethod(string method)
        {
            _paymentMethod = method;
            foreach (var kv in _payMethodCards)
            {
                bool active = kv.Key == method;
                kv.Value.Background = active ? Theme.BrandBg : Theme.Bg;
                kv.Value.BorderBrush = active ? Theme.Brand : Theme.Border;
                var tb = (TextBlock)kv.Value.Child;
                tb.Foreground = active ? Theme.BrandDark : Theme.Muted;
            }

            if (_cashContextPanel != null)
                _cashContextPanel.Visibility = method == "Efectivo" ? Visibility.Visible : Visibility.Collapsed;
            if (_mpContextPanel != null)
            {
                _mpContextPanel.Visibility = method == "Mercado Pago" ? Visibility.Visible : Visibility.Collapsed;
                _mpAliasLabel.Text = "Alias Mercado Pago:  " + (_db.Settings.MpAlias ?? "araucana24");
            }

            UpdateTotalsAndChange();
        }

        private void UpdateTotalsAndChange()
        {
            decimal total = _cart.Sum(c => c.Subtotal);
            if (_totalAmountLabel != null)
                _totalAmountLabel.Text = PosStore.FormatMoney(total);

            if (_vueltoLabel == null || _cashInputBox == null) return;

            decimal paid = 0;
            decimal.TryParse(_cashInputBox.Text.Trim(), out paid);

            if (paid <= 0 || total <= 0)
            {
                _vueltoLabel.Text = "Vuelto: $0";
                _vueltoLabel.Foreground = Theme.Ok;
            }
            else if (paid < total)
            {
                _vueltoLabel.Text = "Faltan " + PosStore.FormatMoney(total - paid);
                _vueltoLabel.Foreground = Theme.Err;
            }
            else
            {
                _vueltoLabel.Text = "Vuelto: " + PosStore.FormatMoney(paid - total);
                _vueltoLabel.Foreground = Theme.Ok;
            }
        }

        private void SubmitOrder()
        {
            _db = PosStore.Load();
            var activeSession = PosStore.GetActiveSession(_db);
            if (activeSession == null)
            {
                MessageBox.Show("La caja actual está cerrada. Iniciá una Nueva Caja para empezar a cobrar.", "Caja Cerrada", MessageBoxButton.OK, MessageBoxImage.Warning);
                OpenNewSessionModal();
                return;
            }

            if (_cart.Count == 0)
            {
                MessageBox.Show("Agregá al menos un plato o bebida al pedido antes de cobrar.", "Pedido Vacío", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            decimal total = _cart.Sum(c => c.Subtotal);
            decimal amountReceived = total;
            decimal changeAmount = 0;

            if (_paymentMethod == "Efectivo")
            {
                decimal rawPaid = 0;
                decimal.TryParse(_cashInputBox.Text.Trim(), out rawPaid);
                if (rawPaid > 0 && rawPaid < total)
                {
                    MessageBox.Show("El monto recibido en efectivo es menor al total a cobrar.", "Efectivo Insuficiente", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (rawPaid > 0)
                {
                    amountReceived = rawPaid;
                    changeAmount = rawPaid - total;
                }
            }

            int nextId = _db.Orders.Count == 0 ? 1 : _db.Orders.Max(o => o.Id) + 1;
            int ticketNum = _db.Settings.NextTicketSequence;

            var newOrder = new OrderRecord
            {
                Id = nextId,
                SessionId = activeSession.Id,
                TicketNumber = ticketNum,
                EventName = activeSession.EventName,
                Total = total,
                PaymentMethod = _paymentMethod,
                AmountReceived = amountReceived,
                ChangeAmount = changeAmount,
                Notes = _notesBox.Text.Trim(),
                Status = "completado",
                KitchenStatus = "pendiente",
                KitchenPrinted = false,
                CreatedAt = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"),
                FinishedAt = "",
                Items = _cart.Select(c => new OrderLineItem
                {
                    ProductId = c.Product.Id,
                    ProductName = c.Product.Name,
                    Category = c.Product.Category,
                    Quantity = c.Quantity,
                    UnitPrice = c.Product.Price,
                    Subtotal = c.Subtotal
                }).ToList()
            };

            foreach (var line in newOrder.Items)
            {
                var p = _db.Products.FirstOrDefault(x => x.Id == line.ProductId);
                if (p != null && p.StockEnabled)
                {
                    p.StockQty = Math.Max(0, p.StockQty - line.Quantity);
                }
            }

            _db.Orders.Add(newOrder);
            _db.Settings.NextTicketSequence = ticketNum + 1;
            PosStore.RecalculateSessionMetrics(_db, activeSession);
            PosStore.Save(_db);

            _lastCreatedOrder = newOrder;
            _cart.Clear();
            _cashInputBox.Text = "";
            _notesBox.Text = "";
            RefreshAllUI();

            try
            {
                EscPosPrinter.PrintOrder(newOrder, _db.Settings, false);
                ShowToast(string.Format("✓ Pedido #{0:000} cobrado ({1}) — Ticket impreso en {2} y enviado a Cocina",
                    newOrder.TicketNumber, PosStore.FormatMoney(newOrder.Total), _db.Settings.PrinterName));
            }
            catch (Exception ex)
            {
                ShowToast(string.Format("⚠ Pedido #{0:000} enviado a Cocina, pero la ticketera reportó: {1}",
                    newOrder.TicketNumber, ex.Message), true);
            }
        }

        private void ReprintLastOrder()
        {
            if (_lastCreatedOrder == null) return;
            try
            {
                EscPosPrinter.PrintOrder(_lastCreatedOrder, _db.Settings, false);
                ShowToast(string.Format("🖨️ Ticket #{0:000} reimpreso correctamente.", _lastCreatedOrder.TicketNumber));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error al imprimir", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PickPhotoForProduct(ProductItem prod)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Seleccionar foto para " + prod.Name,
                Filter = "Imágenes (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp"
            };
            if (dlg.ShowDialog(this) == true)
            {
                try
                {
                    Directory.CreateDirectory(PosStore.ImagesDir);
                    string ext = Path.GetExtension(dlg.FileName);
                    string destName = string.Format("plato_{0}_{1}{2}", prod.Id, DateTime.Now.Ticks % 100000, ext);
                    string destPath = Path.Combine(PosStore.ImagesDir, destName);
                    File.Copy(dlg.FileName, destPath, true);

                    var target = _db.Products.FirstOrDefault(p => p.Id == prod.Id);
                    if (target != null)
                    {
                        target.ImagePath = destName;
                        PosStore.Save(_db);
                    }
                    RefreshAllUI();
                    if (_activeSection == "menu") RenderMenuSection();
                    ShowToast("📷 Foto actualizada para '" + prod.Name + "'");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error al copiar imagen", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OpenProductEditorModal(ProductItem existing)
        {
            var grp = _db.MenuGroups.FirstOrDefault(g => g.Id == _editingGroupId) ?? GetCurrentActiveGroup();

            var win = new Window
            {
                Title = existing == null ? ("Nuevo Plato en " + grp.Name) : "Editar Plato",
                Width = 440,
                Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = Theme.Card,
                ResizeMode = ResizeMode.NoResize,
                FontFamily = new FontFamily("Segoe UI")
            };

            var stack = new StackPanel { Margin = new Thickness(22) };
            stack.Children.Add(new TextBlock
            {
                Text = existing == null ? ("Agregar Plato a: " + grp.Name) : "Editar Plato",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Text,
                Margin = new Thickness(0, 0, 0, 14)
            });

            stack.Children.Add(new TextBlock { Text = "Nombre del Plato / Bebida:", FontSize = 12, Foreground = Theme.Muted, Margin = new Thickness(0, 0, 0, 4) });
            var nameBox = CreateStyledTextBox("");
            nameBox.Text = existing != null ? existing.Name : "";
            stack.Children.Add(nameBox);

            stack.Children.Add(new TextBlock { Text = "Categoría dentro del menú:", FontSize = 12, Foreground = Theme.Muted, Margin = new Thickness(0, 10, 0, 4) });
            var catCombo = new ComboBox { Padding = new Thickness(8, 6, 8, 6), FontSize = 13, IsEditable = true };
            foreach (var c in new[] { "Almuerzo", "Principales", "Bebidas", "Para Llevar", "Postres" }) catCombo.Items.Add(c);
            catCombo.Text = existing != null ? existing.Category : "Principales";
            stack.Children.Add(catCombo);

            stack.Children.Add(new TextBlock { Text = "Precio ($):", FontSize = 12, Foreground = Theme.Muted, Margin = new Thickness(0, 10, 0, 4) });
            var priceBox = CreateStyledTextBox("");
            priceBox.Text = existing != null ? existing.Price.ToString("0") : "1500";
            stack.Children.Add(priceBox);

            stack.Children.Add(new TextBlock { Text = "Descripción corta (opcional):", FontSize = 12, Foreground = Theme.Muted, Margin = new Thickness(0, 10, 0, 4) });
            var descBox = CreateStyledTextBox("");
            descBox.Text = existing != null ? existing.Description : "";
            stack.Children.Add(descBox);

            var chkStock = new CheckBox
            {
                Content = "Activar límite de unidades disponibles (Stock)",
                IsChecked = existing != null && existing.StockEnabled,
                Margin = new Thickness(0, 14, 0, 6),
                FontSize = 12
            };
            stack.Children.Add(chkStock);

            var stockBox = CreateStyledTextBox("");
            stockBox.Text = existing != null ? existing.StockQty.ToString() : "50";
            stack.Children.Add(stockBox);

            var chkActive = new CheckBox
            {
                Content = "Visible para la venta en Caja",
                IsChecked = existing == null || existing.Active,
                Margin = new Thickness(0, 12, 0, 18),
                FontSize = 12
            };
            stack.Children.Add(chkActive);

            var btnSave = CreatePrimaryButton("💾 Guardar Plato", () =>
            {
                if (string.IsNullOrWhiteSpace(nameBox.Text)) return;
                decimal pr = 0;
                decimal.TryParse(priceBox.Text.Trim(), out pr);
                int sq = 0;
                int.TryParse(stockBox.Text.Trim(), out sq);
                string chosenCat = string.IsNullOrWhiteSpace(catCombo.Text) ? "Principales" : catCombo.Text.Trim();

                if (existing != null)
                {
                    var target = _db.Products.FirstOrDefault(p => p.Id == existing.Id);
                    if (target != null)
                    {
                        target.Name = nameBox.Text.Trim();
                        target.Category = chosenCat;
                        target.Price = pr;
                        target.Description = descBox.Text.Trim();
                        target.StockEnabled = chkStock.IsChecked == true;
                        target.StockQty = sq;
                        target.Active = chkActive.IsChecked == true;
                    }
                }
                else
                {
                    int nextId = _db.Products.Count == 0 ? 1 : _db.Products.Max(p => p.Id) + 1;
                    _db.Products.Add(new ProductItem
                    {
                        Id = nextId,
                        GroupId = _editingGroupId,
                        Name = nameBox.Text.Trim(),
                        Category = chosenCat,
                        Price = pr,
                        Description = descBox.Text.Trim(),
                        StockEnabled = chkStock.IsChecked == true,
                        StockQty = sq,
                        Active = chkActive.IsChecked == true,
                        ImagePath = "muzza.png"
                    });
                }

                PosStore.Save(_db);
                RefreshAllUI();
                RenderMenuSection();
                win.Close();
            }, false);

            stack.Children.Add(btnSave);
            win.Content = stack;
            win.ShowDialog();
        }

        // =====================================================================
        // VISTA 4: TICKETS EMITIDOS
        // =====================================================================
        private UIElement BuildOrdersView()
        {
            var dock = new DockPanel { LastChildFill = true };

            var header = new Border
            {
                Background = Theme.Card,
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.CardRadius,
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 12)
            };
            DockPanel.SetDock(header, Dock.Top);
            header.Child = new TextBlock
            {
                Text = "Tickets Emitidos en la Caja Actual y Estado en Cocina",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Text
            };
            dock.Children.Add(header);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _ordersListStack = new StackPanel();
            scroll.Content = _ordersListStack;
            dock.Children.Add(scroll);
            return dock;
        }

        private void RenderOrdersHistoryList()
        {
            _ordersListStack.Children.Clear();
            var activeSess = PosStore.GetActiveSession(_db);
            int sessId = activeSess != null ? activeSess.Id : (_db.Sessions.Count > 0 ? _db.Sessions.Last().Id : 1);

            var orders = _db.Orders.Where(o => o.SessionId == sessId).OrderByDescending(o => o.Id).ToList();
            if (orders.Count == 0)
            {
                _ordersListStack.Children.Add(new TextBlock
                {
                    Text = "Todavía no hay ventas registradas en esta caja.",
                    FontSize = 13,
                    Foreground = Theme.Muted,
                    Margin = new Thickness(10, 30, 10, 10)
                });
                return;
            }

            foreach (var ord in orders)
            {
                var card = new Border
                {
                    Background = Theme.Card,
                    BorderBrush = Theme.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = Theme.CardRadius,
                    Padding = new Thickness(14, 10, 14, 10),
                    Margin = new Thickness(0, 0, 0, 8)
                };

                var dock = new DockPanel();
                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(actions, Dock.Right);

                bool isCancelled = ord.Status == "anulado";
                SolidColorBrush badgeBg = isCancelled ? Theme.ErrBg : (ord.KitchenStatus == "finalizado" ? Theme.OkBg : (ord.KitchenStatus == "preparando" ? Theme.BrandBg : Theme.WarnBg));
                SolidColorBrush badgeFg = isCancelled ? Theme.Err : (ord.KitchenStatus == "finalizado" ? Theme.Ok : (ord.KitchenStatus == "preparando" ? Theme.BrandDark : Theme.Warn));
                string badgeText = isCancelled ? "ANULADO" : (ord.KitchenStatus == "finalizado" ? ("✓ ENTREGADO " + ord.FinishedAt) : (ord.KitchenStatus == "preparando" ? "👨‍🍳 PREPARANDO" : "🔔 EN COCINA"));

                var statusBadge = new Border
                {
                    Background = badgeBg,
                    CornerRadius = Theme.BtnRadius, // Rectangular redondeado uniforme, sin óvalo
                    Padding = new Thickness(10, 5, 10, 5),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                statusBadge.Child = new TextBlock { Text = badgeText, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = badgeFg };
                actions.Children.Add(statusBadge);

                var ordCopy = ord;
                var btnReprint = CreateGhostButton("🖨️ Reimprimir", () =>
                {
                    try
                    {
                        EscPosPrinter.PrintOrder(ordCopy, _db.Settings, false);
                        ShowToast(string.Format("🖨️ Ticket #{0:000} reimpreso.", ordCopy.TicketNumber));
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, "Error al imprimir", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                });
                btnReprint.Margin = new Thickness(0, 0, 6, 0);
                actions.Children.Add(btnReprint);

                if (!isCancelled)
                {
                    var btnCancel = CreateGhostButton("Anular", () =>
                    {
                        if (MessageBox.Show("¿Anular el Ticket #" + ordCopy.TicketNumber.ToString("000") + " y devolver el stock?",
                            "Anular Pedido", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                        {
                            var target = _db.Orders.FirstOrDefault(x => x.Id == ordCopy.Id);
                            if (target != null)
                            {
                                target.Status = "anulado";
                                foreach (var it in target.Items)
                                {
                                    var p = _db.Products.FirstOrDefault(x => x.Id == it.ProductId);
                                    if (p != null && p.StockEnabled) p.StockQty += it.Quantity;
                                }
                                PosStore.RecalculateSessionMetrics(_db, PosStore.GetActiveSession(_db));
                                PosStore.Save(_db);
                                RefreshAllUI();
                                RenderOrdersHistoryList();
                            }
                        }
                    });
                    ((TextBlock)btnCancel.Child).Foreground = Theme.Err;
                    actions.Children.Add(btnCancel);
                }

                dock.Children.Add(actions);

                var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                info.Children.Add(new TextBlock
                {
                    Text = string.Format("TICKET #{0:000}   ·   {1}   ·   {2}   ·   {3}",
                        ord.TicketNumber, ord.CreatedAt, ord.PaymentMethod, PosStore.FormatMoney(ord.Total)),
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = isCancelled ? Theme.Faint : Theme.Text
                });

                string itemsSummary = string.Join(", ", ord.Items.Select(i => i.Quantity + "x " + i.ProductName));
                if (!string.IsNullOrWhiteSpace(ord.Notes)) itemsSummary += "   |   Nota: " + ord.Notes;
                info.Children.Add(new TextBlock
                {
                    Text = itemsSummary,
                    FontSize = 12,
                    Foreground = Theme.Muted,
                    Margin = new Thickness(0, 2, 0, 0)
                });

                dock.Children.Add(info);
                card.Child = dock;
                _ordersListStack.Children.Add(card);
            }
        }

        private Border CreateStatCard(string label, string val, SolidColorBrush valBrush)
        {
            var card = new Border
            {
                Background = Theme.Card,
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.CardRadius,
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(3)
            };
            var st = new StackPanel();
            st.Children.Add(new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Theme.Muted });
            st.Children.Add(new TextBlock { Text = val, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = valBrush, Margin = new Thickness(0, 4, 0, 0) });
            card.Child = st;
            return card;
        }

        private void OpenNewSessionModal()
        {
            var win = new Window
            {
                Title = "Iniciar Nueva Caja / Seleccionar Tipo de Evento",
                Width = 460,
                Height = 430,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = Theme.Card,
                ResizeMode = ResizeMode.NoResize,
                FontFamily = new FontFamily("Segoe UI")
            };

            var stack = new StackPanel { Margin = new Thickness(22) };
            stack.Children.Add(new TextBlock
            {
                Text = "🔓 Iniciar Nueva Caja / Evento",
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Text,
                Margin = new Thickness(0, 0, 0, 6)
            });
            stack.Children.Add(new TextBlock
            {
                Text = "Elegí el nombre de la jornada y qué Grupo de Menú se va a vender en esta caja.",
                FontSize = 12,
                Foreground = Theme.Muted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            });

            stack.Children.Add(new TextBlock { Text = "Nombre del Evento o Jornada:", FontSize = 12, Foreground = Theme.Muted, Margin = new Thickness(0, 0, 0, 4) });
            var evBox = CreateStyledTextBox("");
            evBox.Text = _db.Settings.EventName ?? "Asociación Juvenil Araucana";
            stack.Children.Add(evBox);

            stack.Children.Add(new TextBlock { Text = "Tipo de Evento / Menú a utilizar:", FontSize = 12, Foreground = Theme.Muted, Margin = new Thickness(0, 12, 0, 4) });
            var groupCombo = new ComboBox { Padding = new Thickness(8, 6, 8, 6), FontSize = 13 };
            foreach (var g in _db.MenuGroups) groupCombo.Items.Add(g.Name);
            var currGrp = GetCurrentActiveGroup();
            groupCombo.SelectedItem = currGrp.Name;
            stack.Children.Add(groupCombo);

            stack.Children.Add(new TextBlock { Text = "Fondo Inicial de Cambio en Efectivo ($):", FontSize = 12, Foreground = Theme.Muted, Margin = new Thickness(0, 12, 0, 4) });
            var cashBox = CreateStyledTextBox("");
            cashBox.Text = "0";
            stack.Children.Add(cashBox);

            var chkResetSeq = new CheckBox
            {
                Content = "Reiniciar numeración de tickets a #001",
                IsChecked = true,
                Margin = new Thickness(0, 14, 0, 18),
                FontSize = 12
            };
            stack.Children.Add(chkResetSeq);

            var btnStart = CreatePrimaryButton("✓ ABRIR NUEVA CAJA AHORA", () =>
            {
                string evName = string.IsNullOrWhiteSpace(evBox.Text) ? "Asociación Juvenil Araucana" : evBox.Text.Trim();
                decimal openCash = 0;
                decimal.TryParse(cashBox.Text.Trim(), out openCash);

                string selGrpName = (groupCombo.SelectedItem ?? "").ToString();
                var chosenGrp = _db.MenuGroups.FirstOrDefault(g => g.Name == selGrpName) ?? _db.MenuGroups.First();

                foreach (var s in _db.Sessions.Where(x => x.Status == "abierta"))
                {
                    PosStore.RecalculateSessionMetrics(_db, s);
                    s.Status = "cerrada";
                    s.ClosedAt = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
                }

                int nextId = _db.Sessions.Count == 0 ? 1 : _db.Sessions.Max(s => s.Id) + 1;
                _db.Sessions.Add(new CashSession
                {
                    Id = nextId,
                    MenuGroupId = chosenGrp.Id,
                    MenuGroupName = chosenGrp.Name,
                    EventName = evName,
                    OpenedAt = DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
                    OpeningCash = openCash,
                    Status = "abierta"
                });

                _db.Settings.EventName = evName;
                _db.Settings.ActiveMenuGroupId = chosenGrp.Id;
                if (chkResetSeq.IsChecked == true)
                    _db.Settings.NextTicketSequence = 1;

                PosStore.Save(_db);
                _cart.Clear();
                RefreshAllUI();
                if (_activeSection == "caja_actual") RenderCajaActualView();
                win.Close();
                ShowToast(string.Format("🔓 Caja #{0:000} abierta con el menú '{1}'", nextId, chosenGrp.Name));
            }, false);

            stack.Children.Add(btnStart);
            win.Content = stack;
            win.ShowDialog();
        }

        // =====================================================================
        // VISTA 5: TICKETERA Y AJUSTES
        // =====================================================================
        private UIElement BuildSettingsView()
        {
            var card = new Border
            {
                Background = Theme.Card,
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.CardRadius,
                Padding = new Thickness(24),
                VerticalAlignment = VerticalAlignment.Top
            };

            var stack = new StackPanel { MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
            stack.Children.Add(new TextBlock
            {
                Text = "⚙️ Configuración de Ticketera Térmica y Evento",
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Text,
                Margin = new Thickness(0, 0, 0, 16)
            });

            stack.Children.Add(new TextBlock { Text = "Ticketera instalada en Windows:", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Theme.Muted, Margin = new Thickness(0, 0, 0, 4) });
            _printerCombo = new ComboBox { Padding = new Thickness(10, 7, 10, 7), FontSize = 13, Margin = new Thickness(0, 0, 0, 12) };
            stack.Children.Add(_printerCombo);

            stack.Children.Add(new TextBlock { Text = "Ancho de rollo de papel térmico:", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Theme.Muted, Margin = new Thickness(0, 0, 0, 4) });
            _paperWidthCombo = new ComboBox { Padding = new Thickness(10, 7, 10, 7), FontSize = 13, Margin = new Thickness(0, 0, 0, 12) };
            _paperWidthCombo.Items.Add("80 mm (Ancho completo 48 columnas - Xprinter XP-80)");
            _paperWidthCombo.Items.Add("58 mm (Rollo angosto 32 columnas)");
            stack.Children.Add(_paperWidthCombo);

            _doubleTicketCheck = new CheckBox
            {
                Content = "Imprimir Doble Talón con corte automático (Cliente + Comanda de Cocina)",
                FontSize = 13,
                Margin = new Thickness(0, 4, 0, 14)
            };
            stack.Children.Add(_doubleTicketCheck);

            stack.Children.Add(new TextBlock { Text = "Alias de Mercado Pago:", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Theme.Muted, Margin = new Thickness(0, 0, 0, 4) });
            _mpAliasSettingBox = CreateStyledTextBox("");
            _mpAliasSettingBox.Margin = new Thickness(0, 0, 0, 12);
            stack.Children.Add(_mpAliasSettingBox);

            stack.Children.Add(new TextBlock { Text = "Mensaje al pie del ticket:", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Theme.Muted, Margin = new Thickness(0, 0, 0, 4) });
            _footerSettingBox = CreateStyledTextBox("");
            _footerSettingBox.Margin = new Thickness(0, 0, 0, 18);
            stack.Children.Add(_footerSettingBox);

            var btnsRow = new StackPanel { Orientation = Orientation.Horizontal };
            var btnSave = CreatePrimaryButton("💾 Guardar Configuración", SaveSettingsFromForm, false);
            btnSave.Margin = new Thickness(0, 0, 10, 0);
            btnsRow.Children.Add(btnSave);

            var btnTest = CreateGhostButton("🖨️ Imprimir Ticket de Prueba (80mm)", () =>
            {
                SaveSettingsFromForm();
                try
                {
                    EscPosPrinter.PrintTestTicket(_db.Settings);
                    ShowToast("🖨️ Ticket de prueba de 48 columnas enviado a " + _db.Settings.PrinterName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error en Ticketera", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
            btnsRow.Children.Add(btnTest);
            stack.Children.Add(btnsRow);

            card.Child = stack;
            return card;
        }

        private void LoadSettingsForm()
        {
            _printerCombo.Items.Clear();
            foreach (var p in EscPosPrinter.ListInstalledPrinters())
                _printerCombo.Items.Add(p);

            string currentPrinter = _db.Settings.PrinterName ?? EscPosPrinter.DetectPreferredPrinter();
            _printerCombo.SelectedItem = currentPrinter;
            if (_printerCombo.SelectedItem == null && _printerCombo.Items.Count > 0)
                _printerCombo.SelectedIndex = 0;

            _paperWidthCombo.SelectedIndex = _db.Settings.PaperWidthMm == 58 ? 1 : 0;
            _doubleTicketCheck.IsChecked = _db.Settings.PrintDoubleTicket;
            _mpAliasSettingBox.Text = _db.Settings.MpAlias;
            _footerSettingBox.Text = _db.Settings.TicketFooter;
        }

        private void SaveSettingsFromForm()
        {
            if (_printerCombo.SelectedItem != null)
                _db.Settings.PrinterName = _printerCombo.SelectedItem.ToString();
            _db.Settings.PaperWidthMm = _paperWidthCombo.SelectedIndex == 1 ? 58 : 80;
            _db.Settings.PrintDoubleTicket = _doubleTicketCheck.IsChecked == true;
            _db.Settings.MpAlias = string.IsNullOrWhiteSpace(_mpAliasSettingBox.Text) ? "araucana24" : _mpAliasSettingBox.Text.Trim();
            _db.Settings.TicketFooter = _footerSettingBox.Text.Trim();
            PosStore.Save(_db);
            RefreshAllUI();
            ShowToast("⚙️ Configuración guardada.");
        }

        // =====================================================================
        // SINCRONIZACIÓN Y BOTONES UNIFORMES (SIN ÓVALOS)
        // =====================================================================
        private void RefreshAllUI()
        {
            var activeSess = PosStore.GetActiveSession(_db);
            var activeGrp = GetCurrentActiveGroup();

            if (activeSess != null)
            {
                PosStore.RecalculateSessionMetrics(_db, activeSess);
                _headerTitle.Text = string.Format("{0}  —  Caja #{1:000}", activeSess.EventName, activeSess.Id);
                _headerSub.Text = "Menú activo en esta caja: " + activeGrp.Name;
                _sbSessionStatus.Text = string.Format("● CAJA #{0:000} ABIERTA", activeSess.Id);
                _sbSessionStatus.Foreground = Theme.Ok;
                _sbSessionTotal.Text = "Recaudado: " + PosStore.FormatMoney(activeSess.TotalSales);
            }
            else
            {
                _headerTitle.Text = "CAJA CERRADA — Iniciá una Nueva Caja";
                _headerSub.Text = "Sin turno activo";
                _sbSessionStatus.Text = "● CAJA CERRADA";
                _sbSessionStatus.Foreground = Theme.Err;
                _sbSessionTotal.Text = "Sin turno activo";
            }

            _nextTicketText.Text = string.Format("PRÓXIMO: #{0:000}", _db.Settings.NextTicketSequence);
            UpdateKitchenCounterBadge();
            RenderCategoryButtons();
            RenderProductCards();
            RenderCart();
        }

        private void UpdateKitchenCounterBadge()
        {
            int activeInKitchen = _db.Orders.Count(o => o.Status == "completado" && (o.KitchenStatus == "pendiente" || o.KitchenStatus == "preparando"));
            int doneInKitchen = _db.Orders.Count(o => o.Status == "completado" && o.KitchenStatus == "finalizado");
            _kitchenBadgeText.Text = string.Format("👨‍🍳 Cocina: {0} en curso  |  {1} listos", activeInKitchen, doneInKitchen);
        }

        private void BackgroundSync()
        {
            try
            {
                var diskDb = PosStore.Load();
                _db.Orders = diskDb.Orders;
                UpdateKitchenCounterBadge();
                if (_activeSection == "orders")
                    RenderOrdersHistoryList();
            }
            catch { }
        }

        private Border CreatePrimaryButton(string text, Action onClick, bool compact)
        {
            var b = new Border
            {
                Background = Theme.Brand,
                CornerRadius = Theme.BtnRadius,
                Padding = compact ? new Thickness(10, 7, 10, 7) : new Thickness(16, 9, 16, 9),
                Cursor = Cursors.Hand
            };
            var lbl = new TextBlock
            {
                Text = text,
                FontSize = compact ? 12 : 13,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            b.Child = lbl;
            b.MouseEnter += (s, e) => b.Background = Theme.BrandDark;
            b.MouseLeave += (s, e) => b.Background = Theme.Brand;
            b.MouseLeftButtonUp += (s, e) => onClick();
            return b;
        }

        private Border CreateGhostButton(string text, Action onClick)
        {
            var b = new Border
            {
                Background = Theme.Card,
                BorderBrush = Theme.BorderStrong,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.BtnRadius,
                Padding = new Thickness(12, 7, 12, 7),
                Cursor = Cursors.Hand
            };
            var lbl = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            b.Child = lbl;
            b.MouseEnter += (s, e) => b.Background = Theme.Hover;
            b.MouseLeave += (s, e) => b.Background = Theme.Card;
            b.MouseLeftButtonUp += (s, e) => onClick();
            return b;
        }

        private Border CreateIconButton(string icon, string tooltip, Action onClick)
        {
            var b = new Border
            {
                Width = 36,
                Height = 36,
                Background = Theme.Bg,
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.BtnRadius,
                ToolTip = tooltip,
                Cursor = Cursors.Hand
            };
            b.Child = new TextBlock
            {
                Text = icon,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            b.MouseEnter += (s, e) => b.Background = Theme.Hover;
            b.MouseLeave += (s, e) => b.Background = Theme.Bg;
            b.MouseLeftButtonUp += (s, e) => onClick();
            return b;
        }

        private TextBox CreateStyledTextBox(string placeholder)
        {
            return new TextBox
            {
                Padding = new Thickness(10, 7, 10, 7),
                FontSize = 13,
                Foreground = Theme.Text,
                Background = Theme.Bg,
                BorderBrush = Theme.BorderStrong,
                BorderThickness = new Thickness(1)
            };
        }
    }

    public static class CajaProgram
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        public static void Main()
        {
            try { SetProcessDPIAware(); } catch { }
            var app = new Application();
            app.Run(new CajaMainWindow());
        }
    }
}
