# ĐẶC TẢ THIẾT KẾ THỰC THI PHASE 5 — TADSS v2 + HẠ TẦNG KIỂM THỬ

| | |
|---|---|
| **Phạm vi** | Phase 5 (W05–W08, 05/10 – 01/11/2026 theo kế hoạch v2.1, bắt đầu thực tế 02/10; bước 0 rà soát và điều chỉnh kế hoạch, Gate MR đạt 01/10), phần việc của SV1 trong repo `TADSS-Runtime` (Epic 5A, 5B). Epic 5C của SV2 chỉ nêu ranh giới ở Mục 7 |
| **Branch** | `feat/phase-5-contract-v2-test-infra` (tách từ `develop` tại `f50b158`) |
| **Ngày lập** | 01/10/2026 |
| **Trạng thái** | Đã duyệt 01/10/2026 (gồm P-01, P-02, D-06, D-09); cập nhật cùng ngày theo review: P-02 dùng `WALK_IN`, bổ sung Mục 6.1. Cập nhật 02/10/2026: P-03 (Clean Architecture nhiều project, thay quyết định "không tách project" ở 4.2.4), P-04 (bổ sung bảo mật, minh chứng scale, dashboard bắt buộc, rà soát tài liệu). Cập nhật 04/10/2026: D-11 (compose chỉ để triển khai), D-12 (ba chế độ chạy và chế độ đánh giá một lệnh, thay thế thiết kế lab/E-LOCAL), D-13 (fallback trả `APPROVED` cọc 100%, thay cho `NEED_APPROVAL`) |

