# BitMagic MASM (ml64) Syntax Highlighting

A Visual Studio 2022 extension for Microsoft Macro Assembler 64-bit (`ml64.exe`) source files.
It provides **syntax colouring** plus **Comment / Uncomment Selection**, and nothing else - no
build integration, IntelliSense, outlining or diagnostics.

## What it colours

`.asm` and `.inc` files get a dedicated **`masm`** editor content type. Each token kind reuses
a **stock classification** - the built-in editor ones where they fit, otherwise the C# /
Roslyn ones - so colours follow your theme and your existing *Fonts and Colors* settings. The
only classification the extension adds is **MASM Register** (nothing built-in fits), and it
defaults to the *Keyword* colour.

| MASM token | Examples | Fonts and Colors item |
|------------|----------|-----------------------|
| Comment | `; ...` lines **and** `COMMENT $ ... $` blocks | Comment |
| String | `"abc"`, `'a'`, with doubled-quote escapes | String |
| Number | `42`, `10h`, `0FFh`, `1010b`, `777o`, `0x1F`, `3.14`, `1.5e3`, `3F800000r` | Number |
| Instruction mnemonic | `mov`, `lea`, `vaddps`, `cmovz`, `fldpi`, ... | Keyword |
| Directive | `PROC`, `ENDP`, `MACRO`, `.code`, `.data`, `.if`, `OPTION`, `INCLUDE`, ... | Preprocessor Keyword |
| Data type / size | `db`..`dq`, `BYTE`..`ZMMWORD`, `REAL4/8/10`, `PTR`, `NEAR`, `FAR` | User Types |
| Operator | `OFFSET`, `PTR`, `DUP`, `SIZEOF`, `AND/OR/SHL/...` as operators, and punctuation | Operator |
| PROC / MACRO name, `call` / `invoke` target | `zimodem_init PROC`, `call helper`, `invoke SomeApi` | User Methods |
| STRUCT / RECORD / UNION / TYPEDEF name | `Point STRUCT`, `PNode TYPEDEF PTR Point` | User Types |
| Data variable name | `gBuf BYTE 16 dup(0)`, `gCount dd 0` | User Fields *(plain in most themes)* |
| Constant name | `MAXLEN EQU 80h`, `Banner TEXTEQU <...>` | User Constants *(plain in most themes)* |
| Code label, jump target | `done:`, `@@:`, `jz done`, `jmp short retry` | Label Name *(plain in most themes)* |
| Register | `rax`, `r8d`, `xmm0`..`zmm31`, `k0`..`k7`, `st(0)`, `cr3`, segment regs | **MASM Register** (added; defaults to Keyword) |

Only *definitions* and a few well-known reference forms (`call`/`invoke`/`jmp`/`jCC` targets)
are coloured; other references - a struct type or field in `[rdx].Type.field`, a macro
invoked bare - stay plain identifiers, because the lexer has no symbol table.

Keyword matching is **case-insensitive** (as MASM is). Words that are both an instruction and
an operator (`and`, `or`, `xor`, `not`, `shl`, `shr`) are coloured as an instruction only when
they start a statement, otherwise as an operator.

In struct / record member access - a `.` written directly against a `]`, `)`, register or
identifier, e.g. `lea rcx, [rdx].zimodem.data_dir` - the `.` is an operator and each name
(`zimodem`, `data_dir`) is a plain identifier, *not* a directive. A leading `.` with
whitespace before it (`[rdx] .field`) is still read as a directive.

## Comment / Uncomment

These editor commands work in `.asm` / `.inc` files, using `;` as the marker:

| Command | Default keys |
|---------|--------------|
| Edit > Advanced > **Comment Selection** | Ctrl+K, Ctrl+C |
| Edit > Advanced > **Uncomment Selection** | Ctrl+K, Ctrl+U |
| **Edit.ToggleLineComment** | Ctrl+K, Ctrl+/ |

* **Comment** inserts `;` at the start of every selected non-blank line (blank lines are left
  alone). With no selection it acts on the caret's line.
* **Uncomment** removes the first `;` on each selected line and nothing else, regardless of the
  indentation in front of it.
* **Toggle** uncomments when every selected non-blank line already starts (after indentation)
  with `;`, otherwise it comments.

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

## Continuous integration & releasing

Two GitHub Actions workflows are included (`.github/workflows/`):

