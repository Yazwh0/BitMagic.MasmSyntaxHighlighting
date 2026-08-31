using System.Linq;
using MasmSyntaxHighlight.Lexing;
using Xunit;

namespace MasmSyntaxHighlight.Tests
{
    public class StructIndexTests
    {
        private static MasmStructModel Model(string src)
            => MasmStructIndex.Collect(new MasmLexer(src).Tokenize(), src, null);

        private const string Sample =
            "uart STRUCT\n" +
            "    divisor_latch  dd ?\n" +
            "    flags          dd ?\n" +            // 'flags' also names a register
            "    ctl            fifo_ctl <>\n" +     // struct-typed member, type defined later
            "uart ENDS\n" +
            "\n" +
            "fifo_ctl STRUCT\n" +
            "    depth  dd ?\n" +
            "fifo_ctl ENDS\n" +
            "\n" +
            "point STRUCT\n" +
            "    x  dq ?\n" +
            "    inner STRUCT\n" +
            "        lo dd ?\n" +
            "    inner ENDS\n" +
            "point ENDS\n" +
            "\n" +
            "            .data\n" +
            "state   uart  <>\n" +
            "\n" +
            "Go PROC\n" +
            "    LOCAL tmp:point\n" +
            "    ret\n" +
            "Go ENDP\n";

        [Fact]
        public void Captures_every_struct_and_union()
        {
            var names = Model(Sample).Structs.Select(s => s.Name).ToList();
            Assert.Contains("uart", names);
            Assert.Contains("fifo_ctl", names);
            Assert.Contains("point", names);
        }

        [Fact]
        public void Member_list_is_in_source_order_and_includes_a_register_named_field()
        {
            var uart = Model(Sample).Structs.Single(s => s.Name == "uart");
            Assert.Equal(new[] { "divisor_latch", "flags", "ctl" }, uart.Fields.Select(f => f.Name));
        }

        [Fact]
        public void Primitive_members_have_no_type_struct_typed_members_keep_it()
        {
            var uart = Model(Sample).Structs.Single(s => s.Name == "uart");
            Assert.Null(uart.Fields.Single(f => f.Name == "divisor_latch").TypeName);
            Assert.Equal("fifo_ctl", uart.Fields.Single(f => f.Name == "ctl").TypeName);
        }

        [Fact]
        public void A_nested_struct_is_both_a_top_level_type_and_a_member_of_its_parent()
        {
            var m = Model(Sample);
            Assert.Contains(m.Structs, s => s.Name == "inner");

            var point = m.Structs.Single(s => s.Name == "point");
            Assert.Equal("inner", point.Fields.Single(f => f.Name == "inner").TypeName);
        }

        [Fact]
        public void Instance_bindings_come_from_data_declarations_and_LOCAL()
        {
            var inst = Model(Sample).Instances;
            Assert.Equal("uart", inst["state"]);
            Assert.Equal("point", inst["tmp"]);
        }

        [Fact]
        public void A_struct_missing_its_ENDS_still_yields_the_fields_seen_so_far()
        {
            const string src = "widget STRUCT\n  a dd ?\n  b dd ?\n";
            var w = Model(src).Structs.Single(s => s.Name == "widget");
            Assert.Equal(new[] { "a", "b" }, w.Fields.Select(f => f.Name));
        }

        [Fact]
        public void Union_members_are_collected_like_a_struct()
        {
            const string src =
                "u UNION\n" +
                "  asDword  dd ?\n" +
                "  asBytes  db 4 dup(?)\n" +
                "u ENDS\n";
            var u = Model(src).Structs.Single(s => s.Name == "u");
            Assert.Contains(u.Fields, f => f.Name == "asDword");
            Assert.Contains(u.Fields, f => f.Name == "asBytes");
        }

        [Fact]
        public void No_structs_means_an_empty_model_not_a_null()
        {
            var m = Model("mov rax, 1\nret\n");
            Assert.Empty(m.Structs);
            Assert.Empty(m.Instances);
        }
    }
}
