using Microsoft.VisualStudio.Text;

namespace MasmSyntaxHighlight.Navigation
{
    /// <summary>
    /// One <see cref="MasmDefinitionIndex"/> per buffer, shared by every navigation feature
    /// (Go To Definition, QuickInfo, reference highlighting, Peek, Find All References) so the
    /// buffer is lexed and its symbols collected once per snapshot rather than once per feature.
    /// </summary>
    internal static class MasmBufferServices
    {
        internal static MasmDefinitionIndex GetIndex(
            ITextBuffer buffer, ITextDocumentFactoryService documentFactory)
        {
            return buffer.Properties.GetOrCreateSingletonProperty(() =>
            {
                ITextDocument document = null;
                documentFactory?.TryGetTextDocument(buffer, out document);
                return new MasmDefinitionIndex(buffer, document);
            });
        }
    }
}
