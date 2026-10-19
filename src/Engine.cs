using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BulsOpti
{
    enum TState { Ok, NotOk, NA }

    // ---------- Сведения о программе и её настройки ----------
    static class AppInfo
    {
        public const string Version = "3.1.0";
        public const string SiteUrl = "https://bulsopti.com";
        public const string ProUrl = "https://www.bulsopti.com/app";
        public const string SourceUrl = "https://github.com/bulsopti-debug/FPS-TOOL";
        public const string RegBase = @"Software\BULS Opti";

        public static string DataDir
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BULS Opti");
                try { Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }

        public static bool IsAdmin
        {
            get { try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); } catch { return false; } }
        }

        public static string GetSetting(string name)
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(RegBase)) return k == null ? null : k.GetValue(name) as string; }
            catch { return null; }
        }

        public static void SetSetting(string name, string value)
        {
            try { using (var k = Registry.CurrentUser.CreateSubKey(RegBase)) k.SetValue(name, value, RegistryValueKind.String); }
            catch { }
        }

        [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode)]
        static extern bool WTSQuerySessionInformationW(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytes);
        [DllImport("wtsapi32.dll")]
        static extern void WTSFreeMemory(IntPtr p);

        // Повышение прав под ДРУГОЙ учётной записью: тогда HKCU и папки — чужие
        public static bool OtherUserElevation()
        {
            try
            {
                IntPtr buf; int n;
                if (!WTSQuerySessionInformationW(IntPtr.Zero, -1, 5, out buf, out n)) return false;
                string sessionUser = Marshal.PtrToStringUni(buf);
                WTSFreeMemory(buf);
                if (string.IsNullOrEmpty(sessionUser)) return false;
                return !string.Equals(sessionUser, Environment.UserName, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        // Удалить всё, что программа хранит о себе (только если откатывать нечего)
        public static bool RemoveTraces()
        {
            if (Undo.Ids().Length > 0) return false;
            try { Registry.CurrentUser.DeleteSubKeyTree(RegBase, false); } catch { }
            try
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BULS Opti");
                if (Directory.Exists(d)) Directory.Delete(d, true);
            }
            catch { }
            return true;
        }
    }

    // ---------- Журнал действий (файл + событие для интерфейса) ----------
    static class Journal
    {
        public static event Action<string> Added;
        public static bool Mute;
        static readonly object Gate = new object();

        public static string FilePath { get { return Path.Combine(AppInfo.DataDir, "log.txt"); } }

        public static void Add(string text)
        {
            if (Mute) return;
            string line = DateTime.Now.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) + "   " + text;
            lock (Gate) { try { File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8); } catch { } }
            var h = Added;
            if (h != null) { try { h(line); } catch { } }
        }

        public static string[] Tail(int max)
        {
            lock (Gate)
            {
                try
                {
                    if (!File.Exists(FilePath)) return new string[0];
                    var all = File.ReadAllLines(FilePath, Encoding.UTF8);
                    return all.Skip(Math.Max(0, all.Length - max)).ToArray();
                }
                catch { return new string[0]; }
            }
        }
    }

    // ---------- Реестр (всегда 64-битный вид на 64-битной Windows) ----------
    static class Reg
    {
        public static RegistryKey Base(string hive)
        {
            RegistryHive h = hive == "HKLM" ? RegistryHive.LocalMachine : (hive == "HKU" ? RegistryHive.Users : RegistryHive.CurrentUser);
            return RegistryKey.OpenBaseKey(h, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default);
        }

        public static RegistryKey Open(string hive, string path, bool write)
        {
            try { return Base(hive).OpenSubKey(path, write); } catch { return null; }
        }

        public static object Get(string hive, string path, string name)
        {
            try
            {
                using (var k = Open(hive, path, false))
                    return k == null ? null : k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            }
            catch { return null; }
        }

        public static int? Dword(string hive, string path, string name)
        {
            object v = Get(hive, path, name);
            if (v is int) return (int)v;
            if (v is long) return unchecked((int)(long)v);
            string s = v as string; int n;
            if (s != null && int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
            return null;
        }

        public static string Str(string hive, string path, string name)
        {
            object v = Get(hive, path, name);
            return v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static bool Set(string hive, string path, string name, RegistryValueKind kind, object value)
        {
            try
            {
                using (var k = Base(hive).CreateSubKey(path))
                {
                    if (k == null) return false;
                    k.SetValue(name, value, kind);
                    return true;
                }
            }
            catch (Exception ex) { Journal.Add(Loc.F("log.regFail", hive + "\\" + path + " › " + name, ex.Message)); return false; }
        }

        public static bool Delete(string hive, string path, string name)
        {
            try
            {
                using (var k = Open(hive, path, true)) { if (k != null) k.DeleteValue(name, false); }
                return true;
            }
            catch (Exception ex) { Journal.Add(Loc.F("log.regFail", hive + "\\" + path + " › " + name, ex.Message)); return false; }
        }

        public static bool KeyExists(string hive, string path)
        {
            using (var k = Open(hive, path, false)) return k != null;
        }
    }

    // ---------- Журнал отката: прежние значения пользователя ----------
    static class Undo
    {
        const string Root = AppInfo.RegBase + @"\Undo";

        static string VName(string hive, string path, string name) { return hive + "|" + path + "|" + name; }

        // Запомнить исходное значение ДО первого изменения (повторно не перезаписывается)
        public static void Remember(string id, string hive, string path, string name)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(Root + "\\" + id))
                {
                    string vn = VName(hive, path, name);
                    if (k.GetValue(vn) != null) return;
                    k.SetValue(vn, Encode(hive, path, name), RegistryValueKind.String);
                }
            }
            catch { }
        }

        static string Encode(string hive, string path, string name)
        {
            using (var k = Reg.Open(hive, path, false))
            {
                if (k == null) return "none";
                object v = k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (v == null) return "none";
                RegistryValueKind kind = k.GetValueKind(name);
                if (kind == RegistryValueKind.DWord) return "dw:" + Convert.ToInt32(v).ToString(CultureInfo.InvariantCulture);
                if (kind == RegistryValueKind.QWord) return "qw:" + Convert.ToInt64(v).ToString(CultureInfo.InvariantCulture);
                if (kind == RegistryValueKind.ExpandString) return "ex:" + v;
                if (kind == RegistryValueKind.String) return "sz:" + v;
                return "skip";
            }
        }

        public static void RememberRaw(string id, string key, string data)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(Root + "\\" + id))
                    if (k.GetValue("@" + key) == null) k.SetValue("@" + key, data, RegistryValueKind.String);
            }
            catch { }
        }

        public static string Raw(string id, string key)
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(Root + "\\" + id)) return k == null ? null : k.GetValue("@" + key) as string; }
            catch { return null; }
        }

        public static bool Has(string id)
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(Root + "\\" + id)) return k != null && k.ValueCount > 0; }
            catch { return false; }
        }

        public static string[] Ids()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(Root)) return k == null ? new string[0] : k.GetSubKeyNames().Where(Has).ToArray(); }
            catch { return new string[0]; }
        }

        public static bool TouchesHklm(string id)
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(Root + "\\" + id)) return k != null && k.GetValueNames().Any(n => n.StartsWith("HKLM|")); }
            catch { return false; }
        }

        // Вернуть всё, что записано для id; журнал удаляется только при полном успехе
        public static bool Restore(string id)
        {
            bool ok = true;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Root + "\\" + id))
                {
                    if (k == null) return true;
                    foreach (string vn in k.GetValueNames())
                    {
                        if (vn.StartsWith("@")) continue;
                        string[] p = vn.Split('|');
                        if (p.Length != 3) continue;
                        string data = k.GetValue(vn) as string ?? "skip";
                        bool r = true;
                        if (data == "none") r = Reg.Delete(p[0], p[1], p[2]);
                        else if (data.StartsWith("dw:")) r = Reg.Set(p[0], p[1], p[2], RegistryValueKind.DWord, int.Parse(data.Substring(3), CultureInfo.InvariantCulture));
                        else if (data.StartsWith("qw:")) r = Reg.Set(p[0], p[1], p[2], RegistryValueKind.QWord, long.Parse(data.Substring(3), CultureInfo.InvariantCulture));
                        else if (data.StartsWith("sz:")) r = Reg.Set(p[0], p[1], p[2], RegistryValueKind.String, data.Substring(3));
                        else if (data.StartsWith("ex:")) r = Reg.Set(p[0], p[1], p[2], RegistryValueKind.ExpandString, data.Substring(3));
                        if (!r) ok = false;
                    }
                }
            }
            catch { ok = false; }
            if (ok) Forget(id);
            return ok;
        }

        public static void Forget(string id)
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(Root + "\\" + id, false); } catch { }
        }
    }

    // ---------- Одна запись в реестр, которую делает твик ----------
    class RegWrite
    {
        public string Hive, Path, Name;
        public RegistryValueKind Kind;
        public object Value;    // целевое значение
        public object Default;  // значение Windows по умолчанию (null = значения нет)

        public RegWrite(string hive, string path, string name, RegistryValueKind kind, object value, object def)
        {
            Hive = hive; Path = path; Name = name; Kind = kind; Value = value; Default = def;
        }

        public bool Matches()
        {
            if (Kind == RegistryValueKind.DWord)
            {
                int? cur = Reg.Dword(Hive, Path, Name);
                return cur.HasValue && cur.Value == Convert.ToInt32(Value);
            }
            string s = Reg.Str(Hive, Path, Name);
            return s != null && s == Convert.ToString(Value, CultureInfo.InvariantCulture);
        }

        public string Show(object v)
        {
            if (v == null) return Loc.T("val.none");
            if (Kind == RegistryValueKind.DWord)
            {
                int i = Convert.ToInt32(v);
                return i == -1 ? "0xFFFFFFFF" : i.ToString(CultureInfo.InvariantCulture);
            }
            return "\"" + v + "\"";
        }

        public string Describe()
        {
            string head = Engine.ShortPath(Hive, Path) + " › " + Name + ":  ";
            return Matches() ? head + Show(Value) : head + Show(Reg.Get(Hive, Path, Name)) + "  →  " + Show(Value);
        }
    }

    // ---------- Твик ----------
    class Tweak
    {
        public string Id, Group, Risk = "low";
        public bool Recommended, NeedsAdmin, NeedsRestart;
        public string[] Presets = new string[0];
        public List<RegWrite> Writes = new List<RegWrite>();
        public Func<TState> StateFn;
        public Func<bool> ApplyFn, RevertFn;
        public Func<string> DetailsFn;
        public Action AfterChange;   // применить без перезагрузки (SystemParametersInfo)

        public string Title { get { return Loc.T("tw." + Id + ".t"); } }
        public string Desc { get { return Loc.T("tw." + Id + ".d"); } }

        public TState State()
        {
            try
            {
                if (StateFn != null) return StateFn();
                if (Writes.Count == 0) return TState.NA;
                return Writes.All(w => w.Matches()) ? TState.Ok : TState.NotOk;
            }
            catch { return TState.NA; }
        }

        public bool Apply()
        {
            bool ok = true;
            if (ApplyFn != null) ok = ApplyFn();
            else
            {
                foreach (var w in Writes)
                {
                    Undo.Remember(Id, w.Hive, w.Path, w.Name);
                    if (!Reg.Set(w.Hive, w.Path, w.Name, w.Kind, w.Value)) ok = false;
                }
            }
            if (AfterChange != null) { try { AfterChange(); } catch { } }
            return ok;
        }

        public bool Revert()
        {
            bool ok = true;
            if (RevertFn != null) ok = RevertFn();
            else if (Undo.Has(Id)) ok = Undo.Restore(Id);
            else
            {
                foreach (var w in Writes)
                {
                    bool r = w.Default == null ? Reg.Delete(w.Hive, w.Path, w.Name) : Reg.Set(w.Hive, w.Path, w.Name, w.Kind, w.Default);
                    if (!r) ok = false;
                }
            }
            if (AfterChange != null) { try { AfterChange(); } catch { } }
            return ok;
        }

        public string Details()
        {
            try { return DetailsFn != null ? DetailsFn() : string.Join("\n", Writes.Select(w => w.Describe()).ToArray()); }
            catch { return ""; }
        }

        public bool InPreset(string p) { return p == "rec" ? Recommended : Presets.Contains(p); }

        // Нужны ли права, чтобы вернуть сделанное
        public bool RevertNeedsAdmin { get { return Undo.Has(Id) ? Undo.TouchesHklm(Id) : NeedsAdmin; } }
    }

    // ---------- Системные функции ----------
    static class Spi
    {
        public const int SETMOUSE = 0x0004, SETDESKWALLPAPER = 0x0014, SETANIMATION = 0x0049, SETMENUSHOWDELAY = 0x006B;
        public const int UPDATE = 0x01 | 0x02;

        [StructLayout(LayoutKind.Sequential)]
        public struct ANIMATIONINFO { public int cbSize; public int iMinAnimate; }

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        public static extern bool Set(int action, int uParam, IntPtr pv, int flags);
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool Set(int action, int uParam, string pv, int flags);
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        public static extern bool Set(int action, int uParam, int[] pv, int flags);
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        public static extern bool Set(int action, int uParam, ref ANIMATIONINFO pv, int flags);
    }

    static class Com
    {
        [DllImport("ole32.dll")]
        static extern int CoInitializeSecurity(IntPtr pSecDesc, int cAuthSvc, IntPtr asAuthSvc, IntPtr r1, int authnLevel, int impLevel, IntPtr authList, int capabilities, IntPtr r3);

        // Нужна до первого COM-вызова, иначе служба восстановления не может обратиться к процессу
        public static void InitSecurity()
        {
            try { CoInitializeSecurity(IntPtr.Zero, -1, IntPtr.Zero, IntPtr.Zero, 0, 3, IntPtr.Zero, 0, IntPtr.Zero); } catch { }
        }
    }

    // ---------- Схема питания через powrprof (без запуска powercfg.exe) ----------
    static class Power
    {
        public static readonly Guid High = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
        public static readonly Guid Ultimate = new Guid("e9a42b02-d5df-448d-aa00-03f14749eb61");
        public static readonly Guid Balanced = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e");
        public static readonly Guid Saver = new Guid("a1841308-3541-4fab-bc81-f71556f20b4a");

        [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr guid);
        [DllImport("powrprof.dll")] static extern uint PowerSetActiveScheme(IntPtr root, ref Guid guid);
        [DllImport("powrprof.dll")] static extern uint PowerDuplicateScheme(IntPtr root, ref Guid src, ref IntPtr dst);
        [DllImport("powrprof.dll")] static extern uint PowerEnumerate(IntPtr root, IntPtr scheme, IntPtr sub, uint access, uint index, byte[] buffer, ref uint size);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr h);

        public static Guid? Active()
        {
            IntPtr p;
            try
            {
                if (PowerGetActiveScheme(IntPtr.Zero, out p) != 0 || p == IntPtr.Zero) return null;
                try { return (Guid)Marshal.PtrToStructure(p, typeof(Guid)); }
                finally { LocalFree(p); }
            }
            catch { return null; }
        }

        public static List<Guid> Schemes()
        {
            var list = new List<Guid>();
            try
            {
                for (uint i = 0; i < 64; i++)
                {
                    var buf = new byte[16]; uint size = 16;
                    if (PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16, i, buf, ref size) != 0) break;
                    list.Add(new Guid(buf));
                }
            }
            catch { }
            return list;
        }

        public static bool Set(Guid g)
        {
            try { return PowerSetActiveScheme(IntPtr.Zero, ref g) == 0; } catch { return false; }
        }

        // «Высокая производительность»: есть — берём, скрыта (ноутбуки с Modern Standby) — дублируем
        public static Guid? EnsureHigh()
        {
            if (Schemes().Contains(High)) return High;
            string made = AppInfo.GetSetting("PlanHigh"); Guid mg;
            if (made != null && Guid.TryParse(made, out mg) && Schemes().Contains(mg)) return mg;
            try
            {
                Guid src = High; IntPtr dst = IntPtr.Zero;
                if (PowerDuplicateScheme(IntPtr.Zero, ref src, ref dst) == 0 && dst != IntPtr.Zero)
                {
                    try
                    {
                        Guid g = (Guid)Marshal.PtrToStructure(dst, typeof(Guid));
                        AppInfo.SetSetting("PlanHigh", g.ToString());
                        return g;
                    }
                    finally { LocalFree(dst); }
                }
            }
            catch { }
            return null;
        }

        public static bool IsFast(Guid g)
        {
            if (g == High || g == Ultimate) return true;
            string made = AppInfo.GetSetting("PlanHigh"); Guid mg;
            return made != null && Guid.TryParse(made, out mg) && mg == g;
        }

        public static string Name(Guid? g)
        {
            if (!g.HasValue) return Loc.T("plan.unknown");
            if (g.Value == Balanced) return Loc.T("plan.balanced");
            if (g.Value == Saver) return Loc.T("plan.saver");
            if (g.Value == Ultimate) return Loc.T("plan.ultimate");
            if (IsFast(g.Value)) return Loc.T("plan.high");
            return Loc.T("plan.custom");
        }
    }

    // ---------- Точка восстановления (родной API, с упаковкой как в заголовках Windows) ----------
    static class RestorePoint
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
        struct RESTOREPOINTINFOW
        {
            public int dwEventType;
            public int dwRestorePtType;
            public long llSequenceNumber;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szDescription;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct STATEMGRSTATUS { public int nStatus; public long llSequenceNumber; }

        [DllImport("srclient.dll", EntryPoint = "SRSetRestorePointW", CharSet = CharSet.Unicode)]
        static extern bool SRSetRestorePointW(ref RESTOREPOINTINFOW info, out STATEMGRSTATUS status);

        // 0 — создана; иначе код ошибки (1058 — защита системы выключена)
        public static int Create(string description)
        {
            try
            {
                var begin = new RESTOREPOINTINFOW { dwEventType = 100, dwRestorePtType = 12, llSequenceNumber = 0, szDescription = description };
                STATEMGRSTATUS st;
                if (!SRSetRestorePointW(ref begin, out st)) return st.nStatus == 0 ? -1 : st.nStatus;
                var end = new RESTOREPOINTINFOW { dwEventType = 101, dwRestorePtType = 12, llSequenceNumber = st.llSequenceNumber, szDescription = description };
                STATEMGRSTATUS st2;
                SRSetRestorePointW(ref end, out st2);
                return 0;
            }
            catch { return -1; }
        }
    }

    // ---------- Обои BUL$ Opti: с запоминанием прежних ----------
    static class Wallpaper
    {
        const string Desk = @"Control Panel\Desktop";
        public static string OurFile { get { return Path.Combine(AppInfo.DataDir, "wallpaper.png"); } }

        public static bool IsOurs()
        {
            try
            {
                string cur = Reg.Str("HKCU", Desk, "Wallpaper");
                return !string.IsNullOrEmpty(cur) && string.Equals(Path.GetFullPath(cur), OurFile, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static bool Apply(Stream png)
        {
            try
            {
                if (png == null) return false;
                if (!IsOurs())
                    foreach (var n in new[] { "Wallpaper", "WallpaperStyle", "TileWallpaper" }) Undo.Remember("wallpaper", "HKCU", Desk, n);
                using (var fs = File.Create(OurFile)) png.CopyTo(fs);
                Reg.Set("HKCU", Desk, "WallpaperStyle", RegistryValueKind.String, "10");
                Reg.Set("HKCU", Desk, "TileWallpaper", RegistryValueKind.String, "0");
                return Spi.Set(Spi.SETDESKWALLPAPER, 0, OurFile, Spi.UPDATE);
            }
            catch (Exception ex) { Journal.Add(Loc.F("log.error", ex.Message)); return false; }
        }

        public static bool Revert()
        {
            if (!Undo.Has("wallpaper")) return true;
            bool ok = Undo.Restore("wallpaper");
            string path = Reg.Str("HKCU", Desk, "Wallpaper") ?? "";
            try { Spi.Set(Spi.SETDESKWALLPAPER, 0, path, Spi.UPDATE); } catch { }
            try { if (File.Exists(OurFile) && !IsOurs()) File.Delete(OurFile); } catch { }
            return ok;
        }
    }

    // ---------- Счётчик «освобождено за всё время» (в байтах) ----------
    static class Stats
    {
        public static long TotalFreed
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.CreateSubKey(AppInfo.RegBase))
                    {
                        object b = k.GetValue("TotalFreedBytes");
                        if (b == null)
                        {
                            object mb = k.GetValue("TotalFreedMB");
                            long v = mb == null ? 0 : Convert.ToInt64(mb) * 1048576L;
                            k.SetValue("TotalFreedBytes", v, RegistryValueKind.QWord);
                            if (mb != null) k.DeleteValue("TotalFreedMB", false);
                            return v;
                        }
                        return Convert.ToInt64(b);
                    }
                }
                catch { return 0; }
            }
        }

        public static void AddFreed(long bytes)
        {
            if (bytes <= 0) return;
            long cur = TotalFreed;
            try { using (var k = Registry.CurrentUser.CreateSubKey(AppInfo.RegBase)) k.SetValue("TotalFreedBytes", cur + bytes, RegistryValueKind.QWord); }
            catch { }
        }
    }

    // ---------- Сведения о системе и диагностика ----------
    class HwInfo
    {
        public string Cpu = "", Gpu = "", Windows = "", SysDrive = "C:\\";
        public int Build, CurHz, MaxHz, Width, Height, StartupApps = -1, Hags;
        public long RamMb, DiskFree, DiskTotal;
        public bool RebootPending;
    }

    class DiagItem
    {
        public string Id, Title, Text, ActionKey, ActionUri;
        public int Level; // 0 в порядке, 1 справка, 2 стоит проверить
    }

    static class SysInfo
    {
        [StructLayout(LayoutKind.Sequential)]
        class MEMORYSTATUSEX
        {
            public uint dwLength; public uint dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX m);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettingsW(string device, int mode, ref DEVMODE dm);

        const string NT = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

        public static int Build
        {
            get { int b; return int.TryParse(Reg.Str("HKLM", NT, "CurrentBuild"), out b) ? b : 0; }
        }

        public static string CpuName
        {
            get { return (Reg.Str("HKLM", @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString") ?? "").Trim(); }
        }

        // Двухкристальные X3D: AMD рекомендует «Сбалансированную» схему (драйвер парковки ядер)
        public static bool DualCcdX3D { get { return Regex.IsMatch(CpuName, @"Ryzen 9 \d{4}X3D", RegexOptions.IgnoreCase); } }

        public static HwInfo Read()
        {
            var h = new HwInfo();
            try { h.Cpu = CpuName; } catch { }
            try { h.Gpu = GpuName(); } catch { }
            try
            {
                h.Build = Build;
                string name = Reg.Str("HKLM", NT, "ProductName") ?? "Windows";
                if (h.Build >= 22000 && name.StartsWith("Windows 10")) name = "Windows 11" + name.Substring(10);
                string disp = Reg.Str("HKLM", NT, "DisplayVersion");
                h.Windows = name + (string.IsNullOrEmpty(disp) ? "" : " " + disp) + (h.Build > 0 ? " (" + h.Build + ")" : "");
            }
            catch { }
            try { var m = new MEMORYSTATUSEX(); m.dwLength = (uint)Marshal.SizeOf(m); if (GlobalMemoryStatusEx(m)) h.RamMb = (long)(m.ullTotalPhys / 1048576UL); } catch { }
            try
            {
                h.SysDrive = Path.GetPathRoot(Environment.SystemDirectory);
                var d = new DriveInfo(h.SysDrive);
                h.DiskFree = d.AvailableFreeSpace; h.DiskTotal = d.TotalSize;
            }
            catch { }
            try { ReadDisplay(h); } catch { }
            try
            {
                h.RebootPending = Reg.KeyExists("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired")
                               || Reg.KeyExists("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
            }
            catch { }
            try { int? hs = Reg.Dword("HKLM", @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode"); h.Hags = hs.HasValue ? hs.Value : 0; } catch { }
            try { h.StartupApps = StartupCount(); } catch { }
            return h;
        }

        static string GpuName()
        {
            string cls = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            string best = ""; int bestScore = -1;
            using (var root = Reg.Open("HKLM", cls, false))
            {
                if (root == null) return "";
                foreach (string sub in root.GetSubKeyNames())
                {
                    if (sub.Length != 4) continue;
                    string desc;
                    using (var k = root.OpenSubKey(sub)) desc = k == null ? null : k.GetValue("DriverDesc") as string;
                    if (string.IsNullOrEmpty(desc)) continue;
                    string l = desc.ToLowerInvariant();
                    if (l.Contains("basic") || l.Contains("remote") || l.Contains("virtual") || l.Contains("mirror") || l.Contains("parsec") || l.Contains("hyper-v")) continue;
                    int score = 1;
                    if (l.Contains("nvidia")) score = 3;
                    else if (Regex.IsMatch(l, @"\brx\s?\d")) score = 3;
                    else if (l.Contains("arc")) score = 2;
                    if (score > bestScore) { bestScore = score; best = desc.Trim(); }
                }
            }
            return best;
        }

        static void ReadDisplay(HwInfo h)
        {
            var dm = new DEVMODE(); dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            if (!EnumDisplaySettingsW(null, -1, ref dm)) return;
            h.CurHz = dm.dmDisplayFrequency; h.Width = dm.dmPelsWidth; h.Height = dm.dmPelsHeight;
            int max = h.CurHz;
            for (int i = 0; i < 4000; i++)
            {
                var m = new DEVMODE(); m.dmSize = dm.dmSize;
                if (!EnumDisplaySettingsW(null, i, ref m)) break;
                if (m.dmPelsWidth == h.Width && m.dmPelsHeight == h.Height && m.dmDisplayFrequency > max) max = m.dmDisplayFrequency;
            }
            h.MaxHz = max;
        }

        static int StartupCount()
        {
            int n = 0;
            const string run = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
            const string approved = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
            n += CountRun("HKCU", run, "HKCU", approved + "Run");
            n += CountRun("HKLM", run, "HKLM", approved + "Run");
            n += CountRun("HKLM", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "HKLM", approved + "Run32");
            foreach (var dir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Startup), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup) })
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var f in Directory.GetFiles(dir))
                    {
                        string fn = Path.GetFileName(f);
                        if (fn.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!Disabled("HKCU", approved + "StartupFolder", fn) && !Disabled("HKLM", approved + "StartupFolder", fn)) n++;
                    }
                }
                catch { }
            }
            return n;
        }

        static int CountRun(string hive, string path, string apHive, string apPath)
        {
            int n = 0;
            using (var k = Reg.Open(hive, path, false))
            {
                if (k == null) return 0;
                foreach (var name in k.GetValueNames()) if (!string.IsNullOrEmpty(name) && !Disabled(apHive, apPath, name)) n++;
            }
            return n;
        }

        static bool Disabled(string hive, string path, string name)
        {
            var b = Reg.Get(hive, path, name) as byte[];
            return b != null && b.Length > 0 && (b[0] & 1) == 1;
        }

        public static List<DiagItem> Diagnose(HwInfo h)
        {
            var L = new List<DiagItem>();
            if (h.CurHz > 1)
            {
                if (h.MaxHz - h.CurHz >= 5)
                    L.Add(new DiagItem { Id = "hz", Level = 2, Title = Loc.T("diag.hz.t"), Text = Loc.F("diag.hz.warn", h.CurHz, h.MaxHz), ActionKey = "act.display", ActionUri = "ms-settings:display-advanced" });
                else
                    L.Add(new DiagItem { Id = "hz", Level = 0, Title = Loc.T("diag.hz.t"), Text = Loc.F("diag.hz.ok", h.CurHz) });
            }
            if (h.DiskTotal > 0)
            {
                double pct = 100.0 * h.DiskFree / h.DiskTotal;
                double freeGb = h.DiskFree / 1073741824.0;
                if (pct < 10 || freeGb < 15)
                    L.Add(new DiagItem { Id = "disk", Level = 2, Title = Loc.F("diag.disk.t", h.SysDrive.TrimEnd('\\')), Text = Loc.F("diag.disk.warn", Fmt.Gb(h.DiskFree), Math.Round(pct)), ActionKey = "act.clean", ActionUri = "page:clean" });
                else
                    L.Add(new DiagItem { Id = "disk", Level = 0, Title = Loc.F("diag.disk.t", h.SysDrive.TrimEnd('\\')), Text = Loc.F("diag.disk.ok", Fmt.Gb(h.DiskFree), Math.Round(pct)) });
            }
            if (h.RebootPending)
                L.Add(new DiagItem { Id = "reboot", Level = 2, Title = Loc.T("diag.reboot.t"), Text = Loc.T("diag.reboot.warn") });
            if (h.Hags == 1)
                L.Add(new DiagItem { Id = "hags", Level = 1, Title = Loc.T("diag.hags.t"), Text = Loc.T("diag.hags.off"), ActionKey = "act.graphics", ActionUri = "ms-settings:display-advancedgraphics" });
            else if (h.Hags == 2)
                L.Add(new DiagItem { Id = "hags", Level = 0, Title = Loc.T("diag.hags.t"), Text = Loc.T("diag.hags.on") });
            if (h.StartupApps >= 8)
                L.Add(new DiagItem { Id = "startup", Level = 1, Title = Loc.T("diag.startup.t"), Text = Loc.F("diag.startup.many", h.StartupApps), ActionKey = "act.startup", ActionUri = "ms-settings:startupapps" });
            else if (h.StartupApps >= 0)
                L.Add(new DiagItem { Id = "startup", Level = 0, Title = Loc.T("diag.startup.t"), Text = Loc.F("diag.startup.ok", h.StartupApps) });
            return L;
        }
    }

    // ---------- Форматирование чисел под язык ----------
    static class Fmt
    {
        public static string Size(long bytes)
        {
            if (bytes <= 0) return "0 " + Loc.T("unit.mb");
            double mb = bytes / 1048576.0;
            if (mb >= 1024) return (mb / 1024.0).ToString(mb >= 10240 ? "0" : "0.0", Loc.Culture) + " " + Loc.T("unit.gb");
            if (mb >= 1) return Math.Round(mb).ToString("0", Loc.Culture) + " " + Loc.T("unit.mb");
            return Math.Max(1, (int)Math.Round(bytes / 1024.0)).ToString(Loc.Culture) + " " + Loc.T("unit.kb");
        }

        public static string Gb(long bytes)
        {
            return Math.Round(bytes / 1073741824.0).ToString("0", Loc.Culture) + " " + Loc.T("unit.gb");
        }
    }

    // ---------- Набор твиков ----------
    static class Engine
    {
        const string SysProfile = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
        const string NicClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";
        const string GpuPrefs = @"Software\Microsoft\DirectX\UserGpuPreferences";
        const RegistryValueKind DW = RegistryValueKind.DWord;
        const RegistryValueKind SZ = RegistryValueKind.String;

        public static string ShortPath(string hive, string path)
        {
            string p = path;
            foreach (var pre in new[] { @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\", @"Software\Microsoft\Windows\CurrentVersion\", @"SOFTWARE\Microsoft\Windows\CurrentVersion\", @"SOFTWARE\Policies\Microsoft\Windows\", @"SYSTEM\CurrentControlSet\Control\" })
                if (p.StartsWith(pre, StringComparison.OrdinalIgnoreCase)) { p = "…\\" + p.Substring(pre.Length); break; }
            return hive + "\\" + p;
        }

        public static List<Tweak> Tweaks()
        {
            var L = new List<Tweak>();

            // --- Питание ---
            L.Add(new Tweak
            {
                Id = "power-plan", Group = "power", Recommended = true, Presets = new[] { "games" },
                StateFn = () =>
                {
                    if (SysInfo.DualCcdX3D) return TState.NA;
                    Guid? a = Power.Active();
                    if (!a.HasValue) return TState.NA;
                    return Power.IsFast(a.Value) ? TState.Ok : TState.NotOk;
                },
                ApplyFn = () =>
                {
                    Guid? a = Power.Active();
                    if (a.HasValue && !Power.IsFast(a.Value)) Undo.RememberRaw("power-plan", "plan", a.Value.ToString());
                    Guid? h = Power.EnsureHigh();
                    if (!h.HasValue) { Journal.Add(Loc.T("log.planMissing")); return false; }
                    return Power.Set(h.Value);
                },
                RevertFn = () =>
                {
                    string prev = Undo.Raw("power-plan", "plan"); Guid g;
                    bool ok = (prev != null && Guid.TryParse(prev, out g) && Power.Schemes().Contains(g)) ? Power.Set(g) : Power.Set(Power.Balanced);
                    if (ok) Undo.Forget("power-plan");
                    return ok;
                },
                DetailsFn = () =>
                {
                    Guid? a = Power.Active();
                    return Loc.T("det.plan") + ":  " + Power.Name(a) + (a.HasValue && Power.IsFast(a.Value) ? "" : "  →  " + Loc.T("plan.high"));
                }
            });

            // --- Графика ---
            L.Add(new Tweak
            {
                Id = "game-dvr", Group = "gpu", Recommended = true, Presets = new[] { "games" },
                Writes = new List<RegWrite> {
                    new RegWrite("HKCU", @"System\GameConfigStore", "GameDVR_Enabled", DW, 0, 1),
                    new RegWrite("HKCU", @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", DW, 0, null)
                }
            });

            int build = SysInfo.Build;
            var dxKeys = build >= 22621 ? new[] { "SwapEffectUpgradeEnable", "VRROptimizeEnable" } : (build >= 18362 ? new[] { "VRROptimizeEnable" } : new string[0]);
            L.Add(new Tweak
            {
                Id = build >= 22621 ? "flip-vrr" : "vrr", Group = "gpu", Recommended = true, Presets = new[] { "games" },
                StateFn = () =>
                {
                    if (dxKeys.Length == 0) return TState.NA;
                    var m = ParsePairs(Reg.Str("HKCU", GpuPrefs, "DirectXUserGlobalSettings"));
                    return dxKeys.All(k => m.ContainsKey(k) && m[k] == "1") ? TState.Ok : TState.NotOk;
                },
                ApplyFn = () =>
                {
                    if (dxKeys.Length == 0) return false;
                    Undo.Remember(build >= 22621 ? "flip-vrr" : "vrr", "HKCU", GpuPrefs, "DirectXUserGlobalSettings");
                    var m = ParsePairs(Reg.Str("HKCU", GpuPrefs, "DirectXUserGlobalSettings"));
                    foreach (var k in dxKeys) m[k] = "1";
                    return Reg.Set("HKCU", GpuPrefs, "DirectXUserGlobalSettings", SZ, JoinPairs(m));
                },
                RevertFn = () =>
                {
                    string id = build >= 22621 ? "flip-vrr" : "vrr";
                    if (Undo.Has(id)) return Undo.Restore(id);
                    var m = ParsePairs(Reg.Str("HKCU", GpuPrefs, "DirectXUserGlobalSettings"));
                    foreach (var k in dxKeys) m.Remove(k);
                    return m.Count == 0 ? Reg.Delete("HKCU", GpuPrefs, "DirectXUserGlobalSettings") : Reg.Set("HKCU", GpuPrefs, "DirectXUserGlobalSettings", SZ, JoinPairs(m));
                },
                DetailsFn = () => ShortPath("HKCU", GpuPrefs) + " › DirectXUserGlobalSettings:  " + string.Join("; ", dxKeys.Select(k => k + "=1").ToArray())
            });

            L.Add(new Tweak
            {
                Id = "game-mode", Group = "gpu", Recommended = true, Risk = "none", Presets = new[] { "games" },
                Writes = new List<RegWrite> { new RegWrite("HKCU", @"Software\Microsoft\GameBar", "AutoGameModeEnabled", DW, 1, null) },
                StateFn = () => { int? v = Reg.Dword("HKCU", @"Software\Microsoft\GameBar", "AutoGameModeEnabled"); return (!v.HasValue || v.Value != 0) ? TState.Ok : TState.NotOk; }
            });

            // --- Сеть ---
            L.Add(new Tweak
            {
                Id = "net-throttling", Group = "net", Recommended = true, NeedsAdmin = true, Presets = new[] { "games" },
                Writes = new List<RegWrite> { new RegWrite("HKLM", SysProfile, "NetworkThrottlingIndex", DW, -1, 10) }
            });

            var nicWrites = PhysicalNicWrites();
            L.Add(new Tweak
            {
                Id = "nic-power", Group = "net", Recommended = true, NeedsAdmin = true, NeedsRestart = true, Presets = new[] { "games" },
                Writes = nicWrites,
                StateFn = () =>
                {
                    if (nicWrites.Count == 0) return TState.NA;
                    return nicWrites.All(w => { int? v = Reg.Dword(w.Hive, w.Path, w.Name); return v.HasValue && (v.Value & 0x18) == 0x18; }) ? TState.Ok : TState.NotOk;
                },
                ApplyFn = () =>
                {
                    bool ok = true;
                    foreach (var w in nicWrites)
                    {
                        Undo.Remember("nic-power", w.Hive, w.Path, w.Name);
                        int? cur = Reg.Dword(w.Hive, w.Path, w.Name);
                        if (!Reg.Set(w.Hive, w.Path, w.Name, DW, (cur.HasValue ? cur.Value : 0) | 0x18)) ok = false;
                    }
                    return ok;
                }
            });

            L.Add(new Tweak
            {
                Id = "p2p-off", Group = "net", Recommended = true, NeedsAdmin = true, Presets = new[] { "games" },
                Writes = new List<RegWrite> { new RegWrite("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", DW, 0, null) },
                StateFn = () =>
                {
                    int? pol = Reg.Dword("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode");
                    if (pol.HasValue) return (pol.Value == 0 || pol.Value == 99 || pol.Value == 100) ? TState.Ok : TState.NotOk;
                    int? user = Reg.Dword("HKU", @"S-1-5-20\Software\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Settings", "DownloadMode");
                    return (user.HasValue && user.Value == 0) ? TState.Ok : TState.NotOk;
                }
            });

            // --- Отзывчивость ---
            L.Add(new Tweak
            {
                Id = "sys-resp", Group = "resp", Recommended = true, NeedsAdmin = true, Presets = new[] { "games", "resp" },
                Writes = new List<RegWrite> { new RegWrite("HKLM", SysProfile, "SystemResponsiveness", DW, 10, 20) },
                StateFn = () => { int? v = Reg.Dword("HKLM", SysProfile, "SystemResponsiveness"); return (v.HasValue && v.Value <= 10) ? TState.Ok : TState.NotOk; }
            });

            L.Add(new Tweak
            {
                Id = "menu-delay", Group = "resp", Recommended = true, Risk = "none", Presets = new[] { "resp" },
                Writes = new List<RegWrite> { new RegWrite("HKCU", @"Control Panel\Desktop", "MenuShowDelay", SZ, "100", "400") },
                StateFn = () => { int? v = Reg.Dword("HKCU", @"Control Panel\Desktop", "MenuShowDelay"); return (v.HasValue && v.Value <= 150) ? TState.Ok : TState.NotOk; },
                AfterChange = () => { int? v = Reg.Dword("HKCU", @"Control Panel\Desktop", "MenuShowDelay"); Spi.Set(Spi.SETMENUSHOWDELAY, v.HasValue ? v.Value : 400, IntPtr.Zero, Spi.UPDATE); }
            });

            L.Add(new Tweak
            {
                Id = "animations", Group = "resp", Recommended = true, Risk = "none", Presets = new[] { "resp" },
                Writes = new List<RegWrite> {
                    new RegWrite("HKCU", @"Control Panel\Desktop\WindowMetrics", "MinAnimate", SZ, "0", "1"),
                    new RegWrite("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations", DW, 0, 1)
                },
                StateFn = () => Reg.Str("HKCU", @"Control Panel\Desktop\WindowMetrics", "MinAnimate") == "0" ? TState.Ok : TState.NotOk,
                AfterChange = () =>
                {
                    var ai = new Spi.ANIMATIONINFO { cbSize = 8, iMinAnimate = Reg.Str("HKCU", @"Control Panel\Desktop\WindowMetrics", "MinAnimate") == "0" ? 0 : 1 };
                    Spi.Set(Spi.SETANIMATION, 8, ref ai, Spi.UPDATE);
                }
            });

            L.Add(new Tweak
            {
                Id = "startup-delay", Group = "resp", Recommended = true, Risk = "none", Presets = new[] { "resp" },
                Writes = new List<RegWrite> { new RegWrite("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", DW, 0, null) }
            });

            // --- Ввод ---
            L.Add(new Tweak
            {
                Id = "mouse-accel", Group = "input", Recommended = false, Risk = "none", Presets = new[] { "games" },
                Writes = new List<RegWrite> {
                    new RegWrite("HKCU", @"Control Panel\Mouse", "MouseSpeed", SZ, "0", "1"),
                    new RegWrite("HKCU", @"Control Panel\Mouse", "MouseThreshold1", SZ, "0", "6"),
                    new RegWrite("HKCU", @"Control Panel\Mouse", "MouseThreshold2", SZ, "0", "10")
                },
                StateFn = () => Reg.Str("HKCU", @"Control Panel\Mouse", "MouseSpeed") == "0" ? TState.Ok : TState.NotOk,
                AfterChange = () =>
                {
                    int? t1 = Reg.Dword("HKCU", @"Control Panel\Mouse", "MouseThreshold1");
                    int? t2 = Reg.Dword("HKCU", @"Control Panel\Mouse", "MouseThreshold2");
                    int? sp = Reg.Dword("HKCU", @"Control Panel\Mouse", "MouseSpeed");
                    Spi.Set(Spi.SETMOUSE, 0, new[] { t1.HasValue ? t1.Value : 6, t2.HasValue ? t2.Value : 10, sp.HasValue ? sp.Value : 1 }, Spi.UPDATE);
                }
            });

            // --- Система ---
            const string Cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
            L.Add(new Tweak
            {
                Id = "promo-apps", Group = "sys", Recommended = true, Risk = "none", Presets = new[] { "resp" },
                Writes = new List<RegWrite> {
                    new RegWrite("HKCU", Cdm, "SilentInstalledAppsEnabled", DW, 0, 1),
                    new RegWrite("HKCU", Cdm, "PreInstalledAppsEnabled", DW, 0, 1),
                    new RegWrite("HKCU", Cdm, "OemPreInstalledAppsEnabled", DW, 0, 1)
                },
                StateFn = () => { int? v = Reg.Dword("HKCU", Cdm, "SilentInstalledAppsEnabled"); return (v.HasValue && v.Value == 0) ? TState.Ok : TState.NotOk; }
            });

            const string Sp = @"Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy";
            L.Add(new Tweak
            {
                Id = "storage-sense", Group = "sys", Recommended = false, Risk = "none", Presets = new[] { "resp" },
                Writes = new List<RegWrite> {
                    new RegWrite("HKCU", Sp, "01", DW, 1, null),
                    new RegWrite("HKCU", Sp, "04", DW, 1, null),
                    new RegWrite("HKCU", Sp, "2048", DW, 7, null)
                },
                StateFn = () => { int? v = Reg.Dword("HKCU", Sp, "01"); return (v.HasValue && v.Value == 1) ? TState.Ok : TState.NotOk; }
            });

            return L;
        }

        // Только физические адаптеры: бит NCF_PHYSICAL (0x4) в Characteristics
        static List<RegWrite> PhysicalNicWrites()
        {
            var list = new List<RegWrite>();
            try
            {
                using (var root = Reg.Open("HKLM", NicClass, false))
                {
                    if (root == null) return list;
                    foreach (string sub in root.GetSubKeyNames())
                    {
                        if (sub.Length != 4) continue;
                        using (var k = root.OpenSubKey(sub))
                        {
                            if (k == null || string.IsNullOrEmpty(k.GetValue("NetCfgInstanceId") as string)) continue;
                            object ch = k.GetValue("Characteristics");
                            int c = ch == null ? 0 : Convert.ToInt32(ch);
                            if ((c & 0x4) == 0) continue;
                            list.Add(new RegWrite("HKLM", NicClass + "\\" + sub, "PnPCapabilities", DW, 24, null));
                        }
                    }
                }
            }
            catch { }
            return list;
        }

        static Dictionary<string, string> ParsePairs(string s)
        {
            var d = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(s)) return d;
            foreach (var kv in s.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int i = kv.IndexOf('=');
                if (i > 0) d[kv.Substring(0, i).Trim()] = kv.Substring(i + 1).Trim();
            }
            return d;
        }

        static string JoinPairs(Dictionary<string, string> d)
        {
            var sb = new StringBuilder();
            foreach (var kv in d) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
            return sb.ToString();
        }

        // Вернуть всё, что меняла программа. Возвращает список id, которые вернуть не удалось.
        public static List<string> RevertAll(List<Tweak> tweaks)
        {
            var failed = new List<string>();
            foreach (string id in Undo.Ids())
            {
                bool ok;
                if (id == "wallpaper") ok = Wallpaper.Revert();
                else
                {
                    var t = tweaks.FirstOrDefault(x => x.Id == id);
                    ok = t != null ? t.Revert() : Undo.Restore(id);
                }
                if (!ok) failed.Add(id);
            }
            return failed;
        }

        public static bool AnyHklmUndo() { return Undo.Ids().Any(Undo.TouchesHklm); }
    }
}
