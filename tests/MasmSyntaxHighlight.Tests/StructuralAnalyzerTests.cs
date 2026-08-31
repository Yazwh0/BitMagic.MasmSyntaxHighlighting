using System.Linq;
using MasmSyntaxHighlight.Tagging;
using Xunit;

namespace MasmSyntaxHighlight.Tests
{
    public class StructuralAnalyzerTests
    {
        [Fact]
        public void A_balanced_file_produces_no_diagnostics()
        {
            const string src =
                "Foo PROC\n" +
                "  .if rax == 0\n" +
                "    ret\n" +
                "  .endif\n" +
                "Foo ENDP\n" +
                "pt STRUCT\n  x dq ?\npt ENDS\n";

            Assert.Empty(MasmStructuralAnalyzer.Analyse(src));
        }

        [Fact]
        public void A_proc_with_no_endp_is_flagged_once_at_the_opener()
        {
            var d = MasmStructuralAnalyzer.Analyse("    Foo PROC\n  ret\n");
            var one = Assert.Single(d);
            Assert.Contains("ENDP", one.Message);
            Assert.Equal(0, one.Line);
            Assert.Equal(8, one.Column);   // anchored on the 'PROC' keyword - column 8 of line 0
        }

        [Fact]
        public void A_closer_with_no_opener_is_flagged()
        {
            var d = MasmStructuralAnalyzer.Analyse("  ret\nFoo ENDP\n");
            var one = Assert.Single(d);
            Assert.Contains("no matching PROC", one.Message);
            Assert.Equal(1, one.Line);
        }

        [Fact]
        public void A_mismatched_ENDS_name_is_flagged()
        {
            var d = MasmStructuralAnalyzer.Analyse("pt STRUCT\n  x dq ?\npoint ENDS\n");
            Assert.Contains(d, x => x.Message.Contains("does not match"));
        }

        [Fact]
        public void An_interleaved_block_reports_the_stranded_inner_opener()
        {
            const string src =
                "Foo PROC\n" +
                "bar STRUCT\n" +
                "  x dq ?\n" +
                "Foo ENDP\n";

            var d = MasmStructuralAnalyzer.Analyse(src);
            Assert.Contains(d, x => x.Message.Contains("'STRUCT' has no matching ENDS"));
        }

        [Fact]
        public void Diagnostics_come_back_sorted_by_start_offset()
        {
            var d = MasmStructuralAnalyzer.Analyse("a ENDP\nb ENDS\n");
            Assert.Equal(2, d.Count);
            Assert.True(d[0].Start < d[1].Start);
        }

        [Fact]
        public void Empty_input_is_fine()
        {
            Assert.Empty(MasmStructuralAnalyzer.Analyse(""));
            Assert.Empty(MasmStructuralAnalyzer.Analyse(null));
        }
    }
}
