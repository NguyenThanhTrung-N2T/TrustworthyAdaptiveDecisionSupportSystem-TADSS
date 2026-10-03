# TADSS Runtime

**Trustworthy Adaptive Decision Support System — Decision Runtime Engine** (Đồ án 2)

TADSS là nền tảng runtime độc lập ra quyết định cho hệ thống đặt chỗ dịch vụ. Hệ thống nhận yêu cầu quyết định (ví dụ mức đặt cọc cho một booking), chọn strategy phù hợp, thực thi strategy trong pipeline chịu lỗi Polly v8, lưu audit và cho phép người quản lý phê duyệt hoặc ghi đè. Ứng dụng đối chứng là **SmashCourt** (Đồ án 1). Chiến lược học máy (TADS) thuộc Khóa luận và nằm ngoài phạm vi repo ở Đồ án 2.

> **Trạng thái (03/10/2026):** Phase 1–4 hoàn thành trên contract v1; **Phase R — Re-baseline** đã đóng Gate MR (01/10/2026). Contract v1 đã gỡ hoàn toàn khỏi mã và tài liệu, tra lại qua tag `phase4-baseline`. **Phase 5** hiện thực contract v2 và chuyển toàn bộ kiểm thử sang hạ tầng thật. **Gói 5.1 (nền móng kiểm thử) và 5.2a–5.2d (SDK v2, lõi runtime v2, chuyển hẳn sang v2, tách Clean Architecture nhiều project) đã xong:** endpoint `POST /api/v2/decisions` và `GET /api/v2/decisions/{decisionId}`; migration `DecisionPersistenceV2` (chưa áp lên Neon); integration test chạy trên Testcontainers (PostgreSQL 16, Redis 7, Toxiproxy); hiện có **1112 unit + 121 integration test**. Chưa có: `X-Api-Key`, override, outcome, hàng chờ duyệt (gói 5.3). Kế hoạch: [`docs/planning/MASTER_EXECUTION_PLAN.md`](docs/planning/MASTER_EXECUTION_PLAN.md).

---

## Nghiệp vụ cốt lõi

- TADSS xác định được mức cọc → `APPROVED` kèm số tiền cọc.
- Không xác định được hoặc strategy lỗi → `NEED_APPROVAL`, Manager duyệt kèm mức cọc hoặc từ chối; không bao giờ tự duyệt.
- SmashCourt áp cọc 100% khi tắt feature flag hoặc TADSS không phản hồi.

Chi tiết và contract v2: [`docs/contracts/INTEGRATION_SPEC.md`](docs/contracts/INTEGRATION_SPEC.md).

---

## Cấu trúc

| Project | Vai trò | Phụ thuộc |
| :--- | :--- | :--- |
| `src/Shared.DecisionContractSDK` | Hợp đồng dùng chung v2 (`2.0.0-draft`): `Reservation` (rich domain model), DTO, enum, `StrategyDecision`/`DecisionContext`, `IReservationDecisionStrategy` | Chỉ BCL |
| `src/TADSS.Application` | Use case (`DecisionService`, `DecisionQueryService`, `StrategyEvaluator`), port (`Abstractions/{Caching,Persistence,Resilience}`), `IStrategyRegistry`, options nghiệp vụ + validator, exception của runtime | SDK |
| `src/TADSS.Infrastructure` | Adapter: audit PostgreSQL schema `tadss` (EF Core, migration), cache `StrategyDecision` Redis/`IMemoryCache`, pipeline Polly v8, health check | Application, SDK |
| `src/TADSS.Runtime.Api` | Presentation + composition root: `Program.cs`, `Composition/`, controller, middleware, xử lý lỗi RFC 7807, OpenAPI | Tất cả |
| `src/TADSS.Strategies.Default` | `DefaultDecisionStrategy` | SDK |
| `src/TADSS.Strategies.MockAlternative` | `MockAlternativeDecisionStrategy` | SDK |
| `tests/TADSS.UnitTests` | Cây mirror `src/`: SDK, strategy, Application, Infrastructure, Api (gồm drift OpenAPI), kiểm thử kiến trúc | |
| `tests/TADSS.IntegrationTests` | API qua `WebApplicationFactory` trên Testcontainers (PostgreSQL 16, Redis 7, Toxiproxy): endpoint, idempotency, cache, persistence, tiêm lỗi, migration | |

Hướng phụ thuộc: `SDK ← Application ← Infrastructure ← Runtime.Api`; chỉ `Program.cs` và `Composition/` của Runtime.Api được dùng kiểu của Infrastructure. Quy tắc do compiler và test UT-14 cưỡng chế (ADR-013).

Kiến trúc chi tiết: [`docs/architecture/SOFTWARE_ARCHITECTURE_SPECIFICATION.md`](docs/architecture/SOFTWARE_ARCHITECTURE_SPECIFICATION.md).

---

## Công nghệ