> **Vai trò của tài liệu:** mô tả **cách hiện thực** Phase 5 — cấu trúc mã, thứ tự chuyển đổi, cách dựng hạ tầng kiểm thử. Tài liệu **không** lặp lại đặc tả "cái gì"; nguồn sở hữu của từng chủ đề:
>
> - Contract v2, luồng tích hợp, nghiệp vụ cọc: [`INTEGRATION_SPEC.md`](../docs/contracts/INTEGRATION_SPEC.md)
> - Schema `tadss` v2, bất biến INV-1…INV-10: [`DECISION_PERSISTENCE_INVARIANTS.md`](../docs/architecture/DECISION_PERSISTENCE_INVARIANTS.md)
> - Thành phần runtime v2, bảng quyết định strategy, metric: [`SOFTWARE_ARCHITECTURE_SPECIFICATION.md`](../docs/architecture/SOFTWARE_ARCHITECTURE_SPECIFICATION.md) Mục 4
> - Miền lỗi và ma trận ngữ nghĩa lỗi: [`RESILIENCE_AND_FAULT_TAXONOMY.md`](../docs/architecture/RESILIENCE_AND_FAULT_TAXONOMY.md)
> - Danh mục test case, KPI, môi trường: [`EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](../docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md)
> - Evidence Pack, D1–D5: [`DATASET_SCHEMA_SPECIFICATION.md`](../docs/research/DATASET_SCHEMA_SPECIFICATION.md)
> - Task, lịch, gate: [`BACKLOG_SPRINTS.md`](../docs/planning/BACKLOG_SPRINTS.md), [`MASTER_EXECUTION_PLAN.md`](../docs/planning/MASTER_EXECUTION_PLAN.md)
>
> Khi spec này và tài liệu sở hữu lệch nhau, tài liệu sở hữu thắng; nếu spec phát hiện tài liệu sở hữu thiếu/sai, sửa tài liệu sở hữu (Mục 9).

---

## 1. QUYẾT ĐỊNH ĐÃ CHỐT

| Mã | Quyết định | Lý do |
| :--- | :--- | :--- |
| **D-01** | **Thay thế tại chỗ** v1 → v2: SDK, Runtime, Strategies chuyển thẳng sang v2; v1 truy lại qua tag `phase4-baseline` | Chưa có consumer v1; tránh nhân đôi code/DI/mapping |
| **D-02** | Lõi runtime **tách theo trách nhiệm**: `DecisionService` (đánh giá), `DecisionQueryService` (chi tiết + hàng chờ), `OverrideService`, `OutcomeService`; mọi truy cập DB qua `IDecisionAuditStore`; **gỡ MediatR** | `DecisionService` v1 ~400 dòng gánh mọi việc; MediatR chỉ dùng cho ghi audit đồng bộ kèm nhánh dự phòng gọi handler trực tiếp |
| **D-03** | Strategy chỉ trả `StrategyDecision`; runtime lo định danh, thời gian, quy đổi số tiền, approval window, telemetry | Theo SOFTWARE_ARCHITECTURE_SPECIFICATION Mục 4.1 |
| **D-04** | **Môi trường:** app TADSS khi dev chạy trên **Neon** (schema `tadss`); test tự động chạy trên **Testcontainers**; FI/LT/Schemathesis chạy trên PostgreSQL trong stack đánh giá của `docker compose` (profile `evaluation`, D-12; ban đầu ghi là "lab") | Gate G3 cần DB sạch, tái lập được, chạy trên CI; Toxiproxy phải đứng trước DB; mạng tới Neon làm nhiễu số đo |
| **D-05** | **Migration v2 nối tiếp**: giữ 3 migration v1, thêm một migration v2 (gỡ bảng v1, tạo 3 bảng + trigger). Tạo migration và mỗi lần `database update` lên Neon đều chờ người dùng cho phép | Neon đã áp 3 migration v1; cập nhật tăng dần không phải drop schema thủ công |
| **D-06** | **Public API baseline:** cuối gói 5.2, sau khi người dùng review bề mặt SDK v2, thay `baseline_public_api.txt` bằng bản nháp v2 trong commit riêng; tới M5 mọi thay đổi public API sửa baseline có chủ đích trong cùng commit kèm lý do; đóng băng tại M5 | Không để `PublicApiSurfaceTests` đỏ suốt 4 tuần mà vẫn giữ nguyên tắc "không sửa baseline để chữa test" |
| **D-07** | **Mỗi commit đều build `--warnaserror` + test xanh.** Trong một gói được phép tạm cho kiểu v1 và v2 cùng tồn tại; cuối gói kiểu v1 bị xóa | Branch dài 4 tuần phải luôn ở trạng thái chạy được |
| **D-08** | Kết thúc mỗi gói: PR từ branch Phase 5 vào `develop` (người dùng tạo/merge) | `develop` nhận từng bước đã xanh, review được |
| **D-09** | **Gỡ `tests/TADSS.BenchmarkRunner`** và `BenchmarkHarnessTests` ở gói 5.2 | Phụ thuộc EF InMemory và DTO v1; được thay bằng k6 (LT-01…LT-05); báo cáo Tuần 8 và mã nguồn vẫn truy lại qua tag |
| **D-10** | Mọi test mang `[Trait("CatalogId", "<mã>")]` theo danh mục kiểm thử | Báo cáo trx gom được theo mã test case → minh chứng "100% danh mục đạt" |
| **D-11** | **(04/10/2026, bổ sung cùng ngày) Compose chỉ để triển khai, chạy local trước, expose sau:** repo có MỘT `docker-compose.yml` duy nhất (`tadss-runtime` + `ngrok` + `grafana`; Neon + Redis Cloud + API key đọc từ `appsettings.Development.json` được copy vào image như SmashCourt-BE (`.dockerignore` không loại nó; không nhân đôi secret thành biến môi trường; image chỉ dùng local, không push registry); container chạy `Development` (Swagger công khai khi expose, nên API key riêng thật bắt buộc, không dùng `tadss-local-dev-key`), mọi cổng host chỉ `127.0.0.1`; migration lúc khởi động luôn `false`). Không service nào nằm sau profile (như SmashCourt-BE): MỘT lệnh `docker compose up -d --build` chạy cả `tadss-runtime`, `ngrok` và `grafana` (`grafana/otel-lgtm`); chỉ cần `NGROK_AUTHTOKEN` (không domain tĩnh: URL ngrok đổi mỗi lần khởi động lại, BE phải cập nhật `Tadss:BaseUrl`; domain tĩnh miễn phí có thể thêm sau bằng `--url=<domain>` vào lệnh ngrok). Cả ba service tham gia mạng external `smashcourt-network` (cùng quy ước với SmashCourt-BE, tên cố định) ngay từ bây giờ, không đợi Phase 7: BE trong Docker gọi `http://tadss-runtime:8080`. Lab E-LOCAL (PostgreSQL, Toxiproxy) và hai file override `docker-compose.ngrok.yml`, `docker-compose.neon.yml` đã gỡ; lab quay lại ở gói 5.5 dưới dạng profile `lab` trong chính file này (*đã được D-12 thay bằng profile `evaluation`*). Dev hằng ngày chạy `dotnet run` với Neon (D-04), không dùng compose | Giống compose của SmashCourt-BE (app + ngrok); bỏ ba file chồng nhau và hai chế độ A/B khó nhớ; `tools/expose/verify-expose.sh` tự dựng PostgreSQL/Redis tạm và ghi đè chuỗi kết nối/API key bằng biến môi trường trong override tạm (không đọc file secret, không chạm Neon/Redis thật) nên CI vẫn hermetic |
| **D-12** | **(04/10/2026, duyệt cùng ngày) Ba chế độ chạy và chế độ đánh giá một lệnh.** (1) **Dev hằng ngày:** `dotnet run` (Neon + Redis Cloud qua `appsettings.Development.json`), chỉ app + Swagger để kiểm tra luồng, không Docker, không Grafana. (2) **Vận hành / demo:** `docker compose up -d --build` → TADSS + ngrok + Grafana (đã hiện thực, D-11). (3) **Đánh giá cho hội đồng:** `docker compose --profile evaluation up` → stack đánh giá **cô lập, không bao giờ chạm Neon**: TADSS (build Release) + PostgreSQL 16 + Redis 7 + Toxiproxy + Grafana + container **evaluator** điều phối trọn chiến dịch đánh giá theo thứ tự rồi thoát kèm bản tóm tắt: (a) toàn bộ test gộp một lượt (unit, integration, contract, fault injection FI-01…10, nhiều instance SCALE-1, bảo mật), kết quả gom theo mã danh mục (UT/IT/CT/FI/SEC/NG…); (b) kịch bản tải k6 LT-01, LT-02, LT-05 (LT-04 E2E qua SmashCourt-BE để Phase 6 khi BE tích hợp TADSS); (c) kịch bản sự cố dưới tải (LT-03 + FI qua Toxiproxy: cắt PostgreSQL, làm chậm Redis, strategy lỗi) — thấy trực tiếp trên dashboard Grafana (độ trễ tăng, fallback `NEED_APPROVAL` — *đã được D-13 thay: fallback là `APPROVED` cọc 100%* —, 503, hồi phục); (d) chạy Schemathesis (CT-03) và Stryker mutation (MT-01…03); (e) gom toàn bộ vào `evidence/<run_id>/`: `report.html` (đạt/không đạt theo tiêu chí SC1…SC8, biểu đồ p50/p95/p99, biểu đồ dòng thời gian sự cố vẽ từ metric Grafana đã thu, số test từng nhóm), dữ liệu thô (trx theo `CatalogId`, kết quả k6, metric export, `chaos_log`) và `manifest.json` chứa SHA-256 từng file (EV-1…EV-5). Hai độ dài: **bản nhanh** (~15 phút, tải rút ngắn) để diễn tập, **bản đầy đủ** (~1,5–2 giờ, LT-05 soak 60 phút) để lấy số liệu chính thức. Replay 215 booking SmashCourt (RP-01/RP-02) và adapter Hotel Booking Demand (PT-01/PT-02) đổ vào cùng Evidence Pack/báo cáo (vẫn là phần dữ liệu của gói 5.6; phần dựng báo cáo/minh chứng thuộc chế độ đánh giá). **Hệ quả:** các script rời đã dự kiến (`tools/chaos/run-fi.sh`, `tools/contract/run-schemathesis.sh`, các lượt chạy riêng của bộ dựng Evidence Pack) được evaluator thay thế; profile `lab` và cách gọi môi trường E-LOCAL được thay bằng profile `evaluation` (môi trường **E-EVAL**); test tự động trên CI vẫn dùng Testcontainers (`dotnet test`) như hiện nay. **Lưu ý đo lường:** mọi thứ chạy trên một máy (k6, TADSS, DB tranh CPU) nên số liệu là số liệu phòng lab; báo cáo ghi kèm cấu hình máy (CPU/RAM) và liệt kê vào giới hạn. **Ánh xạ gói:** 5.5 dựng chế độ đánh giá (OpenTelemetry + dashboard + evaluator + điều phối k6/Toxiproxy/Schemathesis/Stryker + bộ sinh báo cáo); 5.6 bổ sung dữ liệu (replay 215 booking, adapter khách sạn) vào cùng evidence/báo cáo; 5.7 chạy đánh giá đầy đủ để đóng băng (Gate M5). **Điều chỉnh khi lập plan 5.5 (06/10/2026):** lệnh chạy là `docker compose -p tadss-eval --profile evaluation run --rm --build evaluator` thay cho `docker compose --profile evaluation up`, vì service không có profile (tadss-runtime Development đọc Neon, ngrok) luôn được khởi động bởi `up` bất kể profile (đã kiểm ở spike S-5); project `tadss-eval` riêng để `down -v` không dừng stack vận hành. Thời lượng bản đầy đủ ước tính 2,5–3 giờ (đo thật ở cuối gói 5.5). Phạm vi Stryker theo cây sau 5.2d (Application/Decisions, Application/Routing, Infrastructure/Resilience thay cho `Services/**`). | Hội đồng cần một lệnh duy nhất tái lập được toàn bộ minh chứng, trên hạ tầng cô lập không làm bẩn Neon dùng chung; ba chế độ tách rõ mục đích (viết mã / vận hành / đánh giá) nên không lẫn cấu hình; gộp script rời vào một evaluator giảm số bước thủ công và bảo đảm Evidence Pack luôn sinh từ cùng một lượt chạy |
| **D-13** | **(04/10/2026, người dùng duyệt) Fallback của runtime trả `APPROVED` cọc 100%, không còn `NEED_APPROVAL`.** Khi strategy không dùng được (inner timeout, exception, circuit breaker mở), `FallbackDecisionPolicy` trả `APPROVED` với tỷ lệ cọc 1.0 (`deposit.amount = totalPrice`), vẫn `executionType = FALLBACK`, `fallbackUsed = true`, giữ `reasonCode` theo nguyên nhân (`FALLBACK_STRATEGY_TIMEOUT` / `FALLBACK_STRATEGY_EXCEPTION` / `FALLBACK_CIRCUIT_OPEN`) và `failure_reason`; không kèm khuyến nghị, không hạn duyệt. **Hệ quả:** quyết định fallback không vào hàng chờ duyệt và không override được (override chỉ áp cho `NEED_APPROVAL` → 409 `NOT_PENDING_APPROVAL`) — đúng chủ đích; vẫn không ghi cache; không đổi wire, SDK (dùng `StrategyDecision.Approve` sẵn có, baseline public API giữ nguyên), schema hay migration. **Thay thế** mọi phát biểu trước đó "fallback → `NEED_APPROVAL`" (INTEGRATION_SPEC Mục 1, RESILIENCE_AND_FAULT_TAXONOMY, D-12 mục (c), các câu tương ứng ở kế hoạch/đề cương); dữ liệu audit ghi trước D-13 có thể còn bản `NEED_APPROVAL` + `FALLBACK`, runtime vẫn đọc/hiển thị nguyên vẹn | Mọi sự cố kỹ thuật có chung một quy tắc cọc 100% (như khi SmashCourt tắt flag hoặc TADSS không phản hồi); `NEED_APPROVAL` chỉ dành cho bất định nghiệp vụ do strategy quyết định (giá trị lớn, lịch sử không đến); tránh dồn hàng loạt đơn cho Manager khi circuit mở |

### 1.1. Quyết định bổ sung (đã duyệt 01/10/2026)

| Mã | Quyết định | Lý do |
| :--- | :--- | :--- |
| **P-01** | Giới hạn thời lượng item (30 phút – 12 giờ) chuyển từ **bất biến SDK** sang **chính sách kiểm tra của runtime** cấu hình được (`Tadss:Validation:MinItemMinutes = 30`, `MaxItemMinutes = 720`). SDK chỉ giữ bất biến `endTime > startTime`. Wire không đổi; INTEGRATION_SPEC ghi rõ giá trị mặc định. Cấu hình được kiểm tra khi khởi động (`ValidateOnStart`): `0 < MinItemMinutes ≤ MaxItemMinutes`, sai → app không khởi động | PT-01 (Hotel Booking Demand, lưu trú nhiều đêm) không chạy được mà không sửa SDK — mâu thuẫn tiêu chí "git diff rỗng" |
| **P-02** | `channel` là enum `ONLINE` \| `WALK_IN` (đặt tại quầy), trùng tên với `BookingSource` của SmashCourt-BE; dùng nhất quán ở JSON schema, OpenAPI, DTO, domain, routing (`When.Channels`), audit và test | INTEGRATION_SPEC chưa định nghĩa tập giá trị → JSON schema v2 không chặt được; dùng đúng từ vựng của hệ thống nguồn để BE không phải dịch giá trị |

### 1.2. Mục tiêu cuối Phase 5 và quyết định bổ sung (duyệt 02/10/2026)

**Mục tiêu cuối Phase 5** (trước khi tích hợp vào SmashCourt ở Phase 6):

1. TADSS chạy ổn định với đủ chức năng v2; tài liệu đồng bộ với mã; mã, test và minh chứng kiểm thử (Evidence Pack, trx theo `CatalogId`, mutation, FI, metric/trace, dashboard) đạt chất lượng cao nhất có thể đo được.
2. Kiến trúc theo Clean Architecture: phụ thuộc hướng vào trong, được compiler và test kiến trúc khóa; mã đạt SOLID, không hardcode giá trị cấu hình được, xử lý lỗi một đường, luồng request qua các tầng được đặc tả và khóa bằng test; có các cơ chế bảo mật và khả năng mở rộng ngang (nhiều instance).

| Mã | Quyết định | Lý do |
| :--- | :--- | :--- |
| **P-03** | **Clean Architecture nhiều project** (thay câu "không tách thêm project" của 4.2.4 bản 02/10 trước đó): `Shared.DecisionContractSDK` (Domain + contract) ← `TADSS.Application` ← `TADSS.Infrastructure` ← `TADSS.Runtime.Api` (Presentation + composition root); Strategies giữ là plugin chỉ phụ thuộc SDK. Thực hiện ở 5.2d (Mục 4.2.4) | Ranh giới tầng do compiler cưỡng chế, không chỉ dựa vào quy ước; sửa các phụ thuộc ngược hiện có (port nằm trong `Infrastructure/`, exception nghiệp vụ nằm trong `Middlewares/`) |
| **P-04** | Bổ sung vào các gói còn lại: gói bảo mật (Mục 4.3.2, 5.3); minh chứng scale nhiều instance (Mục 4.4, 4.5 — 5.4/5.5); dashboard Grafana **bắt buộc** (5.5); rà soát toàn bộ tài liệu (Mục 4.7, 5.7) | Đạt mục tiêu 1: hệ thống đủ an toàn để expose, chứng minh được khả năng scale và theo dõi được trước khi tích hợp |

---

## 2. VẤN ĐỀ v1 PHÁT HIỆN KHI PHÂN TÍCH (bổ sung cho Mục 3 SOFTWARE_ARCHITECTURE_SPECIFICATION)

| # | Vấn đề | Bằng chứng | Xử lý ở gói |
| :--- | :--- | :--- | :--- |
| V-1 | `docker-compose.yml` truyền `Redis__ConnectionString` nhưng app đọc `Tadss:Cache:RedisConnectionString` → container TADSS chưa bao giờ dùng Redis | `docker-compose.yml`, `TadssCacheOptions.SectionName` | 5.1 |
| V-2 | Một circuit breaker dùng chung cho mọi strategy → khi có routing, strategy này lỗi làm ngắt strategy khác | `DecisionResiliencePipeline` singleton, một `_pipeline` | 5.4 |
| V-3 | Cache lưu nguyên `DecisionResultDto` (gồm `decisionId`, `evaluatedAt`); v2 có `approvalExpiresAt` phụ thuộc thời điểm tạo → không được cache response | `DecisionService` bước 6 | 5.2 (cache `StrategyDecision`) |
| V-4 | `InvalidOperationException` → 400; PostgreSQL lỗi → 500 thay vì 503; mọi `OperationCanceledException` → 504; message của exception 4xx bị trả thẳng cho client | `GlobalExceptionHandler` | 5.2 |
| V-5 | Nhận diện vi phạm unique idempotency bằng so khớp chuỗi `Message.Contains("idempotency")` | `DecisionService.IsIdempotencyConstraintViolation` | 5.2 (theo `SqlState` + tên constraint) |
| V-6 | `BenchmarkRunner` phụ thuộc EF InMemory + DTO v1 | `tests/TADSS.BenchmarkRunner` | 5.2 (D-09) |
| V-7 | Replay booking lịch sử: giờ chơi ở quá khứ → approval window luôn rỗng → mọi `NEED_APPROVAL` thành `APPROVED` 100% | Quy tắc Mục 1.1 INTEGRATION_SPEC | 5.6 (dời thời gian theo bội số 7 ngày) |
| V-8 | Mất kết nối PostgreSQL: execution strategy mặc định của Npgsql bọc `NpgsqlException` (`IsTransient = true`) trong `InvalidOperationException` → bị map thành 400 | `GlobalExceptionHandler`, hành vi Npgsql 8 | 5.1 (FI-08 trên Toxiproxy + map 503) |

---

## 3. CHIA GÓI VÀ THỨ TỰ

| Gói | Nội dung | Task | Tuần | Test case xây dựng | Điều kiện xong (DoD) |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **5.1** Nền móng kiểm thử | Testcontainers (PostgreSQL 16, Redis 7, Toxiproxy), chuyển 27 IT v1 sang container, gỡ EF InMemory khỏi IT, compose E-LOCAL (*sau D-11/D-12: đã gỡ, thay bằng chế độ đánh giá ở gói 5.5*), CI | T5.12, T5.13 | W05 (bắt đầu sớm 02/10 vì Gate MR đóng 01/10) | IT-15, FI-08, UT-08 (lỗi persistence), UT-09 (không còn InMemory trong IT); hạ tầng cho FI-06…09 | 27 IT xanh trên container ở local + CI; `docker compose up` một lệnh; V-1, V-8 đã sửa |
| **5.2** Contract & lõi v2 (5.2a, 5.2b, 5.2c, 5.2d — Mục 4.2.0) | SDK v2, strategies v2, `DecisionService` v2, approval window, quy đổi cọc, `RequestHasher` v2, `IDecisionAuditStore`, migration v2, `POST/GET /api/v2/decisions`, map lỗi, gỡ v1 + BenchmarkRunner | T5.1, T5.2, T5.3, T5.4, T5.8, một phần T5.7 | W05 | UT-01…03, 05, 06, 08…12, 14, CT-01, CT-02, IT-01…05, IT-15 | Không còn kiểu/endpoint v1; baseline nháp v2 (D-06); solution theo Clean Architecture nhiều project, mã đạt SOLID (Mục 4.2.4, P-03) |
| **5.3** Quản trị & bảo mật | Override v2, outcome, hàng chờ, `X-Api-Key`, `X-Actor-Id`; gói bảo mật (Mục 4.3.2); expose qua ngrok (E-NGROK, Mục 4.3.1) | T5.5, T5.6, T5.7, T5.30 | W05 | IT-06…11, NG-1…NG-5, SEC-1…SEC-7 | 4 endpoint quản trị đúng INTEGRATION_SPEC §3.5–3.8; SEC-1…SEC-7 đạt; NG-1…NG-3, NG-5 đạt tự động; NG-4 do người dùng chạy bằng token ngrok của mình |
| **5.4** Runtime thích ứng | Routing theo request, pipeline theo strategy, cách ly strategy không hợp tác, chính sách cache + breaker Redis, shadow, idempotency/cache giữa nhiều instance | T5.9, T5.10, T5.11 | W05 | UT-04, UT-07, UT-13, IT-12…14, FI-01…05, SCALE-1 | FI-05 đạt; shadow (kể cả khi lỗi) không đổi response và bản ghi PRIMARY; SCALE-1 đạt |
| **5.5** Observability & chế độ đánh giá | OpenTelemetry, dashboard Grafana, **chế độ đánh giá một lệnh** `docker compose --profile evaluation up` (D-12): evaluator điều phối test gộp (gồm ma trận FI trên Toxiproxy), k6, kịch bản sự cố dưới tải, Schemathesis, Stryker + ReportGenerator, hai replica cho SCALE-2, bộ sinh `report.html` + `manifest.json` | T5.14…T5.17 | W06–W07 | FI-06…10, CT-03, MT-01…03, OB-1…OB-3, SCALE-2, SCALE-3 | FI-01…10 tự động; metric + trace xuất được; mutation ≥ 70%; dashboard đạt OB-1…OB-3 (**bắt buộc**, P-04); SCALE-2, SCALE-3 đạt; một lượt bản nhanh của chế độ đánh giá chạy hết và sinh `evidence/<run_id>/` |
| **5.6** Minh chứng dữ liệu | Replay 215 booking, adapter Hotel Booking Demand — đổ vào cùng Evidence Pack/báo cáo của chế độ đánh giá, notebook | T5.18…T5.20 | W08 | RP-01, RP-02, PT-01, PT-02 | Evidence Pack mẫu qua EV-1…EV-5 |
| **5.7** Đóng băng M5 | Rà soát toàn bộ tài liệu, chạy đánh giá đầy đủ (bản đầy đủ của chế độ đánh giá), đóng băng contract v2, chốt API BE ↔ FE, môi trường tích hợp E-STAGING (Docker Compose, không pilot) | T5.21…T5.23 | W08 | DOC-1…DOC-4 | DOC-1…DOC-4 đạt; Gate M5 |

Phụ thuộc: 5.1 → 5.2 → 5.3 → 5.4 → 5.5 → 5.6 → 5.7. Gói 5.5 phần OpenTelemetry có thể làm song song với 5.4. Mỗi gói có một implementation plan riêng, viết khi tới lượt.

---

## 4. THIẾT KẾ CHI TIẾT

### 4.1. Gói 5.1 — Nền móng kiểm thử

**Package** (khai báo trong `Directory.Packages.props`, chốt bản vá cụ thể khi restore): `Testcontainers.PostgreSql` 4.x, `Testcontainers.Redis` 4.x, `Testcontainers.Toxiproxy` 4.x. Không dùng `Toxiproxy.Net` (không còn bảo trì). Gỡ `Microsoft.EntityFrameworkCore.InMemory` khỏi `TADSS.IntegrationTests` ngay ở gói này; package chỉ bị xóa khỏi `Directory.Packages.props` khi BenchmarkRunner bị gỡ (gói 5.2).

**Fixture** (`tests/TADSS.IntegrationTests/Infrastructure/`):

| Thành phần | Trách nhiệm |
| :--- | :--- |
| `TadssInfrastructureFixture` (xUnit `ICollectionFixture`) | Khởi động một lần cho cả lượt chạy: một Docker network, PostgreSQL 16, Redis 7, Toxiproxy (proxy `postgres`, `redis`). Áp migration vào database mẫu `tadss_template`; tạo database mới cho từng test bằng `CREATE DATABASE … TEMPLATE tadss_template`. Sau khi tạo proxy, kiểm tra đường đi qua Toxiproxy (truy vấn PostgreSQL và `PING` Redis qua proxy); không thông → dừng cả lượt chạy với thông báo rõ thay vì để test lỗi dây chuyền |
| `IntegrationTestBase` (`IAsyncLifetime`) | Vòng đời database: tạo (nhân bản từ mẫu) → test → xóa (`DROP DATABASE … WITH (FORCE)`). Mỗi test có `TadssWebApplicationFactory` và `HttpClient` riêng; `CreateFactory(...)` cho test cần thay DI |
| `TadssWebApplicationFactory` (giữ tên cũ) | Nhận connection string PostgreSQL + Redis của container (trực tiếp hoặc qua Toxiproxy); môi trường `Testing` nên không nạp `appsettings.Development.json` |
| `ToxiproxyControl` | Gọi REST API của Toxiproxy (module `Testcontainers.Toxiproxy` chỉ dựng container, không có client): tạo proxy, bật/tắt, `reset`; test tự `reset` trong `DisposeAsync` |

Image ghim theo tag cụ thể (`postgres:16.15-alpine`, `redis:7.4-alpine`, `ghcr.io/shopify/toxiproxy:2.12.0`, `grafana/otel-lgtm:0.34.0`); builder Testcontainers 4.x phải gọi constructor có tham số image (constructor không tham số bị `[Obsolete]` → hỏng build `--warnaserror`) và ghi digest vào manifest Evidence Pack. Tạo database theo template giữ thời gian chạy IT ở mức giây sau lần khởi động container đầu tiên.

**Lỗi persistence (V-8):** ngoại lệ có `NpgsqlException.IsTransient = true` ở bất kỳ tầng `InnerException` nào → 503 `PERSISTENCE_UNAVAILABLE`. Ở gói 5.1 mã lỗi đặt tại `src/TADSS.Runtime.Api/Common/RuntimeErrorCodes.cs` (thêm vào SDK sẽ phá baseline public API v1); gói 5.2 chuyển vào `ErrorCodes` v2 của SDK.

> **Đã thay thế bởi D-11 và D-12 (04/10/2026):** phần compose E-LOCAL dưới đây đã gỡ khỏi `docker-compose.yml`; D-11 dự kiến lab quay lại ở gói 5.5 dưới dạng profile `lab`, D-12 thay bằng profile `evaluation` (môi trường E-EVAL, mục 4.5). Giữ nguyên để tra thiết kế các service (PostgreSQL, Redis, Toxiproxy, Grafana) mà stack đánh giá sẽ dùng lại.

**`docker-compose.yml` (E-LOCAL)** — thay file hiện tại:

| Service | Image | Ghi chú |
| :--- | :--- | :--- |
| `tadss-runtime` | build từ `Dockerfile` | Biến môi trường đúng tên section (`ConnectionStrings__DefaultConnection`, `Tadss__Cache__RedisConnectionString`) trỏ qua Toxiproxy; sửa V-1 |
| `tadss-postgres` | PostgreSQL 16 | Volume riêng; TADSS tự áp migration khi khởi động nhờ cờ `Tadss:Database:ApplyMigrationsOnStartup=true` — cờ chỉ bật trong compose, mặc định `false` (Neon/staging không bao giờ bật) |
| `tadss-redis` | Redis 7 | |
| `toxiproxy` | Toxiproxy 2.x | Cấu hình proxy trong `docker/toxiproxy/toxiproxy.json` |
| `otel-lgtm` | `grafana/otel-lgtm` | Grafana `:3001` (tránh trùng FE `:3000`); provisioning dashboard ở gói 5.5 |

Compose tự chứa mạng riêng để `docker compose up` chạy được trên máy mới; việc nối vào `smashcourt-network` (external, do SmashCourt-BE tạo) làm ở Phase 7 bằng file override (đã được D-11 làm sớm: compose hiện tại nối `smashcourt-network` ngay). Thông tin đăng nhập PostgreSQL local đọc từ `.env` (không commit) có giá trị mặc định cho DB dùng một lần; mẫu ở `.env.example`. `.dockerignore` (đã loại `appsettings.Development.json`) giữ nguyên (*sau D-11: `.dockerignore` không còn loại file này, nó được copy vào image local*).

**CI** (`.github/workflows/ci.yml`): runner `ubuntu-latest` có Docker sẵn. Tách hai bước test theo project: `TADSS.UnitTests` và `TADSS.IntegrationTests` (Testcontainers); mỗi bước xuất trx riêng.

**Chuyển 27 IT v1:** chỉ thay factory và gắn `CatalogId`; nội dung test giữ nguyên. Lỗi phát hiện khi chạy trên PostgreSQL thật được sửa trong gói này và ghi vào báo cáo gói (đây là minh chứng giá trị của việc chuyển hạ tầng).

### 4.2. Gói 5.2 — Contract & lõi v2

#### 4.2.0. Chia gói con, cùng tồn tại v1/v2 và điểm dừng (duyệt 01/10/2026)

| Gói con | Nội dung | Test case | Phụ thuộc |
| :--- | :--- | :--- | :--- |
| **5.2a** SDK v2 + strategies v2 | Kiểu SDK v2 (4.2.1), JSON schema v2 cho request/result, hai strategy v2 (4.2.2) | UT-01, UT-02, UT-03, UT-05, CT-01 (phần schema) | 5.1 |
| **5.2b** Lõi runtime v2 | `ApprovalWindowPolicy`, `DepositCalculator`, `RequestHasher` v2, cache v2, `IDecisionAuditStore` (interface), `DecisionService` v2, pipeline cho strategy v2, map lỗi, kiểm tra cấu hình P-01 | UT-04 (resolver), UT-06, UT-07, UT-08, UT-10, UT-11, UT-12, UT-13 | 5.2a |
| **5.2c** Chuyển hẳn sang v2 | Migration v2, `EfDecisionAuditStore`, `POST/GET /api/v2/decisions`, gỡ v1 (gồm MediatR, BenchmarkRunner, EF InMemory), viết lại integration test sang v2, đổi namespace V2 về gốc, `openapi-v2.yaml`, baseline nháp v2 | IT-01…05, IT-13, IT-15, CT-01, CT-02, FI-08 | 5.2b |
| **5.2d** Clean Architecture và SOLID | Tách `TADSS.Runtime.Api` thành `TADSS.Application`, `TADSS.Infrastructure`, `TADSS.Runtime.Api` (P-03); kiểm toán SOLID, tách trách nhiệm, bỏ hardcode, đặc tả và khóa luồng request qua các tầng, bộ test kiến trúc, viết lại skill `tadss-architecture` (Mục 4.2.4). Không đổi hành vi, không đổi wire, không đổi bề mặt public của SDK | UT-14 (kiến trúc, mới); toàn bộ test hiện có giữ nguyên | 5.2c |

**Cùng tồn tại v1/v2 (D-07):** trong 5.2a–5.2b, kiểu v2 nằm ở namespace tạm `Shared.DecisionContractSDK.V2.*` (thư mục `src/Shared.DecisionContractSDK/V2/`); strategy v2 ở `TADSS.Strategies.{Name}.V2`; runtime v2 ở thư mục con riêng; test v2 ở `tests/TADSS.UnitTests/V2/`. `DomainInvariantException` và `ContractValidationException` của v1 được dùng lại, không nhân bản. Ở 5.2c, sau khi xóa v1, đổi namespace hàng loạt về gốc (`Shared.DecisionContractSDK.*`, `TADSS.Strategies.{Name}`) đúng cây ở 4.2.1. Migration v2 tạo lại bảng `decision_executions` nên v1 hỏng ngay khi áp migration — vì vậy migration, endpoint v2 và gỡ v1 nằm cùng 5.2c.

**Điểm dừng baseline public API (D-06):** từ 5.2a, mỗi task chủ ý thêm kiểu vào SDK cập nhật `baseline_public_api.txt` trong chính task đó; diff baseline chỉ chứa đúng các kiểu task thêm và được đưa vào báo cáo task. `PublicApiSurfaceTests` khi lệch ghi bề mặt thực tế ra `TestResults/baseline_public_api.actual.txt` để so sánh, không bao giờ tự ghi đè baseline. **Dừng cuối 5.2a:** người dùng review toàn bộ bề mặt SDK v2 trước khi commit. **Dừng ở 5.2c:** sau khi xóa v1 và đổi namespace, baseline mang header `v2.0.0-draft`, người dùng review lại — đây là baseline nháp v2.

**Điểm dừng migration (D-05), ở 5.2c:** dừng trước `dotnet ef migrations add`, chờ người dùng cho phép; người dùng review mã migration (`DropTable` v1, `CreateTable`, SQL trigger). Migration chỉ được áp tự động lên database Testcontainers và compose (*compose E-LOCAL khi đó; từ D-11 `docker-compose.yml` luôn đặt cờ `false`*). Áp lên Neon là bước riêng sau khi 5.2c được merge, do người dùng chạy hoặc cho phép từng lần (migration xóa bảng v1 — dữ liệu kiểm thử).

**JSON enum chặt:** `JsonStringEnumConverter` của .NET nhận cả chuỗi số (`"1"`) và chữ thường, nên SDK dùng converter riêng gắn trên từng enum v2 (chỉ nhận đúng tên định nghĩa, phân biệt hoa thường, không nhận số). Converter đăng ký toàn cục trong `JsonSerializerOptions` **ghi đè** converter gắn trên kiểu (đã kiểm chứng), nên 5.2c phải gỡ `JsonStringEnumConverter` khỏi `AddJsonOptions` của runtime và dùng `TadssJson.Apply`. Thời gian trên wire bắt buộc có offset (`Z` hoặc `±hh:mm`); thiếu offset → 400 `VALIDATION_ERROR` (tránh .NET gán múi giờ máy chủ, INV-3).

**Việc hoãn từ 5.1:** 5.2b — header HTTP `Retry-After` cho 503; SqlState `40001`/`40P01` (lỗi tuần tự hóa, deadlock) → 503 kèm `Retry-After` vì có thể thử lại. 5.2c — flush Redis giữa các integration test; IT-13 chứng minh Redis thật được dùng (tắt fallback bộ nhớ ở môi trường Testing); log cảnh báo khi `ApplyMigrationsOnStartup` bật; chuyển F1–F7 sang v2 và gỡ trùng `CatalogId` FI-08 của F6; biến thể FI-08 làm lỗi đúng lúc ghi audit (INV-10 trên đường ghi).

**Khoảng trống tạm thời:** từ 5.2c tới 5.3 không có endpoint override và hàng chờ (v1 bị gỡ, v2 thuộc 5.3); IT-06, IT-09 quay lại ở 5.3.

#### 4.2.1. SDK `Shared.DecisionContractSDK` (chỉ BCL)

```text
Common/       ErrorCodes (mã lỗi v2 — INTEGRATION_SPEC §3.8), ReasonCodes, ContractValidationException, PagedResult, TadssJson (JsonSerializerOptions dùng chung; converter enum chặt và thời gian bắt buộc offset là internal)
Domain/       Reservation, ReservationItem, CustomerProfile, CustomerHistory, DemandSnapshot, BranchCalendar, PeakWindow, DomainInvariantException
Decisions/    StrategyDecision, StrategyCachePolicy, DecisionContext, DecisionRecommendation
DTOs/         Decisions/ (DecisionRequestDto, ReservationItemDto, CustomerDto, CustomerHistoryDto, DemandDto, DecisionResultDto, DepositDto, DecisionRecommendationDto, DecisionDetailDto, DecisionDetailResultDto, ExecutionDetailDto)
              Governance/ (OverrideRequestDto, OverrideDepositDto, OverrideResponseDto, PendingApprovalItemDto, PendingApprovalCustomerDto)
              Outcomes/ (OutcomeRequestDto, OutcomeResponseDto)
              Mappings/ (DecisionRequestMapping: DTO → Domain)
Enums/        DecisionStatus, ExecutionType, CustomerTier, Channel, OverrideAction, DecisionOutcome, RecommendationType
Interfaces/   IReservationDecisionStrategy
```

Bị gỡ: `PaymentPolicy`, `PaymentStatus`, `BookingPriority`, `ApiResponse`, `ResourceInfo`, `ReservationSchedule`, `ReservationMetadata` (metadata thành `IReadOnlyDictionary<string,string>` trên `Reservation`), `CalculateDepositRatio`, `CanBeCancelledWithoutPenalty`.

**Domain:**

- `Reservation`: `ReservationId`, `BranchId`, `BranchTimeZone` (`TimeZoneInfo` từ mã IANA; mã không hợp lệ → `ContractValidationException`), `Items` (1..20), `Customer`, `TotalPrice` (≥ 0), `Currency` (ISO 4217, 3 ký tự hoa), `Channel`, `RequestedAt` (`DateTimeOffset`), `Demand?` (`DemandSnapshot`), `Metadata`. Thuộc tính suy ra: `EarliestStart`, `ToBranchLocal(DateTimeOffset)`.
- `ReservationItem`: `ResourceId`, `ResourceTypeId`, `Start`, `End` (`DateTimeOffset`, `End > Start`), `Price ≥ 0`. Giới hạn thời lượng theo P-01.
- `CustomerProfile`: `IsGuest`, `CustomerId?` (bắt buộc ⇔ không phải khách vãng lai), `Tier`, `MemberSince?` (null với khách vãng lai), `History?`; `CustomerHistory`: `CompletedCount`, `NoShowCount`, `CancelledCount`, `LateCancelledCount`, `PaymentExpiredCount` (≥ 0; hai trường sau mặc định 0 khi client không gửi), `LastCompletedAt?`.
- `DemandSnapshot` (tùy chọn trên `Reservation.Demand`): `OccupancyRate` ∈ [0, 1], `WaitlistCount` ≥ 0. Không vào `request_hash` (DECISION_PERSISTENCE_INVARIANTS C.2).
- Nguồn và ý nghĩa các tín hiệu (đối chiếu dữ liệu SmashCourt-BE 02/10/2026): INTEGRATION_SPEC §3.3 và §4.2. Strategy v2 ở 5.2a chưa dùng `MemberSince`, `LateCancelledCount`, `PaymentExpiredCount`, `Demand`; các trường này được lưu trong audit làm dữ liệu cho strategy học máy ở Khóa luận.
- `BranchCalendar` + `PeakWindow` (ngày trong tuần, giờ bắt đầu, giờ kết thúc theo giờ địa phương): `IsPeak(Reservation)` xét theo `EarliestStart` quy về giờ chi nhánh. Đây là nơi duy nhất định nghĩa "giờ cao điểm", dùng chung cho router và strategy.
- Bất biến ném `DomainInvariantException` trong constructor (ADR-004).

**Hợp đồng strategy** (theo SOFTWARE_ARCHITECTURE_SPECIFICATION 4.1):

```csharp
public interface IReservationDecisionStrategy
{
    string StrategyName { get; }
    string StrategyVersion { get; }
    StrategyCachePolicy CachePolicy { get; }
    Task<StrategyDecision> EvaluateAsync(Reservation reservation, DecisionContext context, CancellationToken cancellationToken);
}
```

- `StrategyDecision` chỉ tạo qua factory `Approve(rate, reasonCode, reasons)`, `RequireApproval(reasonCode, reasons, recommendations)`, `Reject(reasonCode, reasons)`; `rate ∈ [0, 1]`.
- `DecisionContext`: `EvaluatedAtUtc`, `Calendar` (`BranchCalendar`). Strategy không đọc đồng hồ hệ thống.
- `StrategyCachePolicy`: `Disabled` hoặc `Enabled(TimeSpan ttl)`.

**DTO wire:** ánh xạ 1-1 INTEGRATION_SPEC §3; `[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]` cho `additionalProperties: false`; `JsonStringEnumConverter` không nhận số; `TadssJson.Options` là cấu hình serialize duy nhất cho API, audit payload và test.

**Kiểm tra request — hai tầng:** (1) ràng buộc hình thức (thiếu trường, sai định dạng, enum lạ, trường thừa) → 400 `VALIDATION_ERROR`; (2) bất biến domain khi dựng `Reservation` → 400 `DOMAIN_INVARIANT_VIOLATION`.

**Artifact contract:** `docs/contracts/schemas/v2/*.json` (viết tay, nguồn chuẩn); `docs/contracts/openapi-v2.yaml` sinh từ Swashbuckle của app. Test CT-01 kiểm response IT theo schema bằng `JsonSchema.Net`; một test so OpenAPI sinh ra với file đã commit để phát hiện lệch.

#### 4.2.2. Strategies v2

- `DefaultDecisionStrategy` 2.0.0 và `MockAlternativeDecisionStrategy` 2.0.0 theo bảng quyết định ở SOFTWARE_ARCHITECTURE_SPECIFICATION 4.3.
- Tham số là POCO trong chính project strategy (`DefaultStrategyOptions`, `MockAlternativeStrategyOptions`), nhận qua constructor; runtime bind từ `Tadss:Strategies:{Name}` và khởi tạo strategy. Strategy vẫn chỉ phụ thuộc SDK.
- Tham số strategy đọc khi khởi động (đổi cần restart); routing/shadow reload nóng (gói 5.4).

#### 4.2.3. Runtime

| Thành phần | Vị trí | Trách nhiệm |
| :--- | :--- | :--- |
| `DecisionsController` | `Controllers/` | Route `api/v2/decisions`; chỉ chuyển tiếp, không chứa logic |
| `DecisionService` | `Services/` | Điều phối theo luồng SOFTWARE_ARCHITECTURE_SPECIFICATION 4.4 |
| `ApprovalWindowPolicy` | `Services/Decisions/` | Quy tắc INTEGRATION_SPEC §1.1; dùng `TimeProvider` (BCL .NET 8) |
| `DepositCalculator` | `Services/Decisions/` | `amount = round(totalPrice × rate)` theo số chữ số thập phân của tiền tệ (VND 0; EUR, USD 2; mặc định 2), `MidpointRounding.AwayFromZero` |
| `DecisionResponseComposer` | `Services/Decisions/` | Ghép `StrategyDecision` + telemetry + approval window + cọc → `DecisionResultDto` và bản ghi audit |
| `RequestHasher` | `Services/` | INV-3 / Mục C.2 DECISION_PERSISTENCE_INVARIANTS; danh sách trường đưa vào hash khai báo tường minh trong mã |
| `IDecisionAuditStore` + `EfDecisionAuditStore` | `Infrastructure/Data/` | Mọi đọc/ghi 3 bảng audit; dịch lỗi PostgreSQL thành exception nghiệp vụ. Interface theo use case, xem dưới bảng |
| `PersistenceUnavailableException`, `IdempotencyConflictException`, `DecisionStateConflictException` | `Middlewares/` | Exception có ngữ nghĩa cho `GlobalExceptionHandler` |

Cột "Vị trí" là cây thư mục sau 5.2c (một project `TADSS.Runtime.Api`). Từ 5.2d, vị trí đích theo bảng ánh xạ ở Mục 4.2.4; trách nhiệm không đổi.

**`IDecisionAuditStore` theo use case:** mỗi method ứng với một nhu cầu của service, không có `Add/Update/Delete/Find` chung chung, không trả `IQueryable` hay `DbContext`. Không có method sửa/xóa (append-only, INV-4).

| Method | Dùng bởi |
| :--- | :--- |
| `FindPrimaryByIdempotencyKeyAsync(key)` | `DecisionService` — tra idempotency |
| `InsertExecutionAsync(record)` → bản ghi đã lưu, hoặc bản thắng khi tranh chấp idempotency | `DecisionService` (PRIMARY), `ShadowExecutionWorker` (SHADOW) |
| `GetDecisionDetailAsync(decisionId)` → execution + override + outcome | `DecisionQueryService`, `OverrideService`, `OutcomeService` |
| `GetPendingApprovalsAsync(branchId, page, nowUtc)` | `DecisionQueryService` (SQL Mục B.5) |
| `InsertOverrideAsync(record)`, `InsertOutcomeAsync(record)` | `OverrideService`, `OutcomeService` |

**Luồng `DecisionService.EvaluateAsync`:** dựng `Reservation` từ request (`ToDomain`, thuần: request sai hình thức luôn 400 kể cả khi key đã dùng — ruling 5.2b) → tính `request_hash` → tra idempotency (cùng hash: trả bản ghi cũ; khác: 409) → kiểm tra thời lượng item (P-01) → chọn strategy (gói 5.2: theo `Tadss:Strategy:ActiveStrategyName`; gói 5.4: router) → đọc cache nếu `CachePolicy` cho phép → pipeline nếu miss → ghi cache (chỉ `AUTOMATED`) → approval window → quy đổi cọc → ghi audit đồng bộ (fail-closed, INV-10) → response.

**Cache v2:** giá trị cache là `StrategyDecision` (không phải response). Key: `tadss:v2:{strategyName}:{strategyVersion}:{optionsFingerprint}:{requestHash}`; `optionsFingerprint` = SHA-256 rút gọn của tham số strategy, để đổi tham số mà không đổi version không đọc nhầm kết quả cũ. Fingerprint phải tất định giữa các process và máy, theo cùng cách với `request_hash` (Mục C.2 DECISION_PERSISTENCE_INVARIANTS): danh sách tham số và thứ tự khai báo tường minh trong mã, số định dạng bất biến theo culture, không serialize POCO bằng reflection (thứ tự property không được bảo đảm). UT-13 kiểm tra cùng tham số → cùng fingerprint ở các culture khác nhau, đổi một tham số → fingerprint đổi.

**Ghi audit và tranh chấp idempotency:** `INSERT` một bản ghi `decision_executions`; bắt `PostgresException` với `SqlState = 23505` **và** `ConstraintName = uq_decision_idempotency_key` → đọc lại bản thắng (V-5). Lỗi kết nối/timeout của Npgsql → `PersistenceUnavailableException` → 503.

**Map lỗi** (`GlobalExceptionHandler`, UT-08):

| Exception | HTTP / `code` |
| :--- | :--- |
| `ContractValidationException`, lỗi model binding | 400 `VALIDATION_ERROR` |
| `DomainInvariantException` | 400 `DOMAIN_INVARIANT_VIOLATION` |
| Thiếu header bắt buộc | 400 `MISSING_HEADER` |
| `IdempotencyConflictException` | 409 `IDEMPOTENCY_KEY_REUSE` |
| `DecisionStateConflictException` | 409 với `code` của trạng thái (gói 5.3) |
| `PersistenceUnavailableException` | 503 `PERSISTENCE_UNAVAILABLE` |
| `OperationCanceledException` khi `RequestAborted` | Không ghi response, log mức Information |
| `TimeoutRejectedException` thoát khỏi pipeline | 504 `GATEWAY_TIMEOUT` |
| Còn lại (gồm `InvalidOperationException`, `ArgumentException` không có ngữ nghĩa) | 500 `INTERNAL_ERROR`, ẩn chi tiết |

Message trả về cho 4xx lấy từ exception nghiệp vụ do TADSS tạo; không bao giờ trả message của exception hạ tầng.

**Migration v2** (D-05): một migration EF tạo bảng theo DECISION_PERSISTENCE_INVARIANTS Mục B; trigger và hàm `tadss.reject_mutation()` viết bằng `migrationBuilder.Sql`. Migration chứa thao tác tường minh (`DropTable` bảng v1, `CreateTable`, `CreateIndex`, ràng buộc, hàm, trigger) để lịch sử migration phản ánh đúng quá trình thay đổi schema; không xóa và dựng lại cả schema, không dùng `EnsureDeleted/EnsureCreated`. IT-15 (cùng test của gói 5.1, không phụ thuộc schema cụ thể) áp migration lên DB trống và kiểm tra model không còn thay đổi chưa tạo migration (EF Core 8: `IMigrationsModelDiffer.HasDifferences`).

**Gỡ v1:** controller v1, DTO/domain v1, `Infrastructure/Data/Auditing/` (MediatR), package `MediatR`, `Microsoft.EntityFrameworkCore.InMemory`, `BenchmarkRunner` (D-09), `Consumer/SmashCourtConsumerReferenceTests` (BE không reference SDK; CT-04 nằm ở SmashCourt-BE).

#### 4.2.4. Gói con 5.2d — Clean Architecture và SOLID (duyệt 02/10/2026, cập nhật theo P-03)

**Bối cảnh.** Sau 5.2c chỉ còn code v2 nhưng hướng phụ thuộc còn ngược: port `IDecisionAuditStore` và các record nằm trong `Infrastructure/Data`, nên use case phụ thuộc hạ tầng; exception nghiệp vụ (`DecisionNotFoundException`, `PersistenceUnavailableException`…) nằm trong `Middlewares/`, nên service và store tham chiếu ngược lên tầng HTTP; `Configuration/` gộp options, Swagger filter và composition root; quy ước chỉ nằm trong skill, không có test khóa. Gói này đưa solution về Clean Architecture nhiều project (P-03) **mà không đổi hành vi**.

**Solution đích:**

```text
src/
  Shared.DecisionContractSDK/  Domain + contract dùng chung (chỉ BCL). Không đổi — baseline v2.0.0-draft giữ nguyên
  TADSS.Strategies.{Name}/     Plugin strategy, chỉ phụ thuộc SDK. Không đổi
  TADSS.Application/           Use case, port (interface ra ngoài), exception nghiệp vụ, options của chính sách, AddApplication()
  TADSS.Infrastructure/        Adapter: PostgreSQL (EF Core, Npgsql, migration), cache Redis/Memory, pipeline Polly; (5.5) telemetry; AddInfrastructure(IConfiguration)
  TADSS.Runtime.Api/           Presentation + composition root: controller, middleware, ánh xạ lỗi → ProblemDetails, OpenAPI, Program.cs
tests/
  TADSS.UnitTests/             thư mục con theo project: Sdk/, Strategies/, Application/, Infrastructure/, Api/, Architecture/
  TADSS.IntegrationTests/      giữ nguyên vai trò
```

**Quy tắc phụ thuộc** (compiler cưỡng chế qua `ProjectReference`; UT-14 khóa thêm phần compiler không bắt được):

| Project | Được phụ thuộc | Cấm |
| :--- | :--- | :--- |
| `Shared.DecisionContractSDK` | BCL | mọi project và NuGet (UT-09, có sẵn) |
| `TADSS.Strategies.*` | SDK | mọi thứ khác (UT-09, có sẵn) |
| `TADSS.Application` | SDK; `Microsoft.Extensions.{DependencyInjection,Logging,Options}.Abstractions` (hoặc gói `Options` nếu cần `IValidateOptions`) | Infrastructure, Runtime.Api, Strategies, ASP.NET Core, EF Core, Npgsql, StackExchange.Redis, Polly |
| `TADSS.Infrastructure` | Application, SDK, EF Core/Npgsql, StackExchange.Redis, Polly, `Microsoft.Extensions.*` | Runtime.Api, ASP.NET Core MVC |
| `TADSS.Runtime.Api` | Application, Infrastructure, Strategies, ASP.NET Core | Controller và middleware không dùng kiểu của `TADSS.Infrastructure`; chỉ composition root (`Program.cs`, `Composition/`) được tham chiếu Infrastructure |

**Ánh xạ từ cây sau 5.2c** (tên thư mục con chốt trong plan; bảng này chốt project đích):

| Hiện tại | Project đích |
| :--- | :--- |
| `Services/DecisionService`, `DecisionQueryService`, `RequestHasher`, `Services/Decisions/*` (`ApprovalWindowPolicy`, `DepositCalculator`, `DecisionResponseComposer`) | Application |
| `Services/Strategies/*` (registry, `RegisteredStrategy`, options fingerprint) | Application |
| `IDecisionAuditStore` + record (`DecisionExecutionRecord`, `DecisionDetailRecord`…), interface cache và pipeline, `StrategyDecisionCacheKey` | Application (port) — `CachedStrategyDecision` (định dạng lưu Redis) ở Infrastructure |
| `DecisionNotFoundException`, `IdempotencyConflictException`, `PersistenceUnavailableException` (và `DecisionStateConflictException` ở 5.3) | Application (`Exceptions/`) — `ContractValidationException`, `DomainInvariantException` ở SDK giữ nguyên |
| `Infrastructure/Data/*` (DbContext, entity, configuration, migration, `EfDecisionAuditStore`, mapper, `PersistenceFailureDetector`), `Infrastructure/Caching/*`, `Infrastructure/Resilience/*` (hiện thực) | Infrastructure |
| `Controllers/`, `Middlewares/` (correlation id, `GlobalExceptionHandler`, `ExceptionClassifier`), `InvalidModelStateTranslator`, Swagger filter | Runtime.Api |
| `Configuration/DependencyInjection.cs`, `DecisionRuntimeServiceCollectionExtensions` | Tách: `AddApplication()`, `AddInfrastructure(IConfiguration)`, `AddPresentation()`; `Program.cs` chỉ gọi ba hàm này và cấu hình pipeline |
| `{X}Options` + validator | Project sở hữu chính sách đó, thư mục `Options/` |

**Migration chuyển sang `TADSS.Infrastructure`:** giữ nguyên ID của cả 4 migration (3 v1 + `DecisionPersistenceV2`) để `__EFMigrationsHistory` trên Neon vẫn khớp; cấu hình `MigrationsAssembly`; lệnh `dotnet ef` đổi thành `--project src/TADSS.Infrastructure --startup-project src/TADSS.Runtime.Api` (ghi vào README/skill). IT-15 phải xanh sau khi chuyển. Không tạo migration mới.

**Nguyên tắc mã (điều kiện xong của gói):**

- **SOLID:** `DecisionService` chỉ điều phối — phần "đọc cache → pipeline → ghi cache" tách thành thành phần riêng; `IDecisionAuditStore` chia theo use case (ghi quyết định / đọc quyết định; 5.3 thêm port quản trị) để không phình; port do Application định nghĩa, Infrastructure hiện thực (DIP); thêm strategy = thêm project + một dòng đăng ký (OCP). Interface chỉ thêm ở ranh giới thật (I/O, thời gian, nhiều hiện thực); hàm thuần (`DepositCalculator`, `ApprovalWindowPolicy`, `RequestHasher`…) giữ lớp cụ thể.
- **Không hardcode:** giá trị vận hành điều chỉnh được (giây `Retry-After`, timeout, TTL, tiền tố key cache, tên/nhãn health check…) nằm trong `Options` có validator và `ValidateOnStart`. Hằng số chỉ dành cho giá trị do contract cố định (tên header, mã lỗi, giới hạn 128 ký tự theo INTEGRATION_SPEC) và mỗi hằng chỉ định nghĩa ở một nơi. Kết quả kiểm toán hardcode là một bảng trong plan.
- **Xử lý lỗi một đường:** exception hạ tầng được dịch thành exception của Application ngay tại adapter; Presentation chỉ ánh xạ exception của Application/SDK → ProblemDetails; không `catch` nuốt lỗi; log có cấu trúc kèm correlation id.
- **Luồng request qua các tầng:** đặc tả trong SOFTWARE_ARCHITECTURE_SPECIFICATION Mục 4.4 (sơ đồ tuần tự từ Kestrel → middleware → controller → use case → port → adapter → response, gồm cả nhánh lỗi). Thứ tự middleware cố định, khóa bằng test.

**Việc làm:**

1. **Kiểm toán** SOLID và hardcode từng lớp của Runtime.Api, SDK, Strategies; kết quả là bảng `file → vấn đề → cách sửa → project đích` trong plan 5.2d.
2. **Tách project** theo bảng ánh xạ; cập nhật `TADSS.slnx`, Dockerfile, CI, `InternalsVisibleTo`, `Compile Link` của IT.
3. **Tách trách nhiệm, bỏ hardcode, gom exception và options** theo nguyên tắc trên; mỗi kiểu một file; namespace khớp `project + thư mục`; không còn kiểu/namespace tên "V2".
4. **Tài liệu:** SOFTWARE_ARCHITECTURE_SPECIFICATION Mục 2 (cấu trúc 5 project, quy tắc phụ thuộc) và 4.4 (luồng request); một ADR mới ghi P-03; viết lại skill `tadss-architecture`, `TADSS-Runtime/CLAUDE.md` và bảng project ở `CLAUDE.md` gốc thành bảng "thứ cần tạo → project/thư mục → tên" đầy đủ như BE.
5. **UT-14 — test kiến trúc khóa quy ước:** đồ thị tham chiếu giữa các assembly đúng bảng quy tắc; Application không tham chiếu assembly hạ tầng; controller/middleware không dùng kiểu của Infrastructure; namespace khớp `project + thư mục`; không còn kiểu/namespace tên "V2"; mỗi kiểu public một file; `Options` chỉ ở thư mục `Options/` của project sở hữu; exception nghiệp vụ chỉ ở `Exceptions/`; thứ tự middleware; trần kích thước lớp (ngưỡng chốt trong plan).

**Việc mang từ review cuối 5.2c:** đổi tên entity `DecisionOutcome` (trùng enum SDK) và tên health check "Neon-PostgreSQL" — 5.2d (chỉ đổi tên, không đổi hành vi). Fail-fast khi thiếu connection string, đọc payload audit bằng options nới lỏng (tránh 500 với bản ghi cũ khi DTO thêm trường trước M5), log 503 giả khi client hủy trong pha SQL — 5.3 (đổi hành vi, có test).

**Ràng buộc:** mỗi bước refactor chạy đủ unit + integration test; `baseline_public_api.txt` không đổi (không đổi tên kiểu/namespace SDK); không đổi wire, schema, mã lỗi, migration ID; commit di chuyển/tách tách khỏi commit đổi mã, diff mỗi bước không lẫn thay đổi hành vi.

### 4.3. Gói 5.3 — Quản trị & bảo mật

- `OverrideService`: kiểm tra quyết định tồn tại, `role = PRIMARY`, `status = NEED_APPROVAL`, chưa hết hạn, chưa có outcome → `INSERT decision_overrides`. Tranh chấp đồng thời giải bằng `UNIQUE(decision_id)`: bên thua nhận 409 `ALREADY_OVERRIDDEN`, hoặc 200 với kết quả cũ nếu cùng `X-Idempotency-Key` và cùng payload. `APPROVE` nhận `deposit.rate` hoặc `deposit.amount`; runtime quy đổi phần còn lại bằng `DepositCalculator`.
- `OutcomeService`: `INSERT decision_outcomes`; trùng cùng payload → 200; khác payload → 409 `OUTCOME_ALREADY_RECORDED`.
- `DecisionQueryService`: chi tiết (INTEGRATION_SPEC §3.4) và hàng chờ bằng SQL ở Mục B.5 DECISION_PERSISTENCE_INVARIANTS (`FromSql` có tham số). IT-09 kiểm tra kế hoạch thực thi trên tập ≥ 5.000 bản ghi sinh sẵn (tập nhỏ thì PostgreSQL chọn seq scan, test sẽ không ổn định).
- `ApiKeyAuthenticationMiddleware` (`Middlewares/`): đọc `Tadss:Security:ApiKeys` = danh sách `{ clientId, keySha256 }`; so sánh bằng `CryptographicOperations.FixedTimeEquals`; miễn trừ `/health/*` và Swagger ở môi trường Development; `clientId` gắn vào log scope và trace. Mã băm của khóa thật đặt ở `appsettings.Development.json` (không commit; được copy vào image local theo D-11), user-secrets hoặc biến môi trường.
- `X-Actor-Id` bắt buộc với `POST /override`, lưu vào `actor_id`.

#### 4.3.1. Expose qua ngrok (E-NGROK, T5.30)

> **Phần cấu hình compose (hai file override, chế độ A/B) đã được thay thế bởi D-11 (04/10/2026):** hiện chỉ có một `docker-compose.yml`, không profile, một lệnh `docker compose up -d --build` chạy `tadss-runtime` + `ngrok` + `grafana`, cả ba nối mạng external `smashcourt-network`. Container chạy `Development` như SmashCourt-BE: Neon, Redis Cloud và API key đọc từ `appsettings.Development.json` được copy vào image local (không mount, image không push registry), Swagger công khai khi expose nên bắt buộc API key riêng thật; `.env` chỉ có `NGROK_AUTHTOKEN` (không `NGROK_DOMAIN`). Cách chạy thực tế xem README mục "Chạy bằng Docker và expose qua ngrok". Giữ nguyên bên dưới như bản thiết kế gốc: các câu nói `Staging`, profile `ngrok`, `NGROK_DOMAIN`, biến `TADSS_*`, hai chế độ database và swagger 404 không còn đúng; danh mục NG hiện hành ở Mục 4.13 của EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.

**Mục tiêu:** TADSS chạy trong Docker và được gọi từ bên ngoài qua ngrok giống SmashCourt-BE (BE ở máy khác, demo Swagger), **và** truy cập được database. Khi BE và TADSS cùng Docker network (Phase 7) thì không cần ngrok. Chỉ làm sau khi `X-Api-Key` chạy được, vì v1 hiện không có xác thực.

**Điều kiện an toàn trong mã (làm cùng T5.7):**

- `ApiKeyAuthenticationMiddleware` fail-closed: môi trường khác `Development`/`Testing` mà `Tadss:Security:ApiKeys` rỗng → app **không khởi động**. Không có đường nào để chạy mở hoàn toàn sau tunnel.
- Swagger và miễn trừ API key chỉ ở `Development`; bản expose chạy `ASPNETCORE_ENVIRONMENT=Staging`.
- `ForwardedHeaders` (`X-Forwarded-For/Proto/Host`) cấu hình như SmashCourt-BE (`Program.cs`) để scheme, IP và `UseHttpsRedirection` đúng sau proxy. An toàn vì ở profile này chỉ ngrok chạm được `tadss-runtime`.

**Cấu hình compose** (file override, không sửa `docker-compose.yml` ngoài phần tham số hóa bên dưới):

| File | Nội dung |
| :--- | :--- |
| `docker-compose.ngrok.yml` | Profile `ngrok`. Service `ngrok` (image ghim tag cụ thể, chọn khi viết plan) chạy `http tadss-runtime:8080`; `NGROK_AUTHTOKEN` và `NGROK_DOMAIN` (tùy chọn, tên miền cố định) từ biến môi trường, không ghi vào repo; giao diện kiểm tra `127.0.0.1:4040`. `tadss-runtime` đặt `ASPNETCORE_ENVIRONMENT=Staging`, `Tadss__Security__ApiKeys` từ biến môi trường, và cổng host của `tadss-runtime` chỉ bind `127.0.0.1` (dùng `!override`, cần Docker Compose ≥ 2.24) |
| `docker-compose.neon.yml` | Chỉ dùng khi muốn app trỏ vào Neon (D-04). Đặt `ConnectionStrings__DefaultConnection=${TADSS_DB_CONNECTION}` (Neon, `SSL Mode=Require`), **ép cứng `Tadss__Database__ApplyMigrationsOnStartup=false`** và bỏ phụ thuộc `tadss-postgres`/`toxiproxy`. Tách thành file riêng để không thể bật nhầm cờ migration lên Neon bằng một biến môi trường |

Hai chế độ database:

| Chế độ | Lệnh | Database | Migration |
| :--- | :--- | :--- | :--- |
| A (mặc định) | `docker compose -f docker-compose.yml -f docker-compose.ngrok.yml --profile ngrok up -d --build` | PostgreSQL trong compose (volume bền, qua Toxiproxy) | Tự áp bằng cờ (chỉ DB local) |
| B | thêm `-f docker-compose.neon.yml` | Neon | **Không bao giờ tự áp**; schema `tadss` phải đã migrate, người dùng tự chạy `database update` theo D-05 |

Chuỗi kết nối Neon chỉ nằm trong `.env` của người dùng (không commit, agent không đọc). `.env.example` chỉ thêm tên biến: `NGROK_AUTHTOKEN`, `NGROK_DOMAIN`, `TADSS_DB_CONNECTION`, `TADSS_API_KEY_SHA256`, giá trị mẫu rỗng.

**Bỏ hardcode ở compose (cùng T5.30):** cổng host tham số hóa (`${TADSS_PORT:-8081}`, `${TADSS_PG_PORT:-5433}`, `${TADSS_REDIS_PORT:-6380}`, `${TADSS_TOXIPROXY_PORT:-8474}`, `${TADSS_GRAFANA_PORT:-3001}`) để tránh xung đột cổng; image tag có mặc định `${TADSS_POSTGRES_IMAGE:-postgres:16.15-alpine}`… và một test kiến trúc (UT-09) so khớp mặc định trong compose với hằng của `TadssInfrastructureFixture`, để compose và fixture không lệch phiên bản.

**Tiêu chí chấp nhận (NG):**

| Mã | Kiểm chứng | Ai chạy |
| :--- | :--- | :--- |
| NG-1 | `docker compose config --quiet` hợp lệ cho cả hai chế độ (A, B) | tự động |
| NG-2 | Container thăm dò trong cùng network gọi `http://tadss-runtime:8080`: `/health/ready` 200; `POST /api/v2/decisions` không có key → 401, có key → 200; `/swagger` → 404 ở `Staging`; app **không khởi động** khi thiếu API key ở `Staging` | tự động |
| NG-3 | Truy cập database: sau POST, `GET /api/v2/decisions/{id}` trả đúng quyết định vừa ghi (chứng minh đường ghi và đọc DB), và ở chế độ A đếm bản ghi bằng `psql` trong container PostgreSQL. `/health/ready` kiểm tra kết nối DB nên lỗi kết nối hiện ngay khi khởi động | tự động (chế độ A); chế độ B do người dùng chạy với Neon của mình |
| NG-4 | Qua tunnel thật: `curl https://<domain>/health/ready` 200; POST và GET với `X-Api-Key`; thấy request trong `127.0.0.1:4040` | **người dùng** (cần `NGROK_AUTHTOKEN`) |
| NG-5 | Chỉ `127.0.0.1` truy cập được cổng host của `tadss-runtime`, PostgreSQL, Redis, Toxiproxy (kiểm bằng `docker compose ps`); không có cổng nào bind `0.0.0.0` ở profile `ngrok` | tự động |

**Phía SmashCourt-BE (ghi vào T6.x, không làm ở TADSS):** `Tadss:BaseUrl` trỏ URL ngrok; HttpClient của `TadssClient` gửi `X-Api-Key` và header `ngrok-skip-browser-warning: true` như `FastApiClient` đang làm. URL ngrok bản miễn phí đổi mỗi lần chạy nên dùng `NGROK_DOMAIN` cố định hoặc cập nhật cấu hình BE.

#### 4.3.2. Gói bảo mật (P-04, cùng T5.7)

Mọi giới hạn lấy từ `Tadss:Security:*` có validator; middleware đặt trong `TADSS.Runtime.Api/Middlewares/`; thứ tự trong pipeline: forwarded headers → correlation id → exception handler → security headers → giới hạn kích thước → xác thực API key → rate limiting → routing (khóa bằng test thứ tự middleware của UT-14).

| Mã | Cơ chế | Kiểm chứng |
| :--- | :--- | :--- |
| SEC-1 | **Rate limiting** bằng `RateLimiter` có sẵn của ASP.NET Core 8, phân vùng theo `clientId` của API key (token bucket; cấu hình `PermitLimit`, `ReplenishmentPeriod`, `QueueLimit = 0`); vượt ngưỡng → 429 problem+json `code = RATE_LIMITED` kèm `Retry-After`. `/health/*` không bị giới hạn | IT: vượt ngưỡng → 429 đúng dạng; hai client khác nhau không ảnh hưởng nhau |
| SEC-2 | **Giới hạn kích thước request:** `MaxRequestBodySize` (mặc định 64 KB, cấu hình được); vượt → 413 problem+json `code = PAYLOAD_TOO_LARGE`, không gọi service | IT: body vượt giới hạn → 413; body sát giới hạn → xử lý bình thường |
| SEC-3 | **Security headers** trên mọi response: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, `Cache-Control: no-store` cho `/api/*`; HSTS ngoài `Development`/`Testing`; tắt header `Server` của Kestrel | IT: header có mặt ở 200, 4xx, 5xx |
| SEC-4 | **Fail-closed khi khởi động:** ngoài `Development`/`Testing`, thiếu API key (NG-2) hoặc thiếu connection string → app không khởi động, message không chứa giá trị bí mật | Test khởi động host với cấu hình thiếu → lỗi validate options |
| SEC-5 | **Health tách live/ready:** `/health/live` chỉ kiểm tra process; `/health/ready` kiểm tra PostgreSQL (bắt buộc, lỗi → 503) và Redis (lỗi → `Degraded`, vẫn 200 vì cache có fallback); response không lộ chuỗi kết nối hay message hạ tầng | IT: PostgreSQL bị ngắt qua Toxiproxy → ready 503, live 200 |
| SEC-6 | **Container cứng:** image runtime chạy user non-root (`USER $APP_UID`, user `app` của image .NET 8), stage runtime không chứa SDK, `.dockerignore` loại `.env`/khóa riêng (*sau D-11: `appsettings.Development.json` vào image local như SmashCourt-BE, image không push registry*); compose không bind cổng `0.0.0.0` (NG-5) | Kiểm tra `docker inspect` trong job CI hoặc test script: `Config.User` ≠ root |
| SEC-7 | **Chuỗi cung ứng:** CI chạy `dotnet list package --vulnerable --include-transitive`, có lỗ hổng mức High/Critical → job đỏ; `RestorePackagesWithLockFile` + `packages.lock.json` cho restore tái lập; Dependabot cho NuGet, Docker và GitHub Actions; log không bao giờ ghi giá trị `X-Api-Key` | Job CI; test log: request có `X-Api-Key` không để lại giá trị khóa trong log |

`RATE_LIMITED` và `PAYLOAD_TOO_LARGE` là mã lỗi bổ sung vào INTEGRATION_SPEC §3.8 (contract v2 còn là bản nháp đến M5, người dùng đã duyệt bổ sung ở P-04); JSON schema lỗi và `openapi-v2.yaml` cập nhật cùng gói.

### 4.4. Gói 5.4 — Runtime thích ứng

**Routing** (`TADSS.Application/Routing/`, `Tadss:Routing`; *cập nhật 06/10/2026: không đọc qua `IOptionsMonitor` mà qua port `IRoutingRulesSource` (`Application/Abstractions/Configuration`), adapter `ConfigurationRoutingRulesSource` ở `TADSS.Infrastructure/Configuration` theo dõi `IConfiguration` — Application không giữ `IConfiguration` lúc chạy, khóa bằng UT-14; xem A-02 của plan 5.4*):

```json
"Routing": {
  "Rules": [
    { "Name": "peak-hour", "Strategy": "MockAlternativeDecisionStrategy", "When": { "PeakHour": true } }
  ],
  "DefaultStrategy": "DefaultDecisionStrategy"
}
```

- Điều kiện `When` hỗ trợ: `PeakHour`, `BranchIds`, `Channels`, `Tiers`, `IsGuest`; mọi điều kiện trong một rule là AND; rule đầu tiên khớp được chọn.
- Kiểm tra cấu hình: lúc khởi động sai → fail-fast; khi reload sai → giữ cấu hình cũ, log lỗi, tăng metric `tadss.config.reload_failures`.
- `StrategyResolver` giữ vai trò registry theo tên; `Tadss:Strategy:ActiveStrategyName` bị thay bởi `Routing.DefaultStrategy`.

**Pipeline theo strategy** (sửa V-2): `ResiliencePipelineProvider` tạo và giữ một pipeline Polly cho mỗi `StrategyName`; cấu hình `Tadss:Resilience` giữ nguyên.

**Tiêm lỗi strategy:** `FaultInjectionStrategyDecorator` đặt tại `src/TADSS.Infrastructure/FaultInjection/` (*06/10/2026: đổi tên từ `Testing/` — đây là công cụ tiêm lỗi có chủ đích cho chế độ đánh giá, không phải mã test*), **chỉ** được đăng ký khi `ASPNETCORE_ENVIRONMENT=Testing` và `Tadss:Testing:FaultInjection:Enabled=true`; hồ sơ lỗi (trễ, ném lỗi, bỏ qua token, bận CPU) chọn qua cấu hình. Nằm trong app vì FI-02 phải bật được trong `docker compose` cho LT-03. Test kiến trúc: môi trường `Production` không đăng ký decorator.

**Cách ly strategy không hợp tác** (`IsolatedStrategyInvoker`, FI-05): gọi strategy qua `Task.Run`, chờ bằng `task.WaitAsync(cancellationToken)` → inner timeout cắt được cả strategy bỏ qua token hoặc chạy đồng bộ; task bị bỏ rơi được quan sát lỗi để không sinh `UnobservedTaskException`, và được đếm bằng metric `tadss.strategy.abandoned`. Giới hạn: CPU của task bị bỏ rơi vẫn bị tiêu thụ đến khi nó tự kết thúc — ghi vào Mục 8 tài liệu giao thức đánh giá. Cách phát biểu trong báo cáo: TADSS **giới hạn thời gian chờ** của request và cô lập luồng request khỏi strategy không hợp tác; TADSS **không dừng được** strategy đó.

**Cache + breaker Redis:** `RedisDecisionCache` bọc trong pipeline Polly riêng (timeout `RedisTimeoutMs` + circuit breaker); breaker mở → bỏ qua Redis, dùng `IMemoryCache`, ghi metric `tadss.cache.requests{result="bypass"}`. Lỗi cache không bao giờ kích hoạt fallback của strategy (nguyên tắc 2, RESILIENCE_AND_FAULT_TAXONOMY).

**Shadow** (`Tadss:Shadow`: `Enabled`, `Strategy`, `SampleRate`): sau khi bản ghi PRIMARY đã lưu, đưa công việc vào `Channel<ShadowWorkItem>` có giới hạn (đầy thì bỏ, đếm metric); `ShadowExecutionWorker` (`BackgroundService`) chạy strategy ứng viên qua pipeline của chính nó và ghi bản ghi `role = SHADOW` (không idempotency key, không cache, không approval window ảnh hưởng response). IT-12 chờ bản ghi SHADOW bằng polling có timeout, và có thêm trường hợp strategy shadow ném lỗi/treo: response PRIMARY giống hệt khi tắt shadow, bản ghi PRIMARY vẫn được lưu và không bị hoàn tác; lỗi shadow chỉ thể hiện ở bản ghi SHADOW (`FALLBACK`) hoặc metric.

**Nhiều instance (SCALE-1, P-04):** runtime không giữ trạng thái request trong bộ nhớ ngoài cache L1 và trạng thái breaker. IT dựng **hai host** dùng chung PostgreSQL và Redis của Testcontainers: 50 request song song cùng key chia đều cho hai host → đúng 1 bản ghi, mọi response giống nhau; key cache ghi ở host A được host B đọc (`CACHE_HIT`); request hợp lệ ở host B khi host A đang mở breaker vẫn chạy strategy bình thường.

### 4.5. Gói 5.5 — Observability & chế độ đánh giá

> **Cách chạy theo D-12:** mọi công cụ chứng minh dưới đây (ma trận FI trên Toxiproxy, k6, kịch bản sự cố dưới tải, Schemathesis, Stryker, hai replica cho SCALE-2, dựng báo cáo) được điều phối bởi một container **evaluator** trong profile compose `evaluation` (môi trường **E-EVAL**), chạy bằng `docker compose -p tadss-eval --profile evaluation run --rm --build evaluator` (không phải `up --profile`, xem điều chỉnh ở D-12). Stack gồm TADSS (Release) + PostgreSQL 16 + Redis 7 + Toxiproxy + Grafana, cô lập hoàn toàn khỏi Neon. Evaluator chạy tuần tự (a) test gộp theo `CatalogId`, (b) k6 LT-01/02/05, (c) sự cố dưới tải LT-03 + FI, (d) Schemathesis + Stryker, (e) gom `evidence/<run_id>/` (`report.html`, dữ liệu thô, `manifest.json`), rồi thoát kèm bản tóm tắt; có bản nhanh (đo thật: ~25 phút) và bản đầy đủ (ước tính ≈ 2,8 giờ từ các bước đã đo, khác mức 1,5–2 giờ ghi ban đầu vì LT-05 soak chiếm 61 phút và Stryker ≈ 21 phút; đo trọn vẹn ở gói 5.7). Không còn script rời `tools/chaos/run-fi.sh`, `tools/contract/run-schemathesis.sh`. Test tự động trên CI vẫn dùng Testcontainers (`dotnet test`). Mọi thứ chạy trên một máy nên số liệu là số liệu phòng lab, báo cáo ghi kèm CPU/RAM và liệt kê vào giới hạn. **Hiện trạng:** profile `evaluation` chưa tồn tại; hiện thực ở gói 5.5.

- **OpenTelemetry** (dựng SDK ở `TADSS.Runtime.Api/Composition/TelemetryServiceCollectionExtensions` — ASP.NET Core instrumentation cần framework ASP.NET Core mà UT-14 cấm ở Infrastructure; Application chỉ giữ `TadssActivitySource`/`TadssMetrics` (BCL), Infrastructure giữ `PendingApprovalsMetricRefresher`): bộ phiên bản chốt ở spike S-10 gồm `OpenTelemetry.Extensions.Hosting` 1.15.3, `Exporter.OpenTelemetryProtocol` 1.15.3, `Instrumentation.AspNetCore` 1.15.2, `Npgsql.OpenTelemetry` **8.0.6** (khớp Npgsql 8; bản 10.x không tương thích); bỏ `Instrumentation.Http` vì runtime không gọi HTTP ra ngoài. Redis: span tự tạo quanh lời gọi cache (instrumentation chính thức chỉ có bản prerelease). Meter `TADSS.Runtime` với các metric ở SOFTWARE_ARCHITECTURE_SPECIFICATION 4.5. Serilog thêm sink `Serilog.Sinks.OpenTelemetry`. Tắt được bằng cấu hình; khi không có collector app vẫn chạy bình thường.
- **Dashboard (bắt buộc, P-04):** `docker/grafana/dashboards/tadss-runtime.json` + provisioning trong `docker/grafana/provisioning/`. Panel tối thiểu: tốc độ request và độ trễ p50/p95/p99 theo endpoint; tỉ lệ `status` (APPROVED/NEED_APPROVAL/REJECTED) và `executionType`; tỉ lệ fallback theo `failureReason`; cache hit/miss/bypass; trạng thái breaker theo strategy; số 503/504 theo nguyên nhân; độ trễ strategy; số request theo `service.instance.id`.
  - **OB-1:** test tự động đọc file dashboard, mọi metric được tham chiếu tồn tại trong danh sách tên metric khai báo tại một nơi trong mã (Meter `TADSS.Runtime`).
  - **OB-2:** `docker compose` dựng Grafana, dashboard tự nạp qua provisioning (kiểm bằng HTTP API của Grafana, do evaluator thực hiện ở bước đầu chạy).
  - **OB-4 (bổ sung 05/10/2026):** ở chế độ vận hành (`docker compose up -d --build`), sau vài request tới TADSS, dashboard `tadss-runtime` trên Grafana `127.0.0.1:3001` có dữ liệu (kiểm bằng HTTP API của Grafana/Prometheus) — số liệu hiện ngay khi chạy Docker, không chỉ trong chế độ đánh giá.
  - **OB-3:** sau kịch bản sự cố dưới tải (FI-02/FI-08) ở E-EVAL, JSON snapshot dashboard (không lưu ảnh) và biểu đồ dòng thời gian vẽ từ metric đã thu thể hiện fallback và 503, được lưu vào Evidence Pack.
- **Lưu trữ và hiển thị số liệu (bổ sung 05/10/2026):** không thêm container Prometheus riêng — image `grafana/otel-lgtm` (service `grafana` đã có trong `docker-compose.yml`) gồm sẵn OpenTelemetry Collector, **Prometheus** (metric), **Tempo** (trace), **Loki** (log) và Grafana. TADSS gửi OTLP tới collector (`http://grafana:4317` trong network compose; khi `dotnet run` thì mặc định tắt, bật được bằng cấu hình trỏ `http://localhost:4317`). **k6** đẩy metric của mình (tốc độ request, độ trễ phía client, lỗi) vào cùng Prometheus — output `experimental-prometheus-rw` tới remote-write của Prometheus hoặc output OpenTelemetry tới collector, chốt khi viết plan 5.5 — để tải và độ trễ phía client hiện cùng dashboard với metric phía TADSS. Dashboard và provisioning được nạp ở **cả hai** chế độ Docker: vận hành (`docker compose up -d --build`) và đánh giá (`--profile evaluation`).
- **Chạy nhiều replica (SCALE-2):** một phần của stack đánh giá (không còn profile `scale` riêng): evaluator chạy 2 replica `tadss-runtime` sau reverse proxy (image ghim tag) dùng chung PostgreSQL/Redis; một lượt k6 ngắn qua proxy → không có 5xx ngoài kịch bản, idempotency giữ đúng, dashboard tách được theo `service.instance.id`.
- **Giới hạn scale (SCALE-3):** SOFTWARE_ARCHITECTURE_SPECIFICATION ghi rõ thành phần theo instance (breaker, rate limit SEC-1, cache L1 fallback) và thành phần dùng chung (PostgreSQL, Redis), hệ quả khi scale ngang và hướng mở rộng (rate limit phân tán) — không hiện thực ở Phase 5.
- **FI-01…FI-10** là test xUnit (`tests/TADSS.IntegrationTests/FaultInjection/`), chạy trên Testcontainers + Toxiproxy, có trong CI. Ở E-EVAL, evaluator vừa chạy chúng trong lượt test gộp, vừa bật/tắt toxic qua API Toxiproxy trong kịch bản sự cố dưới tải để thể hiện trên dashboard và ghi `chaos_log` (D3).
- **Schemathesis** (CT-03): evaluator chạy `schemathesis` 4.29.3 (pip, cùng môi trường Python) trên `openapi-v2.yaml` với `X-Api-Key` lấy từ biến môi trường của lượt chạy; kết quả vào `raw/` của Evidence Pack.
- **Stryker.NET** (MT-01…03): `.config/dotnet-tools.json` khai báo `dotnet-stryker` và `dotnet-reportgenerator-globaltool`; mỗi project đích có `stryker-config.json` với `thresholds.break = 70`; phạm vi `mutate` theo cây sau 5.2d: MT-01 `Shared.DecisionContractSDK`, MT-02 `TADSS.Strategies.*` (hai cấu hình con), MT-03 `TADSS.Application/Decisions/**` + `Routing/**` và `TADSS.Infrastructure/Resilience/**` (hai cấu hình con, điểm gộp theo số mutant); cấu hình ở `stryker/`. `dotnet-stryker` chốt **4.16.0** (5.0.0 chỉ có tool net10, không chạy được trong image SDK 8). Báo cáo HTML xuất vào thư mục kết quả của lượt chạy (`evidence/<run_id>/`), không commit (trừ Evidence Pack cuối); evaluator gọi Stryker ở bước (d).

### 4.6. Gói 5.6 — Minh chứng dữ liệu

> **Cách chạy theo D-12:** gói 5.6 chỉ bổ sung **dữ liệu** (replay 215 booking RP-01/RP-02, adapter Hotel Booking Demand PT-01/PT-02) vào cùng Evidence Pack và `report.html` mà evaluator của chế độ đánh giá (mục 4.5) sinh ra. Việc dựng Evidence Pack (`manifest.json`, EV-1…EV-5) và báo cáo là một bước của evaluator, không còn là lượt chạy riêng của `evidence_pack.py`; các module Python dưới đây được evaluator gọi hoặc nhúng. Bước xuất CSV từ SmashCourt vẫn do người dùng tự chạy (không đọc secret).

Công cụ Python đặt tại `tools/evidence/` (`requirements.txt` ghim phiên bản):

| Thành phần | Trách nhiệm |
| :--- | :--- |
| `sql/export_smashcourt_bookings.sql` | Truy vấn **chỉ đọc**; người dùng tự chạy bằng `psql` với thông tin kết nối của mình (agent không đọc secret) → CSV thô ngoài repo |
| `anonymize.py` | HMAC-SHA256 theo DATASET_SCHEMA_SPECIFICATION Mục 4; khóa HMAC từ biến môi trường |
| `replay.py` (RP-01, RP-02) | Dựng request v2 từ CSV ẩn danh; **dời mọi mốc thời gian một bội số 7 ngày** sao cho `requestedAt` gần hiện tại (giữ thứ, giờ địa phương, lead time — sửa V-7); idempotency key `replay:{runId}:{bookingHash}`; ghi D4 kèm chính sách so sánh `STATIC_100` |
| `hotel_adapter.py` (PT-01, PT-02) | Tải dataset theo URL + SHA-256 ghim (không commit dữ liệu gốc nếu giấy phép không cho phép); ánh xạ: `hotel` → `branchId`, múi giờ `Europe/Lisbon`, một item từ ngày đến (14:00) đến ngày đi (12:00), `adr × số đêm` → giá, `EUR`, `is_repeated_guest`/`previous_*` → `history`; thời gian dời theo bội số 7 ngày; ghi D5. Ngưỡng tiền tệ của strategy chỉnh bằng cấu hình chạy riêng — **không sửa mã** |
| `evidence_pack.py` | Dựng `evidence/<run_id>/` (bản cuối chốt vào `docs/research/evidence-packs/{run_id}/`), `manifest.json`, chạy EV-1…EV-5; được evaluator gọi (D-12) |
| `notebooks/evidence_report.ipynb` | Sinh `report.html` (pandas, plotly) từ một Evidence Pack; evaluator dùng cùng bộ sinh báo cáo để xuất `report.html` ở cuối lượt chạy (D-12) |

Test trx gom theo `CatalogId` (D-10) là một nguồn `raw/` của Evidence Pack.

### 4.7. Gói 5.7 — Đóng băng M5

**Rà soát tài liệu (P-04), làm trước khi đóng băng:**

| Mã | Kiểm chứng |
| :--- | :--- |
| DOC-1 | Kiểm kê toàn bộ `docs/**`, `README.md`, `CLAUDE.md`: mỗi file thuộc nhóm **hiện hành** (sở hữu một chủ đề, khớp mã) hoặc **lịch sử** (banner đầu file: đóng băng/hồ sơ, ngày, tài liệu thay thế). Bảng kiểm kê đặt ở `docs/README.md` |
| DOC-2 | Tài liệu hiện hành không còn mô tả như hiện tại những thứ đã gỡ (endpoint v1, MediatR, BenchmarkRunner, EF InMemory, `AuditTimestampInterceptor`…): script grep trong CI, danh sách file lịch sử được miễn trừ |
| DOC-3 | Mọi link nội bộ trong markdown trỏ tới file/anchor tồn tại: job CI |
| DOC-4 | Bảng cấu hình `Tadss:*` trong tài liệu vận hành khớp các `Options` của mã: test so danh sách section/khóa |

Chạy toàn bộ danh mục TADSS: test tự động trên E-TC và **bản đầy đủ** của chế độ đánh giá (E-EVAL, `docker compose --profile evaluation up`, D-12) để lấy số liệu chính thức; Evidence Pack sinh từ lượt chạy đó; đóng băng v2 (baseline public API, `openapi-v2.yaml`, `schemas/v2/`) theo quy tắc quản lý phiên bản ở INTEGRATION_SPEC Mục 6; gỡ ghi chú "bản nháp" khỏi INTEGRATION_SPEC; chốt API contract SmashCourt BE ↔ FE (T5.22) và môi trường staging (T5.23) trong MASTER_EXECUTION_PLAN.

---

## 5. CHUYỂN ĐỔI BỘ TEST v1 → v2

| Nhóm v1 (số test) | Số phận | Mã danh mục v2 |
| :--- | :--- | :--- |
| `Architecture/*` (7) | Giữ, cập nhật cho baseline v2 và test "decorator không có ở Production" | UT-09, CT-02 |
| `Domain/*` (~75) | Viết lại theo domain v2 | UT-01 |
| `Contracts/*` (~78) | Viết lại cho DTO v2 + JSON schema v2 | UT-02, CT-01 |
| `Strategies/*` (19) | Viết lại theo bảng quyết định v2 | UT-03, UT-05 |
| `Services/RequestHasherTests`, `CrossCultureInvariantTests` (7) | Viết lại | UT-06 |
| `Services/DecisionService*`, `StrategyResolverTests` (33) | Viết lại với test double của `IDecisionAuditStore`, cache, pipeline | UT-11, UT-04 |
| `Infrastructure/Resilience/*` (18) | Giữ phần cấu hình; phần lỗi chuyển sang FI | UT-07, FI-01…05 |
| `Infrastructure/Caching/*` (8) | Viết lại cho cache v2 + breaker | UT-13 |
| `Infrastructure/MiddlewareAndInterceptorTests` (10) | Viết lại | UT-08 |
| `Consumer/*` (2), `Benchmarks/*` (15) | Gỡ (D-09; CT-04 ở SmashCourt-BE) | — |
| `TADSS.IntegrationTests/*` (27) | Gói 5.1 chuyển hạ tầng; gói 5.2–5.4 viết lại theo IT/FI | IT-01…15, FI-01…10 |

**Bổ sung vào danh mục** (sửa EXPERIMENTAL_DESIGN Mục 4.1): UT-11 — điều phối `DecisionService` (thứ tự idempotency → cache → pipeline → approval window → audit; fail-closed); UT-12 — `DepositCalculator` (làm tròn theo tiền tệ, biên 0 và 1); UT-13 — cache v2 (key, chính sách theo strategy, breaker Redis bằng test double).

---

## 6. QUY ƯỚC CHUNG

- Tên test `{Method}_{Scenario}_{Expected}`; mỗi test có `[Trait("CatalogId", …)]`; integration test có `[Trait("Category", "Integration")]`.
- Không thêm test dùng EF InMemory; không `Skip`; không nới assertion để xanh.
- Package mới chỉ khai báo version trong `Directory.Packages.props`; SDK và Strategies vẫn 0 NuGet (UT-09).
- Comment và message tiếng Việt như mã hiện có; định danh tiếng Anh.
- Thời gian trong runtime lấy từ `TimeProvider`; test dùng `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`).

