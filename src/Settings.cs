using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Caffeine
{
    /// <summary>User preferences, stored as a two-line file next to the log.</summary>
    internal sealed class UserSettings
    {
        internal bool AllowDisplaySleep = false;   // keep the screen on while awake
        internal bool AutoStart = false;           // launch with Windows
        internal bool AutoAwake = false;           // enter keep-awake as soon as we launch
        internal bool WelcomeShown = false;        // one-time "you got it" balloon
        internal bool HibernateHintShown = false;  // one-time "needs admin for hibernation" hint

        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "Caffeine";

        internal static UserSettings Load()
        {
            UserSettings s = new UserSettings();
            try
            {
                if (File.Exists(AppPaths.SettingsFile))
                {
                    Dictionary<string, string> kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string line in File.ReadAllLines(AppPaths.SettingsFile, Encoding.UTF8))
                    {
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        kv[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                    }
                    string v;
                    if (kv.TryGetValue("allow_display_sleep", out v)) s.AllowDisplaySleep = v == "1";
                    if (kv.TryGetValue("auto_start", out v)) s.AutoStart = v == "1";
                    if (kv.TryGetValue("auto_awake", out v)) s.AutoAwake = v == "1";
                    if (kv.TryGetValue("welcome_shown", out v)) s.WelcomeShown = v == "1";
                    if (kv.TryGetValue("hibernate_hint_shown", out v)) s.HibernateHintShown = v == "1";
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("读取设置失败", ex);
            }
            return s;
        }

        internal void Save()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("allow_display_sleep=" + (AllowDisplaySleep ? "1" : "0"));
                sb.AppendLine("auto_start=" + (AutoStart ? "1" : "0"));
                sb.AppendLine("auto_awake=" + (AutoAwake ? "1" : "0"));
                sb.AppendLine("welcome_shown=" + (WelcomeShown ? "1" : "0"));
                sb.AppendLine("hibernate_hint_shown=" + (HibernateHintShown ? "1" : "0"));
                File.WriteAllText(AppPaths.SettingsFile, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AppLog.Error("保存设置失败", ex);
            }
        }

        internal static string ExePath
        {
            get
            {
                try
                {
                    return ProcessPath();
                }
                catch (Exception)
                {
                    return "";
                }
            }
        }

        private static string ProcessPath()
        {
            // .NET Framework 4.x has no Process.MainModule.FileName without elevation
            // risk on some hosts, so ask the loader directly.
            using (System.Diagnostics.Process p = System.Diagnostics.Process.GetCurrentProcess())
            {
                return p.MainModule.FileName;
            }
        }

        internal static bool IsAutoStartOn()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                {
                    if (k == null) return false;
                    object v = k.GetValue(RunValueName);
                    return v != null && v.ToString().IndexOf("Caffeine", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("读取开机启动项失败", ex);
                return false;
            }
        }

        internal static bool SetAutoStart(bool on)
        {
            try
            {
                if (on)
                {
                    string exe = ProcessPath();
                    if (string.IsNullOrEmpty(exe)) return false;
                    using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                    {
                        k.SetValue(RunValueName, "\"" + exe + "\"", RegistryValueKind.String);
                    }
                    AppLog.Write("已添加开机启动：" + exe);
                    return true;
                }
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (k != null && k.GetValue(RunValueName) != null) k.DeleteValue(RunValueName, false);
                }
                AppLog.Write("已移除开机启动");
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error("设置开机启动失败", ex);
                return false;
            }
        }
    }

    /// <summary>
    /// The two tray icons, pulled out of the embedded .ico resources at exactly
    /// the size the shell is asking for (16px @100%, 24px @150%, 32px @200%).
    /// </summary>
    internal static class AppIcons
    {
        private static readonly List<Stream> keepAlive = new List<Stream>();
        private static readonly Dictionary<string, Icon> cache = new Dictionary<string, Icon>(StringComparer.Ordinal);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        internal static int SmallIconSize
        {
            get
            {
                try
                {
                    int n = GetSystemMetrics(49); // SM_CXSMICON
                    return n >= 16 && n <= 48 ? n : 16;
                }
                catch (Exception)
                {
                    return 16;
                }
            }
        }

        internal static Icon Get(bool full)
        {
            string key = (full ? "full" : "empty") + "|" + SmallIconSize;
            Icon cached;
            if (cache.TryGetValue(key, out cached) && cached != null) return cached;

            Icon icon = Load(full ? "Caffeine.full.ico" : "Caffeine.empty.ico", SmallIconSize);
            cache[key] = icon;
            return icon;
        }

        /// <summary>Verifies both resources decode into real icons (used by --selftest).</summary>
        internal static string Probe()
        {
            StringBuilder sb = new StringBuilder();
            foreach (int size in new int[] { 16, 20, 24, 32, 48, 256 })
            {
                foreach (bool full in new bool[] { false, true })
                {
                    string name = full ? "full" : "empty";
                    try
                    {
                        Icon ic = Load(full ? "Caffeine.full.ico" : "Caffeine.empty.ico", size);
                        IntPtr h = ic == null ? IntPtr.Zero : ic.Handle;
                        sb.AppendFormat(CultureInfo.InvariantCulture,
                            "  icon {0} {1}px -> {2}x{3} handle={4}{5}",
                            name, size, ic.Width, ic.Height, h,
                            (h == IntPtr.Zero ? "  <-- FAILED" : "")).AppendLine();
                    }
                    catch (Exception ex)
                    {
                        sb.AppendFormat(CultureInfo.InvariantCulture, "  icon {0} {1}px -> {2}",
                            name, size, ex.Message).AppendLine();
                    }
                }
            }
            return sb.ToString();
        }

        private static Icon Load(string resourceName, int size)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            string[] names = asm.GetManifestResourceNames();
            string match = null;
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i].EndsWith(resourceName, StringComparison.OrdinalIgnoreCase))
                {
                    match = names[i];
                    break;
                }
            }
            if (match == null)
                throw new InvalidOperationException("缺少图标资源 " + resourceName +
                    "（现有：" + string.Join(",", names) + "）");

            using (Stream src = asm.GetManifestResourceStream(match))
            {
                MemoryStream ms = new MemoryStream();
                byte[] buf = new byte[8192];
                int n;
                while ((n = src.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                ms.Position = 0;
                keepAlive.Add(ms); // Icon keeps reading from this stream lazily
                return new Icon(ms, size, size);
            }
        }
    }
}
