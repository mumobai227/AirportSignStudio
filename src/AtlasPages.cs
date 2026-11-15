using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AirportSignStudio
{
    public sealed partial class AtlasForm
    {
        readonly ComboBox pagePicker = new StudioComboBox { Width = 136, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "当前拼图" };
        bool updatingPages;

        void BuildPageControls(FlowLayoutPanel header)
        {
            header.Controls.Add(StudioTheme.FrameInput(pagePicker, 36));
            Button add = new StudioButton { Text = "+", Quiet = true, Width = 38, Height = 36, AccessibleName = "新增拼图" };
            add.Click += delegate { AddAtlasPage(); };
            header.Controls.Add(add);
            Button batch = new StudioButton { Text = "批量生成…", Width = 108, Height = 36 };
            batch.Click += delegate { OpenBatch(); };
            header.Controls.Add(batch);
            pagePicker.SelectedIndexChanged += delegate
            {
                if (updatingPages || pagePicker.SelectedIndex < 0 || pagePicker.SelectedIndex == book.Active) return;
                ChangePage(pagePicker.SelectedIndex, true);
            };
            SyncPagePicker();
        }

        void SyncPagePicker()
        {
            updatingPages = true;
            pagePicker.BeginUpdate();
            pagePicker.Items.Clear();
            for (int index = 0; index < book.Pages.Count; index++)
                pagePicker.Items.Add("拼图 " + (index + 1).ToString("D2"));
            pagePicker.SelectedIndex = book.Active;
            pagePicker.EndUpdate();
            updatingPages = false;
        }

        void ChangePage(int index, bool reveal)
        {
            preview.FinishDrag(false); CommitEdits();
            book.Active = index;
            ShowActivePage(reveal);
        }

        void ShowActivePage(bool reveal = true)
        {
            preview.Workspace = workspace;
            multiple.Checked = false;
            preview.SyncPages(); SyncPagePicker(); SyncWorkspace(); RefreshAtlas();
            if (reveal) preview.RevealActive();
        }

        void AddAtlasPage()
        {
            CommitEdits();
            if (book.Pages.Count >= AtlasBook.MaxPages) { status.Text = "一个工程最多可有 64 张拼图。"; return; }
            book.Pages.Add(AtlasBook.BlankLike(workspace));
            book.Active = book.Pages.Count - 1;
            ShowActivePage();
        }

        void RemoveAtlasPage()
        {
            CommitEdits();
            if (book.Pages.Count <= 1) { status.Text = "工程至少保留一张拼图。"; return; }
            if (workspace.Slots.Any(slot => slot != null) &&
                MessageBox.Show(this, "删除当前拼图及其中的标牌？", "删除拼图", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            book.Pages.RemoveAt(book.Active);
            book.Active = Math.Min(book.Active, book.Pages.Count - 1);
            ShowActivePage();
        }

        void OpenBatch()
        {
            CommitEdits();
            using BatchSignsForm dialog = new BatchSignsForm(book, engine);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            preview.ResetNeighbours();
            ShowActivePage();
            status.Text = "已生成 " + dialog.Generated + " 个标牌 · 工程共 " + book.Pages.Count + " 张拼图";
        }

        bool SaveBook(bool saveAs)
        {
            string path = book.ProjectPath;
            if (saveAs || string.IsNullOrEmpty(path))
            {
                using SaveFileDialog dialog = new SaveFileDialog
                {
                    Filter = "机场标志工程|*.assproj", DefaultExt = "assproj", AddExtension = true,
                    FileName = string.IsNullOrEmpty(path) ? "AirportSigns.assproj" : Path.GetFileName(path)
                };
                if (dialog.ShowDialog(this) != DialogResult.OK) return false;
                path = dialog.FileName;
            }
            try { book.Save(path); return true; }
            catch (Exception error) { MessageBox.Show(this, error.Message, "工程保存失败"); return false; }
        }

        bool ConfirmSaveBook()
        {
            if (book.Encode() == (book.SavedProject ?? ProjectStore.Encode(new AtlasWorkspace()))) return true;
            DialogResult choice = MessageBox.Show(this, "拼图工程有未保存的更改，是否保存？", "保存工程", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            return choice == DialogResult.No || choice == DialogResult.Yes && SaveBook(false);
        }

        void ExportAll()
        {
            CommitEdits();
            if (!book.Pages.Any(page => page.Filled > 0)) { status.Text = "工程里还没有可导出的标牌。"; return; }
            using FolderBrowserDialog dialog = new FolderBrowserDialog { Description = "选择批量导出的上级文件夹", ShowNewFolderButton = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                UseWaitCursor = true;
                string name = "AirportSigns_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                status.Text = "已导出：" + book.ExportAll(engine, Path.Combine(dialog.SelectedPath, name));
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, "批量导出失败"); }
            finally { UseWaitCursor = false; }
        }
    }

    internal sealed class BatchSignsForm : DpiForm
    {
        public int Generated { get; private set; }

        public BatchSignsForm(AtlasBook book, SignEngine engine)
        {
            Text = "批量生成标牌"; ClientSize = new Size(660, 580); MinimumSize = new Size(650, 470); StartPosition = FormStartPosition.CenterParent;
            Padding = new Padding(16);
            AtlasWorkspace page = book.Current;
            Settings selected = page.Slots[page.Selected];
            bool canUseTemplate = selected != null && (selected.Kind != AssetKind.GroundOther || selected.OtherStyle != "NO ENTRY");
            FlowLayoutPanel top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 191, FlowDirection = FlowDirection.LeftToRight };
            Button import = new StudioButton { Text = "导入航图或截图…", Width = 156, Height = 34 };
            ComboBox mode = new StudioComboBox { Width = 208, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "批量标牌样式" };
            mode.Items.Add("地面机位编号");
            if (canUseTemplate) mode.Items.Add(selected.Kind == AssetKind.TaxiSign ? "套用选中标牌 · 替换首段" : "套用选中标牌样式");
            mode.SelectedIndex = canUseTemplate && selected.Kind == AssetKind.GroundStand ? 1 : 0;
            top.Controls.Add(import); AddInput(mode, endRow: true);

            ComboBox destination = Choose("起始拼图", 125);
            for (int index = 1; index <= book.Pages.Count; index++) destination.Items.Add($"拼图 {index:D2}");
            if (book.Pages.Count < AtlasBook.MaxPages) destination.Items.Add($"新建拼图 {book.Pages.Count + 1:D2}");
            destination.SelectedIndex = book.Active;
            ComboBox split = Choose("分割方式", 144);
            split.Items.AddRange(new object[] { "优先用现有格", "自动按字高", "固定行列" });
            split.SelectedIndex = (int)BatchSplit.ExistingFirst;
            NumericUpDown rows = Number("行", 1, 8, page.Rows, 88), columns = Number("列", 1, 8, page.Columns, 88);
            AddInput(destination, "起始拼图"); AddInput(split, "分割"); AddInput(rows, "行"); AddInput(columns, "列", endRow: true);

            ComboBox preset = Choose("贴图分辨率", 162);
            preset.Items.Add("沿用拼图尺寸");
            preset.Items.AddRange(AtlasForm.PresetLabels);
            NumericUpDown width = Number("宽 px", 128, 8192, page.Width, 128), height = Number("高 px", 128, 8192, page.Height, 128);
            AddInput(preset, "分辨率"); AddInput(width, "宽 px"); AddInput(height, "高 px", endRow: true);

            Label layout = new Label { AutoSize = true, Margin = new Padding(3, 7, 0, 6), ForeColor = StudioTheme.Muted };
            top.Controls.Add(layout);

            AtlasWorkspace Source() => destination.SelectedIndex < book.Pages.Count ? book.Pages[destination.SelectedIndex] : page;
            void SyncResolution()
            {
                int index = preset.SelectedIndex;
                width.Enabled = height.Enabled = index == preset.Items.Count - 1;
                if (width.Enabled) return;
                AtlasWorkspace source = Source();
                Size size = index == 0 ? new Size(source.Width, source.Height) : AtlasForm.Presets[index - 1];
                width.Value = size.Width; height.Value = size.Height;
            }
            void SyncGrid() => rows.Enabled = columns.Enabled = split.SelectedIndex == (int)BatchSplit.Fixed;
            void SyncDestination()
            {
                AtlasWorkspace source = Source();
                layout.Text = $"已有标牌的拼图只补空格，保留分割和尺寸 · 参考字高 {source.TargetTextPixels} px";
                SyncResolution();
            }
            preset.SelectedIndex = 0;
            preset.SelectedIndexChanged += delegate { SyncResolution(); };
            split.SelectedIndexChanged += delegate { SyncGrid(); };
            destination.SelectedIndexChanged += delegate { SyncDestination(); };
            SyncGrid(); SyncDestination();

            void AddInput(Control input, string label = null, bool endRow = false)
            {
                if (label != null) top.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 10, 1, 0) });
                StudioField field = StudioTheme.FrameInput(input, 34);
                top.Controls.Add(field);
                if (endRow) top.SetFlowBreak(field, true);
            }
            ComboBox Choose(string name, int size) => new StudioComboBox { AccessibleName = name, Width = size, DropDownStyle = ComboBoxStyle.DropDownList };
            NumericUpDown Number(string name, int min, int max, int value, int size) =>
                new NumericUpDown { AccessibleName = name, Width = size, Minimum = min, Maximum = max, Value = value };

            TextBox list = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = 250000,
                PlaceholderText = "每行一项，也可用逗号分隔", AccessibleName = "待生成标牌清单"
            };
            Label message = new Label { Dock = DockStyle.Bottom, Height = 58, ForeColor = StudioTheme.Muted, Padding = new Padding(0, 8, 0, 0) };
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 43, FlowDirection = FlowDirection.RightToLeft };
            Button generate = new StudioButton { Text = "生成标牌", Primary = true, Width = 110, Height = 34 };
            Button cancel = new StudioButton { Text = "取消", Width = 86, Height = 34, DialogResult = DialogResult.Cancel };
            actions.Controls.Add(generate); actions.Controls.Add(cancel); CancelButton = cancel;
            Controls.Add(list); Controls.Add(message); Controls.Add(actions); Controls.Add(top);

            string sourceNote = "";
            bool importing = false;
            Action count = delegate
            {
                int entries = BatchSigns.ParseList(list.Text).Length;
                generate.Enabled = entries > 0 && !importing;
                message.Text = $"{entries} 项" + (sourceNote.Length == 0 ? "" : Environment.NewLine + sourceNote);
            };
            list.TextChanged += delegate { count(); };
            import.Click += async delegate
            {
                using OpenFileDialog file = new OpenFileDialog
                {
                    Filter = "航图、截图及清单|*.pdf;*.png;*.jpg;*.jpeg;*.json;*.txt;*.csv|机位坐标表截图|*.png;*.jpg;*.jpeg|航图 PDF|*.pdf|识别结果 JSON|*.json|编号清单|*.txt;*.csv",
                    Multiselect = true, Title = "导入航图或识别编号"
                };
                if (file.ShowDialog(this) != DialogResult.OK) return;
                string previousNote = sourceNote;
                string[] paths = file.FileNames;
                try
                {
                    importing = true; import.Enabled = false;
                    sourceNote = "正在读取机位编号…"; count();
                    var result = await Task.Run(() => BatchSigns.ImportFiles(paths));
                    if (IsDisposed || Disposing) return;
                    string newText = string.Join(Environment.NewLine, result.Rows.Where(line => line.Length > 0));
                    if (newText.Length == 0) throw new ArgumentException("没有可确认的机位编号。请导入 0P 机位坐标表、机位识别 JSON，或粘贴已核对的编号。");
                    sourceNote = result.Note;
                    list.AppendText((list.TextLength == 0 ? "" : Environment.NewLine) + newText);
                }
                catch (Exception error)
                {
                    sourceNote = previousNote;
                    if (!IsDisposed && !Disposing) MessageBox.Show(this, error.Message, "读取失败");
                }
                finally
                {
                    importing = false;
                    if (!IsDisposed && !Disposing) { import.Enabled = true; count(); }
                }
            };
            generate.Click += delegate
            {
                Settings template = mode.SelectedIndex == 1 ? selected : new Settings { Kind = AssetKind.GroundStand, Arrow = "↑" };
                Size resolution = preset.SelectedIndex == 0 ? Size.Empty : new Size((int)width.Value, (int)height.Value);
                Size grid = new Size((int)columns.Value, (int)rows.Value);
                try
                {
                    Generated = BatchSigns.Add(book, engine, list.Text, template, destination.SelectedIndex, (BatchSplit)split.SelectedIndex, resolution, grid);
                    DialogResult = DialogResult.OK; Close();
                }
                catch (Exception error) { MessageBox.Show(this, error.Message, "无法生成标牌"); }
            };
            count(); CompleteLayout();
        }
    }
}
