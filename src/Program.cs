using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Caffeine
{
    internal static class Program
    {
        private const string MutexName = @"Local\CaffeineTraySingleton";
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int processId);

        [STAThread]
        private static int Main(string[] args)
        {
            string mode = ParseMode(args);

            if (mode == "selftest" || mode == "version")
            {
                // console output: this exe has no window of its own
                AttachConsole(-1);
                try
                {
                    Stream stdout = Console.OpenStandardOutput();
                    StreamWriter w = new StreamWriter(stdout);
                    w.AutoFlush = true;
                    Console.SetOut(w);
                }
                catch (Exception) { }

                if (mode == "version")
                {
                    Console.WriteLine(AppVersion.Short);
                    return 0;
                }
                return SelfTest.Run();
            }

            if (mode == "restore-quiet")
            {
                // used by the uninstaller: put the power settings back without
                // any dialog, and report through the exit code only
                AppLog.Write("--restore-quiet：检查是否有待恢复的设置");
                if (!File.Exists(AppPaths.PendingRestoreFile))
                {
                    AppLog.Write("--restore-quiet：没有待恢复的设置");
                    return 0;
                }
                PowerSnapshot snap = PowerKeeper.LoadPending();
                PowerResult res = PowerKeeper.RestoreForced(snap);
                PowerKeeper.DeletePending();
                AppLog.Write("--restore-quiet 结果：" + res.Detail);
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            HookCrashHandlers();

            if (mode == "restore") return RunRestoreOnly();

            bool elevatedHandover = mode == "elevated";
            bool first;
            Mutex mutex = new Mutex(true, MutexName, out first);
            bool weOwn = first;
            if (!weOwn)
            {
                try
                {
                    weOwn = mutex.WaitOne(0);
                }
                catch (AbandonedMutexException)
                {
                    weOwn = true; // a previous instance was killed hard
                }
                if (!weOwn && elevatedHandover)
                    weOwn = WaitForMutex(mutex, 20000);
            }

            if (!weOwn)
            {
                mutex.Dispose();
                if (!elevatedHandover)
                {
                    MessageBox.Show("咖啡因已经在运行了，请看右下角的任务栏图标。",
                        "咖啡因", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return 0;
            }

            try
            {
                AppLog.Write("启动（管理员=" + PowerKeeper.IsAdmin() +
                             "，模式=" + (mode == "" ? "普通" : mode) + "）");
                Application.Run(new TrayContext());
            }
            finally
            {
                try { mutex.ReleaseMutex(); } catch (Exception) { }
                mutex.Dispose();
                AppLog.Write("已退出");
            }
            return 0;
        }

        private static bool WaitForMutex(Mutex mutex, int timeoutMs)
        {
            DateTime until = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < until)
            {
                try
                {
                    if (mutex.WaitOne(250)) return true;
                }
                catch (AbandonedMutexException)
                {
                    return true;
                }
                Thread.Sleep(150);
            }
            return false;
        }

        /// <summary>`--restore` / `--elevated` / `--selftest`, anything else is ignored.</summary>
        private static string ParseMode(string[] args)
        {
            if (args == null) return "";
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (string.IsNullOrEmpty(a)) continue;
                if (a.StartsWith("--", StringComparison.Ordinal))
                    return a.Substring(2).ToLowerInvariant();
            }
            return "";
        }

        /// <summary>Repairs the power settings without showing a tray icon.</summary>
        private static int RunRestoreOnly()
        {
            AppLog.Write("--restore：检查是否有待恢复的设置");
            if (!File.Exists(AppPaths.PendingRestoreFile))
            {
                MessageBox.Show("没有需要恢复的设置，系统当前就是你原来的默认设置。",
                    "咖啡因", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            PowerSnapshot snap = PowerKeeper.LoadPending();
            PowerResult res = PowerKeeper.RestoreForced(snap);
            PowerKeeper.DeletePending();
            AppLog.Write("--restore 结果：" + res.Detail);
            MessageBox.Show(res.UsedFallback
                    ? "没能读取原始设置，已按默认恢复：1 小时后进入睡眠。"
                    : "已经把你原来的电源设置恢复回来了。",
                "咖啡因", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        private static void HookCrashHandlers()
        {
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                AppLog.Error("界面线程异常", e.Exception);
                try
                {
                    MessageBox.Show("咖啡因遇到了一个问题（已记录到日志）：\n" + e.Exception.Message +
                                    "\n\n日志位置：" + AppPaths.LogFile,
                        "咖啡因", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch (Exception) { }
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                AppLog.Error("未处理异常", e.ExceptionObject as Exception);
            };
        }
    }
}
