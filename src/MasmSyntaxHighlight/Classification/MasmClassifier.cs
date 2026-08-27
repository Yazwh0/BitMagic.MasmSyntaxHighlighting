using System;
using System.Collections.Generic;
using MasmSyntaxHighlight.Lexing;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;

namespace MasmSyntaxHighlight.Classification
{
    /// <summary>
    /// Colours a MASM buffer. The whole document is re-lexed whenever it changes (assembly
    /// source files are small, and multi-line constructs such as <c>COMMENT</c> blocks and
    /// line continuations make a stateless per-line classifier unreliable).
    /// </summary>
    internal sealed class MasmClassifier : IClassifier
    {
        private readonly ITextBuffer _buffer;
        private readonly Dictionary<MasmTokenKind, IClassificationType> _types;

        private ITextSnapshot _lexedSnapshot;
        private List<MasmToken> _tokens = new List<MasmToken>();

        internal MasmClassifier(ITextBuffer buffer, IClassificationTypeRegistryService registry)
        {
            _buffer = buffer;
            _types = new Dictionary<MasmTokenKind, IClassificationType>
            {
                [MasmTokenKind.Comment] = registry.GetClassificationType(MasmClassificationNames.Comment),
                [MasmTokenKind.String] = registry.GetClassificationType(MasmClassificationNames.String),
                [MasmTokenKind.Number] = registry.GetClassificationType(MasmClassificationNames.Number),
                [MasmTokenKind.Register] = registry.GetClassificationType(MasmClassificationNames.Register),
                [MasmTokenKind.Mnemonic] = registry.GetClassificationType(MasmClassificationNames.Mnemonic),
                [MasmTokenKind.Directive] = registry.GetClassificationType(MasmClassificationNames.Directive),
                [MasmTokenKind.DataType] = registry.GetClassificationType(MasmClassificationNames.DataType),
                [MasmTokenKind.Operator] = registry.GetClassificationType(MasmClassificationNames.Operator),
                [MasmTokenKind.Label] = registry.GetClassificationType(MasmClassificationNames.Label),
            };

            _buffer.Changed += OnBufferChanged;
        }

        public event EventHandler<ClassificationChangedEventArgs> ClassificationChanged;

        private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            // Force a re-lex on the next request and tell the editor the whole document may
            // have changed colour (an edit on one line can open/close a COMMENT block below).
            _lexedSnapshot = null;
            var snapshot = e.After;
            ClassificationChanged?.Invoke(
                this,
                new ClassificationChangedEventArgs(new SnapshotSpan(snapshot, 0, snapshot.Length)));
        }

        private void EnsureLexed(ITextSnapshot snapshot)
        {
            if (ReferenceEquals(_lexedSnapshot, snapshot)) return;
            _tokens = new MasmLexer(snapshot.GetText()).Tokenize();
            _lexedSnapshot = snapshot;
        }

        public IList<ClassificationSpan> GetClassificationSpans(SnapshotSpan span)
        {
            var result = new List<ClassificationSpan>();
            ITextSnapshot snapshot = span.Snapshot;
            EnsureLexed(snapshot);

            int spanStart = span.Start.Position;
            int spanEnd = span.End.Position;

            // _tokens is ordered by Start and non-overlapping.
            foreach (MasmToken token in _tokens)
            {
                if (token.End <= spanStart) continue;
                if (token.Start >= spanEnd) break;

                if (!_types.TryGetValue(token.Kind, out IClassificationType type) || type == null)
                    continue;

                int start = Math.Max(token.Start, 0);
                int end = Math.Min(token.End, snapshot.Length);
                if (end <= start) continue;

                result.Add(new ClassificationSpan(new SnapshotSpan(snapshot, start, end - start), type));
            }

            return result;
        }
    }
}
