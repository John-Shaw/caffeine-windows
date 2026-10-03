# 上架 Microsoft Store（路线存档，暂缓）

> 记录日期：2026-10-04。状态：**已决策走「MSI/EXE 提交」路线，但因为要先做开源 + 网站，暂缓执行。**
> 先解决托管与分发问题，商店需要的「带版本号的 HTTPS 直链」届时已经有了。

## 一、结论：走 MSI/EXE 提交，不走 MSIX

微软对桌面应用开放两条提交路径，官方推荐 MSIX，但**这个项目必须走 EXE 那条**，原因是权限模型：

| | MSI/EXE 提交 | MSIX 提交 |
|---|---|---|
| 提交物 | 自托管的**带版本号 HTTPS 直链** | 上传 `.msix` 包 |
| 代码签名 | 自己买 CA 证书 Authenticode 签名 | 微软免费重签 + 免费 CDN 托管 |
| 自动更新 | 自己负责 | 系统每 24h 自动检查 |
| **UAC / 提权** | **明确允许弹 UAC** | **`runFullTrust` 不等于管理员权限**，进程跑在 mediumIL |
| **开机自启** | `HKCU\...\Run` 正常 | **Run 键与启动文件夹失效**，必须改用清单 `StartupTask` |
| 改动量 | 几乎为零 | 要重写自启与提权逻辑 |

最后两行是决定性的：

- MSIX 下要提权，需要同时满足「清单声明 `allowElevation`」**且**「exe 清单里 `requestedExecutionLevel=requireAdministrator`」，
  后者意味着**每次启动都必须提权**，无法再提供「按需以管理员重启」这种可选提权。
  → 会废掉菜单里的「以管理员身份重新启动」。
- MSIX 下写 Run 键不生效。→ 会废掉现在的开机自启实现。

参考：
- <https://learn.microsoft.com/windows/apps/publish/publish-your-app/msi/app-package-requirements>
- <https://learn.microsoft.com/windows/apps/distribute-through-store/how-to-distribute-your-win32-app-through-microsoft-store>
- runFullTrust 不授予管理员权限：<https://techcommunity.microsoft.com/t5/msix-discussions/re-single-msix-package-containing-two-parts-requiringnot-requiring-administrator/4375772>

## 二、EXE 提交的硬性要求（逐条对照本项目）

| 官方要求 | 本项目状态 |
| --- | --- |
| 安装包只能是 `.msi` 或 `.exe` | ✅ `dist\Caffeine-Setup-1.1.0.exe` |
| 安装包**及其内部所有 PE** 都要 Authotode 签名，证书链到微软信任根计划内的 CA | ❌ **需要购买代码签名证书** |
| 提交带版本号的 HTTPS 直链，提交后该 URL 的二进制不得变更 | ❌ 需要公网托管（见第五节） |
| 安装过程不得显示安装界面（必须静默），但**允许 UAC 对话框** | ✅ `--silent` 已实测通过 |
| 必须是独立安装器，不能是联网下载器 | ✅ payload 内嵌在 exe 里 |
| 版本号由安装器自行管理，商店不管 Win32 版本号 | ✅ `version.txt` 是唯一来源 |

## 三、开发者账号

- **个人账号现在免费**：新流程（入口 `storedeveloper.microsoft.com`，必须从这个入口进）免掉原本 $19 的注册费，
  覆盖近 200 个市场，验证方式是**政府签发身份证件 + 自拍**。
  <https://learn.microsoft.com/windows/apps/publish/whats-new-individual-developer>
- ⚠️ **国家/地区注册后不可更改**，选中国就永久是���国。
- ⚠️ 未查到官方文档明确说明中国区是否在免费名单内，也**未找到微软官方要求「软著」的文件**。
  国内开发者普遍反映中国区审核会要软著/主体资质，但未获权威依据——**建议直接向 Partner Center 提支持票确认**，
  或先只上非中国区把流程跑通。
- 企业账号约 $99 USD。

## 四、商店页面必填项

包 URL（≥1）、架构、安装参数 + 「支持静默安装」、至少一种语言、**商店 Logo（1:1，必填）**、
**截图至少 1 张**（建议 4+，最多 10）、描述（≤10000 字）、**适用许可条款（必填）**、
年龄分级问卷（全填）、分类、市场、定价。企业账号另需联系信息。
认证周期最多 3 个工作日。

## 五、本项目特有的三个风险点

1. **托盘常驻、启动没有任何窗口** —— 认证团队的自动测试常把「启动后无界面」判为问题。
   解法：**Notes for certification**（2000 字额度）里写清楚这是纯托盘工具、看界面请点右下角图标、
   如何切换状态与退出。⚠️ 这段文字必须在**提交前**写好，因为提交后就改不了了。
2. **两套版本打架** —— 微软明确提醒：官网分发 EXE 与商店版并存会导致用户装两份。
   需决定：商店版独占，还是在应用里检测已装旧版并提示迁移。
3. **应用名** —— 「咖啡因」和「Caffeine」都要先查可用性；macOS 那个 Caffeine 可能已占用同名
   （不冲突，但搜索结果会打架）。抢注最多可提前三个月锁定。

## 六、与开源路线的复用（重要）

**GitHub Releases 的 release asset URL 本身就是「带版本号的 HTTPS 直链」**，正好是商店提交要填的东西。
所以开源路线不是绕路，而是**顺手把商店的托管问题解决了**：

```
GitHub Releases   ->  版本化安装包下载（商店提交要用的 URL）
                   ->  应用内自动更新要轮询的 latest.txt
GitHub Pages      ->  官网
```

⚠️ 待验证：GitHub release 下载链接会 302 跳转到 `objects.githubusercontent.com`，
Partner Center 的下载器**是否跟随重定向**没有公开文档说明。真正提交前需要实测，
不行的话在 Pages 上放一份静态副本即可（Pages 直链不跳转）。

## 七、待办

- [ ] 开 Partner Center 个人账号（证件 + 自拍验证）
- [ ] 预留应用名（先查「咖啡因」/「Caffeine」可用性）
- [ ] 买代码签名证书
- [ ] 把安装包 Authenticode 签名
- [ ] 确认 GitHub Releases 直链能否直接填进商店（实测重定向）
- [ ] 起草 Notes for certification + 商店页面全部文案
- [ ] 生成商店 Logo（1:1）与 ≥1 张截图
- [ ] 提交认证
