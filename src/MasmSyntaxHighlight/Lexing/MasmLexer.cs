using System.Collections.Generic;

namespace MasmSyntaxHighlight.Lexing
{
    /// <summary>
    /// A small hand-written lexer for MASM (ml64) source. It scans the whole document once
    /// and returns an ordered, non-overlapping list of coloured spans. Only tokens that carry
    /// a colour are emitted; plain whitespace and uncoloured identifiers are skipped.
    /// </summary>
    internal sealed class MasmLexer
    {
        private readonly string _text;
        private readonly int _len;
        private int _pos;

        public MasmLexer(string text)
        {
            _text = text ?? string.Empty;
            _len = _text.Length;
        }

        /// <summary>True at the start of a logical statement (start of line, or just past a label / ':').</summary>
        private bool _atStatementStart = true;

        public List<MasmToken> Tokenize()
        {
            var tokens = new List<MasmToken>(256);
            _pos = 0;
            _atStatementStart = true;

            while (_pos < _len)
            {
                char c = _text[_pos];

                // ---- end of line ------------------------------------------------------
                if (c == '\n' || c == '\r')
                {
                    ConsumeNewLine();
                    _atStatementStart = true;
                    continue;
                }

                // ---- whitespace -----------------------------------------------------
                if (c == ' ' || c == '\t' || c == '\f' || c == '\v')
                {
                    _pos++;
                    continue;
                }

                // ---- line continuation:  '\'  [spaces]  [;comment]  newline --------
                if (c == '\\' && IsLineContinuation())
                {
                    ConsumeLineContinuation(tokens);
                    continue; // statement state is deliberately preserved
                }

                // ---- ';' line comment --------------------------------------------
                if (c == ';')
                {
                    int start = _pos;
                    while (_pos < _len && _text[_pos] != '\n' && _text[_pos] != '\r') _pos++;
                    tokens.Add(new MasmToken(start, _pos - start, MasmTokenKind.Comment));
                    continue;
                }

                // ---- string literal --------------------------------------------------
                if (c == '\'' || c == '"')
                {
                    tokens.Add(ReadString(c));
                    _atStatementStart = false;
                    continue;
                }

                // ---- '.' : directive (.code), struct member access ([rdx].field), or real (.5)
                if (c == '.')
                {
                    string dotted = PeekWord();

                    // struct / record member access: a '.' stuck directly onto a preceding
                    // ']' ')' register or identifier - e.g. [rdx].zimodem.data_dir
                    bool memberAccess = dotted.Length > 1
                        && IsIdentStart(_text[_pos + 1])
                        && tokens.Count > 0
                        && tokens[tokens.Count - 1].End == _pos
                        && IsMemberAccessTarget(tokens[tokens.Count - 1]);

                    if (memberAccess)
                    {
                        tokens.Add(new MasmToken(_pos, 1, MasmTokenKind.Operator)); // the dot
                        _pos++;
                        int nameStart = _pos;
                        while (_pos < _len && IsIdentPart(_text[_pos])) _pos++;
                        tokens.Add(new MasmToken(nameStart, _pos - nameStart, MasmTokenKind.Identifier));
                        _atStatementStart = false;
                        continue;
                    }

                    if (dotted.Length > 1 &&
                        (IsLetter(_text[_pos + 1]) || MasmKeywords.Directives.Contains(dotted)))
                    {
                        tokens.Add(ReadWord(tokens));
                        _atStatementStart = false;
                        continue;
                    }
                    if (_pos + 1 < _len && IsDigit(_text[_pos + 1]))
                    {
                        tokens.Add(ReadNumber());
                        _atStatementStart = false;
                        continue;
                    }
                    tokens.Add(new MasmToken(_pos, 1, MasmTokenKind.Operator));
                    _pos++;
                    _atStatementStart = false;
                    continue;
                }

                // ---- numeric literal ----------------------------------------------
                if (IsDigit(c))
                {
                    tokens.Add(ReadNumber());
                    _atStatementStart = false;
                    continue;
                }

                // ---- identifier / keyword ------------------------------------------
                if (IsIdentStart(c))
                {
                    string peeked = PeekWord();

                    // MASM 'COMMENT <delim> ... <delim>' block comment
                    if (_atStatementStart && peeked.Length == 7 &&
                        string.Equals(peeked, "comment", System.StringComparison.OrdinalIgnoreCase))
                    {
                        tokens.Add(new MasmToken(_pos, 7, MasmTokenKind.Directive));
                        _pos += 7;
                        ReadCommentBlock(tokens);
                        _atStatementStart = true;
                        continue;
                    }

                    var word = ReadWord(tokens);
                    tokens.Add(word);
                    // a label / proc name may be followed by another statement on the same line
                    _atStatementStart = word.Kind == MasmTokenKind.Label
                                     || word.Kind == MasmTokenKind.ProcName;
                    continue;
                }

                // ---- ':'  (label separator / segment override) -------------------
                if (c == ':')
                {
                    tokens.Add(new MasmToken(_pos, 1, MasmTokenKind.Operator));
                    _pos++;
                    _atStatementStart = true;
                    continue;
                }

                // ---- other punctuation / operators -------------------------------
                if (IsOperatorChar(c))
                {
                    tokens.Add(new MasmToken(_pos, 1, MasmTokenKind.Operator));
                    _pos++;
                    _atStatementStart = false;
                    continue;
                }

                // ---- anything else: skip one character --------------------------
                _pos++;
                _atStatementStart = false;
            }

            return tokens;
        }

