# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081
EXPOSE 5001


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/DfE.ExternalApplications.Web/DfE.ExternalApplications.Web.csproj", "src/DfE.ExternalApplications.Web/"]
COPY ["src/DfE.ExternalApplications.Application/DfE.ExternalApplications.Application.csproj", "src/DfE.ExternalApplications.Application/"]
COPY ["src/DfE.ExternalApplications.Domain/DfE.ExternalApplications.Domain.csproj", "src/DfE.ExternalApplications.Domain/"]
COPY ["src/DfE.ExternalApplications.Infrastructure/DfE.ExternalApplications.Infrastructure.csproj", "src/DfE.ExternalApplications.Infrastructure/"]
RUN dotnet restore "./src/DfE.ExternalApplications.Web/DfE.ExternalApplications.Web.csproj"
COPY . .
WORKDIR "/src/src/DfE.ExternalApplications.Web"
RUN dotnet build "./DfE.ExternalApplications.Web.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./DfE.ExternalApplications.Web.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DfE.ExternalApplications.Web.dll"]