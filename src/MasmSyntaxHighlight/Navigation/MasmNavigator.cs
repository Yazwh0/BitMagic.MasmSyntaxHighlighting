using System;
using System.IO;
using MasmSyntaxHighlight.Lexing;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;

namespace MasmSyntaxHighlight.Navigation
{
    /// <summary>
    /// Moves the editor caret to a resolved <see cref="MasmSymbolDef"/> - either a span in the
    /// buffer already showing (<see cref="MasmSymbolDef.FilePath"/> <c>null</c>) or a position in
    /// an <c>INCLUDE</c>d file, which is opened first. Shared by the Ctrl+Click navigable-symbol
    /// path, the F12 / "Go To Definition" command filter and the Go To All symbol search. Every
    /// jump is best-effort: a failure must never propagate into the editor.
    /// </summary>
    internal static class MasmNavigator
    {
        public static void Navigate(
            MasmSymbolDef def, ITextView currentView,
            IServiceProvider serviceProvider, IVsEditorAdaptersFactoryService adapters)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (def.FilePath == null)
                    MoveCaret(currentView, def.Start, def.Length);
                else
                    OpenAndNavigate(def.FilePath, def.Start, def.Length, serviceProvider, adapters);
            }
            catch
            {
                // a failed jump must not take the editor down
            }
        }

        /// <summary>Opens <paramref name="path"/> and puts the caret on <c>[start, start+length)</c>.</summary>
        public static void NavigateToFile(
            string path, int start, int length,
            IServiceProvider serviceProvider, IVsEditorAdaptersFactoryService adapters)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                OpenAndNavigate(path, start, length, serviceProvider, adapters);
            }
            catch
            {
                // a failed jump must not take the editor down
            }
        }

        private static void MoveCaret(ITextView view, int start, int length)
        {
            if (view == null) return;
            ITextSnapshot snapshot = view.TextSnapshot;
            if (start < 0 || start > snapshot.Length) return;

            int safeLen = Math.Max(0, Math.Min(length, snapshot.Length - start));
            var target = new SnapshotSpan(snapshot, start, safeLen);

            view.Selection.Select(target, isReversed: false);
            view.Caret.MoveTo(target.Start);
            view.ViewScroller.EnsureSpanVisible(target, EnsureSpanVisibleOptions.AlwaysCenter);
        }

        private static void OpenAndNavigate(
            string path, int start, int length,
            IServiceProvider serviceProvider, IVsEditorAdaptersFactoryService adapters)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (serviceProvider == null || !File.Exists(path)) return;

            VsShellUtilities.OpenDocument(
                serviceProvider, path, VSConstants.LOGVIEWID.TextView_guid,
                out _, out _, out IVsWindowFrame frame, out IVsTextView vsTextView);

            frame?.Show();
            if (vsTextView == null && frame != null)
                vsTextView = VsShellUtilities.GetTextView(frame);
            if (vsTextView == null) return;

            IWpfTextView wpfView = adapters?.GetWpfTextView(vsTextView);
            if (wpfView != null)
            {
                MoveCaret(wpfView, start, length);
                return;
            }

            // Fallback: drive the shell view directly by line/column.
            vsTextView.GetBuffer(out IVsTextLines lines);
            if (lines != null &&
                ErrorHandler.Succeeded(lines.GetLineIndexOfPosition(start, out int line, out int col)))
            {
                vsTextView.SetCaretPos(line, col);
                vsTextView.CenterLines(line, 1);
            }
        }
    }
}
