using System.Net.Http;
using Folio.Domain.Content;
using Folio.Domain.Model;

namespace Folio.Ingestion.Media;

/// <summary>Measures an image without downloading it.</summary>
public interface IMediaProbe
{
    /// <summary>Reads an image's intrinsic size from its leading bytes.</summary>
    /// <param name="repo">The repository, as <c>owner/name</c>.</param>
    /// <param name="pinnedSha">The commit to read from.</param>
    /// <param name="path">The repo-relative path of the image.</param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>The size, or <see langword="null" /> if it could not be measured.</returns>
    Task<MediaSize?> MeasureAsync(string repo, string pinnedSha, string path, CancellationToken cancellationToken);
}

/// <summary>Measures images hosted on <c>raw.githubusercontent.com</c>, and nothing else.</summary>
public sealed class MediaProbe(HttpClient http) : IMediaProbe
{
    /// <inheritdoc />
    public async Task<MediaSize?> MeasureAsync(
        string repo,
        string pinnedSha,
        string path,
        CancellationToken cancellationToken)
    {
        Uri url = RawContentUrl.For(repo, pinnedSha, path);

        try
        {
            byte[]? header = await ReadAsync(url, ImageHeader.ProbeLength, cancellationToken);

            if (header is null)
            {
                return null;
            }

            if (ImageHeader.TryRead(header, out MediaSize size))
            {
                return size;
            }

            // A second round-trip only for the format that can need one.
            if (!ImageHeader.NeedsLongerRead(header))
            {
                return null;
            }

            byte[]? extended = await ReadAsync(url, ImageHeader.ExtendedProbeLength, cancellationToken);

            return extended is not null && ImageHeader.TryRead(extended, out MediaSize larger) ? larger : null;
        }
        // Dimensions are an optimization, so no transport fault here is worth abandoning a refresh for.
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task<byte[]?> ReadAsync(Uri url, int length, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, length - 1);

        using HttpResponseMessage response = await http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        // An origin may ignore Range and answer 200 with the whole file, which is not worth buffering.
        await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);
        byte[] buffer = new byte[length];
        int read = await body.ReadAtLeastAsync(buffer, length, throwOnEndOfStream: false, cancellationToken);

        return read == length ? buffer : buffer[..read];
    }
}

/// <summary>Measures through a pooled client, so its handler rotates.</summary>
internal sealed class PooledMediaProbe(IHttpClientFactory factory) : IMediaProbe
{
    /// <inheritdoc />
    public Task<MediaSize?> MeasureAsync(
        string repo,
        string pinnedSha,
        string path,
        CancellationToken cancellationToken) =>
        new MediaProbe(factory.CreateClient(nameof(MediaProbe)))
            .MeasureAsync(repo, pinnedSha, path, cancellationToken);
}
