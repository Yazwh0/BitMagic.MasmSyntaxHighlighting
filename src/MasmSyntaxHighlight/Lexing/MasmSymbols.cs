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
