# Báo cáo gói 5.6 — Minh chứng dữ liệu và hướng dẫn chạy (bản đang viết)

> Không stage file này (`spec/PHASE5_*.md`). Hiện mới có phần **Spike S-1…S-5** của Task 1; các phần còn lại do Task 2–7 bổ sung.

## Kết quả spike (giá trị cho các task sau sao chép nguyên văn)

Trạng thái: **ĐỀ XUẤT, chờ người dùng duyệt** `MaxItemMinutes`, `HighValueThreshold`, nguồn dataset và số luồng trước Task 4.

| Tham số | Giá trị đề xuất |
| :--- | :--- |
| URL dataset (ghim theo commit, bất biến) | `https://raw.githubusercontent.com/rfordatascience/tidytuesday/d75aaa0d31596ad6487ae0db20138067d301e031/data/2020/2020-02-11/hotels.csv` |
| SHA-256 | `7c2ae42a7353905ea136e5c2287f17c92c5435826598bfbb8491c6f0c7b1fc06` |
| Kích thước | 16.855.599 byte |
| Số dòng dữ liệu / số cột | 119.390 / 32 (không tính header) |
| Số dòng hợp lệ sau lọc Q-08 | 118.674 (loại 716) |
| `Tadss:Validation:MaxItemMinutes` (`eval-tadss-portability`) | **100800** (70 ngày) |
| `Tadss:Strategies:Default:HighValueThreshold` (EUR) | **750** |
| Số luồng gửi mặc định (bước e) | **8** (cấu hình được; trần khuyến nghị 16) |
| Ước lượng bước (e), bản nhanh | khoảng 1 phút (RP ~215 + PT 10.000 request) |
| Ước lượng bước (e), bản đầy đủ | khoảng 4–7 phút (118.674 PT + RP) |

## Spike S-1…S-5

### S-1 — Dataset

| Mục | Kết quả |
| :--- | :--- |
| Nguồn ưu tiên (bản gốc kèm bài báo, Data in Brief 22:41–49, DOI 10.1016/j.dib.2018.11.126) | **Không tải tự động được**: `sciencedirect.com` trả HTTP 403 cho `curl` (cả trang bài và PDF). Phụ lục của bài cần tải bằng trình duyệt. |
| Nguồn dùng (dự phòng) | TidyTuesday 2020-02-11 `hotels.csv` |
| URL nhánh `master` | `https://raw.githubusercontent.com/rfordatascience/tidytuesday/master/data/2020/2020-02-11/hotels.csv` (HTTP 200) |
| URL ghim commit (khuyến nghị đặt vào mã) | `.../tidytuesday/d75aaa0d31596ad6487ae0db20138067d301e031/data/2020/2020-02-11/hotels.csv` — commit `d75aaa0` "add week 7 - hotels dataset" (2020-02-10), commit duy nhất chạm file; **cho cùng SHA-256** với bản `master` (đã tải lại và so sánh) |
| Kích thước | 16.855.599 byte (ASCII, không BOM, kết thúc dòng LF) |
| SHA-256 | `7c2ae42a7353905ea136e5c2287f17c92c5435826598bfbb8491c6f0c7b1fc06` |
| Số dòng dữ liệu | 119.390 (`wc -l` = 119.391 gồm header) |
| Số cột | 32 |
| Phân bố `hotel` | City Hotel 79.330; Resort Hotel 40.060 |
| Vị trí lưu khi spike | `/tmp/hotel-spike/hotels.csv` (ngoài repo; không commit, không chép vào repo) |

Danh sách 32 cột (đúng thứ tự file): `hotel, is_canceled, lead_time, arrival_date_year, arrival_date_month, arrival_date_week_number, arrival_date_day_of_month, stays_in_weekend_nights, stays_in_week_nights, adults, children, babies, meal, country, market_segment, distribution_channel, is_repeated_guest, previous_cancellations, previous_bookings_not_canceled, reserved_room_type, assigned_room_type, booking_changes, deposit_type, agent, company, days_in_waiting_list, customer_type, adr, required_car_parking_spaces, total_of_special_requests, reservation_status, reservation_status_date`.

