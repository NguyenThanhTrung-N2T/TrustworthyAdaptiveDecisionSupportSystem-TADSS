# Môi trường đo hiệu năng `perf` (Epic 6C, gói 6C-4)

Cập nhật 02/11/2026 (SLO và quy tắc đo chốt, task 2.18). Chạy BE, TADSS, AI **đúng image `sha-…` của bản deploy** cùng DB, Redis, email và LLM riêng, chỉ trên máy; k6 đóng vai người dùng theo 7 hành trình; số liệu xem trên dashboard "Kiểm thử" ([TEST_DASHBOARD.md](TEST_DASHBOARD.md)). Kế hoạch: [plan 6C](../../spec/PHASE6_PLAN_6C_DEVOPS_OBSERVABILITY_PERF.md) task 2.10–2.17, 2.30, thiết kế Phụ lục B.6.

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
| `up` | `--be TAG --tadss TAG --ai TAG` (đo bản khác bản deploy), `--tadss-off` (BE không gọi TADSS, cặp đối chứng SP-06), `--trace` (trace 100 %, chỉ cho lượt phân tích), `--pg-locks on\|off` (collector `pg_locks` của Alloy; mặc định **tắt** từ 03/11 vì đọc `pg_locks` khóa chính bộ quản lý khóa PostgreSQL, chỉ bật cho lượt đối chứng), `--db-pool N` (`Maximum Pool Size` của BE, mặc định 100). Mỗi lần `up` đặt lại các tùy chọn về giá trị đã cho hoặc mặc định |
| `seed` | `--seed N`, `--customers N`, `--distribution FILE` (mặc định `distribution.reference.json` rút từ 215 booking thật, xem Mục 3) |
| `reset` | khôi phục hai database từ mẫu + xóa Redis + kiểm checksum (`run` tự gọi; khoảng 20 giây) |
| `run <profile>` | `--rate N` (hành trình/giây, mức danh nghĩa), `--duration 15m`, `--warmup 2m`, `--vus N` (closed), `--code SP-xx` (nạp lên dashboard), `--limited "lý do"` (lượt không dùng để kết luận, P6-47), `--no-reset`, `--selftest` (ngưỡng p95 Đọc 1 ms: kiểm k6 + verdict báo KHÔNG ĐẠT đúng; chỉ load/soak/closed, không nạp dashboard), `--pg-sample activity|locks` (lấy mẫu PostgreSQL suốt lượt bằng `perf/stack/pgsampler.py`: `pg-samples.jsonl.gz` + `hangfire-jobs.json`; `locks` chỉ cho lượt chẩn đoán, không đi cùng `--code`), `--tag TÊN` (hậu tố mã lượt, vd `diag`, `E1`). Breakpoint: `--start-rate`, `--step-rate`, `--max-rate`, `--step 3m` (mặc định 50 % / 25 % / 300 % của `--rate`, giữ 3 phút). Trần VU: `--max-vus N` hoặc `--journey-seconds S` (mặc định 1,5 × tốc độ đỉnh × `journeySeconds` của `slo.json`) |
| `pool <lượt> <lượt> …` | p95/p99 trên mẫu thô **ghép** của các lượt cùng profile, tham số, seed, image (SP-01 ×3, nhóm thiếu mẫu p99); từ chối khi khác cấu hình |
| `status` | tag, commit, trạng thái từng service |

Profile k6 (`perf/k6/mix.js`): `smoke` (kiểm đường đi, đủ 7 hành trình, chỉ chấm lỗi), `load` (SP-01), `stress` (SP-02), `spike` (SP-03), `soak` (SP-04), `breakpoint` (SP-05) đều open model một scenario: warm-up tăng 25 ⇒ 75 % (`phase=warmup`) ⇒ chuyển 10 s + 1 phút `phase=settle` ⇒ `phase=steady` (nhãn theo thời điểm gửi request); `closed` (người dùng cố định có think time). Lịch, ngưỡng và tiêu chí từng kịch bản: plan 6C B.7.2–B.7.5, số máy đọc ở `perf/k6/slo.json`.

