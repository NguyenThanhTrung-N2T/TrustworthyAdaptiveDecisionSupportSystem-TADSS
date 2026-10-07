# Gói 5.5 — Observability & chế độ đánh giá: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Làm cho runtime **quan sát được** và **đánh giá được bằng một lệnh**:
- Gắn **OpenTelemetry** (trace, metric, log) gửi về `grafana/otel-lgtm`; hoàn tất bộ metric của SAS 4.5 (T5.14).
- **Dashboard Grafana bắt buộc** (P-04), tự nạp ở cả chế độ vận hành và chế độ đánh giá (OB-1…OB-4).
- Sửa thứ tự request logging để log đúng status thật (việc mang từ 5.3/5.4).
- Hoàn tất ma trận **FI-01…FI-10** (thêm FI-07, FI-09) (T5.15).
- Dựng **chế độ đánh giá một lệnh** (D-12, môi trường E-EVAL) gồm:
  - Stack cô lập, gồm hai replica TADSS sau reverse proxy (SCALE-2).
  - Container **evaluator** chạy tuần tự: test gộp theo `CatalogId` → k6 LT-01/02/05 → sự cố dưới tải LT-03 + FI → Schemathesis CT-03 (T5.16) và Stryker MT-01…03 (T5.17) → gom `evidence/<run_id>/` (`report.html`, dữ liệu thô, `manifest.json`).
- Ghi giới hạn scale (SCALE-3).

**Architecture:** Giữ Clean Architecture của 5.2d–5.4.
- **Application:**
  - `TadssMetrics`/`TadssMetricNames`: Meter `TADSS.Runtime`, nơi duy nhất khai báo tên metric.
  - `TadssActivitySource`: ActivitySource `TADSS.Runtime`.
  - Cả hai chỉ dùng BCL (`System.Diagnostics`), không phụ thuộc OpenTelemetry SDK.
- **Infrastructure:**
  - Span quanh lệnh Redis.
  - Đọc trạng thái breaker để ghi gauge.
  - Worker làm mới gauge hàng chờ.
  - `TelemetryOptions`.
  - `FaultInjectionControl` (đổi profile tiêm lỗi nóng, chỉ `Testing`).
- **Runtime.Api:**
  - Chỉ ở `Composition/`: dựng OpenTelemetry SDK, gồm ASP.NET Core instrumentation, Npgsql, exporter OTLP, sink log OTLP của Serilog.
  - Middleware ghi `tadss.decision.duration`.
- **Ngoài `src/`:**
  - `docker/grafana/`: dashboard, provisioning.
  - `docker/evaluator/`: image evaluator.
  - `tools/evaluation/`: bộ điều phối Python.
  - `tools/evidence/`: dựng Evidence Pack và báo cáo, dùng lại ở 5.6.
  - `tests/load/`: kịch bản k6.

**Tech Stack:**
- **NuGet mới, chỉ ở `TADSS.Runtime.Api`:**
  - **Phiên bản chốt ở spike S-10 (06/10/2026):** `OpenTelemetry.Extensions.Hosting` 1.15.3, `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.15.3, `OpenTelemetry.Instrumentation.AspNetCore` 1.15.2.
  - `Npgsql.OpenTelemetry` 8.0.6 (khớp Npgsql 8.0.6 trong lock file).
  - `Serilog.Sinks.OpenTelemetry` 4.1.1.
  - Lý do chọn 1.15.3: mọi bản thấp hơn dính hai advisory mức Medium (GHSA-g94r-2vxg-569j, GHSA-4625-4j76-fww9; cùng vá ở 1.15.3); nhóm net8.0 của 1.15.x vẫn dùng `Microsoft.Extensions.*` 8.0.0, nhưng kéo `System.Diagnostics.DiagnosticSource` 10.0.0 (đã chạy thử trên runtime .NET 8; Task 4 phải kiểm lại bằng test của repo).
- **NuGet mới, chỉ ở project test:** `OpenTelemetry.Exporter.InMemory`, `YamlDotNet`.
- Application và Infrastructure **không** thêm package.
- **Công cụ ngoài, ghim phiên bản:**
  - k6 2.3.0 (copy binary từ image `grafana/k6:2.3.0`; xuất metric bằng `--out opentelemetry` với `K6_OTEL_METRIC_PREFIX=k6_`).
  - Schemathesis 4.29.3 (pip).
  - `dotnet-stryker` 5.0.0, `dotnet-reportgenerator-globaltool` 5.5.11 (`.config/dotnet-tools.json`).
  - nginx (reverse proxy SCALE-2; tag chốt và thử ở Task 7), Docker CLI (`docker:cli`).
  - Python 3.11 (Debian bookworm) với `requirements.txt` ghim `==`.
- **Hạ tầng như 5.4:**
  - Ứng dụng: .NET 8, Polly 8.4.2, EF Core 8 + Npgsql 8, StackExchange.Redis 2.8.16.
  - Kiểm thử: Testcontainers (PostgreSQL 16, Redis 7, Toxiproxy), xUnit, FluentAssertions, Moq, `FakeTimeProvider`.
  - Image ghim như fixture: `postgres:16.15-alpine`, `redis:7.4-alpine`, `ghcr.io/shopify/toxiproxy:2.12.0`, `grafana/otel-lgtm:0.34.0`.

**Spec:** [`PHASE5_DESIGN_SPEC.md`](PHASE5_DESIGN_SPEC.md):
- D-11, D-12, D-13, P-04.
- Mục 3 (dòng 5.5), **Mục 4.5** (thiết kế chính).
- Mục 4.6 (ranh giới với 5.6), Mục 6.1 (dòng OB-1, OB-4), 7 (T5.26 k6), 8, 9.

Nguồn sở hữu:
- [`SOFTWARE_ARCHITECTURE_SPECIFICATION.md`](../docs/architecture/SOFTWARE_ARCHITECTURE_SPECIFICATION.md): Mục 2, Mục 3 (hạn chế còn mở), 4.2 (dòng OpenTelemetry), **4.5** (bảng metric).
- [`EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](../docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md): Mục 2 (E-EVAL), 4.3 (CT-03), 4.4 (MT), 4.5 (FI), 4.6 (LT), 4.13 (SCALE), 5 (SC1…SC8), 6.1, 8.
- [`DATASET_SCHEMA_SPECIFICATION.md`](../docs/research/DATASET_SCHEMA_SPECIFICATION.md): Evidence Pack, D1–D3, EV-1…EV-5.
- [`RESILIENCE_AND_FAULT_TAXONOMY.md`](../docs/architecture/RESILIENCE_AND_FAULT_TAXONOMY.md).

Gói trước:
- [`PHASE5_PLAN_5.4_ADAPTIVE_RUNTIME.md`](PHASE5_PLAN_5.4_ADAPTIVE_RUNTIME.md) mục "Ghi chú cho gói 5.5".
- [`PHASE5_REPORT_5.4.md`](PHASE5_REPORT_5.4.md) Mục 6 (gauge breaker, request logging, exporter, SCALE-2/3, LT-03 dùng decorator, FI-07).

> **Cách đọc plan này:**
>
> - Plan mô tả **yêu cầu kỹ thuật**: trách nhiệm, vị trí, chữ ký, quy tắc, bảng test case và tiêu chí nghiệm thu. Plan **không** chứa mã hiện thực.
> - Mục **Interfaces** là ràng buộc, vì task sau dựa vào đúng tên và kiểu đó.
> - Đường dẫn viết từ gốc repo. Tên kiểu và namespace viết theo trạng thái **sau** task đó.
> - Gói **không đổi wire** (OpenAPI, JSON schema, SDK giữ nguyên) và **không đổi schema DB**. Gói có đổi:
>   - cấu hình (`Tadss:Telemetry`);
>   - thứ tự middleware;
>   - `docker-compose.yml` (vận hành + profile `evaluation`);
>   - cách gõ lệnh chế độ đánh giá.
>
>   Mọi thay đổi như vậy nằm ở bảng "Quyết định thiết kế" và Phụ lục B. Người dùng duyệt hai bảng đó trước Task 1.
> - Gói chia **hai phần** có điểm dừng ở giữa (O-01): **Phần A** (Task 1–5, W06) observability; **Phần B** (Task 6–12, W07) chế độ đánh giá.

## Global Constraints

**Build và test sau mỗi task:**
- `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror` → **0 warning, 0 error**.
- Unit test và **toàn bộ** integration test xanh (Docker bật).
- Task có mã Python: `pytest` của `tools/evaluation` và `tools/evidence` xanh.

**Số lượng test:**
- Test hiện có không được mất (đầu gói: **1707 unit / 211 IT**, commit `cad5150`). Mỗi task ghi số test trước và sau của cả hai project (đếm bằng `dotnet test --list-tests`) và số test pytest.
- Ngoại lệ: test bị **thay thế** có chủ đích:
  - test thứ tự middleware ở Task 3;
  - test compose kiểm "không mount/không ApiKeys" trên toàn file ở Task 5 và 7.

  Ghi tên test cũ và test thay thế vào báo cáo.

**Contract:**
- `docs/contracts/openapi-v2.yaml`, `docs/contracts/schemas/v2/*.json`, `docs/contracts/baseline_public_api.txt` **không đổi**. Drift test OpenAPI hoặc `PublicApiSurfaceTests` đỏ ⇒ dừng và hỏi.
- Không đổi SDK, mã lỗi, status HTTP hay nội dung response.
- Schemathesis (Task 10) phát hiện lệch giữa mã và contract ⇒ sửa **mã** cho đúng contract. Nếu cách đúng là sửa contract ⇒ dừng và hỏi.

**Database:**
- **Không** tạo migration, **không** `dotnet ef database update`, **không** kết nối Neon.
- Truy vấn đếm hàng chờ mới (Task 2) dùng bảng và partial index hiện có.
- Stack đánh giá tự áp migration lên PostgreSQL **dùng một lần** của chính nó.

**Cấm nới kiểm tra:**
- Không `Skip`, không nới assertion, không `#pragma warning disable`, không `!` null-forgiving, không xóa test để chữa test đỏ.
- Không hạ ngưỡng Stryker (`break = 70`).
- Không loại mutant khỏi phép tính trừ trường hợp **mutant tương đương** được liệt kê kèm lý do trong báo cáo và người dùng duyệt.

**Quy tắc kiến trúc (UT-14, phải xanh sau mọi task):**
- Application chỉ dùng `System.Diagnostics.Metrics` và `System.Diagnostics.Activity*` (BCL). Không tham chiếu `OpenTelemetry.*`.
- Kiểu `OpenTelemetry.*` và cấu hình sink Serilog OTLP chỉ xuất hiện trong `TADSS.Runtime.Api/Composition/` (mở rộng `CompositionRootBoundaryTests`, Task 4).
- Mỗi kiểu public một file, file ≤ **250 dòng**, options + validator + `ValidateOnStart`, port chỉ ở `Application/Abstractions/**`.
- File hiện có không được vượt trần khi thêm metric. Tách theo O-05 (`TadssMetricNames`) và theo trách nhiệm nếu cần.

**Quy ước:**
- Tên metric, nhãn, giá trị nhãn và biên histogram khai báo **một nơi** (`TadssMetricNames`); không literal ở chỗ khác. OB-1 dựa vào điều này.
- Tên ActivitySource và tên span khai báo một nơi (`TadssActivitySource`).
- Thời gian lấy từ `TimeProvider`; đo khoảng thời gian bằng `TimeProvider.GetTimestamp()`/`GetElapsedTime()`, không `Stopwatch`.
- Comment và message tiếng Việt như mã hiện có; định danh tiếng Anh.
- Python:
  - module ≤ 250 dòng, có type hint;
  - comment/docstring tiếng Việt, thông điệp in ra cho người dùng tiếng Việt;
  - không gọi mạng trong unit test (pytest dùng dữ liệu mẫu trong `tests/fixtures/`).

**Test:**
- Tên `{Method}_{Scenario}_{Expected}`, có `[Trait("CatalogId", …)]` và XML summary tiếng Việt. IT thêm `[Trait("Category","Integration")]`.
- Mã danh mục của gói:
  - **OB-1…OB-5**: OB-5 mới, Task 1.
  - **FI-07**, **FI-09** (mới); FI-01…FI-10 phải đủ.
  - **CT-03**, **MT-01…MT-03**, **SCALE-2**, **SCALE-3**.
  - UT-07, UT-11, UT-14, UT-15, IT-09, SEC-7 (bổ sung).
- Test cần thời gian thật (exporter, worker nền, reload cấu hình, Toxiproxy) **chờ bằng polling có hạn**, tối đa 10 giây mỗi điều kiện. Ngoại lệ: kịch bản chờ breaker hồi phục theo `BreakDuration`, ghi rõ hạn trong test.
- Đo metric trong test bằng `MetricRecorder` lọc theo đúng instance `Meter` (như 5.4). Gauge đọc bằng `MeterListener.RecordObservableInstruments()`.

**Bảo mật:**
- Không in, không ghi vào file commit hay Evidence Pack bất kỳ giá trị secret nào. Không đọc `appsettings.Development.json`, `.env`.
- Stack đánh giá **không bao giờ** chạm Neon/Redis Cloud:
  - TADSS chạy môi trường `Testing` nên không nạp `appsettings.Development.json`;
  - chuỗi kết nối chỉ trỏ tới service `eval-*`;
  - evaluator copy mã nguồn **loại trừ** file secret (O-14).
- Khóa API của stack đánh giá là hằng **không bí mật**, chỉ có hiệu lực trong E-EVAL (O-13). Không dùng khóa thật hay `tadss-local-dev-key`.
- Span, log, metric **không** chứa:
  - `X-Api-Key` hay mã băm của nó;
  - chuỗi kết nối, mật khẩu;
  - body request/response;
  - dữ liệu khách.

  SEC-7 mở rộng ở Task 4.

**Docker:**
- Mọi image ghim tag cụ thể; mọi cổng host chỉ `127.0.0.1` (NG-5 áp cho cả service đánh giá).
- Container chỉ do mình tạo mới được dừng/xóa. Không đụng project compose `tadss` đang chạy của người dùng.

**Git:**
- Stage bằng đường dẫn tường minh. **Không** stage `spec/PHASE5_*.md`.
- Không push, stash, reset, `checkout --`, clean.
- Subject commit theo Conventional Commits, tiếng Anh ASCII.
- Người dùng ưu tiên **ít commit**: đề xuất **một commit mỗi phần** (O-01).

**Sai lệch so với giả định:**
- Nếu OpenTelemetry, otel-lgtm, k6, Schemathesis, Stryker hay Docker Desktop chạy khác giả định: sửa tối thiểu để giữ đúng **ý định**, ghi "sai lệch" vào báo cáo task, không nới test.
- Spike (Task 1) có mục nào thất bại và **không** có phương án dự phòng ghi sẵn ⇒ dừng và hỏi.

## Quyết định thiết kế của gói

