using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using MasmSyntaxHighlight.Lexing;
using MasmSyntaxHighlight.Navigation;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace MasmSyntaxHighlight.Completion
{
    /// <summary>Creates one <see cref="MasmCompletionSource"/> per MASM text view.</summary>
    [Export(typeof(IAsyncCompletionSourceProvider))]
    [Name("MASM Completion")]
    [ContentType(MasmContentTypes.ContentType)]
    internal sealed class MasmCompletionSourceProvider : IAsyncCompletionSourceProvider
    {
        [Import]
        internal ITextDocumentFactoryService DocumentFactory { get; set; }

        public IAsyncCompletionSource GetOrCreate(ITextView textView)
        {
            if (textView == null) return null;
            MasmDefinitionIndex index =
                MasmBufferServices.GetIndex(textView.TextBuffer, DocumentFactory);
            return textView.Properties.GetOrCreateSingletonProperty(
                () => new MasmCompletionSource(index));
        }
    }

    /// <summary>
    /// Statement completion for MASM: instruction mnemonics, registers, directives, type
    /// keywords and operators from <see cref="MasmKeywords"/>, plus every symbol the buffer can
    /// see (procs, structs, constants, data, labels - this file and its <c>INCLUDE</c>s). Not
    /// offered inside a comment or string.
    ///
    /// After a member-access <c>.</c> the list narrows to that struct's fields:
    /// <c>uart.</c> (the type), <c>[rcx].uart.</c> (a register cast), <c>state.</c> (a variable
    /// declared <c>state uart &lt;&gt;</c> or <c>LOCAL state:uart</c>), and chains through
    /// struct-typed fields (<c>outer.inner.</c>).
    /// </summary>
    internal sealed class MasmCompletionSource : IAsyncCompletionSource
    {
        private const string DescKey = "masm.completion.description";

        private readonly MasmDefinitionIndex _index;
        private ImmutableArray<CompletionItem> _keywordItems;

        internal MasmCompletionSource(MasmDefinitionIndex index) => _index = index;

        public CompletionStartData InitializeCompletion(
            CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
        {
            ITextSnapshotLine line = triggerLocation.GetContainingLine();
            string prefix = line.Snapshot.GetText(
                line.Start.Position, triggerLocation.Position - line.Start.Position);

            if (InCommentOrString(prefix))
                return CompletionStartData.DoesNotParticipateInCompletion;

            SnapshotSpan word = WordSpanEndingAt(triggerLocation);

            bool invoked = trigger.Reason == CompletionTriggerReason.Invoke
                           || trigger.Reason == CompletionTriggerReason.InvokeAndCommitIfUnique
                           || trigger.Reason == CompletionTriggerReason.InvokeMatchingType;

            // typing the '.' itself should open the member list even though no word follows yet
            bool afterMemberDot = IsMemberAccessStart(triggerLocation, word.Start.Position);

            if (!invoked && word.Length == 0 && !afterMemberDot)
                return CompletionStartData.DoesNotParticipateInCompletion;

            return new CompletionStartData(CompletionParticipation.ProvidesItems, word);
        }

        public Task<CompletionContext> GetCompletionContextAsync(
            IAsyncCompletionSession session, CompletionTrigger trigger,
            SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan, CancellationToken token)
        {
            var builder = ImmutableArray.CreateBuilder<CompletionItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (TryGetStructMembers(triggerLocation, out MasmStructDef structDef))
            {
                foreach (MasmStructField field in structDef.Fields)
                    if (seen.Add(field.Name))
                        builder.Add(MakeItem(field.Name, FieldDescription(structDef.Name, field)));
                return Task.FromResult(new CompletionContext(builder.ToImmutable()));
            }

            try
            {
                foreach (KeyValuePair<string, MasmTokenKind> sym in _index.SymbolCompletions(triggerLocation.Snapshot))
                {
                    if (!seen.Add(sym.Key)) continue;
                    builder.Add(MakeItem(sym.Key, SymbolDescription(sym.Value)));
                }
            }
            catch
            {
                // fall back to keywords only
            }

            foreach (CompletionItem kw in KeywordItems())
                if (seen.Add(kw.DisplayText))
                    builder.Add(kw);

            return Task.FromResult(new CompletionContext(builder.ToImmutable()));
        }

        public Task<object> GetDescriptionAsync(
            IAsyncCompletionSession session, CompletionItem item, CancellationToken token)
        {
            object text = item.Properties.TryGetProperty(DescKey, out string d) ? d : item.DisplayText;
            return Task.FromResult(text);
        }

        // ---------------------------------------------------------------- item building

        private CompletionItem MakeItem(string text, string description)
        {
            var item = new CompletionItem(text, this);
            item.Properties.AddProperty(DescKey, description);
            return item;
        }

        private ImmutableArray<CompletionItem> KeywordItems()
        {
            if (!_keywordItems.IsDefault) return _keywordItems;

            var builder = ImmutableArray.CreateBuilder<CompletionItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddWords(builder, seen, MasmKeywords.Mnemonics, "instruction");
            AddWords(builder, seen, MasmKeywords.Registers, "register");
            AddWords(builder, seen, MasmKeywords.Directives, "directive");
            AddWords(builder, seen, MasmKeywords.DataTypes, "type");
            AddWords(builder, seen, MasmKeywords.Operators, "operator");

            _keywordItems = builder.ToImmutable();
            return _keywordItems;
        }

        private void AddWords(
            ImmutableArray<CompletionItem>.Builder builder, HashSet<string> seen,
            IEnumerable<string> words, string description)
        {
            foreach (string w in words)
                if (seen.Add(w))
                    builder.Add(MakeItem(w, description));
        }

        private static string FieldDescription(string structName, MasmStructField field)
            => field.TypeName != null
                ? structName + "." + field.Name + " : " + field.TypeName
                : structName + "." + field.Name;

        // ---------------------------------------------------------------- member access

        /// <summary>True when the word being completed sits immediately after a member-access
        /// <c>.</c> that is glued to an identifier or a <c>]</c> / <c>)</c>.</summary>
        private static bool IsMemberAccessStart(SnapshotPoint trigger, int wordStart)
        {
            ITextSnapshot s = trigger.Snapshot;
            if (wordStart <= 0 || s[wordStart - 1] != '.') return false;
            if (wordStart - 2 < 0) return false;
            char b = s[wordStart - 2];
            return b == ']' || b == ')' || IsIdentChar(b);
        }

        private bool TryGetStructMembers(SnapshotPoint trigger, out MasmStructDef structDef)
        {
            structDef = null;
            ITextSnapshot s = trigger.Snapshot;

            int start = trigger.Position;
            while (start > 0 && IsIdentChar(s[start - 1])) start--;
            if (start <= 0 || s[start - 1] != '.') return false;

            try { structDef = _index.ResolveStructMembers(s, start - 1); }
            catch { structDef = null; }
            return structDef != null;
        }

        private static string SymbolDescription(MasmTokenKind kind)
        {
            switch (kind)
            {
                case MasmTokenKind.ProcName: return "MASM procedure";
                case MasmTokenKind.TypeName: return "MASM type";
                case MasmTokenKind.ConstantName: return "MASM constant";
                case MasmTokenKind.DataName: return "MASM data";
                case MasmTokenKind.Label: return "MASM label";
                default: return "MASM symbol";
            }
        }

        // ---------------------------------------------------------------- text helpers

        private static bool IsIdentChar(char c)
            => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
               || c == '_' || c == '@' || c == '$' || c == '?';

        private static SnapshotSpan WordSpanEndingAt(SnapshotPoint end)
        {
            ITextSnapshot snapshot = end.Snapshot;
            int start = end.Position;
            while (start > 0 && IsIdentChar(snapshot[start - 1])) start--;
            // A leading '.' is part of the word only when it opens a directive (".code", ".if")
            // - i.e. it sits at the start of a statement, with whitespace or nothing before it.
            // A '.' glued to an identifier, ']' or ')' is member access ("[rdx].uart.field")
            // and must stay in the buffer, or committing an item would eat it.
            if (start > 0 && snapshot[start - 1] == '.')
            {
                char before = start >= 2 ? snapshot[start - 2] : '\n';
                if (before == '\n' || before == '\r' || before == ' ' || before == '\t')
                    start--;
            }
            return new SnapshotSpan(snapshot, start, end.Position - start);
        }

        private static bool InCommentOrString(string linePrefix)
        {
            char quote = '\0';
            foreach (char c in linePrefix)
            {
                if (quote != '\0')
                {
                    if (c == quote) quote = '\0';
                }
                else if (c == '\'' || c == '"')
                {
                    quote = c;
                }
                else if (c == ';')
                {
                    return true;
                }
            }
            return quote != '\0';
        }
    }
}