| Workflow | Trigger | What it does |
|----------|---------|--------------|
| `build.yml` | push to `main`/`master`, any PR, manual | Restores, builds the VSIX on `windows-latest`, uploads it as a build **artifact** (`MasmSyntaxHighlight-vsix`). |
| `release.yml` | push a `v*` tag (e.g. `v1.2.0`), or manual with a version | Stamps the version into `source.extension.vsixmanifest`, builds, and publishes a **GitHub Release** with the `.vsix` attached. Optionally also pushes to the Visual Studio Marketplace. |

Both build with `msbuild` only - the VSIX packaging targets come from the
`Microsoft.VSSDK.BuildTools` NuGet package, so no Visual Studio workload is needed on the
runner.

### Cutting a release

```
git tag v1.2.0
git push origin v1.2.0
```

The manifest version does not need editing by hand - the workflow rewrites the
`<Identity Version="...">` attribute from the tag.

### Also publishing to the Visual Studio Marketplace (optional)

The Marketplace step in `release.yml` runs only if a repository secret **`VS_MARKETPLACE_PAT`**
is present. To enable it:

1. Create a publisher at <https://marketplace.visualstudio.com/manage> and note its ID.
2. Create an Azure DevOps Personal Access Token (<https://dev.azure.com> > User settings >
   Personal access tokens) with **Marketplace > Manage** scope, "All accessible organizations".
3. Add it as a repo secret: *Settings > Secrets and variables > Actions* > `VS_MARKETPLACE_PAT`.
4. Edit [`vs-publish.json`](vs-publish.json): set `publisher` to your publisher ID, `repo` to
   this repository's URL, and `identity.internalName` to a unique slug for the listing.

## Customising the colours

There are no bespoke colours to configure - each token uses a stock classification (see the
table above), so it already matches your theme. Change any of them in
**Tools > Options > Environment > Fonts and Colors**, "Show settings for: Text Editor":
edit *Comment*, *String*, *Number*, *Keyword*, *Preprocessor Keyword*, *User Types*,
*User Methods*, *Operator* etc. and every language that uses them updates too.

Several of the identifier items - *Label Name*, *User Fields*, *User Constants* - are plain
text by default in most themes (turn on **Tools > Options > Text Editor > C# > Advanced >
Color identifiers**-style semantic colouring, or just give those items a colour, to see them).
To colour registers on their own, edit the one item the extension adds: **MASM Register**
(it inherits the *Keyword* colour until you do).

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
* Data types / STRUCT names / PROC names / labels / data / constants use the Roslyn
  classifications *User Types*, *User Methods*, *Label Name*, *User Fields*, *User Constants*.
  On the rare Visual Studio install without the managed-languages component they fall back to
  *Keyword* (types) or *Symbol Definition* (the rest).
* The lexer has no symbol table. It colours definitions plus the obvious reference forms
  (`call` / `invoke` / `jmp` / `jCC` target); anything needing to know what a name *is* -
  a struct type vs a field in `[rdx].Type.field`, `X ENDS` closing a struct vs a segment,
  a bare macro call - is left as a plain identifier.

## Project layout

```
MasmSyntaxHighlight.sln
.github/workflows/
  build.yml                          CI build + artifact
  release.yml                        Tag -> GitHub Release (+ optional Marketplace)
vs-publish.json                      Visual Studio Marketplace publish manifest
src/MasmSyntaxHighlight/
  MasmSyntaxHighlight.csproj          VSIX project (VS 2022, .NET Framework 4.7.2)
  source.extension.vsixmanifest       Extension manifest (MEF component asset)
  Icon.png / PreviewImage.png         Extension icon (128) and preview (256)
  MasmContentTypes.cs                 "masm" content type + .asm/.inc mapping
  Commands/
    MasmCommentCommandHandler.cs      Comment / Uncomment Selection (inserts / removes ';')
  Classification/
    MasmClassificationNames.cs        Name of the one custom classification (MASM Register)
    MasmClassificationTypes.cs        Registers "MASM/Register" (derives from Keyword)
    MasmClassificationFormats.cs      "MASM Register" Fonts and Colors entry
    MasmClassifierProvider.cs         IClassifierProvider (one per buffer)
    MasmClassifier.cs                 IClassifier - maps tokens to built-in classifications
  Lexing/
    MasmTokenKind.cs                  Token kinds
    MasmToken.cs                      Token struct (offset/length/kind)
    MasmKeywords.cs                   Register / mnemonic / directive / type / operator lists
    MasmLexer.cs                      Hand-written MASM lexer
samples/demo.asm                      Eyeball test file
```

> If a local `msbuild` reports *"doesn't list 'win' as a RuntimeIdentifier"* after you edit
> the `.csproj`, delete `src/MasmSyntaxHighlight/obj` and restore again - it is stale
> intermediate state, not a real project error. Clean CI checkouts are unaffected.
