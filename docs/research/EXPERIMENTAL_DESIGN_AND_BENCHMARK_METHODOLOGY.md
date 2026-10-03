# GIAO THỨC ĐÁNH GIÁ, DANH MỤC KIỂM THỬ & KPI — v2.0

> **Vai trò của tài liệu:** nguồn duy nhất về **danh mục kiểm thử (test catalogue)**, **định nghĩa KPI SC1–SC9**, môi trường đo và giao thức thực nghiệm của Đồ án 2. Lịch xây dựng và chạy: [`MASTER_EXECUTION_PLAN.md`](../planning/MASTER_EXECUTION_PLAN.md). Định dạng dữ liệu minh chứng: [`DATASET_SCHEMA_SPECIFICATION.md`](DATASET_SCHEMA_SPECIFICATION.md).

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
| **RQ3** Chất lượng tích hợp & sử dụng | SC1 (E2E), SC9 | LT-04, E2E, UAT |
| Độc lập domain | SC7 | PT |
| Chất lượng bộ kiểm thử | SC8 | Toàn bộ, MT |

---

## 2. MÔI TRƯỜNG THỰC NGHIỆM

| Mã | Môi trường | Thành phần | Dùng cho |
| :--- | :--- | :--- | :--- |
| **E-TC** | Testcontainers (trong `dotnet test`) | PostgreSQL 16, Redis 7, Toxiproxy | UT, IT, CT-01/02/04, BE-IT, một phần FI |
| **E-LOCAL** | `docker compose` trên máy dev | TADSS, PostgreSQL 16, Redis 7, Toxiproxy, `grafana/otel-lgtm`; SmashCourt-BE/FE khi chạy E2E | FI, LT, CT-03, MT, RP, PT |
| **E-STAGING** | Máy chủ staging (chốt ở W10) | SmashCourt FE/BE + TADSS + PostgreSQL + Redis + VNPay sandbox | E2E trên hệ thống thật, UAT |

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
| **Schemathesis** (Docker) | Sinh request tự động từ OpenAPI v2 để dò lỗi contract | E-LOCAL | CT-03 |
| **Stryker.NET** | Mutation testing — đo khả năng phát hiện lỗi của bộ test | TADSS | MT |
| coverlet + **ReportGenerator** | Độ bao phủ mã (báo cáo HTML) | TADSS, SmashCourt-BE | Bổ trợ MT |
| **Toxiproxy** | Tiêm lỗi mạng: ngắt, trễ, giới hạn băng thông, reset kết nối | E-TC, E-LOCAL | FI |
| **k6** | Tải: TADSS trực tiếp và E2E qua SmashCourt-BE; xuất summary JSON và đẩy metric | E-LOCAL | LT |
| **OpenTelemetry .NET** + **`grafana/otel-lgtm`** | Trace, metric, log tập trung; dashboard Grafana làm minh chứng trực quan | E-LOCAL, E-STAGING | FI, LT, E2E |
| **Playwright** | E2E UI: video, trace viewer, HTML report | smashcourt-fe | E2E, FE |
| **MSW** (Mock Service Worker) | Mock API cho FE trong Phase 6 theo API contract đã chốt | smashcourt-fe | FE |
| **Jupyter notebook** (pandas, plotly) | Sinh báo cáo minh chứng HTML từ dữ liệu thô | TADSS `docs/research/` | Toàn bộ |

**Tiêm lỗi cho strategy:** dùng decorator chỉ đăng ký trong môi trường kiểm thử (bật qua cấu hình `Testing`), không có trong cấu hình production. Lỗi hạ tầng (Redis, PostgreSQL, mạng giữa BE và TADSS) luôn tiêm bằng Toxiproxy.

---

## 4. DANH MỤC KIỂM THỬ

