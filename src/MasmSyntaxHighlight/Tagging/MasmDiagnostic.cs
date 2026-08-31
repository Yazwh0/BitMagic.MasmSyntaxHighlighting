namespace MasmSyntaxHighlight.Tagging
{
    /// <summary>One structural diagnostic: a span, its line/column (zero-based) and a message.</summary>
    internal readonly struct MasmDiagnostic
    {
        public readonly int Start;
        public readonly int End;
        public readonly int Line;
        public readonly int Column;
        public readonly string Message;

        public MasmDiagnostic(int start, int end, int line, int column, string message)
        {
            Start = start;
            End = end;
            Line = line;
            Column = column;
            Message = message;
        }
    }
}
