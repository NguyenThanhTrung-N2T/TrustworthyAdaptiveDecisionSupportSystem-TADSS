# ARCHITECTURE DECISION RECORDS (ADR) — TỔNG HỢP

> Tài liệu tổng hợp các quyết định kiến trúc của TADSS. ADR-001 → ADR-005 có file chi tiết trong [`adr/`](adr/). ADR-006 → ADR-012 được chốt khi re-baseline (01/10/2026) và ADR-013 khi hoàn tất gói 5.2d (03/10/2026); tất cả ghi trực tiếp tại đây.

| ADR | Quyết định | Trạng thái | Phạm vi |
| :--- | :--- | :--- | :--- |
| 001 | Strategy Pattern qua `IReservationDecisionStrategy` | Đã duyệt — điều chỉnh ở v2 | Đồ án 2 |
| 002 | SDK-first (`Shared.DecisionContractSDK`) | Đã duyệt — nâng lên v2 | Đồ án 2 |
| 003 | Pipeline chịu lỗi Polly v8 | Đã duyệt — bổ sung cách ly strategy | Đồ án 2 |
| 004 | Rich Domain Model cho `Reservation` | Đã duyệt | Đồ án 2 |
| 005 | Hướng chọn mô hình học máy cho strategy thích ứng | Định hướng | Khóa luận |
| 006 | Ngữ nghĩa cọc & human-in-the-loop | Đã duyệt | Đồ án 2 |
| 007 | Contract v2 thay thế v1 | Đã duyệt | Đồ án 2 |
| 008 | Audit bất biến ba bảng + trigger | Đã duyệt | Đồ án 2 |
| 009 | Kiểm thử trên hạ tầng thật | Đã duyệt | Đồ án 2 |
| 010 | Observability bằng OpenTelemetry | Đã duyệt | Đồ án 2 |
| 011 | Định tuyến strategy theo request + shadow mode | Đã duyệt | Đồ án 2 |
| 012 | Evidence Pack & ba nguồn dữ liệu minh chứng | Đã duyệt | Đồ án 2 |
| 013 | Clean Architecture nhiều project | Đã hiện thực (gói 5.2d) | Đồ án 2 |

---

## ADR-001 → ADR-005

Tóm tắt (chi tiết trong file riêng):

- **ADR-001:** runtime chỉ làm việc qua `IReservationDecisionStrategy`; strategy được đăng ký qua DI và tra cứu theo tên. Ở v2, strategy chỉ trả quyết định nghiệp vụ (`StrategyDecision`), runtime lo định danh/thời gian/telemetry.
- **ADR-002:** một thư viện hợp đồng dùng chung, không phụ thuộc NuGet ngoài; v1 đóng băng tại Milestone 2, v2 đóng băng tại Milestone 5.
- **ADR-003:** Outer Timeout 500ms → Fallback → Retry 2 lần → Circuit Breaker → Inner Timeout 120ms; v2 bổ sung cách ly strategy không hợp tác và breaker cho Redis.
- **ADR-004:** bất biến nghiệp vụ nằm trong `Reservation` và value object (`EnsureValid`, `CanBeCancelledWithoutPenalty`, `CalculateDepositRatio`, `Schedule.Duration`).
- **ADR-005:** định hướng mô hình học máy cho Khóa luận, ngoài phạm vi Đồ án 2.

---

## ADR-006: Ngữ nghĩa cọc & human-in-the-loop

- **Bối cảnh:** contract v1 chỉ trả trạng thái; số tiền cọc nằm trong chuỗi mô tả; strategy v1 thẩm định số cọc khách đã trả, trong khi lúc tạo booking khách chưa trả gì.
- **Quyết định:** TADSS xác định mức cọc → `APPROVED` + `deposit`; không xác định được hoặc fallback → `NEED_APPROVAL`, Manager duyệt kèm mức cọc hoặc từ chối; SmashCourt áp 100% **chỉ khi** tắt flag hoặc TADSS không phản hồi; không bao giờ tự duyệt; khách không chọn mức cọc. Khi không còn đủ thời gian duyệt trước giờ chơi, runtime trả `APPROVED` 100% (`APPROVAL_WINDOW_UNAVAILABLE`).
- **Hệ quả:** SmashCourt phải hỗ trợ thanh toán cọc một phần và thu phần còn lại khi checkout; cần job hết hạn duyệt.

