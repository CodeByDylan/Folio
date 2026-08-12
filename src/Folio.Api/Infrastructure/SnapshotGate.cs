using Folio.Domain.Model;
using Loom.Results;

namespace Folio.Api.Infrastructure;

/// <summary>A snapshot and the locale a request resolved to.</summary>
/// <param name="Snapshot">The snapshot being served.</param>
/// <param name="Requested">The locale the caller asked for.</param>
/// <param name="Resolved">The declared locale actually serving it.</param>
/// <param name="Resource">The resource being served, so one validator cannot answer for another.</param>
internal sealed record SnapshotView(Snapshot Snapshot, LocaleTag Requested, LocaleTag Resolved, string Resource)
{
    /// <summary>Gets the strong validator for this resource, snapshot and locale.</summary>
    /// <remarks>The requested locale is echoed in the body, so it varies the response too.</remarks>
    public string ETag => $"\"{Snapshot.Id}:{Resource}:{Requested.Value}:{Resolved.Value}\"";
}

/// <summary>Turns a locale query into a snapshot to read, or the failure that prevents one.</summary>
internal static class SnapshotGate
{
    /// <summary>Resolves the snapshot and locale a request should be served from.</summary>
    /// <param name="snapshots">The current snapshot holder.</param>
    /// <param name="locale">The raw <c>locale</c> query value, if any.</param>
    /// <param name="resource">The resource being served, folded into the validator.</param>
    /// <returns>The view to serve, or the failure that prevents it.</returns>
    public static Result<SnapshotView> Open(SnapshotProvider snapshots, string? locale, string resource)
    {
        Snapshot? snapshot = snapshots.Current;

        if (snapshot is null)
        {
            return FolioApiErrors.NoSnapshot();
        }

        if (string.IsNullOrWhiteSpace(locale))
        {
            return new SnapshotView(snapshot, snapshot.DefaultLocale, snapshot.DefaultLocale, resource);
        }

        if (!LocaleTag.TryParse(locale, out LocaleTag requested))
        {
            return FolioApiErrors.MalformedLocale(locale);
        }

        LocaleTag candidate = requested;

        while (true)
        {
            if (snapshot.Localizations.ContainsKey(candidate))
            {
                return new SnapshotView(snapshot, requested, candidate, resource);
            }

            if (!candidate.TryTruncate(out LocaleTag parent))
            {
                return FolioApiErrors.UnservableLocale(requested, snapshot.Localizations.Keys);
            }

            candidate = parent;
        }
    }
}
