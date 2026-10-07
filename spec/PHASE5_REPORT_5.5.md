# Báo cáo gói 5.5 — Observability & chế độ đánh giá (Phần A xong: Task 1–5, đã commit; Task 6–8 xong)

Trạng thái: **Task 1 và Task 2 xong**, các thay đổi **chưa commit** (để người dùng review); không push. Task 3–5 (Phần A) và Task 6–12 (Phần B) chưa làm. Nhánh `feat/phase-5-contract-v2-test-infra`, HEAD `cad5150`.

## 1. Task 1 — Spike S-1…S-10 (kết quả thật, 06/10/2026)

Mọi spike chạy trong thư mục tạm ngoài repo, đã xóa container, network, volume nháp. Image đã tải được giữ lại làm cache (xem Mục 5).

| Mã | Câu hỏi | Kết quả | Quyết định |
| :--- | :--- | :--- | :--- |
| S-1 | otel-lgtm 0.34.0: đường dẫn provisioning, UID datasource, cổng Prometheus, API Grafana không đăng nhập, renderer | **Đạt.** Provisioning dashboard: `/otel-lgtm/grafana/conf/provisioning/dashboards/` (hiện có `grafana-dashboards.yaml`, `sample.yaml`). Datasource UID cố định: `prometheus`, `tempo`, `loki`, `pyroscope`. Prometheus bật sẵn `--web.enable-remote-write-receiver` và `--web.enable-otlp-receiver`, cổng 9090 trong container (truy cập qua proxy datasource của Grafana). API Grafana (`/api/health`, `/api/search`, `/render/...`) dùng được **không cần đăng nhập**. Renderer: `grafana/grafana-image-renderer:v5.12.5` (cùng digest với `latest`) xuất PNG 1000×500 thành công; Grafana **bắt buộc** đặt cặp token (`AUTH_TOKEN` của renderer ↔ `GF_RENDERING_RENDERER_TOKEN` của Grafana), không có thì Grafana thoát | Giữ nguyên. OB-3 dùng được ảnh PNG; thêm cặp token hằng không bí mật vào compose đánh giá (Task 7). Image renderer ~2 GB |
| S-2 | Tên metric và nhãn sau khi OTLP → Prometheus | **Đạt**, đã gửi metric thật từ ứng dụng .NET 8: counter `tadss.decisions` → `tadss_decisions_total`; histogram đơn vị `ms` → `tadss_decision_duration_milliseconds_{bucket,count,sum}`; gauge giữ nguyên `tadss_circuit_state`. Nhãn resource: `job` = `service.name`, `instance` = `service.instance.id`, kèm `service_instance_id`, `service_name`, `service_version`, `deployment_environment_name`. Biên bucket tùy chỉnh (View) hoạt động: truy vấn `…_bucket{le="150"}` ra đúng | Quy tắc đổi tên của O-10 xác nhận đúng; O-04 cập nhật nhãn thật; dashboard dùng `instance` |
| S-3 | k6 xuất metric vào collector | **Đạt** với k6 **2.3.0**: `--out opentelemetry`, biến `K6_OTEL_GRPC_EXPORTER_ENDPOINT`, `K6_OTEL_GRPC_EXPORTER_INSECURE=true`, `K6_OTEL_EXPORT_INTERVAL`. Mặc định metric **không có tiền tố** (`http_reqs_total`); đặt `K6_OTEL_METRIC_PREFIX=k6_` thì ra `k6_http_reqs_total`, `k6_http_req_duration_milliseconds_bucket`… Tag của kịch bản (`scenario`) thành nhãn; có thêm nhãn `name`/`url` theo URL (cố định nên không bùng nổ nhãn) | Dùng OTLP của k6, đặt tiền tố `k6_`; không cần dự phòng remote-write (vẫn sẵn sàng vì Prometheus bật nhận remote-write) |
| S-4 | Testcontainers chạy trong container (Docker Desktop Windows) | **Đạt.** Container `mcr.microsoft.com/dotnet/sdk:8.0` mount `/var/run/docker.sock`, `--add-host host.docker.internal:host-gateway`, `TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal`; copy mã nguồn loại trừ `appsettings.Development.json`, `.env*`, `bin/obj`; `dotnet restore --locked-mode` + 4 integration test (`IdempotencyIntegrationTests`) **4/4 đạt** (16 s). Thư mục làm việc không có file secret; không sót container Testcontainers | Giữ đúng O-14 (mount socket). Không cần dừng/hỏi |
| S-5 | `compose run` so với `up` với profile | **Đạt, và xác nhận lỗ hổng của lệnh D-12.** Với `up --profile evaluation`, service **không có profile** (giả lập `tadss-runtime`) cũng bị khởi động (nó báo lỗi chỉ vì mạng external giả lập không tồn tại; trên máy người dùng mạng `smashcourt-network` có thật nên nó sẽ chạy và nối Neon). Với `-p … --profile evaluation run --rm evaluator` chỉ `eval-db` (phụ thuộc) và `evaluator` chạy; **không** đòi mạng external; sau khi evaluator thoát stack phụ thuộc vẫn chạy | Giữ O-12 |
| S-6 | Đổi file JSON ở container A, `reloadOnChange` ở container B qua named volume | **Đạt mà không cần polling.** Ghi file tạm rồi `mv` (nguyên tử): cả 3 lần đổi đều được nhận trước lần ghi kế tiếp (cách nhau ~5 s). Chế độ polling (`DOTNET_USE_POLLING_FILE_WATCHER=true`) còn **bỏ lỡ** một lần đổi (gộp) nên kém hơn | **Không bật polling** (đổi dự phòng của plan). Độ trễ chính xác sẽ đo ở IT của Task 6 |
| S-7 | CLI Schemathesis | **Đạt** với 4.29.3 (pip, Python 3.13): `st run <openapi> -u <url> -H "X-Api-Key: …" -c not_a_server_error,response_schema_conformance,status_code_conformance,content_type_conformance -n <số ví dụ> --report junit,ndjson --report-dir …`; có `--report-json-path`, `--seed`. Chưa chạy với TADSS thật (làm ở Task 10) | Ghi CLI vào O-19 khi hiện thực |
| S-8 | Stryker | **Chạy được.** `dotnet-stryker` **5.0.0**, `dotnet-reportgenerator-globaltool` **5.5.11** (tool cục bộ). Thử trên `TADSS.Strategies.Default` với `TADSS.UnitTests`: 74 mutant, tổng **3 phút 36 giây** (build 30 s, chạy test ban đầu 33 s, ~2 phút cho 57 mutant). **Điểm mutation hiện tại: 56,90 % (< 70 %)**: 33 giết, 24 sống, 1 không phủ; mutant sống chủ yếu là "String mutation" ở thông điệp `Reasons` (16) và `DefaultStrategyOptions` (9) | **Cảnh báo cho Task 10:** MT-02 chưa đạt 70 % với bộ test hiện có; cần thêm test khẳng định nội dung `Reasons`/giá trị mặc định (đúng mục đích Stryker). Ước tính thời lượng O-24 giữ nguyên |
| S-9 | `verify-expose.sh` chạy trong container evaluator | **Đạt.** Trong container `docker:cli` (bổ sung `bash coreutils tar curl openssl`) với mount socket + bản copy mã nguồn: NG-1, NG-2a, NG-2b, NG-3, NG-5, SEC-6 đều đạt ("TẤT CẢ ĐẠT"); script tự dọn image/network | Evaluator chạy được `verify-expose.sh`; image evaluator cần `openssl`, `bash`, `coreutils`, `curl`, `tar` |
| S-10 | Phiên bản OpenTelemetry tương thích .NET 8, quét lỗ hổng | **Có điểm cần người dùng biết.** Bộ phiên bản cuối dùng `Microsoft.Extensions.*` 8.0.x: **1.9.0** (và 1.8.x); **1.10–1.13 kéo `Microsoft.Extensions.Hosting.Abstractions` 9.0.0**. Nhưng mọi bản ≤ 1.15.2 dính hai advisory mức **Medium**: GHSA-g94r-2vxg-569j (cấp phát quá mức khi phân tích header baggage/B3/Jaeger → DoS) và GHSA-4625-4j76-fww9 (đường dẫn tạm của retry đĩa OTLP); cả hai vá ở **1.15.3**. Bộ **1.15.3** (Hosting 1.15.3, OTLP 1.15.3, ASP.NET Core instrumentation 1.15.2, `Npgsql.OpenTelemetry` 8.0.6, `Serilog.Sinks.OpenTelemetry` 4.1.1) restore **không còn cảnh báo**, giữ `Microsoft.Extensions.*` 8.0.x cho nhóm net8.0 nhưng kéo **`System.Diagnostics.DiagnosticSource` 10.0.0**; ứng dụng .NET 8 mẫu gửi metric/trace thành công với bộ này | Chọn **1.15.3** (không có High/Critical, không có advisory). Rủi ro còn lại là `DiagnosticSource` 10.0.0 cùng các project test; kiểm lại bằng toàn bộ test ở Task 4 và `check-vulnerable-packages.sh` |