Đối chiếu bảng ánh xạ Task 3: **đủ 100 % cột cần dùng** (`hotel`, `is_canceled`, `lead_time`, `arrival_date_year`, `arrival_date_month`, `arrival_date_day_of_month`, `stays_in_weekend_nights`, `stays_in_week_nights`, `adr`, `is_repeated_guest`, `previous_cancellations`, `previous_bookings_not_canceled`, `reserved_room_type`, `deposit_type`, `market_segment`, `distribution_channel`, `customer_type`, `reservation_status`, `reservation_status_date`). Tên tháng là tiếng Anh đầy đủ (`July`…). Không so được với bản gốc của bài báo (403), nên chốt **một** bản là bản TidyTuesday ghim commit. Giấy phép: bài báo công bố theo CC BY 4.0 (cần người dùng xác nhận trước khi commit bất kỳ phần nào của dữ liệu, theo Global Constraints).

### S-2 — Phân bố và tham số

Đếm theo quy tắc lọc Q-08 (các lý do rời nhau, không dòng nào dính hai lý do):

| Quy tắc lọc | Số dòng |
| :--- | ---: |
| Số đêm = 0 (`stays_in_weekend_nights + stays_in_week_nights = 0`) | 715 |
| `adr` < 0 | 1 |
| Ngày đến không hợp lệ (năm/tháng/ngày không dựng được `date`) | 0 |
| Lưu trú vượt `MaxItemMinutes` đề xuất (100800) | 0 |
| **Tổng bị loại** | **716** |
| **Còn lại (hợp lệ)** | **118.674** |

Ghi chú: có 1.244 dòng hợp lệ với `adr = 0` (giữ lại, đây chính là ca "giá 0"); không dòng nào có `adr` quá 2 chữ số thập phân.

**Số đêm (118.674 dòng hợp lệ)**

| Thống kê | Giá trị |
| :--- | ---: |
| p50 / p90 / p99 / p99,9 | 3 / 7 / 14 / 25 đêm |
| max | **69 đêm** |
| Số dòng > 14 / > 21 / > 30 đêm | 439 / 162 / 24 |

**`MaxItemMinutes` đề xuất = 100800.** Lưu trú dài nhất 69 đêm, 14:00 → 12:00 checkout = 69 × 1440 − 120 = **99.240 phút**. Cộng lề 60 phút cho ngày đổi giờ mùa hè Lisbon (`ItemDurationPolicy` tính theo `End − Start` của `DateTimeOffset`, tức theo instant) là 99.300; làm tròn lên bội số của ngày: 70 × 1440 = **100.800** (lề ~1.500 phút). Chọn phủ **mọi** dòng (quy tắc lọc thứ tư loại 0 dòng) thay vì cắt ở p99 (20.040 phút, sẽ loại 439 dòng không có lý do nghiệp vụ). `MinItemMinutes` giữ mặc định 30 (lưu trú ngắn nhất = 1 đêm = 1.320 phút). Giá trị mặc định 720 không dùng được: 1 đêm đã là 1.320 phút (xác nhận ở S-3, 400 `DOMAIN_INVARIANT_VIOLATION`).

**Giá trị đơn `adr × số đêm` (EUR, 118.674 dòng hợp lệ)**

| Thống kê | EUR |
| :--- | ---: |
| p50 | 268,0 |
| p75 | 448,0 |
| **p90** | **729,0** |
| p95 | 1.004,7 |
| p99 | 1.680,4 |
| max / trung bình | 7.590,0 / 360,0 |

| Theo khách sạn | n | p50 | p90 | p95 |
| :--- | ---: | ---: | ---: | ---: |
| City Hotel | 78.999 | 266,9 | 594,0 | 748,0 |
| Resort Hotel | 39.675 | 277,2 | 1.074,0 | 1.382,7 |
| Mẫu 10.000 dòng ngẫu nhiên (seed bất kỳ) | 10.000 | 270,0 | 736,5 | 1.015,0 |

