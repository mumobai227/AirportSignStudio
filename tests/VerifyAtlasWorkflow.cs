using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using AirportSignStudio;

static class VerifyAtlasWorkflow
{
    static int checks;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    delegate bool EnumWindow(IntPtr window, IntPtr data);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool EnumThreadWindows(uint thread, EnumWindow callback, IntPtr data);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] static extern IntPtr GetDlgItem(IntPtr window, int id);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
    static void Assert(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    static object Field(object owner, string name) { return owner.GetType().GetField(name, Private).GetValue(owner); }
    static object Call(object owner, string name, params object[] args) { return owner.GetType().GetMethods(Private).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(owner, args); }
    static void Click(Button button) { typeof(Button).GetMethod("OnClick", Private).Invoke(button, new object[] { EventArgs.Empty }); }
    static void Click(ToolStripMenuItem item) { item.PerformClick(); }
    static string Snapshot(Settings settings) { AtlasWorkspace w = new AtlasWorkspace(); w.Slots[0] = settings; return ProjectStore.Encode(w); }
    static bool Command(AtlasForm form, Keys key) { return (bool)Call(form, "ProcessCmdKey", new Message(), key); }
    static IEnumerable<Control> Tree(Control root) { foreach (Control child in root.Controls) { yield return child; foreach (Control nested in Tree(child)) yield return nested; } }
    static void Shortcut(Control target, Keys key)
    {
        target.Focus();
        // Exercise the focused control's command route with explicit modifiers;
        // desktop keyboard activity must not change this test's synthetic key.
        Message message = Message.Create(target.Handle, 0x0100, new IntPtr((int)key), IntPtr.Zero);
        Assert((bool)Call(target, "ProcessCmdKey", message, Keys.Control | key), "Focused control handles Ctrl+" + key);
    }
    static void ShowOffscreen(Form form)
    {
        form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-30000, -30000);
        form.Show(); Application.DoEvents();
    }
    // Answer only a modal dialog owned by this test form, never another application's window.
    static bool CloseWithAnswer(AtlasForm form, int answer)
    {
        bool answered = false;
        using (Timer timer = new Timer { Interval = 50 })
        {
            timer.Tick += delegate
            {
                EnumThreadWindows(GetCurrentThreadId(), (window, data) =>
                {
                    if (GetWindow(window, 4) != form.Handle || GetDlgItem(window, answer) == IntPtr.Zero) return true;
                    answered = true; timer.Stop(); PostMessage(window, 0x0111, new IntPtr(answer), GetDlgItem(window, answer)); return false;
                }, IntPtr.Zero);
            };
            timer.Start(); form.Close(); timer.Stop();
        }
        return answered;
    }
    public static void Run(SignEngine engine, string directory)
    {
        AtlasWorkspace w = new AtlasWorkspace();
        using (AtlasForm form = new AtlasForm(engine, w))
        {
            ShowOffscreen(form);
            AtlasPreview preview = (AtlasPreview)Field(form, "preview");
            SlotEditor editor = (SlotEditor)Field(form, "editor");
            Button copy = (Button)Field(form, "copy"), paste = (Button)Field(form, "paste");
            ContextMenuStrip menu = preview.ContextMenuStrip;
            Assert(!copy.Enabled && !paste.Enabled, "Empty project disables copy and paste");
            string empty = ProjectStore.Encode(w); Call(form, "PasteSign");
            Assert(ProjectStore.Encode(w) == empty, "Paste without a copied sign is a no-op");
            foreach (AssetKind kind in Enum.GetValues(typeof(AssetKind)))
            {
                Settings source = new Settings { Kind = kind, Text = "13", Arrow = "←", ArrowAbove = true, Border = false,
                    CharacterHeight = kind == AssetKind.TaxiSign ? 400 : kind == AssetKind.StandSign ? 800 : 2000,
                    GroundGap = 0.2f, Coordinates = true, Latitude = "N 52 30.2", Longitude = "E 114 25.1",
                    Width = 12000, Height = 10000, OtherStyle = "HP", BlackBackground = false,
                    Colors = new ColorSettings { Foreground = "#DFAE34", Background = "#252321", Border = "#FCFCF9" } };
                source.Panels.Add(new SignPanel { Text = "B2", Arrow = "←", Width = 3000 });
                source.Panels.Add(new SignPanel { Kind = "位置", Text = "C3", Arrow = "无", Width = 2500 });
                w.Slots[0] = source; Call(form, "Select", 0); Call(form, "RefreshAtlas");
                if (kind == AssetKind.TaxiSign)
                {
                    DataGridView grid = editor.Controls.OfType<DataGridView>().Single();
                    grid.CurrentCell = grid.Rows[0].Cells[1]; grid.BeginEdit(true);
                    ((TextBox)grid.EditingControl).Text = "B7";
                    Assert(grid.IsCurrentCellInEditMode, "Copy test starts with an uncommitted table edit");
                    Click(copy);
                    Assert(!grid.IsCurrentCellInEditMode && source.Panels[0].Text == "B7", "Copy commits the current panel edit");
                }
                else Click(copy);
                string snapshot = Snapshot(source);
                source.Text = "99"; source.Colors.Foreground = "#FF0000"; source.Panels[0].Text = "Z9";
                Call(form, "Select", 1); Click(paste);
                Assert(Snapshot(w.Slots[1]) == snapshot, "Full sign snapshot copied for " + kind);
                Call(form, "Select", 2); ((ToolStripMenuItem)menu.Items[1]).PerformClick();
                w.Slots[1].Text = "88"; w.Slots[1].Colors.Border = "#000000"; w.Slots[1].Panels[0].Width = 6000;
                Assert(Snapshot(w.Slots[2]) == snapshot, "Repeated pastes have independent nested settings for " + kind);
            }
            Call(form, "Select", 3); Call(form, "CopySign");
            Assert(!copy.Enabled && paste.Enabled, "Empty slot keeps the previous copied sign");
            string buffered = Snapshot((Settings)Field(form, "copied"));
            Assert(preview.Focus(), "Preview accepts keyboard focus");
            Assert(Command(form, Keys.Control | Keys.V), "Preview handles Ctrl+V");
            Assert(Snapshot(w.Slots[3]) == buffered, "Keyboard paste reaches the selected empty slot");
            preview.Focus(); Assert(Command(form, Keys.Control | Keys.C), "Preview handles Ctrl+C");
            TextBox input = editor.Controls.Cast<Control>().SelectMany(c => c.Controls.Cast<Control>())
                .SelectMany(c => c.Controls.Cast<Control>()).OfType<TextBox>().First();
            input.Focus(); Assert(input.ContainsFocus, "Text field receives focus");
            string beforeTextKeys = ProjectStore.Encode(w);
            Assert(!Command(form, Keys.Control | Keys.C) && !Command(form, Keys.Control | Keys.V), "Form leaves text copy/paste shortcuts to the input");
            Assert(ProjectStore.Encode(w) == beforeTextKeys, "Text shortcuts do not replace a whole sign");

            w.Selected = 4; w.Slots[4] = new Settings { Text = "D4" }; w.Slots[5] = new Settings { Text = "E5" };
            w.Merge(new[] { 4, 5 }); w.MoveDivider(w.Divider(4, AtlasEdge.Left), 0.28f);
            w.Width = 4096; w.PolBoundary = PolBoundaryMode.Cell; w.PolBleed = 5; w.PolLayer = "taxiways";
            w.Colors.Background = "#342F21"; w.Paint.Seed = 87;
            Call(form, "Select", 0); Call(form, "RefreshAtlas");
            RectangleF view = (RectangleF)Call(preview, "View"), cell = preview.CurrentLayout.Slots[4].Cell;
            float scale = view.Width / w.Width;
            Call(preview, "OnMouseDown", new MouseEventArgs(MouseButtons.Right, 1,
                (int)(view.X + (cell.X + cell.Width / 2) * scale), (int)(view.Y + (cell.Y + cell.Height / 2) * scale), 0));
            Assert(w.Selected == 4 && preview.Selection.SetEquals(new[] { 4 }), "Right-click selects the hit merged region");
            Call(menu, "OnOpening", new CancelEventArgs());
            Assert(menu.Items[0].Enabled && menu.Items[1].Enabled, "Context menu enables valid copy/paste actions");
            AtlasWorkspace expected = ProjectStore.Decode(ProjectStore.Encode(w));
            expected.Slots[4] = ((Settings)Field(form, "copied")).Copy();
            ((ToolStripMenuItem)menu.Items[1]).PerformClick();
            Assert(ProjectStore.Encode(w) == ProjectStore.Encode(expected), "Paste preserves merged bounds, hidden contents, resolution, POL, shared colors and paint");
            ((ToolStripMenuItem)menu.Items[0]).PerformClick();
            Assert(Snapshot((Settings)Field(form, "copied")) == Snapshot(w.Slots[4]), "Context menu copy uses the hit sign");
            string saved = ProjectStore.Encode(w);
            Assert(CloseWithAnswer(form, 2), "Root atlas prompts to save on close");
            Assert(!form.IsDisposed && ProjectStore.Encode(w) == saved, "Cancel close keeps the editable project intact");
            w.ProjectPath = Path.Combine(directory, "atlas-clipboard.assproj");
            Assert(CloseWithAnswer(form, 6), "Root atlas supports saving on close");
            Assert(form.IsDisposed && File.ReadAllText(w.ProjectPath) == saved, "Save on close writes the full project and exits");
        }
        using (AtlasForm clean = new AtlasForm(engine, new AtlasWorkspace()))
        {
            ShowOffscreen(clean);
            Assert(!CloseWithAnswer(clean, 2) && clean.IsDisposed, "Untouched startup project closes without a save prompt");
        }
        HistoryChecks(engine, directory);
        string result = "PASS: " + checks + " atlas workflow assertions: copy/paste, undo/redo, native shortcut routing, nested merges, boundary drag grouping/cancel, drafts, save checkpoints, history branches/reset/limit, and close/cancel/save. Native test windows only.\n";
        File.WriteAllText(Path.Combine(directory, "atlas-workflow-results.txt"), result); Console.WriteLine(result);
    }
    static void HistoryChecks(SignEngine engine, string directory)
    {
        AtlasWorkspace state = new AtlasWorkspace(); state.Slots[0] = new Settings();
        AtlasHistory history = new AtlasHistory(state); string original = ProjectStore.Encode(state);
        state.Slots[0].Panels.Add(new SignPanel { Text = "", Width = -1 }); state.Slots[0].Colors.Foreground = "#102030";
        history.Record(new[] { 0 }); string draft = ProjectStore.Encode(state);
        Assert(history.Undo() && ProjectStore.Encode(state) == original, "History restores before an incomplete draft");
        Assert(history.Redo() && ProjectStore.Encode(state) == draft, "History restores incomplete drafts without file-load validation");
        ProjectStore.Save(state, Path.Combine(directory, "history-checkpoint.assproj"));
        string savedPath = state.ProjectPath;
        Assert(history.Undo() && state.SavedProject == draft && state.ProjectPath == savedPath, "Undo preserves actual save checkpoint and path");
        Assert(ProjectStore.Encode(state) != state.SavedProject, "Undo away from save is dirty");
        Assert(history.Redo() && ProjectStore.Encode(state) == state.SavedProject, "Redo to save is clean");
        history.Undo(); state.Slots[0].Text = "B2"; history.Record(new[] { 0 });
        Assert(!history.CanRedo, "A new edit clears the redo branch");
        Assert(history.Undo() && ProjectStore.Encode(state) == original, "Restored snapshots are independent from later edits");
        history.Reset(new[] { 0 });
        for (int i = 0; i < 120; i++) { state.Slots[0].Text = "A" + i; history.Record(new[] { 0 }); }
        int steps = 0; while (history.Undo()) steps++;
        Assert(steps == 100 && state.Slots[0].Text == "A19", "Undo retains the most recent 100 edits");
        steps = 0; while (history.Redo()) steps++;
        Assert(steps == 100 && state.Slots[0].Text == "A119", "All retained edits can be redone");

        AtlasWorkspace w = new AtlasWorkspace();
        w.Slots[0] = new Settings { Text = "A1" }; w.Slots[1] = new Settings { Text = "B2" }; w.Slots[2] = new Settings { Text = "C3" };
        using (AtlasForm form = new AtlasForm(engine, w))
        {
            ShowOffscreen(form);
            AtlasPreview preview = (AtlasPreview)Field(form, "preview");
            ToolStripMenuItem undo = (ToolStripMenuItem)Field(form, "undo"), redo = (ToolStripMenuItem)Field(form, "redo");
            Assert(!undo.Enabled && !redo.Enabled, "History starts empty");
            Call(form, "CopySign"); Call(form, "SelectPreview", 1, false, false);
            string before = ProjectStore.Encode(w); Call(form, "PasteSign"); string after = ProjectStore.Encode(w);
            Assert(undo.Enabled && !redo.Enabled, "Paste enables Undo");
            Shortcut(preview, Keys.Z);
            Assert(ProjectStore.Encode(w) == before && w.Selected == 1, "Native Ctrl+Z undoes paste and retains its destination");
            Shortcut(preview, Keys.Y);
            Assert(ProjectStore.Encode(w) == after, "Native Ctrl+Y redoes paste");
            Click(Tree(form).OfType<Button>().Single(b => b.Text == "清空此格"));
            Assert(w.Slots[1] == null, "Clear button clears the target");
            Click(undo); Assert(ProjectStore.Encode(w) == after, "Undo button restores cleared content");
            Click(redo); Assert(w.Slots[1] == null, "Redo button clears again");
            Click(undo);
            TextBox text = Tree(form).OfType<TextBox>().First(c => c.AccessibleName == "槽位：内容");
            text.Text = "D4"; string typed = ProjectStore.Encode(w);
            Shortcut(text, Keys.Z);
            Assert(ProjectStore.Encode(w) == after, "Text field routes Ctrl+Z to document history");
            text = Tree(form).OfType<TextBox>().First(c => c.AccessibleName == "槽位：内容");
            Shortcut(text, Keys.Y);
            Assert(ProjectStore.Encode(w) == typed, "Text field routes Ctrl+Y to document history");

            Call(form, "SelectPreview", 0, false, false); Call(form, "SelectPreview", 1, true, false);
            string beforeMerge = ProjectStore.Encode(w); Click((Button)Field(form, "merge")); string firstMerge = ProjectStore.Encode(w);
            Call(form, "SelectPreview", 2, true, false); Click((Button)Field(form, "merge")); string nestedMerge = ProjectStore.Encode(w);
            Shortcut(preview, Keys.Z); Assert(ProjectStore.Encode(w) == firstMerge, "Undo restores inner merge and hidden contents");
            Shortcut(preview, Keys.Z); Assert(ProjectStore.Encode(w) == beforeMerge && preview.Selection.SetEquals(new[] { 0, 1 }), "Undo merge restores original regions and selection");
            Shortcut(preview, Keys.Y); Shortcut(preview, Keys.Y);
            Assert(ProjectStore.Encode(w) == nestedMerge, "Redo restores nested merge");
            Click((Button)Field(form, "split")); Assert(ProjectStore.Encode(w) == firstMerge, "Split restores nested children");
            Click(undo); Assert(ProjectStore.Encode(w) == nestedMerge, "Undo split restores nested merge");

            RectangleF view = (RectangleF)Call(preview, "View"), cell = preview.CurrentLayout.Slots[0].Cell;
            float scale = view.Width / w.Width;
            int x = (int)(view.X + (cell.X + cell.Width / 2) * scale), y = (int)(view.Y + (cell.Bottom + w.Gap / 2f) * scale);
            Call(preview, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
            Assert(Field(preview, "dragging") != null, "Boundary drag begins through the preview hit test");
            for (int distance = 3; distance <= 30; distance += 3) Call(preview, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, x, y + distance, 0));
            Call(preview, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, x, y + 30, 0));
            string dragged = ProjectStore.Encode(w); Assert(dragged != nestedMerge, "Dragging changes nested region bounds");
            Shortcut(preview, Keys.Z); Assert(ProjectStore.Encode(w) == nestedMerge, "Ten drag updates undo in one step with exact nested bounds");
            Shortcut(preview, Keys.Y); Assert(ProjectStore.Encode(w) == dragged, "Redo restores the final drag position");
            Shortcut(preview, Keys.Z);
            Call(preview, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
            Call(preview, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, x, y + 20, 0));
            Call(preview, "OnKeyDown", new KeyEventArgs(Keys.Escape));
            Assert(ProjectStore.Encode(w) == nestedMerge && redo.Enabled, "Escape restores exact pre-drag bounds without clearing redo");
            Call(preview, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
            Call(preview, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, x, y + 15, 0));
            preview.Capture = false; Assert(!redo.Enabled, "Lost capture commits the drag as a new branch");
            Shortcut(preview, Keys.Z); Assert(ProjectStore.Encode(w) == nestedMerge, "Interrupted drag remains undoable");

            Call(form, "BeginEdit"); w.Colors.Foreground = "#ABCDEF"; Call(form, "RefreshAtlas");
            w.Colors.Foreground = ""; Call(form, "RefreshAtlas"); Call(form, "EndEdit");
            Assert(ProjectStore.Encode(w) == nestedMerge && redo.Enabled, "Cancelled live colors add no history and preserve redo");
            NumericUpDown width = (NumericUpDown)Field(form, "width"); width.Value = 4096;
            Shortcut(width, Keys.Z); Assert(w.Width == 2048 && width.Value == 2048, "Undo synchronizes canvas inputs");
            Shortcut(width, Keys.Y); Assert(w.Width == 4096 && width.Value == 4096, "Redo synchronizes canvas inputs");
            string loaded = ProjectStore.Encode(w);
            Call(form, "LoadWorkspace", ProjectStore.Decode(loaded));
            Assert(!undo.Enabled && !redo.Enabled && ProjectStore.Encode(w) == loaded, "Loading a project resets both history stacks");
            Assert(!Tree(form).OfType<Label>().Any(c => c.Text.Contains("点击预览编辑") || c.Text.Contains("按比例缩放") || c.Text.Contains("选择标牌类型或")), "Main editor contains no removed instruction paragraphs");
            AtlasWorkspace taxi = new AtlasWorkspace(); taxi.Slots[0] = new Settings { Kind = AssetKind.TaxiSign, CharacterHeight = 300 };
            taxi.Slots[0].Panels.Add(new SignPanel { Text = "A1" }); Call(form, "LoadWorkspace", taxi);
            DataGridView grid = Tree(form).OfType<DataGridView>().Single();
            grid.CurrentCell = grid.Rows[0].Cells[1]; grid.BeginEdit(true);
            ((TextBox)grid.EditingControl).Text = "Z7";
            Shortcut(grid.EditingControl, Keys.Z);
            Assert(w.Slots[0].Panels[0].Text == "A1", "Ctrl+Z commits and undoes a live table edit");
            grid = Tree(form).OfType<DataGridView>().Single(); Shortcut(grid, Keys.Y);
            Assert(w.Slots[0].Panels[0].Text == "Z7", "Ctrl+Y restores the panel edit through table focus");
        }
    }
}
