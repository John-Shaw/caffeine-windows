using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CaffeineSetup
{
    /// <summary>
    /// Everything the installer does: payload extraction, shortcuts, registry,
    /// version comparison, upgrade and self-uninstall.  No UI in here.
    /// </summary>
    internal static class InstallCore
    {
        internal const string AppName = "咖啡因 Caffeine";
        internal const string ExeName = "Caffeine.exe";
        internal const string UninstallerName = "uninstall.exe";
        internal const string RunValueName = "Caffeine";
        internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        internal const string UninstallKeyPath =
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Caffeine";
        internal const string PayloadResource = "CaffeineSetup.payload.deflate";
        internal const string VersionResource = "CaffeineSetup.version.txt";

        // ---------------------------------------------------------------- paths

        internal static string DefaultDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "Caffeine");
            }
        }

        /// <summary>User data (settings, log) - never touched by install/uninstall
        /// unless the user explicitly asks to remove it.</summary>
        internal static string DataDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Caffeine");
            }
        }

        internal static string StartMenuDir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                    "Microsoft\\Windows\\Start Menu\\Programs");
            }
        }

        internal static string StartMenuLink { get { return Path.Combine(StartMenuDir, "Caffeine.lnk"); } }
        internal static string DesktopLink
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.Desktop), "Caffeine.lnk");
            }
        }

        // ---------------------------------------------------------------- version

        /// <summary>The version of the payload this setup carries.</summary>
        internal static string PayloadVersion
        {
            get
            {
                try
                {
                    using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(VersionResource))
                    {
                        if (s == null) return "0.0.0";
                        using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                            return r.ReadToEnd().Trim();
                    }
                }
                catch (Exception)
                {
                    return "0.0.0";
                }
            }
        }

        /// <summary>Version currently recorded in Add/Remove Programs, or null.</summary>
        internal static string InstalledVersion
        {
            get
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(UninstallKeyPath))
                    {
                        if (k == null) return null;
                        return k.GetValue("DisplayVersion") as string;
                    }
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        internal static string InstalledDir
        {
            get
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(UninstallKeyPath))
                    {
                        if (k == null) return null;
                        return k.GetValue("InstallLocation") as string;
                    }
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        internal enum UpgradeKind { Fresh, SameVersion, Upgrade, Downgrade }

        internal static UpgradeKind Classify(out string from)
        {
            from = InstalledVersion;
            if (string.IsNullOrEmpty(from)) return UpgradeKind.Fresh;
            int c = CompareVersions(from, PayloadVersion);
            if (c == 0) return UpgradeKind.SameVersion;
            return c < 0 ? UpgradeKind.Upgrade : UpgradeKind.Downgrade;
        }

        internal static int CompareVersions(string a, string b)
        {
            string[] pa = (a ?? "0").Split('.');
            string[] pb = (b ?? "0").Split('.');
            for (int i = 0; i < 3; i++)
            {
                int va = i < pa.Length ? ToInt(pa[i]) : 0;
                int vb = i < pb.Length ? ToInt(pb[i]) : 0;
                if (va != vb) return va < vb ? -1 : 1;
            }
            return 0;
        }

        private static int ToInt(string s)
        {
            int n;
            return int.TryParse(s, out n) ? n : 0;
        }

        // ---------------------------------------------------------------- install

        /// <summary>Unpacks the embedded, deflate-compressed Caffeine.exe.</summary>
        internal static void WritePayload(string dir)
        {
            byte[] raw;
            using (Stream src = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResource))
            {
                if (src == null) throw new InvalidOperationException("安装包缺少程序数据（payload 资源）");
                using (MemoryStream mem = new MemoryStream())
                {
                    using (DeflateStream def = new DeflateStream(src, CompressionMode.Decompress))
                        def.CopyTo(mem);
                    raw = mem.ToArray();
                }
            }
            string target = Path.Combine(dir, ExeName);
            string tmp = target + ".new";
            File.WriteAllBytes(tmp, raw);
            // replace atomically-ish: move the old one aside, drop the new one in
            if (File.Exists(target))
            {
                string bak = target + ".old";
                try { if (File.Exists(bak)) File.Delete(bak); } catch (Exception) { }
                File.Move(target, bak);
            }
            File.Move(tmp, target);
            try { string bak = target + ".old"; if (File.Exists(bak)) File.Delete(bak); }
            catch (Exception) { }

            // the uninstaller is a copy of this setup exe
            string me = Assembly.GetExecutingAssembly().Location;
            string ule = Path.Combine(dir, UninstallerName);
            if (!string.Equals(Path.GetFullPath(me), Path.GetFullPath(ule), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(me, ule, true);
            }
        }

        internal static void CreateShortcuts(string dir, bool desktop)
        {
            string target = Path.Combine(dir, ExeName);
            if (!Directory.Exists(StartMenuDir)) Directory.CreateDirectory(StartMenuDir);
            WriteLink(StartMenuLink, target, dir, "保持系统唤醒");
            if (desktop) WriteLink(DesktopLink, target, dir, "保持系统唤醒");
        }

        internal static void RemoveShortcuts(bool desktop)
        {
            TryDelete(StartMenuLink);
            if (desktop) TryDelete(DesktopLink);
        }

        /// <summary>Uses the shell itself to write the .lnk, so it looks native.</summary>
        internal static void WriteLink(string link, string target, string workDir, string description)
        {
            try
            {
                // late-bound COM: WScript.Shell.CreateShortcut returns a COM
                // object, so every call after that goes through the object's own
                // type - not through the shell type.
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(shellType);
                object linkObj = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
                    null, shell, new object[] { link });
                Type lt = linkObj.GetType();
                lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, linkObj, new object[] { target });
                lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, linkObj, new object[] { workDir });
                lt.InvokeMember("Description", BindingFlags.SetProperty, null, linkObj, new object[] { description });
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, linkObj, null);
            }
            catch (Exception ex)
            {
                // a missing shortcut is not worth failing the install over, but
                // say so in the log instead of swallowing it
                Log("创建快捷方式失败 " + link + ": " + ex.Message);
            }
        }

        internal static void RegisterUninstall(string dir, bool desktop)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKeyPath))
                {
                    k.SetValue("DisplayName", AppName);
                    k.SetValue("DisplayVersion", PayloadVersion);
                    k.SetValue("Publisher", "Caffeine");
                    k.SetValue("DisplayIcon", Path.Combine(dir, ExeName));
                    k.SetValue("InstallLocation", dir);
                    k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                    k.SetValue("UninstallString", "\"" + Path.Combine(dir, UninstallerName) + "\" --uninstall");
                    k.SetValue("QuietUninstallString",
                        "\"" + Path.Combine(dir, UninstallerName) + "\" --uninstall --quiet");
                    k.SetValue("NoModify", 1);
                    k.SetValue("NoRepair", 1);
                    k.SetValue("EstimatedSize", (int)(DirSize(dir) / 1024));
                    if (!desktop) k.DeleteValue("DesktopLink", false);
                }
            }
            catch (Exception)
            {
            }
        }

        internal static void Unregister()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(UninstallKeyPath, true))
                    if (k != null) k.DeleteSubKeyTree(UninstallKeyPath.Substring(UninstallKeyPath.LastIndexOf('\\') + 1));
            }
            catch (Exception)
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, false); }
                catch (Exception) { }
            }
        }

        // ---------------------------------------------------------------- autostart

        internal static bool IsAutoStartOn
        {
            get
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                        return k != null && k.GetValue(RunValueName) != null;
                }
                catch (Exception) { return false; }
            }
        }

        internal static void SetAutoStart(bool on, string exePath)
        {
            try
            {
                if (on)
                {
                    using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                        k.SetValue(RunValueName, "\"" + exePath + "\"");
                }
                else
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                        if (k != null && k.GetValue(RunValueName) != null) k.DeleteValue(RunValueName, false);
                }
            }
            catch (Exception)
            {
            }
        }

        // ---------------------------------------------------------------- running app

        /// <summary>Closes any running copy so its exe can be replaced.</summary>
        internal static int StopRunningApp()
        {
            int killed = 0;
            try
            {
                Process[] ps = Process.GetProcessesByName("Caffeine");
                foreach (Process p in ps)
                {
                    try
                    {
                        if (p.Id == Process.GetCurrentProcess().Id) continue;
                        p.Kill();
                        p.WaitForExit(5000);
                        killed++;
                    }
                    catch (Exception) { }
                    finally { try { p.Dispose(); } catch (Exception) { } }
                }
            }
            catch (Exception) { }
            return killed;
        }

        /// <summary>
        /// A process can refuse to die (Process.Kill is not guaranteed), and a
        /// locked exe makes the install fail with a confusing IO error.  Say it
        /// in words the user can act on instead.
        /// </summary>
        internal static bool IsAppRunning()
        {
            try
            {
                Process[] ps = Process.GetProcessesByName("Caffeine");
                try { return ps.Length > 0; }
                finally { foreach (Process p in ps) { try { p.Dispose(); } catch (Exception) { } } }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Hands the "put the power settings back" job to the app itself, so the
        /// restore logic stays in exactly one place.  Quiet: no dialogs.
        /// </summary>
        internal static bool RestorePowerSettings(string dir)
        {
            string exe = Path.Combine(dir, ExeName);
            if (!File.Exists(exe)) return false;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, "--restore-quiet");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    p.WaitForExit(15000);
                    return p.ExitCode == 0;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ---------------------------------------------------------------- uninstall

        internal static void DeleteDirRecursive(string dir)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (Directory.Exists(dir)) Directory.Delete(dir, true);
                    return;
                }
                catch (Exception)
                {
                    Thread.Sleep(400);
                }
            }
        }

        internal static void DeleteDataDir()
        {
            try
            {
                if (Directory.Exists(DataDir)) Directory.Delete(DataDir, true);
            }
            catch (Exception)
            {
            }
        }

        internal static long DirSize(string dir)
        {
            long total = 0;
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(f).Length; } catch (Exception) { }
                }
            }
            catch (Exception) { }
            return total;
        }

        // ---------------------------------------------------------------- misc

        internal static void TryDelete(string file)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch (Exception) { }
        }

        internal static void Log(string message)
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Caffeine");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "setup.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch (Exception)
            {
            }
        }
    }
}
