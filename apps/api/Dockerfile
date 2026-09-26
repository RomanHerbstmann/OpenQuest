# Build from the repository root (like docker-compose.yml does):
#   docker build -f apps/api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so code changes don't download the packages again.
COPY apps/api/OpenQuest.Api/OpenQuest.Api.csproj apps/api/OpenQuest.Api/
COPY packages/core/OpenQuest.Core/OpenQuest.Core.csproj packages/core/OpenQuest.Core/
RUN dotnet restore apps/api/OpenQuest.Api/OpenQuest.Api.csproj

COPY apps/api/OpenQuest.Api/ apps/api/OpenQuest.Api/
COPY packages/core/OpenQuest.Core/ packages/core/OpenQuest.Core/
RUN dotnet publish apps/api/OpenQuest.Api/OpenQuest.Api.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# Npgsql probes for GSSAPI (Kerberos) on connect and logs an error without it.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 tzdata \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "OpenQuest.Api.dll"]
