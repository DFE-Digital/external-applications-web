# Set the major version of dotnet
ARG DOTNET_VERSION=8.0

# Stage 1 - Build the app using the dotnet SDK
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION}-azurelinux3.0 AS build
WORKDIR /build

# Mount GitHub Token as a Docker secret so that NuGet Feed can be accessed
RUN --mount=type=secret,id=github_token dotnet nuget add source --username USERNAME --password $(cat /run/secrets/github_token) --store-password-in-clear-text --name github "https://nuget.pkg.github.com/DFE-Digital/index.json"

# Copy the application code
COPY ./src/ ./

# Build and publish the dotnet solution
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore && \
    dotnet build --no-restore -c Release && \
    dotnet publish --no-build -o /app

# Stage 2 - Build a runtime environment
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}-azurelinux3.0 AS final
WORKDIR /app
LABEL org.opencontainers.image.source="https://github.com/DFE-Digital/external-applications-web"
LABEL org.opencontainers.image.description="External Applications - App"


COPY --from=build /app /app
COPY ./script/web-docker-entrypoint.sh /app/docker-entrypoint.sh
RUN chmod +x ./docker-entrypoint.sh

USER $APP_UID
