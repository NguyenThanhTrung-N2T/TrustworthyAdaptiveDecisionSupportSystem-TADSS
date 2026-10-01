# TADSS Runtime

**Trustworthy Adaptive Decision Support System — Decision Runtime Engine** (Đồ án 2)

TADSS là nền tảng runtime độc lập ra quyết định cho hệ thống đặt chỗ dịch vụ. Hệ thống nhận yêu cầu quyết định (ví dụ mức đặt cọc cho một booking), chọn strategy phù hợp, thực thi strategy trong pipeline chịu lỗi Polly v8, lưu audit và cho phép người quản lý phê duyệt hoặc ghi đè. Ứng dụng đối chứng là **SmashCourt** (Đồ án 1). Chiến lược học máy (TADS) thuộc Khóa luận và nằm ngoài phạm vi repo ở Đồ án 2.

> **Trạng thái (01/10/2026):** Phase 1–4 hoàn thành trên contract v1 (Milestone 4: 510 test pass; sau khi gỡ module chuẩn hóa dataset ở Phase R còn 473 unit + 27 integration test, integration test hiện chạy với EF Core InMemory). Đang ở **Phase R — Re-baseline**; Phase 5 hiện thực contract v2 và chuyển toàn bộ kiểm thử sang hạ tầng thật. Kế hoạch: [`docs/planning/MASTER_EXECUTION_PLAN.md`](docs/planning/MASTER_EXECUTION_PLAN.md).

---

## Nghiệp vụ cốt lõi

- TADSS xác định được mức cọc → `APPROVED` kèm số tiền cọc.
- Không xác định được hoặc strategy lỗi → `NEED_APPROVAL`, Manager duyệt kèm mức cọc hoặc từ chối; không bao giờ tự duyệt.
- SmashCourt áp cọc 100% khi tắt feature flag hoặc TADSS không phản hồi.

Chi tiết và contract v2: [`docs/contracts/INTEGRATION_SPEC.md`](docs/contracts/INTEGRATION_SPEC.md).

---

## Cấu trúc

| Project | Vai trò |
| :--- | :--- |
| `src/Shared.DecisionContractSDK` | Hợp đồng dùng chung: `Reservation` (rich domain model), DTO, enum, `IReservationDecisionStrategy`; chỉ phụ thuộc BCL |
| `src/TADSS.Runtime.Api` | Web API, `DecisionService`, `StrategyResolver`, pipeline Polly v8, cache Redis/`IMemoryCache`, audit PostgreSQL (schema `tadss`) |
| `src/TADSS.Strategies.Default` | `DefaultDecisionStrategy` |
| `src/TADSS.Strategies.MockAlternative` | `MockAlternativeDecisionStrategy` |
| `tests/TADSS.UnitTests` | Domain, contract, service, strategy, hạ tầng, kiểm thử kiến trúc |
| `tests/TADSS.IntegrationTests` | API qua `WebApplicationFactory`, idempotency, tiêm lỗi, governance |
| `tests/TADSS.BenchmarkRunner` | Micro-benchmark phòng lab Phase 4 (lưu trữ — không dùng làm minh chứng SLA) |

Kiến trúc chi tiết: [`docs/architecture/SOFTWARE_ARCHITECTURE_SPECIFICATION.md`](docs/architecture/SOFTWARE_ARCHITECTURE_SPECIFICATION.md).

---

## Công nghệ

- **.NET 8**, ASP.NET Core Web API, EF Core 8 + Npgsql, PostgreSQL 16
- **Polly 8.4**, StackExchange.Redis 2.8, MediatR 12, Serilog 8
- Kiểm thử hiện tại: xUnit 2.5.3, FluentAssertions 6.12.1, Moq 4.20, `Microsoft.AspNetCore.Mvc.Testing` 8.0
- Bổ sung ở Phase 5: Testcontainers, Toxiproxy, OpenTelemetry + `grafana/otel-lgtm`, Schemathesis, Stryker.NET, k6 (xem [`docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md))

Phiên bản package khai báo tập trung trong `Directory.Packages.props`.

---

## Build, test, chạy

```bash
dotnet restore TADSS.slnx
dotnet build TADSS.slnx -c Release --warnaserror        # bắt buộc 0 warning
dotnet test  TADSS.slnx -c Release --no-build -m:1
dotnet run --project src/TADSS.Runtime.Api               # http://localhost:5296/swagger
```

Cấu hình kết nối PostgreSQL/Redis đặt trong user-secrets hoặc `appsettings.Development.json` (không commit). `appsettings.json` chỉ chứa giá trị mẫu.

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
