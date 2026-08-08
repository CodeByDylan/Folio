# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore from the manifests alone so a source-only change keeps the package layer.
COPY global.json Directory.Build.props Directory.Packages.props ./
# Carries the analyzer triage; without it the suppressed rules fail the build as errors.
COPY .editorconfig ./
COPY src/Folio.Api/Folio.Api.csproj src/Folio.Api/
COPY src/Folio.Domain/Folio.Domain.csproj src/Folio.Domain/
COPY src/Folio.Ingestion/Folio.Ingestion.csproj src/Folio.Ingestion/
RUN dotnet restore src/Folio.Api/Folio.Api.csproj

COPY src/ src/
RUN dotnet publish src/Folio.Api/Folio.Api.csproj --configuration Release --no-restore --output /app

# Chiseled: no shell and no package manager, so the runtime carries no interactive attack surface.
# The -extra variant keeps ICU and tzdata, so globalization stays the same as on a developer machine.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Provided by the base image as a non-root uid.
USER $APP_UID

ENTRYPOINT ["dotnet", "Folio.Api.dll"]
