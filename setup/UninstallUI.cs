using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace CaffeineSetup
{
    /// <summary>
    /// Uninstaller.  Stage 1 asks for confirmation, then hands the real work to
    /// a copy of itself in %TEMP% - an exe cannot delete the directory it is
    /// running out of.  Coordinates are 96dpi units, scaled by <see cref="Lay"/>.
    /// </summary>
    internal sealed class UninstallForm : Form
    {
        private readonly string installDir;
        private readonly bool quiet;
        private readonly CheckBox chkData;
        private bool running;

        internal UninstallForm(string installDir, bool quiet)
        {
            this.installDir = installDir;
            this.quiet = quiet;

            Text = "卸载 " + InstallCore.AppName;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = SystemFonts.MessageBoxFont;

            Lay lay = new Lay(Font);
            AutoScaleMode = AutoScaleMode.None;   // Lay does the scaling
            ClientSize = lay.S(470, 300);
            Font = lay.F();

            Controls.Add(MakeLabel(lay, "确定要卸载 " + InstallCore.AppName + " 吗？",
                20, 16, 430, 28, lay.F(13F, FontStyle.Bold), Color.Black));
            Controls.Add(MakeLabel(lay, "安装位置\r\n    " + installDir,
                20, 52, 430, 40, lay.F(), Color.DimGray));
            chkData = new CheckBox
            {
                Text = "同时删除设置和日志",
                Location = lay.P(20, 102),
                Size = lay.S(430, 22),
                Font = lay.F(),
                Checked = false
            };
            Controls.Add(chkData);
            Controls.Add(MakeLabel(lay, InstallCore.DataDir,
                20, 132, 430, 20, lay.F(), Color.DimGray));
            Controls.Add(MakeLabel(lay,
                "卸载前会先把可能被改过的电源设置恢复原样。",
                20, 160, 430, 20, lay.F(), Color.DimGray));

            Label status = MakeLabel(lay, "", 20, 192, 430, 22, lay.F(), Color.Black);
            Controls.Add(status);
            ProgressBar bar = new ProgressBar
            {
                Location = lay.P(20, 218),
                Size = lay.S(430, 16),
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 0,
                Visible = false
            };
            Controls.Add(bar);

            Button yes = MakeButton(lay, "卸载", lay.P(368, 256), lay.S(82, 27));
            yes.Click += delegate { Begin2(status, bar, yes); };
            Controls.Add(yes);
            Button no = MakeButton(lay, "取消", lay.P(278, 256), lay.S(82, 27));
            no.Click += delegate { Close(); };
            Controls.Add(no);
            AcceptButton = yes;
            CancelButton = no;
        }

        private void Begin2(Label status, ProgressBar bar, Button yes)
        {
            if (running) return;
            running = true;
            bool deleteData = chkData.Checked;
            yes.Enabled = false;
            bar.Visible = true;
            bar.MarqueeAnimationSpeed = 30;   // Style is already Marquee
            status.Text = "正在卸载…";
            Application.DoEvents();

            if (Uninstall(installDir, deleteData, quiet))
            {
                InstallCore.Log("已把卸载工作交给临时副本");
            }
            Close();
        }

        /// <summary>
        /// True if this exe is running from inside the directory we are about to
        /// delete.  An exe cannot delete itself, so the work is handed to a copy
        /// in %TEMP%.
        /// </summary>
        internal static bool RunsInside(string dir)
        {
            try
            {
                string me = System.IO.Path.GetFullPath(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                string target = System.IO.Path.GetFullPath(dir);
                return me.StartsWith(target.TrimEnd('\\') + "\\",
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Returns true if the work was handed off to a temp copy.</summary>
        internal static bool Uninstall(string installDir, bool deleteData, bool quiet)
        {
            if (!RunsInside(installDir))
            {
                RunStage2(installDir, deleteData, quiet);
                return false;
            }

            string self = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string stage2 = Path.Combine(Path.GetTempPath(),
                "Caffeine-uninstall-" + Process.GetCurrentProcess().Id + ".exe");
            try
            {
                File.Copy(self, stage2, true);
            }
            catch (Exception ex)
            {
                if (!quiet)
                {
                    MessageBox.Show("无法准备卸载程序：\r\n" + ex.Message, InstallCore.AppName,
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return false;
            }

            string args = "--uninstall-stage2 \"" + installDir + "\""
                        + (deleteData ? " --delete-data" : "")
                        + (quiet ? " --quiet" : "");
            try
            {
                Process.Start(new ProcessStartInfo(stage2, args) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                if (!quiet)
                {
                    MessageBox.Show("无法启动卸载程序：\r\n" + ex.Message, InstallCore.AppName,
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return false;
            }
            InstallCore.Log("已把卸载工作交给 " + stage2);
            return true;
        }

        /// <summary>The part that actually deletes things.  No UI when quiet.</summary>
        internal static void RunStage2(string installDir, bool deleteData, bool quiet)
        {
            try
            {
                InstallCore.Log("开始卸载 " + installDir + " deleteData=" + deleteData);
                InstallCore.StopRunningApp();
                InstallCore.RestorePowerSettings(installDir);
                InstallCore.RemoveShortcuts(true);
                InstallCore.SetAutoStart(false, null);
                InstallCore.Unregister();
                InstallCore.DeleteDirRecursive(installDir);
                if (deleteData) InstallCore.DeleteDataDir();
                InstallCore.Log("卸载完成");
            }
            catch (Exception ex)
            {
                InstallCore.Log("卸载出错: " + ex);
            }
            finally
            {
                if (!quiet)
                {
                    MessageBox.Show("已卸载 " + InstallCore.AppName + "。\r\n\r\n" +
                        "你的电源设置已经恢复原样。", InstallCore.AppName,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private static Label MakeLabel(Lay lay, string text, int x, int y, int w, int h,
            Font font, Color color)
        {
            return new Label
            {
                Text = text,
                Location = lay.P(x, y),
                Size = lay.S(w, h),
                Font = font,
                ForeColor = color,
                BackColor = Color.Transparent
            };
        }

        private static Button MakeButton(Lay lay, string text, Point p, Size s)
        {
            return new Button { Text = text, Location = p, Size = s, Font = lay.F() };
        }
    }
}
