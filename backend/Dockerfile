# ---- Compilación ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restaurar primero (capa en caché mientras no cambien los proyectos ni las versiones centralizadas).
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ChronoLith.Brocal.Domain/ChronoLith.Brocal.Domain.csproj src/ChronoLith.Brocal.Domain/
COPY src/ChronoLith.Brocal.Application/ChronoLith.Brocal.Application.csproj src/ChronoLith.Brocal.Application/
COPY src/ChronoLith.Brocal.Infrastructure/ChronoLith.Brocal.Infrastructure.csproj src/ChronoLith.Brocal.Infrastructure/
COPY src/ChronoLith.Brocal.Api/ChronoLith.Brocal.Api.csproj src/ChronoLith.Brocal.Api/
RUN dotnet restore src/ChronoLith.Brocal.Api/ChronoLith.Brocal.Api.csproj

COPY src/ src/
RUN dotnet publish src/ChronoLith.Brocal.Api/ChronoLith.Brocal.Api.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---- Ejecución ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# curl: healthcheck. fonts-liberation: fuentes de los PDF (boletas, facturas, reportes; ver Documents/FontResolver.cs).
# tzdata: hora local de los folios y vencimientos.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl tzdata fonts-liberation \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    TZ=America/Mexico_City \
    Files__RootPath=/data/files \
    DataProtection__KeysPath=/data/keys

COPY --from=build /app/publish .

# Archivos subidos (fotos, firmas, PDF, XML) y llaves de Data Protection: montar un volumen en /data.
RUN mkdir -p /data/files /data/keys && chown -R $APP_UID /data
VOLUME /data

EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "ChronoLith.Brocal.Api.dll"]
