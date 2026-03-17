# ── Stage 1: Build ──────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy csproj riêng để tận dụng Docker layer cache khi restore
COPY ["SmashCourt-BE.csproj", "."]
RUN dotnet restore "./SmashCourt-BE.csproj" --runtime linux-musl-x64

# Copy toàn bộ source và build
COPY . .
RUN dotnet build "./SmashCourt-BE.csproj" \
    -c $BUILD_CONFIGURATION \
    --no-restore \
    -o /app/build

# ── Stage 2: Publish ─────────────────────────────────────────────────
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./SmashCourt-BE.csproj" \
    -c $BUILD_CONFIGURATION \
    --no-restore \
    --runtime linux-musl-x64 \
    --self-contained false \
    /p:UseAppHost=false \
    -o /app/publish

# ── Stage 3: Runtime (Alpine - nhẹ ~100MB) ───────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final
WORKDIR /app
EXPOSE 8080

# Thêm timezone và culture support cho Alpine
RUN apk add --no-cache icu-libs
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SmashCourt-BE.dll"]