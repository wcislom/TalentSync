# One image per simulator: docker compose passes SIMULATOR (folder and project name under simulators/).
# Build context is the repository root, for Directory.Build.props and Directory.Packages.props.
# global.json is not copied: its SDK pin would fail the build whenever the image's SDK is in a lower feature band.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG SIMULATOR
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY simulators/${SIMULATOR}/${SIMULATOR}.csproj simulators/${SIMULATOR}/
RUN dotnet restore simulators/${SIMULATOR}/${SIMULATOR}.csproj
COPY simulators/${SIMULATOR}/ simulators/${SIMULATOR}/
RUN dotnet publish simulators/${SIMULATOR}/${SIMULATOR}.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG SIMULATOR
ENV SIMULATOR_DLL=${SIMULATOR}.dll
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["sh", "-c", "exec dotnet \"$SIMULATOR_DLL\""]
