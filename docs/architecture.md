# Architecture

Folio assembles portfolio content from **GitHub repository metadata** read via Octokit and
**authored files** committed inside the repositories themselves. The `.folio` format those files
follow is designed in a separate document; this one covers the API that reads it.

## The shape: a snapshot server

Folio does not resolve content per request. A background refresher builds the **entire portfolio** —
every project, every locale, every diagnostic — into one immutable object graph, and requests read
that graph. **No request path touches GitHub.**

```
timer / POST /refresh
        │
        ▼
  Folio.Ingestion ──► fetch refs, trees, blobs, metadata ──► FileSet + RepoMetadata
        │                                                          │
        │  blobs cached by pinned SHA                               │
        ▼                                                          ▼
  ISnapshotStore  ◄──── raw inputs ────────────────────►  Folio.Domain.Resolve()
   (file / Redis)                                                  │
                                                                   ▼
                                                     Snapshot + Diagnostics
                                                                   │
                                              Interlocked.Exchange │
                                                                   ▼
                                                        ISnapshotProvider
                                                                   │
                                                     Folio.Api slices read it
```

What this buys:

| Property | How |
| --- | --- |
| Resolution is testable with zero infrastructure | It is a pure synchronous function |
| Byte-stable responses, so ETags work | Deterministic ordering over an immutable graph |
| GitHub rate limits leave the request path | Requests never fetch |
| A broken config cannot take the site down | A failed refresh keeps the last good snapshot |
| Production bugs are replayable offline | The cached inputs are a complete reproduction case |

## Projects

```text
src/Folio.Domain/      the format: model, parsers, resolver. Pure, synchronous.
src/Folio.Ingestion/   all I/O: GitHub, the snapshot store, media probing.
src/Folio.Api/         endpoints, slices, refresh scheduling, composition root.

tests/Folio.Domain.Tests/        fixture-driven; the bulk of the suite
tests/Folio.Ingestion.Tests/     stubbed at HttpMessageHandler
tests/Folio.Api.Tests/           through HTTP, against a stubbed content source
tests/Folio.ArchitectureTests/   the boundaries below
```

Three projects, because three boundaries are worth making the compiler check. Everything else is a
folder. There is no separate application or infrastructure project: splitting one operation across
two assemblies costs more than it protects when the operation is a read from memory.

`Folio.Ingestion` exists as its own assembly for one reason. The central claim of this design is that
**requests never touch GitHub**, and if Octokit were a reference of `Folio.Api`, nothing would stop a
slice injecting a client and making "just one quick call" in the request path. Behind a separate
assembly, that is a build failure rather than something a reviewer has to catch.

`Folio.Domain` stays consumable on its own, which is what lets the CLI validator — a named deliverable
of the format design — reuse the resolver with a filesystem reader and nothing else.

## Domain is pure

`Folio.Domain` performs no I/O. Its entry point is:

```csharp
Result<Snapshot> Resolve(
    CentralInput central,
    IReadOnlyList<RepoInput> repos,
    string applicationVersion,
    DateTimeOffset builtAt,
    IReadOnlyList<Diagnostic>? priorDiagnostics = null);
```

A `CentralInput` is the central repository, its pinned commit and its files; a `RepoInput` is the same
for one project, plus the repository metadata read from GitHub and any media sizes measured for it — Domain cannot probe an image, so dimensions arrive as input.
`applicationVersion` folds into the snapshot id; `priorDiagnostics` carries what ingestion found, so
its content faults land in the same report. Failure means the central config was fatally broken;
success carries the snapshot **including its diagnostics**.

**One snapshot holds every locale.** `Snapshot.Localizations` is a `ResolvedSite` per declared locale,
all built in one pass, so a request selects a locale by dictionary lookup and does no resolution work.
That is also why parsing and localizing are separate phases: structural diagnostics are produced once
during parsing, while fallback diagnostics are produced per locale. Merging the two would report every
unknown key once per locale.

This is possible because GitHub's recursive Git Trees API returns a repository's entire file listing
in one call, so every path under `.folio/` is known **before anything is parsed**. The parse-then-fetch
cycle that would otherwise force an async pipeline does not exist.

