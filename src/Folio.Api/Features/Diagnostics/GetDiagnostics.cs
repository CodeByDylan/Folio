using Folio.Api.Infrastructure;
using Folio.Domain.Diagnostics;
using Loom.Handlers;
using Loom.Results;

namespace Folio.Api.Features.Diagnostics.GetDiagnostics;

/// <summary>Reads the build report.</summary>
/// <param name="Severity">Only report this severity, if given.</param>
/// <param name="Project">Only report this project, if given.</param>
internal sealed record Request(string? Severity, string? Project);

/// <summary>The build report.</summary>
/// <param name="BuiltAt">When the snapshot being served was built, absent before the first success.</param>
/// <param name="LastRefresh">The most recent rebuild attempt, which may be newer than the snapshot.</param>
/// <param name="Counts">How many diagnostics of each severity are reported, before filtering.</param>
/// <param name="Diagnostics">The matching diagnostics.</param>
internal sealed record Response(
    DateTimeOffset? BuiltAt,
    RefreshView? LastRefresh,
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyList<DiagnosticView> Diagnostics);

/// <summary>The outcome of the most recent rebuild attempt.</summary>
/// <param name="AttemptedAt">When it ran.</param>
/// <param name="Outcome">How it ended.</param>
internal sealed record RefreshView(
    DateTimeOffset AttemptedAt,
    [property: WireEnum(typeof(RefreshOutcome), WireNaming.Hyphenated)] string Outcome);

/// <summary>One diagnostic.</summary>
/// <param name="Code">The stable identifier.</param>
/// <param name="Severity">How much it cost.</param>
/// <param name="Project">The project responsible.</param>
/// <param name="File">The file responsible.</param>
/// <param name="Position">Where in the file it was found.</param>
/// <param name="Pointer">The response field it concerns.</param>
/// <param name="Message">A human-readable description.</param>
internal sealed record DiagnosticView(
    string Code,
    [property: WireEnum(typeof(DiagnosticSeverity))] string Severity,
    string? Project,
    string? File,
    PositionView? Position,
    string? Pointer,
    string Message);

/// <summary>A one-based source location.</summary>
/// <param name="Line">The line number.</param>
/// <param name="Column">The column number.</param>
internal sealed record PositionView(int Line, int Column);

internal sealed class Handler(SnapshotProvider snapshots, RefreshReporter refreshes)
    : IHandler<Request, Response>
{
    // Named severities only; Enum.TryParse would also accept "0" and any other underlying value.
    private static readonly Dictionary<string, DiagnosticSeverity> Severities =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["info"] = DiagnosticSeverity.Info,
            ["warning"] = DiagnosticSeverity.Warning,
            ["error"] = DiagnosticSeverity.Error,
        };

    public Task<Result<Response>> HandleAsync(Request request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Domain.Model.Snapshot? snapshot = snapshots.Current;
        RefreshReport? refresh = refreshes.Last;
        List<Diagnostic> all = [.. refresh?.Diagnostics ?? [], .. snapshot?.Diagnostics ?? []];

        IEnumerable<Diagnostic> matching = all;

        if (request.Severity is not null)
        {
            if (!Severities.TryGetValue(request.Severity, out DiagnosticSeverity severity))
            {
                return Task.FromResult<Result<Response>>(FolioApiErrors.UnknownSeverity(request.Severity));
            }

            matching = matching.Where(diagnostic => diagnostic.Severity == severity);
        }

        if (request.Project is not null)
        {
            if (!Domain.Model.Slug.TryParse(request.Project, out _))
            {
                return Task.FromResult<Result<Response>>(FolioApiErrors.MalformedSlug(request.Project));
            }

            matching = matching.Where(diagnostic =>
                string.Equals(diagnostic.Project, request.Project, StringComparison.Ordinal));
        }

        return Task.FromResult<Result<Response>>(new Response(
            snapshot?.BuiltAt,
            refresh is null ? null : new RefreshView(refresh.AttemptedAt, Wire.Hyphenate(refresh.Outcome.ToString())),
            Enum.GetValues<DiagnosticSeverity>().ToDictionary(
                Wire.Lower,
                severity => all.Count(diagnostic => diagnostic.Severity == severity),
                StringComparer.Ordinal),
            [
                .. matching.Select(diagnostic => new DiagnosticView(
                    diagnostic.Code,
                    Wire.Lower(diagnostic.Severity),
                    diagnostic.Project,
                    diagnostic.File,
                    diagnostic.Position is null
                        ? null
                        : new PositionView(diagnostic.Position.Line, diagnostic.Position.Column),
                    diagnostic.Pointer,
                    diagnostic.Message)),
            ]));
    }
}
