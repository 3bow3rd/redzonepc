using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace RedZoneClub;

public partial class MainWindow : Window
{
    string tab = "dev";
    long day0 = Rz.Now();
    string lview = "day";
    bool resetScroll;
    readonly Dictionary<int, int> saleCart = new();
    readonly List<Action<long>> updaters = new();
    readonly ScrollViewer sv = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly UniformGrid nav = new() { Rows = 1 };
    readonly Ellipse dot = new() { Width = 10, Height = 10, Margin = new Thickness(6, 0, 0, 0) };
    readonly TextBlock syncTb = U.T("", 12, U.B("#7FB2FF"));
    readonly Border banner = new() { Background = U.Ac, CornerRadius = new CornerRadius(16), Margin = new Thickness(10, 4, 10, 4), Padding = new Thickness(12), Visibility = Visibility.Collapsed };
    readonly Border secBanner = new() { Background = U.B("#D81B30"), Padding = new Thickness(14, 10, 14, 10), Visibility = Visibility.Collapsed };
    readonly TextBlock secTb = U.T("", 14, Brushes.White, true);
    readonly Border toastBox = new() { Background = U.B("#EE1A2338"), CornerRadius = new CornerRadius(12), Padding = new Thickness(18, 11, 18, 11), Margin = new Thickness(0, 0, 0, 96), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    readonly TextBlock toastTb = U.T("", 14);
    readonly DispatcherTimer toastTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => DarkTitleBar();
        Rz.Load();
        Background = new RadialGradientBrush
        {
            Center = new Point(0.5, 0), GradientOrigin = new Point(0.5, 0), RadiusX = 0.9, RadiusY = 0.7,
            GradientStops = new GradientStopCollection { new GradientStop(U.C("#2A0C16"), 0), new GradientStop(U.C("#070B14"), 1) }
        };

        var root = new Grid();
        for (int r = 0; r < 4; r++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Insert(2, new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        // الصفوف: 0 إنذار أمني، 1 الترويسة، 2 المحتوى، 3 منبه الوقت، 4 الشريط السفلي

        /* إنذار أمني من الجهاز الآخر */
        var secSp = new StackPanel();
        secSp.Children.Add(secTb);
        secSp.Children.Add(U.Btn("حسناً", "", () => { secBanner.Visibility = Visibility.Collapsed; secTb.Text = ""; }, 100));
        secBanner.Child = secSp;
        Grid.SetRow(secBanner, 0); root.Children.Add(secBanner);

        /* الترويسة: الشعار + الاسم متوهج */
        var logo = new Border
        {
            Width = 50, Height = 50, CornerRadius = new CornerRadius(14),
            Background = new ImageBrush(new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/logo.jpg"))) { Stretch = Stretch.UniformToFill },
            Effect = U.Glow("#FF2D45", 14, .4)
        };
        var wm = new TextBlock { FlowDirection = FlowDirection.LeftToRight, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0), Effect = U.Glow("#FF2D45", 22, .9) };
        wm.Inlines.Add(new Run("RED ZONE") { FontSize = 27, FontWeight = FontWeights.Black, Foreground = U.B("#FF3349") });
        wm.Inlines.Add(new Run(" CLUB") { FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
        var brand = new StackPanel { Orientation = Orientation.Horizontal };
        brand.Children.Add(logo); brand.Children.Add(wm);
        var sync = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        sync.Children.Add(syncTb); sync.Children.Add(dot);
        var hg = new Grid { Margin = new Thickness(14, 10, 14, 8) };
        hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hg.Children.Add(brand);
        Grid.SetColumn(sync, 1); hg.Children.Add(sync);
        var head = new Border { Child = hg, Background = U.B("#99070B14"), BorderBrush = U.B("#14FFFFFF"), BorderThickness = new Thickness(0, 0, 0, 1) };
        Grid.SetRow(head, 1); root.Children.Add(head);

        Grid.SetRow(sv, 2); root.Children.Add(sv);
        Grid.SetRow(banner, 3); root.Children.Add(banner);

        /* الشريط السفلي */
        var navHost = new Border
        {
            Child = nav, Background = U.B("#F20D1322"), CornerRadius = new CornerRadius(24), Padding = new Thickness(4),
            Margin = new Thickness(12, 6, 12, 12), MaxWidth = 760, BorderBrush = U.B("#1FFFFFFF"), BorderThickness = new Thickness(1),
            Effect = U.Glow("#000000", 24, .6)
        };
        Grid.SetRow(navHost, 4); root.Children.Add(navHost);

        toastBox.Child = toastTb; Grid.SetRowSpan(toastBox, 5); root.Children.Add(toastBox);
        Content = root;

        sv.SizeChanged += OnSvResize;
        Rz.ToastFn = ShowToast;
        Rz.AlertFn = ShowAlert;
        Rz.Changed += Render;
        Rz.Soft += UpdateSoft;
        toastTimer.Tick += (_, _) => { toastBox.Visibility = Visibility.Collapsed; toastTimer.Stop(); };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) Rz.SetOK = false; };

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => { Rz.Tick(); long n = Rz.Now(); foreach (var u in updaters.ToList()) u(n); };
        timer.Start();
        if (Rz.Cloud) Rz.StartCloud();
        Render();
        Closed += (_, _) => Rz.StopSound();
    }

    void ShowToast(string m) { toastTb.Text = m; toastBox.Visibility = Visibility.Visible; toastTimer.Stop(); toastTimer.Start(); }

    void ShowAlert(string msg)
    {
        secTb.Text = (secBanner.Visibility == Visibility.Visible && secTb.Text.Length > 0 ? secTb.Text + "\n\n" : "") + "🚨 " + msg;
        secBanner.Visibility = Visibility.Visible;
        if (Rz.Ringing == null) Rz.Play("tone_siren", false);
        Rz.Flash();
    }

    void Go(string t) { if (t != "set") Rz.SetOK = false; tab = t; resetScroll = true; Render(); }

    void Render()
    {
        updaters.Clear();
        double off = resetScroll ? 0 : sv.VerticalOffset;
        resetScroll = false;
        sv.Content = tab switch { "dev" => DevTab(), "sale" => SaleTab(), "log" => LogTab(), _ => SetTab() };
        sv.UpdateLayout(); sv.ScrollToVerticalOffset(off);
        nav.Children.Clear();
        nav.Children.Add(NavBtn("dev", "🎮", "الأجهزة"));
        nav.Children.Add(NavBtn("sale", "🛒", "البيع المباشر"));
        nav.Children.Add(NavBtn("log", "🧾", "السجل"));
        nav.Children.Add(NavBtn("set", "⚙️", "الإعدادات"));
        UpdateSoft();
    }

    Button NavBtn(string id, string ico, string label)
    {
        bool on = tab == id;
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = ico, FontSize = 19, HorizontalAlignment = HorizontalAlignment.Center, FontFamily = new FontFamily("Segoe UI Emoji") });
        sp.Children.Add(new TextBlock { Text = label, FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center, Foreground = on ? Brushes.White : U.Mu, FontWeight = on ? FontWeights.Bold : FontWeights.Normal, Margin = new Thickness(0, 2, 0, 0) });
        var b = new Button
        {
            Content = sp, Style = (Style)Application.Current.FindResource("Rz"), Padding = new Thickness(6, 7, 6, 7), Margin = new Thickness(2),
            Background = on ? new LinearGradientBrush(U.C("#FF3B52"), U.C("#B3122A"), 45) : Brushes.Transparent,
            BorderThickness = on ? new Thickness(1) : new Thickness(0)
        };
        if (on) b.Effect = U.Glow("#FF2D45", 14, .45);
        b.Click += (_, _) => Go(id);
        return b;
    }