        // ------------------------------------------------------------------ helpers

        private void ConsumeNewLine()
        {
            if (_text[_pos] == '\r')
            {
                _pos++;
                if (_pos < _len && _text[_pos] == '\n') _pos++;
            }
            else
            {
                _pos++;
            }
        }

        private bool IsLineContinuation()
        {
            // current char is '\'
            int p = _pos + 1;
            while (p < _len && (_text[p] == ' ' || _text[p] == '\t')) p++;
            return p >= _len || _text[p] == '\n' || _text[p] == '\r' || _text[p] == ';';
        }

        private void ConsumeLineContinuation(List<MasmToken> tokens)
        {
            _pos++; // the backslash
            while (_pos < _len && (_text[_pos] == ' ' || _text[_pos] == '\t')) _pos++;
            if (_pos < _len && _text[_pos] == ';')
            {
                int cs = _pos;
                while (_pos < _len && _text[_pos] != '\n' && _text[_pos] != '\r') _pos++;
                tokens.Add(new MasmToken(cs, _pos - cs, MasmTokenKind.Comment));
            }
            if (_pos < _len && (_text[_pos] == '\n' || _text[_pos] == '\r'))
                ConsumeNewLine();
            // _atStatementStart intentionally left unchanged
        }

        private MasmToken ReadString(char quote)
        {
            int start = _pos;
            _pos++; // opening quote
            while (_pos < _len)
            {
                char c = _text[_pos];
                if (c == '\n' || c == '\r') break;            // MASM strings do not span lines
                if (c == quote)
                {
                    if (_pos + 1 < _len && _text[_pos + 1] == quote)
                    {
                        _pos += 2;                            // doubled quote = literal quote
                        continue;
                    }
                    _pos++;                                   // closing quote
                    break;
                }
                _pos++;
            }
            return new MasmToken(start, _pos - start, MasmTokenKind.String);
        }

        private MasmToken ReadNumber()
        {
            int start = _pos;

            // C-style hex prefix
            if (_text[_pos] == '0' && _pos + 1 < _len && (_text[_pos + 1] == 'x' || _text[_pos + 1] == 'X'))
            {
                _pos += 2;
                while (_pos < _len && IsHexDigit(_text[_pos])) _pos++;
                MaybeConsume('r', 'R');
                return new MasmToken(start, _pos - start, MasmTokenKind.Number);
            }

            // leading run of hex digits (covers decimal, 0FFh, 1010b, etc.)
            while (_pos < _len && IsHexDigit(_text[_pos])) _pos++;

            // radix suffix letter
            if (_pos < _len && IsRadixSuffix(_text[_pos]))
            {
                _pos++;
                return new MasmToken(start, _pos - start, MasmTokenKind.Number);
            }

            // fractional part
            if (_pos + 1 < _len && _text[_pos] == '.' && IsDigit(_text[_pos + 1]))
            {
                _pos++;
                while (_pos < _len && IsDigit(_text[_pos])) _pos++;
            }

            // exponent
            if (_pos < _len && (_text[_pos] == 'e' || _text[_pos] == 'E'))
            {
                int save = _pos;
                _pos++;
                if (_pos < _len && (_text[_pos] == '+' || _text[_pos] == '-')) _pos++;
                if (_pos < _len && IsDigit(_text[_pos]))
                    while (_pos < _len && IsDigit(_text[_pos])) _pos++;
                else
                    _pos = save;
            }

            MaybeConsume('r', 'R'); // encoded-real designator, e.g. 3F800000r
            return new MasmToken(start, _pos - start, MasmTokenKind.Number);
        }

