using System.Collections.Frozen;

namespace Folio.Domain.Model;

/// <summary>The contents of one repository's <c>.folio</c>, keyed by repo-relative path.</summary>
public sealed class FileSet
{
    private readonly FrozenDictionary<string, ReadOnlyMemory<byte>> _files;

    /// <summary>Creates a file set from repo-relative paths to contents.</summary>
    /// <param name="files">Paths separated by <c>/</c>, without a leading slash.</param>
    public FileSet(IEnumerable<KeyValuePair<string, ReadOnlyMemory<byte>>> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        _files = files.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>Gets every path in the set.</summary>
    public IReadOnlyCollection<string> Paths => _files.Keys;

    /// <summary>Gets the contents of a file.</summary>
    /// <param name="path">A repo-relative path.</param>
    /// <param name="contents">The file's bytes.</param>
    /// <returns><see langword="true" /> if the file exists.</returns>
    public bool TryGet(string path, out ReadOnlyMemory<byte> contents) => _files.TryGetValue(path, out contents);

    /// <summary>Gets every path below a directory, ordered.</summary>
    /// <param name="directory">A repo-relative directory path, without a trailing slash.</param>
    /// <returns>The matching paths in ordinal order.</returns>
    public IEnumerable<string> Under(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        string prefix = directory + '/';

        return _files.Keys
            .Where(path => path.StartsWith(prefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);
    }
}