| Mã | Quyết định | Lý do |
| :--- | :--- | :--- |
| **O-01** | **Hai phần, một điểm dừng.**<br>• Phần A (Task 1–5): telemetry, metric, dashboard, compose vận hành.<br>• Phần B (Task 6–12): ma trận FI, stack đánh giá, evaluator, báo cáo.<br>• Sau Task 5: dừng, người dùng review và commit Phần A.<br>• Commit đề xuất: một commit mỗi phần | Khớp lịch W06/W07 của MASTER_EXECUTION_PLAN. Phần A dùng được ngay ở chế độ vận hành (OB-4) dù Phần B chưa xong. Người dùng ưu tiên ít commit |
| **O-02** | **Vị trí OpenTelemetry:**<br>• SDK (TracerProvider, MeterProvider, exporter OTLP, ASP.NET Core instrumentation, Npgsql, sink log Serilog) dựng trong `TADSS.Runtime.Api/Composition/TelemetryServiceCollectionExtensions.AddTadssTelemetry`, gọi từ `AddPresentation`.<br>• Application khai báo `TadssActivitySource` (BCL).<br>• Infrastructure tạo span Redis bằng `TadssActivitySource` và giữ worker gauge hàng chờ (`Infrastructure/Telemetry/`).<br>• `TelemetryOptions` ở `TADSS.Infrastructure/Options` (worker của Infrastructure đọc, composition root đọc được).<br>• **Bỏ** `OpenTelemetry.Instrumentation.Http`: runtime không gọi HTTP ra ngoài; thêm khi có | Spec 4.5 ghi "`TADSS.Infrastructure/Telemetry/`", nhưng ASP.NET Core instrumentation cần framework ASP.NET Core mà UT-14 cấm ở Infrastructure. SAS 4.2 ghi "`TADSS.Runtime.Api/Composition` (dự kiến)". Giữ package ngoài Application/Infrastructure nên allowlist UT-14 không đổi |
| **O-03** | **`TelemetryOptions`** (`Tadss:Telemetry`):<br>• Trường và mặc định: `Enabled` (false), `OtlpEndpoint` ("http://localhost:4317"), `OtlpProtocol` (`Grpc`/`HttpProtobuf`, mặc định `Grpc`), `ServiceName` ("tadss-runtime"), `ServiceInstanceId` (rỗng → tên máy/container), `TraceSampleRatio` (1.0), `ExportLogs` (true), `MetricExportIntervalMs` (10000), `PendingApprovalsRefreshSeconds` (30).<br>• Validator: endpoint là URI tuyệt đối http/https khi `Enabled`; ratio trong [0,1]; interval 1000..60000; refresh 5..3600; `ServiceName` không rỗng, ≤ 64 ký tự.<br>• `Enabled = false` → **không** đăng ký provider OpenTelemetry nào và worker gauge không chạy truy vấn. Meter và ActivitySource vẫn tồn tại, nên test bằng `MeterListener` không phụ thuộc exporter.<br>• Collector chết hoặc không tới được → request không bị ảnh hưởng (exporter chạy nền, lỗi chỉ ghi log nội bộ của SDK) và host dừng trong thời hạn tắt mặc định | `dotnet run` hằng ngày không cần collector (D-12 chế độ 1). Spec 4.5: "tắt được bằng cấu hình; khi không có collector app vẫn chạy bình thường" |
| **O-04** | **Resource:**<br>• `service.name` = `ServiceName`.<br>• `service.version` = `AssemblyInformationalVersion` của Runtime.Api.<br>• `service.instance.id` = `ServiceInstanceId` hoặc tên máy.<br>• `deployment.environment.name` = tên môi trường.<br>• Nhãn Prometheus **đã xác nhận ở spike S-2**: `job` = `service.name`, `instance` = `service.instance.id` (kèm `service_instance_id`, `service_name`, `service_version`, `deployment_environment_name`); dashboard dùng `instance` | SCALE-2 cần tách số liệu theo instance; otel-lgtm ánh xạ thuộc tính resource sang nhãn Prometheus theo quy tắc riêng, phải kiểm chứng chứ không đoán |
| **O-05** | **Bộ metric của Meter `TADSS.Runtime`** (tên, nhãn, giá trị nhãn, biên histogram ở `TadssMetricNames` — nơi khai báo duy nhất; `TadssMetrics` giữ instrument và các `Record*`). Sẵn có từ 5.4: `config.reload_failures`, `strategy.abandoned`, `cache.requests`, `shadow.dropped`, `shadow.executions`. Thêm:<br>• `tadss.decisions` (Counter) {`status`, `strategy`, `execution_type`}: ghi **một lần cho mỗi bản `PRIMARY` mới** (không ghi khi replay idempotency, thua tranh chấp, lỗi; không ghi cho `SHADOW`).<br>• `tadss.decision.duration` (Histogram, `ms`) {`endpoint`, `status_code`}: mọi request `/api/*`. `endpoint` = `"{METHOD} {route template}"` (ví dụ `POST api/v2/decisions`), request bị từ chối trước routing (401/413/429) là `"{METHOD} unmatched"`. Không ghi `/health/*`, `/swagger/*`.<br>• `tadss.pipeline.duration` (Histogram, `ms`) {`strategy`, `outcome` = `automated`/`fallback`/`timeout`}: `timeout` khi outer timeout (`DecisionTimeoutException`). Caller hủy → không ghi.<br>• `tadss.fallbacks` (Counter) {`reason` = `timeout`/`exception`/`circuit_open`, `strategy`}. **Thêm nhãn `strategy`** so với SAS (mỗi strategy có breaker riêng từ 5.4).<br>• `tadss.circuit.state` (ObservableGauge) {`strategy`}: 0 = closed, 1 = half_open, 2 = open, 3 = isolated. Chỉ báo strategy đã có pipeline (tạo lười).<br>• **`tadss.cache.circuit.state`** (ObservableGauge, không nhãn, cùng mã số): breaker Redis — **metric mới**, không có trong SAS.<br>• `tadss.pending_approvals` (ObservableGauge) {`branch`} — O-06.<br>• `tadss.overrides` (Counter) {`action`}: một lần cho mỗi override **mới** (không ghi replay).<br>• **Biên histogram** (ms): 5, 10, 25, 50, 75, 100, **150**, 200, 300, **500**, **700**, 1000, 2500, 5000 — áp bằng View ở composition. Vì 150/500/700 là biên bucket nên kiểm "p95 < 150", "pipeline ≤ 500", "p99 ≤ 700" là phép đếm **chính xác** (tỉ lệ request ≤ biên), không phải nội suy | Hoàn tất SAS 4.5 và các mục 5.4 để lại. Đếm theo bản `PRIMARY` mới để `tadss.decisions` đối chiếu được với số bản ghi (SC3). Biên trùng ngưỡng KPI để kết luận đạt/không đạt không phụ thuộc sai số ước lượng phân vị |
| **O-06** | **Gauge hàng chờ:**<br>• `PendingApprovalsMetricRefresher` (`BackgroundService`, Infrastructure/Telemetry) mỗi `PendingApprovalsRefreshSeconds` gọi port mới `IDecisionQueryStore.CountPendingApprovalsByBranchAsync(nowUtc, ct)`: một truy vấn gộp theo chi nhánh, **cùng điều kiện** Mục B.5 (PRIMARY mới nhất, `NEED_APPROVAL`, chưa override/outcome, còn hạn), dùng partial index `idx_de_pending`.<br>• Kết quả thay nguyên khối vào `TadssMetrics`.<br>• Chi nhánh từng có mặt nhưng nay không còn bản chờ → báo **0** (không để series biến mất).<br>• Lỗi truy vấn → log Warning, giữ giá trị cũ, không dừng worker.<br>• Chỉ chạy khi `Telemetry.Enabled`.<br>• Nhiều instance cùng báo một con số ⇒ dashboard dùng `max by (branch)` | Gauge không gọi DB đồng bộ trong callback xuất metric. Báo 0 thay vì im lặng để dashboard không hiển thị số cũ tới 5 phút (staleness của Prometheus) |
| **O-07** | **Trace:**<br>• Span server của ASP.NET Core (bỏ `/health/*`, `/swagger/*`).<br>• `tadss.decision.evaluate` (Application, quanh `EvaluateAsync`; thuộc tính `tadss.strategy`, `tadss.routing_rule`, `tadss.status`, `tadss.execution_type`, `tadss.cache_hit`, `tadss.correlation_id`).<br>• `tadss.strategy.execute` (Infrastructure, mỗi lần chạy pipeline; `tadss.strategy`, `tadss.outcome`, `tadss.fallback_reason`).<br>• `tadss.shadow.execute` (worker shadow, liên kết `ActivityLink` tới span của request gốc nếu có).<br>• Span Npgsql (`Npgsql.OpenTelemetry`: chỉ văn bản SQL có tham số, không giá trị tham số).<br>• Span Redis tự tạo quanh mỗi lệnh (`db.system = redis`, `db.operation` = `GET`/`SET`, `tadss.cache.result`; **không** ghi khóa).<br>• Lấy mẫu `ParentBased(TraceIdRatioBased(TraceSampleRatio))`.<br>• Tag `tadss.client_id` hiện có giữ nguyên | Spec 4.5 (Redis: span tự tạo vì instrumentation chính thức chỉ có bản prerelease). Correlation id của contract khác W3C trace id nên gắn làm thuộc tính để tra chéo log ↔ trace ↔ `traceId` của problem+json |
| **O-08** | **Log:**<br>• Serilog giữ sink console như hiện nay. Khi `Telemetry.Enabled && ExportLogs` thì thêm sink `Serilog.Sinks.OpenTelemetry` (OTLP → Loki) với cùng resource O-04 và cùng mức log.<br>• Cấu hình bằng mã ở `Composition/` (endpoint lấy từ `TelemetryOptions`, một cờ bật/tắt duy nhất), không viết vào section `Serilog` của `appsettings.json`.<br>• Log mang `TraceId`/`SpanId` của span đang chạy | Một nơi bật/tắt toàn bộ telemetry; không nhân đôi endpoint ở hai section cấu hình |
| **O-09** | **Thứ tự pipeline (F-09) đổi:** forwarded headers → correlation id → **request logging** → **request metrics** → exception handler → security headers → https → giới hạn kích thước → Swagger (Development) → API key → rate limiter → routing.<br>• Request logging và metrics đứng **ngoài** exception handler nên thấy **status cuối** (409/503/504 không còn bị ghi "500").<br>• Mức log: status ≥ 500 → Error, còn lại Information.<br>• Dòng log hoàn tất được làm giàu `ClientId` lúc kết thúc request (đọc `ApiClientIdentity` do bước API key gắn) → gỡ hạn chế "log hoàn tất không mang `ClientId`" ở SAS Mục 3.<br>• Middleware metrics đọc endpoint **sau** khi `next` chạy xong (routing chạy bên trong nên đã gắn endpoint) | Việc 5.3/5.4 để lại. Dashboard 503/504 và log lỗi phải đúng status thật, nếu không bảng sự cố ở LT-03 sai |
| **O-10** | **Dashboard:**<br>• `docker/grafana/dashboards/tadss-runtime.json` (uid `tadss-runtime`), provisioning `docker/grafana/provisioning/dashboards/tadss.yaml`. Datasource dùng UID có sẵn của otel-lgtm (xác nhận ở spike S-1; không cố định thì thêm provisioning datasource).<br>• Hàng panel:<br>  1. Tổng quan: tốc độ request và p50/p95/p99 theo `endpoint`; số request theo instance.<br>  2. Quyết định: tỉ lệ `status`, `execution_type`.<br>  3. Chịu lỗi: fallback theo `reason`/`strategy`; trạng thái breaker theo strategy và breaker Redis; 503/504; độ trễ pipeline; số task strategy bị bỏ rơi.<br>  4. Cache: hit/miss/bypass.<br>  5. Quản trị & shadow: hàng chờ theo chi nhánh, override theo `action`, shadow ghi/lỗi/bị bỏ, reload cấu hình lỗi.<br>  6. Tải (k6): RPS, độ trễ phía client, tỉ lệ lỗi.<br>• Biến `instance`; panel log (Loki) lọc mức Error; liên kết trace (Tempo).<br>• Annotation `chaos` để evaluator đánh dấu thời điểm bật/tắt lỗi.<br>• **OB-1** (test): mọi định danh `tadss_*` trong biểu thức PromQL ánh xạ được về một instrument khai báo ở `TadssMetricNames` theo quy tắc chuyển tên (dấu chấm → gạch dưới; counter thêm `_total`; đơn vị `ms` thêm `_milliseconds`; histogram thêm `_bucket`/`_sum`/`_count`; gauge giữ nguyên). Quy tắc xác nhận ở spike S-2. Mọi định danh `k6_*` nằm trong danh sách metric k6 khai báo một nơi (`TestData/K6MetricNames`). Không có tiền tố khác | Spec 4.5 (panel tối thiểu, OB-1…OB-4). Annotation `chaos` trong JSON snapshot và biểu đồ ở OB-3 tự nói lên thời điểm sự cố |
| **O-11** | **Compose vận hành** (không đổi số service, không đổi cổng):<br>• `tadss-runtime` thêm `Tadss__Telemetry__Enabled: "true"`, `Tadss__Telemetry__OtlpEndpoint: http://grafana:4317`.<br>• `grafana` mount **chỉ đọc** `./docker/grafana/provisioning/dashboards` và `./docker/grafana/dashboards` vào đường dẫn provisioning của otel-lgtm (S-1).<br>• Quy tắc "không mount, không biến chuỗi kết nối/khóa" của NG-1 **thu hẹp về service `tadss-runtime`** (cả `verify-expose.sh` lẫn `ComposeFixtureAlignmentTests`, giờ phân tích theo từng service bằng YamlDotNet thay vì quét cả file).<br>• Câu chú thích đầu file bỏ "chưa có dữ liệu TADSS đến khi có OpenTelemetry ở gói 5.5" | OB-4: chạy Docker là thấy số liệu. Mount chỉ đọc dashboard không chứa secret; ràng buộc bảo mật thật (secret không nhân đôi, migration `false`) vẫn khóa đúng service cần khóa |
| **O-12** | **Lệnh chế độ đánh giá đổi so với D-12:**<br>• Chạy: `docker compose -p tadss-eval --profile evaluation run --rm --build evaluator` (bản nhanh, mặc định). Bản đầy đủ thêm `-e EVAL_MODE=full`.<br>• Dọn: `docker compose -p tadss-eval --profile evaluation down -v`.<br>• Sau khi evaluator thoát, stack đánh giá (Grafana `127.0.0.1:3002`) **vẫn chạy** để xem lại tới khi dọn | Service **không** có profile luôn được khởi động: `docker compose --profile evaluation up` sẽ bật cả `tadss-runtime` (Development, đọc `appsettings.Development.json` → Neon) và ngrok, vi phạm "không bao giờ chạm Neon". `run evaluator` chỉ khởi động evaluator và phụ thuộc của nó. Project riêng `tadss-eval` để lệnh `down` không dừng stack vận hành `tadss`. Kiểm chứng ở spike S-5 |
| **O-13** | **Stack đánh giá** (mọi service có `profiles: [evaluation]`, tên bắt đầu `eval-` hoặc là `evaluator`, mạng mặc định riêng, **không** nối `smashcourt-network`):<br>• `eval-postgres`, `eval-redis`, `eval-toxiproxy` (dùng lại `docker/toxiproxy/toxiproxy.json`, đổi upstream sang `eval-postgres`/`eval-redis`).<br>• `eval-tadss-1`: build `Dockerfile` hiện có; `Testing`; `ApplyMigrationsOnStartup = true` (database dùng một lần); tiêm lỗi bật với `Profile = None`; đọc file lỗi O-17.<br>• `eval-tadss-2`: như trên nhưng migration `false`, không tiêm lỗi, `depends_on` `eval-tadss-1` healthy (tránh hai instance cùng migrate).<br>• `eval-proxy`: nginx, round-robin hai instance.<br>• `eval-grafana`: otel-lgtm, `127.0.0.1:3002`, cùng provisioning.<br>• **Không có renderer ảnh** (người dùng chốt 08/10/2026: không lưu ảnh dashboard; bỏ `grafana-image-renderer`, tiết kiệm ~2 GB).<br>• `evaluator`.<br>• **Cấu hình TADSS bằng biến môi trường trong compose:**<br>  – chuỗi kết nối PostgreSQL/Redis **qua** `eval-toxiproxy`;<br>  – một khóa API client `evaluator` với mã SHA-256 của hằng `tadss-evaluation-key` (không bí mật, chỉ có hiệu lực ở E-EVAL);<br>  – rate limit `PermitLimit = TokensPerPeriod = 100000` (mức tối đa validator cho phép) để tải k6 không bị 429;<br>  – telemetry tới `eval-grafana:4317` với `MetricExportIntervalMs = 5000`, `ServiceInstanceId` = `tadss-1`/`tadss-2`, `TraceSampleRatio = 0.1`.<br>• Dùng môi trường `Testing` vì decorator tiêm lỗi chỉ đăng ký ở `Testing` (A-07). Khác biệt so với Production (HSTS, validator khóa nới lỏng, rate limit nâng) ghi vào giới hạn của báo cáo | D-12, spec 4.5 (SCALE-2 là một phần của stack đánh giá). Qua Toxiproxy để kịch bản sự cố cắt/làm chậm được hạ tầng thật. Tách instance migrate để tránh tranh chấp migration |
| **O-14** | **Evaluator:**<br>• Image `docker/evaluator/Dockerfile` (kèm `Dockerfile.dockerignore`): .NET 8 SDK (Debian) + Python 3.11 venv + `requirements.txt` ghim + k6 (copy binary từ image `grafana/k6` ghim) + Docker CLI (từ `docker:cli` ghim) + git; restore `dotnet tool` từ `.config/dotnet-tools.json`.<br>• Mount:<br>  – repo **chỉ đọc** tại `/repo`;<br>  – `./evidence` ghi được tại `/evidence`;<br>  – `/var/run/docker.sock` (Testcontainers cho IT, đọc digest image);<br>  – volume cache NuGet `eval-nuget`;<br>  – volume `eval-faults`.<br>• Khi bắt đầu, copy `/repo` → `/work` **loại trừ** `**/appsettings.Development.json`, `.env*`, `**/bin`, `**/obj`, `**/TestResults`, `logs/`, `evidence/`, `.vs/`; mọi bước build/test chạy trên `/work`.<br>• Ghi commit và cờ `dirty` (`git status --porcelain` trên `/repo`) vào manifest.<br>• Biến: `EVAL_MODE` (`quick`/`full`, mặc định `quick`), `EVAL_STEPS` (tập con của `a,b,c,d,e`, mặc định tất cả; để phát triển và chạy lại một bước).<br>• Mã thoát:<br>  – 0: chạy hết và mọi tiêu chí áp dụng đều đạt;<br>  – 1: chạy hết nhưng có tiêu chí không đạt;<br>  – 2: lỗi hạ tầng/không chạy hết.<br>• In bảng tóm tắt (bước, thời gian, SC1…SC8, đường dẫn báo cáo).<br>• Bản nhanh: bỏ LT-05 và Stryker, rút ngắn tải. Bản đầy đủ: theo methodology | Mount socket cho phép chạy integration test Testcontainers bên trong evaluator (D-12 bước a). Đánh đổi: evaluator có quyền điều khiển Docker của máy — ghi vào README và giới hạn. Không copy file secret để image/thư mục làm việc không bao giờ chứa nó |
| **O-15** | **k6** (`tests/load/`; T5.26 là việc của SV2, SV1 viết bản đầu theo giao diện dưới đây để evaluator chạy được; SV2 có thể chỉnh kịch bản nhưng giữ giao diện):<br>• Vào: `BASE_URL`, `API_KEY`, `RUN_ID`, `SCENARIO` (`lt01`/`lt02`/`lt03`/`lt05`/`scale2`), `MODE`.<br>• Ra:<br>  – `handleSummary` → `summary.json`;<br>  – **mỗi request một dòng JSON** qua `--console-output` (`ts`, `scenario`, `http_status`, `duration_ms`, `decision_status`, `execution_type`, `correlation_id`, `idempotency_key`, `decision_id`) → D2 timeseries.<br>• Metric k6 gửi vào collector bằng output OpenTelemetry của k6 (S-3; dự phòng: remote-write của Prometheus), **không** gắn tag theo từng request để tránh bùng nổ nhãn.<br>• Mọi request gửi `X-Correlation-ID` (UUID) và `X-Idempotency-Key` duy nhất (trừ phần trùng có chủ đích của `scale2`).<br>• Payload sinh từ bảng trộn khai báo trong script (chi nhánh cố định, giờ cao điểm/thấp điểm theo giờ chi nhánh, các hạng khách, khách vãng lai, giá dưới/trên ngưỡng giá trị lớn, 1–3 item). Thời gian chơi **luôn ở tương lai** đủ xa để cửa sổ duyệt tồn tại; payload hợp lệ schema v2 và bất biến domain | Spec 7 (ranh giới SV2), 4.5 (k6 đẩy metric vào cùng Prometheus — chốt output tại đây). D2 cần từng request nhưng Prometheus không chịu được nhãn theo request |
| **O-16** | **Sự cố dưới tải (LT-03)**, k6 50 RPS liên tục trên `eval-tadss-1`, evaluator bật/tắt lỗi theo lịch cố định (bảng ở Task 9):<br>• nền → FI-02 (strategy `Delay` 2000 ms) → hồi phục → FI-03 (`Throw`) → hồi phục → FI-08 (cắt PostgreSQL) → hồi phục → FI-07 (Redis trễ 200 ms) → hồi phục.<br>• Mỗi lần bật/tắt: một dòng `chaos_log` (D3) và một annotation Grafana.<br>• **"Hồi phục"** = trong khoảng `T_hồi_phục` sau khi gỡ lỗi, chỉ số của lỗi đó về 0 trong một cửa sổ 10 s liên tục:<br>  – FI-02/FI-03: tỉ lệ fallback; `T_hồi_phục` = `CircuitBreakerBreakDurationSeconds` (15 s).<br>  – FI-08: tỉ lệ 503; `T_hồi_phục` = 0 (không có breaker).<br>  – FI-07: tỉ lệ `bypass`; `T_hồi_phục` = `RedisBreakerBreakDurationSeconds` (30 s).<br>  – Cộng thêm 2 × `MetricExportIntervalMs` + 10 s.<br>• **Độ dài pha hồi phục** ≥ `T_hồi_phục` + 2 × `MetricExportIntervalMs` + 10 s (độ trễ ghi nhận) + 10 s (cửa sổ sạch) + 10 s (dự phòng). Với cấu hình đánh giá: ≥ 55 s sau lỗi strategy, ≥ 70 s sau FI-07. Lịch được tính ngược từ cấu hình và có test khóa (Task 9) | D-12 (c); SC2. Lịch cố định để hai lượt chạy so sánh được |
| **O-17** | **Đổi profile tiêm lỗi nóng (chỉ `Testing`):**<br>• `FaultInjectionControl` (Infrastructure/FaultInjection, singleton) giữ `FaultInjectionOptions` hiện hành. Theo dõi `IConfiguration` theo cách của A-02: nạp lại sai → giữ bản cũ, log Error, `tadss.config.reload_failures{section="Tadss:Testing:FaultInjection"}`.<br>• **Chỉ `Profile` và `DelayMs`** đổi nóng. `Enabled` và `TargetStrategy` chỉ đọc lúc khởi động (quyết định bọc decorator giữ nguyên A-07).<br>• Decorator đọc control ở **mỗi** lời gọi.<br>• Nguồn cấu hình: khi môi trường là `Testing` **và** biến môi trường `TADSS_FAULT_INJECTION_FILE` có giá trị, composition thêm file JSON đó (tùy chọn, `reloadOnChange`) **sau cùng** nên ưu tiên hơn biến môi trường; môi trường khác bỏ qua biến này. Program.cs thêm đúng một lời gọi `AddTadssTestingConfiguration`.<br>• Evaluator ghi file trong volume `eval-faults` theo kiểu ghi file tạm rồi đổi tên | Decorator hiện nhận bản chụp options lúc khởi động nên không bật/tắt được giữa lượt tải; muốn dòng thời gian sự cố (độ trễ tăng → fallback → hồi phục) phải đổi được khi đang chạy. Giữ phạm vi đổi nóng nhỏ nhất để test A-07 hiện có không đổi |
| **O-18** | **SCALE-2:**<br>• k6 `scale2` (bản nhanh 1 phút, đầy đủ 3 phút, 50 RPS) qua `eval-proxy`. 20 % request **lặp lại** khóa idempotency + payload của một request trước đó.<br>• Đạt khi:<br>  – 0 response 5xx;<br>  – mỗi khóa có đúng 1 bản `PRIMARY` và mọi response của khóa đó cùng `decisionId`;<br>  – Prometheus có số liệu `tadss.decisions` > 0 cho **cả hai** instance trong cửa sổ chạy | Spec 4.5 SCALE-2 |
| **O-19** | **Schemathesis (CT-03):**<br>• Cài bằng pip (ghim) trong image evaluator thay vì image Docker chính thức (cùng môi trường Python, không cần thêm container).<br>• Chạy trên `openapi-v2.yaml` của `/work`, đích `eval-tadss-1`, header `X-Api-Key`.<br>• Check: `not_a_server_error`, `response_schema_conformance`, `status_code_conformance`, `content_type_conformance`.<br>• **CT-03 đạt** ⇔ 0 lỗi ở hai check đầu. Hai check sau được báo cáo; lỗi thật phải sửa (mã) hoặc hỏi (contract).<br>• Số ví dụ mỗi operation: nhanh 25, đầy đủ 500. CLI và cờ theo phiên bản chốt ở S-7 | Spec 4.5; methodology CT-03 (0 lỗi 5xx, 0 response sai schema) |
| **O-20** | **Stryker.NET (MT):**<br>• `stryker-config.json` cho từng đích, test project `TADSS.UnitTests`, `thresholds.break = 70`, reporter `html` + `json`, xuất vào `evidence/<run_id>/raw/mutation/`.<br>• Phạm vi cập nhật theo cây sau 5.2d (thay `Services/**` cũ của spec):<br>  – MT-01: `src/Shared.DecisionContractSDK/**`;<br>  – MT-02: `src/TADSS.Strategies.*/**`;<br>  – MT-03: `src/TADSS.Application/Decisions/**`, `src/TADSS.Application/Routing/**` và `src/TADSS.Infrastructure/Resilience/**`.<br>• Coverage: coverlet (cobertura) ở bước (a) → ReportGenerator HTML ở bước (d).<br>• Bản nhanh **không** chạy Stryker (SC8 ghi "chưa đủ dữ liệu"); bản đầy đủ và `EVAL_STEPS=d` chạy cả ba | Spec 4.5 (đường dẫn cũ trước 5.2d không còn tồn tại); MT-03 của methodology (DecisionService, routing, pipeline, approval window). Stryker chiếm phần lớn thời gian nên không hợp bản nhanh ~15 phút |
| **O-21** | **Evidence Pack:**<br>• `run_id` = `EV-{yyyyMMdd}-EVAL-{quick\|full}-r{n}` (`n` = số lần tiếp theo trong `evidence/`).<br>• Cây theo DATASET_SCHEMA Mục 2: `manifest.json`, `config/`, `raw/`, `grafana/`, `report.html`. `evidence/` vào `.gitignore` và `.dockerignore`.<br>• D1 xuất từ database của stack, định danh ẩn danh bằng HMAC với **khóa ngẫu nhiên mỗi lượt, không lưu**. Ngoài ra D2 (k6), D3 (`chaos_log`).<br>• `config/`: cấu hình đã dùng, đã lọc secret — compose render của profile, toxiproxy, lịch sự cố, k6 options.<br>• EV-5 với E-EVAL: commit SmashCourt-BE/FE được để `null` kèm lý do "không tham gia lượt chạy" (chỉ hợp lệ khi lượt chạy không có LT-04/E2E).<br>• `report.html` **tự chứa** (không tải script từ Internet), tiếng Việt | DATASET_SCHEMA Mục 2, 5; D-12 (e). Khóa HMAC không lưu để dữ liệu không thể đảo ngược |
| **O-22** | **Truy vết `CatalogId`:**<br>• Mỗi project test có `CatalogTraceabilityTests`, quét bằng reflection, kiểm:<br>  1. mọi phương thức `[Fact]`/`[Theory]` có ít nhất một trait `CatalogId`;<br>  2. mọi mã tồn tại trong bảng danh mục của methodology (đọc file markdown).<br>• Khi có biến môi trường `TADSS_CATALOG_INDEX_DIR`, test ghi thêm chỉ mục `{tên đầy đủ phương thức → [CatalogId]}` ra thư mục đó.<br>• Evaluator ghép trx với chỉ mục để gom kết quả theo mã | D-10/D-12 (a): kết quả gom theo mã danh mục. Không dựa vào việc logger trx có ghi trait hay không. Khóa luôn quy ước "mỗi test gắn một mã" |
| **O-23** | **Kiểm chứng chế độ vận hành:**<br>• Script mới `tools/observability/verify-observability.sh`, kín như `verify-expose.sh`: project compose riêng, PostgreSQL/Redis tạm, không ngrok, không Neon, có Grafana.<br>• Kiểm:<br>  – **OB-2**: dashboard nạp, qua API Grafana;<br>  – **OB-4**: sau vài request có `tadss_decisions_total` trên Prometheus, hạn 60 s.<br>• Thêm job CI `observability-check`.<br>• Evaluator chạy thêm `verify-expose.sh` và `verify-observability.sh` ở bước (a) **nếu** S-9 cho thấy chạy được trong container. Nếu không, báo cáo ghi kết quả CI gần nhất và nêu lý do | OB-2/OB-4 ở chế độ vận hành không thuộc stack đánh giá; giữ cùng cách kiểm hermetic đã dùng cho NG |
| **O-24** | **Thời lượng:**<br>• Ước tính bản nhanh 18–25 phút: bước a ~5, b ~6, c ~8 (LT-03 6 phút 45 giây sau khi kéo dài pha hồi phục + SCALE-2), d ~2, e ~1. Bản đầy đủ 2,5–3 giờ: LT-01 3 × 11 phút, LT-02 ~15, LT-05 61, LT-03 ~14, SCALE-2 3, Schemathesis ~10, Stryker 20–40, test ~6.<br>• Ước tính vượt mức "1,5–2 giờ" của D-12. Đo thật ở Task 12 rồi cập nhật D-12, methodology 6.1, README theo số đo; **không** cắt kịch bản của methodology để vừa con số cũ | Báo cáo trung thực (nguyên tắc 3 của methodology) |

