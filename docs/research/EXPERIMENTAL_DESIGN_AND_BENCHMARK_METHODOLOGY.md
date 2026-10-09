# GIAO THỨC ĐÁNH GIÁ, DANH MỤC KIỂM THỬ & KPI — v2.0

> **Vai trò của tài liệu:** nguồn duy nhất về **danh mục kiểm thử (test catalogue)**, **định nghĩa KPI SC1–SC8**, môi trường đo và giao thức thực nghiệm của Đồ án 2. Lịch xây dựng và chạy: [`MASTER_EXECUTION_PLAN.md`](../planning/MASTER_EXECUTION_PLAN.md). Định dạng dữ liệu minh chứng: [`DATASET_SCHEMA_SPECIFICATION.md`](DATASET_SCHEMA_SPECIFICATION.md).

---

## 0. NGUYÊN TẮC

1. **Hạ tầng thật:** mọi kiểm thử dùng làm minh chứng chạy trên PostgreSQL 16 và Redis 7 thật (container), giao tiếp qua HTTP thật. Lỗi hạ tầng được tiêm ở tầng mạng bằng Toxiproxy. EF Core InMemory và độ trễ giả lập bằng `Task.Delay` không được dùng làm minh chứng.
2. **Toàn bộ danh mục là bắt buộc:** mọi test case dưới đây phải được hiện thực, chạy và đạt. Không có phân tầng ưu tiên.
3. **Không định trước kết quả:** ngưỡng KPI là mục tiêu thiết kế; kết quả đo được báo cáo trung thực kể cả khi không đạt, kèm phân tích nguyên nhân.
4. **Truy vết được:** mỗi lượt chạy có `run_id`, commit, cấu hình, phiên bản image, thông số máy; dữ liệu thô được lưu trong Evidence Pack.
5. **Nêu rõ giới hạn:** báo cáo luôn ghi môi trường đo, cỡ mẫu và các mối đe dọa tính hợp lệ (Mục 8).

---

## 1. LIÊN KẾT CÂU HỎI NGHIÊN CỨU — KPI — NHÓM KIỂM THỬ

| Câu hỏi (xem Playbook) | KPI | Nhóm kiểm thử |
| :--- | :--- | :--- |
| **RQ1** Resilience & toàn vẹn giao dịch | SC1, SC2, SC5, SC6 | FI, LT, E2E, BE-IT |
| **RQ2** Governance & khả năng kiểm toán | SC3, SC4 | IT, CT, E2E |
| **RQ3** Chất lượng tích hợp & quyết định trên dữ liệu thật | SC1 (E2E) | LT-04, E2E, RP |
| Độc lập domain | SC7 | PT |
| Chất lượng bộ kiểm thử | SC8 | Toàn bộ, MT |

---

## 2. MÔI TRƯỜNG THỰC NGHIỆM

| Mã | Môi trường | Thành phần | Dùng cho |
| :--- | :--- | :--- | :--- |
| **E-TC** | Testcontainers (trong `dotnet test`) | PostgreSQL 16, Redis 7, Toxiproxy | UT, IT, CT-01/02/04, BE-IT, một phần FI |
| **E-EVAL** | Chế độ đánh giá một lệnh: `docker compose -p tadss-eval --profile evaluation run --rm --build evaluator` (profile `evaluation` của `docker-compose.yml`; lệnh `run` thay cho `up` để không khởi động service vận hành không có profile, xem spike S-5 ở báo cáo gói 5.5; **đã hiện thực ở gói 5.5**; thay cho môi trường E-LOCAL/profile `lab` ghi trong bản trước, xem D-12 ở `spec/PHASE5_DESIGN_SPEC.md`). Cô lập hoàn toàn, không chạm Neon | TADSS (build Release), PostgreSQL 16, Redis 7, Toxiproxy, `grafana/otel-lgtm` và container **evaluator** điều phối cả chiến dịch (Mục 6.1); SmashCourt-BE/FE nối thêm khi chạy LT-04 (Phase 6) | FI, LT-01/02/03/05, CT-03, MT, SCALE-1, SCALE-2; RP, PT (dữ liệu của gói 5.6) |
| **E-STAGING** | Môi trường tích hợp trên máy nhóm, chạy bằng Docker Compose (chốt ở W08) | SmashCourt FE/BE + TADSS + PostgreSQL + Redis + VNPay sandbox | E2E và LT-04 trên hệ thống đã tích hợp |

Ba chế độ chạy của repo (D-12): **dev hằng ngày** `dotnet run` (Neon + Redis Cloud, chỉ app + Swagger, không Docker/Grafana); **vận hành / demo** `docker compose up -d --build` (TADSS + ngrok + Grafana); **đánh giá** `docker compose -p tadss-eval --profile evaluation run --rm --build evaluator` (E-EVAL). Chỉ chế độ đánh giá tạo số liệu dùng làm minh chứng; test tự động trong CI vẫn chạy ở E-TC (`dotnet test`).

Mỗi lượt chạy ghi lại: CPU, RAM, hệ điều hành, phiên bản Docker, digest của image, commit của cả 3 repo và file cấu hình đã dùng.

---

## 3. BỘ CÔNG CỤ