Phiên bản chốt cho các công cụ: k6 2.3.0; Schemathesis 4.29.3; dotnet-stryker 5.0.0; dotnet-reportgenerator-globaltool 5.5.11; `grafana/grafana-image-renderer:v5.12.5`; `grafana/otel-lgtm:0.34.0` (đã ghim); `docker:cli` (digest `sha256:b1805116…`; ghim tag cụ thể ở Task 7); nginx: chốt và thử ở Task 7 (ứng viên `1.29.8-alpine`).

Sửa tài liệu của Task 1: EXPERIMENTAL_DESIGN (mục 4.13 thêm SCALE-2, SCALE-3; mục mới 4.14 OB-1…OB-5; lệnh E-EVAL theo O-12), DATASET_SCHEMA (`run_id` của lượt đánh giá, trường manifest mới), `spec/PHASE5_DESIGN_SPEC.md` (ghi chú điều chỉnh dưới D-12), plan 5.5 (phiên bản chốt, nhãn Prometheus, kết quả Task 1).

## 2. Task 2 — Bộ metric còn lại, `TelemetryOptions`, gauge hàng chờ

Đã làm (O-03, O-05, O-06):
- `TadssMetricNames` (Application/Common): nơi duy nhất khai báo tên meter, 13 instrument, tên nhãn, giá trị nhãn, đơn vị, `AllInstrumentNames`, biên histogram `[5,10,25,50,75,100,150,200,300,500,700,1000,2500,5000]` ms. Hằng cũ trong `TadssMetrics` được chuyển sang đây (đã đổi mọi chỗ dùng).
- `TadssMetrics`: thêm `tadss.decisions`, `tadss.decision.duration`, `tadss.pipeline.duration`, `tadss.fallbacks`, `tadss.overrides`, các gauge `tadss.circuit.state`, `tadss.cache.circuit.state`, `tadss.pending_approvals` (đọc từ nguồn đăng ký/ảnh chụp, callback không gọi I/O). `CircuitStateValue` (0 đóng, 1 nửa mở, 2 mở, 3 cô lập).
- `DecisionService` ghi `tadss.decisions` đúng một lần cho mỗi bản `PRIMARY` mới của chính request; `OverrideService` ghi `tadss.overrides` cho override mới (không ghi replay/thua tranh chấp).
- `StrategyPipeline`: ghi `tadss.pipeline.duration` (`automated`/`fallback`/`timeout`; hủy của caller không ghi), `tadss.fallbacks` (đúng một lần mỗi quyết định fallback, kèm `reason` và `strategy`), và đăng ký gauge breaker theo từng strategy qua `CircuitBreakerStateProvider`; Redis cache đăng ký gauge breaker Redis (không phát khi `Provider = Memory`).
- Port mới `IDecisionQueryStore.CountPendingApprovalsByBranchAsync` + `PendingApprovalsQuery.CountByBranchAsync` (một truy vấn gộp, cùng điều kiện lọc với truy vấn trang; điều kiện lọc được tách thành một hằng dùng chung cho cả ba câu SQL).
- `PendingApprovalsMetricRefresher` (worker nền, dùng `PeriodicTimer` + `TimeProvider`): chi nhánh hết đơn báo 0, lỗi truy vấn giữ giá trị cũ và chỉ log kiểu exception, tắt khi `Telemetry.Enabled = false`.
- `TelemetryOptions` + validator + enum giao thức; section `Tadss:Telemetry` trong `appsettings.json` (mặc định tắt); đăng ký `ValidateOnStart`.

### Lỗi thật phát hiện nhờ SCALE-1 và đã sửa

Sau khi gắn gauge breaker, `MultiInstanceTests.SameIdempotencyKey_50ParallelAcrossTwoHosts_SingleRecordSameResponse` (SCALE-1) đỏ **4/4 lần** (trước đó xanh 3/3 lần ở gói 5.4): một số request trả 500 `InvalidOperationException: This instance of 'CircuitBreakerStateProvider' is already initialized…`. Nguyên nhân: registry của Polly có thể gọi callback cấu hình **nhiều lần** khi nhiều request đầu tiên của một strategy đến cùng lúc, mà một `CircuitBreakerStateProvider` chỉ gắn được vào một breaker. Cách sửa: mỗi strategy có một mục `Lazy<PipelineEntry>` (pipeline + provider của chính nó) nên pipeline được dựng đúng một lần. Test hồi quy `StrategyPipelineConcurrencyTests` (12 strategy × 16 lượt đồng thời) khóa: mọi lượt thành công, mỗi pipeline dựng một lần, mỗi strategy một series gauge. **Lưu ý trung thực:** test đơn vị này khẳng định bất biến nhưng tôi chưa chứng minh nó đỏ với mã cũ (cuộc đua khó tái hiện tất định ở mức đơn vị); IT SCALE-1 mới là lưới bắt lỗi đã được chứng minh đỏ/xanh.

### Test

Tên test cũ được thay thế có chủ đích:
- `DecisionAuditStoreContractTests.Interface_ExposesOnlyTheExpectedMethods` và `AuditStoreInterfaces_SplitMethodsByConsumer`: kỳ vọng thêm `CountPendingApprovalsByBranchAsync` (port query mở rộng có chủ đích).
- `TadssMetricsTests` viết lại theo `TadssMetricNames` (giữ `TagValueConstants_HaveContractValues`, `Recorders_AreIsolatedPerMeterInstance`; theory counter thêm ca `tadss.overrides`).
- `MetricRecorder` mở rộng (histogram, gauge qua `ObserveGauge`) — dùng chung với IT qua compile-link như trước.

