using MasmSyntaxHighlight.Completion;
using Xunit;

namespace MasmSyntaxHighlight.Tests
{
    public class CompletionCommitPolicyTests
    {
        [Fact]
        public void Dot_after_an_identifier_segment_commits()
        {
            // "[rdx].uart" then '.': the 'uart' segment is being filtered - commit it so a
            // fresh session opens for the next member.
            Assert.True(MasmCompletionCommitPolicy.ShouldCommitOnDot('.', 't', applicableLength: 4));
        }

        [Fact]
        public void Dot_after_a_closing_bracket_does_not_commit()
        {
            // "[rdx]" then '.': committing would replace the bracket expression and eat the ']'
            // (the reported "[rdx." bug). Let the session dismiss instead.
            Assert.False(MasmCompletionCommitPolicy.ShouldCommitOnDot('.', ']', applicableLength: 4));
        }

        [Fact]
        public void Dot_after_a_closing_paren_does_not_commit()
        {
            Assert.False(MasmCompletionCommitPolicy.ShouldCommitOnDot('.', ')', applicableLength: 3));
        }

        [Theory]
        [InlineData(' ')]
        [InlineData('\t')]
        [InlineData('\n')]
        [InlineData('\0')] // start of buffer
        [InlineData('.')]  // ".."
        public void Dot_after_a_non_identifier_does_not_commit(char before)
        {
            Assert.False(MasmCompletionCommitPolicy.ShouldCommitOnDot('.', before, applicableLength: 5));
        }

        [Fact]
        public void Dot_with_an_empty_applicable_span_does_not_commit()
        {
            Assert.False(MasmCompletionCommitPolicy.ShouldCommitOnDot('.', 't', applicableLength: 0));
        }

        [Theory]
        [InlineData('a')]
        [InlineData('Z')]
        [InlineData('0')]
        [InlineData('_')]
        [InlineData('@')]
        [InlineData('$')]
        [InlineData('?')]
        public void Every_identifier_char_before_the_dot_commits(char before)
        {
            Assert.True(MasmCompletionCommitPolicy.ShouldCommitOnDot('.', before, applicableLength: 2));
        }

        [Fact]
        public void Only_the_dot_is_a_commit_char()
        {
            Assert.False(MasmCompletionCommitPolicy.ShouldCommitOnDot('x', 't', applicableLength: 3));
            Assert.False(MasmCompletionCommitPolicy.ShouldCommitOnDot(',', 't', applicableLength: 3));
        }
    }
}
