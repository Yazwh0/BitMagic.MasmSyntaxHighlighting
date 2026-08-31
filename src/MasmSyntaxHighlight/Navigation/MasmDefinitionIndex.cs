using System;
using System.Collections.Generic;
using MasmSyntaxHighlight.Lexing;
using Microsoft.VisualStudio.Text;

namespace MasmSyntaxHighlight.Navigation
{
    /// <summary>
    /// Resolves the identifier under a point to the definition it refers to, using the same
    /// lexer, symbol pass and <c>INCLUDE</c> index as colouring. Parsed once per snapshot and
    /// cached; all access is serialised because navigation features can be probed off the UI
    /// thread. Also finds every occurrence of a symbol in the buffer (for reference highlighting
    /// and Find All References).
    /// </summary>
    internal sealed class MasmDefinitionIndex
    {
        private readonly ITextBuffer _buffer;
        private readonly ITextDocument _document;
        private readonly object _gate = new object();

        private ITextSnapshot _snapshot;
        private string _text = string.Empty;
        private List<MasmToken> _tokens = new List<MasmToken>();
        private Dictionary<string, List<MasmSymbolDef>> _defsByName =
            new Dictionary<string, List<MasmSymbolDef>>(StringComparer.OrdinalIgnoreCase);
        private List<ProcRange> _procRanges = new List<ProcRange>();
        private Dictionary<string, MasmStructDef> _structs =
            new Dictionary<string, MasmStructDef>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _instances =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal MasmDefinitionIndex(ITextBuffer buffer, ITextDocument document)
        {
            _buffer = buffer;
            _document = document;
        }

        /// <summary>Path of the file backing this buffer, or <c>null</c> for an unsaved buffer.</summary>
        internal string DocumentPath => _document?.FilePath;

        /// <summary>
        /// The definition the identifier at <paramref name="position"/> refers to, plus the span
        /// of that identifier (for the hover underline). <c>null</c> when there is nothing to go to
        /// - not an identifier, unknown name, or the caret already sits on the sole definition.
        /// </summary>
        internal MasmSymbolDef? Resolve(
            ITextSnapshot snapshot, int position, out SnapshotSpan symbolSpan,
            bool preferDeclaration = false)
        {
            symbolSpan = default;
            lock (_gate)
            {
                EnsureParsed(snapshot);

                MasmToken? hit = TokenAt(position);
                if (hit == null || !IsNavigable(hit.Value.Kind)) return null;
                MasmToken token = hit.Value;

                MasmSymbolDef? best = ResolveCore(token, position, preferDeclaration);
                if (best == null) return null;

                MasmSymbolDef target = best.Value;
                if (target.FilePath == null && target.Start == token.Start)
                    return null; // caret is on the definition itself

                symbolSpan = new SnapshotSpan(snapshot, token.Start, token.Length);
                return target;
            }
        }

        /// <summary>
        /// Like <see cref="Resolve"/> but returns the definition even when the caret is on it
        /// (QuickInfo and Peek want to describe the symbol you are pointing at, including its own
        /// definition site).
        /// </summary>
        internal MasmSymbolDef? ResolveForInfo(
            ITextSnapshot snapshot, int position, out SnapshotSpan symbolSpan)
        {
            symbolSpan = default;
            lock (_gate)
            {
                EnsureParsed(snapshot);

                MasmToken? hit = TokenAt(position);
                if (hit == null || !IsNavigable(hit.Value.Kind)) return null;

                MasmSymbolDef? best = ResolveCore(hit.Value, position, preferDeclaration: false);
                if (best == null) return null;

                symbolSpan = new SnapshotSpan(snapshot, hit.Value.Start, hit.Value.Length);
                return best;
            }
        }

