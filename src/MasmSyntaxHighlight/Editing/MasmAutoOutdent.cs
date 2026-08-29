using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;

namespace MasmSyntaxHighlight.Editing
{
    /// <summary>
    /// As you finish typing a block-closing keyword on an otherwise empty line
    /// (<c>ENDP</c>, <c>ENDS</c>, <c>ENDM</c>, <c>ENDIF</c>, <c>.ENDIF</c>, <c>.ENDW</c>,
    /// <c>.UNTIL</c>, and <c>ELSE</c> / <c>.ELSE</c> / <c>ELSEIF</c> / <c>.ELSEIF</c>), the line
    /// is re-indented to line up with the matching opener - the counterpart to
    /// <see cref="MasmSmartIndent"/>, which adds a level after an opener.
    /// </summary>
    [Export(typeof(ICommandHandler))]
    [ContentType(MasmContentTypes.ContentType)]
    [Name(nameof(MasmAutoOutdentCommandHandler))]
    internal sealed class MasmAutoOutdentCommandHandler : IChainedCommandHandler<TypeCharCommandArgs>
    {
        public string DisplayName => "MASM auto-outdent";

        public CommandState GetCommandState(TypeCharCommandArgs args, Func<CommandState> nextCommandHandler)
            => nextCommandHandler();

        public void ExecuteCommand(
            TypeCharCommandArgs args, Action nextCommandHandler, CommandExecutionContext executionContext)
        {
            nextCommandHandler(); // let the character be typed first

            char typed = args.TypedChar;
            if (typed != '.' && !char.IsLetter(typed)) return;

            try
            {
                Outdent(args.TextView, args.SubjectBuffer);
            }
            catch
            {
                // never disrupt typing
            }
        }

        private static void Outdent(ITextView view, ITextBuffer buffer)
        {
            ITextSnapshot snapshot = buffer.CurrentSnapshot;
            SnapshotPoint caret = view.Caret.Position.BufferPosition;
            if (!ReferenceEquals(caret.Snapshot, snapshot)) return;

            ITextSnapshotLine line = caret.GetContainingLine();
            string text = line.GetText();
            int caretColumn = caret.Position - line.Start.Position;

            // Everything before the caret must be: whitespace, then one word, and nothing else;
            // everything after the caret on the line must be blank.
            int wordStart = 0;
            while (wordStart < caretColumn && (text[wordStart] == ' ' || text[wordStart] == '\t'))
                wordStart++;

            string word = text.Substring(wordStart, caretColumn - wordStart);
            if (word.Length == 0) return;
            if (caretColumn < text.Length && text.Substring(caretColumn).Trim().Length != 0) return;

            string family = CloserFamily(word);
            if (family == null) return;

            int tabSize = view.Options.GetOptionValue(DefaultOptions.TabSizeOptionId);
            if (tabSize <= 0) tabSize = 4;
            bool spaces = view.Options.GetOptionValue(DefaultOptions.ConvertTabsToSpacesOptionId);

            int currentIndent = LeadingColumns(text, wordStart, tabSize);
            int desiredIndent = MatchingOpenerIndent(snapshot, line.LineNumber, family, tabSize);
            if (desiredIndent < 0)
                desiredIndent = Math.Max(0, currentIndent - IndentSize(view, tabSize));
            if (desiredIndent == currentIndent) return;

            string replacement = BuildIndent(desiredIndent, tabSize, spaces);
            using (ITextEdit edit = buffer.CreateEdit())
            {
                edit.Replace(new Span(line.Start.Position, wordStart), replacement);
                edit.Apply();
            }
        }

        // ---------------------------------------------------------------- matching

        /// <summary>Indent (in columns) of the opener that <paramref name="family"/>'s closer on
        /// <paramref name="closerLine"/> pairs with, or -1 if none is found above.</summary>
        private static int MatchingOpenerIndent(
            ITextSnapshot snapshot, int closerLine, string family, int tabSize)
        {
            int depth = 1;
            for (int ln = closerLine - 1; ln >= 0; ln--)
            {
                ITextSnapshotLine l = snapshot.GetLineFromLineNumber(ln);
                string t = l.GetText();
                int firstNonWs = FirstNonWhitespace(t);
                if (firstNonWs == t.Length) continue;

                foreach (string w in CodeWords(t))
                {
                    if (FamilyOf(w, opener: true) == family)
                    {
                        depth--;
                        if (depth == 0) return LeadingColumns(t, firstNonWs, tabSize);
                    }
                    else if (FamilyOf(w, opener: false) == family)
                    {
                        depth++;
                    }
                }
            }
            return -1;
        }

        private static readonly char[] WordSeparators = { ' ', '\t', ',', ':' };

        private static IEnumerable<string> CodeWords(string lineText)
        {
            int comment = lineText.IndexOf(';');
            string code = comment >= 0 ? lineText.Substring(0, comment) : lineText;
            return code.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
        }

        // family name -> opening keywords / closing keywords
        private static readonly Dictionary<string, string> Openers =
            Build(
                ("proc", "proc"),
                ("struct", "struct"), ("struct", "struc"), ("struct", "union"), ("struct", "segment"),
                ("macro", "macro"), ("macro", "rept"), ("macro", "repeat"), ("macro", "irp"),
                ("macro", "irpc"), ("macro", "for"), ("macro", "forc"), ("macro", "while"),
                ("ifc", "if"), ("ifc", "ife"), ("ifc", "ifb"), ("ifc", "ifnb"), ("ifc", "ifdef"),
                ("ifc", "ifndef"), ("ifc", "ifidn"), ("ifc", "ifidni"), ("ifc", "ifdif"),
                ("ifc", "ifdifi"), ("ifc", "if1"), ("ifc", "if2"),
                ("dotif", ".if"),
                ("dotwhile", ".while"),
                ("dotrepeat", ".repeat"));

        private static readonly Dictionary<string, string> Closers =
            Build(
                ("proc", "endp"),
                ("struct", "ends"),
                ("macro", "endm"),
                ("ifc", "endif"), ("ifc", "else"), ("ifc", "elseif"),
                ("dotif", ".endif"), ("dotif", ".else"), ("dotif", ".elseif"),
                ("dotwhile", ".endw"),
                ("dotrepeat", ".until"), ("dotrepeat", ".untilcxz"));

        private static Dictionary<string, string> Build(params (string family, string word)[] pairs)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach ((string family, string word) in pairs)
                map[word] = family;
            return map;
        }

        private static string CloserFamily(string word)
            => Closers.TryGetValue(word, out string family) ? family : null;

        private static string FamilyOf(string word, bool opener)
            => (opener ? Openers : Closers).TryGetValue(word, out string family) ? family : null;

        // ---------------------------------------------------------------- indentation helpers

        private static int IndentSize(ITextView view, int tabSize)
        {
            int size = view.Options.GetOptionValue(DefaultOptions.IndentSizeOptionId);
            return size > 0 ? size : tabSize;
        }

        private static int FirstNonWhitespace(string text)
        {
            int i = 0;
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
            return i;
        }

        private static int LeadingColumns(string text, int upTo, int tabSize)
        {
            int columns = 0;
            for (int i = 0; i < upTo && i < text.Length; i++)
            {
                if (text[i] == '\t') columns += tabSize - (columns % tabSize);
                else columns++;
            }
            return columns;
        }

        private static string BuildIndent(int columns, int tabSize, bool spaces)
        {
            if (spaces || tabSize <= 0) return new string(' ', columns);
            return new string('\t', columns / tabSize) + new string(' ', columns % tabSize);
        }
    }
}
