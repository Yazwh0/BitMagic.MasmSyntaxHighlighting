using System;
using System.Collections.Generic;

namespace MasmSyntaxHighlight.Lexing
{
    /// <summary>
    /// A whole-document pass that records every definition (<c>PROC</c> / <c>STRUCT</c> /
    /// <c>EQU</c> / data / label name) and then re-classifies later <em>references</em> to
    /// those names - a plain identifier that matches a known symbol is coloured like the
    /// definition (a call target as a method, <c>[rbx].Point.x</c> as a type then a field,
    /// and so on). Definitions from <c>INCLUDE</c>d files are supplied via <c>external</c>.
    /// </summary>
    internal static class MasmSymbols
    {
        /// <summary>Token kinds that introduce a named symbol.</summary>
        private static bool IsDefinition(MasmTokenKind kind) => Rank(kind) >= 0;

        /// <summary>
        /// Precedence when a name has more than one definition (in this file, or across
        /// <c>INCLUDE</c>s): a type or procedure name wins over a data / constant / label of the
        /// same name - a field named after the struct it points to (<c>zimodem qword ?</c>)
        /// should still colour as the type where it is used as one (<c>[r12].zimodem.x</c>).
        /// </summary>
        private static int Rank(MasmTokenKind kind)
        {
            switch (kind)
            {
                case MasmTokenKind.TypeName: return 3;
                case MasmTokenKind.ProcName: return 2;
                case MasmTokenKind.ConstantName:
                case MasmTokenKind.DataName:
                case MasmTokenKind.Label:
                    return 1;
                default:
                    return -1;
            }
        }

        private static void Offer(Dictionary<string, MasmTokenKind> map, string name, MasmTokenKind kind)
        {
            if (!map.TryGetValue(name, out MasmTokenKind existing) || Rank(kind) > Rank(existing))
                map[name] = kind;
        }

        /// <summary>
        /// Adds <paramref name="name"/> to <paramref name="map"/>, keeping the higher-ranked kind
        /// when the name is already present (see <see cref="Rank"/>). Used when merging definitions
        /// gathered from several <c>INCLUDE</c>d files, where a struct type and a same-named field
        /// can both appear.
        /// </summary>
        public static void Merge(Dictionary<string, MasmTokenKind> map, string name, MasmTokenKind kind)
            => Offer(map, name, kind);

        /// <summary>Builds a name -&gt; kind map of every definition in <paramref name="tokens"/>.</summary>
        public static Dictionary<string, MasmTokenKind> CollectDefinitions(
            IReadOnlyList<MasmToken> tokens, string text)
        {
            var symbols = new Dictionary<string, MasmTokenKind>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < tokens.Count; i++)
            {
                MasmToken token = tokens[i];
                if (!IsDefinition(token.Kind)) continue;
                Offer(symbols, text.Substring(token.Start, token.Length), token.Kind);
            }
            return symbols;
        }

        /// <summary>
        /// Like <see cref="CollectDefinitions"/> but keeps every definition's location (for Go To
        /// Definition). Labels carry the offset of their enclosing <c>PROC</c> name so a reused
        /// local label (<c>next:</c> in two different procs) resolves to the one in the caller's
        /// proc; a <c>::</c> label and everything outside a proc is module scope.
        /// <paramref name="filePath"/> is stamped onto each result (<c>null</c> for the live buffer).
        /// </summary>
        public static List<MasmSymbolDef> CollectDefinitionsWithLocations(
            IReadOnlyList<MasmToken> tokens, string text, string filePath)
        {
            var defs = new List<MasmSymbolDef>();
            int procStart = -1; // name-token Start of the proc we are inside, or -1

            for (int i = 0; i < tokens.Count; i++)
            {
                MasmToken token = tokens[i];

                if (token.Kind == MasmTokenKind.Directive)
                {
                    if (KeywordIs(text, token, "proc") &&
                        i > 0 && tokens[i - 1].Kind == MasmTokenKind.ProcName)
                        procStart = tokens[i - 1].Start;
                    else if (KeywordIs(text, token, "endp"))
                        procStart = -1;
                    continue;
                }

                if (!IsDefinition(token.Kind)) continue;

                // The lexer also hands out Label / ProcName kinds to *references* - the operand
                // of a call/invoke or a jmp/jCC (`call helper`, `jl next`). Keep only tokens
                // whose text actually has definition shape: `name:` / `name ::`, or `name` then
                // a definition keyword (PROC, STRUCT, EQU, db, ...).
                bool colonLabel = token.Kind == MasmTokenKind.Label
                                  && token.End < text.Length && text[token.End] == ':';
                bool global = colonLabel
                              && token.End + 1 < text.Length && text[token.End + 1] == ':';
                if (!colonLabel && !HasDefinitionFollower(text, token.Kind, token.End))
                    continue;

                bool isLabel = token.Kind == MasmTokenKind.Label;
                int scope = (isLabel && !global) ? procStart : -1;
                bool declaration = token.Kind == MasmTokenKind.ProcName
                                   && FollowerWordEquals(text, token.End, "proto");

                defs.Add(new MasmSymbolDef(
                    text.Substring(token.Start, token.Length), token.Kind,
                    token.Start, token.Length, filePath, scope, global, declaration));
            }

            return defs;
        }