Ngoài phạm vi 5.5 (ghi vào báo cáo, không làm):
- Rate limit phân tán (SCALE-3 chỉ là tài liệu).
- Cảnh báo (alerting) Grafana.
- Trace phía SmashCourt-BE và LT-04 (Phase 6).
- Dữ liệu replay/khách sạn (5.6).
- Lượt chạy đầy đủ chính thức để đóng băng (5.7).

Nếu người dùng không đồng ý mục nào, sửa plan trước khi chạy Task 1.

## Chuẩn bị (người dùng thực hiện)

1. Bật Docker Desktop. Cấp cho Docker **≥ 4 CPU, ≥ 8 GB RAM, ≥ 20 GB đĩa trống** (image SDK, otel-lgtm, k6, Stryker).
2. Duyệt bảng Quyết định và Phụ lục B. Đặc biệt:
   - O-09 (đổi thứ tự middleware);
   - O-11 (compose vận hành mount dashboard, bật telemetry);
   - O-12 (lệnh chạy đánh giá khác D-12);
   - O-14 (mount Docker socket);
   - O-17 (đổi profile tiêm lỗi nóng);
   - O-24 (thời lượng bản đầy đủ).
3. Sau Task 5: review và commit Phần A (một commit).
4. Cuối gói: tự chạy thử một lượt bản nhanh (lệnh ở README) nếu muốn xem tận mắt; đề xuất PR vào `develop` (người dùng tạo và merge).

## Cấu trúc đích

Chỉ liệt kê file mới hoặc file đổi đáng kể.

```text
src/
  TADSS.Application/
    Common/                    TadssMetricNames, CircuitStateValue (Task 2); TadssActivitySource (Task 4); TadssMetrics + instrument mới (Task 2)
    Abstractions/Persistence/  IDecisionQueryStore + CountPendingApprovalsByBranchAsync (Task 2)
    Decisions/                 DecisionService: tadss.decisions, span evaluate (Task 2, 4)
    Governance/                OverrideService: tadss.overrides (Task 2)
    Shadow/                    ShadowExecutor: span shadow (Task 4)
  TADSS.Infrastructure/
    Options/                   TelemetryOptions, TelemetryOptionsValidator, TelemetryOtlpProtocol (Task 2)
    Telemetry/                 PendingApprovalsMetricRefresher (Task 2)
    Persistence/               PendingApprovalsQuery: truy vấn đếm theo chi nhánh (Task 2)
    Resilience/                StrategyPipeline/StrategyResilienceBuilder: duration, fallbacks, state provider, span (Task 2, 4)
    Caching/                   RedisDecisionCache/RedisCacheResilience: state provider, span Redis (Task 2, 4)
    FaultInjection/            FaultInjectionControl; decorator đọc control (Task 6)
  TADSS.Runtime.Api/
    Composition/               TelemetryServiceCollectionExtensions, SerilogTelemetryConfiguration (Task 4);
                               RequestPipelineExtensions + RequestLoggingConfiguration (Task 3); TestingConfigurationExtensions (Task 6)
    Middlewares/               RequestMetricsMiddleware (Task 3)
    appsettings.json           + Tadss:Telemetry (Task 2)
docker/
  grafana/dashboards/tadss-runtime.json, grafana/provisioning/dashboards/tadss.yaml (Task 5)
  toxiproxy/toxiproxy.json    upstream eval-* (Task 7)
  evaluation/nginx.conf       (Task 7)
  evaluator/Dockerfile, Dockerfile.dockerignore (Task 7)
docker-compose.yml             telemetry + mount Grafana (Task 5); profile evaluation (Task 7)
.config/dotnet-tools.json      dotnet-stryker, dotnet-reportgenerator-globaltool (Task 10)
stryker/                       mt01-sdk.json, mt02-strategies.json, mt03-core-application.json, mt03-core-infrastructure.json (Task 10)
tests/
  load/                        lib/ (payload, output), scenarios/ (lt01, lt02, lt03, lt05, scale2), config/modes.json (Task 8)
  TADSS.UnitTests/             Architecture/ (DashboardMetricReferenceTests, CatalogTraceabilityTests), Application/Common/, Api/Middlewares/,
                               Api/Composition/ (telemetry, thứ tự pipeline), Infrastructure/{Telemetry,FaultInjection}/, TestData/ (K6MetricNames, CatalogIndex)
  TADSS.IntegrationTests/      Observability/ (TelemetryExportTests), FaultInjection/ (FI-07, FI-09, đổi profile nóng),
                               Persistence/ (đếm hàng chờ), Infrastructure/ (ComposeFixtureAlignmentTests theo service, CatalogTraceabilityTests)
tools/
  observability/verify-observability.sh (Task 5)
  expose/verify-expose.sh      NG-1 theo service (Task 5)
  evaluation/                  evaluator/ (cli, steps/, docker, prometheus, grafana, toxiproxy, k6, faults), requirements.txt, tests/ (Task 7–11)
  evidence/                    evidence_pack.py, anonymize.py, d1_export.py, report/ (generator, template), requirements.txt, tests/ (Task 11)
.github/workflows/ci.yml       + observability-check (Task 5), + evaluation-tools (Task 7)
```

