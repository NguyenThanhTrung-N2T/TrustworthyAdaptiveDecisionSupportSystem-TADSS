# ĐẶC TẢ DỮ LIỆU MINH CHỨNG & EVIDENCE PACK — v2.0

> **Vai trò của tài liệu:** định nghĩa cấu trúc Evidence Pack, các bộ dữ liệu minh chứng, quy tắc ẩn danh hóa và kiểm tra toàn vẹn. Test case và KPI tạo ra dữ liệu: [`EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md).
>
> **Thay đổi so với v1.0:** ba bộ dữ liệu v1 (`resilience_evaluation`, `latency_caching`, `governance_audit`) sinh từ EF Core InMemory và strategy giả lập đã bị gỡ ở bước 0 của Phase 5 (rà soát và điều chỉnh kế hoạch), vì không phản ánh hành vi trên hạ tầng thật. Tên file đã bỏ hậu tố `_V1` để tránh gây hiểu lầm; nội dung là đặc tả v2.0.

---

## 1. NGUYÊN TẮC

1. Dữ liệu minh chứng chỉ được sinh từ các lượt chạy trên hạ tầng thật (E-TC, E-EVAL, E-STAGING; E-EVAL là chế độ đánh giá một lệnh, chạy cùng `docker compose up -d --build` (D-14), thay cho E-LOCAL ở bản trước — D-12).
2. Schema production không chứa cột phục vụ thực nghiệm; thông tin lượt chạy nằm trong manifest của Evidence Pack.
3. Không lưu dữ liệu cá nhân (tên, số điện thoại, email, ghi chú tự do); định danh được ẩn danh hóa theo Mục 4.
4. Mỗi file dữ liệu có SHA-256 trong manifest; báo cáo dẫn về `run_id` và file cụ thể.

---

## 2. CẤU TRÚC EVIDENCE PACK

```text
docs/research/evidence-packs/{run_id}/
├── manifest.json        # run_id, thời điểm, commit 3 repo, digest image, thông số máy, danh sách file + SHA-256 + số dòng
├── config/              # cấu hình đã dùng (đã loại secret), k6 options, cấu hình Toxiproxy
├── raw/                 # dữ liệu thô: k6 summary/time series, báo cáo test (trx/HTML), export audit, kết quả truy vấn bất biến
├── grafana/             # JSON snapshot dashboard cùng khoảng thời gian (không lưu ảnh)
└── report.html          # báo cáo do bộ sinh báo cáo (tools/evidence/report/) sinh ra
```

> **Chế độ đánh giá một lệnh (D-12, đã hiện thực ở gói 5.5):** container **evaluator** của `docker-compose.yml` (chạy cùng lệnh `docker compose up -d --build`, D-14) tự dựng Evidence Pack của cả lượt đánh giá tại `evidence/<run_id>/` (cùng cấu trúc trên; thư mục làm việc, không commit), gồm `report.html`, dữ liệu thô và `manifest.json`; không còn bước dựng Evidence Pack bằng script chạy riêng. Bản cuối của lượt đầy đủ được chốt vào `docs/research/evidence-packs/{run_id}/`. Với lượt đánh giá trọn gói, `run_id` dùng nhóm `EVAL`: `EV-{yyyyMMdd}-EVAL-{quick|full}-r{n}` (`n` là số lần tiếp theo trong `evidence/` cùng ngày). Evidence Pack của E-EVAL ghi commit SmashCourt-BE/FE là `null` kèm lý do khi lượt chạy không có LT-04/E2E, và thêm vào `manifest.json` các trường `mode`, `dirty`, `steps`, `kpis`, `catalogCoverage`, `evChecks` (kết quả EV-1…EV-5), `commitNotes` (lý do commit BE/FE là `null`) và `images` (digest image). `steps.json` do evaluator ghi sau cùng (nội dung đã nằm trong `manifest.steps`) nên không có băm trong manifest. Các file của evaluator: `raw/d1/decision_audit.csv` (D1; bản ghi RP nằm chung file này, phân biệt bằng khóa idempotency `replay:...`), `raw/d1/decision_audit_portability.csv` (D1 của database PT `tadss_portability`, xuất khi có PT), `raw/d2/{timeseries,summary}.csv` (D2), `raw/load/<kịch bản>/requests.jsonl` (từng request k6, gồm `decision_id`, `idempotency_key`), `raw/chaos/chaos_log.csv` (D3), `raw/scale/scale2-checks.json`, `raw/contract/`, `raw/mutation/`, `raw/coverage/`, `raw/tests/` (trx + `catalog-results.csv`). Gói 5.6 (bước (e) và (f)) thêm:

| Đường dẫn trong pack | Nội dung | Có khi |
| :--- | :--- | :--- |
| `raw/replay/{requests,prepared}.jsonl`, `raw/replay/summary.json` | RP: kết quả gửi từng request (không có body, định danh duy nhất là khóa idempotency), meta đã băm theo thứ tự đầu vào, số liệu gửi/lỗi contract/thử lại | Bước (e) chạy phần RP; `summary.json` có cả khi `skipped`/`failed` (kèm `note`) |
| `raw/portability/{requests,prepared}.jsonl`, `summary.json`, `source.json` | PT: như trên; `source.json` ghi URL, SHA-256, kích thước, số dòng, bảng lọc Q-08, seed, cỡ mẫu, giấy phép và tham số miền của instance PT | Bước (e) chạy phần PT |
| `raw/d4/replay_smashcourt.csv` | D4 (Mục 3) | Có `raw/replay/` kèm kết quả gửi |
| `raw/d5/portability_hotel.csv` | D5 (Mục 3) | Có `raw/portability/` kèm kết quả gửi |
| `raw/analysis/rp_summary.json`, `pt_summary.json` | Phân tích RP-02 (theo kênh, hạng, khung giờ, so sánh `STATIC_100`/`TADSS_V2`/hiệu chỉnh `window_bias`) và PT-02 (bảng chéo với `is_canceled`) | Có D4/D5 tương ứng (RP-02 còn cần lịch cao điểm trong `appsettings.json`) |
| `raw/analysis/sc7_code_check.json` | Kết quả kiểm mã Q-11 cho SC7: trạng thái, lý do, các commit đã xét | Bước (f) có kết quả PT |

`manifest.json` của E-EVAL có thêm trường `datasets`: `{ "replay": {...}, "portability": {...} }` (mỗi phần `null` nếu bước (e) không chạy). Phần `replay` gồm số liệu gửi (trạng thái, số request đã chuẩn bị/gửi/không gửi, lỗi contract, thử lại, `window_bias`); phần `portability` gồm cùng số liệu cộng `mode`, `sampleSize`, `seed`, `source` (nội dung `source.json`: URL, SHA-256, giấy phép) và `codeCheck`. Không có UUID gốc hay mẫu lỗi chứa định danh.

- `run_id`: `EV-{yyyyMMdd}-{nhóm}-{mã}-{lần}` (ví dụ `EV-20261216-FI-02-r1`).
- File thô lớn hơn 20 MB được đính kèm vào GitHub Release; manifest vẫn ghi đường dẫn và SHA-256.
- Evidence Pack cuối cùng của Phase 8 được commit; các lượt chạy thử không commit.

### 2.1. `manifest.json`

```json
{
  "schemaVersion": "2.0.0",
  "runId": "EV-20261216-LT-01-r1",
  "createdAtUtc": "2026-12-16T03:00:00Z",
  "environment": "E-EVAL",
  "commits": { "tadss": "<sha>", "smashcourtBe": "<sha>", "smashcourtFe": "<sha>" },
  "images": { "postgres": "postgres:16@sha256:...", "redis": "redis:7@sha256:..." },
  "machine": { "cpu": "...", "logicalCores": 8, "ramGb": 16, "os": "...", "docker": "..." },
  "testCases": ["LT-01"],
  "files": [ { "path": "raw/k6-summary.json", "sha256": "<hex>", "rows": null } ]
}
```

---

## 3. CÁC BỘ DỮ LIỆU

### D1 — `decision_audit` (xuất từ schema `tadss`)

Nguồn dữ liệu chính cho Đồ án 2 và Khóa luận. Một dòng = một quyết định, ghép từ `decision_executions`, `decision_overrides`, `decision_outcomes`.

| Nhóm | Cột |
| :--- | :--- |
| Định danh | `decision_id`, `reservation_hash`, `branch_hash`, `role` (`PRIMARY`/`SHADOW`), `correlation_id`, `created_at_utc` |
| Strategy & kết quả | `strategy_name`, `strategy_version`, `strategy_status`, `status`, `deposit_rate`, `deposit_amount`, `reason_code`, `approval_expires_at_utc` |
| Thực thi | `execution_type`, `execution_time_ms`, `fallback_used`, `failure_reason`, `cache_hit`, `request_hash` |
| Đặc trưng request | `items_count`, `total_price`, `currency`, `earliest_start_local`, `lead_time_hours`, `is_peak`, `customer_is_guest`, `customer_tier`, `customer_member_days`, `history_completed`, `history_no_show`, `history_cancelled`, `history_late_cancelled`, `history_payment_expired`, `demand_occupancy_rate`, `demand_waitlist_count` (trường tùy chọn để trống khi nguồn không có) |
| Con người | `override_action`, `override_deposit_rate`, `override_actor_hash`, `overridden_at_utc` |
| Kết quả thực tế | `outcome`, `outcome_occurred_at_utc` |

### D2 — `load_results` (k6)

- `summary`: mỗi dòng một lượt chạy — kịch bản, tốc độ/VU, thời lượng, số request, p50/p95/p99/max, tỷ lệ lỗi.
- `timeseries`: mỗi dòng một request — `timestamp_utc`, `scenario`, `http_status`, `duration_ms`, `decision_status`, `execution_type`, `correlation_id`.

### D3 — `chaos_log`

`fi_id`, `run_id`, `toxic` (loại, tham số), `started_at_utc`, `ended_at_utc`, `expected`, `observed`, `passed`, `invariant_violations`.

### D4 — `replay_smashcourt`

Dạng dài: mỗi booking hai dòng liên tiếp, `STATIC_100` (cọc 100 % tĩnh) rồi `TADSS_V2`. Cột theo thứ tự `replay.D4_COLUMNS`:

- Booking (ẩn danh): `booking_hash`, `branch_hash`, `court_count`, `booking_date`, `start_local`, `duration_min`, `total_price`, `customer_tier`, `customer_is_guest`, `dev_final_status`.
- Kết quả theo chính sách: `policy` (`STATIC_100`, `TADSS_V2`), `status`, `deposit_rate`, `deposit_amount`, `reason_code`.
- Bổ sung (gói 5.6): `decision_id`, `channel` (`ONLINE`/`WALK_IN`), `lead_time_minutes` (lead time **gốc**), `shift_days` (khoảng dời, bội số 7), `window_bias` (lead time gốc ≤ 120 phút), `strategy_status` (ghép từ D1 theo `decision_id`), `status_window_adjusted` (kết quả hiệu chỉnh offline, §1.1 của `INTEGRATION_SPEC`; cột `status` vẫn là kết quả runtime), `contract_errors` (lỗi Q-09 cách nhau ` | `, trống nếu không có).
- Dòng `STATIC_100` để trống `decision_id`, `strategy_status`, `contract_errors`. Dòng `TADSS_V2` không có response 200 thì để trống `status`, mức cọc và lý do.
- `dev_final_status` là nhãn môi trường phát triển; chỉ dùng mô tả (xem Mục 8 của tài liệu giao thức đánh giá). `customer_tier` là hạng tại thời điểm xuất.

### D5 — `portability_hotel`

Một dòng cho mỗi request PT. Cột theo thứ tự `hotel_adapter.D5_COLUMNS`:

`source_row` (số dòng trong file gốc, tiêu đề là dòng 1), `hotel`, `arrival_local` (14:00 ngày đến gốc), `nights`, `room_type`, `total_price` (EUR), `is_repeated_guest`, `previous_cancellations`, `previous_bookings_not_canceled`, kết quả quyết định (`status`, `deposit_rate`, `deposit_amount`, `reason_code`), `is_canceled` (nhãn gốc của dataset), và bổ sung (gói 5.6): `decision_id`, `strategy_status` (ghép từ D1 PT), `contract_errors`, `shift_days`, `lead_time_minutes` (gốc), `deposit_type`, `market_segment`, `attempts` (số lần gửi, có retry Q-09).

**Nguồn và giấy phép dataset.** Hotel Booking Demand (Antonio, de Almeida, Nunes, *Data in Brief* 22:41–49, 2019, DOI 10.1016/j.dib.2018.11.126). Bài báo công bố theo **CC BY 4.0**; việc xác nhận giấy phép là việc của người dùng (U-2) và chưa được ghi nhận trong repo. Evaluator tải bản TidyTuesday 2020-02-11 `hotels.csv` (ghim URL theo commit và SHA-256 trong `hotel_adapter.py`, kiểm khi tải), **không bao giờ commit file gốc** vào repo hay Evidence Pack. Evidence Pack chỉ chứa D5 (các cột trên, không phải toàn bộ 32 cột gốc), `source.json` và số liệu tổng hợp. Khi chốt Evidence Pack vào `docs/research/evidence-packs/`, chỉ đưa D5 nếu giấy phép cho phép phân phối lại dữ liệu dẫn xuất; nếu không thì chỉ đưa `pt_summary.json`.

**Dữ liệu SmashCourt thô** (CSV xuất bằng `tools/evidence/sql/export_smashcourt_bookings.sql`) nằm ở `data/private/` (gitignore, mount chỉ đọc vào evaluator) và không bao giờ vào repo hay Evidence Pack; định danh được băm HMAC-SHA256 bằng khóa ngẫu nhiên của lượt chạy trước khi ghi bất kỳ file nào vào `evidence/`.

---

## 4. ẨN DANH HÓA

- Định danh (customer, booking, branch, actor) → HMAC-SHA256 với khóa bí mật không commit vào repo; cùng một định danh luôn cho cùng một mã băm trong một Evidence Pack.
- Loại bỏ hoàn toàn: tên, số điện thoại, email, ghi chú, địa chỉ IP.
- Giữ nguyên: thời gian, giá, hạng thành viên, trạng thái.

---

## 5. KIỂM TRA TOÀN VẸN EVIDENCE PACK

| Mã | Kiểm tra |
| :--- | :--- |
| EV-1 | SHA-256 của mọi file khớp manifest |
| EV-2 | Mọi `correlation_id`/`decision_id` trong D2 của một lượt chạy tồn tại trong D1 của cùng lượt (từ gói 5.6 kiểm theo từng nguồn: D2 cộng response RP với D1 chính; response PT với D1 của database PT) |
| EV-3 | Số response thành công = số bản ghi `PRIMARY` (SC3), cũng theo từng nguồn |
| EV-4 | Không có cột dữ liệu cá nhân trong mọi file; từ gói 5.6 còn quét UUID gốc của CSV SmashCourt (không được xuất hiện trong pack) |
| EV-5 | Có đủ commit, cấu hình và thông số máy trong manifest |

Bộ sinh báo cáo (ở chế độ đánh giá một lệnh do evaluator gọi ở bước (f); dựng lại từ pack có sẵn bằng `python -m evidence.report <thư mục pack>` chạy trong container evaluator hoặc từ thư mục `tools/` — lệnh cụ thể ở guide chạy đánh giá mục 7; không còn notebook) chạy EV-1…EV-5 trước khi sinh `report.html`; nếu có kiểm tra không đạt thì báo cáo ghi rõ và không dùng lượt chạy đó làm minh chứng.
