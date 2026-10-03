# 官网

单页静态站，没有构建步骤、没有 JS、没有依赖。

```
index.html      页面本身
styles.css      样式（配色取自托盘图标本身）
assets/         图片，由 tools\make_site.py 生成，**不要手改**
```

## 换图 / 重新生成资源

```powershell
python tools\make_site.py
```

它会从 `assets\` 里已有的素材生成 `site\assets\`：

| 输出 | 来源 |
| --- | --- |
| `hero.png` | 用 `tools\make_icons.py` 以 256px 重新渲染（ICO 最大只有 64px） |
| `favicon.png` | 同上，64px |
| `tray-idle.png` / `tray-awake.png` | `assets\taskbar_*.png`，裁掉左边无关部分、保留托盘那一段 |
| `menu.png` | `assets\menu_installed.png` |

后三张需要先跑真实截图脚本才能拿到最新状态：

```powershell
powershell -ExecutionPolicy Bypass -File .\tests\shot-taskbar.ps1
powershell -ExecutionPolicy Bypass -File .\tests\shot-menu.ps1 -out .\assets\menu_installed.png
```

所有图片都是 1:1 原始像素，**不做放大**。

## 上线前必须改一处

页面里所有指向 GitHub 的链接都写成了占位符 `__GH_USER__`：

```powershell
Get-ChildItem .\site -Recurse -File | ForEach-Object {
    (Get-Content $_.FullName -Raw).Replace('__GH_USER__', '你的GitHub用户名') |
        Set-Content $_.FullName -NoNewline -Encoding UTF8
}
```

`CHANGELOG.md` 和 `README.md` 里也有同样的占位符。

## 部署

**GitHub Pages（免费，最省事）**
仓库 Settings → Pages → Source 选 `Deploy from a branch`，
分支 `main`、目录选 `/site`。几分钟后就上线。

**任何静态托管**
`site\` 整个目录传上去即可，它就是一个普通的静态站，没有服务端依赖。
