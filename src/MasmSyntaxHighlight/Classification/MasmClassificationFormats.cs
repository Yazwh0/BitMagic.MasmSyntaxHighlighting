using System.ComponentModel.Composition;
using System.Windows.Media;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace MasmSyntaxHighlight.Classification
{
    // Each class below defines the default appearance of one MASM classification type.
    // Colours are mid-tone so they remain readable on both the light and dark VS themes.
    // Users can override any of them in
    //   Tools > Options > Environment > Fonts and Colors  (items named "MASM ...").

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = MasmClassificationNames.Comment)]
    [Name(MasmClassificationNames.Comment)]
    [UserVisible(true)]
    internal sealed class MasmCommentFormat : ClassificationFormatDefinition
    {
        public MasmCommentFormat()
        {
            DisplayName = "MASM Comment";
            ForegroundColor = Color.FromRgb(0x3E, 0x8E, 0x41);
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = MasmClassificationNames.String)]
    [Name(MasmClassificationNames.String)]
    [UserVisible(true)]
    internal sealed class MasmStringFormat : ClassificationFormatDefinition
    {
        public MasmStringFormat()
        {
            DisplayName = "MASM String";
            ForegroundColor = Color.FromRgb(0xC7, 0x62, 0x1E);
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = MasmClassificationNames.Number)]
    [Name(MasmClassificationNames.Number)]
    [UserVisible(true)]
    internal sealed class MasmNumberFormat : ClassificationFormatDefinition
    {
        public MasmNumberFormat()
        {
            DisplayName = "MASM Number";
            ForegroundColor = Color.FromRgb(0x2E, 0x8B, 0x87);
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = MasmClassificationNames.Register)]
    [Name(MasmClassificationNames.Register)]
    [UserVisible(true)]
    internal sealed class MasmRegisterFormat : ClassificationFormatDefinition
    {
        public MasmRegisterFormat()
        {
            DisplayName = "MASM Register";
            ForegroundColor = Color.FromRgb(0x1F, 0x7F, 0xC4);
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = MasmClassificationNames.Mnemonic)]
    [Name(MasmClassificationNames.Mnemonic)]
    [UserVisible(true)]
    internal sealed class MasmMnemonicFormat : ClassificationFormatDefinition
    {
        public MasmMnemonicFormat()
        {
            DisplayName = "MASM Instruction Mnemonic";
            ForegroundColor = Color.FromRgb(0x7B, 0x3F, 0xA0);
            IsBold = true;
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = MasmClassificationNames.Directive)]
    [Name(MasmClassificationNames.Directive)]
    [UserVisible(true)]
    internal sealed class MasmDirectiveFormat : ClassificationFormatDefinition
    {
        public MasmDirectiveFormat()
        {
            DisplayName = "MASM Directive";
            ForegroundColor = Color.FromRgb(0xB0, 0x2A, 0x6B);
            IsBold = true;
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = MasmClassificationNames.DataType)]
    [Name(MasmClassificationNames.DataType)]
    [UserVisible(true)]
    internal sealed class MasmDataTypeFormat : ClassificationFormatDefinition
    {
        public MasmDataTypeFormat()
        {
            DisplayName = "MASM Data Type";
            ForegroundColor = Color.FromRgb(0x7A, 0x6A, 0x00);
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = MasmClassificationNames.Operator)]
    [Name(MasmClassificationNames.Operator)]
    [UserVisible(true)]
    internal sealed class MasmOperatorFormat : ClassificationFormatDefinition
    {
        public MasmOperatorFormat()
        {
            DisplayName = "MASM Operator";
            ForegroundColor = Color.FromRgb(0x80, 0x80, 0x80);
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = MasmClassificationNames.Label)]
    [Name(MasmClassificationNames.Label)]
    [UserVisible(true)]
    internal sealed class MasmLabelFormat : ClassificationFormatDefinition
    {
        public MasmLabelFormat()
        {
            DisplayName = "MASM Label";
            ForegroundColor = Color.FromRgb(0x5B, 0x7A, 0x9D);
        }
    }
}
