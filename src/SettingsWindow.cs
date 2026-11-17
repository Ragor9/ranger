// Janela de configurações do Ranger (aberta pelo botão "⋯").
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Ranger
{
    public class SettingsWindow : Window
    {
        readonly Settings cfg;
        readonly Theme th;
        readonly Action apply;
        static readonly Brush Hittable = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255));

        public SettingsWindow(Settings cfg, Theme th, Action apply)
        {
            this.cfg = cfg; this.th = th; this.apply = apply;
            Title = "Ranger — Configurações";
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.WidthAndHeight;
            Topmost = true; ShowInTaskbar = true;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            UseLayoutRounding = true;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
            Build();
        }

        static SolidColorBrush B(Color c) { return new SolidColorBrush(c); }

        void Changed()
        {
            apply();
            Build();
        }

        void Build()
        {
            var body = new StackPanel { Width = 340 };

            // cabeçalho (arrasta a janela)
            var head = new Grid { Margin = new Thickness(0, 0, -6, 14), Background = Hittable };
            head.MouseLeftButtonDown += (s, e) => { try { DragMove(); } catch (InvalidOperationException) { } };
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(Widget.Ring(new DeviceState { Kind = Kind.Mouse, Percent = 72 }, th, 22));
            title.Children.Add(new TextBlock { Text = "Ranger", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = B(th.Text), Margin = new Thickness(9, 0, 0, 1), VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(title);
            var close = IconButton("", 11);
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.ToolTip = "Fechar configurações";
            close.MouseLeftButtonUp += delegate { Close(); };
            head.Children.Add(close);
            body.Children.Add(head);

            // modo de exibição
            body.Children.Add(Label("Exibição"));
            body.Children.Add(ModeCard("float", "Flutuante", "Traço na borda da tela; abre ao passar o mouse e pode ser arrastado para cima e para baixo."));
            body.Children.Add(ModeCard("taskbar", "Barra de tarefas", "Ícones com anel de bateria fixos na barra, ao lado do relógio ou no canto esquerdo."));
            body.Children.Add(ModeCard("notch", "Notch", "Barrinha preta no topo da tela, como a do iPhone. Abre ao passar o mouse."));

            // lado
            if (cfg.Mode != "notch")
            {
                body.Children.Add(Label(cfg.Mode == "taskbar" ? "Lado da barra de tarefas" : "Borda da tela", 14));
                var seg = new Grid();
                seg.ColumnDefinitions.Add(new ColumnDefinition());
                seg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                seg.ColumnDefinitions.Add(new ColumnDefinition());
                var l = Segment("Esquerdo", cfg.Side == "left", delegate { cfg.Side = "left"; cfg.TaskbarOffset = 0; Changed(); });
                var r = Segment("Direito", cfg.Side != "left", delegate { cfg.Side = "right"; cfg.TaskbarOffset = 0; Changed(); });
                Grid.SetColumn(r, 2);
                seg.Children.Add(l); seg.Children.Add(r);
                body.Children.Add(seg);
                if (cfg.Mode == "taskbar")
                {
                    var hint = new Grid { Margin = new Thickness(2, 8, 0, 0) };
                    hint.Children.Add(new TextBlock { Text = "Dica: arraste os ícones na barra para ajustar a posição.", FontSize = 11, Foreground = B(th.Muted), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 70, 0) });
                    if (cfg.TaskbarOffset > 0)
                    {
                        var reset = new TextBlock { Text = "Redefinir", FontSize = 11, Foreground = B(th.Text), TextDecorations = TextDecorations.Underline, Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
                        reset.MouseLeftButtonUp += delegate { cfg.TaskbarOffset = 0; Changed(); };
                        hint.Children.Add(reset);
                    }
                    body.Children.Add(hint);
                }
            }

            // opções
            body.Children.Add(Label("Opções", 14));
            if (cfg.Mode != "float")
                body.Children.Add(ToggleRow("Mostrar porcentagem", "Número ao lado de cada ícone.", cfg.ShowPercent, v => { cfg.ShowPercent = v; Changed(); }));
            body.Children.Add(ToggleRow("Iniciar com o Windows", null, Widget.StartupEnabled(), v => { Widget.SetStartup(v); Build(); }));

            // rodapé
            var foot = new Grid { Margin = new Thickness(0, 16, 0, 0) };
            var quit = new TextBlock { Text = "Fechar o Ranger", FontSize = 12, Foreground = B(Theme.Red), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            quit.MouseLeftButtonUp += delegate { Application.Current.Shutdown(); };
            foot.Children.Add(quit);
            var done = new Border {
                CornerRadius = new CornerRadius(6), Background = B(th.Text), Padding = new Thickness(16, 6, 16, 7),
                HorizontalAlignment = HorizontalAlignment.Right, Cursor = Cursors.Hand,
                Child = new TextBlock { Text = "Pronto", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = B(th.IsLight ? Colors.White : Color.FromRgb(0x1E, 0x1F, 0x22)) }
            };
            done.MouseLeftButtonUp += delegate { Close(); };
            foot.Children.Add(done);
            body.Children.Add(foot);

            var card = new Border {
                Margin = new Thickness(18), Padding = new Thickness(18, 14, 18, 16), CornerRadius = new CornerRadius(12),
                Background = B(Opaque(th.Panel)), BorderBrush = B(th.Border), BorderThickness = new Thickness(1), Child = body,
                Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 4, Direction = 270, Opacity = 0.35, Color = Colors.Black }
            };
            Content = card;
        }

        static Color Opaque(Color c) { return Color.FromRgb(c.R, c.G, c.B); }

        TextBlock Label(string text, double top = 0)
        {
            return new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = B(th.Muted), Margin = new Thickness(2, top, 0, 7) };
        }

        Border IconButton(string glyph, double size)
        {
            var b = new Border {
                Width = 28, Height = 26, CornerRadius = new CornerRadius(6), Background = Hittable, Cursor = Cursors.Hand,
                Child = new TextBlock { Text = glyph, FontFamily = Widget.Icons, FontSize = size, Foreground = B(th.Muted), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            b.MouseEnter += delegate { b.Background = B(th.Hover); };
            b.MouseLeave += delegate { b.Background = Hittable; };
            b.MouseLeftButtonDown += (s, e) => { e.Handled = true; };
            return b;
        }

        Border ModeCard(string mode, string title, string desc)
        {
            bool sel = cfg.Mode == mode;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var radio = new Grid { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 1, 0, 0) };
            radio.Children.Add(new Ellipse { Stroke = B(sel ? th.Text : th.Muted), StrokeThickness = sel ? 1.5 : 1 });
            if (sel) radio.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = B(th.Text) });
            g.Children.Add(radio);

            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = B(th.Text) });
            text.Children.Add(new TextBlock { Text = desc, FontSize = 11, Foreground = B(th.Muted), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(text, 1);
            g.Children.Add(text);

            var prev = Preview(mode);
            prev.Margin = new Thickness(10, 0, 0, 0);
            prev.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(prev, 2);
            g.Children.Add(prev);

            var card = new Border {
                Child = g, Padding = new Thickness(11, 10, 11, 10), Margin = new Thickness(0, 0, 0, 6), CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1), BorderBrush = B(sel ? th.Muted : th.Border),
                Background = sel ? (Brush)B(th.Hover) : Hittable, Cursor = Cursors.Hand
            };
            if (!sel)
            {
                card.MouseEnter += delegate { card.Background = B(th.Hover); };
                card.MouseLeave += delegate { card.Background = Hittable; };
                card.MouseLeftButtonUp += delegate { cfg.Mode = mode; Changed(); };
            }
            return card;
        }

        // Miniatura de cada modo: uma "telinha" com o elemento na posição dele.
        FrameworkElement Preview(string mode)
        {
            var screen = new Grid { Width = 58, Height = 38, ClipToBounds = true };
            screen.Children.Add(new Border { CornerRadius = new CornerRadius(4), BorderBrush = B(th.Border), BorderThickness = new Thickness(1), Background = B(th.Track) });
            Color fg = th.Muted;
            if (mode == "float")
            {
                screen.Children.Add(new Border { Width = 3, Height = 12, CornerRadius = new CornerRadius(1.5), Background = B(fg), HorizontalAlignment = cfg.Side == "left" ? HorizontalAlignment.Left : HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 3, 0) });
            }
            else if (mode == "taskbar")
            {
                screen.Children.Add(new Border { Height = 7, VerticalAlignment = VerticalAlignment.Bottom, Background = B(th.Border), CornerRadius = new CornerRadius(0, 0, 4, 4), Margin = new Thickness(1) });
                var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = cfg.Side == "left" ? HorizontalAlignment.Left : HorizontalAlignment.Right, Margin = new Thickness(5, 0, 7, 2) };
                for (int i = 0; i < 2; i++) dots.Children.Add(new Ellipse { Width = 4, Height = 4, Stroke = B(fg), StrokeThickness = 1, Margin = new Thickness(1, 0, 0, 0) });
                screen.Children.Add(dots);
            }
            else
            {
                screen.Children.Add(new Border { Width = 18, Height = 4, Background = Brushes.Black, CornerRadius = new CornerRadius(0, 0, 2, 2), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) });
            }
            return screen;
        }

        Border Segment(string text, bool sel, Action click)
        {
            var b = new Border {
                CornerRadius = new CornerRadius(7), Padding = new Thickness(0, 7, 0, 8), BorderThickness = new Thickness(1),
                BorderBrush = B(sel ? th.Muted : th.Border), Background = sel ? (Brush)B(th.Hover) : Hittable, Cursor = Cursors.Hand,
                Child = new TextBlock { Text = text, FontSize = 12, FontWeight = sel ? FontWeights.SemiBold : FontWeights.Normal, Foreground = B(sel ? th.Text : th.Muted), HorizontalAlignment = HorizontalAlignment.Center }
            };
            if (!sel)
            {
                b.MouseEnter += delegate { b.Background = B(th.Hover); };
                b.MouseLeave += delegate { b.Background = Hittable; };
                b.MouseLeftButtonUp += delegate { click(); };
            }
            return b;
        }

        Border ToggleRow(string title, string desc, bool on, Action<bool> set)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = title, FontSize = 12.5, Foreground = B(th.Text) });
            if (desc != null) text.Children.Add(new TextBlock { Text = desc, FontSize = 11, Foreground = B(th.Muted), Margin = new Thickness(0, 1, 0, 0) });
            g.Children.Add(text);

            Color knobOn = th.IsLight ? Colors.White : Color.FromRgb(0x1E, 0x1F, 0x22);
            var knob = new Ellipse { Width = 12, Height = 12, Fill = B(on ? knobOn : th.Muted), HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left, Margin = new Thickness(3, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center };
            var track = new Border {
                Width = 38, Height = 20, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
                BorderBrush = B(on ? th.Text : th.Muted), Background = on ? (Brush)B(th.Text) : Hittable, Child = knob,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0)
            };
            Grid.SetColumn(track, 1);
            g.Children.Add(track);

            var row = new Border { Child = g, Padding = new Thickness(2, 7, 2, 7), Background = Hittable, Cursor = Cursors.Hand };
            row.MouseLeftButtonUp += delegate { set(!on); };
            return row;
        }
    }
}
