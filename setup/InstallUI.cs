using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace CaffeineSetup
{
    /// <summary>
    /// Three-stage installer window: choose -> progress -> done.  Every
    /// coordinate below is in 96dpi units; <see cref="Lay"/> scales bounds and
    /// fonts together so the window is identical on a 100% or a 200% display.
    /// </summary>
    internal sealed class InstallForm : Form
    {
        private readonly Panel choose;
        private readonly Panel progress;
        private readonly Panel done;

        private readonly Label lblVersion;
        private readonly TextBox txtDir;
        private readonly CheckBox chkAuto;
        private readonly CheckBox chkDesktop;
        private readonly CheckBox chkLaunch;
        private readonly Label lblStatus;
        private readonly ProgressBar bar;
        private readonly Label lblDone;
        private readonly Label lblDetail;

        private readonly string kindText;
        private bool running;

        /// <summary>Values the command line pinned down; null means "ask the user".</summary>
        internal sealed class Options
        {
            internal string Dir;
            internal bool? AutoStart;
            internal bool? Launch;
            internal bool? Desktop;
        }

        internal InstallForm(Options options)
        {
            options = options ?? new Options();
            string from;
            InstallCore.UpgradeKind kind = InstallCore.Classify(out from);
            kindText = Describe(kind, from);

            Text = "安装 " + InstallCore.AppName;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = SystemFonts.MessageBoxFont;

            Lay lay = new Lay(Font);
            AutoScaleMode = AutoScaleMode.None;   // Lay does the scaling
            ClientSize = lay.S(500, 380);
            Font = lay.F();

            choose = new Panel { Dock = DockStyle.Fill, Visible = true };
            progress = new Panel { Dock = DockStyle.Fill, Visible = false };
            done = new Panel { Dock = DockStyle.Fill, Visible = false };
            Controls.Add(done);
            Controls.Add(progress);
            Controls.Add(choose);

            // ---- stage 1 -------------------------------------------------
            choose.Controls.Add(MakeLabel(lay, "安装 " + InstallCore.AppName,
                20, 14, 460, 28, lay.F(13F, FontStyle.Bold), Color.Black));
            lblVersion = MakeLabel(lay, kindText, 20, 48, 460, 22, lay.F(), Color.DimGray);
            choose.Controls.Add(lblVersion);

            choose.Controls.Add(MakeLabel(lay, "安装位置", 20, 80, 200, 20, lay.F(), Color.Black));
            txtDir = new TextBox
            {
                Location = lay.P(20, 102),
                Size = lay.S(362, 23),
                ReadOnly = true,
                Font = lay.F(),
                Text = string.IsNullOrEmpty(options.Dir) ? InstallCore.DefaultDir : options.Dir
            };
            choose.Controls.Add(txtDir);
            Button browse = MakeButton(lay, "浏览…", lay.P(392, 100), lay.S(88, 27));
            browse.Click += delegate
            {
                using (FolderBrowserDialog dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "选择安装位置";
                    dlg.SelectedPath = txtDir.Text;
                    dlg.ShowNewFolderButton = true;
                    if (dlg.ShowDialog(this) == DialogResult.OK) txtDir.Text = dlg.SelectedPath;
                }
            };
            choose.Controls.Add(browse);

            chkAuto = MakeCheck(lay, "开机自动启动", 20, 138,
                options.AutoStart.HasValue ? options.AutoStart.Value : InstallCore.IsAutoStartOn);
            chkAuto.Enabled = !options.AutoStart.HasValue;
            chkDesktop = MakeCheck(lay, "在桌面上创建快捷方式", 20, 164,
                options.Desktop.HasValue ? options.Desktop.Value : false);
            chkDesktop.Enabled = !options.Desktop.HasValue;
            chkLaunch = MakeCheck(lay, "安装完成后立即启动", 20, 190,
                options.Launch.HasValue ? options.Launch.Value : true);
            chkLaunch.Enabled = !options.Launch.HasValue;
            choose.Controls.Add(chkAuto);
            choose.Controls.Add(chkDesktop);
            choose.Controls.Add(chkLaunch);

            choose.Controls.Add(MakeLabel(lay,
                "安装到当前用户目录，不需要管理员权限。\r\n" +
                "设置与日志保存在 " + InstallCore.DataDir + "，\r\n升级时会保留。",
                20, 226, 460, 62, lay.F(), Color.DimGray));

            Button install = MakeButton(lay, "安装", lay.P(392, 336), lay.S(88, 27));
            install.Click += delegate { Start(); };
            choose.Controls.Add(install);
            Button cancel = MakeButton(lay, "取消", lay.P(296, 336), lay.S(88, 27));
            cancel.Click += delegate { Close(); };
            choose.Controls.Add(cancel);
            AcceptButton = install;
            CancelButton = cancel;

            // ---- stage 2 -------------------------------------------------
            lblStatus = MakeLabel(lay, "正在准备…", 20, 150, 460, 44, lay.F(), Color.Black);
            lblStatus.TextAlign = ContentAlignment.MiddleCenter;
            progress.Controls.Add(lblStatus);
            bar = new ProgressBar
            {
                Location = lay.P(20, 206),
                Size = lay.S(460, 18),
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 0
            };
            progress.Controls.Add(bar);
            Button cancel2 = MakeButton(lay, "取消", lay.P(392, 336), lay.S(88, 27));
            cancel2.Click += delegate { Close(); };
            progress.Controls.Add(cancel2);

            // ---- stage 3 -------------------------------------------------
            lblDone = MakeLabel(lay, "安装完成", 20, 34, 460, 30,
                lay.F(15F, FontStyle.Bold), Color.FromArgb(0, 110, 40));
            done.Controls.Add(lblDone);
            lblDetail = MakeLabel(lay, "", 20, 76, 460, 250, lay.F(), Color.Black);
            done.Controls.Add(lblDetail);
            Button folder = MakeButton(lay, "打开数据文件夹", lay.P(20, 336), lay.S(150, 27));
            folder.Click += delegate
            {
                try { Process.Start("explorer.exe", InstallCore.DataDir); } catch (Exception) { }
            };
            done.Controls.Add(folder);
            Button close = MakeButton(lay, "完成", lay.P(392, 336), lay.S(88, 27));
            close.Click += delegate { Close(); };
            done.Controls.Add(close);
            AcceptButton = close;
        }

        private string Describe(InstallCore.UpgradeKind kind, string from)
        {
            switch (kind)
            {
                case InstallCore.UpgradeKind.Upgrade:
                    return "检测到已安装的 " + from + "，将升级到 " + InstallCore.PayloadVersion;
                case InstallCore.UpgradeKind.Downgrade:
                    return "已安装的 " + from + " 比本安装包还新，仍要继续吗？";
                case InstallCore.UpgradeKind.SameVersion:
                    return InstallCore.PayloadVersion + " 已经安装过了，可以重新安装修复。";
                default:
                    return "全新安装 · 版本 " + InstallCore.PayloadVersion;
            }
        }

        private void Start()
        {
            if (running) return;
            if (txtDir.Text.Trim().Length == 0)
            {
                MessageBox.Show("请选择安装位置。", InstallCore.AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string dir = txtDir.Text.Trim();
            bool auto = chkAuto.Checked;
            bool desktop = chkDesktop.Checked;
            bool launch = chkLaunch.Checked;

            running = true;
            choose.Visible = false;
            progress.Visible = true;
            lblStatus.Text = "正在安装…";
            bar.MarqueeAnimationSpeed = 30;   // Style is already Marquee
            Application.DoEvents();

            string error = null;
            Thread worker = new Thread(delegate()
            {
                try
                {
                    InstallCore.Log("开始安装 " + InstallCore.PayloadVersion + " 到 " + dir);
                    Status("正在关闭正在运行的 " + InstallCore.AppName + "…");
                    int killed = InstallCore.StopRunningApp();
                    if (killed > 0) InstallCore.Log("已关闭 " + killed + " 个运行中的实例");
                    Thread.Sleep(600);
                    if (InstallCore.IsAppRunning())
                        throw new InvalidOperationException(
                            "咖啡因还在运行，程序文件被占用。\r\n\r\n" +
                            "请先在托盘图标上点右键 → 退出，然后再运行本安装程序。");

                    Status("正在把电源设置恢复原样…");
                    InstallCore.RestorePowerSettings(dir);

                    Status("正在写入程序文件…");
                    Directory.CreateDirectory(dir);
                    InstallCore.WritePayload(dir);

                    Status("正在创建快捷方式…");
                    InstallCore.CreateShortcuts(dir, desktop);

                    Status("正在写入注册表…");
                    InstallCore.RegisterUninstall(dir, desktop);
                    InstallCore.SetAutoStart(auto, System.IO.Path.Combine(dir, InstallCore.ExeName));

                    InstallCore.Log("安装完成 v" + InstallCore.PayloadVersion +
                        " autostart=" + auto + " desktop=" + desktop);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    InstallCore.Log("安装失败: " + ex);
                }
            });
            worker.IsBackground = true;
            worker.Start();

            while (worker.IsAlive)
            {
                Application.DoEvents();
                Thread.Sleep(40);
            }

            bar.MarqueeAnimationSpeed = 0;
            bar.Style = ProgressBarStyle.Blocks;
            bar.Value = 100;
            if (error != null)
            {
                progress.Visible = false;
                choose.Visible = true;
                running = false;
                MessageBox.Show("安装失败：\r\n" + error, InstallCore.AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (launch)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(
                        System.IO.Path.Combine(dir, InstallCore.ExeName)) { UseShellExecute = true });
                }
                catch (Exception) { }
            }

            progress.Visible = false;
            done.Visible = true;
            lblDone.Text = "安装完成";
            lblDetail.Text =
                "版本 " + InstallCore.PayloadVersion + "\r\n\r\n" +
                "安装位置\r\n    " + dir + "\r\n\r\n" +
                "开机自动启动\r\n    " + (auto ? "已开启" : "未开启") + "\r\n\r\n" +
                "托盘图标在右下角。Windows 11 可能把它藏在「^」里，\r\n" +
                "点开找到咖啡杯，拖到外面就固定了。\r\n\r\n" +
                "左键点图标 = 保持唤醒，再点一次 = 恢复你原来的设置。\r\n" +
                "右键点图标 = 菜单（开机自启、允许熄屏、卸载等）。";
            lblDetail.ForeColor = Color.Black;
        }

        private void Status(string text)
        {
            try
            {
                if (IsHandleCreated && !IsDisposed)
                {
                    MethodInvoker set = delegate { lblStatus.Text = text; };
                    BeginInvoke(set);
                }
            }
            catch (Exception)
            {
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

        private static CheckBox MakeCheck(Lay lay, string text, int x, int y, bool value)
        {
            return new CheckBox
            {
                Text = text,
                Location = lay.P(x, y),
                Size = lay.S(400, 22),
                Font = lay.F(),
                Checked = value
            };
        }
    }
}
