# Gói 5.6 — Minh chứng dữ liệu (Replay SmashCourt, Hotel Booking Demand) và hướng dẫn chạy: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Đưa **dữ liệu có hình dạng thực tế** vào cùng lượt đánh giá một lệnh của gói 5.5, và viết **hướng dẫn chạy từng bước** cho người dùng:
- **RP-01, RP-02:** replay các booking SmashCourt (khoảng 215 bản ghi, xuất bằng truy vấn chỉ đọc, ẩn danh) qua TADSS; so sánh chính sách cọc 100 % tĩnh (`STATIC_100`) với chính sách v2 (`TADSS_V2`). Kết quả là bộ dữ liệu **D4**.
- **PT-01, PT-02:** adapter **Hotel Booking Demand** ánh xạ ≥ 10.000 đơn đặt phòng khách sạn thành request v2 rồi gửi qua TADSS **mà không sửa mã runtime/SDK**. Kết quả là bộ dữ liệu **D5**, và KPI **SC7** chuyển từ `NOT_APPLICABLE` thành tính được.
- Cả hai đổ vào **cùng Evidence Pack và `report.html`** do evaluator sinh ra (D-12). Không có lượt chạy hay script rời.
- **Hướng dẫn chạy** `docs/guides/EVALUATION_RUN_GUIDE.md` (yêu cầu của người dùng ngày 09/10/2026): chuẩn bị máy, lệnh chạy, mỗi bước làm gì, đọc kết quả, chạy lại một bước, chạy test thủ công, xử lý sự cố, dọn Docker.

**Architecture:** Gói **không đụng `src/`**. Toàn bộ thay đổi nằm ngoài mã runtime:
- **`tools/evidence/` — biến đổi dữ liệu thuần, không I/O mạng:**
  - Đọc nguồn: CSV SmashCourt đã xuất, CSV khách sạn đã tải.
  - Ánh xạ sang request v2.
  - Dời thời gian.
  - Kiểm request/response theo JSON schema v2.
  - Dựng D4/D5.
  - Phân tích mô tả RP-02/PT-02.
  - Kết luận SC7.
  - Mục báo cáo.

  Mỗi phần là hàm thuần, kiểm bằng pytest với fixture nhỏ.
- **`tools/evaluation/` — điều phối I/O:**
  - Bước mới **(e) dữ liệu thực tế**: tải và kiểm SHA-256 dataset, gửi request song song có giới hạn, retry đúng khóa idempotency, ghi file thô.
  - Bước Evidence Pack lùi thành **(f)**.
- **Stack đánh giá:**
  - Thêm một instance TADSS **`eval-tadss-portability`**, cùng image `tadss-eval-runtime:local`, cấu hình riêng cho miền khách sạn bằng biến môi trường, database riêng trên `eval-postgres`.
  - Thêm volume cache dataset và mount chỉ đọc thư mục dữ liệu riêng tư của người dùng.
- **SQL xuất dữ liệu SmashCourt:** file chỉ đọc trong repo. **Người dùng tự chạy** bằng thông tin kết nối của mình; agent không đọc secret và không kết nối Neon.

**Tech Stack:**
- **Python và công cụ:**
  - Python 3.11 (image evaluator sẵn có).
  - `jsonschema` 4.26.0 (đã ghim trong `tools/evaluation/requirements.txt`; thêm vào `tools/evidence/requirements.txt` vì `tools/evidence` dùng độc lập).
  - Jinja2 3.1.6.
  - Thư viện chuẩn (`csv`, `concurrent.futures`, `hashlib`, `zoneinfo`).
- **Không thêm** pandas, plotly, Jupyter (xem Q-10).
- **Hạ tầng như 5.5**, không thêm image mới:
  - `postgres:16.15-alpine`, `redis:7.4-alpine`, `grafana/otel-lgtm:0.34.0`, nginx, Toxiproxy;
  - image runtime `tadss-eval-runtime:local`, evaluator `tadss-evaluator:local`.
- **Dataset ngoài:** Hotel Booking Demand (Antonio, de Almeida & Nunes, 2019, *Data in Brief* 22:41–49).
  - Bản CSV 119.390 dòng, hai khách sạn ở Bồ Đào Nha.
  - URL tải và SHA-256 chốt ở Task 1.
  - Giấy phép do người dùng xác nhận (xem "Chuẩn bị").

**Spec:** [`PHASE5_DESIGN_SPEC.md`](PHASE5_DESIGN_SPEC.md):
- D-12, D-13, P-01.
- Mục 2 (V-7: replay ở quá khứ làm approval window luôn rỗng).
- Mục 3 (dòng 5.6), **Mục 4.6** (thiết kế chính), Mục 8, 9.