Test mới (đều gắn `CatalogId`, mã mới OB-5):
- `TadssMetricsTests` (instrument, tên, đơn vị, biên bucket, gauge); `StrategyPipelineMetricsTests` (4 ca theory + outer timeout + hủy + gauge chuyển trạng thái đóng→mở→nửa mở→đóng); `StrategyPipelineConcurrencyTests`; `DecisionServiceTests` (+4), `OverrideServiceTests` (+2); `RedisDecisionCacheTests` (+1), `StrategyDecisionCacheTests` (+2); `TelemetryOptionsValidatorTests` (theory 17 ca + biên độ dài + mặc định + nhiều lỗi); `PendingApprovalsMetricRefresherTests` (3); IT `PendingApprovalsCountTests` (3).

## 3. Kiểm chứng (số liệu thật)

| Kiểm chứng | Kết quả |
| :--- | :--- |
| `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror` | 0 Warning, 0 Error |
| Unit (`TADSS.UnitTests`) | **1756 passed** (đầu gói 1707: +49), 0 failed, 0 skipped; chạy 3 lần liên tiếp đều xanh |
| Integration (Docker/Testcontainers) | **214 test** (đầu gói 211: +3 `PendingApprovalsCountTests`). Sau khi sửa lỗi pipeline ở Mục 2: **8/9 lượt chạy toàn bộ xanh 214/214** (1 m 3 s … 1 m 22 s); **1 lượt có đúng 1 test đỏ** (`OverrideEndpointTests.Override_TwentyConcurrentSameKeyAndPayload_AllSucceedIdentically`, thất bại sau 239 ms; lúc đó tôi chưa lưu nguyên văn lỗi). Không tái hiện được: 0/25 lần khi chạy riêng cả lớp `OverrideEndpointTests`, 0/6 khi chạy riêng test đó, và 6 lượt chạy toàn bộ liên tiếp sau đó đều xanh (log đầy đủ đã lưu). Test này không đi qua mã tôi sửa ở đường ghi override (chỉ thêm một lệnh đếm metric sau khi ghi thành công), nên tôi chưa kết luận được nguyên nhân; xem Mục 4 |
| `dotnet ef migrations list --no-connect` (`Testing`) | đúng 4 ID (`InitialCreate`, `UpdateUniqueIndexes`, `AddCreatedAtIndex`, `DecisionPersistenceV2`); `has-pending-model-changes`: "No changes have been made to the model" |
| `git diff -- docs/contracts` | rỗng (không đổi wire) |
| grep literal `"tadss.` ngoài `TadssMetricNames` trong `src/` | còn đúng 1: tag trace `tadss.client_id` ở `ApiKeyAuthenticationMiddleware` (không phải metric; Task 4 chuyển vào `TadssActivitySource`) |
| File mới/sửa ≤ 250 dòng | đạt (UT-14 xanh) |
| `docs/contracts/*`, SDK, migration | không đổi |

## 3b. Đính chính sau rà soát (07/10/2026)

Sau khi người dùng gửi bản đánh giá bên ngoài, tôi đối chiếu lại từng nhận định với dữ liệu thật. Các điểm đã sửa hoặc làm rõ:

| # | Nhận định / phát biểu trước đó | Thực tế sau kiểm chứng | Xử lý |
| :-: | :--- | :--- | :--- |
| 1 | "Test hồi quy `StrategyPipelineConcurrencyTests` khóa được lỗi" (tôi từng ghi chưa chứng minh đỏ với mã cũ) | **Đã chứng minh:** tạm khôi phục kiểu cũ (một `CircuitBreakerStateProvider` dùng chung, không `Lazy`) thì test đỏ **5/5 lần**, đúng thông báo `already initialized…`; khôi phục bản sửa thì xanh. Tài liệu của Polly **không** nói callback có thể bị gọi nhiều lần, nên "Polly gọi callback nhiều lần" là suy luận từ lỗi quan sát được, chứng minh bằng thí nghiệm này, chưa đọc mã nguồn Polly | Đã hoàn tác thí nghiệm (so với bản sao lưu, không còn dấu vết) |
| 2 | "S-4 cleared, không còn rủi ro" | Quá lời. S-4 chỉ chạy 4 test của `IdempotencyIntegrationTests`. Các test đi qua Toxiproxy (FaultInjection, ~20 test) và toàn bộ 214 test **chưa** chạy trong container; mạng giữa container evaluator, Toxiproxy và `host.docker.internal` còn chưa được chứng minh cho nhóm này | Rủi ro còn lại, chạy toàn bộ IT trong container ở **đầu Task 7** trước khi viết tiếp; nếu đỏ do mạng thì dừng và hỏi |
| 3 | "Test xanh (1756 + 214) là tín hiệu tốt cho `DiagnosticSource` 10.0.0" | **Sai.** Repo chưa thêm package OpenTelemetry nào, nên các test xanh không nói gì về `DiagnosticSource` 10.0.0. Bằng chứng duy nhất hiện có là ứng dụng mẫu .NET 8 ngoài repo chạy được | Chỉ kiểm chứng thật ở Task 4 (toàn bộ unit + IT + `check-vulnerable-packages.sh` sau khi thêm package) |
| 4 | "Chạy 100 lần test override mất ~2 giờ" (trong bản đánh giá) | Sai: mỗi lượt lớp `OverrideEndpointTests` ~3 giây khi máy rảnh, 100 lượt ~5 phút | Đã thử so sánh baseline `cad5150` với mã hiện tại dưới tải CPU ép (6 tiến trình đốt CPU). **Thí nghiệm không hoàn thành:** mã hiện tại đạt 9/9 lượt đầu, từ lượt 10 Docker Desktop ngừng hoạt động (cả engine lẫn tiến trình, không rõ do tải hay do tắt) nên mọi lượt sau là dữ liệu vô giá trị và bị loại; baseline chưa chạy lần nào. **Chưa có kết luận** về nguyên nhân lỗi override đồng thời. Đã dừng và dọn tiến trình tải; chưa khởi động lại Docker |
| 5 | "Ước tính Stryker 20–40 phút cho MT-03" | Chỉ là ngoại suy từ ~2 giây/mutant của strategy `Default` (57 mutant); chưa đo MT-03 thật | Giữ làm ước tính, đo thật ở Task 10; nếu > 60 phút thì tách phạm vi như dự phòng S-8 |
| 6 | Lỗi nhỏ của fix `Lazy`: nếu việc dựng pipeline ném exception thì `Lazy` nhớ exception đó mãi (strategy hỏng vĩnh viễn) thay vì thử lại | Chấp nhận được: dựng pipeline chỉ ghép các lớp từ `ResilienceOptions` đã được validator kiểm tra lúc khởi động, không có I/O; chưa có test cho nhánh này | Ghi nhận, không sửa |
| 7 | Bảng lệnh Docker tôi đưa có `docker system prune -f` | **Bẫy thật:** lệnh này xóa cả mạng không có container nào gắn, tức `smashcourt-network` (mạng `external` mà compose của TADSS và SmashCourt-BE cần) → `docker compose up` báo "declared as external, but could not be found". Ngoài ra `docker image prune -a -f` xóa cả image của SmashCourt-BE/AI khi các stack đó đang tắt. Lệnh `-e` của `compose run` phải đặt **trước** tên service | Đã sửa bảng lệnh (README mục "Lệnh Docker thường dùng" có thêm cảnh báo, dùng `docker container prune`); đã sửa README đang hướng dẫn `docker compose --profile evaluation up` (lệnh nối Neon) sang lệnh `run evaluator` |
| 8 | Mẫu commit "BREAKING CHANGE: thứ tự middleware" trong bản đánh giá | Không phù hợp: đổi thứ tự middleware không đổi wire/contract; hơn nữa Task 3–5 chưa làm nên chưa có gì để ghi. Chỉ ghi vào commit những gì đã thật sự có ở Task 5 | Viết commit message sau Task 5 |

