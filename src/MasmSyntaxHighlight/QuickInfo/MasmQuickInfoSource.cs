using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MasmSyntaxHighlight.Lexing;
using MasmSyntaxHighlight.Navigation;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Utilities;

namespace MasmSyntaxHighlight.QuickInfo
{
    /// <summary>Creates one <see cref="MasmQuickInfoSource"/> per MASM buffer.</summary>
    [Export(typeof(IAsyncQuickInfoSourceProvider))]
    [Name("MASM QuickInfo")]
    [ContentType(MasmContentTypes.ContentType)]
    [Order]
    internal sealed class MasmQuickInfoSourceProvider : IAsyncQuickInfoSourceProvider
    {
        [Import]
        internal ITextDocumentFactoryService DocumentFactory { get; set; }

        public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer)
        {
            if (textBuffer == null) return null;
            MasmDefinitionIndex index = MasmBufferServices.GetIndex(textBuffer, DocumentFactory);
            return textBuffer.Properties.GetOrCreateSingletonProperty(
                () => new MasmQuickInfoSource(textBuffer, index));
        }
    }

    /// <summary>
    /// Hovering an identifier (or a struct field, or a call target) shows what it is: the kind
    /// of symbol, its name, the line it is defined on, and - when it comes from an
    /// <c>INCLUDE</c>d file - which file. Resolution is shared with Go To Definition via
    /// <see cref="MasmDefinitionIndex"/>.
    /// </summary>
    internal sealed class MasmQuickInfoSource : IAsyncQuickInfoSource
    {
        private readonly ITextBuffer _buffer;
        private readonly MasmDefinitionIndex _index;

        internal MasmQuickInfoSource(ITextBuffer buffer, MasmDefinitionIndex index)
        {
            _buffer = buffer;
            _index = index;
        }

        public Task<QuickInfoItem> GetQuickInfoItemAsync(
            IAsyncQuickInfoSession session, CancellationToken cancellationToken)
        {
            SnapshotPoint? point = session.GetTriggerPoint(_buffer.CurrentSnapshot);
            if (point == null) return Task.FromResult<QuickInfoItem>(null);

            try
            {
                MasmSymbolDef? def = _index.ResolveForInfo(
                    point.Value.Snapshot, point.Value.Position, out SnapshotSpan symbolSpan);
                if (def == null) return Task.FromResult<QuickInfoItem>(null);

                ITrackingSpan applicableTo = point.Value.Snapshot.CreateTrackingSpan(
                    symbolSpan, SpanTrackingMode.EdgeInclusive);

                return Task.FromResult(
                    new QuickInfoItem(applicableTo, BuildContent(def.Value, point.Value.Snapshot)));
            }
            catch
            {
                return Task.FromResult<QuickInfoItem>(null);
            }
        }

        private object BuildContent(MasmSymbolDef def, ITextSnapshot snapshot)
        {
            var elements = new List<object>
            {
                new ClassifiedTextElement(
                    new ClassifiedTextRun(PredefinedClassificationTypeNames.Keyword, KindWord(def) + " "),
                    new ClassifiedTextRun(NameClassification(def.Kind), def.Name)),
            };

            string line = _index.GetDefinitionLineText(def, snapshot);
            if (!string.IsNullOrEmpty(line) &&
                !string.Equals(line, def.Name, StringComparison.Ordinal))
            {
                elements.Add(new ClassifiedTextElement(
                    new ClassifiedTextRun(PredefinedClassificationTypeNames.SymbolDefinition, line)));
            }

            if (def.FilePath != null)
            {
                elements.Add(new ClassifiedTextElement(
                    new ClassifiedTextRun(
                        PredefinedClassificationTypeNames.Comment,
                        "from " + Path.GetFileName(def.FilePath))));
            }

            return new ContainerElement(ContainerElementStyle.Stacked, elements);
        }

        private static string KindWord(MasmSymbolDef def)
        {
            switch (def.Kind)
            {
                case MasmTokenKind.ProcName:
                    return def.IsDeclaration ? "procedure (prototype)" : "procedure";
                case MasmTokenKind.TypeName:
                    return "type";
                case MasmTokenKind.ConstantName:
                    return "constant";
                case MasmTokenKind.DataName:
                    return "data";
                case MasmTokenKind.Label:
                    return def.IsGlobalLabel ? "global label" : "label";
                default:
                    return "symbol";
            }
        }

        private static string NameClassification(MasmTokenKind kind)
        {
            switch (kind)
            {
                case MasmTokenKind.ProcName:
                    return PredefinedClassificationTypeNames.SymbolReference;
                case MasmTokenKind.TypeName:
                    return PredefinedClassificationTypeNames.Type;
                default:
                    return PredefinedClassificationTypeNames.Identifier;
            }
        }

        public void Dispose() { }
    }
}
