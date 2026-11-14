# 素材来源

- `assets/glyphs.xml`：依据用户提供的 `MH5001-2021.pdf` 图样与尺寸表重绘。当前低节点轮廓可由 `tools/build_clean_glyphs.py` 重建；不是官方 CAD 或字体母版。对应条款和页码见使用指南。
- `assets/paint/reference.bin`：从用户提供的 `Markings_01_轻污渍Sample.psd` 和 `Markings_01_轻微透明磨损Sample.psd` 提取图层/蒙版。文件名、SHA256、层名、尺寸和混合方式见 `assets/paint/SOURCE.json`。
- `assets/app.ico`：项目现有程序图标，随源码保留。
- `tests/fixtures/`：工程兼容性与材质比对样本。

原始 PDF / PSD 不包含在仓库或发行包中。正常构建和运行不需要它们。素材重建工具只读取原文件；重新提取油漆时需自行提供同样结构的源 PSD，并安装 psd-tools。

```powershell
python tools/extract_paint_reference.py <轻污渍PSD路径> <轻微透明磨损PSD路径>
```

素材来源记录不表示已取得第三方再分发许可。本仓库未附开源许可。
