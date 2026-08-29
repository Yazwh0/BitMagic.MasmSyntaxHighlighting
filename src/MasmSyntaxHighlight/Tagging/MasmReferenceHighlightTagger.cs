using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using MasmSyntaxHighlight.Navigation;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace MasmSyntaxHighlight.Tagging
{
    /// <summary>Creates one <see cref="MasmReferenceHighlightTagger"/> per MASM text view.</summary>
    [Export(typeof(IViewTaggerProvider))]
    [ContentType(MasmContentTypes.ContentType)]
    [TagType(typeof(TextMarkerTag))]
    internal sealed class MasmReferenceHighlightTaggerProvider : IViewTaggerProvider
    {
        [Import]
        internal ITextDocumentFactoryService DocumentFactory { get; set; }

        public ITagger<T> CreateTagger<T>(ITextView textView, ITextBuffer buffer) where T : ITag
        {
            if (textView == null || buffer == null || textView.TextBuffer != buffer)
                return null;

            MasmDefinitionIndex index = MasmBufferServices.GetIndex(buffer, DocumentFactory);
            return textView.Properties.GetOrCreateSingletonProperty(
                () => new MasmReferenceHighlightTagger(textView, index)) as ITagger<T>;
        }
    }

    /// <summary>
    /// Boxes every occurrence of the symbol under the caret - the same reference set Find All
    /// References lists - using the editor's standard "highlighted reference" marker so it themes
    /// with the rest of VS. A lone occurrence is not highlighted.
    /// </summary>
    internal sealed class MasmReferenceHighlightTagger : ITagger<TextMarkerTag>, IDisposable
    {
        // The editor's built-in reference-highlight marker format (shared with C#, etc.).
        private const string MarkerKind = "MarkerFormatDefinition/HighlightedReference";

        private readonly ITextView _view;
        private readonly MasmDefinitionIndex _index;

        private List<SnapshotSpan> _current = new List<SnapshotSpan>();
        private ITextSnapshot _currentSnapshot;
        private bool _disposed;

        internal MasmReferenceHighlightTagger(ITextView view, MasmDefinitionIndex index)
        {
            _view = view;
            _index = index;
            _view.Caret.PositionChanged += OnCaretChanged;
            _view.LayoutChanged += OnLayoutChanged;
            _view.Closed += OnViewClosed;
        }

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        private void OnCaretChanged(object sender, CaretPositionChangedEventArgs e) => Recompute();

        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            // A buffer edit arrives here as a new snapshot; recompute against it.
            if (!ReferenceEquals(_currentSnapshot, _view.TextBuffer.CurrentSnapshot))
                Recompute();
        }

        private void OnViewClosed(object sender, EventArgs e) => Dispose();

        private void Recompute()
        {
            if (_disposed) return;

            ITextSnapshot snapshot = _view.TextBuffer.CurrentSnapshot;
            SnapshotPoint caret = _view.Caret.Position.BufferPosition;
            if (!ReferenceEquals(caret.Snapshot, snapshot))
                caret = caret.TranslateTo(snapshot, PointTrackingMode.Positive);

            var updated = new List<SnapshotSpan>();
            try
            {
                OccurrenceSet occ = _index.FindOccurrences(snapshot, caret.Position);
                if (occ.Spans.Count >= 2) // nothing to gain from boxing a single occurrence
                    foreach (Span sp in occ.Spans)
                        updated.Add(new SnapshotSpan(snapshot, sp));
            }
            catch
            {
                updated.Clear();
            }

            if (ReferenceEquals(_currentSnapshot, snapshot) && SpansEqual(updated, _current))
                return;

            _current = updated;
            _currentSnapshot = snapshot;
            TagsChanged?.Invoke(
                this, new SnapshotSpanEventArgs(new SnapshotSpan(snapshot, 0, snapshot.Length)));
        }

        public IEnumerable<ITagSpan<TextMarkerTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            if (_disposed || spans.Count == 0 || _current.Count == 0) yield break;
            if (!ReferenceEquals(_currentSnapshot, spans[0].Snapshot)) yield break;

            int requestStart = spans[0].Start.Position;
            int requestEnd = spans[spans.Count - 1].End.Position;

            foreach (SnapshotSpan span in _current)
            {
                if (span.End.Position < requestStart || span.Start.Position > requestEnd)
                    continue;
                yield return new TagSpan<TextMarkerTag>(span, new TextMarkerTag(MarkerKind));
            }
        }

        private static bool SpansEqual(List<SnapshotSpan> a, List<SnapshotSpan> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i].Span != b[i].Span) return false;
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _view.Caret.PositionChanged -= OnCaretChanged;
            _view.LayoutChanged -= OnLayoutChanged;
            _view.Closed -= OnViewClosed;
        }
    }
}