**Nguồn sở hữu:**
- [`EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](../docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md): Mục 4.9 (RP), 4.10 (PT), 5 (SC7), 8 (giới hạn: nhãn `NO_SHOW` của môi trường phát triển).
- [`DATASET_SCHEMA_SPECIFICATION.md`](../docs/research/DATASET_SCHEMA_SPECIFICATION.md): D4, D5, Mục 4 (ẩn danh hóa), Mục 5 (EV-1…EV-5).
- [`INTEGRATION_SPEC.md`](../docs/contracts/INTEGRATION_SPEC.md): §1.1 (approval window), §3.3 (request v2), §4.2 (ánh xạ booking → request).
- [`docs/database/schema_smashcourt.sql`](../docs/database/schema_smashcourt.sql): bảng `bookings`, `booking_courts`, `booking_price_items`, `courts`, `invoices`, `customer_loyalty`, `loyalty_tiers`, `users`.

**Gói trước:**
- [`PHASE5_PLAN_5.5_OBSERVABILITY_EVALUATION.md`](PHASE5_PLAN_5.5_OBSERVABILITY_EVALUATION.md), mục "Ghi chú cho gói 5.6".
- [`PHASE5_REPORT_5.5.md`](PHASE5_REPORT_5.5.md), Mục 8 (gồm yêu cầu hướng dẫn chạy).

> **Cách đọc plan này:**
>
> - Plan mô tả **yêu cầu kỹ thuật**: trách nhiệm từng thành phần, vị trí file, giao diện (tên hàm, đầu vào, đầu ra), quy tắc ánh xạ, bảng test case và tiêu chí nghiệm thu. Plan **không** chứa mã hiện thực.
> - Mục **Interfaces** là ràng buộc, vì task sau dựa vào đúng tên và dạng dữ liệu đó.
> - Đường dẫn viết từ gốc repo `TADSS-Runtime/`.
> - Gói **không đổi**: wire (OpenAPI, JSON schema, SDK, baseline public API), schema DB, mã lỗi, hành vi quyết định, mọi file dưới `src/`.
> - Gói **có đổi**:
>   - compose (một service, một volume, một mount);
>   - ký hiệu bước của evaluator (`f` mới);
>   - `run.sh` (tham số chọn bước);
>   - tài liệu.
>
>   Mọi thay đổi như vậy nằm ở bảng "Quyết định thiết kế" và Phụ lục B. Người dùng duyệt hai bảng đó trước Task 1.
> - **Lịch:** W08. Phải xong gói trước **thứ Hai 12/10/2026** để còn 13–14/10 cho gói 5.7 (lượt đầy đủ khoảng 3 giờ và đóng băng). Ước lượng khoảng 3 ngày công. Không có điểm dừng giữa gói; commit **một lần** ở cuối gói.

---

## Global Constraints

**Build và test sau mỗi task:**
- Task có mã Python: `pytest` của `tools/evaluation` và `tools/evidence` xanh.
- Task đụng compose hoặc test .NET:
  - `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror` → **0 warning, 0 error**;
  - unit test và integration test xanh (Docker bật).

**Số lượng test:**
- Test hiện có không được mất. Đầu gói (commit `3f55866`): **1871 unit / 238 IT**, **pytest 175** (`tools/evaluation` 135, `tools/evidence` 40).
- Mỗi task ghi số test trước và sau.

**Không đụng mã runtime (điều kiện của SC7):**
- `git diff 3f55866 -- src/ docs/contracts/` phải **rỗng** ở cuối gói.
- Cấm sửa: `src/**`, `docs/contracts/**`, `src/TADSS.Infrastructure/Persistence/Migrations/**`.
- Nếu adapter không chạy được mà không sửa mã ⇒ **dừng và hỏi**. Không được "sửa nhẹ cho qua", vì như vậy PT-01 mất ý nghĩa.

**Database:**
- Không migration mới, không `dotnet ef database update`, không kết nối Neon.
- Agent **không chạy** truy vấn xuất SmashCourt: chỉ viết file SQL và kiểm nó trên container PostgreSQL dùng một lần nạp `schema_smashcourt.sql` cộng dữ liệu giả.
- Instance `eval-tadss-portability` tự migrate database dùng một lần **của chính nó**.

**Dữ liệu cá nhân và bí mật:**
- CSV SmashCourt thô **không bao giờ** vào repo hay Evidence Pack. Nó nằm ở thư mục gitignore `data/private/`, mount **chỉ đọc**.
- Truy vấn xuất **không chọn** cột tên, số điện thoại, email, ghi chú, mã booking, token hủy.
- Định danh (UUID) được băm HMAC-SHA256 bằng khóa ngẫu nhiên của lượt chạy (`Anonymizer` sẵn có) **trước khi** ghi bất kỳ file nào vào `evidence/`.
- EV-4 phải đạt.
- Dataset khách sạn **không commit** vào repo cho tới khi người dùng xác nhận giấy phép. Kể cả sau khi xác nhận, Evidence Pack chỉ chứa D5 (cột đã chọn), không chứa file gốc.

**Cấm nới kiểm tra:**
- Không `Skip`, không nới assertion, không `# noqa`/`# type: ignore` để test xanh.
- Không lọc bỏ bản ghi lỗi contract khỏi phép đếm. Bản ghi bị **adapter loại trước khi gửi** chỉ hợp lệ khi lý do nằm trong danh sách quy tắc lọc đã duyệt (Q-08), và được đếm, báo cáo riêng.

**Không đổi phạm vi bước khác:** các bước (a)…(d) giữ nguyên hành vi và thời lượng; SCALE-2 vẫn chỉ xét `tadss-1`/`tadss-2`.

**Git:**
- Không commit, push hay đổi branch. Gợi ý **một** commit message cho cả gói ở Task 7.
- Không stage `spec/PHASE5_*.md`.

---

## Quyết định thiết kế của gói

| # | Quyết định | Lý do / phương án đã loại |
| :--- | :--- | :--- |
| **Q-01** | **Thêm bước (e) "dữ liệu thực tế"** (RP + PT) ngay trước Evidence Pack; Evidence Pack đổi từ (e) thành **(f)**. `EVAL_STEPS` nhận `a…f`. | Evidence Pack phải luôn là bước cuối. Thứ tự chữ cái trùng thứ tự chạy thì dễ hiểu nhất cho người đọc guide. **Đã loại:** (1) nhét RP/PT vào bước Evidence Pack, vì chạy lại phần dựng báo cáo sẽ phải gửi lại cả trăm nghìn request; (2) đặt bước `f` chạy trước `e`, vì gây nhầm. |
| **Q-02** | **PT chạy trên instance riêng `eval-tadss-portability`**, cùng image runtime:<br>• database riêng `tadss_portability` trên `eval-postgres` (kết nối thẳng, không qua Toxiproxy);<br>• Redis thẳng `eval-redis` với `InstanceName` riêng;<br>• không tiêm lỗi;<br>• telemetry `ServiceInstanceId = tadss-pt`.<br>Cấu hình miền khách sạn **chỉ bằng biến môi trường** của service (xem Q-08). | Lưu trú khách sạn dài ≥ 22 giờ, vượt `Tadss:Validation:MaxItemMinutes = 720`. `HighValueThreshold` đang tính bằng VND. Đổi cấu hình của `eval-tadss-1/2` sẽ làm sai LT/SCALE (request VND sẽ thành `NEED_APPROVAL`). Database riêng giữ D1 của lượt chính sạch. Đúng tinh thần PT-01: "cấu hình chạy riêng, không sửa mã" (spec 4.6). |
| **Q-03** | **RP gửi qua `eval-proxy`** (cấu hình chuẩn của stack, định tuyến chuẩn), ghi vào database chính.<br>• Khóa idempotency: `replay:{runId}:{bookingHash}`.<br>• Response RP được nối vào nguồn kiểm **EV-2/EV-3** cùng D2.<br>• D1 chứa luôn bản ghi RP, phân biệt bằng `correlation_id`/khóa. | RP-01 yêu cầu "cấu hình định tuyến chuẩn". EV-3 đếm theo chiều response → audit nên bản ghi RP không làm lệch SC3. |
| **Q-04** | **Dời thời gian (sửa V-7):**<br>• Mọi mốc của một bản ghi (`requestedAt`, `startTime`, `endTime`, `memberSince`) dời cùng một khoảng **k × 7 ngày**.<br>• `k` là số **nhỏ nhất** sao cho `requestedAt` sau khi dời **≥ thời điểm gửi**, nên độ lệch δ ∈ [0; 7 ngày).<br>• Giữ thứ trong tuần, giờ địa phương và lead time danh nghĩa.<br>• D4/D5 ghi `shift_days` và `lead_time_minutes` gốc. | Runtime tính approval window bằng giờ máy chủ, không bằng `requestedAt`. Với δ ≥ 0, khoảng tới giờ chơi thực tế ≥ lead time gốc, nên không bao giờ biến booking có đủ thời gian duyệt thành "không kịp duyệt".<br>**Thiên lệch còn lại:** booking gốc có lead time ≤ `MinLeadMinutes` (120 phút) có thể được duyệt trong khi thực tế không. Những bản ghi này gắn cờ `window_bias = true`. Phân tích RP báo cả kết quả runtime lẫn **kết quả hiệu chỉnh offline** (áp lại quy tắc §1.1 bằng lead time gốc trên `strategy_status` của D1).<br>**Đã loại:** δ ∈ (−7; 0], vì giờ chơi có thể rơi vào quá khứ. |
| **Q-05** | **Dữ liệu SmashCourt:**<br>• Người dùng chạy `tools/evidence/sql/export_smashcourt_bookings.sql` bằng `psql`, ghi `data/private/smashcourt_bookings.csv` (thư mục gitignore).<br>• Compose mount `./data/private` chỉ đọc vào evaluator.<br>• Ẩn danh **trong lượt chạy** bằng khóa ngẫu nhiên của lượt.<br>• Thiếu file ⇒ phần RP `skipped` kèm lý do; RP-01/02 ghi `INSUFFICIENT_DATA` ở độ phủ danh mục; bước (e) **không** thất bại. | Hội đồng chạy lại được mà không cần dữ liệu SmashCourt. Dữ liệu thô không rời máy người dùng. Băm theo lượt khớp quy tắc "cùng định danh cùng mã băm trong một Evidence Pack". |
| **Q-06** | **Dataset khách sạn:**<br>• Evaluator tải theo URL + SHA-256 ghim vào volume **`eval-datasets`** (cache giữa các lượt; `--clean` xóa).<br>• Sai SHA-256 hoặc không tải được ⇒ phần PT `error` kèm lý do, SC7 `INSUFFICIENT_DATA`; bước (e) báo `failed` khi SHA sai, `skipped` khi mất mạng.<br>• Không commit file gốc. | Tái lập chính xác (băm ghim) mà không phụ thuộc giấy phép phân phối lại. Mất mạng không được làm hỏng cả lượt đánh giá. |
| **Q-07** | **Cỡ mẫu PT:**<br>• bản nhanh: **10.000** dòng hợp lệ, lấy mẫu tất định (seed cố định ghi trong manifest);<br>• bản đầy đủ: **mọi** dòng hợp lệ (khoảng 118 nghìn).<br>RP luôn gửi mọi booking. | Cả hai chế độ đều đạt ngưỡng SC7 (≥ 10.000). Bản nhanh chỉ thêm khoảng 1–2 phút. |
| **Q-08** | **Ánh xạ khách sạn → request v2.** Theo spec 4.6, bổ sung các điểm spec chưa nói (bảng chi tiết ở Task 3). Cấu hình của `eval-tadss-portability`:<br>• `Tadss:Validation:MaxItemMinutes` đủ phủ lưu trú dài nhất của dataset (chốt ở Task 1 từ dữ liệu);<br>• `Tadss:Strategies:Default:HighValueThreshold` tính bằng EUR (chốt ở Task 1);<br>• các tỷ lệ cọc giữ mặc định.<br>**Quy tắc lọc trước khi gửi:** số đêm = 0; `adr` < 0; ngày đến không hợp lệ; lưu trú vượt `MaxItemMinutes`. Mỗi lý do được đếm. | Số đêm 0 không tạo được item có `endTime > startTime`. `adr` âm vi phạm `price ≥ 0`. Ngưỡng tiền là **tham số vận hành của miền**, không phải mã. Ngưỡng EUR chọn **một lần**, trước lượt chạy, từ phân bố giá; không chỉnh theo kết quả. |
| **Q-09** | **Định nghĩa "lỗi contract"** (RP-01, PT-01, SC7). Một bản ghi được gửi là lỗi contract khi:<br>(1) request do adapter dựng **không qua** `decision-request.schema.json`, tức lỗi của adapter (bị bắt **trước khi gửi**, vẫn tính lỗi); hoặc<br>(2) response cuối cùng không phải 200; hoặc<br>(3) body 200 không qua `decision-result.schema.json`, hoặc vi phạm quy tắc §3.3 (`deposit` có khi và chỉ khi `APPROVED`; `amount = round(totalPrice × rate)`).<br>**Retry:** 503/504 được thử lại tối đa 2 lần **cùng khóa idempotency**, chờ theo `Retry-After` (giới hạn trần 5 giây). Đây đúng là hành vi BE phải làm ở Phase 6. | Một định nghĩa duy nhất cho RP và PT. 503 thoáng qua trên máy lab không phải lỗi contract nếu retry thành công, nhưng số retry được báo cáo. |
| **Q-10** | **Không làm notebook** `notebooks/evidence_report.ipynb` (spec 4.6 liệt kê). Thay bằng lệnh dựng lại báo cáo từ một Evidence Pack có sẵn: module CLI của `tools/evidence`, dùng **cùng** bộ sinh báo cáo. | Notebook kéo theo pandas, plotly và Jupyter (nặng, thêm một bộ sinh biểu đồ thứ hai) trong khi bộ sinh Jinja2 + SVG của 5.5 đã tự chứa. Khóa luận cần bảng hoặc biểu đồ thì lấy từ CSV D4/D5 trong pack. **Cần người dùng duyệt** vì lệch spec. |
| **Q-11** | **Kiểm "0 thay đổi mã runtime/SDK" của SC7 ngay trong lượt chạy:**<br>• Evaluator đọc git của `/repo` (chỉ đọc).<br>• Mọi commit có chạm file của adapter khách sạn (`tools/evidence/hotel_adapter.py` và cấu hình service PT) **không** được chạm `src/TADSS.Runtime.Api/**` hay `src/Shared.DecisionContractSDK/**`.<br>• Working tree không có thay đổi chưa commit trong hai thư mục đó.<br>• Kết quả ghi rõ commit đã xét. | Bám đúng tiêu chí PT-01 ("`git diff` trước/sau khi thêm adapter rỗng"). Không phụ thuộc một commit gốc cố định, nên vẫn đúng khi các gói sau sửa `src/` vì lý do khác.<br>Khi adapter **chưa commit** (lượt chạy lúc đang phát triển), SC7 ghi phần kiểm mã là "chưa xét được — adapter chưa commit" và kết luận `INSUFFICIENT_DATA`. |
| **Q-12** | **Kênh và phạm vi RP:**<br>• Gửi **mọi** booking đã xuất, `channel` lấy từ `bookings.source` (`ONLINE`/`WALK_IN`, contract cho phép cả hai).<br>• Bảng RP tách theo kênh; RP-02 nêu số liệu chính trên tập `ONLINE` (đúng phạm vi BE sẽ gọi TADSS, INTEGRATION_SPEC §4.2) kèm số liệu toàn tập.<br>• Không bỏ `demand` (trường tùy chọn); `tier` là hạng **tại thời điểm xuất** (không có lịch sử hạng). Cả hai ghi vào giới hạn. | Giữ trọn tập dữ liệu (yêu cầu "215 booking") mà vẫn so sánh đúng phạm vi tích hợp thật. Tính `demand` lịch sử cần dựng lại lịch sân theo thời điểm, chi phí cao, giá trị thấp với 215 bản ghi. |
| **Q-13** | **Hướng dẫn chạy** đặt ở `docs/guides/EVALUATION_RUN_GUIDE.md` (thư mục mới). README chỉ giữ lệnh ngắn và liên kết tới guide. Guide là **nơi sở hữu duy nhất** của quy trình từng bước. | Tránh hai nơi mô tả cùng quy trình rồi lệch nhau (nguyên tắc DOC-1 của 5.7). |
| **Q-14** | `run.sh` nhận thêm `--steps <danh sách>` (ví dụ `--steps e,f`), chuyển thành `EVAL_STEPS`. Biến môi trường vẫn dùng được. | Người dùng không phải nhớ cú pháp biến môi trường khi chạy lại một bước. |

---

## Chuẩn bị (người dùng thực hiện)

| # | Việc | Khi nào | Ghi chú |
| :--- | :--- | :--- | :--- |
| U-1 | Duyệt bảng "Quyết định thiết kế", đặc biệt Q-01, Q-02, Q-10, Q-12, và Phụ lục B | Trước Task 1 | |
| U-2 | **Xác nhận giấy phép** dataset Hotel Booking Demand | Trước Task 1 | Bài báo gốc (DOI 10.1016/j.dib.2018.11.126) được công bố dạng truy cập mở, cần đọc điều khoản dữ liệu đi kèm. Cho phép phân phối lại ⇒ có thể commit D5 tổng hợp vào Evidence Pack cuối (5.7). Không cho phép ⇒ chỉ commit script tải và kết quả tổng hợp. Mặc định của plan: **không commit file gốc** trong mọi trường hợp. |
| U-3 | **Chạy truy vấn xuất** SmashCourt | Sau Task 2 (agent báo file SQL đã kiểm xong) | Lệnh mẫu có trong guide và đầu file SQL. Kết nối Neon bằng thông tin của bạn qua `psql`; kết quả ghi vào `TADSS-Runtime/data/private/smashcourt_bookings.csv`. Agent không đọc chuỗi kết nối. Gặp lỗi cột hoặc bảng (schema Neon lệch `schema_smashcourt.sql`) ⇒ gửi thông báo lỗi cho agent, **không** gửi dữ liệu. |
| U-4 | Docker Desktop bật, RAM VM ≥ 8 GB, tắt chế độ ngủ của Windows khi chạy lượt đánh giá | Task 4, 7 | Lượt nhanh khoảng 25 phút. Máy ngủ giữa chừng làm sai số đo (đã gặp ở 5.5). |

---

## Cấu trúc đích

```text
TADSS-Runtime/
├── data/private/                      # MỚI, gitignore: CSV SmashCourt thô do người dùng xuất (Q-05)
├── docs/guides/EVALUATION_RUN_GUIDE.md   # MỚI (Task 6)
├── docker-compose.yml                 # + eval-tadss-portability, volume eval-datasets, mount data/private (Task 4)
├── tools/
│   ├── evaluation/
│   │   ├── run.sh                     # + --steps (Task 6)
│   │   └── evaluator/
│   │       ├── run_context.py         # ALL_STEPS a…f, endpoint PT, đường dẫn dữ liệu
│   │       ├── __main__.py            # STEPS: e = dataset_step, f = evidence_step
│   │       ├── clients/download.py    # MỚI: tải file có kiểm SHA-256 vào cache
│   │       └── steps/
│   │           ├── dataset_step.py    # MỚI: bước (e) — RP rồi PT
│   │           ├── dataset_sender.py  # MỚI: gửi song song có giới hạn + retry cùng khóa
│   │           └── evidence_step.py   # thành bước (f); thêm D4/D5, SC7, EV-2/3 cho nguồn mới
│   └── evidence/
│       ├── requirements.txt           # + jsonschema
│       ├── sql/export_smashcourt_bookings.sql   # MỚI (Task 2)
│       ├── sql/verify-export.sh       # MỚI: kiểm SQL trên PostgreSQL dùng một lần
│       ├── time_shift.py              # MỚI: dời k × 7 ngày (Q-04), dùng chung RP/PT
│       ├── contract_check.py          # MỚI: kiểm request/response theo schema v2 + quy tắc §3.3 (Q-09)
│       ├── replay.py                  # MỚI: CSV SmashCourt → request v2, D4 (RP-01)
│       ├── hotel_adapter.py           # MỚI: CSV khách sạn → request v2, D5 (PT-01)
│       ├── dataset_analysis.py        # MỚI: bảng mô tả RP-01/RP-02/PT-02
│       ├── kpi.py                     # + sc7
│       ├── evidence_pack.py           # EV-2/EV-3 nhận nhiều cặp nguồn
│       ├── report/                    # + mục "Dữ liệu thực tế"; CLI dựng lại báo cáo (Q-10)
│       └── tests/ + fixtures/         # pytest mới
└── tests/TADSS.IntegrationTests/Infrastructure/ComposeFixtureAlignmentTests.cs   # danh sách service đánh giá
```

Tên file Python theo quy ước sẵn có của `tools/evidence` và `tools/evaluation` (snake_case, một trách nhiệm mỗi module, docstring tiếng Việt ở đầu file).

---

### Task 1: Spike dữ liệu và chốt tham số

**Files:**
- Không tạo mã sản phẩm. Kết quả ghi vào bảng spike của báo cáo `spec/PHASE5_REPORT_5.6.md` (không stage).
- Thêm `data/private/` vào `.gitignore`.

**Yêu cầu:**
1. **S-1 (dataset):**
   - Tải bản CSV Hotel Booking Demand từ nguồn chọn được (ưu tiên bản gốc kèm bài báo; dự phòng bản TidyTuesday 2020-02-11).
   - Ghi URL, kích thước, **SHA-256**, số dòng, danh sách cột.
   - Đối chiếu tên cột với bảng ánh xạ Task 3. Hai bản có cột khác nhau thì chốt **một** bản.
2. **S-2 (phân bố):**
   - Đếm theo từng quy tắc lọc Q-08: số đêm = 0, `adr` < 0, ngày lỗi.
   - Phân bố số đêm (max, p99) ⇒ chốt `MaxItemMinutes` của `eval-tadss-portability`.
   - Phân bố `adr × số đêm` (p50, p90, p95) ⇒ đề xuất `HighValueThreshold` (EUR), mặc định đề xuất là **p90** (khoảng 10 % đơn giá trị cao cần duyệt, tương tự tinh thần ngưỡng 2.000.000 VND của SmashCourt).
   - Ghi bảng vào báo cáo. **Người dùng duyệt hai giá trị** trước Task 4.
3. **S-3 (contract):**
   - Dựng tay 3 request khách sạn tiêu biểu: 1 đêm, 14 đêm, giá 0.
   - Kiểm bằng `decision-request.schema.json`, rồi gửi vào một instance runtime chạy với cấu hình đề xuất (stack đánh giá bật tạm, hoặc `dotnet run` môi trường `Testing`).
   - Phải được 200 mà **không sửa mã**. Lỗi ⇒ dừng và hỏi.
   - Kiểm riêng: giá có phần thập phân (EUR 2 chữ số) có qua schema và quy tắc làm tròn `amount` không.
4. **S-4 (schema SmashCourt):**
   - Nạp `docs/database/schema_smashcourt.sql` vào `postgres:16.15-alpine` dùng một lần. File có lệnh `OWNER TO neondb_owner`, nên tạo role đó trước hoặc lọc lệnh.
   - Xác nhận đủ cột Task 2 cần, và các giá trị enum `booking_status`, `booking_source`, `cancel_source_enum`.
5. **S-5 (thời lượng):** đo tốc độ gửi tuần tự và song song (4, 8, 16 luồng) 1.000 request vào stack đánh giá ⇒ chốt mức song song mặc định và ước lượng thời gian bước (e) cho hai chế độ.

**Nghiệm thu:**
- Bảng S-1…S-5 trong báo cáo.
- `MaxItemMinutes`, `HighValueThreshold` (EUR), URL + SHA-256 và mức song song đã chốt và được người dùng duyệt.
- Không có thay đổi nào dưới `src/`.

---

### Task 2: Truy vấn xuất SmashCourt và replay (RP-01 phần dữ liệu)

**Files:**
- Create:
  - `tools/evidence/sql/export_smashcourt_bookings.sql`, `tools/evidence/sql/verify-export.sh`.
  - `tools/evidence/time_shift.py`, `tools/evidence/contract_check.py`, `tools/evidence/replay.py`.
  - Test: `tools/evidence/tests/test_time_shift.py`, `test_contract_check.py`, `test_replay_mapping.py`.
  - Fixture: `tools/evidence/tests/fixtures/smashcourt_seed.sql`, `smashcourt_bookings_sample.csv`, `smashcourt_expected_export.csv`.
- Modify: `tools/evidence/requirements.txt` (+ `jsonschema` cùng phiên bản với `tools/evaluation`).

**Interfaces:**

| Thành phần | Đầu vào | Đầu ra / hành vi |
| :--- | :--- | :--- |
| File SQL xuất | Database SmashCourt (chỉ đọc) | CSV có header, **một dòng một booking**, cột ở bảng dưới. Chạy trong giao dịch `READ ONLY`. Không có cột cá nhân. |
| `time_shift.weeks_to_shift(requested_at, now)` | Hai mốc có múi giờ | Số tuần `k ≥ 0` nhỏ nhất theo Q-04 |
| `time_shift.shift(moment, weeks)` | Mốc có múi giờ, số tuần | Mốc dời `weeks × 7` ngày, giữ giờ địa phương. Dời bằng ngày lịch theo múi giờ của mốc, nên đúng cả khi có DST (dùng chung cho `Europe/Lisbon`). |
| `contract_check.request_errors(body)` | Request đã dựng | Danh sách lỗi theo `decision-request.schema.json` (rỗng = hợp lệ) |
| `contract_check.response_errors(request, status, body)` | Request, status HTTP, body | Danh sách lỗi theo Q-09: status ≠ 200, schema `decision-result`, quy tắc `deposit`/`amount` |
| `replay.load_bookings(path)` | Đường dẫn CSV xuất | Danh sách bản ghi đã kiểm kiểu; dòng sai định dạng ⇒ lỗi kèm số dòng, không bỏ qua im lặng |
| `replay.build_requests(bookings, anonymizer, run_id, now)` | Bản ghi, `Anonymizer` của lượt, `run_id`, giờ gửi | Danh sách `{idempotency_key, body, meta}`. `meta` mang dữ liệu cho D4 (hash, `shift_days`, `lead_time_minutes`, `window_bias`, kênh, nhãn `dev_final_status`). |
| `replay.d4_rows(prepared, results, d1_index)` | Request đã gửi, kết quả gửi, D1 theo `decision_id` | Dòng D4 dạng dài: mỗi booking hai dòng `STATIC_100` và `TADSS_V2` (cột ở DATASET_SCHEMA D4 cộng các cột ở bước dưới) |

**Cột CSV xuất** (một dòng một booking; mọi định danh là UUID gốc, chỉ được băm ở evaluator):

| Cột | Nguồn / quy tắc |
| :--- | :--- |
| `booking_id`, `branch_id`, `customer_id` (rỗng nếu khách vãng lai) | `bookings` |
| `source`, `status` | `bookings.source`, `bookings.status` (nhãn cuối của môi trường phát triển) |
| `created_at` | `bookings.created_at` (timestamptz) ⇒ `requestedAt` |
| `items` | Mảng JSON các sân **đang hoạt động** (`booking_courts.is_active`). Mỗi phần tử gồm: `court_id`, `court_type_id` (qua `courts`), `date`, `start_time`, `end_time`, `price` (tổng `booking_price_items.unit_price` của sân đó) |
| `final_total` | `invoices.final_total` (INTEGRATION_SPEC §4.2) |
| `tier_rank` | Thứ hạng của hạng hiện tại theo `loyalty_tiers.min_points` tăng dần (1…n). Rỗng nếu chưa có hạng. Ánh xạ sang `STANDARD…DIAMOND` ở Python (rank > 5 ⇒ `DIAMOND`). |
| `member_since` | `users.created_at` (rỗng nếu khách vãng lai) |
| `history_completed`, `history_no_show`, `history_cancelled`, `history_late_cancelled`, `history_payment_expired` | Đếm các booking **đã kết thúc trước `created_at`** của cùng khách. Khách có tài khoản ghép theo `customer_id`; khách vãng lai ghép theo số điện thoại đã chuẩn hóa, **chỉ dùng bên trong truy vấn**, không xuất. Quy tắc phân loại theo INTEGRATION_SPEC §3.3: hủy do nhân viên không tính. Bảng ánh xạ trạng thái và `cancel_source` → loại lịch sử ghi trong comment đầu file SQL và trong báo cáo. |

**Quy tắc ánh xạ request** (INTEGRATION_SPEC §4.2):
- `reservationId`, `customerId`, `branchId`, `resourceId`, `resourceTypeId` là **UUID dựng từ mã băm HMAC** của định danh tương ứng: lấy 16 byte đầu, đặt bit version/variant hợp lệ. Cùng định danh trong lượt cho cùng UUID.
- `branchTimeZone = Asia/Ho_Chi_Minh`; `currency = VND`; `channel = source`.
- `items[].startTime/endTime` dựng từ `date + start_time/end_time` theo giờ chi nhánh, rồi dời theo Q-04.
- `totalPrice = final_total`; `isGuest` khi `customer_id` rỗng (khi đó `customerId = null`, `tier = STANDARD`, `memberSince = null`).
- `metadata` chỉ ghi nguồn `replay`, **không** đưa giá trị định danh nào.
- `demand` bỏ trống (Q-12).

**Yêu cầu:**
1. `verify-export.sh`:
   - dựng `postgres:16.15-alpine` dùng một lần (`--rm`);
   - nạp schema rồi `smashcourt_seed.sql`;
   - chạy file SQL xuất;
   - so CSV với `smashcourt_expected_export.csv`;
   - **luôn dọn container**.

   Seed phủ các trường hợp: khách có tài khoản và vãng lai, nhiều sân, sân bị hủy (`is_active = false`), các loại lịch sử (hoàn tất, không đến, hủy được hoàn, hủy muộn, hết hạn thanh toán, hủy do nhân viên — không tính), khách chưa có hạng.
2. Ở đầu file SQL, comment ghi:
   - mục đích;
   - lệnh `psql` mẫu, dùng biến môi trường của **người dùng**, không chứa thông tin kết nối;
   - cam kết không xuất cột cá nhân.
3. `replay` từ chối booking không có hóa đơn, hoặc không còn sân hoạt động: ghi lý do và đếm, không gửi.
4. Bản ghi có request không qua schema ⇒ tính **lỗi contract** (Q-09), không âm thầm loại.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| pytest `test_weeks_to_shift_Theory` | `requestedAt` trước giờ gửi 1 giờ / đúng giờ gửi / sau giờ gửi / trước 400 ngày | `k` nhỏ nhất thỏa δ ∈ [0; 7 ngày); `k = 0` khi đã ở tương lai | — |
| pytest `test_shift_keeps_weekday_and_local_time` | Mốc `Asia/Ho_Chi_Minh` và `Europe/Lisbon`, khoảng dời qua ngày đổi giờ mùa hè | Thứ trong tuần và giờ địa phương giữ nguyên; offset Lisbon đổi đúng | — |
| pytest `test_contract_check_Theory` | Response 200 hợp lệ; `APPROVED` thiếu `deposit`; `amount` sai làm tròn; 503; request thiếu `items` | Rỗng / có lỗi đúng loại | — |
| pytest `test_replay_maps_fixture` | CSV mẫu, khóa cố định, giờ gửi cố định | Request qua schema; UUID ổn định trong lượt và khác giữa hai khóa; khách vãng lai đúng quy tắc; tier rank 7 ⇒ `DIAMOND`; không chuỗi UUID gốc nào xuất hiện trong body | RP-01 |
| pytest `test_replay_window_bias_flag` | Booking có lead time 90 phút và 3 ngày | Cờ `window_bias` lần lượt bật / tắt | RP-01 |
| pytest `test_replay_rejects_incomplete_rows` | Không hóa đơn; không sân hoạt động; dòng sai kiểu | Đếm theo lý do; dòng sai kiểu ⇒ lỗi kèm số dòng | RP-01 |
| `verify-export.sh` | Schema + seed | CSV khớp kỳ vọng từng ô; container đã bị xóa | RP-01 |

**Nghiệm thu:**
- pytest xanh; `verify-export.sh` đạt.
- Báo người dùng bắt đầu **U-3**.

---

### Task 3: Adapter Hotel Booking Demand (PT-01 phần dữ liệu)

**Files:**
- Create:
  - `tools/evidence/hotel_adapter.py`.
  - Test: `tools/evidence/tests/test_hotel_adapter.py`.
  - Fixture: `tools/evidence/tests/fixtures/hotel_sample.csv`, khoảng 30 dòng tự soạn theo đúng cột của dataset (không chép dữ liệu gốc).

**Interfaces:**

| Thành phần | Đầu vào | Đầu ra / hành vi |
| :--- | :--- | :--- |
| Hằng nguồn | — | URL, SHA-256, số dòng kỳ vọng, nhãn giấy phép và trích dẫn (một chỗ duy nhất, Task 1 chốt) |
| `hotel_adapter.load_rows(path)` | CSV đã kiểm băm | Dòng đã kiểm kiểu kèm `source_row` (chỉ số dòng gốc) |
| `hotel_adapter.select(rows, mode, seed)` | Dòng, chế độ, seed | Lọc theo Q-08 (trả kèm bảng đếm theo lý do), rồi lấy mẫu theo Q-07. Cùng seed ⇒ cùng tập. |
| `hotel_adapter.build_requests(rows, anonymizer, run_id, now)` | Dòng đã chọn | `{idempotency_key, body, meta}`; khóa `portability:{runId}:{sourceRow}` |
| `hotel_adapter.d5_rows(prepared, results)` | Request, kết quả gửi | Dòng D5 (cột DATASET_SCHEMA D5 cộng các cột bổ sung ở Task 5) |

**Ánh xạ** (spec 4.6 + Q-08):

| Trường v2 | Quy tắc |
| :--- | :--- |
| `branchId` | UUID dựng từ băm của `hotel` (hai giá trị) |
| `branchTimeZone` | `Europe/Lisbon` (cả hai khách sạn ở Bồ Đào Nha) |
| `items` | **Một** item: ngày đến 14:00 → ngày đi 12:00 giờ Lisbon. Ngày đi = ngày đến + `stays_in_weekend_nights + stays_in_week_nights`. `resourceTypeId` dựng từ `reserved_room_type`; `resourceId` dựng từ cặp (`hotel`, `reserved_room_type`). `price = adr × số đêm`, làm tròn 2 chữ số. |
| `totalPrice`, `currency` | Bằng `price`; `EUR` |
| `requestedAt` | Ngày đến − `lead_time` ngày, lúc 10:00 Lisbon (giờ danh nghĩa, dataset không có giờ đặt) |
| Dời thời gian | `time_shift` theo Q-04 cho mọi mốc |
| `customer` | `isGuest = (is_repeated_guest = 0)`. Khách quay lại: `customerId` dựng từ băm của `source_row` (dataset không có định danh khách), `tier = STANDARD`, `memberSince = null`. `history.completedCount = previous_bookings_not_canceled`, `history.cancelledCount = previous_cancellations`, các số đếm khác = 0. |
| `channel` | `ONLINE` |
| `metadata` | `market_segment`, `distribution_channel`, `deposit_type`, `customer_type` (chuỗi, không ảnh hưởng quyết định) |
| Không dùng | `country`, `agent`, `company` và các cột nhãn (`is_canceled`, `reservation_status*`) — nhãn chỉ vào D5 để phân tích PT-02, **không** vào request |

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| pytest `test_hotel_mapping_fixture` | 30 dòng mẫu | Mọi request qua schema; item 14:00 → 12:00 giờ Lisbon đúng số đêm; `price` = `adr` × số đêm; nhãn không có trong body | PT-01 |
| pytest `test_hotel_filters_Theory` | 0 đêm; `adr` âm; tháng sai tên; lưu trú vượt ngưỡng | Bị loại đúng lý do, đếm đúng | PT-01 |
| pytest `test_hotel_sample_deterministic` | Cùng seed hai lần; seed khác; chế độ full | Cùng tập / khác tập / lấy hết | PT-01 |
| pytest `test_hotel_repeated_guest_history` | `is_repeated_guest` = 0 và 1 có lịch sử | `isGuest` và `history` đúng | PT-01 |

**Nghiệm thu:** pytest xanh; chạy adapter trên file dataset thật (đã tải ở Task 1) cho ≥ 10.000 request qua schema, 0 lỗi; ghi bảng đếm lọc vào báo cáo.

---

### Task 4: Stack đánh giá và bước (e) dữ liệu thực tế

**Files:**
- Create:
  - `tools/evaluation/evaluator/clients/download.py`;
  - `tools/evaluation/evaluator/steps/dataset_step.py`, `dataset_sender.py`;
  - test `tools/evaluation/tests/test_dataset_step.py`, `test_download.py`.
- Modify:
  - `docker-compose.yml`:
    - service `eval-tadss-portability`;
    - volume `eval-datasets`;
    - mount `./data/private:/datasets/private:ro` và `eval-datasets:/datasets/cache` vào evaluator;
    - biến `EVAL_PT_URL`, `EVAL_PT_PG_DSN`;
    - evaluator chờ service mới `service_healthy`.
  - `tools/evaluation/evaluator/run_context.py` (`ALL_STEPS` a…f, endpoint PT, đường dẫn dữ liệu, mức song song).
  - `tools/evaluation/evaluator/__main__.py` (STEPS mới).
  - `tools/evaluation/tests/test_run_context.py` (và test liên quan tới danh sách bước).
  - `tests/TADSS.IntegrationTests/Infrastructure/ComposeFixtureAlignmentTests.cs`: danh sách service đánh giá thêm `eval-tadss-portability`. Các luật NG-1/NG-5 giữ nguyên: service mới **không** mở cổng host, không mount secret.
  - `tools/evaluation/run.sh`: không đổi danh sách image (service mới dùng lại image sẵn có).

**Interfaces:**
- **Service `eval-tadss-portability`:**
  - Môi trường `Testing`. Chuỗi kết nối `eval-postgres`, database `tadss_portability`, tự migrate.
  - Redis `eval-redis`, `InstanceName` riêng. Khóa API đánh giá như hai instance kia.
  - Rate limit nâng như hai instance kia. Telemetry `tadss-pt`. Không tiêm lỗi.
  - Hai giá trị Task 1 (`MaxItemMinutes`, `HighValueThreshold` EUR) dưới dạng biến `Tadss__…`, kèm comment nguồn gốc (S-2).
  - Healthcheck như các instance khác.
- **`download.fetch(url, sha256, cache_dir)`:**
  - Có file đúng băm trong cache ⇒ dùng lại, không tải.
  - Chưa có ⇒ tải vào file tạm, kiểm băm, đổi tên nguyên tử.
  - Sai băm ⇒ xóa file tạm, ném lỗi có tên `DatasetIntegrityError`.
  - Mất mạng ⇒ lỗi riêng `DatasetUnavailableError`.
- **`dataset_sender.send_all(prepared, base_url, api_key, workers, retry_policy)`:**
  - Gửi song song có giới hạn `workers` (mặc định chốt ở S-5).
  - Mỗi request: header khóa API, `X-Idempotency-Key`, `X-Correlation-Id` mới.
  - Retry 503/504 theo Q-09.
  - Trả **theo đúng thứ tự đầu vào** kết quả gồm: status cuối, body, số lần thử, thời gian, `decision_id`, `correlation_id`.
  - Không ném khi một request lỗi; chỉ ném khi không kết nối được service (lỗi hạ tầng).
- **`dataset_step.run(context)`:**
  - Chạy **RP** rồi **PT**, mỗi phần độc lập.
  - Ghi:
    - `raw/replay/requests.jsonl` (mỗi dòng: khóa, status, số lần thử, `decision_id`, `correlation_id`, danh sách lỗi contract — **không** ghi body request vì đã có D4/D1);
    - `raw/replay/summary.json`;
    - `raw/portability/requests.jsonl`, `raw/portability/summary.json`;
    - `raw/portability/source.json` (URL, SHA-256, số dòng, bảng lọc, seed, cỡ mẫu, giấy phép).
  - Trạng thái bước:
    - `failed` nếu có lỗi contract ở phần nào hoặc SHA sai;
    - `skipped` khi cả hai phần không có dữ liệu;
    - còn lại `passed`.
  - Ghi chú nêu rõ phần nào bị bỏ và vì sao.

**Yêu cầu:**
1. RP gửi tới `eval-proxy`; PT gửi tới `eval-tadss-portability` (Q-02, Q-03).
2. Giờ gửi (`now`) lấy **một lần** ở đầu mỗi phần để mọi bản ghi cùng phần dời theo cùng mốc. Ghi `now` vào summary.
3. Bước (e) không đụng Toxiproxy hay file tiêm lỗi. Nếu bước (c) vừa chạy, chờ `/health/ready` của đích, dùng lại hàm chờ của SCALE-2.
4. Thời lượng bước (e) và số request mỗi giây ghi vào summary (O-24 của 5.5: số đo thật).

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| pytest `test_download_Theory` | Cache đúng băm; cache sai băm; tải mới đúng; tải mới sai; lỗi mạng | Dùng lại / tải lại / ghi đúng / `DatasetIntegrityError`, không để lại file / `DatasetUnavailableError` | PT-01 |
| pytest `test_sender_retries_same_key` | Server giả trả 503 (`Retry-After: 1`) rồi 200 | Hai lần thử, cùng khóa idempotency, kết quả 200, số lần thử = 2 | RP-01 |
| pytest `test_sender_preserves_order_and_isolates_errors` | 50 request, vài cái 400/timeout | Thứ tự giữ nguyên; lỗi từng request không dừng cả lô | RP-01 |
| pytest `test_dataset_step_statuses_Theory` | Đủ dữ liệu; thiếu CSV SmashCourt; mất mạng; sai SHA; một response sai schema | `passed` + ghi chú / `passed` với RP `skipped` / `passed` với PT `skipped` / `failed` / `failed` | RP-01, PT-01 |
| pytest `test_parse_steps_a_to_f` | `EVAL_STEPS=e,f`; `EVAL_STEPS=g`; rỗng | `(e, f)` / lỗi cấu hình / đủ sáu bước | — |
| IT `ComposeFixtureAlignmentTests` (sửa danh sách) | File compose | Có `eval-tadss-portability`, vẫn chỉ `eval-grafana` mở cổng host, không mount secret | NG-1, NG-5 |
| Smoke | `EVAL_STEPS=e` trên stack đang chạy, có CSV SmashCourt giả lập (fixture) trong `data/private/` | Mã thoát 0; đủ file `raw/replay/*`, `raw/portability/*`; PT ≥ 10.000 request, 0 lỗi contract | RP-01, PT-01 |

**Nghiệm thu:**
- pytest xanh; build 0/0; IT xanh.
- Smoke đạt, ghi thời lượng bước (e) cho bản nhanh.
- Sau smoke xóa CSV giả lập khỏi `data/private/`.

---

### Task 5: Phân tích RP-02/PT-02, SC7, Evidence Pack và báo cáo — bước (f)

**Files:**
- Create:
  - `tools/evidence/dataset_analysis.py`.
  - Test: `tools/evidence/tests/test_dataset_analysis.py`, `test_sc7_rules.py`.
  - Fixture: D4/D5/D1 mẫu nhỏ.
- Modify:
  - `tools/evidence/kpi.py`: SC7 rời khỏi danh sách ngoài phạm vi.
  - `tools/evidence/evidence_pack.py`: EV-2/EV-3 nhận danh sách cặp nguồn.
  - `tools/evidence/report/generator.py`, `template.html.j2`, `charts.py`: mục mới và CLI dựng lại báo cáo (Q-10).
  - `tools/evaluation/evaluator/steps/evidence_step.py`:
    - xuất D1 của database PT;
    - D4/D5;
    - kiểm mã cho SC7 (Q-11);
    - `testCases`/`catalogCoverage` thêm RP-01, RP-02, PT-01, PT-02;
    - manifest thêm `datasets`.

**Interfaces:**
- **File mới trong pack:**
  - `raw/d4/replay_smashcourt.csv`;
  - `raw/d5/portability_hotel.csv`;
  - `raw/d1/decision_audit_portability.csv` (cùng cột D1, xuất bằng `d1_export` sẵn có với DSN của database PT);
  - `raw/analysis/rp_summary.json`, `raw/analysis/pt_summary.json`.
- **Cột bổ sung so với DATASET_SCHEMA:**
  - D4: `decision_id`, `channel`, `lead_time_minutes`, `shift_days`, `window_bias`, `strategy_status`, `status_window_adjusted`, `contract_errors`.
  - D5: `decision_id`, `deposit_type`, `market_segment`, `shift_days`, `attempts`, `contract_errors`.
- **`dataset_analysis.rp_summary(d4_rows, calendar)`:**
  - Phân bố trạng thái theo kênh.
  - Mức cọc trung bình theo hạng và theo **khung giờ cao điểm / thấp điểm**: phân loại theo `Tadss:Calendar:PeakWindows` đọc từ `src/TADSS.Runtime.Api/appsettings.json`, một nguồn duy nhất.
  - So sánh `STATIC_100` với `TADSS_V2`: tỷ lệ cọc trung bình, số tiền cọc trung bình, tỷ lệ `NEED_APPROVAL`, tổng tiền cọc giữ được trên booking có nhãn `NO_SHOW`.
  - Số liệu chính trên tập `ONLINE` kèm toàn tập (Q-12).
  - Kết quả runtime và kết quả hiệu chỉnh theo `window_bias` (Q-04).
- **`dataset_analysis.pt_summary(d5_rows)`:**
  - Phân bố trạng thái và mức cọc.
  - Bảng chéo trạng thái quyết định × `is_canceled` (số đếm, tỷ lệ hủy mỗi nhóm).
  - Bảng chéo mức cọc TADSS × `deposit_type` gốc của khách sạn.
  - Chỉ số mô tả, **không** kiểm định hay kết luận nhân quả.
- **`kpi.sc7(pt_summary, code_check)`:**
  - `PASS` khi đủ ba điều: ≥ 10.000 bản ghi gửi; 0 lỗi contract (Q-09); kiểm mã Q-11 đạt.
  - `FAIL` khi có lỗi contract hoặc kiểm mã không đạt.
  - `INSUFFICIENT_DATA` khi PT bị bỏ, dưới 10.000 bản ghi, hoặc adapter chưa commit.
  - Mỗi kết luận kèm `reason` và đường dẫn file thô.
- **CLI dựng lại báo cáo:** một lệnh `python -m` của `tools/evidence` nhận đường dẫn một Evidence Pack, chạy lại EV-1…EV-5, rồi ghi lại `report.html`. Không đổi file thô; **tính lại manifest** nếu báo cáo đổi.

**Yêu cầu:**
1. **Mục báo cáo "Dữ liệu thực tế"** (tiếng Việt, tự chứa như 5.5):
   - **RP-01:** bảng phân bố, mức cọc theo hạng và khung giờ.
   - **RP-02:** bảng so sánh hai chính sách và biểu đồ cột SVG.
   - **PT-01:** bảng nguồn — URL, SHA-256, số dòng đọc, bị loại theo lý do, gửi, 200, lỗi contract, số retry, kiểm mã.
   - **PT-02:** hai bảng chéo và biểu đồ.
   - Mục nào không có dữ liệu thì ghi lý do.
2. **Mục "Giới hạn"** của báo cáo thêm:
   - nhãn `NO_SHOW`/`CANCELLED` của môi trường phát triển (methodology Mục 8);
   - thiên lệch dời thời gian (Q-04);
   - hạng tại thời điểm xuất, không có `demand` (Q-12);
   - giờ danh nghĩa 10:00/14:00/12:00 và không ghép được khách của dataset khách sạn;
   - ngưỡng EUR là tham số chọn trước.
3. **EV-4:**
   - D4/D5 không có cột cá nhân.
   - Quét chuỗi khẳng định không có UUID gốc nào của CSV SmashCourt trong pack: evaluator truyền danh sách UUID gốc của lượt vào phép quét, **không** ghi danh sách đó ra file.
4. **Bảng tóm tắt stdout** thêm dòng bước (e) và SC7.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| pytest `test_rp_summary_fixture` | D4 mẫu: 10 booking, hai kênh, có `NO_SHOW`, có `window_bias` | Số liệu khớp tính tay; `STATIC_100` luôn tỷ lệ 1,0; hiệu chỉnh đổi đúng các dòng bị gắn cờ | RP-01, RP-02 |
| pytest `test_pt_summary_crosstab` | D5 mẫu | Bảng chéo đúng; không có cột suy diễn | PT-02 |
| pytest `test_sc7_rules_Theory` | 9.999 / 10.000 bản ghi; 1 lỗi contract; kiểm mã đỏ; adapter chưa commit; PT bị bỏ | `INSUFFICIENT_DATA` / `PASS` / `FAIL` / `FAIL` / `INSUFFICIENT_DATA` / `INSUFFICIENT_DATA` | SC7 |
| pytest `test_code_check_Theory` (evaluator) | Lịch sử git giả: commit adapter chạm/không chạm `src/TADSS.Runtime.Api`; working tree bẩn ở SDK | Đạt / không đạt / không đạt, kèm commit đã xét | SC7 |
| pytest `test_ev2_ev3_multiple_sources` | Cặp (D2 + replay, D1) đạt và cặp (PT, D1-portability) lệch một bản | EV-3 không đạt, chỉ rõ nguồn | — |
| pytest `test_ev4_detects_raw_uuid` | D4 lỡ chứa một UUID gốc | EV-4 không đạt | — |
| pytest `test_report_dataset_sections` | Pack mẫu đủ dữ liệu; pack thiếu RP | Có mọi mục; mục thiếu ghi lý do; không URL `http(s)://` tải tài nguyên (nguồn dataset là **văn bản** trong bảng, không phải liên kết tải tài nguyên — test phân biệt hai loại) | — |
| pytest `test_report_cli_rebuilds` | Chạy CLI trên pack mẫu | `report.html` sinh lại, EV-1 vẫn đạt sau khi tính lại manifest | — |

**Nghiệm thu:**
- pytest xanh.
- Một lượt `EVAL_STEPS=e,f` trên stack đang chạy sinh pack có D4/D5, mục "Dữ liệu thực tế" và SC7 có kết luận.
- EV-1…EV-5 đạt.

---

### Task 6: Hướng dẫn chạy từng bước (yêu cầu của người dùng)

**Files:**
- Create: `docs/guides/EVALUATION_RUN_GUIDE.md`.
- Modify:
  - `tools/evaluation/run.sh`: `--steps` (Q-14), dòng hướng dẫn đầu file trỏ tới guide.
  - `README.md`: mục "Chế độ đánh giá một lệnh" rút gọn còn lệnh chính và liên kết guide (Q-13).

**Người đọc:**
- Chính người dùng và hội đồng.
- Người đọc biết dùng terminal nhưng **không** biết evaluator.
- Máy Windows + Git Bash + Docker Desktop.

**Yêu cầu về nội dung** (mỗi bước có lệnh copy chạy được và mô tả **thấy gì là đúng**):

| Mục | Nội dung |
| :--- | :--- |
| 1. Ba cách chạy TADSS | Bảng ngắn: `dotnet run` (viết mã) / `docker compose up -d --build` (vận hành, demo) / `run.sh` (đánh giá). Khi nào dùng cách nào. |
| 2. Chuẩn bị máy (một lần) | Docker Desktop, RAM VM ≥ 8 GB, ổ trống ≥ 15 GB; tắt chế độ ngủ; mở Git Bash ở `TADSS-Runtime/`; lần đầu tự build và tải image (vài phút) |
| 3. Chạy bản nhanh | `bash tools/evaluation/run.sh`. Các dòng in ra theo thời gian; thời lượng từng bước (a)…(f) đo thật (lấy từ lượt cuối Task 7); dấu hiệu bị treo và cách nhận biết |
| 4. Đọc kết quả | Bảng tóm tắt cuối màn hình; ý nghĩa mã thoát 0/1/2; mở `evidence/<run_id>/report.html` — xem mục nào trước (KPI, EV, từng bước); Grafana `http://127.0.0.1:3002` khi không dùng `--clean` |
| 5. Mỗi bước làm gì | (a) test, (b) tải, (c) sự cố dưới tải + SCALE-2, (d) contract + mutation + coverage, (e) dữ liệu thực tế, (f) Evidence Pack. Mỗi bước 2–3 câu, mã danh mục liên quan, file kết quả trong `raw/` |
| 6. Bản đầy đủ | `run.sh full`; thời lượng; chỉ chạy khi máy rảnh |
| 7. Chạy lại một bước | `run.sh quick --steps c` và các ví dụ hay dùng; bước nào cần bước nào chạy trước |
| 8. Thêm dữ liệu SmashCourt (tùy chọn) | Lệnh `psql` với biến của bạn, chỗ đặt CSV, cách kiểm nhanh số dòng; nhấn mạnh không commit thư mục `data/private/` |
| 9. Chạy test thủ công (không cần evaluator) | `dotnet build` (0/0), unit, integration (cần Docker), pytest hai bộ, `verify-expose.sh`, `verify-observability.sh`; thời lượng mỗi lệnh |
| 10. Sự cố thường gặp | Bảng **triệu chứng → nguyên nhân → cách xử lý**, tối thiểu các trường hợp đã gặp thật ở 5.5–5.6: Docker chưa bật; thiếu RAM; máy ngủ giữa chừng (số đo sai); cổng 3002 bận; mã thoát 2; test không ổn định đã biết; FI-07 thỉnh thoảng không đạt; không tải được dataset; thiếu CSV SmashCourt; build cache phình to |
| 11. Dọn Docker | `run.sh --clean-only`; xem dung lượng; trả dung lượng ổ ảo cho Windows (Clean/Purge data hoặc `wsl --shutdown` + `Optimize-VHD`) |

**Yêu cầu về hình thức:**
- Tiếng Việt, câu ngắn, mỗi mục bắt đầu bằng **một câu nói mục đích**.
- Không lặp đặc tả kỹ thuật: cần chi tiết thì liên kết methodology/SAS.
- Mọi đường dẫn và lệnh trong guide **phải chạy được nguyên văn** từ thư mục `TADSS-Runtime/` trong Git Bash.
- Độ dài mục tiêu ≤ khoảng 300 dòng.

**Test cases:**

| Test | Kịch bản | Kỳ vọng | CatalogId |
| :--- | :--- | :--- | :--- |
| Thử guide | Agent chạy lần lượt mọi lệnh ở mục 3, 7, 9, 11 đúng nguyên văn | Mỗi lệnh chạy được, kết quả khớp mô tả "thấy gì là đúng" | — |
| `run.sh` tham số | `--steps e,f`; `--steps x`; `--steps` thiếu giá trị | Chạy đúng hai bước / báo lỗi rõ, mã 2 / báo lỗi rõ, mã 2 | — |
| Liên kết | Kiểm link nội bộ của guide và README | Mọi link trỏ tới file/anchor có thật | — |

**Nghiệm thu:**
- Guide đủ 11 mục, mọi lệnh đã chạy thử, README trỏ tới guide.
- Người dùng đọc lại và xác nhận dễ hiểu. Nếu có góp ý thì sửa trong gói này.

---

### Task 7: Tài liệu, kiểm chứng cuối gói và dọn Docker

**Files:**
- Modify:
  - `docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`:
    - 4.9, 4.10: cách chạy trong bước (e), định nghĩa lỗi contract Q-09;
    - 5: SC7 tính được;
    - 6.1: bảng bước a…f, thời lượng đo thật;
    - 8: giới hạn mới của Task 5.
  - `docs/research/DATASET_SCHEMA_SPECIFICATION.md`:
    - D4, D5: cột bổ sung;
    - Mục 2: file mới trong pack, trường `datasets` của manifest;
    - nguồn và giấy phép dataset.
  - `spec/PHASE5_DESIGN_SPEC.md`: 4.6 ghi Q-01, Q-02, Q-10 (notebook thay bằng CLI) — **không stage**.
  - `README.md`: trạng thái, số test.
  - `TADSS-Runtime/CLAUDE.md`: bước a…f, thư mục `data/private/`, guide.
  - `docs/planning/BACKLOG_SPRINTS.md` (T5.18…T5.20), `docs/planning/MASTER_EXECUTION_PLAN.md` (dòng W08).
  - Skill `build-verify` (ngoài repo): `verify-export.sh` khi sửa SQL xuất.
- Create: `spec/PHASE5_REPORT_5.6.md` (không stage).

**Yêu cầu:**
1. Tài liệu chỉ ghi điều đã hiện thực, sửa ở file sở hữu. Kiểm link nội bộ mọi `.md` đã sửa.
2. **Kiểm chứng cuối gói** (số liệu thật vào báo cáo):
   - `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror --no-incremental` → 0/0. Unit và IT xanh; số test so với đầu gói (1871 / 238).
   - pytest hai bộ xanh; liệt kê test mới.
   - `git diff 3f55866 -- src/ docs/contracts/` **rỗng**; `dotnet ef migrations list --no-connect` đúng 4 ID.
   - `verify-expose.sh`, `verify-observability.sh` đạt; `verify-export.sh` đạt.
   - **Một lượt bản nhanh trọn vẹn từ Docker trống** bằng đúng lệnh trong guide (`bash tools/evaluation/run.sh quick --clean`): mã thoát, thời lượng từng bước, `run_id`, KPI SC1–SC4 và SC7, EV-1…EV-5.
     - Có CSV SmashCourt thật của người dùng (U-3) thì RP có số liệu.
     - Chưa có thì ghi `skipped` và chạy lại phần (e, f) khi có.
   - grep: không có UUID hay số điện thoại dạng thô trong `evidence/<run_id>/`; `data/private/` không bị git theo dõi.
3. **Dọn Docker** (yêu cầu thường trực của người dùng):
   - Sau lượt cuối, `--clean` để lại Docker không còn container, volume hay build cache của chế độ đánh giá.
   - Ghi `docker system df` vào báo cáo cùng bảng lệnh Docker.
4. **Báo cáo gói (`spec/PHASE5_REPORT_5.6.md`)** gồm:
   - đối chiếu Phụ lục A có cột trạng thái;
   - bảng spike S-1…S-5;
   - tham số PT đã duyệt;
   - kết quả RP-01/02, PT-01/02 (số liệu chính);
   - thời lượng đo thật;
   - việc của người dùng;
   - mục chuyển sang 5.7.

**Nghiệm thu:**
- Mọi mục kiểm chứng đạt; tài liệu khớp mã.
- Gợi ý **một** commit cho cả gói (người dùng tự chạy, loại `spec/PHASE5_*.md`):
  `feat(evidence): smashcourt replay, hotel portability adapter and evaluation run guide (5.6)`.

---

## Phụ lục A — Đối chiếu spec → task

| Yêu cầu (spec 4.6 và liên quan) | Task |
| :--- | :---: |
| `sql/export_smashcourt_bookings.sql` chỉ đọc, người dùng tự chạy | 2 |
| `anonymize.py` HMAC-SHA256 (dùng lại của 5.5) | 2, 3 |
| `replay.py`: request v2 từ CSV ẩn danh, dời bội số 7 ngày (sửa V-7), khóa `replay:{runId}:{bookingHash}`, D4 kèm `STATIC_100` | 1, 2, 4, 5 |
| `hotel_adapter.py`: URL + SHA-256 ghim, không commit dữ liệu gốc, ánh xạ spec, dời thời gian, D5; ngưỡng tiền tệ bằng cấu hình chạy riêng | 1, 3, 4 |
| `evidence_pack.py` được evaluator gọi; EV-1…EV-5 | 5 |
| Notebook sinh báo cáo | 5 (thay bằng CLI, Q-10) |
| RP-01, RP-02 (methodology 4.9) | 2, 4, 5 |
| PT-01, PT-02, SC7 (methodology 4.10, 5) | 3, 4, 5 |
| Đổ vào cùng Evidence Pack và `report.html` (D-12) | 4, 5 |
| Giới hạn: nhãn môi trường phát triển (methodology 8) | 5, 7 |
| Hướng dẫn chạy từng bước (báo cáo 5.5 Mục 8, yêu cầu 09/10/2026) | 6 |
| Tài liệu Mục 9 của spec (methodology, dataset schema, README, CLAUDE.md, kế hoạch) | 7 |

Không thuộc 5.6:
- Lượt đầy đủ chính thức, chốt Evidence Pack vào `docs/research/evidence-packs/`, DOC-1…DOC-4, đóng băng v2: thuộc 5.7.
- LT-04, E2E, SC5/SC6, BE gọi TADSS thật: thuộc Phase 6.

## Phụ lục B — Thay đổi cấu hình và hành vi cần duyệt

| # | Thay đổi | Nơi ghi |
| :--- | :--- | :--- |
| B-1 | Ký hiệu bước evaluator: thêm (e) dữ liệu thực tế, Evidence Pack thành (f); `EVAL_STEPS` nhận a…f (Q-01) | README, guide, methodology 6.1, CLAUDE.md, D-12 |
| B-2 | Service mới `eval-tadss-portability` (không cổng host; database `tadss_portability`; cấu hình EUR và `MaxItemMinutes` riêng) (Q-02) | compose, methodology 4.10, `ComposeFixtureAlignmentTests` |
| B-3 | Volume `eval-datasets` (cache dataset, `--clean` xóa); mount chỉ đọc `./data/private` vào evaluator; `data/private/` vào `.gitignore` (Q-05, Q-06) | compose, guide, `.gitignore` |
| B-4 | Evaluator cần mạng ra ngoài ở lần đầu để tải dataset (đã cần cho pip khi build image) | guide mục 2, methodology 8 |
| B-5 | Không làm notebook; thay bằng CLI dựng lại báo cáo (Q-10) | spec 4.6, DATASET_SCHEMA Mục 5 |
| B-6 | `run.sh --steps` (Q-14) | guide, README |
| B-7 | D4/D5 thêm cột (Task 5); manifest thêm trường `datasets` | DATASET_SCHEMA |

Không đổi:
- Wire (OpenAPI, JSON schema), SDK và baseline public API, mã lỗi, status HTTP.
- Schema DB, mọi file dưới `src/`.
- Cấu hình của `eval-tadss-1/2` và chế độ vận hành.
- Hành vi các bước (a)…(d).

## Ghi chú cho gói 5.7

- **Lượt đầy đủ chính thức:**
  - Có thêm bước (e), dự kiến cộng khoảng 8–12 phút cho khoảng 118 nghìn request PT; con số thật lấy từ S-5 và Task 7.
  - D1 của database PT và D5 bản đầy đủ có thể vượt 20 MB ⇒ theo DATASET_SCHEMA Mục 2, đính kèm GitHub Release, manifest vẫn ghi băm.
- **Chốt Evidence Pack vào `docs/research/evidence-packs/{run_id}/`:** chỉ đưa D5 vào nếu giấy phép (U-2) cho phép phân phối lại dữ liệu dẫn xuất; nếu không thì chỉ đưa `pt_summary.json`.
- **DOC-1:** guide `docs/guides/EVALUATION_RUN_GUIDE.md` là tài liệu **hiện hành**, sở hữu quy trình chạy. README và methodology chỉ liên kết tới nó.
- Theo dõi tiếp từ 5.5: FI-07 (2/1262 request ở lượt nhanh cuối), độ trễ nối lại database khoảng 15 giây của instance không nhận tải sau sự cố.