| Công cụ | Mục đích | Vị trí | Nhóm |
| :--- | :--- | :--- | :--- |
| xUnit + FluentAssertions + Moq | Unit test | TADSS, SmashCourt-BE | UT, BE-UT |
| **Testcontainers for .NET** | PostgreSQL/Redis/Toxiproxy thật trong test | TADSS, SmashCourt-BE | IT, BE-IT |
| `Microsoft.AspNetCore.Mvc.Testing` | Chạy API trong test qua HTTP pipeline thật | TADSS, SmashCourt-BE | IT, BE-IT |
| **WireMock.Net** | Giả lập TADSS cho BE-IT, response kiểm tra theo JSON schema v2 | SmashCourt-BE | BE-IT, CT-04 |
| **JsonSchema.Net** | Kiểm tra response theo JSON schema v2 | TADSS, SmashCourt-BE | CT-01, CT-04 |
| **Schemathesis** 4.29.3 (pip, trong image evaluator) | Sinh request tự động từ OpenAPI v2 để dò lỗi contract | E-EVAL | CT-03 |
| **Stryker.NET** (`dotnet-stryker` 4.16.0, tool cục bộ trong `.config/dotnet-tools.json`; 5.0.0 chỉ chạy trên SDK 10 nên không dùng được trong image SDK 8) | Mutation testing — đo khả năng phát hiện lỗi của bộ test | TADSS | MT |
| coverlet + **ReportGenerator** 5.5.11 | Độ bao phủ mã (báo cáo HTML) | TADSS, SmashCourt-BE | Bổ trợ MT |
| **Toxiproxy** | Tiêm lỗi mạng: ngắt, trễ, giới hạn băng thông, reset kết nối | E-TC, E-EVAL | FI |
| **k6** 2.3.0 | Tải: TADSS trực tiếp (E-EVAL) và E2E qua SmashCourt-BE (LT-04, Phase 6); xuất summary JSON và đẩy metric vào Prometheus của `grafana/otel-lgtm` (hiện cùng dashboard TADSS) | E-EVAL | LT |
| **OpenTelemetry .NET** + **`grafana/otel-lgtm`** (gồm Collector, Prometheus, Tempo, Loki, Grafana) | Trace, metric, log tập trung; dashboard Grafana làm minh chứng trực quan; có dữ liệu ở cả chế độ vận hành `docker compose up` | E-EVAL, E-STAGING | FI, LT, E2E |
| **Playwright** | E2E UI: video, trace viewer, HTML report | smashcourt-fe | E2E, FE |
| **MSW** (Mock Service Worker) | Mock API cho FE trong Phase 6 theo API contract đã chốt | smashcourt-fe | FE |
| **Bộ sinh báo cáo Python** (`tools/evidence/report/`, Jinja2 + biểu đồ SVG tự vẽ) | Sinh `report.html` tự chứa từ Evidence Pack (evaluator gọi ở bước e; notebook của 5.6 dùng lại cùng bộ sinh) | TADSS `tools/evidence/` | Toàn bộ |

**Tiêm lỗi cho strategy:** dùng decorator chỉ đăng ký trong môi trường kiểm thử (bật qua cấu hình `Testing`), không có trong cấu hình production. Lỗi hạ tầng (Redis, PostgreSQL, mạng giữa BE và TADSS) luôn tiêm bằng Toxiproxy.

---

## 4. DANH MỤC KIỂM THỬ

Cột **Xây dựng** là phase viết test; mọi test case được **chạy lại toàn bộ ở Phase 8**.

**Tổng quan nội dung kiểm thử** (cùng nội dung với bảng kế hoạch kiểm thử trong [`PROPOSAL_DO_AN_2.md`](../planning/PROPOSAL_DO_AN_2.md) và [`MASTER_EXECUTION_PLAN.md`](../planning/MASTER_EXECUTION_PLAN.md); chi tiết từng test case ở các mục 4.1–4.12). Toàn bộ kiểm thử do nhóm tự thực hiện ở mức phát triển, **không có pilot với người dùng thật**; chất lượng quyết định được đánh giá bằng dữ liệu import (RP, PT).

| Tên | Nội dung kiểm thử | Công cụ |
| :--- | :--- | :--- |
| Kiểm thử đơn vị (UT) | Từng hàm, từng lớp nhỏ khi chạy riêng lẻ: tính tiền cọc theo hạng khách và khung giờ, cửa sổ phê duyệt, chọn strategy, băm request làm khóa cache, chuyển lỗi thành mã lỗi. Đạt khi 100% test case đúng (SC8) | xUnit, FluentAssertions, Moq |
| Đánh giá chất lượng bộ test (MT) | Cài lỗi giả vào mã nguồn rồi chạy lại bộ test; test phải báo sai thì mới tính là bắt được lỗi. Đạt khi mutation score ≥ 70% (SC8) | Stryker.NET |
| Kiểm thử tích hợp (IT) | Gọi API với PostgreSQL/Redis thật trong container: gửi trùng → một quyết định; hai Manager duyệt cùng đơn → một người thành công (SC4); mỗi response 200 có đúng một bản ghi `PRIMARY`, sửa/xóa lưu vết bị chặn (SC3) | xUnit, Testcontainers |
| Kiểm thử hợp đồng API (CT) | So response với JSON Schema; tự sinh hàng nghìn request từ OpenAPI (thiếu trường, giá trị biên, ký tự lạ). Đạt khi 0 lỗi 5xx, 0 response sai schema | JsonSchema.Net, Schemathesis |
| Kiểm thử chịu lỗi (FI-01…10) | Strategy chậm/treo/lỗi → `APPROVED` cọc 100% trong ≤ 500 ms, không vào hàng chờ (SC2); mất PostgreSQL → 503, không bản ghi dở dang; mất Redis → vẫn phục vụ; client hủy request → không hỏng dữ liệu; hết sự cố → tự hồi phục | xUnit, Toxiproxy |
| Kiểm thử tải (LT-01…03, LT-05) | 50 RPS × 10 phút; tăng dần 10 → 400 RPS; 50 RPS × 60 phút; tải khi có sự cố; theo dõi trên Grafana. Đạt khi p95 < 150 ms (SC1), p99 ≤ 700 ms khi có sự cố (SC2), không rò rỉ | k6, Grafana |
| Kiểm thử bảo mật (SEC-1…7) | Thiếu/sai API key → 401; vượt giới hạn tốc độ → 429; request quá lớn → 413; log không chứa bí mật | xUnit, k6 |
| Feature Flag & toàn vẹn đơn đặt sân (FI-11…14) | Tắt cờ `EnableTadssDecision` khi đang đặt sân: hiệu lực < 2 giây, mọi đơn cọc 100% (SC5); truy vấn bất biến trên DB SmashCourt sau mỗi kịch bản: 0 booking hỏng (SC6) | xUnit, SQL |
| Kiểm thử đầu–cuối trên giao diện (E2E, LT-04) | Tự động thao tác trên trình duyệt qua FE → BE → TADSS: đặt sân → mức cọc → VNPay sandbox → Manager duyệt / từ chối / hết hạn. Đạt khi luồng chính chạy thông, p95 tạo booking < 1000 ms (SC1) | Playwright, k6 |
| Đánh giá trên dữ liệu đặt sân thật (RP-01…02) | Import 215 booking SmashCourt (ẩn danh) qua TADSS; so sánh với chính sách cọc 100%: tỷ lệ duyệt ngay / cần duyệt, mức cọc theo hạng khách và khung giờ | Script Python |
| Đánh giá độc lập nghiệp vụ (PT-01…02) | Import ≥ 10.000 đơn Hotel Booking Demand, chuyển sang request v2 rồi gửi vào TADSS. Đạt khi 0 lỗi contract, 0 thay đổi mã runtime/SDK (SC7) | Script Python |

### 4.1. UT — Unit test (TADSS)

