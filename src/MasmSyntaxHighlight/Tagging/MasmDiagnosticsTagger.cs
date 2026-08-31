using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using MasmSyntaxHighlight.Diagnostics;
using MasmSyntaxHighlight.Lexing;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace MasmSyntaxHighlight.Tagging
{
    /// <summary>
    /// Creates one <see cref="MasmDiagnosticsTagger"/> per MASM buffer and, once per open
    /// document, bridges its diagnostics into the Error List via <see cref="MasmErrorListDataSource"/>.
    /// </summary>
    [Export(typeof(ITaggerProvider))]
    [ContentType(MasmContentTypes.ContentType)]
    [TagType(typeof(IErrorTag))]
    internal sealed class MasmDiagnosticsTaggerProvider : ITaggerProvider
    {
        private readonly MasmErrorListDataSource _errorList;
        private readonly ITextDocumentFactoryService _documentFactory;

        [ImportingConstructor]
        public MasmDiagnosticsTaggerProvider(
            ITextDocumentFactoryService documentFactory,
            [Import(AllowDefault = true)] MasmErrorListDataSource errorList)
        {
            _documentFactory = documentFactory;
            _errorList = errorList;
            if (_documentFactory != null && _errorList != null)
                _documentFactory.TextDocumentDisposed += OnTextDocumentDisposed;
        }

        public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag
        {
            if (buffer == null) return null;

            var tagger = buffer.Properties.GetOrCreateSingletonProperty(
                () => new MasmDiagnosticsTagger(buffer));

            if (_errorList != null && _documentFactory != null)
                buffer.Properties.GetOrCreateSingletonProperty(
                    typeof(MasmErrorListBridge),
                    () => new MasmErrorListBridge(buffer, tagger, _errorList, _documentFactory));

            return tagger as ITagger<T>;
        }

        private void OnTextDocumentDisposed(object sender, TextDocumentEventArgs e)
        {
            ITextBuffer buffer = e.TextDocument?.TextBuffer;
            if (buffer == null) return;

            if (buffer.Properties.TryGetProperty(typeof(MasmErrorListBridge), out MasmErrorListBridge bridge))
            {
                bridge.Dispose();
                buffer.Properties.RemoveProperty(typeof(MasmErrorListBridge));
            }
        }
    }

    /// <summary>
    /// Squiggles the few structural mistakes <c>ml64</c> itself rejects and that need no symbol
    /// resolution: a block opener with no matching closer, a closer with no opener, and a
    /// <c>ENDP</c> / <c>ENDS</c> whose name does not match its <c>PROC</c> / <c>STRUCT</c>.
    /// Deliberately conservative - nothing here fires on merely unknown names. The same list
    /// feeds the Error List (see <see cref="MasmErrorListBridge"/>).
    /// </summary>
    internal sealed class MasmDiagnosticsTagger : ITagger<IErrorTag>
    {
        private readonly ITextBuffer _buffer;
        private ITextSnapshot _snapshot;
        private IReadOnlyList<MasmDiagnostic> _diagnostics = Array.Empty<MasmDiagnostic>();

        internal MasmDiagnosticsTagger(ITextBuffer buffer)
        {
            _buffer = buffer;
            _buffer.Changed += OnBufferChanged;
            Recompute(_buffer.CurrentSnapshot);
        }

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        /// <summary>Raised (after <see cref="TagsChanged"/>) whenever the diagnostic set is recomputed.</summary>
        internal event EventHandler DiagnosticsChanged;

        internal IReadOnlyList<MasmDiagnostic> CurrentDiagnostics => _diagnostics;

        internal ITextSnapshot CurrentSnapshot => _snapshot;

        private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
            => Recompute(e.After);

        private void Recompute(ITextSnapshot snapshot)
        {
            _diagnostics = MasmStructuralAnalyzer.Analyse(snapshot.GetText());
            _snapshot = snapshot;

            TagsChanged?.Invoke(
                this, new SnapshotSpanEventArgs(new SnapshotSpan(snapshot, 0, snapshot.Length)));
            DiagnosticsChanged?.Invoke(this, EventArgs.Empty);
        }

        public IEnumerable<ITagSpan<IErrorTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            if (spans.Count == 0) yield break;

            ITextSnapshot snapshot = spans[0].Snapshot;
            if (!ReferenceEquals(_snapshot, snapshot)) Recompute(snapshot);
            if (_diagnostics.Count == 0) yield break;

            int requestStart = spans[0].Start.Position;
            int requestEnd = spans[spans.Count - 1].End.Position;

            foreach (MasmDiagnostic d in _diagnostics)
            {
                if (d.End <= requestStart || d.Start >= requestEnd) continue;
                if (d.End > snapshot.Length) continue;

                var span = new SnapshotSpan(snapshot, d.Start, d.End - d.Start);
                yield return new TagSpan<IErrorTag>(
                    span, new ErrorTag(PredefinedErrorTypeNames.SyntaxError, d.Message));
            }
        }

        // Block-structure analysis lives in MasmStructuralAnalyzer (pure over a string, unit-tested).
    }

    /// <summary>
    /// Lives once per open MASM document (in the buffer's property bag). Owns a
    /// <see cref="MasmErrorListFactory"/>, keeps it in step with the tagger's diagnostics, and is
    /// disposed when the document closes.
    /// </summary>
    internal sealed class MasmErrorListBridge : IDisposable
    {
        private readonly MasmDiagnosticsTagger _tagger;
        private readonly MasmErrorListDataSource _source;
        private readonly MasmErrorListFactory _factory;
        private readonly ITextDocument _document;
        private bool _disposed;

        internal MasmErrorListBridge(
            ITextBuffer buffer, MasmDiagnosticsTagger tagger,
            MasmErrorListDataSource source, ITextDocumentFactoryService documentFactory)
        {
            _tagger = tagger;
            _source = source;
            _factory = new MasmErrorListFactory();

            documentFactory.TryGetTextDocument(buffer, out _document);

            _source.AddFactory(_factory);
            _tagger.DiagnosticsChanged += OnDiagnosticsChanged;
            Push();
        }

        private void OnDiagnosticsChanged(object sender, EventArgs e) => Push();

        private void Push()
        {
            if (_disposed) return;
            _factory.Update(_document?.FilePath, _tagger.CurrentDiagnostics);
            _source.NotifyChanged(_factory);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _tagger.DiagnosticsChanged -= OnDiagnosticsChanged;
            _source.RemoveFactory(_factory);
        }
    }
}
