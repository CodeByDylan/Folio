# Local development

Folio can serve a portfolio without reaching GitHub, so a change to this repository or to a central
`.folio` can be seen in a frontend before it is pushed anywhere.

## Configuration

Which portfolio you serve is yours, not this repository's, so it is not committed. Copy the template:

```bash
cp src/Folio.Api/appsettings.Local.example.json src/Folio.Api/appsettings.Local.json
```

`appsettings.Local.json` is gitignored and overrides `appsettings.Development.json`, which carries only
portfolio-agnostic defaults. Environment variables still win over both.

Secrets go in user secrets, which never enter the working tree:

```bash
dotnet user-secrets set "GitHub:Token" "<a fine-grained PAT with read access to the listed repos>" \
    --project src/Folio.Api
dotnet user-secrets set "Api:RefreshKey" "$(openssl rand -hex 16)" --project src/Folio.Api
```

Anything added to `appsettings.Development.json` is also loaded by the API test host, so a value that
contradicts a test's expectation fails the suite rather than only affecting a local run.

## Recording a capture

Replay reads a recorded input set, which the file snapshot store already writes after every successful
fetch. Record one by starting once against GitHub:

```bash
dotnet run --project src/Folio.Api
```

`folio-inputs.json` appears in the application directory, which is `src/Folio.Api/bin/<configuration>/<tfm>`.
It is gitignored: it is a build artifact keyed to a moment in time, not source. Set an absolute
`SnapshotStore:FilePath` in `appsettings.Local.json` to keep a capture across a `dotnet clean`.

## Replaying it

Set `Content:Mode` to `Replay` in `appsettings.Local.json` and start again. No token is needed once a
capture exists, and no request reaches GitHub.

```json
{
  "GitHub": {
    "CentralRepository": "your-owner/your-central-repo"
  },
  "Content": {
    "Mode": "Replay",
    "Overlays": {
      "your-central-repo": "../../../your-central-repo"
    }
  }
}
```

Every repository in `Overlays` is read from that working tree instead of the capture. The key matches
either `owner/name` or the bare repository name, case-insensitively; the value is the directory holding
`.folio`, absolute or relative to `src/Folio.Api`. The whole `.folio` subtree is replaced, so a file
deleted locally is absent from the build too.

Three rules keep a replayed build honest:

- **Metadata always comes from the capture.** Stars, topics, languages and releases exist only in the
  GitHub API, so an overlay can never change them. Startup logs how old the capture is.
- **A replayed build never writes to the store.** The capture is an input, so replaying cannot
  overwrite the recording it came from.
- **`Replay` outside Development refuses to boot.** A stale capture served in production would look
  perfectly healthy, so the failure is made loud and immediate.

Media is the one place a replay is visibly not the real thing. A locally added or edited image is
measured, so its dimensions and diagnostics are right, but media URLs address the captured commit on
`raw.githubusercontent.com` and the image will not load until it is pushed.

## Generating a frontend's types against a branch

A frontend generated from `@hey-api/openapi-ts` reads its contract from a published `openapi.json`.
Point it at a local checkout to generate against whatever is committed here:

```bash
./scripts/openapi.sh                       # writes docs/openapi.json from the built API
FOLIO_OPENAPI=../Folio/docs/openapi.json pnpm api:generate
```

Then set the frontend's API base URL to the local API. Reads are anonymous and a server-rendered
frontend fetches them server-side, so no key and no CORS origin are needed.