Tỷ lệ dòng có tổng ≥ ngưỡng: 700 → 10,86 %; **750 → 9,48 % (11.246 dòng)**; 800 → 8,34 %; 1.000 → 5,07 %.

**`HighValueThreshold` đề xuất = 750 EUR.** p90 = 729 làm tròn tới bội số 50 gần nhất; cho ≈ 9,5 % đơn vào `NEED_APPROVAL` (gần mục tiêu ~10 %, hơn 700 là 10,9 %). Mẫu 10.000 dòng cho p90 gần như cùng giá trị (736,5), nên bản nhanh và đầy đủ không lệch nhau. Chọn một lần, trước lượt chạy, không chỉnh theo kết quả (Q-08).

### S-3 — Contract (3 request tay + ca bổ sung)

Cách làm: dựng 7 request bằng script ngoài repo, **kiểm `docs/contracts/schemas/v2/decision-request.schema.json` bằng `jsonschema` 4.26.0 (Draft 2020-12, FormatChecker) trước khi gửi** — cả 7 đều **0 lỗi schema**. Gửi vào một container `tadss-eval-runtime:local` tạm (`docker run` trên mạng `tadss-eval_default`, `ASPNETCORE_ENVIRONMENT=Testing`, DB riêng `tadss_spike`, `ApplyMigrationsOnStartup=true`, `Tadss__Validation__MaxItemMinutes=100800`, `Tadss__Strategies__Default__HighValueThreshold=750`, telemetry tắt, nối thẳng PostgreSQL/Redis). Khách sạn `Europe/Lisbon`, một item 14:00 → 12:00, `currency = EUR`, `channel = ONLINE`. **Không sửa `src/`.**

| Ca | Request | HTTP | Kết quả |
| :--- | :--- | :---: | :--- |
| A — 1 đêm | 2027-03-10 → 03-11, 80 EUR, khách vãng lai | 200 | `APPROVED`, `deposit {rate 0.5, amount 40.0, EUR}`, `TIER_DEPOSIT` |
| B — 14 đêm | 2027-03-20 → 04-03 (qua đổi giờ 28/03: offset +00:00 → +01:00, 19.980 phút), 1728,30 EUR | 200 | `NEED_APPROVAL`, `deposit null`, `HIGH_VALUE_REVIEW`, lý do "Giá trị đơn 1,728.3 EUR đạt ngưỡng cần duyệt 750 EUR", `approvalExpiresAt` = `evaluatedAt` + 30 phút |
| C — giá 0 | 3 đêm, `price = totalPrice = 0`, khách quay lại | 200 | `APPROVED`, `deposit {rate 0.5, amount 0.0}` |
| D1 — 2 chữ số, hòa làm tròn lên | 171,99 EUR × 0,5 = 85,995 | 200 | `amount` = **86.00** |
| D2 — 2 chữ số, hòa mà làm tròn chẵn sẽ xuống | 171,97 EUR × 0,5 = 85,985 | 200 | `amount` = **85.99** |
| E — lưu trú dài nhất dataset | 69 đêm, 99.240 phút, 520,5 EUR | 200 | `APPROVED`, `amount 260.25` |
| F — khách quay lại, có `history` | 2 đêm, 150,5 EUR | 200 | `APPROVED`, `amount 75.25` |

Kết luận phần "giá thập phân": **schema nhận số thập phân 2 chữ số; runtime làm tròn `amount` theo `Math.Round(totalPrice × rate, 2, MidpointRounding.AwayFromZero)`** (`DepositCalculator`; EUR = 2 chữ số, VND = 0). D1/D2 chứng minh đúng AwayFromZero (làm tròn chẵn sẽ cho 86,00 và 85,98).

