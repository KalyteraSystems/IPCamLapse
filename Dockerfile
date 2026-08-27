FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props IPCamLapse.sln ./
COPY IPCamLapse/IPCamLapse.csproj IPCamLapse/packages.lock.json IPCamLapse/
RUN dotnet restore IPCamLapse/IPCamLapse.csproj --locked-mode

COPY IPCamLapse/ IPCamLapse/
RUN dotnet publish IPCamLapse/IPCamLapse.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && apt-get install --yes --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

RUN mkdir --parents /data \
    && chown --recursive "$APP_UID:$APP_UID" /app /data

ENV ASPNETCORE_HTTP_PORTS=8080 \
    Storage__DataPath=/data

EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["dotnet", "IPCamLapse.dll"]
