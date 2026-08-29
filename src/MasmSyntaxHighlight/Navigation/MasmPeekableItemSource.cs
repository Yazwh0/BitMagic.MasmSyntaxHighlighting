using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading;
using MasmSyntaxHighlight.Lexing;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;

namespace MasmSyntaxHighlight.Navigation
{
    /// <summary>Creates one <see cref="MasmPeekableItemSource"/> per MASM buffer.</summary>
    [Export(typeof(IPeekableItemSourceProvider))]
    [Name("MASM Peek Definition")]
    [ContentType(MasmContentTypes.ContentType)]
    [SupportsStandaloneFiles(true)]
    [SupportsPeekRelationship("IsDefinedBy")]
    internal sealed class MasmPeekableItemSourceProvider : IPeekableItemSourceProvider
    {
        [Import]
        internal IPeekResultFactory PeekResultFactory { get; set; }

        [Import]
        internal ITextDocumentFactoryService DocumentFactory { get; set; }

        public IPeekableItemSource TryCreatePeekableItemSource(ITextBuffer textBuffer)
        {
            if (textBuffer == null) return null;
            MasmDefinitionIndex index = MasmBufferServices.GetIndex(textBuffer, DocumentFactory);
            return textBuffer.Properties.GetOrCreateSingletonProperty(
                () => new MasmPeekableItemSource(textBuffer, index, PeekResultFactory));
        }
    }

    /// <summary>
    /// Alt+F12 (Peek Definition) on an identifier: shows the defining line inline without leaving
    /// the file. Resolves the same symbol Go To Definition would, in this file or an
    /// <c>INCLUDE</c>d one.
    /// </summary>
    internal sealed class MasmPeekableItemSource : IPeekableItemSource
    {
        private readonly ITextBuffer _buffer;
        private readonly MasmDefinitionIndex _index;
        private readonly IPeekResultFactory _factory;

        internal MasmPeekableItemSource(
            ITextBuffer buffer, MasmDefinitionIndex index, IPeekResultFactory factory)
        {
            _buffer = buffer;
            _index = index;
            _factory = factory;
        }

        public void AugmentPeekSession(IPeekSession session, IList<IPeekableItem> peekableItems)
        {
            if (!string.Equals(session.RelationshipName, PredefinedPeekRelationships.Definitions.Name,
                               StringComparison.OrdinalIgnoreCase))
                return;

            SnapshotPoint? point = session.GetTriggerPoint(_buffer.CurrentSnapshot);
            if (point == null) return;

            try
            {
                MasmSymbolDef? def = _index.ResolveForInfo(
                    point.Value.Snapshot, point.Value.Position, out _);
                if (def == null) return;

                string path = def.Value.FilePath ?? _index.DocumentPath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

                if (!TryLocate(def.Value, point.Value.Snapshot, path,
                               out int line, out int startColumn, out int endColumn))
                    return;

                peekableItems.Add(new MasmPeekableItem(
                    def.Value.Name, path, line, startColumn, endColumn, _factory));
            }
            catch
            {
                // Peek must never throw into the editor
            }
        }

        private bool TryLocate(
            MasmSymbolDef def, ITextSnapshot snapshot, string path,
            out int line, out int startColumn, out int endColumn)
        {
            line = 0;
            startColumn = 0;
            endColumn = 0;

            if (def.FilePath == null)
            {
                if (def.Start > snapshot.Length) return false;
                ITextSnapshotLine snapLine = snapshot.GetLineFromLineNumber(
                    snapshot.GetLineNumberFromPosition(def.Start));
                line = snapLine.LineNumber;
                startColumn = def.Start - snapLine.Start.Position;
                endColumn = Math.Min(startColumn + def.Length,
                                     snapLine.End.Position - snapLine.Start.Position);
                return true;
            }

            string text = MasmSourceText.GetText(path);
            if (text == null || def.Start > text.Length) return false;

            MasmSourceText.GetLineColumn(text, def.Start, out line, out startColumn);
            endColumn = startColumn + def.Length;
            return true;
        }

        public void Dispose() { }
    }

    internal sealed class MasmPeekableItem : IPeekableItem
    {
        private readonly string _path;
        private readonly int _line;
        private readonly int _startColumn;
        private readonly int _endColumn;
        private readonly IPeekResultFactory _factory;

        internal MasmPeekableItem(
            string displayName, string path, int line, int startColumn, int endColumn,
            IPeekResultFactory factory)
        {
            DisplayName = displayName;
            _path = path;
            _line = line;
            _startColumn = startColumn;
            _endColumn = endColumn;
            _factory = factory;
        }

        public string DisplayName { get; }

        public IEnumerable<IPeekRelationship> Relationships
            => new IPeekRelationship[] { PredefinedPeekRelationships.Definitions };

        public IPeekResultSource GetOrCreateResultSource(string relationshipName)
            => new MasmPeekResultSource(_path, _line, _startColumn, _endColumn, _factory);
    }

    internal sealed class MasmPeekResultSource : IPeekResultSource
    {
        private readonly string _path;
        private readonly int _line;
        private readonly int _startColumn;
        private readonly int _endColumn;
        private readonly IPeekResultFactory _factory;

        internal MasmPeekResultSource(
            string path, int line, int startColumn, int endColumn, IPeekResultFactory factory)
        {
            _path = path;
            _line = line;
            _startColumn = startColumn;
            _endColumn = endColumn;
            _factory = factory;
        }

        public void FindResults(
            string relationshipName, IPeekResultCollection resultCollection,
            CancellationToken cancellationToken, IFindPeekResultsCallback callback)
        {
            if (!string.Equals(relationshipName, PredefinedPeekRelationships.Definitions.Name,
                               StringComparison.OrdinalIgnoreCase))
                return;

            string fileName = Path.GetFileName(_path);
            var display = new PeekResultDisplayInfo(
                label: fileName, labelTooltip: _path, title: fileName, titleTooltip: _path);

            IDocumentPeekResult result = _factory.Create(
                display,
                _path,
                _line, _startColumn,
                _line, _endColumn,
                _line, _startColumn);

            resultCollection.Add(result);
        }
    }
}
