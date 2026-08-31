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
    /// </summary>
    internal sealed class MasmCompletionCommitManager : IAsyncCompletionCommitManager
    {
        private static readonly char[] Commit = { '.' };

        public IEnumerable<char> PotentialCommitCharacters => Commit;

        public bool ShouldCommitCompletion(
            IAsyncCompletionSession session, SnapshotPoint location, char typedChar, CancellationToken token)
        {
            if (typedChar != '.') return false;

            // Only when a word is actually being completed - never turn "[rdx].." or a bare
            // trigger dot into a commit of whatever happens to be selected.
            SnapshotSpan applicable = session.ApplicableToSpan.GetSpan(location.Snapshot);
            return applicable.Length > 0;
        }

        public CommitResult TryCommit(
            IAsyncCompletionSession session, ITextBuffer buffer, CompletionItem item,
            char typedChar, CancellationToken token)
            => CommitResult.Unhandled; // default: replace the span with the item, then type the '.'
    }
}
