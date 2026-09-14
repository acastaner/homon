# Build from the repository root so Directory.Build.props and Directory.Packages.props
# are in context:
#   docker build -f src/Homon.Api/Dockerfile -t homon-api .

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Restore against the manifest files alone, so this layer is cached until a dependency
# actually changes.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Homon.Domain/Homon.Domain.csproj src/Homon.Domain/
COPY src/Homon.Infrastructure/Homon.Infrastructure.csproj src/Homon.Infrastructure/
COPY src/Homon.Api/Homon.Api.csproj src/Homon.Api/
RUN dotnet restore src/Homon.Api/Homon.Api.csproj

# .editorconfig is a build input: EnforceCodeStyleInBuild reads it, and without it the
# style rules that are errors on a developer's machine are silently absent here.
COPY .editorconfig ./
COPY src/ src/
RUN dotnet publish src/Homon.Api/Homon.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

COPY --from=build --chown=$APP_UID:$APP_UID /app .

# Every path a named volume mounts over must exist here, owned by the runtime user, BEFORE
# `USER` drops privileges. Docker initialises an empty named volume from the image's
# directory at that path — contents *and ownership*. When the path does not exist in the
# image, the volume is created root-owned 0755 instead, the unprivileged process cannot
# write to it, and the failure is deferred to the first request that needs it:
#
#   /keys   -> every sign-in (the data-protection key ring)
#
# Compose cannot express this — `volumes:` has no ownership option — so it has to be right
# in the image.
RUN mkdir -p /keys \
 && chown $APP_UID:$APP_UID /keys

# Run unprivileged. The base image ships an `app` user for exactly this.
USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Homon.Api.dll"]
