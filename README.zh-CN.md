<div align="center">

<img src="docs/icon.png" alt="UIPaste" width="96">

<h1>UIPaste</h1>

<p><b>Screenshot it. Stick real UI on it. Paste.</b></p>
<p><sub>Windows 上的 Snipaste 式截图，外加一键贴上 Material 3 / iOS 控件，10 秒拼出原型。</sub></p>

<a href="README.md"><img alt="English" src="https://img.shields.io/badge/English-6e7681?style=for-the-badge"></a> <a href="README.zh-CN.md"><img alt="中文" src="https://img.shields.io/badge/%E4%B8%AD%E6%96%87-0e7c7b?style=for-the-badge"></a>

</div>

<br>

## 这是什么

**一个托盘小工具，把任意截图变成 UI 草图。** 按 `F1` 截一块屏幕，按 `F2` 打开控件面板，点一个控件，它就像贴纸一样贴进截图。可以拖动、缩放、改上面的文字，最后按 `Enter` 复制。

这些控件不是画出来的，而是真正的开源组件：Google 的 Material Web，以及 Framework7 的 iOS 主题。它们在 WebView2 里渲染，再按 3 倍分辨率截成透明 PNG，所以放多大字都清晰。

<p align="center"><img src="docs/widgets.png" alt="面板里的一部分：Material 3 的按钮、输入框、对话框、提示条、标签页；iOS 的按钮、分段控件、开关、设置列表、弹窗、标签栏" width="760"></p>

<br>

## 一、装

> [!TIP]
> **建议让你的 coding agent 帮你装。** 把这句贴给 Claude Code 或 Codex：
>
> ```
> 帮我装 https://github.com/FranklinXuNorth/UIPaste：clone 下来，跑 `dotnet publish -c Release -o dist`，启动 dist/UIPaste.exe，告诉我有没有快捷键被占用。
> ```

需要 Windows 10/11、[.NET 10 SDK](https://dotnet.microsoft.com/download)，以及 WebView2 运行时（Windows 11 自带）。

```bash
git clone https://github.com/FranklinXuNorth/UIPaste.git
cd UIPaste
dotnet publish -c Release -o dist
dist/UIPaste.exe
```

启动后常驻托盘。如果快捷键已经被别的程序占了（Snipaste 会占 `F1`/`F3`），会弹出提示；退出那个程序就好，或者先用托盘菜单。

<br>

## 二、用

| 按键 | 作用 |
| --- | --- |
| `F1` | **截图。** 悬停自动选窗口，拖动选区域。 |
| `F2` | **控件。** 截图开着时点一下就贴进截图，没开就复制到剪贴板；`Shift`+点击直接贴到屏幕上。 |
| `F3` | **贴图。** 把剪贴板内容变成一张置顶的浮动图片。 |

**截图里**

| 想做什么 | 怎么做 |
| --- | --- |
| 标注 | 工具栏：矩形、椭圆、箭头、画笔、文字、马赛克；`Ctrl+Z` 撤销 |
| 移动控件 | 直接拖 |
| 等比缩放 | 滚轮，或拖四个角（按住 `Shift` 自由拉伸） |
| 压扁 / 拉长 | 拖四条边的中点 |
| 删除控件 | 右键它，或按 `Delete` |
| 贴进图片 | `Ctrl+V` |
| 完成 | `Enter` 复制 · `Ctrl+S` 保存（同时复制）· `Ctrl+T` 贴到屏幕 · `Esc` 退出 |

**控件面板里**，右键一个控件可以改它的文字（标签、值、占位文字，连图标名都能改）。改动会记住，点 **重置** 恢复默认。

**贴到屏幕的图片上**，滚轮缩放，`Ctrl`+滚轮调透明度，双击关闭。

<br>

## 控件来源

面板运行时从 jsDelivr / Google Fonts 加载下面这些，仓库里不打包任何一个。

- **Material 3：** [Material Web](https://github.com/material-components/material-web)（Apache-2.0）、Material Symbols、Roboto
- **iOS 风格：** [Framework7](https://github.com/framework7io/framework7) + Framework7 Icons（MIT）。苹果没有开源的 UI 组件库。

想加自己的控件就改 `widgets.html`：任何包在 `<div class="w" data-name="…">` 里的东西都会出现在面板上。

<br>

## 许可证

MIT
