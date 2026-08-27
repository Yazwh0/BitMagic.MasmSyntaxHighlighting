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
        Label,
    }
}
