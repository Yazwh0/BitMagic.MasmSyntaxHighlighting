using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace MasmSyntaxHighlight.Completion
{
    /// <summary>Creates one <see cref="MasmCompletionCommitManager"/> per MASM text view.</summary>
    [Export(typeof(IAsyncCompletionCommitManagerProvider))]
    [Name("MASM Completion Commit")]
    [ContentType(MasmContentTypes.ContentType)]
    internal sealed class MasmCompletionCommitManagerProvider : IAsyncCompletionCommitManagerProvider
    {
        public IAsyncCompletionCommitManager GetOrCreate(ITextView textView)
            => textView.Properties.GetOrCreateSingletonProperty(() => new MasmCompletionCommitManager());
    }

    /// <summary>
    /// Makes <c>.</c> commit the highlighted item. Without it, typing <c>[rdx].uart.field</c> in
    /// one pass leaves the session that opened on the first <c>.</c> filtering a stale list, and
    /// the member list for <c>uart</c> never appears. Committing on <c>.</c> ends that session so
    /// a fresh one opens for the next segment - the same path as typing <c>.</c> against text
    /// that is already there. Enter and Tab still commit as before; this only adds <c>.</c>.
    ///
    /// The <c>.</c> only commits when it closes an identifier segment. Typed after <c>]</c> or
    /// <c>)</c> (<c>[rdx].</c>) it must not commit - the applicable span there covers the bracket
    /// expression, so committing would replace it and eat the <c>]</c> (giving <c>[rdx.</c>).
    /// See <see cref="MasmCompletionCommitPolicy"/>.
    /// </summary>
    internal sealed class MasmCompletionCommitManager : IAsyncCompletionCommitManager
    {
        private static readonly char[] Commit = { '.' };

        public IEnumerable<char> PotentialCommitCharacters => Commit;

        public bool ShouldCommitCompletion(
            IAsyncCompletionSession session, SnapshotPoint location, char typedChar, CancellationToken token)
        {
            if (typedChar != '.') return false;

            ITextSnapshot snapshot = location.Snapshot;
            char before = location.Position > 0 ? snapshot[location.Position - 1] : '\0';
            int applicableLength = session.ApplicableToSpan.GetSpan(snapshot).Length;

            return MasmCompletionCommitPolicy.ShouldCommitOnDot(typedChar, before, applicableLength);
        }

        public CommitResult TryCommit(
            IAsyncCompletionSession session, ITextBuffer buffer, CompletionItem item,
            char typedChar, CancellationToken token)
            => CommitResult.Unhandled; // default: replace the span with the item, then type the '.'
    }
}
