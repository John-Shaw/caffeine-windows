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

页面里所有指向 GitHub 的链接都写成了占位符 `__GH_USER__`（`site\index.html` 8 处，
`README.md` 5 处、`CHANGELOG.md` 2 处）。已经替换成 `John-Shaw`。

替换时**只碰文本文件**——`site\assets\*.png` 是二进制，用
`Get-Content -Raw | Set-Content` 走一遍会把它们写成乱码：

```powershell
$u = '你的GitHub用户名'
'.\site\index.html', '.\README.md', '.\CHANGELOG.md' | ForEach-Object {
    (Get-Content $_ -Raw -Encoding UTF8).Replace('__GH_USER__', $u) |
        Set-Content $_ -NoNewline -Encoding UTF8
}
```

替换完确认没有漏网的：

```powershell
Get-ChildItem . -Recurse -File -Include *.md,*.html,*.yml |
    Select-String '__GH_USER__'
```

> PowerShell 5.1 的 `Set-Content -Encoding UTF8` 会写 BOM。`index.html` 里有 `<meta charset="utf-8">`，
> 多一个 BOM 浏览器也能正常解析，实测无影响；介意的话改用 `-Encoding UTF8NoBOM`（PS 7+）。

改完 CSS 后记得把 `index.html` 里的 `styles.css?v=N` 加一，静态托管普遍给 CSS 加长缓存。

## 部署

**当前线上地址**

```
https://ofl6y9hdf8wvt.space.mcode.cn
```

这是用内置的 `website_deploy` 发布的第一版，drive node id `448588928598514`，
**发布时 `__GH_USER__` 还没替换**，所以页面上 8 个 GitHub / 下载链接当时都是坏的。
占位符替换完成后带同一个 `node_id` 又发布过一次，网址不变、页面内容整体替换，
现在链接指向 `https://github.com/John-Shaw/caffeine-windows`。

**GitHub Pages（免费，最省事）**
仓库 Settings → Pages → Source 选 `Deploy from a branch`，
分支 `main`、目录选 `/site`。几分钟后就上线。
好处是下载链接和源码链接与仓库同源，之后不用再改链接。

**任何静态托管**
`site\` 整个目录传上去即可，它就是一个普通的静态站，没有服务端依赖。

## 本地预览

```powershell
python -m http.server 8765 --directory site
```

