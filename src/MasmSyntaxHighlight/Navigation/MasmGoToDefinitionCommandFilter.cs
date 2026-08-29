using System;
using System.ComponentModel.Composition;
using MasmSyntaxHighlight.Lexing;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using IServiceProvider = System.IServiceProvider;

namespace MasmSyntaxHighlight.Navigation
{
    /// <summary>
    /// Handles the keyboard / menu route to Go To Definition for MASM buffers - F12 and the editor
    /// context-menu items <em>Go To Definition</em>, <em>Go To Declaration</em> and
    /// <em>Go To Implementation</em> - by installing an <see cref="IOleCommandTarget"/> command
    /// filter on each text view. (Ctrl+Click is handled separately by
    /// <see cref="MasmNavigableSymbolSourceProvider"/>.) Resolution and the jump itself are shared
    /// with that path via <see cref="MasmDefinitionIndex"/> and <see cref="MasmNavigator"/>.
    /// </summary>
    [Export(typeof(IVsTextViewCreationListener))]
    [ContentType(MasmContentTypes.ContentType)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    [Name("MASM Go To Definition Command Filter")]
    internal sealed class MasmGoToDefinitionCommandFilterProvider : IVsTextViewCreationListener
    {
        [Import]
        internal IVsEditorAdaptersFactoryService AdapterFactory { get; set; }

        [Import]
        internal ITextDocumentFactoryService DocumentFactory { get; set; }

        [Import(typeof(SVsServiceProvider))]
        internal IServiceProvider ServiceProvider { get; set; }

        public void VsTextViewCreated(IVsTextView textViewAdapter)
        {
            IWpfTextView view = AdapterFactory?.GetWpfTextView(textViewAdapter);
            if (view == null) return;

            MasmDefinitionIndex index = MasmBufferServices.GetIndex(view.TextBuffer, DocumentFactory);
            var filter = new MasmGoToDefinitionCommandFilter(view, index, ServiceProvider, AdapterFactory);
            if (ErrorHandler.Succeeded(textViewAdapter.AddCommandFilter(filter, out IOleCommandTarget next)))
                filter.Next = next;
        }
    }

    internal sealed class MasmGoToDefinitionCommandFilter : IOleCommandTarget
    {
        private static readonly Guid Cmd97 = VSConstants.GUID_VSStandardCommandSet97;
        private static readonly Guid Cmd12 = VSConstants.CMDSETID.StandardCommandSet12_guid;

        private const uint GotoImplementation = 0x0200; // guidVSStd12:cmdidGoToImplementation

        private readonly IWpfTextView _view;
        private readonly MasmDefinitionIndex _index;
        private readonly IServiceProvider _serviceProvider;
        private readonly IVsEditorAdaptersFactoryService _adapters;

        internal IOleCommandTarget Next { get; set; }

        internal MasmGoToDefinitionCommandFilter(
            IWpfTextView view, MasmDefinitionIndex index,
            IServiceProvider serviceProvider, IVsEditorAdaptersFactoryService adapters)
        {
            _view = view;
            _index = index;
            _serviceProvider = serviceProvider;
            _adapters = adapters;
        }

        /// <summary>Recognises the command and reports whether it targets a declaration.</summary>
        private static bool IsNavCommand(ref Guid group, uint id, out bool preferDeclaration)
        {
            preferDeclaration = false;
            if (group == Cmd97)
            {
                if (id == (uint)VSConstants.VSStd97CmdID.GotoDefn) return true;
                if (id == (uint)VSConstants.VSStd97CmdID.GotoDecl) { preferDeclaration = true; return true; }
                return false;
            }
            return group == Cmd12 && id == GotoImplementation;
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            ThreadHelper.ThrowIfNotOnUIThread(); // command routing is always on the UI thread

            if (prgCmds != null && cCmds == 1 &&
                IsNavCommand(ref pguidCmdGroup, prgCmds[0].cmdID, out _))
            {
                prgCmds[0].cmdf = (uint)(OLECMDF.OLECMDF_SUPPORTED | OLECMDF.OLECMDF_ENABLED);
                return VSConstants.S_OK;
            }

            return Next != null
                ? Next.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText)
                : (int)Constants.OLECMDERR_E_NOTSUPPORTED;
        }

        public int Exec(
            ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            ThreadHelper.ThrowIfNotOnUIThread(); // command routing is always on the UI thread

            if (IsNavCommand(ref pguidCmdGroup, nCmdID, out bool preferDeclaration) &&
                TryGoTo(preferDeclaration))
            {
                return VSConstants.S_OK;
            }

            return Next != null
                ? Next.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut)
                : (int)Constants.OLECMDERR_E_NOTSUPPORTED;
        }

        private bool TryGoTo(bool preferDeclaration)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                SnapshotPoint caret = _view.Caret.Position.BufferPosition;
                MasmSymbolDef? def = _index.Resolve(
                    caret.Snapshot, caret.Position, out _, preferDeclaration);
                if (def == null) return false;

                MasmNavigator.Navigate(def.Value, _view, _serviceProvider, _adapters);
                return true;
            }
            catch
            {
                return false; // fall through to the next command target
            }
        }
    }
}
