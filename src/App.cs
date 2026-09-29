using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace BulsOpti
{
    static class AppMain
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0)
            {
                string c = args[0].TrimStart('/','-').ToLower();
                if (c == "screenshot") return Shot(args);
                int r = Program.RunCli(args);
                if (r != -999) return r;
            }
            var app = new Application();
            var shell = new Shell();
            app.Run(shell.BuildWindow());
            return 0;
        }

        static int Shot(string[] args)
        {
            string outp = args.Length > 1 ? args[1] : "ui.png";
            string mode = args.Length > 2 ? args[2].ToLower() : "home";
            var shell = new Shell();
            var root = shell.BuildRoot();
            shell.ShowViewSync(mode);
            double w = 980, h = 660;
            root.Measure(new Size(w, h));
            root.Arrange(new Rect(0, 0, w, h));
            root.UpdateLayout();
            var rtb = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(root);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(outp)) enc.Save(fs);
            Console.WriteLine("saved " + outp);
            return 0;
        }
    }

    class Chk { public Tweak T; public bool On; }
    class CleanChk { public CleanCat Cat; public bool On; public TextBlock SizeLbl; public Border Ind; public TextBlock Mark; }
    class Bar { public Grid Root; public Border Fill; }
    class NavItem { public Border Box; public TextBlock Icon, Label; public string Key; }

    class Shell
    {
        // ---------- Премиум-палитра ----------
        const string C_Base   = "#0C0C11"; // фон окна
        const string C_Side   = "#0A0A0E"; // боковое меню
        const string C_Card   = "#15151C"; // карточки
        const string C_Card2  = "#1B1B23"; // приподнятые/hover
        const string C_Line   = "#26262F"; // рамки
        const string C_Text   = "#F1F1F5";
        const string C_Muted  = "#9C9CAB";
        const string C_Faint  = "#61616D";
        const string C_Accent = "#7C5CF6"; // бренд-акцент
        const string C_AccHi  = "#8E72F8";
        const string C_AccSoft= "#1E1830"; // подложка выбранного пункта
        const string C_Ok     = "#5FC77E";
        const string C_Bad    = "#E0616E";
        const string C_Pro    = "#F3B24A"; // PRO — янтарь
        const string C_ProHi  = "#FFC65E";
        const string C_Close  = "#C43D4B";

        // MDL2 глифы
        const string G_Home = "";
        const string G_Search = "";
        const string G_Gear = "";
        const string G_Star = "";
        const string G_Lock = "";
        const string G_Check = "";
        const string G_Min = "";
        const string G_Close = "";
        const string G_Bolt = "";
        const string G_Clean = "";

        static Color Col(string h) { return (Color)ColorConverter.ConvertFromString(h); }
        static SolidColorBrush B(string h) { var b = new SolidColorBrush(Col(h)); b.Freeze(); return b; }
        static Brush Grad(string a, string b, double ang) { var g = new LinearGradientBrush(Col(a), Col(b), ang); g.Freeze(); return g; }

        List<Tweak> tweaks;
        List<Chk> chks = new List<Chk>();
        List<Action> chkUpdaters = new List<Action>();
        List<NavItem> nav = new List<NavItem>();

        Window win;
        Border TitleBar, sideHeader;
        Grid contentHost;
        TextBlock pageTitle, pageSub;
        string curView = "home";

        // home
        Grid homeView; TextBlock homeSummary, homeStatus, tCpu, tRam, tDisk; Bar homeBar; TextBox logBox;
        // scan
        Grid scanView; TextBlock scanSummary, scanHw; StackPanel scanCol1, scanCol2; Bar scanBar;
        // tweaks / pro
        Grid tweaksView, proView;
        // clean
        Grid cleanView; StackPanel cleanList; TextBlock cleanHint; Border cleanBtn;
        List<CleanChk> cleanChks = new List<CleanChk>();

        List<UIElement> interactive = new List<UIElement>();

        const string URL_Site = "https://bulsopti.com";
        const string URL_App = "https://www.bulsopti.com/app";
        const string URL_Discord = "https://discord.gg/buls";

        TextBlock totalFreedLbl;

        static ImageSource Img(string name)
        {
            var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("BulsOpti." + name);
            if (s == null) return null;
            var bi = new BitmapImage();
            bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = s; bi.EndInit(); bi.Freeze();
            s.Dispose();
            return bi;
        }

        // ======================= ОКНО =======================
        public Window BuildWindow()
        {
            var root = BuildRoot();
            win = new Window {
                Width = 980, Height = 660, WindowStyle = WindowStyle.None, AllowsTransparency = true,
                Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen, Title = "BUL$ Opti",
                Content = root, FontFamily = new FontFamily("Segoe UI"), SnapsToDevicePixels = true,
                MinWidth = 980, MinHeight = 660
            };
            try { win.Icon = Img("bull.png"); } catch {}
            MouseButtonEventHandler dragHandler = (s,e)=>{ if (e.ButtonState == MouseButtonState.Pressed) { try { win.DragMove(); } catch {} } };
            if (TitleBar != null) TitleBar.MouseLeftButtonDown += dragHandler;
            if (sideHeader != null) sideHeader.MouseLeftButtonDown += dragHandler;
            return win;
        }

        public FrameworkElement BuildRoot()
        {
            tweaks = Program.BuildTweaks();

            var outer = new Grid();
            var shell = new Border {
                Margin = new Thickness(10), CornerRadius = new CornerRadius(14),
                Background = B(C_Base), BorderBrush = B(C_Line), BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 28, ShadowDepth = 0, Opacity = 0.5 },
                ClipToBounds = true
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(212) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var side = BuildSidebar(); Grid.SetColumn(side, 0); grid.Children.Add(side);
            var right = BuildRight();  Grid.SetColumn(right, 1); grid.Children.Add(right);

            shell.Child = grid; outer.Children.Add(shell);

            // построить страницы
            homeView   = BuildHome();
            scanView   = BuildScan();
            tweaksView = BuildTweaks();
            cleanView  = BuildClean();
            proView    = BuildPro();
            contentHost.Children.Add(homeView);
            contentHost.Children.Add(scanView);
            contentHost.Children.Add(tweaksView);
            contentHost.Children.Add(cleanView);
            contentHost.Children.Add(proView);

            Log("BUL$ Opti — бесплатный мини-тул. Готов к работе.");
            SetView("home");
            return outer;
        }

        // ======================= БОКОВОЕ МЕНЮ =======================
        Border BuildSidebar()
        {
            var side = new Border { Background = B(C_Side), BorderBrush = B(C_Line), BorderThickness = new Thickness(0,0,1,0), CornerRadius = new CornerRadius(14,0,0,14) };
            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });               // бренд
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // навигация
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });               // PRO-карточка

            // бренд
            sideHeader = new Border { Padding = new Thickness(18,18,18,10) };
            var brand = new StackPanel { Orientation = Orientation.Horizontal };
            brand.Children.Add(new Image { Source = Img("bull.png"), Height = 30, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,10,0) });
            var bt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            bt.Children.Add(new TextBlock { Text = "BUL$ Opti", Foreground = B(C_Text), FontSize = 15, FontWeight = FontWeights.Bold });
            var pill = new Border { Background = B("#16161D"), BorderBrush = B(C_Line), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(6,1,6,1), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,3,0,0) };
            pill.Child = new TextBlock { Text = "FREE · мини-тул", Foreground = B(C_Muted), FontSize = 9.5, FontWeight = FontWeights.SemiBold };
            bt.Children.Add(pill);
            brand.Children.Add(bt);
            sideHeader.Child = brand;
            Grid.SetRow(sideHeader, 0); g.Children.Add(sideHeader);

            // навигация
            var navPanel = new StackPanel { Margin = new Thickness(12,10,12,0) };
            navPanel.Children.Add(NavBtn(G_Home,   "Главная",          "home"));
            navPanel.Children.Add(NavBtn(G_Search, "Проверка ПК",      "scan"));
            navPanel.Children.Add(NavBtn(G_Gear,   "Настройка",        "tweaks"));
            navPanel.Children.Add(NavBtn(G_Clean,  "Очистка",          "clean"));
            navPanel.Children.Add(NavBtn(G_Star,   "BUL$ Opti PRO",    "pro"));
            Grid.SetRow(navPanel, 1); g.Children.Add(navPanel);

            // PRO мини-карточка
            var proCard = new Border { Margin = new Thickness(12,0,12,14), CornerRadius = new CornerRadius(10), Padding = new Thickness(14,12,14,14),
                Background = B("#161016"), BorderBrush = B("#3A2E14"), BorderThickness = new Thickness(1) };
            var pc = new StackPanel();
            var pcHead = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,0,0,6) };
            pcHead.Children.Add(new TextBlock { Text = G_Star, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12, Foreground = B(C_Pro), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,7,0) });
            pcHead.Children.Add(new TextBlock { Text = "Отдельное приложение", Foreground = B(C_Pro), FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            pc.Children.Add(pcHead);
            pc.Children.Add(new TextBlock { Text = "BUL$ Opti PRO — платная полноценная программа. Мониторинг, профили и тонкая настройка под железо.", Foreground = B(C_Muted), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,10) });
            pc.Children.Add(ProButton("Узнать о PRO", ()=> SetView("pro")));
            proCard.Child = pc;
            Grid.SetRow(proCard, 2); g.Children.Add(proCard);

            side.Child = g;
            return side;
        }

        Border NavBtn(string icon, string label, string key)
        {
            var box = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(12,9,12,9), Margin = new Thickness(0,0,0,4), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var ic = new TextBlock { Text = icon, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 15, Foreground = B(C_Muted), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,12,0), Width = 18, TextAlignment = TextAlignment.Center };
            var lb = new TextBlock { Text = label, Foreground = B(C_Muted), FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(ic); row.Children.Add(lb);
            box.Child = row;
            var item = new NavItem { Box = box, Icon = ic, Label = lb, Key = key };
            nav.Add(item);
            box.MouseEnter += (s,e)=>{ if (curView != key) box.Background = B("#131318"); };
            box.MouseLeave += (s,e)=>{ if (curView != key) box.Background = Brushes.Transparent; };
            box.MouseLeftButtonUp += (s,e)=> SetView(key);
            return box;
        }

        // ======================= ПРАВАЯ ЧАСТЬ =======================
        Border BuildRight()
        {
            var r = new Border { Background = B(C_Base), CornerRadius = new CornerRadius(0,14,14,0) };
            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) }); // топбар
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            TitleBar = BuildTopbar(); Grid.SetRow(TitleBar, 0); g.Children.Add(TitleBar);

            contentHost = new Grid { Margin = new Thickness(24,4,24,20) };
            Grid.SetRow(contentHost, 1); g.Children.Add(contentHost);

            r.Child = g;
            return r;
        }

        Border BuildTopbar()
        {
            var bar = new Border { Padding = new Thickness(24,0,14,0) };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            pageTitle = new TextBlock { Text = "Главная", Foreground = B(C_Text), FontSize = 18, FontWeight = FontWeights.Bold };
            pageSub = new TextBlock { Text = "", Foreground = B(C_Faint), FontSize = 11.5, Margin = new Thickness(0,1,0,0) };
            titles.Children.Add(pageTitle); titles.Children.Add(pageSub);
            Grid.SetColumn(titles, 0); g.Children.Add(titles);

            var ctrls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            ctrls.Children.Add(WinBtn(G_Min, "#1D1D26", false, ()=>{ if (win != null) win.WindowState = WindowState.Minimized; }));
            ctrls.Children.Add(WinBtn(G_Close, C_Close, true, ()=>{ if (win != null) win.Close(); }));
            Grid.SetColumn(ctrls, 1); g.Children.Add(ctrls);

            bar.Child = g;
            return bar;
        }
        Border WinBtn(string glyph, string hover, bool danger, Action onClick)
        {
            var b = new Border { Width = 38, Height = 32, CornerRadius = new CornerRadius(7), Background = Brushes.Transparent, Margin = new Thickness(2,0,2,0), Cursor = Cursors.Hand };
            b.Child = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 11, Foreground = B(C_Muted), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            b.MouseEnter += (s,e)=>{ b.Background = B(hover); ((TextBlock)b.Child).Foreground = danger ? Brushes.White : B(C_Text); };
            b.MouseLeave += (s,e)=>{ b.Background = Brushes.Transparent; ((TextBlock)b.Child).Foreground = B(C_Muted); };
            b.MouseLeftButtonDown += (s,e)=> e.Handled = true;
            b.MouseLeftButtonUp += (s,e)=>{ if (onClick != null) onClick(); };
            return b;
        }

        // ======================= НАВИГАЦИЯ / VIEW =======================
        void SetView(string key)
        {
            curView = key;
            if (homeView != null)   homeView.Visibility   = key == "home"   ? Visibility.Visible : Visibility.Collapsed;
            if (scanView != null)   scanView.Visibility   = key == "scan"   ? Visibility.Visible : Visibility.Collapsed;
            if (tweaksView != null) tweaksView.Visibility = key == "tweaks" ? Visibility.Visible : Visibility.Collapsed;
            if (cleanView != null)  cleanView.Visibility  = key == "clean"  ? Visibility.Visible : Visibility.Collapsed;
            if (proView != null)    proView.Visibility    = key == "pro"    ? Visibility.Visible : Visibility.Collapsed;

            foreach (var n in nav)
            {
                bool on = n.Key == key;
                n.Box.Background = on ? B(C_AccSoft) : Brushes.Transparent;
                n.Icon.Foreground = on ? B(C_AccHi) : B(C_Muted);
                n.Label.Foreground = on ? B(C_Text) : B(C_Muted);
            }

            if (key == "home")   { SetTitle("Главная", "Состояние вашего ПК"); RefreshHome(false); }
            if (key == "scan")   { SetTitle("Проверка ПК", "Что уже оптимально, а что можно улучшить"); RefreshScan(false); }
            if (key == "tweaks") SetTitle("Настройка", "Выбери, что применить, или возьми пресет");
            if (key == "clean")  { SetTitle("Очистка", "Поддержка ПК — убираем кэш и хлам"); RefreshClean(false); }
            if (key == "pro")    SetTitle("BUL$ Opti PRO", "Отдельное платное приложение — полный набор под твой ПК");
        }
        void SetTitle(string t, string s) { if (pageTitle != null) pageTitle.Text = t; if (pageSub != null) pageSub.Text = s; }

        public void ShowViewSync(string key)
        {
            string k = key == "settings" ? "tweaks" : (key == "main" ? "home" : key);
            SetView(k);
            if (k == "home") RefreshHome(true);
            if (k == "scan") RefreshScan(true);
            if (k == "clean") RefreshClean(true);
        }

        // ======================= ГЛАВНАЯ =======================
        Grid BuildHome()
        {
            var v = new Grid { Visibility = Visibility.Collapsed };
            v.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // hero
            v.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // статы
            v.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // быстрая чистка
            v.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // pro-полоса
            v.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // журнал

            // hero
            var hero = Card();
            hero.Margin = new Thickness(0,0,0,14);
            var hg = new Grid { Margin = new Thickness(22,20,22,20) };
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var hl = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            hl.Children.Add(new TextBlock { Text = "СОСТОЯНИЕ СИСТЕМЫ", Foreground = B(C_Faint), FontSize = 10.5, FontWeight = FontWeights.SemiBold });
            homeSummary = new TextBlock { Text = "сканирую…", Foreground = B(C_Text), FontSize = 26, FontWeight = FontWeights.Bold, Margin = new Thickness(0,4,0,0) };
            hl.Children.Add(homeSummary);
            homeBar = BuildBar(); homeBar.Root.Margin = new Thickness(0,12,24,0); homeBar.Root.MaxWidth = 360; homeBar.Root.HorizontalAlignment = HorizontalAlignment.Left; homeBar.Root.Width = 360;
            hl.Children.Add(homeBar.Root);
            homeStatus = new TextBlock { Text = "", Foreground = B(C_Ok), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,10,0,0) };
            hl.Children.Add(homeStatus);
            hl.Children.Add(new TextBlock { Text = "При оптимизации также ставятся обои BUL$ Opti (убрать — Параметры → Персонализация).", Foreground = B(C_Faint), FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxWidth = 380, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,12,0,0) });
            Grid.SetColumn(hl, 0); hg.Children.Add(hl);

            var hr = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            hr.Children.Add(PrimaryButton("Оптимизировать", 220, ()=> Run(tweaks.Where(t=>t.DefaultOn).ToList(), true, true)));
            var chk = LinkBtn("Проверить систему →", C_Muted, ()=> SetView("scan")); chk.HorizontalAlignment = HorizontalAlignment.Center; chk.Margin = new Thickness(0,12,0,0);
            hr.Children.Add(chk);
            Grid.SetColumn(hr, 1); hg.Children.Add(hr);

            hero.Child = hg;
            Grid.SetRow(hero, 0); v.Children.Add(hero);

            // статы
            var stats = new Grid { Margin = new Thickness(0,0,0,14) };
            stats.ColumnDefinitions.Add(new ColumnDefinition());
            stats.ColumnDefinitions.Add(new ColumnDefinition());
            stats.ColumnDefinitions.Add(new ColumnDefinition());
            tCpu = null; tRam = null; tDisk = null;
            var c1 = StatTile("ПРОЦЕССОР", out tCpu);  c1.Margin = new Thickness(0,0,7,0);
            var c2 = StatTile("ПАМЯТЬ", out tRam);     c2.Margin = new Thickness(7,0,7,0);
            var c3 = StatTile("ДИСК C:", out tDisk);   c3.Margin = new Thickness(7,0,0,0);
            Grid.SetColumn(c1,0); Grid.SetColumn(c2,1); Grid.SetColumn(c3,2);
            stats.Children.Add(c1); stats.Children.Add(c2); stats.Children.Add(c3);
            Grid.SetRow(stats, 1); v.Children.Add(stats);

            // быстрая чистка
            var clean = new Border { CornerRadius = new CornerRadius(12), Background = B(C_Card), BorderBrush = B(C_Line), BorderThickness = new Thickness(1), Margin = new Thickness(0,0,0,14) };
            var cg = new Grid { Margin = new Thickness(18,13,16,13) };
            cg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var brm = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(9), Background = B(C_AccSoft), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,12,0) };
            brm.Child = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 15, Foreground = B(C_AccHi), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(brm, 0); cg.Children.Add(brm);
            var ct = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            ct.Children.Add(new TextBlock { Text = "Быстрая чистка", Foreground = B(C_Text), FontSize = 13.5, FontWeight = FontWeights.SemiBold });
            totalFreedLbl = new TextBlock { Text = "Освобождено за всё время: 0 МБ", Foreground = B(C_Muted), FontSize = 11.5, Margin = new Thickness(0,2,0,0) };
            ct.Children.Add(totalFreedLbl);
            Grid.SetColumn(ct, 1); cg.Children.Add(ct);
            var cbtn = SmallButton("Очистить", ()=> QuickClean()); cbtn.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(cbtn, 2); cg.Children.Add(cbtn);
            clean.Child = cg;
            Grid.SetRow(clean, 2); v.Children.Add(clean);

            // pro-полоса
            var pro = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(18,14,16,14), Margin = new Thickness(0,0,0,14),
                Background = B("#17110A"), BorderBrush = B("#3A2C12"), BorderThickness = new Thickness(1) };
            var pg = new Grid();
            pg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var pl = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var plh = new StackPanel { Orientation = Orientation.Horizontal };
            plh.Children.Add(new TextBlock { Text = G_Star, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13, Foreground = B(C_Pro), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,8,0) });
            plh.Children.Add(new TextBlock { Text = "Есть отдельное приложение — BUL$ Opti PRO", Foreground = B(C_Text), FontSize = 13.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            pl.Children.Add(plh);
            pl.Children.Add(new TextBlock { Text = "Платная полноценная программа: мониторинг, игровые профили, драйверы и тонкая настройка под твой ПК. Это не эта версия — отдельный продукт.", Foreground = B(C_Muted), FontSize = 11.5, Margin = new Thickness(0,4,0,0), TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(pl, 0); pg.Children.Add(pl);
            var pbtn = ProButton("Смотреть PRO →", ()=> SetView("pro")); pbtn.VerticalAlignment = VerticalAlignment.Center; pbtn.Margin = new Thickness(14,0,0,0);
            Grid.SetColumn(pbtn, 1); pg.Children.Add(pbtn);
            pro.Child = pg;
            Grid.SetRow(pro, 3); v.Children.Add(pro);

            // журнал
            var logCard = Card();
            var lg = new Grid { Margin = new Thickness(16,12,16,12) };
            lg.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            lg.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            lg.Children.Add(new TextBlock { Text = "ЖУРНАЛ", FontSize = 9.5, FontWeight = FontWeights.SemiBold, Foreground = B(C_Faint) });
            logBox = new TextBox { IsReadOnly = true, Background = Brushes.Transparent, Foreground = B(C_Muted), BorderThickness = new Thickness(0), FontFamily = new FontFamily("Consolas"), FontSize = 11.5, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.NoWrap, IsTabStop = false, Margin = new Thickness(0,6,0,0) };
            Grid.SetRow(logBox, 1); lg.Children.Add(logBox);
            logCard.Child = lg;
            Grid.SetRow(logCard, 4); v.Children.Add(logCard);

            return v;
        }

        Border SmallButton(string text, Action onClick)
        {
            var b = new Border { Height = 36, CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand, Padding = new Thickness(18,0,18,0), Background = B(C_Card2), BorderBrush = B(C_Line), BorderThickness = new Thickness(1) };
            b.Child = new TextBlock { Text = text, Foreground = B(C_Text), FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            b.MouseEnter += (s,e)=> b.Background = B("#24242E");
            b.MouseLeave += (s,e)=> b.Background = B(C_Card2);
            b.MouseLeftButtonUp += (s,e)=>{ if (onClick != null) onClick(); };
            interactive.Add(b);
            return b;
        }

        void QuickClean()
        {
            Busy(true);
            var th = new Thread(()=>{
                long freed = Program.CleanNow(Log);
                long mb = freed / 1024 / 1024;
                Program.AddTotalFreedMb(mb);
                Log("Быстрая чистка: освобождено ~" + mb + " МБ");
                Status(mb > 0 ? ("✔ Очищено ~" + mb + " МБ") : "✔ Уже чисто");
                Busy(false);
                if (contentHost != null) contentHost.Dispatcher.BeginInvoke((Action)(()=> UpdateTotalFreed()));
            });
            th.IsBackground = true; th.Start();
        }
        void UpdateTotalFreed()
        {
            if (totalFreedLbl == null) return;
            long t = Program.GetTotalFreedMb();
            totalFreedLbl.Text = "Освобождено за всё время: " + FmtMb(t);
        }
        static string FmtMb(long mb) { return mb >= 1024 ? (Math.Round(mb / 1024.0, 1) + " ГБ") : (mb + " МБ"); }

        Border StatTile(string label, out TextBlock value)
        {
            var c = Card();
            var sp = new StackPanel { Margin = new Thickness(16,14,16,14) };
            sp.Children.Add(new TextBlock { Text = label, Foreground = B(C_Faint), FontSize = 10, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,6) });
            value = new TextBlock { Text = "…", Foreground = B(C_Text), FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            sp.Children.Add(value);
            c.Child = sp;
            return c;
        }

        void RefreshHome(bool sync)
        {
            if (homeSummary == null) return;
            UpdateTotalFreed();
            Action<ScanData> fill = d =>
            {
                homeSummary.Text = "Оптимизировано " + d.Ok + " из " + d.Tot;
                homeSummary.Foreground = B(d.Ok >= d.Tot ? C_Ok : C_Text);
                SetBar(homeBar, d.Tot > 0 ? (double)d.Ok / d.Tot : 0, d.Ok >= d.Tot);
                if (string.IsNullOrEmpty(homeStatus.Text))
                {
                    int need = d.Tot - d.Ok;
                    homeStatus.Text = need > 0 ? (need + " пункт(ов) можно улучшить — нажми «Оптимизировать»") : "Всё оптимально ✔";
                    homeStatus.Foreground = B(need > 0 ? C_Muted : C_Ok);
                }
                if (d.Hw.Length >= 4) { tCpu.Text = ShortCpu(d.Hw[0]); tRam.Text = d.Hw[1]; tDisk.Text = d.Hw[3].Replace("Диск C: ", ""); }
            };
            Gather(sync, fill);
        }

        static string ShortCpu(string s)
        {
            s = s.Replace("(R)", "").Replace("(TM)", "").Replace(" CPU", "");
            int core = s.IndexOf("-Core", StringComparison.OrdinalIgnoreCase);
            if (core > 0) { int j = core; while (j > 0 && char.IsDigit(s[j-1])) j--; if (j > 0 && s[j-1] == ' ') j--; if (j > 0) s = s.Substring(0, j); }
            foreach (var cut in new[]{ " Processor", " with Radeon Graphics", " with Graphics" })
            {
                int i = s.IndexOf(cut, StringComparison.OrdinalIgnoreCase);
                if (i > 0) s = s.Substring(0, i);
            }
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            return s.Trim();
        }

        // ======================= ПРОВЕРКА =======================
        Grid BuildScan()
        {
            var v = new Grid { Visibility = Visibility.Collapsed };
            v.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // сводка
            v.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // список
            v.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // железо + кнопки

            var top = Card(); top.Margin = new Thickness(0,0,0,14);
            var tg = new StackPanel { Margin = new Thickness(20,16,20,16) };
            scanSummary = new TextBlock { Text = "сканирую…", Foreground = B(C_Text), FontSize = 15, FontWeight = FontWeights.SemiBold };
            tg.Children.Add(scanSummary);
            scanBar = BuildBar(); scanBar.Root.Margin = new Thickness(0,12,0,0);
            tg.Children.Add(scanBar.Root);
            top.Child = tg;
            Grid.SetRow(top, 0); v.Children.Add(top);

            var listCard = Card();
            var cols = new Grid { Margin = new Thickness(18,14,18,14) };
            cols.ColumnDefinitions.Add(new ColumnDefinition());
            cols.ColumnDefinitions.Add(new ColumnDefinition());
            scanCol1 = new StackPanel { Margin = new Thickness(0,0,14,0) };
            scanCol2 = new StackPanel { Margin = new Thickness(14,0,0,0) };
            Grid.SetColumn(scanCol1, 0); Grid.SetColumn(scanCol2, 1);
            cols.Children.Add(scanCol1); cols.Children.Add(scanCol2);
            listCard.Child = cols;
            Grid.SetRow(listCard, 1); v.Children.Add(listCard);

            var bottom = new Grid { Margin = new Thickness(0,14,0,0) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            scanHw = new TextBlock { Text = "", Foreground = B(C_Faint), FontSize = 11, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,14,0) };
            Grid.SetColumn(scanHw, 0); bottom.Children.Add(scanHw);
            var opt = PrimaryButton("Оптимизировать всё", 200, ()=>{ SetView("home"); Run(tweaks.Where(t=>t.DefaultOn).ToList(), true, true); });
            Grid.SetColumn(opt, 1); bottom.Children.Add(opt);
            Grid.SetRow(bottom, 2); v.Children.Add(bottom);

            return v;
        }

        void RefreshScan(bool sync)
        {
            if (scanSummary == null) return;
            Action<ScanData> fill = d =>
            {
                scanSummary.Text = "Оптимизировано " + d.Ok + " из " + d.Tot + " проверок";
                scanSummary.Foreground = B(d.Ok >= d.Tot ? C_Ok : C_Text);
                SetBar(scanBar, d.Tot > 0 ? (double)d.Ok / d.Tot : 0, d.Ok >= d.Tot);
                scanCol1.Children.Clear(); scanCol2.Children.Clear();
                int half = (d.Rows.Count + 1) / 2;
                for (int i = 0; i < d.Rows.Count; i++)
                    (i < half ? scanCol1 : scanCol2).Children.Add(ScanRow(d.Rows[i].Key, d.Rows[i].Value));
                // PRO-намёк
                scanCol2.Children.Add(LockedRow("Тонкая настройка под твоё железо"));
                var parts = new List<string>(d.Hw); parts.Add("Мусор в Temp: ~" + d.Junk + " МБ");
                scanHw.Text = string.Join("   ·   ", parts);
            };
            Gather(sync, fill);
        }

        FrameworkElement ScanRow(string title, bool ok)
        {
            var row = new Grid { Margin = new Thickness(2,4,0,4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = B(ok ? C_Ok : C_Bad), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,10,0) };
            var lbl = new TextBlock { Text = title, Foreground = B(ok ? C_Muted : C_Text), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var st = new TextBlock { Text = ok ? "ок" : "улучшить", Foreground = B(ok ? C_Ok : C_Bad), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8,0,2,0) };
            Grid.SetColumn(dot,0); Grid.SetColumn(lbl,1); Grid.SetColumn(st,2);
            row.Children.Add(dot); row.Children.Add(lbl); row.Children.Add(st);
            return row;
        }
        FrameworkElement LockedRow(string title)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2,4,0,4) };
            row.Children.Add(new TextBlock { Text = G_Lock, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 11, Foreground = B(C_Pro), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,8,0) });
            row.Children.Add(new TextBlock { Text = title, Foreground = B(C_Faint), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = "  PRO", Foreground = B(C_Pro), FontSize = 10.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            return row;
        }

        // ======================= НАСТРОЙКА =======================
        Grid BuildTweaks()
        {
            var v = new Grid { Visibility = Visibility.Collapsed };
            v.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // пресеты
            v.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // список
            v.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // кнопки

            var presets = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,0,0,12) };
            presets.Children.Add(new TextBlock { Text = "Пресет:", Foreground = B(C_Faint), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,10,0) });
            presets.Children.Add(Chip("Рекомендуемые", ()=> ApplyPreset(null)));
            presets.Children.Add(Chip("Игры", ()=> ApplyPreset(new[]{"power-plan","game-dvr-off","windowed-flip-vrr","game-mode","net-throttling","nic-power-off","delivery-p2p-off","flush-dns","system-responsiveness"})));
            presets.Children.Add(Chip("Чистка", ()=> ApplyPreset(new[]{"temp-clean","thumb-cache","storage-sense","menu-delay","ui-animations-off","startup-delay-0","no-promo-apps"})));
            presets.Children.Add(Chip("Снять всё", ()=> ApplyPreset(new string[0])));
            Grid.SetRow(presets, 0); v.Children.Add(presets);

            var listCard = Card();
            var cols = new Grid { Margin = new Thickness(20,16,20,16) };
            cols.ColumnDefinitions.Add(new ColumnDefinition());
            cols.ColumnDefinitions.Add(new ColumnDefinition());
            var col1 = new StackPanel { Margin = new Thickness(0,0,16,0) };
            var col2 = new StackPanel { Margin = new Thickness(16,0,0,0) };
            var leftGroups = new HashSet<string> { "Питание и CPU", "Графика и GPU", "Сеть" };
            string g1 = null, g2 = null;
            foreach (var t in tweaks)
            {
                if (leftGroups.Contains(t.Group)) { if (t.Group != g1) { g1 = t.Group; col1.Children.Add(GroupHead(g1)); } col1.Children.Add(CheckRow(t)); }
                else { if (t.Group != g2) { g2 = t.Group; col2.Children.Add(GroupHead(g2)); } col2.Children.Add(CheckRow(t)); }
            }
            col2.Children.Add(GroupHead("Расширенное"));
            col2.Children.Add(LockedRow("Тайминги памяти, привязка ядер, IRQ"));
            Grid.SetColumn(col1, 0); Grid.SetColumn(col2, 1);
            cols.Children.Add(col1); cols.Children.Add(col2);
            listCard.Child = cols;
            Grid.SetRow(listCard, 1); v.Children.Add(listCard);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,14,0,0) };
            var apply = PrimaryButton("Применить выбранное", 210, ()=> Run(chks.Where(c=>c.On).Select(c=>c.T).ToList(), true, true));
            var revert = GhostButton("Откатить оптимизации", ()=> Run(chks.Where(c=>c.On).Select(c=>c.T).ToList(), false, false));
            apply.Margin = new Thickness(0,0,10,0);
            btns.Children.Add(apply); btns.Children.Add(revert);
            Grid.SetRow(btns, 2); v.Children.Add(btns);

            return v;
        }
        TextBlock GroupHead(string g)
        { return new TextBlock { Text = g.ToUpper(), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = B(C_Faint), Margin = new Thickness(2,12,0,5) }; }

        FrameworkElement CheckRow(Tweak t)
        {
            var chk = new Chk { T = t, On = t.DefaultOn };
            var row = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(8,5,8,5), Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = t.Benefit + "\nРиск: " + t.Risk };
            var sp = new Grid();
            sp.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sp.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var ind = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1.3), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,10,0) };
            var mark = new TextBlock { Text = G_Check, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            ind.Child = mark;
            var lbl = new TextBlock { Text = t.Title, Foreground = B(C_Text), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(ind, 0); Grid.SetColumn(lbl, 1);
            sp.Children.Add(ind); sp.Children.Add(lbl); row.Child = sp;
            Action upd = ()=>{
                if (chk.On) { ind.Background = B(C_Accent); ind.BorderBrush = B(C_Accent); mark.Visibility = Visibility.Visible; }
                else { ind.Background = B("#1C1C24"); ind.BorderBrush = B("#3C3C48"); mark.Visibility = Visibility.Hidden; }
            };
            upd(); chks.Add(chk); chkUpdaters.Add(upd);
            row.MouseEnter += (s,e)=> row.Background = B(C_Card2);
            row.MouseLeave += (s,e)=> row.Background = Brushes.Transparent;
            row.MouseLeftButtonUp += (s,e)=>{ chk.On = !chk.On; upd(); };
            return row;
        }

        Border Chip(string text, Action onClick)
        {
            var b = new Border { Height = 30, CornerRadius = new CornerRadius(15), Background = B(C_Card), BorderBrush = B(C_Line), BorderThickness = new Thickness(1), Padding = new Thickness(15,0,15,0), Margin = new Thickness(0,0,8,0), Cursor = Cursors.Hand };
            b.Child = new TextBlock { Text = text, Foreground = B(C_Muted), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            b.MouseEnter += (s,e)=>{ b.Background = B(C_Card2); ((TextBlock)b.Child).Foreground = B(C_Text); };
            b.MouseLeave += (s,e)=>{ b.Background = B(C_Card); ((TextBlock)b.Child).Foreground = B(C_Muted); };
            b.MouseLeftButtonUp += (s,e)=>{ if (onClick != null) onClick(); };
            interactive.Add(b);
            return b;
        }
        void ApplyPreset(string[] ids)
        {
            foreach (var c in chks) c.On = ids == null ? c.T.DefaultOn : ids.Contains(c.T.Id);
            foreach (var u in chkUpdaters) u();
        }

        // ======================= ОЧИСТКА =======================
        Grid BuildClean()
        {
            var v = new Grid { Visibility = Visibility.Collapsed };
            v.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            v.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            v.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            v.Children.Add(new TextBlock { Text = "Убираем кэш и хлам с ПК. Всё безопасно — удаляются только временные файлы и кэш.", Foreground = B(C_Muted), FontSize = 12.5, Margin = new Thickness(2,0,0,12) });

            var listCard = Card();
            cleanList = new StackPanel { Margin = new Thickness(10,8,10,8) };
            listCard.Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = cleanList, Padding = new Thickness(0,0,4,0) };
            Grid.SetRow(listCard, 1); v.Children.Add(listCard);

            var bottom = new Grid { Margin = new Thickness(0,14,0,0) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cleanHint = new TextBlock { Text = "", Foreground = B(C_Faint), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(cleanHint, 0); bottom.Children.Add(cleanHint);
            cleanBtn = PrimaryButton("Очистить выбранное", 210, ()=> RunClean());
            Grid.SetColumn(cleanBtn, 1); bottom.Children.Add(cleanBtn);
            Grid.SetRow(bottom, 2); v.Children.Add(bottom);

            return v;
        }

        void RefreshClean(bool sync)
        {
            if (cleanList == null) return;
            cleanList.Children.Clear(); cleanChks.Clear();
            foreach (var c in Program.BuildCleanCats()) cleanList.Children.Add(CleanRow(c));
            if (cleanHint != null) cleanHint.Text = "считаю размеры…";
            Action done = ()=>{ if (cleanHint != null) cleanHint.Text = "отметь и нажми «Очистить выбранное»"; };
            if (sync)
            {
                foreach (var cc in cleanChks) { long sz; try { sz = cc.Cat.Size(); } catch { sz = 0; } cc.SizeLbl.Text = sz < 0 ? "" : FmtBytes(sz); }
                done();
            }
            else
            {
                var snap = new List<CleanChk>(cleanChks);
                var th = new Thread(()=>{
                    foreach (var cc in snap)
                    {
                        long sz; try { sz = cc.Cat.Size(); } catch { sz = 0; }
                        var lbl = cc.SizeLbl; long szF = sz;
                        lbl.Dispatcher.BeginInvoke((Action)(()=> lbl.Text = szF < 0 ? "" : FmtBytes(szF)));
                    }
                    if (cleanHint != null) cleanHint.Dispatcher.BeginInvoke((Action)done);
                });
                th.IsBackground = true; th.Start();
            }
        }

        FrameworkElement CleanRow(CleanCat cat)
        {
            var cc = new CleanChk { Cat = cat, On = cat.DefaultOn };
            var row = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10,9,12,9), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var ind = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1.3), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,12,0) };
            var mark = new TextBlock { Text = G_Check, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            ind.Child = mark;
            var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            txt.Children.Add(new TextBlock { Text = cat.Title, Foreground = B(C_Text), FontSize = 12.5, FontWeight = FontWeights.SemiBold });
            txt.Children.Add(new TextBlock { Text = cat.Note, Foreground = B(C_Faint), FontSize = 10.5, Margin = new Thickness(0,1,0,0) });
            var size = new TextBlock { Text = "…", Foreground = B(C_Muted), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            cc.SizeLbl = size; cc.Ind = ind; cc.Mark = mark; cleanChks.Add(cc);
            Grid.SetColumn(ind,0); Grid.SetColumn(txt,1); Grid.SetColumn(size,2);
            g.Children.Add(ind); g.Children.Add(txt); g.Children.Add(size);
            row.Child = g;
            Action upd = ()=>{
                if (cc.On) { ind.Background = B(C_Accent); ind.BorderBrush = B(C_Accent); mark.Visibility = Visibility.Visible; }
                else { ind.Background = B("#1C1C24"); ind.BorderBrush = B("#3C3C48"); mark.Visibility = Visibility.Hidden; }
            };
            upd();
            row.MouseEnter += (s,e)=> row.Background = B(C_Card2);
            row.MouseLeave += (s,e)=> row.Background = Brushes.Transparent;
            row.MouseLeftButtonUp += (s,e)=>{ cc.On = !cc.On; upd(); };
            return row;
        }

        void RunClean()
        {
            var sel = cleanChks.Where(c=>c.On).ToList();
            if (sel.Count == 0) { Log("Для очистки ничего не выбрано."); return; }
            Busy(true);
            var th = new Thread(()=>{
                long freed = 0;
                Log(""); Log("=== ОЧИСТКА (" + sel.Count + ") ===");
                foreach (var c in sel) { Log("[~] " + c.Cat.Title); try { freed += c.Cat.Clean(Log); } catch (Exception ex) { Log("    ! " + ex.Message); } }
                long mb = freed / 1024 / 1024;
                Program.AddTotalFreedMb(mb);
                Log("Очистка завершена. Освобождено ~" + mb + " МБ");
                Status(mb > 0 ? ("✔ Очищено ~" + mb + " МБ") : "✔ Уже чисто");
                Busy(false);
                if (contentHost != null) contentHost.Dispatcher.BeginInvoke((Action)(()=>{ RefreshClean(false); UpdateTotalFreed(); }));
            });
            th.IsBackground = true; th.SetApartmentState(ApartmentState.STA); th.Start();
        }
        static string FmtBytes(long b)
        {
            if (b <= 0) return "0 МБ";
            double mb = b / 1024.0 / 1024.0;
            if (mb >= 1024) return Math.Round(mb / 1024.0, 1) + " ГБ";
            if (mb >= 1) return Math.Round(mb) + " МБ";
            return Math.Max(1, (int)Math.Round(b / 1024.0)) + " КБ";
        }

        // ======================= PRO =======================
        Grid BuildPro()
        {
            var v = new Grid { Visibility = Visibility.Collapsed };
            v.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            v.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var card = new Border { CornerRadius = new CornerRadius(14), Background = B(C_Card), BorderBrush = B("#3A2C12"), BorderThickness = new Thickness(1) };
            var g = new Grid { Margin = new Thickness(26,22,26,22) };
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var head = new StackPanel { Margin = new Thickness(0,0,0,16) };
            var ht = new StackPanel { Orientation = Orientation.Horizontal };
            ht.Children.Add(new TextBlock { Text = G_Star, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 20, Foreground = B(C_Pro), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,10,0) });
            ht.Children.Add(new TextBlock { Text = "BUL$ Opti PRO", Foreground = B(C_Text), FontSize = 22, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(ht);
            head.Children.Add(new TextBlock { Text = "BUL$ Opti PRO — это отдельное, полноценное приложение (платное). Оно не заменяет и не «разблокирует» этот мини-тул, а ставится отдельно и даёт полный контроль над ПК: постоянный мониторинг, профили под игры и глубокую настройку под твоё железо.", Foreground = B(C_Muted), FontSize = 12.5, Margin = new Thickness(0,8,0,0), TextWrapping = TextWrapping.Wrap });
            Grid.SetRow(head, 0); g.Children.Add(head);

            // витрина: инструменты + оффер
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var toolsCard = new Border { CornerRadius = new CornerRadius(10), Background = B("#121218"), BorderBrush = B(C_Line), BorderThickness = new Thickness(1), Padding = new Thickness(18,15,18,15), Margin = new Thickness(0,0,8,0) };
            var tv = new StackPanel();
            tv.Children.Add(new TextBlock { Text = "10 ИНСТРУМЕНТОВ В ОДНОМ ПРИЛОЖЕНИИ", Foreground = B(C_Faint), FontSize = 10.5, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,12) });
            var tcols = new Grid();
            tcols.ColumnDefinitions.Add(new ColumnDefinition());
            tcols.ColumnDefinitions.Add(new ColumnDefinition());
            var tc1 = new StackPanel { Margin = new Thickness(0,0,10,0) };
            var tc2 = new StackPanel { Margin = new Thickness(10,0,0,0) };
            string[][] tools = new[]{
                new[]{"ИИ-оптимизация","советует точечно под железо"},
                new[]{"BIOS-анализатор","80+ настроек с путями"},
                new[]{"89 системных твиков","каждый Safe / Caution"},
                new[]{"Профили под игры","CS2, Rust, Valorant, Apex…"},
                new[]{"Раскладка ядер","Affinity под X3D и чиплеты"},
                new[]{"Бенчмарк","10 тестов, замер до/после"},
                new[]{"Real-Time мониторинг","FPS, температуры, графики"},
                new[]{"Обновление драйверов","из проверенных источников"},
                new[]{"Очистка системы","кэш и мусор с диска"},
                new[]{"Бэкап и откат","точка восстановления"}
            };
            for (int i = 0; i < tools.Length; i++) (i < 5 ? tc1 : tc2).Children.Add(ToolItem(tools[i][0], tools[i][1]));
            Grid.SetColumn(tc1,0); Grid.SetColumn(tc2,1);
            tcols.Children.Add(tc1); tcols.Children.Add(tc2);
            tv.Children.Add(tcols);
            toolsCard.Child = tv;
            Grid.SetColumn(toolsCard, 0); body.Children.Add(toolsCard);

            var offer = new Border { CornerRadius = new CornerRadius(10), Background = B("#17120B"), BorderBrush = B("#3A2C12"), BorderThickness = new Thickness(1), Padding = new Thickness(20,18,20,18), Margin = new Thickness(8,0,0,0) };
            var ov = new StackPanel();
            ov.Children.Add(new TextBlock { Text = "РАЗОВЫЙ КЛЮЧ · БЕЗ ПОДПИСОК", Foreground = B(C_Pro), FontSize = 10.5, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,8) });
            var priceRow = new StackPanel { Orientation = Orientation.Horizontal };
            priceRow.Children.Add(new TextBlock { Text = "$9.99", Foreground = B(C_Text), FontSize = 34, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Bottom });
            priceRow.Children.Add(new TextBlock { Text = "разово", Foreground = B(C_Muted), FontSize = 12, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8,0,0,7) });
            ov.Children.Add(priceRow);
            ov.Children.Add(new TextBlock { Text = "Один платёж, без автосписаний. Есть вариант «Навсегда».", Foreground = B(C_Muted), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,4,0,12) });
            var gift = new Border { CornerRadius = new CornerRadius(8), Background = B("#241A0A"), BorderBrush = B("#4A3818"), BorderThickness = new Thickness(1), Padding = new Thickness(12,9,12,9), Margin = new Thickness(0,0,0,12) };
            gift.Child = new TextBlock { Text = "🎁  Каждый ключ = билет в розыгрыше $1000", Foreground = B(C_ProHi), FontSize = 12, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            ov.Children.Add(gift);
            ov.Children.Add(new TextBlock { Text = "✓ 30 дней гарантии · возврат\n✓ Мгновенный ключ\n✓ 624 отзыва · оплата Stripe / карта РФ", Foreground = B(C_Muted), FontSize = 11.5, Margin = new Thickness(0,0,0,14), LineHeight = 19 });
            var buy = ProButtonBig("Купить BUL$ Opti PRO →", ()=> Open(URL_App));
            ov.Children.Add(buy);
            offer.Child = ov;
            Grid.SetColumn(offer, 1); body.Children.Add(offer);

            Grid.SetRow(body, 1); g.Children.Add(body);

            card.Child = g;
            Grid.SetRow(card, 0); v.Children.Add(card);

            var cta = new Grid { Margin = new Thickness(0,14,0,0) };
            cta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cta.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var note = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            note.Children.Add(new TextBlock { Text = "Это отдельное приложение — открывается на сайте, не заменяет этот мини-тул.", Foreground = B(C_Muted), FontSize = 12 });
            Grid.SetColumn(note, 0); cta.Children.Add(note);
            var rlinks = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            rlinks.Children.Add(LinkBtn("Все инструменты и цены →", C_ProHi, ()=> Open(URL_App)));
            rlinks.Children.Add(new TextBlock { Text = "·", Foreground = B(C_Faint), Margin = new Thickness(10,0,10,0), VerticalAlignment = VerticalAlignment.Center });
            rlinks.Children.Add(LinkBtn("Discord", C_Muted, ()=> Open(URL_Discord)));
            Grid.SetColumn(rlinks, 1); cta.Children.Add(rlinks);
            Grid.SetRow(cta, 1); v.Children.Add(cta);

            return v;
        }

        FrameworkElement ToolItem(string name, string desc)
        {
            var row = new Grid { Margin = new Thickness(0,0,0,11) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var ic = new TextBlock { Text = G_Star, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 10, Foreground = B(C_Pro), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0,3,9,0) };
            var tx = new StackPanel();
            tx.Children.Add(new TextBlock { Text = name, Foreground = B(C_Text), FontSize = 12, FontWeight = FontWeights.SemiBold });
            tx.Children.Add(new TextBlock { Text = desc, Foreground = B(C_Faint), FontSize = 10.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,1,0,0) });
            Grid.SetColumn(ic, 0); Grid.SetColumn(tx, 1);
            row.Children.Add(ic); row.Children.Add(tx);
            return row;
        }

        Border ComparePanel(string title, string titleColor, bool isPro, string[] items)
        {
            var c = new Border { CornerRadius = new CornerRadius(10), Background = B(isPro ? "#17120B" : "#121218"), BorderBrush = B(isPro ? "#3A2C12" : C_Line), BorderThickness = new Thickness(1), Padding = new Thickness(18,16,18,16) };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title.ToUpper(), Foreground = B(titleColor), FontSize = 11, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,12) });
            foreach (var it in items)
            {
                var row = new Grid { Margin = new Thickness(0,0,0,9) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var ic = new TextBlock { Text = isPro ? G_Star : G_Check, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 11, Foreground = B(isPro ? C_Pro : C_Ok), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0,2,10,0) };
                var tx = new TextBlock { Text = it, Foreground = B(isPro ? C_Text : C_Muted), FontSize = 12, TextWrapping = TextWrapping.Wrap };
                Grid.SetColumn(ic, 0); Grid.SetColumn(tx, 1);
                row.Children.Add(ic); row.Children.Add(tx);
                sp.Children.Add(row);
            }
            c.Child = sp;
            return c;
        }

        // ======================= КНОПКИ / ХЕЛПЕРЫ =======================
        Border Card()
        { return new Border { CornerRadius = new CornerRadius(12), Background = B(C_Card), BorderBrush = B(C_Line), BorderThickness = new Thickness(1) }; }

        Border PrimaryButton(string text, double width, Action onClick)
        {
            var b = new Border { Width = width, Height = 46, CornerRadius = new CornerRadius(10), Cursor = Cursors.Hand, Background = Grad(C_AccHi, C_Accent, 90),
                Effect = new DropShadowEffect { Color = Col(C_Accent), BlurRadius = 18, ShadowDepth = 0, Opacity = 0.35 } };
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new TextBlock { Text = G_Bolt, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,9,0) });
            row.Children.Add(new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
            b.Child = row;
            b.MouseEnter += (s,e)=> b.Background = Grad("#9B82FA", C_AccHi, 90);
            b.MouseLeave += (s,e)=> b.Background = Grad(C_AccHi, C_Accent, 90);
            b.MouseLeftButtonUp += (s,e)=>{ if (onClick != null) onClick(); };
            interactive.Add(b);
            return b;
        }
        Border GhostButton(string text, Action onClick)
        {
            var b = new Border { Height = 46, MinWidth = 200, CornerRadius = new CornerRadius(10), Cursor = Cursors.Hand, Padding = new Thickness(22,0,22,0), Background = B(C_Card), BorderBrush = B(C_Line), BorderThickness = new Thickness(1) };
            b.Child = new TextBlock { Text = text, Foreground = B(C_Text), FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            b.MouseEnter += (s,e)=> b.Background = B(C_Card2);
            b.MouseLeave += (s,e)=> b.Background = B(C_Card);
            b.MouseLeftButtonUp += (s,e)=>{ if (onClick != null) onClick(); };
            interactive.Add(b);
            return b;
        }
        Border ProButton(string text, Action onClick)
        {
            var b = new Border { Height = 34, CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand, Padding = new Thickness(16,0,16,0), Background = B("#241A0A"), BorderBrush = B("#4A3818"), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left };
            b.Child = new TextBlock { Text = text, Foreground = B(C_ProHi), FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            b.MouseEnter += (s,e)=> b.Background = B("#2E2110");
            b.MouseLeave += (s,e)=> b.Background = B("#241A0A");
            b.MouseLeftButtonUp += (s,e)=>{ if (onClick != null) onClick(); };
            return b;
        }
        Border ProButtonBig(string text, Action onClick)
        {
            var b = new Border { Height = 46, CornerRadius = new CornerRadius(10), Cursor = Cursors.Hand, Padding = new Thickness(24,0,24,0), Background = Grad(C_ProHi, C_Pro, 90),
                Effect = new DropShadowEffect { Color = Col(C_Pro), BlurRadius = 18, ShadowDepth = 0, Opacity = 0.32 } };
            b.Child = new TextBlock { Text = text, Foreground = B("#201400"), FontSize = 13.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            b.MouseEnter += (s,e)=> b.Background = Grad("#FFD37A", C_ProHi, 90);
            b.MouseLeave += (s,e)=> b.Background = Grad(C_ProHi, C_Pro, 90);
            b.MouseLeftButtonUp += (s,e)=>{ if (onClick != null) onClick(); };
            return b;
        }
        TextBlock LinkBtn(string text, string color, Action onClick)
        {
            var t = new TextBlock { Text = text, Foreground = B(color), FontSize = 12, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            t.MouseEnter += (s,e)=> t.TextDecorations = TextDecorations.Underline;
            t.MouseLeave += (s,e)=> t.TextDecorations = null;
            t.MouseLeftButtonUp += (s,e)=>{ if (onClick != null) onClick(); };
            return t;
        }
        void Open(string url) { try { System.Diagnostics.Process.Start(url); } catch { } }

        Bar BuildBar()
        {
            var g = new Grid { Height = 7 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var track = new Border { CornerRadius = new CornerRadius(4), Background = B("#20202A") };
            Grid.SetColumnSpan(track, 2); g.Children.Add(track);
            var fill = new Border { CornerRadius = new CornerRadius(4), Background = B(C_Accent) };
            Grid.SetColumn(fill, 0); g.Children.Add(fill);
            return new Bar { Root = g, Fill = fill };
        }
        void SetBar(Bar bar, double frac, bool full)
        {
            if (bar == null) return;
            if (frac < 0) frac = 0; if (frac > 1) frac = 1;
            bar.Root.ColumnDefinitions[0].Width = new GridLength(frac, GridUnitType.Star);
            bar.Root.ColumnDefinitions[1].Width = new GridLength(1 - frac, GridUnitType.Star);
            bar.Fill.Background = B(full ? C_Ok : C_Accent);
        }

        // ======================= ДАННЫЕ (скан) =======================
        class ScanData { public List<KeyValuePair<string,bool>> Rows; public int Ok, Tot; public string[] Hw; public long Junk; }

        void Gather(bool sync, Action<ScanData> then)
        {
            if (sync) { then(GatherData()); return; }
            var th = new Thread(()=>{
                var d = GatherData();
                if (contentHost != null) contentHost.Dispatcher.BeginInvoke((Action)(()=> then(d)));
            });
            th.IsBackground = true; th.Start();
        }
        ScanData GatherData()
        {
            var rows = new List<KeyValuePair<string,bool>>();
            int ok = 0, tot = 0;
            foreach (var t in tweaks)
            {
                if (t.IsOptimized == null) continue;
                bool st; try { var r = t.IsOptimized(); st = r.HasValue && r.Value; } catch { st = false; }
                rows.Add(new KeyValuePair<string,bool>(t.Title, st));
                tot++; if (st) ok++;
            }
            string[] hw; try { hw = Program.Hardware(); } catch { hw = new string[0]; }
            long junk; try { junk = Program.TempJunkMb(); } catch { junk = 0; }
            return new ScanData { Rows = rows, Ok = ok, Tot = tot, Hw = hw, Junk = junk };
        }

        // ======================= ЛОГ / RUN =======================
        void Log(string s)
        {
            if (logBox == null) return;
            if (!logBox.Dispatcher.CheckAccess()) { logBox.Dispatcher.BeginInvoke((Action)(()=> Log(s))); return; }
            logBox.AppendText(s + "\r\n"); logBox.ScrollToEnd();
        }
        void Status(string s)
        {
            if (homeStatus == null) return;
            if (!homeStatus.Dispatcher.CheckAccess()) { homeStatus.Dispatcher.BeginInvoke((Action)(()=> Status(s))); return; }
            homeStatus.Text = s; homeStatus.Foreground = B(C_Ok);
        }
        void Busy(bool b)
        {
            if (logBox != null && !logBox.Dispatcher.CheckAccess()) { logBox.Dispatcher.BeginInvoke((Action)(()=> Busy(b))); return; }
            foreach (var e in interactive) { e.IsEnabled = !b; e.Opacity = b ? 0.5 : 1.0; }
        }

        void Run(List<Tweak> chosen, bool apply, bool setWall)
        {
            if (chosen.Count == 0) { Log("Ничего не выбрано."); return; }
            if (curView != "home") SetView("home");
            Busy(true);
            var th = new Thread(()=>{
                try
                {
                    Log(""); Log("=== " + (apply ? "ОПТИМИЗАЦИЯ" : "ОТКАТ") + " (" + chosen.Count + ") ===");
                    if (apply) { Program.LastFreedBytes = 0; Log("Создаю точку восстановления..."); Program.RestorePoint(Log); Program.DoBackup(chosen, Log); }
                    foreach (var t in chosen)
                    { Log((apply?"[+] ":"[-] ") + t.Title); try { (apply ? t.Apply : t.Revert)(Log); } catch (Exception ex) { Log("    ! ошибка: " + ex.Message); } }
                    if (apply && setWall) { SetWall(); Log("Установлены обои BUL$ Opti (убрать: Параметры Windows → Персонализация)."); }
                    Log("");
                    if (apply)
                    {
                        Log("Готово. Часть настроек применится после перезагрузки.");
                        long mb = Program.LastFreedBytes / 1024 / 1024;
                        Program.AddTotalFreedMb(mb);
                        Status(mb > 0 ? ("✔ Оптимизировано · освобождено ~" + mb + " МБ") : "✔ Система оптимизирована");
                    }
                    else { Log("Откат завершён."); Status("Оптимизации откачены"); }
                    Log("bulsopti.com");
                }
                finally
                {
                    Busy(false);
                    if (contentHost != null) contentHost.Dispatcher.BeginInvoke((Action)(()=> RefreshHome(false)));
                }
            });
            th.IsBackground = true; th.SetApartmentState(ApartmentState.STA); th.Start();
        }

        void SetWall()
        {
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("BulsOpti.wall.png"))
                {
                    if (s == null) return;
                    string dst = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "BULS Opti Wallpaper.png");
                    using (var fs = File.Create(dst)) s.CopyTo(fs);
                    Sys.SetWallpaper(dst);
                }
            }
            catch { }
        }
    }
}
