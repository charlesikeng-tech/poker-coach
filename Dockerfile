# syntax=docker/dockerfile:1
# Production image (ADR-0011): the API serves the compiled web app from wwwroot, same origin.
#   docker build -t poker-coach .
#   docker run --rm poker-coach migrate      # release step: apply migrations, then exit
#   docker run -p 8080:8080 poker-coach      # the app

FROM node:24-alpine AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npx -y npm@11 ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY .editorconfig ./
COPY backend/global.json backend/Directory.Build.props backend/Directory.Packages.props backend/
# Project files first: the restore layer is reused until a dependency changes.
COPY backend/src/PokerCoach.Domain/PokerCoach.Domain.csproj backend/src/PokerCoach.Domain/
COPY backend/src/PokerCoach.Application/PokerCoach.Application.csproj backend/src/PokerCoach.Application/
COPY backend/src/PokerCoach.HandHistories/PokerCoach.HandHistories.csproj backend/src/PokerCoach.HandHistories/
COPY backend/src/PokerCoach.Infrastructure/PokerCoach.Infrastructure.csproj backend/src/PokerCoach.Infrastructure/
COPY backend/src/PokerCoach.Api/PokerCoach.Api.csproj backend/src/PokerCoach.Api/
RUN dotnet restore backend/src/PokerCoach.Api/PokerCoach.Api.csproj
COPY backend/src/ backend/src/
RUN dotnet publish backend/src/PokerCoach.Api/PokerCoach.Api.csproj --configuration Release --no-restore \
    --output /app -p:UseAppHost=false

# Chiseled: no shell, no package manager, non-root user. Probes are HTTP (/health/live, /health/ready).
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /src/web/dist/web/browser ./wwwroot
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "PokerCoach.Api.dll"]
