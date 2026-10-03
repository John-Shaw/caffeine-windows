using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Caffeine
{
    /// <summary>A copy of every power-scheme value Caffeine is about to change.</summary>
    internal sealed class PowerSnapshot
    {
        internal bool ReadOk;                 // were the user's defaults readable at all?
        internal string Scheme = "";          // active scheme GUID
        internal uint StandbyAc, StandbyDc;  // "sleep after"          (STANDBYIDLE)
        internal uint HibernateAc, HibernateDc; // "hibernate after"    (HIBERNATEIDLE)
        internal uint VideoAc, VideoDc;      // "turn display off after" (VIDEOIDLE)
        internal uint DiskAc, DiskDc;        // "turn hard disk off after" (DISKIDLE)
        internal bool HibernateOn;           // was system hibernation enabled?
        internal string SavedAt = "";

        internal string Summary()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "scheme={0} standby(ac/dc)={1}/{2}s hibernate(ac/dc)={3}/{4}s video(ac/dc)={5}/{6}s disk(ac/dc)={7}/{8}s hibernateOn={9} readOk={10}",
                Scheme, StandbyAc, StandbyDc, HibernateAc, HibernateDc,
                VideoAc, VideoDc, DiskAc, DiskDc, HibernateOn, ReadOk);
        }

        internal string Serialize()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("v=1");
            sb.AppendLine("scheme=" + Scheme);
            sb.AppendLine("readok=" + (ReadOk ? "1" : "0"));
            sb.AppendLine("standby_ac=" + StandbyAc);
            sb.AppendLine("standby_dc=" + StandbyDc);
            sb.AppendLine("hibernate_ac=" + HibernateAc);
            sb.AppendLine("hibernate_dc=" + HibernateDc);
            sb.AppendLine("video_ac=" + VideoAc);
            sb.AppendLine("video_dc=" + VideoDc);
            sb.AppendLine("disk_ac=" + DiskAc);
            sb.AppendLine("disk_dc=" + DiskDc);
            sb.AppendLine("hibernate_on=" + (HibernateOn ? "1" : "0"));
            sb.AppendLine("saved=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        internal static PowerSnapshot Deserialize(string text)
        {
            PowerSnapshot s = new PowerSnapshot();
            Dictionary<string, string> kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string[] lines = (text ?? "").Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                int eq = lines[i].IndexOf('=');
                if (eq <= 0) continue;
                kv[lines[i].Substring(0, eq).Trim()] = lines[i].Substring(eq + 1).Trim();
            }
            s.Scheme = Get(kv, "scheme", "");
            s.ReadOk = Get(kv, "readok", "0") == "1";
            s.StandbyAc = GetU(kv, "standby_ac");
            s.StandbyDc = GetU(kv, "standby_dc");
            s.HibernateAc = GetU(kv, "hibernate_ac");
            s.HibernateDc = GetU(kv, "hibernate_dc");
            s.VideoAc = GetU(kv, "video_ac");
            s.VideoDc = GetU(kv, "video_dc");
            s.DiskAc = GetU(kv, "disk_ac");
            s.DiskDc = GetU(kv, "disk_dc");
            s.HibernateOn = Get(kv, "hibernate_on", "0") == "1";
            s.SavedAt = Get(kv, "saved", "");
            return s;
        }

        private static string Get(Dictionary<string, string> kv, string key, string def)
        {
            string v;
            return kv.TryGetValue(key, out v) ? v : def;
        }

        private static uint GetU(Dictionary<string, string> kv, string key)
        {
            uint n;
            return uint.TryParse(Get(kv, key, "0"), NumberStyles.Integer,
                                 CultureInfo.InvariantCulture, out n) ? n : 0u;
        }
    }

    /// <summary>What actually happened when we asked for / undid "keep awake".</summary>
    internal sealed class PowerResult
    {
        internal bool SettingsChanged;      // at least one scheme value was written
        internal bool NeedsAdmin;           // something we asked for was refused
        internal bool SchemeWriteFailed;    // the power scheme itself could not be changed
        internal bool HibernateFailed;      // only `powercfg /hibernate` was refused
        internal bool HibernateChanged;
        internal bool UsedFallback;         // no readable defaults -> 1 hour
        internal string Detail = "";
    }

    internal static class PowerKeeper
    {
        /// <summary>The user-visible default when the real defaults cannot be read.</summary>
        internal const uint FallbackSeconds = 3600; // 1 hour

        // Verified against this machine with `powercfg /query` (see README).
        private static readonly Guid SubSleep = new Guid("238c9fa8-0aad-41ed-83f4-97be242c8f20");
        private static readonly Guid SubVideo = new Guid("7516b95f-f776-4464-8c53-06167f40cc99");
        private static readonly Guid SubDisk = new Guid("0012ee47-9041-4b5d-9b77-535fba8b1442");
        private static readonly Guid StandbyIdle = new Guid("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");
        private static readonly Guid HibernateIdle = new Guid("9d7815a6-7ee4-497e-8888-515a05f02364");
        private static readonly Guid VideoIdle = new Guid("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");
        private static readonly Guid DiskIdle = new Guid("6738e2c4-e8a5-4a42-b16a-e040e769756e");

        private const uint Never = 0; // 0 seconds == never, for every one of these timeouts

        // ---------------------------------------------------------------- native

        [DllImport("powrprof.dll")]
        private static extern uint PowerGetActiveScheme(IntPtr rootPowerKey, out IntPtr activePolicyGuid);

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid,
            ref Guid subGroupOfPowerSettingsGuid, ref Guid powerSettingGuid, out uint acValueIndex);

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid,
            ref Guid subGroupOfPowerSettingsGuid, ref Guid powerSettingGuid, out uint dcValueIndex);

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid,
            ref Guid subGroupOfPowerSettingsGuid, ref Guid powerSettingGuid, uint acValueIndex);

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern uint PowerWriteDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid,
            ref Guid subGroupOfPowerSettingsGuid, ref Guid powerSettingGuid, uint dcValueIndex);

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern uint PowerSetActiveScheme(IntPtr rootPowerKey, ref Guid schemeGuid);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        internal const uint ES_CONTINUOUS = 0x80000000;
        internal const uint ES_SYSTEM_REQUIRED = 0x00000001;
        internal const uint ES_DISPLAY_REQUIRED = 0x00000002;

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint SetThreadExecutionState(uint esFlags);

        // ---------------------------------------------------------------- capture

        /// <summary>Reads the user's current defaults.  Never throws.</summary>
        internal static PowerSnapshot Capture()
        {
            PowerSnapshot s = new PowerSnapshot();
            bool ok;
            try
            {
                Guid scheme = GetActiveScheme();
                s.Scheme = scheme.ToString("B");
                s.ReadOk = true;
                s.StandbyAc = ReadAc(ref scheme, SubSleep, StandbyIdle, out ok); s.ReadOk &= ok;
                s.StandbyDc = ReadDc(ref scheme, SubSleep, StandbyIdle, out ok); s.ReadOk &= ok;
                s.HibernateAc = ReadAc(ref scheme, SubSleep, HibernateIdle, out ok); s.ReadOk &= ok;
                s.HibernateDc = ReadDc(ref scheme, SubSleep, HibernateIdle, out ok); s.ReadOk &= ok;
                s.VideoAc = ReadAc(ref scheme, SubVideo, VideoIdle, out ok); s.ReadOk &= ok;
                s.VideoDc = ReadDc(ref scheme, SubVideo, VideoIdle, out ok); s.ReadOk &= ok;
                s.DiskAc = ReadAc(ref scheme, SubDisk, DiskIdle, out ok); s.ReadOk &= ok;
                s.DiskDc = ReadDc(ref scheme, SubDisk, DiskIdle, out ok); s.ReadOk &= ok;
            }
            catch (Exception ex)
            {
                AppLog.Error("读取当前电源设置失败", ex);
                s.ReadOk = false;
            }
            s.HibernateOn = IsHibernateEnabled();
            return s;
        }

        internal static Guid GetActiveScheme()
        {
            IntPtr p;
            uint rc = PowerGetActiveScheme(IntPtr.Zero, out p);
            if (rc != 0 || p == IntPtr.Zero)
                throw new InvalidOperationException("PowerGetActiveScheme rc=" + rc);
            try
            {
                Guid g = (Guid)Marshal.PtrToStructure(p, typeof(Guid));
                return g;
            }
            finally
            {
                LocalFree(p);
            }
        }

        private static uint ReadAc(ref Guid scheme, Guid sub, Guid setting, out bool ok)
        {
            uint v;
            uint rc = PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out v);
            ok = rc == 0;
            return v;
        }

        private static uint ReadDc(ref Guid scheme, Guid sub, Guid setting, out bool ok)
        {
            uint v;
            uint rc = PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out v);
            ok = rc == 0;
            return v;
        }

        internal static uint ReadCurrent(Guid sub, Guid setting, bool ac)
        {
            Guid scheme = GetActiveScheme();
            bool ok;
            return ac ? ReadAc(ref scheme, sub, setting, out ok) : ReadDc(ref scheme, sub, setting, out ok);
        }

        // ---------------------------------------------------------------- writing

        private static bool Write(Guid scheme, Guid sub, Guid setting, uint ac, uint dc, List<string> log)
        {
            uint rcA = PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, ac);
            uint rcD = PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, dc);
            if (log != null)
                log.Add(string.Format(CultureInfo.InvariantCulture,
                    "write {0}: ac={1} rc={2}, dc={3} rc={4}",
                    Name(sub, setting), ac, Hex(rcA), dc, Hex(rcD)));
            return rcA == 0 && rcD == 0;
        }

        private static bool Commit(Guid scheme, List<string> log)
        {
            uint rc = PowerSetActiveScheme(IntPtr.Zero, ref scheme);
            if (log != null) log.Add("PowerSetActiveScheme rc=" + Hex(rc));
            return rc == 0;
        }

        // ---------------------------------------------------------------- awake

        /// <summary>
        /// Turns sleeping, hibernation and display sleep off (0 = never) and, when
        /// the process is elevated, disables hibernation system-wide.
        /// </summary>
        internal static PowerResult ApplyAwake(PowerSnapshot snap, bool allowDisplaySleep)
        {
            PowerResult r = new PowerResult();
            List<string> log = new List<string>();
            try
            {
                Guid scheme = string.IsNullOrEmpty(snap.Scheme)
                    ? GetActiveScheme()
                    : new Guid(snap.Scheme);

                bool ok = true;
                ok &= Write(scheme, SubSleep, StandbyIdle, Never, Never, log);
                ok &= Write(scheme, SubSleep, HibernateIdle, Never, Never, log);
                ok &= Write(scheme, SubDisk, DiskIdle, Never, Never, log);
                if (!allowDisplaySleep)
                    ok &= Write(scheme, SubVideo, VideoIdle, Never, Never, log);

                r.SettingsChanged = Commit(scheme, log);
                r.SchemeWriteFailed = !ok;

                if (IsHibernateEnabled())
                {
                    int code = RunPowercfg("/hibernate off");
                    r.HibernateChanged = code == 0;
                    // Turning hibernation off is the one thing that genuinely
                    // needs elevation.  Failing that is NOT a failed keep-awake:
                    // every idle timeout above is already "never", so the system
                    // still cannot fall asleep.  Keep the two apart.
                    if (code != 0) r.HibernateFailed = true;
                    log.Add("powercfg /hibernate off -> exit " + code +
                            (code == 0 ? "" : " (需要管理员权限；不影响保活效果)"));
                }
                r.NeedsAdmin = r.SchemeWriteFailed || r.HibernateFailed;
            }
            catch (Exception ex)
            {
                AppLog.Error("开启保持唤醒时出错", ex);
                r.NeedsAdmin = true;
            }

            r.Detail = string.Join(" | ", log.ToArray());
            AppLog.Write("ApplyAwake: " + r.Detail);
            return r;
        }

        /// <summary>
        /// Called when "allow the display to sleep" is flipped while Caffeine is
        /// already holding the system awake: either pin the display timeout to
        /// "never", or hand the user's own value straight back.
        /// </summary>
        internal static bool ApplyDisplayPolicy(PowerSnapshot snap, bool allowDisplaySleep)
        {
            if (snap == null || !snap.ReadOk || string.IsNullOrEmpty(snap.Scheme)) return false;
            try
            {
                Guid scheme = new Guid(snap.Scheme);
                uint ac = allowDisplaySleep ? snap.VideoAc : Never;
                uint dc = allowDisplaySleep ? snap.VideoDc : Never;
                uint curAc = ReadCurrent(SubVideo, VideoIdle, true);
                if (curAc == ac && ReadCurrent(SubVideo, VideoIdle, false) == dc)
                    return true;
                List<string> log = new List<string>();
                if (!Write(scheme, SubVideo, VideoIdle, ac, dc, log)) return false;
                return Commit(scheme, log);
            }
            catch (Exception ex)
            {
                AppLog.Error("调整显示器策略失败", ex);
                return false;
            }
        }

        // ---------------------------------------------------------------- restore

        /// <summary>
        /// Puts the user's own settings back.  A value that is no longer 0 was
        /// changed by somebody else in the meantime, so it is left alone.
        /// </summary>
        internal static PowerResult Restore(PowerSnapshot snap)
        {
            return RestoreCore(snap, false);
        }

        /// <summary>
        /// Unconditional restore, used by the `--restore` repair command: it
        /// writes every saved value back even if something else changed it in
        /// the meantime, because that is exactly what the user asked for.
        /// </summary>
        internal static PowerResult RestoreForced(PowerSnapshot snap)
        {
            return RestoreCore(snap, true);
        }

        private static PowerResult RestoreCore(PowerSnapshot snap, bool force)
        {
            PowerResult r = new PowerResult();
            List<string> log = new List<string>();

            if (snap == null || !snap.ReadOk || string.IsNullOrEmpty(snap.Scheme))
            {
                AppLog.Write("Restore: 没有可用的原始设置，改用默认回退（1 小时）");
                return Fallback();
            }

            try
            {
                Guid scheme = new Guid(snap.Scheme);
                bool dirty = false;

                dirty |= RestoreOne(scheme, SubSleep, StandbyIdle, "睡眠", snap.StandbyAc, snap.StandbyDc, force, log);
                dirty |= RestoreOne(scheme, SubSleep, HibernateIdle, "休眠", snap.HibernateAc, snap.HibernateDc, force, log);
                dirty |= RestoreOne(scheme, SubVideo, VideoIdle, "关闭显示", snap.VideoAc, snap.VideoDc, force, log);
                dirty |= RestoreOne(scheme, SubDisk, DiskIdle, "关闭硬盘", snap.DiskAc, snap.DiskDc, force, log);

                if (dirty) r.SettingsChanged = Commit(scheme, log);

                if (snap.HibernateOn && !IsHibernateEnabled())
                {
                    int code = RunPowercfg("/hibernate on");
                    r.HibernateChanged = code == 0;
                    if (code != 0)
                    {
                        r.HibernateFailed = true;
                        r.NeedsAdmin = true;
                    }
                    log.Add("powercfg /hibernate on -> exit " + code);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("恢复默认设置时出错", ex);
                r.NeedsAdmin = true;
            }

            r.Detail = string.Join(" | ", log.ToArray());
            AppLog.Write((force ? "RestoreForced" : "Restore") + ": " + r.Detail);
            return r;
        }

        private static bool RestoreOne(Guid scheme, Guid sub, Guid setting, string label,
                                       uint ac, uint dc, bool force, List<string> log)
        {
            bool okAc, okDc;
            uint curAc = ReadAc(ref scheme, sub, setting, out okAc);
            uint curDc = ReadDc(ref scheme, sub, setting, out okDc);
            if (!okAc || !okDc)
            {
                log.Add("跳过 " + label + "：无法读取当前值");
                return false;
            }

            // Only undo what we actually did.  Anything else was changed by the
            // user or another tool while Caffeine was holding the system awake.
            uint newAc = (!force && curAc != Never) ? curAc : ac;
            uint newDc = (!force && curDc != Never) ? curDc : dc;
            if (newAc != ac || newDc != dc)
                log.Add(string.Format(CultureInfo.InvariantCulture,
                    "跳过 {0}：当前值已被其他程序改为 ac={1}/dc={2}", label, curAc, curDc));

            if (newAc == curAc && newDc == curDc) return false;
            return Write(scheme, sub, setting, newAc, newDc, log);
        }

        /// <summary>
        /// The documented default: if the user's own settings cannot be recovered
        /// we fall back to "sleep (and blank the screen) after one hour".
        /// </summary>
        internal static PowerResult Fallback()
        {
            PowerResult r = new PowerResult();
            r.UsedFallback = true;
            List<string> log = new List<string>();
            try
            {
                Guid scheme = GetActiveScheme();
                r.SettingsChanged = Write(scheme, SubSleep, StandbyIdle, FallbackSeconds, FallbackSeconds, log)
                    & Write(scheme, SubVideo, VideoIdle, FallbackSeconds, FallbackSeconds, log)
                    & Write(scheme, SubDisk, DiskIdle, FallbackSeconds, FallbackSeconds, log);
                r.SettingsChanged = Commit(scheme, log) && r.SettingsChanged;
            }
            catch (Exception ex)
            {
                AppLog.Error("回退到 1 小时默认设置失败", ex);
                r.NeedsAdmin = true;
            }
            r.Detail = string.Join(" | ", log.ToArray());
            AppLog.Write("Fallback(1h): " + r.Detail);
            return r;
        }

        // ---------------------------------------------------------------- hibernate

        /// <summary>
        /// Is hibernation currently available?  HiberFileSizePercent is 0 once
        /// `powercfg /hibernate off` has run, which is the authoritative answer.
        /// </summary>
        internal static bool IsHibernateEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Session Manager\Power"))
                {
                    if (k != null)
                    {
                        object pct = k.GetValue("HiberFileSizePercent");
                        if (pct != null)
                        {
                            uint n = Convert.ToUInt32(pct, CultureInfo.InvariantCulture);
                            return n != 0;
                        }
                        if (k.GetValue("HiberFileSize") != null) return true;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("读取休眠状态失败", ex);
            }
            return true; // assume on: the safe default is to leave hibernation alone
        }

        /// <summary>Runs powercfg and returns its exit code (0 = success).</summary>
        internal static int RunPowercfg(string args)
        {
            return RunPowercfgProbe(args);
        }

        /// <summary>Runs powercfg read-only style, returning exit code + stdout.</summary>
        internal static int RunPowercfgProbe(string args)
        {
            return RunPowercfg(args, null);
        }

        /// <summary>powercfg + captured stdout, used to cross-check the API.</summary>
        internal static string RunPowercfgCapture(string args)
        {
            StringBuilder sb = new StringBuilder();
            int code = RunPowercfg(args, sb);
            string text = sb.ToString().Trim();
            if (text.Length > 200) text = text.Substring(0, 200) + "…";
            return "退出码=" + code + "  输出=" + text;
        }

        /// <summary>Runs powercfg and returns its exit code (0 = success).</summary>
        private static int RunPowercfg(string args, StringBuilder output)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("powercfg", args);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    string so = p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    if (output != null) output.Append(so.Replace("\r", " ").Replace("\n", " ").Trim());
                    if (!p.WaitForExit(15000))
                    {
                        try { p.Kill(); } catch (Exception) { }
                        return -1;
                    }
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("执行 powercfg " + args + " 失败", ex);
                return -1;
            }
        }

        // ---------------------------------------------------------------- write probe

        /// <summary>
        /// Read-only-ish self test for the one setting it is given: sets it to
        /// "never", reads it back, then puts the original value straight back and
        /// proves the round trip.  Nothing else is touched.
        /// </summary>
        internal static string WriteProbe(Guid sub, Guid setting, string label)
        {
            StringBuilder log = new StringBuilder();
            try
            {
                Guid scheme = GetActiveScheme();
                bool okA, okD;
                uint curAc = ReadAc(ref scheme, sub, setting, out okA);
                uint curDc = ReadDc(ref scheme, sub, setting, out okD);
                if (!okA || !okD)
                {
                    log.Append("  ").Append(label).Append(": 无法读取当前值 rc=")
                       .Append(Hex(PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out curAc)))
                       .AppendLine();
                    return log.ToString();
                }

                log.AppendFormat(CultureInfo.InvariantCulture,
                    "  {0}: 原值 ac={1} dc={2}", label, curAc, curDc).AppendLine();

                uint rcA = PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, Never);
                uint rcD = PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, Never);
                uint rcC = PowerSetActiveScheme(IntPtr.Zero, ref scheme);
                log.AppendFormat(CultureInfo.InvariantCulture,
                    "  写入 0（从不）: ac rc={0} dc rc={1} commit rc={2}", Hex(rcA), Hex(rcD), Hex(rcC)).AppendLine();

                uint midAc = ReadCurrent(sub, setting, true);
                uint midDc = ReadCurrent(sub, setting, false);
                log.AppendFormat(CultureInfo.InvariantCulture, "  读回: ac={0} dc={1}", midAc, midDc).AppendLine();

                uint rA = PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, curAc);
                uint rD = PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, curDc);
                uint rC = PowerSetActiveScheme(IntPtr.Zero, ref scheme);
                log.AppendFormat(CultureInfo.InvariantCulture,
                    "  还原: ac rc={0} dc rc={1} commit rc={2}", Hex(rA), Hex(rD), Hex(rC)).AppendLine();

                uint endAc = ReadCurrent(sub, setting, true);
                uint endDc = ReadCurrent(sub, setting, false);
                bool same = endAc == curAc && endDc == curDc;
                log.AppendFormat(CultureInfo.InvariantCulture,
                    "  最终: ac={0} dc={1} {2}", endAc, endDc,
                    same ? "（已完全还原）" : "（不一致！）").AppendLine();
                return log.ToString();
            }
            catch (Exception ex)
            {
                log.Append("  ").Append(label).Append(" 失败: ").Append(ex.Message).AppendLine();
                return log.ToString();
            }
        }

        // ---------------------------------------------------------------- pending file

        internal static void SavePending(PowerSnapshot snap)
        {
            try
            {
                File.WriteAllText(AppPaths.PendingRestoreFile, snap.Serialize(), Encoding.UTF8);
                AppLog.Write("已保存原始设置到 " + AppPaths.PendingRestoreFile);
            }
            catch (Exception ex)
            {
                AppLog.Error("保存原始设置失败", ex);
            }
        }

        internal static PowerSnapshot LoadPending()
        {
            try
            {
                if (!File.Exists(AppPaths.PendingRestoreFile)) return null;
                return PowerSnapshot.Deserialize(File.ReadAllText(AppPaths.PendingRestoreFile, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                AppLog.Error("读取待恢复设置失败", ex);
                return null;
            }
        }

        internal static void DeletePending()
        {
            try
            {
                if (File.Exists(AppPaths.PendingRestoreFile))
                    File.Delete(AppPaths.PendingRestoreFile);
            }
            catch (Exception ex)
            {
                AppLog.Error("删除待恢复设置失败", ex);
            }
        }

        // ---------------------------------------------------------------- helpers

        private static string Name(Guid sub, Guid setting)
        {
            if (setting == StandbyIdle) return "STANDBYIDLE";
            if (setting == HibernateIdle) return "HIBERNATEIDLE";
            if (setting == VideoIdle) return "VIDEOIDLE";
            if (setting == DiskIdle) return "DISKIDLE";
            return sub.ToString();
        }

        internal static string Hex(uint rc)
        {
            return "0x" + rc.ToString("X8", CultureInfo.InvariantCulture);
        }

        internal static bool IsAdmin()
        {
            try
            {
                System.Security.Principal.WindowsIdentity id =
                    System.Security.Principal.WindowsIdentity.GetCurrent();
                System.Security.Principal.WindowsPrincipal pr =
                    new System.Security.Principal.WindowsPrincipal(id);
                return pr.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
