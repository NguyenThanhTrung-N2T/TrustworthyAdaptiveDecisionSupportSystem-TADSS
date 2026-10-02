# TADSS-Runtime — Decision Runtime Platform

Nền tảng thực thi quyết định đặt chỗ chịu lỗi (Đồ án 2 → khoá luận). SmashCourt-BE gửi `POST /api/v2/decisions`;
runtime chọn strategy qua `IStrategyRegistry`, bọc trong Polly v8 pipeline (budget 500ms → fallback `NEED_APPROVAL`),
cache `StrategyDecision` (Redis L2 / IMemoryCache L1), ghi audit bất biến vào PostgreSQL schema `tadss`; có `GET /api/v2/decisions/{id}`.
Chưa có (gói 5.3): `X-Api-Key`, override, outcome, hàng chờ duyệt. Thuộc workspace 4 repo — xem `../CLAUDE.md`.

## Lệnh
```bash
dotnet restore TADSS.slnx
dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror          # PHẢI 0 warning
dotnet test  TADSS.slnx -c Release --no-build --filter "FullyQualifiedName~TADSS.UnitTests"
dotnet test  TADSS.slnx -c Release --no-build --filter "FullyQualifiedName~TADSS.IntegrationTests"   # cần Docker (Testcontainers)
docker compose up -d --build                                            # môi trường E-LOCAL tự chứa; dừng + xóa dữ liệu: docker compose down -v
dotnet run --project src/TADSS.Runtime.Api                              # http://localhost:5296/swagger
```
CI: restore → build Release → unit test → integration test (Testcontainers) → Docker build. Nhánh làm việc chính: `develop`.

## Cấu trúc (chi tiết: skill `tadss-architecture`)
| Project | Vai trò | Phụ thuộc cho phép |
|---|---|---|
| `src/Shared.DecisionContractSDK` | Contract v2 (v2.0.0-draft): Domain `Reservation` (rich model, invariant), DTOs (Decisions/Governance/Outcomes/Mappings), Decisions (`StrategyDecision`, `DecisionContext`), Enums, `IReservationDecisionStrategy` | chỉ BCL — 0 NuGet |
| `src/TADSS.Runtime.Api` | Controllers, Services (DecisionService, DecisionQueryService, RequestHasher, Decisions/, Strategies/), Middlewares (ExceptionClassifier, GlobalExceptionHandler), Configuration (DependencyInjection, `AddDecisionRuntime`, Options), Infrastructure (Data + Migrations, Resilience, Caching) | SDK + Strategies |
| `src/TADSS.Strategies.Default` / `.MockAlternative` | Mỗi project một strategy | chỉ SDK |
| `tests/TADSS.UnitTests` | Common, Contracts (gồm drift OpenAPI), Controllers, Configuration, Domain, Services, Strategies, Infrastructure, Middlewares, **Architecture** (dependency purity, public API surface, `NoLegacyV1Tests`) | |
| `tests/TADSS.IntegrationTests` | Testcontainers PostgreSQL 16 / Redis 7 / Toxiproxy: WebApplicationFactory E2E, idempotency, cache, persistence, fault injection, migration | |