### 6.1. Bất biến kiến trúc và test khóa

Điều kiện xong của mỗi gói gồm các bất biến dưới đây (của gói đó và các gói trước) có test khóa và đang xanh. Bảng chỉ ánh xạ sang mã đã có (INV-x, danh mục kiểm thử), không lập hệ mã riêng.

| Bất biến | Test khóa | Gói |
| :--- | :--- | :--- |
| Integration test không dùng EF InMemory | UT-09 (`InfrastructureGuardTests`) | 5.1 |
| SDK chỉ phụ thuộc BCL; Strategies chỉ phụ thuộc SDK, nên không truy cập DB, Redis, HTTP | UT-09 (dependency purity, đã có) | có sẵn |
| Strategy không đọc đồng hồ hệ thống, chỉ dùng `DecisionContext.EvaluatedAtUtc` | UT-03 với `EvaluatedAtUtc` cách xa thời điểm chạy test (strategy đọc đồng hồ hệ thống sẽ cho kết quả sai) | 5.2 |
| Chỉ cache `StrategyDecision`, không cache response | UT-13; IT-13 (hai lần cache hit nhận `decisionId` khác nhau) | 5.2 |
| `request_hash` và `optionsFingerprint` tất định (INV-3) | UT-06, UT-13 | 5.2 |
| Trả thành công ⇔ audit đã lưu (INV-10) | UT-11, FI-08 | 5.1, 5.2 |
| Lỗi PostgreSQL tạm thời → 503; vi phạm ràng buộc không thành 503 | UT-08 | 5.1 |
| Shadow, kể cả khi lỗi, không ảnh hưởng PRIMARY (INV-8) | IT-12 | 5.4 |
| Decorator tiêm lỗi không được đăng ký ở `Production` | UT-09 (test kiến trúc mới) | 5.4 |
| Public API SDK chỉ đổi có chủ đích | CT-02 + quy trình D-06 | có sẵn |
| Không tự áp migration lên Neon | IT-15 (cờ `ApplyMigrationsOnStartup` mặc định `false`) | 5.1 |
| Cấu hình sai làm app không khởi động (`MinItemMinutes ≤ MaxItemMinutes`, quy tắc routing) | UT-01, UT-04 | 5.2, 5.4 |
| Phụ thuộc hướng vào trong: Application không tham chiếu Infrastructure/Runtime.Api/assembly hạ tầng; controller và middleware không dùng kiểu Infrastructure | Compiler (`ProjectReference`) + UT-14 | 5.2d |
| Thứ tự middleware cố định (gồm các middleware bảo mật) | UT-14 | 5.2d, 5.3 |
| Ngoài `Development`/`Testing`, thiếu API key hoặc connection string → app không khởi động | NG-2, SEC-4 | 5.3 |
| Idempotency và cache đúng giữa nhiều instance | SCALE-1 | 5.4 |
| Dashboard chỉ tham chiếu metric có thật | OB-1 | 5.5 |
| Chạy Docker là thấy số liệu TADSS trên Grafana | OB-4 | 5.5 |
| Tài liệu hiện hành không mô tả thứ đã gỡ; link nội bộ hợp lệ | DOC-2, DOC-3 | 5.7 |

