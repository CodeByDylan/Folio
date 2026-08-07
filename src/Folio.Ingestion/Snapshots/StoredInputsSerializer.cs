using System.Text.Json;
using System.Text.Json.Serialization;
using Folio.Domain.Model;

namespace Folio.Ingestion.Snapshots;

/// <summary>Reads and writes stored inputs as JSON.</summary>
public static class StoredInputsSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Serializes stored inputs.</summary>
    /// <param name="inputs">The inputs to write.</param>
    /// <returns>The UTF-8 JSON.</returns>
    public static byte[] Serialize(StoredInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        return JsonSerializer.SerializeToUtf8Bytes(
            new Payload(
                FormatVersion,
                inputs.CentralRepo,
                inputs.CentralSha,
                Files(inputs.Central),
                [.. inputs.Repos.Select(repo => new RepoPayload(
                    repo.Repo,
                    repo.Path,
                    repo.PinnedSha,
                    Files(repo.Files),
                    repo.Metadata,
                    new Dictionary<string, MediaSize>(repo.MediaSizes, StringComparer.Ordinal),
                    [.. repo.MediaPaths.Order(StringComparer.Ordinal)])),
                ],
                inputs.CapturedAt),
            Options);
    }

    /// <summary>Deserializes stored inputs, treating anything unreadable as absent.</summary>
    /// <param name="json">The UTF-8 JSON.</param>
    /// <returns>The inputs, or <see langword="null" /> if they could not be read.</returns>
    public static StoredInputs? Deserialize(ReadOnlySpan<byte> json)
    {
        try
        {
            Payload? payload = JsonSerializer.Deserialize<Payload>(json, Options);

            if (payload is null || payload.Version != FormatVersion)
            {
                return null;
            }

            if (payload.CentralRepo is null || payload.CentralSha is null || payload.Central is null || payload.Repos is null)
            {
                return null;
            }

            if (payload.Repos.Any(repo =>
                repo is null or { Repo: null } or { Path: null } or { PinnedSha: null }
                    or { Files: null } or { Metadata: null } or { MediaSizes: null }
                    || !IsComplete(repo.Metadata)))
            {
                return null;
            }

            return new StoredInputs(
                payload.CentralRepo,
                payload.CentralSha,
                Set(payload.Central),
                [.. payload.Repos.Select(repo => new RepoInput(
                    repo.Repo,
                    repo.Path,
                    repo.PinnedSha,
                    Set(repo.Files),
                    repo.Metadata,
                    repo.MediaSizes,
                    new HashSet<string>(repo.MediaPaths ?? [], StringComparer.Ordinal)))],
                payload.CapturedAt);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static Dictionary<string, byte[]> Files(FileSet files)
    {
        Dictionary<string, byte[]> result = new(StringComparer.Ordinal);

        foreach (string path in files.Paths)
        {
            if (files.TryGet(path, out ReadOnlyMemory<byte> contents))
            {
                result[path] = contents.ToArray();
            }
        }

        return result;
    }

    private static FileSet Set(Dictionary<string, byte[]> files) =>
        new(files.Select(file => new KeyValuePair<string, ReadOnlyMemory<byte>>(file.Key, file.Value)));

    // The reflection deserializer does not enforce non-nullable reference members, so a skewed or
    // tampered blob can leave these null; check them so a bad file becomes "no snapshot", not an NRE.
    private static bool IsComplete(RepoMetadata metadata) =>
        metadata is
        {
            Owner: not null, Name: not null, Topics: not null,
            Languages: not null, Releases: not null,
        }
        && metadata.Releases.All(release => release is { TagName: not null, Url: not null })
        && metadata.Languages.All(language => language.Name is not null);

    private const int FormatVersion = 1;

    private sealed record Payload(
        int Version,
        string? CentralRepo,
        string? CentralSha,
        Dictionary<string, byte[]>? Central,
        List<RepoPayload>? Repos,
        DateTimeOffset CapturedAt);

    private sealed record RepoPayload(
        string Repo,
        string Path,
        string PinnedSha,
        Dictionary<string, byte[]> Files,
        RepoMetadata Metadata,
        Dictionary<string, MediaSize> MediaSizes,
        List<string>? MediaPaths);
}