## ADR-007: Contract v2 thay thế v1

- **Bối cảnh:** v1 không biểu diễn được số cọc, nhiều sân, khách vãng lai, chi nhánh, danh tính Manager, kết quả thực tế; chính sách đóng băng v1 không cho thêm trường.
- **Quyết định:** phát hành v2.0.0 (`/api/v2`) theo [`INTEGRATION_SPEC.md`](../contracts/INTEGRATION_SPEC.md); v1 được giữ làm hồ sơ và gỡ endpoint khi v2 đóng băng vì chưa có consumer.
- **Hệ quả:** cập nhật SDK, JSON schema, public API baseline; viết lại strategies.
- **Cập nhật 02/10/2026:** endpoint, mã và toàn bộ hồ sơ v1 (contract, OpenAPI, schema, số đo Milestone 2, benchmark Tuần 8) đã gỡ khỏi repo ở Phase 5 (gói 5.2c và đợt dọn sau đó); tra lại qua tag git `phase4-baseline`.

## ADR-008: Audit bất biến ba bảng + trigger

- **Bối cảnh:** v1 cập nhật cờ override trực tiếp lên bản ghi máy; không lưu trạng thái đích, mức cọc của override, input quyết định hay kết quả thực tế.
- **Quyết định:** `decision_executions`, `decision_overrides`, `decision_outcomes` đều append-only, được trigger PostgreSQL chặn `UPDATE/DELETE/TRUNCATE`; chống ghi đè đồng thời bằng `UNIQUE(decision_id)` trên bảng override (thay cho cột `version`); lưu snapshot request dạng `jsonb`.
- **Hệ quả:** hàng chờ tính "bản mới nhất mỗi reservation" bằng truy vấn; dữ liệu đủ cho replay và cho Khóa luận. Chi tiết: [`DECISION_PERSISTENCE_INVARIANTS.md`](DECISION_PERSISTENCE_INVARIANTS.md).

## ADR-009: Kiểm thử trên hạ tầng thật

- **Bối cảnh:** test và benchmark Phase 3–4 dùng EF Core InMemory và `Task.Delay`, không kiểm chứng được constraint, trigger, transaction, độ trễ mạng.
- **Quyết định:** integration test dùng Testcontainers (PostgreSQL 16, Redis 7); lỗi hạ tầng tiêm bằng Toxiproxy; gỡ `Microsoft.EntityFrameworkCore.InMemory`; bổ sung Schemathesis (contract), Stryker.NET (mutation), k6 (tải), Playwright (E2E).
- **Hệ quả:** cần Docker trên máy dev và CI; thời gian chạy test tăng; đổi lại minh chứng phản ánh hành vi thật.

## ADR-010: Observability bằng OpenTelemetry

- **Quyết định:** TADSS (và SmashCourt-BE) xuất trace, metric, log qua OTLP tới `grafana/otel-lgtm` (Grafana + Prometheus + Tempo + Loki trong một container). Serilog giữ nguyên và xuất thêm qua OTLP.
- **Hệ quả:** có dashboard và trace làm minh chứng trực quan; correlation id nối log SmashCourt ↔ TADSS ↔ PostgreSQL.

## ADR-011: Định tuyến strategy theo request + shadow mode

- **Bối cảnh:** v1 chọn một strategy toàn cục lúc khởi động — chưa thể hiện được "adaptive" và không có cách so sánh strategy ứng viên trên traffic thật.
- **Quyết định:** `StrategyRouter` chọn strategy theo quy tắc cấu hình cho từng request (reload không cần restart); `ShadowExecutor` chạy strategy ứng viên song song, chỉ ghi audit `SHADOW`.
- **Hệ quả:** Khóa luận có thể đưa strategy TADS vào chế độ shadow để so sánh với baseline trên cùng traffic mà không ảnh hưởng khách.

## ADR-012: Evidence Pack & ba nguồn dữ liệu minh chứng

