using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Caffeine
{
    /// <summary>
    /// Log + state file locations.  Everything lives under %LOCALAPPDATA%\Caffeine
    /// so an unprivileged install works and nothing is written next to the exe.
    /// </summary>
    internal static class AppPaths
    {
        private static string root;

        internal static string Root
        {
            get
            {
                if (root == null)
                {
                    string baseDir = Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData);
                    if (string.IsNullOrEmpty(baseDir))
                        baseDir = Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory);
                    root = Path.Combine(baseDir, "Caffeine");
                }
                if (!Directory.Exists(root))
                    Directory.CreateDirectory(root);
                return root;
            }
        }

        /// <summary>Append-only activity log (also the main debugging surface).</summary>
        internal static string LogFile { get { return Path.Combine(Root, "caffeine.log"); } }

        /// <summary>
        /// Written *before* the power settings are touched and deleted after they
        /// are restored, so a crash or a kill can never strand the machine in the
        /// "never sleep" state: the next launch replays this file.
        /// </summary>
        internal static string PendingRestoreFile { get { return Path.Combine(Root, "pending-restore.cfg"); } }

        /// <summary>User preferences (allow display sleep, start with Windows).</summary>
        internal static string SettingsFile { get { return Path.Combine(Root, "settings.cfg"); } }
    }

    internal static class AppLog
    {
        private const long MaxBytes = 512 * 1024;
        private static readonly object gate = new object();

        internal static void Write(string message)
        {
            string line = string.Format(CultureInfo.InvariantCulture,
                "{0:yyyy-MM-dd HH:mm:ss}  {1}", DateTime.Now, message);
            lock (gate)
            {
                try
                {
                    string f = AppPaths.LogFile;
                    if (File.Exists(f) && new FileInfo(f).Length > MaxBytes)
                        File.Delete(f);
                    File.AppendAllText(f, line + Environment.NewLine, Encoding.UTF8);
                }
                catch (Exception)
                {
                    /* logging must never take the app down */
                }
            }
        }

        internal static void Error(string context, Exception ex)
        {
            Write(context + " -> " + ex.GetType().Name + ": " + ex.Message);
        }
    }
}
