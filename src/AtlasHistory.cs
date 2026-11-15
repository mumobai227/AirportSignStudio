using System.Collections.Generic;
using System.Linq;

namespace AirportSignStudio
{
    public sealed class AtlasHistory
    {
        sealed class Snapshot
        {
            public AtlasWorkspace[] Pages;
            public int Active;
            public string[] Text;
            public int[] Selection;
        }
        readonly AtlasBook book;
        readonly List<Snapshot> undo = new List<Snapshot>(), redo = new List<Snapshot>();
        Snapshot current;
        int grouping;
        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }
        public int[] Selection { get { return current.Selection; } }
        public AtlasHistory(AtlasWorkspace state) : this(new AtlasBook(state)) { }
        internal AtlasHistory(AtlasBook state) { book = state; Reset(new[] { state.Current.Selected }); }

        // Copy editable drafts directly: loading a file applies stricter validation.
        static AtlasWorkspace Copy(AtlasWorkspace source)
        {
            AtlasWorkspace copy = new AtlasWorkspace(); ProjectStore.Apply(copy, source);
            for (int i = 0; i < source.Slots.Length; i++)
            {
                copy.Slots[i] = source.Slots[i] == null ? null : source.Slots[i].Copy();
                copy.Regions[i] = source.Regions[i] == null ? null : RegionData.From(source.Regions[i]).ToRegion();
            }
            return copy;
        }
        Snapshot Capture(IEnumerable<int> selection)
        {
            Snapshot next = new Snapshot
            {
                Pages = new AtlasWorkspace[book.Pages.Count], Text = new string[book.Pages.Count],
                Active = book.Active, Selection = selection.ToArray()
            };
            for (int i = 0; i < book.Pages.Count; i++)
            {
                string text = ProjectStore.Encode(book.Pages[i]);
                if (current != null && i < current.Pages.Length && current.Text[i] == text && current.Pages[i].Selected == book.Pages[i].Selected)
                {
                    next.Text[i] = current.Text[i]; next.Pages[i] = current.Pages[i];
                }
                else
                {
                    next.Text[i] = text; next.Pages[i] = Copy(book.Pages[i]);
                }
            }
            return next;
        }
        public void Reset(IEnumerable<int> selection)
        {
            undo.Clear(); redo.Clear(); grouping = 0; current = null; current = Capture(selection);
        }
        public void Record(IEnumerable<int> selection)
        {
            if (grouping > 0) return;
            Snapshot next = Capture(selection);
            if (!current.Text.SequenceEqual(next.Text))
            {
                undo.Add(current); if (undo.Count > 100) undo.RemoveAt(0);
                redo.Clear();
            }
            current = next;
        }
        public void Begin(IEnumerable<int> selection) { Record(selection); grouping++; }
        public void End(IEnumerable<int> selection) { grouping--; Record(selection); }
        public bool Undo() { return Restore(undo, redo); }
        public bool Redo() { return Restore(redo, undo); }
        bool Restore(List<Snapshot> source, List<Snapshot> destination)
        {
            if (grouping > 0 || source.Count == 0) return false;
            destination.Add(current); current = source[source.Count - 1]; source.RemoveAt(source.Count - 1);
            book.Restore(current.Pages, current.Active, true);
            return true;
        }
    }
}
