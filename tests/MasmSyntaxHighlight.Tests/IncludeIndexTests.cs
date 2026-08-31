using System;
using System.IO;
using System.Linq;
using MasmSyntaxHighlight.Lexing;
using Xunit;

namespace MasmSyntaxHighlight.Tests
{
    /// <summary>
    /// <see cref="MasmIncludeIndex"/> reads real files from disk, so these tests build a throwaway
    /// project tree under the temp directory. A marker <c>.sln</c> keeps its root-finding scan
    /// inside that tree.
    /// </summary>
    public sealed class IncludeIndexTests : IDisposable
    {
        private readonly string _dir;

        public IncludeIndexTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "masmtests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, "marker.sln"), "");
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        private string Write(string name, string content)
        {
            string path = Path.Combine(_dir, name);
            File.WriteAllText(path, content);
            return path;
        }

        [Fact]
        public void Definitions_from_an_included_file_are_visible_to_the_includer()
        {
            Write("types.inc",
                "Widget STRUCT\n  w dd ?\nWidget ENDS\n" +
                "GfxInit PROTO\n");
            string main = Write("main.asm",
                "INCLUDE types.inc\n" +
                "            .code\n" +
                "Start PROC\n  ret\nStart ENDP\n" +
                "            END\n");

            var defs = MasmIncludeIndex.CollectDefs(main, File.ReadAllText(main));
            Assert.Contains(defs, d => d.Name == "Widget" && d.Kind == MasmTokenKind.TypeName);
            Assert.Contains(defs, d => d.Name == "GfxInit");
        }

        [Fact]
        public void The_struct_model_follows_the_include_graph()
        {
            Write("uart.inc",
                "uart STRUCT\n" +
                "  divisor_latch dd ?\n" +
                "  ctl           fifo_ctl <>\n" +
                "uart ENDS\n" +
                "fifo_ctl STRUCT\n  depth dd ?\nfifo_ctl ENDS\n");
            string main = Write("io.asm",
                "INCLUDE uart.inc\n" +
                "            .code\n" +
                "Reset PROC\n  mov eax, [rcx].uart.divisor_latch\n  ret\nReset ENDP\n" +
                "            END\n");

            var model = MasmIncludeIndex.CollectStructModel(main, File.ReadAllText(main));

            var uart = model.Structs.SingleOrDefault(s => s.Name == "uart");
            Assert.NotNull(uart);
            Assert.Contains(uart.Fields, f => f.Name == "divisor_latch");
            Assert.Contains(model.Structs, s => s.Name == "fifo_ctl");
        }

        [Fact]
        public void A_buffer_with_no_backing_file_yields_nothing()
        {
            Assert.Empty(MasmIncludeIndex.CollectDefs(null, "x EQU 1"));
            Assert.Empty(MasmIncludeIndex.CollectStructModel(null, "x EQU 1").Structs);
        }
    }
}