        /// <summary>
        /// Every occurrence of the symbol under <paramref name="position"/> that lies in this
        /// buffer - its references and, when it is defined here, the definition itself. Occurrences
        /// that merely share the name but bind to a different symbol (a same-named proc-local label
        /// in another proc) are excluded. <see cref="OccurrenceSet.Target"/> is the definition they
        /// all bind to and may live in an <c>INCLUDE</c>d file.
        /// </summary>
        internal OccurrenceSet FindOccurrences(ITextSnapshot snapshot, int position)
        {
            lock (_gate)
            {
                EnsureParsed(snapshot);

                var empty = new OccurrenceSet(null, Array.Empty<Span>());

                MasmToken? hit = TokenAt(position);
                if (hit == null || !IsNavigable(hit.Value.Kind)) return empty;

                MasmSymbolDef? target = ResolveCore(hit.Value, position, preferDeclaration: false);
                if (target == null) return empty;
                MasmSymbolDef t = target.Value;

                string name = _text.Substring(hit.Value.Start, hit.Value.Length);
                var spans = new List<Span>();

                foreach (MasmToken tok in _tokens)
                {
                    if (!IsNavigable(tok.Kind) || tok.Length != name.Length) continue;
                    if (string.Compare(_text, tok.Start, name, 0, name.Length,
                                       StringComparison.OrdinalIgnoreCase) != 0)
                        continue;

                    MasmSymbolDef? bound = ResolveCore(tok, tok.Start, preferDeclaration: false);
                    if (bound != null && SameSite(bound.Value, t))
                        spans.Add(new Span(tok.Start, tok.Length));
                }

                return new OccurrenceSet(target, spans);
            }
        }

        /// <summary>
        /// Distinct symbol names visible to this buffer (this file plus <c>INCLUDE</c>d ones),
        /// each with its best kind, for completion. Proc-local labels are included - completing
        /// a label name inside the wrong proc is harmless, and filtering by caret position would
        /// churn the list on every keystroke.
        /// </summary>
        internal List<KeyValuePair<string, MasmTokenKind>> SymbolCompletions(ITextSnapshot snapshot)
        {
            lock (_gate)
            {
                EnsureParsed(snapshot);

                var result = new List<KeyValuePair<string, MasmTokenKind>>(_defsByName.Count);
                foreach (KeyValuePair<string, List<MasmSymbolDef>> pair in _defsByName)
                {
                    MasmTokenKind best = pair.Value[0].Kind;
                    for (int i = 1; i < pair.Value.Count; i++)
                        if (KindRank(pair.Value[i].Kind) > KindRank(best))
                            best = pair.Value[i].Kind;
                    result.Add(new KeyValuePair<string, MasmTokenKind>(pair.Key, best));
                }
                return result;
            }
        }

        /// <summary>
        /// The trimmed source line a definition sits on, for a QuickInfo tooltip. Read from the
        /// live snapshot for a symbol defined in this buffer, otherwise from the (cached) contents
        /// of the <c>INCLUDE</c>d file on disk. <c>null</c> when it cannot be read.
        /// </summary>
        internal string GetDefinitionLineText(MasmSymbolDef def, ITextSnapshot snapshot)
        {
            try
            {
                if (def.FilePath == null)
                {
                    int ln = snapshot.GetLineNumberFromPosition(Math.Min(def.Start, snapshot.Length));
                    return snapshot.GetLineFromLineNumber(ln).GetText().Trim();
                }

                string text = MasmSourceText.GetText(def.FilePath);
                return text == null ? null : MasmSourceText.GetLineText(text, def.Start).Trim();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The struct whose members should be offered for completion after the <c>.</c> at
        /// <paramref name="dotPosition"/>. Walks the access chain to the left of the dot -
        /// <c>TYPE.</c>, <c>[reg].TYPE.</c>, <c>var.</c> (var declared as a struct), and chains
        /// through struct-typed members (<c>a.b.c.</c>). <c>null</c> when the chain does not land
        /// on a known struct.
        /// </summary>
        internal MasmStructDef ResolveStructMembers(ITextSnapshot snapshot, int dotPosition)
        {
            lock (_gate)
            {
                EnsureParsed(snapshot);

                int di = DotTokenIndexAt(dotPosition);
                if (di < 0) return null;

                var segments = new List<string>();
                int k = di;
                while (k - 1 >= 0)
                {
                    MasmToken prev = _tokens[k - 1];
                    if (prev.Kind == MasmTokenKind.Operator)
                        break; // ']' / ')' base, or any other punctuation - stop the chain here
                    if (!IsChainSegment(prev.Kind))
                        break;

                    segments.Insert(0, _text.Substring(prev.Start, prev.Length));

                    if (k - 2 >= 0 && _tokens[k - 2].Kind == MasmTokenKind.Operator
                        && _tokens[k - 2].Length == 1 && _text[_tokens[k - 2].Start] == '.')
                    {
                        k -= 2;
                        continue;
                    }
                    break;
                }

                return segments.Count == 0 ? null : ResolveChain(segments);
            }
        }

        private int DotTokenIndexAt(int dotPosition)
        {
            int lo = 0, hi = _tokens.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                MasmToken t = _tokens[mid];
                if (dotPosition < t.Start) hi = mid - 1;
                else if (dotPosition >= t.End) lo = mid + 1;
                else
                    return (t.Kind == MasmTokenKind.Operator && t.Length == 1 && _text[t.Start] == '.')
                        ? mid : -1;
            }
            return -1;
        }

        private static bool IsChainSegment(MasmTokenKind kind)
        {
            switch (kind)
            {
                case MasmTokenKind.Identifier:
                case MasmTokenKind.Register:
                case MasmTokenKind.DataType:
                case MasmTokenKind.Label:
                case MasmTokenKind.TypeName:
                case MasmTokenKind.DataName:
                    return true;
                default:
                    return false;
            }
        }

        private MasmStructDef ResolveChain(List<string> segments)
        {
            if (!_structs.TryGetValue(segments[0], out MasmStructDef current))
            {
                if (!_instances.TryGetValue(segments[0], out string type)
                    || !_structs.TryGetValue(type, out current))
                    return null;
            }

            for (int s = 1; s < segments.Count; s++)
            {
                string fieldType = FieldType(current, segments[s]);
                if (fieldType == null || !_structs.TryGetValue(fieldType, out current))
                    return null;
            }
            return current;
        }

        private static string FieldType(MasmStructDef structDef, string fieldName)
        {
            foreach (MasmStructField f in structDef.Fields)
                if (string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase))
                    return f.TypeName;
            return null;
        }

