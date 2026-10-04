# 参与开发

## 门槛比想象的低

不需要 Visual Studio，也不需要 .NET SDK。项目只用 Windows 自带的
`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` 编译，
目标是每台 Windows 10/11 都自带的 .NET Framework 4.x，零运行时依赖。

```powershell
# 构建应用 -> bin\Caffeine.exe
powershell -ExecutionPolicy Bypass -File .\build.ps1

# 构建应用 + 安装器 -> dist\Caffeine-Setup-<version>.exe
powershell -ExecutionPolicy Bypass -File .\build-setup.ps1

# 63 项单元测试（编译真实的 src\ 源码并断言电源设置往返）
powershell -ExecutionPolicy Bypass -File .\tests\run-tests.ps1
```

只有重新生成图标和预览图才需要 Python + Pillow（`pip install pillow`）。
图标本身是**入库**的，所以纯 C# 改动不需要装 Python。

## 编码约定（踩过坑才知道的，请照着做）

这几条不是风格问题，改错了会静默失败或直接编译不过：

1. **`.ps1` 必须是纯 ASCII。** Windows PowerShell 5.1 在文件没有 BOM 时按 ANSI 读取，
   里面的中文会变成乱码。需要匹配中文时用码点拼接：
   ```powershell
   $cafe = [string][char]0x5496 + [char]0x5561 + [char]0x56E0   # 咖啡因
   ```
2. **PowerShell 里写 `catch [Exception]`，不是 `catch (Exception)`。** 后者是语法错误。
3. **避免反引号续行。** 一行太长就拆成多条语句——反引号后面多一个空格就断。
4. **`.cs` 是 UTF-8 无 BOM。** 构建用 `/codepage:65001` 编译。带 BOM 会让中文字符串乱码。
5. **`Start-Process -Wait` 会等整个进程树**（包括安装器启动的 `Caffeine.exe`），会永久阻塞。
6. **`Stop-Process` / `taskkill` 可能杀不掉 Caffeine**，用
   `Invoke-CimMethod -InputObject $_ -MethodName Terminate`。
7. **调安装器（WinExe）必须用 `Start-Process -Wait -PassThru` 读 `$p.ExitCode`，
   不能用 `& $setup ...` + `$LASTEXITCODE`。** PowerShell 对 GUI 程序
   **不等待、也不更新 `$LASTEXITCODE`**，它只会留着上一条原生命令的值。
   实测一个睡 3 秒的 WinExe：`&` 27 ms 就返回，`$LASTEXITCODE` 是上条命令的残留值
   （全新会话里是 `$null`，于是 `-ne 0` 成立，CI 直接挂）。
   同样的原因，`&` 之后紧跟 `Test-Path` 会和文件解压赛跑。
   这只对 `/target:exe` 的控制台程序成立——`tests\run-tests.ps1` 里的测试
   harness 是控制台程序，用 `&` 是对的，别顺手改。

## 改图标

所有图形参数集中在 `tools\make_icons.py` **顶部的常量块**：

```
CX / RIM_Y / CUP_BOTTOM / RIM_HW / FOOT_HW / CURVE / RIM_HH     杯身
SAUCER_Y / SAUCER_HW / SAUCER_HH                                碟子
HANDLE_X / HANDLE_HW / HANDLE_Y0 / HANDLE_Y1                   把手
W_BODY / W_RIM / W_HANDLE / HALO                                线宽
STEAM_*                                                          蒸汽
INK_SCALE / INK_AX / INK_AY                                     整体等比缩放
```

改完重新生成：

```powershell
python tools\make_icons.py
```

想看不同缩放倍率长什么样（按真实像素、深浅任务栏各渲染一遍）：

```powershell
python tools\icon_scale.py      # -> assets\icon_scale.png
```

**托盘图标非常小、非常挑。** 16px 下描边只有 1.0px，而白色 halo 会吃掉大半，
这就是「空杯看不清」的根因——所以 halo 必须收到约 0.5px、描边加粗到约 1.2px。
下结论前先量，别靠眼睛：

```powershell
python tools\probe_tray.py assets\taskbar_idle.png assets\menu.png
```

它会报告图形在图标格子里到底占了多少、以及菜单行距。

## 改菜单 / 界面

- **字号绝对不能手工乘 DPI 因子。** pt 本身已经是 DPI 相对的，系统已经缩过一次，
  再乘就变成两倍大的字。`setup\Dpi.cs` 的 `Lay` 类只缩放坐标，不缩放字号。
- WinForms 顶层窗体的 `AutoScaleMode.Dpi` **不可靠**——它只放大字体、不缩放控件坐标。
  安装器界面用的是手工坐标 + `Lay` 缩放，`AutoScaleMode=None`。
- 高 DPI 下改菜单行距要给菜单项加上下 `Padding`，这是唯一受支持的做法；
  不要改 `ImageScalingSize`，那会连带把左边对勾那一列也撑宽。

## 测试约定

- 测试必须**相对进入时的机器状态**断言，并在结束时完整还原电源设置。
- 涉及真实交互的测试脚本要有 preflight：前置状态不对时**直接拒绝运行**并说明原因，
  而不是给出一个误导性的 FAIL。
- 真实安装和 `tests\verify-install.ps1` 共用同一套用户级注册
  （`HKCU\...\Uninstall\Caffeine`、Run 值、开始菜单快捷方式），并排跑会互相覆盖，
  所以脚本会在检测到有真实安装时拒绝运行。

## 提 PR

1. `version.txt` 是版本的唯一来源——要发版就改它，别去手改程序集属性。
2. 跑一遍 `tests\run-tests.ps1`，确认 63 项全过。
3. 改了图标的话附上 `assets\icon_scale.png` 或真实任务栏截图。
4. PR 描述里写清楚**为什么**，不只是改了什么。
