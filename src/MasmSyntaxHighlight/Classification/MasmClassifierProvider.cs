using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace MasmSyntaxHighlight.Classification
{
    /// <summary>Creates one <see cref="MasmClassifier"/> per text buffer of the "masm" content type.</summary>
    [Export(typeof(IClassifierProvider))]
    [ContentType(MasmContentTypes.ContentType)]
    internal sealed class MasmClassifierProvider : IClassifierProvider
    {
        [Import]
        internal IClassificationTypeRegistryService ClassificationRegistry { get; set; }

        [Import]
        internal IStandardClassificationService StandardClassifications { get; set; }

        [Import]
        internal ITextDocumentFactoryService DocumentFactory { get; set; }

        public IClassifier GetClassifier(ITextBuffer textBuffer)
        {
            return textBuffer.Properties.GetOrCreateSingletonProperty(() =>
            {
                ITextDocument document = null;
                DocumentFactory?.TryGetTextDocument(textBuffer, out document);
                return new MasmClassifier(
                    textBuffer, document, ClassificationRegistry, StandardClassifications);
            });
        }
    }
}