    void UpdateSoft()
    {
        bool ok = Rz.Cloud && Rz.Live;
        dot.Fill = ok ? U.B("#22C55E") : U.B("#EF4444");
        dot.Effect = U.Glow(ok ? "#22C55E" : "#EF4444", 10, .8);
        syncTb.Text = "المزامنة";
        if (Rz.Ringing is int i)
        {
            var sp = new StackPanel();
            sp.Children.Add(U.T($"🔔 انتهى وقت الجهاز {i + 1}", 17, Brushes.White, true, TextAlignment.Center));
            sp.Children.Add(U.Row(U.Btn("▶ استمرار الحساب", "", () => Rz.Continue(i)), U.Btn("⏹ إيقاف", "", () => StopAt(i)), U.Btn("كتم الصوت", "", () => Rz.Dismiss(i))));
            banner.Child = sp; banner.Visibility = Visibility.Visible;
        }
        else banner.Visibility = Visibility.Collapsed;
    }

    /// <summary>شبكة المشروبات (4 في كل صف). pick يستلم رقم المشروب في قائمة الأسعار.</summary>
    static UIElement Drinks(Action<int> pick) =>
        U.DrinkGrid(Rz.S.Drinks.Select((x, j) => (x, (Action)(() => pick(j)))));

    void ExportInv(LogEntry e)
    {
        try { Rz.SaveXlsx(U.InvFile(e), M.InvoiceXlsx(e)); }
        catch (Exception ex) { Rz.Toast("تعذر إنشاء الملف: " + ex.Message); }
    }

    void ExportAll()
    {
        if (Rz.Logs.Count == 0) { Rz.Toast("لا توجد عمليات للتصدير"); return; }
        try { Rz.SaveXlsx($"RedZone-log-{M.DKey(Rz.Now())}.xlsx", M.LogXlsx(Rz.Logs.ToList())); }
        catch (Exception ex) { Rz.Toast("تعذر إنشاء الملف: " + ex.Message); }
    }

    /* ===================== شريط العنوان الأسود ===================== */
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

    void DarkTitleBar()
    {
        try
        {
            var h = new WindowInteropHelper(this).Handle;
            int on = 1, black = 0x000000, white = 0xFFFFFF;
            DwmSetWindowAttribute(h, 20, ref on, 4);      // وضع داكن (ويندوز 10/11)
            DwmSetWindowAttribute(h, 19, ref on, 4);      // ويندوز 10 القديم
            DwmSetWindowAttribute(h, 35, ref black, 4);   // لون الشريط أسود (ويندوز 11)
            DwmSetWindowAttribute(h, 36, ref white, 4);   // لون النص أبيض
            DwmSetWindowAttribute(h, 34, ref black, 4);   // لون الإطار
        }
        catch { }
    }

    /* ===================== الأجهزة ===================== */
    double devW = 0;

    UIElement DevTab()
    {
        const double BaseW = 350;                         // العرض الأساسي للبطاقة
        int n = Math.Max(1, Rz.S.Devices.Count);
        double avail = (sv.ActualWidth > 0 ? sv.ActualWidth : ActualWidth) - 36; // هامش + مكان شريط التمرير
        if (avail < BaseW) avail = BaseW;
        int cols = Math.Clamp((int)(avail / BaseW), 1, n);  // عدد الأعمدة
        double cell = avail / cols;                         // العرض المتاح لكل بطاقة (مع الهوامش)
        double scale = Math.Clamp(cell / (BaseW + 12), 1.0, 1.9); // تكبير البطاقة لملء الشاشة

        var root = new StackPanel { Margin = new Thickness(8) };
        var wp = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        for (int i = 0; i < Rz.S.Devices.Count; i++)
        {
            var c = Rz.S.Devices[i].On ? RunCard(i) : IdleCard(i);
            c.Width = BaseW;
            c.LayoutTransform = new ScaleTransform(scale, scale);
            wp.Children.Add(c);
        }
        root.Children.Add(wp);
        return root;
    }

    // إعادة ترتيب البطاقات عند تغيير حجم النافذة
    void OnSvResize(object? s, SizeChangedEventArgs e)
    {
        if (tab != "dev" || !e.WidthChanged) return;
        if (Math.Abs(e.NewSize.Width - devW) < 8) return;
        devW = e.NewSize.Width;
        Render();
    }

