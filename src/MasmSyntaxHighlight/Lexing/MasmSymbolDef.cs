namespace MasmSyntaxHighlight.Lexing
{
    /// <summary>
    /// One definition site: the name, what kind of thing it is, where it is (character offset
    /// within <see cref="FilePath"/>, or the buffer being edited when that is <c>null</c>), and -
    /// for labels - which proc it is scoped to. Produced by
    /// <see cref="MasmSymbols.CollectDefinitionsWithLocations"/> and consumed by Go To Definition.
    /// </summary>
    internal readonly struct MasmSymbolDef
    {
        public readonly string Name;
        public readonly MasmTokenKind Kind;
        public readonly int Start;
        public readonly int Length;

        /// <summary>File the definition lives in; <c>null</c> means the buffer being edited.</summary>
        public readonly string FilePath;

        /// <summary>
        /// Character offset of the enclosing <c>PROC</c>'s name token when this is a proc-local
        /// label, otherwise <c>-1</c> (module scope). Only comparable within a single file.
        /// </summary>
        public readonly int EnclosingProcStart;

        /// <summary>A <c>::</c> label - module scope even though it is written inside a proc.</summary>
        public readonly bool IsGlobalLabel;

        /// <summary>A forward declaration (<c>PROTO</c>) rather than the implementation.</summary>
        public readonly bool IsDeclaration;

        public MasmSymbolDef(
            string name, MasmTokenKind kind, int start, int length, string filePath,
            int enclosingProcStart, bool isGlobalLabel, bool isDeclaration)
        {
            Name = name;
            Kind = kind;
            Start = start;
            Length = length;
            FilePath = filePath;
            EnclosingProcStart = enclosingProcStart;
            IsGlobalLabel = isGlobalLabel;
            IsDeclaration = isDeclaration;
        }

        public int End => Start + Length;

        /// <summary>A label confined to one proc - not reachable from elsewhere or from other files.</summary>
        public bool IsProcLocal => EnclosingProcStart >= 0 && !IsGlobalLabel;
    }
}
