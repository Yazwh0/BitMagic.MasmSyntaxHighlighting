using System;
using System.Collections.Generic;
using MasmSyntaxHighlight.Lexing;
using Microsoft.VisualStudio.Text;

namespace MasmSyntaxHighlight.Navigation
{
    /// <summary>
    /// Resolves the identifier under a point to the definition it refers to, using the same
    /// lexer, symbol pass and <c>INCLUDE</c> index as colouring. Parsed once per snapshot and
    /// cached; all access is serialised because Go To Definition can be probed off the UI thread.
    /// </summary>
    internal sealed class MasmDefinitionIndex
    {
        private readonly ITextBuffer _buffer;
        private readonly ITextDocument _document;
        private readonly object _gate = new object();

        private ITextSnapshot _snapshot;
        private string _text = string.Empty;
        private List<MasmToken> _tokens = new List<MasmToken>();
        private List<MasmSymbolDef> _defs = new List<MasmSymbolDef>();

        internal MasmDefinitionIndex(ITextBuffer buffer, ITextDocument document)
        {
            _buffer = buffer;
            _document = document;
        }

        /// <summary>
        /// The definition the identifier at <paramref name="position"/> refers to, plus the span
        /// of that identifier (for the hover underline). <c>null</c> when there is nothing to go to
        /// - not an identifier, unknown name, or the caret already sits on the sole definition.
        /// </summary>
        internal MasmSymbolDef? Resolve(ITextSnapshot snapshot, int position, out SnapshotSpan symbolSpan)
        {
            symbolSpan = default;
            lock (_gate)
            {
                EnsureParsed(snapshot);

                MasmToken? hit = TokenAt(position);
                if (hit == null || !IsNavigable(hit.Value.Kind)) return null;
                MasmToken token = hit.Value;

                string name = _text.Substring(token.Start, token.Length);
                int caretProc = EnclosingProc(position);

                MasmSymbolDef? best = null;
                int bestScore = int.MinValue;
                foreach (MasmSymbolDef def in _defs)
                {
                    if (!string.Equals(def.Name, name, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // a proc-local label is only reachable from inside its own proc, same file
                    if (def.IsProcLocal &&
                        !(def.FilePath == null && def.EnclosingProcStart == caretProc))
                        continue;

                    int score = Score(def, caretProc);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = def;
                    }
                }

                if (best == null) return null;

                MasmSymbolDef target = best.Value;
                if (target.FilePath == null && target.Start == token.Start)
                    return null; // caret is on the definition itself

                symbolSpan = new SnapshotSpan(snapshot, token.Start, token.Length);
                return target;
            }
        }

        private static int Score(MasmSymbolDef def, int caretProc)
        {
            int score = 0;
            if (def.FilePath == null && caretProc >= 0 && def.EnclosingProcStart == caretProc)
                score += 1000;                    // the label defined in this very proc
            if (!def.IsDeclaration) score += 8;   // the PROC body over its PROTO
            if (def.FilePath == null) score += 4; // this file over an included one
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

        private int EnclosingProc(int position)
        {
            int procStart = -1;
            for (int i = 0; i < _tokens.Count; i++)
            {
                MasmToken t = _tokens[i];
                if (t.Start > position) break;
                if (t.Kind != MasmTokenKind.Directive) continue;
                if (KeywordIs(t, "proc") && i > 0 && _tokens[i - 1].Kind == MasmTokenKind.ProcName)
                    procStart = _tokens[i - 1].Start;
                else if (KeywordIs(t, "endp"))
                    procStart = -1;
            }
            return procStart;
        }

        private bool KeywordIs(MasmToken token, string keyword)
            => token.Length == keyword.Length
               && string.Compare(_text, token.Start, keyword, 0, keyword.Length,
                                 StringComparison.OrdinalIgnoreCase) == 0;

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

            _text = text;
            _tokens = tokens;
            _defs = defs;
            _snapshot = snapshot;
        }
    }
}
