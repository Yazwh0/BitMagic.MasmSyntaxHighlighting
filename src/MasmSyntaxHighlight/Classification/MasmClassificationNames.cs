namespace MasmSyntaxHighlight.Classification
{
    /// <summary>
    /// Canonical names for every classification (colour) type contributed by the extension.
    /// These strings are the keys shown under Tools > Options > Environment > Fonts and Colors.
    /// </summary>
    internal static class MasmClassificationNames
    {
        public const string Comment = "MASM/Comment";
        public const string String = "MASM/String";
        public const string Number = "MASM/Number";
        public const string Register = "MASM/Register";
        public const string Mnemonic = "MASM/Mnemonic";
        public const string Directive = "MASM/Directive";
        public const string DataType = "MASM/DataType";
        public const string Operator = "MASM/Operator";
        public const string Label = "MASM/Label";
    }
}
