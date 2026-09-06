namespace MasmSyntaxHighlight.Completion
{
    /// <summary>
    /// The pure decision behind <see cref="MasmCompletionCommitManager.ShouldCommitCompletion"/>,
    /// kept free of Visual Studio types so it can be unit tested.
    /// </summary>
    internal static class MasmCompletionCommitPolicy
    {
        /// <summary>
        /// Should typing <paramref name="typedChar"/> commit the highlighted completion item?
        /// </summary>
        /// <param name="typedChar">the character the user just typed</param>
        /// <param name="charBeforeCaret">
        /// the character immediately left of where <paramref name="typedChar"/> lands,
        /// or <c>'\0'</c> at the start of the buffer
        /// </param>
        /// <param name="applicableLength">length of the session's applicable-to span</param>
        public static bool ShouldCommitOnDot(char typedChar, char charBeforeCaret, int applicableLength)
        {
            if (typedChar != '.') return false;

            // Commit only when the '.' closes an identifier segment that is being filtered,
            // e.g. the "uart" in "[rdx].uart.". After ']' or ')' - "[rdx]." - or after
            // whitespace there is nothing to complete; committing would replace the bracket
            // expression and swallow the ']' (you would get "[rdx."). Returning false lets the
            // session dismiss so a fresh member-access session opens on the '.' instead.
            if (!IsIdentChar(charBeforeCaret)) return false;

            return applicableLength > 0;
        }

        internal static bool IsIdentChar(char c)
            => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
               || c == '_' || c == '@' || c == '$' || c == '?';
    }
}