        // ---------------------------------------------------------------- resolution core

        private MasmSymbolDef? ResolveCore(MasmToken token, int position, bool preferDeclaration)
        {
            string name = _text.Substring(token.Start, token.Length);
            if (!_defsByName.TryGetValue(name, out List<MasmSymbolDef> named))
                return null;

            int caretProc = EnclosingProc(position);

            MasmSymbolDef? best = null;
            int bestScore = int.MinValue;
            foreach (MasmSymbolDef def in named)
            {
                // a proc-local label is only reachable from inside its own proc, same file
                if (def.IsProcLocal &&
                    !(def.FilePath == null && def.EnclosingProcStart == caretProc))
                    continue;

                int score = Score(def, caretProc, preferDeclaration);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = def;
                }
            }
            return best;
        }

        private static bool SameSite(MasmSymbolDef a, MasmSymbolDef b)
            => string.Equals(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase)
               && a.Start == b.Start && a.Kind == b.Kind;

        private static int Score(MasmSymbolDef def, int caretProc, bool preferDeclaration)
        {
            int score = 0;
            if (def.FilePath == null && caretProc >= 0 && def.EnclosingProcStart == caretProc)
                score += 1000;                                  // the label defined in this very proc
            if (def.IsDeclaration == preferDeclaration) score += 8; // PROC body, or its PROTO for Go To Declaration
            if (def.FilePath == null) score += 4;               // this file over an included one
            score += KindRank(def.Kind);
            return score;
        }

        private static int KindRank(MasmTokenKind kind)
        {
            switch (kind)
            {
                case MasmTokenKind.TypeName: return 3;
                case MasmTokenKind.ProcName: return 2;
                default: return 1;
            }
        }

        private static bool IsNavigable(MasmTokenKind kind)
        {
            switch (kind)
            {
                case MasmTokenKind.Identifier:
                case MasmTokenKind.ProcName:
                case MasmTokenKind.TypeName:
                case MasmTokenKind.Label:
                case MasmTokenKind.DataName:
                case MasmTokenKind.ConstantName:
                    return true;
                default:
                    return false;
            }
        }