Đối chứng với **cấu hình mặc định** (qua `eval-proxy`, `MaxItemMinutes = 720`): ca B và ca E đều **400 `DOMAIN_INVARIANT_VIOLATION`** ("Thời lượng mỗi item phải từ 30 đến 720 phút; item thứ 1 dài 19980 phút" / "...99240 phút"); ca A (1.320 phút) cũng sẽ bị từ chối. Đây là lý do PT cần instance riêng (Q-02). Với mặc định `HighValueThreshold = 2.000.000` thì mọi đơn EUR đều `APPROVED` — cũng là lý do cần ngưỡng EUR riêng.

Lưu ý cho Task 2/3/5 (phát hiện khi làm S-3):
- Body 200 mang số thập phân với số chữ số thừa/thiếu (`40.0`, `86.00`, `0.0`): `contract_check` phải parse bằng `Decimal` (`json.loads(..., parse_float=Decimal)`) và làm tròn bằng `ROUND_HALF_UP` (không dùng `round()` của Python vì là làm tròn chẵn), số chữ số theo tiền tệ (VND 0, còn lại 2).
- `zoneinfo.ZoneInfo("Europe/Lisbon")` không chạy trên Python Windows nếu thiếu gói `tzdata` (lỗi `ZoneInfoNotFoundError`); trong container Linux thì có sẵn. `tools/evidence` cần ghim `tzdata` vào `requirements.txt` nếu pytest chạy trên Windows.
- `NEED_APPROVAL` luôn có cửa sổ duyệt 30 phút tính từ giờ máy chủ, không phụ thuộc `requestedAt` hay lead time (khớp Q-04).
- Không gặp 503/429 trong toàn bộ S-3/S-5 (mọi request 200 hoặc 400 có chủ đích); retry Q-09 chưa được kích hoạt.

### S-4 — Schema SmashCourt

Nạp `docs/database/schema_smashcourt.sql` (pg_dump 17.7) vào `postgres:16.15-alpine` dùng một lần, sau khi tạo role `neondb_owner` (SUPERUSER, chỉ trong container) — nên không phải lọc 77 dòng `OWNER TO`. Container và volume ẩn của nó đã xóa.

**Một sai khác duy nhất:** dòng 13 `SET transaction_timeout = 0;` là tham số của PostgreSQL 17, nên máy chủ 16.15 báo `ERROR: unrecognized configuration parameter "transaction_timeout"`. Lỗi vô hại (psql chạy tiếp, đây là lỗi duy nhất trong toàn log), nhưng **`verify-export.sh` (Task 2) phải nạp không bật `ON_ERROR_STOP`** hoặc lọc dòng đó (hoặc dùng `postgres:17`). `\restrict` của dump được psql 16.15 chấp nhận.

Mọi cột Task 2 cần đều có:

| Bảng | Cột (kiểu) xác nhận |
| :--- | :--- |
| `bookings` | `id`, `branch_id` (uuid); `customer_id` (uuid, null được); `status` (`booking_status`); `source` (`booking_source`); `created_at` (timestamptz); `cancel_source` (`cancel_source_enum`, null được); `guest_phone` (varchar, chỉ dùng trong truy vấn); ngoài ra có `booking_date`, `cancelled_at`, `booking_code`, `guest_name`, `guest_email`, `note`, `cancel_token_hash` — **các cột cá nhân/mã này không được xuất** |
| `booking_courts` | `booking_id`, `court_id` (uuid); `date` (date); `start_time`, `end_time` (time); `is_active` (bool) |
| `booking_price_items` | `booking_court_id` (uuid); `unit_price` (numeric) |
| `courts` | `id`, `court_type_id` (uuid) |
| `invoices` | `booking_id` (uuid); `final_total` (numeric) |
| `customer_loyalty` | `user_id`, `tier_id` (uuid) |
| `loyalty_tiers` | `id`; `min_points` (int4); `name` |
| `users` | `id`; `created_at` (timestamptz); `phone` (varchar) |

