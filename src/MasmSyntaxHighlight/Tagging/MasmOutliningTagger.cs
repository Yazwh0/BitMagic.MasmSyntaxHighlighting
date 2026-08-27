using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using MasmSyntaxHighlight.Lexing;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace MasmSyntaxHighlight.Tagging
{
    /// <summary>Creates one <see cref="MasmOutliningTagger"/> per MASM buffer.</summary>
    [Export(typeof(ITaggerProvider))]
    [ContentType(MasmContentTypes.ContentType)]
    [TagType(typeof(IOutliningRegionTag))]
    internal sealed class MasmOutliningTaggerProvider : ITaggerProvider
    {
        public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag
        {
            if (buffer == null) return null;
            return buffer.Properties.GetOrCreateSingletonProperty(
                () => new MasmOutliningTagger(buffer)) as ITagger<T>;
        }
    }

    /// <summary>
    /// Provides collapsible regions for paired block directives (<c>PROC</c>/<c>ENDP</c>,
    /// <c>MACRO</c>/<c>ENDM</c>, <c>STRUCT</c>/<c>ENDS</c>, <c>.IF</c>/<c>.ENDIF</c>,
    /// <c>.WHILE</c>/<c>.ENDW</c>, <c>.REPEAT</c>/<c>.UNTIL</c>, repeat blocks) and for
    /// <c>;region</c> ... <c>;endregion</c> comment markers.
    /// </summary>
    internal sealed class MasmOutliningTagger : ITagger<IOutliningRegionTag>
    {
        private readonly ITextBuffer _buffer;
        private ITextSnapshot _parsedSnapshot;
        private List<Region> _regions = new List<Region>();

        internal MasmOutliningTagger(ITextBuffer buffer)
        {
            _buffer = buffer;
            _buffer.Changed += OnBufferChanged;
        }

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            _parsedSnapshot = null;
            TagsChanged?.Invoke(
                this,
                new SnapshotSpanEventArgs(new SnapshotSpan(e.After, 0, e.After.Length)));
        }

        public IEnumerable<ITagSpan<IOutliningRegionTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            if (spans.Count == 0) yield break;

            ITextSnapshot snapshot = spans[0].Snapshot;
            EnsureParsed(snapshot);
            if (_regions.Count == 0) yield break;

            int requestStart = spans[0].Start.Position;
            int requestEnd = spans[spans.Count - 1].End.Position;

            foreach (Region region in _regions)
            {
                if (region.StartLine >= snapshot.LineCount || region.EndLine >= snapshot.LineCount)
                    continue;

                ITextSnapshotLine startLine = snapshot.GetLineFromLineNumber(region.StartLine);
                ITextSnapshotLine endLine = snapshot.GetLineFromLineNumber(region.EndLine);
                if (endLine.End.Position < requestStart || startLine.Start.Position > requestEnd)
                    continue;

                var span = new SnapshotSpan(startLine.Start, endLine.End);
                yield return new TagSpan<IOutliningRegionTag>(
                    span,
                    new OutliningRegionTag(false, false, Ellipsize(region.Banner), span.GetText()));
            }
        }

        private void EnsureParsed(ITextSnapshot snapshot)
        {
            if (ReferenceEquals(_parsedSnapshot, snapshot)) return;
            _regions = ComputeRegions(snapshot);
            _parsedSnapshot = snapshot;
        }

        private static string Ellipsize(string text)
        {
            text = text.Trim();
            const int max = 60;
            return (text.Length > max ? text.Substring(0, max) : text) + " …";
        }

        // ---------------------------------------------------------------- parsing

        private readonly struct Region
        {
            public readonly int StartLine;
            public readonly int EndLine;
            public readonly string Banner;

            public Region(int startLine, int endLine, string banner)
            {
                StartLine = startLine;
                EndLine = endLine;
                Banner = banner;
            }
        }

        private readonly struct Pending
        {
            public readonly string Keyword; // null for a ;region marker
            public readonly int Line;
            public readonly string Banner;

            public Pending(string keyword, int line, string banner)
            {
                Keyword = keyword;
                Line = line;
                Banner = banner;
            }
        }

        private static List<Region> ComputeRegions(ITextSnapshot snapshot)
        {
            string text = snapshot.GetText();
            List<MasmToken> tokens = new MasmLexer(text).Tokenize();

            var regions = new List<Region>();
            var blocks = new Stack<Pending>();
            var markers = new Stack<Pending>();

            foreach (MasmToken token in tokens)
            {
                int line = snapshot.GetLineNumberFromPosition(token.Start);

                if (token.Kind == MasmTokenKind.Comment)
                {
                    string body = text.Substring(token.Start, token.Length).TrimStart(';', ' ', '\t');
                    if (StartsWithWord(body, "region"))
                    {
                        string name = body.Substring("region".Length).Trim();
                        markers.Push(new Pending(null, line, name.Length > 0 ? name : "region"));
                    }
                    else if (StartsWithWord(body, "endregion") && markers.Count > 0)
                    {
                        Pending open = markers.Pop();
                        if (line > open.Line)
                            regions.Add(new Region(open.Line, line, open.Banner));
                    }
                    continue;
                }

                if (token.Kind != MasmTokenKind.Directive)
                    continue;

                string keyword = text.Substring(token.Start, token.Length).ToLowerInvariant();

                if (BlockOpeners.Contains(keyword))
                {
                    blocks.Push(new Pending(keyword, line, GetLineText(snapshot, line)));
                }
                else if (BlockClosers.Contains(keyword) &&
                         blocks.Count > 0 &&
                         Closes(blocks.Peek().Keyword, keyword))
                {
                    Pending open = blocks.Pop();
                    if (line > open.Line)
                        regions.Add(new Region(open.Line, line, open.Banner));
                }
            }

            return regions;
        }

        private static string GetLineText(ITextSnapshot snapshot, int line)
            => snapshot.GetLineFromLineNumber(line).GetText().Trim();

        private static bool StartsWithWord(string text, string word)
        {
            if (!text.StartsWith(word, StringComparison.OrdinalIgnoreCase)) return false;
            return text.Length == word.Length || !char.IsLetterOrDigit(text[word.Length]);
        }

        private static readonly HashSet<string> BlockOpeners = new HashSet<string>(StringComparer.Ordinal)
        {
            "proc", "macro", "struct", "struc", "union", "segment",
            "rept", "repeat", "irp", "irpc", "for", "forc", "while",
            "if", "ife", "ifb", "ifnb", "ifdef", "ifndef",
            "ifidn", "ifidni", "ifdif", "ifdifi", "if1", "if2",
            ".if", ".while", ".repeat",
        };

        private static readonly HashSet<string> BlockClosers = new HashSet<string>(StringComparer.Ordinal)
        {
            "endp", "endm", "ends", "endif",
            ".endif", ".endw", ".until", ".untilcxz",
        };

        private static bool Closes(string opener, string closer)
        {
            switch (opener)
            {
                case "proc":
                    return closer == "endp";
                case "macro":
                case "rept":
                case "repeat":
                case "irp":
                case "irpc":
                case "for":
                case "forc":
                case "while":
                    return closer == "endm";
                case "struct":
                case "struc":
                case "union":
                case "segment":
                    return closer == "ends";
                case "if":
                case "ife":
                case "ifb":
                case "ifnb":
                case "ifdef":
                case "ifndef":
                case "ifidn":
                case "ifidni":
                case "ifdif":
                case "ifdifi":
                case "if1":
                case "if2":
                    return closer == "endif";
                case ".if":
                    return closer == ".endif";
                case ".while":
                    return closer == ".endw";
                case ".repeat":
                    return closer == ".until" || closer == ".untilcxz";
                default:
                    return false;
            }
        }
    }
}
