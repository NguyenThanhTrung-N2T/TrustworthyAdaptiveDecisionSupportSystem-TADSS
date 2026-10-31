# ── Stage 1: Base Build & Restore ────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# 1. Copy Directory.Packages.props (Central Package Management) và Directory.Build.props (bật RestorePackagesWithLockFile;
#    thiếu file này thì restore --locked-mode không áp dụng lock file trong container)
COPY ["Directory.Packages.props", "Directory.Build.props", "./"]

# 2. Copy tất cả các file .csproj và packages.lock.json liên quan (giữ đúng đường dẫn) để restore cache
COPY ["src/Shared.DecisionContractSDK/Shared.DecisionContractSDK.csproj", "src/Shared.DecisionContractSDK/packages.lock.json", "src/Shared.DecisionContractSDK/"]
COPY ["src/TADSS.Strategies.Default/TADSS.Strategies.Default.csproj", "src/TADSS.Strategies.Default/packages.lock.json", "src/TADSS.Strategies.Default/"]
COPY ["src/TADSS.Strategies.MockAlternative/TADSS.Strategies.MockAlternative.csproj", "src/TADSS.Strategies.MockAlternative/packages.lock.json", "src/TADSS.Strategies.MockAlternative/"]
COPY ["src/TADSS.Application/TADSS.Application.csproj", "src/TADSS.Application/packages.lock.json", "src/TADSS.Application/"]
COPY ["src/TADSS.Infrastructure/TADSS.Infrastructure.csproj", "src/TADSS.Infrastructure/packages.lock.json", "src/TADSS.Infrastructure/"]
COPY ["src/TADSS.Runtime.Api/TADSS.Runtime.Api.csproj", "src/TADSS.Runtime.Api/packages.lock.json", "src/TADSS.Runtime.Api/"]

# 3. Restore tái lập: lock file lệch với đồ thị phụ thuộc thì build đỏ (SEC-7)
RUN dotnet restore "src/TADSS.Runtime.Api/TADSS.Runtime.Api.csproj" --locked-mode

# 4. Copy thư mục src và biên dịch
COPY src/ src/
WORKDIR "/src/src/TADSS.Runtime.Api"
RUN dotnet build "TADSS.Runtime.Api.csproj" \
    -c $BUILD_CONFIGURATION \
    --no-restore \
    -o /app/build

# ── Stage 2: Publish ─────────────────────────────────────────────────
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "TADSS.Runtime.Api.csproj" \
    -c $BUILD_CONFIGURATION \
    --no-restore \
    /p:UseAppHost=false \
    -o /app/publish

# ── Stage 3: Runtime (Alpine Linux ~100MB) ───────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final
WORKDIR /app
EXPOSE 8080

# Cấu hình Timezone VN và hỗ trợ Globalization
RUN apk add --no-cache icu-libs tzdata
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
ENV TZ=Asia/Ho_Chi_Minh
ENV ASPNETCORE_URLS=http://+:8080

COPY --from=publish /app/publish .

# Serilog File sink ghi vào logs/ (đường dẫn tương đối trong appsettings.Development.json, file này mount lúc chạy local): /app thuộc root nên phải tạo sẵn thư mục ghi được
# cho user non-root, nếu không sink im lặng bỏ log. Đặt TRƯỚC USER vì mkdir/chown cần root.
RUN mkdir -p /app/logs && chown $APP_UID /app/logs

# Phiên bản của image (Epic 6C, P6-33): CI truyền --build-arg GIT_SHA=<sha> BUILD_VERSION=<tag>; CD so nhãn revision với sha cần triển khai
ARG GIT_SHA=""
ARG BUILD_VERSION="local"
LABEL org.opencontainers.image.revision="${GIT_SHA}" \
      org.opencontainers.image.version="${BUILD_VERSION}" \
      org.opencontainers.image.source="https://github.com/NguyenThanhTrung-N2T/TrustworthyAdaptiveDecisionSupportSystem-TADSS" \
      org.opencontainers.image.title="tadss-runtime"
ENV APP_REVISION="${GIT_SHA}" \
    APP_VERSION="${BUILD_VERSION}"

# SEC-6: chạy bằng user non-root của image .NET 8 (APP_UID); đặt SAU apk add vì apk cần root
USER $APP_UID
ENTRYPOINT ["dotnet", "TADSS.Runtime.Api.dll"]