| ID | Phạm vi | Tiêu chí đạt | Xây dựng |
| :--- | :--- | :--- | :--- |
| UT-01 | Bất biến domain v2 (items, khách vãng lai, thời lượng, giá, lịch sử) | Mọi bất biến có test biên đúng/sai | P5 |
| UT-02 | Serialization contract v2 (round-trip, enum strict, `additionalProperties`) | Round-trip không mất dữ liệu; giá trị lạ bị từ chối | P5 |
| UT-03 | Strategies v2: bảng quyết định theo hạng, khung giờ, ngưỡng giá trị lớn | Mọi ô trong bảng quyết định có test, gồm biên | P5 |
| UT-04 | Quy tắc định tuyến strategy theo request | Đúng strategy cho từng quy tắc; quy tắc mặc định | P5 |
| UT-05 | Múi giờ chi nhánh & ranh giới cao điểm (16:59/17:00/20:59/21:00, cuối tuần, đổi ngày) | Đúng với `Asia/Ho_Chi_Minh` và một múi giờ khác | P5 |
| UT-06 | `RequestHasher` tất định, không phụ thuộc múi giờ máy chủ | Cùng hash khi chạy với TZ = UTC và UTC+7; khác hash khi đổi dữ liệu nghiệp vụ | P5 |
| UT-07 | Cấu hình pipeline Polly (thứ tự, tham số, ngân sách) | Khớp cấu hình; ngân sách ≤ outer timeout | P1–P5 |
| UT-08 | Map exception → HTTP/`code` | Lỗi server không thành 4xx | P5 |
| UT-09 | Kiểm thử kiến trúc: dependency purity, public API baseline v2 | Đạt | P2, P5 |
| UT-10 | Quy tắc approval window | Đổi `NEED_APPROVAL` → `APPROVED` 100% đúng điều kiện | P5 |
| UT-11 | Điều phối `DecisionService` bằng test double (thứ tự idempotency → cache → pipeline → approval window → audit; fail-closed; lỗi cache chỉ là miss) | Đúng thứ tự gọi; lỗi ghi audit lan ra và không có response; idempotency trả lại bản cũ hoặc 409; không gọi strategy khi request sai | P5 |
| UT-12 | `DepositCalculator` (làm tròn theo tiền tệ, biên 0 và 1) | `amount = round(totalPrice × rate)` đúng đơn vị nhỏ nhất của từng tiền tệ, gồm biên rate 0 và 1 | P5 |
| UT-13 | Cache v2: key (tên, phiên bản strategy, `optionsFingerprint`, `request_hash`), chính sách theo strategy, chỉ lưu `StrategyDecision`, lỗi cache bị cô lập (breaker Redis bằng test double thuộc gói 5.4) | Key đổi khi đổi tham số strategy hoặc lịch cao điểm; lỗi/payload hỏng chỉ là miss; `request_hash` và fingerprint tất định (INV-3) | P5 |
| UT-14 | Kiến trúc và quy ước (gói 5.2d, Clean Architecture nhiều project): đồ thị tham chiếu SDK ← Application ← Infrastructure ← Runtime.Api đúng quy tắc; Application không tham chiếu assembly hạ tầng; controller/middleware không dùng kiểu Infrastructure (kể cả qua `global using`/`<Using>`); Application và Infrastructure không có `FrameworkReference`/Web SDK; namespace khớp project + thư mục; không còn kiểu/namespace "V2"; mỗi kiểu public một file; `Options` chỉ ở `Options/`, exception nghiệp vụ chỉ ở `Exceptions/`; port chỉ khai báo ở `Application/Abstractions`, EF Core chỉ trong `Persistence/`; bất biến tỷ lệ cọc 4 chữ số thập phân; mọi options có `SectionName` (tìm bằng reflection) có validator đăng ký cùng `ValidateOnStart` (không có ngoại lệ, kể cả `DatabaseOptions`); thứ tự middleware, gồm các middleware bảo mật (xác thực API key, giới hạn kích thước, rate limiter, security headers), qua hành vi quan sát được; trần 250 dòng mỗi file nguồn (trừ `Migrations/`) | Vi phạm quy ước làm test đỏ; thêm kiểu sai chỗ bị phát hiện ngay | P5 |
| UT-15 | Quy tắc quản trị của override và outcome bằng test double (gói 5.3): thứ tự kiểm tra, idempotency theo payload, quy đổi cọc `rate`/`amount`, hạn duyệt, ranh giới với bản `SHADOW` | Mỗi quy tắc và mỗi nhánh lỗi (400/404/409) có test; không gọi lưu trữ khi từ chối | P5 |

### 4.2. IT — Integration test trên hạ tầng thật (TADSS, E-TC)

| ID | Kịch bản | Tiêu chí đạt |
| :--- | :--- | :--- |
| IT-01 | Đánh giá hợp lệ | 200; đúng 1 bản ghi `decision_executions` có snapshot request; response hợp lệ theo schema |
| IT-02 | Gửi lại cùng key + cùng payload | Trả kết quả cũ; vẫn 1 bản ghi; strategy không chạy lại |
| IT-03 | Cùng key + payload khác | 409 `IDEMPOTENCY_KEY_REUSE` |
| IT-04 | 50 request song song cùng key | Đúng 1 bản ghi; 50 response giống nhau (đi qua nhánh xử lý lỗi `23505`) |
| IT-05 | `UPDATE`/`DELETE` trực tiếp vào bảng audit | Trigger từ chối; dữ liệu không đổi |
| IT-06 | Override APPROVE kèm mức cọc | Có bản ghi `decision_overrides`; chi tiết hiển thị override; hàng chờ không còn quyết định đó |
| IT-07 | 20 override song song trên cùng quyết định | Đúng 1 thành công; còn lại 409 `ALREADY_OVERRIDDEN` |
| IT-08 | Override quyết định không chờ duyệt / đã hết hạn | 409 `NOT_PENDING_APPROVAL` / `APPROVAL_EXPIRED` |
| IT-09 | Hàng chờ: thay thế bản cũ cùng reservation, lọc `branchId`, loại bản hết hạn, dùng partial index | Kết quả đúng; `EXPLAIN` dùng index |
| IT-10 | Ghi outcome; ghi trùng | 200; trùng khác payload → 409 |
| IT-11 | Thiếu/sai `X-Api-Key`; override thiếu `X-Actor-Id` | 401; 400 |
| IT-12 | Shadow mode | Bản ghi `SHADOW` được lưu; response không đổi; không vào hàng chờ; strategy shadow ném lỗi hoặc treo: response `PRIMARY` giống hệt khi tắt shadow, bản `PRIMARY` vẫn lưu, lỗi chỉ thể hiện ở bản `SHADOW` (`FALLBACK`) hoặc metric |
| IT-13 | Cache trên Redis thật; strategy tắt cache | Có `CACHE_HIT` khi được phép; strategy tắt cache không bao giờ hit |
| IT-14 | Định tuyến: request giờ cao điểm / thấp điểm theo giờ chi nhánh | Đúng strategy theo quy tắc |
| IT-15 | Migration trên DB trống; model snapshot | Áp dụng thành công; không có thay đổi chưa tạo migration |

