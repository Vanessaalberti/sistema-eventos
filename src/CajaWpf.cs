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
        // Estética visual moderna inspirada en la referencia: limpio, blanco puro y azul eléctrico #3B76F6
        public static readonly SolidColorBrush Bg = BrushFromHex("#F6F7F9");
        public static readonly SolidColorBrush Card = BrushFromHex("#FFFFFF");
        public static readonly SolidColorBrush SurfaceSoft = BrushFromHex("#F8FAFC");
        public static readonly SolidColorBrush Border = BrushFromHex("#EAECF0");
        public static readonly SolidColorBrush BorderStrong = BrushFromHex("#D0D5DD");
        public static readonly SolidColorBrush Hover = BrushFromHex("#F3F4F6");
        public static readonly SolidColorBrush BrandHover = BrushFromHex("#EFF6FF");
        public static readonly SolidColorBrush Text = BrushFromHex("#111827");
        public static readonly SolidColorBrush Muted = BrushFromHex("#6B7280");
        public static readonly SolidColorBrush Faint = BrushFromHex("#9CA3AF");
        public static readonly SolidColorBrush Brand = BrushFromHex("#3B76F6");
        public static readonly SolidColorBrush BrandDark = BrushFromHex("#2563EB");
        public static readonly SolidColorBrush BrandBg = BrushFromHex("#EFF6FF");
        public static readonly SolidColorBrush BrandSoft = BrushFromHex("#BFDBFE");
        public static readonly SolidColorBrush Ok = BrushFromHex("#16A34A");
        public static readonly SolidColorBrush OkBg = BrushFromHex("#DCFCE7");
        public static readonly SolidColorBrush Warn = BrushFromHex("#CA8A04");
        public static readonly SolidColorBrush WarnBg = BrushFromHex("#FEF9C3");
        public static readonly SolidColorBrush Err = BrushFromHex("#DC2626");
        public static readonly SolidColorBrush ErrBg = BrushFromHex("#FEE2E2");

        // Bordes redondeados rectangulares uniformes (sin óvalos)
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
                BlurRadius = 12,
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
                bmp.DecodePixelWidth = 180;
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
        public string Note { get; set; }
        public List<PackFlavorChoice> PackChoices { get; set; }
        public decimal Subtotal { get { return Product.Price * Quantity; } }

        public CartEntry()
        {
            PackChoices = new List<PackFlavorChoice>();
        }
    }

    public class CajaMainWindow : Window
    {
        private PosDatabasePayload _db;
        private readonly List<CartEntry> _cart = new List<CartEntry>();
        private string _selectedCategory = "Todos";
        private string _searchQuery = "";
        private string _paymentMethod = "Efectivo";
        private string _activeSection = "caja_actual"; // Primera vista al abrir: Caja Actual
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
        private readonly Dictionary<string, TextBlock> _navIcons = new Dictionary<string, TextBlock>();
        private readonly Dictionary<string, TextBlock> _navLabels = new Dictionary<string, TextBlock>();

        // Barra superior
        private TextBlock _headerEventLabel;
        private TextBlock _headerSubLabel;
        private TextBlock _kitchenBadgeText;
        private TextBlock _nextTicketText;

        // Notificación flotante abajo a la derecha (se cierra sola o con X)
        private Border _toastBanner;
        private TextBlock _toastText;
        private DispatcherTimer _toastTimer;

        // Vistas principales
        private Grid _viewHost;
        private UIElement _cajaActualView;
        private UIElement _posView;
        private UIElement _menuView;
        private UIElement _ordersView;
        private UIElement _settingsView;

        // Controles de Vista Punto de Venta
        private WrapPanel _catCardsWrap;
        private TextBlock _posMenuSectionTitle;
        private WrapPanel _productsWrap;
        private StackPanel _cartListPanel;
        private TextBox _generalNotesBox;
        private TextBox _cashInputBox;
        private TextBlock _vueltoLabel;
        private TextBlock _subtotalAmountLabel;
        private TextBlock _totalAmountLabel;
        private StackPanel _cashContextPanel;
        private Border _mpContextPanel;
        private TextBlock _mpAliasLabel;
        private UniformGrid _payMethodsGrid;
        private readonly Dictionary<string, Border> _payMethodCards = new Dictionary<string, Border>();

        // Controles de Vista Menú (Grupos de Evento + Platos)
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
        private ComboBox _copiesCombo;
        private CheckBox _doubleTicketCheck;
        private StackPanel _kitchenPrinterPanel;
        private ComboBox _kitchenPrinterCombo;
        private CheckBox _chkEnableCash;
        private CheckBox _chkEnableTransfer;
        private CheckBox _chkEnableCard;
        private CheckBox _chkPrintLogo;
        private ComboBox _logoSizeCombo;
        private Border _logoPreviewBox;
        private TextBox _mpAliasSettingBox;
        private TextBox _footerSettingBox;

        private DispatcherTimer _syncTimer;

        public CajaMainWindow()
        {
            _db = PosStore.Load();
            var activeSess = PosStore.GetActiveSession(_db);
            _editingGroupId = activeSess != null ? activeSess.MenuGroupId : _db.Settings.ActiveMenuGroupId;

            Title = "CAJA POS — Asociación Juvenil Araucana";
            Width = 1400;
            Height = 850;
            MinWidth = 1120;
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

            PosNetworkSync.StartCajaServer();

            BuildShellLayout();
            RefreshAllUI();

            _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _syncTimer.Tick += (s, e) => BackgroundSync();
            _syncTimer.Start();

            Loaded += (s, e) => GitHubUpdater.CheckForUpdatesAsync(this, "Caja_POS.exe", false);
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
        // SHELL PRINCIPAL (ESTÉTICA BLANCA + AZUL #3B76F6 DE LA REFERENCIA)
        // =====================================================================
        private void BuildShellLayout()
        {
            var rootGrid = new Grid();
            _sidebarCol = new ColumnDefinition { Width = new GridLength(225) };
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

            var sbHeader = new DockPanel { Margin = new Thickness(14, 16, 14, 18) };
            DockPanel.SetDock(sbHeader, Dock.Top);

            var logoSquare = new Border
            {
                Width = 38,
                Height = 38,
                Background = Theme.Brand,
                CornerRadius = Theme.BtnRadius,
                Cursor = Cursors.Hand,
                ToolTip = "Desplegar / Contraer menú lateral"
            };
            logoSquare.Child = new TextBlock
            {
                Text = "☰",
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            logoSquare.MouseLeftButtonUp += (s, e) => ToggleSidebar();
            DockPanel.SetDock(logoSquare, Dock.Left);
            sbHeader.Children.Add(logoSquare);

            _brandTitle = new TextBlock
            {
                Text = "Araucana POS",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Text,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            sbHeader.Children.Add(_brandTitle);
            sbDock.Children.Add(sbHeader);

            // Footer del Sidebar: Resumen rápido de Caja
            _sidebarFooter = new StackPanel { Margin = new Thickness(12, 8, 12, 16) };
            DockPanel.SetDock(_sidebarFooter, Dock.Bottom);

            var statusCard = new Border
            {
                Background = Theme.SurfaceSoft,
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.CardRadius,
                Padding = new Thickness(12, 10, 12, 10)
            };
            var scStack = new StackPanel();
            _sbSessionStatus = new TextBlock
            {
                Text = "● CAJA ABIERTA",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Ok
            };
            _sbSessionTotal = new TextBlock
            {
                Text = "Recaudado: $0",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Text,
                Margin = new Thickness(0, 3, 0, 8)
            };
            var btnGoToPos = CreatePrimaryButton("🛒 Tomar Pedido", () => SwitchSection("pos"), true);
            scStack.Children.Add(_sbSessionStatus);
            scStack.Children.Add(_sbSessionTotal);
            scStack.Children.Add(btnGoToPos);
            statusCard.Child = scStack;
            _sidebarFooter.Children.Add(statusCard);
            sbDock.Children.Add(_sidebarFooter);

            // Navegación Lateral
            var navStack = new StackPanel { Margin = new Thickness(12, 0, 12, 0) };
            AddNavItem(navStack, "caja_actual", "🏠", "Caja Actual");
            AddNavItem(navStack, "pos", "🛒", "Tomar Pedido");
            AddNavItem(navStack, "menu", "🍽️", "Menú");
            AddNavItem(navStack, "orders", "🧾", "Tickets");
            AddNavItem(navStack, "settings", "⚙️", "Ajustes");
            sbDock.Children.Add(navStack);

            sidebarBorder.Child = sbDock;
            rootGrid.Children.Add(sidebarBorder);

            // 2. PANEL PRINCIPAL DERECHO
            var rightDock = new DockPanel { LastChildFill = true };
            Grid.SetColumn(rightDock, 1);

            // TopBar estilo referencia (Buscador limpio a la izquierda + estado e IP a la derecha)
            var topBar = new Border
            {
                Background = Theme.Card,
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(24, 12, 24, 12)
            };
            DockPanel.SetDock(topBar, Dock.Top);

            var topGrid = new Grid();
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var leftTopRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var infoHeaderStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _headerEventLabel = new TextBlock
            {
                Text = "Asociación Juvenil Araucana",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Text
            };
            _headerSubLabel = new TextBlock
            {
                Text = "Caja #001 · Menú: Almuerzo y Buffet Araucana",
                FontSize = 12,
                Foreground = Theme.Muted
            };
            infoHeaderStack.Children.Add(_headerEventLabel);
            infoHeaderStack.Children.Add(_headerSubLabel);
            leftTopRow.Children.Add(infoHeaderStack);

            Grid.SetColumn(leftTopRow, 0);
            topGrid.Children.Add(leftTopRow);

            var rightBadges = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var lanBadge = new Border
            {
                Background = Theme.SurfaceSoft,
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.BtnRadius,
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "IP local de esta PC para conectar Cocina_POS.exe desde otra computadora"
            };
            lanBadge.Child = new TextBlock
            {
                Text = "📡 IP: " + PosNetworkSync.GetLocalLanIp(),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Muted
            };
            rightBadges.Children.Add(lanBadge);

            var kitchenBadge = new Border
            {
                Background = Theme.WarnBg,
                BorderBrush = Theme.Warn,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.BtnRadius,
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 8, 0)
            };
            _kitchenBadgeText = new TextBlock
            {
                Text = "👨‍🍳 Cocina: 0",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Warn
            };
            kitchenBadge.Child = _kitchenBadgeText;
            rightBadges.Children.Add(kitchenBadge);

            var nextTicketBadge = new Border
            {
                Background = Theme.BrandBg,
                BorderBrush = Theme.BrandSoft,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.BtnRadius,
                Padding = new Thickness(12, 6, 12, 6)
            };
            _nextTicketText = new TextBlock
            {
                Text = "PRÓXIMO: #001",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Brand
            };
            nextTicketBadge.Child = _nextTicketText;
            rightBadges.Children.Add(nextTicketBadge);

            Grid.SetColumn(rightBadges, 1);
            topGrid.Children.Add(rightBadges);
            topBar.Child = topGrid;
            rightDock.Children.Add(topBar);

            // Host de vistas
            _viewHost = new Grid { Margin = new Thickness(24, 16, 24, 20) };
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

            // Notificación flotante superpuesta abajo a la derecha (se cierra sola en 4.5s o con la X)
            _toastBanner = new Border
            {
                Background = Theme.Card,
                BorderBrush = Theme.Ok,
                BorderThickness = new Thickness(1.5),
                CornerRadius = Theme.CardRadius,
                Padding = new Thickness(14, 10, 10, 10),
                Margin = new Thickness(0, 0, 24, 24),
                MaxWidth = 460,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Visibility = Visibility.Collapsed,
                Effect = new DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 4, BlurRadius = 16, Opacity = 0.16 }
            };
            Panel.SetZIndex(_toastBanner, 999);

            var toastDock = new DockPanel();
            var btnCloseToast = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = Theme.BtnRadius,
                Background = Theme.Bg,
                Cursor = Cursors.Hand,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Cerrar notificación"
            };
            btnCloseToast.Child = new TextBlock
            {
                Text = "✕",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Muted,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            btnCloseToast.MouseLeftButtonUp += (s, e) => HideToast();
            DockPanel.SetDock(btnCloseToast, Dock.Right);
            toastDock.Children.Add(btnCloseToast);

            _toastText = new TextBlock
            {
                Text = "",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Text,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            toastDock.Children.Add(_toastText);
            _toastBanner.Child = toastDock;

            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.5) };
            _toastTimer.Tick += (s, e) => HideToast();

            var windowOverlayGrid = new Grid();
            windowOverlayGrid.Children.Add(rootGrid);
            windowOverlayGrid.Children.Add(_toastBanner);
            Content = windowOverlayGrid;

            SwitchSection("caja_actual");
        }

        private void AddNavItem(StackPanel parent, string key, string icon, string label)
        {
            var border = new Border
            {
                CornerRadius = Theme.BtnRadius,
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 3, 0, 3),
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
                Foreground = Theme.Muted,
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
            _navIcons[key] = ic;
            _navLabels[key] = lbl;
            parent.Children.Add(border);
        }

        private void ToggleSidebar()
        {
            _sidebarExpanded = !_sidebarExpanded;
            _sidebarCol.Width = new GridLength(_sidebarExpanded ? 225 : 68);
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

            // Estilo de la referencia: botón activo en azul sólido #3B76F6 con texto blanco
            foreach (var kv in _navButtons)
            {
                bool active = kv.Key == key;
                kv.Value.Background = active ? Theme.Brand : Brushes.Transparent;
                _navLabels[kv.Key].Foreground = active ? Brushes.White : Theme.Muted;
                _navLabels[kv.Key].FontWeight = active ? FontWeights.Bold : FontWeights.SemiBold;
                _navIcons[kv.Key].Foreground = active ? Brushes.White : Theme.Muted;
            }

            if (key == "caja_actual") RenderCajaActualView();
            else if (key == "pos")
            {
                RenderCategoryCards();
                RenderProductCards();
                RenderCart();
            }
            else if (key == "menu") RenderMenuSection();
            else if (key == "orders") RenderOrdersHistoryList();
            else if (key == "settings") LoadSettingsForm();
        }

        private void ShowToast(string message, bool isError = false)
        {
            _toastBanner.Background = isError ? Theme.ErrBg : Theme.Card;
            _toastBanner.BorderBrush = isError ? Theme.Err : Theme.Ok;
            _toastText.Foreground = isError ? Theme.Err : Theme.Text;
            _toastText.Text = message;
            _toastBanner.Visibility = Visibility.Visible;
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        private void HideToast()
        {
            _toastTimer.Stop();
            _toastBanner.Visibility = Visibility.Collapsed;
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
        // VISTA 1 (INICIAL): CAJA ACTUAL
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

            var heroCard = new Border
            {
                Background = Theme.Card,
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.CardRadius,
                Padding = new Thickness(22, 18, 22, 18),
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

            var btnNewCaja = CreateGhostButton("🔓 Iniciar Nueva Caja / Evento", OpenNewSessionModal);
            heroActions.Children.Add(btnNewCaja);
            heroDock.Children.Add(heroActions);

            var heroInfo = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };

            var statusBadge = new Border
            {
                Background = activeSess != null ? Theme.OkBg : Theme.ErrBg,
                BorderBrush = activeSess != null ? Theme.Ok : Theme.Err,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.BtnRadius,
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(0, 0, 8, 0)
            };
            statusBadge.Child = new TextBlock
            {
                Text = activeSess != null ? string.Format("CAJA #{0:000} ABIERTA", activeSess.Id) : "CAJA CERRADA",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = activeSess != null ? Theme.Ok : Theme.Err
            };
            statusRow.Children.Add(statusBadge);

            var groupBadge = new Border
            {
                Background = Theme.BrandBg,
                BorderBrush = Theme.BrandSoft,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.BtnRadius,
                Padding = new Thickness(10, 3, 10, 3)
            };
            groupBadge.Child = new TextBlock
            {
                Text = "MENÚ: " + activeGroup.Name.ToUpperInvariant(),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Brand
            };
            statusRow.Children.Add(groupBadge);
            heroInfo.Children.Add(statusRow);

            heroInfo.Children.Add(new TextBlock
            {
                Text = activeSess != null ? activeSess.EventName : "No hay ninguna caja abierta actualmente",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Text
            });

            heroInfo.Children.Add(new TextBlock
            {
                Text = activeSess != null
                    ? string.Format("Abierta: {0}   ·   Fondo inicial: {1}   ·   Próximo ticket: #{2:000}",
                        activeSess.OpenedAt, PosStore.FormatMoney(activeSess.OpeningCash), _db.Settings.NextTicketSequence)
                    : "Iniciá una Nueva Caja para seleccionar el menú del evento y empezar a cobrar.",
                FontSize = 13,
                Foreground = Theme.Muted,
                Margin = new Thickness(0, 3, 0, 0)
            });

            heroDock.Children.Add(heroInfo);
            heroCard.Child = heroDock;
            _cajaActualHostStack.Children.Add(heroCard);

            if (activeSess != null)
            {
                // Selector de Menú / Grupo de Evento
                var switchMenuCard = new Border
                {
                    Background = Theme.Card,
                    BorderBrush = Theme.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = Theme.CardRadius,
                    Padding = new Thickness(18, 12, 18, 12),
                    Margin = new Thickness(0, 0, 0, 14)
                };
                var smDock = new DockPanel();
                var btnEditMenus = CreateGhostButton("🍽️ Administrar Menús por Evento", () => SwitchSection("menu"));
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
                foreach (var g in _db.MenuGroups) grpCombo.Items.Add(g.Name);
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
                        ShowToast("Menú activo cambiado a: " + chosenGrp.Name);
                    }
                };
                smLeft.Children.Add(grpCombo);
                smDock.Children.Add(smLeft);
                switchMenuCard.Child = smDock;
                _cajaActualHostStack.Children.Add(switchMenuCard);

                // Métricas
                bool showCardStat = _db.Settings.EnableCard || activeSess.CardSales > 0;
                var metricsGrid = new UniformGrid { Columns = showCardStat ? 5 : 4, Margin = new Thickness(0, 0, 0, 14) };
                metricsGrid.Children.Add(CreateStatCard("TOTAL RECAUDADO", PosStore.FormatMoney(activeSess.TotalSales), Theme.Brand));
                metricsGrid.Children.Add(CreateStatCard("PEDIDOS COBRADOS", activeSess.OrdersCount.ToString(), Theme.Text));
                metricsGrid.Children.Add(CreateStatCard("💵 EFECTIVO", PosStore.FormatMoney(activeSess.CashSales), Theme.Ok));
                metricsGrid.Children.Add(CreateStatCard("📱 TRANSFERENCIA", PosStore.FormatMoney(activeSess.MpSales), Theme.BrandDark));
                if (showCardStat)
                    metricsGrid.Children.Add(CreateStatCard("💳 TARJETA", PosStore.FormatMoney(activeSess.CardSales), Theme.Muted));
                _cajaActualHostStack.Children.Add(metricsGrid);

                // Arqueo + Ranking
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
                    Text = "🔐 Arqueo de Efectivo y Cierre de Caja",
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
                var countedBox = CreateStyledTextBox();
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
                    if (Math.Abs(diff) < 1) { diffLabel.Text = "✓ Caja Exacta ($0)"; diffLabel.Foreground = Theme.Ok; }
                    else if (diff > 0) { diffLabel.Text = "Sobrante: +" + PosStore.FormatMoney(diff); diffLabel.Foreground = Theme.Brand; }
                    else { diffLabel.Text = "Faltante: " + PosStore.FormatMoney(diff); diffLabel.Foreground = Theme.Err; }
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
                    catch (Exception ex) { MessageBox.Show(ex.Message, "Error al imprimir", MessageBoxButton.OK, MessageBoxImage.Error); }
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
                        try { EscPosPrinter.PrintCashClosure(activeSess, sessionOrders, _db.Settings); } catch { }
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
                        var amtTxt = new TextBlock { Text = PosStore.FormatMoney(r.Amt), FontWeight = FontWeights.Bold, Foreground = Theme.Brand };
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
                        catch (Exception ex) { MessageBox.Show(ex.Message, "Error al imprimir", MessageBoxButton.OK, MessageBoxImage.Error); }
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
        // VISTA 2: TOMAR PEDIDO (ESTÉTICA VISUAL DE LA REFERENCIA EN ESPAÑOL)
        // - Tarjetas de categoría con icono y cantidad
        // - Tarjetas horizontales de plato con foto a la izquierda y stepper - 0 +
        // - Panel derecho "Pedido Actual" con miniaturas, lápiz ✏️ para comentario
        //   por artículo y Resumen de Pago limpio
        // =====================================================================
        private UIElement BuildPosView()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(395) });

            // IZQUIERDA: Categorías en tarjetas + Título de sección + Grilla horizontal de platos
            var leftDock = new DockPanel { LastChildFill = true };
            Grid.SetColumn(leftDock, 0);

            var topCategoriesStack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            DockPanel.SetDock(topCategoriesStack, Dock.Top);

            // Barra de búsqueda exclusivamente dentro de "Tomar Pedido"
            var posSearchBorder = new Border
            {
                Background = Theme.Card,
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.BtnRadius,
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 12),
                Effect = Theme.CardShadow()
            };
            var posSearchDock = new DockPanel();
            posSearchDock.Children.Add(new TextBlock
            {
                Text = "🔍",
                FontSize = 13,
                Foreground = Theme.Faint,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });
            var posSearchInput = new TextBox
            {
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                FontSize = 13,
                Foreground = Theme.Text,
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Buscar platos o bebidas del menú actual"
            };
            posSearchInput.TextChanged += (s, e) =>
            {
                _searchQuery = posSearchInput.Text.Trim();
                RenderProductCards();
            };
            posSearchDock.Children.Add(posSearchInput);
            posSearchBorder.Child = posSearchDock;
            topCategoriesStack.Children.Add(posSearchBorder);

            _catCardsWrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            topCategoriesStack.Children.Add(_catCardsWrap);

            _posMenuSectionTitle = new TextBlock
            {
                Text = "Menú Completo",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Text,
                Margin = new Thickness(2, 2, 0, 2)
            };
            topCategoriesStack.Children.Add(_posMenuSectionTitle);
            leftDock.Children.Add(topCategoriesStack);

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

            // DERECHA: Panel "Pedido Actual" (Exacto a "Invoice" en la referencia)
            var invoiceCard = new Border
            {
                Background = Theme.Card,
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(18),
                Effect = Theme.CardShadow()
            };
            Grid.SetColumn(invoiceCard, 2);

            var cartDock = new DockPanel { LastChildFill = true };

            // Cabecera "Pedido Actual"
            var cartHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            DockPanel.SetDock(cartHeader, Dock.Top);

            var btnClearCart = CreateGhostButton("Vaciar", () =>
            {
                _cart.Clear();
                RenderProductCards();
                RenderCart();
            });
            btnClearCart.Padding = new Thickness(10, 5, 10, 5);
            DockPanel.SetDock(btnClearCart, Dock.Right);
            cartHeader.Children.Add(btnClearCart);

            cartHeader.Children.Add(new TextBlock
            {
                Text = "Pedido Actual",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Text,
                VerticalAlignment = VerticalAlignment.Center
            });
            cartDock.Children.Add(cartHeader);

            // Pie del Panel Derecho: Nota general + Resumen de Pago + Medios de Pago + Botón Azul
            var checkoutStack = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            DockPanel.SetDock(checkoutStack, Dock.Bottom);

            // Caja "Resumen de Pago" (como "Payment Summary" en la captura)
            var summaryBox = new Border
            {
                Background = Theme.SurfaceSoft,
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.CardRadius,
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var sumStack = new StackPanel();
            sumStack.Children.Add(new TextBlock
            {
                Text = "Resumen de Pago",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Text,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var subRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            _subtotalAmountLabel = new TextBlock { Text = "$0", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text };
            DockPanel.SetDock(_subtotalAmountLabel, Dock.Right);
            subRow.Children.Add(_subtotalAmountLabel);
            subRow.Children.Add(new TextBlock { Text = "Subtotal", FontSize = 12, Foreground = Theme.Muted });
            sumStack.Children.Add(subRow);

            sumStack.Children.Add(new Border { Height = 1, Background = Theme.Border, Margin = new Thickness(0, 4, 0, 8) });

            var totRow = new DockPanel();
            _totalAmountLabel = new TextBlock { Text = "$0", FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Theme.Text };
            DockPanel.SetDock(_totalAmountLabel, Dock.Right);
            totRow.Children.Add(_totalAmountLabel);
            totRow.Children.Add(new TextBlock { Text = "Total a Cobrar", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Theme.Text, VerticalAlignment = VerticalAlignment.Center });
            sumStack.Children.Add(totRow);

            summaryBox.Child = sumStack;
            checkoutStack.Children.Add(summaryBox);

            // Selector segmentado dinámico de Medios de Pago (configurado en Ajustes)
            var payContainer = new Border
            {
                Background = Theme.Hover,
                CornerRadius = Theme.CardRadius,
                Padding = new Thickness(4),
                Margin = new Thickness(0, 0, 0, 10)
            };
            _payMethodsGrid = new UniformGrid { Columns = 2 };
            payContainer.Child = _payMethodsGrid;
            checkoutStack.Children.Add(payContainer);

            // Panel contextual de Efectivo (Vuelto rápido)
            _cashContextPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            var cashBoxBorder = new Border
            {
                Background = Theme.SurfaceSoft,
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
                FontSize = 13,
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
                Foreground = Theme.Muted,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });

            _cashInputBox = new TextBox
            {
                Width = 90,
                Padding = new Thickness(6, 4, 6, 4),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                BorderBrush = Theme.BorderStrong,
                BorderThickness = new Thickness(1),
                Background = Theme.Card,
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
                chip.Padding = new Thickness(3, 4, 3, 4);
                chip.Margin = new Thickness(1);
                ((TextBlock)chip.Child).FontSize = 10;
                quickBillsGrid.Children.Add(chip);
            }
            cashInner.Children.Add(quickBillsGrid);
            cashBoxBorder.Child = cashInner;
            _cashContextPanel.Children.Add(cashBoxBorder);
            checkoutStack.Children.Add(_cashContextPanel);

            // Panel contextual de Mercado Pago
            _mpContextPanel = new Border
            {
                Background = Theme.BrandBg,
                BorderBrush = Theme.BrandSoft,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.BtnRadius,
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 0, 0, 10),
                Visibility = Visibility.Collapsed
            };
            _mpAliasLabel = new TextBlock
            {
                Text = "Alias Transferencia:  araucana24",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Brand,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _mpContextPanel.Child = _mpAliasLabel;
            checkoutStack.Children.Add(_mpContextPanel);

            // Nota general opcional del pedido
            var noteRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            noteRow.Children.Add(new TextBlock
            {
                Text = "Nota general:",
                FontSize = 11,
                Foreground = Theme.Muted,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });
            _generalNotesBox = CreateStyledTextBox();
            _generalNotesBox.Padding = new Thickness(8, 5, 8, 5);
            _generalNotesBox.FontSize = 12;
            noteRow.Children.Add(_generalNotesBox);
            checkoutStack.Children.Add(noteRow);

            // Botón principal azul "Cobrar e Imprimir Pedido" (como "Place An Order")
            var btnPlaceOrder = CreatePrimaryButton("Cobrar e Imprimir Pedido (F12)", SubmitOrder, false);
            btnPlaceOrder.Padding = new Thickness(14, 13, 14, 13);
            checkoutStack.Children.Add(btnPlaceOrder);

            cartDock.Children.Add(checkoutStack);

            // Lista central de artículos en el pedido (con foto, comentario y lápiz ✏️)
            var cartScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _cartListPanel = new StackPanel();
            cartScroll.Content = _cartListPanel;
            cartDock.Children.Add(cartScroll);

            invoiceCard.Child = cartDock;
            grid.Children.Add(invoiceCard);

            RenderPaymentMethodButtons();
            return grid;
        }

        private string GetCategoryIcon(string cat)
        {
            string low = (cat ?? "").ToLowerInvariant();
            if (low.Contains("todos")) return "📋";
            if (low.Contains("almuerzo")) return "🍽️";
            if (low.Contains("principal") || low.Contains("comida")) return "🍕";
            if (low.Contains("bebida") || low.Contains("barra")) return "🥤";
            if (low.Contains("llevar")) return "🥡";
            if (low.Contains("postre") || low.Contains("dulce")) return "🍰";
            return "🍴";
        }

        private void RenderCategoryCards()
        {
            _catCardsWrap.Children.Clear();
            int activeGroupId = GetCurrentActiveGroupId();
            var groupProds = _db.Products.Where(p => p.GroupId == activeGroupId && p.Active).ToList();

            var cats = new List<string> { "Todos" };
            foreach (var c in groupProds.Select(p => p.Category).Distinct())
            {
                if (!cats.Contains(c)) cats.Add(c);
            }
            if (!cats.Contains(_selectedCategory)) _selectedCategory = "Todos";

            foreach (var cat in cats)
            {
                bool active = _selectedCategory == cat;
                int countInCat = cat == "Todos" ? groupProds.Count : groupProds.Count(p => p.Category == cat);

                var card = new Border
                {
                    Width = 152,
                    Background = active ? Theme.Brand : Theme.Card,
                    BorderBrush = active ? Theme.Brand : Theme.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = Theme.CardRadius,
                    Padding = new Thickness(10, 10, 10, 10),
                    Margin = new Thickness(0, 0, 10, 8),
                    Cursor = Cursors.Hand,
                    Effect = Theme.CardShadow()
                };

                var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

                var iconBox = new Border
                {
                    Width = 36,
                    Height = 36,
                    CornerRadius = Theme.BtnRadius,
                    Background = active ? Brushes.White : Theme.Bg,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                iconBox.Child = new TextBlock
                {
                    Text = GetCategoryIcon(cat),
                    FontSize = 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                row.Children.Add(iconBox);

                var txtStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                txtStack.Children.Add(new TextBlock
                {
                    Text = cat,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = active ? Brushes.White : Theme.Text
                });
                txtStack.Children.Add(new TextBlock
                {
                    Text = countInCat + " en menú",
                    FontSize = 11,
                    Foreground = active ? Theme.BrandSoft : Theme.Muted
                });
                row.Children.Add(txtStack);

                card.Child = row;
                string catCopy = cat;
                card.MouseLeftButtonUp += (s, e) =>
                {
                    _selectedCategory = catCopy;
                    RenderCategoryCards();
                    RenderProductCards();
                };
                _catCardsWrap.Children.Add(card);
            }
        }

        private void RenderProductCards()
        {
            _productsWrap.Children.Clear();
            int activeGroupId = GetCurrentActiveGroupId();
            var activeGroup = GetCurrentActiveGroup();

            if (_posMenuSectionTitle != null)
            {
                _posMenuSectionTitle.Text = _selectedCategory == "Todos"
                    ? ("Menú: " + activeGroup.Name)
                    : ("Menú de " + _selectedCategory);
            }

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
                    Text = "No hay platos que coincidan con la búsqueda o categoría.",
                    FontSize = 13,
                    Foreground = Theme.Muted,
                    Margin = new Thickness(8, 20, 8, 8)
                });
                return;
            }

            foreach (var prod in filtered)
            {
                bool outOfStock = prod.StockEnabled && prod.StockQty <= 0;
                int qty = _cart.Where(c => c.Product.Id == prod.Id).Sum(c => c.Quantity);

                // Tarjeta horizontal estilo referencia (Foto cuadrada izquierda + Texto derecha + Precio y - 0 + abajo)
                var card = new Border
                {
                    Width = 266,
                    Background = Theme.Card,
                    BorderBrush = qty > 0 ? Theme.Brand : (outOfStock ? Theme.Err : Theme.Border),
                    BorderThickness = new Thickness(qty > 0 ? 1.5 : 1),
                    CornerRadius = Theme.CardRadius,
                    Margin = new Thickness(0, 0, 12, 12),
                    Padding = new Thickness(12),
                    Cursor = outOfStock ? Cursors.No : Cursors.Hand,
                    Opacity = outOfStock ? 0.6 : 1.0,
                    Effect = Theme.CardShadow()
                };

                var mainStack = new StackPanel();

                // Mitad superior: Foto (66x66) a la izquierda + Título y descripción a la derecha
                var topDock = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };

                var imgHost = new Border
                {
                    Width = 66,
                    Height = 66,
                    CornerRadius = Theme.BtnRadius,
                    Background = Theme.Bg,
                    ClipToBounds = true,
                    Margin = new Thickness(0, 0, 12, 0)
                };
                var bmp = Theme.LoadImageCached(prod.ImagePath);
                if (bmp != null)
                {
                    imgHost.Child = new Image { Source = bmp, Stretch = Stretch.UniformToFill };
                }
                else
                {
                    imgHost.Child = new TextBlock { Text = "🍽️", FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                }
                DockPanel.SetDock(imgHost, Dock.Left);
                topDock.Children.Add(imgHost);

                var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
                textStack.Children.Add(new TextBlock
                {
                    Text = prod.Name,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = Theme.Text,
                    TextWrapping = TextWrapping.Wrap,
                    MaxHeight = 36
                });

                string descText = string.IsNullOrWhiteSpace(prod.Description) ? prod.Category : prod.Description;
                if (prod.StockEnabled)
                    descText = (outOfStock ? "AGOTADO" : ("Stock: " + prod.StockQty + " u.")) + " · " + descText;

                textStack.Children.Add(new TextBlock
                {
                    Text = descText,
                    FontSize = 11,
                    Foreground = outOfStock ? Theme.Err : Theme.Muted,
                    TextWrapping = TextWrapping.Wrap,
                    MaxHeight = 30,
                    Margin = new Thickness(0, 3, 0, 0)
                });
                topDock.Children.Add(textStack);
                mainStack.Children.Add(topDock);

                // Mitad inferior: Precio grande a la izquierda + Controles [ − ]  0  [ + ] a la derecha
                var footDock = new DockPanel();

                var stepperStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(stepperStack, Dock.Right);

                var prodCopy = prod;

                var btnMinus = new Border
                {
                    Width = 28,
                    Height = 28,
                    CornerRadius = Theme.BtnRadius,
                    Background = Theme.Bg,
                    BorderBrush = Theme.Border,
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand
                };
                btnMinus.Child = new TextBlock
                {
                    Text = "−",
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = qty > 0 ? Theme.Text : Theme.Faint,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                btnMinus.MouseLeftButtonUp += (s, e) =>
                {
                    e.Handled = true;
                    DecrementCartProduct(prodCopy);
                };

                var lblQty = new TextBlock
                {
                    Text = qty.ToString(),
                    Width = 28,
                    TextAlignment = TextAlignment.Center,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = qty > 0 ? Theme.Text : Theme.Muted,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var btnPlus = new Border
                {
                    Width = 28,
                    Height = 28,
                    CornerRadius = Theme.BtnRadius,
                    Background = qty > 0 ? Theme.Brand : Theme.Bg,
                    BorderBrush = qty > 0 ? Theme.Brand : Theme.Border,
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand
                };
                btnPlus.Child = new TextBlock
                {
                    Text = "+",
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = qty > 0 ? Brushes.White : Theme.Brand,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                btnPlus.MouseLeftButtonUp += (s, e) =>
                {
                    e.Handled = true;
                    AddToCart(prodCopy);
                };

                stepperStack.Children.Add(btnMinus);
                stepperStack.Children.Add(lblQty);
                stepperStack.Children.Add(btnPlus);
                footDock.Children.Add(stepperStack);

                footDock.Children.Add(new TextBlock
                {
                    Text = PosStore.FormatMoney(prod.Price),
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = Theme.Text,
                    VerticalAlignment = VerticalAlignment.Center
                });

                mainStack.Children.Add(footDock);
                card.Child = mainStack;

                // Clic en la tarjeta también agrega +1
                card.MouseLeftButtonUp += (s, e) => AddToCart(prodCopy);

                _productsWrap.Children.Add(card);
            }
        }

        // =====================================================================
        // CARRITO DERECHO CON LÁPIZ ✏️ Y SELECTOR DE SABORES (DOCENA / MEDIA)
        // =====================================================================
        private int GetReservedQtyInCart(int productId, CartEntry excludeEntry = null)
        {
            int total = 0;
            foreach (var c in _cart)
            {
                if (c == excludeEntry) continue;
                if (c.Product.Id == productId)
                    total += c.Quantity;
                if (c.PackChoices != null)
                {
                    foreach (var pc in c.PackChoices)
                    {
                        if (pc.ProductId == productId)
                            total += pc.Quantity * c.Quantity;
                    }
                }
            }
            return total;
        }

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

            // Si es un Pack con selección de sabores (ej. Docena = 12, Media Docena = 6)
            if (prod.PackRequiredQty > 0)
            {
                OpenPackFlavorSelectorModal(prod, null);
                return;
            }

            int alreadyReserved = GetReservedQtyInCart(prod.Id);
            if (prod.StockEnabled && alreadyReserved + 1 > prod.StockQty)
            {
                int avail = Math.Max(0, prod.StockQty - alreadyReserved);
                MessageBox.Show(
                    string.Format("No hay suficiente stock de '{0}'.\nStock real disponible: {1} u.", prod.Name, avail),
                    "Límite de Stock",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var existing = _cart.FirstOrDefault(c => c.Product.Id == prod.Id && (c.PackChoices == null || c.PackChoices.Count == 0));
            if (existing != null)
            {
                existing.Quantity++;
            }
            else
            {
                _cart.Add(new CartEntry { Product = prod, Quantity = 1, Note = "" });
            }

            RenderProductCards();
            RenderCart();
        }

        private void DecrementCartProduct(ProductItem prod)
        {
            var existing = _cart.LastOrDefault(c => c.Product.Id == prod.Id);
            if (existing == null) return;
            existing.Quantity--;
            if (existing.Quantity <= 0)
                _cart.Remove(existing);
            RenderProductCards();
            RenderCart();
        }

        private void OpenPackFlavorSelectorModal(ProductItem packProd, CartEntry editingEntry)
        {
            int activeGroupId = GetCurrentActiveGroupId();
            string filter = string.IsNullOrWhiteSpace(packProd.PackSourceFilter) ? "Empanada" : packProd.PackSourceFilter;
            int requiredQty = packProd.PackRequiredQty;

            // 1. Buscar primero por Categoría exacta enlazada (ej. Categoría "Empanadas")
            var flavorProducts = _db.Products
                .Where(p => p.GroupId == activeGroupId && p.Active && p.PackRequiredQty == 0
                            && string.Equals((p.Category ?? "").Trim(), filter.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();

            // 2. Respaldo por nombre si la categoría aún no existía con ese nombre exacto
            if (flavorProducts.Count == 0)
            {
                flavorProducts = _db.Products
                    .Where(p => p.GroupId == activeGroupId && p.Active && p.PackRequiredQty == 0
                                && (p.Name ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }

            if (flavorProducts.Count == 0)
            {
                MessageBox.Show(
                    string.Format("No hay productos individuales cargados en la categoría '{0}' dentro de este menú.\n\nAndá a '🍽️ Menú' y creá los sabores individuales dentro de la categoría '{0}'.", filter),
                    "Categoría sin productos",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Cantidades seleccionadas por ProductId
            var selectedCounts = new Dictionary<int, int>();
            foreach (var fp in flavorProducts) selectedCounts[fp.Id] = 0;

            if (editingEntry != null && editingEntry.PackChoices != null)
            {
                foreach (var pc in editingEntry.PackChoices)
                {
                    if (selectedCounts.ContainsKey(pc.ProductId))
                        selectedCounts[pc.ProductId] = pc.Quantity;
                }
            }

            var win = new Window
            {
                Title = "Elegir Sabores — " + packProd.Name,
                Width = 520,
                Height = 560,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = Theme.Card,
                ResizeMode = ResizeMode.NoResize,
                FontFamily = new FontFamily("Segoe UI")
            };

            var rootDock = new DockPanel { Margin = new Thickness(20), LastChildFill = true };

            // Cabecera + Banner en vivo de "Restante: X"
            var topStack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            DockPanel.SetDock(topStack, Dock.Top);

            topStack.Children.Add(new TextBlock
            {
                Text = "🥟 " + packProd.Name + " (" + requiredQty + " unidades)",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Text
            });
            topStack.Children.Add(new TextBlock
            {
                Text = "Elegí los sabores. El stock se descontará automáticamente de cada sabor individual.",
                FontSize = 12,
                Foreground = Theme.Muted,
                Margin = new Thickness(0, 2, 0, 10)
            });

            var statusBanner = new Border
            {
                Background = Theme.WarnBg,
                BorderBrush = Theme.Warn,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.BtnRadius,
                Padding = new Thickness(14, 10, 14, 10)
            };
            var statusDock = new DockPanel();
            var lblRemaining = new TextBlock
            {
                Text = "Restante: " + requiredQty,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Warn,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(lblRemaining, Dock.Right);
            statusDock.Children.Add(lblRemaining);

            var lblSelected = new TextBlock
            {
                Text = "Seleccionadas: 0 / " + requiredQty,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Warn,
                VerticalAlignment = VerticalAlignment.Center
            };
            statusDock.Children.Add(lblSelected);
            statusBanner.Child = statusDock;
            topStack.Children.Add(statusBanner);
            rootDock.Children.Add(topStack);

            // Botonera inferior
            var bottomGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 14, 0, 0) };
            DockPanel.SetDock(bottomGrid, Dock.Bottom);

            var btnCancel = CreateGhostButton("Cancelar", () => win.Close());
            btnCancel.Margin = new Thickness(0, 0, 8, 0);
            bottomGrid.Children.Add(btnCancel);

            Border btnConfirm = null;
            Action refreshFlavorRows = null;

            btnConfirm = CreatePrimaryButton("✓ Confirmar Sabores", () =>
            {
                int totalSel = selectedCounts.Values.Sum();
                int remaining = requiredQty - totalSel;

                if (totalSel == 0)
                {
                    MessageBox.Show("Todavía no seleccionaste ningún sabor.", "Sin sabores", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (remaining > 0)
                {
                    var ans = MessageBox.Show(
                        string.Format("Todavía faltan seleccionar {0} empanada(s) para completar {1} (llevás {2} de {3}).\n\n¿Querés volver atrás para completar las restantes?\n\n(Tocá 'Sí' para seguir eligiendo, o 'No' si querés confirmar igual con {2}).",
                            remaining, packProd.Name, totalSel, requiredQty),
                        "Faltan sabores por elegir (Restante: " + remaining + ")",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    if (ans == MessageBoxResult.Yes)
                        return;
                }

                var choices = flavorProducts
                    .Where(fp => selectedCounts[fp.Id] > 0)
                    .Select(fp => new PackFlavorChoice
                    {
                        ProductId = fp.Id,
                        ProductName = fp.Name,
                        Quantity = selectedCounts[fp.Id]
                    })
                    .ToList();

                if (editingEntry != null)
                {
                    editingEntry.PackChoices = choices;
                }
                else
                {
                    _cart.Add(new CartEntry
                    {
                        Product = packProd,
                        Quantity = 1,
                        Note = "",
                        PackChoices = choices
                    });
                }

                RenderProductCards();
                RenderCart();
                win.Close();
            }, false);
            bottomGrid.Children.Add(btnConfirm);
            rootDock.Children.Add(bottomGrid);

            // Lista scrollable de sabores disponibles
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var flavorsStack = new StackPanel();
            scroll.Content = flavorsStack;
            rootDock.Children.Add(scroll);

            refreshFlavorRows = () =>
            {
                int totalSel = selectedCounts.Values.Sum();
                int remaining = Math.Max(0, requiredQty - totalSel);

                if (remaining == 0)
                {
                    statusBanner.Background = Theme.OkBg;
                    statusBanner.BorderBrush = Theme.Ok;
                    lblSelected.Foreground = Theme.Ok;
                    lblRemaining.Foreground = Theme.Ok;
                    lblSelected.Text = string.Format("✓ ¡Completo! {0} / {1}", totalSel, requiredQty);
                    lblRemaining.Text = "Restante: 0";
                }
                else
                {
                    statusBanner.Background = Theme.WarnBg;
                    statusBanner.BorderBrush = Theme.Warn;
                    lblSelected.Foreground = Theme.Warn;
                    lblRemaining.Foreground = Theme.Warn;
                    lblSelected.Text = string.Format("Seleccionadas: {0} / {1}", totalSel, requiredQty);
                    lblRemaining.Text = "Restante: " + remaining;
                }

                flavorsStack.Children.Clear();
                foreach (var fp in flavorProducts)
                {
                    int curCount = selectedCounts[fp.Id];
                    int reservedElsewhere = GetReservedQtyInCart(fp.Id, editingEntry);
                    int maxByStock = fp.StockEnabled ? Math.Max(0, fp.StockQty - reservedElsewhere) : 9999;
                    int canAddMore = Math.Min(remaining, Math.Max(0, maxByStock - curCount));
                    bool noStock = fp.StockEnabled && maxByStock <= 0;

                    var row = new Border
                    {
                        Background = curCount > 0 ? Theme.BrandBg : Theme.Bg,
                        BorderBrush = curCount > 0 ? Theme.Brand : Theme.Border,
                        BorderThickness = new Thickness(1),
                        CornerRadius = Theme.BtnRadius,
                        Padding = new Thickness(12, 10, 12, 10),
                        Margin = new Thickness(0, 0, 0, 8),
                        Opacity = noStock ? 0.5 : 1.0
                    };

                    var dock = new DockPanel();
                    var ctrl = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    DockPanel.SetDock(ctrl, Dock.Right);

                    var fpCopy = fp;

                    var bMinus = CreateIconButton("−", "Restar 1", () =>
                    {
                        if (selectedCounts[fpCopy.Id] > 0)
                        {
                            selectedCounts[fpCopy.Id]--;
                            refreshFlavorRows();
                        }
                    });
                    bMinus.Width = 30; bMinus.Height = 30;

                    var lblC = new TextBlock
                    {
                        Text = curCount.ToString(),
                        Width = 32,
                        TextAlignment = TextAlignment.Center,
                        FontSize = 15,
                        FontWeight = FontWeights.Bold,
                        Foreground = curCount > 0 ? Theme.Brand : Theme.Text,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    var bPlus = CreateIconButton("+", "Sumar 1", () =>
                    {
                        if (canAddMore >= 1)
                        {
                            selectedCounts[fpCopy.Id]++;
                            refreshFlavorRows();
                        }
                        else if (remaining == 0)
                        {
                            MessageBox.Show("Ya completaste las " + requiredQty + " unidades de " + packProd.Name + ".", "Pack Completo", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            MessageBox.Show("No queda más stock disponible de '" + fpCopy.Name + "'.", "Sin Stock", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    });
                    bPlus.Width = 30; bPlus.Height = 30;
                    if (canAddMore >= 1)
                    {
                        bPlus.Background = Theme.Brand;
                        bPlus.BorderBrush = Theme.Brand;
                        ((TextBlock)bPlus.Child).Foreground = Brushes.White;
                    }

                    var bPlus3 = CreateGhostButton("+3", () =>
                    {
                        int add = Math.Min(3, canAddMore);
                        if (add > 0)
                        {
                            selectedCounts[fpCopy.Id] += add;
                            refreshFlavorRows();
                        }
                    });
                    bPlus3.Padding = new Thickness(8, 5, 8, 5);
                    bPlus3.Margin = new Thickness(6, 0, 0, 0);

                    ctrl.Children.Add(bMinus);
                    ctrl.Children.Add(lblC);
                    ctrl.Children.Add(bPlus);
                    ctrl.Children.Add(bPlus3);
                    dock.Children.Add(ctrl);

                    var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    infoStack.Children.Add(new TextBlock
                    {
                        Text = fp.Name,
                        FontSize = 14,
                        FontWeight = FontWeights.Bold,
                        Foreground = Theme.Text
                    });
                    string stLabel = fp.StockEnabled
                        ? (noStock ? "SIN STOCK (0 u.)" : string.Format("Stock disponible: {0} u.", maxByStock - curCount))
                        : "Stock libre";
                    infoStack.Children.Add(new TextBlock
                    {
                        Text = stLabel,
                        FontSize = 11,
                        Foreground = noStock ? Theme.Err : Theme.Muted
                    });
                    dock.Children.Add(infoStack);

                    row.Child = dock;
                    flavorsStack.Children.Add(row);
                }
            };

            refreshFlavorRows();
            win.Content = rootDock;
            win.ShowDialog();
        }

        private void RenderCart()
        {
            _cartListPanel.Children.Clear();
            if (_cart.Count == 0)
            {
                _cartListPanel.Children.Add(new TextBlock
                {
                    Text = "Tocá cualquier plato del menú para agregarlo al pedido.\nLuego podés hacer clic en el lápiz ✏️ para agregarle un comentario.",
                    FontSize = 12,
                    Foreground = Theme.Faint,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(16, 45, 16, 20)
                });
            }
            else
            {
                foreach (var entry in _cart.ToList())
                {
                    var entryCopy = entry;
                    var rowBorder = new Border
                    {
                        Background = Theme.Card,
                        BorderBrush = Theme.Border,
                        BorderThickness = new Thickness(0, 0, 0, 1),
                        Padding = new Thickness(4, 10, 4, 10),
                        Cursor = Cursors.Hand,
                        ToolTip = "Hacé clic o tocá el lápiz ✏️ para agregar o editar un comentario en este artículo"
                    };

                    rowBorder.MouseEnter += (s, e) => rowBorder.Background = Theme.SurfaceSoft;
                    rowBorder.MouseLeave += (s, e) => rowBorder.Background = Theme.Card;

                    var dock = new DockPanel();

                    // Derecha: Subtotal + Botón Lápiz ✏️ para comentario del artículo
                    var rightBox = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                    DockPanel.SetDock(rightBox, Dock.Right);

                    var btnPencil = new Border
                    {
                        Width = 30,
                        Height = 30,
                        CornerRadius = Theme.BtnRadius,
                        Background = !string.IsNullOrWhiteSpace(entry.Note) ? Theme.BrandBg : Theme.Bg,
                        BorderBrush = !string.IsNullOrWhiteSpace(entry.Note) ? Theme.Brand : Theme.Border,
                        BorderThickness = new Thickness(1),
                        Margin = new Thickness(0, 0, 10, 0),
                        ToolTip = "Agregar o editar comentario para este plato (sale en ticket y cocina)",
                        Cursor = Cursors.Hand
                    };
                    btnPencil.Child = new TextBlock
                    {
                        Text = "✏️",
                        FontSize = 13,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    btnPencil.MouseLeftButtonUp += (s, e) =>
                    {
                        e.Handled = true;
                        OpenItemCommentModal(entryCopy);
                    };
                    rightBox.Children.Add(btnPencil);

                    rightBox.Children.Add(new TextBlock
                    {
                        Text = PosStore.FormatMoney(entry.Subtotal),
                        FontSize = 13,
                        FontWeight = FontWeights.Bold,
                        Foreground = Theme.Text,
                        VerticalAlignment = VerticalAlignment.Center,
                        MinWidth = 60,
                        TextAlignment = TextAlignment.Right
                    });
                    dock.Children.Add(rightBox);

                    // Izquierda: Miniatura cuadrada redondeada (48x48) como en "Invoice"
                    var thumb = new Border
                    {
                        Width = 48,
                        Height = 48,
                        CornerRadius = Theme.BtnRadius,
                        Background = Theme.Bg,
                        ClipToBounds = true,
                        Margin = new Thickness(0, 0, 10, 0)
                    };
                    var bmp = Theme.LoadImageCached(entry.Product.ImagePath);
                    if (bmp != null)
                        thumb.Child = new Image { Source = bmp, Stretch = Stretch.UniformToFill };
                    else
                        thumb.Child = new TextBlock { Text = "🍽️", FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    DockPanel.SetDock(thumb, Dock.Left);
                    dock.Children.Add(thumb);

                    // Centro: Nombre + Cantidad + Comentario del artículo
                    var centerStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    centerStack.Children.Add(new TextBlock
                    {
                        Text = entry.Product.Name,
                        FontSize = 13,
                        FontWeight = FontWeights.Bold,
                        Foreground = Theme.Text,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                    centerStack.Children.Add(new TextBlock
                    {
                        Text = entry.Quantity + "x  ·  " + PosStore.FormatMoney(entry.Product.Price) + " c/u",
                        FontSize = 11,
                        Foreground = Theme.Muted
                    });

                    if (entry.PackChoices != null && entry.PackChoices.Count > 0)
                    {
                        string flavorsSummary = string.Join(", ", entry.PackChoices.Select(pc => pc.Quantity + "x " + pc.ProductName.Replace("Empanada de ", "").Replace("Empanada ", "")));
                        centerStack.Children.Add(new TextBlock
                        {
                            Text = "🥟 " + flavorsSummary,
                            FontSize = 11,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = Theme.Text,
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 2, 0, 0)
                        });
                    }

                    bool hasNote = !string.IsNullOrWhiteSpace(entry.Note);
                    centerStack.Children.Add(new TextBlock
                    {
                        Text = hasNote ? ("📝 " + entry.Note) : "+ Agregar comentario (✏️)",
                        FontSize = 11,
                        FontWeight = hasNote ? FontWeights.SemiBold : FontWeights.Normal,
                        Foreground = hasNote ? Theme.Brand : Theme.Faint,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Margin = new Thickness(0, 2, 0, 0)
                    });
                    dock.Children.Add(centerStack);

                    rowBorder.Child = dock;
                    rowBorder.MouseLeftButtonUp += (s, e) => OpenItemCommentModal(entryCopy);

                    _cartListPanel.Children.Add(rowBorder);
                }
            }

            UpdateTotalsAndChange();
        }

        private void OpenItemCommentModal(CartEntry entry)
        {
            var win = new Window
            {
                Title = "Comentario / Modificación del Artículo",
                Width = 440,
                Height = 350,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = Theme.Card,
                ResizeMode = ResizeMode.NoResize,
                FontFamily = new FontFamily("Segoe UI")
            };

            var stack = new StackPanel { Margin = new Thickness(22) };
            stack.Children.Add(new TextBlock
            {
                Text = "✏️ Comentario para: " + entry.Product.Name,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Text,
                Margin = new Thickness(0, 0, 0, 4)
            });
            stack.Children.Add(new TextBlock
            {
                Text = "Este comentario se imprimirá en el Ticket del Cliente y destacado en la Comanda de Cocina.",
                FontSize = 12,
                Foreground = Theme.Muted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });

            // Botones rápidos de modificaciones comunes
            stack.Children.Add(new TextBlock
            {
                Text = "Sugerencias rápidas (tocá para agregar):",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Muted,
                Margin = new Thickness(0, 0, 0, 6)
            });

            var noteInput = CreateStyledTextBox();
            noteInput.Text = entry.Note ?? "";
            noteInput.FontSize = 13;

            var quickWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            string[] quickMods = { "Sin mayonesa", "Sin sal", "Sin tomate", "Sin cebolla", "Sin aderezos", "Bien cocido", "Para llevar" };
            foreach (var mod in quickMods)
            {
                string mCopy = mod;
                var qBtn = CreateGhostButton(mod, () =>
                {
                    string cur = noteInput.Text.Trim();
                    noteInput.Text = string.IsNullOrEmpty(cur) ? mCopy : (cur + ", " + mCopy);
                    noteInput.CaretIndex = noteInput.Text.Length;
                });
                qBtn.Padding = new Thickness(8, 4, 8, 4);
                qBtn.Margin = new Thickness(0, 0, 5, 5);
                ((TextBlock)qBtn.Child).FontSize = 11;
                quickWrap.Children.Add(qBtn);
            }
            stack.Children.Add(quickWrap);

            stack.Children.Add(new TextBlock
            {
                Text = "Comentario personalizado:",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Text,
                Margin = new Thickness(0, 0, 0, 4)
            });
            stack.Children.Add(noteInput);

            var btnsRow = new UniformGrid { Columns = entry.Product.PackRequiredQty > 0 ? 3 : 2, Margin = new Thickness(0, 16, 0, 0) };
            var btnClear = CreateGhostButton("Borrar", () =>
            {
                entry.Note = "";
                RenderCart();
                win.Close();
            });
            btnClear.Margin = new Thickness(0, 0, 6, 0);
            btnsRow.Children.Add(btnClear);

            if (entry.Product.PackRequiredQty > 0)
            {
                var btnFlavors = CreateGhostButton("🥟 Sabores", () =>
                {
                    win.Close();
                    OpenPackFlavorSelectorModal(entry.Product, entry);
                });
                btnFlavors.Margin = new Thickness(0, 0, 6, 0);
                btnsRow.Children.Add(btnFlavors);
            }

            var btnSave = CreatePrimaryButton("✓ Guardar", () =>
            {
                entry.Note = noteInput.Text.Trim();
                RenderCart();
                win.Close();
            }, false);
            btnsRow.Children.Add(btnSave);
            stack.Children.Add(btnsRow);

            win.Content = stack;
            win.Loaded += (s, e) => { noteInput.Focus(); noteInput.SelectAll(); };
            win.ShowDialog();
        }

        private void RenderPaymentMethodButtons()
        {
            if (_payMethodsGrid == null) return;
            _payMethodsGrid.Children.Clear();
            _payMethodCards.Clear();

            var enabledMethods = new List<Tuple<string, string>>();
            if (_db.Settings.EnableCash) enabledMethods.Add(Tuple.Create("Efectivo", "💵"));
            if (_db.Settings.EnableTransfer) enabledMethods.Add(Tuple.Create("Transferencia", "📱"));
            if (_db.Settings.EnableCard) enabledMethods.Add(Tuple.Create("Tarjeta", "💳"));
            if (enabledMethods.Count == 0) enabledMethods.Add(Tuple.Create("Efectivo", "💵"));

            _payMethodsGrid.Columns = enabledMethods.Count;

            foreach (var pm in enabledMethods)
            {
                var pCard = new Border
                {
                    CornerRadius = Theme.BtnRadius,
                    Padding = new Thickness(6, 8, 6, 8),
                    Margin = new Thickness(2),
                    Cursor = Cursors.Hand
                };
                var pStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                pStack.Children.Add(new TextBlock { Text = pm.Item2, FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center });
                pStack.Children.Add(new TextBlock { Text = pm.Item1, FontSize = 11, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) });
                pCard.Child = pStack;

                string methodCopy = pm.Item1;
                pCard.MouseLeftButtonUp += (s, e) => SelectPaymentMethod(methodCopy);
                _payMethodCards[pm.Item1] = pCard;
                _payMethodsGrid.Children.Add(pCard);
            }

            if (!_payMethodCards.ContainsKey(_paymentMethod))
                _paymentMethod = enabledMethods[0].Item1;

            SelectPaymentMethod(_paymentMethod);
        }

        private void SelectPaymentMethod(string method)
        {
            _paymentMethod = method;
            foreach (var kv in _payMethodCards)
            {
                bool active = kv.Key == method;
                kv.Value.Background = active ? Theme.Card : Brushes.Transparent;
                kv.Value.Effect = active ? Theme.CardShadow() : null;
                var sp = (StackPanel)kv.Value.Child;
                ((TextBlock)sp.Children[1]).Foreground = active ? Theme.Brand : Theme.Muted;
                ((TextBlock)sp.Children[1]).FontWeight = active ? FontWeights.Bold : FontWeights.SemiBold;
            }

            if (_cashContextPanel != null)
                _cashContextPanel.Visibility = method == "Efectivo" ? Visibility.Visible : Visibility.Collapsed;
            if (_mpContextPanel != null)
            {
                _mpContextPanel.Visibility = method == "Transferencia" ? Visibility.Visible : Visibility.Collapsed;
                _mpAliasLabel.Text = "Alias Transferencia:  " + (_db.Settings.MpAlias ?? "araucana24");
            }

            UpdateTotalsAndChange();
        }

        private void UpdateTotalsAndChange()
        {
            decimal total = _cart.Sum(c => c.Subtotal);
            if (_subtotalAmountLabel != null) _subtotalAmountLabel.Text = PosStore.FormatMoney(total);
            if (_totalAmountLabel != null) _totalAmountLabel.Text = PosStore.FormatMoney(total);

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
                Notes = _generalNotesBox.Text.Trim(),
                Status = "completado",
                KitchenStatus = "pendiente",
                KitchenPrinted = false,
                CreatedAt = DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
                FinishedAt = "",
                DeliveredAt = "",
                Items = _cart.Select(c => new OrderLineItem
                {
                    ProductId = c.Product.Id,
                    ProductName = c.Product.Name,
                    Category = c.Product.Category,
                    Quantity = c.Quantity,
                    UnitPrice = c.Product.Price,
                    Subtotal = c.Subtotal,
                    Note = c.Note ?? "",
                    PackChoices = c.PackChoices != null
                        ? c.PackChoices.Select(pc => new PackFlavorChoice { ProductId = pc.ProductId, ProductName = pc.ProductName, Quantity = pc.Quantity }).ToList()
                        : new List<PackFlavorChoice>()
                }).ToList()
            };

            // Descontar stock real (si es Pack/Docena, descuenta el stock de cada sabor elegido)
            foreach (var line in newOrder.Items)
            {
                if (line.PackChoices != null && line.PackChoices.Count > 0)
                {
                    foreach (var pc in line.PackChoices)
                    {
                        var flavorProd = _db.Products.FirstOrDefault(x => x.Id == pc.ProductId);
                        if (flavorProd != null && flavorProd.StockEnabled)
                            flavorProd.StockQty = Math.Max(0, flavorProd.StockQty - (pc.Quantity * line.Quantity));
                    }
                }
                else
                {
                    var p = _db.Products.FirstOrDefault(x => x.Id == line.ProductId);
                    if (p != null && p.StockEnabled)
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
            _generalNotesBox.Text = "";
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
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error al imprimir", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        // =====================================================================
        // VISTA 3: "MENÚ" (GRUPOS / TIPOS DE EVENTO + PLATOS Y FOTOS)
        // =====================================================================
        private UIElement BuildMenuView()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(310) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

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

            var btnUseGroupInCaja = CreateGhostButton("✓ Activar en Caja Actual", () =>
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
                ShowToast("✓ Menú '" + grp.Name + "' activado en la Caja Actual.");
            });
            btnUseGroupInCaja.Margin = new Thickness(0, 0, 8, 0);
            rtButtons.Children.Add(btnUseGroupInCaja);

            var btnAddDishToGroup = CreatePrimaryButton("+ Nuevo Plato en este Menú", () => OpenProductEditorModal(null), false);
            rtButtons.Children.Add(btnAddDishToGroup);
            rtDock.Children.Add(rtButtons);

            var rtTitles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _selectedGroupHeaderTitle = new TextBlock { Text = "Menú del Evento", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Theme.Text };
            _selectedGroupHeaderSub = new TextBlock { Text = "Platos, precios, fotos y stock asignados", FontSize = 12, Foreground = Theme.Muted };
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

                var btnEditG = CreateIconButton("✏️", "Editar nombre del grupo", () => OpenGroupEditorModal(grpCopy));
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
                    Foreground = isSelected ? Theme.Brand : Theme.Text,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                });
                st.Children.Add(headRow);

                var metaRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                metaRow.Children.Add(new TextBlock { Text = dishesCount + " platos", FontSize = 11, Foreground = Theme.Muted, VerticalAlignment = VerticalAlignment.Center });

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
                    activeTag.Child = new TextBlock { Text = "ACTIVO EN CAJA", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Theme.Ok };
                    metaRow.Children.Add(activeTag);
                }
                st.Children.Add(metaRow);

                card.Child = st;
                card.MouseLeftButtonUp += (s, e) => { _editingGroupId = grpCopy.Id; RenderMenuSection(); };
                _groupsListStack.Children.Add(card);
            }

            var currentGrp = _db.MenuGroups.First(g => g.Id == _editingGroupId);
            _selectedGroupHeaderTitle.Text = "Menú: " + currentGrp.Name;
            _selectedGroupHeaderSub.Text = string.IsNullOrWhiteSpace(currentGrp.Description)
                ? "Administrá los platos, fotos y precios de este tipo de evento"
                : currentGrp.Description;

            _groupDishesStack.Children.Clear();
            var groupProducts = _db.Products.Where(p => p.GroupId == _editingGroupId).ToList();

            if (groupProducts.Count == 0)
            {
                var emptyCard = new Border { Background = Theme.Card, BorderBrush = Theme.Border, BorderThickness = new Thickness(1), CornerRadius = Theme.CardRadius, Padding = new Thickness(24) };
                emptyCard.Child = new TextBlock { Text = "Este tipo de evento todavía no tiene platos cargados. Tocá '+ Nuevo Plato en este Menú'.", FontSize = 13, Foreground = Theme.Muted };
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
                    Width = 84,
                    Height = 52,
                    CornerRadius = Theme.BtnRadius,
                    Background = Theme.Bg,
                    ClipToBounds = true,
                    Margin = new Thickness(0, 0, 14, 0)
                };
                var bmp = Theme.LoadImageCached(prod.ImagePath);
                if (bmp != null) thumbBorder.Child = new Image { Source = bmp, Stretch = Stretch.UniformToFill };
                DockPanel.SetDock(thumbBorder, Dock.Left);
                dock.Children.Add(thumbBorder);

                var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
                titleRow.Children.Add(new TextBlock { Text = prod.Name, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = prod.Active ? Theme.Text : Theme.Faint });

                var catBadge = new Border { Background = Theme.BrandBg, CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                catBadge.Child = new TextBlock { Text = prod.Category, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Theme.Brand };
                titleRow.Children.Add(catBadge);
                info.Children.Add(titleRow);

                string stockInfo = prod.StockEnabled ? ("Stock: " + prod.StockQty + " u.") : "Stock libre";
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
                Title = existing == null ? "Nuevo Tipo de Evento / Grupo" : "Editar Tipo de Evento",
                Width = 440,
                Height = 320,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = Theme.Card,
                ResizeMode = ResizeMode.NoResize,
                FontFamily = new FontFamily("Segoe UI")
            };

            var stack = new StackPanel { Margin = new Thickness(22) };
            stack.Children.Add(new TextBlock { Text = existing == null ? "Crear Nuevo Tipo de Evento" : "Editar Tipo de Evento", FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Theme.Text, Margin = new Thickness(0, 0, 0, 12) });

            stack.Children.Add(new TextBlock { Text = "Nombre del Tipo de Evento:", FontSize = 12, Foreground = Theme.Muted, Margin = new Thickness(0, 0, 0, 4) });
            var nameBox = CreateStyledTextBox();
            nameBox.Text = existing != null ? existing.Name : "";
            stack.Children.Add(nameBox);

            stack.Children.Add(new TextBlock { Text = "Descripción (opcional):", FontSize = 12, Foreground = Theme.Muted, Margin = new Thickness(0, 10, 0, 4) });
            var descBox = CreateStyledTextBox();
            descBox.Text = existing != null ? existing.Description : "";
            stack.Children.Add(descBox);

            CheckBox chkCopyFromCurrent = null;
            if (existing == null)
            {
                chkCopyFromCurrent = new CheckBox { Content = "Copiar platos del grupo actual como base", IsChecked = false, Margin = new Thickness(0, 12, 0, 4), FontSize = 12 };
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
                    _db.MenuGroups.Add(new MenuGroup { Id = newGroupId, Name = nameBox.Text.Trim(), Description = descBox.Text.Trim() });

                    if (chkCopyFromCurrent != null && chkCopyFromCurrent.IsChecked == true)
                    {
                        int nextProdId = _db.Products.Count == 0 ? 1 : _db.Products.Max(p => p.Id) + 1;
                        foreach (var sp in _db.Products.Where(p => p.GroupId == _editingGroupId).ToList())
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
                    File.Copy(dlg.FileName, Path.Combine(PosStore.ImagesDir, destName), true);

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
                catch (Exception ex) { MessageBox.Show(ex.Message, "Error al copiar imagen", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }

        private void OpenProductEditorModal(ProductItem existing)
        {
            var grp = _db.MenuGroups.FirstOrDefault(g => g.Id == _editingGroupId) ?? GetCurrentActiveGroup();
            var groupProducts = _db.Products.Where(p => p.GroupId == _editingGroupId).ToList();

            // Categorías existentes en este menú (tanto individuales como generales)
            var existingCategories = groupProducts
                .Select(p => (p.Category ?? "").Trim())
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var defCat in new[] { "Empanadas", "Principales", "Almuerzo", "Bebidas", "Para Llevar", "Postres" })
            {
                if (!existingCategories.Any(c => string.Equals(c, defCat, StringComparison.OrdinalIgnoreCase)))
                    existingCategories.Add(defCat);
            }

            // Categorías que tienen productos individuales (para enlazar stock de Combos/Packs)
            var individualCategories = groupProducts
                .Where(p => p.PackRequiredQty == 0)
                .Select(p => (p.Category ?? "").Trim())
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (individualCategories.Count == 0)
                individualCategories.AddRange(existingCategories);

            var win = new Window
            {
                Title = existing == null ? ("Nuevo Producto en " + grp.Name) : ("Editar: " + existing.Name),
                Width = 520,
                Height = 680,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = Theme.Card,
                ResizeMode = ResizeMode.NoResize,
                FontFamily = new FontFamily("Segoe UI")
            };

            string selectedMode = existing == null ? "" : (existing.PackRequiredQty > 0 ? "combo" : "individual");
            string selectedImagePath = existing != null ? (existing.ImagePath ?? "") : "";
            string pendingLocalPhotoSource = "";

            var rootDock = new DockPanel { Margin = new Thickness(22), LastChildFill = true };

            // =================================================================
            // PASO 1: SELECCIÓN DE TIPO (INDIVIDUAL vs COMBO/PACK)
            // =================================================================
            var topStepStack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            DockPanel.SetDock(topStepStack, Dock.Top);

            topStepStack.Children.Add(new TextBlock
            {
                Text = existing == null ? ("Nuevo Producto en: " + grp.Name) : ("Editar Producto: " + existing.Name),
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Text,
                Margin = new Thickness(0, 0, 0, 4)
            });

            topStepStack.Children.Add(new TextBlock
            {
                Text = "Paso 1: Elegí cómo se vende este producto",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Brand,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var modeGrid = new UniformGrid { Columns = 2 };

            var cardIndiv = new Border
            {
                Background = Theme.Bg,
                BorderBrush = Theme.BorderStrong,
                BorderThickness = new Thickness(1.5),
                CornerRadius = Theme.CardRadius,
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand
            };
            var ciStack = new StackPanel();
            var ciTitle = new TextBlock { Text = "🍽️ Venta Individual", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Theme.Text };
            var ciSub = new TextBlock { Text = "Por unidad (ej. Empanada Carne, Pancho, Bebida)", FontSize = 11, Foreground = Theme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            ciStack.Children.Add(ciTitle);
            ciStack.Children.Add(ciSub);
            cardIndiv.Child = ciStack;

            var cardCombo = new Border
            {
                Background = Theme.Bg,
                BorderBrush = Theme.BorderStrong,
                BorderThickness = new Thickness(1.5),
                CornerRadius = Theme.CardRadius,
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(6, 0, 0, 0),
                Cursor = Cursors.Hand
            };
            var ccStack = new StackPanel();
            var ccTitle = new TextBlock { Text = "📦 Combo / Pack", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Theme.Text };
            var ccSub = new TextBlock { Text = "Varios sabores de una categoría (ej. Media o Docena)", FontSize = 11, Foreground = Theme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            ccStack.Children.Add(ccTitle);
            ccStack.Children.Add(ccSub);
            cardCombo.Child = ccStack;

            modeGrid.Children.Add(cardIndiv);
            modeGrid.Children.Add(cardCombo);
            topStepStack.Children.Add(modeGrid);
            rootDock.Children.Add(topStepStack);

            // =================================================================
            // PASO 2: CAMPOS QUE SE DESBLOQUEAN AL ELEGIR EL TIPO
            // =================================================================
            var lockedHint = new Border
            {
                Background = Theme.SurfaceSoft,
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.CardRadius,
                Padding = new Thickness(24),
                VerticalAlignment = VerticalAlignment.Center
            };
            lockedHint.Child = new TextBlock
            {
                Text = "👆 Primero seleccioná arriba si el producto es\n'Venta Individual' o 'Combo / Pack' para continuar.",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Muted,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };

            var formScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = Visibility.Collapsed };
            var formStack = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };

            // Foto del producto (opcional, integrada en el mismo formulario)
            var photoCard = new Border
            {
                Background = Theme.SurfaceSoft,
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.BtnRadius,
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var photoDock = new DockPanel();
            var thumbBox = new Border
            {
                Width = 56,
                Height = 56,
                CornerRadius = Theme.BtnRadius,
                Background = Theme.Card,
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(1),
                ClipToBounds = true,
                Margin = new Thickness(0, 0, 12, 0)
            };
            var thumbImg = new Image { Stretch = Stretch.UniformToFill };
            var thumbPlaceholder = new TextBlock { Text = "📷", FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

            Action refreshPhotoPreview = () =>
            {
                string pathToShow = !string.IsNullOrEmpty(pendingLocalPhotoSource) ? pendingLocalPhotoSource : selectedImagePath;
                var bmp = Theme.LoadImageCached(pathToShow);
                if (bmp != null)
                {
                    thumbImg.Source = bmp;
                    thumbBox.Child = thumbImg;
                }
                else
                {
                    thumbBox.Child = thumbPlaceholder;
                }
            };
            refreshPhotoPreview();
            DockPanel.SetDock(thumbBox, Dock.Left);
            photoDock.Children.Add(thumbBox);

            var photoBtns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(photoBtns, Dock.Right);

            var btnPickPhoto = CreateGhostButton("📷 Elegir Foto", () =>
            {
                var dlg = new OpenFileDialog
                {
                    Title = "Seleccionar foto del producto (opcional)",
                    Filter = "Imágenes (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp"
                };
                if (dlg.ShowDialog(win) == true)
                {
                    pendingLocalPhotoSource = dlg.FileName;
                    refreshPhotoPreview();
                }
            });
            btnPickPhoto.Margin = new Thickness(0, 0, 6, 0);
            photoBtns.Children.Add(btnPickPhoto);

            var btnClearPhoto = CreateGhostButton("Quitar", () =>
            {
                pendingLocalPhotoSource = "";
                selectedImagePath = "";
                refreshPhotoPreview();
            });
            photoBtns.Children.Add(btnClearPhoto);
            photoDock.Children.Add(photoBtns);

            var photoText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            photoText.Children.Add(new TextBlock { Text = "Foto del Producto (Opcional)", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Theme.Text });
            photoText.Children.Add(new TextBlock { Text = "Podés agregarla ahora o después.", FontSize = 11, Foreground = Theme.Muted });
            photoDock.Children.Add(photoText);
            photoCard.Child = photoDock;
            formStack.Children.Add(photoCard);

            // Nombre y Precio (en 2 columnas para que sea súper limpio)
            var namePriceGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            namePriceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
            namePriceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            namePriceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var nameStack = new StackPanel();
            nameStack.Children.Add(new TextBlock { Text = "Nombre del Producto:", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text, Margin = new Thickness(0, 0, 0, 4) });
            var nameBox = CreateStyledTextBox();
            nameBox.Text = existing != null ? existing.Name : "";
            nameStack.Children.Add(nameBox);
            Grid.SetColumn(nameStack, 0);
            namePriceGrid.Children.Add(nameStack);

            var priceStack = new StackPanel();
            priceStack.Children.Add(new TextBlock { Text = "Precio ($):", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text, Margin = new Thickness(0, 0, 0, 4) });
            var priceBox = CreateStyledTextBox();
            priceBox.Text = existing != null ? existing.Price.ToString("0") : "1500";
            priceStack.Children.Add(priceBox);
            Grid.SetColumn(priceStack, 2);
            namePriceGrid.Children.Add(priceStack);
            formStack.Children.Add(namePriceGrid);

            // Categoría donde se muestra el producto (editable para poder crear categorías nuevas)
            var lblCategoryField = new TextBlock { Text = "Categoría a la que pertenece (podés elegir o escribir una nueva):", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text, Margin = new Thickness(0, 0, 0, 4) };
            formStack.Children.Add(lblCategoryField);

            var catCombo = new ComboBox { Padding = new Thickness(8, 6, 8, 6), FontSize = 13, IsEditable = true, Margin = new Thickness(0, 0, 0, 10) };
            foreach (var c in existingCategories) catCombo.Items.Add(c);
            catCombo.Text = existing != null ? existing.Category : "Empanadas";
            formStack.Children.Add(catCombo);

            // SECCIÓN EXCLUSIVA DE VENTA INDIVIDUAL (Stock Propio)
            var indivStockPanel = new Border
            {
                Background = Theme.SurfaceSoft,
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.BtnRadius,
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var isStack = new StackPanel();
            var chkStock = new CheckBox { Content = "Activar control de stock disponible para esta unidad", IsChecked = existing != null && existing.StockEnabled, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
            isStack.Children.Add(chkStock);
            var stockRow = new DockPanel();
            stockRow.Children.Add(new TextBlock { Text = "Unidades disponibles en stock:", FontSize = 12, Foreground = Theme.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            var stockBox = CreateStyledTextBox();
            stockBox.Width = 100;
            stockBox.Text = existing != null ? existing.StockQty.ToString() : "40";
            stockRow.Children.Add(stockBox);
            isStack.Children.Add(stockRow);
            indivStockPanel.Child = isStack;
            formStack.Children.Add(indivStockPanel);

            // SECCIÓN EXCLUSIVA DE COMBO / PACK (Enlace con Categoría + Cantidad de Unidades)
            var comboConfigPanel = new Border
            {
                Background = Theme.BrandBg,
                BorderBrush = Theme.BrandSoft,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.BtnRadius,
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var ccInner = new StackPanel();
            ccInner.Children.Add(new TextBlock
            {
                Text = "🔗 Enlace de Stock con Categoría Individual",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Brand,
                Margin = new Thickness(0, 0, 0, 4)
            });
            ccInner.Children.Add(new TextBlock
            {
                Text = "Elegí de qué categoría salen los sabores de este combo. Al vender el combo se descontará automáticamente el stock de cada sabor elegido:",
                FontSize = 11,
                Foreground = Theme.Muted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });

            ccInner.Children.Add(new TextBlock { Text = "Categoría de sabores a enlazar:", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text, Margin = new Thickness(0, 0, 0, 4) });
            var sourceCatCombo = new ComboBox { Padding = new Thickness(8, 6, 8, 6), FontSize = 13, IsEditable = true, Margin = new Thickness(0, 0, 0, 10) };
            foreach (var ic in individualCategories) sourceCatCombo.Items.Add(ic);
            sourceCatCombo.Text = existing != null && !string.IsNullOrWhiteSpace(existing.PackSourceFilter)
                ? existing.PackSourceFilter
                : (individualCategories.FirstOrDefault() ?? "Empanadas");
            ccInner.Children.Add(sourceCatCombo);

            ccInner.Children.Add(new TextBlock { Text = "Cantidad de unidades que incluye este Combo / Pack:", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text, Margin = new Thickness(0, 0, 0, 6) });
            var qtyRow = new StackPanel { Orientation = Orientation.Horizontal };
            var packQtyBox = CreateStyledTextBox();
            packQtyBox.Width = 85;
            packQtyBox.Background = Theme.Card;
            packQtyBox.Text = existing != null && existing.PackRequiredQty > 0 ? existing.PackRequiredQty.ToString() : "12";
            packQtyBox.Margin = new Thickness(0, 0, 8, 0);
            qtyRow.Children.Add(packQtyBox);

            var btnQ6 = CreateGhostButton("6 (Media Docena)", () => packQtyBox.Text = "6");
            btnQ6.Margin = new Thickness(0, 0, 6, 0);
            qtyRow.Children.Add(btnQ6);

            var btnQ12 = CreateGhostButton("12 (Docena)", () => packQtyBox.Text = "12");
            qtyRow.Children.Add(btnQ12);
            ccInner.Children.Add(qtyRow);

            comboConfigPanel.Child = ccInner;
            formStack.Children.Add(comboConfigPanel);

            // Descripción corta + Activo
            formStack.Children.Add(new TextBlock { Text = "Descripción corta (opcional):", FontSize = 12, Foreground = Theme.Muted, Margin = new Thickness(0, 0, 0, 4) });
            var descBox = CreateStyledTextBox();
            descBox.Text = existing != null ? existing.Description : "";
            descBox.Margin = new Thickness(0, 0, 0, 10);
            formStack.Children.Add(descBox);

            var chkActive = new CheckBox { Content = "Visible para la venta en Caja", IsChecked = existing == null || existing.Active, Margin = new Thickness(0, 2, 0, 12), FontSize = 12 };
            formStack.Children.Add(chkActive);

            var btnSave = CreatePrimaryButton("💾 Guardar Producto", () =>
            {
                if (string.IsNullOrEmpty(selectedMode)) return;
                if (string.IsNullOrWhiteSpace(nameBox.Text))
                {
                    MessageBox.Show("Escribí el nombre del producto.", "Falta nombre", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                decimal pr = 0; decimal.TryParse(priceBox.Text.Trim(), out pr);
                int sq = 0; int.TryParse(stockBox.Text.Trim(), out sq);
                bool isComboMode = selectedMode == "combo";
                int pQty = 0;
                if (isComboMode)
                {
                    int.TryParse(packQtyBox.Text.Trim(), out pQty);
                    if (pQty <= 0)
                    {
                        MessageBox.Show("Indicá cuántas unidades incluye el Combo / Pack (por ejemplo 6 o 12).", "Cantidad inválida", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                string pFilter = isComboMode ? (sourceCatCombo.Text ?? "").Trim() : "";
                string chosenCat = string.IsNullOrWhiteSpace(catCombo.Text) ? (isComboMode ? "Para Llevar" : "Principales") : catCombo.Text.Trim();

                // Si eligió una foto nueva desde su PC, copiarla a static/img/productos/
                string finalImageFile = selectedImagePath;
                if (!string.IsNullOrEmpty(pendingLocalPhotoSource) && File.Exists(pendingLocalPhotoSource))
                {
                    try
                    {
                        Directory.CreateDirectory(PosStore.ImagesDir);
                        string ext = Path.GetExtension(pendingLocalPhotoSource);
                        finalImageFile = string.Format("plato_{0}{1}", DateTime.Now.Ticks % 1000000, ext);
                        File.Copy(pendingLocalPhotoSource, Path.Combine(PosStore.ImagesDir, finalImageFile), true);
                    }
                    catch { }
                }

                if (existing != null)
                {
                    var target = _db.Products.FirstOrDefault(p => p.Id == existing.Id);
                    if (target != null)
                    {
                        target.Name = nameBox.Text.Trim();
                        target.Category = chosenCat;
                        target.Price = pr;
                        target.Description = descBox.Text.Trim();
                        target.StockEnabled = !isComboMode && chkStock.IsChecked == true;
                        target.StockQty = isComboMode ? 0 : sq;
                        target.IsCombo = isComboMode;
                        target.PackRequiredQty = pQty;
                        target.PackSourceFilter = pFilter;
                        target.ImagePath = finalImageFile;
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
                        StockEnabled = !isComboMode && chkStock.IsChecked == true,
                        StockQty = isComboMode ? 0 : sq,
                        IsCombo = isComboMode,
                        PackRequiredQty = pQty,
                        PackSourceFilter = pFilter,
                        Active = chkActive.IsChecked == true,
                        ImagePath = finalImageFile
                    });
                }

                PosStore.Save(_db);
                RefreshAllUI();
                RenderMenuSection();
                win.Close();
            }, false);
            formStack.Children.Add(btnSave);

            formScroll.Content = formStack;

            // Contenedor que alterna entre el cartel de "Elegí primero el tipo" y el formulario desbloqueado
            var centerGrid = new Grid();
            centerGrid.Children.Add(lockedHint);
            centerGrid.Children.Add(formScroll);
            rootDock.Children.Add(centerGrid);

            Action<string> applyMode = mode =>
            {
                selectedMode = mode;
                bool isInd = mode == "individual";
                bool isCmb = mode == "combo";

                cardIndiv.Background = isInd ? Theme.BrandBg : Theme.Bg;
                cardIndiv.BorderBrush = isInd ? Theme.Brand : Theme.BorderStrong;
                ciTitle.Foreground = isInd ? Theme.Brand : Theme.Text;

                cardCombo.Background = isCmb ? Theme.BrandBg : Theme.Bg;
                cardCombo.BorderBrush = isCmb ? Theme.Brand : Theme.BorderStrong;
                ccTitle.Foreground = isCmb ? Theme.Brand : Theme.Text;

                if (string.IsNullOrEmpty(mode))
                {
                    lockedHint.Visibility = Visibility.Visible;
                    formScroll.Visibility = Visibility.Collapsed;
                }
                else
                {
                    lockedHint.Visibility = Visibility.Collapsed;
                    formScroll.Visibility = Visibility.Visible;
                    indivStockPanel.Visibility = isInd ? Visibility.Visible : Visibility.Collapsed;
                    comboConfigPanel.Visibility = isCmb ? Visibility.Visible : Visibility.Collapsed;
                    lblCategoryField.Text = isInd
                        ? "Categoría a la que pertenece (elegí o escribí una nueva, ej: Empanadas):"
                        : "Categoría donde se muestra este Combo en el menú (ej: Para Llevar):";
                    if (existing == null && isCmb && string.Equals(catCombo.Text, "Empanadas", StringComparison.OrdinalIgnoreCase))
                    {
                        catCombo.Text = "Para Llevar";
                    }
                }
            };

            cardIndiv.MouseLeftButtonUp += (s, e) => applyMode("individual");
            cardCombo.MouseLeftButtonUp += (s, e) => applyMode("combo");

            applyMode(selectedMode);

            win.Content = rootDock;
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
            header.Child = new TextBlock { Text = "Tickets Emitidos en la Caja Actual y Estado en Cocina", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Theme.Text };
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
                _ordersListStack.Children.Add(new TextBlock { Text = "Todavía no hay ventas registradas en esta caja.", FontSize = 13, Foreground = Theme.Muted, Margin = new Thickness(10, 30, 10, 10) });
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
                bool isReadyInKitchen = !isCancelled && ord.KitchenStatus == "finalizado";
                bool isDelivered = !isCancelled && ord.KitchenStatus == "entregado";

                string readyTime = (ord.FinishedAt ?? "").Length >= 5 ? ord.FinishedAt.Substring(0, 5) : (ord.FinishedAt ?? "");
                string delivTime = (ord.DeliveredAt ?? "").Length >= 5 ? ord.DeliveredAt.Substring(0, 5) : (ord.DeliveredAt ?? "");

                SolidColorBrush badgeBg = isCancelled ? Theme.ErrBg
                    : (isDelivered ? Theme.OkBg
                    : (isReadyInKitchen ? Theme.BrandBg
                    : (ord.KitchenStatus == "preparando" ? Theme.BrandBg : Theme.WarnBg)));

                SolidColorBrush badgeFg = isCancelled ? Theme.Err
                    : (isDelivered ? Theme.Ok
                    : (isReadyInKitchen ? Theme.Brand
                    : (ord.KitchenStatus == "preparando" ? Theme.BrandDark : Theme.Warn)));

                string badgeText = isCancelled ? "ANULADO"
                    : (isDelivered ? ("✓ ENTREGADO " + delivTime).Trim()
                    : (isReadyInKitchen ? ("✓ LISTO EN COCINA " + readyTime).Trim()
                    : (ord.KitchenStatus == "preparando" ? "👨‍🍳 PREPARANDO" : "🔔 EN COCINA")));

                var statusBadge = new Border { Background = badgeBg, CornerRadius = Theme.BtnRadius, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                statusBadge.Child = new TextBlock { Text = badgeText, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = badgeFg };
                actions.Children.Add(statusBadge);

                var ordCopy = ord;
                if (isReadyInKitchen)
                {
                    var btnMarkDelivered = CreatePrimaryButton("📦 Marcar Entregado", () =>
                    {
                        _db = PosStore.Load();
                        var target = _db.Orders.FirstOrDefault(x => x.Id == ordCopy.Id);
                        if (target != null)
                        {
                            target.KitchenStatus = "entregado";
                            target.DeliveredAt = DateTime.Now.ToString("HH:mm");
                            PosStore.Save(_db);
                            UpdateKitchenCounterBadge();
                            RenderOrdersHistoryList();
                            ShowToast(string.Format("📦 Pedido #{0:000} marcado como ENTREGADO al cliente.", target.TicketNumber));
                        }
                    }, true);
                    btnMarkDelivered.Margin = new Thickness(0, 0, 6, 0);
                    actions.Children.Add(btnMarkDelivered);
                }

                var btnReprint = CreateGhostButton("🖨️ Reimprimir", () =>
                {
                    try { EscPosPrinter.PrintOrder(ordCopy, _db.Settings, false); ShowToast(string.Format("🖨️ Ticket #{0:000} reimpreso.", ordCopy.TicketNumber)); }
                    catch (Exception ex) { MessageBox.Show(ex.Message, "Error al imprimir", MessageBoxButton.OK, MessageBoxImage.Error); }
                });
                btnReprint.Margin = new Thickness(0, 0, 6, 0);
                actions.Children.Add(btnReprint);

                if (!isCancelled)
                {
                    var btnCancel = CreateGhostButton("Anular", () =>
                    {
                        if (MessageBox.Show("¿Anular el Ticket #" + ordCopy.TicketNumber.ToString("000") + " y devolver el stock?", "Anular Pedido", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                        {
                            var target = _db.Orders.FirstOrDefault(x => x.Id == ordCopy.Id);
                            if (target != null)
                            {
                                target.Status = "anulado";
                                foreach (var it in target.Items)
                                {
                                    if (it.PackChoices != null && it.PackChoices.Count > 0)
                                    {
                                        foreach (var pc in it.PackChoices)
                                        {
                                            var fp = _db.Products.FirstOrDefault(x => x.Id == pc.ProductId);
                                            if (fp != null && fp.StockEnabled) fp.StockQty += (pc.Quantity * it.Quantity);
                                        }
                                    }
                                    else
                                    {
                                        var p = _db.Products.FirstOrDefault(x => x.Id == it.ProductId);
                                        if (p != null && p.StockEnabled) p.StockQty += it.Quantity;
                                    }
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
                string createdShort = (ord.CreatedAt ?? "").Length >= 16 ? ord.CreatedAt.Substring(0, 16) : (ord.CreatedAt ?? "");
                info.Children.Add(new TextBlock
                {
                    Text = string.Format("TICKET #{0:000}   ·   {1}   ·   {2}   ·   {3}", ord.TicketNumber, createdShort, ord.PaymentMethod, PosStore.FormatMoney(ord.Total)),
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = isCancelled ? Theme.Faint : Theme.Text
                });

                string itemsSummary = string.Join(", ", ord.Items.Select(i => i.Quantity + "x " + i.ProductName + (string.IsNullOrWhiteSpace(i.Note) ? "" : " (" + i.Note + ")")));
                if (!string.IsNullOrWhiteSpace(ord.Notes)) itemsSummary += "   |   Nota: " + ord.Notes;
                info.Children.Add(new TextBlock { Text = itemsSummary, FontSize = 12, Foreground = Theme.Muted, Margin = new Thickness(0, 2, 0, 0) });

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
            stack.Children.Add(new TextBlock { Text = "🔓 Iniciar Nueva Caja / Evento", FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Theme.Text, Margin = new Thickness(0, 0, 0, 6) });
            stack.Children.Add(new TextBlock { Text = "Elegí el nombre de la jornada y qué Grupo de Menú se va a vender en esta caja.", FontSize = 12, Foreground = Theme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });

            stack.Children.Add(new TextBlock { Text = "Nombre del Evento o Jornada:", FontSize = 12, Foreground = Theme.Muted, Margin = new Thickness(0, 0, 0, 4) });
            var evBox = CreateStyledTextBox();
            evBox.Text = _db.Settings.EventName ?? "Asociación Juvenil Araucana";
            stack.Children.Add(evBox);

            stack.Children.Add(new TextBlock { Text = "Tipo de Evento / Menú a utilizar:", FontSize = 12, Foreground = Theme.Muted, Margin = new Thickness(0, 12, 0, 4) });
            var groupCombo = new ComboBox { Padding = new Thickness(8, 6, 8, 6), FontSize = 13 };
            foreach (var g in _db.MenuGroups) groupCombo.Items.Add(g.Name);
            groupCombo.SelectedItem = GetCurrentActiveGroup().Name;
            stack.Children.Add(groupCombo);

            stack.Children.Add(new TextBlock { Text = "Fondo Inicial de Cambio en Efectivo ($):", FontSize = 12, Foreground = Theme.Muted, Margin = new Thickness(0, 12, 0, 4) });
            var cashBox = CreateStyledTextBox();
            cashBox.Text = "0";
            stack.Children.Add(cashBox);

            var chkResetSeq = new CheckBox { Content = "Reiniciar numeración de tickets a #001", IsChecked = true, Margin = new Thickness(0, 14, 0, 18), FontSize = 12 };
            stack.Children.Add(chkResetSeq);

            var btnStart = CreatePrimaryButton("✓ ABRIR NUEVA CAJA AHORA", () =>
            {
                string evName = string.IsNullOrWhiteSpace(evBox.Text) ? "Asociación Juvenil Araucana" : evBox.Text.Trim();
                decimal openCash = 0; decimal.TryParse(cashBox.Text.Trim(), out openCash);
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
                if (chkResetSeq.IsChecked == true) _db.Settings.NextTicketSequence = 1;

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
        // VISTA 5: AJUSTES (TICKETERA, MÉTODOS DE PAGO Y LOGO DEL TICKET)
        // =====================================================================
        private UIElement BuildSettingsView()
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var mainStack = new StackPanel();

            var twoCols = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            twoCols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            twoCols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            twoCols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // TARJETA IZQUIERDA: Ticketera y Métodos de Pago
            var leftCard = new Border
            {
                Background = Theme.Card,
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.CardRadius,
                Padding = new Thickness(22),
                Effect = Theme.CardShadow()
            };
            var lStack = new StackPanel();
            lStack.Children.Add(new TextBlock { Text = "🖨️ Ticketera y Métodos de Pago", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Theme.Text, Margin = new Thickness(0, 0, 0, 14) });

            lStack.Children.Add(new TextBlock { Text = "Ticketera instalada en Windows:", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Theme.Muted, Margin = new Thickness(0, 0, 0, 4) });
            _printerCombo = new ComboBox { Padding = new Thickness(10, 7, 10, 7), FontSize = 13, Margin = new Thickness(0, 0, 0, 12) };
            lStack.Children.Add(_printerCombo);

            lStack.Children.Add(new TextBlock { Text = "Ancho de rollo de papel térmico:", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Theme.Muted, Margin = new Thickness(0, 0, 0, 4) });
            _paperWidthCombo = new ComboBox { Padding = new Thickness(10, 7, 10, 7), FontSize = 13, Margin = new Thickness(0, 0, 0, 12) };
            _paperWidthCombo.Items.Add("80 mm (Ancho completo 48 columnas - Xprinter XP-80)");
            _paperWidthCombo.Items.Add("58 mm (Rollo angosto 32 columnas)");
            lStack.Children.Add(_paperWidthCombo);

            lStack.Children.Add(new TextBlock { Text = "Cantidad de tickets iguales a imprimir en Caja (máximo 4):", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Theme.Muted, Margin = new Thickness(0, 0, 0, 4) });
            _copiesCombo = new ComboBox { Padding = new Thickness(10, 7, 10, 7), FontSize = 13, Margin = new Thickness(0, 0, 0, 12) };
            _copiesCombo.Items.Add("1 ticket (Solo Cliente)");
            _copiesCombo.Items.Add("2 tickets (Cliente + Control de Caja)");
            _copiesCombo.Items.Add("3 tickets iguales");
            _copiesCombo.Items.Add("4 tickets iguales (Máximo)");
            lStack.Children.Add(_copiesCombo);

            _doubleTicketCheck = new CheckBox
            {
                Content = "Imprimir doble talón con corte automático (Cliente + Comanda Cocina)",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 4, 0, 6)
            };
            lStack.Children.Add(_doubleTicketCheck);

            _kitchenPrinterPanel = new StackPanel
            {
                Margin = new Thickness(20, 4, 0, 12),
                Visibility = Visibility.Collapsed
            };
            _kitchenPrinterPanel.Children.Add(new TextBlock
            {
                Text = "Ticketera para la Comanda de Cocina:",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Brand,
                Margin = new Thickness(0, 0, 0, 4)
            });
            _kitchenPrinterCombo = new ComboBox { Padding = new Thickness(8, 6, 8, 6), FontSize = 12 };
            _kitchenPrinterPanel.Children.Add(_kitchenPrinterCombo);
            lStack.Children.Add(_kitchenPrinterPanel);

            _doubleTicketCheck.Checked += (s, e) => _kitchenPrinterPanel.Visibility = Visibility.Visible;
            _doubleTicketCheck.Unchecked += (s, e) => _kitchenPrinterPanel.Visibility = Visibility.Collapsed;

            // Métodos de Pago Habilitados
            var payMethodsBox = new Border
            {
                Background = Theme.SurfaceSoft,
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.BtnRadius,
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var pmStack = new StackPanel();
            pmStack.Children.Add(new TextBlock { Text = "💳 Métodos de Pago Habilitados en Caja", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Theme.Text, Margin = new Thickness(0, 0, 0, 8) });

            _chkEnableCash = new CheckBox { Content = "💵 Efectivo (con calculadora de vuelto)", FontSize = 13, Margin = new Thickness(0, 2, 0, 6) };
            _chkEnableTransfer = new CheckBox { Content = "📱 Transferencia (muestra Alias en pantalla y ticket)", FontSize = 13, Margin = new Thickness(0, 2, 0, 6) };
            _chkEnableCard = new CheckBox { Content = "💳 Tarjeta (Débito / Crédito por Posnet)", FontSize = 13, Margin = new Thickness(0, 2, 0, 4) };
            pmStack.Children.Add(_chkEnableCash);
            pmStack.Children.Add(_chkEnableTransfer);
            pmStack.Children.Add(_chkEnableCard);
            payMethodsBox.Child = pmStack;
            lStack.Children.Add(payMethodsBox);

            lStack.Children.Add(new TextBlock { Text = "Alias para Transferencia:", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Theme.Muted, Margin = new Thickness(0, 2, 0, 4) });
            _mpAliasSettingBox = CreateStyledTextBox();
            lStack.Children.Add(_mpAliasSettingBox);

            leftCard.Child = lStack;
            Grid.SetColumn(leftCard, 0);
            twoCols.Children.Add(leftCard);

            // TARJETA DERECHA: Personalización del Ticket y Logo
            var rightCard = new Border
            {
                Background = Theme.Card,
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.CardRadius,
                Padding = new Thickness(22),
                Effect = Theme.CardShadow()
            };
            var rStack = new StackPanel();
            rStack.Children.Add(new TextBlock { Text = "🎨 Personalización del Ticket y Logo", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Theme.Text, Margin = new Thickness(0, 0, 0, 14) });

            var logoPanel = new Border
            {
                Background = Theme.SurfaceSoft,
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.BtnRadius,
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 14)
            };
            var logoStack = new StackPanel();
            logoStack.Children.Add(new TextBlock { Text = "Logo en el encabezado del Ticket (PNG / JPG)", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Theme.Text, Margin = new Thickness(0, 0, 0, 8) });

            _logoPreviewBox = new Border
            {
                Height = 110,
                Background = Theme.Card,
                BorderBrush = Theme.BorderStrong,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.BtnRadius,
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(8)
            };
            logoStack.Children.Add(_logoPreviewBox);

            var logoBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            var btnUploadLogo = CreatePrimaryButton("📷 Subir Logo del Ticket", UploadTicketLogo, true);
            btnUploadLogo.Margin = new Thickness(0, 0, 8, 0);
            logoBtns.Children.Add(btnUploadLogo);

            var btnRemoveLogo = CreateGhostButton("Quitar Logo", () =>
            {
                _db.Settings.TicketLogoPath = "";
                PosStore.Save(_db);
                RefreshLogoPreview();
                ShowToast("Logo quitado del ticket.");
            });
            logoBtns.Children.Add(btnRemoveLogo);
            logoStack.Children.Add(logoBtns);

            _chkPrintLogo = new CheckBox { Content = "Imprimir este logo centrado al inicio de cada ticket", FontSize = 12, Margin = new Thickness(0, 2, 0, 10) };
            logoStack.Children.Add(_chkPrintLogo);

            logoStack.Children.Add(new TextBlock { Text = "Tamaño del logo en el papel térmico:", FontSize = 12, Foreground = Theme.Muted, Margin = new Thickness(0, 0, 0, 4) });
            _logoSizeCombo = new ComboBox { Padding = new Thickness(8, 6, 8, 6), FontSize = 12 };
            _logoSizeCombo.Items.Add("Pequeño (200 px)");
            _logoSizeCombo.Items.Add("Mediano (280 px - Recomendado)");
            _logoSizeCombo.Items.Add("Grande (360 px)");
            logoStack.Children.Add(_logoSizeCombo);

            logoPanel.Child = logoStack;
            rStack.Children.Add(logoPanel);

            rStack.Children.Add(new TextBlock { Text = "Mensaje al pie del ticket:", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Theme.Muted, Margin = new Thickness(0, 0, 0, 4) });
            _footerSettingBox = CreateStyledTextBox();
            rStack.Children.Add(_footerSettingBox);

            rightCard.Child = rStack;
            Grid.SetColumn(rightCard, 2);
            twoCols.Children.Add(rightCard);

            mainStack.Children.Add(twoCols);

            // Barra inferior de acciones de Configuración
            var footerCard = new Border
            {
                Background = Theme.Card,
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = Theme.CardRadius,
                Padding = new Thickness(18, 14, 18, 14)
            };
            var btnsRow = new StackPanel { Orientation = Orientation.Horizontal };
            var btnSave = CreatePrimaryButton("💾 Guardar Configuración", SaveSettingsFromForm, false);
            btnSave.Margin = new Thickness(0, 0, 10, 0);
            btnsRow.Children.Add(btnSave);

            var btnTest = CreateGhostButton("🖨️ Imprimir Ticket de Prueba (con Logo)", () =>
            {
                SaveSettingsFromForm();
                try { EscPosPrinter.PrintTestTicket(_db.Settings); ShowToast("🖨️ Ticket de prueba (con logo) enviado a " + _db.Settings.PrinterName); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "Error en Ticketera", MessageBoxButton.OK, MessageBoxImage.Error); }
            });
            btnTest.Margin = new Thickness(0, 0, 10, 0);
            btnsRow.Children.Add(btnTest);

            var btnUpdates = CreateGhostButton("🔄 Buscar Actualizaciones (v" + GitHubUpdater.AppVersion + ")", () =>
            {
                GitHubUpdater.CheckForUpdatesAsync(this, "Caja_POS.exe", true);
            });
            btnsRow.Children.Add(btnUpdates);

            footerCard.Child = btnsRow;
            mainStack.Children.Add(footerCard);

            scroll.Content = mainStack;
            return scroll;
        }

        private void UploadTicketLogo()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Seleccionar imagen de Logo para el Ticket",
                Filter = "Imágenes (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp"
            };
            if (dlg.ShowDialog(this) == true)
            {
                try
                {
                    Directory.CreateDirectory(PosStore.ImagesDir);
                    string ext = Path.GetExtension(dlg.FileName);
                    string destName = "ticket_logo_" + (DateTime.Now.Ticks % 100000) + ext;
                    File.Copy(dlg.FileName, Path.Combine(PosStore.ImagesDir, destName), true);
                    _db.Settings.TicketLogoPath = destName;
                    _db.Settings.PrintTicketLogo = true;
                    PosStore.Save(_db);
                    _chkPrintLogo.IsChecked = true;
                    RefreshLogoPreview();
                    ShowToast("📷 Logo cargado correctamente. Probá tocar 'Imprimir Ticket de Prueba' para verlo en papel.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error al cargar logo", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void RefreshLogoPreview()
        {
            if (_logoPreviewBox == null) return;
            var bmp = Theme.LoadImageCached(_db.Settings.TicketLogoPath);
            if (bmp != null)
            {
                _logoPreviewBox.Child = new Image
                {
                    Source = bmp,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            else
            {
                _logoPreviewBox.Child = new TextBlock
                {
                    Text = "Sin logo cargado\n(Tocá 'Subir Logo del Ticket' para elegir una imagen)",
                    FontSize = 12,
                    Foreground = Theme.Faint,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
        }

        private void LoadSettingsForm()
        {
            var installed = EscPosPrinter.ListInstalledPrinters();
            _printerCombo.Items.Clear();
            _kitchenPrinterCombo.Items.Clear();
            _kitchenPrinterCombo.Items.Add("(Misma ticketera seleccionada en Caja)");
            foreach (var p in installed)
            {
                _printerCombo.Items.Add(p);
                _kitchenPrinterCombo.Items.Add(p);
            }

            string currentPrinter = _db.Settings.PrinterName ?? EscPosPrinter.DetectPreferredPrinter();
            _printerCombo.SelectedItem = currentPrinter;
            if (_printerCombo.SelectedItem == null && _printerCombo.Items.Count > 0) _printerCombo.SelectedIndex = 0;

            if (string.IsNullOrWhiteSpace(_db.Settings.KitchenPrinterName))
                _kitchenPrinterCombo.SelectedIndex = 0;
            else
            {
                _kitchenPrinterCombo.SelectedItem = _db.Settings.KitchenPrinterName;
                if (_kitchenPrinterCombo.SelectedItem == null) _kitchenPrinterCombo.SelectedIndex = 0;
            }

            _paperWidthCombo.SelectedIndex = _db.Settings.PaperWidthMm == 58 ? 1 : 0;
            int copiesIdx = Math.Max(1, Math.Min(4, _db.Settings.ClientTicketCopies)) - 1;
            _copiesCombo.SelectedIndex = copiesIdx;

            _doubleTicketCheck.IsChecked = _db.Settings.PrintDoubleTicket;
            _kitchenPrinterPanel.Visibility = _db.Settings.PrintDoubleTicket ? Visibility.Visible : Visibility.Collapsed;
            _chkEnableCash.IsChecked = _db.Settings.EnableCash;
            _chkEnableTransfer.IsChecked = _db.Settings.EnableTransfer;
            _chkEnableCard.IsChecked = _db.Settings.EnableCard;
            _chkPrintLogo.IsChecked = _db.Settings.PrintTicketLogo;

            int lw = _db.Settings.TicketLogoWidthPx;
            _logoSizeCombo.SelectedIndex = lw <= 220 ? 0 : (lw >= 330 ? 2 : 1);

            _mpAliasSettingBox.Text = _db.Settings.MpAlias;
            _footerSettingBox.Text = _db.Settings.TicketFooter;
            RefreshLogoPreview();
        }

        private void SaveSettingsFromForm()
        {
            if (_chkEnableCash.IsChecked != true && _chkEnableTransfer.IsChecked != true && _chkEnableCard.IsChecked != true)
            {
                MessageBox.Show("Debe quedar al menos un método de pago habilitado.", "Métodos de Pago", MessageBoxButton.OK, MessageBoxImage.Warning);
                _chkEnableCash.IsChecked = true;
            }

            if (_printerCombo.SelectedItem != null) _db.Settings.PrinterName = _printerCombo.SelectedItem.ToString();
            _db.Settings.PaperWidthMm = _paperWidthCombo.SelectedIndex == 1 ? 58 : 80;
            _db.Settings.ClientTicketCopies = Math.Max(1, Math.Min(4, _copiesCombo.SelectedIndex + 1));
            _db.Settings.PrintDoubleTicket = _doubleTicketCheck.IsChecked == true;
            _db.Settings.KitchenPrinterName = _kitchenPrinterCombo.SelectedIndex <= 0 ? "" : _kitchenPrinterCombo.SelectedItem.ToString();
            _db.Settings.EnableCash = _chkEnableCash.IsChecked == true;
            _db.Settings.EnableTransfer = _chkEnableTransfer.IsChecked == true;
            _db.Settings.EnableCard = _chkEnableCard.IsChecked == true;
            _db.Settings.PrintTicketLogo = _chkPrintLogo.IsChecked == true;
            _db.Settings.TicketLogoWidthPx = _logoSizeCombo.SelectedIndex == 0 ? 200 : (_logoSizeCombo.SelectedIndex == 2 ? 360 : 280);
            _db.Settings.MpAlias = string.IsNullOrWhiteSpace(_mpAliasSettingBox.Text) ? "araucana24" : _mpAliasSettingBox.Text.Trim();
            _db.Settings.TicketFooter = _footerSettingBox.Text.Trim();
            PosStore.Save(_db);
            RenderPaymentMethodButtons();
            RefreshAllUI();
            ShowToast("⚙️ Configuración guardada correctamente.");
        }

        // =====================================================================
        // SINCRONIZACIÓN Y BOTONES UNIFORMES
        // =====================================================================
        private void RefreshAllUI()
        {
            var activeSess = PosStore.GetActiveSession(_db);
            var activeGrp = GetCurrentActiveGroup();

            if (activeSess != null)
            {
                PosStore.RecalculateSessionMetrics(_db, activeSess);
                _headerEventLabel.Text = activeSess.EventName;
                _headerSubLabel.Text = string.Format("Caja #{0:000} · Menú: {1}", activeSess.Id, activeGrp.Name);
                _sbSessionStatus.Text = string.Format("● CAJA #{0:000} ABIERTA", activeSess.Id);
                _sbSessionStatus.Foreground = Theme.Ok;
                _sbSessionTotal.Text = "Recaudado: " + PosStore.FormatMoney(activeSess.TotalSales);
            }
            else
            {
                _headerEventLabel.Text = "CAJA CERRADA";
                _headerSubLabel.Text = "Iniciá una Nueva Caja para vender";
                _sbSessionStatus.Text = "● CAJA CERRADA";
                _sbSessionStatus.Foreground = Theme.Err;
                _sbSessionTotal.Text = "Sin turno activo";
            }

            _nextTicketText.Text = string.Format("PRÓXIMO: #{0:000}", _db.Settings.NextTicketSequence);
            UpdateKitchenCounterBadge();
            RenderCategoryCards();
            RenderProductCards();
            RenderCart();
        }

        private void UpdateKitchenCounterBadge()
        {
            int activeInKitchen = _db.Orders.Count(o => o.Status == "completado" && (o.KitchenStatus == "pendiente" || o.KitchenStatus == "preparando"));
            int readyToDeliver = _db.Orders.Count(o => o.Status == "completado" && o.KitchenStatus == "finalizado");
            _kitchenBadgeText.Text = string.Format("👨‍🍳 Cocina: {0} en curso · {1} listos para entregar", activeInKitchen, readyToDeliver);
        }

        private void BackgroundSync()
        {
            try
            {
                var diskDb = PosStore.Load();
                _db.Orders = diskDb.Orders;
                UpdateKitchenCounterBadge();
                if (_activeSection == "orders") RenderOrdersHistoryList();
            }
            catch { }
        }

        private Border CreatePrimaryButton(string text, Action onClick, bool compact)
        {
            var b = new Border
            {
                Background = Theme.Brand,
                CornerRadius = Theme.BtnRadius,
                Padding = compact ? new Thickness(10, 7, 10, 7) : new Thickness(16, 10, 16, 10),
                Cursor = Cursors.Hand
            };
            b.Child = new TextBlock
            {
                Text = text,
                FontSize = compact ? 12 : 13,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
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
            b.Child = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            b.MouseEnter += (s, e) => b.Background = Theme.Hover;
            b.MouseLeave += (s, e) => b.Background = Theme.Card;
            b.MouseLeftButtonUp += (s, e) => onClick();
            return b;
        }

        private Border CreateIconButton(string icon, string tooltip, Action onClick)
        {
            var b = new Border
            {
                Width = 34,
                Height = 34,
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
                FontSize = 13,
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

        private TextBox CreateStyledTextBox()
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
