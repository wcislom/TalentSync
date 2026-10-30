# Idempotent migration scripts of the three DbContexts and the sqlcmd that applies them (ADR 011).
# Build context is the repository root.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY dotnet-tools.json Directory.Build.props Directory.Packages.props ./
RUN dotnet tool restore
COPY src/ src/
# dotnet ef builds but does not restore.
RUN for host in Ingress/TalentSync.Ingress.Api Sync/TalentSync.Sync.Worker Matching/TalentSync.Matching.Worker; do \
      dotnet build "src/$host" || exit 1; \
    done
# dotnet ef builds each host's services to find its DbContext. It runs as Development, so appsettings.Development.json
# supplies the other keys. The connection string is a placeholder: generating a script never connects.
ENV ConnectionStrings__Sql="Server=unused"
RUN dotnet ef migrations script --no-build --idempotent --output /scripts/ingress.sql \
      --project src/Ingress/TalentSync.Ingress.Api --context IngressDbContext \
 && dotnet ef migrations script --no-build --idempotent --output /scripts/sync.sql \
      --project src/Sync/TalentSync.Sync.Infrastructure --startup-project src/Sync/TalentSync.Sync.Worker --context SyncDbContext \
 && dotnet ef migrations script --no-build --idempotent --output /scripts/matching.sql \
      --project src/Matching/TalentSync.Matching.Infrastructure --startup-project src/Matching/TalentSync.Matching.Worker --context MatchingDbContext
# EF wraps each migration in a transaction but does not set XACT_ABORT. Without it a failing statement does not stop
# the batch, so its COMMIT keeps the rest of the migration and its history row: half a migration marked as applied.
# EF writes a UTF-8 BOM, which sqlcmd skips only at the start of a file, so it is removed before the line goes in.
RUN for f in /scripts/*.sql; do \
      { printf 'SET XACT_ABORT ON;\nGO\n'; sed '1s/^\xEF\xBB\xBF//' "$f"; } > "$f.tmp" && mv "$f.tmp" "$f" || exit 1; \
    done

# sqlcmd from Microsoft's package repository, as documented for Ubuntu. The standalone tools image is from 2017 and
# the SQL Server image is over 1.5 GB. The base is the Ubuntu of the .NET 10 images.
FROM ubuntu:24.04
# libgssapi-krb5-2: the ODBC driver loads it without declaring it, so removing curl would remove it too.
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl \
 && curl -sSLo /tmp/packages-microsoft-prod.deb https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb \
 && dpkg -i /tmp/packages-microsoft-prod.deb \
 && apt-get update \
 && ACCEPT_EULA=Y apt-get install -y --no-install-recommends mssql-tools18 libgssapi-krb5-2 \
 && apt-get purge -y curl \
 && apt-get autoremove -y \
 && rm -rf /var/lib/apt/lists/* /tmp/packages-microsoft-prod.deb
COPY --from=build /scripts/ /migrations/
COPY --chmod=755 deploy/migrate.sh /migrations/migrate.sh
USER ubuntu
ENTRYPOINT ["/migrations/migrate.sh"]
