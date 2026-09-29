using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace BulsOpti
{
    // ---------- Модель твика ----------
    class Tweak
    {
        public string Id, Group, Title, Benefit, Risk;
        public bool DefaultOn = true;
        public string[] BackupKeys = new string[0];
        public Action<Action<string>> Apply;
        public Action<Action<string>> Revert;
        // Проверка текущего состояния для скана: true=оптимально, false=нет, null=действие (нет состояния)
        public Func<bool?> IsOptimized;
    }

    // Категория глубокой очистки (вкладка «Очистка»)
    class CleanCat
    {
        public string Id, Title, Note;
        public bool DefaultOn = true;
        public Func<long> Size;                 // оценка размера в байтах, -1 = без размера
        public Func<Action<string>, long> Clean; // очистка, возвращает освобождённые байты
    }

    static class Reg
    {
        static RegistryKey Root(string r)
        {
            if (r == "HKLM") return Registry.LocalMachine;
            if (r == "HKCU") return Registry.CurrentUser;
            throw new ArgumentException(r);
        }
        public static void SetDword(string root, string path, string name, int val)
        { using (var k = Root(root).CreateSubKey(path)) k.SetValue(name, unchecked(val), RegistryValueKind.DWord); }
        public static void SetDwordU(string root, string path, string name, uint val)
        { using (var k = Root(root).CreateSubKey(path)) k.SetValue(name, unchecked((int)val), RegistryValueKind.DWord); }
        public static void SetString(string root, string path, string name, string val)
        { using (var k = Root(root).CreateSubKey(path)) k.SetValue(name, val, RegistryValueKind.String); }
        public static void DeleteValue(string root, string path, string name)
        { using (var k = Root(root).OpenSubKey(path, true)) { if (k != null) k.DeleteValue(name, false); } }
        public static string GetString(string root, string path, string name)
        { using (var k = Root(root).OpenSubKey(path)) { return k == null ? null : k.GetValue(name) as string; } }
        public static int? GetDword(string root, string path, string name)
        { using (var k = Root(root).OpenSubKey(path)) { if (k == null) return null; var v = k.GetValue(name); if (v == null) return null; try { return Convert.ToInt32(v); } catch { return null; } } }
    }

    static class Sys
    {
        public static int Run(string exe, string args, Action<string> log)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    string e = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    return p.ExitCode;
                }
            }
            catch (Exception ex) { if (log != null) log("    ! " + exe + ": " + ex.Message); return -1; }
        }

        public static string RunOut(string exe, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(psi)) { string o = p.StandardOutput.ReadToEnd(); p.WaitForExit(); return o; }
            }
            catch { return ""; }
        }

        [StructLayout(LayoutKind.Sequential)]
        class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX m);
        public static long TotalRamMb()
        {
            try { var m = new MEMORYSTATUSEX(); m.dwLength = (uint)Marshal.SizeOf(m); if (GlobalMemoryStatusEx(m)) return (long)(m.ullTotalPhys / 1024 / 1024); } catch { }
            return 0;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern int SystemParametersInfo(int a, int b, string c, int d);
        public static void SetWallpaper(string path)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop"))
            { k.SetValue("WallpaperStyle", "10"); k.SetValue("TileWallpaper", "0"); }
            SystemParametersInfo(20, 0, path, 3);
        }
    }

    static class Program
    {
        public static long LastFreedBytes; // сколько освободила последняя очистка temp

        // ---------- Быстрая чистка + счётчик «за всё время» ----------
        public static long CleanNow(Action<string> log) { LastFreedBytes = 0; CleanTemp(log); return LastFreedBytes; }
        public static long GetTotalFreedMb()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(@"Software\BULS Opti")) { if (k == null) return 0; var v = k.GetValue("TotalFreedMB"); return v == null ? 0 : Convert.ToInt64(v); } }
            catch { return 0; }
        }
        public static void AddTotalFreedMb(long mb)
        {
            if (mb <= 0) return;
            try { long cur = GetTotalFreedMb(); using (var k = Registry.CurrentUser.CreateSubKey(@"Software\BULS Opti")) k.SetValue("TotalFreedMB", cur + mb, RegistryValueKind.QWord); }
            catch { }
        }

        // ---------- Чтение состояния (для скана) ----------
        public static bool IsHighPerfPlan()
        { return Sys.RunOut("powercfg", "/getactivescheme").IndexOf("8c5e7fda", StringComparison.OrdinalIgnoreCase) >= 0; }

        public static bool NicIsOff()
        {
            try
            {
                string cls = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";
                using (var root = Registry.LocalMachine.OpenSubKey(cls))
                {
                    if (root == null) return false;
                    foreach (var sub in root.GetSubKeyNames())
                    {
                        if (sub.Length != 4) continue;
                        using (var k = root.OpenSubKey(sub))
                        { if (k == null) continue; var v = k.GetValue("PnPCapabilities"); if (v != null && Convert.ToInt32(v) == 24) return true; }
                    }
                }
            }
            catch { }
            return false;
        }

        public static long TempJunkMb()
        {
            long total = 0;
            foreach (var t in new[] { Path.GetTempPath(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp") })
            {
                if (!Directory.Exists(t)) continue;
                foreach (var f in SafeEnum(t)) { try { total += new FileInfo(f).Length; } catch { } }
            }
            return total / 1024 / 1024;
        }

        public static string[] Hardware()
        {
            string cpu = (Reg.GetString("HKLM", @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString") ?? "CPU").Trim();
            long ram = Sys.TotalRamMb();
            string ramStr = ram > 0 ? (Math.Round(ram / 1024.0) + " ГБ ОЗУ") : "ОЗУ —";
            string winName = Reg.GetString("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName") ?? "Windows";
            string build = Reg.GetString("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuild") ?? "";
            string disp = Reg.GetString("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion") ?? "";
            string win = winName + (string.IsNullOrEmpty(disp) ? "" : " " + disp) + (string.IsNullOrEmpty(build) ? "" : " · сборка " + build);
            string disk;
            try { var d = new DriveInfo("C"); disk = "Диск C: " + (d.AvailableFreeSpace / 1073741824) + " / " + (d.TotalSize / 1073741824) + " ГБ свободно"; }
            catch { disk = "Диск C: —"; }
            return new[] { cpu, ramStr, win, disk };
        }

        // ---------- Глубокая очистка (вкладка «Очистка») ----------
        [StructLayout(LayoutKind.Sequential)]
        struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHQueryRecycleBin(string pszRootPath, ref SHQUERYRBINFO info);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, uint dwFlags);

        static string WinDir { get { return Environment.GetFolderPath(Environment.SpecialFolder.Windows); } }
        static string ThumbDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Explorer"); } }

        public static long DirSize(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;
            long t = 0; foreach (var f in SafeEnum(dir)) { try { t += new FileInfo(f).Length; } catch { } } return t;
        }
        public static long DeleteInDir(string dir, Action<string> log)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;
            long freed = 0;
            foreach (var f in SafeEnum(dir)) { try { var fi = new FileInfo(f); long s = fi.Exists ? fi.Length : 0; File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); freed += s; } catch { } }
            foreach (var d in SafeDirs(dir)) { try { Directory.Delete(d, true); } catch { } }
            return freed;
        }
        public static long DirsSize(IEnumerable<string> dirs) { long t = 0; foreach (var d in dirs) t += DirSize(d); return t; }
        public static long DeleteInDirs(IEnumerable<string> dirs, Action<string> log) { long f = 0; foreach (var d in dirs) f += DeleteInDir(d, log); return f; }
        public static long DeleteFile(string path)
        {
            try { if (File.Exists(path)) { long s = new FileInfo(path).Length; File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); return s; } } catch { }
            return 0;
        }
        public static long FileSize(string path) { try { if (File.Exists(path)) return new FileInfo(path).Length; } catch { } return 0; }
        static long ThumbSize()
        {
            string c = ThumbDir; if (!Directory.Exists(c)) return 0; long t = 0;
            foreach (var pat in new[]{ "thumbcache_*.db", "iconcache_*.db" }) { try { foreach (var f in Directory.GetFiles(c, pat)) { try { t += new FileInfo(f).Length; } catch { } } } catch { } }
            return t;
        }
        static long RecycleSize()
        {
            try { var info = new SHQUERYRBINFO(); info.cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO)); if (SHQueryRecycleBin(null, ref info) == 0) return info.i64Size; } catch { }
            return 0;
        }
        static long RecycleEmpty(Action<string> log)
        {
            long s = RecycleSize();
            try { SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4); } catch (Exception ex) { if (log != null) log("    ! " + ex.Message); }
            if (log != null) log("    корзина очищена (~" + (s/1024/1024) + " МБ)");
            return s;
        }

        public static List<CleanCat> BuildCleanCats()
        {
            string LAD = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string PROG = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string WIN = WinDir;
            string t1 = Path.GetTempPath();
            string t2 = Path.Combine(WIN, "Temp");
            string wuDl = Path.Combine(WIN, @"SoftwareDistribution\Download");
            string memdmp = Path.Combine(WIN, "MEMORY.DMP");

            var browser = new List<string>();
            foreach (var p in new[]{
                @"Google\Chrome\User Data\Default\Cache", @"Google\Chrome\User Data\Default\Code Cache", @"Google\Chrome\User Data\Default\GPUCache",
                @"Microsoft\Edge\User Data\Default\Cache", @"Microsoft\Edge\User Data\Default\Code Cache", @"Microsoft\Edge\User Data\Default\GPUCache",
                @"BraveSoftware\Brave-Browser\User Data\Default\Cache",
                @"Opera Software\Opera Stable\Cache", @"Opera Software\Opera GX Stable\Cache",
                @"Yandex\YandexBrowser\User Data\Default\Cache" })
                browser.Add(Path.Combine(LAD, p));
            string ffRoot = Path.Combine(LAD, @"Mozilla\Firefox\Profiles");
            if (Directory.Exists(ffRoot)) { try { foreach (var prof in Directory.GetDirectories(ffRoot)) browser.Add(Path.Combine(prof, "cache2")); } catch { } }

            var shaders = new List<string>{
                Path.Combine(LAD, "D3DSCache"), Path.Combine(LAD, @"Microsoft\DirectX Shader Cache"),
                Path.Combine(LAD, @"NVIDIA\DXCache"), Path.Combine(LAD, @"NVIDIA\GLCache"), Path.Combine(PROG, @"NVIDIA Corporation\NV_Cache"),
                Path.Combine(LAD, @"AMD\DxCache"), Path.Combine(LAD, @"AMD\GLCache") };

            var dumps = new List<string>{
                Path.Combine(LAD, "CrashDumps"),
                Path.Combine(PROG, @"Microsoft\Windows\WER\ReportQueue"), Path.Combine(PROG, @"Microsoft\Windows\WER\ReportArchive"),
                Path.Combine(WIN, "Minidump") };

            var wincache = new List<string>{
                Path.Combine(LAD, @"Microsoft\Windows\INetCache"), Path.Combine(WIN, "Downloaded Program Files") };

            var doCache = new List<string>{
                Path.Combine(WIN, @"SoftwareDistribution\DeliveryOptimization"),
                Path.Combine(WIN, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache") };

            var L = new List<CleanCat>();
            L.Add(new CleanCat { Id="temp", Title="Временные файлы", Note="%TEMP% и Windows\\Temp",
                Size=()=> DirSize(t1) + DirSize(t2), Clean=log=> DeleteInDir(t1, log) + DeleteInDir(t2, log) });
            L.Add(new CleanCat { Id="recycle", Title="Корзина", Note="все диски",
                Size=()=> RecycleSize(), Clean=log=> RecycleEmpty(log) });
            L.Add(new CleanCat { Id="browser", Title="Кэш браузеров", Note="Chrome, Edge, Brave, Opera, Firefox · закрой браузер",
                Size=()=> DirsSize(browser), Clean=log=> DeleteInDirs(browser, log) });
            L.Add(new CleanCat { Id="shaders", Title="Кэш шейдеров GPU", Note="DirectX, NVIDIA, AMD · пересоздастся",
                Size=()=> DirsSize(shaders), Clean=log=> DeleteInDirs(shaders, log) });
            L.Add(new CleanCat { Id="wu", Title="Кэш обновлений Windows", Note="SoftwareDistribution\\Download",
                Size=()=> DirSize(wuDl), Clean=log=> DeleteInDir(wuDl, log) });
            L.Add(new CleanCat { Id="do", Title="Кэш доставки обновлений", Note="Delivery Optimization",
                Size=()=> DirsSize(doCache), Clean=log=> DeleteInDirs(doCache, log) });
            L.Add(new CleanCat { Id="dumps", Title="Отчёты об ошибках и дампы", Note="WER, CrashDumps, Minidump, MEMORY.DMP",
                Size=()=> DirsSize(dumps) + FileSize(memdmp), Clean=log=> DeleteInDirs(dumps, log) + DeleteFile(memdmp) });
            L.Add(new CleanCat { Id="wincache", Title="Системный кэш Windows", Note="INetCache, Downloaded Program Files",
                Size=()=> DirsSize(wincache), Clean=log=> DeleteInDirs(wincache, log) });
            L.Add(new CleanCat { Id="prefetch", Title="Prefetch", Note="Windows\\Prefetch",
                Size=()=> DirSize(Path.Combine(WIN,"Prefetch")), Clean=log=> DeleteInDir(Path.Combine(WIN,"Prefetch"), log) });
            L.Add(new CleanCat { Id="logs", Title="Логи Windows", Note="Windows\\Logs",
                Size=()=> DirSize(Path.Combine(WIN,"Logs")), Clean=log=> DeleteInDir(Path.Combine(WIN,"Logs"), log) });
            L.Add(new CleanCat { Id="thumbs", Title="Кэш эскизов и иконок", Note="перезапустит Проводник", DefaultOn=false,
                Size=()=> ThumbSize(), Clean=log=>{ long s = ThumbSize(); ClearThumbs(log); return s; } });
            L.Add(new CleanCat { Id="dns", Title="Сбросить кэш DNS", Note="ipconfig /flushdns",
                Size=()=> -1, Clean=log=>{ Sys.Run("ipconfig", "/flushdns", log); return 0; } });
            return L;
        }

        // ---------- Набор твиков ----------
        public static List<Tweak> BuildTweaks()
        {
            var L = new List<Tweak>();

            L.Add(new Tweak {
                Id="power-plan", Group="Питание и CPU", Title="План питания «Высокая производительность»",
                Benefit="Убирает агрессивное снижение частоты CPU и парковку ядер — стабильнее 1% low, отзывчивее ввод.", Risk="низкий",
                Apply=log=>{ Sys.Run("powercfg","-setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c",log); log("    план: Высокая производительность"); },
                Revert=log=>{ Sys.Run("powercfg","-setactive 381b4222-f694-41f0-9685-ff5bb260df2e",log); log("    план: Сбалансированная (дефолт)"); },
                IsOptimized=()=> IsHighPerfPlan()
            });
            L.Add(new Tweak {
                Id="game-dvr-off", Group="Графика и GPU", Title="Отключить фоновую запись Game DVR",
                Benefit="Убирает фоновый захват Xbox-энкодера — ровнее фреймтайм, меньше микрофризов в тяжёлых сценах.", Risk="низкий",
                BackupKeys=new[]{@"HKCU\System\GameConfigStore", @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR"},
                Apply=log=>{ Reg.SetDword("HKCU",@"System\GameConfigStore","GameDVR_Enabled",0); Reg.SetDword("HKCU",@"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR","AppCaptureEnabled",0); Reg.SetDword("HKLM",@"SOFTWARE\Policies\Microsoft\Windows\GameDVR","AllowGameDVR",0); log("    Game DVR фоновая запись выключена"); },
                Revert=log=>{ Reg.SetDword("HKCU",@"System\GameConfigStore","GameDVR_Enabled",1); Reg.SetDword("HKCU",@"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR","AppCaptureEnabled",1); Reg.DeleteValue("HKLM",@"SOFTWARE\Policies\Microsoft\Windows\GameDVR","AllowGameDVR"); log("    Game DVR возвращён к дефолту"); },
                IsOptimized=()=> Reg.GetDword("HKCU",@"System\GameConfigStore","GameDVR_Enabled")==0
            });
            L.Add(new Tweak {
                Id="windowed-flip-vrr", Group="Графика и GPU", Title="Оптимизация оконных игр (flip) + VRR",
                Benefit="Flip-модель для оконных/безрамочных игр снижает задержку ввода; VRR убирает тиринг на G-Sync/FreeSync мониторе.", Risk="низкий",
                BackupKeys=new[]{@"HKCU\SOFTWARE\Microsoft\DirectX\UserGpuPreferences"},
                Apply=log=>{ SetGpuPref("SwapEffectUpgradeEnable","1"); SetGpuPref("VRROptimizeEnable","1"); log("    оконная flip-оптимизация + VRR включены"); },
                Revert=log=>{ RemoveGpuPref("SwapEffectUpgradeEnable"); RemoveGpuPref("VRROptimizeEnable"); log("    вернул к дефолту"); },
                IsOptimized=()=>{ var s = Reg.GetString("HKCU",@"SOFTWARE\Microsoft\DirectX\UserGpuPreferences","DirectXUserGlobalSettings") ?? ""; return s.Contains("SwapEffectUpgradeEnable=1") && s.Contains("VRROptimizeEnable=1"); }
            });
            L.Add(new Tweak {
                Id="game-mode", Group="Графика и GPU", Title="Игровой режим Windows (Game Mode)",
                Benefit="Приоритезирует активную игру и откладывает установку драйверов/перезагрузку Windows Update во время игры.", Risk="нет",
                Apply=log=>{ Reg.SetDword("HKCU",@"Software\Microsoft\GameBar","AutoGameModeEnabled",1); log("    Game Mode включён"); },
                Revert=log=>{ Reg.DeleteValue("HKCU",@"Software\Microsoft\GameBar","AutoGameModeEnabled"); log("    Game Mode -> дефолт"); },
                IsOptimized=()=> Reg.GetDword("HKCU",@"Software\Microsoft\GameBar","AutoGameModeEnabled")==1
            });
            L.Add(new Tweak {
                Id="net-throttling", Group="Сеть", Title="Отключить сетевой троттлинг (MMCSS)",
                Benefit="Снимает лимит ~10 пакетов/мс под мультимедиа-нагрузкой — меньше сетевого джиттера при игре со звуком/голосом.", Risk="низкий",
                BackupKeys=new[]{@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"},
                Apply=log=>{ Reg.SetDwordU("HKLM",@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile","NetworkThrottlingIndex",0xFFFFFFFF); log("    NetworkThrottlingIndex=off"); },
                Revert=log=>{ Reg.SetDword("HKLM",@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile","NetworkThrottlingIndex",10); log("    NetworkThrottlingIndex=10 (дефолт)"); },
                IsOptimized=()=> Reg.GetDword("HKLM",@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile","NetworkThrottlingIndex")==-1
            });
            L.Add(new Tweak {
                Id="nic-power-off", Group="Сеть", Title="Отключить энергосбережение сетевой карты",
                Benefit="Не даёт адаптеру засыпать (selective suspend) — стабильнее пинг, нет рывков при возврате линка.", Risk="низкий",
                Apply=log=>{ NicPower(24, log); }, Revert=log=>{ NicPower(-1, log); },
                IsOptimized=()=> NicIsOff()
            });
            L.Add(new Tweak {
                Id="delivery-p2p-off", Group="Сеть", Title="Отключить P2P-раздачу обновлений",
                Benefit="Windows перестаёт в фоне раздавать обновления другим ПК — освобождает исходящий канал, меньше bufferbloat в онлайне.", Risk="низкий",
                BackupKeys=new[]{@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization"},
                Apply=log=>{ Reg.SetDword("HKLM",@"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization","DODownloadMode",0); log("    P2P-раздача выключена"); },
                Revert=log=>{ Reg.DeleteValue("HKLM",@"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization","DODownloadMode"); log("    -> дефолт"); },
                IsOptimized=()=> Reg.GetDword("HKLM",@"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization","DODownloadMode")==0
            });
            L.Add(new Tweak {
                Id="flush-dns", Group="Сеть", Title="Сбросить кэш DNS",
                Benefit="Убирает устаревшие/битые DNS-записи — меньше таймаутов при коннекте к игровым серверам.", Risk="нет",
                Apply=log=>{ Sys.Run("ipconfig","/flushdns",log); log("    кэш DNS сброшен"); },
                Revert=log=>{ log("    (нечего откатывать — кэш наполнится сам)"); }
            });
            L.Add(new Tweak {
                Id="system-responsiveness", Group="Отзывчивость", Title="SystemResponsiveness = 10",
                Benefit="Уменьшает резерв CPU под фоновые задачи с 20% до 10% — больше времени переднему плану (игре и звуку).", Risk="низкий",
                BackupKeys=new[]{@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"},
                Apply=log=>{ Reg.SetDword("HKLM",@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile","SystemResponsiveness",10); log("    SystemResponsiveness=10"); },
                Revert=log=>{ Reg.SetDword("HKLM",@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile","SystemResponsiveness",20); log("    =20 (дефолт)"); },
                IsOptimized=()=> Reg.GetDword("HKLM",@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile","SystemResponsiveness")==10
            });
            L.Add(new Tweak {
                Id="menu-delay", Group="Отзывчивость", Title="Ускорить появление меню (100 мс)",
                Benefit="Задержка меню Пуск/контекстных меню с 400 до 100 мс — интерфейс ощутимо отзывчивее.", Risk="нет",
                BackupKeys=new[]{@"HKCU\Control Panel\Desktop"},
                Apply=log=>{ Reg.SetString("HKCU",@"Control Panel\Desktop","MenuShowDelay","100"); log("    MenuShowDelay=100"); },
                Revert=log=>{ Reg.SetString("HKCU",@"Control Panel\Desktop","MenuShowDelay","400"); log("    =400 (дефолт)"); },
                IsOptimized=()=>{ var v = Reg.GetString("HKCU",@"Control Panel\Desktop","MenuShowDelay"); int n; return int.TryParse(v, out n) && n <= 150; }
            });
            L.Add(new Tweak {
                Id="ui-animations-off", Group="Отзывчивость", Title="Отключить анимации окон (ClearType сохранён)",
                Benefit="Мгновенный alt-tab и переключение окон. Сглаживание шрифтов и эскизы файлов НЕ трогаются.", Risk="нет",
                BackupKeys=new[]{@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", @"HKCU\Control Panel\Desktop\WindowMetrics"},
                Apply=log=>{ Reg.SetDword("HKCU",@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced","TaskbarAnimations",0); Reg.SetString("HKCU",@"Control Panel\Desktop\WindowMetrics","MinAnimate","0"); log("    анимации окон выключены"); },
                Revert=log=>{ Reg.SetDword("HKCU",@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced","TaskbarAnimations",1); Reg.SetString("HKCU",@"Control Panel\Desktop\WindowMetrics","MinAnimate","1"); log("    анимации вернул"); },
                IsOptimized=()=> Reg.GetString("HKCU",@"Control Panel\Desktop\WindowMetrics","MinAnimate")=="0"
            });
            L.Add(new Tweak {
                Id="startup-delay-0", Group="Отзывчивость", Title="Убрать задержку автозагрузки",
                Benefit="Windows держит ~10 сек паузы перед стартом программ автозагрузки. Убираем — Discord/лаунчер готовы быстрее.", Risk="низкий",
                Apply=log=>{ Reg.SetDword("HKCU",@"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize","StartupDelayInMSec",0); log("    задержка автозагрузки=0"); },
                Revert=log=>{ Reg.DeleteValue("HKCU",@"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize","StartupDelayInMSec"); log("    -> дефолт"); },
                IsOptimized=()=> Reg.GetDword("HKCU",@"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize","StartupDelayInMSec")==0
            });
            L.Add(new Tweak {
                Id="temp-clean", Group="Очистка", Title="Очистить временные файлы",
                Benefit="Освобождает место на SSD (мусор из %TEMP% и Windows\\Temp). Занятые файлы пропускаются.", Risk="низкий",
                Apply=log=>{ CleanTemp(log); }, Revert=log=>{ log("    (нечего откатывать — файлы временные)"); }
            });
            L.Add(new Tweak {
                Id="thumb-cache", Group="Очистка", Title="Очистить кэш эскизов Explorer",
                Benefit="Устраняет подтормаживания Проводника из-за битого кэша миниатюр. Explorer перезапустится (мигнёт панель задач).", Risk="низкий", DefaultOn=false,
                Apply=log=>{ ClearThumbs(log); }, Revert=log=>{ log("    (кэш пересоздаётся сам)"); }
            });
            L.Add(new Tweak {
                Id="storage-sense", Group="Очистка", Title="Включить Контроль памяти (Storage Sense)",
                Benefit="Раз в неделю авто-чистит temp и корзину (старше 30 дней). Папку «Загрузки» НЕ трогает.", Risk="низкий", DefaultOn=false,
                BackupKeys=new[]{@"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy"},
                Apply=log=>{ string sp=@"SOFTWARE\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy"; Reg.SetDword("HKCU",sp,"01",1); Reg.SetDword("HKCU",sp,"04",1); Reg.SetDword("HKCU",sp,"08",1); Reg.SetDword("HKCU",sp,"256",30); Reg.SetDword("HKCU",sp,"2048",7); log("    Storage Sense: еженедельно, temp + корзина >30д"); },
                Revert=log=>{ Reg.SetDword("HKCU",@"SOFTWARE\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy","01",0); log("    Storage Sense выключен (дефолт)"); },
                IsOptimized=()=> Reg.GetDword("HKCU",@"SOFTWARE\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy","01")==1
            });
            L.Add(new Tweak {
                Id="no-promo-apps", Group="Система", Title="Отключить авто-установку рекламных приложений",
                Benefit="Запрещает Windows тихо докачивать промо-приложения и игры (Candy Crush и пр.) в Пуск и автозагрузку.", Risk="низкий",
                BackupKeys=new[]{@"HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent"},
                Apply=log=>{ Reg.SetDword("HKLM",@"SOFTWARE\Policies\Microsoft\Windows\CloudContent","DisableWindowsConsumerFeatures",1); log("    промо-приложения выключены"); },
                Revert=log=>{ Reg.DeleteValue("HKLM",@"SOFTWARE\Policies\Microsoft\Windows\CloudContent","DisableWindowsConsumerFeatures"); log("    -> дефолт"); },
                IsOptimized=()=> Reg.GetDword("HKLM",@"SOFTWARE\Policies\Microsoft\Windows\CloudContent","DisableWindowsConsumerFeatures")==1
            });
            return L;
        }

        static void SetGpuPref(string key, string val)
        {
            string p = @"SOFTWARE\Microsoft\DirectX\UserGpuPreferences";
            string cur = Reg.GetString("HKCU", p, "DirectXUserGlobalSettings") ?? "";
            var map = ParsePairs(cur); map[key] = val;
            Reg.SetString("HKCU", p, "DirectXUserGlobalSettings", JoinPairs(map));
        }
        static void RemoveGpuPref(string key)
        {
            string p = @"SOFTWARE\Microsoft\DirectX\UserGpuPreferences";
            string cur = Reg.GetString("HKCU", p, "DirectXUserGlobalSettings"); if (cur == null) return;
            var map = ParsePairs(cur); map.Remove(key);
            if (map.Count == 0) Reg.DeleteValue("HKCU", p, "DirectXUserGlobalSettings");
            else Reg.SetString("HKCU", p, "DirectXUserGlobalSettings", JoinPairs(map));
        }
        static Dictionary<string,string> ParsePairs(string s)
        { var d = new Dictionary<string,string>(); foreach (var kv in s.Split(new[]{';'}, StringSplitOptions.RemoveEmptyEntries)) { int i = kv.IndexOf('='); if (i>0) d[kv.Substring(0,i).Trim()] = kv.Substring(i+1).Trim(); } return d; }
        static string JoinPairs(Dictionary<string,string> d) { var sb=new StringBuilder(); foreach (var kv in d) sb.Append(kv.Key+"="+kv.Value+";"); return sb.ToString(); }

        static void NicPower(int val, Action<string> log)
        {
            string cls = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";
            int n=0;
            using (var root = Registry.LocalMachine.OpenSubKey(cls, true))
            {
                if (root == null) { log("    ! класс сетевых адаптеров не найден"); return; }
                foreach (var sub in root.GetSubKeyNames())
                {
                    if (sub.Length != 4) continue;
                    using (var k = root.OpenSubKey(sub, true))
                    {
                        if (k == null) continue;
                        var inst = k.GetValue("NetCfgInstanceId") as string;
                        var desc = k.GetValue("DriverDesc") as string;
                        if (string.IsNullOrEmpty(inst) || string.IsNullOrEmpty(desc)) continue;
                        if (val >= 0) { k.SetValue("PnPCapabilities", val, RegistryValueKind.DWord); n++; }
                        else { k.DeleteValue("PnPCapabilities", false); n++; }
                    }
                }
            }
            log(val >= 0 ? ("    энергосбережение выключено на адаптерах: "+n) : ("    возвращено на адаптерах: "+n));
        }

        static void CleanTemp(Action<string> log)
        {
            long freed = 0; int files = 0;
            foreach (var t in new[]{ Path.GetTempPath(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"Temp") })
            {
                if (!Directory.Exists(t)) continue;
                foreach (var entry in SafeEnum(t))
                { try { var fi=new FileInfo(entry); long s=fi.Exists?fi.Length:0; File.SetAttributes(entry, FileAttributes.Normal); File.Delete(entry); freed+=s; files++; } catch { } }
                foreach (var dsub in SafeDirs(t)) { try { Directory.Delete(dsub, true); } catch { } }
            }
            LastFreedBytes += freed;
            log("    удалено файлов: "+files+", освобождено ~"+(freed/1024/1024)+" МБ");
        }
        static IEnumerable<string> SafeEnum(string root)
        { string[] a; try { a=Directory.GetFiles(root,"*",SearchOption.AllDirectories); } catch { try { a=Directory.GetFiles(root); } catch { a=new string[0]; } } return a; }
        static IEnumerable<string> SafeDirs(string root)
        { try { return Directory.GetDirectories(root); } catch { return new string[0]; } }

        static void ClearThumbs(Action<string> log)
        {
            string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Explorer");
            if (!Directory.Exists(cache)) { log("    кэш эскизов не найден"); return; }
            bool running = Process.GetProcessesByName("explorer").Length > 0;
            try
            {
                foreach (var p in Process.GetProcessesByName("explorer")) { try { p.Kill(); } catch { } }
                Thread.Sleep(1000);
                int n=0;
                foreach (var pat in new[]{"thumbcache_*.db","iconcache_*.db"})
                    foreach (var f in Directory.GetFiles(cache, pat)) { try { File.Delete(f); n++; } catch { } }
                log("    удалено баз кэша: "+n);
            }
            finally { if (running && Process.GetProcessesByName("explorer").Length==0) { try { Process.Start("explorer.exe"); } catch { } } }
        }

        // ---------- Бэкап + точка восстановления ----------
        public static string DoBackup(IEnumerable<Tweak> selected, Action<string> log)
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BULS Opti Backups", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
            Directory.CreateDirectory(dir);
            var keys = new HashSet<string>();
            foreach (var t in selected) foreach (var k in t.BackupKeys) keys.Add(k);
            int i=0;
            foreach (var key in keys) { string file = Path.Combine(dir, "backup_"+(i++)+".reg"); Sys.Run("reg", "export \""+key+"\" \""+file+"\" /y", log); }
            log("Бэкап реестра: "+dir+" ("+keys.Count+" веток)");
            return dir;
        }
        // Точка восстановления через родной системный API (без запуска powershell — меньше ложных срабатываний антивируса)
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct RESTOREPOINTINFO { public int dwEventType; public int dwRestorePtType; public long llSequenceNumber; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szDescription; }
        [StructLayout(LayoutKind.Sequential)]
        struct STATEMGRSTATUS { public int nStatus; public long llSequenceNumber; }
        [DllImport("srclient.dll", CharSet = CharSet.Unicode)]
        static extern bool SRSetRestorePoint(ref RESTOREPOINTINFO pRestorePtSpec, out STATEMGRSTATUS pSMgrStatus);

        public static void RestorePoint(Action<string> log)
        {
            try
            {
                var info = new RESTOREPOINTINFO { dwEventType = 100 /*BEGIN_SYSTEM_CHANGE*/, dwRestorePtType = 12 /*MODIFY_SETTINGS*/, llSequenceNumber = 0, szDescription = "BULS Opti" };
                STATEMGRSTATUS status;
                bool ok = SRSetRestorePoint(ref info, out status);
                log(ok ? "Точка восстановления создана." : "Точка восстановления: пропущена (Защита системы выключена или лимит частоты).");
            }
            catch (Exception ex) { log("Точка восстановления: " + ex.Message); }
        }

        // ---------- CLI (без UI) ----------
        public static int RunCli(string[] args)
        {
            string cmd = args[0].TrimStart('/','-').ToLower();
            Action<string> log = Console.WriteLine;
            var tweaks = BuildTweaks();
            if (cmd == "selftest")
            {
                string before = Reg.GetString("HKCU",@"Control Panel\Desktop","MenuShowDelay");
                tweaks.First(t=>t.Id=="menu-delay").Apply(log);
                string mid = Reg.GetString("HKCU",@"Control Panel\Desktop","MenuShowDelay");
                tweaks.First(t=>t.Id=="menu-delay").Revert(log);
                string after = Reg.GetString("HKCU",@"Control Panel\Desktop","MenuShowDelay");
                log("MenuShowDelay before="+before+" applied="+mid+" reverted="+after);
                log(mid=="100" && after=="400" ? "SELFTEST OK" : "SELFTEST FAIL");
                return (mid=="100" && after=="400") ? 0 : 1;
            }
            if (cmd == "dryrun") { foreach (var t in tweaks) log("["+t.Group+"] "+t.Title+" ("+t.Risk+")"); return 0; }
            if (cmd == "apply" || cmd == "revert")
            {
                var ids = args.Length>1 ? args[1].Split(',') : tweaks.Select(t=>t.Id).ToArray();
                foreach (var t in tweaks.Where(t=>ids.Contains(t.Id)))
                { log((cmd=="apply"?"APPLY ":"REVERT ")+t.Title); try { (cmd=="apply"?t.Apply:t.Revert)(log); } catch(Exception ex){ log("  ! "+ex.Message);} }
                return 0;
            }
            return -999; // не CLI-команда
        }
    }
}
