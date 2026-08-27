using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Utilities;

namespace MasmSyntaxHighlight
{
    /// <summary>
    /// Declares the "masm" editor content type and maps the file extensions we colour to it.
    /// </summary>
    internal static class MasmContentTypes
    {
        /// <summary>Name of the content type used throughout the extension.</summary>
        public const string ContentType = "masm";

#pragma warning disable 649 // fields are assigned by the MEF composition engine

        [Export(typeof(ContentTypeDefinition))]
        [Name(ContentType)]
        [BaseDefinition("code")]
        internal static ContentTypeDefinition MasmContentTypeDefinition;

        [Export(typeof(FileExtensionToContentTypeDefinition))]
        [FileExtension(".asm")]
        [ContentType(ContentType)]
        internal static FileExtensionToContentTypeDefinition AsmFileExtensionDefinition;

        [Export(typeof(FileExtensionToContentTypeDefinition))]
        [FileExtension(".inc")]
        [ContentType(ContentType)]
        internal static FileExtensionToContentTypeDefinition IncFileExtensionDefinition;

#pragma warning restore 649
    }
}
