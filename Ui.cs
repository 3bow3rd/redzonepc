using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace RedZoneClub;

/// <summary>مكوّنات واجهة مساعدة (أزرار، بطاقات، نوافذ حوار) بنمط داكن عصري بألوان الشعار.</summary>
public static class U
{
    public static Color C(string h) => (Color)ColorConverter.ConvertFromString(h);
    public static SolidColorBrush B(string h) => (SolidColorBrush)new BrushConverter().ConvertFromString(h)!;
    public static readonly Brush Mu = B("#8B93A7"), Tx = B("#F2F5FB"), Tot = B("#FF4D63"), Ok = B("#2D8CFF"), Ac = B("#E5192D"), Card = B("#0F1626");
    static Brush Grad(string a, string b) => new LinearGradientBrush(C(a), C(b), 45);

    public static DropShadowEffect Glow(string c, double r, double o) =>
        new() { Color = C(c), BlurRadius = r, ShadowDepth = 0, Opacity = o };

    public static Button Btn(string t, string kind, Action a, double? w = null)
    {
        var b = new Button
        {
            Content = new TextBlock { Text = t, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
            Style = (Style)Application.Current.FindResource("Rz"),
            Background = kind switch
            {
                "pri" or "on" => Grad("#FF3B52", "#B3122A"),
                "ok" => Grad("#2D8CFF", "#1B5FD1"),
                "bad" => B("#D81B30"),
                "ghost" => Brushes.Transparent,
                _ => B("#1A2338")
            }
        };
        if (kind == "pri" || kind == "on") b.Effect = Glow("#FF2D45", 14, .45);
        else if (kind == "ok") b.Effect = Glow("#2D8CFF", 12, .35);
        if (kind == "ghost") b.BorderThickness = new Thickness(0);
        if (w != null) b.Width = w.Value;
        b.Click += (_, _) => a();
        return b;
    }

    public static TextBlock T(string s, double sz = 14, Brush? fg = null, bool bold = false, TextAlignment al = TextAlignment.Left) => new()
    {
        Text = s, FontSize = sz, Foreground = fg ?? Tx, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
        TextWrapping = TextWrapping.Wrap, TextAlignment = al, Margin = new Thickness(2, 3, 2, 3)
    };
    public static TextBlock Muted(string s, TextAlignment al = TextAlignment.Left) => T(s, 12.5, Mu, false, al);
    public static TextBlock Title(string s) => T(s, 16, Brushes.White, true);

    public static Border Panel(UIElement child, bool run = false) => new()
    {
        Background = new LinearGradientBrush(C("#141C30"), C("#0D1322"), 60),
        CornerRadius = new CornerRadius(20), Padding = new Thickness(16), Margin = new Thickness(6),
        BorderThickness = new Thickness(1), BorderBrush = run ? B("#B32D8CFF") : B("#1AFFFFFF"), Child = child,
        Effect = run ? Glow("#2D8CFF", 18, .25) : null
    };

    public static UniformGrid Row(params UIElement[] els)
    {
        var g = new UniformGrid { Rows = 1 }; foreach (var e in els) g.Children.Add(e); return g;
    }

    public static Grid Line(string a, string b, bool bold = false)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var x = T(a, 14, Tx, bold); var y = T(b, 14, Tx, bold); Grid.SetColumn(y, 1);
        g.Children.Add(x); g.Children.Add(y); return g;
    }

    public static TextBox Tb(string v, Action<string>? ch = null, bool ltr = false)
    {
        var t = new TextBox
        {
            Text = v, Background = B("#0A101D"), Foreground = Tx, CaretBrush = Brushes.White, BorderBrush = B("#33FFFFFF"),
            Padding = new Thickness(9, 8, 9, 8), Margin = new Thickness(2), FontSize = 14,
            FlowDirection = ltr ? FlowDirection.LeftToRight : FlowDirection.RightToLeft
        };
        if (ch != null) t.TextChanged += (_, _) => ch(t.Text);
        return t;
    }

    public static PasswordBox Pw() => new()
    {
        Background = B("#0A101D"), Foreground = Tx, CaretBrush = Brushes.White, BorderBrush = B("#33FFFFFF"),
        Padding = new Thickness(9, 8, 9, 8), Margin = new Thickness(2), FontSize = 14, FlowDirection = FlowDirection.LeftToRight
    };

