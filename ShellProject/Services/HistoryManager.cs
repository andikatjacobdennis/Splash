using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using PaintClone.Models;

namespace PaintClone.Services
{
    /// <summary>
    /// Undo/redo built on whole-frame pixel snapshots. This is the simplest approach that is
    /// guaranteed to restore the *exact* previous document state (per spec section 5), which
    /// matters more here than the memory efficiency a delta/command-pattern history would give.
    /// One entry == one committed user operation (a whole stroke, a whole shape, a whole fill, etc.),
    /// never one entry per mouse-move event (spec section 53).
    ///
    /// Each entry also carries a short label ("Pencil", "Fill", "Resize Canvas", "Add Layer", ...)
    /// describing the action that produced that state, and the whole history can be read as one
    /// ordered list and jumped to directly - both needed for the History window.
    ///
    /// Every snapshot captures *every* layer, not just the active one. This is what makes layer
    /// structure changes (add/delete/reorder/merge - previously a documented "not part of undo"
    /// limitation) undoable: MainWindow now calls PushUndoState before those operations too, the
    /// same way every drawing tool already does before it mutates pixels, and restoring a snapshot
    /// naturally restores the whole layer list, not just one layer's content. For a single-layer
    /// document (by far the common case) this costs exactly what the old single-surface design
    /// did; the extra cost only appears once a document actually has more than one layer.
    /// </summary>
    public class HistoryManager
    {
        private class LayerSnap
        {
            public byte[] Pixels;
            public int Width, Height;
            public string Name;
            public bool Visible;

            /// <summary>Null for a raster layer; an independent deep copy for a text layer - without
            /// this, undoing past a text edit would restore the right pixels but leave every text
            /// layer's live TextLayerData pointing at whatever it last was, silently turning it into
            /// a plain (no longer re-editable) raster layer the moment any edit was ever undone.</summary>
            public TextLayerData Text;
        }

        private class Snapshot
        {
            public string Label;
            public List<LayerSnap> Layers;
            public int ActiveLayerIndex;
        }

        // Used stack-wise (push/pop from the end) but kept as a List so we can enumerate the whole
        // history in order and trim the oldest entry cheaply - Stack<T> supports neither well.
        private readonly List<Snapshot> _undo = new();
        private readonly List<Snapshot> _redo = new();
        private string _currentLabel = "New Document";
        private const int MaxDepth = 60;

        public event EventHandler StateChanged;

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        /// <summary>The index of the currently-active state within Labels below.</summary>
        public int CurrentIndex => _undo.Count;

        /// <summary>The full history in order, oldest first - one label per state, including the
        /// current one and any "future" (redo) states. Recomputed on demand; these lists are small
        /// (MaxDepth entries) so this isn't worth caching.</summary>
        public IReadOnlyList<string> Labels =>
            _undo.Select(s => s.Label)
                 .Append(_currentLabel)
                 .Concat(Enumerable.Reverse(_redo).Select(s => s.Label))
                 .ToList();

        /// <summary>Call BEFORE mutating the document, to push the pre-operation state onto the undo
        /// stack. label describes the action about to happen (shown in the History window once it
        /// completes and becomes "the current state"). Used both for pixel edits (by every drawing
        /// tool, via ctx.Document.Surface) and layer structure changes (add/delete/reorder/merge,
        /// called by MainWindow before delegating to PaintDocument) - both are just "the document
        /// changed" from this class's point of view.</summary>
        public void PushUndoState(PaintDocument doc, string label = "Edit")
        {
            _undo.Add(CaptureSnapshot(doc, _currentLabel));
            _currentLabel = label;
            if (_undo.Count > MaxDepth) _undo.RemoveAt(0);
            _redo.Clear();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Undo(PaintDocument doc)
        {
            if (!CanUndo) return;
            var current = CaptureSnapshot(doc, _currentLabel);
            var prev = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);
            _redo.Add(current);
            _currentLabel = prev.Label;
            ApplySnapshot(doc, prev);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Pops the most recent undo entry and restores it, like Undo, but does NOT push
        /// the current (about-to-be-discarded) state onto the redo stack. Used when a caller needs
        /// to cleanly roll back an operation that failed partway through - e.g. a plugin that threw
        /// an exception after partially modifying the bitmap - where that broken in-between state
        /// shouldn't be reachable afterward via Redo the way a normal Undo would leave it.</summary>
        public void DiscardLastPush(PaintDocument doc)
        {
            if (!CanUndo) return;
            var prev = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);
            _currentLabel = prev.Label;
            ApplySnapshot(doc, prev);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Redo(PaintDocument doc)
        {
            if (!CanRedo) return;
            var current = CaptureSnapshot(doc, _currentLabel);
            var next = _redo[^1];
            _redo.RemoveAt(_redo.Count - 1);
            _undo.Add(current);
            _currentLabel = next.Label;
            ApplySnapshot(doc, next);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Jumps directly to any point in the history (used by the History window) by
        /// calling Undo/Redo the appropriate number of times - reuses the exact same
        /// snapshot-restore logic as a single step, just repeated.</summary>
        public void JumpTo(int index, PaintDocument doc)
        {
            int diff = index - CurrentIndex;
            for (int i = 0; i < -diff; i++) Undo(doc);
            for (int i = 0; i < diff; i++) Redo(doc);
        }

        /// <summary>Removes one entry from the history entirely (not the same as Undo - this
        /// deletes a step rather than reverting to it). Only removes entries that are currently in
        /// the "past" (already-applied undo stack) or "future" (redo stack); the current state
        /// itself can't be deleted since it's not a discrete stored entry.</summary>
        public bool DeleteEntry(int index)
        {
            if (index < _undo.Count)
            {
                _undo.RemoveAt(index);
                StateChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
            int redoPos = index - _undo.Count - 1; // index into Labels' redo section
            int redoListIndex = _redo.Count - 1 - redoPos; // Labels lists redo nearest-first; _redo stores furthest-pushed-last
            if (redoPos >= 0 && redoListIndex >= 0 && redoListIndex < _redo.Count)
            {
                _redo.RemoveAt(redoListIndex);
                StateChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
            return false; // index pointed at the current state, which can't be deleted this way
        }

        private static Snapshot CaptureSnapshot(PaintDocument doc, string label) => new Snapshot
        {
            Label = label,
            ActiveLayerIndex = doc.ActiveLayerIndex,
            Layers = doc.Layers.Select(l => new LayerSnap
            {
                Pixels = l.Surface.SnapshotBytes(),
                Width = l.Surface.Width,
                Height = l.Surface.Height,
                Name = l.Name,
                Visible = l.Visible,
                Text = l.Text?.Clone()
            }).ToList()
        };

        private static void ApplySnapshot(PaintDocument doc, Snapshot snap)
        {
            var restored = new List<PaintLayer>(snap.Layers.Count);
            foreach (var ls in snap.Layers)
            {
                var surf = new RasterSurface(ls.Width, ls.Height, Colors.White);
                surf.RestoreBytes(ls.Pixels);
                restored.Add(new PaintLayer { Surface = surf, Name = ls.Name, Visible = ls.Visible, Text = ls.Text?.Clone() });
            }
            doc.RestoreLayers(restored, snap.ActiveLayerIndex);
        }

        public void Clear(string label = "New Document")
        {
            _undo.Clear();
            _redo.Clear();
            _currentLabel = label;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
