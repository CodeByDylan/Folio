# Deployment

Folio ships as a single stateless container. It holds the whole portfolio in memory, so it needs no
database — only a GitHub token, a place to keep the last successful inputs, and one instance.

## Configuration

Every setting binds from configuration, so each one is an environment variable with `__` separating
the section from the key. There is no `appsettings.json`; the container is configured entirely by
environment.

Options are validated **at startup**. A missing or malformed required value stops the process before
it serves a request, rather than failing later:

```
Unhandled exception. Microsoft.Extensions.Options.OptionsValidationException:
  DataAnnotation validation failed for 'ApiOptions' members: 'RefreshKey' with the error:
  'The RefreshKey field is required.'
```

### Required

| Variable | Constraint | Meaning |
| --- | --- | --- |
| `GitHub__Token` | non-empty | Fine-grained PAT with read access to the central repo and every showcased repo |
| `GitHub__CentralRepository` | `owner/name` | The repository holding the central `.folio` |
| `Api__RefreshKey` | ≥ 32 characters | Authorizes `POST /v1/refresh`; anything shorter is rejected at startup |

### Optional

| Variable | Default | Meaning |
| --- | --- | --- |
| `GitHub__CentralRef` | default branch | Branch, tag or SHA to read the central config from |
| `Api__AllowedOrigins__0`, `__1`, … | none | CORS origins; an empty list allows none |
| `Api__RateLimitWindow` | `00:01:00` | Rate-limit window, between 1s and 5m |
| `Api__RateLimitPermits` | `120` | Requests per window per client, 1–10000 |
| `Api__CacheMaxAge` | `00:01:00` | `max-age` on content responses, up to 1h |
| `Api__TrustForwardedHeaders` | `false` | Read the client address from `X-Forwarded-For`. **Enable only behind a trusted proxy** — a direct client can otherwise forge its address and evade the rate limit |
| `Refresh__Interval` | `00:15:00` | Time between rebuilds, 1m–24h |
| `Refresh__Timeout` | `00:05:00` | How long one rebuild may take, 30s–1h |
| `Refresh__FetchConcurrency` | `6` | In-flight GitHub requests, 1–32 |
| `Refresh__MinimumRateLimitBudget` | `500` | Remaining GitHub budget below which a rebuild will not start |
| `Refresh__MaxFileBytes` | `5242880` | Largest single file fetched from a repo |
| `Refresh__MaxFileCount` | `2000` | Most files fetched from one repo |
| `Refresh__MaxTotalBytes` | `67108864` | Most bytes fetched from one repo |
| `SnapshotStore__Mode` | `File` | `File` or `Redis` |
| `SnapshotStore__FilePath` | `folio-inputs.json` | Where the file store writes; relative paths resolve against the application directory |
| `SnapshotStore__RedisConnectionString` | none | Required when `Mode` is `Redis` |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | none | Set to enable OTLP export; telemetry is collected but not exported without it |

## Health endpoints

Both sit outside the versioned contract and outside the rate limiter, so a monitor cannot exhaust a
caller's budget.

| Path | Meaning | Unhealthy when |
| --- | --- | --- |
| `/alive` | Liveness. Runs no checks — a response means the process is serving HTTP | The process is wedged or gone |
| `/ready` | Readiness. Gates traffic | No snapshot has been built yet (`503`, `No snapshot has been built yet.`) |

The distinction matters on first boot: the process answers `/alive` immediately but stays `503` on
`/ready` until the first refresh completes. Route traffic on `/ready`, restart on `/alive`.

## Container

Multi-stage build on the .NET 10 SDK, published onto `aspnet:10.0-noble-chiseled-extra` — no shell
and no package manager in the runtime image, running as a non-root uid (1654). The `-extra` variant
keeps ICU and tzdata so globalization behaves as it does on a developer machine.

```bash
docker build -t folio .

docker run --rm -p 8080:8080 \
  -e GitHub__Token=github_pat_… \
  -e GitHub__CentralRepository=owner/portfolio \
  -e Api__RefreshKey=$(openssl rand -hex 24) \
  folio
```

There is deliberately no `HEALTHCHECK` instruction: the runtime image has no shell or `curl` to run
one, Fly ignores it in favour of the checks in `fly.toml`, and Kubernetes would use its own probes.
The health *endpoints* are the contract; each orchestrator polls them its own way.

## Fly.io

`fly.toml` defines both checks — `/ready` gates load-balancer traffic, `/alive` is a machine-level
check whose failure restarts the machine.

The readiness check's grace period is one minute, which is Fly's ceiling: a longer value is silently
lowered, so a deploy cannot be told to wait longer than that for the first snapshot. That ceiling
only bites on a **first** deploy, where the volume is empty and the app must fetch every showcased
repository before `/ready` turns green — a portfolio large enough to take over a minute will fail the
check and roll back a healthy app. Later deploys republish from the volume on boot and are ready
almost at once. If a first deploy does time out, deploy once without the health check, let the
refresh land in the volume, then restore it.

```bash
fly launch --no-deploy          # sets the app name and region
fly volumes create folio_data --size 1 --region ams

fly secrets set \
  GitHub__Token=github_pat_… \
  GitHub__CentralRepository=owner/portfolio \
  Api__RefreshKey=$(openssl rand -hex 24)

fly deploy
```

Secrets belong in `fly secrets`, never in `[env]` — everything in `fly.toml` is committed to the
repository.

### Run exactly one machine

`fly.toml` pins `min_machines_running = 1` with `auto_stop_machines = "off"`, for two reasons.

The refresher runs on a **timer inside the process**, so a stopped machine is a portfolio that has
stopped updating. Auto-stop would trade staleness for idle savings without telling anyone.

More importantly, each instance builds **its own snapshot** and derives its own ETags. Two machines
refresh independently, so they will hold different snapshot ids and answer the same request with
different validators — a client whose conditional request lands on the other machine gets a spurious
`200` instead of `304`. Scaling out needs a shared snapshot (`SnapshotStore__Mode=Redis`) and a
refresher that elects a single writer; neither exists yet. Until then, one machine.

The volume follows from the same constraint: it is per-machine and region-bound, and it exists so a
restart with GitHub unreachable can still serve the last good content rather than refusing.
