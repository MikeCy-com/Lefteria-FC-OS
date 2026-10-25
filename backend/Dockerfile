FROM mcr.microsoft.com/dotnet/sdk:10.0.402 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/LefteriaFc.Api/LefteriaFc.Api.csproj src/LefteriaFc.Api/
COPY src/LefteriaFc.Infrastructure/LefteriaFc.Infrastructure.csproj src/LefteriaFc.Infrastructure/
COPY src/LefteriaFc.Domain/LefteriaFc.Domain.csproj src/LefteriaFc.Domain/
COPY src/LefteriaFc.Api/packages.lock.json src/LefteriaFc.Api/
COPY src/LefteriaFc.Infrastructure/packages.lock.json src/LefteriaFc.Infrastructure/
COPY src/LefteriaFc.Domain/packages.lock.json src/LefteriaFc.Domain/
RUN dotnet restore src/LefteriaFc.Api/LefteriaFc.Api.csproj --locked-mode
COPY src/ src/
RUN dotnet publish src/LefteriaFc.Api/LefteriaFc.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish/ ./
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "LefteriaFc.Api.dll"]
