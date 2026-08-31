using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace MasmSyntaxHighlight.Lexing
{
    /// <summary>
    /// Collects symbol definitions visible to a MASM source file so that types, prototypes and
    /// constants declared elsewhere colour when referenced.
    ///
    /// Two directions are followed:
    /// <list type="bullet">
    /// <item><description>
    /// <b>Down</b> - files reached through this file's own <c>INCLUDE</c> directives.
    /// </description></item>
    /// <item><description>
    /// <b>Up and across</b> - a file that is pulled in by <c>INCLUDE</c> from a parent (with no
    /// <c>INCLUDE</c> of its own) still sees everything that parent includes, because MASM
    /// concatenates the text. So the containing project tree is scanned for files that
    /// <c>INCLUDE</c> this one (directly or transitively), and every file <em>those</em> parents
    /// include is folded in too. This is why the <c>uart</c> struct from <c>Uart.asm</c> colours
    /// inside <c>Io.asm</c> even though <c>Io.asm</c> has no includes - <c>Core.asm</c> includes
    /// both.
    /// </description></item>
    /// </list>
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

        // Reverse (parent) search bounds.
        private const int MaxAncestorClimb = 4;    // parent directories to climb looking for a project root
        private const int MaxScanFiles = 4000;     // .asm / .inc files examined during the parent search
        private const int MaxAncestors = 128;      // parent files whose includes get folded in
        private const int MaxAncestorDepth = 16;   // levels of the reverse include graph to walk

        // Everything after "INCLUDE " up to a ';' comment or the end of the line; trailing
        // whitespace is trimmed in ExtractRawIncludes. No end anchor - $ is unreliable with
        // CRLF line endings. "INCLUDELIB" does not match (the [ \t]+ after INCLUDE fails).
        private static readonly Regex IncludeLine = new Regex(
            @"^[ \t]*INCLUDE[ \t]+(?<path>[^;\r\n]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

        private sealed class FileEntry
        {
            public DateTime WriteTimeUtc;
            public List<MasmSymbolDef> Defs; // null until symbols are actually needed
            public MasmStructModel Structs;  // rides along with Defs (same needSymbols pass)
            public List<string> RawIncludes;
            public string Directory;
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, FileEntry> Cache =
            new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);

        // The reverse include graph barely changes while typing, and rebuilding it means globbing
        // a directory tree, so the resolved ancestor set is memoised per file for a few seconds.
        private static readonly TimeSpan AncestorTtl = TimeSpan.FromSeconds(5);
        private static readonly Dictionary<string, (DateTime StampUtc, string[] Ancestors)> AncestorCache =
            new Dictionary<string, (DateTime, string[])>(StringComparer.OrdinalIgnoreCase);

        private static readonly string[] EnvIncludeDirs = BuildEnvIncludeDirs();

        private static readonly HashSet<string> SkipDirNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", "packages", "node_modules", "x64", "x86", "debug", "release", ".vs", ".git",
        };

        /// <summary>
        /// Returns a name -&gt; kind map of every definition visible to the file at
        /// <paramref name="rootFilePath"/> - through its own <c>INCLUDE</c>s and through any
        /// parent file that includes it. Empty when the buffer is not backed by a file on disk.
        /// </summary>
        public static Dictionary<string, MasmTokenKind> Collect(string rootFilePath, string rootText)
        {
            var accumulated = new Dictionary<string, MasmTokenKind>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(rootFilePath) || string.IsNullOrEmpty(rootText))
                return accumulated;

            Walk(rootFilePath, rootText, entry => MergeSymbols(accumulated, entry.Defs));
            return accumulated;
        }

        /// <summary>
        /// Like <see cref="Collect"/> but returns every visible definition <em>with its
        /// location</em> (for Go To Definition). A proc-local label from another file is dropped -
        /// it is not visible outside its own proc.
        /// </summary>
        public static List<MasmSymbolDef> CollectDefs(string rootFilePath, string rootText)
        {
            var accumulated = new List<MasmSymbolDef>();
            if (string.IsNullOrEmpty(rootFilePath) || string.IsNullOrEmpty(rootText))
                return accumulated;

            Walk(rootFilePath, rootText, entry => AddVisibleDefs(accumulated, entry.Defs));
            return accumulated;
        }

        /// <summary>
        /// Every <c>STRUCT</c> / <c>UNION</c> type and struct-instance binding visible to the file
        /// at <paramref name="rootFilePath"/> - through its own <c>INCLUDE</c>s and through any
        /// parent that includes it. Backs member completion after <c>.</c>. Empty when the buffer
        /// is not backed by a file on disk.
        /// </summary>
        public static MasmStructModel CollectStructModel(string rootFilePath, string rootText)
        {
            var structs = new List<MasmStructDef>();
            var instances = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(rootFilePath) || string.IsNullOrEmpty(rootText))
                return new MasmStructModel(structs, instances);

            Walk(rootFilePath, rootText, entry =>
            {
                MasmStructModel model = entry.Structs;
                if (model == null) return;

                foreach (MasmStructDef sd in model.Structs)
                {
                    if (structs.Count >= MaxSymbols) break;
                    structs.Add(sd);
                }
                foreach (KeyValuePair<string, string> kv in model.Instances)
                    if (!instances.ContainsKey(kv.Key)) instances[kv.Key] = kv.Value;
            });

            return new MasmStructModel(structs, instances);
        }

        /// <summary>
        /// Every non-proc-local definition in the <c>.asm</c> / <c>.inc</c> files under
        /// <paramref name="startDir"/> (after climbing to a project / repo root), for the
        /// Go To All / Navigate To symbol search. Files are lexed once and cached by last-write
        /// time, shared with the <c>INCLUDE</c> index. Bounded by <see cref="MaxScanFiles"/> and
        /// <see cref="MaxSymbols"/>.
        /// </summary>
        public static List<MasmSymbolDef> CollectProjectDefs(string startDir)
        {
            var result = new List<MasmSymbolDef>();
            if (string.IsNullOrEmpty(startDir)) return result;

            string[] files;
            try { files = EnumerateProjectFiles(startDir); }
            catch { return result; }

            foreach (string file in files)
            {
                FileEntry entry = Load(file);
                if (entry?.Defs == null) continue;

                foreach (MasmSymbolDef def in entry.Defs)
                {
                    if (def.IsProcLocal) continue; // a proc-local label is not a project-wide symbol
                    result.Add(def);
                    if (result.Count >= MaxSymbols) return result;
                }
            }
            return result;
        }

        /// <summary>
        /// Breadth-first visit of every file visible to the root: its own <c>INCLUDE</c>s
        /// (transitively) and every file pulled in alongside it by a parent that includes it.
        /// <paramref name="onFile"/> is called once per successfully loaded file.
        /// </summary>
        private static void Walk(string rootFilePath, string rootText, Action<FileEntry> onFile)
        {
            string rootFull;
            string rootDir;
            try
            {
                rootFull = Path.GetFullPath(rootFilePath);
                rootDir = Path.GetDirectoryName(rootFull);
            }
            catch
            {
                return;
            }

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootFull };
            var queue = new Queue<PendingInclude>();

            foreach (string raw in ExtractRawIncludes(rootText))
                queue.Enqueue(new PendingInclude(raw, rootDir, 1));

            // Parents: files elsewhere in the project that INCLUDE this one. Their own
            // definitions, and everything they include, are visible here.
            foreach (string ancestor in GetAncestors(rootFull, rootDir))
            {
                FileEntry entry = Load(ancestor);
                if (entry == null) continue;

                onFile(entry);
                foreach (string childRaw in entry.RawIncludes)
                    queue.Enqueue(new PendingInclude(childRaw, entry.Directory, 1));
            }

            int produced = 0;
            while (queue.Count > 0)
            {
                PendingInclude pending = queue.Dequeue();
                if (pending.Depth > MaxDepth || produced >= MaxSymbols) continue;

                string full = ResolvePath(pending.RawPath, pending.BaseDirectory);
                if (full == null || !visited.Add(full)) continue;

                FileEntry entry = Load(full);
                if (entry == null) continue;

                onFile(entry);
                produced += entry.Defs?.Count ?? 0;

                foreach (string childRaw in entry.RawIncludes)
                    queue.Enqueue(new PendingInclude(childRaw, entry.Directory, pending.Depth + 1));
            }
        }

        private static void MergeSymbols(Dictionary<string, MasmTokenKind> into, List<MasmSymbolDef> from)
        {
            if (from == null) return;
            foreach (MasmSymbolDef def in from)
            {
                if (into.Count >= MaxSymbols && !into.ContainsKey(def.Name)) break;
                // Higher-ranked kind wins: a struct type seen in one header should not be
                // masked by a same-named field declared in another (e.g. state.uart).
                MasmSymbols.Merge(into, def.Name, def.Kind);
            }
        }

        private static void AddVisibleDefs(List<MasmSymbolDef> into, List<MasmSymbolDef> from)
        {
            if (from == null) return;
            foreach (MasmSymbolDef def in from)
            {
                if (into.Count >= MaxSymbols) break;
                if (def.IsProcLocal) continue; // another file's proc-local label is not visible here
                into.Add(def);
            }
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

        // --- reverse include graph -------------------------------------------------------------

        private static string[] GetAncestors(string targetFull, string startDir)
        {
            DateTime now = DateTime.UtcNow;
            lock (Gate)
            {
                if (AncestorCache.TryGetValue(targetFull, out var cached)
                    && now - cached.StampUtc < AncestorTtl)
                    return cached.Ancestors;
            }

            string[] ancestors;
            try
            {
                ancestors = FindAncestorFiles(targetFull, startDir);
            }
            catch
            {
                ancestors = Array.Empty<string>();
            }

            lock (Gate)
            {
                AncestorCache[targetFull] = (now, ancestors);
                if (AncestorCache.Count > MaxCachedFiles)
                    AncestorCache.Clear();
            }
            return ancestors;
        }

        private static string[] FindAncestorFiles(string targetFull, string startDir)
        {
            if (string.IsNullOrEmpty(startDir)) return Array.Empty<string>();

            string[] candidates = EnumerateProjectFiles(startDir);
            if (candidates.Length == 0) return Array.Empty<string>();

            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var remaining = new List<string>(candidates.Length);
            foreach (string c in candidates)
                if (!c.Equals(targetFull, StringComparison.OrdinalIgnoreCase))
                    remaining.Add(c);

            // Reverse breadth-first: a file is an ancestor if one of its resolved INCLUDE targets
            // is already in the frontier (which starts as just the file we care about). Newly
            // found ancestors form the next frontier, so parents-of-parents are picked up too.
            var frontier = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { targetFull };

            for (int depth = 0;
                 depth < MaxAncestorDepth && frontier.Count > 0 && result.Count < MaxAncestors;
                 depth++)
            {
                var next = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                for (int i = remaining.Count - 1; i >= 0; i--)
                {
                    string candidate = remaining[i];
                    FileEntry entry = LoadIncludesOnly(candidate);
                    if (entry == null)
                    {
                        remaining.RemoveAt(i);
                        continue;
                    }

                    bool hits = false;
                    foreach (string raw in entry.RawIncludes)
                    {
                        string resolved = ResolvePath(raw, entry.Directory);
                        if (resolved != null && frontier.Contains(resolved))
                        {
                            hits = true;
                            break;
                        }
                    }

                    if (hits)
                    {
                        result.Add(candidate);
                        next.Add(candidate);
                        remaining.RemoveAt(i);
                        if (result.Count >= MaxAncestors) break;
                    }
                }

                frontier = next;
            }

            var array = new string[result.Count];
            result.CopyTo(array);
            return array;
        }

        private static string[] EnumerateProjectFiles(string startDir)
        {
            // Climb up to a directory that looks like a project or repo root (or run out of
            // climbs), then walk everything beneath it for .asm / .inc files.
            string root = startDir;
            for (int i = 0; i < MaxAncestorClimb; i++)
            {
                if (LooksLikeProjectRoot(root)) break;
                string parent;
                try { parent = Path.GetDirectoryName(root); }
                catch { break; }
                if (string.IsNullOrEmpty(parent) ||
                    parent.Equals(root, StringComparison.OrdinalIgnoreCase))
                    break;
                root = parent;
            }

            var files = new List<string>();
            var dirs = new Stack<string>();
            dirs.Push(root);

            while (dirs.Count > 0 && files.Count < MaxScanFiles)
            {
                string dir = dirs.Pop();

                string[] here;
                try { here = Directory.GetFiles(dir); }
                catch { continue; }

                foreach (string f in here)
                {
                    if (HasAsmExtension(f)) files.Add(f);
                    if (files.Count >= MaxScanFiles) break;
                }

                string[] subdirs;
                try { subdirs = Directory.GetDirectories(dir); }
                catch { continue; }

                foreach (string d in subdirs)
                {
                    string name = Path.GetFileName(d);
                    if (name.Length == 0 || name[0] == '.' || SkipDirNames.Contains(name))
                        continue;
                    dirs.Push(d);
                }
            }

            return files.ToArray();
        }

        private static bool LooksLikeProjectRoot(string dir)
        {
            try
            {
                if (Directory.Exists(Path.Combine(dir, ".git")) ||
                    File.Exists(Path.Combine(dir, ".git")))
                    return true;
                if (Directory.GetFiles(dir, "*.sln").Length > 0) return true;
                if (Directory.GetFiles(dir, "*.vcxproj").Length > 0) return true;
                if (Directory.GetFiles(dir, "*.csproj").Length > 0) return true;
            }
            catch
            {
                // unreadable directory - treat as not-a-root and keep climbing
            }
            return false;
        }

        private static bool HasAsmExtension(string path)
        {
            string ext = Path.GetExtension(path);
            return ext.Equals(".asm", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".inc", StringComparison.OrdinalIgnoreCase);
        }

        // --- shared file loading -------------------------------------------------------------

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

        private static FileEntry Load(string path) => GetEntry(path, needSymbols: true);

        private static FileEntry LoadIncludesOnly(string path) => GetEntry(path, needSymbols: false);

        private static FileEntry GetEntry(string path, bool needSymbols)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > MaxFileBytes) return null;
                DateTime writeTime = info.LastWriteTimeUtc;

                lock (Gate)
                {
                    if (Cache.TryGetValue(path, out FileEntry cached)
                        && cached.WriteTimeUtc == writeTime
                        && (!needSymbols || cached.Defs != null))
                        return cached;
                }

                string text = File.ReadAllText(path);

                var entry = new FileEntry
                {
                    WriteTimeUtc = writeTime,
                    RawIncludes = ExtractRawIncludes(text),
                    Directory = Path.GetDirectoryName(path),
                };

                if (needSymbols)
                {
                    List<MasmToken> tokens = new MasmLexer(text).Tokenize();
                    entry.Defs = MasmSymbols.CollectDefinitionsWithLocations(tokens, text, path);
                    entry.Structs = MasmStructIndex.Collect(tokens, text, path);
                }

                lock (Gate)
                {
                    if (Cache.Count >= MaxCachedFiles && !Cache.ContainsKey(path))
                        Cache.Clear();

                    // Don't downgrade an entry that already has symbols with a light one.
                    if (Cache.TryGetValue(path, out FileEntry existing)
                        && existing.WriteTimeUtc == writeTime
                        && existing.Defs != null
                        && entry.Defs == null)
                        return existing;

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
