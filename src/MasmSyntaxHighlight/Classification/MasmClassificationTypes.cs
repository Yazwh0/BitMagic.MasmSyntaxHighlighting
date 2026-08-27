using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace MasmSyntaxHighlight.Classification
{
    /// <summary>
    /// The single classification type the extension registers. It derives from the built-in
    /// "keyword" classification, so registers look like keywords out of the box while still
    /// getting their own "MASM Register" entry in Tools > Options > Fonts and Colors.
    /// </summary>
    internal static class MasmClassificationTypes
    {
#pragma warning disable 649 // assigned by the MEF composition engine

        [Export(typeof(ClassificationTypeDefinition))]
        [Name(MasmClassificationNames.Register)]
        [BaseDefinition(PredefinedClassificationTypeNames.Keyword)]
        internal static ClassificationTypeDefinition Register;

#pragma warning restore 649
    }
}