    Border IdleCard(int i)
    {
        if (!Rz.Forms.TryGetValue(i, out var f)) Rz.Forms[i] = f = new Form();
        var sp = new StackPanel();
        var h = new DockPanel(); h.Children.Add(U.Title($"🎮 جهاز {i + 1}"));
        var badge = new Border { Background = U.B("#2222C55E"), CornerRadius = new CornerRadius(99), Padding = new Thickness(12, 2, 12, 2), Child = U.T("متاح", 12, U.B("#4ADE80"), true) };
        DockPanel.SetDock(badge, Dock.Left); h.Children.Insert(0, badge); sp.Children.Add(h);
        sp.Children.Add(U.Row(U.Btn("👥 لاعبان", f.Mode == 2 ? "on" : "", () => { f.Mode = 2; Render(); }), U.Btn("👥 ٤ لاعبين", f.Mode == 4 ? "on" : "", () => { f.Mode = 4; Render(); })));
        sp.Children.Add(U.Muted("سعر الساعة: " + M.Fmt(f.Mode == 4 ? Rz.S.Rates.P4 : Rz.S.Rates.P2)));
        sp.Children.Add(U.Row(U.Btn("مفتوح", f.Type == "open" ? "on" : "", () => { f.Type = "open"; Render(); }), U.Btn("مدة", f.Type == "timed" ? "on" : "", () => { f.Type = "timed"; Render(); }), U.Btn("بمبلغ", f.Type == "amount" ? "on" : "", () => { f.Type = "amount"; Render(); })));
        if (f.Type != "open")
        {
            sp.Children.Add(U.Muted(f.Type == "timed" ? "المدة بالدقائق (60 = ساعة)" : "المبلغ بالليرة السورية"));
            sp.Children.Add(U.Tb(f.V, s => f.V = s, true));
        }
        sp.Children.Add(U.Btn("▶ بدء", "ok", () => { var e = Rz.Start(i, f); if (e != null) Rz.Toast(e); }));
        return U.Panel(sp);
    }

    Border RunCard(int i)
    {
        var d = Rz.S.Devices[i];
        var sp = new StackPanel();
        var h = new DockPanel(); h.Children.Add(U.Title($"🎮 جهاز {i + 1}"));
        sp.Children.Add(h);
        string tn = d.Type == "timed" ? $"مدة {M.JsRound(d.Mins)} د" : d.Type == "amount" ? "بمبلغ " + M.Fmt(d.Amount) : "مفتوح";
        sp.Children.Add(U.Muted($"{(d.Mode == 4 ? "٤ لاعبين" : "لاعبان")} · {tn} · {M.Tm(d.Start)}"));
        var timerTb = U.T("", 34, Brushes.White, true, TextAlignment.Center); sp.Children.Add(timerTb);
        var pb = new ProgressBar { Height = 6, Maximum = 1, Foreground = U.Ok, Background = U.B("#22FFFFFF"), BorderThickness = new Thickness(0), Margin = new Thickness(2, 2, 2, 6), Visibility = d.Type == "open" ? Visibility.Collapsed : Visibility.Visible };
        sp.Children.Add(pb);
        var reached = U.Row(U.Btn("▶ استمرار الحساب", "ok", () => Rz.Continue(i)), U.Btn("⏹ إيقاف", "bad", () => StopAt(i)));
        reached.Visibility = Visibility.Collapsed; sp.Children.Add(reached);
        sp.Children.Add(Drinks(j => { var dd = Rz.S.Drinks[j]; Rz.AddDrink(i, dd.N, dd.P); }));
        foreach (var x in d.Drinks.ToList())
        {
            var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var l = U.T($"{Emo.Of(x.N)} {x.N} × {x.Q}"); var p = U.T(M.Fmt(x.P * x.Q)); Grid.SetColumn(p, 1);
            var m = U.Btn("−", "", () => Rz.SubDrink(i, x.N), 40); Grid.SetColumn(m, 2);
            g.Children.Add(l); g.Children.Add(p); g.Children.Add(m); sp.Children.Add(g);
        }
        var totalTb = U.T("", 22, U.Tot, true, TextAlignment.Center); sp.Children.Add(totalTb);
        sp.Children.Add(U.Row(U.Btn(d.Mode == 4 ? "👥 تحويل إلى لاعبين" : "👥 تحويل إلى ٤ لاعبين", "", () => Rz.SwMode(i)), U.Btn("🔁 نقل لجهاز آخر", "", () => MoveDlg(i))));
        sp.Children.Add(U.Btn("✏️ تعديل الوقت / المبلغ", "", () => EditDlg(i)));
        sp.Children.Add(U.Btn("⏹ إنهاء وحساب", "bad", () => InvDlg(i, Rz.Now())));
        void Upd(long n)
        {
            if (i >= Rz.S.Devices.Count || !Rz.S.Devices[i].On) return;
            var dv = Rz.S.Devices[i]; var c = M.Compute(dv, n, Rz.S.Rates);
            timerTb.Text = c.Kind ? (c.Rem > 0 ? "متبقي " + M.Hms(c.Rem) : "انتهى الوقت +" + M.Hms(-c.Rem)) : M.Hms(c.El);
            if (c.Kind) pb.Value = double.IsFinite(c.Pr) ? Math.Clamp(c.Pr, 0, 1) : 0;
            reached.Visibility = c.Reached && !dv.Cont ? Visibility.Visible : Visibility.Collapsed;
            totalTb.Text = M.Fmt(c.Total);
        }
        updaters.Add(Upd); Upd(Rz.Now());
        return U.Panel(sp, true);
    }

    void StopAt(int i)
    {
        var d = Rz.S.Devices[i]; long n = Rz.Now(); var c = M.Compute(d, n, Rz.S.Rates);
        InvDlg(i, Math.Min(n, (long)(n + c.Rem)));
    }

