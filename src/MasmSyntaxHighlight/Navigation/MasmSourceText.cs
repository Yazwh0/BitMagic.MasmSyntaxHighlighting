using System;
using System.Collections.Generic;
using System.IO;

namespace MasmSyntaxHighlight.Navigation
{
    /// <summary>
    /// On-demand, last-write-time-keyed cache of <c>INCLUDE</c>d file contents, used by QuickInfo
    /// and Peek to show and locate a definition that lives in another file. Reads come from disk,
    /// so a header open with unsaved edits is seen in its last-saved state - matching how
    /// <see cref="Lexing.MasmIncludeIndex"/> already behaves.
    /// </summary>
    internal static class MasmSourceText
    {
        private const int MaxEntries = 256;
        private const long MaxBytes = 8L * 1024 * 1024;

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Entry> Cache =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        private sealed class Entry
        {
            public DateTime WriteTimeUtc;
            public string Text;
        }

        /// <summary>File contents, or <c>null</c> when the path is empty, missing, too large or unreadable.</summary>
        internal static string GetText(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > MaxBytes) return null;
                DateTime writeTime = info.LastWriteTimeUtc;

                lock (Gate)
                {
                    if (Cache.TryGetValue(path, out Entry cached) && cached.WriteTimeUtc == writeTime)
                        return cached.Text;
                }

                string text = File.ReadAllText(path);

                lock (Gate)
                {
                    if (Cache.Count >= MaxEntries && !Cache.ContainsKey(path))
                        Cache.Clear();
                    Cache[path] = new Entry { WriteTimeUtc = writeTime, Text = text };
                }
                return text;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
        }

        /// <summary>Zero-based line and column of a character offset (counts <c>\n</c>).</summary>
        internal static void GetLineColumn(string text, int offset, out int line, out int column)
        {
            line = 0;
            column = 0;
            if (string.IsNullOrEmpty(text)) return;

            int limit = Math.Max(0, Math.Min(offset, text.Length));
            int lastNewline = -1;
            for (int i = 0; i < limit; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                    lastNewline = i;
                }
            }
            column = limit - lastNewline - 1;
        }

        /// <summary>The whole physical line (no terminator) containing <paramref name="offset"/>.</summary>
        internal static string GetLineText(string text, int offset)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            int p = Math.Max(0, Math.Min(offset, text.Length));
            int start = p;
            while (start > 0 && text[start - 1] != '\n' && text[start - 1] != '\r') start--;
            int end = p;
            while (end < text.Length && text[end] != '\n' && text[end] != '\r') end++;
            return text.Substring(start, end - start);
        }
    }
}
