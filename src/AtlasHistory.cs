using System.Collections.Generic;
using System.Linq;

namespace AirportSignStudio
{
    public sealed class AtlasHistory
    {
        sealed class Snapshot
        {
            public AtlasWorkspace State;
            public string Text;
            public int[] Selection;
        }
        readonly AtlasWorkspace workspace;
        readonly List<Snapshot> undo = new List<Snapshot>(), redo = new List<Snapshot>();
        Snapshot current;
        int grouping;
        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }
        public int[] Selection { get { return current.Selection; } }
        public AtlasHistory(AtlasWorkspace state) { workspace = state; Reset(new[] { state.Selected }); }

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
            return new Snapshot { State = Copy(workspace), Text = ProjectStore.Encode(workspace), Selection = selection.ToArray() };
        }
        public void Reset(IEnumerable<int> selection)
        {
            undo.Clear(); redo.Clear(); grouping = 0; current = Capture(selection);
        }
        public void Record(IEnumerable<int> selection)
        {
            if (grouping > 0) return;
            Snapshot next = Capture(selection);
            if (current.Text != next.Text)
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
            string path = workspace.ProjectPath, saved = workspace.SavedProject;
            ProjectStore.Apply(workspace, Copy(current.State));
            // Undo changes the document, never the last on-disk save or its path.
            workspace.ProjectPath = path; workspace.SavedProject = saved;
            return true;
        }
    }
}
