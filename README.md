# BitMagic MASM (ml64) Syntax Highlighting

A Visual Studio 2022 extension that provides **syntax colouring only** for Microsoft Macro
Assembler 64-bit (`ml64.exe`) source files. It contributes no build integration, IntelliSense,
outlining or diagnostics - just colours.

## What it colours

`.asm` and `.inc` files get a dedicated **`masm`** editor content type with nine classification
types:

| Classification            | Examples                                              |
|---------------------------|------------------------------------------------------|
| Comment                   | `; ...` line comments **and** `COMMENT $ ... $` blocks |
| String                    | `"abc"`, `'a'`, with doubled-quote escapes            |
| Number                    | `42`, `10h`, `0FFh`, `1010b`, `777o`, `0x1F`, `3.14`, `1.5e3`, `3F800000r` |
| Register                  | `rax`, `r8d`, `xmm0`..`zmm31`, `k0`..`k7`, `st(0)`, `cr3`, segment regs |
| Instruction mnemonic      | `mov`, `lea`, `vaddps`, `cmovz`, `fldpi`, ...         |
| Directive                 | `PROC`, `ENDP`, `MACRO`, `.code`, `.data`, `.if`, `OPTION`, `INCLUDE`, ... |
| Data type                 | `db`..`dq`, `BYTE`..`ZMMWORD`, `REAL4/8/10`, `PTR`, `NEAR`, `FAR` |
| Operator                  | `OFFSET`, `PTR`, `DUP`, `SIZEOF`, `AND/OR/SHL/...` used as operators, and punctuation |
| Label                     | `name:` code labels and definition names (`name PROC`, `name EQU`, `name db ...`) |

Keyword matching is **case-insensitive** (as MASM is). Words that are both an instruction and
an operator (`and`, `or`, `xor`, `not`, `shl`, `shr`) are coloured as an instruction only when
they start a statement, otherwise as an operator.

## Requirements

* Visual Studio 2022 (17.x), 64-bit.
* To **build** the extension: the *"Visual Studio extension development"* workload
  (supplies the VSSDK). NuGet restore needs internet access the first time.

## Build

From a *Developer Command Prompt / Developer PowerShell for VS 2022*:

```
msbuild MasmSyntaxHighlight.sln /t:Restore,Build /p:Configuration=Release
```

or just open `MasmSyntaxHighlight.sln` in Visual Studio and build.

The extension is produced at:

```
src\MasmSyntaxHighlight\bin\Release\MasmSyntaxHighlight.vsix
```

## Run / debug

Press **F5** in Visual Studio. This launches the *experimental instance* of VS with the
extension loaded; open `samples\demo.asm` to see the colouring.

## Install

Double-click `MasmSyntaxHighlight.vsix` (or use *Extensions > Manage Extensions*) and restart
Visual Studio. Open any `.asm` / `.inc` file.

## Customising the colours

The shipped colours are mid-tone so they read on both the light and dark themes. Override any
of them per theme in:

**Tools > Options > Environment > Fonts and Colors**, "Show settings for: Text Editor",
items named **`MASM ...`** (e.g. *MASM Instruction Mnemonic*, *MASM Register*).

## Extending the keyword lists

Every recognised word lives in
[`src/MasmSyntaxHighlight/Lexing/MasmKeywords.cs`](src/MasmSyntaxHighlight/Lexing/MasmKeywords.cs)
as whitespace-separated lists. Add missing instructions or directives there; no other code
needs to change.

## Notes & limitations

* Colouring only - by design.
* The buffer is fully re-lexed on every edit. This is negligible for assembly files; if you
  open a very large generated `.asm`, expect the classifier to do O(n) work per keystroke.
* `.inc` is mapped to the `masm` content type. If you use `.inc` for C/C++ includes in the
  same solution, open those with *File > Open With* or remove the `.inc` mapping in
  [`src/MasmSyntaxHighlight/MasmContentTypes.cs`](src/MasmSyntaxHighlight/MasmContentTypes.cs).
* Theme-adaptive default colours (separate light/dark values) are a possible future
  enhancement; today VS applies one default colour per classification.

## Project layout

```
MasmSyntaxHighlight.sln
src/MasmSyntaxHighlight/
  MasmSyntaxHighlight.csproj          VSIX project (VS 2022, .NET Framework 4.7.2)
  source.extension.vsixmanifest       Extension manifest (MEF component asset)
  MasmContentTypes.cs                 "masm" content type + .asm/.inc mapping
  Classification/
    MasmClassificationNames.cs        Classification type name constants
    MasmClassificationTypes.cs        MEF ClassificationTypeDefinition exports
    MasmClassificationFormats.cs      Default colours (EditorFormatDefinition)
    MasmClassifierProvider.cs         IClassifierProvider (one per buffer)
    MasmClassifier.cs                 IClassifier - maps tokens to classification spans
  Lexing/
    MasmTokenKind.cs                  Token kinds
    MasmToken.cs                      Token struct (offset/length/kind)
    MasmKeywords.cs                   Register / mnemonic / directive / type / operator lists
    MasmLexer.cs                      Hand-written MASM lexer
samples/demo.asm                      Eyeball test file
```
