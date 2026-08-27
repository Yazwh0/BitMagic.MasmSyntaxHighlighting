namespace MasmSyntaxHighlight.Classification
{
    /// <summary>
    /// Names of classification types the extension defines itself. Every other MASM token maps
    /// onto a built-in Visual Studio classification - see <see cref="MasmClassifier"/>.
    /// </summary>
    internal static class MasmClassificationNames
    {
        /// <summary>
        /// x86-64 registers. Visual Studio has no built-in "register" classification and the
        /// identifier/variable classifications have no distinct default colour, so this is the
        /// one classification the extension adds. It inherits the "keyword" appearance until
        /// the user overrides the "MASM Register" entry in Fonts and Colors.
        /// </summary>
        public const string Register = "MASM/Register";
    }
}