    void MoveDlg(int i)
    {
        var free = Enumerable.Range(0, Rz.S.Devices.Count).Where(j => j != i && !Rz.S.Devices[j].On).ToList();
        if (free.Count == 0) { Rz.Toast("لا يوجد جهاز فارغ"); return; }
        U.Modal("نقل اللاعب", 320, (sp, win) =>
        {
            sp.Children.Add(U.Title($"نقل جهاز {i + 1} إلى:"));
            foreach (var j in free) { int jj = j; sp.Children.Add(U.Btn($"جهاز {j + 1}", "pri", () => { win.Close(); Rz.MoveTo(i, jj); })); }
        });
    }

    void InvDlg(int i, long end0)
    {
        Rz.Dismiss(i); long end = end0;
        U.Modal("فاتورة", 440, (sp, win) =>
        {
            void Fill()
            {
                sp.Children.Clear();
                var d = Rz.S.Devices[i]; if (!d.On) { win.Close(); return; }
                var e = M.EntryFrom(d, i, end, Rz.S.Rates);
                U.AddInv(sp, e);
                var sdt = new U.DtBox(e.Start); var edt = new U.DtBox(e.End);
                sp.Children.Add(U.Muted("وقت البدء")); sp.Children.Add(sdt);
                sp.Children.Add(U.Muted("وقت الانتهاء")); sp.Children.Add(edt);
                sp.Children.Add(U.Btn("✏️ تطبيق الأوقات", "", () =>
                {
                    var s = sdt.Value; var en = edt.Value;
                    if (s == null || en == null) { Rz.Toast("أدخل التاريخ والوقت بصيغة HH:mm"); return; }
                    var d2 = Rz.S.Devices[i];
                    if (en <= s || en <= d2.Seg) { Rz.Toast("الوقت غير صحيح: الانتهاء يجب أن يكون بعد البدء"); return; }
                    if (Math.Abs(s.Value - d2.Start) >= 60000) { var err = Rz.SetStart(i, s.Value); if (err != null) { Rz.Toast(err); return; } }
                    end = en.Value; Rz.SaveS(); Fill();
                }));
                sp.Children.Add(U.Btn("📊 تصدير الفاتورة (Excel)", "", () => ExportInv(e)));
                sp.Children.Add(U.Row(U.Btn("⏹ تأكيد وإنهاء الجلسة", "ok", () => { win.Close(); Rz.Finish(i, end); }), U.Btn("رجوع", "", () => win.Close())));
            }
            Fill();
        });
    }

    void EditDlg(int i)
    {
        var d0 = Rz.S.Devices[i]; if (!d0.On) return;
        string type = d0.Type; bool fromNow = true; string v = "";
        U.Modal($"تعديل جهاز {i + 1}", 440, (sp, win) =>
        {
            var sdt = new U.DtBox(d0.Start);
            void Fill()
            {
                sp.Children.Clear(); sp.Children.Add(U.Title($"✏️ تعديل جهاز {i + 1}"));
                sp.Children.Add(U.Muted("وقت البدء")); sp.Children.Add(sdt);
                sp.Children.Add(U.Muted("نوع الحساب"));
                sp.Children.Add(U.Row(U.Btn("مفتوح", type == "open" ? "on" : "", () => { type = "open"; Fill(); }), U.Btn("مدة", type == "timed" ? "on" : "", () => { type = "timed"; Fill(); }), U.Btn("بمبلغ", type == "amount" ? "on" : "", () => { type = "amount"; Fill(); })));
                if (type != "open")
                {
                    sp.Children.Add(U.Muted(type == "timed" ? "المدة بالدقائق (30 = نصف ساعة، 45 = ثلاثة أرباع)" : "المبلغ (مثلاً 5000 أو 7000)"));
                    sp.Children.Add(U.Tb(v, s => v = s, true));
                    sp.Children.Add(U.Row(U.Btn("يُحسب من الآن", fromNow ? "on" : "", () => { fromNow = true; Fill(); }), U.Btn("من بداية الجلسة", !fromNow ? "on" : "", () => { fromNow = false; Fill(); })));
                    sp.Children.Add(U.Muted(fromNow ? "يُضاف على ما مضى" : "إجمالي من بداية الجلسة"));
                }
                sp.Children.Add(U.Row(U.Btn("حفظ التعديل", "pri", () =>
                {
                    var st = sdt.Value; if (st == null) { Rz.Toast("وقت البدء غير صحيح (HH:mm)"); return; }
                    var err = Rz.ApplyEdit(i, st.Value, type, U.Num(v), fromNow);
                    if (err != null) Rz.Toast(err); else { win.Close(); Rz.Toast("تم تعديل الجلسة"); }
                }), U.Btn("إلغاء", "", () => win.Close())));
            }
            Fill();
        });
    }

    /* ===================== البيع المباشر ===================== */
    UIElement SaleTab()
    {
        var sp = new StackPanel();
        sp.Children.Add(U.Title("🛒 بيع مباشر للمشروبات"));
        sp.Children.Add(U.Muted("اضغط على المشروب لإضافته"));
        sp.Children.Add(Drinks(di => { saleCart[di] = saleCart.GetValueOrDefault(di) + 1; Render(); }));
        double tot = 0;
        foreach (var (idx, q) in saleCart.Where(k => k.Value > 0).ToList())
        {
            if (idx >= Rz.S.Drinks.Count) continue;
            var x = Rz.S.Drinks[idx]; tot += x.P * q; int jj = idx, qq = q;
            var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var l = U.T($"{Emo.Of(x.N)} {x.N} × {q}"); var p = U.T(M.Fmt(x.P * q)); Grid.SetColumn(p, 1);
            var m = U.Btn("−", "", () => { if (qq <= 1) saleCart.Remove(jj); else saleCart[jj] = qq - 1; Render(); }, 40); Grid.SetColumn(m, 2);
            g.Children.Add(l); g.Children.Add(p); g.Children.Add(m); sp.Children.Add(g);
        }
        sp.Children.Add(U.T(M.Fmt(tot), 24, U.Tot, true, TextAlignment.Center));
        sp.Children.Add(U.Row(U.Btn("🛒 تأكيد البيع", "ok", () => { if (Rz.Sale(saleCart)) { saleCart.Clear(); Render(); } }), U.Btn("مسح", "", () => { saleCart.Clear(); Render(); })));
        var wrap = new StackPanel { MaxWidth = 580, Margin = new Thickness(8) }; wrap.Children.Add(U.Panel(sp)); return wrap;
    }

