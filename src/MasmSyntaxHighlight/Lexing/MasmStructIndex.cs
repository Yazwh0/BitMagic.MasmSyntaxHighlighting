using System;
using System.Collections.Generic;

namespace MasmSyntaxHighlight.Lexing
{
    /// <summary>One <c>STRUCT</c> / <c>UNION</c> definition and its member list.</summary>
    internal sealed class MasmStructDef
    {
        public readonly string Name;

        /// <summary>File the struct is declared in; <c>null</c> means the buffer being edited.</summary>
        public readonly string FilePath;

        /// <summary>Character offset of the struct's name token.</summary>
        public readonly int Start;

        public readonly IReadOnlyList<MasmStructField> Fields;

        public MasmStructDef(string name, string filePath, int start, IReadOnlyList<MasmStructField> fields)
        {
            Name = name;
            FilePath = filePath;
            Start = start;
            Fields = fields ?? Array.Empty<MasmStructField>();
        }
    }

    /// <summary>One member inside a <see cref="MasmStructDef"/>.</summary>
    internal readonly struct MasmStructField
    {
        public readonly string Name;

        /// <summary>The declared type word, or <c>null</c> for a primitive (<c>db</c>..<c>real10</c>).
        /// Used to walk chained access (<c>a.b.c</c>) when the type names another struct.</summary>
        public readonly string TypeName;

        /// <summary>Character offset of the member's name token, in its declaring file.</summary>
        public readonly int Start;

        public MasmStructField(string name, string typeName, int start)
        {
            Name = name;
            TypeName = typeName;
            Start = start;
        }
    }

    /// <summary>The struct types plus the variable-&gt;struct-type bindings found in one file.</summary>
    internal sealed class MasmStructModel
    {
        public readonly List<MasmStructDef> Structs;

        /// <summary>Variable name -&gt; the struct type it was declared with (unfiltered - the
        /// type may not name a real struct; callers validate once the full type set is known).</summary>
        public readonly Dictionary<string, string> Instances;

