# ĐẶC TẢ DỮ LIỆU MINH CHỨNG & EVIDENCE PACK — v2.0

> **Vai trò của tài liệu:** định nghĩa cấu trúc Evidence Pack, các bộ dữ liệu minh chứng, quy tắc ẩn danh hóa và kiểm tra toàn vẹn. Test case và KPI tạo ra dữ liệu: [`EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md).
>
> **Thay đổi so với v1.0:** ba bộ dữ liệu v1 (`resilience_evaluation`, `latency_caching`, `governance_audit`) sinh từ EF Core InMemory và strategy giả lập đã bị gỡ ở bước 0 của Phase 5 (rà soát và điều chỉnh kế hoạch), vì không phản ánh hành vi trên hạ tầng thật. Tên file đã bỏ hậu tố `_V1` để tránh gây hiểu lầm; nội dung là đặc tả v2.0.

---

## 1. NGUYÊN TẮC

1. Dữ liệu minh chứng chỉ được sinh từ các lượt chạy trên hạ tầng thật (E-TC, E-EVAL, E-STAGING; E-EVAL là chế độ đánh giá một lệnh `docker compose --profile evaluation up`, thay cho E-LOCAL ở bản trước — D-12).
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
├── grafana/             # ảnh chụp + JSON snapshot dashboard cùng khoảng thời gian
└── report.html          # báo cáo do bộ sinh báo cáo (notebook) sinh ra
```

> **Chế độ đánh giá một lệnh (D-12, kế hoạch gói 5.5, chưa hiện thực):** container **evaluator** của profile `evaluation` tự dựng Evidence Pack của cả lượt đánh giá tại `evidence/<run_id>/` (cùng cấu trúc trên; thư mục làm việc, không commit), gồm `report.html`, dữ liệu thô và `manifest.json`; không còn bước dựng Evidence Pack bằng script chạy riêng. Bản cuối của lượt đầy đủ được chốt vào `docs/research/evidence-packs/{run_id}/`. Với lượt đánh giá trọn gói, `run_id` dùng nhóm `EVAL` (đề xuất: `EV-{yyyyMMdd}-EVAL-{quick|full}-{lần}`, chốt khi hiện thực).

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

- Booking (ẩn danh): `booking_hash`, `branch_hash`, `court_count`, `booking_date`, `start_local`, `duration_min`, `total_price`, `customer_tier`, `customer_is_guest`, `dev_final_status`.
- Kết quả theo chính sách: `policy` (`STATIC_100`, `TADSS_V2`), `status`, `deposit_rate`, `deposit_amount`, `reason_code`.
- `dev_final_status` là nhãn môi trường phát triển; chỉ dùng mô tả (xem Mục 8 của tài liệu giao thức đánh giá).

### D5 — `portability_hotel`

`source_row`, `hotel`, `arrival_local`, `nights`, `room_type`, `total_price`, `is_repeated_guest`, `previous_cancellations`, `previous_bookings_not_canceled`, kết quả quyết định (`status`, `deposit_rate`, `reason_code`), `is_canceled` (nhãn gốc của dataset). Trước khi đưa dữ liệu gốc vào repo phải kiểm tra giấy phép của dataset; nếu không được phép thì chỉ commit script tải và kết quả tổng hợp.

### D6 — `uat_pilot`

- Booking: `booking_hash`, `created_at_utc`, `decision_status`, `approval_latency_sec`, `final_status`.
- SUS: `respondent_hash`, `role` (`CUSTOMER`/`MANAGER`), `q1`…`q10`, `sus_score`.

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
| EV-2 | Mọi `correlation_id`/`decision_id` trong D2 của một lượt chạy tồn tại trong D1 của cùng lượt |
| EV-3 | Số response thành công = số bản ghi `PRIMARY` (SC3) |
| EV-4 | Không có cột dữ liệu cá nhân trong mọi file |
| EV-5 | Có đủ commit, cấu hình và thông số máy trong manifest |

Bộ sinh báo cáo (notebook; ở chế độ đánh giá một lệnh do evaluator gọi) chạy EV-1…EV-5 trước khi sinh `report.html`; nếu có kiểm tra không đạt thì báo cáo ghi rõ và không dùng lượt chạy đó làm minh chứng.
