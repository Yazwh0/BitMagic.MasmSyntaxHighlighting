using System;
using System.Collections.Generic;
using System.Linq;
using MasmSyntaxHighlight.Lexing;
using Xunit;

namespace MasmSyntaxHighlight.Tests
{
    public class ReferenceResolutionTests
    {
        private static List<MasmToken> Resolve(
            string src, IReadOnlyDictionary<string, MasmTokenKind> external = null)
            => MasmSymbols.ResolveReferences(new MasmLexer(src).Tokenize(), src, external);

        private static MasmTokenKind KindOf(string src, string word, int occurrence)
        {
            int seen = 0;
            foreach (MasmToken t in Resolve(src))
            {
                if (src.Substring(t.Start, t.Length) != word) continue;
                if (seen++ == occurrence) return t.Kind;
            }
            throw new Xunit.Sdk.XunitException($"'{word}' #{occurrence} not found");
        }

        [Fact]
        public void A_struct_name_in_an_include_filename_is_not_recoloured_as_the_struct()
        {
            const string src =
                "uart    STRUCT\n" +
                "count   dq      ?\n" +
                "uart    ENDS\n" +
                "        include uart.asm\n";

            // occurrence 2 is the 'uart' in "include uart.asm" - a file name, not a type reference
            Assert.Equal(MasmTokenKind.Identifier, KindOf(src, "uart", occurrence: 2));
        }

        [Fact]
        public void Includelib_operand_is_protected_too()
        {
            const string src =
                "uart    STRUCT\n" +
                "uart    ENDS\n" +
                "        includelib uart.lib\n";

            Assert.Equal(MasmTokenKind.Identifier, KindOf(src, "uart", occurrence: 2));
        }

        [Fact]
        public void An_included_struct_named_in_the_include_filename_is_not_recoloured()
        {
            // the real case: the struct lives in uart.asm, another file INCLUDEs it
            const string src = "        include uart.asm\n        ret\n";
            var external = new Dictionary<string, MasmTokenKind>(StringComparer.OrdinalIgnoreCase)
            {
                ["uart"] = MasmTokenKind.TypeName,
            };

            MasmToken uart = Resolve(src, external)
                .Single(t => src.Substring(t.Start, t.Length) == "uart");
            Assert.Equal(MasmTokenKind.Identifier, uart.Kind);
        }

        [Fact]
        public void A_genuine_type_reference_after_an_include_line_is_still_resolved()
        {
            const string src =
                "uart    STRUCT\n" +
                "count   dq      ?\n" +
                "uart    ENDS\n" +
                "        include uart.asm\n" +
                "        mov rax, [rbx].uart.count\n";

            // the include line must not switch resolution off for the rest of the file
            MasmToken last = Resolve(src).Last(t => src.Substring(t.Start, t.Length) == "uart");
            Assert.Equal(MasmTokenKind.TypeName, last.Kind);
        }

        [Fact]
        public void A_struct_reference_resolves_to_the_type_when_there_is_no_include()
        {
            const string src =
                "uart    STRUCT\n" +
                "count   dq      ?\n" +
                "uart    ENDS\n" +
                "        mov rax, [rbx].uart.count\n";

            MasmToken last = Resolve(src).Last(t => src.Substring(t.Start, t.Length) == "uart");
            Assert.Equal(MasmTokenKind.TypeName, last.Kind);
        }
    }
}
