namespace MasmSyntaxHighlight.Lexing
{
    /// <summary>A single classified span of source text (absolute character offsets).</summary>
    internal readonly struct MasmToken
    {
        public readonly int Start;
        public readonly int Length;
        public readonly MasmTokenKind Kind;

        public MasmToken(int start, int length, MasmTokenKind kind)
        {
            Start = start;
            Length = length;
            Kind = kind;
        }

        public int End => Start + Length;
    }
}
