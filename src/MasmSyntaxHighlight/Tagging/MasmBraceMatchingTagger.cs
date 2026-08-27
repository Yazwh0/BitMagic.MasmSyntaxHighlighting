using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace MasmSyntaxHighlight.Tagging
{
    /// <summary>Creates one <see cref="MasmBraceMatchingTagger"/> per MASM text view.</summary>
    [Export(typeof(IViewTaggerProvider))]
    [ContentType(MasmContentTypes.ContentType)]
    [TagType(typeof(TextMarkerTag))]
    internal sealed class MasmBraceMatchingTaggerProvider : IViewTaggerProvider
    {
        public ITagger<T> CreateTagger<T>(ITextView textView, ITextBuffer buffer) where T : ITag
        {
            if (textView == null || buffer == null || textView.TextBuffer != buffer)
                return null;

            return textView.Properties.GetOrCreateSingletonProperty(
                () => new MasmBraceMatchingTagger(textView)) as ITagger<T>;
        }
    }

    /// <summary>
    /// Highlights the <c>()</c> / <c>[]</c> pair adjacent to the caret. Angle brackets are not
    /// matched - MASM uses <c>&lt;</c> / <c>&gt;</c> as comparison operators in <c>.IF</c> /
    /// <c>.WHILE</c> expressions. Braces inside <c>;</c> comments and string literals are skipped.
    /// </summary>
    internal sealed class MasmBraceMatchingTagger : ITagger<TextMarkerTag>
    {
        private static readonly (char Open, char Close)[] Pairs =
        {
            ('(', ')'),
            ('[', ']'),
        };

        private static readonly TextMarkerTag Marker = new TextMarkerTag("blockmatch");

        private readonly ITextView _view;

        internal MasmBraceMatchingTagger(ITextView view)
        {
            _view = view;
            _view.Caret.PositionChanged += (s, e) => RaiseTagsChanged();
            _view.LayoutChanged += (s, e) =>
            {
                if (e.OldSnapshot != e.NewSnapshot) RaiseTagsChanged();
            };
        }

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        private void RaiseTagsChanged()
        {
            ITextSnapshot snapshot = _view.TextBuffer.CurrentSnapshot;
            TagsChanged?.Invoke(
                this,
                new SnapshotSpanEventArgs(new SnapshotSpan(snapshot, 0, snapshot.Length)));
        }

        public IEnumerable<ITagSpan<TextMarkerTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            if (spans.Count == 0) yield break;

            SnapshotPoint caret = _view.Caret.Position.BufferPosition;
            ITextSnapshot snapshot = spans[0].Snapshot;
            if (caret.Snapshot != snapshot) yield break;

            foreach (int position in CandidatePositions(caret.Position, snapshot.Length))
            {
                if (!TryMatch(snapshot, position, out SnapshotSpan open, out SnapshotSpan close))
                    continue;

                if (spans.IntersectsWith(open) || spans.IntersectsWith(close))
                {
                    yield return new TagSpan<TextMarkerTag>(open, Marker);
                    yield return new TagSpan<TextMarkerTag>(close, Marker);
                }
                yield break; // one pair at a time
            }
        }

        private static IEnumerable<int> CandidatePositions(int caret, int length)
        {
            if (caret < length) yield return caret;      // brace just after the caret
            if (caret > 0) yield return caret - 1;       // brace just before the caret
        }

        private bool TryMatch(ITextSnapshot snapshot, int position, out SnapshotSpan open, out SnapshotSpan close)
        {
            open = default;
            close = default;

            char c = snapshot[position];
            if (IsInCommentOrString(snapshot, position)) return false;

            foreach ((char o, char cl) in Pairs)
            {
                if (c == o)
                {
                    int match = Scan(snapshot, position, o, cl, forward: true);
                    if (match < 0) return false;
                    open = new SnapshotSpan(snapshot, position, 1);
                    close = new SnapshotSpan(snapshot, match, 1);
                    return true;
                }
                if (c == cl)
                {
                    int match = Scan(snapshot, position, o, cl, forward: false);
                    if (match < 0) return false;
                    open = new SnapshotSpan(snapshot, match, 1);
                    close = new SnapshotSpan(snapshot, position, 1);
                    return true;
                }
            }

            return false;
        }

        private int Scan(ITextSnapshot snapshot, int start, char open, char close, bool forward)
        {
            int depth = 1;
            int step = forward ? 1 : -1;
            for (int p = start + step; p >= 0 && p < snapshot.Length; p += step)
            {
                char ch = snapshot[p];
                if (ch != open && ch != close) continue;
                if (IsInCommentOrString(snapshot, p)) continue;

                if (ch == open) depth += forward ? 1 : -1;
                else depth += forward ? -1 : 1;

                if (depth == 0) return p;
            }
            return -1;
        }

        private static bool IsInCommentOrString(ITextSnapshot snapshot, int position)
        {
            ITextSnapshotLine line = snapshot.GetLineFromPosition(position);
            string text = line.GetText();
            int column = position - line.Start.Position;

            char quote = '\0';
            for (int i = 0; i < column && i < text.Length; i++)
            {
                char c = text[i];
                if (quote != '\0')
                {
                    if (c == quote)
                    {
                        if (i + 1 < text.Length && text[i + 1] == quote) i++; // doubled quote = literal
                        else quote = '\0';
                    }
                }
                else if (c == '"' || c == '\'')
                {
                    quote = c;
                }
                else if (c == ';')
                {
                    return true; // rest of the line is a comment
                }
            }

            return quote != '\0';
        }
    }
}