Tài liệu: `docs/contracts/` (INTEGRATION_SPEC = contract v2, `openapi-v2.yaml` sinh từ mã, `schemas/v2/`, `baseline_public_api.txt`; hồ sơ v1 đóng băng: API_CONTRACT_V1, CONTRACT_V1_FREEZE, openapi-v1.yaml, schemas/*.json),
`docs/architecture/` (spec, ADR, resilience taxonomy), `docs/planning/`, `docs/research/`.

## Trạng thái & kế hoạch
- Re-baseline v2.0 (01/10/2026): lịch trình, phase, gate ở `docs/planning/MASTER_EXECUTION_PLAN.md`; mỗi chủ đề tài liệu có một file sở hữu
  (bản đồ: Mục III `docs/planning/THESIS_DEVELOPMENT_PLAYBOOK.md`) — sửa thông số ở file sở hữu, file khác chỉ dẫn link.
- Danh mục test case (UT/IT/CT/MT/FI/LT/BE/FE/E2E/RP/PT/UAT) + KPI SC1–SC9: `docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`.

## Điểm cần nhớ
- 🔒 **Hồ sơ contract v1.0.0** (Milestone 2) chỉ còn là tài liệu lịch sử: không sửa `API_CONTRACT_V1.md`, `CONTRACT_V1_FREEZE.md`, `openapi-v1.yaml`, `schemas/*.json` v1. Mã v1 (SDK, runtime, `/api/v1`, BenchmarkRunner, MediatR, EF InMemory) đã gỡ ở gói 5.2c.
- **Contract v2** là bản duy nhất, đặc tả tại `docs/contracts/INTEGRATION_SPEC.md`; đã hiện thực `POST /api/v2/decisions` và `GET /api/v2/decisions/{id}`.
  Thay đổi wire nằm ngoài đặc tả ⇒ dừng lại hỏi người dùng. `openapi-v2.yaml` sinh từ mã (test `OpenApiDriftTests` báo lệch; muốn cập nhật thì chép file sinh ra theo hướng dẫn trong test).
  Không sửa `baseline_public_api.txt` để test xanh — baseline v2 là bản nháp (`2.0.0-draft`), chỉ đổi có chủ đích khi đóng băng v2.
- Minh chứng/kiểm thử mới chạy trên hạ tầng thật (Testcontainers PostgreSQL/Redis, Toxiproxy); **không viết thêm test dùng EF InMemory**.
  Integration test kế thừa `IntegrationTestBase` + `[Collection(TadssInfrastructureCollection.Name)]` (fixture `TadssInfrastructureFixture`, mỗi test một database riêng).
- `docker-compose.yml` = môi trường E-LOCAL tự chứa (postgres `5433`, redis `6380`, toxiproxy `8474`, runtime `8081`, Grafana `3001`, OTLP `4317/4318`).
  `Tadss:Database:ApplyMigrationsOnStartup` mặc định `false`, chỉ compose đặt `true`.
- Lỗi → `ExceptionClassifier` (hàm thuần) + `GlobalExceptionHandler`: mất kết nối PostgreSQL (lỗi tạm thời) → 503 `PERSISTENCE_UNAVAILABLE`; mã lỗi ở `ErrorCodes` của SDK. Audit ghi đồng bộ (fail-closed), `created_at` do `DecisionService` gán (µs, UTC) — không còn interceptor.
- `TreatWarningsAsErrors` + XML doc bắt buộc ở SDK/Strategies; version package chỉ khai báo trong `Directory.Packages.props`.
- Options: `{X}Options` với `SectionName = "Tadss:{X}"`; strategy active chọn qua `Tadss:Strategy:ActiveStrategyName` (định tuyến theo request thuộc 5.4).
- Lỗi API trả RFC 7807 `ProblemDetails`; header idempotency xem `Common/HttpHeaderNames`.
- Migration EF (schema `tadss`) chỉ tạo/áp dụng khi người dùng yêu cầu; `20261002101457_DecisionPersistenceV2` **xóa** bảng v1 và chưa được áp lên Neon. Không đọc/sửa `appsettings.Development.json`, `.env`.
- Tên test: `{Method}_{Scenario}_{Expected}`, dùng FluentAssertions.

## Quy tắc làm việc
- **Không tự ý `git commit` / `push` / `merge` / `rebase` / `reset`** — chỉ khi người dùng yêu cầu rõ ràng (xem `../.claude/rules/git-safety.md`).
- Sửa code xong **phải** build `--warnaserror` + chạy unit test liên quan, sửa hết lỗi trước khi báo xong (skill `build-verify`).
- Class/strategy/test mới theo skill `tadss-architecture`.