    /* ===================== السجل ===================== */
    UIElement LogRow(LogEntry e)
    {
        bool sale = e.Type == "sale";
        string title = sale ? "🛒 بيع مباشر" : $"جهاز {e.Dev}" + (e.M0 != e.Mode && e.M0 != 0 ? $" ({e.M0}→{e.Mode})" : e.Mode == 4 ? " (4)" : " (2)");
        string ds = string.Join("، ", e.Drinks.Select(x => $"{x.N}×{x.Q}"));
        var g = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var l = new StackPanel();
        l.Children.Add(U.T(title + "   " + (sale ? M.Tm(e.End) : $"{M.Tm(e.Start)} – {M.Tm(e.End)}"), 14, Brushes.White, true));
        l.Children.Add(U.Muted((sale ? "" : $"لعب: {M.Fmt(e.TimeCost)} · ") + $"مشروبات: {M.Fmt(e.DrinkTotal)}" + (ds.Length > 0 ? "\n" + ds : "")));
        var r = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left }; Grid.SetColumn(r, 1);
        r.Children.Add(U.T(M.Fmt(e.Total), 15, U.Tot, true));
        r.Children.Add(U.Row(U.Btn("🧾", "", () => ViewDlg(e), 46), U.Btn("🗑", "", () => DelDlg(e), 46)));
        g.Children.Add(l); g.Children.Add(r); return g;
    }

    /// <summary>حذف عملية: يتطلب نفس الرمز السري المستخدم لقفل الإعدادات (إن كان مفعّلاً)</summary>
    void DelDlg(LogEntry e) => U.Modal("حذف عملية", 380, (sp, win) =>
    {
        if (Rz.LockHash == null)
        {
            sp.Children.Add(U.Title("🗑 حذف هذه العملية؟"));
            sp.Children.Add(U.Muted("لحماية الحذف برمز سري فعّل قفل الإعدادات من صفحة الإعدادات."));
            sp.Children.Add(U.Row(U.Btn("حذف", "bad", () => { win.Close(); Rz.DelLog(e.Id); }), U.Btn("إلغاء", "", () => win.Close())));
            return;
        }
        sp.Children.Add(U.Title("🔒 أدخل الرمز السري لحذف العملية"));
        var pw = U.Pw(); sp.Children.Add(pw);
        var msg = U.T("", 13, U.Ac); sp.Children.Add(msg);
        void Try()
        {
            if (Rz.TryPin(pw.Password, "delete")) { win.Close(); Rz.DelLog(e.Id); }
            else { msg.Text = "الرمز غير صحيح"; pw.Password = ""; }
        }
        pw.KeyDown += (_, k) => { if (k.Key == System.Windows.Input.Key.Enter) Try(); };
        sp.Children.Add(U.Row(U.Btn("🗑 حذف", "bad", Try), U.Btn("إلغاء", "", () => win.Close())));
        pw.Loaded += (_, _) => pw.Focus();
    });

    void ViewDlg(LogEntry e) => U.Modal("فاتورة", 440, (sp, win) =>
    {
        U.AddInv(sp, e);
        sp.Children.Add(U.Btn("📊 تصدير الفاتورة (Excel)", "", () => ExportInv(e)));
        sp.Children.Add(U.Row(U.Btn("🧾 نسخ الفاتورة", "", () => { try { Clipboard.SetText(M.InvText(e)); Rz.Toast("تم نسخ الفاتورة"); } catch { } }), U.Btn("إغلاق", "", () => win.Close())));
    });

    UIElement LogTab()
    {
        var root = new StackPanel { Margin = new Thickness(8), MaxWidth = 900 };
        root.Children.Add(U.Row(U.Btn("📅 حسب اليوم", lview == "day" ? "on" : "", () => { lview = "day"; Render(); }), U.Btn("📈 السجل الكامل", lview == "all" ? "on" : "", () => { lview = "all"; Render(); })));
        if (lview == "day")
        {
            string k = M.DKey(day0); var l = Rz.Logs.Where(x => x.Day == k).OrderByDescending(x => x.End).ToList();
            var dp = new DatePicker { SelectedDate = DateTimeOffset.FromUnixTimeMilliseconds(day0).LocalDateTime.Date, Margin = new Thickness(2) };
            dp.SelectedDateChanged += (_, _) => { if (dp.SelectedDate is DateTime d) { day0 = M.ToMs(d.Date.AddHours(12)); Render(); } };
            var s = new StackPanel(); s.Children.Add(dp);
            s.Children.Add(U.T("الإجمالي: " + M.Fmt(l.Sum(x => x.Total)), 20, U.Tot, true, TextAlignment.Center));
            s.Children.Add(U.Muted($"لعب: {M.Fmt(l.Sum(x => x.TimeCost))} · مشروبات: {M.Fmt(l.Sum(x => x.DrinkTotal))} · عمليات: {l.Count}", TextAlignment.Center));
            root.Children.Add(U.Panel(s));
            var p = new StackPanel(); if (l.Count == 0) p.Children.Add(U.Muted("لا توجد عمليات"));
            foreach (var x in l) p.Children.Add(LogRow(x));
            root.Children.Add(U.Panel(p));
        }
        else
        {
            var days = Rz.Logs.GroupBy(x => x.Day).OrderByDescending(g => g.Key).ToList();
            var tools = new StackPanel();
            tools.Children.Add(U.Row(U.Btn("➕ جلسة قديمة", "pri", OldDlg), U.Btn("📊 تصدير السجل (Excel)", "ok", ExportAll)));
            root.Children.Add(U.Panel(tools));
            var s = new StackPanel();
            s.Children.Add(U.T("إجمالي كل الأيام: " + M.Fmt(Rz.Logs.Sum(x => x.Total)), 20, U.Tot, true, TextAlignment.Center));
            s.Children.Add(U.Muted($"{days.Count} يوم عمل · {Rz.Logs.Count} عملية", TextAlignment.Center));
            root.Children.Add(U.Panel(s));
            foreach (var g in days)
            {
                var p = new StackPanel(); p.Children.Add(U.Line(g.Key, M.Fmt(g.Sum(x => x.Total)), true));
                foreach (var x in g.OrderByDescending(q => q.End)) p.Children.Add(LogRow(x));
                root.Children.Add(U.Panel(p));
            }
        }
        return root;
    }

    /* ===================== جلسة قديمة (من السجل الكامل) ===================== */
    void OldDlg()
    {
        int dev = 1, mode = 2; DateTime date = DateTime.Today; string st = "", en = "", amt = ""; bool byAmt = true;
        var cart = new Dictionary<int, int>();
        U.Modal("➕ إضافة جلسة سابقة", 470, (sp, win) =>
        {
            var sum = U.T("", 13.5, U.Mu);

            (string? Err, long S, long E, double Cost, double Dr) Eval()
            {
                double dr = cart.Where(k => k.Value > 0 && k.Key < Rz.S.Drinks.Count).Sum(k => Rz.S.Drinks[k.Key].P * k.Value);
                if (!U.ParseTime(st, out var ts)) return ("أدخل وقت البدء (مثال 14:30)", 0, 0, 0, dr);
                long s0 = M.ToMs(date.Date + ts), e0; double cost;
                if (byAmt)
                {
                    var a = U.Num(amt);
                    if (a == null || a <= 0) return ("أدخل مبلغ اللعب", s0, 0, 0, dr);
                    double ms = a.Value / Rz.RatePerMs(mode);
                    if (double.IsNaN(ms) || double.IsInfinity(ms)) return ("سعر الساعة غير صحيح (من الإعدادات)", s0, 0, 0, dr);
                    e0 = s0 + (long)Math.Round(ms); cost = a.Value;
                }
                else
                {
                    if (!U.ParseTime(en, out var te)) return ("أدخل وقت الانتهاء (مثال 16:00)", s0, 0, 0, dr);
                    e0 = M.ToMs(date.Date + te); if (e0 <= s0) e0 += 86400000L;
                    cost = M.Up((e0 - s0) * Rz.RatePerMs(mode));
                }
                return (null, s0, e0, cost, dr);
            }

            void Upd()
            {
                var r = Eval();
                sum.Text = r.Err != null ? r.Err : $"ينتهي {M.Tm(r.E)} · المدة {M.Dur(r.E - r.S)}\nاللعب {M.Fmt(r.Cost)} + المشروبات {M.Fmt(r.Dr)} = {M.Fmt(r.Cost + r.Dr)}";
            }

            void Save()
            {
                var r = Eval();
                if (r.Err != null) { Rz.Toast(r.Err); return; }
                var dl = cart.Where(k => k.Value > 0 && k.Key < Rz.S.Drinks.Count)
                    .Select(k => new DrinkQty { N = Rz.S.Drinks[k.Key].N, P = Rz.S.Drinks[k.Key].P, Q = k.Value }).ToList();
                var x = Rz.AddOld(dev, mode, r.S, r.E, r.Cost, dl);
                win.Close();
                day0 = x.Start; lview = "all";
                Rz.Toast("تمت إضافة الجلسة: " + M.Fmt(x.Total));
                Go("log");
            }

            void Fill()
            {
                sp.Children.Clear();
                sp.Children.Add(U.Title("➕ إضافة جلسة سابقة"));
                sp.Children.Add(U.Muted("سجّل جلسة انتهت دون تشغيل المؤقت"));
                sp.Children.Add(U.Muted("الجهاز"));
                var cb = new ComboBox { Margin = new Thickness(2) };
                for (int n = 1; n <= Rz.S.Devices.Count; n++) cb.Items.Add(n);
                cb.SelectedItem = Math.Min(dev, Rz.S.Devices.Count);
                cb.SelectionChanged += (_, _) => { if (cb.SelectedItem is int v) dev = v; };
                sp.Children.Add(cb);
                sp.Children.Add(U.Row(U.Btn("لاعبان", mode == 2 ? "on" : "", () => { mode = 2; Fill(); }), U.Btn("٤ لاعبين", mode == 4 ? "on" : "", () => { mode = 4; Fill(); })));
                sp.Children.Add(U.Muted("التاريخ"));
                var dp = new DatePicker { SelectedDate = date, Margin = new Thickness(2) };
                dp.SelectedDateChanged += (_, _) => { if (dp.SelectedDate is DateTime dd) { date = dd; Upd(); } };
                sp.Children.Add(dp);
                sp.Children.Add(U.Muted("وقت البدء (مثال 14:30 أو 2:30 PM)"));
                sp.Children.Add(U.Tb(st, s => { st = s; Upd(); }, true));
                sp.Children.Add(U.Row(U.Btn("💰 حسب المبلغ", byAmt ? "on" : "", () => { byAmt = true; Fill(); }), U.Btn("⏱ حسب وقت الانتهاء", !byAmt ? "on" : "", () => { byAmt = false; Fill(); })));
                if (byAmt)
                {
                    sp.Children.Add(U.Muted("مبلغ اللعب — يُحسب وقت الانتهاء تلقائياً من سعر الساعة"));
                    sp.Children.Add(U.Tb(amt, s => { amt = s; Upd(); }, true));
                }
                else
                {
                    sp.Children.Add(U.Muted("وقت الانتهاء (مثال 16:00) — يُحسب المبلغ تلقائياً"));
                    sp.Children.Add(U.Tb(en, s => { en = s; Upd(); }, true));
                }
                sp.Children.Add(U.Muted("المشروبات (اختياري) — اضغط لإضافة ما تم أخذه"));
                sp.Children.Add(Drinks(di => { cart[di] = cart.GetValueOrDefault(di) + 1; Fill(); }));
                foreach (var (idx, q) in cart.Where(k => k.Value > 0).ToList())
                {
                    if (idx >= Rz.S.Drinks.Count) continue;
                    var x = Rz.S.Drinks[idx]; int jj = idx, qq = q;
                    var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var l = U.T($"{Emo.Of(x.N)} {x.N} × {q}"); var p = U.T(M.Fmt(x.P * q)); Grid.SetColumn(p, 1);
                    var m = U.Btn("−", "", () => { if (qq <= 1) cart.Remove(jj); else cart[jj] = qq - 1; Fill(); }, 40); Grid.SetColumn(m, 2);
                    g.Children.Add(l); g.Children.Add(p); g.Children.Add(m); sp.Children.Add(g);
                }
                sp.Children.Add(sum);
                sp.Children.Add(U.Row(U.Btn("حفظ الجلسة", "pri", Save), U.Btn("إلغاء", "", () => win.Close())));
                Upd();
            }
            Fill();
        });
    }

    /* ===================== الإعدادات ===================== */
    UIElement SetTab()
    {
        if (Rz.LockHash != null && !Rz.SetOK) return LockPanel();
        var root = new StackPanel { Margin = new Thickness(8), MaxWidth = 700 };
        // الأجهزة
        var a = new StackPanel(); a.Children.Add(U.Line("🎮 الأجهزة", $"{Rz.S.Devices.Count} أجهزة", true));
        a.Children.Add(U.Row(U.Btn("➕ إضافة جهاز", "pri", Rz.AddDev), U.Btn("🗑 حذف آخر جهاز", "", () => { if (U.Confirm($"حذف جهاز {Rz.S.Devices.Count}؟")) Rz.DelDev(); })));
        root.Children.Add(U.Panel(a));
        // الأسعار
        string p2 = M.JsRound(Rz.S.Rates.P2).ToString(), p4 = M.JsRound(Rz.S.Rates.P4).ToString();
        var r = new StackPanel(); r.Children.Add(U.Title("أسعار الساعة"));
        r.Children.Add(U.Muted("لاعبان")); r.Children.Add(U.Tb(p2, v => p2 = v, true));
        r.Children.Add(U.Muted("٤ لاعبين")); r.Children.Add(U.Tb(p4, v => p4 = v, true));
        r.Children.Add(U.Muted("يُقرَّب مبلغ الوقت دائماً إلى مضاعفات 500 ل.س (الحد الأدنى 500)."));
        r.Children.Add(U.Btn("حفظ", "pri", () => { Rz.SaveRates(U.Num(p2) ?? 0, U.Num(p4) ?? 0); Rz.Toast("تم الحفظ"); }));
        root.Children.Add(U.Panel(r));
        // المشروبات
        string dn = "", dpr = "";
        var dr = new StackPanel(); dr.Children.Add(U.Title("المشروبات والمنتجات"));
        for (int j = 0; j < Rz.S.Drinks.Count; j++)
        {
            int jj = j; var x = Rz.S.Drinks[j];
            var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var b = U.Btn("حذف", "", () => Rz.DelDrinkDef(jj), 70); Grid.SetColumn(b, 1);
            g.Children.Add(U.T($"{Emo.Of(x.N)} {x.N} — {M.Fmt(x.P)}")); g.Children.Add(b); dr.Children.Add(g);
        }
        dr.Children.Add(U.Muted("اسم المنتج (يُختار له إيموجي تلقائياً: نودلز 🍜، شاي 🍵، قهوة ☕ ...)")); dr.Children.Add(U.Tb("", v => dn = v));
        dr.Children.Add(U.Muted("السعر")); dr.Children.Add(U.Tb("", v => dpr = v, true));
        dr.Children.Add(U.Btn("إضافة", "pri", () => { var pv = U.Num(dpr); if (dn.Trim().Length > 0 && pv != null && pv >= 0) Rz.AddDrinkDef(dn.Trim(), pv.Value); }));
        root.Children.Add(U.Panel(dr));
        // المنبه (زر يفتح نافذة)
        string tn = Rz.ToneId == "custom" ? "ملف: " + Rz.CustomName : (Rz.Tones.Where(x => x.Id == Rz.ToneId).Select(x => x.Name).FirstOrDefault() ?? "");
        var t = new StackPanel(); t.Children.Add(U.Line("🎵 المنبه", tn, true));
        t.Children.Add(U.Btn("🎵 خيارات المنبه", "pri", ToneDlg));
        root.Children.Add(U.Panel(t));
        // القفل
        var k = new StackPanel(); k.Children.Add(U.Line("🔒 قفل الإعدادات", Rz.LockHash != null ? "مفعّل" : "غير مفعّل", true));
        k.Children.Add(U.Muted("يُطلب الرمز السري عند فتح الإعدادات وعند حذف أي عملية من السجل. ويصل إنذار للجهاز الآخر عند إدخال رمز خاطئ أو تغييره."));
        var kr = new List<UIElement> { U.Btn(Rz.LockHash != null ? "تغيير الرمز السري" : "تفعيل القفل برمز سري", "pri", PinDlg) };
        if (Rz.LockHash != null) kr.Add(U.Btn("إيقاف القفل", "bad", () => { Rz.PinOff(); Render(); Rz.Toast("تم إيقاف القفل"); }));
        k.Children.Add(U.Row(kr.ToArray()));
        root.Children.Add(U.Panel(k));
        // المزامنة
        var sy = new StackPanel(); sy.Children.Add(U.Line("☁️ المزامنة", Rz.Cloud ? (Rz.Live ? "✅ متصل" : "⏳ غير متصل الآن") : "غير مرتبط", true));
        sy.Children.Add(U.Btn("☁️ ربط / إعدادات المزامنة", "pri", SyncDlg));
        root.Children.Add(U.Panel(sy));
        return root;
    }

    void ToneDlg() => U.Modal("🎵 خيارات المنبه", 440, (sp, win) =>
    {
        void Fill()
        {
            sp.Children.Clear();
            sp.Children.Add(U.Title("🎵 نغمة التنبيه"));
            sp.Children.Add(U.Muted("يعمل المنبه ما دام التطبيق مفتوحاً (يمكن تصغيره). تومض أيقونة شريط المهام عند انتهاء الوقت."));
            var list = Rz.Tones.Select(x => (x.Id, x.Name)).ToList();
            if (Rz.CustomPath.Length > 0) list.Add(("custom", "ملف: " + Rz.CustomName));
            foreach (var (id, nm) in list)
            {
                var idd = id; bool on = Rz.ToneId == id;
                var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var pl = U.Btn("▶", "", () => Rz.Play(idd, false), 50); Grid.SetColumn(pl, 1);
                g.Children.Add(U.Btn((on ? "✓ " : "") + nm, on ? "on" : "", () => { Rz.SelTone(idd); Fill(); })); g.Children.Add(pl); sp.Children.Add(g);
            }
            sp.Children.Add(U.Btn("➕ إضافة نغمة من الملفات", "", () =>
            {
                var dlg = new OpenFileDialog { Filter = "ملفات صوت|*.mp3;*.wav;*.wma;*.m4a;*.aac" };
                if (dlg.ShowDialog() == true) { Rz.AddCustomTone(dlg.FileName); Fill(); }
            }));
            sp.Children.Add(U.Btn("تم", "pri", () => win.Close()));
        }
        Fill();
        win.Closed += (_, _) => { Rz.StopSound(); Render(); };
    });

    UIElement LockPanel()
    {
        var sp = new StackPanel();
        sp.Children.Add(U.T("🔒 الإعدادات مقفلة", 16, Brushes.White, true, TextAlignment.Center));
        sp.Children.Add(U.Muted("أدخل الرمز السري للدخول", TextAlignment.Center));
        var pw = U.Pw(); sp.Children.Add(pw);
        var msg = U.T("", 13, U.Ac); sp.Children.Add(msg);
        void Try() { if (Rz.TryPin(pw.Password, "settings")) { Rz.SetOK = true; Render(); } else { msg.Text = "الرمز غير صحيح"; pw.Password = ""; } }
        pw.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Try(); };
        sp.Children.Add(U.Btn("دخول", "pri", Try));
        sp.Children.Add(U.Btn("نسيت الرمز؟", "", () =>
        {
            if (Rz.Rt == null) { Rz.Toast("لا يمكن استرجاع الرمز بدون ربط المزامنة. يمكنك حذف مجلد البيانات %AppData%\\RedZoneClub (تُحذف البيانات المحلية)."); return; }
            Rz.SendAlert("forgot", "🔐 طلب استرجاع/تغيير الرمز السري (نسيت الرمز)");
            U.Modal("استرجاع الدخول", 400, (m, win) =>
            {
                m.Children.Add(U.Title("استرجاع الدخول")); m.Children.Add(U.Muted("أدخل كلمة سر قاعدة البيانات (Database secret) لإلغاء القفل"));
                var sec = U.Pw(); m.Children.Add(sec);
                m.Children.Add(U.Btn("إلغاء القفل", "pri", () =>
                {
                    if (Rz.Rt?.Sec == sec.Password.Trim()) { Rz.PinOff(); Rz.SetOK = true; win.Close(); Render(); Rz.Toast("تم إلغاء القفل"); }
                    else { Rz.SendAlert("wrong", "⚠️ محاولة استرجاع الرمز بكلمة سر قاعدة بيانات خاطئة"); Rz.Toast("كلمة السر غير صحيحة"); }
                }));
            });
        }));
        var wrap = new StackPanel { MaxWidth = 380, Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Center }; wrap.Children.Add(U.Panel(sp)); return wrap;
    }

    void PinDlg()
    {
        if (Rz.LockHash != null) Rz.SendAlert("request", "🔐 طلب تغيير الرمز السري");
        U.Modal("رمز سري جديد", 380, (sp, win) =>
        {
            sp.Children.Add(U.Title("رمز سري جديد")); sp.Children.Add(U.Muted("من 4 إلى 8 أرقام"));
            var a = U.Pw(); var b = U.Pw(); sp.Children.Add(a); sp.Children.Add(U.Muted("تأكيد الرمز")); sp.Children.Add(b);
            var msg = U.T("", 13, U.Ac); sp.Children.Add(msg);
            sp.Children.Add(U.Btn("حفظ", "pri", () =>
            {
                string x = a.Password;
                if (x.Length < 4 || x.Length > 8 || !x.All(char.IsDigit)) msg.Text = "الرمز يجب أن يكون من 4 إلى 8 أرقام";
                else if (x != b.Password) msg.Text = "الرمزان غير متطابقين";
                else { Rz.SetPin(x); win.Close(); Render(); Rz.Toast("تم حفظ الرمز السري"); }
            }));
        });
    }

    void SyncDlg() => U.Modal("المزامنة", 460, (sp, win) =>
    {
        sp.Children.Add(U.Title("☁️ المزامنة (Realtime Database)"));
        sp.Children.Add(U.Muted("الحالة: " + (Rz.Cloud ? (Rz.Live ? "✅ متصل" : "⏳ مرتبط (غير متصل الآن)") : "غير مرتبط")));
        sp.Children.Add(U.Muted("رابط قاعدة البيانات (Realtime Database ← Data):"));
        var url = U.Tb(Rz.Rt?.Url ?? "", null, true); sp.Children.Add(url);
        sp.Children.Add(U.Muted("كلمة السر (Project settings ← Service accounts ← Database secrets):"));
        var sec = U.Tb(Rz.Rt?.Sec ?? "", null, true); sp.Children.Add(sec);
        var msg = U.T("", 13, U.Ac); sp.Children.Add(msg);
        sp.Children.Add(U.Btn("ربط واختبار الاتصال", "pri", () =>
        {
            if (url.Text.Trim().Length == 0 || sec.Text.Trim().Length == 0) { msg.Text = "أدخل رابط قاعدة البيانات وكلمة السر"; return; }
            msg.Text = "جاري اختبار الاتصال…";
            Rz.Link(url.Text, sec.Text, err => { if (err == null) { win.Close(); Render(); Rz.Toast("تم الربط"); } else msg.Text = err; });
        }));
        if (Rz.Cloud) sp.Children.Add(U.Btn("قطع الربط", "bad", () => { Rz.Unlink(); win.Close(); Render(); }));
    });
}
