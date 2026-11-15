using System;
using System.Drawing;
using System.IO;
using System.Linq;
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
            Text = "批量生成标牌"; ClientSize = new Size(640, 530); MinimumSize = new Size(640, 430); StartPosition = FormStartPosition.CenterParent;
            Padding = new Padding(16);
            AtlasWorkspace page = book.Current;
            Settings selected = page.Slots[page.Selected];
            bool canUseTemplate = selected != null && (selected.Kind != AssetKind.GroundOther || selected.OtherStyle != "NO ENTRY");
            FlowLayoutPanel top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 82, FlowDirection = FlowDirection.LeftToRight };
            Button import = new StudioButton { Text = "导入航图 / 清单…", Width = 156, Height = 34 };
            ComboBox mode = new StudioComboBox { Width = 208, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "批量标牌样式" };
            mode.Items.Add("地面机位编号");
            if (canUseTemplate) mode.Items.Add(selected.Kind == AssetKind.TaxiSign ? "套用选中标牌 · 替换首段" : "套用选中标牌样式");
            mode.SelectedIndex = canUseTemplate && selected.Kind == AssetKind.GroundStand ? 1 : 0;
            CheckBox fill = new CheckBox { Text = "先填已有空格", AutoSize = true, Checked = true, Margin = new Padding(8, 9, 0, 0) };
            top.Controls.Add(import); top.Controls.Add(StudioTheme.FrameInput(mode, 34)); top.Controls.Add(fill); top.SetFlowBreak(fill, true);
            Label layout = new Label { Text = $"自动分页 · {page.Width} × {page.Height} px · 参考字高 {page.TargetTextPixels} px", AutoSize = true, Margin = new Padding(3, 8, 0, 6) };
            top.Controls.Add(layout);

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
            Action count = delegate
            {
                int entries = BatchSigns.ParseList(list.Text).Length;
                generate.Enabled = entries > 0;
                message.Text = $"{entries} 项" + (sourceNote.Length == 0 ? "" : Environment.NewLine + sourceNote);
            };
            list.TextChanged += delegate { count(); };
            import.Click += delegate
            {
                using OpenFileDialog file = new OpenFileDialog
                {
                    Filter = "航图与清单|*.pdf;*.json;*.txt;*.csv|航图 PDF|*.pdf|识别结果 JSON|*.json|编号清单|*.txt;*.csv",
                    Multiselect = true, Title = "导入航图或识别编号"
                };
                if (file.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    UseWaitCursor = true;
                    var imported = new System.Collections.Generic.List<string>();
                    int review = 0;
                    string[] pdf = file.FileNames.Where(path => Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)).ToArray();
                    bool table = true;
                    if (pdf.Length > 0) imported.AddRange(BatchSigns.FromPdfs(pdf, out table));
                    foreach (string path in file.FileNames.Except(pdf))
                    {
                        string text = File.ReadAllText(path);
                        if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
                        {
                            imported.AddRange(BatchSigns.FromReport(text, out int needsReview));
                            review += needsReview;
                        }
                        else imported.AddRange(BatchSigns.ParseList(text));
                    }
                    sourceNote = review > 0 ? $"{review} 条识别记录待核对。请补全“{BatchSigns.MissingStand}”或删除该行。" :
                        !table ? "已跳过普通平面图，请导入 0P 机位坐标表或带机位依据的识别 JSON。" :
                        "请对照来源检查编号清单。";
                    string newText = string.Join(Environment.NewLine, imported.Where(line => line.Length > 0));
                    if (newText.Length == 0) throw new ArgumentException("没有可确认的机位编号。请导入 0P 机位坐标表、机位识别 JSON，或粘贴已核对的编号。");
                    list.AppendText((list.TextLength == 0 ? "" : Environment.NewLine) + newText);
                    count();
                }
                catch (Exception error) { MessageBox.Show(this, error.Message, "读取失败"); }
                finally { UseWaitCursor = false; }
            };
            generate.Click += delegate
            {
                Settings template = mode.SelectedIndex == 1 ? selected : new Settings { Kind = AssetKind.GroundStand, Arrow = "↑" };
                try { Generated = BatchSigns.Add(book, engine, list.Text, template, fill.Checked); DialogResult = DialogResult.OK; Close(); }
                catch (Exception error) { MessageBox.Show(this, error.Message, "无法生成标牌"); }
            };
            count(); CompleteLayout();
        }
    }
}
