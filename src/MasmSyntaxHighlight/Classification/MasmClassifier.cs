using System;
using System.Collections.Generic;
using MasmSyntaxHighlight.Lexing;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;

namespace MasmSyntaxHighlight.Classification
{
    /// <summary>
    /// Colours a MASM buffer. The whole document is re-lexed whenever it changes (assembly
    /// source files are small, and multi-line constructs such as <c>COMMENT</c> blocks and
    /// line continuations make a stateless per-line classifier unreliable).
    ///
    /// Token kinds map onto stock classifications so the colours follow the user's theme and
    /// Fonts and Colors settings: the built-in editor ones where they fit, the C# / Roslyn
    /// classifications ("User Types", "User Methods", label name) for data types, PROC names
    /// and labels, and the extension's own "MASM Register" (which has no equivalent at all).
    /// </summary>
    internal sealed class MasmClassifier : IClassifier
    {
        // Roslyn classifications reused for tokens with no built-in editor equivalent.
        private const string ClassNameClassification = "class name";     // data-type / size keywords
        private const string StructNameClassification = "struct name";   // STRUCT / RECORD / TYPEDEF names
        private const string MethodNameClassification = "method name";   // PROC / MACRO names, call targets
        private const string LabelNameClassification = "label name";     // code labels, jump targets
        private const string FieldNameClassification = "field name";     // data variable names
        private const string ConstantNameClassification = "constant name"; // EQU / = names

        private readonly ITextBuffer _buffer;
        private readonly Dictionary<MasmTokenKind, IClassificationType> _types;

        private ITextSnapshot _lexedSnapshot;
        private List<MasmToken> _tokens = new List<MasmToken>();

        internal MasmClassifier(
            ITextBuffer buffer,
            IClassificationTypeRegistryService registry,
            IStandardClassificationService standard)
        {
            _buffer = buffer;

            IClassificationType Roslyn(string name) => registry.GetClassificationType(name);

            IClassificationType dataType = Roslyn(ClassNameClassification) ?? standard.Keyword;
            IClassificationType typeName = Roslyn(StructNameClassification)
                                          ?? Roslyn(ClassNameClassification) ?? standard.Keyword;
            IClassificationType procName = Roslyn(MethodNameClassification) ?? standard.SymbolDefinition;
            IClassificationType label = Roslyn(LabelNameClassification) ?? standard.SymbolDefinition;
            IClassificationType dataName = Roslyn(FieldNameClassification) ?? standard.SymbolDefinition;
            IClassificationType constantName = Roslyn(ConstantNameClassification) ?? standard.SymbolDefinition;
            IClassificationType register = Roslyn(MasmClassificationNames.Register) ?? standard.Keyword;

            _types = new Dictionary<MasmTokenKind, IClassificationType>
            {
                [MasmTokenKind.Comment] = standard.Comment,
                [MasmTokenKind.String] = standard.StringLiteral,
                [MasmTokenKind.Number] = standard.NumberLiteral,
                [MasmTokenKind.Operator] = standard.Operator,
                [MasmTokenKind.Label] = label,
                [MasmTokenKind.ProcName] = procName,
                [MasmTokenKind.TypeName] = typeName,
                [MasmTokenKind.DataName] = dataName,
                [MasmTokenKind.ConstantName] = constantName,
                [MasmTokenKind.Mnemonic] = standard.Keyword,
                [MasmTokenKind.Directive] = standard.PreprocessorKeyword,
                [MasmTokenKind.DataType] = dataType,
                [MasmTokenKind.Register] = register,
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