Thứ tự phụ thuộc: 1 → 2 → 3 → 4 → 5 (**dừng, commit Phần A**) → 6 → 7 → 8 → 9 → 10 → 11 → 12.

---

## PHẦN A — OBSERVABILITY

### Task 1: Spike hạ tầng và mở rộng danh mục

**Files:**
- Modify:
  - `docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`: mục mới 4.14 OB; dòng SCALE-2, SCALE-3 ở 4.13.
  - `docs/research/DATASET_SCHEMA_SPECIFICATION.md`: chốt `run_id` của lượt đánh giá (O-21).
  - `spec/PHASE5_DESIGN_SPEC.md`: ghi chú ngày dưới D-12 về lệnh O-12, phạm vi Stryker O-20, vị trí OTel O-02.
- Không đổi `src/`. Mọi thử nghiệm làm trong thư mục tạm ngoài repo.

**Interfaces:** không có.

**Yêu cầu:**
1. **Spike.** Mỗi mục ghi: cách thử, kết quả, quyết định cuối (giữ / dùng dự phòng). Thử bằng container tạm, xóa sau khi xong.

   | Mã | Câu hỏi | Dự phòng nếu không đạt |
   | :--- | :--- | :--- |
   | S-1 | otel-lgtm 0.34.0: đường dẫn provisioning dashboard, UID datasource Prometheus/Tempo/Loki, cổng Prometheus trong mạng compose, API Grafana không cần đăng nhập, biến `GF_RENDERING_*` có tác dụng (renderer) | Thêm provisioning datasource với UID cố định; không có renderer → OB-3 chỉ gồm JSON snapshot + biểu đồ trong báo cáo, ghi giới hạn |
   | S-2 | Gửi metric OTLP thử (image `telemetrygen` ghim) với resource `service.name`/`service.instance.id`, counter + histogram đơn vị `ms`, rồi đọc Prometheus: tên sau chuyển đổi và nhãn của resource | Cập nhật quy tắc chuyển tên của O-10 và nhãn instance của O-04 theo kết quả thật |
   | S-3 | k6 (phiên bản ghim) output OpenTelemetry tới collector: tên metric `k6_*` trên Prometheus | Output remote-write của Prometheus (kiểm cờ nhận remote-write của otel-lgtm) |
   | S-4 | Chạy `TADSS.IntegrationTests` trong container SDK có mount `docker.sock` (Docker Desktop Windows), `TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal`, `extra_hosts: host.docker.internal:host-gateway` | **Không có dự phòng** ⇒ dừng và hỏi (đổi D-12 bước a) |
   | S-5 | `docker compose -p tadss-eval --profile evaluation run --rm evaluator` không khởi động service không có profile và không đòi mạng external `smashcourt-network` | Nếu đòi mạng: README ghi điều kiện tạo mạng trước (đã là điều kiện của chế độ vận hành) |
   | S-6 | Hai container dùng chung named volume: thay file JSON ở container A thì `reloadOnChange` ở container B nhận trong ≤ 5 s | (1) Đặt `DOTNET_USE_POLLING_FILE_WATCHER=true` cho `eval-tadss-1`; ghi độ trễ kích hoạt tối đa vào lịch sự cố. (2) Nếu cả polling không đạt: lỗi strategy của LT-03 chạy thành các lượt ngắn riêng, mỗi lượt khởi động lại `eval-tadss-1` với profile cố định; lỗi hạ tầng (Toxiproxy) vẫn trong một lượt liên tục. Báo cáo ghi rõ mất tính liên tục ở phần lỗi strategy |
   | S-7 | Schemathesis (phiên bản ghim): tải `openapi-v2.yaml`, cú pháp CLI cho header, số ví dụ, check, xuất báo cáo máy đọc được | Chọn phiên bản khác cùng dòng chính; ghi CLI thật vào O-19 |
   | S-8 | `dotnet-stryker` (phiên bản ghim) chạy trên SDK với `TADSS.UnitTests`; đo thời gian để ước tính MT-03 | Nếu MT-03 > 60 phút: tách MT-03 theo thư mục, chạy song song tối đa 2 tiến trình (ghi vào O-24) |
   | S-9 | `verify-expose.sh` chạy được bên trong container evaluator (Docker CLI + socket) | Không chạy trong evaluator; báo cáo dùng kết quả CI (O-23) |
   | S-10 | Bộ package OpenTelemetry chọn được có phụ thuộc `Microsoft.Extensions.*` **8.0.x**. Thử restore trong project nháp với CPM + ghim bắc cầu như repo; quét lỗ hổng | Không có bản dùng 8.0.x hoặc có lỗ hổng High/Critical ⇒ dừng và hỏi |

2. **Danh mục:**
   - Mục **4.14 OB — Observability (gói 5.5)**:
     - **OB-1**: dashboard chỉ tham chiếu metric có thật (test).
     - **OB-2**: dashboard tự nạp qua provisioning (API Grafana; vận hành và đánh giá).
     - **OB-3**: sau sự cố dưới tải, Evidence Pack có JSON snapshot dashboard (kèm annotation `chaos`) và biểu đồ dòng thời gian vẽ từ metric đã thu thể hiện fallback và 503; **không lưu ảnh** dashboard (quyết định người dùng 08/10/2026).
     - **OB-4**: chế độ vận hành có số liệu TADSS trên Grafana sau vài request.
     - **OB-5** (mới): telemetry trong tiến trình — mỗi metric ghi đúng tên/nhãn/thời điểm; span có cây cha–con đúng và log mang trace context; không thuộc tính bí mật; collector không chạy không ảnh hưởng request.
   - **SCALE-2** theo O-18. **SCALE-3**: SAS ghi thành phần theo instance, thành phần dùng chung, hệ quả khi scale ngang và hướng rate limit phân tán — người kiểm: rà soát tài liệu.
   - Mục 2 (E-EVAL) và 6.1 giữ ghi chú "chưa hiện thực" tới Task 12; chỉ sửa lệnh theo O-12.
3. Ghi phiên bản chốt của k6, Schemathesis, Stryker, ReportGenerator, nginx, `docker:cli`, OpenTelemetry vào báo cáo.
4. **Kết quả (06/10/2026):** S-1…S-10 đã chạy, chi tiết ở `spec/PHASE5_REPORT_5.5.md` Mục 1. Không còn spike nào chặn; các quyết định đổi so với plan nằm trong Tech Stack ở trên, O-04 và báo cáo.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| Toàn bộ unit + IT | — | Xanh (không đổi mã) | — |

**Nghiệm thu:** bảng spike đầy đủ S-1…S-10 trong báo cáo, mỗi mục có quyết định; danh mục có OB-1…OB-5, SCALE-2, SCALE-3; không còn container/image nháp.

---

### Task 2: Bộ metric còn lại, `TelemetryOptions` và gauge hàng chờ

**Files:**
- Create:
  - `src/TADSS.Application/Common/TadssMetricNames.cs`
  - `src/TADSS.Application/Common/CircuitStateValue.cs` (enum)
  - `src/TADSS.Infrastructure/Options/TelemetryOptions.cs`, `TelemetryOptionsValidator.cs`, `TelemetryOtlpProtocol.cs` (enum)
  - `src/TADSS.Infrastructure/Telemetry/PendingApprovalsMetricRefresher.cs`
- Modify:
  - `src/TADSS.Application/Common/TadssMetrics.cs`: chuyển hằng sang `TadssMetricNames`, thêm instrument.
  - `src/TADSS.Application/Decisions/DecisionService.cs` (`tadss.decisions`).
  - `src/TADSS.Application/Governance/OverrideService.cs` (`tadss.overrides`).
  - `src/TADSS.Infrastructure/Resilience/StrategyPipeline.cs`, `StrategyResilienceBuilder.cs` (duration, fallbacks, `CircuitBreakerStateProvider` từng strategy).
  - `src/TADSS.Infrastructure/Caching/RedisCacheResilience.cs`, `RedisDecisionCache.cs` (state provider của breaker Redis).
  - `src/TADSS.Application/Abstractions/Persistence/IDecisionQueryStore.cs`, `src/TADSS.Infrastructure/Persistence/EfDecisionAuditStore.cs`, `PendingApprovalsQuery.cs` (đếm theo chi nhánh).
  - `src/TADSS.Infrastructure/InfrastructureServiceCollectionExtensions.cs`: options + validator + `ValidateOnStart`, hosted service refresher.
  - `src/TADSS.Runtime.Api/appsettings.json`: section `Tadss:Telemetry` (`Enabled: false`, còn lại mặc định O-03).
  - Mọi chỗ đang dùng hằng `TadssMetrics.*` cũ (cache, shadow, router, invoker) đổi sang `TadssMetricNames`.
  - Test double hiện thực `IDecisionQueryStore` (nếu có).
- Test:
  - `tests/TADSS.UnitTests/Application/Common/TadssMetricsTests.cs` (mở rộng).
  - `tests/TADSS.UnitTests/Infrastructure/Telemetry/PendingApprovalsMetricRefresherTests.cs`.
  - `tests/TADSS.UnitTests/Infrastructure/Options/TelemetryOptionsValidatorTests.cs`.
  - Bổ sung `DecisionServiceTests`, `OverrideServiceTests`, `StrategyPipelineTests`, `RedisDecisionCacheTests`.
  - IT `tests/TADSS.IntegrationTests/Persistence/PendingApprovalsCountTests.cs`.

**Interfaces:**
- Produces:
  - `public static class TadssMetricNames`:
    - Hằng tên của **mọi** instrument (5 cũ + 8 mới theo O-05), tên nhãn, giá trị nhãn (`CacheResult*`, `ShadowOutcome*`, `ShadowDropReasonQueueFull`, `PipelineOutcome{Automated,Fallback,Timeout}`, `FallbackReason{Timeout,Exception,CircuitOpen}`, `EndpointUnmatched`).
    - Đơn vị `"ms"`.
    - `IReadOnlyList<double> DurationBucketBoundariesMs`.
    - `IReadOnlyList<string> AllInstrumentNames` (để OB-1 và test đối chiếu).
  - `public enum CircuitStateValue { Closed = 0, HalfOpen = 1, Open = 2, Isolated = 3 }`.
  - `TadssMetrics` thêm:
    - `RecordDecision(string status, string strategy, string executionType)`;
    - `RecordRequestDuration(string endpoint, int statusCode, double elapsedMs)`;
    - `RecordPipelineDuration(string strategy, string outcome, double elapsedMs)`;
    - `RecordFallback(string reason, string strategy)`;
    - `RecordOverride(string action)`;
    - `void RegisterCircuitStateSource(Func<IEnumerable<KeyValuePair<string, CircuitStateValue>>> source)` (một nguồn, đăng ký lại thì thay);
    - `void RegisterCacheCircuitStateSource(Func<CircuitStateValue?> source)`;
    - `void ReplacePendingApprovals(IReadOnlyDictionary<Guid, long> countsByBranch)`.
  - `IDecisionQueryStore.CountPendingApprovalsByBranchAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)` → `Task<IReadOnlyDictionary<Guid, long>>`.
  - `public sealed class TelemetryOptions` (`SectionName = "Tadss:Telemetry"`, trường O-03), `TelemetryOptionsValidator`, `public enum TelemetryOtlpProtocol { Grpc, HttpProtobuf }`.
  - `internal sealed class PendingApprovalsMetricRefresher : BackgroundService`, constructor `(IServiceScopeFactory, IOptions<TelemetryOptions>, TadssMetrics, TimeProvider, ILogger<PendingApprovalsMetricRefresher>)`.

**Yêu cầu:**
1. **Ngữ nghĩa O-05:**
   - `DecisionService` gọi `RecordDecision` đúng chỗ đang gửi việc shadow (chỉ bản `PRIMARY` mới của chính request).
   - `OverrideService` gọi `RecordOverride` sau khi ghi override **mới** (không gọi khi replay hay `IsExistingWinner`).
   - `StrategyPipeline` đo bằng `TimeProvider` từ đầu tới cuối `ExecuteAsync`. Ghi `RecordPipelineDuration` cho `automated`/`fallback`/`timeout`; ghi `RecordFallback` một lần cho mỗi kết quả fallback (ánh xạ `FallbackCause` → giá trị nhãn). Hủy của caller → không ghi.
2. **Gauge breaker:**
   - `StrategyResilienceBuilder` gắn một `CircuitBreakerStateProvider` cho mỗi strategy và lưu vào từ điển thread-safe của `StrategyPipeline`.
   - `StrategyPipeline` đăng ký nguồn với `TadssMetrics` một lần trong constructor.
   - Breaker Redis: `RedisCacheResilience` trả kèm state provider; cache đăng ký `RegisterCacheCircuitStateSource`. Không dùng Redis (`Provider = Memory`) → nguồn trả `null` → gauge không phát số đo.
3. **Truy vấn đếm (O-06):**
   - Cùng điều kiện với `GetPendingApprovalsAsync` (bản `PRIMARY` mới nhất mỗi đơn, `NEED_APPROVAL`, chưa override/outcome, `approval_expires_at > nowUtc`), gộp theo `branch_id`.
   - Lỗi tạm thời được dịch thành `PersistenceUnavailableException` như các method khác.
   - Không trả chi nhánh có 0 bản ghi; refresher tự bù 0 cho chi nhánh đã biết.
4. **Refresher:**
   - `Enabled = false` → thoát ngay khỏi `ExecuteAsync`, không truy vấn.
   - Bật thì: chờ một chu kỳ đầu (để khởi động không truy vấn ngay); mỗi chu kỳ một DI scope; `nowUtc` cắt về µs.
   - Lỗi bất kỳ (trừ hủy khi dừng host) → log Warning (chỉ kiểu exception), giữ số cũ.
   - Chờ bằng `TimeProvider` (`Task.Delay(TimeSpan, TimeProvider, CancellationToken)`) để test dùng `FakeTimeProvider`.
5. `TadssMetrics.cs` và `TadssMetricNames.cs` mỗi file ≤ 250 dòng; không file nào khác chứa literal tên metric (grep `"tadss\.` trong `src/` chỉ thấy `TadssMetricNames` và `TadssActivitySource`).

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `Instruments_AllDeclaredNamesPublished` | `MeterListener` bắt mọi instrument của meter | Tập tên instrument = `AllInstrumentNames`; mỗi histogram đơn vị `ms` | OB-5 |
| `DurationBuckets_IncludeKpiThresholds` | Đọc `DurationBucketBoundariesMs` | Tăng dần, chứa 150, 500, 700 | OB-5 |
| `Record_NewInstruments_ExpectedTags_Theory` | Gọi từng `Record*` mới | Đúng tên, nhãn, giá trị | OB-5 |
| `EvaluateAsync_FreshPrimary_RecordsDecisionOnce` | Như test dispatch shadow hiện có | `tadss.decisions` = 1 với `status`/`strategy`/`execution_type` của response | OB-5, UT-11 |
| `EvaluateAsync_ReplayOrRaceLoserOrFailure_RecordsNoDecision_Theory` | Ba nhánh | 0 | OB-5, UT-11 |
| `OverrideAsync_NewOverride_RecordsOnce_ReplayDoesNot` | Override mới rồi gửi lại cùng payload | 1 rồi vẫn 1; nhãn `action` đúng | OB-5, UT-15 |
| `ExecuteAsync_Outcome_RecordsDurationAndFallback_Theory` | Thành công; inner timeout mọi lần thử; ném lỗi; breaker mở | `automated` không có fallback; ba ca còn lại `fallback` + `tadss.fallbacks{reason, strategy}` = 1 | OB-5, UT-07 |
| `ExecuteAsync_OuterTimeout_RecordsTimeoutOutcome` | Outer timeout | `outcome = timeout`; không ghi fallback | OB-5, UT-07 |
| `ExecuteAsync_CallerCancelled_RecordsNothing` | Token của caller hủy | Không có số đo pipeline | OB-5, UT-07 |
| `CircuitStateGauge_FollowsBreakerTransitions` | `FakeTimeProvider`; strategy A lỗi tới khi mở → hết `BreakDuration` → thăm dò thành công | Gauge A: 0 → 2 → 1 → 0; strategy B chưa chạy thì không có series | OB-5, UT-07 |
| `CacheCircuitStateGauge_FollowsRedisBreaker` | Như `RedisDecisionCacheTests` mở breaker | 0 → 2; `Provider = Memory` → không phát | OB-5, UT-13 |
| `Refresher_Disabled_NeverQueries` | `Enabled = false` | Store không được gọi | OB-5 |
| `Refresher_PublishesCountsAndZeroForEmptiedBranch` | Lần 1 {A:2, B:1}, lần 2 {A:1} | Gauge {A:2, B:1} rồi {A:1, B:0} | OB-5 |
| `Refresher_StoreThrows_KeepsLastValuesAndLogsWarning` | Lần 2 ném `PersistenceUnavailableException` | Giữ {A:2, B:1}; một Warning không chứa message của exception | OB-5 |
| `Validate_Theory` (`TelemetryOptionsValidator`) | Endpoint tương đối/khác http(s) khi bật, ratio −0.1/1.1, interval 999/60001, refresh 4/3601, `ServiceName` rỗng/65 ký tự; mặc định | Ca sai fail với tên khóa; mặc định đạt | OB-5 |
| `CountPendingApprovalsByBranchAsync_MatchesPendingQuery` (IT) | Dữ liệu như IT-09: hai chi nhánh, bản hết hạn, đã override, có outcome, bị thay thế, `SHADOW` | Số đếm mỗi chi nhánh = `TotalItems` của `GetPendingApprovalsAsync` cùng `nowUtc` | IT-09 |
| `AppSettingsValidationTests` hiện có | `Tadss:Telemetry` mới | Xanh (validator đăng ký `ValidateOnStart`) | UT-14 |

