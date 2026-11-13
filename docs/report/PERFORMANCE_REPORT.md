# ĐÁNH GIÁ HIỆU NĂNG HỆ THỐNG SMASHCOURT + TADSS (PHASE 6, EPIC 6C)

| | |
| :--- | :--- |
| **Dành cho** | Chương "Đánh giá hiệu năng" của báo cáo đồ án; người đọc là hội đồng và giảng viên hướng dẫn |
| **Trạng thái** | 12/11/2026: SP-01…SP-10 đều có kết quả hợp lệ; nút cổ chai (task 2.27) và tối ưu trước / sau (task 2.28) đã phân tích; P6-55 (Mục 10.2) đã chốt bằng buổi đo lại SP-01 ×3 ngày 13/11 |
| **Nguồn** | Thiết kế, SLO và nhật ký từng buổi đo: [`spec/PHASE6_PLAN_6C_DEVOPS_OBSERVABILITY_PERF.md`](../../spec/PHASE6_PLAN_6C_DEVOPS_OBSERVABILITY_PERF.md) (Phụ lục B.6–B.7, Mục 13 giai đoạn 2C). Cách chạy lại: [`docs/guides/RUNBOOK.md`](../guides/RUNBOOK.md). Danh mục mã kịch bản: [`EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](../research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md) Mục 4.16 |
| **Bằng chứng** | [`perf/evidence/PERF-6C-20261111/`](../../perf/evidence/PERF-6C-20261111/manifest.json) (mọi kịch bản tới 12/11) và [`perf/evidence/PERF-6C-20261113/`](../../perf/evidence/PERF-6C-20261113/manifest.json) (SP-01 sau P6-55, Mục 10.2): kết luận, cấu hình, tài nguyên của các lượt dùng trong chương này + `manifest.json` (SHA-256 của mọi file, kể cả mẫu thô không chép vào repo) |

Chương này trả lời ba câu hỏi: hệ thống có đạt mục tiêu hiệu năng đã đặt trước ở tải bình thường không; giới hạn và cách xuống cấp khi vượt tải; và việc đưa TADSS vào luồng đặt sân tốn bao nhiêu. Số trong chương lấy từ lượt đo thật; mọi ngưỡng được chốt **trước** khi có kết quả (02/11/2026) và không đổi sau đó.

---

## 1. Mục tiêu và phạm vi

- **Đo cả hệ thống** SmashCourt-BE + TADSS (+ SmashCourt-AI ở SP-10) qua các hành trình người dùng thật của ứng dụng đặt sân, không đo từng hàm riêng lẻ. Bổ sung cho các bài đo riêng của TADSS (LT-01…05 trong bộ đánh giá Phase 5) và không thay kiểm thử tính đúng (unit, integration, E2E).
- **Ngoài phạm vi:** cổng thanh toán thật (k6 tự ký IPN bằng khóa giả), email thật (vào Mailpit), Gemini thật (LLM giả có độ trễ cấu hình được), giao diện FE.

## 2. Tiêu chí chấp nhận (SLO)

Chốt 02/11/2026 (plan B.7.2), đo **phía client** (k6) trên pha ổn định `steady`, theo nhóm request:

| Nhóm | p95 | p99 | Gồm |
| :--- | ---: | ---: | :--- |
| Đọc | < 300 ms | < 800 ms | xem chi nhánh, sân, lịch trống, lịch sử, chi tiết đơn, danh sách / lịch của nhân viên, hàng chờ duyệt |
| Đặt sân | < 1000 ms | < 2000 ms | `POST /api/bookings/online`, gồm lời gọi TADSS |
| Đăng nhập | < 500 ms | < 1000 ms | bcrypt cố ý chậm |
| IPN | < 500 ms | < 1000 ms | kết quả thanh toán từ cổng |
| Ghi | < 500 ms | < 1000 ms | hủy đơn, check-in, check-out, duyệt cọc |
| Lỗi | < 0,5 % | | 5xx, timeout 5 s, 4xx ngoài danh sách nghiệp vụ hợp lệ |
| Tải danh nghĩa | ≥ 50 request/giây | | = 19 hành trình/giây (hệ số 2,72 request / hành trình đo ở SP-08) |

TADSS giữ SLO riêng: **SC1** p95 < 150 ms phía server; **SC2** p99 ≤ 700 ms khi strategy lỗi (đã chấm ở bộ đánh giá Phase 5). AI (SP-10A): p95 < 300 ms phần xử lý của AI.

**Quy tắc đọc kết quả.** p95 cần ≥ 100 mẫu, p99 cần ≥ 1000 mẫu; thiếu mẫu ghi **KHÔNG ĐỦ DỮ LIỆU**, không bao giờ coi là đạt. p99 thiếu mẫu ở từng lượt thì tính trên mẫu thô ghép của 3 lượt (không lấy trung bình các p99). Phân vị tính trên mẫu thô từng request (nội suy tuyến tính như k6). So sánh hai cấu hình chỉ gọi là **cải thiện / khác biệt** khi khoảng min–max của ≥ 3 lượt mỗi bên **không chồng nhau**; chồng nhau thì ghi "không phân biệt được".

## 3. Môi trường và công cụ

| Thành phần | Cấu hình |
| :--- | :--- |
| Máy | Laptop Intel Core i5-1135G7 (4 nhân × 2 luồng, xung cơ bản 2,4 GHz), 16 GB RAM, Windows 11, Docker Desktop (WSL2, VM 8 CPU, 8 GB); cắm sạc, chế độ nguồn "Best power efficiency" ở mọi ngày đo |
| Hệ thống đo | Image đúng bản deploy (theo sha): BE, TADSS, AI; PostgreSQL 17 (SmashCourt, schema production), PostgreSQL 16 + Redis 7 (TADSS), Mailpit, LLM giả, Toxiproxy (chỉ SP-09) |
| Trần tài nguyên | BE 2 CPU / 1 GB, TADSS 1,5 CPU, PostgreSQL SmashCourt 1 CPU / 1 GB, PostgreSQL TADSS 0,5 CPU, AI 1 CPU, k6 1,5 CPU / 2 GB |
| Tách CPU | `cpuset`: hệ thống dùng CPU 0–5 (3 nhân vật lý), k6 + bộ quan sát dùng CPU 6–7 (1 nhân), để công cụ đo không tranh CPU với hệ thống được đo |
| Công cụ | k6 2.3.0 (mẫu thô từng request); Prometheus 3.15 + Alloy (cAdvisor, OTLP), Tempo 2.10 (trace, lượt phân tích), Grafana "Kiểm thử"; `pg_stat_activity` lấy mẫu mỗi giây |
| Điều kiện lượt chính thức | Tắt bản deploy và runner CI trên cùng máy; khôi phục DB về dữ liệu mẫu + kiểm checksum trước mỗi lượt; kiểm sự kiện ngủ của Windows và xung nhịp CPU từng lượt (`cpu-clock.json`) |

**Dữ liệu.** Bộ sinh tất định (seed 42): 2.000 khách, chi nhánh, sân, bảng giá và đơn có sẵn theo phân bố rút từ 215 booking trên DB chính của SmashCourt (phát sinh khi phát triển và demo Đồ án 1; chỉ file thống kê vào repo, không dữ liệu cá nhân); cùng seed ⇒ cùng dữ liệu ⇒ các lượt so sánh được.

## 4. Mô hình người dùng

- **Hành trình:** J1 xem sân / lịch trống; J2 đăng nhập; J3 đặt sân online ⇒ J4 thanh toán (IPN do k6 ký) / hủy / bỏ dở; J5 xem lịch sử và chi tiết đơn; J6 nhân viên xem danh sách, check-in, check-out; J7 quản lý duyệt cọc.
- **Tỷ lệ:** J1 55 %, J5 15 %, J3+J4 15 %, J2 5 %, J6 7 %, J7 3 % (giả định có lý do: đọc nhiều hơn ghi; không có log truy cập thật). Tỷ lệ request khác tỷ lệ hành trình: ở tải danh nghĩa 96 % request là Đọc.
- **Think time** có seed: xem trang 2–6 s, điền form 8–20 s, giữa hai hành trình 5–15 s.
- **Open model** (`constant-arrival-rate`, `ramping-arrival-rate`): hành trình đến theo nhịp bất kể hệ thống chậm, tránh sai số "coordinated omission" của mô hình đóng; trần VU cấp sẵn để k6 không phải tạo VU giữa lượt.
- **Giờ thật:** giờ đặt sân, đơn để check-in / check-out chọn theo giờ VN lúc chạy (sân mở 06–22 giờ) ⇒ tỷ lệ request ghi phụ thuộc giờ chạy; mỗi bảng ghi khung giờ của lượt.

## 5. Ramp-up và quy trình mỗi lượt

Khôi phục DB + xóa Redis ⇒ kiểm checksum ⇒ nghỉ 120 s cho máy nguội ⇒ **warm-up 2 phút** tăng dần 25 % ⇒ 75 % mức danh nghĩa (JIT, pool kết nối, cache TADSS) ⇒ 10 s chuyển + **1 phút ổn định** ⇒ pha `steady` (15 phút với SP-01). Chỉ pha `steady` vào thống kê. Lượt bị dừng giữa chừng là `INCOMPLETE`, không bao giờ "đạt".

## 6. Kết quả theo kịch bản

### 6.1. Tổng hợp

| Mã | Kịch bản | Kết luận | Số chính |
| :--- | :--- | :--- | :--- |
| SP-08 | Năng lực ban đầu (03/11) | Mốc so sánh | Tải danh nghĩa 19 hành trình/s; nghẽn khóa PostgreSQL từ 20 hành trình/s |
| SP-01 | Tải danh nghĩa 15 phút ×3 | **Đạt** mọi p95 khi máy ở trạng thái bình thường; **không đạt Đăng nhập** khi máy vừa khởi động (xung nhịp thấp) | Mục 6.2 |
| SP-02 | Stress 100 ⇒ 250 % | Suy giảm từ **200 %**; tự hồi phục khi về 100 % | Đọc p95 600 ms ở 38 hành trình/s |
| SP-03 | Spike ×5 trong 1 phút | **Đạt**: hồi phục **30 s** (ngưỡng 60 s) | |
| SP-04 | Soak 90 phút | **Không đạt** luật bộ nhớ (+15,5 %); mọi SLO độ trễ đạt, heap .NET không tăng | Mục 6.4 |
| SP-05 | Điểm gãy | Gãy ở **48 hành trình/s (250 %)**, nút cổ chai CPU BE | Mục 6.5 |
| SP-06 | A/B TADSS | TADSS thêm **15–30 ms** vào Đặt sân p95 | Mục 6.6 |
| SP-07 | A/A telemetry | Không đo được chi phí, trừ Đăng nhập p95 +20–110 ms | Mục 6.7 |
| SP-09 | Lỗi TADSS dưới tải (FI-11, 12, 14) | **Đạt cả ba**, SC6 không vi phạm | Mục 6.8 |
| SP-10 | AI qua BE | A **Đạt** (p95 19 ms); B chỉ báo cáo | Mục 6.9 |
| SC1 | Thời gian ra quyết định của TADSS | **Đạt**: p95 khoảng 15 ms, p99 khoảng 24 ms phía server (ngưỡng 150 ms) | Lượt SP-01 tối 07/11 |

### 6.2. Tải danh nghĩa (SP-01)

Ba bộ SP-01 ×3 cùng cấu hình bản deploy (BE 2 CPU, auto-prepare tắt, P6-53 đã áp), khác nhau ở trạng thái máy và giờ chạy. Số là p50 / p95 / p99 (ms) phía client trên mẫu thô ghép 3 lượt, kèm số mẫu.

| Nhóm | Tối 07/11 (21:39–22:45) | Sáng 08/11 (10:14–11:20, máy vừa khởi động) | Ngưỡng p95 / p99 |
| :--- | :--- | :--- | :--- |
| Đọc | 7,0 / **16,2** / 96,4 (n = 135 973) | 8,0 / **27,6** / 437,5 (n = 135 890) | 300 / 800 |
| Đặt sân | 60,1 / **109,0** / 277,4 (n = 1 868) | 64,8 / **182,3** / 839,2 (n = 2 773) | 1000 / 2000 |
| Đăng nhập | 314,9 / **420,0** / 565,4 (n = 2 502) | 324,6 / **607,5** ✗ / 4 969,9 ✗ (n = 2 507) | 500 / 1000 |
| IPN | 26,7 / **55,6** / 182,2 (n = 774, p99 thiếu mẫu) | 29,0 / **98,6** / 431,0 (n = 1 084) | 500 / 1000 |
| Ghi | 31,3 / **55,0** / 199,1 (n = 210, p99 thiếu mẫu) | 12,4 / **51,2** / 289,7 (n = 2 263) | 500 / 1000 |
| Lỗi | 0,000 % | 0,15 % (Đăng nhập 1,04 % ✗, Đặt sân 0,505 % ✗) | 0,5 % |
| Throughput | 50,9 request/s | 52,1 request/s | ≥ 50 |
| Đợt nghẽn | 0 / 0 / 0 | 2 / 1 / 2 | |
| Xung nhịp trung vị (p10) | 3486–3500 MHz (3193–3251) | 3268–3435 MHz (2886–2952) | |

- **Tối 07/11: mọi p95 đạt**, 0 lỗi, 0 đợt nghẽn; kết luận gộp "không đủ dữ liệu" chỉ vì p99 của IPN và Ghi thiếu mẫu (chạy tối nên ít đơn mới, gần như không đơn đang chơi).
- **Sáng 08/11:** đủ mẫu cho mọi nhóm, Đọc / Đặt sân / IPN / Ghi đạt; **Đăng nhập không đạt** do một đợt nghẽn 5 s (r1) và xung nhịp thấp (r2, r3). Cùng cấu hình, cùng tỷ lệ request, buổi chiều cùng ngày (lượt SP-06 bên A, 13:35–15:52) Đăng nhập p95 lại **390–428 ms**, 0 đợt nghẽn ⇒ nguyên nhân là trạng thái máy (Mục 9), không phải tải ban ngày. Kết luận ghi cả hai, ngưỡng không đổi.
- **Sau P6-55 (bcrypt 11):** xem Mục 10.2.

### 6.3. Stress (SP-02) và Spike (SP-03)

- **SP-02** (07/11, 3140 MHz): 100 % ⇒ Đọc p95 22–28 ms; 150 % (29 hành trình/s) 36–93 ms; **200 % (38) vi phạm** Đọc p95 (600 ms ở cửa sổ thứ hai); 250 % (48) Đọc p95 1,9–2,9 s, lỗi 0,96 %, bỏ lượt 0,38 %. Về 100 %: Đọc p95 15–77 ms, 0 % lỗi ⇒ **tự hồi phục**. Tín hiệu suy giảm đầu tiên là độ trễ của nhóm Đọc, chưa phải lỗi.
- **SP-03** (07/11, 3408 MHz): tăng ×5 trong 1 phút rồi về danh nghĩa ⇒ hồi phục **30 s** (tiêu chí: 3 cửa sổ 10 s liên tiếp có Đọc p95 < 300 ms và lỗi < 0,5 %; ngưỡng 60 s) ⇒ **ĐẠT**.

### 6.4. Soak 90 phút (SP-04)

08/11 11:24–12:59, 3399 MHz. Mọi SLO độ trễ đạt (p95 / p99: Đọc 15,8 / 110 ms, Đặt sân 108 / 236, Đăng nhập 421 / 566, IPN 63 / 113), p95 Đọc trôi tối đa 1,15 lần (ngưỡng 1,2), số kết nối PostgreSQL không tăng (−1,8 %). **Không đạt luật bộ nhớ:** working set container BE tăng **15,5 %** (357 ⇒ 412 MB, ngưỡng 10 %). Heap .NET (`process_runtime_dotnet_gc_objects_size_bytes`) dao động 63–147 MB theo chu kỳ GC và **không có xu hướng tăng**; bộ nhớ GC đã cấp phẳng 150–206 MB ⇒ không có dấu hiệu rò bộ nhớ managed; phần tăng nằm ngoài heap (bộ nhớ native hoặc GC chưa trả lại hệ điều hành), nguyên nhân chưa xác định. Kết luận chắc cần một lượt soak dài hơn (≥ 3 giờ).

### 6.5. Điểm gãy (SP-05)

07/11, 3307 MHz, tăng mỗi bậc 25 % (giữ 3 phút): đạt mọi bậc tới **43 hành trình/s (225 %)**; **gãy ở 48 hành trình/s (250 %)**: Đọc p95 2751 ms, Đặt sân 5001 ms (timeout), lỗi 0,44 %. Tại bậc gãy BE dùng **1,65 / 2 lõi**, PostgreSQL 0,46 lõi ⇒ nút cổ chai là **CPU của BE**. So với SP-08 ngày 03/11 (nghẽn ở khoảng 20 hành trình/s do tranh chấp khóa PostgreSQL), năng lực tăng khoảng **2,4 lần** (Mục 10).

### 6.6. Tác động của TADSS (SP-06)

08/11 13:35–15:52, xen kẽ A B B A A B (A = bước quyết định cọc qua TADSS bật như thiết kế, B = BE không gọi TADSS), xung nhịp 3408–3447 MHz đều hai bên.

| Chỉ số (min–max 3 lượt) | A: có TADSS | B: không TADSS | Kết luận |
| :--- | ---: | ---: | :--- |
| Đặt sân p95 | 90,6–94,8 ms | 63,7–76,2 ms | **khác biệt**: +15–30 ms |
| Đặt sân p99 (gộp) | 217 ms | 180 ms | |
| Đăng nhập / Đọc / IPN p95 | 390–428 / 15–17 / 43–53 ms | 402–409 / 16 / 42–60 ms | không phân biệt được |

- Một lời gọi TADSS tốn trung bình **8,4–10,8 ms** phía BE (khoảng 12 % thời gian một lần đặt sân); TADSS ra quyết định phía server p95 khoảng **15 ms**, p99 khoảng 24 ms (SC1 ngưỡng 150 ms; đo ở SP-01 tối 07/11 r1, 641 quyết định, histogram TADSS biên 10 / 25 ms nên là số nội suy).
- Với dữ liệu thử, `DefaultDecisionStrategy` **từ chối khoảng 390–460 quyết định mỗi lượt** (BE hủy đơn, nhả sân, trả `BOOKING_REJECTED`): đây là hành vi nghiệp vụ đúng thiết kế, nhưng làm bên A ít đơn chờ thanh toán hơn; vì vậy A/B so "luồng có TADSS" với "luồng không TADSS" chứ không phải cùng một luồng chỉ khác một lời gọi.

### 6.7. Chi phí của telemetry (SP-07)

10–11/11 22:07–02:48, A = OpenTelemetry bật (như bản deploy), B = tắt. Lượt A1 bị **loại** (một trình duyệt dùng khoảng 26 % CPU máy, xung nhịp 2778 MHz) và chạy bù; lượt sạch A = 3, B = 4.

| Chỉ số (min–max) | A: bật | B: tắt | Kết luận |
| :--- | ---: | ---: | :--- |
| Đọc / Đặt sân / IPN / Ghi p95 | 15–17 / 92–100 / 44–65 / 40–48 ms | 15–16 / 87–101 / 39–80 / 36–124 ms | không phân biệt được |
| **Đăng nhập p95** | **418–478 ms** | **368–398 ms** | **khác biệt** |
| CPU BE / PostgreSQL, RAM BE | 0,59–0,61 / 0,18–0,19 lõi, 291–357 MB | 0,58–0,61 / 0,18–0,21 lõi, 271–343 MB | không phân biệt được |

Phép đo không làm méo kết quả ở các nhóm chính; riêng Đăng nhập (request nặng CPU nhất) chậm hơn 20–110 ms khi bật telemetry, vẫn dưới ngưỡng 500 ms. 3–4 lượt mỗi bên chỉ cho thấy có chênh lệch, không đủ ước lượng chính xác.

### 6.8. Chịu lỗi TADSS dưới tải (SP-09)

Tải danh nghĩa 15 phút; từ phút thứ khoảng 8 tạo lỗi **5 phút** giữa BE và TADSS (Toxiproxy), rồi gỡ; ngay sau lượt chạy bộ kiểm bất biến dữ liệu **SC6** của BE trên DB của lượt.

| Kiểu lỗi | Lỗi request trong / sau | Đặt sân p95 từ 60 s sau khi gỡ | Đơn trong lúc lỗi cọc 100 % (đường dự phòng) | Quyết định trùng | SC6 |
| :--- | ---: | ---: | ---: | ---: | :--- |
| FI-11 dừng TADSS | 0 % / 0 % | 96 ms | 197 / 197 | 0 | Đạt |
| FI-12 TADSS chậm thêm 1,5 s | 0 % / 0 % | 98 ms | 197 / 197 | 0 | Đạt |
| FI-14 cắt kết nối (TCP RST) | 0 % / 0 % | 139 ms | 196 / 196 | 0 | Đạt |

Khách vẫn đặt được sân trong mọi kiểu lỗi, không đơn nào vào hàng chờ duyệt, không dữ liệu sai. **Đánh đổi đã chấp nhận (FI-12):** khi TADSS chậm, BE chờ tối đa 800 ms rồi **thử lại một lần** trước khi cọc 100 % ⇒ Đặt sân p95 của cả lượt khoảng **1,66 s** (vượt ngưỡng 1000 ms trong 5 phút lỗi). Đây là mục tiêu thiết kế ban đầu (chờ có hạn, thử lại, hỏng thì cọc 100 %), người dùng chốt giữ nguyên ngày 11/11. Bảng trên là số **trước P6-57** (11/11, BE `sha-8ee8e63`).

**Sau P6-57: mạch ngắt lệnh đánh giá cọc ở BE (13/11, buổi 10, 15:02–16:56).** Giữ timeout 800 ms và một lần thử lại, BE thêm mạch ngắt: một lần gọi lỗi mà `/health/ready` của TADSS cũng lỗi (hoặc 3 lần gọi lỗi liên tiếp) thì ngừng gọi TADSS, đơn cọc 100 % ngay; một đơn đi thử khi TADSS hồi phục hoặc theo lịch 60 → 120 → 240 → 300 s ([plan 6C](../../spec/PHASE6_PLAN_6C_DEVOPS_OBSERVABILITY_PERF.md) P6-57). Đo trên **cùng một image** BE `sha-c01a71f`, chỉ đổi `TadssConfig:CircuitEnabled` (`perf.sh up --circuit-off`), thứ tự xen kẽ tắt, bật, bật, tắt; xung nhịp và tải như trên. Độ trễ tính bằng `faults.py latency` trên request `POST /api/bookings/online` **trong đúng 5 phút lỗi**; số đơn và cọc 100 % từ bộ kiểm DB (cửa sổ bỏ 2 s ở mỗi đầu, nên ít hơn số request; request bị từ chối vì tranh khung giờ có độ trễ nhưng không tạo đơn).

| Kiểu lỗi | Mạch ngắt | Request trong lỗi | Chờ > 800 ms | Đặt sân p50 / p95 trong lỗi | Đơn cọc 100 % | Mạch mở / đóng lại | SC6 |
| :--- | :--- | ---: | ---: | :--- | ---: | :--- | :--- |
| FI-11 dừng TADSS | tắt | 189 | 163 | 1.653 / 1.682 ms | 159 / 159 | — | Đạt |
| | **bật** | 175 | **3** | **50 / 75 ms** | 157 / 157 | khoảng 10 s sau lỗi / khoảng 9 s sau khi gỡ | Đạt |
| FI-12 TADSS chậm thêm 1,5 s | tắt | 180 | 160 | 1.653 / 1.682 ms | 160 / 160 | — | Đạt |
| | **bật** | 192 | **4** | **50 / 74 ms** | 159 / 159 | khoảng 5 s / khoảng 5 s | Đạt |
| FI-14 cắt kết nối (TCP RST) | bật | 186 | 0 | 51 / 75 ms | 161 / 161 | khoảng 5 s / khoảng 10 s | Đạt |

- **Khi TADSS treo hoặc chậm (FI-11, FI-12), số đơn phải chờ quá 800 ms giảm từ 86–89 % số request (160–163 request) xuống 3–4 request trong 5 phút**; p95 đặt sân trong lúc lỗi giảm từ khoảng 1,68 s xuống khoảng 75 ms, ngang lúc không lỗi. Các đơn còn chậm đúng là các đơn thiết kế đã tính trước: một hai đơn ở giây đầu (trước khi mạch kịp mở) và **đơn thử ở giây +64 và +188**, khớp lịch 60 s rồi 120 s (đơn thử gọi một lần, chờ tối đa 800 ms).
- **Mạch tự đóng trong khoảng 5–10 s sau khi gỡ lỗi** (lần kiểm nền 10 s thấy TADSS tốt lại, cho một đơn thử thành công); không ai phải thao tác. Thời điểm mở / đóng đọc từ metric `smashcourt_tadss_circuit_state` của perf (nhịp xuất 5 s nên sai số khoảng 5 s).
- **FI-14:** khi kết nối bị cắt thẳng, BE biết lỗi ngay nên trước P6-57 cũng đã nhanh (p95 85 ms); bật mạch không làm chậm hơn và BE ngừng gửi request vô ích tới TADSS.
- Trong mọi lượt khách vẫn đặt được sân, mọi đơn trong lúc lỗi cọc 100 %, không đơn nào chờ duyệt, không quyết định trùng, SC6 đạt; lỗi request ≤ 0,09 % (ngưỡng 0,5 %).
- **Phạm vi kết luận:** một lượt mỗi bên cho mỗi kiểu lỗi, nên đây là **độ lớn hiệu ứng** (khoảng 22 lần ở p95 trong lúc lỗi), không phải phép so B.7.8 (cần ≥ 3 lượt mỗi bên). Đánh đổi của mạch ngắt: trong lúc mạch mở mọi đơn cọc 100 % (bỏ qua `NEED_APPROVAL` / `REJECTED`); TADSS chậm dưới 800 ms thì mạch không bắt được.

### 6.9. AI qua BE (SP-10)

LLM giả thay Gemini (người dùng: AI là use case phụ, chỉ dùng model miễn phí); 2 request/s × 10 phút, chia đều chat, gợi ý đặt sân, phân tích.

- **SP-10A** (LLM giả 0 ms, đo phần xử lý của AI): AI server p95 **19 ms**, p99 24 ms (n = 1 198), 0 lỗi ⇒ **ĐẠT** (ngưỡng 300 ms); toàn chặng qua BE p95: chat 16, gợi ý 25, phân tích 22 ms.
- **SP-10B** (LLM giả 800 ± 200 ms, chỉ báo cáo): AI server p95 975 ms; chat và phân tích toàn chặng p95 khoảng 996 ms (BE thêm khoảng 15 ms). Gợi ý đặt sân p95 19 ms vì tài khoản thử chưa có lịch sử nên AI trả gợi ý mặc định, không gọi LLM.

## 7. Client và server: hai góc nhìn trên p95 / p99

Lượt SP-01 tối 07/11 r1:

| Endpoint | p95 client (k6, mẫu thô) | p95 server (histogram BE) |
| :--- | ---: | ---: |
| `POST /api/auth/login` | 401,9 ms (n = 835) | 489,8 ms |
| `POST /api/bookings/online` | 107,5 ms (n = 623) | 99,6 ms |
| `GET /api/me/bookings/{id}` | 19,6 ms (n = 2 409) | 23,0 ms |
| `GET …/time-grid` | 19,1 ms (n = 12 140) | 23,1 ms |
| `GET /api/courts` | 8,4 ms (n = 12 088) | 9,7 ms |
| `GET /api/payments/vnpay/ipn` | 47,9 ms (n = 258) | 49,5 ms |

Hai cột là hai tập mẫu khác nhau: client gồm mạng nội bộ Docker, xếp hàng ở Kestrel và thời gian của k6; server là thời gian bên trong BE nhưng **xấp xỉ theo bucket** (biên 10, 25, 50, 100, 250, 500, 1000, 2500 ms…, nội suy tuyến tính trong bucket). Vì vậy p95 server có thể **cao hơn** client (Đăng nhập: 490 so với 402 ms rơi vào bucket rộng 250–500 ms). Đây là lý do SLO chấm trên mẫu thô phía client; histogram chỉ dùng cho tài nguyên, phân rã và dashboard. Cách đọc theo trải nghiệm: p95 là 1/20 lượt chậm nhất người dùng gặp thường xuyên; p99 quyết định cảm giác "thỉnh thoảng treo" (đợt nghẽn 5 s của sáng 08/11 hiện rõ ở p99 Đăng nhập 4,97 s trong khi p50 vẫn 325 ms).

## 8. Giám sát bằng Grafana

- **Dashboard "Kiểm thử"** (Grafana local, nguồn `perf/testboard/`): mỗi nhóm test một ô theo màu (đạt, không đạt, không đủ dữ liệu), bấm ô ⇒ bảng mã hỏng ⇒ link báo cáo gốc; hàng hiệu năng: request/s, VU, p95 / p99 phía k6 cạnh phía server, lỗi, CPU / RAM từng container perf. Lượt không hợp lệ hoặc thiếu mẫu hiện là hạn chế, không phải đạt.
- **Dashboard "Vận hành"** (Grafana Cloud, bản deploy): ba tab BE / TADSS / AI, phiên bản đang chạy theo nhãn image, cảnh báo qua email (không sẵn sàng, 5xx, đường lùi TADSS, quota Gemini).
- **Tempo** (lượt phân tích `up --trace`, trace 100 %): cây span BE ⇒ PostgreSQL ⇒ TADSS cho từng request, dùng ở Mục 9.

## 9. Nút cổ chai (task 2.27)

Hai cách nhìn, không trộn: (a) phân rã **trung bình** cộng được từ `_sum` / `_count` của histogram (mọi request BE = DB + TADSS + phần còn lại); (b) **trace** từng request (100 mẫu + 30 mẫu đuôi trên p95 mỗi endpoint, thời gian DB / TADSS là hợp các span con), không lấy percentile trừ nhau. Công cụ: `perf/stack/bottleneck.py`.

| Endpoint (trace, ms) | p50 / p95 | Trung bình: DB + TADSS + BE | Câu SQL |
| :--- | ---: | :--- | ---: |
| `POST api/auth/login` | 313 / 374 | 14 + 0 + **309** | 6 |
| `POST api/bookings/online` | 59 / 97 | **37** + 7,5 + 20 | **25** |
| Đọc (lịch trống, lịch sử, chi tiết, sân…) | 3–11 / 6–18 | DB chiếm 60–75 % | 2–5 |

Trung bình mọi request BE ở tải danh nghĩa 13,7–23,5 ms (DB 5,2–12,5 ms, khoảng 3,3 câu SQL / request).

| # | Nguyên nhân | Bằng chứng | Mức kết luận |
| :---: | :--- | :--- | :--- |
| 1 | **bcrypt hệ số 12 khi đăng nhập** là phần nặng nhất | 96 % thời gian Đăng nhập là kiểm mật khẩu; 0,90 lần đăng nhập/s × 309 ms ≈ **45 % CPU của BE**. Khớp: điểm gãy do CPU BE (SP-05), Đăng nhập nhạy nhất với xung nhịp (SP-01), Đăng nhập chậm hơn khi bật telemetry (SP-07) | Chắc chắn về thời gian, có khả năng cao về CPU ⇒ xử lý bằng P6-55 |
| 2 | **Đợt nghẽn hiếm**: câu đọc chi tiết booking nối 12 bảng dồn lên 1 lõi PostgreSQL | Đợt 5 s sáng 08/11 (xung nhịp 2836 MHz): **54 phiên** PostgreSQL chạy cùng lúc, phần lớn tranh CPU, câu chiếm nhiều nhất là chi tiết booking; pool BE dùng **97 / 100 kết nối, 82 request chờ** (mọi lượt khác ≤ 16, không chờ) | Có khả năng cao ⇒ hướng cải tiến (Mục 11) |
| 3 | Đặt sân online chạy 25 câu SQL | DB 57 % thời gian, có câu đọc lặp | Chắc chắn, chưa phải nút cổ chai (p95 khoảng 100 ms so với 1000) |

**Giả thuyết đã loại bằng số liệu:** pool kết nối ở tải danh nghĩa (dùng ≤ 16 / 100, 0 chờ); rate limiter (0 request bị từ chối); job nền Hangfire chạy chồng (job dài nhất 6–37 s luôn ở pha khởi động, ngoài pha đo); tranh chấp khung giờ (khoảng 100 lần `SLOT_UNAVAILABLE` mỗi lượt là kết quả nghiệp vụ, không làm chậm request); chờ khóa PostgreSQL (`LockManager` gần như hết sau P6-53).

## 10. Tối ưu và so sánh trước / sau (task 2.28)

### 10.1. Các thay đổi đã đo

| Thay đổi | Đo | Kết quả |
| :--- | :--- | :--- |
| Tắt collector `pg_locks` của bộ quan sát | E1 / E2 (04/11) | Không phải nguyên nhân (11 = 11 đợt nghẽn) |
| Pool kết nối 100 ⇒ 20 (thử, không áp) | E3 | Không phân biệt được |
| Auto-prepare của Npgsql 50 (P6-54, chỉ môi trường đánh giá) | E4; xen kẽ với đối chứng 07/11 | **Giảm CPU PostgreSQL** (0,12–0,16 so với 0,20–0,21 lõi); độ trễ không phân biệt được |
| Bỏ index trùng của `bookings` (P6-53, áp mọi môi trường) | SP-01 06/11, trace 11/11 | Hết chờ khóa `LockManager` ở tải danh nghĩa |
| BE 2 ⇒ 3 ⇒ 4 CPU (thử, không áp) | 06/11 | **Không cải thiện** (Đăng nhập 450 / 578 / 645 ms, BE chỉ dùng 0,6–0,8 lõi, xung nhịp máy giảm) ⇒ giữ 2 CPU |
| **Tổng** | SP-08 03/11 ⇒ SP-05 07/11 | Điểm gãy **20 ⇒ 48 hành trình/s (khoảng 2,4 lần)**, nút cổ chai chuyển từ khóa PostgreSQL sang CPU BE; không quy được cho một yếu tố (nhiều thay đổi cùng lúc) |

### 10.2. P6-55: bcrypt 12 ⇒ 11

**Thay đổi.** Mọi hash mật khẩu mới dùng hệ số 11 (`PasswordHelper.BcryptWorkFactor`); hash cũ hệ số 12 vẫn kiểm được vì hệ số nằm trong hash. Dữ liệu mẫu perf dựng lại bằng hash hệ số 11 (đúng tình huống sau khi người dùng đã đổi mật khẩu). Một lần kiểm bằng hệ số 11 tốn đúng một nửa công so với hệ số 12.

**Đo chính thức (13/11, buổi 9, 10:08–11:13, cấu hình bản deploy, BE `sha-2ac0b70`; khác `sha-d4592c1` của các lượt 08/11 chỉ ở P6-55 và cờ đổi nóng P6-56, phần sau không chạy trong perf).** Máy khởi động 09:15 nên chạy một lượt làm nóng bỏ đi (`p655c-warm`, 3439 MHz, 1 đợt nghẽn), rồi 3 lượt: xung nhịp trung vị 3440–3459 MHz, **0 đợt nghẽn**, hợp lệ cả ba; bản gộp `pooled-20261113-100848-load-SP-01-p655c-r1-3runs.json` **ĐẠT mọi nhóm p95 và p99** (đủ mẫu), lỗi 0,004 %.

Có hai mốc "trước" (hệ số 12), cùng dải xung nhịp, mỗi bên 3 lượt; phép so theo quy tắc B.7.8 (khác nhau chỉ khi khoảng min–max không chồng nhau):

- **Tối 07/11** (SP-01, 21:39–22:46): khác tỷ lệ hành trình. Tỷ lệ theo giờ thật nên buổi tối ít đặt sân và ghi hơn (ghi 68–71 request mỗi lượt so với 748–774 hôm nay), so được nhóm Đăng nhập và Đọc.
- **Chiều 08/11** (SP-06 bên A, cùng cấu hình, 13:35–15:52): **cùng tỷ lệ request** với buổi 9 (Đăng nhập 836–841, Đặt sân 765–896, Ghi 726–768 mỗi lượt), dùng làm mốc chính.

| Nhóm (min–max 3 lượt) | Trước: chiều 08/11 | Sau: 13/11 | Kết luận B.7.8 |
| :--- | :--- | :--- | :--- |
| Đăng nhập p50 / p95 / p99 | 308–316 / 390–428 / 486–557 ms | 160–162 / 188–193 / 214–225 ms | **Giảm** cả ba (khoảng một nửa) |
| Đọc p95 / p99 | 15,3–17,2 / 61–116 ms | 14,5–14,7 / 22–23 ms | **Giảm** |
| Đặt sân p95 / p99 | 90,6–94,8 / 164–238 ms | 82,6–91,0 / 112–132 ms | p95 chồng nhau; p99 **giảm** |
| Ghi p95 / p99 | 37,1–45,6 / 130–193 ms | 34,7–35,9 / 47–61 ms | **Giảm** |
| IPN p95 / p99 | 43,2–52,9 / 85–209 ms | 43,7–49,0 / 80–91 ms | Chồng nhau (không phân biệt được) |
| CPU BE trung bình | 0,60–0,61 lõi | 0,47–0,48 lõi | **Giảm khoảng 20 %** |

So với tối 07/11 cũng ra cùng kết luận: Đăng nhập p50 / p95 / p99 313–318 / 402–433 / 532–574 ⇒ 160–162 / 188–193 / 214–225 ms; Đọc p99 82–104 ⇒ 22–23 ms; CPU BE 0,58–0,61 ⇒ 0,47–0,48 lõi.

**Kết luận.**

- **Đăng nhập nhanh khoảng 2 lần** ở mọi phân vị, đúng lý thuyết (hệ số 11 tốn một nửa công băm so với 12).
- **CPU của BE giảm khoảng 20 %**, khớp ước lượng của 2.27 (bcrypt khoảng 45 % CPU BE, hạ một nửa thì tiết kiệm khoảng 22 %).
- **Đuôi phân bố của các nhóm khác cũng giảm** (Đọc, Ghi, Đặt sân p99), dù các nhóm này không băm mật khẩu: bcrypt chiếm luồng xử lý của BE nên request khác phải xếp hàng; bớt bcrypt thì bớt xếp hàng. p50 của Đặt sân, Ghi, IPN gần như không đổi, đúng với giải thích này.
- Mức Đăng nhập p95 hiện còn khoảng 38 % ngưỡng 500 ms, nên khoảng dư không còn mỏng như trước (390–850 ms giữa các buổi cùng cấu hình, Mục 6.2).

**Lần đo trước đó (11–12/11, buổi 8 và 8b) không dùng để kết luận.** Buổi 8 bị **loại** vì chính máy đo bị dùng cho build và test trong lúc đo (r2 nghẽn 100 s trùng lúc build; r3 bị preflight chặn vì RAM VM 96 %). Buổi 8b: `p655b-r1` (2857 MHz, 1 đợt nghẽn, Đăng nhập p50 173 ms) và `p655b-r2` (2775 MHz, 4 đợt nghẽn, Đăng nhập p95 698 ms) chạy ở xung nhịp thấp; `p655b-r3` bị cắt do máy tắt lúc 00:38:54. Hai lượt này cho cùng p50 Đăng nhập với buổi 9 nhưng đuôi xấu hơn nhiều; đối chiếu mọi lượt từ 06/11 thì lượt có xung nhịp trung vị ≥ khoảng 3400 MHz hầu như 0 đợt nghẽn, còn lượt ≤ khoảng 3100 MHz luôn có 1–6 đợt (Mục 12, hạn chế).

## 11. Đánh đổi đã chốt và hướng cải tiến

| Vấn đề | Quyết định | Lý do |
| :--- | :--- | :--- |
| Đặt sân chậm tới khoảng 1,66 s khi TADSS chậm | **Giữ** `TimeoutMs` 800 ms + một lần thử lại; thêm **P6-57** mạch ngắt ở BE | Mục tiêu thiết kế: ưu tiên có quyết định thật, hỏng mới cọc 100 %. Từ P6-57 chỉ vài đơn đầu và đơn thử còn chờ; p95 trong lúc lỗi khoảng 75 ms (Mục 6.8) |
| bcrypt chiếm gần nửa CPU BE | **P6-55**: hệ số 11 cho mọi hash mới | Nhanh khoảng 2 lần, vẫn trên mức tối thiểu 10 khuyến nghị; hash cũ hệ số 12 vẫn kiểm được (hệ số nằm trong hash) và chỉ thành 11 khi người dùng đổi / đặt lại mật khẩu |
| Câu đọc chi tiết booking 12 bảng | **Hướng cải tiến**, không sửa trong Phase 6 | Dùng ở 19 chỗ (đọc chi tiết cho FE, nạp trước khi ghi để ra quyết định tiền, đọc lại sau khi ghi); chỉ nhóm cuối làm nhẹ được và chiếm khoảng 1/4 số lần chạy. Hướng: bật auto-prepare cho bản deploy khi thấy đúng triệu chứng; về lâu dài mỗi luồng một câu đọc vừa đủ, có test riêng |
| Working set BE tăng trong soak | Theo dõi | Heap .NET phẳng; cần soak ≥ 3 giờ để kết luận |
| Đặt sân online 25 câu SQL | Hướng cải tiến | Còn xa ngưỡng; gộp câu đọc lặp khi cần |

## 12. Kết luận và hạn chế

**Kết luận.**

1. Ở tải danh nghĩa (khoảng 51 request/s, 19 hành trình/s), khi máy ở trạng thái bình thường, hệ thống **đạt mọi SLO p95** với 0 % lỗi; p99 đạt ở mọi nhóm đủ mẫu.
2. Hệ thống chịu được **2 lần** tải danh nghĩa trước khi Đọc vượt ngưỡng, gãy ở **2,5 lần**, hồi phục sau spike ×5 trong **30 s** và tự hồi phục sau stress.
3. Đưa TADSS vào luồng đặt sân tốn **15–30 ms** p95; TADSS ra quyết định p95 khoảng 15 ms; khi TADSS dừng, chậm hay mất kết nối, khách **vẫn đặt được** (cọc 100 %), 0 lỗi, dữ liệu đúng (SC6).
4. Nút cổ chai hiện tại là **CPU của BE**, gần một nửa dành cho bcrypt khi đăng nhập; đã xử lý bằng P6-55: Đăng nhập nhanh khoảng 2 lần (p95 khoảng 190 ms), CPU BE giảm khoảng 20 %, đuôi Đọc / Ghi / Đặt sân cũng giảm.
5. Telemetry không làm méo kết quả ở các nhóm chính.

**Hạn chế.**

- Một laptop chạy cả hệ thống lẫn công cụ đo; kết quả là **cận dưới** và phụ thuộc xung nhịp CPU (Đăng nhập p95 dao động 390–850 ms giữa các buổi cùng cấu hình; lượt có xung nhịp trung vị ≤ khoảng 3100 MHz luôn có đợt nghẽn, lượt ≥ khoảng 3400 MHz hầu như không); chế độ nguồn "Best power efficiency" ở mọi ngày đo; sau khi khởi động máy cần khoảng 1,5 giờ mới ổn định. Buổi đo không ghi tiến trình khác trên máy nên không quy được xung nhịp thấp cho một nguyên nhân.
- PostgreSQL chạy cục bộ trong Docker, không có độ trễ mạng tới Neon như môi trường dev.
- Cache quyết định: từ P6-58 (13/11, sau các buổi đo trong chương này) bản deploy và perf tắt cache của hai strategy quy tắc (khóa chứa mã đơn nên không bao giờ trúng); mọi lượt trong chương đo khi cache còn bật, nên số là thận trọng (mỗi quyết định thêm hai lệnh Redis). Stack đánh giá của TADSS vẫn bật cache để kiểm cơ chế.
- Rate limit của TADSS đếm theo từng instance; chạy N instance thì hạn mức tối đa gấp N lần. Bản deploy và perf chạy một instance.
- Tỷ lệ hành trình là giả định (không có log truy cập thật); tỷ lệ request ghi phụ thuộc giờ chạy (buổi tối khoảng 70 request ghi mỗi lượt, buổi ngày khoảng 750), nên phép so trước / sau chỉ dùng các lượt cùng khung giờ hoặc chỉ so nhóm không phụ thuộc tỷ lệ.
- Cổng thanh toán, email, Gemini là bản giả; SP-10B không đo được độ trễ LLM của gợi ý đặt sân.
- Bộ nhớ working set trong soak và nguyên nhân Đăng nhập chậm hơn khi bật telemetry chưa xác định.
- Lượt bị nhiễu được loại và ghi rõ (SP07-A1, hai lượt đầu ngày đo 3 do mạng tới GHCR, buổi 8 do máy đo bị dùng cho build / test trong lúc đo, `p655b-r3` do máy bị tắt giữa lượt), không dùng để kết luận.

---

## Phụ lục A. Lượt dùng làm bằng chứng

Danh sách đầy đủ, SHA-256 và kích thước của mọi file ở [`perf/evidence/PERF-6C-20261111/manifest.json`](../../perf/evidence/PERF-6C-20261111/manifest.json). Kiểm lại bản chép: `python perf/stack/perf_evidence.py verify perf/evidence/PERF-6C-20261111`. Mẫu thô (`raw.csv.gz`, khoảng 5 MB mỗi lượt) giữ ở `perf/results/k6/` của máy đo (gitignore); hash trong manifest để đối chiếu.

| Nhóm | Lượt |
| :--- | :--- |
| SP-08 | `20261103-213705-breakpoint-SP-08` |
| SP-01 tối 07/11 | `20261107-213903-load-SP-01-r1`, `-220151-…-r2`, `-222426-…-r3` + bản gộp |
| SP-01 sáng 08/11 | `20261108-101424-load-SP-01-day-r1`, `-103709-…-r2`, `-105956-…-r3` + bản gộp |
| SP-01 sau P6-55 | `…-load-SP-01-p655c-r1…r3` + bản gộp `pooled-20261113-100848-load-SP-01-p655c-r1-3runs.json` (gói [`PERF-6C-20261113`](../../perf/evidence/PERF-6C-20261113/manifest.json)); lượt làm nóng `p655c-warm` và hai lượt buổi 8b `p655b-r1`, `-r2` (gói `PERF-6C-20261111`) chỉ để tham khảo |
| SP-02, SP-03, SP-05 | lượt `stress`, `spike`, `breakpoint-SP-05` ngày 07/11 |
| SP-04 | `20261108-112353-soak-SP-04` |
| SP-06 | `SP06-A1`, `B2`, `B3`, `A4`, `A5`, `B6` (08/11) + hai bản gộp |
| SP-07 | `SP07-A1` (loại), `B2`, `B3`, `A4`, `A5`, `B6`, `B8`, `A7` + bản gộp |
| SP-09 | `SP09-stop-tadss`, `SP09-latency`, `SP09-reset` (có `faults.json`, `sc6.json`); sau P6-57: `SP09-p657-stop-tadss-off`, `-on`, `-latency-on`, `-off`, `-reset-on` (gói [`PERF-6C-20261113-SP09`](../../perf/evidence/PERF-6C-20261113-SP09/manifest.json), có `fault-latency.json`) |
| SP-10 | `ai-SP-10-A`, `ai-B` |
| Phân tích 2.27 | `20261111-014033-load-trace-2.27` (có `bottleneck.json`) |
| Đối chứng auto-prepare 07/11 | `CTRL-r1…r5`, `AP50-r1…r3` |

## Phụ lục B. Dựng lại kết quả

`bash perf/perf.sh up` ⇒ `bash perf/perf.sh run load --rate 19 --duration 15m --code SP-01 --tag r1` (mỗi lượt tự khôi phục dữ liệu, warm-up, đo, chấm `verdict.json`) ⇒ `bash perf/perf.sh pool <lượt>…` ⇒ `python perf/stack/bottleneck.py <lượt> [--traces]`. Chi tiết, điều kiện máy và cách dừng an toàn: [`docs/guides/RUNBOOK.md`](../guides/RUNBOOK.md).