### 4.3. CT — Contract

| ID | Kịch bản | Tiêu chí đạt |
| :--- | :--- | :--- |
| CT-01 | Mọi response trong IT hợp lệ theo JSON schema v2 | 0 vi phạm |
| CT-02 | Public API surface SDK so với baseline v2 | Khớp |
| CT-03 | Schemathesis trên `openapi-v2.yaml` (E-EVAL, do evaluator chạy) | 0 lỗi 5xx; 0 response sai schema |
| CT-04 | `TadssClient` của SmashCourt-BE với response mẫu hợp lệ theo schema v2 | Parse đúng mọi trạng thái và mã lỗi |

### 4.4. MT — Mutation testing (Stryker.NET)

| ID | Phạm vi | Tiêu chí đạt |
| :--- | :--- | :--- |
| MT-01 | `Shared.DecisionContractSDK` | Mutation score ≥ 70% |
| MT-02 | `TADSS.Strategies.*` | Mutation score ≥ 70% |
| MT-03 | Service lõi của runtime (`DecisionService`, routing, pipeline, approval window) | Mutation score ≥ 70% |

### 4.5. FI — Fault injection / chaos

| ID | Lỗi tiêm | Cách tiêm | Kết quả kỳ vọng |
| :--- | :--- | :--- | :--- |
| FI-01 | Strategy chậm hơn inner timeout ở lần thử đầu | Decorator kiểm thử | Retry thành công; `AUTOMATED`; không fallback |
| FI-02 | Strategy luôn chậm | Decorator kiểm thử | `APPROVED` cọc 100% (`deposit.amount = totalPrice`, không hạn duyệt), `FALLBACK`, `FALLBACK_STRATEGY_TIMEOUT` / `STRATEGY_TIMEOUT`; thời gian pipeline ≤ 500ms |
| FI-03 | Strategy ném lỗi | Decorator kiểm thử | Fallback `APPROVED` cọc 100%, `FALLBACK_STRATEGY_EXCEPTION` / `STRATEGY_EXCEPTION`; không lộ message lỗi; quyết định không vào hàng chờ duyệt, override → 409 `NOT_PENDING_APPROVAL` |
| FI-04 | Lỗi liên tục | Decorator kiểm thử | Circuit mở, fast-fail `APPROVED` cọc 100% với `FALLBACK_CIRCUIT_OPEN` (không đẩy đơn sang Manager); sau `BreakDuration` half-open → closed khi strategy hồi phục |
| FI-05 | Strategy không hợp tác (bỏ qua `CancellationToken`, bận CPU) | Decorator kiểm thử | Vẫn bị cắt trong ngân sách; request không bị treo |
| FI-06 | Redis mất kết nối | Toxiproxy (ngắt) | Không fallback strategy; độ trễ tăng có giới hạn nhờ breaker Redis |
| FI-07 | Redis trễ 200ms | Toxiproxy (latency) | Bỏ qua cache; độ trễ có giới hạn |
| FI-08 | PostgreSQL mất kết nối | Toxiproxy (ngắt) | 503 `PERSISTENCE_UNAVAILABLE`; không có bản ghi dở dang |
| FI-09 | PostgreSQL trễ 300ms | Toxiproxy (latency) | Phản hồi đúng; không trùng bản ghi; độ trễ được ghi nhận |
| FI-10 | Client hủy request giữa chừng | Hủy HTTP | Không tạo bản ghi audit |
| FI-11 | TADSS dừng hoàn toàn (E2E) | Dừng container | SmashCourt áp 100%; booking hợp lệ; không lỗi 500 cho khách |
| FI-12 | TADSS chậm hơn `TimeoutMs` của BE | Toxiproxy (BE → TADSS) | Áp 100%; retry dùng cùng idempotency key; không có quyết định trùng |
| FI-13 | Tắt `EnableTadssDecision` khi đang chạy | Sửa cấu hình | Có hiệu lực < 2 giây; booking mới không gọi TADSS và áp 100% |
| FI-14 | Đứt mạng BE ↔ TADSS giữa chừng | Toxiproxy (reset kết nối) | Booking nhất quán; không treo slot |

Sau **mỗi** kịch bản FI ở mức E2E (FI-11…FI-14), chạy bộ truy vấn bất biến booking (Mục 5, SC6).

### 4.6. LT — Tải (k6, E-EVAL)

| ID | Hồ sơ tải | Đo |
| :--- | :--- | :--- |
| LT-01 | TADSS, tốc độ đến cố định 50 RPS trong 10 phút, 3 lần lặp | p50/p95/p99, tỷ lệ lỗi (SC1) |
| LT-02 | TADSS, tăng dần 10 → 400 RPS | Điểm gãy: RPS lớn nhất khi p95 < 150ms và lỗi < 1% |
| LT-03 | TADSS 50 RPS khi FI-02 đang bật | Thời gian pipeline, p99 endpoint (SC2) |
| LT-04 | E2E qua SmashCourt-BE: 20 VU tạo booking trên các slot khác nhau trong 10 phút | Độ trễ API tạo booking, tỷ lệ thành công, tính đúng của mức cọc |
| LT-05 | TADSS soak 50 RPS trong 60 phút | Trôi độ trễ, bộ nhớ, kết nối DB |

Pha warm-up 1 phút bị loại khỏi thống kê. Kết quả kèm dashboard Grafana cùng khoảng thời gian. Evaluator của chế độ đánh giá (Mục 6.1) chạy LT-01, LT-02, LT-05 và LT-03 (kèm tiêm lỗi); LT-04 (E2E qua SmashCourt-BE) để Phase 6 khi BE tích hợp TADSS. Ở **bản nhanh** của chế độ đánh giá, thời lượng các kịch bản tải được rút ngắn nên số liệu chỉ để diễn tập, không dùng làm số liệu chính thức.

### 4.7. BE — SmashCourt-BE (Testcontainers + WireMock.Net)

