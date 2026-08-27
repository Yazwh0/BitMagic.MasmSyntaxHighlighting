using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace MasmSyntaxHighlight.Lexing
{
    /// <summary>
    /// Collects symbol definitions from the files reached through <c>INCLUDE</c> directives so
    /// that types, prototypes and constants declared in headers colour when referenced.
    ///
    /// Files are resolved relative to the including file's directory (and the <c>%INCLUDE%</c>
    /// search path), lexed once, and cached by last-write time. Reads come from disk, so a
    /// header that is open with unsaved edits is seen in its last-saved state.
    /// </summary>
    internal static class MasmIncludeIndex
    {
        private const int MaxDepth = 24;
        private const long MaxFileBytes = 8L * 1024 * 1024;
        private const int MaxSymbols = 200_000;
        private const int MaxCachedFiles = 512;

        // Everything after "INCLUDE " up to a ';' comment or the end of the line; trailing
        // whitespace is trimmed in ExtractRawIncludes. No end anchor - $ is unreliable with
        // CRLF line endings. "INCLUDELIB" does not match (the [ \t]+ after INCLUDE fails).
        private static readonly Regex IncludeLine = new Regex(
            @"^[ \t]*INCLUDE[ \t]+(?<path>[^;\r\n]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

        private sealed class FileEntry
        {
            public DateTime WriteTimeUtc;
            public Dictionary<string, MasmTokenKind> Symbols;
            public List<string> RawIncludes;
            public string Directory;
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, FileEntry> Cache =
            new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);

        private static readonly string[] EnvIncludeDirs = BuildEnvIncludeDirs();

        /// <summary>
        /// Returns a name -&gt; kind map of every definition reachable via <c>INCLUDE</c> from
        /// <paramref name="rootText"/>. Empty when the buffer is not backed by a file on disk.
        /// </summary>
        public static Dictionary<string, MasmTokenKind> Collect(string rootFilePath, string rootText)
        {
            var accumulated = new Dictionary<string, MasmTokenKind>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(rootFilePath) || string.IsNullOrEmpty(rootText))
                return accumulated;

            string rootDir;
            try { rootDir = Path.GetDirectoryName(rootFilePath); }
            catch { return accumulated; }

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<PendingInclude>();
            foreach (string raw in ExtractRawIncludes(rootText))
                queue.Enqueue(new PendingInclude(raw, rootDir, 1));

            while (queue.Count > 0)
            {
                PendingInclude pending = queue.Dequeue();
                if (pending.Depth > MaxDepth || accumulated.Count >= MaxSymbols) continue;

                string full = ResolvePath(pending.RawPath, pending.BaseDirectory);
                if (full == null || !visited.Add(full)) continue;

                FileEntry entry = Load(full);
                if (entry == null) continue;

                foreach (KeyValuePair<string, MasmTokenKind> pair in entry.Symbols)
                {
                    if (accumulated.Count >= MaxSymbols) break;
                    if (!accumulated.ContainsKey(pair.Key))
                        accumulated[pair.Key] = pair.Value;
                }

                foreach (string childRaw in entry.RawIncludes)
                    queue.Enqueue(new PendingInclude(childRaw, entry.Directory, pending.Depth + 1));
            }

            return accumulated;
        }

        private readonly struct PendingInclude
        {
            public readonly string RawPath;
            public readonly string BaseDirectory;
            public readonly int Depth;

            public PendingInclude(string rawPath, string baseDirectory, int depth)
            {
                RawPath = rawPath;
                BaseDirectory = baseDirectory;
                Depth = depth;
            }
        }

        private static List<string> ExtractRawIncludes(string text)
        {
            var list = new List<string>();
            foreach (Match match in IncludeLine.Matches(text))
            {
                string raw = match.Groups["path"].Value.Trim().Trim('"', '<', '>').Trim();
                if (raw.Length > 0) list.Add(raw);
            }
            return list;
        }

        private static string ResolvePath(string raw, string baseDir)
        {
            try
            {
                raw = raw.Replace('/', '\\');

                var candidates = new List<string>(4);
                if (Path.IsPathRooted(raw))
                {
                    candidates.Add(raw);
                }
                else
                {
                    if (!string.IsNullOrEmpty(baseDir))
                        candidates.Add(Path.Combine(baseDir, raw));
                    foreach (string dir in EnvIncludeDirs)
                        candidates.Add(Path.Combine(dir, raw));
                }

                foreach (string candidate in candidates)
                {
                    string full = Path.GetFullPath(candidate);
                    if (File.Exists(full)) return full;
                }
            }
            catch
            {
                // malformed path - treat as unresolved
            }
            return null;
        }

        private static FileEntry Load(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > MaxFileBytes) return null;
                DateTime writeTime = info.LastWriteTimeUtc;

                lock (Gate)
                {
                    if (Cache.TryGetValue(path, out FileEntry cached) && cached.WriteTimeUtc == writeTime)
                        return cached;
                }

                string text = File.ReadAllText(path);
                List<MasmToken> tokens = new MasmLexer(text).Tokenize();

                var entry = new FileEntry
                {
                    WriteTimeUtc = writeTime,
                    Symbols = MasmSymbols.CollectDefinitions(tokens, text),
                    RawIncludes = ExtractRawIncludes(text),
                    Directory = Path.GetDirectoryName(path),
                };

                lock (Gate)
                {
                    if (Cache.Count >= MaxCachedFiles && !Cache.ContainsKey(path))
                        Cache.Clear();
                    Cache[path] = entry;
                }

                return entry;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
        }

        private static string[] BuildEnvIncludeDirs()
        {
            try
            {
                string value = Environment.GetEnvironmentVariable("INCLUDE");
                if (string.IsNullOrEmpty(value)) return Array.Empty<string>();

                var dirs = new List<string>();
                foreach (string part in value.Split(';'))
                {
                    string trimmed = part.Trim();
                    if (trimmed.Length > 0) dirs.Add(trimmed);
                }
                return dirs.ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }
}
