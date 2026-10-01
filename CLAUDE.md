# TADSS-Runtime — Decision Runtime Platform

Nền tảng thực thi quyết định đặt chỗ chịu lỗi (Đồ án 2 → khoá luận). SmashCourt-BE gửi `POST /api/v1/decisions`;
runtime phân giải strategy qua DI, bọc trong Polly v8 pipeline (budget 500ms → fallback `NEED_APPROVAL`),
cache theo request-hash (Redis L2 / IMemoryCache L1), ghi audit bất biến vào PostgreSQL schema `tadss`, có human-in-the-loop
(`GET /pending-approvals`, `POST /override`). Thuộc workspace 4 repo — xem `../CLAUDE.md`.

## Lệnh
```bash
dotnet restore TADSS.slnx
dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror          # PHẢI 0 warning
dotnet test  TADSS.slnx -c Release --no-build --filter "FullyQualifiedName~TADSS.UnitTests"
dotnet test  TADSS.slnx -c Release --no-build --filter "FullyQualifiedName~TADSS.IntegrationTests"   # cần Docker (Testcontainers)
docker compose up -d --build                                            # môi trường E-LOCAL tự chứa; dừng + xóa dữ liệu: docker compose down -v
dotnet run --project src/TADSS.Runtime.Api                              # http://localhost:5296/swagger
dotnet run --project tests/TADSS.BenchmarkRunner -c Release            # benchmark nghiên cứu — chạy lâu, chỉ khi được yêu cầu
```
CI: restore → build Release → unit test → integration test (Testcontainers) → Docker build. Nhánh làm việc chính: `develop`.

## Cấu trúc (chi tiết: skill `tadss-architecture`)
| Project | Vai trò | Phụ thuộc cho phép |
|---|---|---|
| `src/Shared.DecisionContractSDK` | Domain `Reservation` (rich model, invariant), DTOs (Decisions/Governance/Mappings), Enums, `IReservationDecisionStrategy` | chỉ BCL — 0 NuGet |
| `src/TADSS.Runtime.Api` | Controllers, Services (DecisionService, StrategyResolver, RequestHasher), Middlewares, Configuration/DependencyInjection.cs, Infrastructure (Data + Migrations, Resilience, Caching) | SDK + Strategies |
| `src/TADSS.Strategies.Default` / `.MockAlternative` | Mỗi project một strategy | chỉ SDK |
| `tests/TADSS.UnitTests` | Domain, Contracts, Services, Strategies, Infrastructure, **Architecture** (dependency purity, public API surface) | |
| `tests/TADSS.IntegrationTests` | Testcontainers PostgreSQL 16 / Redis 7 / Toxiproxy: WebApplicationFactory E2E, idempotency, fault injection, governance, migration | |
| `tests/TADSS.BenchmarkRunner` | Micro-benchmark lab Phase 4 (EF InMemory — lưu trữ, không dùng làm minh chứng SLA) | |

Tài liệu: `docs/contracts/` (API_CONTRACT_V1, **CONTRACT_V1_FREEZE**, openapi-v1.yaml, schemas/, baseline_public_api.txt),
`docs/architecture/` (spec, ADR, resilience taxonomy), `docs/planning/`, `docs/research/`.

## Trạng thái & kế hoạch
- Re-baseline v2.0 (01/10/2026): lịch trình, phase, gate ở `docs/planning/MASTER_EXECUTION_PLAN.md`; mỗi chủ đề tài liệu có một file sở hữu
  (bản đồ: Mục III `docs/planning/THESIS_DEVELOPMENT_PLAYBOOK.md`) — sửa thông số ở file sở hữu, file khác chỉ dẫn link.
- Danh mục test case (UT/IT/CT/MT/FI/LT/BE/FE/E2E/RP/PT/UAT) + KPI SC1–SC9: `docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`.

## Điểm cần nhớ
- 🔒 **Contract v1.0.0 đóng băng** (hồ sơ Milestone 2): không sửa `API_CONTRACT_V1.md`, `openapi-v1.yaml`, `schemas/*.json` v1.
- **Contract v2** (Phase 5) đã được duyệt hướng đi, đặc tả tại `docs/contracts/INTEGRATION_SPEC.md`. Hiện thực đúng đặc tả;
  thay đổi wire nằm ngoài đặc tả ⇒ dừng lại hỏi người dùng. Không sửa `baseline_public_api.txt` để test xanh — chỉ cập nhật khi đóng băng v2.
- Minh chứng/kiểm thử mới chạy trên hạ tầng thật (Testcontainers PostgreSQL/Redis, Toxiproxy); **không viết thêm test dùng EF InMemory**.
  Integration test kế thừa `IntegrationTestBase` + `[Collection(TadssInfrastructureCollection.Name)]` (fixture `TadssInfrastructureFixture`, mỗi test một database riêng).
- `docker-compose.yml` = môi trường E-LOCAL tự chứa (postgres `5433`, redis `6380`, toxiproxy `8474`, runtime `8081`, Grafana `3001`, OTLP `4317/4318`).
  `Tadss:Database:ApplyMigrationsOnStartup` mặc định `false`, chỉ compose đặt `true`.
- Mất kết nối PostgreSQL (lỗi tạm thời) → `GlobalExceptionHandler` trả 503 `PERSISTENCE_UNAVAILABLE` (mã ở `Common/RuntimeErrorCodes.cs`).
- `TreatWarningsAsErrors` + XML doc bắt buộc ở SDK/Strategies; version package chỉ khai báo trong `Directory.Packages.props`.
- Options: `{X}Options` với `SectionName = "Tadss:{X}"`; strategy active chọn qua `Tadss:Strategy:ActiveStrategyName`.
- Lỗi API trả RFC 7807 `ProblemDetails`; header idempotency xem `Common/HttpHeaderNames`.
- Migration EF (schema `tadss`) chỉ tạo/áp dụng khi người dùng yêu cầu. Không đọc/sửa `appsettings.Development.json`, `.env`.
- Tên test: `{Method}_{Scenario}_{Expected}`, dùng FluentAssertions.

## Quy tắc làm việc
- **Không tự ý `git commit` / `push` / `merge` / `rebase` / `reset`** — chỉ khi người dùng yêu cầu rõ ràng (xem `../.claude/rules/git-safety.md`).
- Sửa code xong **phải** build `--warnaserror` + chạy unit test liên quan, sửa hết lỗi trước khi báo xong (skill `build-verify`).
- Class/strategy/test mới theo skill `tadss-architecture`.