        private void MaybeConsume(char lower, char upper)
        {
            if (_pos < _len && (_text[_pos] == lower || _text[_pos] == upper)) _pos++;
        }

        /// <summary>Reads an identifier (possibly '.'-prefixed) and classifies it.</summary>
        private MasmToken ReadWord(List<MasmToken> tokens)
        {
            int start = _pos;
            bool dotted = _text[_pos] == '.';
            if (dotted) _pos++;
            while (_pos < _len && IsIdentPart(_text[_pos])) _pos++;
            int length = _pos - start;
            string word = _text.Substring(start, length);

            // code label:   name:
            if (!dotted && _atStatementStart && _pos < _len && _text[_pos] == ':')
                return new MasmToken(start, length, MasmTokenKind.Label);

            if (dotted)
                return new MasmToken(start, length, MasmTokenKind.Directive);

            bool isMnemonic = MasmKeywords.Mnemonics.Contains(word);
            bool isOperator = MasmKeywords.Operators.Contains(word);

            // A known reserved word is always classified as that word - this must come before
            // the definition-name check so that e.g. "mov byte ptr ..." or "movaps xmmword ..."
            // are not mistaken for "<name> <size>" data definitions.
            if (isMnemonic && isOperator)
                return new MasmToken(start, length,
                    _atStatementStart ? MasmTokenKind.Mnemonic : MasmTokenKind.Operator);
            if (isMnemonic)
                return new MasmToken(start, length, MasmTokenKind.Mnemonic);
            if (MasmKeywords.Registers.Contains(word))
                return new MasmToken(start, length, MasmTokenKind.Register);
            if (MasmKeywords.DataTypes.Contains(word))
                return new MasmToken(start, length, MasmTokenKind.DataType);
            if (MasmKeywords.Directives.Contains(word))
                return new MasmToken(start, length, MasmTokenKind.Directive);
            if (isOperator)
                return new MasmToken(start, length, MasmTokenKind.Operator);

            // definition name:   name PROC | name EQU | name = | name db ... | name BYTE ...
            if (_atStatementStart)
            {
                MasmTokenKind? definition = ClassifyDefinitionName();
                if (definition.HasValue)
                    return new MasmToken(start, length, definition.Value);
            }

            // reference coloured by the operand it follows:
            //   call / invoke <name>   -> proc name
            //   jmp / jCC / loop / short <name>  -> label
            if (tokens.Count > 0)
            {
                MasmToken prev = tokens[tokens.Count - 1];
                if (IsCallLikePrefix(prev))
                    return new MasmToken(start, length, MasmTokenKind.ProcName);
                if (IsBranchPrefix(prev))
                    return new MasmToken(start, length, MasmTokenKind.Label);
            }

            return new MasmToken(start, length, MasmTokenKind.Identifier);
        }