**Nghiệm thu:** build 0/0, unit + IT xanh, số test ghi vào báo cáo; grep literal tên metric như yêu cầu 5.

---

### Task 3: Request logging đúng status và metric thời gian request

> **Việc tồn từ Task 2 (ghi 07/10/2026) — ĐÃ XỬ LÝ ở báo cáo mục 3d (40/40 lượt xanh, chưa giải thích được lần đỏ cũ, không so baseline vì không tái hiện):**
> 1. **Điều tra lỗi `OverrideEndpointTests.Override_TwentyConcurrentSameKeyAndPayload_AllSucceedIdentically`.** Đỏ 1 trong 9 lượt chạy toàn bộ IT sau Task 2 (thất bại sau 239 ms; chạy riêng 0/31 lần tái hiện; lúc đó chưa lưu nguyên văn lỗi).
>    - Chạy `dotnet test tests/TADSS.IntegrationTests -c Release --no-build -nologo --filter "FullyQualifiedName~OverrideEndpointTests"` 40 lượt cho **mã hiện tại** và 40 lượt cho **baseline `cad5150`** (dựng bằng `git archive cad5150` vào thư mục tạm, xóa sau khi xong). **Lưu nguyên văn mọi lỗi.**
>    - Kiểm đầu tiên: có phải 503 `PERSISTENCE_UNAVAILABLE` do deadlock/serialization (SqlState `40001`/`40P01`) khi 20 request cùng ghi một dòng.
>    - Kết luận một trong hai: lỗi có từ trước (ghi known issue, báo người dùng) hoặc do thay đổi của gói (sửa trước khi làm tiếp). Mất ~15 phút và ~500 MB tải lại image test.
> 2. **Không ép tải CPU khi chạy test trên máy này.** Lần thử ép tải (6 tiến trình đốt CPU) làm Docker Desktop ngừng hoạt động nên số liệu vô giá trị.

**Files:**
- Create:
  - `src/TADSS.Runtime.Api/Middlewares/RequestMetricsMiddleware.cs`
  - `src/TADSS.Runtime.Api/Composition/RequestLoggingConfiguration.cs` (tùy chọn `UseSerilogRequestLogging`: mức log theo status, làm giàu `ClientId`)
- Modify: `src/TADSS.Runtime.Api/Composition/RequestPipelineExtensions.cs` (thứ tự O-09, XML doc).
- Test:
  - `tests/TADSS.UnitTests/Api/Middlewares/RequestMetricsMiddlewareTests.cs`.
  - `tests/TADSS.UnitTests/Api/Composition/RequestLoggingTests.cs`.
  - Cập nhật `RequestPipelineTests`, `SecurityPipelineOrderTests` (nếu khẳng định vị trí logging).

**Interfaces:**
- Produces:
  - `public sealed class RequestMetricsMiddleware`, constructor `(RequestDelegate next, TadssMetrics metrics, TimeProvider timeProvider)`.
  - `public static class RequestLoggingConfiguration { public static void Configure(RequestLoggingOptions options); }` (kiểu `RequestLoggingOptions` của Serilog.AspNetCore).

**Yêu cầu:**
1. Thứ tự đúng O-09; XML doc của `UseTadssRequestPipeline` cập nhật.
2. **`RequestMetricsMiddleware`:**
   - Chỉ đo khi path bắt đầu `/api/`.
   - Ghi trong `finally`, kể cả khi exception lan qua: status khi exception chưa được xử lý ghi là 500. Thực tế exception handler đứng trong nên middleware luôn thấy status cuối.
   - Nhãn `endpoint`: lấy từ endpoint (`RouteEndpoint.RoutePattern.RawText`) **sau** `next`; không có endpoint → `"{METHOD} unmatched"`. Không bao giờ dùng path thật (chứa `decisionId`) làm nhãn.
3. **Request logging:**
   - `GetLevel`: exception chưa xử lý hoặc status ≥ 500 → Error, còn lại Information.
   - `EnrichDiagnosticContext` gắn `ClientId` nếu request đã xác thực.
   - Template giữ như hiện nay.
   - Không log header (đặc biệt `X-Api-Key`).
4. Hành vi bảo mật quan sát được không đổi: 413 trước 401, 401 trước 429, `/health/*` miễn khóa.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `RequestLog_ExceptionMappedToStatus_LogsFinalStatus_Theory` | Host in-process; request gây 409 (khóa idempotency dùng lại), 404, 503 (store ném `PersistenceUnavailableException`) | Dòng hoàn tất ghi đúng 409/404 (Information) và 503 (Error); không có dòng "500" | UT-14 |
| `RequestLog_Authenticated_IncludesClientId` | Request có khóa hợp lệ | Thuộc tính `ClientId` của dòng hoàn tất = `ClientId` cấu hình; không có giá trị khóa hay mã băm trong mọi log | UT-14, SEC-7 |
| `RequestMetrics_RecordsEndpointTemplateAndFinalStatus_Theory` | 200 (POST), 200 (GET theo id), 400, 401, 409, 413, 429 | Đúng một số đo mỗi request; `endpoint` = `POST api/v2/decisions` / `GET api/v2/decisions/{decisionId}` hoặc `"{METHOD} unmatched"` (401/413/429); `status_code` đúng | OB-5 |
| `RequestMetrics_HealthAndSwagger_NotRecorded` | `/health/live`, `/health/ready`, `/swagger/index.html` (Development) | 0 số đo | OB-5 |
| `RequestPipelineTests`, `SecurityPipelineOrderTests` (cập nhật) | Như hiện có + vị trí logging/metrics ngoài exception handler | Xanh; test cũ khẳng định "logging trong exception handler" được thay thế, ghi tên vào báo cáo | UT-14 |

**Nghiệm thu:** build 0/0, unit + IT xanh, số test ghi vào báo cáo.

---

### Task 4: OpenTelemetry — exporter, trace và log

> **Việc tồn (ghi 07/10/2026) — ĐÃ XỬ LÝ ở báo cáo mục 3e (quét lỗ hổng sạch; unit 1793 + IT 220 xanh với package thật):**
> 1. Nếu việc điều tra lỗi override ở đầu Task 3 chưa làm xong thì làm trước khi thêm package, để lỗi mới (nếu có) không lẫn với lỗi cũ.
> 2. **Kiểm chứng `System.Diagnostics.DiagnosticSource` 10.0.0 (spike S-10) chỉ có ý nghĩa sau khi thêm package vào repo:** chạy toàn bộ unit, IT, `tools/ci/check-vulnerable-packages.sh` và build `--warnaserror`. Test xanh trước đó (1756 + 214) **không** phải bằng chứng vì chưa có package OpenTelemetry nào trong repo. Gói NuGet đã có sẵn trong cache máy (do spike), nên không cần tải lại.

**Files:**
- Create:
  - `src/TADSS.Application/Common/TadssActivitySource.cs`
  - `src/TADSS.Runtime.Api/Composition/TelemetryServiceCollectionExtensions.cs`
  - `src/TADSS.Runtime.Api/Composition/SerilogTelemetryConfiguration.cs`
- Modify:
  - `Directory.Packages.props`, `src/TADSS.Runtime.Api/TADSS.Runtime.Api.csproj`, các `packages.lock.json` (Runtime.Api, hai project test).
  - `tests/TADSS.IntegrationTests/TADSS.IntegrationTests.csproj` (`OpenTelemetry.Exporter.InMemory`).
  - `src/TADSS.Runtime.Api/Program.cs`: callback `UseSerilog` gọi thêm `SerilogTelemetryConfiguration.Apply(...)`; không thêm logic khác.
  - `src/TADSS.Runtime.Api/Composition/PresentationServiceCollectionExtensions.cs`: gọi `AddTadssTelemetry`.
  - Span: `DecisionService`, `ShadowExecutor` (Application); `StrategyPipeline`, `RedisDecisionCache` (Infrastructure).
  - `tests/TADSS.UnitTests/Api/Composition/CompositionRootBoundaryTests.cs`: thêm namespace `OpenTelemetry` vào danh sách chỉ được dùng trong `Composition/`/`Program.cs`.
- Test:
  - `tests/TADSS.UnitTests/Api/Composition/TelemetryRegistrationTests.cs`.
  - IT `tests/TADSS.IntegrationTests/Observability/TelemetryExportTests.cs`, `TelemetryCollectorUnavailableTests.cs`.

**Interfaces:**
- Produces:
  - `public static class TadssActivitySource`: `SourceName = "TADSS.Runtime"`, hằng tên span (`DecisionEvaluate`, `StrategyExecute`, `ShadowExecute`, `RedisCommand`), hằng tên thuộc tính (`tadss.*` theo O-07), `ActivitySource Source { get; }`.
  - `public static IServiceCollection AddTadssTelemetry(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)`.
  - `public static class SerilogTelemetryConfiguration { public static LoggerConfiguration Apply(LoggerConfiguration logger, IConfiguration configuration, IHostEnvironment environment); }`.

**Yêu cầu:**
1. **Package (S-10):**
   - Dùng đúng bộ phiên bản đã chốt ở spike; ghim trong `Directory.Packages.props`; lock file cập nhật.
   - `tools/ci/check-vulnerable-packages.sh` sạch.
   - Không package nào vào Application/Infrastructure (`ProjectReferenceRulesTests` không đổi allowlist).
2. **`AddTadssTelemetry`** đọc `TelemetryOptions` (bind trực tiếp từ section, đã có validator ở Task 2).
   - `Enabled = false` → return, không đăng ký gì.
   - Bật:
     - Resource O-04.
     - Tracing: `AddSource(TadssActivitySource.SourceName)`; ASP.NET Core instrumentation lọc bỏ `/health/*`, `/swagger/*`; Npgsql; sampler O-07; exporter OTLP theo `OtlpEndpoint`/`OtlpProtocol`.
     - Metrics: `AddMeter(TadssMetricNames.MeterName)`; View histogram với `DurationBucketBoundariesMs` cho mọi instrument đơn vị `ms`; reader định kỳ `MetricExportIntervalMs`; exporter OTLP.
     - **Không** bật metric mặc định của ASP.NET Core/runtime (dashboard chỉ dùng meter của TADSS, OB-1).
3. **Span:**
   - Tạo bằng `TadssActivitySource.Source.StartActivity(...)`, thuộc tính theo O-07. Không có listener → `StartActivity` trả `null`, mã phải chịu được.
   - Span lỗi đặt `ActivityStatusCode.Error` với **kiểu** exception, không message.
   - Span shadow chạy trên worker nên là gốc mới với `ActivityLink` tới context của request (lưu `ActivityContext` vào `ShadowWorkItem`; thêm trường tùy chọn, không đổi ngữ nghĩa A-11).
4. **Log (O-08):** sink OTLP chỉ thêm khi `Enabled && ExportLogs`; mức theo cấu hình `Serilog` hiện có; thuộc tính resource như trace.
5. **Bảo mật:**
   - ASP.NET Core instrumentation không ghi header request/response và không ghi query string (route có `decisionId` chấp nhận được, không phải dữ liệu cá nhân).
   - Npgsql không ghi giá trị tham số.
   - Không thuộc tính nào chứa khóa, mã băm, chuỗi kết nối.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `AddTadssTelemetry_Disabled_RegistersNoProviders` | Host `Testing`, `Enabled = false` | Không có `TracerProvider`/`MeterProvider` trong DI | OB-5 |
| `AddTadssTelemetry_Enabled_ResourceAndSources` | `Enabled = true`, `ServiceInstanceId = "tadss-x"` | Resource có bốn thuộc tính O-04; tracer nghe `TADSS.Runtime`; meter `TADSS.Runtime` được thu | OB-5 |
| `CompositionRootBoundaryTests` (mở rộng) | Quét Runtime.Api | `OpenTelemetry.*` chỉ ở `Composition/`/`Program.cs` | UT-14 |
| `PostDecision_TelemetryEnabled_ExportsSpanTree` (IT) | Exporter in-memory; POST hợp lệ (cache miss, Redis thật) | Một trace: span server → `tadss.decision.evaluate` → `tadss.strategy.execute`; có span Npgsql và span Redis cùng trace; thuộc tính `tadss.correlation_id` = header gửi; `tadss.client_id` có mặt | OB-5 |
| `PostDecision_Fallback_SpanMarksOutcome` (IT) | Strategy ném lỗi | `tadss.strategy.execute` có `tadss.outcome = fallback`, `tadss.fallback_reason = exception`; không có message lỗi trong thuộc tính | OB-5 |
| `Telemetry_NoSecretsInSpansOrLogs` (IT) | Gom mọi span + log của vài request (có khóa, có lỗi 401/409/503) | Không giá trị nào chứa khóa API, mã băm khóa, mật khẩu chuỗi kết nối PostgreSQL/Redis | SEC-7, OB-5 |
| `Logs_CarryTraceContext` (IT) | Sink Serilog thu trong bộ nhớ (test double) + exporter span in-memory | Log ghi trong request có `TraceId` = trace id của span server | OB-5 |
| `Metrics_ExportedNamesAndBuckets` (IT) | Exporter metric in-memory, ép xuất | Tên metric đã xuất ⊆ `AllInstrumentNames`; histogram có đúng biên `DurationBucketBoundariesMs` | OB-5 |
| `PostDecision_CollectorUnreachable_ServesNormally` (IT) | `Enabled = true`, endpoint `http://127.0.0.1:{cổng đóng}` | 20 POST đều 200; độ trễ không lớn hơn lượt đối chứng tắt telemetry quá 100 ms ở p95; host dừng trong ≤ 10 s | OB-5 |
| `ShadowSpan_LinksToRequest` (IT) | Shadow bật | Span `tadss.shadow.execute` có link tới trace của request `PRIMARY` | OB-5 |

**Nghiệm thu:** build 0/0, unit + IT xanh, quét lỗ hổng sạch, số test và phiên bản package ghi vào báo cáo.

> **Ghi chú triển khai (07/10/2026):** test `Metrics_ExportedNamesAndBuckets` (IT) được thay bằng `TelemetryMetricViewTests` (unit, `MeterProvider` thật với meter tên riêng) vì meter `TADSS.Runtime` trùng tên của hàng trăm host trong cùng process làm SDK vô hiệu hóa luồng metric ở lượt chạy toàn bộ IT (chi tiết báo cáo mục 3e). Cấu hình đọc lúc đăng ký DI cần `UseSetting` (`EarlySettings` của `DecisionApiTestBase`) vì `ConfigureAppConfiguration` chỉ nạp lúc `Build()`.

---

### Task 5: Dashboard, compose vận hành và kiểm chứng OB (điểm dừng Phần A)

**Files:**
- Create:
  - `docker/grafana/dashboards/tadss-runtime.json`
  - `docker/grafana/provisioning/dashboards/tadss.yaml`
  - `tools/observability/verify-observability.sh`
  - `tests/TADSS.UnitTests/Architecture/DashboardMetricReferenceTests.cs`
  - `tests/TADSS.UnitTests/TestData/K6MetricNames.cs`, `PrometheusNameMapper.cs` (quy tắc chuyển tên O-10, dùng chung test)
- Modify:
  - `docker-compose.yml` (O-11).
  - `tools/expose/verify-expose.sh` (NG-1 theo service).
  - `tests/TADSS.IntegrationTests/Infrastructure/ComposeFixtureAlignmentTests.cs`: phân tích theo service bằng YamlDotNet; `Directory.Packages.props` + csproj test thêm `YamlDotNet`.
  - `.github/workflows/ci.yml`: job `observability-check` sau `build-and-test`.

**Interfaces:**
- Produces:
  - `PrometheusNameMapper.ToPrometheusNames(string instrumentName, InstrumentKind kind, string? unit)` → tập tên series hợp lệ (ví dụ counter → `{name}_total`).
  - `K6MetricNames.All` (danh sách metric k6 dashboard được dùng).

