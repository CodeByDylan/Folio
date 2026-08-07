using System.Collections.Concurrent;
using System.Net;
using Octokit;
using Octokit.Internal;

namespace Folio.Ingestion.GitHub;

/// <summary>A response GitHub has already sent, kept so a <c>304</c> can be answered from it.</summary>
/// <param name="ETag">The validator to send back as <c>If-None-Match</c>.</param>
/// <param name="Body">The body the last <c>200</c> carried.</param>
/// <param name="ContentType">That response's content type.</param>
internal sealed record CachedResponse(string ETag, object? Body, string ContentType);

/// <summary>Remembers the last response for a URL, so it can be revalidated instead of re-fetched.</summary>
public sealed class EtagCache(int capacity = 512)
{
    private readonly ConcurrentDictionary<string, CachedResponse> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _order = new();

    /// <summary>Gets how many responses are held.</summary>
    public int Count => _entries.Count;

    internal bool TryGet(string key, out CachedResponse entry) => _entries.TryGetValue(key, out entry!);

    internal void Set(string key, CachedResponse entry)
    {
        if (_entries.TryAdd(key, entry))
        {
            _order.Enqueue(key);
        }
        else
        {
            _entries[key] = entry;
        }

        // Tree keys carry a commit SHA, so the key space grows without a bound of its own.
        while (_entries.Count > capacity && _order.TryDequeue(out string? oldest))
        {
            _ = _entries.TryRemove(oldest, out _);
        }
    }
}

/// <summary>Adds <c>If-None-Match</c> to GitHub reads and answers a <c>304</c> from the cached body.</summary>
internal sealed class ConditionalHttpClient(IHttpClient inner, EtagCache cache) : IHttpClient
{
    /// <inheritdoc />
    public async Task<IResponse> Send(
        IRequest request,
        CancellationToken cancellationToken,
        Func<object, object>? preprocessResponseBody = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? key = KeyFor(request);
        CachedResponse? previous = key is null ? null : Entry(key);

        if (previous is not null)
        {
            request.Headers["If-None-Match"] = previous.ETag;
        }

        IResponse response = await inner.Send(request, cancellationToken, preprocessResponseBody);

        if (response.StatusCode == HttpStatusCode.NotModified && previous is not null)
        {
            return new ReplayedResponse(previous.Body, previous.ContentType, response);
        }

        if (key is not null && response.StatusCode == HttpStatusCode.OK)
        {
            string? etag = response.Headers.TryGetValue("ETag", out string? value) ? value : response.ApiInfo?.Etag;

            if (!string.IsNullOrEmpty(etag))
            {
                cache.Set(key, new CachedResponse(etag, response.Body, response.ContentType));
            }
        }

        return response;
    }

    /// <inheritdoc />
    public void SetRequestTimeout(TimeSpan timeout) => inner.SetRequestTimeout(timeout);

    /// <inheritdoc />
    public void Dispose() => inner.Dispose();

    private CachedResponse? Entry(string key) => cache.TryGet(key, out CachedResponse entry) ? entry : null;

    private static string? KeyFor(IRequest request)
    {
        if (request.Method != HttpMethod.Get || request.Endpoint is null)
        {
            return null;
        }

        string endpoint = request.Endpoint.ToString();

        // The budget must be live, and a blob is addressed by its own hash so it is fetched at most once.
        if (endpoint.Contains("rate_limit", StringComparison.OrdinalIgnoreCase)
            || endpoint.Contains("git/blobs", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string parameters = request.Parameters is { Count: > 0 }
            ? string.Join('&', request.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => $"{p.Key}={p.Value}"))
            : string.Empty;

        return parameters.Length == 0 ? endpoint : $"{endpoint}?{parameters}";
    }

    private sealed class ReplayedResponse(object? body, string contentType, IResponse original) : IResponse
    {
        public object? Body { get; } = body;

        public IReadOnlyDictionary<string, string> Headers { get; } = original.Headers;

        public ApiInfo ApiInfo { get; } = original.ApiInfo;

        public HttpStatusCode StatusCode { get; } = HttpStatusCode.OK;

        public string ContentType { get; } = contentType;
    }
}