Domain owns its parsers — Tomlyn and Markdig — rather than receiving a pre-parsed neutral tree.
Diagnostics carry file positions, and Tomlyn's syntax nodes are where those positions come from;
routing through an intermediate representation would mean either discarding them or rebuilding
position tracking by hand.

### Two phases

**Phase 1 is per-project and independent.** Parse, localize, rewrite markdown, merge authored data
with GitHub metadata. Nothing in this phase needs to know another project exists.

**Phase 2 is portfolio-wide.** Slug uniqueness, relation target resolution and auto-inversion,
ordering, assembly.

The boundary is forced, not stylistic: relation inversion and duplicate-slug detection are the only
rules that cannot be evaluated against a single project, and they define where the break goes.

### Collaborators, not stage interfaces

`TomlDocumentReader`, `CentralConfigParser`, `ProjectConfigParser`, `LocaleBundle`, `LocaleResolver`,
`MarkdownRewriter`, `SectionResolver`, `RelationGraph`, `ProjectResolver` and `PortfolioResolver` —
concrete sealed types with constructor-injected collaborators, composed by hand.
The interface count in Domain is near zero. Because Domain is pure and synchronous, every one of these
is directly constructible in a test, so the substitutability an interface would buy has no customer.

Interfaces belong where a second implementation genuinely exists — `ISnapshotStore` (file and Redis)
and `IGitHubContentSource` (real and stubbed) — and both live in `Folio.Ingestion`.

### Diagnostics are collected through a scoped sink

Threading `(value, diagnostics)` tuples through ten collaborators produces more merging boilerplate
than logic. Instead a `DiagnosticSink` is passed down, and scoped views stamp context automatically:

```csharp
DiagnosticSink file = sink.ForProject("folio").ForFile(".folio/locales/nl.toml");
```

The resolver stays pure **at its boundary** — same inputs, same outputs — because the sink is created
inside the call and never escapes it. One sink per project, merged in `projects.toml` array order,
which keeps output byte-stable and leaves phase 1 free to parallelise later.

## Ingestion

**Auth is a fine-grained PAT.** The repositories are public, but unauthenticated GitHub allows 60
requests an hour, which will not survive one refresh. A token buys 5,000.

**Blobs are keyed by pinned SHA and immutable** — fetched once, then never again. This is where most
of the call volume lives.

That falls out of persisting raw inputs: the snapshot store is a content cache with a SHA index, not
only an outage fallback. Because a blob is addressed by its own hash, a hit can never be stale, so the
fetch is skipped outright rather than revalidated.

**Mutable endpoints are revalidated, not re-fetched.** Repository metadata, trees, languages and
releases carry an `ETag`, and `ConditionalHttpClient` sends it back as `If-None-Match`. GitHub does
not charge a `304` against the rate limit, so a steady-state refresh over unchanged repositories costs
almost nothing.

That happens at the transport because Octokit exposes no supported way to send the header through its
typed clients: a decorator over `Octokit.Internal.IHttpClient` adds it, and answers a `304` from the
body it cached, reporting `200` upward. Nothing above the transport knows a revalidation happened.

The cache is **in memory and not persisted**. Persisting it would mean storing GitHub's JSON beside
the file contents for a saving that only applies to the first refresh after a restart; the blob cache
already covers the expensive part and is durable. The rate-limit check is never revalidated — a cached
budget would report a figure that has already been spent.

### Transient faults abandon the refresh; content faults drop the project

| Fault | Class | Behaviour |
| --- | --- | --- |
| `404` on a repo or path | content | Project dropped, diagnostic recorded |
| Unparseable TOML, unknown version, duplicate slug | content | Project dropped, diagnostic recorded |
| `5xx`, timeout, connection failure | transient | Refresh abandoned, previous snapshot keeps serving |
| `403`/`429` secondary rate limit, exhausted budget | transient | Refresh abandoned, retried next tick |

Without this split, a network blip removes a project from the live site and the diagnostic blames the
repository. Abandoning is strictly better: the site keeps working and the diagnostic says what
actually happened.

A refresh also checks its remaining rate-limit budget before starting and abandons up front if it
cannot plausibly complete. Half a refresh is worse than none.

### Media dimensions

Intrinsic width and height are read from the first kilobyte of the file via a range request, parsed by
hand for PNG, JPEG, GIF, WebP and SVG. No imaging library: decoding capability is not wanted, and the
obvious candidate carries a licence condition in exchange for it. An unknown or unparseable format
omits the dimensions and warns; the whole file is never downloaded.

