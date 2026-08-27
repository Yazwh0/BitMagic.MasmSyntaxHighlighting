using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;

namespace MasmSyntaxHighlight.Commands
{
    /// <summary>
    /// Wires up the editor's line-comment commands for <c>.asm</c> / <c>.inc</c> files using
    /// <c>;</c> as the comment marker:
    /// <list type="bullet">
    ///   <item><description><c>Edit &gt; Advanced &gt; Comment Selection</c> (Ctrl+K, Ctrl+C)</description></item>
    ///   <item><description><c>Edit &gt; Advanced &gt; Uncomment Selection</c> (Ctrl+K, Ctrl+U)</description></item>
    ///   <item><description><c>Edit.ToggleLineComment</c> (Ctrl+K, Ctrl+/)</description></item>
    /// </list>
    /// Commenting inserts <c>;</c> at the start of every selected non-blank line; uncommenting
    /// removes the first <c>;</c> on each line whatever indentation precedes it; toggling
    /// uncomments when every selected non-blank line is already commented, otherwise comments.
    /// </summary>
    [Export(typeof(ICommandHandler))]
    [ContentType(MasmContentTypes.ContentType)]
    [Name(nameof(MasmCommentCommandHandler))]
    internal sealed class MasmCommentCommandHandler :
        ICommandHandler<CommentSelectionCommandArgs>,
        ICommandHandler<UncommentSelectionCommandArgs>,
        ICommandHandler<ToggleLineCommentCommandArgs>
    {
        private const string LineCommentMarker = ";";

        public string DisplayName => "MASM Comment / Uncomment Selection";

        public CommandState GetCommandState(CommentSelectionCommandArgs args) => CommandState.Available;

        public CommandState GetCommandState(UncommentSelectionCommandArgs args) => CommandState.Available;

        public CommandState GetCommandState(ToggleLineCommentCommandArgs args) => CommandState.Available;

        public bool ExecuteCommand(CommentSelectionCommandArgs args, CommandExecutionContext context)
            => ApplyToSelectedLines(args.TextView, args.SubjectBuffer, comment: true);

        public bool ExecuteCommand(UncommentSelectionCommandArgs args, CommandExecutionContext context)
            => ApplyToSelectedLines(args.TextView, args.SubjectBuffer, comment: false);

        public bool ExecuteCommand(ToggleLineCommentCommandArgs args, CommandExecutionContext context)
        {
            bool allCommented = AllSelectedLinesCommented(args.TextView, args.SubjectBuffer);
            return ApplyToSelectedLines(args.TextView, args.SubjectBuffer, comment: !allCommented);
        }

        // ---------------------------------------------------------------- helpers

        private static void GetSelectedLineRange(
            ITextView view, ITextSnapshot snapshot, out int firstLine, out int lastLine)
        {
            ITextSelection selection = view.Selection;

            SnapshotPoint startPoint = selection.IsEmpty
                ? view.Caret.Position.BufferPosition
                : selection.Start.Position;
            SnapshotPoint endPoint = selection.IsEmpty
                ? startPoint
                : selection.End.Position;

            firstLine = snapshot.GetLineNumberFromPosition(startPoint.Position);
            lastLine = snapshot.GetLineNumberFromPosition(endPoint.Position);

            // A multi-line selection that ends exactly at a line start shouldn't touch that line.
            if (lastLine > firstLine &&
                endPoint.Position == snapshot.GetLineFromLineNumber(lastLine).Start.Position)
            {
                lastLine--;
            }
        }

        private static int IndexOfFirstNonWhitespace(string text)
        {
            int i = 0;
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
            return i;
        }

        private static bool AllSelectedLinesCommented(ITextView view, ITextBuffer buffer)
        {
            ITextSnapshot snapshot = buffer.CurrentSnapshot;
            GetSelectedLineRange(view, snapshot, out int firstLine, out int lastLine);

            bool sawContent = false;
            for (int lineNumber = firstLine; lineNumber <= lastLine; lineNumber++)
            {
                string text = snapshot.GetLineFromLineNumber(lineNumber).GetText();
                int start = IndexOfFirstNonWhitespace(text);
                if (start == text.Length) continue; // blank line - ignored

                sawContent = true;
                if (text[start] != ';') return false;
            }

            return sawContent;
        }

        private static bool ApplyToSelectedLines(ITextView view, ITextBuffer buffer, bool comment)
        {
            ITextSnapshot snapshot = buffer.CurrentSnapshot;
            GetSelectedLineRange(view, snapshot, out int firstLine, out int lastLine);

            using (ITextEdit edit = buffer.CreateEdit())
            {
                bool changed = false;

                for (int lineNumber = firstLine; lineNumber <= lastLine; lineNumber++)
                {
                    ITextSnapshotLine line = snapshot.GetLineFromLineNumber(lineNumber);
                    string text = line.GetText();
                    int start = IndexOfFirstNonWhitespace(text);

                    if (comment)
                    {
                        if (start == text.Length) continue; // leave blank lines alone
                        edit.Insert(line.Start.Position, LineCommentMarker);
                        changed = true;
                    }
                    else if (start < text.Length && text[start] == ';')
                    {
                        edit.Delete(line.Start.Position + start, 1);
                        changed = true;
                    }
                }

                if (changed)
                    edit.Apply();
                else
                    edit.Cancel();
            }

            return true;
        }
    }
}
