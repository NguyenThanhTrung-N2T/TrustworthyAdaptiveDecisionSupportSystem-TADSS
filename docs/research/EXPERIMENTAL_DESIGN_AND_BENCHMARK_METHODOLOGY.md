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
| **RQ2** Governance & khả năng kiểm toán | SC3, SC4 | IT, CT, BE-IT, E2E |
| **RQ3** Chất lượng tích hợp & quyết định trên dữ liệu thật | SC1 (E2E) | LT-04, E2E, RP |
| Độc lập domain | SC7 | PT |
| Chất lượng bộ kiểm thử | SC8 | Toàn bộ, MT |

---

## 2. MÔI TRƯỜNG THỰC NGHIỆM

| Mã | Môi trường | Thành phần | Dùng cho |
| :--- | :--- | :--- | :--- |
| **E-TC** | Testcontainers (trong `dotnet test`) | PostgreSQL 16, Redis 7, Toxiproxy | UT, IT, CT-01/02/04, BE-IT, một phần FI |
| **E-EVAL** | Chế độ đánh giá một lệnh: `docker compose up -d --build` (D-14: khởi động cùng chế độ vận hành, evaluator tự chạy một lượt); chạy lại riêng `bash tools/evaluation/run.sh` (các service `eval-*` của `docker-compose.yml`, không profile; **đã hiện thực ở gói 5.5**, gộp vào lệnh `up` ở D-14; thay cho môi trường E-LOCAL/profile `lab` ghi trong bản trước, xem D-12 ở `spec/PHASE5_DESIGN_SPEC.md`). Cô lập hoàn toàn, không chạm Neon | TADSS (build Release), PostgreSQL 16, Redis 7, Toxiproxy, `grafana/otel-lgtm` và container **evaluator** điều phối cả chiến dịch (Mục 6.1); không có SmashCourt-BE/FE (LT-04 chạy ở E-STAGING) | FI, LT-01/02/03/05, CT-03, MT, SCALE-1, SCALE-2; RP, PT (dữ liệu của gói 5.6) |
| **E-STAGING** | Môi trường tích hợp trên máy nhóm, chạy bằng Docker Compose (chi tiết chốt ở W08: [Môi trường tích hợp E-STAGING](../planning/MASTER_EXECUTION_PLAN.md#môi-trường-tích-hợp-e-staging) trong MASTER_EXECUTION_PLAN) | SmashCourt FE/BE + TADSS + PostgreSQL trên nhánh Neon riêng `staging` (DB SmashCourt và schema `tadss`, tách khỏi DB chính) + Redis + VNPay sandbox | E2E, LT-04 và FI-11…FI-14 trên hệ thống đã tích hợp (đặc tả từng bước E2E: [`E2E_TEST_SPECIFICATION.md`](E2E_TEST_SPECIFICATION.md)) |

Ba chế độ chạy của repo (D-12): **dev hằng ngày** `dotnet run` (Neon + Redis Cloud, chỉ app + Swagger, không Docker/Grafana); **vận hành / demo** `docker compose up -d --build` (TADSS + ngrok + Grafana); **đánh giá** (E-EVAL) chạy cùng lệnh `docker compose up -d --build` của chế độ vận hành (D-14) hoặc riêng bằng `bash tools/evaluation/run.sh`. Chỉ chế độ đánh giá tạo số liệu dùng làm minh chứng; test tự động trong CI vẫn chạy ở E-TC (`dotnet test`).

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
| **Bộ sinh báo cáo Python** (`tools/evidence/report/`, Jinja2 + biểu đồ SVG tự vẽ) | Sinh `report.html` tự chứa từ Evidence Pack (evaluator gọi ở bước f; lệnh `python -m evidence.report <thư mục pack>` (chạy trong container evaluator hoặc từ thư mục `tools/`, xem guide chạy đánh giá mục 7) dựng lại `report.html` từ một pack có sẵn bằng cùng bộ sinh, thay cho notebook, xem Mục 6.1) | TADSS `tools/evidence/` | Toàn bộ |

**Tiêm lỗi cho strategy:** dùng decorator chỉ đăng ký trong môi trường kiểm thử (bật qua cấu hình `Testing`), không có trong cấu hình production. Lỗi hạ tầng (Redis, PostgreSQL, mạng giữa BE và TADSS) luôn tiêm bằng Toxiproxy.

---

## 4. DANH MỤC KIỂM THỬ

Cột **Xây dựng** là phase viết test; mọi test case được **chạy lại toàn bộ ở Phase 8**.

**Tổng quan nội dung kiểm thử** (cùng các dòng, cùng thứ tự với bảng kế hoạch kiểm thử trong [`PROPOSAL_DO_AN_2.md`](../planning/PROPOSAL_DO_AN_2.md) và [`MASTER_EXECUTION_PLAN.md`](../planning/MASTER_EXECUTION_PLAN.md); hai bảng đó diễn đạt bằng lời thường cho người đọc kế hoạch, PROPOSAL không ghi mã test case; khi khác nhau về chi tiết thì bảng này và các mục 4.1–4.15 đúng; chi tiết từng test case ở các mục 4.1–4.15). Toàn bộ kiểm thử do nhóm tự thực hiện ở mức phát triển, **không có pilot với người dùng thật**; chất lượng quyết định được đánh giá bằng dữ liệu import (RP, PT).

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
| Kiểm thử SmashCourt-BE và FE (BE-UT, BE-IT, FE) | BE: ánh xạ booking → request v2, `TadssClient`, luồng cọc `APPROVED` / `NEED_APPROVAL` / `REJECTED` / cọc 100 % khi có sự cố, ba endpoint duyệt theo `bookingId` (phân quyền chi nhánh, kiểm body, mã lỗi, TADSS lỗi khi duyệt → 503 không có đường lùi 100 %, bấm lần hai → 409), job hết hạn duyệt (không tự duyệt) và job outcome (đúng một lần), sự kiện SignalR; chạy trên PostgreSQL thật với TADSS giả lập. FE: `tsc`/lint/build và luồng giao diện trên mock MSW theo hợp đồng BE ↔ FE (mã lỗi, 401/403 ngoài envelope, phần trăm → tỷ lệ). Đạt khi 100 % test case đạt (SC8) | xUnit, Testcontainers, WireMock.Net, Playwright, MSW |
| Kiểm thử đầu–cuối trên giao diện (E2E, LT-04) | Tự động thao tác trên trình duyệt qua FE → BE → TADSS (E2E-01…E2E-14): đặt sân → mức cọc → VNPay sandbox → Manager duyệt / từ chối / hết hạn; tắt cờ, TADSS dừng khi đặt sân và khi duyệt; hai Manager, bấm lần hai, phạm vi chi nhánh, realtime, đặt tại quầy không qua TADSS. Đạt khi mọi kịch bản đạt, 0 vi phạm bất biến booking sau mỗi kịch bản (SC6), p95 tạo booking < 1000 ms (SC1) | Playwright, k6 |
| Đánh giá trên dữ liệu đặt sân thật (RP-01…02) | Import 215 booking SmashCourt (ẩn danh) qua TADSS; so sánh với chính sách cọc 100%: tỷ lệ duyệt ngay / cần duyệt, mức cọc theo hạng khách và khung giờ | Script Python |
| Đánh giá độc lập nghiệp vụ (PT-01…02) | Import ≥ 10.000 đơn Hotel Booking Demand, chuyển sang request v2 rồi gửi vào TADSS. Đạt khi 0 lỗi contract, 0 thay đổi mã runtime/SDK (SC7) | Script Python |
| Kiểm tra tài liệu (DOC-1…4) | Mọi tài liệu có đúng một dòng kiểm kê (hiện hành / lịch sử); tài liệu hiện hành không mô tả như hiện tại những thứ đã gỡ; mọi link nội bộ và anchor trong markdown trỏ đúng; bảng cấu hình khớp mã. Đạt khi cả bốn mã đạt | Python (`tools/docs/check_docs.py`), xUnit |

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
| UT-11 | Điều phối `DecisionService` bằng test double (thứ tự idempotency → cache → pipeline → approval window → audit; fail-closed; lỗi cache chỉ là miss); làm nóng lúc khởi động (`DecisionPathWarmup`) | Đúng thứ tự gọi; lỗi ghi audit lan ra và không có response; idempotency trả lại bản cũ hoặc 409; không gọi strategy khi request sai; làm nóng chạy mọi strategy nhưng không ghi audit/cache, lỗi hay hết ngân sách không chặn khởi động | P5 |
| UT-12 | `DepositCalculator` (làm tròn theo tiền tệ, biên 0 và 1) | `amount = round(totalPrice × rate)` đúng đơn vị nhỏ nhất của từng tiền tệ, gồm biên rate 0 và 1 | P5 |
| UT-13 | Cache v2: key (tên, phiên bản strategy, `optionsFingerprint`, `request_hash`), chính sách theo strategy, chỉ lưu `StrategyDecision`, lỗi cache bị cô lập (breaker Redis bằng test double thuộc gói 5.4) | Key đổi khi đổi tham số strategy hoặc lịch cao điểm; lỗi/payload hỏng chỉ là miss; `request_hash` và fingerprint tất định (INV-3) | P5 |
| UT-14 | Kiến trúc và quy ước (gói 5.2d, Clean Architecture nhiều project): đồ thị tham chiếu SDK ← Application ← Infrastructure ← Runtime.Api đúng quy tắc; Application không tham chiếu assembly hạ tầng; controller/middleware không dùng kiểu Infrastructure (kể cả qua `global using`/`<Using>`); Application và Infrastructure không có `FrameworkReference`/Web SDK; namespace khớp project + thư mục; không còn kiểu/namespace "V2"; mỗi kiểu public một file; `Options` chỉ ở `Options/`, exception nghiệp vụ chỉ ở `Exceptions/`; port chỉ khai báo ở `Application/Abstractions`, EF Core chỉ trong `Persistence/`; bất biến tỷ lệ cọc 4 chữ số thập phân; mọi options có `SectionName` (tìm bằng reflection) có validator đăng ký cùng `ValidateOnStart` (không có ngoại lệ, kể cả `DatabaseOptions`); thứ tự middleware, gồm các middleware bảo mật (xác thực API key, giới hạn kích thước, rate limiter, security headers), qua hành vi quan sát được; trần 250 dòng mỗi file nguồn (trừ `Migrations/`) | Vi phạm quy ước làm test đỏ; thêm kiểu sai chỗ bị phát hiện ngay | P5 |
| UT-15 | Quy tắc quản trị của override và outcome bằng test double (gói 5.3): thứ tự kiểm tra, idempotency theo payload, quy đổi cọc `rate`/`amount`, hạn duyệt, ranh giới với bản `SHADOW` | Mỗi quy tắc và mỗi nhánh lỗi (400/404/409) có test; không gọi lưu trữ khi từ chối | P5 |

### 4.2. IT — Integration test trên hạ tầng thật (TADSS, E-TC)

| ID | Kịch bản | Tiêu chí đạt |
| :--- | :--- | :--- |
| IT-01 | Đánh giá hợp lệ; khởi động có làm nóng | 200; đúng 1 bản ghi `decision_executions` có snapshot request; response hợp lệ theo schema; làm nóng mở đủ kết nối và để lại 0 bản ghi |
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
| CT-04 | `TadssClient` của SmashCourt-BE với response mẫu hợp lệ theo schema v2 (E-TC, SmashCourt-BE, Phase 6, T6.2; BE không reference SDK nên đây là chốt tương thích duy nhất, INTEGRATION_SPEC §4.1) | Parse đúng mọi trạng thái và mã lỗi. Response thành công BE nhận — mẫu `APPROVED` (kể cả fallback `fallbackUsed = true`, `APPROVAL_WINDOW_UNAVAILABLE`), `NEED_APPROVAL`, `REJECTED`, override `APPROVE`/`REJECT`, outcome — đều qua schema tương ứng trong `docs/contracts/schemas/v2/` (`decision-result`, `override-response`, `outcome-response`) trước khi đưa vào test. Response lỗi: `schemas/v2/` không có schema cho problem+json, nên mẫu lỗi được đối chiếu với bảng INTEGRATION_SPEC §3.8 (HTTP status ↔ `code` ↔ header `Retry-After` của 503 `PERSISTENCE_UNAVAILABLE` và 429 `RATE_LIMITED`), mỗi dòng của bảng có ít nhất một mẫu; 415 không có `code` nên `TadssClient` phân loại theo HTTP status. `reasonCode` lạ không làm lỗi (§6). Chiều gửi: request đánh giá, override và outcome do BE serialize hợp lệ theo `decision-request`, `override-request` (`REJECT` bỏ hẳn khóa `deposit`), `outcome-request` |

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
| LT-04 | E2E qua SmashCourt-BE (chạy ở E-STAGING, không thuộc E-EVAL): 20 VU tạo booking trên các slot khác nhau trong 10 phút | Độ trễ API tạo booking, tỷ lệ thành công, tính đúng của mức cọc |
| LT-05 | TADSS soak 50 RPS trong 60 phút | Trôi độ trễ, bộ nhớ, kết nối DB |

Pha warm-up 1 phút bị loại khỏi thống kê. Kết quả kèm dashboard Grafana cùng khoảng thời gian. Evaluator của chế độ đánh giá (Mục 6.1) chạy LT-01, LT-02, LT-05 và LT-03 (kèm tiêm lỗi); LT-04 (E2E qua SmashCourt-BE) để Phase 6 khi BE tích hợp TADSS. Ở **bản nhanh** của chế độ đánh giá, thời lượng các kịch bản tải được rút ngắn nên số liệu chỉ để diễn tập, không dùng làm số liệu chính thức.

### 4.7. BE — SmashCourt-BE (Testcontainers + WireMock.Net)

**Nguồn hành vi:** phía BE ↔ TADSS theo [`INTEGRATION_SPEC.md`](../contracts/INTEGRATION_SPEC.md) Mục 1–4; phía BE ↔ FE theo [`DEPOSIT_APPROVAL_API.md`](../../../SmashCourt-BE/docs/api/DEPOSIT_APPROVAL_API.md) (route theo `bookingId`, mã lỗi Mục 5 và sự kiện SignalR Mục 6 là **đề xuất** sẽ thêm vào mã ở Phase 6). Ca nào phụ thuộc câu hỏi mở của tài liệu đó gắn nhãn **(Qn)**: hiện thực theo đề xuất hiện tại; SV2 chốt khác thì sửa `DEPOSIT_APPROVAL_API.md` trước, rồi sửa ca kiểm thử.

**Môi trường và công cụ:** BE-UT chạy không cần container; BE-IT chạy ở E-TC: PostgreSQL 16 thật bằng Testcontainers (hạ tầng `SmashCourt-BE.Tests/Integration/` hiện có) và TADSS giả lập bằng WireMock.Net, response giả lập lấy từ bộ mẫu đã qua JSON schema v2 của CT-04. Hiện trạng 12/10/2026: project `SmashCourt-BE.Tests` có xUnit, Moq, Testcontainers.PostgreSql; WireMock.Net và JsonSchema.Net được thêm ở T6.2/T6.10. Cột *Task* theo [`BACKLOG_SPRINTS.md`](../planning/BACKLOG_SPRINTS.md) Epic 6A. "WireMock nhận 0 request" nghĩa là BE không gọi TADSS.

| ID | Kịch bản | Tiêu chí đạt | Task |
| :--- | :--- | :--- | :--- |
| BE-UT-01 | `TadssClient`: header, timeout, map lỗi, retry đúng một lần với cùng key | Gửi `X-Api-Key` (giá trị khóa, không phải mã băm), `X-Idempotency-Key` (`bookingId` khi đánh giá, `{decisionId}:{action}` khi override, và cả ở `/outcome` — thiếu ⇒ TADSS trả 400 `MISSING_HEADER`, INTEGRATION_SPEC §3.2; giá trị key của outcome chốt ở T6.8), `X-Actor-Id` chỉ ở override, `X-Correlation-ID`, `ngrok-skip-browser-warning: true`; quá `TimeoutMs` thì hủy request; timeout, lỗi mạng, 5xx, 401, 413, 429 và problem+json thành kết quả có kiểu, không ném ra ngoài; POST retry tối đa một lần với cùng idempotency key, 413/429 không retry ngay; giá trị khóa API không xuất hiện trong log | T6.2 |
| BE-UT-02 | Ánh xạ booking → request v2 (nhiều sân, hạng, lịch sử) | Mỗi `booking_court` một item (`resourceId = courtId`, `resourceTypeId = courtTypeId`, giá trước giảm); `totalPrice = finalTotal`; `branchTimeZone = Asia/Ho_Chi_Minh`; thời gian ISO 8601 có offset; hạng theo thứ tự `MinPoints` tăng dần → `STANDARD`…`DIAMOND` (từ hạng thứ 6 gộp `DIAMOND`, chưa có hạng → `STANDARD`), không phụ thuộc tên hạng; luôn `isGuest = false` (đặt online bắt buộc đăng nhập, DEPOSIT_APPROVAL_API P6-01); lịch sử theo lý do hủy (BE-UT-09); `channel = ONLINE`; request hợp lệ theo `decision-request.schema.json` | T6.3 |
| BE-UT-03 | Kiểm body `ApproveBookingDto` / `RejectBookingDto` | `depositRate` bắt buộc, trong `[0, 1]`, tối đa 4 chữ số thập phân, không làm tròn âm thầm (`0.12345` bị từ chối); `reason` 5..500 **Unicode code point** (biên 4/5/500/501, ký tự ngoài BMP như emoji tính một), không chỉ gồm khoảng trắng; vi phạm → 400 `VALIDATION_ERROR` với khóa `errors` là `DepositRate` / `Reason`; `depositRate = 0` hợp lệ (Q4) | T6.5 |
| BE-UT-04 | Ánh xạ lỗi TADSS → mã BE khi duyệt/từ chối (DEPOSIT_APPROVAL_API Mục 5, dòng 4–16) | 400 `VALIDATION_ERROR` về `deposit.rate` → 400 `VALIDATION_ERROR` (`errors.DepositRate`); 400 khác, 413, 415 → 500 `INTERNAL_ERROR` kèm log mức Error có correlation id; 404 `DECISION_NOT_FOUND` → 409 `APPROVAL_DECISION_NOT_FOUND` (Q10); 409 `NOT_PENDING_APPROVAL` → 409 `BOOKING_NOT_PENDING_APPROVAL`; 409 `ALREADY_OVERRIDDEN` và `IDEMPOTENCY_KEY_REUSE` → 409 `APPROVAL_ALREADY_RESOLVED`; 409 `APPROVAL_EXPIRED` → 409 `APPROVAL_EXPIRED`; timeout, lỗi mạng, 500, 503, 504, 429, 401 → 503 `TADSS_UNAVAILABLE`; không nhánh nào dẫn tới cọc 100 % | T6.5 |
| BE-UT-05 | Chọn mã 409 theo DB của BE (quy tắc a/b/c, DEPOSIT_APPROVAL_API 4.2 bước 4) | Xét đúng thứ tự: (a) `approval_resolved_at` có giá trị (đã từ chối; đã duyệt rồi bị hủy vì hết hạn thanh toán) → `APPROVAL_ALREADY_RESOLVED`; (b) `cancel_reason = APPROVAL_EXPIRED`, hoặc booking còn `PENDING_APPROVAL` mà `approvalExpiresAt ≤ now` → `APPROVAL_EXPIRED`; (c) còn lại — `WALK_IN`, booking chưa từng cần duyệt, khách/nhân viên hủy khi đang chờ duyệt (Q8) → `BOOKING_NOT_PENDING_APPROVAL`; thiếu `tadss_decision_id` → `APPROVAL_DECISION_NOT_FOUND`; duyệt với số tiền quy đổi > 0 nhưng dưới `MinOnlinePaymentAmount` → 400 `VALIDATION_ERROR` (bước 4b), không gọi TADSS | T6.5 |
| BE-UT-06 | `PENDING_APPROVAL` trong `BookingStatusTransition` | `PENDING_APPROVAL` thuộc `GetActiveStatuses()` (slot được coi là đã giữ ở lịch sân, time grid, kiểm trùng giờ); chỉ có các chuyển của sơ đồ DEPOSIT_APPROVAL_API 2.1: vào từ tạo booking, `→ PENDING` khi duyệt, `→ CANCELLED` khi từ chối, hết hạn duyệt hoặc hủy (Q8); không chuyển thẳng sang `PAID_ONLINE`/`CONFIRMED` (trừ nhánh cọc 0 % của Q4) | T6.1 |
| BE-UT-07 | Trường cọc trên DTO (`BookingDto`, `OnlineBookingResponse`, `BookingApprovalResultDto`) | `depositRate`/`depositAmount` lấy nguyên từ TADSS, BE không tự quy đổi; `FULL_PAYMENT_FALLBACK` → `depositRate = 1`, `depositAmount = finalTotal` lúc đặt; `remainingAmount = finalTotal hiện tại − depositAmount`; `PENDING_APPROVAL` → bốn trường cọc và `expiresAt` là `null`; nhánh a′ và duyệt 0 % → `depositRate = 0`, `depositAmount = 0`; booking đã hủy có `cancelReason`; `WALK_IN` và booking online tạo trước Phase 6 → mọi trường cọc `null`; endpoint của khách luôn trả `approvalReasonCode`/`approvalReasons` `null` (Q6); không DTO nào có `decisionId`, `strategyName`, `executionType`, `fallbackUsed`; khóa có giá trị `null` vẫn được serialize | T6.4, T6.5 |
| BE-UT-08 | Chính sách giữ chỗ (`PaymentHoldHelper`) | Bốn thời điểm: đặt (`expiresAt = now + 10'`, hạn tuyệt đối `now + 15'`), duyệt (`min(now + 15', giờ chơi)`, hạn tuyệt đối bằng hạn), IPN thất bại (`min(expiresAt, now + 5')`, không bao giờ kéo dài), thanh toán lại (`min(hạn tuyệt đối, max(expiresAt, now + 10'))`, quá hạn tuyệt đối → `PAYMENT_HOLD_EXPIRED`); biên đúng hạn, trước/sau 1 giây; tham số đọc từ `BookingPayment` | T6.6 |
| BE-UT-09 | Lịch sử khách và outcome theo `cancel_reason` | `paymentExpiredCount` chỉ đếm `PAYMENT_EXPIRED`; `APPROVAL_EXPIRED`, `APPROVAL_REJECTED`, `POLICY_REJECTED`, `STAFF_CANCELLED`, `SYSTEM_CLEANUP` không vào lịch sử; `CUSTOMER_CANCELLED` tách `cancelled`/`lateCancelled` theo tỷ lệ hoàn như Phase 5; chỉ booking kết thúc trước booking đang xét; ánh xạ outcome: `APPROVAL_REJECTED`/`POLICY_REJECTED` → `REJECTED`, `CUSTOMER_CANCELLED` → `CANCELLED_BY_CUSTOMER`, `STAFF_CANCELLED` → `CANCELLED_BY_STAFF`, `SYSTEM_CLEANUP` không gửi; `occurredAt` cố định (giờ kết thúc chơi hoặc `cancelled_at`) | T6.3, T6.8 |
| BE-UT-10 | % hoàn tiền theo chính sách và múi giờ | Ở mỗi mốc `cancel_policies` (đúng mốc, trước/sau 1 phút, giờ VN), % hoàn đúng chính sách (sửa lệch 7 giờ của mã cũ); số hoàn = `round(Σ Payment SUCCESS × %)`, không vượt số đã trả; hạn link hủy = `min(giờ chơi theo UTC đúng, now + 24 giờ)`, cùng một hàm cho đơn online và đơn tại quầy | T6.9 |
| BE-UT-11 | Phân loại trạng thái booking | Mọi giá trị `BookingStatus` thuộc ít nhất một tập có tên trong `BookingStatusTransition` hoặc danh sách cố ý không giữ sân; `PENDING_APPROVAL` có trong tập giữ sân; thêm giá trị enum mà không phân loại → test đỏ | T6.1 |
| BE-UT-12 | Quy tắc thời gian (DEPOSIT_APPROVAL_API P6-22) | Request TADSS: mọi trường thời gian có offset (`items`, `lastCompletedAt` là giờ chơi `+07:00`; `requestedAt`, `memberSince` đúng thời điểm UTC), hợp lệ theo `decision-request.schema.json`; `approvalExpiresAt` của TADSS lưu đúng UTC; hạn link hủy (online và tại quầy) có `Kind = Utc`, bằng giờ bắt đầu khi đơn trong 24 giờ tới; khách hủy (app, link) lúc đúng giờ bắt đầu bị chặn với 400 `BOOKING_ALREADY_STARTED` (P6-23), trước 1 phút vẫn hủy được, nhân viên hủy được; thời điểm cố định bằng `FakeTimeProvider` (00:30, 06:59, 23:45 giờ VN), không phụ thuộc đồng hồ máy | T6.3, T6.9 |
| BE-IT-01 | Script DB mới (enum `PENDING_APPROVAL`, trường cọc) trên DB thật | Áp dụng thành công; enum mới đọc/ghi được qua EF; có các cột cọc, `tadss_decision_id`, nguồn cọc và dữ liệu duyệt (`resolvedBy`/`resolvedAt`/`approvalResult`; tên cột chốt ở T6.1); luồng cũ vẫn đạt (bộ test hiện có xanh) | T6.1 |
| BE-IT-02 | Luồng APPROVED | `POST /api/bookings/online` → 201, `status = PENDING`, `depositSource = TADSS`; số tiền VNPay = `deposit.amount`; hóa đơn lưu tỷ lệ, số tiền cọc, `decisionId`, nguồn cọc; quyết định fallback của runtime (`APPROVED` 100 %, `fallbackUsed = true`) và `APPROVAL_WINDOW_UNAVAILABLE` xử lý như mọi `APPROVED`; `BookingCreated` phát sau khi có kết quả, mang trạng thái cuối; IPN thành công → `PAID_ONLINE`, hóa đơn `PARTIALLY_PAID` (hành vi IPN hiện có); `retry-payment` tạo URL với `depositAmount` | T6.4 |
| BE-IT-03 | Luồng NEED_APPROVAL | 201, `status = PENDING_APPROVAL`, `paymentUrl`, `expiresAt` và trường cọc `null`, `approvalExpiresAt` lấy từ TADSS; không tạo Payment/URL VNPay; lưu `tadss_decision_id` (và lý do cần duyệt, Q6); slot lock giữ tới `approvalExpiresAt`, slot hiện là đã giữ ở time grid; `retry-payment` → 400 `BAD_REQUEST` | T6.4 |
| BE-IT-04 | Flag tắt / TADSS lỗi 5xx / timeout / 401 (và 413, 429, lỗi mạng) | 201 như nhánh (d): `depositSource = FULL_PAYMENT_FALLBACK`, `depositRate = 1`, số tiền VNPay = `finalTotal`; flag tắt → WireMock nhận 0 request; lỗi → tối đa hai request cùng `X-Idempotency-Key = bookingId` (413/429: một request); không trả 500, không lộ mã lỗi TADSS cho khách; log có correlation id; booking hợp lệ theo bộ truy vấn SC6 (BE-IT-20) | T6.4 |
| BE-IT-05 | Hàng chờ cho Manager | `GET /api/bookings/pending-approval` đọc DB của BE (WireMock nhận 0 request): chỉ booking `PENDING_APPROVAL` có `approvalExpiresAt > now`; `BRANCH_MANAGER` chỉ thấy chi nhánh được gán (`branchId` bỏ trống hoặc đúng chi nhánh), `branchId` khác → 403 `FORBIDDEN`; chi nhánh được gán của Manager không hoạt động → 400 `BAD_REQUEST` (DEPOSIT_APPROVAL_API 4.1); `OWNER` thấy tất cả hoặc lọc một chi nhánh; sắp theo `approvalExpiresAt` tăng dần, rồi `createdAt`, `bookingId`; `pageSize` 1..50 (ngoài miền → 400 `VALIDATION_ERROR`); item đúng các trường của `PendingApprovalBookingDto` (`bookingDate` dạng `yyyy-MM-dd`) | T6.5 |
| BE-IT-06 | Duyệt / từ chối | Override gửi `X-Actor-Id` = `userId` trong JWT (không lấy từ body), `X-Idempotency-Key = {decisionId}:APPROVE` hoặc `:REJECT` với `decisionId` tra từ booking (FE chỉ gửi `bookingId`); body duyệt `{ action: APPROVE, deposit: { rate }, reason }`, body từ chối `{ action: REJECT, reason }` không có khóa `deposit`; cập nhật bằng câu lệnh có điều kiện trạng thái; duyệt: `PENDING_APPROVAL → PENDING`, cọc lấy từ response TADSS, `depositSource = MANAGER`, ghi `resolvedBy`/`resolvedAt`/`approvalResult`; từ chối: `CANCELLED` (`CancelSource = STAFF`), ghi `resolvedBy`/`resolvedAt`/`approvalResult = REJECTED`, giải phóng slot lock, báo người đăng ký chờ slot, gửi email cho khách (DEPOSIT_APPROVAL_API 4.3 bước 7), không hoàn tiền; xử lý 409 theo BE-UT-04 | T6.5 |
| BE-IT-07 | Sau duyệt | Duyệt mức > 0: `PENDING`, hóa đơn lưu mức cọc TADSS trả, `depositSource = MANAGER`, `expiresAt` = duyệt + 15 phút (Q2, không vượt giờ chơi) và cũng là hạn giữ chỗ tuyệt đối, slot lock gia hạn tới đó; **không** tạo Payment/URL VNPay (P6-10); email cho khách dẫn tới lịch sử đặt sân, không chứa URL VNPay (Q3); `retry-payment` của khách sau đó trả URL với `depositAmount` và không kéo dài hạn; duyệt 0 % (Q4): `CONFIRMED`, không VNPay | T6.6 |
| BE-IT-08 | Job hết hạn duyệt | Booking `PENDING_APPROVAL` có `approvalExpiresAt ≤ now` → `CANCELLED` (`CancelSource = SYSTEM`, `cancel_reason = APPROVAL_EXPIRED`, không giảm lượt dùng khuyến mãi), xóa slot lock, báo người đăng ký chờ slot, gửi email; booking còn hạn không đổi; job outcome sau đó gửi `APPROVAL_EXPIRED`; **không bao giờ** chuyển sang `PENDING`, đặt `depositSource = MANAGER` hay gọi override; phát `BookingExpired` và `PendingApprovalQueueChanged` (`REMOVED`/`EXPIRED`) | T6.7 |
| BE-IT-09 | Job outcome | Mỗi booking kết thúc có `tadss_decision_id` được gửi đúng một outcome (gửi tuần tự, tối đa một lô cố định mỗi chu kỳ để không tranh rate limit với lúc đặt sân) theo trạng thái (tập giá trị ở INTEGRATION_SPEC §2.3; ánh xạ trạng thái → outcome chốt ở T6.8); chạy job lần hai không gửi lại outcome TADSS đã ghi nhận; lần gửi lại sau lỗi tạm thời dùng cùng `outcome`/`occurredAt` (TADSS chống trùng theo nội dung, §3.7); booking không có `decisionId` (flag tắt, TADSS lỗi, `WALK_IN`) không gửi outcome; WireMock đếm đúng số request | T6.8 |
| BE-IT-10 | Checkout thu phần còn lại; hủy theo chính sách | Checkout booking đã cọc thu đúng `remainingAmount` (`finalTotal` hiện tại − `depositAmount`, gồm dịch vụ thêm sau), hóa đơn `PARTIALLY_PAID → PAID`, booking `COMPLETED`; booking cọc 100 % chỉ thu dịch vụ phát sinh; hủy booking đã cọc hoàn `round(Σ Payment SUCCESS × % hoàn)`, không hoàn quá số đã thanh toán, % hoàn tính theo giờ chơi quy đổi đúng múi giờ (BE-UT-10); số thu tại quầy bằng 0 thì không tạo Payment | T6.9 |
| BE-IT-11 | Phân quyền ba endpoint mới | Không đăng nhập → 401 không theo envelope (`{ message, detail }`); `STAFF`, `CUSTOMER` → 403 không theo envelope (`{ "message": "Forbidden", ... }`, policy `OwnerOrManager`), WireMock nhận 0 request; `BRANCH_MANAGER` duyệt/từ chối booking chi nhánh khác hoặc chưa được gán chi nhánh → 403 envelope `FORBIDDEN`, booking không đổi; `OWNER` duyệt/từ chối được mọi chi nhánh (Q11) | T6.5 |
| BE-IT-12 | Thứ tự kiểm tra trước khi gọi TADSS (DEPOSIT_APPROVAL_API 4.2 bước 1–4) | Body sai → 400; booking không tồn tại → 404 `NOT_FOUND`; chi nhánh khác → 403; không còn `PENDING_APPROVAL` → 409 theo BE-UT-05; `approvalExpiresAt ≤ now` → 409 `APPROVAL_EXPIRED`; thiếu `tadss_decision_id` → 409 `APPROVAL_DECISION_NOT_FOUND` (Q10); dừng ở bước đầu tiên không đạt; ở mọi trường hợp WireMock nhận 0 request, booking và slot lock không đổi, không phát sự kiện | T6.5 |
| BE-IT-13 | Lặp request: bấm lần hai, song song, thử lại sau 503 | Lần hai sau khi lần đầu thành công (cùng Manager, cùng nội dung) → 409 `APPROVAL_ALREADY_RESOLVED`, WireMock không nhận override thứ hai; hai request song song cùng Manager → một 200, một 409 (thua cập nhật có điều kiện); hai Manager song song (TADSS giả lập trả `IDEMPOTENCY_KEY_REUSE` hoặc `ALREADY_OVERRIDDEN` cho người thua) → một 200, một 409 `APPROVAL_ALREADY_RESOLVED`; thử lại sau 503 khi TADSS đã ghi (giả lập trả lại override đã lưu, 200) với cùng Manager và nội dung → 200, booking `PENDING`; thử lại với nội dung khác → 409 `APPROVAL_ALREADY_RESOLVED`, đồng bộ theo override đã lưu (Q9); booking chỉ chuyển trạng thái một lần | T6.5 |
| BE-IT-14 | TADSS lỗi khi duyệt/từ chối: không có đường lùi 100 % | Timeout (`TimeoutMs`), lỗi mạng, 500, 503 `PERSISTENCE_UNAVAILABLE`, 504, 429, 401 → 503 `TADSS_UNAVAILABLE`; booking vẫn `PENDING_APPROVAL`, `depositSource` vẫn `null`, không tạo Payment/URL VNPay, slot lock không đổi, không phát `BookingApprovalResolved`/`PendingApprovalQueueChanged`; mọi lần gửi (kể cả retry) cùng `X-Idempotency-Key`; 401 ghi log mức Error | T6.5 |
| BE-IT-15 | TADSS 200 nhưng booking đã đổi trạng thái trong lúc gọi (DEPOSIT_APPROVAL_API Mục 5 dòng 17) | Job hết hạn, khách hủy (Q8) hoặc request duyệt/từ chối khác ghi trước: BE trả 409 theo quy tắc a/b/c, không hồi sinh booking (giữ kết quả của bên ghi trước), không tạo Payment; ghi log để đối soát theo INTEGRATION_SPEC §4.3 | T6.5, T6.7 |
| BE-IT-16 | Booking `WALK_IN` không qua TADSS | `POST /api/bookings/walk-in` (cờ bật) → WireMock nhận 0 request, booking `CONFIRMED` như hiện tại, trường cọc `null`, không có `tadss_decision_id`; duyệt/từ chối booking `WALK_IN` → 409 `BOOKING_NOT_PENDING_APPROVAL`; job outcome không gửi outcome cho booking này | T6.4 |
| BE-IT-17 | Luồng REJECTED khi đặt sân | `POST /api/bookings/online` nhận `REJECTED` → 400 `BOOKING_REJECTED`, `data = null` (Q1), `reasons` của TADSS không lộ cho khách (Q6); booking `CANCELLED` với `cancel_reason = POLICY_REJECTED`, slot lock giải phóng trước khi trả lỗi; không ghi `slot_interest` (R-8), không giảm lượt dùng khuyến mãi; không tạo Payment; `decisionId` được lưu (INTEGRATION_SPEC §4.3) | T6.4 |
| BE-IT-18 | Sự kiện SignalR của luồng cọc và duyệt | Phát sau commit, đúng nhóm và payload của DEPOSIT_APPROVAL_API Mục 6: `BookingPendingApproval` → `branch_{branchId}`, `role_OWNER` (Q5); `BookingApprovalResolved` → `user_{customerId}` (khách có tài khoản), `branch_{branchId}`, `role_OWNER`; `PendingApprovalQueueChanged` `ADDED`/`NEW` và `REMOVED`/`APPROVED`, `REJECTED`, `EXPIRED`, `CANCELLED` (Q8); payload không có số tiền, `paymentUrl`, SĐT, email, lý do, `decisionId`; thời gian ISO 8601 UTC; lỗi phát sự kiện không làm request thất bại; tên sự kiện khớp `SignalREvents.cs` ↔ `signalr-events.ts` | T6.6 |
| BE-IT-19 | Đổi `EnableTadssDecision` khi đang chạy (mức BE của FI-13) | Đổi cấu hình trong test host qua `IOptionsMonitor`, không khởi động lại: booking mới tạo sau thời điểm đổi < 2 giây không gọi TADSS và cọc 100 % (`FULL_PAYMENT_FALLBACK`); bật lại thì gọi TADSS; booking đã `PENDING_APPROVAL` vẫn duyệt/từ chối qua override (Q7) | T6.2, T6.4 |
| BE-IT-20 | Bộ truy vấn bất biến booking (SC6, Mục 5) | Điều 1–4 chạy trên DB của BE-IT cho 0 vi phạm sau BE-IT-02…BE-IT-19 và BE-IT-21…BE-IT-24; mỗi điều có một bộ dữ liệu vi phạm dựng cố ý và bị phát hiện (chứng minh truy vấn bắt được lỗi); điều 5 (tra chéo `tadss.decision_executions`) chỉ chạy ở E-STAGING. Ca này kiểm công cụ đo, không phải số liệu KPI | T6.10 |
| BE-IT-21 | Thanh toán thất bại, ân hạn, hạn giữ chỗ tuyệt đối | IPN thất bại: Payment `FAILED`, booking vẫn `PENDING`, hạn và slot lock rút về tối đa 5 phút; thanh toán lại trong ân hạn thành công → `PAID_ONLINE`; bỏ đi → job hủy với `cancel_reason = PAYMENT_EXPIRED`; gọi `retry-payment` liên tục dừng ở hạn tuyệt đối (400 `PAYMENT_HOLD_EXPIRED`); IPN thất bại của link cũ không đổi booking | T6.6 |
| BE-IT-22 | Tiền về sau khi booking đã hủy | IPN thành công trên booking `CANCELLED`: Payment `SUCCESS`, Refund 100 % `PENDING`, booking `CANCELLED_PENDING_REFUND`, `cancel_reason` giữ nguyên; nhân viên xác nhận hoàn bằng luồng có sẵn | T6.6 |
| BE-IT-23 | Hủy đơn chưa thanh toán | Khách hủy `PENDING`/`PENDING_APPROVAL`, nhân viên hủy `PENDING_APPROVAL`: một câu lệnh ghi `status`, `cancelled_at`, `cancel_source`, `cancel_reason`; không hoàn tiền; không giảm `UsedCount` khuyến mãi; đơn đã hủy không tính vào giới hạn khuyến mãi mỗi khách (`UsagePerUserLimit`); Payment `PENDING` → `FAILED`; slot nhả; đơn rời hàng chờ (`PendingApprovalQueueChanged`) | T6.9 |
| BE-IT-24 | Đặt online bắt buộc đăng nhập; cọc dưới mức tối thiểu | Không đăng nhập → 401, vai trò khác `CUSTOMER` → 403 (không envelope), WireMock nhận 0 request; TADSS `APPROVED` với số tiền < `MinOnlinePaymentAmount` (gồm 0) hoặc `finalTotal = 0` → 201 `CONFIRMED`, không Payment/URL, cọc áp dụng 0 | T6.4 |
| BE-IT-25 | Lỗi thời gian có sẵn, trên schema thật | Đơn tại quầy trong ngày cho khách có email ghi được hạn link hủy (`timestamptz`, `Kind = Utc`) và email được gửi; khách đăng ký lúc 00:30 giờ VN tính đúng ngày ở báo cáo khách mới và xu hướng khách mới; danh sách "sắp bắt đầu" lúc 23:45 không quay vòng qua nửa đêm; lọc giao dịch điểm theo ngày trả 200 và gồm cả ngày cuối; cột thời gian mới của Phase 6 do BE ghi bằng đồng hồ được tiêm (không dùng `now()` của DB) | T6.9, T6.10 |

### 4.8. FE & E2E (smashcourt-fe, Playwright)

**Môi trường:** *CI* là pipeline của `smashcourt-fe`; *MSW (P6)* là FE chạy với Mock Service Worker chặn mọi request tới BE theo `DEPOSIT_APPROVAL_API.md`, không có BE, TADSS hay VNPay thật (Phase 6, T6.15); *E-STAGING* như Mục 2 (W13–W15). Hiện trạng 12/10/2026: `smashcourt-fe` chưa có test tự động (CI chạy `npm run lint` và `npm run build`); Playwright và MSW được thêm ở T5.25/T6.15. Các ca FE-03…FE-10 là luồng giao diện chạy bằng Playwright trên MSW. Ca nào phụ thuộc câu hỏi mở gắn nhãn **(Qn)** như Mục 4.7. Cột *Task* theo [`BACKLOG_SPRINTS.md`](../planning/BACKLOG_SPRINTS.md) Epic 6B (SV2).

| ID | Kịch bản | Tiêu chí đạt | Môi trường | Task |
| :--- | :--- | :--- | :--- | :--- |
| FE-01 | `tsc` + lint + build | `npx tsc --noEmit -p .`, `npm run lint`, `npm run build` đạt; CI chạy cả ba (`tsc` thêm vào CI ở T6.15) | CI | T6.15 |
| FE-02 | Toàn bộ E2E-01…E2E-14 chạy trên mock MSW | Mỗi E2E có bản chạy trên MSW với cùng thao tác và cùng kiểm tra giao diện; phần kiểm DB/TADSS/VNPay thật thay bằng kiểm request FE gửi (route, body, chỉ dùng `bookingId`) và trang trả về VNPay giả lập; sự kiện SignalR phát giả lập (cách giả lập chốt ở T6.15) | MSW (P6) | T6.15 |
| FE-03 | Mock và kiểu dữ liệu khớp hợp đồng BE ↔ FE | Handler và dữ liệu mẫu của MSW lấy từ ví dụ của DEPOSIT_APPROVAL_API: envelope đủ năm khóa, camelCase, khóa `null` vẫn có, giờ VN `dd/MM/yyyy HH:mm:ss`, `DateOnly` `yyyy-MM-dd`, thời gian SignalR ISO 8601 UTC; type TS khớp DTO (`paymentUrl`, `expiresAt` nullable; trường cọc; `PendingApprovalBookingDto`; `BookingApprovalResultDto`); ở Phase 7 so dữ liệu mẫu với response thật của BE, lệch thì sửa mock trước khi chạy E-STAGING | MSW (P6) | T6.15 |
| FE-04 | Kết quả đặt sân rẽ nhánh theo `status`, không theo `message` | `PENDING` → trang kết quả hiện `depositAmount`/`remainingAmount` từ response, khách bấm "Thanh toán cọc" mới chuyển tới `paymentUrl`; `CONFIRMED` (cọc dưới mức tối thiểu) → "thanh toán tại quầy", không chuyển VNPay; `PENDING_APPROVAL` → trang "Chờ duyệt cọc" có `bookingCode` và đồng hồ đếm tới `approvalExpiresAt`, không chuyển VNPay; 400 `BOOKING_REJECTED` → thông báo không nhận đơn, gợi ý chọn khung giờ khác (Q1); `FULL_PAYMENT_FALLBACK` hiển thị như thanh toán bình thường, không báo lỗi; `depositSource = null` coi là "không áp dụng cọc" | MSW (P6) | T6.12 |
| FE-05 | Hộp thoại duyệt: phần trăm → `depositRate` | Manager nhập phần trăm (0…100, tối đa 2 chữ số thập phân), FE gửi `phần trăm / 100` (30 → `0.3`, 12,5 → `0.125`), không làm tròn âm thầm; `reason` 5..500 code point, không chỉ khoảng trắng; lỗi `VALIDATION_ERROR` hiện dưới ô theo khóa `errors.DepositRate` / `errors.Reason`; nút bị khóa khi đang gửi; số tiền cọc hiển thị lấy từ response của BE, FE không tự tính; giá trị 0 % theo (Q4) | MSW (P6) | T6.13 |
| FE-06 | Thông báo theo `code` khi duyệt/từ chối | Mỗi mã ở DEPOSIT_APPROVAL_API Mục 5 dòng 3–18 có thông điệp và hành động đúng bảng: `NOT_FOUND`, `BOOKING_NOT_PENDING_APPROVAL`, `APPROVAL_ALREADY_RESOLVED`, `APPROVAL_EXPIRED` → báo và tải lại hàng chờ; `FORBIDDEN` → báo không có quyền; `APPROVAL_DECISION_NOT_FOUND` → thông điệp của Q10; 503 `TADSS_UNAVAILABLE` → giữ hộp thoại và giá trị đã nhập, nút "Thử lại" gửi lại đúng nội dung cũ; 500 → thông báo lỗi chung; thông điệp chọn theo `code` vì `apiParser.ts` thay `message` của mọi response ≥ 500 | MSW (P6) | T6.13 |
| FE-07 | 401/403 ngoài envelope và ẩn chức năng theo vai trò | 401 `{ message: "Unauthorized" \| "Invalid token", detail }` và 403 `{ message: "Forbidden", detail }` xử lý theo HTTP status, không đọc `code` (Q11); `STAFF`/`CUSTOMER` không thấy menu và trang hàng chờ/duyệt (RoleGuard); 403 envelope `FORBIDDEN` (nghiệp vụ) hiện thông báo như FE-06 | MSW (P6) | T6.13 |
| FE-08 | Realtime và tải lại qua REST | Nhận `BookingPendingApproval`/`PendingApprovalQueueChanged` → gọi lại `GET /api/bookings/pending-approval` (badge lấy `totalItems`); trang chờ của khách nhận `BookingApprovalResolved` → gọi lại `GET /api/me/bookings/{id}`; luôn tải lại qua REST khi mở trang, khi kết nối lại và khi tab được focus; không lấy số tiền từ payload sự kiện; giao diện `STAFF` bỏ qua hai sự kiện hàng chờ (Q5) | MSW (P6) | T6.12, T6.13 |
| FE-09 | Hiển thị trạng thái `PENDING_APPROVAL` và trường cọc | Nhãn/màu cho `PENDING_APPROVAL` ở danh sách, chi tiết, lịch sân, time grid; chi tiết của khách có `depositAmount`, `remainingAmount`, không có lý do cần duyệt (Q6); chi tiết của nhân viên có lý do (Q6); không hiện nút thanh toán lại khi `PENDING_APPROVAL`; màn checkout của staff hiện số cần thu = `remainingAmount` | MSW (P6) | T6.12, T6.13, T6.14 |
| FE-10 | Trang hàng chờ duyệt | Hiển thị các trường của `PendingApprovalBookingDto` theo thứ tự BE trả về; phân trang (`pageSize` ≤ 50); `OWNER` lọc được theo chi nhánh, `BRANCH_MANAGER` không có bộ chọn chi nhánh; giờ hiển thị theo giờ VN; đơn hết hạn biến mất sau khi tải lại | MSW (P6) | T6.13 |
| FE-11 | Trang kết quả thanh toán thất bại | Hai nút "Thanh toán lại" (gọi `retry-payment`, chuyển tới URL mới) và "Hủy đơn" (gọi hủy của khách, nhả slot); `PAYMENT_HOLD_EXPIRED` hiện "Đã hết thời gian giữ chỗ"; trang kết quả đặt sân rẽ nhánh `CONFIRMED` (thanh toán tại quầy) | MSW (P6) | T6.12 |
| FE-12 | Hàm giờ VN | `getTodayVN()` lúc 00:30 và 06:59 giờ VN trả đúng ngày VN (không lùi một ngày như `toISOString`); `parseBackendDate("05/10/2026 07:00:00")` là ngày 05 tháng 10 (không đảo ngày/tháng); đồng hồ đếm ngược tới hạn `dd/MM/yyyy HH:mm:ss` đúng khi trình duyệt đặt múi giờ khác VN; nút hủy của khách ẩn từ giờ bắt đầu và `BOOKING_ALREADY_STARTED` có thông điệp (DEPOSIT_APPROVAL_API Mục 5 dòng 21) | CI | T6.11, T6.12 |

Các ca E2E chạy ở **E-STAGING** (W13–W15; Phase 6 chạy bản trên MSW của FE-02); đặc tả từng bước (điều kiện trước, thao tác, quan sát kỳ vọng ở UI/API/DB/TADSS, minh chứng, câu hỏi mở) ở [`E2E_TEST_SPECIFICATION.md`](E2E_TEST_SPECIFICATION.md). Mọi ca E2E liên quan **SC6**: sau mỗi ca chạy bộ truy vấn bất biến booking (Mục 5).

| ID | Kịch bản | Tiêu chí đạt (tóm tắt) | KPI | Môi trường | Đặc tả |
| :--- | :--- | :--- | :--- | :--- | :--- |
| E2E-01 | Khách đặt sân → APPROVED → trang thanh toán đúng số tiền cọc → VNPay sandbox → booking xác nhận | Số cọc trên UI = response BE = VNPay = bản ghi TADSS; booking `PAID_ONLINE`, hóa đơn `PARTIALLY_PAID` | SC6 | E-STAGING | [E2E-01](E2E_TEST_SPECIFICATION.md#e2e-01--approved-và-thanh-toán-cọc) |
| E2E-02 | Khách đặt sân → NEED_APPROVAL → trang chờ; Manager duyệt kèm mức cọc → khách nhận link → thanh toán | Không chuyển VNPay khi chờ; một override `APPROVE` với `actor_id` của Manager; cọc = tỷ lệ đã nhập × `finalTotal` | SC6 | E-STAGING | [E2E-02](E2E_TEST_SPECIFICATION.md#e2e-02--need_approval-và-manager-duyệt) |
| E2E-03 | Manager từ chối → khách thấy bị từ chối; slot được giải phóng | Booking `CANCELLED`; slot đặt lại được; một override `REJECT` | SC6 | E-STAGING | [E2E-03](E2E_TEST_SPECIFICATION.md#e2e-03--manager-từ-chối) |
| E2E-04 | Hết hạn duyệt → booking bị hủy; không tự duyệt | `CANCELLED` (`SYSTEM`) trong một chu kỳ job sau hạn; 0 override; duyệt sau hạn → 409 `APPROVAL_EXPIRED`; outcome `APPROVAL_EXPIRED` | SC6 | E-STAGING | [E2E-04](E2E_TEST_SPECIFICATION.md#e2e-04--hết-hạn-duyệt) |
| E2E-05 | Tắt flag → thanh toán 100% | Booking tạo sau khi tắt cờ: cọc 100 % `FULL_PAYMENT_FALLBACK`, không có quyết định TADSS; bật lại thì qua TADSS | SC5 (minh họa trên UI; số đo ở FI-13), SC6 | E-STAGING | [E2E-05](E2E_TEST_SPECIFICATION.md#e2e-05--tắt-cờ-enabletadssdecision) |
| E2E-06 | TADSS dừng → thanh toán 100%; UI không lỗi | 201, cọc 100 %, không thông báo lỗi; TADSS bật lại thì luồng trở lại bình thường | SC6 | E-STAGING | [E2E-06](E2E_TEST_SPECIFICATION.md#e2e-06--tadss-dừng-khi-đặt-sân) |
| E2E-07 | Hai Manager duyệt cùng đơn → một thành công, một thấy thông báo xung đột | Một 200, một 409 `APPROVAL_ALREADY_RESOLVED`; đúng một override | SC6 | E-STAGING | [E2E-07](E2E_TEST_SPECIFICATION.md#e2e-07--hai-manager-duyệt-cùng-đơn) |
| E2E-08 | Manager chỉ thấy đơn chờ của chi nhánh mình | Hàng chờ chỉ chi nhánh được gán; chi nhánh khác → 403 `FORBIDDEN`; `OWNER` thấy tất cả | SC6 | E-STAGING | [E2E-08](E2E_TEST_SPECIFICATION.md#e2e-08--phạm-vi-chi-nhánh) |
| E2E-09 | Staff checkout thu phần còn lại | Số thu tại quầy = `remainingAmount`; hóa đơn `PAID`; booking `COMPLETED`; outcome `COMPLETED` | SC6 | E-STAGING | [E2E-09](E2E_TEST_SPECIFICATION.md#e2e-09--staff-thu-phần-còn-lại) |
| E2E-10 | Hàng chờ cập nhật realtime khi có đơn mới (SignalR) | Đơn mới xuất hiện và đơn đã xử lý biến mất không cần tải lại trang; sau mất kết nối, REST bù lại | SC6 | E-STAGING | [E2E-10](E2E_TEST_SPECIFICATION.md#e2e-10--hàng-chờ-realtime) |
| E2E-11 | TADSS dừng khi Manager duyệt → báo thử lại, booking vẫn chờ duyệt; TADSS bật lại → thử lại thành công | 503 `TADSS_UNAVAILABLE`, không cọc 100 %, booking giữ `PENDING_APPROVAL`; thử lại → 200, đúng một override. *Lý do thêm:* quyết định "duyệt không có đường lùi 100 %" (DEPOSIT_APPROVAL_API, quyết định 4) chưa có E2E — E2E-06 chỉ dừng TADSS lúc đặt sân | SC6 | E-STAGING | [E2E-11](E2E_TEST_SPECIFICATION.md#e2e-11--tadss-dừng-khi-duyệt) |
| E2E-12 | Cùng Manager bấm duyệt lần hai (tab cũ) sau khi lần đầu thành công | 409 `APPROVAL_ALREADY_RESOLVED`, không gọi TADSS lần hai, kết quả lần đầu giữ nguyên. *Lý do thêm:* quy tắc "bấm lần hai ⇒ 409, không trả lại kết quả cũ" (DEPOSIT_APPROVAL_API 4.2) — E2E-07 chỉ phủ hai Manager khác nhau | SC6 | E-STAGING | [E2E-12](E2E_TEST_SPECIFICATION.md#e2e-12--bấm-duyệt-lần-hai) |
| E2E-13 | Staff đặt sân tại quầy khi tích hợp TADSS đang bật | Booking `CONFIRMED`, không trường cọc, không quyết định TADSS, không vào hàng chờ. *Lý do thêm:* quy tắc "chỉ booking `ONLINE` qua TADSS" (INTEGRATION_SPEC §4.2) cần hồi quy luồng tại quầy trên hệ thống tích hợp | SC6 | E-STAGING | [E2E-13](E2E_TEST_SPECIFICATION.md#e2e-13--đặt-sân-tại-quầy-không-qua-tadss) |
| E2E-14 | Khách lặp lại việc giữ chỗ hết hạn thanh toán bị từ chối khi đặt sân | `POST /api/bookings/online` → 400 `BOOKING_REJECTED`; booking `CANCELLED`, slot được nhả, không có Payment/URL VNPay; TADSS lưu quyết định `REJECTED` (`REPEATED_PAYMENT_EXPIRY`); khách khác đặt lại slot được. *Lý do thêm:* nhánh `REJECTED` (Q1) chưa có kịch bản kiểm, nay strategy mặc định 2.1.0 sinh ra nó | SC6 | E-STAGING | [E2E-14](E2E_TEST_SPECIFICATION.md#e2e-14--đặt-sân-bị-từ-chối-theo-chính-sách) |

Mỗi kịch bản E2E lưu video, trace Playwright và correlation id để tra trên Grafana.

### 4.9. RP — Replay dữ liệu SmashCourt

| ID | Kịch bản | Đầu ra |
| :--- | :--- | :--- |
| RP-01 | Xuất 215 booking bằng truy vấn chỉ đọc, ẩn danh hóa, dựng request v2, chạy qua TADSS với cấu hình định tuyến chuẩn | Phân bố trạng thái, mức cọc theo hạng/khung giờ; 0 lỗi contract |
| RP-02 | So sánh chính sách cọc 100% tĩnh với chính sách v2 trên cùng tập booking | Cọc trung bình, tỷ lệ cần duyệt, số tiền giữ được trên các booking có nhãn NO_SHOW (chỉ mang tính mô tả, xem Mục 8) |

**Cách chạy (bước (e) của chế độ đánh giá, Mục 6.1; gói 5.6):**

- **Nguồn:** người dùng tự chạy `tools/evidence/sql/export_smashcourt_bookings.sql` (chỉ đọc, không chọn tên, số điện thoại, email, ghi chú, mã booking, token hủy) bằng `psql` và ghi `data/private/smashcourt_bookings.csv` (thư mục gitignore, mount chỉ đọc vào evaluator). Thiếu file thì phần RP ghi `skipped` kèm lý do, RP-01/RP-02 ở độ phủ danh mục là `INSUFFICIENT_DATA` và bước (e) không thất bại. Mọi định danh được băm HMAC-SHA256 bằng khóa ngẫu nhiên của lượt chạy **trước khi** ghi file vào `evidence/`.
- **Gửi:** mọi booking đã xuất (kể cả `WALK_IN`; `channel` lấy từ `bookings.source`) thành request v2 gửi qua `eval-proxy` (định tuyến chuẩn của stack) vào database chính, khóa idempotency `replay:{runId}:{bookingHash}`. Bản ghi RP nằm chung D1 của lượt chính, phân biệt bằng khóa/`correlation_id`; response RP được nối vào nguồn kiểm EV-2/EV-3 cùng D2.
- **Dời thời gian (Q-04):** mọi mốc của một booking (`requestedAt`, `startTime`, `endTime`, `memberSince`) dời cùng một khoảng `k × 7 ngày`, `k` nhỏ nhất để `requestedAt` sau khi dời không sớm hơn thời điểm gửi. Giữ thứ trong tuần, giờ địa phương và lead time danh nghĩa; D4 ghi `shift_days` và `lead_time_minutes` gốc. Runtime tính cửa sổ duyệt bằng giờ máy chủ chứ không bằng `requestedAt`, nên dời tiến không bao giờ biến booking còn đủ thời gian duyệt thành "không kịp duyệt". Sai lệch còn lại: booking gốc có lead time ≤ `MinLeadMinutes` (120 phút) có thể được runtime xếp `NEED_APPROVAL` trong khi thực tế không kịp duyệt; các bản ghi này gắn `window_bias = true`, và báo cáo nêu thêm kết quả hiệu chỉnh offline (áp lại quy tắc §1.1 của `INTEGRATION_SPEC` bằng lead time gốc trên `strategy_status` của D1).
- **Phạm vi so sánh (Q-12):** bảng RP tách theo kênh; số liệu chính của RP-02 nằm trên tập `ONLINE` (đúng phạm vi BE sẽ gọi TADSS) kèm số liệu toàn tập. `demand` (trường tùy chọn) không được gửi; `tier` là hạng tại thời điểm xuất (xem Mục 8).
- **Lỗi contract (Q-09):** một bản ghi là lỗi contract khi (1) request do adapter dựng không qua `decision-request.schema.json` (lỗi của adapter, bị bắt trước khi gửi nhưng vẫn tính), hoặc (2) response cuối cùng không phải 200, hoặc (3) body 200 không qua `decision-result.schema.json` hay vi phạm quy tắc §3.3 (`deposit` có khi và chỉ khi `APPROVED`; `amount = round(totalPrice × rate)` theo `ROUND_HALF_UP`, 0 chữ số thập phân với VND, 2 với các tiền tệ còn lại). Khi gặp 503/504, bộ gửi thử lại tối đa 2 lần **cùng khóa idempotency**, chờ theo `Retry-After` (trần 5 giây); số lần thử lại được báo cáo, không tính là lỗi nếu lần sau thành công. Bản ghi bị loại *trước khi gửi* chỉ hợp lệ khi lý do nằm trong quy tắc lọc đã duyệt và được đếm riêng (ở RP: `no_invoice`, booking không có hóa đơn; `no_active_courts`, không có sân để dựng item).

### 4.10. PT — Độc lập domain (Hotel Booking Demand)

| ID | Kịch bản | Tiêu chí đạt |
| :--- | :--- | :--- |
| PT-01 | Adapter ánh xạ ≥ 10.000 đơn đặt phòng khách sạn (múi giờ `Europe/Lisbon`) → request v2 → TADSS | 0 lỗi contract; `git diff` của `src/TADSS.Runtime.Api` và `src/Shared.DecisionContractSDK` trước/sau khi thêm adapter là rỗng |
| PT-02 | Phân bố quyết định và đối chiếu mô tả với nhãn `is_canceled` của dataset | Báo cáo mô tả, không kết luận nhân quả |
| PT-03 | Mô phỏng kinh tế các chính sách cọc (`NO_DEPOSIT`, mốc tham chiếu cọc 100 %, Default, MockAlternative chạy bóng, mốc lý tưởng) trên các cặp `PAIRED` của PT, theo chính sách hủy thật của SmashCourt (`S-SC`, hai biến thể hoàn tiền ngang hàng, hai cận do độ phân giải ngày) và các kịch bản độ nhạy | Báo cáo mô tả: tỷ lệ cọc giữ lại mô phỏng, nghĩa vụ ứng trước (proxy), AUC của mức cọc được phân bổ, CI 95 % bootstrap ghép cặp; không kết luận nhân quả, không phải kết quả tài chính |
| PT-04 | Shadow mode trong đánh giá: `MockAlternativeDecisionStrategy` chạy bóng cạnh Default trên instance PT; đối soát tổng hợp (DB + delta counter) và ghép cặp theo `correlation_id` | `RECONCILED` và 0 vi phạm toàn vẹn (`UNKNOWN` = chưa kiểm được; `FAILED` = đỏ); tỷ lệ `PAIRED` ≥ 99 % là ngưỡng vận hành để chạy bản đầy đủ |

**Cách chạy (bước (e) của chế độ đánh giá, Mục 6.1; gói 5.6):**

- **Dataset:** Hotel Booking Demand (Antonio, de Almeida, Nunes, *Data in Brief* 22:41–49, 2019, DOI 10.1016/j.dib.2018.11.126; bài công bố theo CC BY 4.0, người dùng cần xác nhận giấy phép trước khi phân phối lại bất kỳ phần nào). Bản gốc của bài trên ScienceDirect không tải tự động được (HTTP 403), nên evaluator dùng bản TidyTuesday 2020-02-11 `hotels.csv` ghim theo commit và SHA-256 trong `tools/evidence/hotel_adapter.py` (119.390 dòng, 32 cột), tải vào volume `eval-datasets` (cache giữa các lượt, `--clean` xóa). Sai SHA-256 thì phần PT `failed` (bước (e) `failed`); không tải được thì PT `skipped` (SC7 `INSUFFICIENT_DATA`). File gốc không bao giờ vào repo hay Evidence Pack.
- **Instance riêng (Q-02):** PT gửi vào `eval-tadss-portability`, cùng image runtime nhưng database riêng `tadss_portability` (nối thẳng PostgreSQL, không qua Toxiproxy), Redis với `InstanceName` riêng, không tiêm lỗi, telemetry `ServiceInstanceId = tadss-pt`. Chỉ cấu hình bằng biến môi trường, không sửa mã: `Tadss:Validation:MaxItemMinutes = 100800` (lưu trú dài nhất 69 đêm = 99.240 phút) và `Tadss:Strategies:Default:HighValueThreshold = 750` (EUR). Ngưỡng 750 EUR được chọn **một lần, trước lượt chạy**, từ phân vị p90 của `adr × số đêm` trên dataset (p90 = 729; khoảng 9,5 % đơn vào `NEED_APPROVAL`) và không chỉnh theo kết quả.
- **Ánh xạ và lọc (Q-08):** theo spec 4.6 (múi giờ `Europe/Lisbon`, tiền tệ EUR, một item 14:00 → 12:00). Quy tắc lọc trước khi gửi, mỗi lý do được đếm và báo cáo riêng: số đêm = 0 (715 dòng), `adr` < 0 (1), ngày đến không hợp lệ (0), lưu trú vượt `MaxItemMinutes` (0); còn 118.674 dòng hợp lệ. Cỡ mẫu: bản nhanh 10.000 dòng hợp lệ lấy mẫu tất định (seed `20260907`, ghi trong manifest), bản đầy đủ mọi dòng hợp lệ. Thời gian dời theo Q-04 như RP (Lisbon đổi giờ mùa hè nên dời theo ngày lịch).
- **Lỗi contract:** cùng định nghĩa Q-09 với RP (Mục 4.9), khóa idempotency `portability:{runId}:{source_row}`.
- **SC7 và kiểm mã (Q-11):** evaluator đọc git của `/repo` (chỉ đọc) và xét các commit chạm `tools/evidence/hotel_adapter.py`: không commit nào được chạm `src/TADSS.Runtime.Api/**` hay `src/Shared.DecisionContractSDK/**`, và hai thư mục đó không có thay đổi chưa commit. Kết quả ghi rõ commit đã xét (`raw/analysis/sc7_code_check.json`). Khi adapter chưa commit thì phần kiểm mã là "chưa xét được" và SC7 là `INSUFFICIENT_DATA`.
- **PT-03/PT-04 (gói 5.7b):** instance PT chạy thêm `MockAlternativeDecisionStrategy` ở chế độ shadow (chạy bóng, không đổi response). Sau khi gửi xong, bước (e) xả hàng đợi shadow và **đối soát tổng hợp** (`tools/evaluation/evaluator/shadow_drain.py`): hiệu counter `tadss.shadow.*` giữa trước và sau lượt gửi so với số dòng `PRIMARY`/`SHADOW` trong `tadss.decision_executions` của đúng tập `correlation_id` của lượt; nhãn `RECONCILED`, `UNKNOWN` (chưa kiểm được: thiếu Prometheus hay DSN, counter reset, quá thời gian chờ) hoặc `FAILED` (counter không khớp DB). Đây là đối soát tổng hợp (DB + counter), không phải bằng chứng tuyệt đối về từng tác vụ. Bằng chứng theo request là ghép cặp theo `correlation_id` ở bước (f) (`tools/evidence/shadow_pairing.py`): mỗi request vào đúng một trạng thái `PAIRED`, `PRIMARY_FALLBACK`, `SHADOW_FALLBACK` hoặc `UNPAIRED`, và **chỉ `PAIRED` được dùng để so sánh hai strategy** (fallback cọc 100 % không phải quyết định của strategy). Cũng ở bước (f), `tools/evidence/policy_economics.py` (công thức thuần ở `policy_metrics.py`) dựng `raw/d5/policy_economics.json`: sự kiện giữ cọc là `reservation_status ∈ {Canceled, No-Show}` (nhãn `is_canceled` chỉ dùng cho AUC và Δr); chính sách hủy thật của SmashCourt đọc từ ảnh chụp có commit `tools/evidence/config/smashcourt_cancel_policies.json` (`cancel_policy.py`, theo thuật toán chọn mốc của BE); dataset chỉ có ngày hủy nên `S-SC` báo **hai cận** (hủy lúc 00:00 và 23:59), cùng giả định giờ đến 14:00; **hai biến thể hoàn tiền** (`REFUND_OF_PAID`, `REFUND_OF_TOTAL_CAPPED`) đi ngang hàng vì gốc tính hoàn tiền chưa chốt (Q-6); rủi ro hoàn vượt của mã BE hiện tại (`AS_IS_BE`) đo riêng, không vào xếp hạng; `NEED_APPROVAL` tính r = 1 (chính) và r = 0 (cận dưới); CI 95 % bootstrap ghép cặp với seed cố định. Kịch bản độ nhạy: `S-SC×2`, `S-SC÷2` (giãn/co mốc hoàn tiền ×2 / ÷2), `S-FULL` (mọi sự kiện giữ toàn bộ cọc), `S-NOSHOW` (chỉ `No-Show` giữ cọc). Mọi con số là **chỉ số mô phỏng theo kịch bản, không phải kết quả tài chính** và giả định hành vi khách không đổi theo mức cọc. Cột D5, cấu trúc JSON và quy tắc giấy phép: [`DATASET_SCHEMA_SPECIFICATION.md`](DATASET_SCHEMA_SPECIFICATION.md) Mục 2–3.

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
| OB-4 | Chế độ vận hành (`docker compose up -d --build`): sau vài request tới TADSS, dashboard có dữ liệu (kiểm bằng API Grafana/Prometheus trong tối đa 60 giây) | tự động (`verify-observability.sh`, chạy trong bước (a) của evaluator) | P5 |
| OB-5 | Telemetry trong tiến trình: mỗi metric ghi đúng tên, nhãn, thời điểm (quyết định, override, pipeline, fallback, gauge breaker, gauge hàng chờ); span có cây cha–con đúng và log mang trace context; không có thuộc tính bí mật (khóa API, mã băm, chuỗi kết nối); collector không chạy không làm ảnh hưởng request | tự động (test) | P5 |

### 4.15. DOC — Tài liệu (gói 5.7)

| ID | Kiểm chứng | Ai chạy | Xây dựng |
| :--- | :--- | :--- | :--- |
| DOC-1 | Mọi file trong `docs/**` và các README/CLAUDE.md của repo có đúng một dòng trong bảng kiểm kê ở mục "Tài liệu" của `README.md` (gốc repo), nhóm `hiện hành` hoặc `lịch sử`; file lịch sử dạng markdown mở đầu bằng banner `> **Tài liệu lịch sử**` | tự động (`tools/docs/check_docs.py`: CI, bước (a)) | P5 |
| DOC-2 | Tài liệu hiện hành không mô tả như hiện tại những thứ đã gỡ (danh sách `REMOVED_TERMS`): mỗi lần xuất hiện phải nằm trong file lịch sử hoặc trên dòng nói về quá khứ/cấm (`gỡ`, `đã bỏ`, `thay cho`, `thay bằng`, `không còn`, `không dùng`, `cấm`, `phase4-baseline`, `lịch sử`). Đoạn văn kể lại một quyết định cũ mà không có dấu hiệu quá khứ (ví dụ "MediatR cung cấp pipeline…" trong một ADR) là vi phạm, trừ khi cả file là tài liệu lịch sử; cách xử lý là phân loại lại file hoặc viết lại câu, không thêm từ khóa cho lọt | tự động (như DOC-1) | P5 |
| DOC-3 | Mọi link nội bộ tương đối trong markdown của repo trỏ tới file tồn tại; `#anchor` khớp tiêu đề (slug GitHub); link ra ngoài repo và link http không kiểm | tự động (như DOC-1) | P5 |
| DOC-4 | Bảng `docs/guides/CONFIGURATION_REFERENCE.md` khớp hai chiều với các `Options` có `SectionName` `Tadss:*` trong mã | tự động (unit test) | P5 |

---

## 5. ĐỊNH NGHĨA KPI

| Mã | Chỉ số | Cách đo | Mục tiêu thiết kế |
| :--- | :--- | :--- | :--- |
| **SC1** | Độ trễ quyết định bình thường | p95 thời gian xử lý phía server của `POST /api/v2/decisions` trong LT-01 (E-EVAL, PostgreSQL/Redis thật); báo cáo thêm p95 API tạo booking của LT-04 | p95 TADSS < 150ms; p95 tạo booking E2E < 1000ms |
| **SC2** | Độ trễ khi strategy lỗi | Trong FI-02/FI-03/FI-04 và LT-03 | 100% request kết thúc bằng quyết định fallback an toàn (`APPROVED` cọc 100%, `executionType = FALLBACK`) với thời gian pipeline ≤ 500ms; p99 endpoint ≤ 700ms |
| **SC3** | Toàn vẹn & bất biến audit | Mỗi lượt chạy: số response 200 = số bản ghi `PRIMARY`; thử `UPDATE/DELETE` | 0 thiếu, 0 thừa; 0 lần sửa thành công |
| **SC4** | Idempotency & chống ghi đè đồng thời | IT-04, IT-07 và các lượt LT | 0 bản ghi quyết định trùng; 0 override kép |
| **SC5** | Feature flag | FI-13 (E-STAGING). BE-IT-19 kiểm cùng hành vi ở mức BE và E2E-05 minh họa trên giao diện; hai ca này không thay số đo của FI-13 | Hiệu lực < 2 giây; 100% booking trong lúc tắt áp cọc 100% |
| **SC6** | Không hỏng booking | Bộ truy vấn bất biến chạy sau FI-11…FI-14 và sau các kịch bản E2E (E2E-01…E2E-14), chỉ xét dữ liệu trong khoảng thời gian của lượt chạy (nhánh `staging` mang sẵn dữ liệu cũ); tính đúng của bộ truy vấn kiểm ở BE-IT-20 | 0 vi phạm |
| **SC7** | Độc lập domain | PT-01 | ≥ 10.000 bản ghi; 0 lỗi contract; 0 thay đổi mã runtime/SDK |
| **SC8** | Chất lượng bộ kiểm thử | Toàn bộ danh mục; MT | 100% test case đạt; mutation score ≥ 70% |

**Cách kết luận SC7 (tính được từ gói 5.6, `tools/evidence/kpi.py`):**

| Kết luận | Điều kiện |
| :--- | :--- |
| `PASS` | PT gửi ≥ 10.000 bản ghi, 0 lỗi contract (Q-09) và kiểm mã (Q-11) đạt |
| `FAIL` | Kiểm mã không đạt (commit chạm adapter đồng thời chạm `src/TADSS.Runtime.Api/**` hoặc `src/Shared.DecisionContractSDK/**`, hoặc hai thư mục đó có thay đổi chưa commit), hoặc có ≥ 1 bản ghi lỗi contract (kể cả request do adapter dựng sai schema bị chặn trước khi gửi) |
| `INSUFFICIENT_DATA` | PT bị bỏ (`skipped`) hoặc lỗi hạ tầng/sai SHA-256; gửi dưới 10.000 bản ghi; hoặc phần kiểm mã chưa kết luận được (adapter chưa commit, hoặc không đọc được git) |

Số liệu RP/PT chỉ được báo cáo là "kết quả mô tả"; chỉ SC7 là KPI đạt/không đạt của PT. Lượt chạy lúc phát triển (adapter còn là tệp chưa commit) luôn cho `INSUFFICIENT_DATA`; sau khi người dùng commit adapter, chạy lại `--steps e,f` để SC7 chuyển thành `PASS`.

**Bộ truy vấn bất biến booking (SC6)**, chạy trên DB SmashCourt sau mỗi kịch bản:

1. Booking `CONFIRMED`/`PAID_ONLINE` từ luồng TADSS phải có hóa đơn với số đã thanh toán ≥ số tiền cọc đã áp dụng (`invoices.deposit_amount`; bằng 0 với cọc dưới mức tối thiểu và duyệt 0 %).
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
5. Sinh manifest SHA-256 cho mọi file; chạy bộ sinh báo cáo để sinh `report.html`.

Cấu trúc thư mục, schema dữ liệu và quy tắc ẩn danh hóa: xem [`DATASET_SCHEMA_SPECIFICATION.md`](DATASET_SCHEMA_SPECIFICATION.md).

Với nhóm TADSS chạy ở E-EVAL, các bước 2–5 trên do evaluator thực hiện tự động trong một lượt (Mục 6.1), không còn script rời cho từng nhóm.

### 6.1. Chế độ đánh giá một lệnh (D-12)

> **Hiện trạng:** đã hiện thực ở gói 5.5 (evaluator `tools/evaluation/`, bộ dựng Evidence Pack `tools/evidence/`, kịch bản k6 `tests/load/`). Thiết kế: D-12 và Mục 4.5 của `spec/PHASE5_DESIGN_SPEC.md`.

Lệnh chuẩn: `docker compose up -d --build` (D-14; bản đầy đủ: `EVAL_MODE=full`), chạy lại riêng: `bash tools/evaluation/run.sh [quick|full] [--steps a,b,...] [--clean]` (hướng dẫn từng bước, đọc kết quả, sự cố thường gặp: [`docs/guides/EVALUATION_RUN_GUIDE.md`](../guides/EVALUATION_RUN_GUIDE.md), nơi sở hữu quy trình chạy). Cả hai cách dựng stack cô lập (TADSS build Release, PostgreSQL 16, Redis 7, Toxiproxy, Grafana; **không bao giờ chạm Neon**) cùng container **evaluator**, và thêm service `eval-tadss-portability` cho PT. Evaluator chạy tuần tự rồi thoát kèm bản tóm tắt (`EVAL_STEPS` hay `--steps` nhận các chữ a…f):

| Bước | Nội dung |
| :--- | :--- |
| **a** | Toàn bộ bộ test gộp một lượt: unit, integration, contract, fault injection FI-01…10, nhiều instance SCALE-1, bảo mật; kết quả gom theo mã danh mục (UT/IT/CT/FI/SEC/NG…). Cùng bước: `verify-expose.sh` (NG, SEC-6), `verify-observability.sh` (OB-2/OB-4 của chế độ vận hành; trong container dashboard được copy vào Grafana thay cho mount), `verify-export.sh` (truy vấn xuất SmashCourt), pytest `tools/evaluation`, `tools/evidence` và `tools/docs`, `python tools/docs/check_docs.py` (DOC-1…3; DOC-4 nằm trong unit test), OB-2 trên Grafana của stack đánh giá |
| **b** | Kịch bản tải k6: LT-01, LT-02, LT-05 (LT-04 E2E qua SmashCourt-BE để Phase 6) |
| **c** | Sự cố dưới tải: LT-03 + FI qua Toxiproxy (cắt PostgreSQL, làm chậm Redis, strategy lỗi); quan sát trực tiếp trên dashboard Grafana: độ trễ tăng, fallback `APPROVED` cọc 100%, 503, hồi phục |
| **d** | Schemathesis (CT-03) và Stryker mutation (MT-01…03) |
| **e** | Dữ liệu thực tế (gói 5.6): RP (replay booking SmashCourt qua `eval-proxy`, bỏ qua nếu không có `data/private/smashcourt_bookings.csv`) rồi PT (adapter Hotel Booking Demand vào `eval-tadss-portability`); ghi `raw/replay/`, `raw/portability/`; với PT còn xả hàng đợi shadow và đối soát tổng hợp (PT-04, nhãn `RECONCILED`/`UNKNOWN`/`FAILED` vào `summary.json`). Mục 4.9, 4.10 |
| **f** | Gom minh chứng vào `evidence/<run_id>/`: `report.html` (đạt/không đạt theo SC1…SC8, biểu đồ p50/p95/p99, biểu đồ dòng thời gian sự cố vẽ từ metric Grafana đã thu, số test từng nhóm, mục "Dữ liệu thực tế" cho RP/PT, SC7), dữ liệu thô (trx theo `CatalogId`, kết quả k6, metric export, `chaos_log`, D4/D5 và phân tích RP/PT, gồm `raw/d5/policy_economics.json` của PT-03/PT-04) và `manifest.json` chứa SHA-256 từng file (EV-1…EV-5). Luôn là bước cuối. Bước trước `failed` (kể cả (e) khi đích chưa sẵn sàng hoặc sai SHA-256) không chặn (f); chỉ `error` hạ tầng (ngoại lệ bất ngờ) làm các bước sau `skipped` |

**Thời lượng đo thật (bản nhanh, máy lab, lượt `EV-20261015-EVAL-quick-r2` chạy bằng `docker compose up -d --build` sau gói 5.7b, đạt mọi tiêu chí áp dụng được):** (a) 693 s, (b) 400 s, (c) 511 s, (d) 48 s, (e) 92 s, (f) 61 s — tổng 1.805 s (~30 phút), chưa tính lần đầu dựng image.

**Hai độ dài:** **bản nhanh** (đo thật ~28 phút, tải rút ngắn, không chạy Stryker) để diễn tập; **bản đầy đủ** (ước tính ≈ 2,8 giờ từ các bước đã đo: LT-01 3 × 11 phút, LT-02 ~15 phút, LT-05 soak 61 phút, Stryker ≈ 21 phút; đo trọn vẹn ở gói 5.7) để lấy số liệu chính thức. Bước (e) ở bản nhanh đo thật 101 s (`quick-r1`, PT 10.000 request, 107 request/s) và, theo ngoại suy từ 107–330 request/s tùy độ bận của máy, khoảng 8–18 phút ở bản đầy đủ (PT ≈ 118 nghìn request; chưa đo trọn vẹn, thuộc gói 5.7). Replay booking SmashCourt (RP-01/RP-02) và adapter Hotel Booking Demand (PT-01…PT-04) đổ vào cùng Evidence Pack và báo cáo.

**Giới hạn đo lường:** toàn bộ chạy trên một máy (k6, TADSS và cơ sở dữ liệu tranh nhau CPU) nên số liệu là số liệu phòng lab; báo cáo ghi thông số máy (CPU/RAM) và liệt kê vào Mục 8.

---

## 7. PHÂN TÍCH & TRÌNH BÀY

- Độ trễ báo cáo p50/p95/p99/max kèm số mẫu; với LT-01 và LT-03 báo cáo khoảng tin cậy 95% của p95 bằng bootstrap trên 3 lần lặp.
- Biểu đồ trong `report.html` (cần thêm bảng hay biểu đồ cho khóa luận thì lấy từ CSV D4/D5 trong pack): phân bố độ trễ theo kịch bản; đường thời gian độ trễ + trạng thái circuit khi tiêm lỗi; tỷ lệ trạng thái quyết định; phân bố mức cọc theo hạng/khung giờ (RP, PT).
- Mỗi con số trong báo cáo dẫn về `run_id` và file dữ liệu thô tương ứng.

---

## 8. GIỚI HẠN & MỐI ĐE DỌA TÍNH HỢP LỆ

| Giới hạn | Ảnh hưởng | Cách xử lý |
| :--- | :--- | :--- |
| 215 booking SmashCourt là dữ liệu môi trường phát triển; nhãn `NO_SHOW` do job tự gán khi không check-in, `CANCELLED` phần lớn do hết hạn thanh toán | Không phản ánh hành vi khách thật | Chỉ dùng làm input có hình dạng thực tế (RP); mọi số liệu theo nhãn chỉ mang tính mô tả |
| Replay là phân tích phản thực tế: mức cọc có thể làm thay đổi chính hành vi khách | Không suy ra được "đã ngăn bao nhiêu no-show" | Không đưa kết luận nhân quả; để Khóa luận xử lý bằng đánh giá off-policy/shadow mode |
| Dataset khách sạn khác domain | Chỉ là proxy | Dùng cho kiểm chứng độc lập domain và mô tả, không kết luận về sân cầu lông |
| Dời thời gian theo bội số 7 ngày (Q-04) để bản ghi cũ trở thành yêu cầu "trong tương lai" | Độ lệch δ ∈ [0; 7 ngày) so với thời điểm gửi; booking gốc có lead time ≤ `MinLeadMinutes` (120 phút) có thể được runtime xếp `NEED_APPROVAL` trong khi thực tế không kịp duyệt | Gắn cờ `window_bias`; báo cáo RP nêu cả kết quả runtime lẫn kết quả hiệu chỉnh offline theo lead time gốc |
| `tier` của RP là hạng khách **tại thời điểm xuất**, không có lịch sử hạng; `demand` không gửi (trường tùy chọn) | Mức cọc theo hạng có thể lệch so với thời điểm đặt thật; tín hiệu nhu cầu không tham gia quyết định | Nêu trong báo cáo; không suy diễn mức cọc theo hạng thành hành vi lịch sử |
| Giờ danh nghĩa của PT: check-in 14:00, check-out 12:00, giờ gửi yêu cầu 10:00 (dataset chỉ có ngày) | Lead time và độ dài lưu trú là xấp xỉ; không đại diện giờ thật | Nêu ở đây và trong docstring của adapter; PT chỉ dùng để kiểm chứng hợp đồng và độc lập domain |
| PT-03 dùng dữ liệu proxy khách sạn và giả định giờ đến 14:00 (Lisbon) cho mọi đơn; giá trị đơn là `adr × số đêm` | Tỷ lệ cọc giữ lại mô phỏng không đại diện hành vi hay giá của sân cầu lông; đây là chỉ số mô phỏng theo kịch bản, không phải kết quả tài chính | Báo cáo gắn câu "chỉ số mô phỏng theo kịch bản, không phải kết quả tài chính" và giả định hành vi khách không đổi theo mức cọc; không kết luận nhân quả, không quy ra tiền |
| Dataset chỉ có ngày hủy; mốc hoàn tiền của SmashCourt (2 giờ, 24 giờ…) tính theo giờ trước khi bắt đầu | Một đơn hủy trong ngày có thể rơi vào hai mốc khác nhau tùy giờ hủy thật (00:00 hay 23:59) và giờ đến thật; phân loại mốc là bất định | `S-SC` báo hai cận (hủy lúc 00:00 và 23:59) thay vì một số; kết luận xếp hạng chỉ nêu khi đúng ở cả hai cận, ngược lại ghi "không phân biệt được" hoặc "phụ thuộc giả định" |
| Gốc tính tiền hoàn của chính sách hủy chưa chốt (Q-6: hoàn theo tiền đã cọc hay theo giá trị đơn, có chặn trên hay không); mã BE hiện tại hoàn theo giá trị đơn, không chặn (rủi ro B-1) | Số cọc giữ lại mô phỏng phụ thuộc cách hiểu; `AS_IS_BE` chỉ đo phần hoàn vượt số cọc đã thu và, vì đơn `NEED_APPROVAL` coi như cọc 100 %, đánh giá THẤP rủi ro trên các đơn đó | **Q-6 đã chốt 18/10/2026** (Q12 của `DEPOSIT_APPROVAL_API.md`): hoàn theo tiền đã thanh toán, tức biến thể `REFUND_OF_PAID` là chính sách; `REFUND_OF_TOTAL_CAPPED` giữ làm phân tích độ nhạy; `AS_IS_BE` đứng riêng (rủi ro của mã BE trước Phase 6). Nhãn "tạm thời" trong báo cáo của evaluator đổi ở lượt chạy lại sau |
| Ngưỡng `HighValueThreshold = 750` EUR của PT chọn một lần, trước lượt chạy, từ phân vị p90 của dataset; PT chạy trên instance riêng với `MaxItemMinutes = 100800` | Tỷ lệ `NEED_APPROVAL` (≈ 9,5 %) do ngưỡng chọn, không phải kết quả đo; instance riêng khác cấu hình `eval-tadss-1/2` | Ngưỡng và cấu hình ghi trong `source.json`/manifest; PT-02 chỉ mô tả, không dùng để đánh giá chất lượng chính sách |
| Số liệu RP chính nằm trên tập `ONLINE` (đúng phạm vi BE sẽ gọi TADSS); tập `WALK_IN` vẫn được gửi và báo cáo riêng | Số toàn tập và số `ONLINE` khác nhau | Bảng RP tách theo kênh; nêu cả hai |
| Không có `data/private/smashcourt_bookings.csv` (người dùng chưa xuất) | RP `skipped`, RP-01/RP-02 `INSUFFICIENT_DATA`; lượt không có số liệu SmashCourt | Ghi rõ trong báo cáo; chạy lại `--steps e,f` khi có file |
| E-EVAL chạy trên một máy: k6, TADSS, PostgreSQL, Redis, Toxiproxy và Grafana tranh nhau CPU/RAM | Số tuyệt đối là số liệu phòng lab, khác production; độ trễ đo được gồm cả nhiễu do tải của chính bộ công cụ | Báo cáo kèm thông số máy (CPU/RAM) và liệt kê vào giới hạn; so sánh tương đối giữa kịch bản trên cùng máy; ghi nhận bản nhanh chỉ để diễn tập |
| Cách ly strategy không hợp tác (`IsolatedStrategyInvoker`, FI-05) chỉ **giới hạn thời gian chờ** của request: strategy bỏ qua `CancellationToken` hoặc chặn đồng bộ bị cắt ở inner timeout (fallback cọc 100%) nhưng không bị dừng; task bị bỏ rơi vẫn giữ một luồng riêng và tiêu thụ CPU tới khi tự kết thúc | Nhiều task bị bỏ rơi cùng lúc (strategy treo lặp lại dưới tải) tạo nhiều luồng và tranh CPU, kéo dài độ trễ của request khác (phần đồng bộ của strategy chạy trên luồng riêng nên không còn làm cạn thread pool); thí nghiệm sự cố dưới tải (LT-03) có thể phóng đại hiệu ứng này | Đo `tadss.strategy.abandoned` trong LT-03; báo cáo số task bị bỏ rơi cùng độ trễ p99; không kết luận TADSS "dừng được" strategy lỗi — chỉ cô lập luồng request khỏi nó |
| E-EVAL chạy ở môi trường ASP.NET Core `Testing` (decorator tiêm lỗi chỉ đăng ký ở đây) với rate limit nâng lên 100000 và trace lấy mẫu 10 % | HSTS, validator khóa API và giới hạn tốc độ thật khác Production; không phải request nào cũng có trace (metric và audit không lấy mẫu nên KPI không bị ảnh hưởng) | Ghi vào `report.html`; hành vi giới hạn tốc độ và khóa API được kiểm riêng ở SEC-1…7 |
| Histogram metric chỉ chính xác tại biên bucket (…, 150, 500, 700, … ms) và cửa sổ đo theo metric lệch tối đa một `MetricExportIntervalMs` ở hai đầu | Phân vị không trùng biên là ước lượng; tỉ lệ ≤ 150/500/700 ms là phép đếm chính xác | SC1 kết luận bằng cả tỉ lệ bucket phía server và p95 phía client của k6 (chính xác từng request) |
| Evaluator mount `/var/run/docker.sock` (Testcontainers cho integration test) | Container có quyền điều khiển Docker của máy chủ | Chỉ chạy trên máy của chính người dùng; mã nguồn mount chỉ đọc, file bí mật không được copy |
| Tài nguyên VM Docker (Docker Desktop) quyết định độ trễ đo được; trong container evaluator, `verify-observability.sh` copy dashboard vào Grafana thay cho mount (daemon không thấy đường dẫn `/work`) | Số liệu phụ thuộc cấu hình máy; đường dẫn mount dashboard của chế độ vận hành không được kiểm bằng Docker thật trong evaluator | Báo cáo ghi CPU/RAM Docker và digest image; mount được `ComposeFixtureAlignmentTests` khóa tĩnh và job CI `observability-check` kiểm bằng Docker thật |
| Không có pilot với người dùng thật | Không đo được mức độ dễ dùng và hành vi thật của khách/Manager khi gặp chính sách cọc mới | Nêu rõ trong báo cáo; chất lượng quyết định chỉ được đánh giá bằng dữ liệu import (RP, PT); để Khóa luận đánh giá trên vận hành thật |

---

## 9. LƯU TRỮ: MICRO-BENCHMARK PHASE 4

Micro-benchmark Tuần 8 (19 kịch bản × 4 mức tải) chạy trên EF Core InMemory với strategy giả lập bằng `Task.Delay`, nên chỉ mô tả **overhead của pipeline trong phòng lab** và **không** được dùng làm minh chứng cho SC1–SC8. Mã và báo cáo đã gỡ khỏi repo ở Phase 5, tra lại qua tag git `phase4-baseline`; đo hiệu năng của Phase 5 dùng k6 (LT-01…LT-05) trên hạ tầng thật.