**Số lượng test chỉ là chỉ số phụ**, dùng để theo dõi việc chuyển đổi. Báo cáo cuối gói và Evidence Pack trình bày: độ phủ danh mục (mã nào đạt, mã nào chưa), bảng 6.1, kịch bản FI đã chạy, mutation score.

---

## 7. RANH GIỚI VỚI SV2 (EPIC 5C)

| Việc của SV2 | Cần từ SV1 | Thời điểm |
| :--- | :--- | :--- |
| T5.24 review contract v2 (gộp góp ý TR.4) | Bản nháp DTO + `schemas/v2` + `openapi-v2.yaml` | Cuối gói 5.2 |
| T5.25 Playwright trong `smashcourt-fe` | Không phụ thuộc TADSS | W05–W06 |
| T5.26 k6 LT-01…LT-05 (`tests/load/` trong `TADSS-Runtime`) | Stack đánh giá `docker compose --profile evaluation` (gói 5.5, D-12; evaluator chạy k6 LT-01/02/05, LT-04 để Phase 6), endpoint v2 + API key test (gói 5.3); script gửi `X-Correlation-ID` để thỏa EV-2 | Từ W06 (viết script); chạy trong chế độ đánh giá từ 5.5 |
| T5.27 đặc tả E2E, T5.29 review API BE ↔ FE (T5.28 UAT/SUS đã bỏ: không pilot với người dùng thật) | API contract SmashCourt (T5.22) | W06–W08 |

