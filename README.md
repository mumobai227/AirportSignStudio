# Airport Sign Studio / 机场标志工坊

Windows 离线标牌贴图工具，支持机场地面标志、滑行道指示牌及机位号码牌，适用于国内地景制作。

## 下载与运行

在 [版本发布页](https://github.com/mumobai227/AirportSignStudio/releases/latest) 下载 `AirportSignStudio.exe`，双击运行。也可下载 Windows ZIP，解压后运行其中的 EXE。

适用于 Windows 10 / 11 x64。成品内含运行环境、字形和油漆素材，只需复制一个 EXE，无需另带 config 文件、安装字体或 .NET。工程 `.assproj` 需要另外保存和携带。

## 功能

- 同一张贴图混排不同标牌，支持 1–8 行列、合并与拆分区域、拖动局部边界、自定义分辨率和字高密度参考。
- 滑行道地面标志、地面机位编号、组合指示牌、机位识别牌、MAX SPAN、HP、NO ENTRY 和机型文字。
- 四类基础颜色统一替换、单格覆盖、实时拾色器、配色预设，以及直接烘焙进 PNG 的油漆脏污和磨损。
- 工程保存/读取、标牌复制/粘贴、撤销/重做，顶部菜单和可缩放、平移的预览。
- 编号清单、文字或扫描版机位坐标表 PDF、PNG/JPG 截图或识别结果可批量生成标牌，自动分配到多张拼图，并一起导出 PNG、POL 和槽位索引。
- 默认导出透明 PNG；开启 X-Plane 后可导出 PNG + 一个含全部分区的 POL，支持图案边界/分割边界和出血，也可导出 SVG。

## 开始使用

1. 展开“布局与分辨率”设置网格和贴图尺寸；单个标牌使用 1 × 1。
2. 点选预览格子，在左侧设置类型、文字和尺寸。
3. 保存工程，再选择“导出 PNG”；需要 POL 时开启顶部 X-Plane 选项。

[使用指南](docs/USER_GUIDE.md) · [更新记录](docs/CHANGELOG.md) · [构建说明](docs/BUILD.md) · [素材来源](docs/ASSET_SOURCES.md) · [v3.19 验证记录](docs/verification/v3.19.0.md)

## 从源码构建

在 Windows 上用 PowerShell 7 执行：

```powershell
./build.ps1 -Test
```

首次构建下载并校验项目内的 .NET SDK 10.0.404。输出为 `release_v3_25_1/AirportSignStudio.exe`；`-Test` 仅执行无窗口功能检查。

## 目录

| 目录 | 内容 |
|---|---|
| `src/` | Windows Forms 界面、排版、工程存取及导出 |
| `assets/` | 内置字形、图标和油漆图层数据 |
| `tests/` | 验证程序与兼容性样本 |
| `tools/` | SDK 准备、素材重建及可选检查工具 |
| `docs/` | 使用、开发、来源和版本说明 |

字形参考 MH 5001—2021 图样重绘，不是官方字体母版。软件用于场景制作，不代替真实机场的工程设计或合规审核；立牌贴图仍需贴到三维模型。源码及素材未附开放许可，原始 PDF / PSD 不包含在本仓库中。