        /// <summary>True when <paramref name="prev"/> is <c>call</c> or <c>invoke</c>.</summary>
        private bool IsCallLikePrefix(MasmToken prev)
        {
            if (prev.Kind != MasmTokenKind.Mnemonic && prev.Kind != MasmTokenKind.Directive)
                return false;
            string s = _text.Substring(prev.Start, prev.Length);
            return s.Equals("call", System.StringComparison.OrdinalIgnoreCase)
                || s.Equals("invoke", System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True when <paramref name="prev"/> is a branch mnemonic (j*, loop*) or the <c>short</c> operator.</summary>
        private bool IsBranchPrefix(MasmToken prev)
        {
            if (prev.Kind == MasmTokenKind.Mnemonic)
            {
                char c0 = _text[prev.Start];
                if (c0 == 'j' || c0 == 'J') return true;
                return _text.Substring(prev.Start, prev.Length)
                            .StartsWith("loop", System.StringComparison.OrdinalIgnoreCase);
            }
            if (prev.Kind == MasmTokenKind.Operator)
            {
                return _text.Substring(prev.Start, prev.Length)
                            .Equals("short", System.StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        /// <summary>
        /// True when a '.' immediately following this token should be read as struct/record
        /// member access (<c>[rdx].field</c>, <c>var.field</c>) rather than a directive.
        /// </summary>
        private bool IsMemberAccessTarget(MasmToken token)
        {
            switch (token.Kind)
            {
                case MasmTokenKind.Register:
                case MasmTokenKind.Identifier:
                case MasmTokenKind.Label:
                case MasmTokenKind.DataType:
                    return true;
                case MasmTokenKind.Operator:
                    return _text[token.Start] == ']' || _text[token.Start] == ')';
                default:
                    return false;
            }
        }

        /// <summary>
        /// Looks past the just-read word for a keyword that marks it as a definition name, and
        /// returns which kind: <see cref="MasmTokenKind.ProcName"/> for <c>PROC</c> / <c>ENDP</c>
        /// / <c>PROTO</c> / <c>MACRO</c>, <see cref="MasmTokenKind.Label"/> for the rest
        /// (<c>EQU</c>, <c>=</c>, <c>db</c>, <c>STRUCT</c>, ...), or <c>null</c> if it is not a
        /// definition.
        /// </summary>
        private MasmTokenKind? ClassifyDefinitionName()
        {
            int p = _pos;
            if (p >= _len || (_text[p] != ' ' && _text[p] != '\t')) return null;
            while (p < _len && (_text[p] == ' ' || _text[p] == '\t')) p++;
            if (p >= _len) return null;
            if (_text[p] == '=') return MasmTokenKind.ConstantName;

            int s = p;
            if (_text[p] == '.') p++;
            if (p >= _len || !IsIdentPart(_text[p])) return null;
            while (p < _len && IsIdentPart(_text[p])) p++;
            string follower = _text.Substring(s, p - s);

            if (MasmKeywords.ProcDefinitionFollowers.Contains(follower))
                return MasmTokenKind.ProcName;
            if (MasmKeywords.TypeDefinitionFollowers.Contains(follower))
                return MasmTokenKind.TypeName;
            if (MasmKeywords.ConstantDefinitionFollowers.Contains(follower))
                return MasmTokenKind.ConstantName;
            if (MasmKeywords.DataDefinitionFollowers.Contains(follower))
                return MasmTokenKind.DataName;
            if (MasmKeywords.DefinitionFollowers.Contains(follower))
                return MasmTokenKind.Label;
            return null;
        }

        /// <summary>Consumes a MASM COMMENT block starting at the current position (just past the keyword).</summary>
        private void ReadCommentBlock(List<MasmToken> tokens)
        {
            int p = _pos;
            while (p < _len && (_text[p] == ' ' || _text[p] == '\t' || _text[p] == '\r' || _text[p] == '\n')) p++;
            if (p >= _len) { _pos = p; return; }

            char delimiter = _text[p];
            int start = p;
            p++;                                              // opening delimiter
            while (p < _len && _text[p] != delimiter) p++;
            if (p < _len) p++;                                // closing delimiter
            while (p < _len && _text[p] != '\n' && _text[p] != '\r') p++; // rest of the closing line

            tokens.Add(new MasmToken(start, p - start, MasmTokenKind.Comment));
            _pos = p;
        }

        /// <summary>Returns the identifier/directive word at the current position without advancing.</summary>
        private string PeekWord()
        {
            int p = _pos;
            if (p < _len && _text[p] == '.') p++;
            while (p < _len && IsIdentPart(_text[p])) p++;
            return _text.Substring(_pos, p - _pos);
        }

        // --------------------------------------------------------------- char classes

        private static bool IsDigit(char c) => c >= '0' && c <= '9';

        private static bool IsHexDigit(char c) =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

        private static bool IsRadixSuffix(char c)
        {
            switch (c)
            {
                case 'h': case 'H':   // hex
                case 'b': case 'B':   // binary
                case 'y': case 'Y':   // binary
                case 'o': case 'O':   // octal
                case 'q': case 'Q':   // octal
                case 'd': case 'D':   // decimal
                case 't': case 'T':   // decimal
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsLetter(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        private static bool IsIdentStart(char c) =>
            IsLetter(c) || c == '_' || c == '@' || c == '$' || c == '?';

        private static bool IsIdentPart(char c) =>
            IsLetter(c) || IsDigit(c) || c == '_' || c == '@' || c == '$' || c == '?';

        private static bool IsOperatorChar(char c) =>
            "+-*/,()[]<>=&|^~%!".IndexOf(c) >= 0;
    }
}
