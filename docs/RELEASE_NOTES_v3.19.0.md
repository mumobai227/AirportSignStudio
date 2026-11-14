机场地面标志、滑行道指示牌和机位号码牌的 Windows 离线贴图工具。

## 本版更新

- 文件、编辑、视图、标牌和 X-Plane 操作集中到顶部菜单。
- 预览支持 1%–800% 缩放、100%、适合窗口、中键或空格拖动平移。
- 新工程默认导出 PNG；开启 X-Plane 后可导出 PNG + 一个含全部分区的 POL，或 SVG。
- 保留混合拼图、合并与边界调整、工程保存、复制粘贴、撤销重做、四色配色和 PSD 参考油漆效果。
- 本次整理源码、使用指南、构建说明及素材来源，程序与此前 v3.19 构建保持一致。

## 下载

- `AirportSignStudio.exe`：Windows 10 / 11 x64 单文件程序，下载后直接运行，不需另带 config、安装字体或 .NET。
- `AirportSignStudio_v3.19.0_Windows.zip`：同一 EXE 和简短使用说明。
- `AirportSignStudio_v3.19.0_Source.zip`：源码、内置素材、测试样本和文档；不含 SDK 与构建缓存。
- `SHA256SUMS.txt`：上述三个文件的 SHA256。

工程 `.assproj` 请另外保存和携带。原始 PDF / PSD 不包含在发行包中。

## 验证

重新构建成功，8,259 项无窗口断言通过，70 个 PNG / SVG / POL 与 v3.18 输出完全一致。此次没有打开程序窗口或操控界面；另一设备、真实跨屏及 WED / X-Plane 内效果未验收。

EXE SHA256：`23a0fb46bd81b3a597106b3049374a3255355f03921cfdad75538d066abaeb4c`
