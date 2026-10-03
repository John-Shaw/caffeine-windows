using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Caffeine
{
    /// <summary>
    /// `Caffeine.exe --selftest` - proves every moving part works on this
    /// machine and prints a report to the console and to selftest.txt.
    /// </summary>
    internal static class SelfTest
    {
        internal static int Run()
        {
            StringBuilder r = new StringBuilder();
            Line(r, "咖啡因 自检报告  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            Line(r, "系统: " + Environment.OSVersion.VersionString + "  64位进程=" + Environment.Is64BitProcess);
            Line(r, "运行时: CLR " + Environment.Version + "  位置=" + AppDomain.CurrentDomain.BaseDirectory);
            using (System.Drawing.Graphics screen = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
            {
                Line(r, string.Format(CultureInfo.InvariantCulture,
                    "DPI: 进程={0}  托盘小图标={1}px  (DPI 感知后菜单文字才会清晰)",
                    screen.DpiX, AppIcons.SmallIconSize));
            }
            Line(r, "管理员权限: " + PowerKeeper.IsAdmin());
            Line(r, "数据目录: " + AppPaths.Root);
            Line(r, "待恢复文件: " + (File.Exists(AppPaths.PendingRestoreFile) ? "存在（说明上次未恢复）" : "无"));

            try
            {
                Guid scheme = PowerKeeper.GetActiveScheme();
                Line(r, "");
                Line(r, "[电源方案] active = " + scheme.ToString("B"));
                Dump(r, "STANDBYIDLE  在此时间后睡眠", scheme, new Guid("238c9fa8-0aad-41ed-83f4-97be242c8f20"), new Guid("29f6c1db-86da-48c5-9fdb-f2b67b1f44da"));
                Dump(r, "HIBERNATEIDLE 在此时间后休眠", scheme, new Guid("238c9fa8-0aad-41ed-83f4-97be242c8f20"), new Guid("9d7815a6-7ee4-497e-8888-515a05f02364"));
                Dump(r, "VIDEOIDLE 关闭显示", scheme, new Guid("7516b95f-f776-4464-8c53-06167f40cc99"), new Guid("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e"));
                Dump(r, "DISKIDLE 关闭硬盘", scheme, new Guid("0012ee47-9041-4b5d-9b77-535fba8b1442"), new Guid("6738e2c4-e8a5-4a42-b16a-e040e769756e"));
                Line(r, "系统休眠已启用: " + PowerKeeper.IsHibernateEnabled());
            }
            catch (Exception ex)
            {
                Line(r, "读取电源方案失败: " + ex.Message);
            }

            Line(r, "");
            Line(r, "[SetThreadExecutionState] 这是免管理员权限也能阻止睡眠/息屏的那一层");
            try
            {
                uint prev = PowerKeeper.SetThreadExecutionState(
                    PowerKeeper.ES_CONTINUOUS | PowerKeeper.ES_SYSTEM_REQUIRED | PowerKeeper.ES_DISPLAY_REQUIRED);
                Line(r, "  设置(返回上次状态) = 0x" + prev.ToString("X8", CultureInfo.InvariantCulture));
                uint back = PowerKeeper.SetThreadExecutionState(PowerKeeper.ES_CONTINUOUS);
                Line(r, "  释放(返回上次状态) = 0x" + back.ToString("X8", CultureInfo.InvariantCulture) +
                        "  (含 0x1=曾要求系统保持唤醒, 0x2=曾要求显示保持点亮)");
            }
            catch (Exception ex)
            {
                Line(r, "  失败: " + ex.Message);
            }

            Line(r, "");
            Line(r, "[写权限测试] 只动 STANDBYIDLE：改成 0 再改回来");
            try
            {
                Guid sub = new Guid("238c9fa8-0aad-41ed-83f4-97be242c8f20");
                Guid setting = new Guid("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");
                r.Append(PowerKeeper.WriteProbe(sub, setting, "STANDBYIDLE"));
                Line(r, "  提示: rc=0x80070005(E_ACCESSDENIED) 表示当前不是管理员，只能靠 SetThreadExecutionState 保活");
            }
            catch (Exception ex)
            {
                Line(r, "  失败: " + ex.Message);
            }

            Line(r, "");
            Line(r, "[powercfg 调用]");
            Line(r, "  powercfg /getactivescheme " + PowerKeeper.RunPowercfgCapture("/getactivescheme"));
            if (PowerKeeper.IsAdmin())
            {
                Line(r, "  powercfg /hibernate off —— 已跳过：管理员身份下这条命令会真的关掉系统休眠");
            }
            else
            {
                int code = PowerKeeper.RunPowercfgProbe("/hibernate off");
                Line(r, "  powercfg /hibernate off 退出码 = " + code +
                         "（非管理员必然非 0，这就是“需要管理员权限才能关闭休眠”的原因；本次未产生任何改动）");
            }
            Line(r, "  当前系统休眠状态: " + (PowerKeeper.IsHibernateEnabled() ? "已启用" : "已关闭"));

            Line(r, "");
            Line(r, "[图标资源] SM_CXSMICON = " + AppIcons.SmallIconSize);
            Line(r, AppIcons.Probe().TrimEnd());

            string report = r.ToString();
            try
            {
                File.WriteAllText(Path.Combine(AppPaths.Root, "selftest.txt"), report, Encoding.UTF8);
            }
            catch (Exception) { }
            AppLog.Write("自检完成，报告已写入 selftest.txt");

            Console.Write(report);
            Console.WriteLine("（同时已保存到 " + Path.Combine(AppPaths.Root, "selftest.txt") + "）");
            return 0;
        }

        private static void Dump(StringBuilder r, string label, Guid scheme, Guid sub, Guid setting)
        {
            try
            {
                uint ac = PowerKeeper.ReadCurrent(sub, setting, true);
                uint dc = PowerKeeper.ReadCurrent(sub, setting, false);
                Line(r, string.Format(CultureInfo.InvariantCulture, "  {0}: 交流={1} 直流={2}", label, Sec(ac), Sec(dc)));
            }
            catch (Exception ex)
            {
                Line(r, "  " + label + " 读取失败: " + ex.Message);
            }
        }

        private static void Line(StringBuilder r, string s)
        {
            r.AppendLine(s);
        }

        internal static string Sec(uint s)
        {
            if (s == 0) return "0秒(从不)";
            if (s == 0xFFFFFFFFu) return "0xFFFFFFFF(不变)";
            if (s % 3600 == 0) return (s / 3600).ToString(CultureInfo.InvariantCulture) + "小时";
            if (s % 60 == 0) return (s / 60).ToString(CultureInfo.InvariantCulture) + "分钟";
            return s.ToString(CultureInfo.InvariantCulture) + "秒";
        }
    }
}