---

## 8. RỦI RO RIÊNG PHASE 5

| Rủi ro | Giảm thiểu |
| :--- | :--- |
| Docker Desktop không chạy / máy chậm làm IT lâu | Container dùng chung một lượt chạy + database theo template; CI là nơi kiểm chứng cuối |
| Viết lại ~400 test chiếm quá nhiều thời gian | Viết test theo bảng quyết định/bất biến (dữ liệu dạng `Theory`), không chép từng test v1; mục tiêu là phủ danh mục, không phải giữ số lượng |
| Neon dùng chung với SmashCourt | Chỉ schema `tadss`; mọi migration lên Neon chờ người dùng cho phép (D-05) |
| Mutation score < 70% ở service lõi | Stryker chạy từ cuối gói 5.2 cho SDK/Strategies để phát hiện sớm |
| Shadow / task bị bỏ rơi gây cạn thread pool khi tải cao | Channel giới hạn + metric; kiểm chứng ở LT-03 |
| Expose TADSS qua ngrok khi chưa có xác thực / lộ Swagger / lộ cổng DB | Chỉ làm sau `X-Api-Key`; app fail-closed khi thiếu API key ngoài Development/Testing; container expose chạy `Development` (D-11) nên Swagger công khai và validator nới lỏng: bắt buộc API key riêng thật trong `appsettings.Development.json`, không dùng `tadss-local-dev-key`; compose bind mọi cổng host vào `127.0.0.1`; NG-2, NG-5 kiểm chứng (Mục 4.13 EXPERIMENTAL_DESIGN) |
| Bật nhầm tự áp migration lên Neon khi chạy compose với Neon | Cờ bị ép `"false"` (literal) trong `docker-compose.yml` (D-11, có test khóa); chuỗi Neon chỉ ở `appsettings.Development.json` của người dùng (được copy vào image local, image không push registry) |
| Token ngrok hoặc chuỗi kết nối Neon lọt vào repo | Token ngrok chỉ ở `.env`, chuỗi Neon chỉ ở `appsettings.Development.json`, cả hai đã gitignore; `.env.example` giá trị rỗng; agent không đọc `.env` |