- **.NET 8**, ASP.NET Core Web API, EF Core 8 + Npgsql, PostgreSQL 16
- **Polly 8.4**, StackExchange.Redis 2.8, Swashbuckle 6.6, Serilog 8
- Kiểm thử hiện tại: xUnit 2.5.3, FluentAssertions 6.12.1, Moq 4.20, `Microsoft.AspNetCore.Mvc.Testing` 8.0, Testcontainers (PostgreSQL 16.15, Redis 7.4, Toxiproxy 2.12.0)
- Bổ sung ở Phase 5: OpenTelemetry + `grafana/otel-lgtm`, Schemathesis, Stryker.NET, k6 (xem [`docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md))

Phiên bản package khai báo tập trung trong `Directory.Packages.props`.

---

## Build, test, chạy

```bash
dotnet restore TADSS.slnx
dotnet build TADSS.slnx -c Release --warnaserror        # bắt buộc 0 warning
dotnet test  TADSS.slnx -c Release --no-build -m:1       # 1112 unit + 121 integration; integration cần Docker Desktop đang chạy
dotnet run --project src/TADSS.Runtime.Api               # http://localhost:5296/swagger
```

**Integration test cần Docker Desktop** (Testcontainers tự dựng PostgreSQL 16.15, Redis 7.4, Toxiproxy 2.12.0; mỗi test dùng một database riêng, xóa sau khi chạy). Chỉ chạy unit test (không cần Docker): `dotnet test tests/TADSS.UnitTests -c Release --no-build`.

Lệnh EF Core (chỉ đọc, không kết nối database):

```bash
ASPNETCORE_ENVIRONMENT=Testing ConnectionStrings__DefaultConnection="Host=localhost;Database=x" dotnet ef migrations list --no-connect --project src/TADSS.Infrastructure --startup-project src/TADSS.Runtime.Api
```

Biến `ConnectionStrings__DefaultConnection` ở trên là chuỗi giả (không phải secret, không mở kết nối): công cụ EF khởi động host ở design-time, mà môi trường `Testing` không nạp `appsettings.Development.json` và validator từ chối chuỗi giữ chỗ trong `appsettings.json`.

Cấu hình kết nối PostgreSQL/Redis đặt trong user-secrets hoặc `appsettings.Development.json` (không commit). `appsettings.json` chỉ chứa giá trị mẫu. Chuỗi kết nối PostgreSQL (`ConnectionStrings:DefaultConnection`, bắt buộc ở mọi môi trường, thiếu hoặc sai thì app không khởi động) phải ở dạng Npgsql `key=value` (ví dụ `Host=...;Port=5432;Database=...;Username=...;Password=...`); URI dạng `postgresql://...` (kiểu Neon hay cấp) **không** được chấp nhận, cần đổi sang dạng `key=value`.

### Môi trường E-LOCAL (docker compose)

`docker-compose.yml` là môi trường tự chứa (self-contained): không cần cài PostgreSQL/Redis trên máy. TADSS kết nối PostgreSQL/Redis **qua Toxiproxy** để kịch bản lỗi (FI) tiêm được mà không phải khởi động lại.

```bash
docker compose up -d --build     # dựng toàn bộ môi trường
docker compose down -v           # dừng và xóa dữ liệu (volume)
```

| Dịch vụ | Cổng trên máy host | Ghi chú |
| :--- | :---: | :--- |
| `tadss-runtime` | `8081` | Swagger: `http://localhost:8081/swagger` |
| `tadss-postgres` | `5433` | PostgreSQL 16.15, database `tadss` |
| `tadss-redis` | `6380` | Redis 7.4 |
| `toxiproxy` | `8474` | API điều khiển toxic; proxy nội bộ `15432` (PostgreSQL), `16379` (Redis) |
| `otel-lgtm` (Grafana) | `3001` | OTLP gRPC `4317`, OTLP HTTP `4318` |

Cờ `Tadss:Database:ApplyMigrationsOnStartup` mặc định `false`; chỉ compose đặt `true` (biến `Tadss__Database__ApplyMigrationsOnStartup`) nên migration tự áp dụng khi container khởi động.

---

## Tài liệu

Bản đồ tài liệu (mỗi chủ đề một file sở hữu): Mục III của [`docs/planning/THESIS_DEVELOPMENT_PLAYBOOK.md`](docs/planning/THESIS_DEVELOPMENT_PLAYBOOK.md).

| Cần tìm | Tài liệu |
| :--- | :--- |
| Đề cương | [`docs/planning/PROPOSAL_DO_AN_2.md`](docs/planning/PROPOSAL_DO_AN_2.md) |
| Lịch trình, phase, gate | [`docs/planning/MASTER_EXECUTION_PLAN.md`](docs/planning/MASTER_EXECUTION_PLAN.md) |
| Tổng quan & sơ đồ | [`docs/PROJECT_BLUEPRINT_AND_SYSTEM_OVERVIEW.md`](docs/PROJECT_BLUEPRINT_AND_SYSTEM_OVERVIEW.md) |
| Contract v2 & tích hợp SmashCourt | [`docs/contracts/INTEGRATION_SPEC.md`](docs/contracts/INTEGRATION_SPEC.md) |
| Danh mục kiểm thử & KPI | [`docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md) |

---

## License

Phát triển cho mục đích học thuật trong khuôn khổ Đồ án 2 (và mở rộng cho Khóa luận tốt nghiệp).
