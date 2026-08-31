using System.Collections.Generic;
using System.Linq;
using MasmSyntaxHighlight.Lexing;
using Xunit;

namespace MasmSyntaxHighlight.Tests
{
    public class LexerTests
    {
        private static List<MasmToken> Lex(string s) => new MasmLexer(s).Tokenize();

        private static string Text(string src, MasmToken t) => src.Substring(t.Start, t.Length);

        [Fact]
        public void Mnemonic_and_registers_are_classified()
        {
            const string src = "    mov rax, rbx";
            var toks = Lex(src);

            Assert.Equal(MasmTokenKind.Mnemonic, toks[0].Kind);
            Assert.Equal("mov", Text(src, toks[0]));
            Assert.Contains(toks, t => t.Kind == MasmTokenKind.Register && Text(src, t) == "rax");
            Assert.Contains(toks, t => t.Kind == MasmTokenKind.Register && Text(src, t) == "rbx");
        }

        [Fact]
        public void Line_comment_runs_to_end_of_line()
        {
            const string src = "mov rax, 1 ; load one\n nop";
            var comment = Lex(src).Single(t => t.Kind == MasmTokenKind.Comment);
            Assert.Equal("; load one", Text(src, comment));
        }

        [Fact]
        public void Code_label_with_colon_is_a_label()
        {
            const string src = "next:\n  jmp next";
            var toks = Lex(src);
            Assert.Equal(MasmTokenKind.Label, toks[0].Kind);
            Assert.Equal("next", Text(src, toks[0]));
        }

        [Fact]
        public void Leading_dot_directive_is_a_directive()
        {
            const string src = "    .code";
            var toks = Lex(src);
            Assert.Equal(MasmTokenKind.Directive, toks[0].Kind);
            Assert.Equal(".code", Text(src, toks[0]));
        }

        [Fact]
        public void Member_access_dot_splits_into_operator_then_identifier()
        {
            const string src = "mov eax, [rcx].uart.divisor_latch";
            var toks = Lex(src);

            int firstDot = toks.FindIndex(t => t.Kind == MasmTokenKind.Operator && Text(src, t) == ".");
            Assert.True(firstDot >= 0, "expected a '.' operator token");
            Assert.Equal(MasmTokenKind.Identifier, toks[firstDot + 1].Kind);
            Assert.Equal("uart", Text(src, toks[firstDot + 1]));

            // and a second member hop
            Assert.Equal(MasmTokenKind.Operator, toks[firstDot + 2].Kind);
            Assert.Equal(".", Text(src, toks[firstDot + 2]));
            Assert.Equal("divisor_latch", Text(src, toks[firstDot + 3]));
        }

        [Fact]
        public void String_literal_keeps_a_doubled_quote()
        {
            const string src = "db 'it''s fine', 0";
            var str = Lex(src).First(t => t.Kind == MasmTokenKind.String);
            Assert.Equal("'it''s fine'", Text(src, str));
        }

        [Fact]
        public void Comment_directive_block_hides_the_code_inside_it()
        {
            const string src = "COMMENT !\n  mov rax, 1\n!\n  nop";
            var toks = Lex(src);

            Assert.Contains(toks, t => t.Kind == MasmTokenKind.Comment);
            Assert.DoesNotContain(toks, t => t.Kind == MasmTokenKind.Mnemonic && Text(src, t) == "mov");
            Assert.Contains(toks, t => t.Kind == MasmTokenKind.Mnemonic && Text(src, t) == "nop");
        }

        [Theory]
        [InlineData("0FFh")]
        [InlineData("1010b")]
        [InlineData("777o")]
        [InlineData("0x1F")]
        [InlineData("3.14159")]
        [InlineData("42")]
        public void Numbers_of_various_radixes_are_one_number_token(string literal)
        {
            string src = "dd " + literal;
            var number = Lex(src).Single(t => t.Kind == MasmTokenKind.Number);
            Assert.Equal(literal, Text(src, number));
        }

        [Fact]
        public void Line_continuation_keeps_the_statement_going()
        {
            const string src = "    mulps  xmm0, \\\n           xmmword ptr [rdx]";
            var toks = Lex(src);
            // the operand after the backslash-newline is still read as part of the statement
            Assert.Contains(toks, t => t.Kind == MasmTokenKind.Register && Text(src, t) == "xmm0");
            Assert.Contains(toks, t => t.Kind == MasmTokenKind.DataType && Text(src, t) == "xmmword");
        }

        [Fact]
        public void Tokens_are_ordered_and_non_overlapping()
        {
            const string src =
                "AddNumbers PROC\n" +
                "    xor rax, rax\n" +
                "    ret\n" +
                "AddNumbers ENDP\n";
            var toks = Lex(src);

            for (int i = 1; i < toks.Count; i++)
                Assert.True(toks[i].Start >= toks[i - 1].End,
                    $"token {i} (@{toks[i].Start}) overlaps the previous token (ends @{toks[i - 1].End})");
        }
    }
}
