using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("MasmSyntaxHighlight")]
[assembly: AssemblyDescription("Syntax colouring for MASM 64-bit (ml64) .asm/.inc files.")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("BitMagic")]
[assembly: AssemblyProduct("BitMagic MASM (ml64) Syntax Highlighting")]
[assembly: AssemblyCopyright("Licensed under the GNU General Public License v3.0 (GPL-3.0-only)")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]
// Sentinel dev version: higher than anything the Marketplace will ever carry, so a locally
// built / F5'd extension is never auto-updated over your changes. Both CI workflows rewrite
// this and the manifest version before building - release.yml from the pushed vX.Y.Z tag,
// build.yml to a throwaway 0.0.<run> - so the sentinel only ever ships in a hand-built VSIX.
[assembly: AssemblyVersion("9999.0.0.0")]
[assembly: AssemblyFileVersion("9999.0.0.0")]
