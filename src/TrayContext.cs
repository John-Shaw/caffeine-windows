using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace Caffeine
{
    /// <summary>The tray icon, its menu, and the keep-awake state machine.</summary>
    internal sealed class TrayContext : ApplicationContext
    {
        private const int WM_DPICHANGED = 0x02E0;

        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu;
        private readonly System.Windows.Forms.Timer esTimer;
        private readonly DpiWatcher watcher;
        private readonly UserSettings settings;

        private ToolStripMenuItem statusItem;
        private ToolStripMenuItem restoreItem;
        private ToolStripMenuItem displayItem;
        private ToolStripMenuItem autoStartItem;
        private ToolStripMenuItem autoAwakeItem;
        private ToolStripMenuItem adminItem;

        private bool keepAwake;
        private PowerSnapshot snapshot;
        private bool mouseIsDown;
        private DateTime mouseDownAt;
        private DateTime lastToggleAt;
        private bool shuttingDown;

        internal TrayContext()
        {
            settings = UserSettings.Load();

            RecoverPendingRestore();

            tray = new NotifyIcon();
            tray.Icon = AppIcons.Get(false);
            tray.Visible = true;

            menu = new ContextMenuStrip();
            statusItem = new ToolStripMenuItem("状态：未激活（正常跟随系统设置）");
            statusItem.Enabled = false;
            restoreItem = new ToolStripMenuItem("立即恢复默认设置");
            restoreItem.Click += delegate { Deactivate(true); };
            displayItem = new ToolStripMenuItem("允许显示器自动熄屏");
            // CheckOnClick is deliberately NOT used: it toggles at a point
            // that is awkward to reason about from the handler below, so the
            // handler flips the box itself and then applies the new value.
            displayItem.Click += delegate
            {
                displayItem.Checked = !displayItem.Checked;
                OnDisplayPolicyChanged();
            };
            displayItem.Checked = settings.AllowDisplaySleep;
            autoStartItem = new ToolStripMenuItem("开机自动启动");
            autoStartItem.Click += delegate
            {
                autoStartItem.Checked = !autoStartItem.Checked;
                OnAutoStartChanged();
            };
            autoStartItem.Checked = UserSettings.IsAutoStartOn();
            autoAwakeItem = new ToolStripMenuItem("启动后自动保持唤醒");
            autoAwakeItem.Click += delegate
            {
                autoAwakeItem.Checked = !autoAwakeItem.Checked;
                OnAutoAwakeChanged();
            };
            autoAwakeItem.Checked = settings.AutoAwake;
            adminItem = new ToolStripMenuItem("以管理员身份重新启动");
            adminItem.Click += delegate { RestartAsAdmin(); };
            adminItem.Visible = !PowerKeeper.IsAdmin();

            ToolStripMenuItem folderItem = new ToolStripMenuItem("打开数据文件夹");
            folderItem.Click += delegate { OpenDataFolder(); };
            ToolStripMenuItem exitItem = new ToolStripMenuItem("退出");
            exitItem.Click += delegate { ExitApp(); };

            menu.Renderer = new CrispMenuRenderer();
            menu.Items.Add(statusItem);
            menu.Items.Add(restoreItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(displayItem);
            menu.Items.Add(autoStartItem);
            menu.Items.Add(autoAwakeItem);
            menu.Items.Add(adminItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(folderItem);
            menu.Items.Add(exitItem);
            ApplyItemSpacing(menu);

            tray.ContextMenuStrip = menu;
            tray.MouseDown += OnTrayMouseDown;
            tray.MouseUp += OnTrayMouseUp;
            RefreshTrayIcon();

            esTimer = new System.Windows.Forms.Timer();
            esTimer.Interval = 10000;
            esTimer.Tick += delegate { PulseExecutionState(); };

            watcher = new DpiWatcher(delegate { RefreshTrayIcon(); });
            SystemEventsHook();

            AppLog.Write("托盘已就绪，小图标尺寸=" + AppIcons.SmallIconSize +
                         "px，管理员=" + PowerKeeper.IsAdmin());

            // Must be last: Activate() needs the tray, the menu items and the
            // 10s pulse timer to all exist already.
            ApplyAutoAwakeOnStartup();
        }

        /// <summary>
        /// "Start already awake".  This is the whole point for a machine that
        /// serves something to the network (a render box, a small server): after
        /// a reboot nobody is there to click the tray icon, and the machine
        /// would simply fall asleep again.
        ///
        /// Runs after RecoverPendingRestore(), so the snapshot it takes is the
        /// restored one -- capturing the "never sleep" values of a crashed
        /// previous run would make the restore impossible.
        ///
        /// Only affects startup: clicking the tray icon afterwards still wins,
        /// so the user can turn it off for the rest of the session.
        /// </summary>
        private void ApplyAutoAwakeOnStartup()
        {
            if (!settings.AutoAwake || keepAwake) return;
            AppLog.Write("已启用「启动后自动保持唤醒」，正在自动进入保活");
            Activate();
            if (keepAwake)
                Balloon("咖啡因已进入保活",
                    "已根据「启动后自动保持唤醒」自动开启。托盘图标点一下即可随时恢复你原来的设置。");
        }

        private void SystemEventsHook()
        {
            try
            {
                Microsoft.Win32.SystemEvents.SessionEnding += OnSessionEnding;
            }
            catch (Exception ex)
            {
                AppLog.Error("注册关机事件失败", ex);
            }
        }

        // ------------------------------------------------------------ state

        /// <summary>
        /// Toggle on mouse *up*, not down.  Holding the button down makes Windows
        /// auto-repeat WM_LBUTTONDOWN, which used to flip the state (and pop a
        /// balloon) dozens of times per second.  Auto-repeat, long presses and
        /// the second click of a double click are all ignored.
        /// </summary>
        private void OnTrayMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (mouseIsDown) return;          // auto-repeat while still held
            mouseIsDown = true;
            mouseDownAt = DateTime.Now;
        }

        private void OnTrayMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (!mouseIsDown) return;
            mouseIsDown = false;
            if ((DateTime.Now - mouseDownAt).TotalMilliseconds > 600) return; // held, not clicked
            if ((DateTime.Now - lastToggleAt).TotalMilliseconds <=
                SystemInformation.DoubleClickTime) return;                   // 2nd click of a double click
            lastToggleAt = DateTime.Now;
            Toggle();
        }

        internal void Toggle()
        {
            if (keepAwake) Deactivate(false);
            else Activate();
        }

        private void Activate()
        {
            snapshot = PowerKeeper.Capture();
            if (snapshot.ReadOk)
                PowerKeeper.SavePending(snapshot);
            else
                PowerKeeper.DeletePending();

            AppLog.Write("点击 → 保持唤醒。原始设置：" + snapshot.Summary());

            PowerResult res = PowerKeeper.ApplyAwake(snapshot, settings.AllowDisplaySleep);
            uint es = PowerKeeper.SetThreadExecutionState(ExecutionStateFlags());
            AppLog.Write(string.Format(CultureInfo.InvariantCulture,
                "SetThreadExecutionState -> 0x{0:X8}（返回上次状态）", es));

            keepAwake = true;
            esTimer.Start();
            RefreshTrayIcon();

            if (res.SchemeWriteFailed)
            {
                // a real failure: the power scheme could not be changed at all
                Balloon("无法修改电源设置", "请从右键菜单选择「以管理员身份重新启动」后再点一次。");
            }
            else if (res.HibernateFailed && !settings.HibernateHintShown)
            {
                // Only the `powercfg /hibernate off` step needs elevation, and
                // without it the machine still cannot sleep.  Say it once.
                settings.HibernateHintShown = true;
                settings.Save();
                Balloon("保活已生效",
                    "系统睡眠与息屏已关闭。若想连系统休眠也一并关掉，请从右键菜单选择「以管理员身份重新启动」。");
            }
            else if (!settings.WelcomeShown)
            {
                settings.WelcomeShown = true;
                settings.Save();
                Balloon("咖啡因已就绪",
                    "左键点图标 = 保持唤醒，再点一次 = 恢复你原来的设置。图标若在「显示隐藏的图标」里，可拖到外面固定。");
            }
            // otherwise: stay silent.  The icon change is the feedback.
        }

        private void Deactivate(bool fromMenu)
        {
            if (!keepAwake) return;

            esTimer.Stop();
            PowerKeeper.SetThreadExecutionState(PowerKeeper.ES_CONTINUOUS); // release our request

            PowerSnapshot snap = snapshot != null ? snapshot : PowerKeeper.LoadPending();
            AppLog.Write("点击 → 恢复默认设置。使用快照：" + (snap == null ? "无" : snap.Summary()));
            PowerResult res = PowerKeeper.Restore(snap);
            PowerKeeper.DeletePending();

            keepAwake = false;
            snapshot = null;
            RefreshTrayIcon();

            // Only complain when something actually went wrong; a clean restore
            // is obvious from the icon going back to the empty cup.
            if (res.UsedFallback)
                Balloon("已使用默认设置", "未能读取你原来的电源设置，已恢复为默认：1 小时后进入睡眠。");
            else if (res.NeedsAdmin && !res.UsedFallback)
                Balloon("部分恢复失败", "修改电源方案需要管理员权限，可从右键菜单选择「以管理员身份重新启动」后重试。");
        }

        private uint ExecutionStateFlags()
        {
            uint f = PowerKeeper.ES_CONTINUOUS | PowerKeeper.ES_SYSTEM_REQUIRED;
            if (!settings.AllowDisplaySleep) f |= PowerKeeper.ES_DISPLAY_REQUIRED;
            return f;
        }

        private void PulseExecutionState()
        {
            if (!keepAwake) return;
            PowerKeeper.SetThreadExecutionState(ExecutionStateFlags());
        }

        // ------------------------------------------------------------ ui

        private void RefreshTrayIcon()
        {
            if (tray == null) return;
            try
            {
                tray.Icon = AppIcons.Get(keepAwake);
                tray.Text = keepAwake
                    ? "咖啡因 · 保持唤醒中（" + HeldLayers() + "，再点一次恢复）"
                    : "咖啡因 · 未激活（点击保持唤醒）";
                statusItem.Text = keepAwake
                    ? "状态：保持唤醒中 · " + HeldLayers()
                    : "状态：未激活（跟随系统默认设置）";
                restoreItem.Enabled = keepAwake;
            }
            catch (Exception ex)
            {
                AppLog.Error("刷新托盘图标失败", ex);
            }
        }

        /// <summary>
        /// Names the layers that are actually held.  "I can never tell whether
        /// it is really working" was a real complaint, and a filled cup on its
        /// own does not say which of the four timeouts are pinned.
        /// ApplyAwake always writes sleep, hibernation and the disk timeout to
        /// "never"; the display is the only one that is optional.
        /// </summary>
        private string HeldLayers()
        {
            return settings.AllowDisplaySleep
                ? "睡眠/休眠/硬盘已阻止，屏幕可熄"
                : "睡眠/休眠/硬盘/息屏已阻止";
        }

        private void Balloon(string title, string text)
        {
            try
            {
                if (tray != null && tray.Visible)
                    tray.ShowBalloonTip(4000, title, text, ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                AppLog.Error("显示气泡提示失败", ex);
            }
        }

        private void OnDisplayPolicyChanged()
        {
            settings.AllowDisplaySleep = displayItem.Checked;
            settings.Save();
            if (keepAwake)
            {
                bool ok = PowerKeeper.ApplyDisplayPolicy(snapshot, settings.AllowDisplaySleep);
                PowerKeeper.SetThreadExecutionState(ExecutionStateFlags());
                AppLog.Write("允许显示器熄屏 -> " + settings.AllowDisplaySleep + "，写入=" + ok);
                if (!ok) Balloon("无法调整", "修改显示器超时需要管理员权限。");
            }
            else
            {
                AppLog.Write("允许显示器熄屏 -> " + settings.AllowDisplaySleep);
            }
            // The status line names the held layers, so it has to follow this.
            RefreshTrayIcon();
        }

        private void OnAutoStartChanged()
        {
            if (UserSettings.SetAutoStart(autoStartItem.Checked))
            {
                settings.AutoStart = autoStartItem.Checked;
                settings.Save();
                AppLog.Write("开机自动启动 -> " + autoStartItem.Checked);
            }
            else
            {
                autoStartItem.Checked = !autoStartItem.Checked;
                Balloon("无法修改", "写入开机启动项失败。");
            }
        }

        private void OnAutoAwakeChanged()
        {
            settings.AutoAwake = autoAwakeItem.Checked;
            settings.Save();
            AppLog.Write("启动后自动保持唤醒 -> " + settings.AutoAwake);
            if (settings.AutoAwake && !UserSettings.IsAutoStartOn())
            {
                Balloon("还差一步：开机自启",
                    "「启动后自动保持唤醒」只在咖啡因自己启动的时候才生效。\n\n" +
                    "请同时勾选上面的「开机自动启动」，否则每次开机后仍要手动点一次图标。");
            }
        }

        private void OpenDataFolder()
        {
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", "\"" + AppPaths.Root + "\"");
            }
            catch (Exception ex)
            {
                AppLog.Error("打开数据文件夹失败", ex);
            }
        }

        private void RestartAsAdmin()
        {
            Deactivate(false);
            try
            {
                string exe = UserSettings.ExePath;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "--elevated")
                {
                    UseShellExecute = true,
                    Verb = "runas"
                });
                AppLog.Write("以管理员身份重启：" + exe);
                ExitApp();
            }
            catch (Exception ex)
            {
                AppLog.Error("以管理员身份重启失败", ex);
                Balloon("重启失败", ex.Message);
            }
        }

        private void OnSessionEnding(object sender, Microsoft.Win32.SessionEndingEventArgs e)
        {
            if (!keepAwake || shuttingDown) return;
            shuttingDown = true;
            try
            {
                // Never let the machine be left with "never sleep" because the
                // user logged off or shut down while Caffeine was holding it awake.
                AppLog.Write("检测到" + e.Reason + "，退出前先恢复默认设置");
                PowerSnapshot snap = snapshot != null ? snapshot : PowerKeeper.LoadPending();
                PowerKeeper.Restore(snap);
                PowerKeeper.DeletePending();
                keepAwake = false;
            }
            catch (Exception ex)
            {
                AppLog.Error("关机前恢复设置失败", ex);
            }
        }

        // ------------------------------------------------------------ lifecycle

        /// <summary>
        /// A previous run died while the system was held awake.  Put the user's
        /// settings back before the tray icon even appears.
        /// </summary>
        private void RecoverPendingRestore()
        {
            try
            {
                if (!File.Exists(AppPaths.PendingRestoreFile)) return;
                PowerSnapshot snap = PowerKeeper.LoadPending();
                AppLog.Write("发现上次未恢复的设置（" + (snap == null ? "无法解析" : snap.Summary()) + "），立即恢复");
                PowerResult res = PowerKeeper.Restore(snap);
                PowerKeeper.DeletePending();
                AppLog.Write("启动时恢复结果：detail=" + res.Detail + " fallback=" + res.UsedFallback);
            }
            catch (Exception ex)
            {
                AppLog.Error("启动时恢复设置失败", ex);
            }
        }

        private void ExitApp()
        {
            if (keepAwake)
            {
                if (MessageBox.Show("咖啡因正在保持唤醒。\n\n退出会立刻把你原来的电源设置恢复回来，确定退出吗？",
                        "咖啡因", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                    return;
                Deactivate(false);
            }
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    if (keepAwake) Deactivate(false);
                    PowerKeeper.SetThreadExecutionState(PowerKeeper.ES_CONTINUOUS);
                    if (esTimer != null) { esTimer.Stop(); esTimer.Dispose(); }
                    if (tray != null)
                    {
                        tray.Visible = false;
                        tray.Dispose();
                    }
                    if (menu != null) menu.Dispose();
                    if (watcher != null) { watcher.DestroyHandle(); }
                    try { Microsoft.Win32.SystemEvents.SessionEnding -= OnSessionEnding; }
                    catch (Exception) { }
                }
                catch (Exception ex)
                {
                    AppLog.Error("退出清理失败", ex);
                }
            }
            base.Dispose(disposing);
        }

        private const float MenuRowPad = 5f;   // logical px added above/below a row

        /// <summary>
        /// Vertical breathing room between menu rows.
        ///
        /// WinForms sizes a menu item to exactly its text, which on a 200%
        /// display measured out at a 36px row around 23px of glyphs - about 6
        /// device px of air above and below.  Chinese text needs more than that
        /// or the rows read as one solid block.  Padding the items is the
        /// supported way to grow the row; the tick column keeps its old width
        /// so only the vertical rhythm changes.
        ///
        /// Measured, not guessed: tests\shot-menu.ps1 shoots the real popup and
        /// tools\probe_tray.py reports the resulting row pitch.
        /// </summary>
        private static void ApplyItemSpacing(ContextMenuStrip menu)
        {
            float k = 1f;
            try
            {
                if (menu.DeviceDpi > 0) k = menu.DeviceDpi / 96f;
            }
            catch (Exception) { k = 1f; }

            int pad = (int)Math.Round(MenuRowPad * k);
            if (pad < 1) pad = 1;

            foreach (ToolStripItem it in menu.Items)
            {
                if (it is ToolStripSeparator) continue;   // separators stay thin
                it.Padding = new Padding(it.Padding.Left, pad, it.Padding.Right, pad);
            }
        }
    }

    /// <summary>
    /// The stock professional renderer draws menu text with GDI+ ClearType,
    /// which fringes on a layered popup, and its tick mark is so pale that you
    /// cannot tell whether an option is on.  This one paints the text with GDI
    /// onto the opaque menu background, and draws a fat, dark tick (plus bold
    /// text) for anything that is checked.
    /// </summary>
    internal sealed class CrispMenuRenderer : ToolStripProfessionalRenderer
    {
        /// <summary>ToolStripItem has no CheckState; menu items do.</summary>
        private static CheckState StateOf(ToolStripItem item)
        {
            ToolStripMenuItem mi = item as ToolStripMenuItem;
            return mi == null ? CheckState.Unchecked : mi.CheckState;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            Color color = e.Item.Enabled ? e.Item.ForeColor : SystemColors.GrayText;
            bool on = StateOf(e.Item) != CheckState.Unchecked;
            Font font = e.TextFont;
            Font bold = null;
            if (on && font != null && !font.Bold)
            {
                bold = new Font(font, FontStyle.Bold);
                font = bold;
            }
            try
            {
                TextRenderer.DrawText(e.Graphics, e.Text, font, e.TextRectangle, color,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix |
                    TextFormatFlags.NoClipping | TextFormatFlags.Left);
            }
            finally
            {
                if (bold != null) bold.Dispose();
            }
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            if (StateOf(e.Item) == CheckState.Unchecked) return;

            Rectangle r = e.ImageRectangle;
            float k = 1f;
            if (e.ToolStrip != null && e.ToolStrip.DeviceDpi > 0)
                k = e.ToolStrip.DeviceDpi / 96f;

            int w = (int)Math.Round(2.0f * k);          // stroke weight
            if (w < 2) w = 2;
            int s = (int)Math.Round(9.0f * k);          // tick size
            int cx = r.Left + r.Width / 2;
            int cy = r.Top + r.Height / 2;

            Point[] pts = new Point[] {
                new Point(cx - s / 2,          cy),
                new Point(cx - s / 6,          cy + s / 2),
                new Point(cx + s / 2,          cy - s / 2)
            };
            SmoothingMode saved = e.Graphics.SmoothingMode;
            try
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = new GraphicsPath())
                using (Pen pen = new Pen(Color.FromArgb(0, 104, 55), w))
                {
                    path.AddLines(pts);
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    e.Graphics.DrawPath(pen, path);
                }
            }
            finally
            {
                e.Graphics.SmoothingMode = saved;
            }
        }
    }

    /// <summary>
    /// Message-only window used for the two events ApplicationContext has no hook
    /// for: a DPI change (re-pick the tray icon size) and session end.
    /// </summary>
    internal sealed class DpiWatcher : NativeWindow
    {
        private readonly Action onDpiChanged;

        internal DpiWatcher(Action onDpiChanged)
        {
            this.onDpiChanged = onDpiChanged;
            CreateHandle(new CreateParams { Caption = "Caffeine.Watcher" });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x02E0 && onDpiChanged != null) // WM_DPICHANGED
            {
                try { onDpiChanged(); }
                catch (Exception ex) { AppLog.Error("DPI 变化处理失败", ex); }
            }
            base.WndProc(ref m);
        }
    }
}
