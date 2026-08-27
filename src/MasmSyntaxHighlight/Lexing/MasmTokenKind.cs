namespace MasmSyntaxHighlight.Lexing
{
    /// <summary>The kind of a lexical token produced by <see cref="MasmLexer"/>.</summary>
    internal enum MasmTokenKind
    {
        /// <summary>Plain identifier - not coloured.</summary>
        Identifier,
        Comment,
        String,
        Number,
        Register,
        Mnemonic,
        Directive,
        DataType,
        Operator,
        /// <summary>A code label (<c>bar:</c>), a jump target, or a <c>LABEL</c> / <c>SEGMENT</c> name.</summary>
        Label,
        /// <summary>A procedure or macro name, or a <c>call</c> / <c>invoke</c> target.</summary>
        ProcName,
        /// <summary>A <c>STRUCT</c> / <c>RECORD</c> / <c>UNION</c> / <c>TYPEDEF</c> name.</summary>
        TypeName,
        /// <summary>A data variable name (the identifier before <c>db</c>..<c>dq</c>, <c>BYTE</c>..<c>REAL10</c>).</summary>
        DataName,
        /// <summary>A constant name (the identifier before <c>EQU</c> / <c>=</c> / <c>TEXTEQU</c> / <c>CATSTR</c>...).</summary>
        ConstantName,
    }
}
