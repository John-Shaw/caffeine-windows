using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace CaffeineSetup
{
    internal static class ProcessStart
    {
        internal static void StartApp(string exe)
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }
    }

    /// <summary>
    /// Caffeine-Setup.exe
    ///
    ///   (no args)                    install / upgrade, interactive
    ///   --silent                     install without any UI (exit code says it all)
    ///   --dir "PATH"                 install location (default: per-user Programs)
    ///   --autostart / --no-autostart add or remove the Windows startup entry
    ///   --launch / --no-launch       start the app when finished
    ///   --desktop                    also create a desktop shortcut
    ///   --uninstall [--quiet] [--delete-data]
    ///   --uninstall-stage2 DIR       internal: the real uninstall, run from %TEMP%
    ///   --version                    print the payload version
    /// </summary>
    internal static class SetupProgram
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            string mode = "";
            string dir = null;
            bool quiet = false, deleteData = false, silent = false;
            bool? autoStart = null, launch = null;
            bool desktop = false;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a.StartsWith("--", StringComparison.Ordinal))
                {
                    string name = a.Substring(2).ToLowerInvariant();
                    if (name == "uninstall") mode = "uninstall";
                    else if (name == "uninstall-stage2") mode = "stage2";
                    else if (name == "quiet") quiet = true;
                    else if (name == "silent") silent = true;
                    else if (name == "delete-data") deleteData = true;
                    else if (name == "desktop") desktop = true;
                    else if (name == "autostart") autoStart = true;
                    else if (name == "no-autostart") autoStart = false;
                    else if (name == "launch") launch = true;
                    else if (name == "no-launch") launch = false;
                    else if (name == "dir" && i + 1 < args.Length) dir = args[++i].Trim('"');
                    else if (name == "version")
                    {
                        Console.WriteLine(InstallCore.PayloadVersion);
                        return 0;
                    }
                }
                else if (a.Length > 0 && dir == null)
                {
                    dir = a.Trim('"');
                }
            }

            try
            {
                if (mode == "stage2")
                {
                    UninstallForm.RunStage2(
                        dir ?? InstallCore.InstalledDir ?? InstallCore.DefaultDir,
                        deleteData, quiet);
                    return 0;
                }

                if (mode == "uninstall")
                {
                    string target = InstallCore.InstalledDir ?? InstallCore.DefaultDir;
                    if (quiet || silent)
                    {
                        // may hand itself off to a temp copy so it can delete
                        // the folder it is running out of
                        UninstallForm.Uninstall(target, deleteData, true);
                        return 0;
                    }
                    if (!Directory.Exists(target))
                    {
                        // already gone (or never installed here) - still clean up
                        InstallCore.RemoveShortcuts(true);
                        InstallCore.SetAutoStart(false, null);
                        InstallCore.Unregister();
                        MessageBox.Show("没有找到已安装的 " + InstallCore.AppName + "，已清理注册表中的残留项。",
                            InstallCore.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return 0;
                    }
                    Application.Run(new UninstallForm(target, quiet));
                    return 0;
                }

                if (silent)
                {
                    return SilentInstall(
                        dir ?? InstallCore.DefaultDir,
                        autoStart.HasValue ? autoStart.Value : InstallCore.IsAutoStartOn,
                        desktop,
                        launch.HasValue ? launch.Value : false);
                }

                InstallForm.Options opts = new InstallForm.Options();
                opts.Dir = dir;
                opts.AutoStart = autoStart;
                opts.Launch = launch;
                opts.Desktop = desktop;
                Application.Run(new InstallForm(opts));
                return 0;
            }
            catch (Exception ex)
            {
                InstallCore.Log("安装程序异常: " + ex);
                if (!silent)
                {
                    MessageBox.Show("安装程序遇到了问题：\r\n" + ex.Message, InstallCore.AppName,
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return 1;
            }
        }

        /// <summary>No UI, no prompts, exit code 0 = done.  For scripted installs.</summary>
        private static int SilentInstall(string dir, bool autoStart, bool desktop, bool launch)
        {
            try
            {
                InstallCore.Log("静默安装 v" + InstallCore.PayloadVersion + " -> " + dir +
                    " autostart=" + autoStart + " desktop=" + desktop + " launch=" + launch);
                InstallCore.StopRunningApp();
                System.Threading.Thread.Sleep(600);
                if (InstallCore.IsAppRunning())
                    throw new InvalidOperationException(
                        "the app is still running and its exe is locked");
                InstallCore.RestorePowerSettings(dir);
                Directory.CreateDirectory(dir);
                InstallCore.WritePayload(dir);
                InstallCore.CreateShortcuts(dir, desktop);
                InstallCore.RegisterUninstall(dir, desktop);
                InstallCore.SetAutoStart(autoStart, Path.Combine(dir, InstallCore.ExeName));
                if (launch)
                {
                    try
                    {
                        ProcessStart.StartApp(Path.Combine(dir, InstallCore.ExeName));
                    }
                    catch (Exception) { }
                }
                InstallCore.Log("静默安装完成");
                return 0;
            }
            catch (Exception ex)
            {
                InstallCore.Log("静默安装失败: " + ex);
                return 1;
            }
        }
    }
}
