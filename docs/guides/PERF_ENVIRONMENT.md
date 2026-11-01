# Môi trường đo hiệu năng `perf` (Epic 6C, gói 6C-4)

Cập nhật 01/11/2026. Chạy BE, TADSS, AI **đúng image `sha-…` của bản deploy** cùng DB, Redis, email và LLM riêng, chỉ trên máy; k6 đóng vai người dùng theo 7 hành trình; số liệu xem trên dashboard "Kiểm thử" ([TEST_DASHBOARD.md](TEST_DASHBOARD.md)). Kế hoạch: [plan 6C](../../spec/PHASE6_PLAN_6C_DEVOPS_OBSERVABILITY_PERF.md) task 2.10–2.17, 2.30, thiết kế Phụ lục B.6.

## 1. Bốn lệnh

Chạy trong `TADSS-Runtime/` (Git Bash trên Windows được), cần Docker Desktop và Python (đã cài cho `perf/board.py`).

```bash
bash perf/perf.sh up          # bật bộ quan sát + môi trường perf, chờ healthy (tag = bản đang deploy)
bash perf/perf.sh seed        # MỘT LẦN sau up: dữ liệu benchmark (2.000 khách, seed 42) + database mẫu
bash perf/perf.sh run smoke   # một lượt k6: tự khôi phục dữ liệu ⇒ chạy ⇒ lưu perf/results/k6/<lượt>/
bash perf/perf.sh down        # tắt perf, xóa dữ liệu của nó (bộ quan sát và kết quả giữ nguyên)
```

| Lệnh | Tùy chọn |
| :--- | :--- |
| `up` | `--be TAG --tadss TAG --ai TAG` (đo bản khác bản deploy), `--tadss-off` (BE không gọi TADSS, cặp đối chứng SP-06), `--trace` (trace 100 %, chỉ cho lượt phân tích) |
| `seed` | `--seed N`, `--customers N`, `--distribution FILE` (mặc định `distribution.reference.json` rút từ 215 booking thật, xem Mục 3) |
| `reset` | khôi phục hai database từ mẫu + xóa Redis + kiểm checksum (`run` tự gọi; khoảng 20 giây) |
| `run <profile>` | `--rate N` (hành trình/giây), `--max-rate N` (breakpoint), `--duration 15m`, `--warmup 2m`, `--vus N`, `--max-vus N`, `--code SP-xx` (nạp lên dashboard), `--limited "lý do"` (lượt không dùng để kết luận, P6-47), `--no-reset` |
| `status` | tag, commit, trạng thái từng service |

Profile k6 (`perf/k6/mix.js`): `smoke` (kiểm đường đi, đủ 7 hành trình), `load`, `stress`, `spike`, `soak`, `breakpoint` (open model, có warm-up gắn `phase=warmup` bị loại khỏi thống kê), `closed` (người dùng cố định có think time). Thông số chính thức của từng kịch bản SP chốt ở gói 6C-5.

## 2. Thành phần

| Container | Vai trò | Cổng trên máy |
| :--- | :--- | :--- |
| `perf-be`, `perf-tadss`, `perf-ai` | Image GHCR theo tag; cấu hình toàn hằng không bí mật (`perf/stack/docker-compose.yml`), không đọc `smashcourt-deploy.env` | BE `127.0.0.1:18080` |
| `perf-postgres` (17), `perf-tadss-postgres` (16), `perf-redis` | DB dùng một lần; database mẫu `smashcourt_seed`, `tadss_seed` để khôi phục | — |
| `perf-mailpit` | Nhận email của BE | `127.0.0.1:18025` |
| `perf-fake-llm` | Trả lời như Gemini `generateContent`, trễ 800 ± 200 ms (`FAKE_LLM_DELAY_MS`, `FAKE_LLM_JITTER_MS`) | — |
| `perf-k6` | Chỉ chạy khi `run`; metric client đẩy OTLP về Alloy của bộ quan sát | — |

Cấu hình đo: rate limit nâng (k6 đăng nhập từ một IP), VNPay khóa giả (k6 tự ký IPN đúng thuật toán BE kiểm), Hangfire bật, telemetry 5 giây/lần, trace tắt (P6-40), log Warning. `perf/stack/tests/test_compose.py` chặn host/chuỗi kết nối ra ngoài stack, biến mang bí mật, service thiếu trần CPU/RAM.

