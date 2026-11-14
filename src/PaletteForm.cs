using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AirportSignStudio
{
    public sealed class PaletteForm : DpiForm
    {
        readonly ColorPreset original;
        readonly Action<ColorPreset> livePreview;
        readonly Button[] swatches = new Button[4];
        readonly Label presetName = new Label();
        ColorPreset working;
        public PaletteForm(ColorPreset current, Action<ColorPreset> changed)
        {
            original = current.Copy(); working = current.Copy(); livePreview = changed;
            Text = "标牌配色"; ClientSize = new Size(512, 320); MinimumSize = new Size(528, 359); StartPosition = FormStartPosition.CenterParent;
            FlowLayoutPanel header = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(16), WrapContents = false };
            Button load = new StudioButton { Text = "读取预设…", Size = new Size(112, 32) }, save = new StudioButton { Text = "保存预设…", Size = new Size(112, 32) };
            load.Click += delegate { LoadPreset(); }; save.Click += delegate { SavePreset(); };
            presetName.AutoSize = true; presetName.ForeColor = StudioTheme.Muted; presetName.Margin = new Padding(12, 10, 0, 0);
            header.Controls.Add(load); header.Controls.Add(save); header.Controls.Add(presetName);
            FlowLayoutPanel fields = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16, 4, 16, 4), FlowDirection = FlowDirection.TopDown, WrapContents = false };
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                FlowLayoutPanel row = new FlowLayoutPanel { Width = 468, Height = 42, WrapContents = false };
                row.Controls.Add(new Label { Text = BaseColorSettings.Names[i], Width = 100, Height = 30, TextAlign = ContentAlignment.MiddleLeft });
                swatches[i] = new StudioButton { Width = 190, Height = 32, Swatch = true, AccessibleName = BaseColorSettings.Names[i] + "配色" };
                swatches[i].FlatAppearance.BorderColor = StudioTheme.Line;
                swatches[i].Click += delegate
                {
                    string before = working.BaseColors.Get(index);
                    using (ColorPicker picker = new ColorPicker(swatches[index].BackColor, BaseColorSettings.Names[index], color => SetColor(index, ColorPicker.Hex(color))))
                        if (picker.ShowDialog(this) != DialogResult.OK) SetColor(index, before);
                };
                Button reset = new StudioButton { Text = "重置", Size = new Size(76, 32), AccessibleName = "重置" + BaseColorSettings.Names[i] };
                reset.Click += delegate { SetColor(index, ""); };
                row.Controls.Add(swatches[i]); row.Controls.Add(reset); fields.Controls.Add(row);
            }
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(16, 8, 0, 0), WrapContents = false };
            Button apply = new StudioButton { Text = "应用配色", Primary = true, Size = new Size(110, 32) };
            Button defaults = new StudioButton { Text = "恢复默认", Size = new Size(110, 32) };
            Button cancel = new StudioButton { Text = "取消", Size = new Size(90, 32), DialogResult = DialogResult.Cancel };
            apply.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            defaults.Click += delegate { ApplyPreset(new ColorPreset { Name = "默认" }); };
            actions.Controls.Add(apply); actions.Controls.Add(defaults); actions.Controls.Add(cancel); CancelButton = cancel;
            Controls.Add(fields); Controls.Add(actions); Controls.Add(header); RefreshColors(); CompleteLayout();
        }
        void SetColor(int index, string value)
        {
            BaseColorSettings colors = working.BaseColors.Copy(); colors.Set(index, value); colors.Validate();
            working.BaseColors = colors; working.Name = "自定义"; Publish(); RefreshColors();
        }
        void Publish() { if (livePreview != null) livePreview(working.Copy()); }
        void RefreshColors()
        {
            for (int i = 0; i < swatches.Length; i++)
            {
                string value = working.BaseColors.Get(i);
                Color color = value.Length > 0 ? ColorTranslator.FromHtml(value) : BaseColorSettings.Defaults[i];
                swatches[i].Text = ColorPicker.Hex(color); swatches[i].BackColor = color;
                swatches[i].ForeColor = color.GetBrightness() < 0.5f ? Color.White : Color.Black;
            }
            presetName.Text = working.Name;
        }
        void ApplyPreset(ColorPreset preset)
        {
            ColorPresetStore.Encode(preset); working = preset.Copy(); Publish(); RefreshColors();
        }
        void LoadPreset()
        {
            using (OpenFileDialog dialog = new OpenFileDialog { Filter = "配色预设|*.asscolors", Title = "读取配色预设" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { ApplyPreset(ColorPresetStore.Decode(File.ReadAllText(dialog.FileName))); }
                catch (Exception error) { MessageBox.Show(this, error.Message, "读取失败"); }
            }
        }
        void SavePreset()
        {
            using (SaveFileDialog dialog = new SaveFileDialog { Filter = "配色预设|*.asscolors", DefaultExt = "asscolors", AddExtension = true, FileName = "机场配色.asscolors", Title = "保存配色预设" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    ColorPreset preset = working.Copy(); preset.Version = 2; preset.Name = Path.GetFileNameWithoutExtension(dialog.FileName);
                    ColorPresetStore.Save(preset, dialog.FileName); working.Name = preset.Name; presetName.Text = preset.Name;
                }
                catch (Exception error) { MessageBox.Show(this, error.Message, "保存失败"); }
            }
        }
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e); if (Owner == null) return;
            Rectangle area = Screen.FromControl(Owner).WorkingArea;
            Location = new Point(Math.Max(area.Left, Math.Min(Owner.Left + 16, area.Right - Width)), Math.Max(area.Top, Math.Min(Owner.Top + 80, area.Bottom - Height)));
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (DialogResult != DialogResult.OK && livePreview != null) livePreview(original.Copy());
            base.OnFormClosed(e);
        }
    }
}
