# BitMagic MASM (ml64) Syntax Highlighting

Editor support for **Microsoft Macro Assembler 64-bit** (`ml64.exe`) source in Visual Studio 2022.
It covers `.asm` and `.inc` files and does four things — colouring, comment commands, outlining
and brace matching — and nothing else: no build integration, IntelliSense or diagnostics.

## Syntax colouring

Every token reuses a stock Visual Studio classification, so it already matches your theme and
your **Fonts and Colors** settings:

| Token | Colour follows |
|-------|----------------|
| `; …` comments and `COMMENT $ … $` blocks | *Comment* |
| Strings, with `''` / `""` escapes | *String* |
| Numbers — `10h`, `0FFh`, `1010b`, `777o`, `0x1F`, `3.14`, `1.5e3`, `3F800000r` | *Number* |
| Instruction mnemonics (integer, x87, SSE/AVX/AVX-512, BMI, AES…) | *Keyword* |
| Directives — `PROC`, `.code`, `.if`, `OPTION`, `INCLUDE`… | *Preprocessor Keyword* |
| Size / type keywords — `db`..`dq`, `BYTE`..`ZMMWORD`, `PTR` | *User Types* |
| `STRUCT` / `RECORD` / `UNION` / `TYPEDEF` names | *User Types* |
| `PROC` / `MACRO` names, and `call` / `invoke` targets | *User Methods* |
| Data names (`buf db 0`) | *User Fields* |
| `EQU` / `=` constants | *User Constants* |
| Code labels and `jmp` / `jCC` targets | *Label Name* |
| Registers — `rax`..`r15b`, `xmm/ymm/zmm`, `k0`..`k7`, `st(0)`, `cr`, segment regs | **MASM Register** (added; defaults to the keyword colour) |

Keyword matching is case-insensitive. The dual instruction/operator words (`and`, `or`, `xor`,
`not`, `shl`, `shr`) are coloured as instructions only when they start a statement. Struct
member access — `[rdx].Point.x` — is handled: the `.` is an operator, the names are plain
identifiers, not directives.

## Comment / Uncomment

`;`-based line comments via **Comment Selection** (Ctrl+K, Ctrl+C), **Uncomment Selection**
(Ctrl+K, Ctrl+U) and **Toggle Line Comment** (Ctrl+K, Ctrl+/).

## Outlining

Collapsible regions for `PROC`/`ENDP`, `MACRO`/`ENDM`, `STRUCT`/`ENDS`, `SEGMENT`/`ENDS`,
`.IF`/`.ENDIF`, `.WHILE`/`.ENDW`, `.REPEAT`/`.UNTIL`, `IF*`/`ENDIF`, the repeat blocks, and
`;region … ;endregion` comment markers.

## Brace matching

The `()` and `[]` pair next to the caret is highlighted. Braces inside comments and strings
are ignored; angle brackets are not matched (MASM uses `<` / `>` as comparison operators).

## Customising

Nothing bespoke to configure. Adjust the standard **Fonts and Colors** items to recolour, or
edit the single added item **MASM Register**.

## Licence

[GPL-3.0-only](https://www.gnu.org/licenses/gpl-3.0.html).