**Yêu cầu:**
1. **Dashboard theo O-10:**
   - Mỗi panel có `title` tiếng Việt, mô tả ngắn.
   - Datasource tham chiếu bằng UID xác nhận ở S-1.
   - `refresh` 5s, khoảng mặc định 30 phút.
   - Phân vị tính từ histogram (`histogram_quantile` trên `_bucket`).
   - Panel "tỉ lệ ≤ 150 ms" và "tỉ lệ ≤ 700 ms" tính trực tiếp từ bucket `le="150"`/`le="700"` (khớp cách evaluator kết luận SC1/SC2).
2. **Provisioning:** chỉ đọc, không cho sửa trên UI (`allowUiUpdates: false`), thư mục Grafana "TADSS".
3. **Compose vận hành (O-11):**
   - `docker compose config` vẫn hợp lệ; ba service, năm cổng loopback.
   - `tadss-runtime` không mount, migration literal `"false"`, không biến secret.
4. **`verify-observability.sh`** (theo mẫu `verify-expose.sh`):
   - Project riêng `tadss-observability-check`, override tạm thêm PostgreSQL/Redis dùng một lần và biến chuỗi kết nối/khóa ngẫu nhiên, cổng trống.
   - Khởi động `tadss-runtime` + `grafana`, **không** ngrok.
   - Gửi 20 POST hợp lệ.
   - Kiểm:
     - **OB-2**: `GET /api/dashboards/uid/tadss-runtime` 200.
     - **OB-4**: truy vấn Prometheus (qua API datasource của Grafana) `tadss_decisions_total` > 0 trong ≤ 60 s.
   - Thoát dọn sạch; không đọc file secret; khóa không in ra; chạy được trên Git Bash và Ubuntu.
5. **CI:** job `observability-check` chạy script trên `ubuntu-latest`.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `Dashboard_ReferencesOnlyDeclaredMetrics` | Đọc JSON, trích định danh trong mọi `expr` | Mọi `tadss_*` ánh xạ về một instrument của `AllInstrumentNames`; mọi `k6_*` ∈ `K6MetricNames.All`; không tiền tố khác | OB-1 |
| `Dashboard_HasRequiredPanels` | Đọc JSON | Có panel cho mọi mục tối thiểu của spec 4.5 + hàng chờ, override, shadow, k6 (so theo danh sách metric bắt buộc phải xuất hiện) | OB-1 |
| `Dashboard_UidAndProvisioningPathConsistent` | Đọc JSON + yaml + compose | uid `tadss-runtime`; provider trỏ đúng thư mục mount; mount chỉ đọc | OB-2 |
| `PrometheusNameMapper_Theory` | Counter, histogram `ms`, gauge | Đúng quy tắc đã xác nhận ở S-2 | OB-1 |
| `ComposeFixtureAlignmentTests` (viết lại theo service) | Như hiện có, phạm vi `tadss-runtime`; thêm: `grafana` chỉ mount hai thư mục provisioning ở chế độ chỉ đọc; `tadss-runtime` bật telemetry tới `grafana:4317` | Xanh; test quét cả file cũ ghi tên vào báo cáo là "thay thế" | NG-1, NG-5, OB-2 |
| `verify-expose.sh` | Chạy thật | TẤT CẢ ĐẠT (NG-1 theo service) | NG-1…NG-5, SEC-6 |
| `verify-observability.sh` | Chạy thật | OB-2, OB-4 ĐẠT | OB-2, OB-4 |

**Nghiệm thu:**
- Build 0/0, unit + IT xanh, hai script đạt, số test ghi vào báo cáo.
- `docker compose up -d --build` của người dùng mở được `127.0.0.1:3001` thấy dashboard (mô tả cách xem trong README ở Task 12, ghi chú tạm vào báo cáo Phần A).
> **Ghi chú triển khai (08/10/2026):** provisioning mount bằng **một file** (không phải thư mục) để không che file có sẵn của otel-lgtm; script kiểm chứng dùng mạng riêng của project thử; `Dashboard_UidAndProvisioningPathConsistent` đặt ở IT cùng test compose (chi tiết báo cáo mục 3f).

- **Dừng.** Báo cáo Phần A; đề xuất commit `feat(observability): opentelemetry, runtime metrics and grafana dashboard (5.5 part A)`.

---

## PHẦN B — CHẾ ĐỘ ĐÁNH GIÁ

### Task 6: Hoàn tất ma trận FI và đổi profile tiêm lỗi nóng

**Files:**
- Create:
  - `src/TADSS.Infrastructure/FaultInjection/FaultInjectionControl.cs`
  - `src/TADSS.Runtime.Api/Composition/TestingConfigurationExtensions.cs`
  - IT `tests/TADSS.IntegrationTests/FaultInjection/InfrastructureLatencyTests.cs` (FI-07, FI-09), `FaultInjectionReloadTests.cs`
- Modify:
  - `FaultInjectionStrategyDecorator.cs`: nhận `FaultInjectionControl`.
  - `StrategyServiceCollectionExtensions.cs` (đăng ký control).
  - `Program.cs` (một dòng `builder.Configuration.AddTadssTestingConfiguration(builder.Environment)`).
  - `FaultInjectionStrategyDecoratorTests`.
- Test: `tests/TADSS.UnitTests/Infrastructure/FaultInjection/FaultInjectionControlTests.cs`, `tests/TADSS.UnitTests/Api/Composition/TestingConfigurationExtensionsTests.cs`.

**Interfaces:**
- Produces:
  - `public sealed class FaultInjectionControl : IDisposable` — constructor `(IOptions<FaultInjectionOptions> options, IConfiguration configuration, IEnumerable<IValidateOptions<FaultInjectionOptions>> validators, TadssMetrics metrics, ILogger<FaultInjectionControl> logger)`; `FaultInjectionSnapshot Current { get; }`.
  - `public sealed record FaultInjectionSnapshot(FaultInjectionProfile Profile, int DelayMs)` (Infrastructure/FaultInjection): bất biến; control thay **nguyên** tham chiếu (`Volatile`) khi reload thành công, nên không lời gọi nào thấy `Profile` mới đi với `DelayMs` cũ. Decorator đọc `Current` **một lần** đầu mỗi lời gọi và dùng bản đó tới hết lời gọi.
  - `FaultInjectionStrategyDecorator(IReservationDecisionStrategy inner, FaultInjectionControl control, TimeProvider timeProvider)`.
  - `public static IConfigurationBuilder AddTadssTestingConfiguration(this IConfigurationBuilder configuration, IHostEnvironment environment)`; hằng `FaultInjectionFileVariable = "TADSS_FAULT_INJECTION_FILE"`.

**Yêu cầu:**
1. **O-17:**
   - Callback reload không bao giờ ném; bản mới chỉ thay `Profile`/`DelayMs`.
   - `Enabled` hay `TargetStrategy` đổi lúc chạy → log Information "chỉ có hiệu lực sau khi khởi động lại", không áp.
   - Profile đổi → log Warning một dòng (profile mới, `DelayMs`) để dòng thời gian log khớp `chaos_log`.
2. `CachePolicy` của decorator đọc `Profile` hiện hành mỗi lần (Disabled khi ≠ None).
3. `AddTadssTestingConfiguration`: chỉ khi `environment.IsEnvironment("Testing")` và biến có giá trị → `AddJsonFile(path, optional: true, reloadOnChange: true)` thêm vào **cuối**; ngược lại không làm gì (không đọc file).
4. **FI-07 và FI-09** dùng `FaultInjectionTestBase` (đồng hồ thật, Toxiproxy) và thời hạn tính từ cấu hình test, không hằng số rời.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `PostDecision_RedisLatency200Ms_NoFallbackBoundedLatency` (IT) | Toxic latency 200 ms trên proxy Redis; `RedisTimeoutMs = 150`, ngưỡng breaker nhỏ; 30 POST tuần tự, payload khác nhau | Mọi response 200 `AUTOMATED` (không `FALLBACK`); mỗi request ≤ 2 × `RedisTimeoutMs` + 400 ms; sau ngưỡng có `tadss.cache.requests{result=bypass}` > 0 và các request sau ≤ `RedisTimeoutMs` + 300 ms | FI-07 |
| `PostDecision_PostgresLatency300Ms_CorrectSingleRecords` (IT) | Toxic latency 300 ms trên proxy PostgreSQL; 10 POST khóa khác nhau + gửi lại 5; rồi 10 POST song song cùng khóa | Mọi response đúng (200, replay cùng `decisionId`); đúng 10 + 1 bản `PRIMARY`; mỗi request ≥ 300 ms; `tadss.decision.duration` ghi nhận | FI-09 |
| `Reload_ValidProfile_AppliesWithoutRestart` | Control với `IConfiguration` trong bộ nhớ; đổi `Profile` → `Throw` | `Profile` mới; một Warning | FI-03 |
| `Reload_Invalid_KeepsPreviousAndCountsFailure_Theory` | `DelayMs = 0`, `Profile = "Bogus"` | Giữ bản cũ; Error; `tadss.config.reload_failures{section=Tadss:Testing:FaultInjection}` = 1 | UT-09 |
| `Current_ConcurrentReload_NeverMixesFields` | 8 luồng đọc `Current` liên tục trong khi reload xen kẽ (`Delay`, 1000) ↔ (`Throw`, 5000) 1000 lần | Mọi snapshot đọc được là một trong hai cặp gốc, không có cặp lai | UT-09 |
| `Reload_TruncatedJsonFile_KeepsPrevious` (IT) | Host `Testing`; ghi file JSON cụt (nửa nội dung) | Profile giữ nguyên; `tadss.config.reload_failures` tăng; request vẫn phục vụ | UT-09 |
| `Reload_EnabledOrTarget_NotApplied` | Đổi `Enabled`/`TargetStrategy` | Không đổi hành vi; log Information | UT-09 |
| `AddTadssTestingConfiguration_NonTesting_IgnoresVariable_Theory` | `Production`/`Development`/`Staging` + biến có giá trị | Không thêm nguồn cấu hình nào | UT-09 |
| `PostDecision_FaultFileChanged_ProfileSwitchesLive` (IT) | Host `Testing`, file tạm `Profile = None` → POST 200 `AUTOMATED` → ghi file `Throw` | Trong ≤ 10 s POST trả fallback `FALLBACK_STRATEGY_EXCEPTION`; ghi lại `None` → hết fallback | FI-03 |
| FI-01…FI-06, FI-08, FI-10 hiện có; `FaultInjectionRegistrationTests` | — | Xanh, không sửa kỳ vọng | FI-*, UT-09 |

**Nghiệm thu:** build 0/0, unit + IT xanh; mọi mã FI-01…FI-10 có ít nhất một test (đếm theo trait, ghi bảng vào báo cáo).

> **Ghi chú triển khai (08/10/2026):** nguồn JSON thêm với `OnLoadException = Ignore` (nếu không, file cụt làm sập tiến trình) và control coi section/khóa `Profile` biến mất là nạp lại thất bại (giữ bản cũ); chi tiết báo cáo mục 3g.

---

### Task 7: Stack đánh giá, image evaluator và bước (a) test gộp

**Files:**
- Create:
  - `docker/evaluator/Dockerfile`, `docker/evaluator/Dockerfile.dockerignore`, `docker/evaluation/nginx.conf`.
  - `tools/evaluation/requirements.txt`; `tools/evaluation/evaluator/` gồm:
    - `__main__.py`: CLI, đọc `EVAL_MODE`/`EVAL_STEPS`, điều phối, mã thoát, bảng tóm tắt;
    - `run_context.py`: `run_id`, thư mục, thời điểm;
    - `workspace.py`: copy `/repo` → `/work` có loại trừ, commit, cờ dirty;
    - `machine.py`: CPU/RAM/OS/Docker, digest image;
    - `catalog.py`: đọc danh mục, ghép trx;
    - `steps/tests_step.py`: bước a;
    - `clients/grafana.py`: OB-2.
  - `tools/evaluation/tests/` (pytest + `fixtures/`).
  - `tests/TADSS.UnitTests/Architecture/CatalogTraceabilityTests.cs`, `tests/TADSS.UnitTests/TestData/CatalogIndex.cs` (đọc danh mục markdown, quét reflection, ghi chỉ mục), `tests/TADSS.IntegrationTests/Infrastructure/CatalogTraceabilityTests.cs`.
- Modify:
  - `docker-compose.yml` (service `eval-*` + `evaluator`, volume `eval-faults`, `eval-nuget`).
  - `docker/toxiproxy/toxiproxy.json` (upstream `eval-postgres`/`eval-redis`).
  - `.gitignore`, `.dockerignore` (thêm `evidence/`).
  - `ComposeFixtureAlignmentTests` (luật cho service đánh giá).
  - `.github/workflows/ci.yml`: job `evaluation-tools` (setup-python 3.11, `pip install -r`, `pytest` cho `tools/evaluation` và `tools/evidence`; `docker build` image evaluator, không chạy).
  - Test thiếu `CatalogId` mà `CatalogTraceabilityTests` phát hiện: thêm trait đúng mã, **không** đổi nội dung test.

**Interfaces:**
- CLI evaluator:
  - `python -m evaluator` (entrypoint của image).
  - Biến `EVAL_MODE`, `EVAL_STEPS`; nội bộ: `EVAL_TADSS_URL`, `EVAL_PROXY_URL`, `EVAL_API_KEY`, `EVAL_GRAFANA_URL`, `EVAL_PROMETHEUS_URL`, `EVAL_TOXIPROXY_URL`, `EVAL_PG_DSN` (đặt trong compose).
- Mỗi bước là một hàm `run(context) -> StepResult`:
  - `StepResult`: `name`, `status` = `passed`/`failed`/`skipped`/`error`, `started_at`, `ended_at`, `artifacts`, `notes`.
  - Bước sau chạy cả khi bước trước `failed`; dừng khi `error` hạ tầng thì đánh dấu các bước còn lại `skipped`.
- Chỉ mục catalog: `{"<Namespace.Class.Method>": ["UT-01", ...]}` ghi ra `catalog-index.{AssemblyName}.json`.

**Yêu cầu:**
0. **Làm trước tiên (rủi ro S-4 còn lại):** spike S-4 mới chạy 4 test. Trước khi viết tiếp, chạy **toàn bộ** `TADSS.IntegrationTests` (đặc biệt `FaultInjection/` đi qua Toxiproxy và `Scale/`) trong container SDK có mount socket như S-4. Đỏ do mạng giữa evaluator, Toxiproxy và `host.docker.internal` (không phải do test) ⇒ dừng và hỏi, vì D-12 bước (a) phụ thuộc điều này. Ghi số test và thời gian vào báo cáo.
1. **Compose theo O-13:**
   - Healthcheck: `pg_isready`, `redis-cli ping`, `wget /health/live` cho TADSS; nginx kiểm `/health/live` qua proxy.
   - `evaluator` `depends_on` (`service_healthy` khi có healthcheck) mọi service đánh giá.
   - Cổng host duy nhất của profile: `127.0.0.1:3002` (Grafana).
2. **Image evaluator (O-14):**
   - Mọi `FROM`/`COPY --from` ghim tag.
   - `Dockerfile.dockerignore` loại trừ secret như O-14 (image không chứa mã nguồn; mã lấy từ mount lúc chạy).
   - Chạy được bằng user không phải root **trừ** khi cần socket. Nếu quyền socket buộc root thì ghi lý do vào báo cáo và README.
3. **Workspace:**
   - Loại trừ đúng danh sách O-14.
   - Sau khi copy: kiểm `/work` **không** có file nào tên `appsettings.Development.json` hay `.env*`; có ⇒ `error`, dừng.
4. **Bước (a):**
   - Trên `/work`: `dotnet restore --locked-mode`, build Release `--warnaserror`.
   - Unit test và IT với logger trx, coverlet cobertura; biến Testcontainers theo S-4; `TADSS_CATALOG_INDEX_DIR` trỏ `raw/tests/`.
   - Ghép trx + chỉ mục → `raw/tests/catalog-results.csv` (`catalog_id`, `test`, `outcome`, `duration_ms`).
   - Chạy `verify-expose.sh`, `verify-observability.sh` nếu S-9 đạt.
   - Kiểm **OB-2** của stack đánh giá (API `eval-grafana`).
   - `failed` nếu có test đỏ; `error` nếu không build được.
