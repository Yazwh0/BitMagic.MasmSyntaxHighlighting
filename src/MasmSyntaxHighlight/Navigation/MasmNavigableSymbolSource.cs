using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using MasmSyntaxHighlight.Lexing;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace MasmSyntaxHighlight.Navigation
{
    /// <summary>
    /// Wires Ctrl+Click / "Go To Definition" navigation for MASM buffers. The editor asks for a
    /// navigable symbol at the mouse position; we resolve the identifier there to its definition
    /// (proc, struct/record, data, constant, or a proc-scoped label) via
    /// <see cref="MasmDefinitionIndex"/> and jump to it - in this file or in an <c>INCLUDE</c>d one.
    /// </summary>
    [Export(typeof(INavigableSymbolSourceProvider))]
    [Name("MASM Navigable Symbols")]
    [ContentType(MasmContentTypes.ContentType)]
    internal sealed class MasmNavigableSymbolSourceProvider : INavigableSymbolSourceProvider
    {
        [Import]
        internal ITextDocumentFactoryService DocumentFactory { get; set; }

        [Import(typeof(SVsServiceProvider))]
        internal IServiceProvider ServiceProvider { get; set; }

        [Import]
        internal IVsEditorAdaptersFactoryService AdapterFactory { get; set; }

        public INavigableSymbolSource TryCreateNavigableSymbolSource(ITextView textView, ITextBuffer buffer)
        {
            if (buffer == null) return null;

            MasmDefinitionIndex index = MasmBufferServices.GetIndex(buffer, DocumentFactory);
            return new MasmNavigableSymbolSource(textView, index, ServiceProvider, AdapterFactory);
        }
    }

    internal sealed class MasmNavigableSymbolSource : INavigableSymbolSource
    {
        private readonly ITextView _view;
        private readonly MasmDefinitionIndex _index;
        private readonly IServiceProvider _serviceProvider;
        private readonly IVsEditorAdaptersFactoryService _adapters;

        internal MasmNavigableSymbolSource(
            ITextView view, MasmDefinitionIndex index,
            IServiceProvider serviceProvider, IVsEditorAdaptersFactoryService adapters)
        {
            _view = view;
            _index = index;
            _serviceProvider = serviceProvider;
            _adapters = adapters;
        }

        public Task<INavigableSymbol> GetNavigableSymbolAsync(
            SnapshotSpan triggerSpan, CancellationToken cancellationToken)
        {
            INavigableSymbol symbol = null;
            try
            {
                MasmSymbolDef? def = _index.Resolve(
                    triggerSpan.Snapshot, triggerSpan.Start.Position, out SnapshotSpan span);
                if (def != null)
                    symbol = new MasmNavigableSymbol(span, def.Value, _view, _serviceProvider, _adapters);
            }
            catch
            {
                symbol = null;
            }
            return Task.FromResult(symbol);
        }

        public void Dispose() { }
    }

    internal sealed class MasmNavigableSymbol : INavigableSymbol
    {
        private readonly MasmSymbolDef _def;
        private readonly ITextView _view;
        private readonly IServiceProvider _serviceProvider;
        private readonly IVsEditorAdaptersFactoryService _adapters;

        internal MasmNavigableSymbol(
            SnapshotSpan symbolSpan, MasmSymbolDef def, ITextView view,
            IServiceProvider serviceProvider, IVsEditorAdaptersFactoryService adapters)
        {
            SymbolSpan = symbolSpan;
            _def = def;
            _view = view;
            _serviceProvider = serviceProvider;
            _adapters = adapters;
        }

        public SnapshotSpan SymbolSpan { get; }

        public IEnumerable<INavigableRelationship> Relationships
            => new[] { PredefinedNavigableRelationships.Definition };

        public void Navigate(INavigableRelationship relationship)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            MasmNavigator.Navigate(_def, _view, _serviceProvider, _adapters);
        }
    }
}
