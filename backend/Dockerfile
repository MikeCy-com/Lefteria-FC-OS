FROM mcr.microsoft.com/dotnet/sdk:10.0.402 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/LefteriaFc.Api/LefteriaFc.Api.csproj src/LefteriaFc.Api/
COPY src/LefteriaFc.Infrastructure/LefteriaFc.Infrastructure.csproj src/LefteriaFc.Infrastructure/
COPY src/LefteriaFc.Domain/LefteriaFc.Domain.csproj src/LefteriaFc.Domain/
COPY src/LefteriaFc.Application/LefteriaFc.Application.csproj src/LefteriaFc.Application/
COPY src/LefteriaFc.Api/packages.lock.json src/LefteriaFc.Api/
COPY src/LefteriaFc.Infrastructure/packages.lock.json src/LefteriaFc.Infrastructure/
COPY src/LefteriaFc.Domain/packages.lock.json src/LefteriaFc.Domain/
COPY src/LefteriaFc.Application/packages.lock.json src/LefteriaFc.Application/
RUN dotnet restore src/LefteriaFc.Api/LefteriaFc.Api.csproj --locked-mode
COPY src/ src/
COPY third-party/ third-party/
RUN dotnet publish src/LefteriaFc.Api/LefteriaFc.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

# Explicit owner-tool target. BuildKit skips this branch for the default API
# image; no preview/Test host or fixed fixture OTP is included in either image.
FROM build AS import-build
COPY tools/LefteriaFc.RosterImport/ tools/LefteriaFc.RosterImport/
RUN dotnet restore tools/LefteriaFc.RosterImport/LefteriaFc.RosterImport.csproj --locked-mode
RUN dotnet publish tools/LefteriaFc.RosterImport/LefteriaFc.RosterImport.csproj \
    --configuration Release \
    --no-restore \
    --output /app/import \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS roster-import
WORKDIR /app
COPY --from=import-build /app/import/ ./
USER $APP_UID
ENTRYPOINT ["dotnet", "LefteriaFc.RosterImport.dll"]

# Keep the API as the final/default target for existing Compose builds.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish/ ./
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "LefteriaFc.Api.dll"]