## 3c. Việc tồn (cập nhật 07/10/2026: mục 1 và 3 đã xử lý ở 3d, 3e)

- Nguyên nhân lỗi `Override_TwentyConcurrentSameKeyAndPayload_AllSucceedIdentically` (1/9 lượt): **chưa kết luận**; thí nghiệm so sánh với baseline `cad5150` bị gián đoạn vì Docker Desktop bị tắt. Việc cần làm ghi ở đầu Task 3 của plan.
- Chạy toàn bộ IT trong container evaluator (S-4 mới chạy 4 test): ghi ở đầu Task 7.
- Kiểm chứng `DiagnosticSource` 10.0.0 với package thật: ghi ở Task 4.
- Dữ liệu tạm đã dọn (thư mục tạm, container, volume, image, build cache); chỉ còn gói NuGet OpenTelemetry trong cache toàn cục vì Task 4 dùng lại.

## 3d. Task 3 — Request logging đúng status và metric thời gian request (07/10/2026; xong)

**Thay đổi**
- `Middlewares/RequestMetricsMiddleware.cs` (mới): ghi `tadss.decision.duration` cho `/api/*`, trong `finally`, đo bằng `TimeProvider.GetTimestamp/GetElapsedTime`. Nhãn `endpoint` = `"{METHOD} {mẫu route}"`, không có endpoint → `"{METHOD} unmatched"`.
- `Composition/RequestLoggingConfiguration.cs` (mới): `GetLevel` (status ≥ 500 hoặc exception → Error, còn lại Information), `EnrichDiagnosticContext` gắn `ClientId`; template giữ mặc định.
- `Composition/RequestPipelineExtensions.cs`: thứ tự mới O-09 (forwarded headers → correlation → **request logging → request metrics → exception handler** → security headers → https → kích thước → Swagger → API key → rate limiter → routing); XML doc cập nhật. Logger của request logging lấy từ DI (`Serilog.ILogger`, cùng đối tượng với logger của host) thay vì static `Log` để test bắt được dòng hoàn tất.
- Tài liệu: SAS (Mục 2, 3 — gỡ hạn chế "responded 500"/"không mang ClientId" —, 4.4.3 và sơ đồ), README (thứ tự middleware, mục Log), skill `tadss-architecture`.

**Hai điều plan chưa lường tới (đã xử lý)**
1. Exception handler của ASP.NET Core **xóa endpoint** của request trước khi dựng response lỗi; chỉ đọc `GetEndpoint()` thì 409/503/504 sẽ bị gắn nhãn `unmatched`. Middleware đọc thêm `IExceptionHandlerFeature.Endpoint`. Test 503/409/400/404 khóa điều này.
2. `RawText` của route là `api/v2/decisions/{decisionId:guid}` (có ràng buộc). Plan ghi nhãn `GET api/v2/decisions/{decisionId}` (khớp đường dẫn OpenAPI) nên middleware bỏ phần ràng buộc (`:guid`) bằng regex sinh sẵn.

**Test (unit 1756 → 1780, +24; IT chưa đổi)**
- `RequestLoggingTests` (12 ca): 409/404 → Information, 503 → Error, mỗi lần đúng một dòng hoàn tất, không dòng nào ghi 500; 500 thật → Error; `ClientId` có ở request đã xác thực, không có ở 401; không log nào chứa khóa/mã băm; `GetLevel` (6 ca), bộ làm giàu.
- `RequestMetricsMiddlewareTests` (12 ca): 200 POST, 200 GET theo id, 400, 409, 503, 404 (mẫu route + status cuối, đúng một số đo mỗi request); 401, 413, 429 → `POST unmatched`; `/health/live`, `/health/ready`, `/swagger/...` không được đo.
- Test thay thế: không có. `RequestPipelineTests` và `SecurityPipelineOrderTests` không khẳng định vị trí logging nên giữ nguyên và vẫn xanh.
- Build `--warnaserror` 0/0; unit 1780/1780 xanh.

**Integration test sau khi Docker bật:** toàn bộ 214 IT xanh ngay lượt đầu sau thay đổi pipeline (không test nào đổi hành vi).

**Điều tra lỗi `Override_TwentyConcurrentSameKeyAndPayload_AllSucceedIdentically` (việc tồn của Task 2)**
- 40 lượt `OverrideEndpointTests` trên mã Task 3 (Docker thật, không ép tải CPU): **40/40 xanh**, không có lỗi nào để lưu nguyên văn.
- Sau đó thêm 1 lượt toàn bộ IT (214) và 6 lượt toàn bộ IT trên mã Task 4 (220): **7/7 xanh**; riêng test này không đỏ lần nào trong tổng cộng 47 lượt (40 lượt lớp + 7 lượt toàn bộ).
- **Không so với baseline `cad5150`**: không có lỗi tái hiện nên không có gì để so sánh; chạy baseline 40 lượt (≈30 phút) chỉ cho kết quả "cũng 0 lỗi", không phân biệt được hai giả thuyết.
- **Kết luận trung thực: chưa giải thích được lần đỏ 1/9 sau Task 2 (239 ms, chưa lưu nguyên văn lỗi).** Không có bằng chứng nó do thay đổi của gói; cũng không đủ bằng chứng nó có từ trước. Ghi nhận là "chập chờn chưa tái hiện", theo dõi: nếu gặp lại, lưu nguyên văn lỗi (kiểm 503 `PERSISTENCE_UNAVAILABLE`, SqlState `40001`/`40P01`).

## 3e. Task 4 — OpenTelemetry: exporter, trace và log (07/10/2026; xong)