## 3. Dữ liệu

- `perf/stack/datagen.py`: sinh tất định từ seed + ngày mốc (hôm nay giờ VN) + file phân bố: 3 chi nhánh × 8 sân, giá theo giờ, chủ hệ thống, quản lý và nhân viên mỗi chi nhánh, khách kèm lịch sử theo hồ sơ (hoàn tất / không đến / hết hạn thanh toán: đúng thứ BE đếm gửi TADSS), đơn có sẵn 14 ngày tới chiếm 30 % khung giờ. Mọi tài khoản `perf-*@smashcourt.perf`, mật khẩu thử `Perf@12345`.
- `distribution.reference.json` (mặc định, commit được): phân bố **tổng hợp** rút từ 215 booking SmashCourt bằng `python perf/stack/reference_stats.py` (đọc `data/private/smashcourt_bookings.csv` của gói 5.6, không in hay chép định danh): giờ bắt đầu, thời lượng, số sân/đơn, thời gian đặt trước, hồ sơ lịch sử khách lúc đặt. Phần cấu trúc (chi nhánh, sân, giờ mở cửa, lấp lịch tương lai) giữ của bản placeholder. Giới hạn: 215 booking là dữ liệu môi trường phát triển (nhiều đơn hủy, không đến, hết hạn thanh toán do thử nghiệm) ⇒ ghi ở phần hạn chế của báo cáo.
- `distribution.placeholder.json`: phân bố giả lập (nhãn `PLACEHOLDER`), chỉ dùng khi chưa có file tham chiếu. Nguồn phân bố của từng lượt ghi trong `perf/results/perf-seed.json` và `metadata.json`.
- Mỗi lượt bắt đầu từ cùng trạng thái: `reset` chép lại database từ mẫu và so checksum nội dung với lúc seed (P6-46).

## 4. Hành trình (`perf/k6/lib/journeys.js`)

Token: `setup()` đăng nhập sẵn 300 khách (rải đều), mọi nhân viên và quản lý, các VU dùng chung; token của perf sống 4 giờ (`Jwt__AccessTokenExpirationMinutes`). Lý do: open model sinh thêm VU khi hệ thống chậm, nếu VU mới tự đăng nhập thì BCrypt dồn thành bão đăng nhập không có ở người dùng thật (thấy ở lượt thử 01/11, đăng nhập từ 5 % lên 14 % request). Chi phí đăng nhập đo riêng ở J2.

J1 xem chi nhánh, sân, lịch trống (55 %) · J2 đăng nhập (5 %) · J3 đặt sân ⇒ J4 trả cọc qua IPN 80 % / hủy 10 % / bỏ dở 10 % (15 %) · J5 lịch sử (15 %) · J6 nhân viên: danh sách, lịch, tổng quan, check-in/check-out (7 %) · J7 quản lý: hàng chờ, duyệt cọc (3 %). Mỗi request có tag `name` (mẫu endpoint), `slo` (read, booking, login, ipn, write), `journey`; 4xx nghiệp vụ (409 trùng sân, 400 hết chỗ) không tính lỗi mà đếm ở `business_rejections`.

## 5. Kết quả

`perf/results/k6/<lượt>/`: `metadata.json` (image, commit, cờ TADSS, trace, seed + checksum, CPU/RAM máy), `summary.json` (k6), `resources.json` + bảng in ra màn hình (`perf/stack/analyze.py`, tự chạy sau mỗi lượt): theo từng phút request/giây, VU, p95 k6 và BE, lỗi, kết nối DB đang chạy, CPU từng container (đánh dấu khi ≥ 90 % trần), RAM BE.

Trace (lượt phân tích, `up --trace`): Grafana <http://127.0.0.1:3003> ⇒ Explore ⇒ Tempo, tìm theo `service.name`. Hàng "Hiệu năng" có thêm panel PostgreSQL (kết nối, giao dịch, khóa) và Redis (lệnh, cache hit) từ exporter trong Alloy. `--code SP-xx` nạp vào dashboard; xem biểu đồ ở hàng "Hiệu năng" (chỉnh khung thời gian về lượt đo).
