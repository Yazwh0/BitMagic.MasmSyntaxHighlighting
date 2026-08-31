using System.Collections.Generic;
using System.Linq;
using MasmSyntaxHighlight.Lexing;
using Xunit;

namespace MasmSyntaxHighlight.Tests
{
    public class SymbolTests
    {
        private static List<MasmSymbolDef> Defs(string src)
            => MasmSymbols.CollectDefinitionsWithLocations(new MasmLexer(src).Tokenize(), src, null);

        [Fact]
        public void Proc_struct_constant_and_data_names_are_captured_with_kinds()
        {
            const string src =
                "MAX     EQU     10h\n" +
                "Greet   db      'hi', 0\n" +
                "pt      STRUCT\n" +
                "x       dq      ?\n" +
                "pt      ENDS\n" +
                "Foo     PROC\n" +
                "        ret\n" +
                "Foo     ENDP\n";

            var d = Defs(src);
            Assert.Contains(d, s => s.Name == "MAX" && s.Kind == MasmTokenKind.ConstantName);
            Assert.Contains(d, s => s.Name == "Greet" && s.Kind == MasmTokenKind.DataName);
            Assert.Contains(d, s => s.Name == "pt" && s.Kind == MasmTokenKind.TypeName);
            Assert.Contains(d, s => s.Name == "Foo" && s.Kind == MasmTokenKind.ProcName);

            // exactly one proc def - the name on the ENDP line is not a second definition
            Assert.Single(d, s => s.Name == "Foo");
        }

        [Fact]
        public void A_reused_proc_local_label_binds_to_a_different_proc_each_time()
        {
            const string src =
                "A PROC\n" +
                "next:\n" +
                "  jmp next\n" +
                "A ENDP\n" +
                "B PROC\n" +
                "next:\n" +
                "  jmp next\n" +
                "B ENDP\n";

            var nexts = Defs(src).Where(s => s.Name == "next" && s.Kind == MasmTokenKind.Label).ToList();
            Assert.Equal(2, nexts.Count);
            Assert.All(nexts, n => Assert.True(n.IsProcLocal));
            Assert.NotEqual(nexts[0].EnclosingProcStart, nexts[1].EnclosingProcStart);
        }

        [Fact]
        public void A_double_colon_label_is_module_scope_not_proc_local()
        {
            const string src =
                "A PROC\n" +
                "shared::\n" +
                "  ret\n" +
                "A ENDP\n";

            var g = Defs(src).Single(s => s.Name == "shared");
            Assert.True(g.IsGlobalLabel);
            Assert.False(g.IsProcLocal);
        }

        [Fact]
        public void Proto_is_a_declaration_the_proc_body_is_not()
        {
            const string src =
                "MyFunc  PROTO\n" +
                "MyFunc  PROC\n" +
                "        ret\n" +
                "MyFunc  ENDP\n";

            var d = Defs(src).Where(s => s.Name == "MyFunc").ToList();
            Assert.Contains(d, s => s.IsDeclaration);
            Assert.Contains(d, s => !s.IsDeclaration);
        }

        [Fact]
        public void A_call_target_is_a_reference_not_a_definition()
        {
            const string src =
                "A PROC\n" +
                "  call Helper\n" +
                "A ENDP\n";

            Assert.DoesNotContain(Defs(src), s => s.Name == "Helper");
        }

        [Fact]
        public void Definition_offsets_point_at_the_name_token()
        {
            const string src = "Widget  PROC\n  ret\nWidget  ENDP\n";
            var proc = Defs(src).Single(s => s.Name == "Widget");
            Assert.Equal(0, proc.Start);
            Assert.Equal("Widget", src.Substring(proc.Start, proc.Length));
        }
    }
}
