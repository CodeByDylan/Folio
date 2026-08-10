#!/usr/bin/env bash

# Writes docs/openapi.json from the built API. Run `dotnet build` first.

set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
configuration="${FOLIO_CONFIGURATION:-Debug}"
port="${FOLIO_OPENAPI_PORT:-5399}"
assembly="$root/src/Folio.Api/bin/$configuration/net10.0/Folio.Api.dll"
output="$root/docs/openapi.json"

if [ ! -f "$assembly" ]; then
    echo "No $configuration build at $assembly. Run 'dotnet build' first." >&2
    exit 1
fi

state="$(mktemp -d)"
trap 'rm -rf "$state"; [ -n "${api:-}" ] && kill "$api" 2>/dev/null; true' EXIT

# The document is served in development only, and every option is validated on start.
ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_HTTP_PORTS="$port" \
    GitHub__Token=openapi \
    GitHub__CentralRepository=openapi/openapi \
    Api__RefreshKey=00000000000000000000000000000000 \
    SnapshotStore__FilePath="$state/inputs.json" \
    dotnet "$assembly" >"$state/api.log" 2>&1 &
api=$!

if ! curl --fail --silent --output "$state/openapi.json" \
    --retry 60 --retry-delay 1 --retry-connrefused --retry-all-errors \
    "http://127.0.0.1:$port/openapi/v1.json"; then
    echo "The API never served its OpenAPI document." >&2
    cat "$state/api.log" >&2
    exit 1
fi

# servers carries whichever port this run used, which is not a fact about the contract.
node -e '
const {readFileSync, writeFileSync} = require("node:fs");
const document = JSON.parse(readFileSync(process.argv[1], "utf8"));
delete document.servers;
writeFileSync(process.argv[2], JSON.stringify(document, null, 2) + "\n");
' "$state/openapi.json" "$output"

echo "Wrote ${output#"$root/"}."
