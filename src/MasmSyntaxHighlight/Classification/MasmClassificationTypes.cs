using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace MasmSyntaxHighlight.Classification
{
    /// <summary>
    /// MEF exports that register each MASM classification type with the editor.
    /// The actual colours live in <see cref="MasmClassificationFormats"/>.
    /// </summary>
    internal static class MasmClassificationTypes
    {
#pragma warning disable 649 // assigned by the MEF composition engine

        [Export(typeof(ClassificationTypeDefinition))]
        [Name(MasmClassificationNames.Comment)]
        internal static ClassificationTypeDefinition Comment;

        [Export(typeof(ClassificationTypeDefinition))]
        [Name(MasmClassificationNames.String)]
        internal static ClassificationTypeDefinition String;

        [Export(typeof(ClassificationTypeDefinition))]
        [Name(MasmClassificationNames.Number)]
        internal static ClassificationTypeDefinition Number;

        [Export(typeof(ClassificationTypeDefinition))]
        [Name(MasmClassificationNames.Register)]
        internal static ClassificationTypeDefinition Register;

        [Export(typeof(ClassificationTypeDefinition))]
        [Name(MasmClassificationNames.Mnemonic)]
        internal static ClassificationTypeDefinition Mnemonic;

        [Export(typeof(ClassificationTypeDefinition))]
        [Name(MasmClassificationNames.Directive)]
        internal static ClassificationTypeDefinition Directive;

        [Export(typeof(ClassificationTypeDefinition))]
        [Name(MasmClassificationNames.DataType)]
        internal static ClassificationTypeDefinition DataType;

        [Export(typeof(ClassificationTypeDefinition))]
        [Name(MasmClassificationNames.Operator)]
        internal static ClassificationTypeDefinition Operator;

        [Export(typeof(ClassificationTypeDefinition))]
        [Name(MasmClassificationNames.Label)]
        internal static ClassificationTypeDefinition Label;

#pragma warning restore 649
    }
}
