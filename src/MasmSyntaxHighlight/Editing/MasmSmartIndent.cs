using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace MasmSyntaxHighlight.Editing
{
    /// <summary>Provides a <see cref="MasmSmartIndent"/> per text view (the editor uses smart
    /// indenting automatically once such a provider exists for the content type).</summary>
    [Export(typeof(ISmartIndentProvider))]
    [ContentType(MasmContentTypes.ContentType)]
    internal sealed class MasmSmartIndentProvider : ISmartIndentProvider
    {
        public ISmartIndent CreateSmartIndent(ITextView textView)
        {
            return textView.Properties.GetOrCreateSingletonProperty(() => new MasmSmartIndent(textView));
        }
    }

    /// <summary>
    /// On a new line, keeps the previous non-blank line's indentation and adds one level when
    /// that line opens a block (<c>PROC</c>, <c>MACRO</c>, <c>STRUCT</c>, <c>SEGMENT</c>,
    /// <c>.IF</c>/<c>.ELSE</c>/<c>.ELSEIF</c>, <c>.WHILE</c>, <c>.REPEAT</c>, <c>IF*</c>, and the
    /// repeat blocks). It does not out-dent a closing keyword you type - fix that with Backspace
    /// or Shift+Tab.
    /// </summary>
    internal sealed class MasmSmartIndent : ISmartIndent
    {
        private readonly ITextView _textView;

        public MasmSmartIndent(ITextView textView) => _textView = textView;

        public int? GetDesiredIndentation(ITextSnapshotLine line)
        {
            int indentSize = _textView.Options.GetOptionValue(DefaultOptions.IndentSizeOptionId);
            if (indentSize <= 0) indentSize = 4;

            for (int lineNumber = line.LineNumber - 1; lineNumber >= 0; lineNumber--)
            {
                ITextSnapshotLine previous = line.Snapshot.GetLineFromLineNumber(lineNumber);
                string text = previous.GetText();
                if (string.IsNullOrWhiteSpace(text)) continue;

                int indent = LeadingColumns(text, indentSize);
                if (OpensBlock(text)) indent += indentSize;
                return indent;
            }

            return 0;
        }

        public void Dispose() { }

        private static int LeadingColumns(string text, int tabSize)
        {
            int columns = 0;
            foreach (char c in text)
            {
                if (c == ' ') columns++;
                else if (c == '\t') columns += tabSize - (columns % tabSize);
                else break;
            }
            return columns;
        }

        private static readonly char[] WordSeparators = { ' ', '\t' };

        private static bool OpensBlock(string lineText)
        {
            int comment = lineText.IndexOf(';');
            string code = comment >= 0 ? lineText.Substring(0, comment) : lineText;

            foreach (string word in code.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                if (BlockOpeners.Contains(word)) return true;
            }
            return false;
        }

        private static readonly HashSet<string> BlockOpeners = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "proc", "macro", "struc", "struct", "union", "segment",
            "rept", "repeat", "irp", "irpc", "for", "forc", "while",
            "if", "ife", "ifb", "ifnb", "ifdef", "ifndef",
            "ifidn", "ifidni", "ifdif", "ifdifi", "if1", "if2", "else", "elseif",
            ".if", ".else", ".elseif", ".while", ".repeat",
        };
    }
}