Enum (đúng thứ tự khai báo):
- `booking_status`: `PENDING, CONFIRMED, PAID_ONLINE, IN_PROGRESS, PENDING_PAYMENT, COMPLETED, CANCELLED, CANCELLED_PENDING_REFUND, CANCELLED_REFUNDED, NO_SHOW`
- `booking_source`: `ONLINE, WALK_IN`
- `cancel_source_enum`: `LINK, STAFF, SYSTEM, CUSTOMER`

Gợi ý cho bảng ánh xạ lịch sử (Task 2 chốt bản cuối): "hủy do nhân viên không tính" ⇒ `cancel_source = 'STAFF'`; `paymentExpiredCount` ⇒ `CANCELLED` với `cancel_source = 'SYSTEM'`; `CANCELLED_PENDING_REFUND`/`CANCELLED_REFUNDED` là nhánh hủy có hoàn tiền.

### S-5 — Thông lượng

Máy: Docker Desktop 8 CPU / 7,6 GiB, stack `tadss-eval` (nginx round-robin 2 instance → Toxiproxy → PostgreSQL/Redis). Client: script Python `http.client` (keep-alive, một kết nối mỗi luồng) chạy trong container cùng mạng; mỗi request khác `reservationId`/khóa idempotency/giá/ngày (không trùng cache); 1.000 request mỗi cấu hình, đã khởi động nóng trước. Mọi request đều 200.

**Cấu hình định tuyến chuẩn của RP: `eval-proxy` (2 instance, qua Toxiproxy), item 2 giờ, cấu hình mặc định**

| Luồng | Thời gian (s) | req/s | p50 (ms) | p95 (ms) |
| ---: | ---: | ---: | ---: | ---: |
| 1 (tuần tự) | 10,74 | 93 | 9,9 | 14,1 |
| 4 | 4,03 | 248 | 14,5 | 25,7 |
| 8 | 2,75 | 364 | 19,5 | 35,1 |
| 16 | 2,13 | 471 | 30,7 | 57,0 |
| 8 (bền vững, 20.000 request) | 67,3 | 297 | 22,0 | 53,4 |

**Cấu hình PT: một instance cấu hình khách sạn, nối thẳng PostgreSQL/Redis (tương tự `eval-tadss-portability`), item 1–7 đêm**

| Luồng | Thời gian (s) | req/s | p50 (ms) | p95 (ms) |
| ---: | ---: | ---: | ---: | ---: |
| 1 (tuần tự) | 10,78 | 93 | 10,2 | 13,6 |
| 4 | 3,44 | 291 | 11,3 | 24,8 |
| 8 | 1,99 | 503 | 14,8 | 23,6 |
| 16 | 1,45 | 692 | 20,9 | 36,1 |
| 8 (bền vững, 20.000 request) | 38,3 | 522 | 14,2 | 22,1 |

**Chốt: 8 luồng mặc định** (cấu hình được). Lý do: 8 luồng nhanh gấp 4–5 lần tuần tự, p95 dưới 60 ms, thông lượng bền vững không suy giảm sau 20.000 request, và còn dư địa (16 luồng chỉ nhanh hơn thêm ~30 %, tăng p95 và nguy cơ 503 cần retry). 1 luồng là phương án gỡ lỗi.

**Ước lượng thời lượng bước (e)** (dùng tốc độ bền vững ở 8 luồng: PT ≈ 520 req/s, RP qua proxy ≈ 300 req/s; chưa tính tải 16,9 MB, phân tích, kiểm schema phía client, ghi D4/D5, nên nhân hệ số an toàn ~1,5):

| Chế độ | Nội dung | Thuần gửi | Ước tính thực tế |
| :--- | :--- | ---: | ---: |
| Nhanh | RP ~215 + PT 10.000 | ~1 s + ~19 s | ≈ 1 phút |
| Đầy đủ | RP ~215 + PT 118.674 | ~1 s + ~3,8 phút | ≈ 4–7 phút |
| Tham chiếu: 1 luồng, đầy đủ | — | ~21 phút | — |