5. **`CatalogTraceabilityTests`:** mỗi phương thức test có `CatalogId` (trait ở phương thức hoặc lớp) và mọi mã có trong bảng của methodology (cột ID của Mục 4.1–4.14).

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| `EveryTest_HasKnownCatalogId` (UT và IT) | Quét assembly | 0 test thiếu mã; 0 mã lạ | UT-14 |
| `CatalogIndex_ExportedWhenVariableSet` | Đặt biến tới thư mục tạm | File chỉ mục có mọi test của assembly | UT-14 |
| `EvaluationServices_Theory` (compose) | Mỗi service đánh giá | Có profile `evaluation`; không nối `smashcourt-network`; TADSS chạy `Testing`; chuỗi kết nối chỉ trỏ `eval-toxiproxy`; image Postgres/Redis/Toxiproxy/otel-lgtm bằng hằng của fixture; mọi tag ghim | NG-1 |
| `EvaluationProfile_OnlyGrafanaPortOnLoopback` | Cổng của các service đánh giá | Đúng một cổng `127.0.0.1:3002` | NG-5 |
| `OperationsServices_UnaffectedByEvaluationProfile` | Render không profile (YamlDotNet: service không có `profiles`) | Đúng ba service vận hành như Task 5 | NG-1 |
| pytest `test_workspace_excludes_secrets` | Cây mẫu có `appsettings.Development.json`, `.env`, `bin/` | Không file nào bị copy; kiểm tra sau copy bắt được file lọt | — |
| pytest `test_run_id_next_sequence` | `evidence/` có `r1`, `r2` cùng ngày | Trả `r3`; khác ngày → `r1` | — |
| pytest `test_catalog_join` | trx mẫu (theory nhiều ca) + chỉ mục | Mỗi ca theory gom về đúng mã; test không có trong chỉ mục → báo lỗi | — |
| pytest `test_exit_codes` | Kết hợp trạng thái bước | 0/1/2 đúng O-14 | — |
| Smoke | `docker compose -p tadss-eval --profile evaluation run --rm --build -e EVAL_STEPS=a evaluator` | Bước a `passed`; `raw/tests/` có trx của hai project, chỉ mục, `catalog-results.csv`; OB-2 đạt; mã thoát 0 | OB-2 |

**Nghiệm thu:** build 0/0, unit + IT xanh, pytest xanh, smoke đạt (ghi thời gian bước a); dọn stack bằng lệnh O-12.

> **Ghi chú triển khai (08/10/2026):** SDK .NET 8 không đọc `TADSS.slnx` nên evaluator restore/build theo hai project test; `verify-observability.sh` không chạy được trong container (mount compose theo đường dẫn container), OB-2 kiểm trên `eval-grafana`; không có renderer ảnh (đã chốt bỏ); bước (a) mất 11,4 phút (báo cáo mục 3h).

---

### Task 8: Kịch bản tải k6 và bước (b)

**Files:**
- Create:
  - `tests/load/lib/payload.js` (bảng trộn O-15), `tests/load/lib/output.js` (dòng D2, `handleSummary`).
  - `tests/load/scenarios/lt01.js`, `lt02.js`, `lt03.js`, `lt05.js`, `scale2.js`; `tests/load/config/modes.json` (tham số theo chế độ); `tests/load/README.md`.
  - `tools/evaluation/evaluator/clients/k6.py`, `prometheus.py`; `tools/evaluation/evaluator/steps/load_step.py`.
- Test: pytest `tools/evaluation/tests/test_load_*.py`, `test_payload_samples.py` (kiểm mẫu payload theo `docs/contracts/schemas/v2` bằng `jsonschema` ghim).

**Interfaces:**
- `k6 run` với `--env` theo O-15, `--console-output raw/load/{scenario}/requests.jsonl`, `--summary-export`/`handleSummary` → `raw/load/{scenario}/summary.json`, output metric theo S-3.
- `scenarios/*.js` xuất `options` từ `modes.json` theo `MODE`. `lib/payload.js` có chế độ `SAMPLE_ONLY=N` in N payload (không gửi) để pytest kiểm schema.

**Yêu cầu:**
1. **Hồ sơ tải:**

   | Kịch bản | Bản nhanh | Bản đầy đủ |
   | :--- | :--- | :--- |
   | LT-01 | constant-arrival-rate 50 RPS, warm-up 30 s + 2 phút, 1 lần | warm-up 1 phút + 10 phút, **3 lần lặp** |
   | LT-02 | ramping-arrival-rate 10 → 200 RPS trong 3 phút | 10 → 400 RPS theo bậc, ~15 phút |
   | LT-05 | không chạy | 50 RPS, warm-up 1 phút + 60 phút |

   - `preAllocatedVUs`/`maxVUs` đủ để k6 không bỏ lượt; số lượt bị bỏ (`dropped_iterations`) được báo cáo.
2. Warm-up gắn tag `phase=warmup` và bị loại khỏi thống kê ở bước (e).
3. **Bước (b):**
   - Chạy LT-01 → LT-02 → LT-05 trên `EVAL_TADSS_URL` (instance 1 trực tiếp).
   - Ghi cửa sổ thời gian thật của từng lượt vào `raw/load/windows.json` (cho truy vấn Prometheus ở (e)).
   - Sau mỗi lượt chờ 2 × `MetricExportIntervalMs` cho metric kịp xuất.
4. **LT-02 điểm gãy:** RPS lớn nhất mà trong cửa sổ 30 s tại bậc đó p95 phía client < 150 ms và lỗi < 1 % (tính ở bước e từ D2).
5. Payload: 0 lỗi 400 trong mọi lượt (lỗi 400 nghĩa là bộ sinh sai ⇒ sửa bộ sinh).

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| pytest `test_payload_samples_valid_schema` | 500 mẫu từ `SAMPLE_ONLY` | Hợp lệ JSON schema v2; thời gian chơi ở tương lai; mỗi ô của bảng trộn có mặt | — |
| pytest `test_modes_config` | Đọc `modes.json` | Thời lượng/tốc độ đúng bảng trên; nhanh không có LT-05 | — |
| pytest `test_requests_jsonl_to_d2` | JSONL mẫu | Cột D2 timeseries đúng DATASET_SCHEMA; dòng warm-up bị loại | — |
| Smoke `EVAL_STEPS=b` (nhanh) | Stack đánh giá | LT-01, LT-02 chạy xong; 0 response 400/5xx (trừ 429/503 = 0 vì rate limit nâng); Prometheus có `k6_*` và `tadss_decision_duration_*` trong cửa sổ chạy | LT-01, LT-02 |

**Nghiệm thu:** pytest xanh, smoke đạt, ghi số request/RPS thực tế của lượt nhanh vào báo cáo.

> **Ghi chú triển khai (08/10/2026):** k6 ghi console dạng logfmt nên evaluator đổi thành `requests.jsonl`; số liệu lượt nhanh ở báo cáo mục 3i.

---

### Task 9: Sự cố dưới tải LT-03, OB-3 và SCALE-2 — bước (c)

**Files:**
- Create:
  - `tools/evaluation/evaluator/clients/toxiproxy.py`, `faults.py` (ghi file profile O-17 kiểu tạm + đổi tên), `grafana.py` (annotation, snapshot JSON; không ảnh).
  - `tools/evaluation/evaluator/chaos_schedule.py` (bảng lịch), `steps/incident_step.py`, `steps/scale_step.py`.
- Test: pytest `test_chaos_schedule.py`, `test_chaos_log.py`, `test_scale_checks.py`.

**Interfaces:**
- `chaos_log` (D3) `raw/chaos/chaos_log.csv`: `fi_id`, `run_id`, `toxic` (loại + tham số), `started_at_utc`, `ended_at_utc`, `expected`, `observed`, `passed`, `invariant_violations`.
- Mỗi sự kiện bật/tắt tạo annotation Grafana tag `chaos`, `fi_id`.

**Yêu cầu:**
1. **Lịch LT-03 (O-16).** Thời điểm tính từ lúc k6 bắt đầu; bản nhanh co lại:

   | Pha | Bản đầy đủ | Bản nhanh | Lỗi |
   | :--- | :--- | :--- | :--- |
   | Nền | 0:00–2:00 | 0:00–0:30 | — |
   | FI-02 | 2:00–4:00 | 0:30–1:00 | file profile `Delay`, `DelayMs = 2000` |
   | Hồi phục | 4:00–5:30 | 1:00–2:00 | profile `None` |
   | FI-03 | 5:30–7:00 | 2:00–2:30 | profile `Throw` |
   | Hồi phục | 7:00–8:30 | 2:30–3:30 | profile `None` |
   | FI-08 | 8:30–9:30 | 3:30–4:00 | Toxiproxy tắt proxy `postgres` |
   | Hồi phục | 9:30–11:00 | 4:00–5:00 | bật lại |
   | FI-07 | 11:00–12:30 | 5:00–5:30 | toxic latency 200 ms trên `redis` |
   | Hồi phục | 12:30–14:00 | 5:30–6:45 | gỡ toxic |

   - Pha hồi phục của bản nhanh dài 60–75 s theo quy tắc O-16; bản cũ dùng 30 s, ngắn hơn thời gian breaker strategy cần để đóng lại (15 s + độ trễ metric + cửa sổ sạch ≈ 45 s).
   - Nếu S-6 cho thấy file profile mất tới N giây mới có hiệu lực, cộng N vào đầu mỗi pha lỗi strategy và ghi thời điểm hiệu lực thật vào `chaos_log`.

   - Cuối lượt: kiểm mọi proxy bật, không còn toxic, profile `None`.
   - Lỗi bất kỳ khi dọn ⇒ `error`, không để stack ở trạng thái lỗi.
2. **`expected`/`observed` mỗi pha:**
   - FI-02/FI-03: response trong pha là `APPROVED` cọc 100% `FALLBACK` với reason tương ứng (từ D2). Thời gian pipeline của bản `FALLBACK` ≤ 500 ms (từ D1 `execution_time_ms`). Tỉ lệ endpoint ≤ 700 ms ≥ 99 % (bucket).
   - FI-08: 503 `PERSISTENCE_UNAVAILABLE`; không bản ghi dở dang (số 200 = số `PRIMARY` mới trong pha).
   - FI-07: không có `FALLBACK` do cache; bypass > 0.
   - Hồi phục theo định nghĩa O-16.
   - `invariant_violations`: số khóa có > 1 bản `PRIMARY` + số 200 không có bản ghi.
3. **Bằng chứng (OB-3):**
   - Sau LT-03 lưu `grafana/dashboard-snapshot.json` (JSON dashboard + dữ liệu các panel cho cửa sổ LT-03).
   - Không lưu ảnh PNG của dashboard (quyết định người dùng); trực quan hóa là biểu đồ vẽ từ metric đã thu (bước e) và JSON snapshot.
   - `raw/metrics/lt03-*.json`: kết quả `query_range` của các biểu thức dùng cho biểu đồ dòng thời gian (bước e).
4. **SCALE-2 (O-18)** qua `EVAL_PROXY_URL`, kiểm bằng D2 + truy vấn PostgreSQL + Prometheus theo nhãn instance của S-2. Kết quả `raw/scale/scale2-checks.json`.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| pytest `test_schedule_ordered_non_overlapping` | Cả hai chế độ | Pha liên tiếp, không chồng; mỗi lỗi có pha hồi phục ngay sau | — |
| pytest `test_recovery_phases_cover_required_time` | Cả hai chế độ, cấu hình đánh giá trong compose (BreakDuration, export interval) | Mỗi pha hồi phục ≥ độ dài tối thiểu của O-16 cho đúng loại lỗi; đổi cấu hình làm thiếu thì test đỏ | — |
| pytest `test_phase_evaluation_rules` | D2/D1 mẫu cho từng pha (đạt và không đạt) | `passed` đúng quy tắc; `invariant_violations` đếm đúng | — |
| pytest `test_cleanup_always_runs` | Client giả ném lỗi giữa lịch | Hàm dọn vẫn được gọi; bước trả `error` | — |
| pytest `test_scale2_checks` | D2 có khóa lặp, kết quả DB và Prometheus giả | Đạt khi đủ ba điều kiện O-18; thiếu một instance → không đạt | — |
| Smoke `EVAL_STEPS=c` (nhanh) | Stack đánh giá | `chaos_log` có 4 dòng lỗi đều `passed`; annotation thấy trên Grafana; SCALE-2 đạt; snapshot JSON tồn tại | FI-02, FI-03, FI-07, FI-08, LT-03, OB-3, SCALE-2 |

**Nghiệm thu:** pytest xanh, smoke đạt; snapshot JSON mở được; mô tả trong báo cáo dòng thời gian quan sát được (độ trễ, fallback, 503, hồi phục).

---

### Task 10: Schemathesis, Stryker và coverage — bước (d)

**Files:**
- Create:
  - `.config/dotnet-tools.json` (`dotnet-stryker`, `dotnet-reportgenerator-globaltool`, phiên bản S-8).
  - `stryker/mt01-sdk.json`, `mt02-strategies.json`, `mt03-core-application.json`, `mt03-core-infrastructure.json`.
  - `tools/evaluation/evaluator/steps/contract_step.py`, `mutation_step.py`, `coverage.py`.
- Modify: `src/**`/`tests/**` chỉ khi Schemathesis tìm ra lỗi thật hoặc cần test giết mutant sống (yêu cầu 3, 4).
- Test: pytest `test_schemathesis_report.py`, `test_stryker_report.py`.

**Interfaces:**
- `raw/contract/schemathesis-report.*` (định dạng theo S-7), `raw/mutation/{mt}/mutation-report.{html,json}`, `raw/coverage/` (HTML + cobertura gộp).

**Yêu cầu:**
1. **CT-03 (O-19)** chạy trên `eval-tadss-1`. Tổng hợp theo operation: số ví dụ, số lỗi mỗi check.
2. **MT (O-20):**
   - Chạy tuần tự bốn cấu hình.
   - Score mỗi MT = (killed + timeout) / (tổng − không phủ … theo định nghĩa của Stryker, ghi rõ công thức vào báo cáo).
   - MT-03 = score gộp hai cấu hình con, tính theo số mutant.
3. **Lỗi thật từ Schemathesis:**
   - Thêm IT tái hiện (`CatalogId` CT-03) rồi sửa mã, không sửa contract.
   - Nếu contract sai ⇒ dừng và hỏi.
4. **Score < 70 %:** thêm test (đúng mã danh mục của đối tượng: UT-01/02/03/…) giết mutant sống có ý nghĩa; liệt kê mutant tương đương theo quy tắc ở Global Constraints. Không hạ ngưỡng.
5. Bản nhanh: chạy CT-03 (25 ví dụ), bỏ MT (bước ghi `skipped` cho MT), có coverage.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| pytest `test_schemathesis_verdict` | Báo cáo mẫu: không lỗi; có 5xx; có lỗi schema; chỉ lỗi `status_code_conformance` | Đạt; không; không; đạt kèm cảnh báo | — |
| pytest `test_stryker_score_and_merge` | JSON mẫu hai cấu hình MT-03 | Score từng phần và gộp đúng theo số mutant | — |
| Chạy `EVAL_STEPS=d` bản nhanh | Stack đánh giá | CT-03 đạt (0 lỗi 5xx, 0 sai schema); coverage HTML có | CT-03 |
| Chạy `EVAL_MODE=full EVAL_STEPS=d` | Stack đánh giá | CT-03 đạt với 500 ví dụ/operation; MT-01, MT-02, MT-03 mỗi cái ≥ 70 % | CT-03, MT-01…MT-03 |
| Test mới (nếu có) | Theo lỗi/mutant | Xanh | theo đối tượng |

**Nghiệm thu:** build 0/0, unit + IT + pytest xanh; điểm mutation từng MT và thời gian chạy ghi vào báo cáo (cập nhật ước tính O-24).

---

### Task 11: Evidence Pack, báo cáo và tóm tắt — bước (e)

**Files:**
- Create:
  - `tools/evidence/requirements.txt`; `tools/evidence/anonymize.py` (HMAC-SHA256 theo DATASET_SCHEMA Mục 4).
  - `tools/evidence/d1_export.py` (D1 từ database của stack: `decision_executions` ghép override/outcome, cột theo DATASET_SCHEMA D1, đặc trưng request tách từ `request_payload`).
  - `tools/evidence/evidence_pack.py` (manifest, EV-1…EV-5).
  - `tools/evidence/kpi.py` (kết luận SC1…SC8).
  - `tools/evidence/report/generator.py`, `tools/evidence/report/template.html.j2`.
  - `tools/evidence/tests/` + `fixtures/`; `tools/evaluation/evaluator/steps/evidence_step.py`.
- Test: pytest dưới `tools/evidence/tests/`.

**Interfaces:**
- `manifest.json` theo DATASET_SCHEMA 2.1 + trường:
  - `mode`, `dirty`;
  - `steps` (kết quả từng bước);
  - `kpis` (mã, kết luận, giá trị, nguồn file);
  - `catalogCoverage` (mã có test đạt / có test đỏ / chưa có test và lý do theo phase).
- Kết luận KPI:
  - giá trị: `PASS`, `FAIL`, `NOT_APPLICABLE`, `INSUFFICIENT_DATA`;
  - mỗi kết luận có `reason` và đường dẫn file thô.
- `tools/evidence` dùng được độc lập với evaluator (5.6 gọi lại để thêm D4/D5 vào cùng pack).

