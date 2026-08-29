using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MasmSyntaxHighlight.Lexing;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.NavigateTo.Interfaces;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.PatternMatching;

namespace MasmSyntaxHighlight.Navigation
{
    /// <summary>
    /// Feeds MASM procedures, structures, constants, data and module-scope labels into VS's
    /// <b>Go To All</b> / <b>Go To Symbol</b> search (Ctrl+T, Ctrl+;). Symbols are gathered from
    /// every <c>.asm</c> / <c>.inc</c> file under the solution (or repo) root, lexed once and
    /// cached with the <c>INCLUDE</c> index.
    /// </summary>
    [Export(typeof(INavigateToItemProviderFactory))]
    internal sealed class MasmNavigateToItemProviderFactory : INavigateToItemProviderFactory
    {
        [Import(typeof(SVsServiceProvider))]
        internal IServiceProvider ServiceProvider { get; set; }

        [Import]
        internal IVsEditorAdaptersFactoryService AdapterFactory { get; set; }

        [Import]
        internal IPatternMatcherFactory PatternMatcherFactory { get; set; }

        public bool TryCreateNavigateToItemProvider(
            IServiceProvider serviceProvider, out INavigateToItemProvider provider)
        {
            provider = new MasmNavigateToItemProvider(
                serviceProvider ?? ServiceProvider, AdapterFactory, PatternMatcherFactory);
            return true;
        }
    }

    internal sealed class MasmNavigateToItemProvider : INavigateToItemProvider
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IPatternMatcherFactory _patternMatcherFactory;
        private readonly INavigateToItemDisplayFactory _displayFactory;
        private CancellationTokenSource _cts;

        internal MasmNavigateToItemProvider(
            IServiceProvider serviceProvider,
            IVsEditorAdaptersFactoryService adapters,
            IPatternMatcherFactory patternMatcherFactory)
        {
            _serviceProvider = serviceProvider;
            _patternMatcherFactory = patternMatcherFactory;
            _displayFactory = new MasmNavigateToItemDisplayFactory(serviceProvider, adapters);
        }

        public void StartSearch(INavigateToCallback callback, string searchValue)
        {
            StopSearch();

            if (string.IsNullOrWhiteSpace(searchValue))
            {
                callback.Done();
                return;
            }

            var cts = new CancellationTokenSource();
            _cts = cts;
            CancellationToken token = cts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    IPatternMatcher matcher = _patternMatcherFactory.CreatePatternMatcher(
                        searchValue,
                        new PatternMatcherCreationOptions(
                            CultureInfo.CurrentCulture, PatternMatcherCreationFlags.None,
                            containerSplitCharacters: null));
                    if (matcher.HasInvalidPattern) return;

                    string root = await GetSearchRootAsync(token).ConfigureAwait(false);
                    List<MasmSymbolDef> defs = MasmIncludeIndex.CollectProjectDefs(root);

                    for (int i = 0; i < defs.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();

                        MasmSymbolDef def = defs[i];
                        PatternMatch? match = matcher.TryMatch(def.Name);
                        if (match == null) continue;

                        callback.AddItem(new NavigateToItem(
                            def.Name,
                            KindOf(def.Kind),
                            "MASM",
                            def.Name,
                            new MasmNavTarget(def.FilePath, def.Start, def.Length),
                            match.Value,
                            _displayFactory));

                        if ((i & 31) == 0)
                            callback.ReportProgress(i + 1, defs.Count);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch
                {
                    // a search failure should not tear down the Go To All dialog
                }
                finally
                {
                    callback.Done();
                }
            }, token);
        }

        public void StopSearch()
        {
            CancellationTokenSource cts = _cts;
            _cts = null;
            if (cts == null) return;
            try { cts.Cancel(); } catch { }
            cts.Dispose();
        }

        public void Dispose() => StopSearch();

        private async Task<string> GetSearchRootAsync(CancellationToken token)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
            try
            {
                if (_serviceProvider?.GetService(typeof(SVsSolution)) is IVsSolution solution &&
                    ErrorHandler.Succeeded(solution.GetSolutionInfo(out string dir, out _, out _)) &&
                    !string.IsNullOrEmpty(dir))
                    return dir;
            }
            catch
            {
                // no solution loaded, or the service is unavailable
            }
            return null;
        }

        private static string KindOf(MasmTokenKind kind)
        {
            switch (kind)
            {
                case MasmTokenKind.ProcName: return NavigateToItemKind.Method;
                case MasmTokenKind.TypeName: return NavigateToItemKind.Structure;
                case MasmTokenKind.ConstantName: return NavigateToItemKind.Constant;
                case MasmTokenKind.DataName: return NavigateToItemKind.Field;
                default: return NavigateToItemKind.OtherSymbol;
            }
        }
    }

    /// <summary>What a Go To All result points at - carried on <see cref="NavigateToItem.Tag"/>.</summary>
    internal sealed class MasmNavTarget
    {
        public readonly string FilePath;
        public readonly int Start;
        public readonly int Length;

        public MasmNavTarget(string filePath, int start, int length)
        {
            FilePath = filePath;
            Start = start;
            Length = length;
        }
    }

    internal sealed class MasmNavigateToItemDisplayFactory : INavigateToItemDisplayFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IVsEditorAdaptersFactoryService _adapters;

        internal MasmNavigateToItemDisplayFactory(
            IServiceProvider serviceProvider, IVsEditorAdaptersFactoryService adapters)
        {
            _serviceProvider = serviceProvider;
            _adapters = adapters;
        }

        public INavigateToItemDisplay CreateItemDisplay(NavigateToItem item)
            => new MasmNavigateToItemDisplay(item, _serviceProvider, _adapters);
    }

    internal sealed class MasmNavigateToItemDisplay : INavigateToItemDisplay
    {
        private static readonly ReadOnlyCollection<DescriptionItem> NoDescription =
            new ReadOnlyCollection<DescriptionItem>(Array.Empty<DescriptionItem>());

        private readonly NavigateToItem _item;
        private readonly IServiceProvider _serviceProvider;
        private readonly IVsEditorAdaptersFactoryService _adapters;

        internal MasmNavigateToItemDisplay(
            NavigateToItem item, IServiceProvider serviceProvider, IVsEditorAdaptersFactoryService adapters)
        {
            _item = item;
            _serviceProvider = serviceProvider;
            _adapters = adapters;
        }

        public Icon Glyph => null;

        public string Name => _item.Name;

        public string AdditionalInformation
            => _item.Tag is MasmNavTarget t && !string.IsNullOrEmpty(t.FilePath)
                ? Path.GetFileName(t.FilePath)
                : string.Empty;

        public string Description => string.Empty;

        public ReadOnlyCollection<DescriptionItem> DescriptionItems => NoDescription;

        public void NavigateTo()
        {
            if (!(_item.Tag is MasmNavTarget t) || string.IsNullOrEmpty(t.FilePath)) return;

            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                MasmNavigator.NavigateToFile(t.FilePath, t.Start, t.Length, _serviceProvider, _adapters);
            });
        }
    }
}