Cột **Xây dựng** là phase viết test; mọi test case được **chạy lại toàn bộ ở Phase 8**.

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
| UT-14 | Kiến trúc và quy ước (gói 5.2d, Clean Architecture nhiều project): đồ thị tham chiếu SDK ← Application ← Infrastructure ← Runtime.Api đúng quy tắc; Application không tham chiếu assembly hạ tầng; controller/middleware không dùng kiểu Infrastructure; namespace khớp project + thư mục; không còn kiểu/namespace "V2"; mỗi kiểu public một file; `Options` chỉ ở `Options/`, exception nghiệp vụ chỉ ở `Exceptions/`; port chỉ khai báo ở `Application/Abstractions`, EF Core chỉ trong `Persistence/`; bất biến tỷ lệ cọc 4 chữ số thập phân; mọi options có validator đăng ký; thứ tự middleware qua hành vi quan sát được; trần 250 dòng mỗi file nguồn (trừ `Migrations/`) | Vi phạm quy ước làm test đỏ; thêm kiểu sai chỗ bị phát hiện ngay | P5 |

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
| IT-12 | Shadow mode | Bản ghi `SHADOW` được lưu; response không đổi; không vào hàng chờ |
| IT-13 | Cache trên Redis thật; strategy tắt cache | Có `CACHE_HIT` khi được phép; strategy tắt cache không bao giờ hit |
| IT-14 | Định tuyến: request giờ cao điểm / thấp điểm theo giờ chi nhánh | Đúng strategy theo quy tắc |
| IT-15 | Migration trên DB trống; model snapshot | Áp dụng thành công; không có thay đổi chưa tạo migration |

### 4.3. CT — Contract

| ID | Kịch bản | Tiêu chí đạt |
| :--- | :--- | :--- |
| CT-01 | Mọi response trong IT hợp lệ theo JSON schema v2 | 0 vi phạm |
| CT-02 | Public API surface SDK so với baseline v2 | Khớp |
| CT-03 | Schemathesis trên `openapi-v2.yaml` (E-LOCAL) | 0 lỗi 5xx; 0 response sai schema |
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
| FI-02 | Strategy luôn chậm | Decorator kiểm thử | `NEED_APPROVAL`, `FALLBACK`, `STRATEGY_TIMEOUT`; thời gian pipeline ≤ 500ms |
| FI-03 | Strategy ném lỗi | Decorator kiểm thử | Fallback, `STRATEGY_EXCEPTION` |
| FI-04 | Lỗi liên tục | Decorator kiểm thử | Circuit mở, fast-fail; sau `BreakDuration` half-open → closed khi strategy hồi phục |
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

### 4.6. LT — Tải (k6, E-LOCAL)

| ID | Hồ sơ tải | Đo |
| :--- | :--- | :--- |
| LT-01 | TADSS, tốc độ đến cố định 50 RPS trong 10 phút, 3 lần lặp | p50/p95/p99, tỷ lệ lỗi (SC1) |
| LT-02 | TADSS, tăng dần 10 → 400 RPS | Điểm gãy: RPS lớn nhất khi p95 < 150ms và lỗi < 1% |
| LT-03 | TADSS 50 RPS khi FI-02 đang bật | Thời gian pipeline, p99 endpoint (SC2) |
| LT-04 | E2E qua SmashCourt-BE: 20 VU tạo booking trên các slot khác nhau trong 10 phút | Độ trễ API tạo booking, tỷ lệ thành công, tính đúng của mức cọc |
| LT-05 | TADSS soak 50 RPS trong 60 phút | Trôi độ trễ, bộ nhớ, kết nối DB |

Pha warm-up 1 phút bị loại khỏi thống kê. Kết quả kèm dashboard Grafana cùng khoảng thời gian.

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

### 4.11. UAT — Pilot người dùng thật

| ID | Nội dung | Tiêu chí |
| :--- | :--- | :--- |
| UAT-01 | Pilot 2 tuần trên E-STAGING | ≥ 15 người dùng, ≥ 50 booking đi qua luồng tích hợp |
| UAT-02 | Khảo sát System Usability Scale (SUS) cho khách và Manager | Báo cáo điểm trung bình, độ lệch chuẩn, cỡ mẫu |
| UAT-03 | Thời gian từ lúc tạo đơn chờ đến lúc Manager xử lý | Báo cáo trung vị, p90 |
| UAT-04 | Bộ truy vấn bất biến booking trên dữ liệu pilot | 0 vi phạm |

---

## 5. ĐỊNH NGHĨA KPI

