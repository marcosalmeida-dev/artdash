# syntax=docker/dockerfile:1
# ArtDash: imagem do host web (API REST + Blazor Server + SignalR).
#   docker build -t artdash-ui .
#   docker compose --profile app up -d --build      (veja docker-compose.yml)
# O schema e os dados iniciais são criados pela própria aplicação na primeira execução.

# Runtime: ASP.NET Core 10 (Ubuntu), que já traz ICU (cultura pt-BR) e tzdata (timezone dos tenants).
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
# curl só para o HEALTHCHECK (/healthz).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
# App_Data guarda logs e uploads; criado com dono "app" para que o volume nomeado herde a permissão.
RUN mkdir -p /app/App_Data/logs /app/App_Data/uploads && chown -R app:app /app/App_Data
USER app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    TZ=America/Sao_Paulo
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
    CMD curl -fsS http://localhost:8080/healthz || exit 1


# Build: restaura primeiro só com os .csproj e os arquivos de build centrais (camada em cache
# enquanto as dependências não mudarem), depois copia o código.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "./"]
COPY ["src/ArtDash.Shared/ArtDash.Shared.csproj", "src/ArtDash.Shared/"]
COPY ["src/ArtDash.Domain/ArtDash.Domain.csproj", "src/ArtDash.Domain/"]
COPY ["src/ArtDash.Application/ArtDash.Application.csproj", "src/ArtDash.Application/"]
COPY ["src/ArtDash.Infrastructure/ArtDash.Infrastructure.csproj", "src/ArtDash.Infrastructure/"]
COPY ["src/ArtDash.UI.Client/ArtDash.UI.Client.csproj", "src/ArtDash.UI.Client/"]
COPY ["src/ArtDash.UI/ArtDash.UI.csproj", "src/ArtDash.UI/"]
RUN --mount=type=cache,id=artdash-nuget,target=/root/.nuget/packages \
    dotnet restore "src/ArtDash.UI/ArtDash.UI.csproj"
COPY . .


# Publish: sem apphost (roda com "dotnet") e sem o log SARIF (útil só no desenvolvimento).
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN --mount=type=cache,id=artdash-nuget,target=/root/.nuget/packages \
    dotnet publish "src/ArtDash.UI/ArtDash.UI.csproj" -c $BUILD_CONFIGURATION -o /app/publish \
        --no-restore -p:UseAppHost=false -p:ArtDashSarifLog=false


# Final: só a saída do publish sobre o runtime.
FROM base AS final
WORKDIR /app
COPY --from=publish --chown=app:app /app/publish .
ENTRYPOINT ["dotnet", "ArtDash.UI.dll"]
