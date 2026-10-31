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
RUN apk add --no-cache icu-libs tzdata
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
ENV TZ=Asia/Ho_Chi_Minh

COPY --from=publish /app/publish .

# Phiên bản của image (Epic 6C, P6-33): CI truyền --build-arg GIT_SHA=<sha> BUILD_VERSION=<tag>; CD so nhãn revision với sha cần triển khai
ARG GIT_SHA=""
ARG BUILD_VERSION="local"
LABEL org.opencontainers.image.revision="${GIT_SHA}"       org.opencontainers.image.version="${BUILD_VERSION}"       org.opencontainers.image.source="https://github.com/NguyenThanhTrung-N2T/SmashCourt-BE"       org.opencontainers.image.title="smashcourt-be"
ENV APP_REVISION="${GIT_SHA}"     APP_VERSION="${BUILD_VERSION}"

# Chạy bằng user không phải root có sẵn trong image .NET (app, UID $APP_UID), như TADSS
USER $APP_UID
ENTRYPOINT ["dotnet", "SmashCourt-BE.dll"]