        private MasmToken? TokenAt(int position)
        {
            int lo = 0, hi = _tokens.Count - 1; // ordered by Start, non-overlapping
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                MasmToken t = _tokens[mid];
                if (position < t.Start) hi = mid - 1;
                else if (position >= t.End) lo = mid + 1;
                else return t;
            }
            return null;
        }

        /// <summary>Name-token Start of the <c>PROC</c> containing <paramref name="position"/>, or -1.</summary>
        private int EnclosingProc(int position)
        {
            foreach (ProcRange r in _procRanges)
                if (position >= r.BodyStart && position < r.BodyEnd)
                    return r.NameStart;
            return -1;
        }

        private readonly struct ProcRange
        {
            public readonly int NameStart; // Start of the PROC name token (== MasmSymbolDef.EnclosingProcStart)
            public readonly int BodyStart; // first char after the 'proc' directive
            public readonly int BodyEnd;   // Start of the matching 'endp', or int.MaxValue

            public ProcRange(int nameStart, int bodyStart, int bodyEnd)
            {
                NameStart = nameStart;
                BodyStart = bodyStart;
                BodyEnd = bodyEnd;
            }
        }

        private static bool SameKeyword(string text, MasmToken token, string keyword)
            => token.Length == keyword.Length
               && string.Compare(text, token.Start, keyword, 0, keyword.Length,
                                 StringComparison.OrdinalIgnoreCase) == 0;

        private static List<ProcRange> BuildProcRanges(IReadOnlyList<MasmToken> tokens, string text)
        {
            var ranges = new List<ProcRange>();
            int openName = -1, openBody = -1;

            for (int i = 0; i < tokens.Count; i++)
            {
                MasmToken t = tokens[i];
                if (t.Kind != MasmTokenKind.Directive) continue;

                if (SameKeyword(text, t, "proc") && i > 0 && tokens[i - 1].Kind == MasmTokenKind.ProcName)
                {
                    openName = tokens[i - 1].Start;
                    openBody = t.End;
                }
                else if (SameKeyword(text, t, "endp") && openName >= 0)
                {
                    ranges.Add(new ProcRange(openName, openBody, t.Start));
                    openName = -1;
                }
            }

            if (openName >= 0) // a PROC with no matching ENDP - scope runs to end of file
                ranges.Add(new ProcRange(openName, openBody, int.MaxValue));

            return ranges;
        }

        private void EnsureParsed(ITextSnapshot snapshot)
        {
            if (ReferenceEquals(_snapshot, snapshot)) return;

            string text = snapshot.GetText();
            List<MasmToken> tokens = new MasmLexer(text).Tokenize();
            List<MasmSymbolDef> defs = MasmSymbols.CollectDefinitionsWithLocations(tokens, text, null);

            try
            {
                List<MasmSymbolDef> external = MasmIncludeIndex.CollectDefs(_document?.FilePath, text);
                if (external != null && external.Count > 0)
                    defs.AddRange(external);
            }
            catch
            {
                // navigation must never throw into the editor
            }

            var byName = new Dictionary<string, List<MasmSymbolDef>>(StringComparer.OrdinalIgnoreCase);
            foreach (MasmSymbolDef def in defs)
            {
                if (!byName.TryGetValue(def.Name, out List<MasmSymbolDef> list))
                    byName[def.Name] = list = new List<MasmSymbolDef>(1);
                list.Add(def);
            }

            var structs = new Dictionary<string, MasmStructDef>(StringComparer.OrdinalIgnoreCase);
            var instances = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            MasmStructModel local = MasmStructIndex.Collect(tokens, text, null);
            foreach (MasmStructDef sd in local.Structs)
                structs[sd.Name] = sd; // local wins
            foreach (KeyValuePair<string, string> kv in local.Instances)
                instances[kv.Key] = kv.Value;

            try
            {
                MasmStructModel ext = MasmIncludeIndex.CollectStructModel(_document?.FilePath, text);
                if (ext != null)
                {
                    foreach (MasmStructDef sd in ext.Structs)
                        if (!structs.ContainsKey(sd.Name)) structs[sd.Name] = sd;
                    foreach (KeyValuePair<string, string> kv in ext.Instances)
                        if (!instances.ContainsKey(kv.Key)) instances[kv.Key] = kv.Value;
                }
            }
            catch
            {
                // completion must never throw into the editor
            }

            // an instance is only useful if its declared type names a struct we actually know
            foreach (string key in new List<string>(instances.Keys))
                if (!structs.ContainsKey(instances[key]))
                    instances.Remove(key);

            _text = text;
            _tokens = tokens;
            _defsByName = byName;
            _procRanges = BuildProcRanges(tokens, text);
            _structs = structs;
            _instances = instances;
            _snapshot = snapshot;
        }
    }

    /// <summary>Result of <see cref="MasmDefinitionIndex.FindOccurrences"/>.</summary>
    internal readonly struct OccurrenceSet
    {
        /// <summary>The definition every listed span binds to (may be in an <c>INCLUDE</c>d file).</summary>
        public readonly MasmSymbolDef? Target;

        /// <summary>Occurrence spans within the queried buffer, in source order.</summary>
        public readonly IReadOnlyList<Span> Spans;

        public OccurrenceSet(MasmSymbolDef? target, IReadOnlyList<Span> spans)
        {
            Target = target;
            Spans = spans ?? Array.Empty<Span>();
        }
    }
}