**Dimensions are probed only for media resolved to `raw.githubusercontent.com`.** Absolute URLs are
accepted by the format and `projects.toml` may list repositories you do not own, so a media URL is
third-party input — and fetching one server-side is a server-side request forgery primitive. External
media passes through with its URL intact, no dimensions, and an `info` diagnostic.

## Refresh and snapshot lifecycle

- **A timer** (`PeriodicTimer` in a `BackgroundService`, default 15 minutes) and **`POST /refresh`**
  invoke the same handler, so both are logged, guarded and instrumented identically.
- **No webhooks.** The format deliberately supports repositories you do not own, which cannot carry a
  webhook, so timer coverage is required regardless. A GitHub Action calling `POST /refresh` covers
  "I just pushed" without inbound plumbing.
- **The snapshot is immutable and swapped by reference.** A request takes the reference once and reads
  it for its lifetime, so a mid-request swap is invisible rather than a torn read.
- **Refreshes never overlap.** A trigger arriving mid-refresh joins the running one.
- **A failed refresh leaves the previous snapshot serving.** A fatal central-config error is fatal to
  *that refresh*, not to the service.
- **The whole snapshot is rebuilt every time**, never incrementally patched. Fetches are skipped for
  unchanged SHAs; resolution always runs over the full input set. Incremental invalidation is where
  determinism dies, and re-resolving from cached bytes is milliseconds.
- **Before the first snapshot exists, content endpoints return `503`** and `/ready` reports unready.
  Not an empty portfolio — an empty portfolio is a valid state, so serving one would make "still
  booting" indistinguishable from "you have no projects".

## Persistence

There is no database. Everything served is derived from GitHub and from files committed in
repositories; there are no writes, no transactions and no user-generated state. A database here would
be a cache with a schema, a migration history and a container to run in tests.

What is persisted is the **raw inputs** — the fetched file sets and repository metadata — not the
resolved snapshot. New code always resolves with new code, so there is no second schema to version and
no stale blob that can deserialize into a plausible-looking wrong answer. On a boot with GitHub
unreachable, the portfolio is resolved by current code from cached inputs.

`ISnapshotStore` has two implementations: Redis behind a feature flag, and a file otherwise.

Both are best-effort in each direction. A read that fails reports no stored inputs, and the refresh
refetches. A write that fails is logged and nothing more — by the time it runs the snapshot has
already been published, so letting a storage outage propagate would turn a refresh that succeeded
into one reported as failed, and cost the next refresh nothing but a refetch it can afford. Each
implementation catches its own failures rather than the handler catching theirs, because the handler
cannot know what a third store would throw.

## Operations

**No Aspire.** One process with one optional dependency does not need an orchestrator, and an AppHost
would add a second way to run the app that drifts from how it is deployed. Telemetry and health
wiring live in `Infrastructure/Telemetry.cs` and `Infrastructure/Health.cs`, called once from
`Program.cs`.

| Endpoint | Meaning |
| --- | --- |
| `/alive` | The process is up. |
| `/ready` | **A snapshot exists.** Traffic must not route here before the first build completes. |

| Metric | Why |
| --- | --- |
| `folio.snapshot.age` | The alert that matters. A failing refresh is invisible by design — the previous snapshot serves perfectly — so staleness is the only signal. |
| `folio.refresh.duration` / `.outcome` | Distinguishes succeeded, abandoned-transient, failed-fatal. |
| `folio.github.calls`, remaining budget | The number wanted when a refresh gets slow. |
| `folio.diagnostics.count` by severity | Surfaces a new `error` without watching the endpoint. |

Individual diagnostics are **not** logged. There can be hundreds, they are already data at
`/diagnostics`, and duplicating them into logs trains you to ignore both.

## Single-tenant

One central repository, one site, one default locale. The central repository's identity cannot come
from inside itself, so it is application configuration (`GitHubOptions.CentralRepository`).

Multi-tenancy is not a later feature bolted on: it would make the central repository a route
parameter, the snapshot a keyed collection, ETags tenant-dimensioned, and rate limits shared between
tenants unaware of each other. It is a different system.