**Yêu cầu:**
1. **Quy tắc kết luận KPI:**

   | KPI | Dữ liệu | Đạt khi | Ngoài phạm vi lượt này |
   | :--- | :--- | :--- | :--- |
   | SC1 | LT-01: tỉ lệ `tadss_decision_duration` `POST api/v2/decisions` ≤ 150 ms (bucket) trong cửa sổ đo sau warm-up; p95 phía client (D2) | Tỉ lệ ≥ 95 % ở **mọi** lần lặp **và** p95 phía client < 150 ms. Bucket đếm "≤ 150" còn KPI viết "< 150": báo cáo nêu rõ điều này, và k6 (chính xác từng request) là phép kiểm "< 150". Báo cáo kèm p95 ước lượng và khoảng tin cậy 95 % bootstrap trên các lần lặp (bản đầy đủ) | Phần E2E (LT-04) → `NOT_APPLICABLE` (Phase 6) |
   | SC2 | FI-02/03/04 trong test bước a + LT-03 pha FI-02/FI-03 | 100 % response trong pha là fallback `APPROVED` cọc 100 %; mọi bản `FALLBACK` có `execution_time_ms` ≤ 500; tỉ lệ endpoint ≤ 700 ms ≥ 99 % | — |
   | SC3 | D2 + D1 mọi lượt tải | Số khóa idempotency có response 200 = số bản `PRIMARY` mang các khóa đó; IT-05 đạt | — |
   | SC4 | IT-04, IT-07 + SCALE-1/2 | Đều đạt; 0 khóa có > 1 bản `PRIMARY` | — |
   | SC5, SC6 | — | — | `NOT_APPLICABLE` (SmashCourt-BE, Phase 6) |
   | SC7 | — | — | `NOT_APPLICABLE` (PT-01, gói 5.6) |
   | SC8 | Bước a + MT | 100 % test đạt và mọi MT ≥ 70 % | Bản nhanh: `INSUFFICIENT_DATA` (không chạy MT) |

2. **EV-1…EV-5** chạy trước khi sinh báo cáo; không đạt → báo cáo ghi rõ ở đầu trang "lượt chạy không dùng làm minh chứng".
3. **`report.html`:** tự chứa (nhúng thư viện biểu đồ), tiếng Việt. Gồm:
   - đầu trang: `run_id`, chế độ, commit + dirty, máy;
   - bảng KPI;
   - độ phủ danh mục theo nhóm mã;
   - LT (bảng p50/p95/p99/max, số mẫu, biểu đồ theo thời gian, điểm gãy LT-02);
   - dòng thời gian LT-03 (độ trễ p99, tỉ lệ fallback, 503, trạng thái breaker, vùng tô màu theo `chaos_log`);
   - SCALE-2; CT-03; MT; coverage; EV-1…EV-5;
   - giới hạn: máy đơn; môi trường `Testing`; rate limit nâng; trace lấy mẫu 10 % (metric và audit không lấy mẫu nên KPI không bị ảnh hưởng, nhưng không phải request nào cũng có trace); bản nhanh chỉ để diễn tập; dữ liệu tải tổng hợp; cửa sổ đo theo metric lệch tối đa một `MetricExportIntervalMs` ở hai đầu (số liệu k6 phía client là đối chứng chính xác theo từng request).
4. **Bảng tóm tắt stdout** (O-14) và mã thoát.
5. Không file nào trong pack chứa: khóa API thật, chuỗi kết nối có mật khẩu, định danh chưa ẩn danh (EV-4 kiểm cột, cộng một lượt quét chuỗi mẫu bí mật đã biết của lượt chạy).

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| pytest `test_anonymize_deterministic_within_run` | Cùng khóa, cùng định danh; hai khóa khác | Cùng mã trong lượt; khác lượt khác mã; không còn định danh gốc | — |
| pytest `test_d1_columns` | Bản ghi mẫu (PRIMARY/SHADOW, có/không override, outcome) | Đủ cột D1; trường tùy chọn để trống | — |
| pytest `test_ev_checks_Theory` | Pack mẫu đạt; sửa 1 byte; thiếu correlation trong D1; lệch số 200 và `PRIMARY`; có cột `email`; thiếu commit | EV-1…EV-5 lần lượt đạt/không đạt đúng | — |
| pytest `test_kpi_rules_Theory` | Dữ liệu mẫu mỗi KPI ở ngưỡng (ví dụ 94,9 % / 95 %) | Kết luận đúng bảng; ngoài phạm vi trả đúng `NOT_APPLICABLE`/`INSUFFICIENT_DATA` kèm lý do | — |
| pytest `test_report_self_contained` | Sinh báo cáo từ pack mẫu (nhanh và đầy đủ) | Không có URL `http(s)://` tải tài nguyên; mọi mục có mặt; mục không có dữ liệu ghi lý do | — |
| Smoke: lượt nhanh trọn vẹn | `docker compose -p tadss-eval --profile evaluation run --rm --build evaluator` | Mã thoát 0 hoặc 1 (không phải 2); `evidence/EV-…-EVAL-quick-r{n}/` đủ cây O-21; EV-1…EV-5 đạt; thời gian ≤ ~25 phút | EV-1…EV-5 |

**Nghiệm thu:** pytest xanh; một lượt nhanh trọn vẹn sinh pack hợp lệ (ghi `run_id`, thời gian, KPI vào báo cáo).

---

### Task 12: Tài liệu, skill và kiểm chứng cuối gói

**Files:**
- Modify:
  - `README.md`:
    - mục "Observability": bật telemetry khi `dotnet run` (chạy riêng otel-lgtm), xem dashboard ở chế độ vận hành;
    - mục "Chế độ đánh giá": lệnh O-12, nhanh/đầy đủ, `EVAL_STEPS`, kết quả ở `evidence/`, dọn, yêu cầu tài nguyên, cảnh báo mount Docker socket;
    - bảng cấu hình `Tadss:Telemetry`; trạng thái và số test.
  - `docs/architecture/SOFTWARE_ARCHITECTURE_SPECIFICATION.md`:
    - Mục 2: cây thư mục mới;
    - Mục 3: gỡ hạn chế request logging; thêm mục con **SCALE-3**: thành phần theo instance — breaker strategy, breaker Redis, rate limit, L1, gauge hàng chờ báo trùng; thành phần dùng chung; hệ quả; hướng rate limit phân tán;
    - 4.2: dòng OpenTelemetry → đã hiện thực ở `Composition/`;
    - 4.4.3: thứ tự pipeline mới;
    - 4.5: bảng metric đầy đủ (nhãn `strategy` của fallbacks, `tadss.cache.circuit.state`, biên histogram, quy tắc đếm `tadss.decisions`), trace và log.
  - `docs/architecture/RESILIENCE_AND_FAULT_TAXONOMY.md`: FI-07, FI-09 đã có test; đổi profile tiêm lỗi nóng ở `Testing`.
  - `docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`:
    - Mục 2, 6.1: bỏ "chưa hiện thực", lệnh O-12, thời lượng đo thật;
    - Mục 3: công cụ đã chốt + phiên bản;
    - Mục 8: thêm giới hạn — môi trường `Testing`, rate limit nâng, trace lấy mẫu 10 %, tài nguyên VM Docker, histogram chỉ chính xác tại biên bucket (và cửa sổ thời gian lệch ≤ một chu kỳ xuất metric), mount socket.
  - `docs/research/DATASET_SCHEMA_SPECIFICATION.md`: bỏ "chưa hiện thực"; `run_id`, manifest thêm trường O-21.
  - `spec/PHASE5_DESIGN_SPEC.md`: D-12 (lệnh O-12, thời lượng đo thật), 4.5 (vị trí OTel, đường dẫn Stryker) — không stage.
  - `docs/planning/BACKLOG_SPRINTS.md` (T5.14–T5.17, T5.26 phần SV1), `docs/planning/MASTER_EXECUTION_PLAN.md` (dòng W06–W07 nếu cần).
  - `TADSS-Runtime/CLAUDE.md`: thư mục mới, lệnh chạy đánh giá.
  - `../.claude/skills/tadss-architecture/SKILL.md` (ngoài repo): hàng "metric mới → `TadssMetricNames` + `TadssMetrics` + panel dashboard + OB-1"; "span → `TadssActivitySource`"; "OpenTelemetry chỉ ở `Composition/`"; "service compose đánh giá → luật O-13"; skill `build-verify` thêm pytest nếu sửa `tools/evaluation`, `tools/evidence`.

**Yêu cầu:**
1. Tài liệu chỉ ghi điều đã hiện thực, sửa ở file sở hữu. Kiểm link nội bộ mọi `.md` đã sửa.
2. **Kiểm chứng cuối gói** (số liệu thật vào báo cáo):
   - `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror --no-incremental` → 0/0.
   - Unit, IT xanh (IT chạy 3 lần liên tiếp không flaky, ghi thời gian); pytest xanh. So với đầu gói (1707 / 211), liệt kê test mới theo mã.
   - `git diff cad5150 -- docs/contracts/` rỗng.
   - `dotnet ef migrations list --no-connect` đúng 4 ID; `has-pending-model-changes` không có thay đổi.
   - `tools/ci/check-vulnerable-packages.sh` sạch; `verify-expose.sh` và `verify-observability.sh` đạt.
   - Một lượt **bản nhanh** trọn vẹn từ trạng thái sạch (sau `down -v`): mã thoát, thời gian, `run_id`, KPI.
   - Lượt `EVAL_MODE=full EVAL_STEPS=d` đã chạy ở Task 10 (MT ≥ 70 %).
   - grep: literal `"tadss.` ngoài `TadssMetricNames`/`TadssActivitySource` (rỗng); `OpenTelemetry` ngoài `Composition/`/`Program.cs` trong `src/` (rỗng); `appsettings.Development.json` trong `evidence/` (rỗng).
   - **Dọn dẹp cuối gói (yêu cầu của người dùng, 07/10/2026):** sau khi xong, Docker chỉ còn thứ cần thiết để duy trì:
     - Xóa mọi container, volume, network, build cache do spike, test và lượt đánh giá tạo ra (`down -v` project `tadss-eval`, `image prune`, `builder prune`).
     - Giữ lại **chỉ image của chế độ vận hành** (`tadss-runtime`, `ngrok`, `otel-lgtm`) nếu người dùng đang chạy stack đó; image của chế độ đánh giá (k6, SDK, evaluator…) và image test (postgres, redis, toxiproxy, ryuk) được xóa vì sẽ tự tải lại khi cần.
     - Ghi vào báo cáo: danh sách còn lại và dung lượng (`docker system df`), lệnh dọn lại sau mỗi lượt.
   - README có mục **"Lệnh Docker thường dùng"** (chạy vận hành, chạy đánh giá, xem log, dừng, dọn dẹp) để người dùng không phải nhớ.
3. **Báo cáo gói (`spec/PHASE5_REPORT_5.5.md`)** gồm:
   - Phụ lục A có cột trạng thái.
   - Bảng spike S-1…S-10.
   - Bảng 6.1 cho dòng OB-1, OB-4 (và OB-2, OB-3, OB-5, SCALE-2).
   - Phiên bản công cụ đã ghim.
   - Thời lượng đo thật (O-24).
   - Việc của người dùng.
   - Mục chuyển sang 5.6/5.7.

**Nghiệm thu:**
- Mọi mục kiểm chứng đạt; tài liệu khớp mã.
- Commit đề xuất Phần B: `feat(evaluation): one-command evaluation mode with load, chaos and evidence report (5.5 part B)`.
- Đề xuất PR vào `develop`.

---

## Phụ lục A — Đối chiếu spec → task

| Yêu cầu (spec 4.5 và liên quan) | Task |
| :--- | :---: |
| OpenTelemetry trace/metric/log, tắt được, không có collector vẫn chạy | 2, 4 |
| Metric SAS 4.5 trên Meter `TADSS.Runtime`, khai báo một nơi | 2, 3 |
| Npgsql 8.0.x, span Redis tự tạo, Serilog sink OTLP | 4 |
| Dashboard bắt buộc, panel tối thiểu, provisioning ở cả hai chế độ Docker | 5, 7 |
| OB-1 (test), OB-2 (API Grafana), OB-3 (Evidence Pack sau sự cố), OB-4 (chế độ vận hành) | 5, 7, 9 |
| Lưu số liệu trong otel-lgtm (không Prometheus riêng); k6 đẩy metric vào cùng Prometheus (chốt output) | 1, 8 |
| Profile `evaluation`, stack cô lập, không chạm Neon | 7 |
| Evaluator (a) test gộp theo `CatalogId` | 7 |
| (b) k6 LT-01/02/05 | 8 |
| (c) LT-03 + FI qua Toxiproxy, quan sát trên dashboard, `chaos_log` | 6, 9 |
| (d) Schemathesis CT-03, Stryker MT-01…03 + ReportGenerator | 10 |
| (e) `evidence/<run_id>/`: `report.html`, dữ liệu thô, `manifest.json`, EV-1…EV-5 | 11 |
| Bản nhanh / bản đầy đủ | 8–12 |
| SCALE-2 (hai replica sau reverse proxy) | 7, 9 |
| SCALE-3 (tài liệu) | 1, 12 |
| FI-01…FI-10 tự động, có trong CI | 6 |
| Mang từ 5.4: gauge breaker, request logging, exporter, LT-03 dùng decorator, FI-07 | 2, 3, 4, 6, 9 |
| 6.1: dashboard chỉ tham chiếu metric có thật; chạy Docker là thấy số liệu | 5 |
| 9: README/CLAUDE.md/methodology/dataset/kế hoạch bỏ chú thích "chưa hiện thực" | 12 |

Không thuộc 5.5: replay 215 booking, adapter khách sạn, notebook (5.6); lượt đầy đủ chính thức, đóng băng v2, DOC-1…4 (5.7); LT-04, FI-11…14, SC5/SC6 (Phase 6).

## Phụ lục B — Thay đổi cấu hình và hành vi cần duyệt

| # | Thay đổi | Nơi ghi |
| :--- | :--- | :--- |
| B-1 | Section mới `Tadss:Telemetry` (mặc định tắt); compose vận hành bật telemetry tới `grafana` | README, `appsettings.json`, compose |
| B-2 | Thứ tự middleware: request logging và metrics ra ngoài exception handler; log hoàn tất đúng status thật và có `ClientId` | SAS 2, 4.4.3 |
| B-3 | Compose vận hành: `grafana` mount chỉ đọc thư mục dashboard/provisioning; luật NG-1 "không mount, không biến secret" thu hẹp về `tadss-runtime` | README, `verify-expose.sh`, test compose |
| B-4 | Thêm profile `evaluation` (8–9 service, cổng `127.0.0.1:3002`) vào `docker-compose.yml`; khóa API đánh giá là hằng không bí mật | README, methodology 6.1 |
| B-5 | Lệnh chế độ đánh giá: `docker compose -p tadss-eval --profile evaluation run --rm --build evaluator` thay cho `docker compose --profile evaluation up` của D-12 | D-12, README, methodology, CLAUDE.md |
| B-6 | Evaluator mount `/var/run/docker.sock` (Testcontainers trong container) | README (cảnh báo), methodology Mục 8 |
| B-7 | Ở môi trường `Testing`, profile tiêm lỗi (`Profile`, `DelayMs`) đổi nóng qua file JSON do `TADSS_FAULT_INJECTION_FILE` chỉ định | RESILIENCE_AND_FAULT_TAXONOMY, README |
| B-8 | Metric mới/khác SAS: nhãn `strategy` cho `tadss.fallbacks`, `tadss.cache.circuit.state`; `tadss.decisions` chỉ đếm bản `PRIMARY` mới | SAS 4.5 |
| B-9 | Port mới `IDecisionQueryStore.CountPendingApprovalsByBranchAsync` (nội bộ, không đổi wire/DB) | SAS 4.2 |
| B-10 | Phạm vi Stryker theo cây sau 5.2d; bản nhanh không chạy Stryker | spec 4.5, methodology 4.4 |
| B-11 | Bỏ `OpenTelemetry.Instrumentation.Http` khỏi danh sách package của spec | spec 4.5 |
| B-12 | Thời lượng bản đầy đủ có thể vượt 1,5–2 giờ; cập nhật theo số đo | D-12, methodology 6.1 |

Không đổi: wire (OpenAPI, JSON schema), SDK và baseline public API, mã lỗi, status HTTP, schema DB, khóa cache, hành vi quyết định.

## Ghi chú cho gói 5.6

- `tools/evidence/` (anonymize, D1 export, evidence pack, KPI, bộ sinh báo cáo) là nền cho 5.6:
  - thêm `sql/export_smashcourt_bookings.sql`, `replay.py` (D4), `hotel_adapter.py` (D5);
  - thêm mục RP/PT vào báo cáo và đưa SC7 về trạng thái tính được.
- Replay/adapter gửi request vào stack đánh giá qua cùng khóa API đánh giá. Ngưỡng tiền tệ của strategy chỉnh bằng biến môi trường của `eval-tadss-*` (không sửa mã — SC7).
- Notebook `notebooks/evidence_report.ipynb` nên gọi `tools/evidence/report/generator.py` thay vì viết lại biểu đồ.
- Lượt đầy đủ chính thức (5.7) chốt Evidence Pack vào `docs/research/evidence-packs/{run_id}/` theo O-21.
