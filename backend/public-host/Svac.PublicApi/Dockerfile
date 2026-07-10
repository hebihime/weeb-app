# backend/public-host/Svac.PublicApi/Dockerfile — SLICE_S1_CONTRACT.md §0/§12.1.
# Build context is the REPO ROOT (docker-compose.yml), not this directory, so this stage can COPY
# backend/, contracts/, and i18n/ — the host reads i18n/locales.json at boot (§1c) and the OpenAPI
# contract lives at contracts/openapi.v0.json (§1c drift gate).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src


# .editorconfig marks backend/**/Migrations/*.cs as generated_code (root .editorconfig's own comment
# explains why) — without it in the build context, the analyzer treats the migration file as regular
# hand-written code and CA1861 fires on its raw-SQL loops, a real repro caught building this image.
COPY .editorconfig .editorconfig
COPY backend/Directory.Build.props backend/Directory.Build.props
COPY backend/Directory.Packages.props backend/Directory.Packages.props
COPY backend/Svac.sln backend/Svac.sln
COPY backend/domain-core/ backend/domain-core/
COPY backend/public-host/ backend/public-host/

RUN dotnet restore backend/public-host/Svac.PublicApi/Svac.PublicApi.csproj
RUN dotnet publish backend/public-host/Svac.PublicApi/Svac.PublicApi.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl is not in the aspnet base image by default; added so both this Dockerfile's own HEALTHCHECK and
# docker-compose.yml's healthcheck (matching the CMD-SHELL pattern every other S0 service already uses)
# can probe /health the same way.
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
COPY i18n/ i18n/

# The mcr.microsoft.com/dotnet/aspnet base image already sets ASPNETCORE_HTTP_PORTS=8080 by default;
# also setting ASPNETCORE_URLS here would override it and log a benign-but-noisy startup warning
# ("Overriding HTTP_PORTS ... Binding to values defined by URLS instead") on every single boot — a real
# log-sweep false-positive risk caught running the compose fresh-boot smoke test. Left unset deliberately.
EXPOSE 8080

HEALTHCHECK --interval=5s --timeout=5s --retries=10 --start-period=15s \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Svac.PublicApi.dll"]
