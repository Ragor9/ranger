// Ranger: mostra a bateria dos dispositivos sem fio em três modos de exibição:
//   float   – traço fino na borda da tela que abre um painel ao passar o mouse (arrastável na vertical);
//   taskbar – ícones com anel de bateria fixos sobre a barra de tarefas (lado esquerdo ou direito);
//   notch   – barrinha preta no topo da tela, estilo iPhone, que se abre ao passar o mouse.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Path = System.Windows.Shapes.Path;

namespace Ranger
{
    public class Settings
    {
        public string Mode = "float";      // float | taskbar | notch
        public string Side = "right";      // borda da tela (float) ou lado da barra de tarefas (taskbar)
        public double CenterY = 0.5;       // altura do modo flutuante (0–1)
        public double TaskbarOffset = 0;   // deslocamento (px) a partir do lado escolhido na barra
        public bool ShowPercent = true;    // porcentagem ao lado dos anéis (taskbar / notch)

        static string AppData { get { return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); } }
        static string Dir { get { return System.IO.Path.Combine(AppData, "Ranger"); } }
        static string FilePath { get { return System.IO.Path.Combine(Dir, "settings.ini"); } }
        // Nome antigo do programa: as configurações de lá são aproveitadas na primeira execução.
        static string LegacyPath { get { return System.IO.Path.Combine(AppData, "BatteryWidget", "settings.ini"); } }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                string path = File.Exists(FilePath) ? FilePath : File.Exists(LegacyPath) ? LegacyPath : null;
                if (path == null) return s;
                foreach (var line in File.ReadAllLines(path))
                {
                    int eq = line.IndexOf('='); if (eq < 0) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                    double d;
                    if (k == "mode" && (v == "float" || v == "taskbar" || v == "notch")) s.Mode = v;
                    if (k == "side" && (v == "left" || v == "right")) s.Side = v;
                    if (k == "centerY" && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) s.CenterY = Math.Max(0, Math.Min(1, d));
                    if (k == "taskbarOffset" && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) s.TaskbarOffset = Math.Max(0, Math.Min(4000, d));
                    if (k == "showPercent") s.ShowPercent = v != "0" && v != "false";
                }
            }
            catch (Exception) { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var sb = new StringBuilder();
                sb.Append("mode=").Append(Mode).Append("\r\n");
                sb.Append("side=").Append(Side).Append("\r\n");
                sb.Append("centerY=").Append(CenterY.ToString("0.####", CultureInfo.InvariantCulture)).Append("\r\n");
                sb.Append("taskbarOffset=").Append(TaskbarOffset.ToString("0.#", CultureInfo.InvariantCulture)).Append("\r\n");
                sb.Append("showPercent=").Append(ShowPercent ? "1" : "0").Append("\r\n");
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch (Exception) { }
        }
    }

    public class Theme
    {
        public Color Panel, Border, Text, Muted, Track, Fill, Pill, Green, Hover;
        public bool IsLight;
        public static readonly Color Amber = Color.FromRgb(0xE3, 0xA3, 0x3B);
        public static readonly Color Red = Color.FromRgb(0xE5, 0x53, 0x4B);

        static Color C(uint argb) { return Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb); }

        static bool Reg(string name)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue(name);
                    return v is int && (int)v == 1;
                }
            }
            catch (Exception) { return false; }
        }

        // system = true usa o tema da barra de tarefas (SystemUsesLightTheme); senão, o tema dos apps.
        public static Theme Get(string forced, bool system)
        {
            bool light;
            if (forced == "light") light = true;
            else if (forced == "dark") light = false;
            else light = Reg(system ? "SystemUsesLightTheme" : "AppsUseLightTheme");
            return light ? Light() : Dark();
        }

        public static Theme Light()
        {
            return new Theme {
                IsLight = true,
                Panel = C(0xF5FAFAFB), Border = C(0x1C000000), Text = C(0xFF1B1C1E), Muted = C(0xFF6B6F76),
                Track = C(0x16000000), Fill = C(0xFF3A3D42), Pill = C(0xFF7D8189), Green = C(0xFF1F9D5C), Hover = C(0x12000000) };
        }

        public static Theme Dark()
        {
            return new Theme {
                Panel = C(0xF01E1F22), Border = C(0x26FFFFFF), Text = C(0xFFF2F3F5), Muted = C(0xFF9DA1A8),
                Track = C(0x1FFFFFFF), Fill = C(0xFFDDE0E4), Pill = C(0xFFA9ADB4), Green = C(0xFF46C285), Hover = C(0x18FFFFFF) };
        }

        // O notch é sempre preto, como no iPhone.
        public static Theme Notch()
        {
            var t = Dark();
            t.Panel = Colors.Black; t.Track = C(0x2EFFFFFF); t.Hover = C(0x24FFFFFF);
            return t;
        }
    }

    public class Widget : Window
    {
        const double PanelW = 236, WinH = 440, EdgeGap = 6, ShadowRoom = 24;
        const double NotchWinW = 760, NotchWinH = 140, NotchW = 126, NotchH = 9, NotchRadius = 16;
        const int LowAlert = 15;
        public static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        static readonly Brush Hittable = Frozen(Color.FromArgb(1, 255, 255, 255));

        // Glifos (Segoe Fluent Icons / MDL2)
        const string GMouse = "", GKeyboard = "", GHeadset = "", GGamepad = "",
                     GOther = "", GBolt = "", GMore = "";

        readonly Settings cfg = Settings.Load();
        readonly X11Provider x11 = new X11Provider();
        readonly string snapshotBase, dumpPath, forcedTheme;
        Theme th, tbTh;
        readonly Theme nth = Theme.Notch();

        List<DeviceState> bt = new List<DeviceState>();
        DateTime btAt = DateTime.MinValue;
        bool btBusy;

        // comuns
        Grid root; Border panel; StackPanel rows;
        // flutuante
        Border hit, pill; TranslateTransform slide;
        // barra de tarefas
        Border strip; StackPanel chips; Popup popup; TranslateTransform popSlide; bool tbTop; Rect tbRect;
        // notch
        Border notch, notchDot; Canvas notchCanvas; StackPanel notchContent, notchChips;

        bool expanded, dragArmed, dragging; Point dragStart; UIElement dragSrc;
        readonly DispatcherTimer tick, collapseTimer, expandTimer, peekTimer, topTimer;
        string lastSig = null;
        List<DeviceState> current = new List<DeviceState>();
        readonly HashSet<string> alerted = new HashSet<string>();
        int ticks;
        SettingsWindow settingsWin;

        static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        public Widget(string snapshotBase, string dumpPath, string forcedTheme)
        {
            this.snapshotBase = snapshotBase; this.dumpPath = dumpPath; this.forcedTheme = forcedTheme;
            Title = "Ranger";
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize; ShowActivated = false;
            UseLayoutRounding = true;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            th = Theme.Get(forcedTheme, false);
            tbTh = Theme.Get(forcedTheme, true);

            tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            tick.Tick += delegate {
                Refresh(false);
                UpdateFullscreenHide();
                if (++ticks % 3 == 0) KeepOnTop();
            };
            expandTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
            expandTimer.Tick += delegate { expandTimer.Stop(); Expand(); };
            collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            collapseTimer.Tick += delegate {
                collapseTimer.Stop();
                if (PointerInside() || dragging || MenuOpen()) return;
                Collapse();
            };
            peekTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            peekTimer.Tick += delegate { peekTimer.Stop(); if (!PointerInside()) Collapse(); };
            // Na barra de tarefas o Windows traz a barra pra frente quando você clica nela;
            // reafirmamos o "sempre no topo" com frequência e acompanhamos mudanças de tamanho da bandeja.
            topTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            topTimer.Tick += delegate { if (!dragging) { Position(); KeepOnTop(); } };

            BuildUi();
            Position();

            SourceInitialized += delegate {
                var h = new WindowInteropHelper(this).Handle;
                SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x80 | 0x08000000); // TOOLWINDOW | NOACTIVATE
                Position();
            };
            Loaded += delegate {
                x11.Start();
                if (snapshotBase != null || dumpPath != null) { RunCapture(); return; }
                Refresh(true);
                tick.Start();
                if (IsTaskbar) topTimer.Start();
            };
            SizeChanged += delegate { if (IsTaskbar) Position(); };
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            SystemEvents.UserPreferenceChanged += OnPrefsChanged;
            Closed += delegate {
                SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
                SystemEvents.UserPreferenceChanged -= OnPrefsChanged;
                if (popup != null) popup.IsOpen = false;
                if (settingsWin != null) settingsWin.Close();
            };
        }

        void OnDisplayChanged(object s, EventArgs e) { Dispatcher.BeginInvoke(new Action(Position)); }
        void OnPrefsChanged(object s, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color)
                Dispatcher.BeginInvoke(new Action(delegate { th = Theme.Get(forcedTheme, false); tbTh = Theme.Get(forcedTheme, true); Rebuild(); }));
        }

        bool Right { get { return cfg.Side != "left"; } }
        bool IsTaskbar { get { return cfg.Mode == "taskbar"; } }
        bool IsNotch { get { return cfg.Mode == "notch"; } }
        bool IsFloat { get { return !IsTaskbar && !IsNotch; } }
        bool Capturing { get { return snapshotBase != null || dumpPath != null; } }

        bool PointerInside()
        {
            if (panel != null && panel.IsMouseOver) return true;
            if (IsTaskbar && strip != null && strip.IsMouseOver) return true;
            if (IsNotch && notch != null && notch.IsMouseOver) return true;
            return false;
        }

        bool MenuOpen()
        {
            foreach (var el in new FrameworkElement[] { panel, strip, notch })
                if (el != null && el.ContextMenu != null && el.ContextMenu.IsOpen) return true;
            return false;
        }

        // ------------------------------------------------------------ layout
        void BuildUi()
        {
            if (popup != null) { popup.IsOpen = false; popup.Child = null; popup = null; }
            strip = null; chips = null; notch = null; notchChips = null; notchContent = null; notchCanvas = null; notchDot = null;
            hit = null; pill = null; slide = null; popSlide = null;

            root = new Grid();
            panel = BuildPanel();
            if (IsTaskbar) BuildTaskbar();
            else if (IsNotch) BuildNotch();
            else BuildFloat();
            Content = root;
        }

        Border BuildPanel()
        {
            rows = new StackPanel();
            var stack = new StackPanel();
            stack.Children.Add(PanelHeader());
            stack.Children.Add(rows);
            var p = new Border {
                Width = PanelW, CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 7, 14, 12),
                Background = B(th.Panel), BorderBrush = B(th.Border), BorderThickness = new Thickness(1),
                Child = stack,
                Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Direction = 270, Opacity = 0.32, Color = Colors.Black }
            };
            p.MouseEnter += delegate { collapseTimer.Stop(); peekTimer.Stop(); };
            p.MouseLeave += delegate { if (expanded) collapseTimer.Start(); };
            p.ContextMenu = new ContextMenu();
            p.ContextMenuOpening += delegate { FillMenu(p.ContextMenu); };
            p.ContextMenu.Closed += delegate { if (!PointerInside()) collapseTimer.Start(); };
            return p;
        }

        UIElement PanelHeader()
        {
            var g = new Grid { Margin = new Thickness(0, 0, -7, 5) };
            g.Children.Add(new TextBlock {
                Text = "Baterias", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = B(th.Muted),
                VerticalAlignment = VerticalAlignment.Center
            });
            var dots = DotsButton(th.Muted, th.Hover);
            dots.HorizontalAlignment = HorizontalAlignment.Right;
            g.Children.Add(dots);
            return g;
        }

        // Botão "⋯" que abre as configurações.
        Border DotsButton(Color fg, Color hover)
        {
            var b = new Border {
                Width = 28, Height = 24, CornerRadius = new CornerRadius(6), Background = Hittable, Cursor = Cursors.Hand,
                ToolTip = "Configurações",
                Child = new TextBlock {
                    Text = GMore, FontFamily = Icons, FontSize = 14, Foreground = B(fg),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            b.MouseEnter += delegate { b.Background = B(hover); };
            b.MouseLeave += delegate { b.Background = Hittable; };
            b.MouseLeftButtonDown += (s, e) => { e.Handled = true; };   // não inicia o arraste
            b.MouseLeftButtonUp += (s, e) => { e.Handled = true; OpenSettings(); };
            return b;
        }

        // ---------- modo flutuante
        void BuildFloat()
        {
            SizeToContent = SizeToContent.Manual;
            Width = PanelW + EdgeGap + ShadowRoom; Height = WinH;
            var edge = Right ? HorizontalAlignment.Right : HorizontalAlignment.Left;

            pill = new Border {
                Width = 4, Height = 34, CornerRadius = new CornerRadius(2), HorizontalAlignment = edge,
                VerticalAlignment = VerticalAlignment.Center, Margin = Right ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0),
                Background = B(th.Pill), Opacity = 0.5
            };
            hit = new Border {
                Width = 12, Height = 64, HorizontalAlignment = edge, VerticalAlignment = VerticalAlignment.Center,
                Background = Hittable, Child = pill
            };
            hit.MouseEnter += delegate { if (!expanded) expandTimer.Start(); };
            hit.MouseLeave += delegate { expandTimer.Stop(); };

            panel.HorizontalAlignment = edge; panel.VerticalAlignment = VerticalAlignment.Center;
            panel.Margin = Right ? new Thickness(0, 0, EdgeGap, 0) : new Thickness(EdgeGap, 0, 0, 0);
            panel.Visibility = Visibility.Hidden; panel.Opacity = 0;
            slide = new TranslateTransform(Hidden(), 0);
            panel.RenderTransform = slide;
            panel.MouseLeftButtonDown += OnDown;
            panel.MouseMove += OnMove;
            panel.MouseLeftButtonUp += OnUp;

            root.Children.Add(panel);
            root.Children.Add(hit);
        }

        double Hidden() { return (Right ? 1 : -1) * (PanelW + EdgeGap + ShadowRoom); }

        // ---------- modo barra de tarefas
        void BuildTaskbar()
        {
            SizeToContent = SizeToContent.Width;
            tbRect = TaskbarRect();
            tbTop = tbRect.Top < SystemParameters.PrimaryScreenHeight / 2;
            Height = Math.Max(24, tbRect.Height);

            chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 9, 0) };
            strip = new Border {
                CornerRadius = new CornerRadius(5), Background = Hittable, Child = chips,
                Margin = new Thickness(0, 4, 0, 4), Cursor = Cursors.Hand
            };
            strip.MouseEnter += delegate { strip.Background = B(tbTh.Hover); collapseTimer.Stop(); if (!expanded) expandTimer.Start(); };
            strip.MouseLeave += delegate { strip.Background = Hittable; expandTimer.Stop(); if (expanded) collapseTimer.Start(); };
            strip.MouseLeftButtonDown += OnDown;
            strip.MouseMove += OnMove;
            strip.MouseLeftButtonUp += OnUp;
            strip.ContextMenu = new ContextMenu();
            strip.ContextMenuOpening += delegate { FillMenu(strip.ContextMenu); };
            root.Children.Add(strip);

            // Painel de detalhes num Popup acima (ou abaixo, se a barra estiver no topo) dos ícones.
            panel.Margin = new Thickness(0);
            panel.Opacity = 0;
            popSlide = new TranslateTransform(0, 0);
            panel.RenderTransform = popSlide;
            var host = new Border { Padding = new Thickness(ShadowRoom / 2, tbTop ? 6 : ShadowRoom / 2, ShadowRoom / 2, tbTop ? ShadowRoom / 2 : 6), Child = panel, Background = Brushes.Transparent };
            popup = new Popup {
                AllowsTransparency = true, StaysOpen = true, PopupAnimation = PopupAnimation.None,
                PlacementTarget = strip, Placement = PlacementMode.Custom, Child = host
            };
            popup.CustomPopupPlacementCallback = (popupSize, targetSize, offset) => {
                double x = Right ? targetSize.Width - popupSize.Width : 0;
                double y = tbTop ? targetSize.Height : -popupSize.Height;
                return new[] { new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.Horizontal) };
            };
        }

        // ---------- modo notch
        void BuildNotch()
        {
            SizeToContent = SizeToContent.Manual;
            Width = NotchWinW; Height = NotchWinH;

            hit = new Border {
                Width = NotchW + 70, Height = 14, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top, Background = Hittable
            };
            hit.MouseEnter += delegate { if (!expanded) expandTimer.Start(); };
            hit.MouseLeave += delegate { expandTimer.Stop(); };

            notchChips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            notchContent = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 9, 10, 11), Opacity = 0 };
            notchContent.Children.Add(notchChips);
            var dots = DotsButton(nth.Muted, nth.Hover);
            dots.Margin = new Thickness(14, 0, 0, 0);
            dots.VerticalAlignment = VerticalAlignment.Center;
            notchContent.Children.Add(dots);

            notchDot = new Border { Width = 22, Height = 3, CornerRadius = new CornerRadius(1.5), Opacity = 0, Background = B(Theme.Amber) };
            notchCanvas = new Canvas { ClipToBounds = true };
            notchCanvas.Children.Add(notchContent);
            notchCanvas.Children.Add(notchDot);

            notch = new Border {
                Width = NotchW, Height = NotchH, Background = Brushes.Black,
                CornerRadius = new CornerRadius(0, 0, NotchRadius, NotchRadius),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Child = notchCanvas,
                Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Direction = 270, Opacity = 0.35, Color = Colors.Black }
            };
            notch.SizeChanged += delegate { LayoutNotch(); };
            notchContent.SizeChanged += delegate {
                LayoutNotch();
                if (expanded) ResizeOpenNotch();
            };
            notch.MouseEnter += delegate { collapseTimer.Stop(); peekTimer.Stop(); if (!expanded) expandTimer.Start(); };
            notch.MouseLeave += delegate { expandTimer.Stop(); if (expanded) collapseTimer.Start(); };
            notch.ContextMenu = new ContextMenu();
            notch.ContextMenuOpening += delegate { FillMenu(notch.ContextMenu); };
            notch.ContextMenu.Closed += delegate { if (!PointerInside()) collapseTimer.Start(); };

            root.Children.Add(hit);
            root.Children.Add(notch);
        }

        void LayoutNotch()
        {
            if (notch == null) return;
            double w = notch.ActualWidth, h = notch.ActualHeight;
            Canvas.SetLeft(notchContent, Math.Round((w - notchContent.DesiredSize.Width) / 2));
            Canvas.SetTop(notchContent, 0);
            Canvas.SetLeft(notchDot, Math.Round((w - notchDot.Width) / 2));
            Canvas.SetTop(notchDot, Math.Max(0, h - notchDot.Height - 2));
        }

        Size NotchOpenSize()
        {
            notchContent.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size s = notchContent.DesiredSize;
            return new Size(Math.Max(NotchW + 40, Math.Ceiling(s.Width)), Math.Max(NotchH, Math.Ceiling(s.Height)));
        }

        // Lista mudou com o notch aberto: só acompanha o novo tamanho, sem refazer o fade.
        void ResizeOpenNotch()
        {
            Size target = NotchOpenSize();
            if (Math.Abs(target.Width - notch.Width) < 0.5 && Math.Abs(target.Height - notch.Height) < 0.5) return;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(200);
            notch.BeginAnimation(WidthProperty, new DoubleAnimation(target.Width, dur) { EasingFunction = ease });
            notch.BeginAnimation(HeightProperty, new DoubleAnimation(target.Height, dur) { EasingFunction = ease });
        }

        void AnimateNotch(bool open, bool instant)
        {
            Size target = open ? NotchOpenSize() : new Size(NotchW, NotchH);
            if (instant)
            {
                notch.BeginAnimation(WidthProperty, null); notch.BeginAnimation(HeightProperty, null);
                notchContent.BeginAnimation(OpacityProperty, null);
                notch.Width = target.Width; notch.Height = target.Height;
                notchContent.Opacity = open ? 1 : 0;
                return;
            }
            IEasingFunction ease = open ? (IEasingFunction)new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut }
                                        : new CubicEase { EasingMode = EasingMode.EaseIn };
            var dur = TimeSpan.FromMilliseconds(open ? 280 : 190);
            notch.BeginAnimation(WidthProperty, new DoubleAnimation(target.Width, dur) { EasingFunction = ease });
            notch.BeginAnimation(HeightProperty, new DoubleAnimation(target.Height, dur) { EasingFunction = ease });
            notchContent.BeginAnimation(OpacityProperty, open
                ? new DoubleAnimation(1, TimeSpan.FromMilliseconds(170)) { BeginTime = TimeSpan.FromMilliseconds(110) }
                : new DoubleAnimation(0, TimeSpan.FromMilliseconds(90)));
        }

        void Rebuild()
        {
            bool wasExpanded = expanded;
            expanded = false;
            expandTimer.Stop(); collapseTimer.Stop(); peekTimer.Stop();
            BuildUi();
            Position();
            lastSig = null;
            Refresh(true);
            if (!Capturing) { if (IsTaskbar) topTimer.Start(); else topTimer.Stop(); }
            UpdateFullscreenHide();
            if (wasExpanded && IsFloat) Expand();
        }

        // Chamado pela janela de configurações.
        public void ApplySettings()
        {
            cfg.Save();
            Rebuild();
        }

        void Position()
        {
            if (Capturing) { Left = -20000; Top = 0; return; }
            Rect wa = SystemParameters.WorkArea;
            if (IsNotch)
            {
                Left = Math.Round(wa.Left + (wa.Width - Width) / 2);
                Top = wa.Top;
                return;
            }
            if (IsTaskbar) { PositionTaskbar(); return; }
            Left = Right ? wa.Right - Width : wa.Left;
            double cy = wa.Top + cfg.CenterY * wa.Height;
            Top = Math.Max(wa.Top, Math.Min(wa.Bottom - Height, cy - Height / 2));
        }

        // Ponto de referência no lado escolhido: logo depois da borda esquerda da barra,
        // ou logo antes da área de notificação (relógio / ícones da bandeja) na direita.
        double TaskbarAnchor(Rect tb)
        {
            if (!Right) return tb.Left + 6;
            Rect tray = TrayRect();
            if (tray.Width > 0 && tray.Left > tb.Left + tb.Width / 2 && tray.Left < tb.Right) return tray.Left - 4;
            return tb.Right - 230; // não achou a bandeja: deixa espaço para relógio e ícones
        }

        void PositionTaskbar()
        {
            tbRect = TaskbarRect();
            double w = ActualWidth > 0 ? ActualWidth : Width;
            if (double.IsNaN(w) || w <= 0) w = 60;
            double h = Math.Max(24, tbRect.Height);
            if (Math.Abs(Height - h) > 0.5) Height = h;
            double anchor = TaskbarAnchor(tbRect);
            double x = Right ? anchor - w - cfg.TaskbarOffset : anchor + cfg.TaskbarOffset;
            x = Math.Max(tbRect.Left, Math.Min(tbRect.Right - w, x));
            if (Math.Abs(Left - x) > 0.5) Left = x;
            if (Math.Abs(Top - tbRect.Top) > 0.5) Top = tbRect.Top;
        }

        // ------------------------------------------------------- animation
        void Expand()
        {
            if (expanded) return;
            if (IsTaskbar && root.Visibility != Visibility.Visible) return;
            expanded = true;
            if (IsNotch) { AnimateNotch(true, false); notchDot.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(90))); return; }
            if (IsTaskbar)
            {
                popup.IsOpen = true;
                double from = tbTop ? -8 : 8;
                popSlide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(170)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                panel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
                return;
            }
            panel.Visibility = Visibility.Visible;
            hit.IsHitTestVisible = false;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(Hidden() * 0.35, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
            panel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
            pill.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(90)));
        }

        void Collapse()
        {
            if (!expanded) return;
            expanded = false;
            if (IsNotch) { AnimateNotch(false, false); UpdateIndicator(current, 260); return; }
            if (IsTaskbar)
            {
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
                fade.Completed += delegate { if (!expanded && popup != null) popup.IsOpen = false; };
                panel.BeginAnimation(OpacityProperty, fade);
                popSlide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(tbTop ? -6 : 6, TimeSpan.FromMilliseconds(120)));
                return;
            }
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var a = new DoubleAnimation(Hidden() * 0.35, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease };
            a.Completed += delegate { if (!expanded && IsFloat && hit != null) { panel.Visibility = Visibility.Hidden; hit.IsHitTestVisible = true; } };
            slide.BeginAnimation(TranslateTransform.XProperty, a);
            panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)));
            UpdateIndicator(current, 220);
        }

        void Peek()
        {
            if (expanded || ForegroundIsFullscreen()) return;
            Expand();
            peekTimer.Stop(); peekTimer.Start();
        }

        // Em tela cheia (jogos, vídeos) a barra de tarefas some; o notch e os ícones somem junto.
        void UpdateFullscreenHide()
        {
            if (IsFloat || Capturing) { root.Visibility = Visibility.Visible; return; }
            bool hide = ForegroundIsFullscreen();
            if (IsTaskbar && !hide)
            {
                // Barra com ocultação automática recolhida: não deixa os ícones boiando na tela.
                Rect tb = TaskbarRect();
                hide = tb.Height <= 0 || tb.Top >= SystemParameters.PrimaryScreenHeight - 4 || tb.Bottom <= 4;
            }
            var v = hide ? Visibility.Hidden : Visibility.Visible;
            if (root.Visibility != v)
            {
                root.Visibility = v;
                if (hide && expanded) Collapse();
            }
        }

        // ------------------------------------------------------ drag
        // flutuante: arrasta na vertical; barra de tarefas: arrasta na horizontal para ajustar a posição.
        void OnDown(object s, MouseButtonEventArgs e)
        {
            dragSrc = (UIElement)s;
            dragArmed = true; dragStart = e.GetPosition(this);
            dragSrc.CaptureMouse();
        }

        void OnMove(object s, MouseEventArgs e)
        {
            if (!dragArmed) return;
            Point p = e.GetPosition(this);
            if (IsTaskbar)
            {
                if (!dragging && Math.Abs(p.X - dragStart.X) < 4) return;
                if (!dragging) { dragging = true; if (expanded) Collapse(); }
                Rect tb = tbRect;
                Left = Math.Max(tb.Left, Math.Min(tb.Right - ActualWidth, Left + p.X - dragStart.X));
                return;
            }
            if (!dragging && Math.Abs(p.Y - dragStart.Y) < 3) return;
            dragging = true;
            Rect wa = SystemParameters.WorkArea;
            Top = Math.Max(wa.Top - Height / 2 + 40, Math.Min(wa.Bottom - Height / 2 - 40, Top + p.Y - dragStart.Y));
        }

        void OnUp(object s, MouseButtonEventArgs e)
        {
            if (dragSrc != null) dragSrc.ReleaseMouseCapture();
            dragArmed = false;
            if (!dragging)
            {
                if (IsTaskbar && !expanded) { expandTimer.Stop(); Expand(); }
                return;
            }
            dragging = false;
            if (IsTaskbar)
            {
                double anchor = TaskbarAnchor(tbRect);
                double off = Right ? anchor - (Left + ActualWidth) : Left - anchor;
                cfg.TaskbarOffset = Math.Max(0, Math.Round(off));
                cfg.Save();
                Position();
                return;
            }
            Rect wa = SystemParameters.WorkArea;
            cfg.CenterY = Math.Max(0, Math.Min(1, (Top + Height / 2 - wa.Top) / wa.Height));
            cfg.Save();
            Position();
        }

        // ------------------------------------------------------------- menu / configurações
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "Ranger";

        public static bool StartupEnabled()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(RunName) != null; }
            catch (Exception) { return false; }
        }

        public static void SetStartup(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue(RunName, "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\"");
                    else k.DeleteValue(RunName, false);
                }
            }
            catch (Exception) { }
        }

        void OpenSettings()
        {
            if (expanded && !IsFloat) Collapse();
            if (settingsWin != null) { settingsWin.Activate(); return; }
            settingsWin = new SettingsWindow(cfg, th, ApplySettings);
            settingsWin.Closed += delegate { settingsWin = null; };
            settingsWin.Show();
            settingsWin.Activate();
        }

        void FillMenu(ContextMenu m)
        {
            m.Items.Clear();
            var settings = new MenuItem { Header = "Configurações…" };
            settings.Click += delegate { OpenSettings(); };
            m.Items.Add(settings);
            if (!IsNotch)
            {
                string h = IsTaskbar ? (Right ? "Mover para o lado esquerdo da barra" : "Mover para o lado direito da barra")
                                     : (Right ? "Mover para a borda esquerda" : "Mover para a borda direita");
                var side = new MenuItem { Header = h };
                side.Click += delegate { cfg.Side = Right ? "left" : "right"; cfg.TaskbarOffset = 0; ApplySettings(); };
                m.Items.Add(side);
            }
            var startup = new MenuItem { Header = "Iniciar com o Windows", IsCheckable = true, IsChecked = StartupEnabled() };
            startup.Click += delegate { SetStartup(startup.IsChecked); };
            var quit = new MenuItem { Header = "Fechar" };
            quit.Click += delegate { Application.Current.Shutdown(); };
            m.Items.Add(startup); m.Items.Add(new Separator()); m.Items.Add(quit);
        }

        // ------------------------------------------------------------ data
        List<DeviceState> Collect()
        {
            var list = new List<DeviceState>();
            var m = x11.Snapshot();
            if (m != null) list.Add(m);
            foreach (var d in bt)
            {
                // Se o X11 estiver em modo Bluetooth, não duplica.
                if (m != null && !m.Stale && d.Name.IndexOf("X11", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                list.Add(d);
            }
            return list;
        }

        void PollBluetooth()
        {
            if (btBusy || (DateTime.UtcNow - btAt).TotalSeconds < 20) return;
            btBusy = true;
            ThreadPool.QueueUserWorkItem(delegate {
                List<DeviceState> r = null;
                try { r = BluetoothProvider.Poll(); } catch (Exception) { }
                Dispatcher.BeginInvoke(new Action(delegate {
                    if (r != null) bt = r;
                    btAt = DateTime.UtcNow; btBusy = false;
                    Refresh(false);
                }));
            });
        }

        void Refresh(bool force)
        {
            PollBluetooth();
            current = Collect();
            CheckAlerts(current);
            if (!expanded) UpdateIndicator(current, 300);

            var sb = new StringBuilder();
            foreach (var d in current) sb.Append(d.Signature()).Append('\n');
            string sig = sb.ToString();
            if (force || sig != lastSig)
            {
                lastSig = sig;
                BuildRows(current);
                if (IsTaskbar) BuildChips(chips, current, tbTh, Math.Min(28, Math.Max(18, Height - 18)), 12);
                if (IsNotch) BuildChips(notchChips, current, nth, 26, 13);
            }
        }

        void CheckAlerts(List<DeviceState> list)
        {
            foreach (var d in list)
            {
                if (d.Percent < 0 || d.Stale) continue;
                if (d.Charging || d.Full || d.Percent > LowAlert + 5) { alerted.Remove(d.Key); continue; }
                if (d.Percent <= LowAlert && alerted.Add(d.Key) && !Capturing) Peek();
            }
        }

        static Color LevelColor(DeviceState d, Theme t)
        {
            if (d.Charging || d.Full) return t.Green;
            if (d.Percent >= 0 && d.Percent <= 10) return Theme.Red;
            if (d.Percent >= 0 && d.Percent <= 25) return Theme.Amber;
            return t.Fill;
        }

        // Indicador recolhido: o traço (flutuante) ou o risquinho colorido dentro do notch quando há bateria baixa.
        void UpdateIndicator(List<DeviceState> list, int ms)
        {
            int worst = 101;
            foreach (var d in list) if (!d.Charging && !d.Full && !d.Stale && d.Percent >= 0) worst = Math.Min(worst, d.Percent);
            if (IsFloat && pill != null)
            {
                Color c = worst <= 10 ? Theme.Red : worst <= 25 ? Theme.Amber : th.Pill;
                double op = worst <= 25 ? 0.95 : list.Count == 0 ? 0.25 : 0.5;
                pill.Background = B(c);
                pill.BeginAnimation(OpacityProperty, new DoubleAnimation(op, TimeSpan.FromMilliseconds(ms)));
            }
            if (IsNotch && notchDot != null)
            {
                notchDot.Background = B(worst <= 10 ? Theme.Red : Theme.Amber);
                notchDot.BeginAnimation(OpacityProperty, new DoubleAnimation(worst <= 25 ? 1 : 0, TimeSpan.FromMilliseconds(ms)));
            }
        }

        public static string Glyph(Kind k)
        {
            switch (k)
            {
                case Kind.Mouse: return GMouse;
                case Kind.Keyboard: return GKeyboard;
                case Kind.Headset: return GHeadset;
                case Kind.Gamepad: return GGamepad;
                default: return GOther;
            }
        }

        static SolidColorBrush B(Color c) { return new SolidColorBrush(c); }

        void BuildRows(List<DeviceState> list)
        {
            rows.Children.Clear();
            if (list.Count == 0)
            {
                rows.Children.Add(new TextBlock {
                    Text = "Nenhum dispositivo sem fio com bateria conectado.", TextWrapping = TextWrapping.Wrap,
                    FontSize = 11.5, Foreground = B(th.Muted), Margin = new Thickness(0, 3, 0, 3)
                });
                return;
            }
            for (int i = 0; i < list.Count; i++) rows.Children.Add(Row(list[i], i == 0));
        }

        UIElement Row(DeviceState d, bool first)
        {
            var g = new Grid { Margin = new Thickness(0, first ? 1 : 11, 0, 1), Opacity = d.Stale ? 0.55 : 1 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var icon = new TextBlock { Text = Glyph(d.Kind), FontFamily = Icons, FontSize = 15, Foreground = B(th.Muted), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(icon);

            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(new TextBlock { Text = d.Name, FontSize = 12.5, Foreground = B(th.Text), TextTrimming = TextTrimming.CharacterEllipsis });
            if (!string.IsNullOrEmpty(d.Detail))
                names.Children.Add(new TextBlock { Text = d.Detail, FontSize = 10.5, Foreground = B(th.Muted), Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(names, 1);
            g.Children.Add(names);

            Color lc = LevelColor(d, th);
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            if (d.Charging || d.Full)
                right.Children.Add(new TextBlock { Text = GBolt, FontFamily = Icons, FontSize = 11, Foreground = B(th.Green), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 4, 0) });
            var pct = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            bool low = !d.Charging && !d.Full && d.Percent >= 0 && d.Percent <= 25;
            pct.Inlines.Add(new System.Windows.Documents.Run(d.Percent >= 0 ? d.Percent.ToString() : "—") {
                FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = B(low ? lc : th.Text) });
            if (d.Percent >= 0)
                pct.Inlines.Add(new System.Windows.Documents.Run("%") { FontSize = 10.5, Foreground = B(th.Muted) });
            right.Children.Add(pct);
            Grid.SetColumn(right, 2);
            g.Children.Add(right);

            int p = Math.Max(0, Math.Min(100, d.Percent));
            var bar = new Grid { Height = 3, Margin = new Thickness(26, 7, 0, 0) };
            bar.Children.Add(new Border { CornerRadius = new CornerRadius(1.5), Background = B(th.Track) });
            var fillHost = new Grid();
            fillHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(p, 0.001), GridUnitType.Star) });
            fillHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(100 - p, 0.001), GridUnitType.Star) });
            if (p > 0) fillHost.Children.Add(new Border { CornerRadius = new CornerRadius(1.5), Background = B(lc) });
            bar.Children.Add(fillHost);
            Grid.SetRow(bar, 1); Grid.SetColumnSpan(bar, 3);
            g.Children.Add(bar);
            return g;
        }

        // ------------------------------------------- ícones com anel (barra / notch)
        void BuildChips(StackPanel host, List<DeviceState> list, Theme t, double ring, double fontSize)
        {
            if (host == null) return;
            host.Children.Clear();
            if (list.Count == 0)
            {
                var empty = new DeviceState { Kind = Kind.Other, Percent = -1 };
                var e = Chip(empty, t, ring, fontSize, false);
                e.Opacity = 0.5;
                e.ToolTip = "Nenhum dispositivo sem fio com bateria conectado";
                host.Children.Add(e);
                return;
            }
            for (int i = 0; i < list.Count; i++)
            {
                var c = Chip(list[i], t, ring, fontSize, cfg.ShowPercent);
                if (i > 0) c.Margin = new Thickness(cfg.ShowPercent ? 12 : 8, 0, 0, 0);
                host.Children.Add(c);
            }
        }

        static FrameworkElement Chip(DeviceState d, Theme t, double ring, double fontSize, bool showPct)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Opacity = d.Stale ? 0.55 : 1 };
            sp.Children.Add(Ring(d, t, ring));
            if (showPct)
            {
                var tb = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
                bool low = !d.Charging && !d.Full && d.Percent >= 0 && d.Percent <= 25;
                tb.Inlines.Add(new System.Windows.Documents.Run(d.Percent >= 0 ? d.Percent + "%" : "—") {
                    FontSize = fontSize, FontWeight = FontWeights.SemiBold, Foreground = B(low ? LevelColor(d, t) : t.Text) });
                sp.Children.Add(tb);
            }
            if (!string.IsNullOrEmpty(d.Name))
                sp.ToolTip = d.Name + (d.Percent >= 0 ? " · " + d.Percent + "%" : "") + (string.IsNullOrEmpty(d.Detail) ? "" : " · " + d.Detail);
            return sp;
        }

        // Ícone do dispositivo dentro de um anel que se preenche conforme a bateria.
        public static FrameworkElement Ring(DeviceState d, Theme t, double size)
        {
            double thick = Math.Max(2, Math.Round(size / 10.0 * 2) / 2);
            var g = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(new Ellipse { Stroke = B(t.Track), StrokeThickness = thick });

            int p = Math.Max(0, Math.Min(100, d.Percent));
            Color c = LevelColor(d, t);
            if (p >= 100) g.Children.Add(new Ellipse { Stroke = B(c), StrokeThickness = thick });
            else if (p > 0)
            {
                double r = (size - thick) / 2, cx = size / 2, a = p / 100.0 * 2 * Math.PI;
                var fig = new PathFigure { StartPoint = new Point(cx, cx - r), IsClosed = false };
                fig.Segments.Add(new ArcSegment(new Point(cx + r * Math.Sin(a), cx - r * Math.Cos(a)), new Size(r, r), 0, p > 50, SweepDirection.Clockwise, true));
                var geo = new PathGeometry(); geo.Figures.Add(fig);
                g.Children.Add(new Path {
                    Data = geo, Stroke = B(c), StrokeThickness = thick,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
                });
            }
            g.Children.Add(new TextBlock {
                Text = d.Charging || d.Full ? GBolt : Glyph(d.Kind), FontFamily = Icons, FontSize = Math.Round(size * 0.44),
                Foreground = B(d.Charging || d.Full ? t.Green : t.Text),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            });
            return g;
        }

        // --------------------------------------------- capture (testes)
        void RunCapture()
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
            t.Tick += delegate {
                t.Stop();
                try { bt = BluetoothProvider.Poll(); } catch (Exception) { }
                btAt = DateTime.UtcNow;
                Refresh(true);
                if (dumpPath != null)
                {
                    var sb = new StringBuilder();
                    foreach (var d in current) sb.AppendLine(d.Signature());
                    if (current.Count == 0) sb.AppendLine("(nenhum)");
                    File.WriteAllText(dumpPath, sb.ToString());
                }
                if (snapshotBase != null)
                {
                    Save(snapshotBase + "-collapsed.png");
                    if (IsFloat)
                    {
                        panel.Visibility = Visibility.Visible; panel.Opacity = 1; slide.X = 0; pill.Opacity = 0; expanded = true;
                        Save(snapshotBase + "-expanded.png");
                    }
                    else if (IsNotch)
                    {
                        expanded = true; AnimateNotch(true, true);
                        Save(snapshotBase + "-expanded.png");
                    }
                }
                Application.Current.Shutdown();
            };
            t.Start();
        }

        void Save(string path)
        {
            root.UpdateLayout();
            var rtb = new RenderTargetBitmap((int)(ActualWidth * 2), (int)(ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
            rtb.Render(root);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(path)) enc.Save(fs);
        }

        // ----------------------------------------------------------- win32
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint f);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public int dwFlags; }

        void KeepOnTop()
        {
            var h = new WindowInteropHelper(this).Handle;
            if (h != IntPtr.Zero) SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // NOSIZE|NOMOVE|NOACTIVATE
        }

        // Converte um retângulo em pixels físicos para unidades do WPF.
        Rect FromDevice(RECT r)
        {
            Matrix m = Matrix.Identity;
            var src = PresentationSource.FromVisual(this);
            if (src != null && src.CompositionTarget != null) m = src.CompositionTarget.TransformFromDevice;
            Point a = m.Transform(new Point(r.L, r.T)), b = m.Transform(new Point(r.R, r.B));
            return new Rect(a, b);
        }

        Rect TaskbarRect()
        {
            IntPtr tw = FindWindow("Shell_TrayWnd", null);
            RECT r;
            if (tw != IntPtr.Zero && GetWindowRect(tw, out r) && r.R > r.L && r.B > r.T) return FromDevice(r);
            // Sem a barra (explorer reiniciando): usa a diferença entre a tela e a área de trabalho.
            Rect wa = SystemParameters.WorkArea;
            double sh = SystemParameters.PrimaryScreenHeight, sw = SystemParameters.PrimaryScreenWidth;
            if (wa.Top > 0) return new Rect(0, 0, sw, wa.Top);
            return new Rect(0, wa.Bottom, sw, Math.Max(0, sh - wa.Bottom));
        }

        Rect TrayRect()
        {
            IntPtr tw = FindWindow("Shell_TrayWnd", null);
            if (tw == IntPtr.Zero) return Rect.Empty;
            IntPtr tray = FindWindowEx(tw, IntPtr.Zero, "TrayNotifyWnd", null);
            RECT r;
            if (tray == IntPtr.Zero || !GetWindowRect(tray, out r) || r.R <= r.L) return Rect.Empty;
            return FromDevice(r);
        }

        static bool ForegroundIsFullscreen()
        {
            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero) return false;
            var sb = new StringBuilder(64); GetClassName(h, sb, 64);
            string cls = sb.ToString();
            if (cls == "WorkerW" || cls == "Progman" || cls == "Shell_TrayWnd") return false;
            RECT r; if (!GetWindowRect(h, out r)) return false;
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            if (!GetMonitorInfo(MonitorFromWindow(h, 2), ref mi)) return false;
            return r.L <= mi.rcMonitor.L && r.T <= mi.rcMonitor.T && r.R >= mi.rcMonitor.R && r.B >= mi.rcMonitor.B;
        }
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            string snap = null, dump = null, theme = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--snapshot") snap = args[i + 1];
                if (args[i] == "--dump") dump = args[i + 1];
                if (args[i] == "--theme") theme = args[i + 1];
            }
            bool capture = snap != null || dump != null;
            bool created;
            using (var mutex = new Mutex(true, "Local\\Ranger.SingleInstance", out created))
            {
                if (!created && !capture) return 0; // já está aberto
                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                app.Run(new Widget(snap, dump, theme));
                GC.KeepAlive(mutex);
            }
            return 0;
        }
    }
}
