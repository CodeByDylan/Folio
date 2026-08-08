using Folio.Domain.Diagnostics;
using Folio.Domain.Toml;

namespace Folio.Domain.Configuration;

/// <summary>The schema version this parser reads, and the window it accepts.</summary>
internal static class SchemaVersion
{
    /// <summary>The version this parser writes and prefers.</summary>
    public const long Current = 1;

    /// <summary>The oldest version still accepted.</summary>
    public const long Oldest = Current - 1 < 1 ? 1 : Current - 1;

    /// <summary>Reads and checks the <c>version</c> key.</summary>
    /// <param name="document">The document to read from.</param>
    /// <param name="sink">A sink scoped to the file.</param>
    /// <param name="version">The declared version, or <see cref="Current" /> when absent.</param>
    /// <returns><see langword="false" /> if the version is outside the accepted window.</returns>
    public static bool TryRead(TomlDocumentReader document, DiagnosticSink sink, out long version)
    {
        version = Current;

        if (!document.Root.Has("version"))
        {
            sink.Warning(
                DiagnosticCodes.SchemaVersionMissing,
                $"No 'version' key; assuming {Current}.");
            return true;
        }

        long? declared = document.Root.Integer("version", sink);

        if (declared is null)
        {
            return true;
        }

        version = declared.Value;

        if (version > Current)
        {
            sink.Error(
                DiagnosticCodes.SchemaVersionUnsupportedHigh,
                $"Schema version {version} is newer than this parser understands ({Current}).",
                document.Root.PositionOf("version"));
            return false;
        }

        if (version < Oldest)
        {
            sink.Error(
                DiagnosticCodes.SchemaVersionUnsupportedLow,
                $"Schema version {version} is below the supported window ({Oldest}).",
                document.Root.PositionOf("version"));
            return false;
        }

        return true;
    }
}
