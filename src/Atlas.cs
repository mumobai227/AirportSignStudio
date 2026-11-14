using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace AirportSignStudio
{
    public sealed class AtlasPreview : Control
    {
        public AtlasLayout CurrentLayout;
        public AtlasWorkspace Workspace;
        public Bitmap Painted;
        public readonly HashSet<int> Selection = new HashSet<int>();
        public bool MultiSelect;
        public Action<int, bool, bool> SelectSlot;
        public Action LayoutChanged;
        public Action LayoutEditStarted, LayoutEditEnded;
        AtlasDivider dragging;
        AtlasRegion[] dragStart;
        readonly StudioMotion selectionMotion;
        readonly PreviewViewport viewport = new PreviewViewport();
        public Action ViewChanged;
        bool panning, spaceHeld;
        Point panStart;
        int animatedSelection = -1;
        public AtlasPreview()
        {
            DoubleBuffered = true; ResizeRedraw = true; TabStop = true;
            selectionMotion = new StudioMotion(this, value => Invalidate());
            AccessibleName = "拼图预览：滚轮缩放，中键或空格加拖动平移，点击编辑，Ctrl 多选，拖动分隔线调整边界";
        }
        public void SelectionChanged()
        {
            if (Workspace == null || animatedSelection == Workspace.Selected) return;
            animatedSelection = Workspace.Selected; selectionMotion.To(0, false); selectionMotion.To(1);
        }
        RectangleF View()
        {
            if (Workspace == null) return RectangleF.Empty;
            viewport.Resize(ClientSize, new Size(Workspace.Width, Workspace.Height), 26 * StudioTheme.Scale(this));
            return viewport.Bounds;
        }
        public float Zoom { get { View(); return viewport.Scale; } }
        public bool IsFit { get { return viewport.IsFit; } }
        public void FitView() { FinishDrag(false); StopPan(); viewport.Fit(); ChangedView(); }
        public void SetZoom(float scale) { FinishDrag(false); StopPan(); View(); viewport.ZoomAt(scale, new PointF(Width / 2f, Height / 2f)); ChangedView(); }
        public void ZoomBy(float factor) { SetZoom(Zoom * factor); }
        void ChangedView() { Invalidate(); if (ViewChanged != null) ViewChanged(); }
        void StopPan() { if (!panning) return; panning = false; Capture = false; Cursor = spaceHeld ? Cursors.Hand : Cursors.Default; }
        protected override void OnResize(EventArgs e) { base.OnResize(e); if (viewport != null) { View(); ChangedView(); } }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (dragging == null && !panning && CurrentLayout != null)
            {
                View(); viewport.ZoomAt(viewport.Scale * (float)Math.Pow(1.2, e.Delta / 120.0), e.Location); ChangedView();
            }
            base.OnMouseWheel(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics; g.Clear(StudioTheme.Canvas);
            RectangleF view = View();
            if (CurrentLayout == null || view.IsEmpty) return;
            float uiScale = StudioTheme.Scale(this);
            using (Brush shadow = new SolidBrush(Color.FromArgb(18, 36, 36, 33)))
                g.FillRectangle(shadow, view.X - 3 * uiScale, view.Y + 3 * uiScale, view.Width + 6 * uiScale, view.Height + 3 * uiScale);
            using (Brush a = new SolidBrush(Color.FromArgb(255, 255, 254)))
            using (Brush b = new SolidBrush(Color.FromArgb(239, 239, 235)))
            {
                g.FillRectangle(a, view);
                GraphicsState clip = g.Save(); g.SetClip(view);
                int tile = Math.Max(8, (int)(12 * uiScale));
                int startX = Math.Max(0, (int)Math.Floor(-view.X / tile)), startY = Math.Max(0, (int)Math.Floor(-view.Y / tile));
                int endX = (int)Math.Ceiling((Math.Min(Width, view.Right) - view.X) / tile), endY = (int)Math.Ceiling((Math.Min(Height, view.Bottom) - view.Y) / tile);
                for (int y = startY; y < endY; y++) for (int x = startX; x < endX; x++)
                    if ((x + y) % 2 == 0) g.FillRectangle(b, view.X + x * tile, view.Y + y * tile, tile, tile);
                g.Restore(clip);
            }
            float scale = view.Width / Workspace.Width;
            if (Painted != null) g.DrawImage(Painted, view);
            else
            {
                GraphicsState state = g.Save();
                using (Matrix matrix = new Matrix(scale, 0, 0, scale, view.X, view.Y)) g.Transform = matrix;
                CurrentLayout.Document.Draw(g); g.Restore(state);
            }
            using (Font font = DpiLayout.Font(this, 9, FontStyle.Regular))
            using (StringFormat center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                for (int i = 0; i < CurrentLayout.Slots.Count; i++)
                {
                    AtlasSlotInfo slot = CurrentLayout.Slots[i];
                    if (!slot.Visible) continue;
                    RectangleF box = new RectangleF(view.X + slot.Cell.X * scale, view.Y + slot.Cell.Y * scale, slot.Cell.Width * scale, slot.Cell.Height * scale);
                    if (!box.IntersectsWith(ClientRectangle)) continue;
                    bool low = slot.Occupied && slot.Error.Length == 0 && slot.SmallestTextPixels < Workspace.TargetTextPixels;
                    Color color = Selection.Contains(i) ? StudioTheme.Blend(StudioTheme.Muted, StudioTheme.Ink, selectionMotion.Value) : slot.Error.Length > 0 ? Color.Firebrick : low ? Color.FromArgb(173, 98, 17) : Color.FromArgb(175, 175, 166);
                    using (Pen pen = new Pen(color, (i == Workspace.Selected ? 1.5f + selectionMotion.Value * 0.5f : Selection.Contains(i) ? 2 : 0.8f) * uiScale))
                    {
                        if (!slot.Occupied) pen.DashStyle = DashStyle.Dash;
                        g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
                    }
                    using (Brush text = new SolidBrush(color))
                    using (Brush badge = new SolidBrush(Color.FromArgb(230, 247, 247, 245)))
                    {
                        string label = (i + 1).ToString() + (Workspace.Regions[i].Children == null ? "" : " 合并") + (slot.Error.Length > 0 ? "  检查输入" : low ? "  字高 " + slot.SmallestTextPixels.ToString("0") + " px" : "");
                        SizeF size = g.MeasureString(label, font);
                        GraphicsState cellClip = g.Save(); g.SetClip(box);
                        using (GraphicsPath pill = StudioTheme.Rounded(new RectangleF(box.X + 4, box.Y + 4, size.Width + 8, size.Height + 3), 4 * uiScale)) g.FillPath(badge, pill);
                        g.DrawString(label, font, text, box.X + 8, box.Y + 5);
                        if (!slot.Occupied) g.DrawString("空白", font, text, box, center);
                        if (slot.Error.Length > 0) g.DrawString("输入有误", font, text, box, center);
                        g.Restore(cellClip);
                    }
                    if (i == Workspace.Selected && !MultiSelect)
                        using (Brush handle = new SolidBrush(StudioTheme.Ink))
                            foreach (AtlasEdge edge in Enum.GetValues(typeof(AtlasEdge)))
                            {
                                if (Workspace.Divider(i, edge) == null) continue;
                                float x = edge == AtlasEdge.Left ? box.Left - Workspace.Gap * scale / 2 : edge == AtlasEdge.Right ? box.Right + Workspace.Gap * scale / 2 : box.Left + box.Width / 2;
                                float y = edge == AtlasEdge.Top ? box.Top - Workspace.Gap * scale / 2 : edge == AtlasEdge.Bottom ? box.Bottom + Workspace.Gap * scale / 2 : box.Top + box.Height / 2;
                                g.FillEllipse(handle, x - 3 * uiScale, y - 3 * uiScale, 6 * uiScale, 6 * uiScale);
                            }
                }
            if (dragging != null)
                using (Pen pen = new Pen(StudioTheme.Ink, 2))
                {
                    float x = view.X + (Workspace.Padding - Workspace.Gap / 2f) * scale, y = view.Y + (Workspace.Padding - Workspace.Gap / 2f) * scale;
                    float w = (Workspace.Width - 2f * Workspace.Padding + Workspace.Gap) * scale, h = (Workspace.Height - 2f * Workspace.Padding + Workspace.Gap) * scale;
                    if (dragging.Vertical) g.DrawLine(pen, x + dragging.Position * w, y + dragging.Start * h, x + dragging.Position * w, y + dragging.End * h);
                    else g.DrawLine(pen, x + dragging.Start * w, y + dragging.Position * h, x + dragging.End * w, y + dragging.Position * h);
                }
            StudioTheme.CanvasFrame(this, g);
        }
        protected override void Dispose(bool disposing) { if (disposing) selectionMotion.Dispose(); base.Dispose(disposing); }

        AtlasDivider DividerAt(Point point, out int index)
        {
            index = -1;
            if (CurrentLayout == null || MultiSelect) return null;
            RectangleF view = View(); if (view.IsEmpty) return null;
            float scale = view.Width / Workspace.Width, nearest = 7;
            AtlasDivider found = null;
            foreach (AtlasSlotInfo slot in CurrentLayout.Slots.Where(s => s.Visible))
            {
                RectangleF b = slot.Cell;
                foreach (AtlasEdge edge in Enum.GetValues(typeof(AtlasEdge)))
                {
                    bool vertical = edge == AtlasEdge.Left || edge == AtlasEdge.Right;
                    float position = edge == AtlasEdge.Left ? b.Left - Workspace.Gap / 2f : edge == AtlasEdge.Right ? b.Right + Workspace.Gap / 2f :
                        edge == AtlasEdge.Top ? b.Top - Workspace.Gap / 2f : b.Bottom + Workspace.Gap / 2f;
                    float distance = Math.Abs((vertical ? point.X - view.X : point.Y - view.Y) - position * scale);
                    float along = vertical ? point.Y - view.Y : point.X - view.X;
                    if (distance >= nearest || along < (vertical ? b.Top : b.Left) * scale - 3 || along > (vertical ? b.Bottom : b.Right) * scale + 3) continue;
                    AtlasDivider divider = Workspace.Divider(slot.Index, edge);
                    if (divider != null) { nearest = distance; found = divider; index = slot.Index; }
                }
            }
            return found;
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); Focus();
            if (CurrentLayout == null) return;
            if (e.Button == MouseButtons.Middle || e.Button == MouseButtons.Left && spaceHeld)
            {
                FinishDrag(false); panning = true; panStart = e.Location; Capture = true; Cursor = Cursors.Hand; return;
            }
            if (e.Button == MouseButtons.Right)
            {
                RectangleF box = View(); if (box.IsEmpty || !box.Contains(e.Location)) return;
                float ratio = box.Width / Workspace.Width;
                int clicked = CurrentLayout.HitTest(new PointF((e.X - box.X) / ratio, (e.Y - box.Y) / ratio));
                if (clicked >= 0 && SelectSlot != null) SelectSlot(clicked, false, false);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            int owner = -1;
            AtlasDivider divider = (ModifierKeys & (Keys.Control | Keys.Shift)) == 0 ? DividerAt(e.Location, out owner) : null;
            if (divider != null)
            {
                // Select before capturing; the editor may rebuild its controls.
                if (SelectSlot != null) SelectSlot(owner, false, false);
                if (LayoutEditStarted != null) LayoutEditStarted();
                dragging = divider;
                dragStart = Workspace.Regions.Select(r => r == null ? null : RegionData.From(r).ToRegion()).ToArray();
                Capture = true; Cursor = divider.Vertical ? Cursors.SizeWE : Cursors.SizeNS; Invalidate(); return;
            }
            RectangleF view = View(); if (!view.Contains(e.Location)) return;
            float scale = view.Width / Workspace.Width;
            int index = CurrentLayout.HitTest(new PointF((e.X - view.X) / scale, (e.Y - view.Y) / scale));
            if (index >= 0 && SelectSlot != null) SelectSlot(index, MultiSelect || (ModifierKeys & Keys.Control) != 0, (ModifierKeys & Keys.Shift) != 0);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (panning)
            {
                View(); viewport.Move(e.X - panStart.X, e.Y - panStart.Y); panStart = e.Location; ChangedView(); return;
            }
            if (dragging != null)
            {
                RectangleF view = View(); float scale = view.Width / Workspace.Width;
                float pixel = (dragging.Vertical ? e.X - view.X : e.Y - view.Y) / scale;
                float span = (dragging.Vertical ? Workspace.Width : Workspace.Height) - 2f * Workspace.Padding + Workspace.Gap;
                float position = (pixel - Workspace.Padding + Workspace.Gap / 2f) / span;
                if (Workspace.MoveDivider(dragging, position) && LayoutChanged != null) LayoutChanged();
                return;
            }
            int owner; AtlasDivider hover = DividerAt(e.Location, out owner);
            Cursor = spaceHeld ? Cursors.Hand : hover == null ? Cursors.Default : hover.Vertical ? Cursors.SizeWE : Cursors.SizeNS;
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (panning && (e.Button == MouseButtons.Middle || e.Button == MouseButtons.Left)) { StopPan(); return; }
            if (e.Button == MouseButtons.Left) FinishDrag(false);
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture) { panning = false; FinishDrag(false); }
        }
        public void FinishDrag(bool cancel)
        {
            if (dragging == null) return;
            if (cancel) Array.Copy(dragStart, Workspace.Regions, dragStart.Length);
            dragging = null; dragStart = null; Capture = false;
            if (cancel && LayoutChanged != null) LayoutChanged();
            if (LayoutEditEnded != null) LayoutEditEnded();
            Invalidate();
        }
        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            return key == Keys.Space || key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down || key == Keys.Escape || base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space) { spaceHeld = true; Cursor = Cursors.Hand; e.Handled = true; return; }
            if (e.KeyCode == Keys.Escape && panning) { StopPan(); e.Handled = true; return; }
            if (Workspace == null || CurrentLayout == null || SelectSlot == null) return;
            if (e.KeyCode == Keys.Escape && dragging != null)
            {
                FinishDrag(true); e.Handled = true; return;
            }
            RectangleF b = CurrentLayout.Slots[Workspace.Selected].Cell;
            PointF point = new PointF(b.Left + b.Width / 2, b.Top + b.Height / 2);
            if (e.KeyCode == Keys.Left) point.X = b.Left - Workspace.Gap - 1;
            else if (e.KeyCode == Keys.Right) point.X = b.Right + Workspace.Gap + 1;
            else if (e.KeyCode == Keys.Up) point.Y = b.Top - Workspace.Gap - 1;
            else if (e.KeyCode == Keys.Down) point.Y = b.Bottom + Workspace.Gap + 1;
            else return;
            int index = CurrentLayout.HitTest(point);
            if (index >= 0) SelectSlot(index, false, e.Shift);
            e.Handled = true;
        }
        protected override void OnKeyUp(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { spaceHeld = false; StopPan(); Cursor = Cursors.Default; } base.OnKeyUp(e); }
        protected override void OnLostFocus(EventArgs e) { spaceHeld = false; StopPan(); base.OnLostFocus(e); }
    }

    public sealed class AtlasForm : DpiForm
    {
        readonly SignEngine engine;
        readonly AtlasWorkspace workspace;
        readonly AtlasHistory history;
        readonly AtlasPreview preview = new AtlasPreview();
        readonly SlotEditor editor = new SlotEditor();
        readonly Label selectedTitle = new Label(), detail = new Label(), density = new Label(), status = new Label();
        readonly NumericUpDown rows = new NumericUpDown(), columns = new NumericUpDown(), width = new NumericUpDown(), height = new NumericUpDown(), gap = new NumericUpDown(), padding = new NumericUpDown(), target = new NumericUpDown();
        readonly ComboBox preset = new StudioComboBox();
        readonly Button exportPrimary = new StudioButton { Primary = true, IconKind = StudioIcon.Export }, recommend = new StudioButton(), paste = new StudioButton();
        readonly ToolStripMenuItem png = new ToolStripMenuItem("导出 PNG…"), svg = new ToolStripMenuItem("导出 SVG…"), pol = new ToolStripMenuItem("导出 PNG + POL…");
        readonly ToolStripMenuItem xplaneMode = new ToolStripMenuItem("启用 X-Plane") { CheckOnClick = true }, polSettings = new ToolStripMenuItem("POL 设置…");
        readonly ToolStripSeparator xplaneSeparator = new ToolStripSeparator();
        readonly Button merge = new StudioButton(), split = new StudioButton(), copy = new StudioButton();
        readonly ToolStripMenuItem undo = new ToolStripMenuItem("撤销") { ShortcutKeyDisplayString = "Ctrl+Z" }, redo = new ToolStripMenuItem("重做") { ShortcutKeyDisplayString = "Ctrl+Y" };
        readonly Label zoomLabel = new Label();
        readonly Button zoomOut = new StudioButton(), zoomIn = new StudioButton();
        readonly ContextMenuStrip slotMenu = new ContextMenuStrip();
        readonly CheckBox multiple = new CheckBox();
        readonly StudioDisclosure layoutOptions = new StudioDisclosure();
        readonly Timer paintTimer = new Timer { Interval = 180 };
        static readonly Size[] Presets = { new Size(1024, 1024), new Size(2048, 2048), new Size(4096, 4096), new Size(8192, 8192), new Size(2048, 1024), new Size(4096, 2048) };
        Settings copied;
        AtlasLayout layout;
        Size suggested;
        bool updating;

        public AtlasForm(SignEngine renderer, AtlasWorkspace state)
        {
            engine = renderer; workspace = state; preview.Workspace = workspace; history = new AtlasHistory(workspace);
            ProjectTitle();
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            BackColor = StudioTheme.Surface; ForeColor = StudioTheme.Ink;
            ClientSize = new Size(1440, (int)Math.Round(1440 * StudioTheme.Golden)); MinimumSize = new Size(1140, 700); StartPosition = FormStartPosition.CenterParent;
            Panel top = new Panel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(21, 8, 21, 8) };
            top.Paint += delegate(object sender, PaintEventArgs e) { using (Pen line = new Pen(StudioTheme.Line)) e.Graphics.DrawLine(line, 0, top.Height - 1, top.Width, top.Height - 1); };
            FlowLayoutPanel header = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            Panel brand = new Panel { Width = 312, Height = 40, Margin = new Padding(0) };
            brand.Controls.Add(new Label { Text = "拼图编辑器", AutoSize = true, Font = new Font(Font.FontFamily, 12, FontStyle.Bold), Location = new Point(0, 9) });
            header.Controls.Add(brand);
            Panel primary = new Panel { Dock = DockStyle.Right, Width = 166 };
            exportPrimary.SetBounds(13, 4, 153, 36); primary.Controls.Add(exportPrimary);
            top.Controls.Add(header); top.Controls.Add(primary);
            Panel left = new Panel { Dock = DockStyle.Left, Width = 356, BackColor = StudioTheme.Sidebar };
            selectedTitle.Dock = DockStyle.Top; selectedTitle.Height = 55; selectedTitle.Padding = new Padding(21, 19, 0, 0); selectedTitle.Font = new Font(Font.FontFamily, 10.5f, FontStyle.Bold);
            FlowLayoutPanel slotActions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(12, 8, 0, 0), WrapContents = false };
            copy.Text = "复制标牌"; copy.Width = 105; copy.Height = 34; ((StudioButton)copy).Quiet = true; ((StudioButton)copy).IconKind = StudioIcon.Copy;
            paste.Text = "粘贴标牌"; paste.Width = 105; paste.Height = 34; paste.Enabled = false; ((StudioButton)paste).Quiet = true; ((StudioButton)paste).IconKind = StudioIcon.Paste;
            Button clear = new StudioButton { Text = "清空此格", Quiet = true, Width = 105, Height = 34, IconKind = StudioIcon.Clear };
            copy.Click += delegate { CopySign(); };
            paste.Click += delegate { PasteSign(); };
            clear.Click += delegate { ClearSign(); };
            slotActions.Controls.Add(copy); slotActions.Controls.Add(paste); slotActions.Controls.Add(clear);
            ToolStripMenuItem copyItem = new ToolStripMenuItem("复制标牌") { ShortcutKeyDisplayString = "Ctrl+C" };
            ToolStripMenuItem pasteItem = new ToolStripMenuItem("粘贴标牌") { ShortcutKeyDisplayString = "Ctrl+V" };
            copyItem.Click += delegate { CopySign(); }; pasteItem.Click += delegate { PasteSign(); };
            slotMenu.Items.Add(copyItem); slotMenu.Items.Add(pasteItem);
            slotMenu.Opening += delegate { copyItem.Enabled = workspace.Slots[workspace.Selected] != null; pasteItem.Enabled = copied != null; };
            preview.ContextMenuStrip = slotMenu;
            editor.Dock = DockStyle.Fill; editor.Changed = delegate(Settings settings) { workspace.Slots[workspace.Selected] = settings; RefreshAtlas(); };
            editor.EditStarted = BeginEdit; editor.EditEnded = EndEdit;
            editor.InheritedColors = settings =>
            {
                using (SignDocument sample = engine.Build(TypeColorSettings.Sample(TypeColorSettings.For(settings)), workspace.Colors, workspace.TypeColors, workspace.BaseColors)) return ColorSettings.DisplayColors(sample);
            };
            left.Controls.Add(editor); left.Controls.Add(selectedTitle); left.Controls.Add(slotActions);

            Panel right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(21, 13, 21, 13) };
            layoutOptions.Dock = DockStyle.Top;
            FlowLayoutPanel grid = Row(0), texture = Row(44), quality = Row(88), regions = Row(0);
            regions.Dock = DockStyle.Top; regions.Height = 47;
            Number(grid, "列", columns, 1, 8, workspace.Columns, 55);
            Number(grid, "行", rows, 1, 8, workspace.Rows, 55);
            Number(grid, "间距 px", gap, 0, 512, workspace.Gap, 64);
            Number(grid, "留白 px", padding, 0, 256, workspace.Padding, 64);
            texture.Controls.Add(new Label { Text = "分辨率", AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
            preset.DropDownStyle = ComboBoxStyle.DropDownList; preset.Width = 202; preset.AccessibleName = "拼图分辨率预设";
            preset.Items.AddRange(new object[] { "1K · 1024 × 1024", "2K · 2048 × 2048", "4K · 4096 × 4096", "8K · 8192 × 8192", "横向 · 2048 × 1024", "横向 · 4096 × 2048", "自定义" }); texture.Controls.Add(StudioTheme.FrameInput(preset, 34));
            Number(texture, "宽", width, 128, 8192, workspace.Width, 84); Number(texture, "高", height, 128, 8192, workspace.Height, 84);
            Number(quality, "参考最小字高 px", target, 16, 1024, workspace.TargetTextPixels, 72);
            recommend.Text = "采用建议分辨率"; recommend.Width = 153; recommend.Height = 34; recommend.Margin = new Padding(12, 3, 12, 0); quality.Controls.Add(recommend);
            density.AutoSize = true; density.Margin = new Padding(0, 6, 0, 0); quality.Controls.Add(density);
            multiple.Text = "多选"; multiple.Width = 68; multiple.Height = 30; regions.Controls.Add(multiple);
            merge.Text = "合并选区"; merge.Size = new Size(96, 34); ((StudioButton)merge).Quiet = true; regions.Controls.Add(merge);
            split.Text = "拆分合并区"; split.Size = new Size(104, 34); ((StudioButton)split).Quiet = true; regions.Controls.Add(split);
            Button reset = new StudioButton { Text = "恢复均分", Quiet = true, Size = new Size(92, 34) }; regions.Controls.Add(reset);
            multiple.CheckedChanged += delegate { preview.MultiSelect = multiple.Checked; if (!multiple.Checked) Select(workspace.Selected); preview.Invalidate(); };
            merge.Click += delegate
            {
                try
                {
                    workspace.Merge(preview.Selection); multiple.Checked = false; Select(workspace.Selected); RefreshAtlas();
                }
                catch (ArgumentException error) { status.Text = error.Message; }
            };
            split.Click += delegate
            {
                try { workspace.Split(workspace.Selected); multiple.Checked = false; Select(workspace.Selected); RefreshAtlas(); }
                catch (ArgumentException error) { status.Text = error.Message; }
            };
            reset.Click += delegate { workspace.ResetGridLayout(); multiple.Checked = false; Select(workspace.Selected); RefreshAtlas(); };
            layoutOptions.Content.Controls.Add(grid); layoutOptions.Content.Controls.Add(texture); layoutOptions.Content.Controls.Add(quality);
            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 105 };
            detail.Dock = DockStyle.Top; detail.Height = 54; detail.Padding = new Padding(0, 10, 0, 0);
            FlowLayoutPanel zoomTools = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 320, Height = 40, WrapContents = false, Padding = new Padding(0, 10, 0, 0) };
            zoomOut.Text = "−"; zoomOut.Size = new Size(32, 32); zoomOut.AccessibleName = "缩小预览";
            zoomIn.Text = "+"; zoomIn.Size = new Size(32, 32); zoomIn.AccessibleName = "放大预览";
            zoomLabel.Size = new Size(54, 32); zoomLabel.TextAlign = ContentAlignment.MiddleCenter; zoomLabel.ForeColor = StudioTheme.Muted; zoomLabel.AccessibleName = "预览缩放比例";
            Button actual = new StudioButton { Text = "100%", Quiet = true, Size = new Size(62, 32) }, fit = new StudioButton { Text = "适合窗口", Quiet = true, Size = new Size(96, 32) };
            zoomOut.Click += delegate { preview.ZoomBy(1 / 1.2f); }; zoomIn.Click += delegate { preview.ZoomBy(1.2f); };
            actual.Click += delegate { preview.SetZoom(1); }; fit.Click += delegate { preview.FitView(); };
            zoomTools.Controls.Add(zoomOut); zoomTools.Controls.Add(zoomLabel); zoomTools.Controls.Add(zoomIn); zoomTools.Controls.Add(actual); zoomTools.Controls.Add(fit);
            status.Dock = DockStyle.Bottom; status.Height = 39; status.Padding = new Padding(0, 5, 0, 0); status.ForeColor = StudioTheme.Muted;
            detail.AutoEllipsis = true; detail.ForeColor = StudioTheme.Muted;
            Panel information = new Panel { Dock = DockStyle.Fill }; information.Controls.Add(detail);
            footer.Controls.Add(information); footer.Controls.Add(zoomTools); footer.Controls.Add(status);
            preview.Dock = DockStyle.Fill; preview.SelectSlot = SelectPreview; preview.LayoutChanged = RefreshAtlas;
            preview.LayoutEditStarted = BeginEdit; preview.LayoutEditEnded = EndEdit;
            right.Controls.Add(preview); right.Controls.Add(footer); right.Controls.Add(regions); right.Controls.Add(layoutOptions);
            Controls.Add(right); Controls.Add(left); Controls.Add(top);
            MainMenuStrip = CreateMenus(); Controls.Add(MainMenuStrip);

            rows.ValueChanged += delegate { ResizeGrid(); }; columns.ValueChanged += delegate { ResizeGrid(); };
            width.ValueChanged += delegate { ChangeCanvas(); }; height.ValueChanged += delegate { ChangeCanvas(); };
            gap.ValueChanged += delegate { if (!updating) { workspace.Gap = (int)gap.Value; RefreshAtlas(); } };
            padding.ValueChanged += delegate { if (!updating) { workspace.Padding = (int)padding.Value; RefreshAtlas(); } };
            target.ValueChanged += delegate { if (!updating) { workspace.TargetTextPixels = (int)target.Value; RefreshAtlas(); } };
            preset.SelectedIndexChanged += delegate
            {
                if (updating || preset.SelectedIndex < 0 || preset.SelectedIndex >= Presets.Length) return;
                SetCanvas(Presets[preset.SelectedIndex]);
            };
            recommend.Click += delegate { if (!suggested.IsEmpty) SetCanvas(suggested); };
            png.Click += delegate { Export(false); }; svg.Click += delegate { Export(true); };
            pol.Click += delegate { OpenPol(); }; polSettings.Click += delegate { OpenPol(); };
            exportPrimary.Click += delegate { if (workspace.XPlane) OpenPol(); else Export(false); };
            preview.ViewChanged = UpdateZoom;
            paintTimer.Tick += delegate
            {
                paintTimer.Stop(); if (layout == null || !workspace.Paint.Active) return;
                try { Bitmap next = PaintRenderer.Raster(layout, workspace.Paint, PaintPreviewSize()); Bitmap old = preview.Painted; preview.Painted = next; if (old != null) old.Dispose(); preview.Invalidate(); }
                catch (Exception error) { status.Text = "磨损预览失败：" + error.Message; }
            };
            SyncPreset(); Select(workspace.Selected); RefreshAtlas();
            CompleteLayout(); UpdateZoom();
        }

        static ToolStripMenuItem Command(string text, Action action, string shortcut = "")
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text) { ShortcutKeyDisplayString = shortcut };
            item.Click += delegate { action(); }; return item;
        }
        MenuStrip CreateMenus()
        {
            MenuStrip menu = new MenuStrip { Dock = DockStyle.Top, BackColor = StudioTheme.Surface, ForeColor = StudioTheme.Ink, Font = Font, Padding = new Padding(15, 5, 15, 5), Renderer = new StudioMenuRenderer() };
            ToolStripMenuItem file = new ToolStripMenuItem("文件(&F)"), edit = new ToolStripMenuItem("编辑(&E)"), view = new ToolStripMenuItem("视图(&V)"), sign = new ToolStripMenuItem("标牌(&S)"), xplane = new ToolStripMenuItem("X-Plane(&X)");
            file.DropDownItems.Add(Command("读取工程…", OpenProject, "Ctrl+O"));
            file.DropDownItems.Add(Command("保存工程", () => SaveProject(false), "Ctrl+S"));
            file.DropDownItems.Add(Command("另存为…", () => SaveProject(true), "Ctrl+Shift+S"));
            file.DropDownItems.Add(new ToolStripSeparator()); file.DropDownItems.Add(png); file.DropDownItems.Add(pol); file.DropDownItems.Add(svg);
            file.DropDownItems.Add(new ToolStripSeparator()); file.DropDownItems.Add(Command("退出", Close, "Alt+F4"));
            undo.Click += delegate { UndoRedo(false); }; redo.Click += delegate { UndoRedo(true); };
            edit.DropDownItems.Add(undo); edit.DropDownItems.Add(redo); edit.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem copyMenu = Command("复制标牌", CopySign, "Ctrl+C"), pasteMenu = Command("粘贴标牌", PasteSign, "Ctrl+V");
            edit.DropDownItems.Add(copyMenu); edit.DropDownItems.Add(pasteMenu); edit.DropDownItems.Add(Command("清空此格", ClearSign));
            edit.DropDownOpening += delegate { UpdateHistoryButtons(); copyMenu.Enabled = copy.Enabled; pasteMenu.Enabled = paste.Enabled; };
            ToolStripMenuItem layoutMenu = Command("布局与分辨率", () => layoutOptions.SetExpanded(!layoutOptions.Expanded));
            view.DropDownItems.Add(layoutMenu); view.DropDownItems.Add(new ToolStripSeparator());
            view.DropDownItems.Add(Command("放大预览", () => preview.ZoomBy(1.2f), "Ctrl++"));
            view.DropDownItems.Add(Command("缩小预览", () => preview.ZoomBy(1 / 1.2f), "Ctrl+−"));
            view.DropDownItems.Add(Command("适合窗口", preview.FitView, "Ctrl+0"));
            view.DropDownItems.Add(Command("实际大小 · 100%", () => preview.SetZoom(1), "Ctrl+1"));
            view.DropDownOpening += delegate { layoutMenu.Checked = layoutOptions.Expanded; };
            sign.DropDownItems.Add(Command("标牌配色…", OpenColors)); sign.DropDownItems.Add(Command("油漆 / 磨损…", OpenPaint));
            xplane.DropDownItems.Add(xplaneMode); xplane.DropDownItems.Add(xplaneSeparator); xplane.DropDownItems.Add(polSettings);
            xplaneMode.CheckedChanged += delegate
            {
                if (workspace.XPlane == xplaneMode.Checked) return;
                bool enabled = xplaneMode.Checked; CommitEdits(); workspace.XPlane = enabled; RefreshAtlas();
            };
            menu.Items.AddRange(new ToolStripItem[] { file, edit, view, sign, xplane });
            foreach (ToolStripMenuItem root in menu.Items) { root.Padding = new Padding(10, 4, 10, 4); root.DropDown.Renderer = menu.Renderer; }
            slotMenu.Renderer = menu.Renderer;
            return menu;
        }
        void OpenColors()
        {
            BeginEdit();
            try
            {
                using (PaletteForm dialog = new PaletteForm(new ColorPreset { Name = "自定义", Common = workspace.Colors, Types = workspace.TypeColors, BaseColors = workspace.BaseColors },
                    value => { workspace.Colors = value.Common; workspace.TypeColors = value.Types; workspace.BaseColors = value.BaseColors; RefreshAtlas(); })) dialog.ShowDialog(this);
            }
            finally { EndEdit(); }
        }
        void OpenPaint()
        {
            CommitEdits(); using (PaintForm dialog = new PaintForm(engine, workspace))
                if (dialog.ShowDialog(this) == DialogResult.OK) RefreshAtlas();
        }
        void OpenPol()
        {
            if (!workspace.XPlane) return;
            CommitEdits(); using (PolExportForm dialog = new PolExportForm(engine, workspace)) dialog.ShowDialog(this);
            RefreshAtlas();
        }
        void SyncExportMode()
        {
            xplaneMode.Checked = workspace.XPlane;
            pol.Available = svg.Available = polSettings.Available = xplaneSeparator.Available = workspace.XPlane;
            bool canExport = layout != null && layout.CanExport;
            png.Enabled = exportPrimary.Enabled = canExport;
            pol.Enabled = svg.Enabled = polSettings.Enabled = workspace.XPlane && canExport;
            exportPrimary.Text = workspace.XPlane ? "PNG + POL…" : "导出 PNG…";
        }
        int PaintPreviewSize() { return Math.Min(4096, Math.Max(1440, (int)Math.Ceiling(Math.Max(workspace.Width, workspace.Height) * Math.Min(1, preview.Zoom)))); }
        void UpdateZoom()
        {
            float zoom = preview.Zoom;
            zoomLabel.Text = (zoom * 100).ToString("0.#") + "%";
            zoomLabel.AccessibleDescription = preview.IsFit ? "适合窗口" : "自定义缩放";
            zoomOut.Enabled = zoom > PreviewViewport.MinimumZoom; zoomIn.Enabled = zoom < PreviewViewport.MaximumZoom;
            if (layout != null && workspace.Paint.Active)
            {
                int expected = Math.Min(Math.Max(workspace.Width, workspace.Height), PaintPreviewSize());
                if (preview.Painted == null || Math.Max(preview.Painted.Width, preview.Painted.Height) != expected) { paintTimer.Stop(); paintTimer.Start(); }
            }
        }
        void ProjectTitle() { Text = "拼图编辑器 · " + (string.IsNullOrEmpty(workspace.ProjectPath) ? "未命名工程" : Path.GetFileName(workspace.ProjectPath)) + " · Airport Sign Studio 3.20"; }
        void CommitEdits() { ValidateChildren(); foreach (DataGridView grid in editor.Controls.OfType<DataGridView>()) grid.EndEdit(); }
        void BeginEdit() { CommitEdits(); history.Begin(preview.Selection); }
        void EndEdit() { history.End(preview.Selection); UpdateHistoryButtons(); }
        void UpdateHistoryButtons() { undo.Enabled = history.CanUndo; redo.Enabled = history.CanRedo; }
        void SyncWorkspace()
        {
            updating = true;
            rows.Value = workspace.Rows; columns.Value = workspace.Columns; width.Value = workspace.Width; height.Value = workspace.Height;
            gap.Value = workspace.Gap; padding.Value = workspace.Padding; target.Value = workspace.TargetTextPixels;
            updating = false; SyncPreset(); Select(workspace.Selected);
        }
        void UndoRedo(bool forward)
        {
            preview.FinishDrag(false); CommitEdits(); history.Record(preview.Selection);
            if (!(forward ? history.Redo() : history.Undo())) return;
            int[] selection = history.Selection;
            SyncWorkspace(); preview.Selection.Clear(); preview.Selection.UnionWith(selection); UpdateSelection();
            RefreshAtlas(); preview.Focus();
        }
        void ClearSign()
        {
            CommitEdits(); workspace.Slots[workspace.Selected] = null;
            Select(workspace.Selected); RefreshAtlas();
        }
        void CopySign()
        {
            CommitEdits();
            Settings source = workspace.Slots[workspace.Selected];
            if (source == null) return;
            copied = source.Copy(); paste.Enabled = true;
            status.Text = "已复制 · 区域 " + (workspace.Selected + 1);
        }
        void PasteSign()
        {
            if (copied == null) return;
            CommitEdits();
            workspace.Slots[workspace.Selected] = copied.Copy();
            Select(workspace.Selected); RefreshAtlas();
            status.Text = "已粘贴 · 区域 " + (workspace.Selected + 1);
        }
        void SaveProject(bool saveAs)
        {
            CommitEdits();
            if (ProjectDialogs.Save(this, workspace, saveAs)) { ProjectTitle(); status.Text = "工程已保存：" + workspace.ProjectPath; }
        }
        void OpenProject()
        {
            using (OpenFileDialog open = new OpenFileDialog { Filter = "机场标志工程|*.assproj", Title = "读取拼图工程" })
            {
                if (open.ShowDialog(this) != DialogResult.OK) return;
                AtlasWorkspace loaded;
                try { loaded = ProjectStore.Decode(File.ReadAllText(open.FileName)); loaded.ProjectPath = open.FileName; }
                catch (Exception error) { MessageBox.Show(this, error.Message, "工程读取失败，当前工程未改变"); return; }
                CommitEdits(); if (!ProjectDialogs.ConfirmSave(this, workspace)) return;
                LoadWorkspace(loaded);
                status.Text = "已读取：" + open.FileName;
            }
        }
        void LoadWorkspace(AtlasWorkspace loaded)
        {
            ProjectStore.Apply(workspace, loaded);
            multiple.Checked = false; copied = null; paste.Enabled = false;
            preview.FitView(); SyncWorkspace(); history.Reset(preview.Selection); RefreshAtlas(); ProjectTitle();
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Z)) { UndoRedo(false); return true; }
            if (keyData == (Keys.Control | Keys.Y)) { UndoRedo(true); return true; }
            if (preview.ContainsFocus)
            {
                if (keyData == (Keys.Control | Keys.C)) { CopySign(); return true; }
                if (keyData == (Keys.Control | Keys.V)) { PasteSign(); return true; }
            }
            if (keyData == (Keys.Control | Keys.S)) { SaveProject(false); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.S)) { SaveProject(true); return true; }
            if (keyData == (Keys.Control | Keys.O)) { OpenProject(); return true; }
            if (keyData == (Keys.Control | Keys.D0) || keyData == (Keys.Control | Keys.NumPad0)) { preview.FitView(); return true; }
            if (keyData == (Keys.Control | Keys.D1) || keyData == (Keys.Control | Keys.NumPad1)) { preview.SetZoom(1); return true; }
            if (keyData == (Keys.Control | Keys.Oemplus) || keyData == (Keys.Control | Keys.Shift | Keys.Oemplus) || keyData == (Keys.Control | Keys.Add)) { preview.ZoomBy(1.2f); return true; }
            if (keyData == (Keys.Control | Keys.OemMinus) || keyData == (Keys.Control | Keys.Subtract)) { preview.ZoomBy(1 / 1.2f); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        static FlowLayoutPanel Row(int y)
        {
            return new FlowLayoutPanel { Location = new Point(0, y), Width = 1200, Height = 40, WrapContents = false };
        }
        static void Number(FlowLayoutPanel panel, string label, NumericUpDown input, int min, int max, int value, int size)
        {
            panel.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(panel.Controls.Count == 0 ? 0 : 10, 6, 7, 0) });
            input.Minimum = min; input.Maximum = max; input.Value = value; input.Width = size; input.AccessibleName = "拼图：" + label;
            panel.Controls.Add(StudioTheme.FrameInput(input, 34));
        }

        void Select(int index)
        {
            workspace.Selected = index;
            preview.Selection.Clear(); preview.Selection.Add(index); UpdateSelection();
        }
        void SelectPreview(int index, bool toggle, bool range)
        {
            if (range)
            {
                RectangleF bounds = RectangleF.Union(workspace.Regions[workspace.Selected].Bounds, workspace.Regions[index].Bounds);
                preview.Selection.Clear();
                foreach (AtlasRegion region in workspace.Regions.Where(r => r != null))
                    if (bounds.Contains(region.Bounds)) preview.Selection.Add(region.Index);
            }
            else if (toggle)
            {
                if (!preview.Selection.Contains(index)) preview.Selection.Add(index);
                else if (preview.Selection.Count > 1) preview.Selection.Remove(index);
                if (!preview.Selection.Contains(workspace.Selected)) workspace.Selected = preview.Selection.Min();
            }
            else { Select(index); history.Record(preview.Selection); return; }
            UpdateSelection(); history.Record(preview.Selection);
        }
        void UpdateSelection()
        {
            AtlasRegion region = workspace.Regions[workspace.Selected];
            selectedTitle.Text = "区域 " + (workspace.Selected + 1) + (region.CellCount > 1 ? " · 合并 " + region.CellCount + " 格" : "") +
                (preview.Selection.Count > 1 ? " · 已选 " + preview.Selection.Count + " 区" : "");
            merge.Enabled = preview.Selection.Count > 1; split.Enabled = preview.Selection.Count == 1 && region.Children != null;
            editor.LoadSlot(workspace.Slots[workspace.Selected]); copy.Enabled = workspace.Slots[workspace.Selected] != null; UpdateDetails(); preview.SelectionChanged(); preview.Invalidate();
        }
        void ResizeGrid()
        {
            if (updating) return;
            try { workspace.Resize((int)rows.Value, (int)columns.Value); Select(workspace.Selected); RefreshAtlas(); }
            catch (ArgumentException error)
            {
                updating = true; rows.Value = workspace.Rows; columns.Value = workspace.Columns; updating = false;
                status.Text = error.Message;
            }
        }
        void SyncPreset()
        {
            updating = true;
            int index = Array.FindIndex(Presets, s => s.Width == workspace.Width && s.Height == workspace.Height);
            preset.SelectedIndex = index < 0 ? Presets.Length : index; updating = false;
        }
        void SetCanvas(Size size)
        {
            updating = true; width.Value = size.Width; height.Value = size.Height; updating = false; ChangeCanvas();
        }
        void ChangeCanvas()
        {
            if (updating) return;
            workspace.Width = (int)width.Value; workspace.Height = (int)height.Value; SyncPreset(); RefreshAtlas();
        }

        void RefreshAtlas()
        {
            history.Record(preview.Selection); UpdateHistoryButtons();
            copy.Enabled = workspace.Slots[workspace.Selected] != null;
            paintTimer.Stop(); if (preview.Painted != null) { preview.Painted.Dispose(); preview.Painted = null; }
            preview.CurrentLayout = null; if (layout != null) { layout.Dispose(); layout = null; }
            try
            {
                layout = AtlasLayout.Build(engine, workspace); preview.CurrentLayout = layout;
                suggested = layout.Recommend(workspace); recommend.Enabled = !suggested.IsEmpty && (suggested.Width != workspace.Width || suggested.Height != workspace.Height);
                int low = layout.Slots.Count(s => s.Occupied && s.Error.Length == 0 && s.SmallestTextPixels < workspace.TargetTextPixels);
                int errors = layout.Slots.Count(s => s.Error.Length > 0);
                density.Text = errors > 0 ? errors + " 格输入有误" : workspace.Filled == 0 ? "" : low == 0 ? "字高达标" : low + " 格字高不足";
                density.ForeColor = errors > 0 ? Color.Firebrick : low > 0 ? Color.FromArgb(173, 98, 17) : Color.DimGray;
                status.Text = workspace.Filled + " / " + workspace.RegionCount + " 区已设置 · " + workspace.Width + " × " + workspace.Height + " px" +
                    (suggested.IsEmpty ? (workspace.Filled > 0 && errors == 0 ? " · 超出 8K" : "") : " · 建议 " + suggested.Width + " × " + suggested.Height + " px");
                layoutOptions.Summary = workspace.Columns + " 列 × " + workspace.Rows + " 行    ·    " + workspace.Width + " × " + workspace.Height + " px";
                UpdateDetails();
                if (workspace.Paint.Active) paintTimer.Start();
            }
            catch (ArgumentException error)
            {
                recommend.Enabled = false; status.Text = error.Message; density.Text = "请调整画布"; detail.Text = "";
            }
            SyncExportMode(); UpdateZoom(); preview.Invalidate();
        }
        void UpdateDetails()
        {
            if (layout == null || workspace.Selected >= layout.Slots.Count) return;
            AtlasSlotInfo slot = layout.Slots[workspace.Selected];
            if (!slot.Occupied) { detail.Text = "空白 · " + slot.Cell.Width.ToString("0") + " × " + slot.Cell.Height.ToString("0") + " px"; return; }
            if (slot.Error.Length > 0) { detail.Text = "槽位 " + (workspace.Selected + 1) + "：" + slot.Error; return; }
            string physicalSize = workspace.Slots[workspace.Selected].AspectRatio == null ? "" :
                " · 尺寸 " + slot.PhysicalWidth.ToString("0.##") + " × " + slot.PhysicalHeight.ToString("0.##") + " mm";
            detail.Text = "槽位 " + (workspace.Selected + 1) + " · " + slot.Caption + physicalSize + " · 图案 " + slot.Image.Width.ToString("0") + " × " + slot.Image.Height.ToString("0") + " px · " + (slot.Scale * 1000).ToString("0") + " px/m\n" +
                "主字高 " + slot.CharacterPixels.ToString("0") + " px · 最小字高 " + slot.SmallestTextPixels.ToString("0") + " px · 参考 " + workspace.TargetTextPixels + " px";
        }
        void Export(bool asSvg)
        {
            if (asSvg && !workspace.XPlane) return;
            CommitEdits();
            if (layout == null || !layout.CanExport) return;
            using (SaveFileDialog save = new SaveFileDialog { Filter = asSvg ? "SVG 矢量文件|*.svg" : "PNG 透明贴图|*.png", DefaultExt = asSvg ? "svg" : "png", AddExtension = true,
                FileName = "Mixed_atlas_" + workspace.Columns + "x" + workspace.Rows + "_" + workspace.Width + "x" + workspace.Height + (asSvg ? ".svg" : ".png") })
            {
                if (save.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    UseWaitCursor = true;
                    if (asSvg) File.WriteAllText(save.FileName, Exporter.Svg(layout.Document, true), new UTF8Encoding(false));
                    else using (Bitmap bitmap = PaintRenderer.Raster(layout, workspace.Paint, 0)) bitmap.Save(save.FileName, ImageFormat.Png);
                    status.Text = "已保存：" + save.FileName;
                }
                catch (Exception error) { MessageBox.Show(this, error.Message, "保存失败"); }
                finally { UseWaitCursor = false; }
            }
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            CommitEdits();
            if (Owner == null && !ProjectDialogs.ConfirmSave(this, workspace)) e.Cancel = true;
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { preview.ViewChanged = null; paintTimer.Dispose(); slotMenu.Dispose(); if (preview.Painted != null) preview.Painted.Dispose(); if (layout != null) layout.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
