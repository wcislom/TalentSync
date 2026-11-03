# One image per host: pass HOST, the project name (TalentSync.Ingress.Api, TalentSync.Sync.Worker,
# TalentSync.Matching.Worker). Build context is the repository root, for Directory.Build.props and Directory.Packages.props.
# global.json is not copied: its SDK pin would fail the build whenever the image's SDK is in a lower feature band.
# All of src/ is copied before the restore, so any source change repeats it: a host references most of src/, and listing
# every project file by hand would drift from the references.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG HOST
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish "$(find src -name "${HOST}.csproj")" -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG HOST
ENV HOST_DLL=${HOST}.dll
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["sh", "-c", "exec dotnet \"$HOST_DLL\""]
