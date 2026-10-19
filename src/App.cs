using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BulsOpti
{
    // ---------- Параметры запуска (перезапуск с правами продолжает начатое действие) ----------
    class LaunchOpts
    {
        public string Run, Page;
        public bool Rp = true, Wall = true, Yes;
        public string[] On = new string[0], Off = new string[0], Ids = new string[0];

        public static LaunchOpts Parse(string[] a)
        {
            var o = new LaunchOpts();
            for (int i = 0; i < a.Length; i++)
            {
                string k = a[i].ToLowerInvariant();
                string v = i + 1 < a.Length ? a[i + 1] : "";
                if (k == "--run") { o.Run = v.ToLowerInvariant(); i++; }
                else if (k == "--page") { o.Page = v.ToLowerInvariant(); i++; }
                else if (k == "--rp") { o.Rp = v == "1"; i++; }
                else if (k == "--wall") { o.Wall = v == "1"; i++; }
                else if (k == "--yes") o.Yes = true;
                else if (k == "--on") { o.On = Split(v); i++; }
                else if (k == "--off") { o.Off = Split(v); i++; }
                else if (k == "--ids") { o.Ids = Split(v); i++; }
            }
            return o;
        }

        static string[] Split(string v) { return v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries); }

        public string ToArgs()
        {
            var s = "--run " + Run + " --rp " + (Rp ? "1" : "0") + " --wall " + (Wall ? "1" : "0");
            if (On.Length > 0) s += " --on " + string.Join(",", On);
            if (Off.Length > 0) s += " --off " + string.Join(",", Off);
            if (Ids.Length > 0) s += " --ids " + string.Join(",", Ids);
            if (Yes) s += " --yes";
            return s;
        }
    }

    static class Crash
    {
        public static void Write(Exception ex)
        {
            if (ex == null) return;
            try { File.AppendAllText(Path.Combine(AppInfo.DataDir, "crash.log"), DateTime.Now.ToString("s") + "  " + ex + Environment.NewLine + Environment.NewLine); }
            catch { }
        }
    }

    static class AppMain
    {
        public const string Title = "BUL$ Opti";

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowW(string cls, string title);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);

        [STAThread]
        static int Main(string[] args)
        {
            Com.InitSecurity();
            if (args.Length > 0 && args[0].TrimStart('-', '/').ToLowerInvariant() == "screenshot") return Shot(args);

            var opts = LaunchOpts.Parse(args);
            bool owned;
            var mx = new Mutex(true, @"Local\BulsOptiFree", out owned);
            if (!owned && opts.Run != null)
            {
                try { owned = mx.WaitOne(15000); } catch (AbandonedMutexException) { owned = true; }
            }
            if (!owned)
            {
                IntPtr h = FindWindowW(null, Title);
                if (h != IntPtr.Zero) { ShowWindow(h, 9); SetForegroundWindow(h); }
                return 0;
            }

            AppDomain.CurrentDomain.UnhandledException += (s, e) => Crash.Write(e.ExceptionObject as Exception);
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            Th.Install(app);
            Loc.Init();
            var shell = new Shell(opts);
            app.DispatcherUnhandledException += (s, e) => { Crash.Write(e.Exception); shell.ShowError(e.Exception); e.Handled = true; };
            app.Run(shell.CreateWindow());
            GC.KeepAlive(mx);
            return 0;
        }

        // screenshot <out.png> <страница> [язык] [admin|user] [ширина] [высота] — рисует экран в PNG, ничего не меняя в системе
        static int Shot(string[] args)
        {
            Journal.Mute = true;
            string outp = args.Length > 1 ? args[1] : "shot.png";
            string page = args.Length > 2 ? args[2].ToLowerInvariant() : "overview";
            if (!Shell.Pages.Contains(page)) { Console.Error.WriteLine("unknown page: " + page); return 2; }
            var app = new Application();
            Th.Install(app);
            Loc.Init();
            if (args.Length > 3) { int i = Array.IndexOf(Loc.Codes, args[3].ToLowerInvariant()); if (i >= 0) Loc.Lang = i; }
            var shell = new Shell(new LaunchOpts()) { ShotMode = true, ShotAdmin = args.Length > 4 && args[4].ToLowerInvariant() == "admin" };
            int w = 1100, h = 740;
            if (args.Length > 5) int.TryParse(args[5], out w);
            if (args.Length > 6) int.TryParse(args[6], out h);
            var root = shell.BuildRoot();
            shell.ShowPage(page);
            for (int pass = 0; pass < 2; pass++) { root.Measure(new Size(w, h)); root.Arrange(new Rect(0, 0, w, h)); root.UpdateLayout(); }
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(root);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            try { using (var fs = File.Create(outp)) enc.Save(fs); }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 3; }
            return 0;
        }
    }

    class ScanData
    {
        public Dictionary<string, TState> States = new Dictionary<string, TState>();
        public HwInfo Hw = new HwInfo();
        public List<DiagItem> Diag = new List<DiagItem>();
        public long TempBytes;
        public bool HasUndo;
    }

    class Shell
    {
        public static readonly string[] Pages = { "overview", "check", "tweaks", "clean", "journal", "pro", "about" };
        public bool ShotMode, ShotAdmin;

        readonly LaunchOpts opts;
        readonly List<Tweak> tweaks;
        readonly List<CleanCat> cats;
        Window win;
        Grid root, overlay;
        ScrollViewer scroller;
        ContentControl pageHost;
        TextBlock status;
        StackPanel navPanel;
        Button langButton;
        Popup langPopup;
        readonly Dictionary<string, RadioButton> navButtons = new Dictionary<string, RadioButton>();
        string page = "overview";

        ScanData scan; int scanGen; Action afterScan;
        Dictionary<string, long> sizes; int sizeGen; DateTime sizesAt;
        readonly Dictionary<string, bool> want = new Dictionary<string, bool>();
        readonly Dictionary<string, bool> cleanSel = new Dictionary<string, bool>();
        readonly Dictionary<string, CleanResult> cleanDone = new Dictionary<string, CleanResult>();
        bool rp, wall;
        bool busy, closeWhenDone, restarting;
        string busyText, lastMessage, preset;
        TextBlock pendingText, cleanTotal;
        Button pendingApply, pendingReset;

        bool IsAdmin { get { return ShotMode ? ShotAdmin : AppInfo.IsAdmin; } }

        public Shell(LaunchOpts o)
        {
            opts = o;
            tweaks = Engine.Tweaks();
            cats = Cleaner.Categories();
            foreach (var c in cats) cleanSel[c.Id] = c.DefaultOn;
            rp = o.Rp; wall = o.Wall;
            Journal.Added += OnJournal;
        }

        // ---------- Окно ----------
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        static void DarkTitle(IntPtr h)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(h, 20, ref on, 4) != 0) DwmSetWindowAttribute(h, 19, ref on, 4);
                int cap = 0x00060505; DwmSetWindowAttribute(h, 35, ref cap, 4);
                int txt = 0x00AAA1A1; DwmSetWindowAttribute(h, 36, ref txt, 4);
                int brd = 0x00201B1B; DwmSetWindowAttribute(h, 34, ref brd, 4);
            }
            catch { }
        }

        public Window CreateWindow()
        {
            var wa = SystemParameters.WorkArea;
            double w = Math.Min(1100, wa.Width - 40), h = Math.Min(740, wa.Height - 40);
            win = new Window
            {
                Title = AppMain.Title, Width = w, Height = h,
                MinWidth = Math.Min(900, w), MinHeight = Math.Min(600, h),
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = Th.B(Th.Bg), FontFamily = Th.Font,
                UseLayoutRounding = true, SnapsToDevicePixels = true
            };
            TextOptions.SetTextFormattingMode(win, TextFormattingMode.Display);
            win.Content = BuildRoot();
            win.SourceInitialized += (s, e) => DarkTitle(new WindowInteropHelper(win).Handle);
            win.Loaded += (s, e) =>
            {
                ShowPage(opts.Page ?? PageForRun(opts.Run));
                if (opts.Run != null) root.Dispatcher.BeginInvoke(new Action(RunFromArgs));
            };
            win.Closing += (s, e) =>
            {
                if (busy && !restarting) { e.Cancel = true; closeWhenDone = true; busyText = Loc.T("status.closing"); UpdateStatus(); }
            };
            return win;
        }

        static string PageForRun(string run)
        {
            if (run == "apply") return "tweaks";
            if (run == "clean") return "clean";
            return "overview";
        }

        public FrameworkElement BuildRoot()
        {
            navButtons.Clear();
            root = new Grid { Background = Th.B(Th.Bg) };
            TextElement.SetFontFamily(root, Th.Font);
            TextElement.SetForeground(root, Th.B(Th.Text));
            root.PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape && overlay != null && overlay.Visibility == Visibility.Visible) { CloseDialog(); e.Handled = true; } };

            var cols = Th.Cols(Th.Px(232), Th.Star);
            root.Children.Add(cols);
            cols.Children.Add(Th.At(BuildSidebar(), 0));

            var right = new Grid();
            right.RowDefinitions.Add(new RowDefinition { Height = Th.Star });
            right.RowDefinitions.Add(new RowDefinition { Height = Th.Auto });
            scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
            pageHost = new ContentControl { Focusable = false, IsTabStop = false };
            scroller.Content = pageHost;
            right.Children.Add(Th.Row(scroller, 0));

            var bar = new Border { BorderBrush = Th.B(Th.Line), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(44, 9, 30, 10) };
            var bg = Th.Cols(Th.Star, Th.Auto);
            status = Th.T("", 12, Th.Text3);
            status.TextWrapping = TextWrapping.NoWrap; status.TextTrimming = TextTrimming.CharacterEllipsis; status.VerticalAlignment = VerticalAlignment.Center;
            bg.Children.Add(Th.At(status, 0));
            var jl = Th.Btn("Link", Loc.T("status.journal"), (s, e) => ShowPage("journal"));
            jl.FontSize = 12; jl.Margin = new Thickness(16, 0, 0, 0);
            bg.Children.Add(Th.At(jl, 1));
            bar.Child = bg;
            right.Children.Add(Th.Row(bar, 1));
            cols.Children.Add(Th.At(right, 1));

            overlay = new Grid { Visibility = Visibility.Collapsed, Background = new SolidColorBrush(Color.FromArgb(0xD0, 0, 0, 0)) };
            root.Children.Add(overlay);
            return root;
        }

        FrameworkElement BuildSidebar()
        {
            var side = new Border { BorderBrush = Th.B(Th.Line), BorderThickness = new Thickness(0, 0, 1, 0) };
            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = Th.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = Th.Star });
            g.RowDefinitions.Add(new RowDefinition { Height = Th.Auto });

            var top = new StackPanel();
            var brand = Th.Cols(Th.Auto, Th.Star);
            brand.Margin = new Thickness(24, 26, 16, 0);
            var img = new Image { Source = Th.Res("bull.png"), Width = 28, Height = 28, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 11, 0) };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            brand.Children.Add(Th.At(img, 0));
            var bt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            bt.Children.Add(Th.T("BUL$ Opti", 14.5, Th.Text, FontWeights.SemiBold));
            bt.Children.Add(Th.T(Loc.T("app.edition") + " · " + AppInfo.Version, 11.5, Th.Text3));
            brand.Children.Add(Th.At(bt, 1));
            top.Children.Add(brand);

            navPanel = new StackPanel { Margin = new Thickness(12, 30, 12, 0) };
            AddNav("overview", "nav.overview");
            AddNav("check", "nav.check");
            AddNav("tweaks", "nav.tweaks");
            AddNav("clean", "nav.clean");
            AddNav("journal", "nav.journal");
            navPanel.Children.Add(Th.Hair(14, 14));
            AddNav("pro", "nav.pro");
            AddNav("about", "nav.about");
            top.Children.Add(navPanel);
            g.Children.Add(Th.Row(top, 0));

            var bottom = new StackPanel { Margin = new Thickness(24, 0, 16, 22) };
            langButton = Th.Btn("Link", Loc.Names[Loc.Lang] + "  ▾", (s, e) => ShowLangPopup());
            langButton.ToolTip = Loc.T("lang.label");
            bottom.Children.Add(langButton);
            var mode = Th.T(IsAdmin ? Loc.T("mode.admin") : Loc.T("mode.user"), 11.5, Th.Text3);
            mode.Margin = new Thickness(0, 8, 0, 0);
            bottom.Children.Add(mode);
            g.Children.Add(Th.Row(bottom, 2));

            side.Child = g;
            return side;
        }

        void AddNav(string key, string label)
        {
            var rb = new RadioButton { Style = Th.S("Nav"), GroupName = "nav", Content = Loc.T(label), Margin = new Thickness(0, 0, 0, 2) };
            rb.Checked += (s, e) => { if (page != key) ShowPage(key); };
            navButtons[key] = rb;
            navPanel.Children.Add(rb);
        }

        void ShowLangPopup()
        {
            var list = new StackPanel();
            for (int i = 0; i < Loc.Codes.Length; i++)
            {
                int idx = i;
                var b = Th.Btn("Link", Loc.Names[i], (s, e) => { langPopup.IsOpen = false; SetLang(idx); });
                b.FontSize = 13; b.Margin = new Thickness(0, 3, 0, 3);
                if (i == Loc.Lang) b.Foreground = Th.B(Th.Text);
                list.Children.Add(b);
            }
            var border = new Border { Background = Th.B(Th.Raised), BorderBrush = Th.B(Th.LineStrong), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 10, 28, 10), Child = list };
            langPopup = new Popup { PlacementTarget = langButton, Placement = PlacementMode.Top, StaysOpen = false, AllowsTransparency = true, Child = border };
            langPopup.IsOpen = true;
        }

        void SetLang(int i)
        {
            Loc.Lang = i;
            AppInfo.SetSetting("Lang", Loc.Codes[i]);
            string cur = page;
            win.Content = BuildRoot();
            page = "";
            ShowPage(cur);
        }

        // ---------- Навигация и данные ----------
        public void ShowPage(string key)
        {
            if (!Pages.Contains(key)) key = "overview";
            bool changed = page != key;
            page = key;
            RadioButton rb;
            if (navButtons.TryGetValue(key, out rb) && rb.IsChecked != true) rb.IsChecked = true;
            if (key == "overview" || key == "check" || key == "tweaks") EnsureScan();
            if (key == "clean") EnsureSizes();
            Render(changed);
        }

        void Render(bool toTop)
        {
            double off = scroller.VerticalOffset;
            FrameworkElement c;
            switch (page)
            {
                case "check": c = PageCheck(); break;
                case "tweaks": c = PageTweaks(); break;
                case "clean": c = PageClean(); break;
                case "journal": c = PageJournal(); break;
                case "pro": c = PagePro(); break;
                case "about": c = PageAbout(); break;
                default: c = PageOverview(); break;
            }
            pageHost.Content = c;
            if (toTop) scroller.ScrollToTop();
            else { scroller.UpdateLayout(); scroller.ScrollToVerticalOffset(off); }
            UpdateStatus();
        }

        void Render() { Render(false); }

        TState St(Tweak t)
        {
            TState s;
            return scan != null && scan.States.TryGetValue(t.Id, out s) ? s : TState.NA;
        }

        void EnsureScan() { if (scan == null) StartScan(); }

        void StartScan()
        {
            int my = ++scanGen;
            Func<ScanData> work = () =>
            {
                var d = new ScanData();
                foreach (var t in tweaks) d.States[t.Id] = t.State();
                d.Hw = SysInfo.Read();
                d.Diag = SysInfo.Diagnose(d.Hw);
                var temp = cats.FirstOrDefault(c => c.Id == "temp");
                d.TempBytes = temp != null ? temp.Measure() : 0;
                d.HasUndo = Undo.Ids().Length > 0;
                return d;
            };
            if (ShotMode) { scan = work(); return; }
            Bg(work, d =>
            {
                if (my != scanGen) return;
                scan = d ?? new ScanData();
                Render();
                if (afterScan != null) { var a = afterScan; afterScan = null; a(); }
            });
        }

        void WhenScan(Action a)
        {
            if (scan != null) { a(); return; }
            afterScan = a;
            EnsureScan();
        }

        void EnsureSizes()
        {
            if (sizes != null && (DateTime.UtcNow - sizesAt).TotalSeconds < 60) return;
            int my = ++sizeGen;
            Func<Dictionary<string, long>> work = () =>
            {
                var d = new Dictionary<string, long>();
                foreach (var c in cats) { long v; try { v = c.Measure(); } catch { v = 0; } d[c.Id] = v; }
                return d;
            };
            if (ShotMode) { sizes = work(); sizesAt = DateTime.UtcNow; return; }
            Bg(work, d =>
            {
                if (my != sizeGen) return;
                sizes = d; sizesAt = DateTime.UtcNow;
                if (page == "clean") Render();
            });
        }

        void Invalidate() { scan = null; sizes = null; }

        void Bg<TR>(Func<TR> work, Action<TR> done)
        {
            var th = new Thread(() =>
            {
                TR r = default(TR); Exception err = null;
                try { r = work(); } catch (Exception ex) { err = ex; }
                Dispatch(() =>
                {
                    if (err != null) { Crash.Write(err); Journal.Add(Loc.F("log.error", err.Message)); }
                    done(r);
                });
            });
            th.IsBackground = true;
            th.Start();
        }

        void Dispatch(Action a)
        {
            var d = root != null ? root.Dispatcher : Application.Current.Dispatcher;
            d.BeginInvoke(a);
        }

        void Progress(string text) { Dispatch(() => { busyText = text; UpdateStatus(); }); }

        void OnJournal(string line)
        {
            Dispatch(() =>
            {
                int i = line.IndexOf("   ");
                lastMessage = i >= 0 ? line.Substring(i + 3) : line;
                UpdateStatus();
            });
        }

        void UpdateStatus()
        {
            if (status == null) return;
            status.Text = busy ? (busyText ?? Loc.T("status.working")) : (lastMessage ?? Loc.T("status.ready"));
            status.Foreground = Th.B(busy ? Th.Text2 : Th.Text3);
        }

        void RunAction(Func<string> work, Action after)
        {
            if (busy) return;
            busy = true; busyText = Loc.T("status.working");
            Render();
            Bg(work, msg =>
            {
                busy = false; busyText = null;
                if (after != null) after();
                Invalidate();
                if (msg != null) lastMessage = msg;
                ShowPage(page);
                if (closeWhenDone && win != null) win.Close();
            });
        }

        void Open(string uri)
        {
            if (uri.StartsWith("page:")) { ShowPage(uri.Substring(5)); return; }
            try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
            catch (Exception ex) { lastMessage = Loc.F("log.error", ex.Message); UpdateStatus(); }
        }

        void Elevate(LaunchOpts o)
        {
            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                Process.Start(new ProcessStartInfo(exe, o.ToArgs()) { Verb = "runas", UseShellExecute = true });
                Journal.Add(Loc.T("log.elevated"));
                restarting = true;
                if (win != null) win.Close();
            }
            catch (Win32Exception ex)
            {
                lastMessage = ex.NativeErrorCode == 1223 ? Loc.T("msg.elevCancel") : Loc.F("log.error", ex.Message);
                UpdateStatus();
            }
        }

        void RunFromArgs()
        {
            if (!IsAdmin) { Journal.Add(Loc.T("log.notElevated")); return; }
            switch (opts.Run)
            {
                case "optimize": WhenScan(() => DoOptimize(true)); break;
                case "apply":
                    WhenScan(() => ExecApply(tweaks.Where(t => opts.On.Contains(t.Id)).ToList(), tweaks.Where(t => opts.Off.Contains(t.Id)).ToList()));
                    break;
                case "revert": ExecRevertAll(); break;
                case "clean": ExecClean(cats.Where(c => opts.Ids.Contains(c.Id)).ToList()); break;
            }
        }

        // ---------- Действия ----------
        void DoOptimize(bool fromArgs)
        {
            if (scan == null || busy) return;
            var list = tweaks.Where(t => t.Recommended && St(t) == TState.NotOk).ToList();
            bool setWall = wall && !Wallpaper.IsOurs();
            if (list.Count == 0 && !setWall) { lastMessage = Loc.T("msg.nothing"); UpdateStatus(); return; }
            bool needAdmin = list.Any(t => t.NeedsAdmin) || (rp && list.Count > 0);
            if (needAdmin && !IsAdmin && !fromArgs)
            {
                var adminItems = list.Where(t => t.NeedsAdmin).Select(t => t.Title).ToList();
                if (rp && list.Count > 0) adminItems.Add(Loc.T("ov.rp"));
                var userList = list.Where(t => !t.NeedsAdmin).ToList();
                Action userOnly = null;
                if (userList.Count > 0 || setWall) userOnly = () => ExecOptimize(userList, false, setWall);
                AskElevate(adminItems, userList.Count, () => Elevate(new LaunchOpts { Run = "optimize", Rp = rp, Wall = wall }), userOnly);
                return;
            }
            ExecOptimize(list, rp && IsAdmin && list.Count > 0, setWall);
        }

        void ExecOptimize(List<Tweak> list, bool restorePoint, bool setWall)
        {
            RunAction(() =>
            {
                int ok = 0, fail = 0;
                if (restorePoint)
                {
                    Progress(Loc.T("prog.rp"));
                    int rc = RestorePoint.Create("BUL$ Opti");
                    Journal.Add(rc == 0 ? Loc.T("log.rpOk") : (rc == 1058 ? Loc.T("log.rpOff") : Loc.F("log.rpFail", rc)));
                }
                int i = 0;
                foreach (var t in list)
                {
                    i++;
                    Progress(Loc.F("prog.apply", i, list.Count));
                    if (t.Apply()) { ok++; Journal.Add(Loc.F("log.applied", t.Title)); }
                    else { fail++; Journal.Add(Loc.F("log.applyFail", t.Title)); }
                }
                if (setWall)
                {
                    using (var s = Th.ResStream("wall.png")) Journal.Add(Wallpaper.Apply(s) ? Loc.T("log.wallOk") : Loc.T("log.wallFail"));
                }
                if (ok > 0 && list.Any(t => t.NeedsRestart)) Journal.Add(Loc.T("log.restart"));
                return fail == 0 ? Loc.F("msg.applied", ok) : Loc.F("msg.appliedFail", ok, fail);
            }, () => want.Clear());
        }

        void DoApplyPending()
        {
            if (scan == null || busy) return;
            var on = tweaks.Where(t => St(t) == TState.NotOk && Want(t)).ToList();
            var off = tweaks.Where(t => St(t) == TState.Ok && !Want(t)).ToList();
            if (on.Count + off.Count == 0) return;
            bool needAdmin = on.Any(t => t.NeedsAdmin) || off.Any(t => t.RevertNeedsAdmin);
            if (needAdmin && !IsAdmin)
            {
                var adminItems = on.Where(t => t.NeedsAdmin).Concat(off.Where(t => t.RevertNeedsAdmin)).Select(t => t.Title).ToList();
                var uOn = on.Where(t => !t.NeedsAdmin).ToList();
                var uOff = off.Where(t => !t.RevertNeedsAdmin).ToList();
                Action userOnly = null;
                if (uOn.Count + uOff.Count > 0) userOnly = () => ExecApply(uOn, uOff);
                AskElevate(adminItems, uOn.Count + uOff.Count,
                    () => Elevate(new LaunchOpts { Run = "apply", On = on.Select(t => t.Id).ToArray(), Off = off.Select(t => t.Id).ToArray() }), userOnly);
                return;
            }
            ExecApply(on, off);
        }

        void ExecApply(List<Tweak> on, List<Tweak> off)
        {
            RunAction(() =>
            {
                int ok = 0, fail = 0, i = 0, n = on.Count + off.Count;
                foreach (var t in on)
                {
                    Progress(Loc.F("prog.apply", ++i, n));
                    if (t.Apply()) { ok++; Journal.Add(Loc.F("log.applied", t.Title)); } else { fail++; Journal.Add(Loc.F("log.applyFail", t.Title)); }
                }
                foreach (var t in off)
                {
                    Progress(Loc.F("prog.apply", ++i, n));
                    if (t.Revert()) { ok++; Journal.Add(Loc.F("log.reverted", t.Title)); } else { fail++; Journal.Add(Loc.F("log.revertFailOne", t.Title)); }
                }
                if (ok > 0 && on.Concat(off).Any(t => t.NeedsRestart)) Journal.Add(Loc.T("log.restart"));
                return fail == 0 ? Loc.F("msg.changed", ok) : Loc.F("msg.appliedFail", ok, fail);
            }, () => { want.Clear(); preset = null; });
        }

        void DoRevertAll()
        {
            if (busy) return;
            int n = Undo.Ids().Length;
            if (n == 0) return;
            bool needAdmin = Engine.AnyHklmUndo() && !IsAdmin;
            string text = Loc.T("dlg.revert.text") + (needAdmin ? "\n\n" + Loc.T("dlg.revert.admin") : "");
            Confirm(Loc.T("dlg.revert.title"), text, Loc.T("dlg.revert.yes"), () =>
            {
                if (needAdmin) Elevate(new LaunchOpts { Run = "revert" });
                else ExecRevertAll();
            });
        }

        void ExecRevertAll()
        {
            RunAction(() =>
            {
                Progress(Loc.T("prog.revert"));
                var failed = Engine.RevertAll(tweaks);
                if (failed.Count == 0) { Journal.Add(Loc.T("log.revertAll")); return Loc.T("msg.reverted"); }
                Journal.Add(Loc.F("log.revertFail", string.Join(", ", failed.ToArray())));
                return Loc.T("msg.revertPartial");
            }, () => { want.Clear(); preset = null; });
        }

        void DoClean(List<CleanCat> sel)
        {
            if (busy) return;
            if (sel.Count == 0) { lastMessage = Loc.T("msg.selectSomething"); UpdateStatus(); return; }
            Action go = () =>
            {
                if (sel.Any(c => c.NeedsAdmin) && !IsAdmin)
                {
                    var adminItems = sel.Where(c => c.NeedsAdmin).Select(c => c.Title).ToList();
                    var userSel = sel.Where(c => !c.NeedsAdmin).ToList();
                    Action userOnly = null;
                    if (userSel.Count > 0) userOnly = () => ExecClean(userSel);
                    AskElevate(adminItems, userSel.Count, () => Elevate(new LaunchOpts { Run = "clean", Ids = sel.Select(c => c.Id).ToArray(), Yes = true }), userOnly);
                    return;
                }
                ExecClean(sel);
            };
            var notes = new List<string>();
            if (sel.Any(c => c.Id == "recycle")) { long s, n; Cleaner.RecycleInfo(out s, out n); if (n > 0) notes.Add(Loc.F("dlg.recycle.text", n, Fmt.Size(s))); }
            if (sel.Any(c => c.Id == "temp") && !Cleaner.TempIsStandard) notes.Add(Loc.F("dlg.temp.text", Cleaner.SafeUserTemp() ?? Path.GetTempPath()));
            if (notes.Count > 0) { Confirm(Loc.T("dlg.clean.title"), string.Join("\n\n", notes.ToArray()), Loc.T("dlg.recycle.yes"), go); return; }
            go();
        }

        void ExecClean(List<CleanCat> sel)
        {
            var res = new Dictionary<string, CleanResult>();
            RunAction(() =>
            {
                long total = 0; int i = 0;
                foreach (var c in sel)
                {
                    Progress(Loc.F("prog.clean", c.Title, ++i, sel.Count));
                    CleanResult r;
                    try { r = c.Run(); } catch (Exception ex) { r = new CleanResult(); r.Notes.Add(ex.Message); }
                    res[c.Id] = r; total += r.Freed;
                    Journal.Add(c.Measure != null && r.Files == 0 && r.Freed == 0 && r.Notes.Count > 0 ? c.Title + ": " + string.Join(" ", r.Notes.ToArray()) : Loc.F("log.cleaned", c.Title, Fmt.Size(r.Freed), r.Locked));
                }
                Stats.AddFreed(total);
                return Loc.F("msg.cleaned", Fmt.Size(total));
            }, () => { foreach (var kv in res) cleanDone[kv.Key] = kv.Value; });
        }

        // ---------- Диалоги (внутри окна, с клавиатурой) ----------
        void ShowDialog(string title, UIElement body, List<Button> buttons)
        {
            overlay.Children.Clear();
            var panel = new Border
            {
                Background = Th.B(Th.Raised), BorderBrush = Th.B(Th.LineStrong), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(28, 24, 28, 24), MaxWidth = 540, Margin = new Thickness(24), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            var sp = new StackPanel();
            sp.Children.Add(Th.T(title, 17, Th.Text, FontWeights.SemiBold));
            var holder = new ContentControl { Content = body, Margin = new Thickness(0, 10, 0, 0), Focusable = false };
            sp.Children.Add(holder);
            var row = new WrapPanel { Margin = new Thickness(0, 22, 0, 0) };
            foreach (var b in buttons) { b.Margin = new Thickness(0, 0, 10, 6); row.Children.Add(b); }
            sp.Children.Add(row);
            panel.Child = sp;
            overlay.Children.Add(panel);
            overlay.Visibility = Visibility.Visible;
            if (buttons.Count > 0) buttons[0].Focus();
        }

        void CloseDialog() { overlay.Visibility = Visibility.Collapsed; overlay.Children.Clear(); }

        Button DlgBtn(string style, string text, Action a)
        {
            return Th.Btn(style, text, (s, e) => { CloseDialog(); if (a != null) a(); });
        }

        void Confirm(string title, string text, string yes, Action onYes)
        {
            ShowDialog(title, Th.T(text, 13, Th.Text2), new List<Button> { DlgBtn("Primary", yes, onYes), DlgBtn("Secondary", Loc.T("dlg.cancel"), null) });
        }

        void AskElevate(List<string> items, int userCount, Action elevate, Action userOnly)
        {
            var body = new StackPanel();
            body.Children.Add(Th.T(Loc.T("dlg.admin.text"), 13, Th.Text2));
            var list = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            foreach (var s in items) { var t = Th.T("—  " + s, 13, Th.Text); t.Margin = new Thickness(0, 2, 0, 2); list.Children.Add(t); }
            body.Children.Add(list);
            var note = Th.T(Loc.T("dlg.admin.note"), 12, Th.Text3); note.Margin = new Thickness(0, 12, 0, 0);
            body.Children.Add(note);
            var buttons = new List<Button> { DlgBtn("Primary", Loc.T("dlg.admin.yes"), elevate) };
            if (userOnly != null) buttons.Add(DlgBtn("Secondary", Loc.F("dlg.admin.userOnly", userCount), userOnly));
            buttons.Add(DlgBtn("Secondary", Loc.T("dlg.cancel"), null));
            ShowDialog(Loc.T("dlg.admin.title"), body, buttons);
        }

        public void ShowError(Exception ex)
        {
            if (overlay == null) return;
            busy = false;
            ShowDialog(Loc.T("dlg.error.title"), Th.T(Loc.T("dlg.error.text") + "\n\n" + ex.Message, 13, Th.Text2),
                new List<Button> { DlgBtn("Primary", Loc.T("dlg.ok"), () => Render()), DlgBtn("Secondary", Loc.T("ab.openFolder"), () => Open(AppInfo.DataDir)) });
        }

        // ---------- Строители страниц ----------
        StackPanel Frame(string titleKey, string subKey)
        {
            var p = new StackPanel { Margin = new Thickness(44, 34, 44, 40), MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left };
            var t = Th.T(Loc.T(titleKey), 26, Th.Text, FontWeights.SemiBold);
            t.FontFamily = Th.Display;
            p.Children.Add(t);
            if (subKey != null) { var s = Th.T(Loc.T(subKey), 13.5, Th.Text2); s.Margin = new Thickness(0, 6, 0, 0); p.Children.Add(s); }
            if (IsAdmin && !ShotMode && AppInfo.OtherUserElevation())
            {
                var w = Th.T(Loc.T("warn.otherUser"), 12.5, Th.Warn); w.Margin = new Thickness(0, 14, 0, 0); p.Children.Add(w);
            }
            return p;
        }

        TextBlock Section(string text)
        {
            var t = Th.T(text, 13, Th.Text, FontWeights.SemiBold);
            t.Margin = new Thickness(0, 34, 0, 8);
            return t;
        }

        TextBlock Loading()
        {
            var t = Th.T(Loc.T("common.loading"), 13, Th.Text3);
            t.Margin = new Thickness(0, 30, 0, 0);
            return t;
        }

        FrameworkElement KV(string k, string v)
        {
            var sp = new StackPanel();
            var g = Th.Cols(Th.Px(180), Th.Star);
            g.Margin = new Thickness(0, 9, 0, 9);
            g.Children.Add(Th.At(Th.T(k, 13, Th.Text3), 0));
            g.Children.Add(Th.At(Th.T(v, 13, Th.Text), 1));
            sp.Children.Add(g);
            sp.Children.Add(Th.Hair(0, 0));
            return sp;
        }

        static string ShortCpu(string s)
        {
            if (string.IsNullOrEmpty(s)) return "—";
            s = s.Replace("(R)", "").Replace("(TM)", "").Replace("(tm)", "").Replace(" CPU", "");
            int at = s.IndexOf(" @ "); if (at > 0) s = s.Substring(0, at);
            int gen = s.IndexOf("Gen Intel", StringComparison.OrdinalIgnoreCase); if (gen > 0) s = s.Substring(gen + 4);
            int core = s.IndexOf("-Core", StringComparison.OrdinalIgnoreCase);
            if (core > 0) { int j = core; while (j > 0 && char.IsDigit(s[j - 1])) j--; if (j > 0 && s[j - 1] == ' ') j--; if (j > 0) s = s.Substring(0, j); }
            int pr = s.IndexOf(" Processor", StringComparison.OrdinalIgnoreCase); if (pr > 0) s = s.Substring(0, pr);
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            return s.Trim();
        }

        // ----- Обзор -----
        FrameworkElement PageOverview()
        {
            var p = Frame("ov.title", "ov.sub");
            if (AppInfo.GetSetting("SeenIntro") != "1")
            {
                var intro = new Border { BorderBrush = Th.B(Th.LineStrong), BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(16, 2, 0, 2), Margin = new Thickness(0, 24, 0, 0) };
                var ip = new StackPanel();
                ip.Children.Add(Th.T(Loc.T("ov.intro"), 13, Th.Text2));
                var ok = Th.Btn("Link", Loc.T("ov.introOk"), (s, e) => { AppInfo.SetSetting("SeenIntro", "1"); Render(); });
                ok.Margin = new Thickness(0, 8, 0, 0);
                ip.Children.Add(ok);
                intro.Child = ip;
                p.Children.Add(intro);
            }
            if (scan == null) { p.Children.Add(Loading()); return p; }

            var rec = tweaks.Where(t => t.Recommended && St(t) != TState.NA).ToList();
            int done = rec.Count(t => St(t) == TState.Ok);

            var score = Th.Cols(Th.Auto, Th.Star);
            score.Margin = new Thickness(0, 30, 0, 0);
            var num = new TextBlock { FontFamily = Th.Display, FontSize = 58, VerticalAlignment = VerticalAlignment.Bottom };
            num.Inlines.Add(new Run(done.ToString(Loc.Culture)) { Foreground = Th.B(Th.Text) });
            num.Inlines.Add(new Run(" / " + rec.Count.ToString(Loc.Culture)) { Foreground = Th.B(Th.Text3) });
            score.Children.Add(Th.At(num, 0));
            var sl = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(18, 0, 0, 14) };
            sl.Children.Add(Th.T(Loc.T("ov.scoreLabel"), 13, Th.Text2));
            sl.Children.Add(Th.T(done == rec.Count ? Loc.T("ov.allDone") : Loc.F("ov.left", rec.Count - done), 12.5, Th.Text3));
            score.Children.Add(Th.At(sl, 1));
            p.Children.Add(score);
            p.Children.Add(new Ruler { Total = rec.Count, Done = done, Width = 440, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) });

            var todo = rec.Where(t => St(t) == TState.NotOk).ToList();
            p.Children.Add(Section(Loc.T("ov.changes") + (todo.Count > 0 ? "  ·  " + todo.Count : "")));
            if (todo.Count == 0) p.Children.Add(Th.T(Loc.T("ov.noChanges"), 13, Th.Text2));
            else
            {
                p.Children.Add(Th.Hair(0, 0));
                foreach (var t in todo)
                {
                    var g = Th.Cols(Th.Star, Th.Auto);
                    g.Margin = new Thickness(0, 11, 0, 11);
                    var txt = new StackPanel();
                    var title = Th.T(t.Title, 13.5, Th.Text);
                    title.ToolTip = t.Details();
                    txt.Children.Add(title);
                    var d = Th.T(t.Desc, 12.5, Th.Text2); d.Margin = new Thickness(0, 2, 0, 0);
                    txt.Children.Add(d);
                    g.Children.Add(Th.At(txt, 0));
                    if (t.NeedsAdmin && !IsAdmin) { var tag = Th.T(Loc.T("tag.admin"), 12, Th.Text3); tag.Margin = new Thickness(20, 1, 0, 0); g.Children.Add(Th.At(tag, 1)); }
                    p.Children.Add(g);
                    p.Children.Add(Th.Hair(0, 0));
                }
            }

            var optPanel = new StackPanel { Margin = new Thickness(0, 22, 0, 0) };
            var rpc = Th.Check(Loc.T("ov.rp"), rp, (s, e) => rp = ((CheckBox)s).IsChecked == true);
            optPanel.Children.Add(rpc);
            var rpn = Th.T(Loc.T("ov.rpNote"), 12, Th.Text3); rpn.Margin = new Thickness(26, 2, 0, 12);
            optPanel.Children.Add(rpn);
            bool ours = Wallpaper.IsOurs();
            var wc = Th.Check(ours ? Loc.T("ov.wallSet") : Loc.T("ov.wall"), ours || wall, (s, e) => wall = ((CheckBox)s).IsChecked == true);
            wc.IsEnabled = !ours;
            optPanel.Children.Add(wc);
            var wn = Th.T(Loc.T("ov.wallNote"), 12, Th.Text3); wn.Margin = new Thickness(26, 2, 0, 0);
            optPanel.Children.Add(wn);
            p.Children.Add(optPanel);

            var bar = new WrapPanel { Margin = new Thickness(0, 24, 0, 0) };
            var apply = Th.Btn("Primary", Loc.T("ov.apply"), (s, e) => DoOptimize(false));
            apply.IsEnabled = !busy && (todo.Count > 0 || (wall && !ours));
            apply.Margin = new Thickness(0, 0, 10, 8);
            bar.Children.Add(apply);
            var rv = Th.Btn("Secondary", Loc.T("ov.revertAll"), (s, e) => DoRevertAll());
            rv.IsEnabled = !busy && scan.HasUndo;
            rv.Margin = new Thickness(0, 0, 10, 8);
            bar.Children.Add(rv);
            p.Children.Add(bar);
            if (!IsAdmin && (todo.Any(t => t.NeedsAdmin) || (rp && todo.Count > 0)))
            {
                var an = Th.T(Loc.T("ov.adminNote"), 12, Th.Text3); an.Margin = new Thickness(0, 2, 0, 0);
                p.Children.Add(an);
            }

            var hw = scan.Hw;
            p.Children.Add(Section(Loc.T("ov.system")));
            p.Children.Add(Th.Hair(0, 0));
            p.Children.Add(KV(Loc.T("hw.cpu"), ShortCpu(hw.Cpu)));
            if (!string.IsNullOrEmpty(hw.Gpu)) p.Children.Add(KV(Loc.T("hw.gpu"), hw.Gpu));
            if (hw.RamMb > 0) p.Children.Add(KV(Loc.T("hw.ram"), Math.Round(hw.RamMb / 1024.0).ToString("0", Loc.Culture) + " " + Loc.T("unit.gb")));
            p.Children.Add(KV(Loc.T("hw.windows"), hw.Windows));
            if (hw.DiskTotal > 0) p.Children.Add(KV(Loc.F("hw.disk", hw.SysDrive.TrimEnd('\\')), Loc.F("hw.diskVal", Fmt.Gb(hw.DiskFree), Fmt.Gb(hw.DiskTotal))));
            if (hw.CurHz > 1)
                p.Children.Add(KV(Loc.T("hw.display"), hw.Width + "×" + hw.Height + ", " + hw.CurHz + " " + Loc.T("unit.hz") + (hw.MaxHz - hw.CurHz >= 5 ? "  " + Loc.F("hw.maxHz", hw.MaxHz) : "")));

            p.Children.Add(Section(Loc.T("ov.quick")));
            var q = Th.Cols(Th.Star, Th.Auto);
            var qt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            qt.Children.Add(Th.T(Loc.F("ov.quickTemp", Fmt.Size(scan.TempBytes)), 13.5, Th.Text));
            if (!Cleaner.TempIsStandard)
            {
                var tp = Th.T(Loc.F("ov.quickPath", Cleaner.SafeUserTemp() ?? Path.GetTempPath()), 12.5, Th.Text3); tp.Margin = new Thickness(0, 2, 0, 0);
                qt.Children.Add(tp);
            }
            var ft = Th.T(Loc.F("ov.freedTotal", Fmt.Size(Stats.TotalFreed)), 12.5, Th.Text3); ft.Margin = new Thickness(0, 2, 0, 0);
            qt.Children.Add(ft);
            q.Children.Add(Th.At(qt, 0));
            var qb = Th.Btn("Secondary", Loc.T("ov.quickBtn"), (s, e) => DoClean(cats.Where(c => c.Id == "temp").ToList()));
            qb.IsEnabled = !busy && scan.TempBytes > 0;
            qb.Margin = new Thickness(20, 0, 0, 0); qb.VerticalAlignment = VerticalAlignment.Center;
            q.Children.Add(Th.At(qb, 1));
            p.Children.Add(q);
            return p;
        }

        // ----- Проверка -----
        FrameworkElement PageCheck()
        {
            var p = Frame("ck.title", "ck.sub");
            var rf = Th.Btn("Link", Loc.T("ck.refresh"), (s, e) => { Invalidate(); ShowPage("check"); });
            rf.Margin = new Thickness(0, 10, 0, 0);
            rf.IsEnabled = !busy;
            p.Children.Add(rf);
            if (scan == null) { p.Children.Add(Loading()); return p; }

            p.Children.Add(Section(Loc.T("ck.diag")));
            p.Children.Add(Th.Hair(0, 0));
            foreach (var d in scan.Diag)
            {
                var g = Th.Cols(Th.Px(150), Th.Star);
                g.Margin = new Thickness(0, 11, 0, 11);
                var lv = Th.T(Loc.T(d.Level == 2 ? "ck.lv.warn" : (d.Level == 1 ? "ck.lv.info" : "ck.lv.ok")), 12.5, d.Level == 2 ? Th.Warn : (d.Level == 1 ? Th.Text2 : Th.Good));
                g.Children.Add(Th.At(lv, 0));
                var sp = new StackPanel();
                sp.Children.Add(Th.T(d.Title, 13.5, Th.Text));
                var dt = Th.T(d.Text, 12.5, Th.Text2); dt.Margin = new Thickness(0, 2, 0, 0); sp.Children.Add(dt);
                if (d.ActionKey != null)
                {
                    string uri = d.ActionUri;
                    var a = Th.Btn("Link", Loc.T(d.ActionKey) + (uri.StartsWith("page:") ? "" : "  ↗"), (s, e) => Open(uri));
                    a.Margin = new Thickness(0, 6, 0, 0);
                    sp.Children.Add(a);
                }
                g.Children.Add(Th.At(sp, 1));
                p.Children.Add(g);
                p.Children.Add(Th.Hair(0, 0));
            }

            var list = tweaks.ToList();
            p.Children.Add(Section(Loc.T("ck.settings")));
            var head = Th.Cols(Th.Star, Th.Px(190), Th.Px(140));
            head.Margin = new Thickness(0, 0, 0, 8);
            head.Children.Add(Th.At(Th.T(Loc.T("ck.col.param"), 12, Th.Text3), 0));
            head.Children.Add(Th.At(Th.T(Loc.T("ck.col.state"), 12, Th.Text3), 1));
            head.Children.Add(Th.At(Th.T(Loc.T("ck.col.rec"), 12, Th.Text3), 2));
            p.Children.Add(head);
            p.Children.Add(Th.Hair(0, 0));
            foreach (var t in list)
            {
                var s = St(t);
                var g = Th.Cols(Th.Star, Th.Px(190), Th.Px(140));
                g.Margin = new Thickness(0, 10, 0, 10);
                var title = Th.T(t.Title, 13, Th.Text);
                title.ToolTip = t.Desc + "\n\n" + t.Details();
                title.Margin = new Thickness(0, 0, 16, 0);
                g.Children.Add(Th.At(title, 0));
                var st = Th.T(Loc.T(s == TState.Ok ? "st.ok" : (s == TState.NotOk ? "st.notok" : "st.na")), 13, s == TState.Ok ? Th.Good : (s == TState.NotOk ? Th.Text2 : Th.Text3));
                st.Margin = new Thickness(0, 0, 12, 0);
                g.Children.Add(Th.At(st, 1));
                g.Children.Add(Th.At(Th.T(Loc.T(t.Recommended ? "ck.rec" : "ck.optional"), 13, Th.Text3), 2));
                p.Children.Add(g);
                p.Children.Add(Th.Hair(0, 0));
            }
            var go = Th.Btn("Link", Loc.T("ck.toTweaks"), (s, e) => ShowPage("tweaks"));
            go.Margin = new Thickness(0, 16, 0, 0);
            p.Children.Add(go);
            return p;
        }

        // ----- Настройка -----
        bool Want(Tweak t)
        {
            bool w;
            return want.TryGetValue(t.Id, out w) ? w : St(t) == TState.Ok;
        }

        int Pending() { return tweaks.Count(t => St(t) != TState.NA && Want(t) != (St(t) == TState.Ok)); }

        void UpdatePending()
        {
            int n = Pending();
            if (pendingText != null) pendingText.Text = n == 0 ? Loc.T("tw.noPending") : Loc.F("tw.pending", n);
            if (pendingApply != null) pendingApply.IsEnabled = n > 0 && !busy;
            if (pendingReset != null) pendingReset.IsEnabled = n > 0 && !busy;
        }

        void ApplyPreset(string key)
        {
            preset = key;
            foreach (var t in tweaks)
            {
                if (St(t) == TState.NA) continue;
                if (key == "none") want[t.Id] = false;
                else if (t.InPreset(key)) want[t.Id] = true;
            }
            Render();
        }

        FrameworkElement PageTweaks()
        {
            var p = Frame("tw.title", "tw.sub");
            var tabs = new WrapPanel { Margin = new Thickness(0, 22, 0, 0) };
            foreach (var pr in new[] { "rec", "games", "resp", "none" })
            {
                string k = pr;
                var rb = new RadioButton { Style = Th.S("Tab"), GroupName = "preset", Content = Loc.T("pr." + k), IsChecked = preset == k, IsEnabled = !busy && scan != null };
                rb.Checked += (s, e) => { if (preset != k) ApplyPreset(k); };
                tabs.Children.Add(rb);
            }
            p.Children.Add(tabs);
            p.Children.Add(Th.Hair(0, 0));
            if (scan == null) { p.Children.Add(Loading()); return p; }

            foreach (var grp in new[] { "power", "gpu", "net", "resp", "input", "sys" })
            {
                var items = tweaks.Where(t => t.Group == grp).ToList();
                if (items.Count == 0) continue;
                p.Children.Add(Section(Loc.T("grp." + grp)));
                p.Children.Add(Th.Hair(0, 0));
                foreach (var t in items)
                {
                    var s = St(t);
                    var g = Th.Cols(Th.Star, Th.Auto);
                    g.Margin = new Thickness(0, 12, 0, 12);
                    var txt = new StackPanel { Margin = new Thickness(0, 0, 24, 0) };
                    var trow = new WrapPanel();
                    trow.Children.Add(Th.T(t.Title, 13.5, Th.Text));
                    var tags = new List<string> { Loc.T("risk." + t.Risk) };
                    if (t.NeedsAdmin) tags.Add(Loc.T("tag.admin"));
                    if (t.NeedsRestart) tags.Add(Loc.T("tag.restart"));
                    if (!t.Recommended) tags.Add(Loc.T("ck.optional"));
                    var tg = Th.T(string.Join("  ·  ", tags.ToArray()), 12, Th.Text3);
                    tg.Margin = new Thickness(12, 1, 0, 0); tg.VerticalAlignment = VerticalAlignment.Center;
                    trow.Children.Add(tg);
                    txt.Children.Add(trow);
                    var d = Th.T(s == TState.NA ? Loc.T("st.naLong") : t.Desc, 12.5, Th.Text2); d.Margin = new Thickness(0, 3, 0, 0);
                    txt.Children.Add(d);
                    if (s != TState.NA)
                    {
                        var det = Th.T(t.Details(), 11.5, Th.Text3); det.Margin = new Thickness(0, 5, 0, 0);
                        txt.Children.Add(det);
                    }
                    g.Children.Add(Th.At(txt, 0));
                    var sw = new CheckBox { Style = Th.S("Switch"), IsChecked = s != TState.NA && Want(t), IsEnabled = s != TState.NA && !busy, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
                    AutomationProperties_SetName(sw, t.Title);
                    var tw = t;
                    sw.Checked += (o, e) => { want[tw.Id] = true; UpdatePending(); };
                    sw.Unchecked += (o, e) => { want[tw.Id] = false; UpdatePending(); };
                    g.Children.Add(Th.At(sw, 1));
                    p.Children.Add(g);
                    p.Children.Add(Th.Hair(0, 0));
                }
            }

            var foot = new WrapPanel { Margin = new Thickness(0, 26, 0, 0) };
            pendingText = Th.T("", 13, Th.Text2);
            pendingText.VerticalAlignment = VerticalAlignment.Center; pendingText.Margin = new Thickness(0, 0, 20, 8);
            foot.Children.Add(pendingText);
            pendingApply = Th.Btn("Primary", Loc.T("tw.apply"), (s, e) => DoApplyPending());
            pendingApply.Margin = new Thickness(0, 0, 10, 8);
            foot.Children.Add(pendingApply);
            pendingReset = Th.Btn("Secondary", Loc.T("tw.reset"), (s, e) => { want.Clear(); preset = null; Render(); });
            pendingReset.Margin = new Thickness(0, 0, 10, 8);
            foot.Children.Add(pendingReset);
            p.Children.Add(foot);
            UpdatePending();
            return p;
        }

        static void AutomationProperties_SetName(UIElement el, string name)
        {
            System.Windows.Automation.AutomationProperties.SetName(el, name);
        }

        // ----- Очистка -----
        void UpdateCleanTotal()
        {
            if (cleanTotal == null) return;
            if (sizes == null) { cleanTotal.Text = Loc.T("cl.counting"); return; }
            long sum = cats.Where(c => cleanSel[c.Id]).Sum(c => { long v; return sizes.TryGetValue(c.Id, out v) && v > 0 ? v : 0; });
            cleanTotal.Text = Loc.F("cl.selected", Fmt.Size(sum));
        }

        FrameworkElement PageClean()
        {
            var p = Frame("cl.title", "cl.sub");
            p.Children.Add(new Border { Height = 18 });
            p.Children.Add(Th.Hair(0, 0));
            foreach (var c in cats)
            {
                var g = Th.Cols(Th.Star, Th.Px(110));
                g.Margin = new Thickness(0, 12, 0, 12);
                var txt = new StackPanel();
                var trow = new WrapPanel();
                trow.Children.Add(new TextBlock { Text = c.Title, FontSize = 13.5, TextWrapping = TextWrapping.Wrap });
                if (c.NeedsAdmin && !IsAdmin) { var tg = Th.T(Loc.T("tag.admin"), 12, Th.Text3); tg.Margin = new Thickness(12, 1, 0, 0); trow.Children.Add(tg); }
                txt.Children.Add(trow);
                var d = Th.T(c.Desc, 12.5, Th.Text2); d.Margin = new Thickness(0, 3, 0, 0); txt.Children.Add(d);
                if (c.Warn != null) { var w = Th.T(c.Warn, 12, Th.Warn); w.Margin = new Thickness(0, 4, 0, 0); txt.Children.Add(w); }
                CleanResult r;
                if (cleanDone.TryGetValue(c.Id, out r))
                {
                    string line = c.Id == "dns" ? string.Join(" ", r.Notes.ToArray()) : Loc.F("cl.done", Fmt.Size(r.Freed), r.Locked) + (r.Notes.Count > 0 ? "  " + string.Join(" ", r.Notes.ToArray()) : "");
                    var rt = Th.T(line, 12, Th.Good); rt.Margin = new Thickness(0, 4, 0, 0); txt.Children.Add(rt);
                }
                var cb = new CheckBox { Style = Th.S("Check"), IsChecked = cleanSel[c.Id], Content = txt, IsEnabled = !busy };
                string id = c.Id;
                cb.Checked += (o, e) => { cleanSel[id] = true; UpdateCleanTotal(); };
                cb.Unchecked += (o, e) => { cleanSel[id] = false; UpdateCleanTotal(); };
                g.Children.Add(Th.At(cb, 0));
                long v;
                string size = sizes == null ? "…" : (sizes.TryGetValue(c.Id, out v) ? (v < 0 ? "" : Fmt.Size(v)) : "");
                var st = Th.T(size, 13, Th.Text2); st.TextAlignment = TextAlignment.Right; st.HorizontalAlignment = HorizontalAlignment.Right;
                g.Children.Add(Th.At(st, 1));
                p.Children.Add(g);
                p.Children.Add(Th.Hair(0, 0));
            }

            var foot = Th.Cols(Th.Star, Th.Auto);
            foot.Margin = new Thickness(0, 22, 0, 0);
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            cleanTotal = Th.T("", 13, Th.Text);
            left.Children.Add(cleanTotal);
            var ss = Th.Btn("Link", Loc.T("cl.storageSense") + "  ↗", (s, e) => Open("ms-settings:storagesense"));
            ss.Margin = new Thickness(0, 4, 0, 0);
            left.Children.Add(ss);
            foot.Children.Add(Th.At(left, 0));
            var run = Th.Btn("Primary", Loc.T("cl.run"), (s, e) => DoClean(cats.Where(c => cleanSel[c.Id]).ToList()));
            run.IsEnabled = !busy && sizes != null;
            run.Margin = new Thickness(20, 0, 0, 0); run.VerticalAlignment = VerticalAlignment.Center;
            foot.Children.Add(Th.At(run, 1));
            p.Children.Add(foot);
            UpdateCleanTotal();
            return p;
        }

        // ----- Журнал -----
        FrameworkElement PageJournal()
        {
            var p = Frame("jr.title", "jr.sub");
            var lines = Journal.Tail(400).Reverse().ToArray();
            var bar = new WrapPanel { Margin = new Thickness(0, 20, 0, 6) };
            var of = Th.Btn("Secondary", Loc.T("ab.openFolder"), (s, e) => Open(AppInfo.DataDir)); of.Margin = new Thickness(0, 0, 10, 8); bar.Children.Add(of);
            var cp = Th.Btn("Secondary", Loc.T("jr.copy"), (s, e) => { try { Clipboard.SetText(string.Join(Environment.NewLine, lines.Reverse().ToArray())); lastMessage = Loc.T("jr.copied"); UpdateStatus(); } catch { } });
            cp.IsEnabled = lines.Length > 0; cp.Margin = new Thickness(0, 0, 10, 8); bar.Children.Add(cp);
            p.Children.Add(bar);
            p.Children.Add(Th.Hair(8, 0));
            if (lines.Length == 0) { var e0 = Th.T(Loc.T("jr.empty"), 13, Th.Text3); e0.Margin = new Thickness(0, 14, 0, 0); p.Children.Add(e0); return p; }
            foreach (var l in lines)
            {
                int i = l.IndexOf("   ");
                var g = Th.Cols(Th.Px(140), Th.Star);
                g.Margin = new Thickness(0, 7, 0, 7);
                g.Children.Add(Th.At(Th.T(i > 0 ? l.Substring(0, i) : "", 12, Th.Text3), 0));
                g.Children.Add(Th.At(Th.T(i > 0 ? l.Substring(i + 3) : l, 12.5, Th.Text), 1));
                p.Children.Add(g);
            }
            return p;
        }

        // ----- PRO -----
        FrameworkElement PagePro()
        {
            var p = Frame("pro.title", "pro.sub");
            var intro = Th.T(Loc.T("pro.text"), 13.5, Th.Text2); intro.Margin = new Thickness(0, 18, 0, 0);
            p.Children.Add(intro);
            p.Children.Add(Section(Loc.T("pro.tools")));
            p.Children.Add(Th.Hair(0, 0));
            var grid = Th.Cols(Th.Star, Th.Star);
            for (int i = 0; i < 10; i++) grid.RowDefinitions.Add(new RowDefinition { Height = Th.Auto });
            for (int i = 0; i < 10; i++)
            {
                var sp = new StackPanel { Margin = new Thickness(0, 12, 28, 12) };
                sp.Children.Add(Th.T(Loc.T("pro.t" + (i + 1)), 13.5, Th.Text));
                var d = Th.T(Loc.T("pro.t" + (i + 1) + ".d"), 12.5, Th.Text2); d.Margin = new Thickness(0, 2, 0, 0);
                sp.Children.Add(d);
                Grid.SetRow(sp, i / 2); Grid.SetColumn(sp, i % 2);
                grid.Children.Add(sp);
            }
            p.Children.Add(grid);
            p.Children.Add(Th.Hair(0, 0));
            var price = Th.T(Loc.T("pro.price"), 13.5, Th.Text); price.Margin = new Thickness(0, 22, 0, 0);
            p.Children.Add(price);
            var bar = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
            var open = Th.Btn("Primary", Loc.T("pro.open"), (s, e) => Open(AppInfo.ProUrl)); open.Margin = new Thickness(0, 0, 14, 8);
            bar.Children.Add(open);
            var note = Th.T(Loc.T("pro.note"), 12, Th.Text3); note.VerticalAlignment = VerticalAlignment.Center; note.Margin = new Thickness(0, 0, 0, 8);
            bar.Children.Add(note);
            p.Children.Add(bar);
            return p;
        }

        // ----- О программе -----
        FrameworkElement PageAbout()
        {
            var p = Frame("ab.title", "ab.sub");
            p.Children.Add(new Border { Height = 18 });
            p.Children.Add(Th.Hair(0, 0));
            p.Children.Add(KV(Loc.T("ab.version"), AppInfo.Version));
            p.Children.Add(KV(Loc.T("ab.publisher"), "BUL$ Opti"));
            p.Children.Add(KV(Loc.T("ab.license"), "MIT"));
            p.Children.Add(KV(Loc.T("ab.mode"), IsAdmin ? Loc.T("mode.admin") : Loc.T("mode.user")));
            var links = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            var src = Th.Btn("Link", Loc.T("ab.source") + "  ↗", (s, e) => Open(AppInfo.SourceUrl)); src.Margin = new Thickness(0, 0, 24, 0); links.Children.Add(src);
            var site = Th.Btn("Link", "bulsopti.com  ↗", (s, e) => Open(AppInfo.SiteUrl)); links.Children.Add(site);
            p.Children.Add(links);

            p.Children.Add(Section(Loc.T("ab.does")));
            for (int i = 1; i <= 5; i++) { var t = Th.T("—  " + Loc.T("ab.does" + i), 13, Th.Text2); t.Margin = new Thickness(0, 4, 0, 4); p.Children.Add(t); }

            p.Children.Add(Section(Loc.T("ab.files")));
            p.Children.Add(Th.T(AppInfo.DataDir, 12.5, Th.Text2));
            var bar = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            var of = Th.Btn("Secondary", Loc.T("ab.openFolder"), (s, e) => Open(AppInfo.DataDir)); of.Margin = new Thickness(0, 0, 10, 8); bar.Children.Add(of);
            bool hasUndo = Undo.Ids().Length > 0;
            var rm = Th.Btn("Secondary", Loc.T("ab.remove"), (s, e) => Confirm(Loc.T("ab.remove"), Loc.T("ab.removeText"), Loc.T("ab.removeYes"), () =>
            {
                AppInfo.RemoveTraces();
                lastMessage = Loc.T("ab.removed"); Render();
            }));
            rm.IsEnabled = !busy && !hasUndo; rm.Margin = new Thickness(0, 0, 10, 8);
            bar.Children.Add(rm);
            p.Children.Add(bar);
            var rn = Th.T(hasUndo ? Loc.T("ab.removeBlocked") : Loc.T("ab.removeNote"), 12, Th.Text3);
            p.Children.Add(rn);
            return p;
        }
    }
}