**Package (khớp spike S-10):** `OpenTelemetry.Extensions.Hosting` 1.15.3, `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.15.3, `OpenTelemetry.Instrumentation.AspNetCore` 1.15.2, `Npgsql.OpenTelemetry` 8.0.6, `Serilog.Sinks.OpenTelemetry` 4.1.1 (chỉ ở Runtime.Api); `OpenTelemetry.Exporter.InMemory` 1.15.3 (hai project test). Lock file của Runtime.Api và hai project test cập nhật; `dotnet restore --locked-mode` đạt.

**Kiểm chứng việc tồn S-10 (DiagnosticSource 10.0.0 với package thật):**
- `tools/ci/check-vulnerable-packages.sh`: OK, không package lỗ hổng nào (kể cả bắc cầu, 8 project).
- Build `--warnaserror` 0/0; unit 1793 xanh (3 lượt); IT 220 xanh 6/6 lượt toàn bộ. Đây là bằng chứng với package thật (trước đó 1756 + 214 chưa có package OpenTelemetry nào).

**Mã mới / đổi**
- `Application/Common/TadssActivitySource.cs`: nguồn trace, tên span và thuộc tính (một nơi); `MarkError` chỉ ghi kiểu exception. Tag `tadss.client_id` chuyển sang hằng `TagClientId` (middleware API key dùng hằng).
- Span: `tadss.decision.evaluate` (`DecisionService`, tag strategy/rule/cache_hit/status/execution_type/correlation_id), `tadss.strategy.execute` (`StrategyPipeline`: outcome, fallback_reason; outer timeout → Error + tên kiểu), `tadss.shadow.execute` (`ShadowExecutor`: gốc trace mới + `ActivityLink` tới span evaluate qua trường tùy chọn `ShadowWorkItem.RequestActivityContext`), `tadss.redis.command` (`RedisDecisionCache`: GET/SET, chỉ kết quả, không khóa).
- Composition: `TelemetryServiceCollectionExtensions.AddTadssTelemetry` (resource O-04, trace, metric + View bucket, OTLP), `SerilogTelemetryConfiguration.Apply` (sink log OTLP khi `Enabled && ExportLogs`), `TelemetrySettings` (đọc + kiểm tra bằng validator ngay khi dựng host, dựng endpoint từng tín hiệu: với HTTP/protobuf phải thêm `/v1/traces|metrics|logs` vì đặt endpoint bằng mã thì SDK không tự thêm). `AddPresentation` nhận thêm `IHostEnvironment`. `Program.cs` chỉ đổi lời gọi `UseSerilog`/`AddPresentation`.
- ASP.NET Core instrumentation: bỏ `/health/*`, `/swagger/*`, tắt `RecordException`, xóa tag `url.query`/`http.target` (không ghi query string).

**Phát hiện đáng ghi**
1. Cấu hình đọc **ngay lúc đăng ký DI** (quyết định `Enabled`) không thấy nguồn của `WebApplicationFactory.ConfigureAppConfiguration` (chỉ nạp lúc `Build()`); test dùng `UseSetting` (unit) và `EarlySettings` mới của `DecisionApiTestBase` (IT, chỉ khóa telemetry vì `UseSetting` làm hỏng khóa dạng mảng của test routing). Production không bị ảnh hưởng.
2. Test double `CapturingLogEventSink.Attach` thay `ILoggerFactory` nên `Log.Logger` tĩnh không được gán; `DiagnosticContext` (thuộc tính `ClientId` của dòng log hoàn tất) dùng logger tĩnh đó nên test `RequestLog_Authenticated_IncludesClientId` đỏ khi chạy riêng/đua với test khác (không phải lỗi mã). Đã gắn `DiagnosticContext` tường minh vào logger của sink.
3. **Test IT `Metrics_ExportedNamesAndBuckets` của plan đã thay bằng test hermetic.** Ở lượt toàn bộ IT nó đỏ 3/3 (không metric nào xuất, dù chạy riêng thì xanh): hàng trăm host trong cùng process tạo `Meter` trùng tên `TADSS.Runtime` nên SDK gộp luồng metric và vô hiệu hóa nó khi một meter bị dispose — chỉ có ở test (production có một meter). Thay bằng `TelemetryMetricViewTests` (3 test: histogram `ms` nhận đúng biên O-05 gồm 150/500/700; đơn vị khác giữ biên mặc định; chỉ `decision.duration` và `pipeline.duration` bị View chạm tới) trên `MeterProvider` thật với meter tên riêng. Phần "tên đã xuất ⊆ danh mục" do `TadssMetricsTests` khóa ở mức instrument. **Sai lệch so với plan (test case thay thế), cần bạn biết.**
4. Span Npgsql của truy vấn nền (worker gauge hàng chờ, health check) không có cha nên là trace gốc riêng; chấp nhận được, ghi ở đây để khỏi bất ngờ khi xem Tempo.

**Test (unit 1780 → 1793, +13; IT 214 → 220, +6)**
- Unit: `TelemetryRegistrationTests` (5: tắt không có provider; bật có resource 4 thuộc tính + nguồn trace; instance id rỗng = tên máy; cấu hình sai dừng host), `StrategyPipelineActivityTests` (5), `TelemetryMetricViewTests` (3); `CompositionRootBoundaryTests` mở rộng: `OpenTelemetry` và `Serilog.Sinks.OpenTelemetry` chỉ được ở `Composition/`.
- IT (`Observability/`): `PostDecision_TelemetryEnabled_ExportsSpanTree` (cây span server → evaluate → strategy, Npgsql, Redis GET/SET, correlation, client id, không query), `Telemetry_NoSecretsInSpansOrLogs`, `Logs_CarryTraceContext`, `PostDecision_Fallback_SpanMarksOutcome`, `ShadowSpan_LinksToRequest`, `PostDecision_CollectorUnreachable_ServesNormally`.
- Test thay thế: `Metrics_ExportedNamesAndBuckets` (IT) → `TelemetryMetricViewTests` (unit), lý do ở mục 3 ở trên.

## 3f. Task 5 — Dashboard, compose vận hành, kiểm chứng OB (điểm dừng Phần A; 08/10/2026; xong)

**Đã làm**
- `docker/grafana/dashboards/tadss-runtime.json` (uid `tadss-runtime`, 36 panel, 7 hàng theo O-10: tổng quan, quyết định, chịu lỗi, cache, quản trị + shadow, tải k6, log + trace), `docker/grafana/provisioning/dashboards/tadss.yaml` (thư mục "TADSS", `allowUiUpdates: false`, quét mỗi 30 s). Tiêu đề và mô tả tiếng Việt; biến `instance`; annotation `chaos`; làm mới 5 s, khoảng 30 phút. Ba panel tỉ lệ đọc thẳng từ bucket `le="150"`, `le="700"` (request) và `le="500"` (pipeline). Panel tỉ lệ hit/bypass dùng `or vector(0)` để hiện 0 thay vì trống.
- `docker-compose.yml`: `tadss-runtime` thêm `Tadss__Telemetry__Enabled: "true"` và `OtlpEndpoint: http://grafana:4317`; `grafana` mount **chỉ đọc** hai đường dẫn. Vẫn 3 service, 5 cổng loopback.
- `tools/observability/verify-observability.sh` (mới) + job CI `observability-check`; `tools/expose/verify-expose.sh` đổi NG-1 sang kiểm theo từng service (tách khối service từ `docker compose config`): `tadss-runtime` không mount/không biến secret/Development/migration `false`/gửi telemetry tới grafana; `grafana` đúng 2 mount đều chỉ đọc.
- Test: `DashboardMetricReferenceTests` (OB-1, 5 test: mọi `tadss_*` ánh xạ về instrument thật — loại và đơn vị đọc từ chính instrument, không khai báo lặp; mọi `k6_*` ∈ `K6MetricNames`; mọi instrument đều được dashboard dùng; quy tắc đổi tên S-2), `PrometheusNameMapper` + `K6MetricNames` (TestData). `ComposeFixtureAlignmentTests` viết lại theo service bằng YamlDotNet 16.3.0 (thêm `Compose_RuntimeSendsTelemetryToGrafana_GrafanaMountsOnlyDashboardsReadOnly`, `Dashboard_UidAndProvisioningPathConsistent`, theory ảnh script có thêm `verify-observability.sh`).

**Kiểm chứng bằng Docker thật (không phải chỉ test tĩnh)**
- `verify-expose.sh`: **TẤT CẢ ĐẠT** (NG-1 theo service, NG-2a, NG-2b, NG-3, NG-5, SEC-6).
- `verify-observability.sh`: **TẤT CẢ ĐẠT** — OB-2 (dashboard nạp vào thư mục TADSS) và OB-4 (20/20 POST 200, `tadss_decisions_total = 20` trên Prometheus). Lần đầu tải `grafana/otel-lgtm:0.34.0` (3,66 GB).
- Chạy thật **mọi biểu thức của 32 target** qua API Grafana trên stack sống: **0 lỗi cú pháp/datasource**; 17 có dữ liệu, 15 trống đúng như dự kiến (không có fallback, 503/504, bypass, hàng chờ, override, shadow, lỗi nạp lại cấu hình, k6 trong lượt thử). Loki: truy vấn `detected_level` hoạt động (field cấu trúc, `info` có 3 luồng); Tempo: có trace của `tadss-runtime`. Nhãn Prometheus xác nhận: `job`, `instance`, `service_instance_id`, `deployment_environment_name`…

**Sai lệch so với plan / cần biết**
1. **Mount provisioning bằng một file, không phải thư mục.** Mount cả thư mục `provisioning/dashboards` sẽ che các file provisioning có sẵn của otel-lgtm (`grafana-dashboards.yaml`, `sample.yaml`), nên `tadss.yaml` được mount riêng thành file (chỉ đọc); thư mục dashboard mount ở `/otel-lgtm/tadss-dashboards`.
2. **Script kiểm chứng dùng mạng riêng của project thử** (không dùng `smashcourt-network`): tránh trùng tên DNS `grafana`/`tadss-runtime` với stack thật đang chạy của người dùng và không đụng mạng external; không publish cổng nào ra host.
3. **Test `Dashboard_UidAndProvisioningPathConsistent` đặt ở project IT** (cùng file với test compose, cần YamlDotNet), không ở unit test.
4. **Panel k6 chưa kiểm với dữ liệu thật:** truy vấn hợp lệ về cú pháp nhưng nhãn `status` của `k6_http_reqs_total` (panel tỉ lệ lỗi) chỉ xác nhận được khi chạy k6 ở Task 8.
5. **Test thay thế (compose, Task 5):** các test quét **cả file** `Compose_DoesNotMountVolumes`, `Compose_DoesNotDeclareSecretVariable_Theory` (6 ca), `Compose_RunsDevelopmentAndForcesMigrationsOff` được thay bằng `Runtime_MountsNothingAndDeclaresNoSecretVariable`, `Runtime_RunsDevelopmentAndForcesMigrationsOff` (phân tích YAML theo service; ràng buộc áp riêng cho `tadss-runtime`); các test còn lại giữ tên, phân tích lại bằng YAML.
6. Nhận xét phụ: NG-2b của `verify-expose.sh` thấy container Staging không có khóa thoát mã **139** (không phải 1) sau 2 giây. Kiểm tra vẫn đạt (mã khác 0, log nêu `Tadss:Security`, không lộ chuỗi kết nối) nhưng 139 là tín hiệu thoát bất thường của tiến trình; không do gói này gây ra, chưa điều tra.

**Cách xem dashboard (ghi tạm, README đầy đủ ở Task 12):** `docker compose up -d --build` rồi mở `http://127.0.0.1:3001` → Dashboards → thư mục TADSS → "TADSS Runtime".

## 3g. Task 6 — Ma trận FI và đổi nóng profile tiêm lỗi (08/10/2026; xong)

**Mã**
- `FaultInjectionSnapshot` (bản chụp bất biến `Profile` + `DelayMs`), `FaultInjectionControl` (Infrastructure/FaultInjection): theo dõi `IConfiguration`, thay nguyên tham chiếu bằng `Volatile`; nạp lại sai → giữ bản cũ, log Error, `tadss.config.reload_failures{section=Tadss:Testing:FaultInjection}`; `Enabled`/`TargetStrategy` đổi lúc chạy → Information "chỉ có hiệu lực sau khi khởi động lại"; profile đổi → một dòng Warning.
- `FaultInjectionStrategyDecorator` nhận control: đọc `Current` **một lần** mỗi lời gọi, `CachePolicy` đọc profile hiện hành. Control đăng ký trong `AddStrategies` (singleton, dựng lười).
- `TestingConfigurationExtensions.AddTadssTestingConfiguration` (một dòng trong `Program.cs`): chỉ ở `Testing` và khi biến `TADSS_FAULT_INJECTION_FILE` là đường dẫn tuyệt đối trong thư mục có thật → thêm file JSON (tùy chọn, `reloadOnChange`) vào cuối.

**Hai điểm plan chưa lường tới (đã xử lý)**
1. **File JSON cụt có thể làm sập tiến trình.** Khi nạp lại lỗi, nguồn JSON ném exception ngay trên luồng theo dõi file. Nguồn được thêm với `OnLoadException = Ignore`.
2. **Bỏ qua lỗi không đủ để "giữ profile cũ".** Nguồn JSON xóa dữ liệu khi nạp lỗi, còn `Enabled` đến từ nguồn khác nên section vẫn tồn tại và binder âm thầm trả `Profile = None` — gỡ lỗi đang tiêm giữa sự cố mà không báo. Control coi "section biến mất" hoặc "khóa `Profile` từng có nay mất" là nạp lại **thất bại** (giữ bản cũ + đếm lỗi); muốn tắt lỗi phải ghi `Profile = None`. IT file cụt bắt được lỗi này ở lần chạy đầu (đỏ), sau khi sửa xanh.

**Test (unit 1793 → 1816 gồm cả Task 5; IT 220 → 222)**
- Unit: `FaultInjectionControlTests` (9: áp profile hợp lệ + 1 Warning; sai (DelayMs 0, Profile Bogus) giữ cũ + đếm; hợp lệ sau sai; 8 luồng đọc đồng thời trong 2000 lần đổi cặp (Delay,1000) ↔ (Throw,5000) không thấy cặp lai; Enabled/Target không áp; section biến mất; khóa Profile biến mất; không thay đổi thì im lặng), `TestingConfigurationExtensionsTests` (non-Testing 3 ca bỏ qua biến; đường dẫn thiếu/tương đối/thư mục lạ không thêm nguồn; thêm đúng một nguồn tùy chọn + reload + `OnLoadException` + nằm cuối + đổi file được thấy), decorator tests chuyển sang control (không đổi kỳ vọng).
- IT: `InfrastructureLatencyTests` (FI-07: Redis chậm 200 ms, timeout 150 ms, 30 POST — mọi response `AUTOMATED`, breaker mở, request trước ngưỡng ≤ 2×150+400 ms, sau ngưỡng ≤ 150+300 ms, bypass > 0; FI-09: PostgreSQL chậm 300 ms — 10 POST + 5 replay + 10 POST đồng thời cùng khóa, đúng 12 bản `PRIMARY`, mỗi request ≥ 300 ms), `FaultInjectionReloadTests` (`PostDecision_FaultFileChanged_ProfileSwitchesLive` None → Throw (≤ 10 s fallback `FALLBACK_STRATEGY_EXCEPTION`) → None (hết fallback sau thời gian nghỉ của breaker); `Reload_TruncatedJsonFile_KeepsPrevious`). `ToxiproxyControl.AddLatencyAsync` mới.
- Ma trận FI (đếm theo trait `CatalogId`): FI-01 ×1, FI-02 ×4, FI-03 ×6, FI-04 ×1, FI-05 ×8, FI-06 ×2, **FI-07 ×1 (mới)**, FI-08 ×3, **FI-09 ×1 (mới)**, FI-10 ×1 — đủ FI-01…FI-10; test FI hiện có không đổi kỳ vọng.

## 3h. Task 7 — Stack đánh giá, image evaluator và bước (a) (08/10/2026; xong)

**Bước 0 (rủi ro S-4 còn lại): toàn bộ test trong container SDK có mount socket.** Chạy `dotnet restore`/`build`/`test` của cả hai project trong `mcr.microsoft.com/dotnet/sdk:8.0` với `/var/run/docker.sock`, `host.docker.internal` và `TESTCONTAINERS_HOST_OVERRIDE`: **222/222 integration test xanh** (gồm `FaultInjection/` qua Toxiproxy và `Scale/`) trong 2 phút 53 giây; restore 45 s, build 82 s, unit 21 s. Không có lỗi mạng giữa evaluator, Toxiproxy và `host.docker.internal`, nên D-12 bước (a) khả thi. Hai phát hiện:
1. **SDK .NET 8 không đọc được `TADSS.slnx`** (định dạng slnx cần SDK ≥ 9): evaluator restore/build theo **hai project test** (kéo theo mọi project `src` qua tham chiếu) thay vì theo solution.
2. Một unit test của chính tôi (`Current_ConcurrentReload_NeverMixesFields`, Task 6) đỏ trong container vì luồng đọc có thể chưa kịp chạy khi luồng ghi đã xong (lập lịch chậm hơn trên máy ít lõi). Đã sửa: chờ mọi luồng đọc bắt đầu rồi mới ghi.

**Đã làm**
- Compose: profile `evaluation` gồm `eval-postgres`, `eval-redis`, `eval-toxiproxy` (healthcheck `/toxiproxy-cli list`; `docker/toxiproxy/toxiproxy.json` đổi upstream sang `eval-*`), `eval-tadss-1` (Testing, migrate database dùng một lần, tiêm lỗi + file `/faults/fault.json`), `eval-tadss-2` (không migrate, không tiêm lỗi, chờ instance 1 khỏe), `eval-proxy` (nginx round-robin, `docker/evaluation/nginx.conf`), `eval-grafana` (`127.0.0.1:3002`, cùng provisioning dashboard) và `evaluator`; volume `eval-nuget`, `eval-faults`. Không service nào nối `smashcourt-network`; khóa API là hằng không bí mật `tadss-evaluation-key` (chỉ lưu SHA-256); rate limit nâng 100000; telemetry 5 s, `TraceSampleRatio 0.1`. `evidence/` vào `.gitignore` và `.dockerignore`.
- `docker/evaluator/Dockerfile` (+ `Dockerfile.dockerignore`): SDK `8.0.425` (Debian) + Python 3.11 venv + k6 2.3.0 + Docker CLI/compose (`docker:29.1-cli`) + git; mọi `FROM` ghim tag; **không chứa mã nguồn** dự án. Chạy bằng **root**: cần ghi `/evidence` của người dùng và dùng `docker.sock` (quyền socket vốn tương đương root trên máy chủ nên hạ user không tăng an toàn); ghi vào README ở Task 12.
- `tools/evaluation/evaluator/`: `__main__` (CLI, mã thoát 0/1/2, bảng tóm tắt, `steps.json` kèm thông số máy, commit, cờ dirty, digest image), `run_context` (run_id `EV-{yyyyMMdd}-EVAL-{quick|full}-r{n}`, `EVAL_STEPS`), `workspace` (copy `/repo` → `/work` loại bí mật + kiểm sau copy), `machine`, `catalog` (ghép trx + chỉ mục → `catalog-results.csv`), `results`, `clients/grafana` (OB-2), `steps/tests_step` (bước a). `requirements.txt` ghim `pytest 9.1.1`, `jsonschema 4.26.0`.
- C#: `CatalogIndex` (đọc danh mục methodology, quét reflection, xuất `catalog-index.{Assembly}.json` khi có `TADSS_CATALOG_INDEX_DIR`), `CatalogTraceabilityTests` ở hai project (mỗi test có `CatalogId`, mọi mã có trong danh mục). Phát hiện 4 unit test thiếu mã (`SdkDependencyPurityTests` ×2, `StrategyDependencyPurityTests` ×2) → thêm trait `UT-09` (không đổi nội dung test); regex ID sửa để nhận mã một chữ số (`SEC-1`, `NG-1`, `OB-1`…).
- CI: job `evaluation-tools` (Python 3.11, k6 2.3.0, pytest, dựng thử image evaluator).

**Smoke `EVAL_STEPS=a` (stack thật): `passed` sau 685 s (11 phút 25 giây), mã thoát 0.** Copy 4314 file (không có file bí mật), unit **1818/1818** (46 s), integration **232/232** (2 phút 40 giây), 2050 kết quả → 2084 dòng theo **56 mã** danh mục, `verify-expose.sh` TẤT CẢ ĐẠT, OB-2 trên `eval-grafana` đạt. Manifest máy: 8 CPU, 7,6 GB RAM (Docker Desktop).

**Sai lệch / cần biết**
1. **`verify-observability.sh` không chạy trong evaluator** (S-9 chỉ xác nhận `verify-expose.sh`): compose mount `./docker/grafana/...` theo đường dẫn của container (`/work`), daemon Docker của máy không thấy nên Grafana sẽ không có dashboard. OB-2 vẫn kiểm trên `eval-grafana` (đường dẫn mount là của máy chủ); kết quả `verify-observability.sh` lấy từ job CI. Lý do được ghi trong ghi chú của bước.
2. **Không có `eval-grafana-renderer`:** người dùng chốt (08/10/2026) **không lưu ảnh dashboard**, bỏ renderer (~2 GB). OB-3 chỉ gồm JSON snapshot + biểu đồ vẽ từ metric đã thu; đã sửa plan (O-13, Task 9, OB-3), methodology 4.14, DATASET_SCHEMA và PHASE5_DESIGN_SPEC cho khớp.
3. **Thời gian bước (a) 11,4 phút** (ước tính plan ~5): copy qua bind mount của Windows chậm (đã loại thêm `.superpowers`, `.claude` khỏi bản copy), build + IT trong container, `verify-expose.sh` ~4 phút. Đo lại ở Task 12 để cập nhật O-24/D-12.
4. Hai service `eval-tadss-*` cùng `build: .` (dựng hai lần, lần hai dùng cache).
5. **Test thay thế (compose):** `Compose_StartsAllServicesWithoutProfiles_NgrokNeedsOnlyAuthtoken` → `OperationsServices_UnaffectedByEvaluationProfile` (đúng ba service không profile); `Compose_BindsOnlyLoopback` đếm 6 cổng (5 + `127.0.0.1:3002`); `Compose_JoinsExternalSmashCourtNetwork` chỉ áp cho service vận hành; thêm `EvaluationServices_Theory` (8 ca), `EvaluationProfile_OnlyGrafanaPortOnLoopback`.

**Test:** unit 1816 → 1818 (+2 traceability); IT 222 → 232 (+8 theory, +1 traceability, +1 cổng); pytest **45** (workspace/secret, run_id, catalog join, mã thoát, Grafana OB-2, bước a với lệnh giả, CLI).

## 3i. Task 8 — Kịch bản tải k6 và bước (b) (08/10/2026; xong)

**Đã làm:** `tests/load/` (`lib/payload.js` bảng trộn O-15, `lib/options.js` + `config/modes.json` theo `MODE`, `lib/run.js`, `lib/output.js`, `scenarios/lt01|lt02|lt05.js`, `README.md`); `evaluator/d2.py`, `clients/k6.py`, `clients/prometheus.py`, `steps/load_step.py` (bước b: LT-01 → LT-02 → LT-05 trên `eval-tadss-1` trực tiếp, `raw/load/windows.json` kèm cửa sổ từng bậc của LT-02, chờ 15 s sau mỗi lượt, kiểm Prometheus có `k6_*` và `tadss_decision_duration_*`). Bản đầy đủ lặp LT-01 ba lần (thư mục `lt01-r1…r3`); bản nhanh không có LT-05.

**Phát hiện:** k6 `--console-output` ghi **logfmt** (`time=… level=info msg="<JSON đã escape>"`), không phải JSON thô; evaluator đổi thành `requests.jsonl` sạch (D2) rồi xóa file thô.

**Smoke `EVAL_STEPS=b` (bản nhanh, stack thật): `passed` sau 407 s (6 phút 47 giây).**
| | LT-01 (50 RPS, 2 phút) | LT-02 (10 → 200 RPS, 6 bậc × 30 s) |
| :--- | :--- | :--- |
| Request pha chính | 6000, **100 % 200** | 19 941, **100 % 200** |
| p50 / p95 / p99 (ms, phía client) | 14,4 / 60,2 / 192,9 (max 512) | 17,9 / 155,2 / 260,3 (max 561) |
| `dropped_iterations` | 89 (đều ở pha warm-up) | 112 |
- p95 theo bậc LT-02 (RPS thực): 10 → 22 ms; 50 → 19; 90 → 30; 125 → 154; 169 → 86; 199 → 219. Chưa thấy điểm gãy rõ ở ≤ 200 RPS trên máy này (p95 chập chờn quanh 150 ms từ ~125 RPS; kết luận điểm gãy tính ở bước (e) từ D2, một lượt chưa đủ).
- Prometheus trong cửa sổ chạy: `k6_http_reqs_total` tăng 27 525, `tadss_decision_duration_milliseconds_count` tăng 27 621. Mọi response `AUTOMATED` (52 % `NEED_APPROVAL`/48 % `APPROVED`).
- Nhãn `status` của `k6_http_reqs_total` (panel tỉ lệ lỗi của dashboard) **chưa kiểm** vì chưa truy vấn thẳng Prometheus lúc stack còn chạy; kiểm ở Task 9.

**Test:** pytest 45 → **69** (D2/logfmt, `modes.json` đúng bảng, bước b với k6/Prometheus giả: mã cấm 400/429/5xx làm `failed`, mã 0 chỉ báo cáo, thiếu số liệu Prometheus, `dropped_iterations`, lệnh k6; **500 mẫu payload hợp lệ JSON schema v2**, thời gian chơi ở tương lai ≥ 6 ngày, thời lượng item trong P-01, phủ đủ 15 ô của bảng trộn — chạy bằng image `grafana/k6:2.3.0`).

**Docker sau hai task:** đã `compose down` (không `-v`), xóa evidence của lần thử, build cache và volume tạm. Còn 14 image (7,6 GB; trong đó vận hành: otel-lgtm 3,66 GB; evaluator 1,56 GB; sdk alpine 1,03 GB; sdk 8.0.425 chung layer) và 2 volume (`eval-nuget`, `eval-faults`, 413 MB — giữ để Task 9–12 không tải lại NuGet; xóa bằng `docker compose -p tadss-eval --profile evaluation down -v`).

## 4. Việc cần người dùng quyết định / lưu ý

1. **Phiên bản OpenTelemetry (S-10):** plan dùng bộ 1.15.3 vì bản thấp hơn dính advisory mức Medium; đổi lại có `System.Diagnostics.DiagnosticSource` 10.0.0 trên runtime .NET 8. Nếu bạn muốn tránh DiagnosticSource 10.0.0, lựa chọn thay thế là bộ 1.9.0 (chấp nhận hai advisory Medium, giảm thiểu bằng cách chỉ dùng propagator TraceContext và không bật retry đĩa OTLP). Tôi khuyến nghị 1.15.3.
2. **MT-02 chưa đạt 70 % (S-8):** `Default` strategy đang 56,9 %; sẽ phải thêm test ở Task 10 (thông điệp `Reasons`, giá trị mặc định của options).
3. **Dung lượng đĩa:** image đã tải để chạy spike chiếm khoảng 8 GB (otel-lgtm 3,66 GB, renderer 2,06 GB, dotnet sdk 1,25 GB, postgres 420 MB…); chúng sẽ được dùng lại ở Task 7–12. Muốn giải phóng thì `docker image prune`/`docker rmi` (tôi không tự xóa).
4. **RAM:** Docker Desktop đang cấp 8 GB / 8 CPU; plan khuyến nghị tối thiểu 8 GB, bản đầy đủ của chế độ đánh giá có thể chật khi chạy cùng lúc nhiều container (sẽ đo ở Task 12).
5. **Một lần test override đồng thời đỏ không rõ nguyên nhân** (Mục 3). Nếu gặp lại ở máy bạn, hãy gửi nguyên văn lỗi; nếu muốn, tôi có thể chạy riêng một đợt dài hơn (ví dụ 100 lượt lớp `OverrideEndpointTests`) trước khi sang Task 3.
6. Lỗi tôi gặp khi chạy `dotnet ef` lần đầu: quên đặt `ASPNETCORE_ENVIRONMENT=Testing` nên host design-time nạp cấu hình Development (có file bí mật). Tôi không in giá trị nào ra và `--no-connect` không mở kết nối DB; đã chạy lại đúng quy ước. Nêu ra để bạn biết.
7. Chưa commit gì; nhớ không stage `spec/PHASE5_*.md`.

Commit đề xuất cho Phần A (sau Task 5; gộp một commit theo ghi chú của người dùng): `feat(observability): opentelemetry, runtime metrics and grafana dashboard (5.5 part A)`.