| Mã | Chỉ số | Cách đo | Mục tiêu thiết kế |
| :--- | :--- | :--- | :--- |
| **SC1** | Độ trễ quyết định bình thường | p95 thời gian xử lý phía server của `POST /api/v2/decisions` trong LT-01 (E-LOCAL, PostgreSQL/Redis thật); báo cáo thêm p95 API tạo booking của LT-04 | p95 TADSS < 150ms; p95 tạo booking E2E < 1000ms |
| **SC2** | Độ trễ khi strategy lỗi | Trong FI-02/FI-03/FI-04 và LT-03 | 100% request có thời gian pipeline ≤ 500ms; p99 endpoint ≤ 700ms |
| **SC3** | Toàn vẹn & bất biến audit | Mỗi lượt chạy: số response 200 = số bản ghi `PRIMARY`; thử `UPDATE/DELETE` | 0 thiếu, 0 thừa; 0 lần sửa thành công |
| **SC4** | Idempotency & chống ghi đè đồng thời | IT-04, IT-07 và các lượt LT | 0 bản ghi quyết định trùng; 0 override kép |
| **SC5** | Feature flag | FI-13 | Hiệu lực < 2 giây; 100% booking trong lúc tắt áp cọc 100% |
| **SC6** | Không hỏng booking | Bộ truy vấn bất biến chạy sau FI-11…FI-14 và trên dữ liệu pilot | 0 vi phạm |
| **SC7** | Độc lập domain | PT-01 | ≥ 10.000 bản ghi; 0 lỗi contract; 0 thay đổi mã runtime/SDK |
| **SC8** | Chất lượng bộ kiểm thử | Toàn bộ danh mục; MT | 100% test case đạt; mutation score ≥ 70% |
| **SC9** | Tính khả dụng trong pilot | UAT-01, UAT-02 | ≥ 15 người dùng, ≥ 50 booking; SUS trung bình ≥ 68 |

**Bộ truy vấn bất biến booking (SC6, UAT-04)**, chạy trên DB SmashCourt sau mỗi kịch bản:

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
4. Chụp dashboard Grafana cùng khoảng thời gian (ảnh + JSON snapshot).
5. Sinh manifest SHA-256 cho mọi file; chạy notebook để sinh báo cáo HTML.

Cấu trúc thư mục, schema dữ liệu và quy tắc ẩn danh hóa: xem [`DATASET_SCHEMA_SPECIFICATION.md`](DATASET_SCHEMA_SPECIFICATION.md).

---

## 7. PHÂN TÍCH & TRÌNH BÀY

- Độ trễ báo cáo p50/p95/p99/max kèm số mẫu; với LT-01 và LT-03 báo cáo khoảng tin cậy 95% của p95 bằng bootstrap trên 3 lần lặp.
- Biểu đồ trong notebook: phân bố độ trễ theo kịch bản; đường thời gian độ trễ + trạng thái circuit khi tiêm lỗi; tỷ lệ trạng thái quyết định; phân bố mức cọc theo hạng/khung giờ (RP, PT); thời gian duyệt (UAT-03); điểm SUS.
- Mỗi con số trong báo cáo dẫn về `run_id` và file dữ liệu thô tương ứng.

---

## 8. GIỚI HẠN & MỐI ĐE DỌA TÍNH HỢP LỆ

| Giới hạn | Ảnh hưởng | Cách xử lý |
| :--- | :--- | :--- |
| 215 booking SmashCourt là dữ liệu môi trường phát triển; nhãn `NO_SHOW` do job tự gán khi không check-in, `CANCELLED` phần lớn do hết hạn thanh toán | Không phản ánh hành vi khách thật | Chỉ dùng làm input có hình dạng thực tế (RP); mọi số liệu theo nhãn chỉ mang tính mô tả |
| Replay là phân tích phản thực tế: mức cọc có thể làm thay đổi chính hành vi khách | Không suy ra được "đã ngăn bao nhiêu no-show" | Không đưa kết luận nhân quả; để Khóa luận xử lý bằng đánh giá off-policy/shadow mode |
| Dataset khách sạn khác domain | Chỉ là proxy | Dùng cho kiểm chứng độc lập domain và mô tả, không kết luận về sân cầu lông |
| E-LOCAL chạy trên một máy | Số tuyệt đối khác production | Báo cáo kèm thông số máy; so sánh tương đối giữa kịch bản trên cùng máy |
| Pilot cỡ mẫu nhỏ | Sai số lớn | Báo cáo cỡ mẫu, trung vị và phân vị; không khái quát hóa |

---

## 9. LƯU TRỮ: MICRO-BENCHMARK PHASE 4

Micro-benchmark Tuần 8 (19 kịch bản × 4 mức tải) chạy trên EF Core InMemory với strategy giả lập bằng `Task.Delay`, nên chỉ mô tả **overhead của pipeline trong phòng lab** và **không** được dùng làm minh chứng cho SC1–SC9. Mã và báo cáo đã gỡ khỏi repo ở Phase 5, tra lại qua tag git `phase4-baseline`; đo hiệu năng của Phase 5 dùng k6 (LT-01…LT-05) trên hạ tầng thật.
