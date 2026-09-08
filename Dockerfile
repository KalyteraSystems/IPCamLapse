FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props IPCamLapse.sln ./
COPY IPCamLapse/IPCamLapse.csproj IPCamLapse/packages.lock.json IPCamLapse/
COPY OpenCamInterop/OpenCamInterop.csproj OpenCamInterop/packages.lock.json OpenCamInterop/
RUN dotnet restore IPCamLapse/IPCamLapse.csproj --locked-mode

COPY IPCamLapse/ IPCamLapse/
COPY OpenCamInterop/ OpenCamInterop/
RUN dotnet publish IPCamLapse/IPCamLapse.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl ffmpeg \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

RUN mkdir --parents /data \
    && chown --recursive "$APP_UID:$APP_UID" /app /data

ENV ASPNETCORE_HTTP_PORTS=8080 \
    Storage__DataPath=/data \
    DataProtection__KeysPath=data-protection-keys

EXPOSE 8080
USER $APP_UID

HEALTHCHECK --interval=10s --timeout=3s --start-period=20s --retries=6 \
    CMD curl --fail --silent --show-error --output /dev/null http://127.0.0.1:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "IPCamLapse.dll"]
