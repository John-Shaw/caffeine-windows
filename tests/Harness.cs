// Test harness - NOT part of the shipped app.
// It compiles the real sources into a console exe so the keep-awake / restore
// logic can be driven and asserted without a human clicking the tray icon.
//
//   powershell -File tests\run-tests.ps1

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Caffeine
{
    internal static class Harness
    {
        private static int failures;
        private static int checks;

        private static readonly Guid SubSleep = new Guid("238c9fa8-0aad-41ed-83f4-97be242c8f20");
        private static readonly Guid SubVideo = new Guid("7516b95f-f776-4464-8c53-06167f40cc99");
        private static readonly Guid SubDisk = new Guid("0012ee47-9041-4b5d-9b77-535fba8b1442");
        private static readonly Guid StandbyIdle = new Guid("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");
        private static readonly Guid HibernateIdle = new Guid("9d7815a6-7ee4-497e-8888-515a05f02364");
        private static readonly Guid VideoIdle = new Guid("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");
        private static readonly Guid DiskIdle = new Guid("6738e2c4-e8a5-4a42-b16a-e040e769756e");

        [STAThread]
        private static int Main()
        {
            Application.EnableVisualStyles();
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            Title("环境");
            Console.WriteLine("管理员权限      : " + PowerKeeper.IsAdmin());
            Console.WriteLine("待恢复文件      : " + (File.Exists(AppPaths.PendingRestoreFile) ? "存在" : "无"));

            // settings.cfg is real user state as well.  The tests flip
            // allow_display_sleep and auto_awake, and auto_awake silently
            // decides whether a server comes back up awake after a reboot --
            // clobbering it would be a real, invisible loss.  Keep the bytes.
            bool hadSettings = File.Exists(AppPaths.SettingsFile);
            byte[] settingsBackup = hadSettings ? File.ReadAllBytes(AppPaths.SettingsFile) : null;
            bool backupAutoAwake = hadSettings && UserSettings.Load().AutoAwake;
            Console.WriteLine("进入测试前的 settings.cfg: " + (hadSettings ? "已备份" : "不存在"));

            // A user who turned on "start already awake" (a render box, a small
            // server) would get every TrayContext below coming up in keep-awake
            // mode by itself, which inverts what Toggle() is supposed to do here:
            // the first Toggle would deactivate instead of activate.  The harness
            // drives the state machine itself, so force the preference off for
            // the duration.  Section 7 sets it back on deliberately, and the
            // finally block puts the user's own bytes back.
            UserSettings neutral = UserSettings.Load();
            if (neutral.AutoAwake)
            {
                Console.WriteLine("进入测试前 auto_awake=true，测试期间强制置 false（结束后还原）");
                neutral.AutoAwake = false;
                neutral.Save();
            }

            PowerSnapshot before = PowerKeeper.Capture();
            Console.WriteLine("操作前的默认设置: " + before.Summary());
            if (!before.ReadOk)
            {
                Console.WriteLine("!! 无法读取默认设置，后续断言会失真，终止");
                return 2;
            }

            try
            {
                Title("1. 托盘实例 + 一次完整的 开 -> 关 循环");
                TrayContext ctx = new TrayContext();
                Console.WriteLine("创建 TrayContext 成功（托盘图标已注册）");

                ctx.Toggle();
                AssertState("开启后", 0u, true);
                AssertFile("开启后待恢复文件", true);

                ctx.Toggle();
                AssertSame("关闭后", before);
                AssertFile("关闭后待恢复文件", false);

                Title("2. 连续点击（等效于连点三下）");
                ctx.Toggle();
                ctx.Toggle();
                ctx.Toggle(); // -> awake
                AssertState("第三次点击后应为开启", 0u, true);
                ctx.Toggle(); // -> back to idle
                AssertSame("第四次点击后", before);
                ctx.Toggle(); // leave it awake for the next test
                AssertState("第五次点击后应为开启", 0u, true);

                Title("3. 允许显示器熄屏：开启期间不应把 VIDEOIDLE 归零");
                // the option is read when the tray context is created, so the
                // switch has to go through a fresh context
                ctx.Toggle(); // off
                AssertSame("关掉后", before);
                ((IDisposable)ctx).Dispose();

                UserSettings s = UserSettings.Load();
                s.AllowDisplaySleep = true;
                s.Save();
                TrayContext ctxA = new TrayContext();
                ctxA.Toggle();
                AssertSetting("VIDEOIDLE(开启时允许熄屏，应保持用户原值)",
                              SubVideo, VideoIdle, before.VideoAc, before.VideoDc);
                AssertSetting("STANDBYIDLE(开启时仍然禁止睡眠)", SubSleep, StandbyIdle, 0u);
                ctxA.Toggle();
                AssertSame("允许熄屏时关闭后", before);
                ((IDisposable)ctxA).Dispose();
                s.AllowDisplaySleep = false;
                s.Save();

                ctx = new TrayContext();

                Title("4. 崩溃恢复：留一个待恢复文件，新实例启动时必须自动还原");
                PowerSnapshot snap = PowerKeeper.Capture();
                PowerKeeper.SavePending(snap);
                Console.WriteLine("手工写入待恢复文件（模拟上次被强杀）: 交流=" + snap.StandbyAc + " 直流=" + snap.StandbyDc);
                Check("写入前 STANDBYIDLE 交流与快照一致",
                      PowerKeeper.ReadCurrent(SubSleep, StandbyIdle, true) == snap.StandbyAc);
                PowerKeeper.ApplyAwake(snap, false);
                AssertSetting("模拟开启后 STANDBYIDLE", SubSleep, StandbyIdle, 0u);

                TrayContext ctx2 = new TrayContext(); // ctor runs RecoverPendingRestore
                Check("新实例启动后 STANDBYIDLE 交流已还原为 " + snap.StandbyAc,
                      PowerKeeper.ReadCurrent(SubSleep, StandbyIdle, true) == snap.StandbyAc);
                Check("新实例启动后 STANDBYIDLE 直流已还原为 " + snap.StandbyDc,
                      PowerKeeper.ReadCurrent(SubSleep, StandbyIdle, false) == snap.StandbyDc);
                Check("新实例启动后 DISKIDLE 交流已还原为 " + snap.DiskAc,
                      PowerKeeper.ReadCurrent(SubDisk, DiskIdle, true) == snap.DiskAc);
                AssertFile("新实例启动后待恢复文件", false);
                ctx2.ExitThread();
                ((IDisposable)ctx2).Dispose();

                Title("5. 1 小时回退：没有原始设置时必须回到 1 小时");
                PowerResult fb = PowerKeeper.Fallback();
                Console.WriteLine("Fallback() -> usedFallback=" + fb.UsedFallback + " detail=" + fb.Detail);
                AssertSetting("回退后 STANDBYIDLE", SubSleep, StandbyIdle, 3600u);
                AssertSetting("回退后 VIDEOIDLE", SubVideo, VideoIdle, 3600u);
                // put the machine back exactly as we found it
                Restore(before);

                Title("6. 设置文件往返");
                Check("harness 已把 auto_awake 中性化为 false（否则本文件里的每个 TrayContext 都会自己进入保活）",
                      !UserSettings.Load().AutoAwake);
                s.AllowDisplaySleep = true; s.Save();
                bool roundTrip = UserSettings.Load().AllowDisplaySleep;
                Console.WriteLine("AllowDisplaySleep 写入 true 后读回 = " + roundTrip);
                Check("设置往返", roundTrip);
                s.AllowDisplaySleep = false; s.Save();

                Title("7. 启动后自动保持唤醒");
                // The case this exists for: a machine that serves something to
                // the network (a render box, a small server) is asleep right
                // after a reboot because nobody is there to click the icon.
                UserSettings aw = UserSettings.Load();
                aw.AutoAwake = true;
                aw.Save();
                Check("auto_awake 写入 true 后读回", UserSettings.Load().AutoAwake);

                TrayContext ctx3 = new TrayContext();  // nobody clicks anything
                AssertState("自动保活：构造后即已开启", 0u, true);
                AssertSetting("自动保活 STANDBYIDLE", SubSleep, StandbyIdle, 0u);
                AssertSetting("自动保活 HIBERNATEIDLE", SubSleep, HibernateIdle, 0u);
                AssertSetting("自动保活 DISKIDLE", SubDisk, DiskIdle, 0u);
                AssertFile("自动保活：待恢复文件已写入", true);

                // It must have snapshotted the *current* defaults, not the
                // "never" values it just wrote -- otherwise nothing could ever
                // be restored.
                PowerSnapshot autoSnap = PowerKeeper.LoadPending();
                Check("自动保活：快照有效", autoSnap != null && autoSnap.ReadOk);
                Check("自动保活：快照等于进入前的设置",
                      autoSnap != null && autoSnap.ReadOk &&
                      autoSnap.StandbyAc == before.StandbyAc &&
                      autoSnap.StandbyDc == before.StandbyDc &&
                      autoSnap.VideoAc == before.VideoAc &&
                      autoSnap.DiskAc == before.DiskAc);

                // Turning it off from the tray still wins, and must not
                // silently clear the preference itself.
                ctx3.Toggle();
                AssertSame("自动保活：手动关闭后", before);
                AssertFile("自动保活：手动关闭后待恢复文件", false);
                Check("手动关闭保活不会关掉 auto_awake 偏好", UserSettings.Load().AutoAwake);
                ctx3.ExitThread();
                ((IDisposable)ctx3).Dispose();

                aw.AutoAwake = false;
                aw.Save();
                Check("auto_awake 关闭后读回 false", !UserSettings.Load().AutoAwake);

                ctx.ExitThread();
                ((IDisposable)ctx).Dispose();
            }
            finally
            {
                Title("清理：必须把机器恢复成进来之前的样子");
                Restore(before);
                PowerKeeper.DeletePending();
                AssertSame("清理后", before);
                Check("清理后没有残留待恢复文件", !File.Exists(AppPaths.PendingRestoreFile));
                RestoreSettings(settingsBackup, hadSettings);
                // The byte-for-byte restore is the whole point: a user running
                // this as a server must not silently lose auto_awake=1 because
                // the tests happened to overwrite settings.cfg.
                Check("settings.cfg 已还原成进入测试前的内容",
                      !hadSettings || File.Exists(AppPaths.SettingsFile));
                if (hadSettings)
                {
                    byte[] now = File.ReadAllBytes(AppPaths.SettingsFile);
                    bool same = now.Length == settingsBackup.Length;
                    for (int i = 0; same && i < now.Length; i++) same = now[i] == settingsBackup[i];
                    Check("settings.cfg 逐字节一致", same);
                    Check("还原后 auto_awake 恢复为 " + UserSettings.Load().AutoAwake,
                          UserSettings.Load().AutoAwake == backupAutoAwake);
                }
            }

            Title("结果");
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "断言 {0} 个，失败 {1} 个", checks, failures));
            return failures == 0 ? 0 : 1;
        }

        private static void Restore(PowerSnapshot s)
        {
            // forced: the fallback test leaves non-zero values behind, and the
            // "only undo what we changed" guard would then skip everything
            if (s.ReadOk) PowerKeeper.RestoreForced(s);
            PowerKeeper.DeletePending();
            Console.WriteLine("  [cleanup] 已强制还原: " + s.Summary());
        }

        private static void RestoreSettings(byte[] backup, bool hadFile)
        {
            try
            {
                if (hadFile && backup != null)
                {
                    File.WriteAllBytes(AppPaths.SettingsFile, backup);
                    Console.WriteLine("  [cleanup] settings.cfg 已还原为进入测试前的内容");
                }
                else if (File.Exists(AppPaths.SettingsFile))
                {
                    File.Delete(AppPaths.SettingsFile);
                    Console.WriteLine("  [cleanup] settings.cfg 已删除（测试前本不存在）");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("  [cleanup] 还原 settings.cfg 失败: " + ex.Message);
            }
        }

        private static void Title(string t)
        {
            Console.WriteLine();
            Console.WriteLine("=== " + t + " ===");
        }

        private static void Check(string what, bool ok)
        {
            checks++;
            if (!ok) failures++;
            Console.WriteLine((ok ? "  [PASS] " : "  [FAIL] ") + what);
        }

        private static void AssertState(string when, uint expect, bool awake)
        {
            AssertSetting(when + " STANDBYIDLE", SubSleep, StandbyIdle, expect);
            AssertSetting(when + " HIBERNATEIDLE", SubSleep, HibernateIdle, expect);
            AssertSetting(when + " DISKIDLE", SubDisk, DiskIdle, expect);
        }

        private static void AssertSame(string when, PowerSnapshot s)
        {
            Check(when + " STANDBYIDLE 交流还原为 " + s.StandbyAc,
                  PowerKeeper.ReadCurrent(SubSleep, StandbyIdle, true) == s.StandbyAc);
            Check(when + " STANDBYIDLE 直流还原为 " + s.StandbyDc,
                  PowerKeeper.ReadCurrent(SubSleep, StandbyIdle, false) == s.StandbyDc);
            Check(when + " HIBERNATEIDLE 交流还原为 " + s.HibernateAc,
                  PowerKeeper.ReadCurrent(SubSleep, HibernateIdle, true) == s.HibernateAc);
            Check(when + " HIBERNATEIDLE 直流还原为 " + s.HibernateDc,
                  PowerKeeper.ReadCurrent(SubSleep, HibernateIdle, false) == s.HibernateDc);
            Check(when + " VIDEOIDLE 交流还原为 " + s.VideoAc,
                  PowerKeeper.ReadCurrent(SubVideo, VideoIdle, true) == s.VideoAc);
            Check(when + " VIDEOIDLE 直流还原为 " + s.VideoDc,
                  PowerKeeper.ReadCurrent(SubVideo, VideoIdle, false) == s.VideoDc);
            Check(when + " DISKIDLE 交流还原为 " + s.DiskAc,
                  PowerKeeper.ReadCurrent(SubDisk, DiskIdle, true) == s.DiskAc);
            Check(when + " DISKIDLE 直流还原为 " + s.DiskDc,
                  PowerKeeper.ReadCurrent(SubDisk, DiskIdle, false) == s.DiskDc);
        }

        private static void AssertSetting(string what, Guid sub, Guid setting, uint expect)
        {
            uint ac = PowerKeeper.ReadCurrent(sub, setting, true);
            uint dc = PowerKeeper.ReadCurrent(sub, setting, false);
            Check(string.Format(CultureInfo.InvariantCulture, "{0} = 交流 {1} / 直流 {2} (期望 {3})",
                                what, ac, dc, expect), ac == expect && dc == expect);
        }

        private static void AssertSetting(string what, Guid sub, Guid setting, uint expectAc, uint expectDc)
        {
            uint ac = PowerKeeper.ReadCurrent(sub, setting, true);
            uint dc = PowerKeeper.ReadCurrent(sub, setting, false);
            Check(string.Format(CultureInfo.InvariantCulture, "{0} = 交流 {1} / 直流 {2} (期望 {3}/{4})",
                                what, ac, dc, expectAc, expectDc), ac == expectAc && dc == expectDc);
        }

        private static void AssertFile(string what, bool expectExists)
        {
            bool e = File.Exists(AppPaths.PendingRestoreFile);
            Check(what + "（期望" + (expectExists ? "存在" : "已删除") + "）", e == expectExists);
        }
    }
}
