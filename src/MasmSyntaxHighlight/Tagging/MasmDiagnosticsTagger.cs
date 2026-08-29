using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using MasmSyntaxHighlight.Diagnostics;
using MasmSyntaxHighlight.Lexing;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace MasmSyntaxHighlight.Tagging
{
    /// <summary>One structural diagnostic: a span, its line/column (zero-based) and a message.</summary>
    internal readonly struct MasmDiagnostic
    {
        public readonly int Start;
        public readonly int End;
        public readonly int Line;
        public readonly int Column;
        public readonly string Message;

        public MasmDiagnostic(int start, int end, int line, int column, string message)
        {
            Start = start;
            End = end;
            Line = line;
            Column = column;
            Message = message;
        }
    }

    /// <summary>
    /// Creates one <see cref="MasmDiagnosticsTagger"/> per MASM buffer and, once per open
    /// document, bridges its diagnostics into the Error List via <see cref="MasmErrorListDataSource"/>.
    /// </summary>
    [Export(typeof(ITaggerProvider))]
    [ContentType(MasmContentTypes.ContentType)]
    [TagType(typeof(IErrorTag))]
    internal sealed class MasmDiagnosticsTaggerProvider : ITaggerProvider
    {
        private readonly MasmErrorListDataSource _errorList;
        private readonly ITextDocumentFactoryService _documentFactory;

        [ImportingConstructor]
        public MasmDiagnosticsTaggerProvider(
            ITextDocumentFactoryService documentFactory,
            [Import(AllowDefault = true)] MasmErrorListDataSource errorList)
        {
            _documentFactory = documentFactory;
            _errorList = errorList;
            if (_documentFactory != null && _errorList != null)
                _documentFactory.TextDocumentDisposed += OnTextDocumentDisposed;
        }

        public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag
        {
            if (buffer == null) return null;

            var tagger = buffer.Properties.GetOrCreateSingletonProperty(
                () => new MasmDiagnosticsTagger(buffer));

            if (_errorList != null && _documentFactory != null)
                buffer.Properties.GetOrCreateSingletonProperty(
                    typeof(MasmErrorListBridge),
                    () => new MasmErrorListBridge(buffer, tagger, _errorList, _documentFactory));

            return tagger as ITagger<T>;
        }

        private void OnTextDocumentDisposed(object sender, TextDocumentEventArgs e)
        {
            ITextBuffer buffer = e.TextDocument?.TextBuffer;
            if (buffer == null) return;

            if (buffer.Properties.TryGetProperty(typeof(MasmErrorListBridge), out MasmErrorListBridge bridge))
            {
                bridge.Dispose();
                buffer.Properties.RemoveProperty(typeof(MasmErrorListBridge));
            }
        }
    }

    /// <summary>
    /// Squiggles the few structural mistakes <c>ml64</c> itself rejects and that need no symbol
    /// resolution: a block opener with no matching closer, a closer with no opener, and a
    /// <c>ENDP</c> / <c>ENDS</c> whose name does not match its <c>PROC</c> / <c>STRUCT</c>.
    /// Deliberately conservative - nothing here fires on merely unknown names. The same list
    /// feeds the Error List (see <see cref="MasmErrorListBridge"/>).
    /// </summary>
    internal sealed class MasmDiagnosticsTagger : ITagger<IErrorTag>
    {
        private readonly ITextBuffer _buffer;
        private ITextSnapshot _snapshot;
        private IReadOnlyList<MasmDiagnostic> _diagnostics = Array.Empty<MasmDiagnostic>();

        internal MasmDiagnosticsTagger(ITextBuffer buffer)
        {
            _buffer = buffer;
            _buffer.Changed += OnBufferChanged;
            Recompute(_buffer.CurrentSnapshot);
        }

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        /// <summary>Raised (after <see cref="TagsChanged"/>) whenever the diagnostic set is recomputed.</summary>
        internal event EventHandler DiagnosticsChanged;

        internal IReadOnlyList<MasmDiagnostic> CurrentDiagnostics => _diagnostics;

        internal ITextSnapshot CurrentSnapshot => _snapshot;

        private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
            => Recompute(e.After);

        private void Recompute(ITextSnapshot snapshot)
        {
            _diagnostics = Analyse(snapshot);
            _snapshot = snapshot;

            TagsChanged?.Invoke(
                this, new SnapshotSpanEventArgs(new SnapshotSpan(snapshot, 0, snapshot.Length)));
            DiagnosticsChanged?.Invoke(this, EventArgs.Empty);
        }

        public IEnumerable<ITagSpan<IErrorTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            if (spans.Count == 0) yield break;

            ITextSnapshot snapshot = spans[0].Snapshot;
            if (!ReferenceEquals(_snapshot, snapshot)) Recompute(snapshot);
            if (_diagnostics.Count == 0) yield break;

            int requestStart = spans[0].Start.Position;
            int requestEnd = spans[spans.Count - 1].End.Position;

            foreach (MasmDiagnostic d in _diagnostics)
            {
                if (d.End <= requestStart || d.Start >= requestEnd) continue;
                if (d.End > snapshot.Length) continue;

                var span = new SnapshotSpan(snapshot, d.Start, d.End - d.Start);
                yield return new TagSpan<IErrorTag>(
                    span, new ErrorTag(PredefinedErrorTypeNames.SyntaxError, d.Message));
            }
        }

        // ---------------------------------------------------------------- analysis

        private sealed class OpenBlock
        {
            public string Keyword;
            public string Family;
            public MasmToken KeywordToken;
            public string Name;
            public MasmToken NameToken;
        }

        private static IReadOnlyList<MasmDiagnostic> Analyse(ITextSnapshot snapshot)
        {
            string text = snapshot.GetText();
            List<MasmToken> tokens = new MasmLexer(text).Tokenize();

            var diagnostics = new List<MasmDiagnostic>();
            var stack = new List<OpenBlock>();

            for (int i = 0; i < tokens.Count; i++)
            {
                MasmToken token = tokens[i];
                if (token.Kind != MasmTokenKind.Directive) continue;

                string keyword = text.Substring(token.Start, token.Length).ToLowerInvariant();

                if (OpenerFamily.TryGetValue(keyword, out string openFamily))
                {
                    GetName(tokens, i, text, out string name, out MasmToken nameToken);
                    stack.Add(new OpenBlock
                    {
                        Keyword = keyword,
                        Family = openFamily,
                        KeywordToken = token,
                        Name = name,
                        NameToken = nameToken,
                    });
                    continue;
                }

                if (!CloserFamily.TryGetValue(keyword, out string closeFamily))
                    continue;

                int match = -1;
                for (int s = stack.Count - 1; s >= 0; s--)
                {
                    if (stack[s].Family == closeFamily) { match = s; break; }
                }

                if (match < 0)
                {
                    Add(diagnostics, snapshot, token.Start, token.End,
                        $"'{keyword.ToUpperInvariant()}' has no matching {OpenerWord(closeFamily)}.");
                    continue;
                }

                for (int s = stack.Count - 1; s > match; s--)
                {
                    OpenBlock stranded = stack[s];
                    Add(diagnostics, snapshot, stranded.KeywordToken.Start, stranded.KeywordToken.End,
                        $"'{stranded.Keyword.ToUpperInvariant()}' has no matching {CloserWord(stranded.Family)}.");
                    stack.RemoveAt(s);
                }

                OpenBlock opener = stack[match];
                stack.RemoveAt(match);

                if (NameBearing.Contains(closeFamily) && opener.Name != null)
                {
                    GetName(tokens, i, text, out string closerName, out MasmToken closerNameToken);
                    if (closerName != null &&
                        !string.Equals(closerName, opener.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        Add(diagnostics, snapshot, closerNameToken.Start, closerNameToken.End,
                            $"'{keyword.ToUpperInvariant()}' name '{closerName}' does not match " +
                            $"'{opener.Keyword.ToUpperInvariant()}' name '{opener.Name}'.");
                    }
                }
            }

            foreach (OpenBlock left in stack)
                Add(diagnostics, snapshot, left.KeywordToken.Start, left.KeywordToken.End,
                    $"'{left.Keyword.ToUpperInvariant()}' has no matching {CloserWord(left.Family)}.");

            diagnostics.Sort((a, b) => a.Start.CompareTo(b.Start));
            return diagnostics;
        }

        private static void Add(
            List<MasmDiagnostic> into, ITextSnapshot snapshot, int start, int end, string message)
        {
            int safeStart = Math.Min(Math.Max(start, 0), snapshot.Length);
            ITextSnapshotLine line = snapshot.GetLineFromLineNumber(
                snapshot.GetLineNumberFromPosition(safeStart));
            into.Add(new MasmDiagnostic(
                start, end, line.LineNumber, safeStart - line.Start.Position, message));
        }

        /// <summary>The identifier immediately before the directive at <paramref name="dirIndex"/>,
        /// when it is a name token on the same line (<c>Foo</c> in <c>Foo PROC</c> / <c>Foo ENDP</c>).</summary>
        private static void GetName(
            IReadOnlyList<MasmToken> tokens, int dirIndex, string text,
            out string name, out MasmToken nameToken)
        {
            name = null;
            nameToken = default;
            if (dirIndex == 0) return;

            MasmToken prev = tokens[dirIndex - 1];
            switch (prev.Kind)
            {
                case MasmTokenKind.ProcName:
                case MasmTokenKind.TypeName:
                case MasmTokenKind.DataName:
                case MasmTokenKind.ConstantName:
                case MasmTokenKind.Label:
                case MasmTokenKind.Identifier:
                    break;
                default:
                    return;
            }

            for (int p = prev.End; p < tokens[dirIndex].Start && p < text.Length; p++)
                if (text[p] == '\n') return;

            name = text.Substring(prev.Start, prev.Length);
            nameToken = prev;
        }

        private static string OpenerWord(string family)
        {
            switch (family)
            {
                case "proc": return "PROC";
                case "ends": return "STRUCT / SEGMENT";
                case "endm": return "MACRO / REPEAT";
                case "endif": return "IF";
                case ".endif": return ".IF";
                case ".endw": return ".WHILE";
                case ".until": return ".REPEAT";
                default: return "opener";
            }
        }

        private static string CloserWord(string family)
        {
            switch (family)
            {
                case "proc": return "ENDP";
                case "ends": return "ENDS";
                case "endm": return "ENDM";
                case "endif": return "ENDIF";
                case ".endif": return ".ENDIF";
                case ".endw": return ".ENDW";
                case ".until": return ".UNTIL";
                default: return "closer";
            }
        }

        // opener keyword -> family key (the family key is the canonical closer keyword)
        private static readonly Dictionary<string, string> OpenerFamily = Build(
            ("proc", "proc"),
            ("struct", "ends"), ("struc", "ends"), ("union", "ends"), ("segment", "ends"),
            ("macro", "endm"), ("rept", "endm"), ("repeat", "endm"), ("irp", "endm"),
            ("irpc", "endm"), ("for", "endm"), ("forc", "endm"), ("while", "endm"),
            ("if", "endif"), ("ife", "endif"), ("ifb", "endif"), ("ifnb", "endif"),
            ("ifdef", "endif"), ("ifndef", "endif"), ("ifidn", "endif"), ("ifidni", "endif"),
            ("ifdif", "endif"), ("ifdifi", "endif"), ("if1", "endif"), ("if2", "endif"),
            (".if", ".endif"),
            (".while", ".endw"),
            (".repeat", ".until"));

        private static readonly Dictionary<string, string> CloserFamily = Build(
            ("endp", "proc"),
            ("ends", "ends"),
            ("endm", "endm"),
            ("endif", "endif"),
            (".endif", ".endif"),
            (".endw", ".endw"),
            (".until", ".until"), (".untilcxz", ".until"));

        private static readonly HashSet<string> NameBearing =
            new HashSet<string>(StringComparer.Ordinal) { "proc", "ends" };

        private static Dictionary<string, string> Build(params (string key, string value)[] pairs)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach ((string key, string value) in pairs)
                map[key] = value;
            return map;
        }
    }

    /// <summary>
    /// Lives once per open MASM document (in the buffer's property bag). Owns a
    /// <see cref="MasmErrorListFactory"/>, keeps it in step with the tagger's diagnostics, and is
    /// disposed when the document closes.
    /// </summary>
    internal sealed class MasmErrorListBridge : IDisposable
    {
        private readonly MasmDiagnosticsTagger _tagger;
        private readonly MasmErrorListDataSource _source;
        private readonly MasmErrorListFactory _factory;
        private readonly ITextDocument _document;
        private bool _disposed;

        internal MasmErrorListBridge(
            ITextBuffer buffer, MasmDiagnosticsTagger tagger,
            MasmErrorListDataSource source, ITextDocumentFactoryService documentFactory)
        {
            _tagger = tagger;
            _source = source;
            _factory = new MasmErrorListFactory();

            documentFactory.TryGetTextDocument(buffer, out _document);

            _source.AddFactory(_factory);
            _tagger.DiagnosticsChanged += OnDiagnosticsChanged;
            Push();
        }

        private void OnDiagnosticsChanged(object sender, EventArgs e) => Push();

        private void Push()
        {
            if (_disposed) return;
            _factory.Update(_document?.FilePath, _tagger.CurrentDiagnostics);
            _source.NotifyChanged(_factory);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _tagger.DiagnosticsChanged -= OnDiagnosticsChanged;
            _source.RemoveFactory(_factory);
        }
    }
}
