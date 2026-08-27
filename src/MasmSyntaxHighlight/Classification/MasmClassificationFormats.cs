using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace MasmSyntaxHighlight.Classification
{
    /// <summary>
    /// Fonts and Colors entry for the one classification the extension adds. No colour is set:
    /// registers inherit the "keyword" appearance from the base classification until the user
    /// picks a colour for the "MASM Register" item themselves.
    /// </summary>
    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = MasmClassificationNames.Register)]
    [Name(MasmClassificationNames.Register)]
    [UserVisible(true)]
    internal sealed class MasmRegisterFormat : ClassificationFormatDefinition
    {
        public MasmRegisterFormat()
        {
            DisplayName = "MASM Register";
        }
    }
}