---

## 9. TÀI LIỆU PHẢI CẬP NHẬT TRONG LÚC HIỆN THỰC

| Tài liệu | Nội dung | Gói |
| :--- | :--- | :--- |
| `INTEGRATION_SPEC.md` | Giá trị `channel` = `ONLINE` \| `WALK_IN` (P-02); giới hạn thời lượng là chính sách runtime cấu hình được (P-01) | 5.2 |
| `SOFTWARE_ARCHITECTURE_SPECIFICATION.md` | Mục 2 cấu trúc solution v2; Mục 4.2 bổ sung `IDecisionAuditStore`, pipeline theo strategy, gỡ MediatR | 5.2, 5.4 |
| `EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md` | UT-11…13; tiêu chí IT-12 thêm "shadow lỗi không ảnh hưởng PRIMARY"; Mục 8 giới hạn của cách ly strategy; Mục 9 ghi BenchmarkRunner đã gỡ | 5.2, 5.4 |
| `ARCHITECTURE_DECISION_RECORDS.md` | Ghi D-02 (gỡ MediatR) vào ADR-008 hoặc ADR mới nếu người dùng muốn | 5.2 |
| `README.md`, `CLAUDE.md`, skill `tadss-architecture` | Lệnh test mới (Docker), cấu trúc v2, route `api/v2` | 5.1, 5.2 |
| `BACKLOG_SPRINTS.md` | Trạng thái task theo gói; T5.30 (E-NGROK) | Mỗi gói; 5.3 |
| `README.md`, `.env.example` | Mục "Expose qua ngrok" (hai chế độ database, biến môi trường, NG-4; *sau D-11: một file compose, `.env` chỉ `NGROK_AUTHTOKEN`*) | 5.3 |
| `INTEGRATION_SPEC.md` | Ghi chú triển khai: `X-Api-Key`, `Tadss:BaseUrl` qua ngrok, header `ngrok-skip-browser-warning` | 5.3 |
| `SOFTWARE_ARCHITECTURE_SPECIFICATION.md`, `ARCHITECTURE_DECISION_RECORDS.md` | Mục 2: solution 5 project và quy tắc phụ thuộc; Mục 4.4: luồng request qua các tầng (gồm nhánh lỗi); ADR mới ghi P-03 | 5.2d |
| Skill `tadss-architecture`, `TADSS-Runtime/CLAUDE.md`, `CLAUDE.md` gốc | Bảng "thứ cần tạo → project/thư mục → tên"; lệnh `dotnet ef` mới | 5.2d |
| `EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md` | Mã mới SEC-1…7, SCALE-1…3, OB-1…4, DOC-1…4; UT-14 theo P-03 | 5.2d (UT-14), 5.3–5.7 |
| `INTEGRATION_SPEC.md` §3.8, schema lỗi, `openapi-v2.yaml` | `RATE_LIMITED` (429), `PAYLOAD_TOO_LARGE` (413), security headers | 5.3 |
| `docs/README.md` | Bảng kiểm kê tài liệu hiện hành/lịch sử (DOC-1) | 5.7 |
| `README.md`, `CLAUDE.md` (repo), `EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`, `DATASET_SCHEMA_SPECIFICATION.md`, kế hoạch (`MASTER_EXECUTION_PLAN`, `BACKLOG_SPRINTS`, `PROJECT_WBS_MAPPING`, `THESIS_DEVELOPMENT_PLAYBOOK`), `PROPOSAL_DO_AN_2.md` | Ba chế độ chạy và chế độ đánh giá một lệnh (D-12); E-LOCAL → E-EVAL; evaluator thay script rời. Khi gói 5.5 hiện thực xong: bỏ chú thích "chưa hiện thực" | 5.5 (đã cập nhật phần kế hoạch 04/10/2026) |
| `INTEGRATION_SPEC.md`, `RESILIENCE_AND_FAULT_TAXONOMY.md`, `SOFTWARE_ARCHITECTURE_SPECIFICATION.md`, `EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`, `MASTER_EXECUTION_PLAN.md`, `PROPOSAL_DO_AN_2.md`, `PROJECT_BLUEPRINT_AND_SYSTEM_OVERVIEW.md`, `README.md`, `CLAUDE.md` (repo và gốc), skill `tadss-architecture` | Fallback = `APPROVED` cọc 100% (D-13); FI-02/03/04 và SC2 kỳ vọng kết quả mới | Đã cập nhật 04/10/2026 |