**Lượt chính thức** (SP-xx): tắt bản deploy + runner trước (tranh CPU làm sai số liệu và làm VM đứng, 2.16). `perf.sh` ghim CPU: container hệ thống dùng các CPU đầu, k6 + bộ quan sát dùng 3 CPU cuối của VM Docker (máy 8 CPU: `0-4` / `5-7`), ghi ở `metadata.json`.

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

J1 xem chi nhánh, sân, lịch trống (55 %) · J2 đăng nhập (5 %) · J3 đặt sân ⇒ J4 trả cọc qua IPN 80 % / hủy 10 % / bỏ dở 10 % (15 %) · J5 lịch sử (15 %) · J6 nhân viên: danh sách, lịch, tổng quan, check-in/check-out (7 %) · J7 quản lý: hàng chờ, duyệt cọc (3 %). Mỗi request có tag `name` (mẫu endpoint), `slo` (read, booking, login, ipn, write), `journey`, `phase`; timeout 5 s. Lỗi (`request_errors`) = 5xx + timeout + 4xx **ngoài** danh sách cặp (status, `code`) hợp lệ của endpoint trong `slo.json` (`businessRejections`, mỗi cặp có lý do: TADSS từ chối, tranh khung giờ, nhân viên/quản lý ảo tranh cùng đơn); cặp hợp lệ đếm ở `business_rejections` theo `code`.

## 5. Kết quả

`perf/results/k6/<lượt>/`:

| File | Nội dung |
| :--- | :--- |
| `metadata.json` | image + commit 3 hệ, commit TADSS-Runtime + trạng thái sửa dở, SHA-256 script k6 / `slo.json` / compose, cờ TADSS, trace, seed + checksum, CPU/RAM máy, `cpuset` và trần từng container |
| `schedule.json` | lịch tải thực dùng (pha, bậc tải, điểm hồi phục spike, trần VU) do `mix.js` ghi |
| `summary.json`, `k6-exit` | summary k6 (threshold theo nhóm SLO), mã thoát (0 đạt, 99 vượt ngưỡng, khác = dừng giữa chừng) |
| `raw.csv.gz` | mẫu thô từng request (khoảng 8 MB cho 15 phút ở 50 request/giây): nguồn chấm SLO chính xác |
| `resources.json` + bảng in | theo phút từ Prometheus: request/giây, VU, p95 k6 và BE (xấp xỉ theo bucket), lỗi, kết nối DB, CPU từng container gồm k6 (đánh dấu khi ≥ 90 % trần), RAM BE; `INFRA_LIMITED` khi có phút mất số liệu hoặc k6 chạm 80 % CPU / 90 % RAM trần |
| `verdict.json` + bảng in | **kết luận** (`perf/stack/verdict.py`): ĐẠT / KHÔNG ĐẠT / KHÔNG ĐỦ DỮ LIỆU theo nhóm (p95 ≥ 100 mẫu, p99 ≥ 1000 mẫu), lỗi, p95 từng endpoint; breakpoint/stress: từng bậc, điểm gãy + tài nguyên tại bậc gãy và bậc trước (CPU/RAM, GC, thread pool, pool Npgsql, kết nối và khóa PostgreSQL, tỷ lệ đường lùi TADSS); spike: thời gian hồi phục; soak: RAM, kết nối DB, trôi p95. `INCOMPLETE` khi k6 dừng giữa chừng. Dashboard đọc file này: thiếu mẫu hoặc lượt không hợp lệ hiện là hạn chế, không phải đạt |

Threshold k6 chấm trên mọi mẫu nên có thể "vượt" ở nhóm chỉ vài chục mẫu (thoát 99): kết luận theo `verdict.json`.

Trace (lượt phân tích, `up --trace`): Grafana <http://127.0.0.1:3003> ⇒ Explore ⇒ Tempo, tìm theo `service.name`. Hàng "Hiệu năng" có thêm panel PostgreSQL (kết nối, giao dịch, khóa) và Redis (lệnh, cache hit) từ exporter trong Alloy. `--code SP-xx` nạp vào dashboard; xem biểu đồ ở hàng "Hiệu năng" (chỉnh khung thời gian về lượt đo).