| ID | Kịch bản | Tiêu chí đạt |
| :--- | :--- | :--- |
| BE-UT-01 | `TadssClient`: header, timeout, map lỗi, retry đúng một lần với cùng key | Đạt |
| BE-UT-02 | Ánh xạ booking → request v2 (nhiều sân, khách vãng lai, hạng, lịch sử) | Đạt, hợp lệ theo schema |
| BE-IT-01 | Script DB mới (enum `PENDING_APPROVAL`, trường cọc) trên DB thật | Áp dụng thành công; luồng cũ vẫn đạt |
| BE-IT-02 | Luồng APPROVED | Số tiền VNPay = `deposit.amount`; hóa đơn lưu cọc và `decisionId` |
| BE-IT-03 | Luồng NEED_APPROVAL | Booking `PENDING_APPROVAL`; slot giữ tới `approvalExpiresAt` |
| BE-IT-04 | Flag tắt / TADSS lỗi 5xx / timeout / 401 | Áp 100%; booking hợp lệ |
| BE-IT-05 | Hàng chờ cho Manager | Chỉ thấy chi nhánh của mình; Owner thấy tất cả |
| BE-IT-06 | Duyệt / từ chối | Gửi override với `X-Actor-Id` từ JWT; xử lý 409 |
| BE-IT-07 | Sau duyệt | Tạo link thanh toán cọc; gửi thông báo |
| BE-IT-08 | Job hết hạn duyệt | Hủy booking, giải phóng slot, gửi outcome `APPROVAL_EXPIRED`; không tự duyệt |
| BE-IT-09 | Job outcome | Gửi đúng outcome cho mỗi trạng thái kết thúc, đúng một lần |
| BE-IT-10 | Checkout thu phần còn lại; hủy theo chính sách | Số tiền đúng; hoàn tiền theo chính sách hiện có |

### 4.8. FE & E2E (smashcourt-fe, Playwright)

| ID | Kịch bản | Môi trường |
| :--- | :--- | :--- |
| FE-01 | `tsc` + lint + build | CI |
| FE-02 | Toàn bộ E2E-01…E2E-10 chạy trên mock MSW | P6 |
| E2E-01 | Khách đặt sân → APPROVED → trang thanh toán đúng số tiền cọc → VNPay sandbox → booking xác nhận | E-STAGING |
| E2E-02 | Khách đặt sân → NEED_APPROVAL → trang chờ; Manager duyệt kèm mức cọc → khách nhận link → thanh toán | E-STAGING |
| E2E-03 | Manager từ chối → khách thấy bị từ chối; slot được giải phóng | E-STAGING |
| E2E-04 | Hết hạn duyệt → booking bị hủy; không tự duyệt | E-STAGING |
| E2E-05 | Tắt flag → thanh toán 100% | E-STAGING |
| E2E-06 | TADSS dừng → thanh toán 100%; UI không lỗi | E-STAGING |
| E2E-07 | Hai Manager duyệt cùng đơn → một thành công, một thấy thông báo xung đột | E-STAGING |
| E2E-08 | Manager chỉ thấy đơn chờ của chi nhánh mình | E-STAGING |
| E2E-09 | Staff checkout thu phần còn lại | E-STAGING |
| E2E-10 | Hàng chờ cập nhật realtime khi có đơn mới (SignalR) | E-STAGING |

Mỗi kịch bản E2E lưu video, trace Playwright và correlation id để tra trên Grafana.

### 4.9. RP — Replay dữ liệu SmashCourt

| ID | Kịch bản | Đầu ra |
| :--- | :--- | :--- |
| RP-01 | Xuất 215 booking bằng truy vấn chỉ đọc, ẩn danh hóa, dựng request v2, chạy qua TADSS với cấu hình định tuyến chuẩn | Phân bố trạng thái, mức cọc theo hạng/khung giờ; 0 lỗi contract |
| RP-02 | So sánh chính sách cọc 100% tĩnh với chính sách v2 trên cùng tập booking | Cọc trung bình, tỷ lệ cần duyệt, số tiền giữ được trên các booking có nhãn NO_SHOW (chỉ mang tính mô tả, xem Mục 8) |

### 4.10. PT — Độc lập domain (Hotel Booking Demand)

| ID | Kịch bản | Tiêu chí đạt |
| :--- | :--- | :--- |
| PT-01 | Adapter ánh xạ ≥ 10.000 đơn đặt phòng khách sạn (múi giờ `Europe/Lisbon`) → request v2 → TADSS | 0 lỗi contract; `git diff` của `src/TADSS.Runtime.Api` và `src/Shared.DecisionContractSDK` trước/sau khi thêm adapter là rỗng |
| PT-02 | Phân bố quyết định và đối chiếu mô tả với nhãn `is_canceled` của dataset | Báo cáo mô tả, không kết luận nhân quả |

### 4.11. SEC — Bảo mật (gói 5.3)

| ID | Cơ chế | Kiểm chứng | Xây dựng |
| :--- | :--- | :--- | :--- |
| SEC-1 | **Rate limiting** bằng `RateLimiter` có sẵn của ASP.NET Core 8, phân vùng theo `clientId` của API key (token bucket; cấu hình `Tadss:Security:RateLimiting:{PermitLimit, TokensPerPeriod, ReplenishmentPeriodSeconds}`, `QueueLimit = 0`; tính theo từng instance); vượt ngưỡng → 429 problem+json `code = RATE_LIMITED` kèm `Retry-After`. `/health/*` không bị giới hạn | Test host in-process (`TADSS.UnitTests`, F-11): vượt ngưỡng → 429 đúng dạng; hai client khác nhau không ảnh hưởng nhau | P5 |
| SEC-2 | **Giới hạn kích thước request:** `MaxRequestBodySize` (mặc định 64 KB, cấu hình được); vượt → 413 problem+json `code = PAYLOAD_TOO_LARGE`, không gọi service | Test host in-process (`TADSS.UnitTests`): body vượt giới hạn → 413; body sát giới hạn → xử lý bình thường | P5 |
| SEC-3 | **Security headers** trên mọi response: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, `Cache-Control: no-store` cho `/api/*`; HSTS ngoài `Development`/`Testing`; tắt header `Server` của Kestrel | Test host in-process (`TADSS.UnitTests`): header có mặt ở 200, 4xx, 5xx | P5 |
| SEC-4 | **Fail-closed khi khởi động:** ngoài `Development`/`Testing`, thiếu API key (NG-2) hoặc thiếu connection string → app không khởi động, message không chứa giá trị bí mật. Lưu ý: connection string được kiểm ở **mọi** môi trường (F-13, kể cả `Development`/`Testing`); chỉ API key mới được miễn ở `Development`/`Testing` | Test khởi động host với cấu hình thiếu → lỗi validate options | P5 |
| SEC-5 | **Health tách live/ready:** `/health/live` chỉ kiểm tra process; `/health/ready` kiểm tra PostgreSQL (bắt buộc, lỗi → 503) và Redis (lỗi → `Degraded`, vẫn 200 vì cache có fallback); response không lộ chuỗi kết nối hay message hạ tầng | IT: PostgreSQL bị ngắt qua Toxiproxy → ready 503, live 200 | P5 |
| SEC-6 | **Container cứng:** image runtime chạy user non-root (`USER $APP_UID`, user `app` của image .NET 8), stage runtime không chứa SDK, `.dockerignore` loại `.env`/khóa riêng (`appsettings.Development.json` vào image local như SmashCourt-BE, image không được push); `docker-compose.yml` không bind cổng host ngoài `127.0.0.1` (NG-5) | Kiểm tra `docker inspect` trong job CI hoặc test script: `Config.User` ≠ root | P5 |
| SEC-7 | **Chuỗi cung ứng:** CI chạy `dotnet list package --vulnerable --include-transitive`, có lỗ hổng mức High/Critical → job đỏ; `RestorePackagesWithLockFile` + `packages.lock.json` cho restore tái lập; Dependabot cho NuGet, Docker và GitHub Actions; log không bao giờ ghi giá trị `X-Api-Key` | Job CI; test log: request có `X-Api-Key` không để lại giá trị khóa trong log | P5 |

