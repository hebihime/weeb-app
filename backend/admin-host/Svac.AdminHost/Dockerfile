# backend/admin-host/Svac.AdminHost/Dockerfile — SLICE_S5_CONTRACT.md §0/§1a. Mirrors
# backend/public-host/Svac.PublicApi/Dockerfile's shape exactly (build context = repo root so this stage
# can COPY backend/ and i18n/; runtime image serves /health + the sign-in/dashboard stubs). The admin
# host references ONLY backend/domain-core + its own backend/admin-host subtree — never backend/modules
# (identity/AimlRouter) — so this image never bloats with a module it does not consume, and the admin
# trust boundary (§0 law c/d) holds at the container-image layer too.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# .editorconfig marks backend/**/Migrations/*.cs as generated_code — without it in the build context,
# the analyzer treats the migration file as regular hand-written code (the same S1 Dockerfile scar).
COPY .editorconfig .editorconfig
COPY backend/Directory.Build.props backend/Directory.Build.props
COPY backend/Directory.Packages.props backend/Directory.Packages.props
COPY backend/Svac.sln backend/Svac.sln
COPY backend/domain-core/ backend/domain-core/
COPY backend/admin-host/ backend/admin-host/

RUN dotnet restore backend/admin-host/Svac.AdminHost/Svac.AdminHost.csproj
RUN dotnet publish backend/admin-host/Svac.AdminHost/Svac.AdminHost.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl is not in the aspnet base image by default; added so both this Dockerfile's own HEALTHCHECK and
# docker-compose.yml's healthcheck can probe /health the same way every other S0/S1 service already does.
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# The mcr.microsoft.com/dotnet/aspnet base image already sets ASPNETCORE_HTTP_PORTS=8080 by default;
# also setting ASPNETCORE_URLS here would override it and log a benign-but-noisy startup warning on
# every single boot — the same fresh-boot log-sweep false-positive Svac.PublicApi's own Dockerfile
# documents. Left unset deliberately.
EXPOSE 8080

HEALTHCHECK --interval=5s --timeout=5s --retries=10 --start-period=15s \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Svac.AdminHost.dll"]
