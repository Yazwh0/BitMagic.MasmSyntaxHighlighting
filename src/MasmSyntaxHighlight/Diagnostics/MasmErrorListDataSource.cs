using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using MasmSyntaxHighlight.Tagging;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.TableManager;

namespace MasmSyntaxHighlight.Diagnostics
{
    /// <summary>
    /// Publishes the structural diagnostics from <see cref="MasmDiagnosticsTagger"/> to the
    /// <b>Error List</b>. One <see cref="MasmErrorListFactory"/> per open MASM document; the
    /// tagger drives its content, this source relays factory changes to whichever sinks the
    /// Error List tool window has subscribed.
    /// </summary>
    [Export(typeof(MasmErrorListDataSource))]
    internal sealed class MasmErrorListDataSource : ITableDataSource
    {
        private readonly object _gate = new object();
        private readonly List<ITableDataSink> _sinks = new List<ITableDataSink>();
        private readonly List<MasmErrorListFactory> _factories = new List<MasmErrorListFactory>();

        [ImportingConstructor]
        public MasmErrorListDataSource(ITableManagerProvider tableManagerProvider)
        {
            try
            {
                ITableManager manager = tableManagerProvider.GetTableManager(StandardTables.ErrorsTable);
                manager.AddSource(
                    this,
                    StandardTableKeyNames.ErrorSeverity,
                    StandardTableKeyNames.ErrorSource,
                    StandardTableKeyNames.ErrorCode,
                    StandardTableKeyNames.Text,
                    StandardTableKeyNames.DocumentName,
                    StandardTableKeyNames.Line,
                    StandardTableKeyNames.Column,
                    StandardTableKeyNames.BuildTool);
            }
            catch
            {
                // no table manager (unlikely) - diagnostics still show as editor squiggles
            }
        }

        public string SourceTypeIdentifier => StandardTableDataSources.ErrorTableDataSource;

        public string Identifier => "MASM.Diagnostics";

        public string DisplayName => "MASM";

        public IDisposable Subscribe(ITableDataSink sink)
        {
            lock (_gate)
            {
                _sinks.Add(sink);
                foreach (MasmErrorListFactory factory in _factories)
                    sink.AddFactory(factory, removeAllFactories: false);
            }
            return new Subscription(this, sink);
        }

        internal void AddFactory(MasmErrorListFactory factory)
        {
            lock (_gate)
            {
                _factories.Add(factory);
                foreach (ITableDataSink sink in _sinks)
                    sink.AddFactory(factory, removeAllFactories: false);
            }
        }

        internal void RemoveFactory(MasmErrorListFactory factory)
        {
            lock (_gate)
            {
                _factories.Remove(factory);
                foreach (ITableDataSink sink in _sinks)
                    sink.RemoveFactory(factory);
            }
        }

        internal void NotifyChanged(MasmErrorListFactory factory)
        {
            lock (_gate)
            {
                foreach (ITableDataSink sink in _sinks)
                {
                    sink.FactorySnapshotChanged(factory);
                    sink.IsStable = true;
                }
            }
        }

        private sealed class Subscription : IDisposable
        {
            private readonly MasmErrorListDataSource _owner;
            private readonly ITableDataSink _sink;

            public Subscription(MasmErrorListDataSource owner, ITableDataSink sink)
            {
                _owner = owner;
                _sink = sink;
            }

            public void Dispose()
            {
                lock (_owner._gate)
                    _owner._sinks.Remove(_sink);
            }
        }
    }

    /// <summary>The Error List's handle on one document's diagnostics; swaps in a new immutable
    /// <see cref="MasmErrorListSnapshot"/> each time the tagger re-analyses.</summary>
    internal sealed class MasmErrorListFactory : ITableEntriesSnapshotFactory
    {
        private MasmErrorListSnapshot _snapshot = new MasmErrorListSnapshot(0, null, Array.Empty<MasmDiagnostic>());

        public int CurrentVersionNumber => _snapshot.VersionNumber;

        public ITableEntriesSnapshot GetCurrentSnapshot() => _snapshot;

        public ITableEntriesSnapshot GetSnapshot(int versionNumber)
            => versionNumber == _snapshot.VersionNumber ? _snapshot : null;

        internal void Update(string documentPath, IReadOnlyList<MasmDiagnostic> diagnostics)
            => _snapshot = new MasmErrorListSnapshot(
                _snapshot.VersionNumber + 1, documentPath, diagnostics);

        public void Dispose() { }
    }

    internal sealed class MasmErrorListSnapshot : ITableEntriesSnapshot
    {
        private readonly string _documentPath;
        private readonly IReadOnlyList<MasmDiagnostic> _items;

        internal MasmErrorListSnapshot(
            int versionNumber, string documentPath, IReadOnlyList<MasmDiagnostic> items)
        {
            VersionNumber = versionNumber;
            _documentPath = documentPath;
            _items = items;
        }

        public int VersionNumber { get; }

        public int Count => _items.Count;

        public void StartCaching() { }

        public void StopCaching() { }

        public void Dispose() { }

        // A fresh snapshot every version - no attempt to map an entry to its successor.
        public int IndexOf(int currentIndex, ITableEntriesSnapshot newSnapshot) => -1;

        public bool TryGetValue(int index, string keyName, out object content)
        {
            content = null;
            if (index < 0 || index >= _items.Count) return false;

            MasmDiagnostic d = _items[index];
            switch (keyName)
            {
                case StandardTableKeyNames.ErrorSeverity:
                    content = __VSERRORCATEGORY.EC_ERROR;
                    return true;
                case StandardTableKeyNames.ErrorSource:
                    content = ErrorSource.Other;
                    return true;
                case StandardTableKeyNames.BuildTool:
                    content = "MASM";
                    return true;
                case StandardTableKeyNames.Text:
                    content = d.Message;
                    return true;
                case StandardTableKeyNames.DocumentName:
                    content = _documentPath;
                    return _documentPath != null;
                case StandardTableKeyNames.Line:
                    content = d.Line;      // zero-based, as the Error List expects
                    return true;
                case StandardTableKeyNames.Column:
                    content = d.Column;
                    return true;
                default:
                    return false;
            }
        }
    }
}
