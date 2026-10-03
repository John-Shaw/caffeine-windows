# Changelog

本项目的版本历史从 1.1.0 开始记录；更早的开发过程没有留下变更日志。

格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，
版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [Unreleased]

还没有已发布但未进 CHANGELOG 的改动。`version.txt` 是版本的唯一来源，
构建脚本从这里生成程序集属性，所以改版本号只需要改那一个文件。

## [1.1.0] - 2026-10-04

### 新增

- 托盘图标全新绘制：弧形外翻碗身 + 可见碟子 + 加大加圆的把手 + 两缕不等长微倾的小蒸汽。
  图形用 Python + Pillow 生成矢量图后 8 倍超采样，尺寸集合 16/20/24/32/40/48/64，
  每条笔画先画一层白色 halo 再画本色，深浅任务栏上都清晰。
- 图标按实测尺寸等比放大 1.18 倍（`tools\make_icons.py` 的 `INK_SCALE`）：
  图形原本只填满 32px 图标格高度的 51%，放大后升到 56%。
- 菜单行距从 36px 增加到 44px（按 DPI 换算的上下内边距），中文不再挤成一坨。
- `--selftest` 自检模式：打印电源方案、权限、图标、写入往返情况。
- `--restore` 命令行修复：把上次没还原的电源设置还原回去。
- 崩溃 / 强杀后自动恢复电源设置（`pending-restore.cfg`）。
- 1 小时兜底：读不到原设置时不会把机器设成永不睡眠。

### 改进

- 清单声明 per-monitor v2 DPI 感知。声明之前菜单按 1 倍画好再拉伸 2 倍，文字糊成一团；
  声明之后菜单按 192 DPI 原生绘制。
- 安装器界面改为手工 DPI 缩放（`setup\Dpi.cs`）。WinForms 顶层窗体的
  `AutoScaleMode.Dpi` 只放大字体、不缩放控件坐标，实测排版是坏的。
- 勾选项用自绘的深绿色对勾 + 加粗文字，不依赖系统主题的淡色对勾。
- 菜单文字用 GDI 绘制在菜单的不透明背景上，避免 GDI+ ClearType 在分层窗口上出现红蓝色边。
- 托盘图标跟随 DPI 变化重建（`WM_DPICHANGED`）。

### 修复

- 长按托盘图标不再连发几十次切换：拦截 `MouseDown` 自动重复。
- 菜单勾选项点了没反应：没有用 `CheckOnClick`，改为在处理函数里自己翻转并应用。
- 错误的管理员权限告警气泡不再误弹。
- Windows 11 托盘把长按转成「一次点击」的行为已被识别，不会当成 bug 重复处理。

### 测试

- 63 项单元测试（`tests\run-tests.ps1`），编译真实的 `src\` 源码并断言电源设置往返。
- 三条真实交互测试脚本，用 UI Automation 发真实鼠标点击：
  - `tests\verify-tray.ps1` —— 单击 / 双击 / 按住 2.5 秒，核对 `powercfg /query` 的实际值
  - `tests\verify-menu.ps1` —— 翻转两个勾选项，验证设置真的落到磁盘
  - `tests\verify-install.ps1` —— 完整安装 + 卸载往返
- 所有测试都**相对进入时的机器状态**断言，结束时完整还原；`verify-tray.ps1` 和
  `verify-install.ps1` 都有 preflight，发现前置状态不对会直接拒绝运行而不是给出误导性结果。

[Unreleased]: https://github.com/__GH_USER__/caffeine-windows/compare/v1.1.0...HEAD
[1.1.0]: https://github.com/__GH_USER__/caffeine-windows/releases/tag/v1.1.0