        public MasmStructModel(List<MasmStructDef> structs, Dictionary<string, string> instances)
        {
            Structs = structs ?? new List<MasmStructDef>();
            Instances = instances ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public static MasmStructModel Empty =>
            new MasmStructModel(new List<MasmStructDef>(),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A line-oriented pass over the lexer's tokens that records every <c>NAME STRUCT</c> /
    /// <c>NAME UNION</c> block (nested and anonymous blocks included) with its members, plus
    /// obvious <c>var TYPE &lt;&gt;</c> / <c>LOCAL var:TYPE</c> struct-instance declarations.
    /// Feeds member completion after <c>.</c> - the flat symbol pass in <see cref="MasmSymbols"/>
    /// has no notion of what belongs to which struct.
    /// </summary>
    internal static class MasmStructIndex
    {
        private const int MaxStructDepth = 32;

        public static MasmStructModel Collect(IReadOnlyList<MasmToken> tokens, string text, string filePath)
        {
            var structs = new List<MasmStructDef>();
            var instances = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (tokens == null || tokens.Count == 0 || string.IsNullOrEmpty(text))
                return new MasmStructModel(structs, instances);

            var open = new Stack<Builder>();

            int i = 0;
            while (i < tokens.Count)
            {
                MasmToken first = tokens[i];
                MasmToken? second = null;
                if (i + 1 < tokens.Count &&
                    !HasNewlineBetween(text, tokens[i].End, tokens[i + 1].Start))
                    second = tokens[i + 1];

                HandleLine(text, tokens, i, first, second, open, structs, instances, filePath);

                int n = i + 1;
                while (n < tokens.Count && !HasNewlineBetween(text, tokens[n - 1].End, tokens[n].Start))
                    n++;
                i = n;
            }

            while (open.Count > 0)
                FinishStruct(open, structs, filePath);

            return new MasmStructModel(structs, instances);
        }

        private static void HandleLine(
            string text, IReadOnlyList<MasmToken> tokens, int idx,
            MasmToken first, MasmToken? second, Stack<Builder> open,
            List<MasmStructDef> structs, Dictionary<string, string> instances, string filePath)
        {
            string fw = Word(text, first);

            // ---- close:  "NAME ENDS"  or bare "ENDS"
            bool bareEnds = first.Kind == MasmTokenKind.Directive && Eq(fw, "ends");
            bool namedEnds = second.HasValue && second.Value.Kind == MasmTokenKind.Directive
                             && Eq(Word(text, second.Value), "ends");
            if (bareEnds || namedEnds)
            {
                FinishStruct(open, structs, filePath);
                return;
            }

            // ---- open:  "NAME STRUCT" / "NAME UNION" (nested allowed) or an anonymous "STRUCT"
            if (second.HasValue && second.Value.Kind == MasmTokenKind.Directive
                && IsStructKeyword(Word(text, second.Value)) && IsNameShaped(text, first))
            {
                if (open.Count < MaxStructDepth)
                    open.Push(new Builder(fw, first.Start));
                else
                    open.Push(new Builder(null, first.Start)); // over-deep: keep balance, drop the type
                return;
            }
            if (first.Kind == MasmTokenKind.Directive && IsStructKeyword(fw))
            {
                open.Push(new Builder(null, first.Start));
                return;
            }

            // ---- member line inside a struct
            if (open.Count > 0)
            {
                if (IsFieldName(text, first) && second.HasValue)
                {
                    MasmToken t = second.Value;
                    string tw = Word(text, t);
                    if (t.Kind == MasmTokenKind.DataType || MasmKeywords.DataTypes.Contains(tw))
                        open.Peek().Fields.Add(new MasmStructField(fw, null, first.Start));
                    else if (IsNameShaped(text, t))
                        open.Peek().Fields.Add(new MasmStructField(fw, tw, first.Start));
                }
                return;
            }

            // ---- top-level instance:  "var TYPE ..."
            if (IsNameShaped(text, first) && second.HasValue && IsNameShaped(text, second.Value))
                instances[fw] = Word(text, second.Value);

            // ---- "LOCAL var:TYPE, var2:TYPE2"
            if (first.Kind == MasmTokenKind.Directive && Eq(fw, "local"))
                ScanLocals(text, tokens, idx, instances);
        }

        private static void ScanLocals(
            string text, IReadOnlyList<MasmToken> tokens, int localIdx, Dictionary<string, string> instances)
        {
            for (int j = localIdx + 1; j + 2 < tokens.Count; j++)
            {
                if (HasNewlineBetween(text, tokens[j - 1].End, tokens[j].Start)) break;
                if (tokens[j].Kind != MasmTokenKind.Operator || text[tokens[j].Start] != ':') continue;

                MasmToken name = tokens[j - 1];
                MasmToken type = tokens[j + 1];
                if (IsNameShaped(text, name) && IsNameShaped(text, type))
                    instances[Word(text, name)] = Word(text, type);
            }
        }

        private static void FinishStruct(Stack<Builder> open, List<MasmStructDef> structs, string filePath)
        {
            if (open.Count == 0) return;
            Builder b = open.Pop();

            if (b.Name == null)
            {
                if (open.Count > 0) open.Peek().Fields.AddRange(b.Fields); // flatten anonymous block
                return;
            }

            structs.Add(new MasmStructDef(b.Name, filePath, b.Start, b.Fields));

            // a nested named struct is also a member of its parent, of its own type
            if (open.Count > 0)
                open.Peek().Fields.Add(new MasmStructField(b.Name, b.Name, b.Start));
        }

        private sealed class Builder
        {
            public readonly string Name;   // null == anonymous nested block
            public readonly int Start;
            public readonly List<MasmStructField> Fields = new List<MasmStructField>();

            public Builder(string name, int start)
            {
                Name = name;
                Start = start;
            }
        }

        // ---------------------------------------------------------------- helpers

        private static bool IsStructKeyword(string w) => Eq(w, "struct") || Eq(w, "struc") || Eq(w, "union");

        private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static string Word(string text, MasmToken t) => text.Substring(t.Start, t.Length);

        private static bool IsNameShaped(string text, MasmToken t)
        {
            switch (t.Kind)
            {
                case MasmTokenKind.Identifier:
                case MasmTokenKind.DataName:
                case MasmTokenKind.TypeName:
                case MasmTokenKind.Label:
                case MasmTokenKind.ConstantName:
                    break;
                default:
                    return false;
            }

            char c = text[t.Start];
            if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_' || c == '@' || c == '$' || c == '?'))
                return false;

            string w = Word(text, t);
            return !MasmKeywords.Registers.Contains(w)
                   && !MasmKeywords.Mnemonics.Contains(w)
                   && !MasmKeywords.Directives.Contains(w)
                   && !MasmKeywords.DataTypes.Contains(w)
                   && !MasmKeywords.Operators.Contains(w);
        }

        /// <summary>
        /// A struct member name. More permissive than <see cref="IsNameShaped"/>: the first word
        /// on a line inside a <c>STRUCT</c> body is a field even when it collides with a register
        /// or mnemonic (<c>flags</c>, <c>si</c>, <c>or</c>...) - context rules it out as an
        /// instruction. Directives, data-type keywords and pure-operator words are still rejected
        /// (<c>align</c>, <c>db</c>, an anonymous field with no name).
        /// </summary>
        private static bool IsFieldName(string text, MasmToken t)
        {
            switch (t.Kind)
            {
                case MasmTokenKind.Identifier:
                case MasmTokenKind.DataName:
                case MasmTokenKind.TypeName:
                case MasmTokenKind.Label:
                case MasmTokenKind.ConstantName:
                case MasmTokenKind.Register:
                case MasmTokenKind.Mnemonic:
                    break;
                default:
                    return false;
            }

            char c = text[t.Start];
            if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_' || c == '@' || c == '$' || c == '?'))
                return false;

            string w = Word(text, t);
            return !MasmKeywords.Directives.Contains(w)
                   && !MasmKeywords.DataTypes.Contains(w)
                   && !MasmKeywords.Operators.Contains(w);
        }

        private static bool HasNewlineBetween(string text, int from, int to)
        {
            if (from < 0) from = 0;
            if (to > text.Length) to = text.Length;
            for (int p = from; p < to; p++)
                if (text[p] == '\n') return true;
            return false;
        }
    }
}