Ước lượng có độ bất định do tốc độ máy người dùng và chi phí client của evaluator thật (kiểm schema bằng `jsonschema` mỗi request có thể chậm hơn client thử nghiệm); cần đo lại ở Task 4/5.

### Dọn dẹp và kiểm tra phạm vi

- Container tạm (`spike-pg-smash`, `spike-pt`), database `tadss_spike`, stack `tadss-eval` (`down -v --remove-orphans`), volume và mạng đã xóa; chỉ còn **image** (`tadss-eval-runtime:local`, `grafana/otel-lgtm:0.34.0`, `postgres:16.15-alpine`, `redis:7.4-alpine`, `nginx:1.29.8-alpine`, `ghcr.io/shopify/toxiproxy:2.12.0`, `python:3.12-slim`).
- Không đổi `src/`, `docs/contracts/`, `docker-compose.yml`. File đổi trong repo: `.gitignore` (thêm `data/private/`) và báo cáo này. Dataset và script thống kê nằm ngoài repo (`/tmp/hotel-spike*`).

## Task 3 – kết quả chạy adapter trên dataset thật

`tools/evidence/hotel_adapter.py` chạy trên `hotels.csv` thật (SHA-256 khớp `7c2ae42a…fc06`, 119.390 dòng đúng `EXPECTED_ROW_COUNT`; file nằm ngoài repo). Mọi request được kiểm bằng `contract_check.request_errors` (schema `decision-request` v2); ngày giả định "gửi" là 2026-10-09 12:00 giờ Lisbon, seed lấy mẫu `SAMPLE_SEED = 20260907`.

| Chế độ | Request dựng | Lỗi schema | Khóa idempotency trùng | Thời gian chọn + dựng + kiểm schema |
| :--- | ---: | ---: | ---: | ---: |
| Nhanh (`quick`, 10.000 dòng hợp lệ) | 10.000 | **0** | 0 | 0,5 s + 7,9 s |
| Đầy đủ (`full`, mọi dòng hợp lệ) | 118.674 | **0** | 0 | 1,0 s + 112,3 s (chưa tính nạp CSV 1,4 s) |

Bảng đếm lọc theo lý do (Q-08, rời nhau, khớp spike S-2; giống nhau ở cả hai chế độ vì đếm trên toàn bộ dòng nguồn):

| Lý do | Số dòng |
| :--- | ---: |
| `zero_nights` (số đêm = 0) | 715 |
| `negative_adr` (`adr` < 0) | 1 |
| `invalid_arrival_date` | 0 |
| `stay_too_long` (> 100800 phút) | 0 |
| **Tổng bị loại** | **716** |
| **Hợp lệ** | **118.674** |

Quan sát: `shift_days` nằm trong [3332; 4592] (bội số 7); lưu trú dài nhất 99.240 phút (< `MaxItemMinutes` 100800); giá trị đơn lớn nhất 7.590,00 EUR; tỷ lệ đơn ≥ 750 EUR là 9,44 % (mẫu nhanh) và 9,48 % (đầy đủ, 11.246 dòng), khớp ước lượng ~9,5 % của S-2. Cùng seed hai lần cho cùng tập mẫu. Thời gian dựng ~0,8–1,1 ms/request chủ yếu là kiểm schema; chế độ đầy đủ tốn ~2 phút CPU phía client (cần tính vào ước lượng bước (e) ở Task 5, chạy song song với việc gửi).

Cập nhật sau review Task 3 (vòng 1): theo spec thiết kế 4.6 (`is_repeated_guest`/`previous_*` -> `history`), `history` nay lấy từ `previous_*` cho MỌI dòng (khách `is_repeated_guest = 0` mà `previous_*` > 0 vẫn mang lịch sử); `isGuest`/`customerId` giữ nguyên. Chạy lại bản nhanh trên dataset thật: 10.000 request, 0 lỗi schema, bảng lọc không đổi.