### 4.12. NG — Expose qua ngrok (gói 5.3)

| ID | Kiểm chứng | Ai chạy | Xây dựng |
| :--- | :--- | :--- | :--- |
| NG-1 | `docker compose config --quiet` của `docker-compose.yml` (file compose duy nhất) hợp lệ (với `NGROK_AUTHTOKEN` giả) và `config --services` liệt kê đủ `tadss-runtime`, `ngrok`, `grafana` (không profile); môi trường `Development`, không mount file, compose không chứa biến chuỗi kết nối/khóa; `ApplyMigrationsOnStartup` là literal `"false"` | tự động | P5 |
| NG-2 | Container thăm dò trong cùng network gọi `http://tadss-runtime:8080`: `/health/ready` 200; `POST /api/v2/decisions` không có key → 401, có key → 200; app **không khởi động** khi thiếu API key ở `Staging` (Swagger được phục vụ ở Development nên không kiểm 404) | tự động | P5 |
| NG-3 | Truy cập database: sau POST, `GET /api/v2/decisions/{id}` trả đúng quyết định vừa ghi (chứng minh đường ghi và đọc DB), và đếm bản ghi bằng `psql` trong container PostgreSQL tạm của `verify-expose.sh`. `/health/ready` kiểm tra kết nối DB nên lỗi kết nối hiện ngay khi khởi động | tự động (PostgreSQL tạm); với Neon thật do người dùng chạy ở NG-4 | P5 |
| NG-4 | Qua tunnel thật: `curl https://<domain>/health/ready` 200; POST và GET với `X-Api-Key`; thấy request trong `127.0.0.1:4040` | **người dùng** (cần `NGROK_AUTHTOKEN`) | P5 |
| NG-5 | Mọi cổng host của `docker-compose.yml` (`tadss-runtime`, giao diện ngrok `4040`, Grafana `3001` và OTLP `4317`/`4318`) chỉ bind `127.0.0.1` (kiểm cả cấu hình render ra và `docker compose ps`); không có cổng nào bind `0.0.0.0` | tự động | P5 |

### 4.13. SCALE — Nhiều instance (gói 5.4)

| ID | Kiểm chứng | Ai chạy | Xây dựng |
| :--- | :--- | :--- | :--- |
| SCALE-1 | Hai host dùng chung PostgreSQL/Redis (Testcontainers): 50 request song song cùng idempotency key chia đều hai host → đúng 1 bản ghi `PRIMARY`, mọi response giống nhau; khóa cache ghi ở host A được host B đọc (`CACHE_HIT`); host A mở breaker của strategy thì host B vẫn chạy strategy bình thường | tự động | P5 |
| SCALE-2 | Hai replica `tadss-runtime` sau reverse proxy (nginx) dùng chung PostgreSQL/Redis trong stack đánh giá (E-EVAL): một lượt k6 50 RPS qua proxy, 20 % request lặp lại khóa idempotency + payload của request trước → 0 response 5xx; mỗi khóa có đúng 1 bản `PRIMARY` và mọi response của khóa đó cùng `decisionId`; Prometheus có số liệu `tadss.decisions` > 0 cho cả hai instance (tách theo `service.instance.id`) | evaluator (bước c) | P5 |
| SCALE-3 | Tài liệu giới hạn scale ngang: SAS liệt kê thành phần theo từng instance (breaker strategy, breaker Redis, rate limit SEC-1, cache L1, gauge hàng chờ báo trùng giữa các instance) và thành phần dùng chung (PostgreSQL, Redis), hệ quả khi scale ngang và hướng mở rộng (rate limit phân tán); không hiện thực ở Phase 5 | rà soát tài liệu | P5 |

### 4.14. OB — Observability (gói 5.5)

| ID | Kiểm chứng | Ai chạy | Xây dựng |
| :--- | :--- | :--- | :--- |
| OB-1 | Dashboard `tadss-runtime` chỉ tham chiếu metric có thật: mọi định danh `tadss_*` trong biểu thức PromQL ánh xạ được về một instrument khai báo ở một nơi trong mã (Meter `TADSS.Runtime`) theo quy tắc chuyển tên của Prometheus (dấu chấm → gạch dưới; counter thêm `_total`; đơn vị `ms` thêm `_milliseconds`; histogram thêm `_bucket`/`_sum`/`_count`); mọi định danh `k6_*` nằm trong danh sách metric k6 khai báo một nơi | tự động (test) | P5 |
| OB-2 | Dashboard tự nạp qua provisioning khi dựng Grafana, ở cả chế độ vận hành và chế độ đánh giá (kiểm bằng HTTP API của Grafana) | tự động (`verify-observability.sh`, evaluator) | P5 |
| OB-3 | Sau kịch bản sự cố dưới tải (FI-02/FI-03/FI-07/FI-08) ở E-EVAL, Evidence Pack có JSON snapshot dashboard (không lưu ảnh) và biểu đồ dòng thời gian vẽ từ metric đã thu, thể hiện fallback, 503 và hồi phục | evaluator (bước c, e) | P5 |
| OB-4 | Chế độ vận hành (`docker compose up -d --build`): sau vài request tới TADSS, dashboard có dữ liệu (kiểm bằng API Grafana/Prometheus trong tối đa 60 giây) | tự động (`verify-observability.sh`) | P5 |
| OB-5 | Telemetry trong tiến trình: mỗi metric ghi đúng tên, nhãn, thời điểm (quyết định, override, pipeline, fallback, gauge breaker, gauge hàng chờ); span có cây cha–con đúng và log mang trace context; không có thuộc tính bí mật (khóa API, mã băm, chuỗi kết nối); collector không chạy không làm ảnh hưởng request | tự động (test) | P5 |

