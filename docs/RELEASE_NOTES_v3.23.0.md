## 批量标牌和多张拼图

- 在「批量生成…」粘贴或导入编号清单，可按当前样式生成地面机位编号并自动分图。
- 直接读取带文字层的机位坐标表 PDF，支持 AutoCharts 分析 JSON 和 sign_autogen OCR 工程 JSON。普通航图读取结果作为候选，生成前在清单里核对。截图先在 sign_autogen 识别，再导入 JSON。
- 同一工程可编辑并保存最多 64 张拼图。一次批量生成可一步撤销，原有标牌和合并格保留。
- 「导出全部拼图」按每张图的 X-Plane 设置导出 PNG 或 PNG + POL，并生成含槽位和 UV 的 `AtlasIndex.csv`。

8,317 项无窗口检查通过。虹桥、浦东、深圳、邯郸四个机场的本地 0P PDF 试读结果共 877 个编号，逐项与 AutoCharts 机位坐标表提取结果一致。

发布包包含 Windows x64 单文件 EXE、Windows ZIP、源码 ZIP 和 SHA256 校验文件。