        private static bool KeywordIs(string text, MasmToken token, string keyword)
            => token.Length == keyword.Length
               && string.Compare(text, token.Start, keyword, 0, keyword.Length,
                                 StringComparison.OrdinalIgnoreCase) == 0;

        /// <summary>
        /// True when the word immediately after <paramref name="from"/> (past spaces/tabs) is a
        /// keyword that marks the preceding identifier as a definition of <paramref name="kind"/> -
        /// the same test the lexer's <c>ClassifyDefinitionName</c> applies.
        /// </summary>
        private static bool HasDefinitionFollower(string text, MasmTokenKind kind, int from)
        {
            int p = from;
            if (p >= text.Length || (text[p] != ' ' && text[p] != '\t')) return false;
            while (p < text.Length && (text[p] == ' ' || text[p] == '\t')) p++;
            if (p >= text.Length) return false;

            if (text[p] == '=') return kind == MasmTokenKind.ConstantName;

            int s = p;
            if (text[p] == '.') p++;
            if (p >= text.Length || !IsIdentPart(text[p])) return false;
            while (p < text.Length && IsIdentPart(text[p])) p++;
            string follower = text.Substring(s, p - s);

            switch (kind)
            {
                case MasmTokenKind.ProcName:
                    return MasmKeywords.ProcDefinitionFollowers.Contains(follower);
                case MasmTokenKind.TypeName:
                    return MasmKeywords.TypeDefinitionFollowers.Contains(follower);
                case MasmTokenKind.ConstantName:
                    return MasmKeywords.ConstantDefinitionFollowers.Contains(follower);
                case MasmTokenKind.DataName:
                    return MasmKeywords.DataDefinitionFollowers.Contains(follower);
                case MasmTokenKind.Label:
                    return MasmKeywords.DefinitionFollowers.Contains(follower);
                default:
                    return false;
            }
        }

        private static bool FollowerWordEquals(string text, int from, string keyword)
        {
            int p = from;
            while (p < text.Length && (text[p] == ' ' || text[p] == '\t')) p++;
            if (p + keyword.Length > text.Length) return false;
            if (string.Compare(text, p, keyword, 0, keyword.Length,
                               StringComparison.OrdinalIgnoreCase) != 0)
                return false;
            int end = p + keyword.Length;
            return end >= text.Length || !IsIdentPart(text[end]);
        }

        private static bool IsIdentPart(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
            || c == '_' || c == '@' || c == '$' || c == '?';

        /// <summary>
        /// Returns a token list in which every <see cref="MasmTokenKind.Identifier"/> that
        /// matches a definition - in this document, or in <paramref name="external"/> (included
        /// files) - carries that definition's kind. On a clash the higher-ranked kind wins (see
        /// <see cref="Rank"/>), otherwise the local definition. The input list is returned
        /// unchanged when there is nothing to resolve.
        /// </summary>
        public static List<MasmToken> ResolveReferences(
            List<MasmToken> tokens,
            string text,
            IReadOnlyDictionary<string, MasmTokenKind> external = null)
        {
            Dictionary<string, MasmTokenKind> symbols = CollectDefinitions(tokens, text);

            if (external != null && external.Count > 0)
            {
                // an included type / proc still wins over a same-named local data field
                foreach (KeyValuePair<string, MasmTokenKind> pair in external)
                    Offer(symbols, pair.Key, pair.Value);
            }

            if (symbols.Count == 0) return tokens;

            var resolved = new List<MasmToken>(tokens.Count);
            foreach (MasmToken token in tokens)
            {
                if (token.Kind == MasmTokenKind.Identifier &&
                    symbols.TryGetValue(text.Substring(token.Start, token.Length), out MasmTokenKind kind))
                {
                    resolved.Add(new MasmToken(token.Start, token.Length, kind));
                }
                else
                {
                    resolved.Add(token);
                }
            }

            return resolved;
        }
    }
}
