using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace BulsOpti
{
    class CleanResult
    {
        public long Freed;
        public int Files, Locked;
        public List<string> Notes = new List<string>();
        public void Add(CleanResult r) { Freed += r.Freed; Files += r.Files; Locked += r.Locked; Notes.AddRange(r.Notes); }
    }

    class CleanCat
    {
        public string Id;
        public bool DefaultOn, NeedsAdmin, Confirm;
        public Func<long> Measure;          // байты; -1 — размера нет
        public Func<CleanResult> Run;
        public string Title { get { return Loc.T("cl." + Id + ".t"); } }
        public string Desc { get { return Loc.T("cl." + Id + ".d"); } }
        public string Warn { get { string w = Loc.T("cl." + Id + ".w"); return w == "cl." + Id + ".w" ? null : w; } }
    }

    // Обход без захода в точки повторной обработки (junction, symlink): удаление никогда не выйдет за пределы папки
    static class Walk
    {
        static bool IsLink(FileSystemInfo i) { return (i.Attributes & FileAttributes.ReparsePoint) != 0; }

        static bool OldEnough(FileSystemInfo i, DateTime limitUtc)
        {
            return i.LastWriteTimeUtc <= limitUtc && i.CreationTimeUtc <= limitUtc;
        }

        public static long Size(string root, TimeSpan minAge)
        {
            long total = 0;
            if (string.IsNullOrEmpty(root)) return 0;
            try
            {
                var d = new DirectoryInfo(root);
                if (!d.Exists || IsLink(d)) return 0;
                SizeOf(d, DateTime.UtcNow - minAge, ref total);
            }
            catch { }
            return total;
        }

        static void SizeOf(DirectoryInfo dir, DateTime limit, ref long total)
        {
            IEnumerable<FileSystemInfo> items;
            try { items = dir.EnumerateFileSystemInfos().ToList(); } catch { return; }
            foreach (var i in items)
            {
                try
                {
                    if (IsLink(i)) continue;
                    var f = i as FileInfo;
                    if (f != null) { if (OldEnough(f, limit)) total += f.Length; }
                    else SizeOf((DirectoryInfo)i, limit, ref total);
                }
                catch { }
            }
        }

        public static CleanResult Delete(string root, TimeSpan minAge)
        {
            var r = new CleanResult();
            if (string.IsNullOrEmpty(root)) return r;
            try
            {
                var d = new DirectoryInfo(root);
                if (!d.Exists) return r;
                if (IsLink(d)) { r.Notes.Add(Loc.F("cl.note.link", root)); return r; }
                DeleteIn(d, DateTime.UtcNow - minAge, r, true);
            }
            catch { }
            return r;
        }

        static void DeleteIn(DirectoryInfo dir, DateTime limit, CleanResult r, bool isRoot)
        {
            List<FileSystemInfo> items;
            try { items = dir.EnumerateFileSystemInfos().ToList(); } catch { r.Locked++; return; }
            foreach (var i in items)
            {
                if (IsLink(i)) continue;                      // ссылку не открываем и не удаляем
                var f = i as FileInfo;
                if (f != null)
                {
                    if (!OldEnough(f, limit)) continue;
                    long len = 0;
                    try { len = f.Length; } catch { }
                    try
                    {
                        if ((f.Attributes & FileAttributes.ReadOnly) != 0) f.Attributes = FileAttributes.Normal;
                        f.Delete();
                        f.Refresh();
                        if (!f.Exists) { r.Freed += len; r.Files++; } else r.Locked++;
                    }
                    catch { r.Locked++; }
                }
                else
                {
                    var sub = (DirectoryInfo)i;
                    DeleteIn(sub, limit, r, false);
                    try
                    {
                        sub.Refresh();
                        if (sub.Exists && OldEnough(sub, limit) && !sub.EnumerateFileSystemInfos().Any()) sub.Delete(false);
                    }
                    catch { }
                }
            }
        }

        public static CleanResult DeleteFile(string path)
        {
            var r = new CleanResult();
            try
            {
                var f = new FileInfo(path);
                if (!f.Exists || IsLink(f)) return r;
                long len = f.Length;
                if ((f.Attributes & FileAttributes.ReadOnly) != 0) f.Attributes = FileAttributes.Normal;
                f.Delete(); f.Refresh();
                if (!f.Exists) { r.Freed = len; r.Files = 1; } else r.Locked = 1;
            }
            catch { r.Locked = 1; }
            return r;
        }

        public static long FileSize(string path)
        {
            try { var f = new FileInfo(path); return f.Exists ? f.Length : 0; } catch { return 0; }
        }
    }

    static class Cleaner
    {
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct RB32 { public int cbSize; public long i64Size; public long i64NumItems; }
        [StructLayout(LayoutKind.Sequential)]
        struct RB64 { public int cbSize; public long i64Size; public long i64NumItems; }
        [DllImport("shell32.dll", EntryPoint = "SHQueryRecycleBinW", CharSet = CharSet.Unicode)] static extern int QueryRB32(string root, ref RB32 info);
        [DllImport("shell32.dll", EntryPoint = "SHQueryRecycleBinW", CharSet = CharSet.Unicode)] static extern int QueryRB64(string root, ref RB64 info);
        [DllImport("shell32.dll", EntryPoint = "SHEmptyRecycleBinW", CharSet = CharSet.Unicode)] static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, uint flags);
        [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")] static extern bool DnsFlushResolverCache();

        static string LAD { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }
        static string WIN { get { return Environment.GetFolderPath(Environment.SpecialFolder.Windows); } }
        static string PD { get { return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData); } }

        static readonly TimeSpan Day = TimeSpan.FromHours(24);
        static readonly TimeSpan Week = TimeSpan.FromDays(7);

        // %TEMP% указывает на стандартную папку Windows? Если нет — там могут жить чужие рабочие файлы, чистим только с согласия
        public static bool TempIsStandard
        {
            get
            {
                string t = SafeUserTemp();
                return t != null && string.Equals(t.TrimEnd('\\'), Path.Combine(LAD, "Temp").TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }
        }

        // Корзина: размер и число объектов
        public static void RecycleInfo(out long size, out long items)
        {
            size = 0; items = 0;
            try
            {
                if (IntPtr.Size == 8) { var i = new RB64(); i.cbSize = Marshal.SizeOf(typeof(RB64)); if (QueryRB64(null, ref i) == 0) { size = i.i64Size; items = i.i64NumItems; } }
                else { var i = new RB32(); i.cbSize = Marshal.SizeOf(typeof(RB32)); if (QueryRB32(null, ref i) == 0) { size = i.i64Size; items = i.i64NumItems; } }
            }
            catch { }
        }

        // %TEMP% проверяется: если он указывает на корень диска, профиль или системную папку — не трогаем
        public static string SafeUserTemp()
        {
            try
            {
                string t = Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\');
                string root = (Path.GetPathRoot(t) ?? "").TrimEnd('\\');
                var forbidden = new[] {
                    root, WIN, Environment.SystemDirectory,
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    LAD, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                };
                if (t.Length <= 3) return null;
                foreach (var f in forbidden)
                    if (!string.IsNullOrEmpty(f) && string.Equals(f.TrimEnd('\\'), t, StringComparison.OrdinalIgnoreCase)) return null;
                return t;
            }
            catch { return null; }
        }

        // Кэши браузеров: только кэш (не куки, не пароли), все профили; открытый браузер пропускаем
        class Browser { public string Name, Proc, UserData; public bool Firefox; }

        static List<Browser> Browsers()
        {
            return new List<Browser> {
                new Browser { Name = "Chrome", Proc = "chrome", UserData = Path.Combine(LAD, @"Google\Chrome\User Data") },
                new Browser { Name = "Edge", Proc = "msedge", UserData = Path.Combine(LAD, @"Microsoft\Edge\User Data") },
                new Browser { Name = "Brave", Proc = "brave", UserData = Path.Combine(LAD, @"BraveSoftware\Brave-Browser\User Data") },
                new Browser { Name = "Yandex", Proc = "browser", UserData = Path.Combine(LAD, @"Yandex\YandexBrowser\User Data") },
                new Browser { Name = "Vivaldi", Proc = "vivaldi", UserData = Path.Combine(LAD, @"Vivaldi\User Data") },
                new Browser { Name = "Opera", Proc = "opera", UserData = Path.Combine(LAD, @"Opera Software") },
                new Browser { Name = "Firefox", Proc = "firefox", UserData = Path.Combine(LAD, @"Mozilla\Firefox\Profiles"), Firefox = true }
            };
        }

        static List<string> CacheDirs(Browser b)
        {
            var list = new List<string>();
            try
            {
                if (!Directory.Exists(b.UserData)) return list;
                if (b.Firefox)
                {
                    foreach (var p in Directory.GetDirectories(b.UserData)) list.Add(Path.Combine(p, "cache2"));
                }
                else if (b.Proc == "opera")
                {
                    foreach (var p in Directory.GetDirectories(b.UserData)) list.Add(Path.Combine(p, "Cache"));
                }
                else
                {
                    foreach (var p in Directory.GetDirectories(b.UserData))
                    {
                        string n = Path.GetFileName(p);
                        if (n != "Default" && !n.StartsWith("Profile ") && n != "Guest Profile") continue;
                        foreach (var c in new[] { "Cache", "Code Cache", "GPUCache" }) list.Add(Path.Combine(p, c));
                    }
                }
            }
            catch { }
            return list;
        }

        static bool IsOpen(Browser b)
        {
            try { return Process.GetProcessesByName(b.Proc).Any(p => { try { return p.MainWindowHandle != IntPtr.Zero; } catch { return false; } }); }
            catch { return false; }
        }

        static List<string> ShaderDirs()
        {
            return new List<string> {
                Path.Combine(LAD, "D3DSCache"), Path.Combine(LAD, @"Microsoft\DirectX Shader Cache"),
                Path.Combine(LAD, @"NVIDIA\DXCache"), Path.Combine(LAD, @"NVIDIA\GLCache"), Path.Combine(PD, @"NVIDIA Corporation\NV_Cache"),
                Path.Combine(LAD, @"AMD\DxCache"), Path.Combine(LAD, @"AMD\DxcCache"), Path.Combine(LAD, @"AMD\VkCache"), Path.Combine(LAD, @"AMD\GLCache"),
                Path.Combine(LAD, @"Intel\ShaderCache")
            };
        }

        static long SumDirs(IEnumerable<string> dirs, TimeSpan age) { long t = 0; foreach (var d in dirs) t += Walk.Size(d, age); return t; }

        static CleanResult DelDirs(IEnumerable<string> dirs, TimeSpan age)
        {
            var r = new CleanResult();
            foreach (var d in dirs) r.Add(Walk.Delete(d, age));
            return r;
        }

        public static List<CleanCat> Categories()
        {
            string userTemp = SafeUserTemp();
            string winTemp = Path.Combine(WIN, "Temp");
            string wu = Path.Combine(WIN, @"SoftwareDistribution\Download");
            var doDirs = new[] {
                Path.Combine(WIN, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"),
                Path.Combine(WIN, @"SoftwareDistribution\DeliveryOptimization")
            };
            var dumpDirs = new[] {
                Path.Combine(LAD, "CrashDumps"),
                Path.Combine(PD, @"Microsoft\Windows\WER\ReportQueue"), Path.Combine(PD, @"Microsoft\Windows\WER\ReportArchive"),
                Path.Combine(WIN, "Minidump")
            };
            string memDump = Path.Combine(WIN, "MEMORY.DMP");
            string logs = Path.Combine(WIN, "Logs");
            string thumbs = Path.Combine(LAD, @"Microsoft\Windows\Explorer");

            var L = new List<CleanCat>();

            L.Add(new CleanCat
            {
                Id = "temp", DefaultOn = true, Confirm = !TempIsStandard,
                Measure = () => Walk.Size(userTemp, Day) + (AppInfo.IsAdmin ? Walk.Size(winTemp, Week) : 0),
                Run = () =>
                {
                    var r = new CleanResult();
                    if (userTemp == null) r.Notes.Add(Loc.T("cl.note.tempUnsafe"));
                    else r.Add(Walk.Delete(userTemp, Day));
                    if (AppInfo.IsAdmin) r.Add(Walk.Delete(winTemp, Week));
                    else r.Notes.Add(Loc.T("cl.note.winTempAdmin"));
                    return r;
                }
            });

            L.Add(new CleanCat
            {
                Id = "browser", DefaultOn = true,
                Measure = () => Browsers().Sum(b => SumDirs(CacheDirs(b), TimeSpan.Zero)),
                Run = () =>
                {
                    var r = new CleanResult();
                    var skipped = new List<string>();
                    foreach (var b in Browsers())
                    {
                        var dirs = CacheDirs(b);
                        if (dirs.Count == 0) continue;
                        if (IsOpen(b)) { skipped.Add(b.Name); continue; }
                        r.Add(DelDirs(dirs, TimeSpan.Zero));
                    }
                    if (skipped.Count > 0) r.Notes.Add(Loc.F("cl.note.browserOpen", string.Join(", ", skipped.ToArray())));
                    return r;
                }
            });

            L.Add(new CleanCat
            {
                Id = "dns", DefaultOn = true,
                Measure = () => -1,
                Run = () =>
                {
                    var r = new CleanResult();
                    bool ok = false;
                    try { ok = DnsFlushResolverCache(); } catch { }
                    r.Notes.Add(Loc.T(ok ? "cl.note.dnsOk" : "cl.note.dnsFail"));
                    return r;
                }
            });

            L.Add(new CleanCat
            {
                Id = "recycle", DefaultOn = false, Confirm = true,
                Measure = () => { long s, n; RecycleInfo(out s, out n); return s; },
                Run = () =>
                {
                    var r = new CleanResult();
                    long s, n; RecycleInfo(out s, out n);
                    if (n == 0) return r;
                    int hr = -1;
                    try { hr = SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4); } catch { }
                    long s2, n2; RecycleInfo(out s2, out n2);
                    if (hr == 0 || n2 < n) { r.Freed = Math.Max(0, s - s2); r.Files = (int)Math.Max(0, n - n2); }
                    else r.Notes.Add(Loc.T("cl.note.recycleFail"));
                    return r;
                }
            });

            L.Add(new CleanCat
            {
                Id = "shaders", DefaultOn = false,
                Measure = () => SumDirs(ShaderDirs(), TimeSpan.Zero),
                Run = () => DelDirs(ShaderDirs(), TimeSpan.Zero)
            });

            L.Add(new CleanCat
            {
                Id = "thumbs", DefaultOn = false,
                Measure = () =>
                {
                    long t = 0;
                    try { foreach (var pat in new[] { "thumbcache_*.db", "iconcache_*.db" }) foreach (var f in Directory.GetFiles(thumbs, pat)) t += Walk.FileSize(f); } catch { }
                    return t;
                },
                Run = () =>
                {
                    // Проводник не завершаем: удаляются только свободные базы, занятые очистятся после перезагрузки
                    var r = new CleanResult();
                    try { foreach (var pat in new[] { "thumbcache_*.db", "iconcache_*.db" }) foreach (var f in Directory.GetFiles(thumbs, pat)) r.Add(Walk.DeleteFile(f)); } catch { }
                    if (r.Locked > 0) r.Notes.Add(Loc.T("cl.note.thumbsLocked"));
                    return r;
                }
            });

            L.Add(new CleanCat
            {
                Id = "wu", DefaultOn = false, NeedsAdmin = true,
                Measure = () => Walk.Size(wu, TimeSpan.FromDays(10)),
                Run = () =>
                {
                    var r = new CleanResult();
                    bool pending = Reg.KeyExists("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired")
                                || Reg.KeyExists("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
                    if (pending) { r.Notes.Add(Loc.T("cl.note.rebootPending")); return r; }
                    return Walk.Delete(wu, TimeSpan.FromDays(10));
                }
            });

            L.Add(new CleanCat
            {
                Id = "do", DefaultOn = false, NeedsAdmin = true,
                Measure = () => SumDirs(doDirs, TimeSpan.Zero),
                Run = () => DelDirs(doDirs, TimeSpan.Zero)
            });

            L.Add(new CleanCat
            {
                Id = "dumps", DefaultOn = false, NeedsAdmin = true,
                Measure = () => SumDirs(dumpDirs, TimeSpan.Zero) + Walk.FileSize(memDump),
                Run = () => { var r = DelDirs(dumpDirs, TimeSpan.Zero); r.Add(Walk.DeleteFile(memDump)); return r; }
            });

            L.Add(new CleanCat
            {
                Id = "logs", DefaultOn = false, NeedsAdmin = true,
                Measure = () => Walk.Size(logs, TimeSpan.FromDays(30)),
                Run = () => Walk.Delete(logs, TimeSpan.FromDays(30))
            });

            return L;
        }
    }
}
