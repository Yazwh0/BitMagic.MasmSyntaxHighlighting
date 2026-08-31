using System;
using System.Collections.Generic;
using MasmSyntaxHighlight.Lexing;

namespace MasmSyntaxHighlight.Tagging
{
    /// <summary>
    /// The token-only block-structure check behind the squiggles and the Error List: a block
    /// opener with no matching closer, a closer with no opener, and an <c>ENDP</c> / <c>ENDS</c>
    /// whose name does not match its <c>PROC</c> / <c>STRUCT</c>. Deliberately conservative -
    /// no symbol resolution, so nothing here fires on a merely unknown name. Pure over a
    /// <see cref="string"/> so it can be unit-tested without an editor.
    /// </summary>
    internal static class MasmStructuralAnalyzer
    {
        private sealed class OpenBlock
        {
            public string Keyword;
            public string Family;
            public MasmToken KeywordToken;
            public string Name;
            public MasmToken NameToken;
        }

        public static IReadOnlyList<MasmDiagnostic> Analyse(string text)
        {
            text = text ?? string.Empty;
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
                    Add(diagnostics, text, token.Start, token.End,
                        $"'{keyword.ToUpperInvariant()}' has no matching {OpenerWord(closeFamily)}.");
                    continue;
                }

                for (int s = stack.Count - 1; s > match; s--)
                {
                    OpenBlock stranded = stack[s];
                    Add(diagnostics, text, stranded.KeywordToken.Start, stranded.KeywordToken.End,
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
                        Add(diagnostics, text, closerNameToken.Start, closerNameToken.End,
                            $"'{keyword.ToUpperInvariant()}' name '{closerName}' does not match " +
                            $"'{opener.Keyword.ToUpperInvariant()}' name '{opener.Name}'.");
                    }
                }
            }

            foreach (OpenBlock left in stack)
                Add(diagnostics, text, left.KeywordToken.Start, left.KeywordToken.End,
                    $"'{left.Keyword.ToUpperInvariant()}' has no matching {CloserWord(left.Family)}.");

            diagnostics.Sort((a, b) => a.Start.CompareTo(b.Start));
            return diagnostics;
        }

        private static void Add(
            List<MasmDiagnostic> into, string text, int start, int end, string message)
        {
            int safeStart = Math.Min(Math.Max(start, 0), text.Length);
            LineColumn(text, safeStart, out int line, out int column);
            into.Add(new MasmDiagnostic(start, end, line, column, message));
        }

        /// <summary>Zero-based line and column of <paramref name="pos"/>, counting <c>\n</c>
        /// (the column is measured from the last <c>\n</c>, so a <c>\r</c> before it is included).</summary>
        private static void LineColumn(string text, int pos, out int line, out int column)
        {
            line = 0;
            int lineStart = 0;
            int limit = Math.Min(pos, text.Length);
            for (int i = 0; i < limit; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                    lineStart = i + 1;
                }
            }
            column = pos - lineStart;
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
}