    /// <summary>يحوّل الأرقام العربية/الفارسية إلى لاتينية ويزيل فواصل الآلاف.</summary>
    public static string Latin(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch >= '\u0660' && ch <= '\u0669') sb.Append((char)('0' + (ch - '\u0660')));
            else if (ch >= '\u06F0' && ch <= '\u06F9') sb.Append((char)('0' + (ch - '\u06F0')));
            else if (ch == '\u066B') sb.Append('.');
            else if (ch == '\u066C' || ch == ',' || ch == '\u060C') continue;
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    public static double? Num(string s) =>
        double.TryParse(Latin(s).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    /// <summary>يقبل 14:30 أو 2:30 PM أو 2:30 م</summary>
    public static bool ParseTime(string s, out TimeSpan t)
    {
        s = Latin(s).Trim().Replace("ص", "AM").Replace("م", "PM");
        if (TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out t) && t >= TimeSpan.Zero && t < TimeSpan.FromHours(24)) return true;
        if (DateTime.TryParse("2000-01-01 " + s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) { t = d.TimeOfDay; return true; }
        t = TimeSpan.Zero; return false;
    }

    /// <summary>نافذة حوار نمطية. fill يملأ المحتوى ويمكنه إغلاق النافذة.</summary>
    public static void Modal(string title, double w, Action<StackPanel, Window> fill)
    {
        var sp = new StackPanel { Margin = new Thickness(18) };
        var win = new Window
        {
            Title = title, Width = w, SizeToContent = SizeToContent.Height, MaxHeight = 760, Background = B("#0D1322"),
            Owner = Application.Current.MainWindow, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FlowDirection = FlowDirection.RightToLeft, ShowInTaskbar = false, ResizeMode = ResizeMode.NoResize,
            Content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        };
        fill(sp, win);
        win.ShowDialog();
    }

    public static bool Confirm(string msg) =>
        MessageBox.Show(msg, "Red Zone Club", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign) == MessageBoxResult.Yes;

    /// <summary>زر مشروب: إيموجي + اسم + سعر</summary>
    public static Button DrinkBtn(DrinkDef x, Action a)
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
        sp.Children.Add(new TextBlock { Text = Emo.Of(x.N), FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center, FontFamily = new FontFamily("Segoe UI Emoji") });
        sp.Children.Add(new TextBlock { Text = x.N, FontSize = 11.5, Foreground = Tx, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 2, 0, 0) });
        sp.Children.Add(new TextBlock { Text = M.JsRound(x.P).ToString("N0", CultureInfo.InvariantCulture), FontSize = 10.5, Foreground = Mu, TextAlignment = TextAlignment.Center });
        var b = new Button { Content = sp, Style = (Style)Application.Current.FindResource("Rz"), Background = B("#1A2338"), Padding = new Thickness(2, 7, 2, 7), Margin = new Thickness(2) };
        b.Click += (_, _) => a(); return b;
    }

    /// <summary>شبكة مشروبات: 4 أيقونات في كل صف</summary>
    public static UniformGrid DrinkGrid(IEnumerable<(DrinkDef D, Action A)> items)
    {
        var g = new UniformGrid { Columns = 4, Margin = new Thickness(0, 2, 0, 2) };
        foreach (var (d, a) in items) g.Children.Add(DrinkBtn(d, a));
        return g;
    }

    /// <summary>محرر تاريخ + وقت (24 ساعة) يرجع الوقت بالميلي ثانية.</summary>
    public class DtBox : StackPanel
    {
        readonly DatePicker dp = new(); readonly TextBox tb = U.Tb("", null, true);
        public DtBox(long ms)
        {
            Orientation = Orientation.Horizontal;
            var l = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;
            dp.SelectedDate = l.Date; dp.Margin = new Thickness(2); tb.Text = l.ToString("HH:mm"); tb.Width = 80;
            Children.Add(dp); Children.Add(tb);
        }
        public long? Value
        {
            get
            {
                if (dp.SelectedDate is not DateTime d || !ParseTime(tb.Text, out var t)) return null;
                return M.ToMs(d.Date + t);
            }
        }
    }

    public static void AddInv(StackPanel p, LogEntry e)
    {
        bool sale = e.Type == "sale";
        p.Children.Add(T("Red Zone Club", 18, Brushes.White, true, TextAlignment.Center));
        if (sale)
        {
            p.Children.Add(Muted("فاتورة — بيع مباشر", TextAlignment.Center));
            p.Children.Add(Muted($"{M.DKey(e.End)} — {M.Tm(e.End)}", TextAlignment.Center));
        }
        else
        {
            p.Children.Add(Muted($"فاتورة — جهاز {e.Dev}", TextAlignment.Center));
            p.Children.Add(Muted($"{M.DKey(e.Start)} — من {M.Tm(e.Start)} إلى {M.Tm(e.End)}", TextAlignment.Center));
            p.Children.Add(Muted("وقت اللعب"));
            if (e.T2 > 0 || e.T4 > 0)
            {
                if (e.T2 > 0) p.Children.Add(Line($"لاعبان ({M.Dur(e.T2)})", M.Fmt(e.C2)));
                if (e.T4 > 0) p.Children.Add(Line($"٤ لاعبين ({M.Dur(e.T4)})", M.Fmt(e.C4)));
                p.Children.Add(Line($"إجمالي وقت اللعب ({M.Dur(e.T2 + e.T4)})", M.Fmt(e.TimeCost), true));
            }
            else p.Children.Add(Line($"المدة ({M.Dur(e.End - e.Start)})", M.Fmt(e.TimeCost), true));
        }
        p.Children.Add(Muted("المشروبات"));
        if (e.Drinks.Count == 0 && e.DrinkTotal == 0) p.Children.Add(Muted("لا توجد مشروبات"));
        foreach (var x in e.Drinks) p.Children.Add(Line($"{Emo.Of(x.N)} {x.N} × {x.Q} ({M.Fmt(x.P)})", M.Fmt(x.P * x.Q)));
        if (e.DrinkTotal > 0 || e.Drinks.Count > 0) p.Children.Add(Line("مجموع المشروبات", M.Fmt(e.DrinkTotal), true));
        p.Children.Add(T("الإجمالي النهائي: " + M.Fmt(e.Total), 22, Tot, true, TextAlignment.Center));
    }

    /// <summary>اسم ملف آمن للتصدير</summary>
    public static string InvFile(LogEntry e) => e.Type == "sale" ? $"RedZone-sale-{M.DKey(e.End)}.xlsx" : $"RedZone-invoice-dev{e.Dev}-{M.DKey(e.End)}.xlsx";
}