- **Bối cảnh:** giảng viên yêu cầu minh chứng bằng dữ liệu chạy thật; dữ liệu SmashCourt hiện có là dữ liệu môi trường phát triển (nhãn do job tự sinh).
- **Quyết định:** ba nguồn dữ liệu, mỗi nguồn cho một mục đích: replay 215 booking SmashCourt (hình dạng input thực tế), dataset công khai Hotel Booking Demand (độc lập domain), pilot UAT với người dùng thật (luồng tích hợp, khả dụng). Mọi lượt chạy đóng gói thành Evidence Pack có manifest SHA-256 và báo cáo notebook.
- **Hệ quả:** định nghĩa tại [`EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](../research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md) và [`DATASET_SCHEMA_SPECIFICATION.md`](../research/DATASET_SCHEMA_SPECIFICATION.md).

## ADR-013: Clean Architecture nhiều project

- **Bối cảnh:** sau 5.2c toàn bộ runtime nằm trong một project `TADSS.Runtime.Api`, nên ranh giới tầng chỉ dựa vào quy ước thư mục. Hệ quả: port `IDecisionAuditStore` nằm trong `Infrastructure/`, nên use case phụ thuộc hạ tầng (vi phạm DIP); exception nghiệp vụ nằm ở `Middlewares/` và bị service/store tham chiếu ngược; `DecisionService` gánh điều phối, cache và log (SRP); một hàm DI đăng ký mọi tầng; một số giá trị vận hành (`Retry-After`, timeout kết nối Redis) và hằng của contract bị hardcode, lặp ở nhiều nơi. Khóa luận cần mở rộng nhiều port (override, outcome, hàng chờ ở 5.3; router, shadow ở 5.4), nên phụ thuộc ngược phải được compiler chặn trước đó.
- **Quyết định (P-03):** tách thành bốn project theo hướng phụ thuộc vào trong: `Shared.DecisionContractSDK` (Domain + contract) ← `TADSS.Application` (use case, port, options nghiệp vụ) ← `TADSS.Infrastructure` (adapter: EF Core/Npgsql, Redis, Polly, health check, migration) ← `TADSS.Runtime.Api` (presentation + composition root). Hai project strategy là plugin chỉ phụ thuộc SDK. Quy tắc phụ thuộc do `ProjectReference`/`PackageReference` cưỡng chế ở compiler và khóa bằng UT-14 (Mục 5 của [`SOFTWARE_ARCHITECTURE_SPECIFICATION.md`](SOFTWARE_ARCHITECTURE_SPECIFICATION.md)). Các quyết định kèm theo:
  - Port nằm ở `TADSS.Application/Abstractions/{Caching,Persistence,Resilience}`; adapter ở Infrastructure. Port audit tách theo consumer (ISP): `IDecisionEvaluationStore` và `IDecisionQueryStore`, một `EfDecisionAuditStore` hiện thực cả hai.
  - Infrastructure dịch mọi exception hạ tầng ngay tại adapter (outer timeout của Polly → `DecisionTimeoutException`; lỗi tạm thời của PostgreSQL → `PersistenceUnavailableException`); `ExceptionClassifier` chỉ biết exception của SDK và Application.
  - Mỗi tầng tự đăng ký phần của mình (`AddApplication`, `AddInfrastructure`, `AddStrategies`, `AddPresentation`); việc dựng strategy cụ thể từ cấu hình (`StrategyRegistrations`) thuộc composition root `TADSS.Runtime.Api/Composition/`; `Program.cs` chỉ còn lắp ráp và gọi `StartupTasks`, `UseTadssRequestPipeline`.
  - Tách `DecisionService` (SRP): `StrategyEvaluator` (cache + pipeline) và `DecisionPayloadSerializer` (dùng chung cho composer, replay, query).
  - Bỏ hardcode: `ContractLimits` (giới hạn 128 ký tự), `Tadss:ErrorHandling:RetryAfterSeconds`, `Tadss:Cache:RedisConnectTimeoutMs`, `CacheProviders`, hằng tên health check; xóa option chết `MaxMemoryCacheSize`.
  - Đổi tên entity EF thành `DecisionExecutionEntity`/`DecisionOverrideEntity`/`DecisionOutcomeEntity` (tránh trùng enum `DecisionOutcome` của SDK). Chuỗi tên kiểu trong `TadssDbContextModelSnapshot` và `*.Designer.cs` sửa theo; ID và DDL của 4 migration không đổi.
  - Thư mục composition root tên `Composition/` (không dùng `DependencyInjection/` để tránh namespace trùng đoạn cuối với `Microsoft.Extensions.DependencyInjection`).
- **Hệ quả:**
  - *Không đổi:* wire, JSON schema, mã lỗi và status HTTP, `openapi-v2.yaml`, `baseline_public_api.txt`, ID/nội dung migration, các khóa cấu hình hiện có và giá trị mặc định.
  - *Đổi hành vi có chủ đích:* (1) thêm validator cho `StrategyOptions`, `CacheOptions`, `ErrorHandlingOptions` nên khởi động từ chối cấu hình sai: `Tadss:Cache:Provider` lạ (khác `Redis`/`Memory`, không phân biệt hoa thường), `RedisTimeoutMs`/`RedisConnectTimeoutMs`/`TtlHours` không dương, `Tadss:Strategy:ActiveStrategyName` rỗng, `Tadss:ErrorHandling:RetryAfterSeconds` ngoài 1..3600 (`DatabaseOptions` chỉ có một cờ bool nên không có validator); (2) `Retry-After` của 503/504 đọc từ `Tadss:ErrorHandling:RetryAfterSeconds` (mặc định 5, đúng giá trị cũ); (3) khóa mới `Tadss:Cache:RedisConnectTimeoutMs` (mặc định 3000) là timeout kết nối tối thiểu, thay số 3000 hardcode: timeout kết nối thực tế vẫn là `max(RedisTimeoutMs, RedisConnectTimeoutMs)`; (4) khóa `Tadss:Cache:MaxMemoryCacheSize` bị xóa (không mã nào đọc; khóa còn sót trong file cấu hình cục bộ bị bỏ qua, không gây lỗi); (5) tên health check PostgreSQL đổi thành `postgresql` (không lộ ra response); (6) category của logger đổi theo namespace mới (cần cập nhật bộ lọc log nếu có lọc theo category); (7) kiểu `DecisionTimeoutException` mới, ánh xạ 504 `GATEWAY_TIMEOUT` giữ nguyên; (8) khi outer timeout của strategy và việc caller hủy xảy ra cùng lúc, request kết thúc như "client đã hủy" (log mức Information, không ghi response) thay vì 504 kèm log mức Error như trước (`StrategyPipeline` ném `OperationCanceledException` thay vì `DecisionTimeoutException`; xem Mục 4.4.1 của [`SOFTWARE_ARCHITECTURE_SPECIFICATION.md`](SOFTWARE_ARCHITECTURE_SPECIFICATION.md)).
  - *Chưa thay đổi / để lại cho 5.4:* (i) tiền tố khóa cache `tadss:v2:` (`StrategyDecisionCacheKey`) vẫn là hằng, chưa đưa vào cấu hình; (ii) tên health check PostgreSQL vẫn là hằng `"postgresql"`; (iii) `CacheOptions.InstanceName` và `CacheOptions.TtlHours` vẫn được bind nhưng không mã nào đọc (validator vẫn yêu cầu `TtlHours` > 0 theo plan), là ứng viên xóa ở 5.4 như `MaxMemoryCacheSize` đã xóa ở 5.2d; XML doc của hai thuộc tính ghi rõ hiện không có tác dụng.
  - *Chi phí:* thêm hai project, thêm bước đăng ký qua bốn hàm `Add*`; bù lại thêm port mới ở 5.3/5.4 chỉ cần khai báo ở `Application/Abstractions` và hiện thực ở Infrastructure, còn compiler chặn mọi phụ thuộc ngược. Kiểu mới đặt sai chỗ hoặc vi phạm quy ước làm UT-14 đỏ.
  - *Việc mang sang 5.3 (đã hiện thực ở 5.3; truy vấn hàng chờ nằm ở `IDecisionQueryStore` vì là thao tác đọc):* `IDecisionGovernanceStore` (đặt ở `Application/Abstractions/Persistence`), `DecisionStateConflictException` (đặt ở `Application/Exceptions`), middleware bảo mật chèn vào `UseTadssRequestPipeline`; fail-fast khi thiếu connection string, nới options đọc payload audit, log 503 giả khi client hủy trong pha SQL.