---

## 5. ĐỊNH NGHĨA KPI

| Mã | Chỉ số | Cách đo | Mục tiêu thiết kế |
| :--- | :--- | :--- | :--- |
| **SC1** | Độ trễ quyết định bình thường | p95 thời gian xử lý phía server của `POST /api/v2/decisions` trong LT-01 (E-EVAL, PostgreSQL/Redis thật); báo cáo thêm p95 API tạo booking của LT-04 | p95 TADSS < 150ms; p95 tạo booking E2E < 1000ms |
| **SC2** | Độ trễ khi strategy lỗi | Trong FI-02/FI-03/FI-04 và LT-03 | 100% request kết thúc bằng quyết định fallback an toàn (`APPROVED` cọc 100%, `executionType = FALLBACK`) với thời gian pipeline ≤ 500ms; p99 endpoint ≤ 700ms |
| **SC3** | Toàn vẹn & bất biến audit | Mỗi lượt chạy: số response 200 = số bản ghi `PRIMARY`; thử `UPDATE/DELETE` | 0 thiếu, 0 thừa; 0 lần sửa thành công |
| **SC4** | Idempotency & chống ghi đè đồng thời | IT-04, IT-07 và các lượt LT | 0 bản ghi quyết định trùng; 0 override kép |
| **SC5** | Feature flag | FI-13 | Hiệu lực < 2 giây; 100% booking trong lúc tắt áp cọc 100% |
| **SC6** | Không hỏng booking | Bộ truy vấn bất biến chạy sau FI-11…FI-14 và sau các kịch bản E2E | 0 vi phạm |
| **SC7** | Độc lập domain | PT-01 | ≥ 10.000 bản ghi; 0 lỗi contract; 0 thay đổi mã runtime/SDK |
| **SC8** | Chất lượng bộ kiểm thử | Toàn bộ danh mục; MT | 100% test case đạt; mutation score ≥ 70% |

**Bộ truy vấn bất biến booking (SC6)**, chạy trên DB SmashCourt sau mỗi kịch bản:

1. Booking `CONFIRMED`/`PAID_ONLINE` từ luồng TADSS phải có hóa đơn với số đã thanh toán ≥ số tiền cọc đã áp dụng.
2. Booking `PENDING_APPROVAL` phải có `decisionId` và slot lock còn hiệu lực.
3. Booking đã hủy không còn slot lock hiệu lực.
4. Không có hai booking hiệu lực chồng giờ trên cùng sân.
5. Mỗi `decisionId` lưu ở SmashCourt tồn tại trong `tadss.decision_executions` (truy vấn chéo schema chỉ dùng trong kiểm thử).

---

## 6. QUY TRÌNH CHẠY & EVIDENCE PACK

1. `run_id` dạng `EV-{yyyyMMdd}-{nhóm}-{mã}-{lần}` (ví dụ `EV-20261216-LT-01-r2`).
2. Trước khi chạy: ghi commit 3 repo, digest image, file cấu hình, thông số máy.
3. Chạy test/kịch bản; xuất dữ liệu thô (k6 summary + time series, báo cáo test, kết quả truy vấn, bản ghi audit liên quan).
4. Lưu JSON snapshot dashboard Grafana cùng khoảng thời gian (không lưu ảnh) và dữ liệu metric dùng để vẽ biểu đồ.
5. Sinh manifest SHA-256 cho mọi file; chạy notebook để sinh báo cáo HTML.

Cấu trúc thư mục, schema dữ liệu và quy tắc ẩn danh hóa: xem [`DATASET_SCHEMA_SPECIFICATION.md`](DATASET_SCHEMA_SPECIFICATION.md).

Với nhóm TADSS chạy ở E-EVAL, các bước 2–5 trên do evaluator thực hiện tự động trong một lượt (Mục 6.1), không còn script rời cho từng nhóm.

### 6.1. Chế độ đánh giá một lệnh (D-12)

> **Hiện trạng:** đã hiện thực ở gói 5.5 (evaluator `tools/evaluation/`, bộ dựng Evidence Pack `tools/evidence/`, kịch bản k6 `tests/load/`). Thiết kế: D-12 và Mục 4.5 của `spec/PHASE5_DESIGN_SPEC.md`.

`docker compose -p tadss-eval --profile evaluation run --rm --build evaluator` dựng stack cô lập (TADSS build Release, PostgreSQL 16, Redis 7, Toxiproxy, Grafana; **không bao giờ chạm Neon**) cùng container **evaluator**. Evaluator chạy tuần tự rồi thoát kèm bản tóm tắt:

| Bước | Nội dung |
| :--- | :--- |
| **a** | Toàn bộ bộ test gộp một lượt: unit, integration, contract, fault injection FI-01…10, nhiều instance SCALE-1, bảo mật; kết quả gom theo mã danh mục (UT/IT/CT/FI/SEC/NG…) |
| **b** | Kịch bản tải k6: LT-01, LT-02, LT-05 (LT-04 E2E qua SmashCourt-BE để Phase 6) |
| **c** | Sự cố dưới tải: LT-03 + FI qua Toxiproxy (cắt PostgreSQL, làm chậm Redis, strategy lỗi); quan sát trực tiếp trên dashboard Grafana: độ trễ tăng, fallback `APPROVED` cọc 100%, 503, hồi phục |
| **d** | Schemathesis (CT-03) và Stryker mutation (MT-01…03) |
| **e** | Gom minh chứng vào `evidence/<run_id>/`: `report.html` (đạt/không đạt theo SC1…SC8, biểu đồ p50/p95/p99, biểu đồ dòng thời gian sự cố vẽ từ metric Grafana đã thu, số test từng nhóm), dữ liệu thô (trx theo `CatalogId`, kết quả k6, metric export, `chaos_log`) và `manifest.json` chứa SHA-256 từng file (EV-1…EV-5) |

**Hai độ dài:** **bản nhanh** (đo thật 22–25 phút, tải rút ngắn, không chạy Stryker) để diễn tập; **bản đầy đủ** (ước tính ≈ 2,8 giờ từ các bước đã đo: LT-01 3 × 11 phút, LT-02 ~15 phút, LT-05 soak 61 phút, Stryker ≈ 21 phút; đo trọn vẹn ở gói 5.7) để lấy số liệu chính thức. Replay 215 booking (RP-01/RP-02) và adapter Hotel Booking Demand (PT-01/PT-02) đổ vào cùng Evidence Pack và báo cáo (dữ liệu của gói 5.6).

**Giới hạn đo lường:** toàn bộ chạy trên một máy (k6, TADSS và cơ sở dữ liệu tranh nhau CPU) nên số liệu là số liệu phòng lab; báo cáo ghi thông số máy (CPU/RAM) và liệt kê vào Mục 8.

---

## 7. PHÂN TÍCH & TRÌNH BÀY

- Độ trễ báo cáo p50/p95/p99/max kèm số mẫu; với LT-01 và LT-03 báo cáo khoảng tin cậy 95% của p95 bằng bootstrap trên 3 lần lặp.
- Biểu đồ trong notebook: phân bố độ trễ theo kịch bản; đường thời gian độ trễ + trạng thái circuit khi tiêm lỗi; tỷ lệ trạng thái quyết định; phân bố mức cọc theo hạng/khung giờ (RP, PT).
- Mỗi con số trong báo cáo dẫn về `run_id` và file dữ liệu thô tương ứng.

---

## 8. GIỚI HẠN & MỐI ĐE DỌA TÍNH HỢP LỆ

| Giới hạn | Ảnh hưởng | Cách xử lý |
| :--- | :--- | :--- |
| 215 booking SmashCourt là dữ liệu môi trường phát triển; nhãn `NO_SHOW` do job tự gán khi không check-in, `CANCELLED` phần lớn do hết hạn thanh toán | Không phản ánh hành vi khách thật | Chỉ dùng làm input có hình dạng thực tế (RP); mọi số liệu theo nhãn chỉ mang tính mô tả |
| Replay là phân tích phản thực tế: mức cọc có thể làm thay đổi chính hành vi khách | Không suy ra được "đã ngăn bao nhiêu no-show" | Không đưa kết luận nhân quả; để Khóa luận xử lý bằng đánh giá off-policy/shadow mode |
| Dataset khách sạn khác domain | Chỉ là proxy | Dùng cho kiểm chứng độc lập domain và mô tả, không kết luận về sân cầu lông |
| E-EVAL chạy trên một máy: k6, TADSS, PostgreSQL, Redis, Toxiproxy và Grafana tranh nhau CPU/RAM | Số tuyệt đối là số liệu phòng lab, khác production; độ trễ đo được gồm cả nhiễu do tải của chính bộ công cụ | Báo cáo kèm thông số máy (CPU/RAM) và liệt kê vào giới hạn; so sánh tương đối giữa kịch bản trên cùng máy; ghi nhận bản nhanh chỉ để diễn tập |
| Cách ly strategy không hợp tác (`IsolatedStrategyInvoker`, FI-05) chỉ **giới hạn thời gian chờ** của request: strategy bỏ qua `CancellationToken` hoặc chặn đồng bộ bị cắt ở inner timeout (fallback cọc 100%) nhưng không bị dừng; task bị bỏ rơi vẫn tiêu thụ CPU/thread pool tới khi tự kết thúc | Nhiều task bị bỏ rơi cùng lúc (strategy treo lặp lại dưới tải) có thể làm cạn thread pool và kéo dài độ trễ của request khác; thí nghiệm sự cố dưới tải (LT-03) có thể phóng đại hiệu ứng này | Đo `tadss.strategy.abandoned` trong LT-03; báo cáo số task bị bỏ rơi cùng độ trễ p99; không kết luận TADSS "dừng được" strategy lỗi — chỉ cô lập luồng request khỏi nó |
| E-EVAL chạy ở môi trường ASP.NET Core `Testing` (decorator tiêm lỗi chỉ đăng ký ở đây) với rate limit nâng lên 100000 và trace lấy mẫu 10 % | HSTS, validator khóa API và giới hạn tốc độ thật khác Production; không phải request nào cũng có trace (metric và audit không lấy mẫu nên KPI không bị ảnh hưởng) | Ghi vào `report.html`; hành vi giới hạn tốc độ và khóa API được kiểm riêng ở SEC-1…7 |
| Histogram metric chỉ chính xác tại biên bucket (…, 150, 500, 700, … ms) và cửa sổ đo theo metric lệch tối đa một `MetricExportIntervalMs` ở hai đầu | Phân vị không trùng biên là ước lượng; tỉ lệ ≤ 150/500/700 ms là phép đếm chính xác | SC1 kết luận bằng cả tỉ lệ bucket phía server và p95 phía client của k6 (chính xác từng request) |
| Evaluator mount `/var/run/docker.sock` (Testcontainers cho integration test) | Container có quyền điều khiển Docker của máy chủ | Chỉ chạy trên máy của chính người dùng; mã nguồn mount chỉ đọc, file bí mật không được copy |
| Tài nguyên VM Docker (Docker Desktop) quyết định độ trễ đo được; `verify-observability.sh` không chạy được trong container evaluator (đường dẫn mount) | Số liệu phụ thuộc cấu hình máy; OB-2/OB-4 ở chế độ vận hành lấy từ CI | Báo cáo ghi CPU/RAM Docker và digest image; kết quả CI `observability-check` thay thế |
| Không có pilot với người dùng thật | Không đo được mức độ dễ dùng và hành vi thật của khách/Manager khi gặp chính sách cọc mới | Nêu rõ trong báo cáo; chất lượng quyết định chỉ được đánh giá bằng dữ liệu import (RP, PT); để Khóa luận đánh giá trên vận hành thật |

---

## 9. LƯU TRỮ: MICRO-BENCHMARK PHASE 4

Micro-benchmark Tuần 8 (19 kịch bản × 4 mức tải) chạy trên EF Core InMemory với strategy giả lập bằng `Task.Delay`, nên chỉ mô tả **overhead của pipeline trong phòng lab** và **không** được dùng làm minh chứng cho SC1–SC8. Mã và báo cáo đã gỡ khỏi repo ở Phase 5, tra lại qua tag git `phase4-baseline`; đo hiệu năng của Phase 5 dùng k6 (LT-01…LT-05) trên hạ tầng thật.
