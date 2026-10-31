# ĐẶC TẢ TỪNG BƯỚC CÁC KỊCH BẢN KIỂM THỬ ĐẦU–CUỐI (E2E-01…E2E-14)

| | |
| :--- | :--- |
| **Trạng thái** | Bản đặc tả cho Phase 6–8, **chưa hiện thực**; SV2 review. Bản đầu do SV1 viết ngày 12/10/2026 theo yêu cầu người dùng (task T5.27, gói 5.7), bàn lại với SV2 sau Phase 5 |
| **Sở hữu** | Đặc tả **từng bước** của mỗi kịch bản E2E: điều kiện trước, thao tác, quan sát kỳ vọng ở giao diện, API, DB và TADSS, minh chứng, tiêu chí đạt |
| **Không sở hữu** | Mã kịch bản, tiêu chí tóm tắt và KPI: [`EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md) Mục 4.8 và Mục 5 (khi khác nhau, methodology đúng). Hợp đồng BE ↔ FE (route, DTO, mã lỗi, sự kiện SignalR, câu hỏi mở Q1–Q11): [`DEPOSIT_APPROVAL_API.md`](../../../SmashCourt-BE/docs/api/DEPOSIT_APPROVAL_API.md). Hợp đồng BE ↔ TADSS: [`INTEGRATION_SPEC.md`](../contracts/INTEGRATION_SPEC.md). Dựng và vận hành E-STAGING: [`MASTER_EXECUTION_PLAN.md`](../planning/MASTER_EXECUTION_PLAN.md#môi-trường-tích-hợp-e-staging) |
| **Hiện thực** | Phase 6: bản chạy trên MSW (FE-02, T6.15, SV2). Phase 7: chuyển Playwright sang E-STAGING (T7.3) và smoke các luồng của Gate M7 (T7.1). Phase 8: chạy chính thức E2E-01…E2E-14 kèm video và trace (T8.4, SV2) |

Tài liệu này không chọn selector, tên file test hay câu SQL cụ thể; các chi tiết đó chốt khi hiện thực (Mục 8). Mọi giá trị trong ví dụ là giá trị mẫu.

---

## 1. Mục đích và phạm vi

- **Mục đích:** mỗi kịch bản E2E của danh mục được viết thành các bước đủ chính xác để hai người chạy độc lập cho cùng kết luận đạt/không đạt, và để bản trên MSW (Phase 6) và bản trên E-STAGING (Phase 7–8) dùng chung một kịch bản.
- **Trong phạm vi:** luồng cọc, chờ duyệt, duyệt/từ chối, hết hạn duyệt, cọc 100 % khi có sự cố, thu phần còn lại tại quầy, cập nhật realtime, phạm vi chi nhánh, đặt tại quầy không qua TADSS — trên hệ thống thật FE → BE → TADSS → VNPay sandbox.
- **Ngoài phạm vi:** tải (LT-04), tiêm lỗi mạng FI-11…FI-14 do SV1 chạy riêng (methodology Mục 4.5), các kịch bản chỉ kiểm được ở mức BE hoặc TADSS (Mục 7). Nội dung email chỉ được ghi nhận là "có gửi", không kiểm nội dung.

---

## 2. Môi trường

### 2.1. MSW — Phase 6 (FE-02)

- FE chạy với Mock Service Worker chặn mọi request tới BE; handler trả đúng các ví dụ của `DEPOSIT_APPROVAL_API.md` (FE-03). Không có BE, TADSS, DB hay VNPay thật.
- Mỗi kịch bản ở Mục 5 chạy với **cùng thao tác và cùng kiểm tra giao diện**. Các kiểm tra ở cột *DB*/*TADSS* được thay bằng kiểm request mà FE gửi đi (route, method, body, chỉ dùng `bookingId`, không có `decisionId`). VNPay thay bằng trang trả về giả lập; sự kiện SignalR phát giả lập (cách giả lập chốt ở T6.15).
- Các bước dừng TADSS hay đổi cờ (E2E-05, E2E-06, E2E-11) trên MSW chỉ là chọn response tương ứng: nhánh (d) của `POST /api/bookings/online` cho E2E-05/06, 503 `TADSS_UNAVAILABLE` rồi 200 cho E2E-11.

### 2.2. E-STAGING — W13–W15

Theo [Môi trường tích hợp E-STAGING](../planning/MASTER_EXECUTION_PLAN.md#môi-trường-tích-hợp-e-staging):

- Ba repo chạy bằng Docker Compose trên máy nhóm, chung mạng `smashcourt-network`; TADSS chỉ bật `tadss-runtime` và `grafana` (`docker compose up -d --build tadss-runtime grafana` trong `TADSS-Runtime/`), không bật ngrok của TADSS và không bật stack đánh giá.
- Dữ liệu trên nhánh Neon `staging` (DB SmashCourt và schema `tadss`), không bao giờ trỏ vào nhánh chính. VNPay sandbox nhận callback/IPN qua ngrok của **BE**.
- **Email qua Mailpit** (plan 6A-6 Task 12): BE chạy kèm `docker-compose.mailpit.yml`, mọi email (OTP, xác nhận, duyệt, hủy, slot trống) vào Mailpit; các bước ghi "Email gửi khách (chỉ ghi nhận)" kiểm bằng API Mailpit (`GET /api/v1/search?query=to:"<email>"`: có đúng thư, đúng người nhận). Không dùng Gmail SMTP cho đợt E2E (giới hạn khoảng 500 thư/ngày).
- **Link VNPay có hạn** (P6-27, sandbox 30/10): link hết hạn trước hạn thanh toán 2 phút; khách mở trang VNPay rồi trả sau hạn link thì VNPay chặn (mã `15`, không trừ tiền, không gửi IPN), nên kịch bản không cần kiểm khe hở "trả trên trang đã mở sau khi đơn hết hạn" của trước 6A-6. Trang kết quả đọc `bookingStatus` của `POST /api/payments/vnpay/confirm` (P6-28).
- Định tuyến strategy chuẩn: `Tadss:Routing:DefaultStrategy = DefaultDecisionStrategy`, không rule (cấu hình mặc định). Mọi thay đổi cấu hình của TADSS staging (Mục 3.3, điều kiện N3) được ghi vào cấu hình đã dùng của minh chứng.
- Các kịch bản chạy **tuần tự**: E2E-05 đổi cờ, E2E-06 và E2E-11 dừng TADSS — trạng thái dùng chung của cả hệ thống — nên không chạy song song với kịch bản khác hay với FI-11…FI-14.

### 2.3. Mốc thời gian dùng trong kịch bản

| Đại lượng | Giá trị | Nguồn |
| :--- | :--- | :--- |
| Hạn duyệt | `approvalExpiresAt = min(T0 + WindowMinutes, giờ bắt đầu − MinLeadMinutes)`, mặc định `WindowMinutes = 30`, `MinLeadMinutes = 120`; nếu kết quả ≤ T0 thì TADSS trả `APPROVED` cọc 100 % (`APPROVAL_WINDOW_UNAVAILABLE`) thay cho `NEED_APPROVAL` | INTEGRATION_SPEC §1.1; `CONFIGURATION_REFERENCE.md` `Tadss:Approval` |
| Khung giờ cho các ca chờ duyệt có cửa sổ đầy đủ | Giờ bắt đầu ≥ T0 + 150 phút ⇒ `approvalExpiresAt = T0 + 30 phút` | Suy từ dòng trên |
| Khung giờ cho E2E-04 (hết hạn nhanh) | Giờ bắt đầu − 120 phút − T0 ∈ [3; 10] phút ⇒ `approvalExpiresAt = giờ bắt đầu − 120 phút` | Suy từ dòng trên |
| Chu kỳ job hết hạn duyệt | 1 phút (như job hủy `PENDING` quá hạn hiện có) | DEPOSIT_APPROVAL_API Mục 7 |
| Hạn thanh toán sau khi duyệt | 15 phút (Q2, chốt 18/10/2026), không vượt giờ bắt đầu; cũng là hạn giữ chỗ tuyệt đối của đơn (thanh toán lại không kéo dài quá mốc này) | DEPOSIT_APPROVAL_API Q2, Mục 3.3 |
| Hạn thanh toán khi tạo booking `PENDING` | 10 phút (hành vi hiện có) | UC-BOOKING-01 |
| Cửa sổ check-in | Từ 15 phút trước tới 15 phút sau giờ bắt đầu | UC-BOOKING-05 |
| Hiệu lực khi đổi `EnableTadssDecision` | < 2 giây, không khởi động lại | INTEGRATION_SPEC §4.1; KPI SC5 |
| Chu kỳ job outcome | 5 phút, tối đa 20 booking mỗi chu kỳ, gửi tuần tự (chốt 27/10, P6-19, P6-26); E2E chờ tối đa 2 chu kỳ | spec Phase 6 Mục 7.11 |

T0 là thời điểm FE gửi `POST /api/bookings/online`. Khung giờ đặt phải tồn tại trong lịch mở cửa và bảng `time_slots` của chi nhánh thử, nên giờ chạy E2E-04 và E2E-09 phải chọn theo lưới khung giờ (Mục 8).

---

## 3. Điều kiện chung và dữ liệu thử

### 3.1. Tài khoản theo vai trò

Bí danh dưới đây dùng trong mọi kịch bản. Tài liệu không ghi mật khẩu; danh sách bước tạo lại nằm trong ghi chú của T7.2 (MASTER_EXECUTION_PLAN, mục Dữ liệu, bước 6).

| Bí danh | Vai trò | Chi nhánh | Dùng trong |
| :--- | :--- | :--- | :--- |
| `OWNER-1` | `OWNER` | Mọi chi nhánh | E2E-08 |
| `MGR-A1` | `BRANCH_MANAGER` | `CN-A` | E2E-02, 03, 04, 05, 07, 08, 10, 11, 12, 13 |
| `MGR-A2` | `BRANCH_MANAGER` | `CN-A` (Manager thứ hai) | E2E-07, 10 |
| `MGR-B1` | `BRANCH_MANAGER` | `CN-B` (Manager chi nhánh khác) | E2E-08 |
| `STAFF-A1` | `STAFF` | `CN-A` | E2E-08, 09, 10, 13 |
| `CUS-STD` | `CUSTOMER` | — | Khách hạng thấp nhất (`STANDARD` sau ánh xạ), không có booking `NO_SHOW`: các ca `APPROVED` (E2E-01, 05, 06, 09) và người đặt lại slot ở E2E-03 |
| `CUS-EXPIRY` | `CUSTOMER` | — | Khách có ít nhất `PaymentExpiryRejectThreshold` booking `PAYMENT_EXPIRED` (nhiều hơn số booking hoàn tất) trên nhánh `staging`: ca bị từ chối (E2E-14) |
| `CUS-REVIEW` | `CUSTOMER` | — | Khách tạo các đơn chờ duyệt (E2E-02, 03, 04, 05, 07, 08, 10, 11, 12) theo điều kiện N ở Mục 3.3; ở E2E-05 còn đặt slot thỏa điều kiện N khi cờ tắt |

Khách vãng lai không dùng trong E2E đặt online: từ Phase 6 đặt sân online bắt buộc đăng nhập `CUSTOMER` (DEPOSIT_APPROVAL_API P6-01; FE đã chỉ cho đặt khi đăng nhập từ 12/05/2026). Khách vãng lai chỉ xuất hiện ở đặt tại quầy (E2E-13). Mọi email và số điện thoại của persona là địa chỉ thử của nhóm.

### 3.2. Chi nhánh, sân, bảng giá

- Hai chi nhánh dành riêng cho E2E, `CN-A` và `CN-B`, trạng thái hoạt động; mỗi chi nhánh ít nhất hai sân hoạt động cùng loại sân; có bảng giá và khung giờ phủ các giờ chạy kịch bản. Chi nhánh riêng giữ cho màn hình của Manager/Staff thử chỉ chứa dữ liệu thử (Mục 3.5) và không có người thật đăng ký chờ slot nhận thông báo.
- Mỗi kịch bản dùng **slot riêng** (sân + ngày + giờ) để không đụng slot lock của kịch bản khác; bảng phân slot lập trước mỗi đợt chạy.
- Ghi lại `finalTotal` (ký hiệu F) của từng slot thử theo bảng giá để tính số cọc kỳ vọng.

### 3.3. Hạng thành viên, mức cọc và điều kiện tạo `NEED_APPROVAL`

Strategy mặc định (`DefaultDecisionStrategy` 2.0.0) với tham số mặc định (`CONFIGURATION_REFERENCE.md`, `Tadss:Strategies:Default`); BE ánh xạ hạng theo thứ tự `MinPoints` tăng dần (INTEGRATION_SPEC §4.2):

| Khách | Hạng gửi TADSS | Tỷ lệ cọc khi `APPROVED` |
| :--- | :--- | :--- |
| Khách vãng lai; hạng thứ nhất hoặc chưa có hạng | `STANDARD` | 0.5 |
| Hạng thứ hai | `SILVER` | 0.4 |
| Hạng thứ ba | `GOLD` | 0.3 |
| Hạng thứ tư trở lên | `PLATINUM` / `DIAMOND` | 0.2 |

Số cọc kỳ vọng: `depositAmount = round(F × rate)`, VND không có chữ số thập phân, làm tròn nửa xa số 0 (INTEGRATION_SPEC §3.6, DEPOSIT_APPROVAL_API 2.2).

- **Điều kiện A (kỳ vọng `APPROVED`):** F < `HighValueThreshold` (mặc định 2.000.000 VND) và khách có ít hơn `NoShowReviewThreshold` (mặc định 2) lần không đến.
- **Điều kiện N (kỳ vọng `NEED_APPROVAL`), chọn một cách ở T7.2:**
  - N1 — đơn có F ≥ `HighValueThreshold` (nhiều sân cùng khung giờ hoặc nhiều giờ), giữ nguyên cấu hình mặc định;
  - N2 — `CUS-REVIEW` có ít nhất `NoShowReviewThreshold` booking `NO_SHOW` đã kết thúc trên nhánh `staging`;
  - N3 — hạ `Tadss:Strategies:Default:HighValueThreshold` của TADSS staging bằng biến môi trường (chỉ cấu hình, như cách PT cấu hình instance riêng), ghi giá trị vào cấu hình đã dùng của minh chứng.
- Với điều kiện N, giờ bắt đầu phải đủ xa theo Mục 2.3, nếu không TADSS trả `APPROVED` cọc 100 % (`APPROVAL_WINDOW_UNAVAILABLE`).
- Điều kiện N là thuộc tính của **cặp khách + slot**: N1 và N3 là thuộc tính của slot (F ≥ ngưỡng, khách nào đặt cũng cần duyệt), N2 là thuộc tính của khách `CUS-REVIEW` (slot nào cũng cần duyệt, khách khác đặt cùng slot thì `APPROVED`). Vì vậy mọi đơn chờ duyệt đều do `CUS-REVIEW` đặt trên slot thỏa cách đã chọn; bước nào cho khách khác đặt lại slot đó (E2E-03 bước 5) ghi kỳ vọng riêng cho từng cách.

- **Điều kiện R (kỳ vọng `REJECTED`):** khách `CUS-EXPIRY` có `paymentExpiredCount ≥ PaymentExpiryRejectThreshold` (mặc định 5) và `paymentExpiredCount > completedCount`. Điều kiện R xét trước điều kiện N: khách này bị từ chối kể cả khi đặt slot giá trị lớn. Lịch sử `PAYMENT_EXPIRED` chỉ tạo được bằng SQL trên nhánh `staging` (Mục 3.4, điều 3) hoặc để các booking `PENDING` quá hạn thanh toán.

### 3.4. Dựng và dựng lại dữ liệu trên nhánh `staging`

1. Làm mới nhánh `staging` từ nhánh chính trước mỗi đợt chạy chính thức (smoke Gate M7, đợt T8.4) theo MASTER_EXECUTION_PLAN mục Dữ liệu, bước 5. Mọi tài khoản và dữ liệu thử tạo trên nhánh mất theo.
2. Tài khoản, chi nhánh, sân, bảng giá đã có trên nhánh chính thì nhánh mới thừa hưởng; phần còn thiếu tạo lại bằng màn hình hiện có (đăng ký theo UC-AUTH-01 cho khách; Owner tạo chi nhánh, sân, giá, tài khoản và gán Manager/Staff), theo ghi chú dựng lại của T7.2.
3. Dữ liệu không tạo được qua giao diện (điểm thành viên để có hạng, lịch sử `NO_SHOW` cho N2) chỉ được sửa bằng SQL **trên nhánh `staging`**, không bao giờ trên nhánh chính; câu lệnh nằm trong ghi chú T7.2.
4. Migration mới của schema `tadss` không được tự áp lên nhánh (D-05); ưu tiên tạo lại nhánh từ nhánh chính đã migrate.

### 3.5. Dữ liệu cá nhân

Nhánh `staging` sao cả dữ liệu thật của nhánh chính (tên, số điện thoại khách, booking thật). Vì vậy:

- Video, trace, ảnh chụp màn hình và log của E2E có thể chứa dữ liệu cá nhân: **không commit, không chia sẻ ra ngoài nhóm** (MASTER_EXECUTION_PLAN, mục Dữ liệu, bước 1). Chúng nằm trong thư mục `evidence/` ở gốc repo TADSS (đã gitignore) hoặc thư mục tương đương ngoài repo.
- Chỉ dùng tài khoản thử và chi nhánh thử; không đăng nhập bằng tài khoản của khách thật, không thao tác trên booking thật.
- Màn hình của `OWNER-1` thấy mọi chi nhánh: ở E2E-08 lọc theo chi nhánh thử trước khi chụp; ảnh đưa vào báo cáo chỉ lấy từ màn hình chỉ có dữ liệu thử.
- Không bao giờ xuất dữ liệu từ nhánh `staging` cho RP (MASTER_EXECUTION_PLAN, mục Thứ tự với việc xuất CSV RP).

### 3.6. Chuẩn bị trước mỗi đợt chạy

1. Ghi commit của ba repo, digest image, cấu hình đã dùng (không kèm bí mật), thông số máy.
2. Xác nhận chuỗi kết nối của BE và TADSS trỏ vào nhánh `staging` (kiểm tên nhánh/host trong trang quản trị Neon).
3. TADSS `GET /health/ready` trả 200; `EnableTadssDecision = true`; URL ngrok của BE đã cập nhật vào `VNPAY_CALLBACK_URL`/`VNPAY_IPN_URL`. **VNPay chỉ gọi IPN URL khai ở trang "VNPAYGW SIT Testing" của đúng terminal** (biến `VNPAY_IPN_URL` không gửi sang VNPay): kiểm dòng của terminal đang dùng trỏ `https://<ngrok BE>/api/payments/vnpay/ipn`; mở thử một link do BE tạo phải tới trang chọn phương thức thanh toán (terminal bị khóa ⇒ trang lỗi mã 71). Mã terminal và HashSecret trong `.env` của BE phải khớp `appsettings.Development.json`. Ghi chú môi trường đầy đủ: [spec Phase 6 Mục 10.1](../../spec/PHASE6_DESIGN_SPEC.md#101-xác-minh-trước-khi-bắt-đầu-6a-1) (thử sandbox 28/10/2026).
4. Kiểm giới hạn tốc độ `booking` của BE (vượt ngưỡng trả 429 `OTP_LIMIT_EXCEEDED`, DEPOSIT_APPROVAL_API 3.1) đủ cho số booking của đợt chạy.
5. Ghi mốc bắt đầu `t_start` của đợt; cuối đợt ghi `t_end`. Bộ truy vấn SC6 chỉ xét dữ liệu trong `[t_start; t_end]`.
5a. **LT-04** (methodology 4.6; plan 6A-6 Task 14): BE bật kèm Mailpit. Lượt chính thức (cờ bật) trước khi chạy đối chiếu strategy bằng `GET /api/v2/decisions/{id}` của một đơn thử (`execution.strategyName`, `execution.strategyVersion`), lấy tỷ lệ kỳ vọng của hạng STANDARD từ cấu hình strategy của lượt chạy. Lệnh trong `TADSS-Runtime/tests/load` (k6 2.3.0 cùng image của evaluator, container nối mạng `smashcourt-network`):
   `docker run --rm --network smashcourt-network -v "<tests/load>:/load" -v "<thư mục kết quả>:/out" -w /load/scenarios -e BE_URL=http://smashcourt-be:8080 -e MAILPIT_URL=http://smashcourt-mailpit:8025 -e LT04_BRANCH_ID=<id> -e LT04_PROFILE=full -e LT04_EXPECT=tadss -e LT04_EXPECTED_RATE=<tỷ lệ> -e LT04_STRATEGY="<tên phiên bản>" -e LT04_EXPECTED_RATE_SOURCE="<file@commit khóa>" -e SUMMARY_PATH=/out/lt04.json grafana/k6:2.3.0 run --quiet lt04.js`
   (chạy thử cờ tắt: `LT04_PROFILE=smoke LT04_EXPECT=fallback`; tài khoản có sẵn qua `LT04_ACCOUNTS`). Đạt khi k6 thoát 0 (ngưỡng check), rồi chạy K-SC6 (bước 6) cho khoảng của lượt.
6. **Chạy bộ kiểm SC6 (K-SC6) sau đợt** (BE-IT-20; plan 6A-6 Task 11): trong `SmashCourt-BE`, đặt biến môi trường rồi chạy `dotnet test SmashCourt-BE.Tests --filter "Category=Staging"`.
   - `SC6_CONNECTION_STRING`: chuỗi kết nối Npgsql tới nhánh `staging`, nên dùng role chỉ đọc. Cần quyền đọc `public` của BE; quyền đọc `tadss.decision_executions` chỉ phục vụ phần chẩn đoán "quyết định mồ côi", thiếu quyền **hoặc schema `tadss` nằm ở database khác** (trường hợp của dev 31/10: DB của BE không có schema `tadss`) thì báo cáo ghi `orphanCheckError` và các điều SC6 vẫn được kiểm.
   - `SC6_FROM_UTC = t_start`, `SC6_TO_UTC = t_end` (ISO 8601, bắt buộc, `t_end` phải trước lúc chạy kiểm để tập dữ liệu cố định); `TADSS_BASE_URL`, `TADSS_API_KEY` (điều 5); `SC6_OUTPUT` (đường dẫn file JSON vào Evidence Pack).
   - Đọc trường `status` của file JSON: `PASS` (0 vi phạm và 0 mục không kết luận được, test đạt), `FAIL` (có vi phạm, xem `violations`, `violationsByRule`), `INCONCLUSIVE` (lỗi gọi TADSS, lỗi DB hoặc lượt bị dừng, xem `inconclusiveChecks`, `abortReason`: chạy lại sau khi sửa môi trường, không tính là đạt). `orphanDecisions` chỉ để chẩn đoán, không ảnh hưởng kết luận.

---

## 4. Quy ước chung cho mọi kịch bản

### 4.1. Bốn loại quan sát

| Ký hiệu | Quan sát | Cách lấy |
| :--- | :--- | :--- |
| **UI** | Nội dung, trạng thái giao diện | Assertion của Playwright |
| **API** | Request FE gửi và response BE trả | Network trong trace của Playwright; kiểm `code`, HTTP status, trường của `data` |
| **DB** | Trạng thái DB SmashCourt trên nhánh `staging` | Truy vấn **chỉ đọc**; tên cột mới (`tadss_decision_id`, trường cọc, `resolvedBy`/`resolvedAt`/`approvalResult`) chốt ở T6.1 |
| **TADSS** | Bản ghi của TADSS trong schema `tadss` cùng nhánh | Truy vấn chỉ đọc ba bảng `decision_executions`, `decision_overrides`, `decision_outcomes` (cột theo [`DECISION_PERSISTENCE_INVARIANTS.md`](../architecture/DECISION_PERSISTENCE_INVARIANTS.md) Mục B), chỉ xét `role = 'PRIMARY'` |

### 4.2. Định danh và correlation id

- `bookingId`: lấy từ response của `POST /api/bookings/online` (hoặc `/walk-in`) trong trace. FE và giao diện không bao giờ có `decisionId`.
- `decisionId`: đọc từ cột `tadss_decision_id` của booking trong DB; kiểm chéo bằng `tadss.decision_executions.reservation_id = bookingId`.
- Correlation id: `tadss.decision_executions.correlation_id` của quyết định; dùng để tra log trên Loki và trace trên Tempo của Grafana (`127.0.0.1:3001`) và log của BE.

### 4.3. Các kiểm tra đặt tên dùng lại

| Mã | Kiểm tra |
| :--- | :--- |
| **K-DEC** | `decision_executions` có đúng một dòng `PRIMARY` với `reservation_id = bookingId`, `idempotency_key = bookingId`, `correlation_id` khác `null`; kèm giá trị `status`, `deposit_rate`, `deposit_amount`, `approval_expires_at`, `execution_type`, `reason_code` mà kịch bản chỉ ra |
| **K-NODEC** | `decision_executions` không có dòng nào với `reservation_id = bookingId` |
| **K-OVR(n)** | `decision_overrides` có đúng `n` dòng với `decision_id` của booking (n = 0 hoặc 1); khi n = 1 kiểm `action`, `deposit_rate`, `deposit_amount`, `actor_id` (= `userId` của Manager), `idempotency_key` (= `{decisionId}:APPROVE` hoặc `:REJECT`) |
| **K-OUT(v)** | Sau chu kỳ của job outcome: `decision_outcomes` có đúng một dòng với `decision_id` của booking, `outcome = v` |
| **K-SC6** | Bộ truy vấn bất biến booking (methodology Mục 5, điều 1–6) trong `[t_start; t_end]`: 0 vi phạm |

### 4.4. Minh chứng chung

- `run_id` theo methodology Mục 6, ví dụ `EV-20261210-E2E-02-r1`.
- Mỗi kịch bản: báo cáo HTML, trace và video của Playwright; ảnh chụp ở các mốc ghi trong kịch bản; file JSON kết quả các kiểm tra K-*; danh sách `bookingId`, `decisionId`, correlation id; JSON snapshot dashboard "TADSS Runtime" trong khoảng thời gian của kịch bản (không lưu ảnh dashboard, như OB-3) với các panel kịch bản nêu; manifest SHA-256 cho thư mục lưu (MASTER_EXECUTION_PLAN, mục Thu Evidence).

### 4.5. Tiêu chí đạt chung

Một kịch bản **đạt** khi mọi kỳ vọng của mọi bước đúng, K-SC6 cho 0 vi phạm, và không có lỗi hiển thị cho người dùng ngoài lỗi kịch bản chờ đợi. Thời gian chờ của Playwright (ví dụ 10 giây cho một sự kiện realtime) là tham số kiểm thử, không phải KPI. Mọi kịch bản đều liên quan **SC6**.

---

## 5. Các kịch bản

### E2E-01 — APPROVED và thanh toán cọc

**Mục tiêu:** khách đặt sân, TADSS trả `APPROVED`, khách trả đúng số cọc qua VNPay sandbox, booking được xác nhận (`PAID_ONLINE`).

**Điều kiện trước:** Mục 3.6; `CUS-STD` đăng nhập; slot `CN-A` thỏa điều kiện A, F đã biết; tỷ lệ kỳ vọng r = 0.5.

| # | Tác nhân → thao tác | Kỳ vọng |
| :---: | :--- | :--- |
| 1 | `CUS-STD` chọn `CN-A`, sân, ngày, khung giờ; xem tóm tắt (UC-BOOKING-01 bước 1–19) | UI: tóm tắt có F |
| 2 | `CUS-STD` bấm "Xác nhận đặt sân" | API: `POST /api/bookings/online` → 201, `status = PENDING`, `depositSource = TADSS`, `depositRate = 0.5`, `depositAmount = round(F × 0.5)`, `remainingAmount = F − depositAmount`, `paymentUrl` khác `null`, `approvalExpiresAt = null` |
| 3 | — | UI: hiển thị `depositAmount` và `remainingAmount` đúng giá trị của response trước khi chuyển sang VNPay |
| 4 | Kiểm DB và TADSS | DB: booking `PENDING`, `source = ONLINE`, trường cọc như bước 2, `tadss_decision_id` khác `null`, slot lock còn hiệu lực. TADSS: K-DEC với `status = APPROVED`, `deposit_rate = 0.5`, `deposit_amount = depositAmount`, `execution_type` ∈ {`AUTOMATED`, `CACHE_HIT`}, `reason_code = TIER_DEPOSIT`; K-OVR(0) |
| 5 | `CUS-STD` trên trang VNPay sandbox | UI: số tiền thanh toán = `depositAmount`; thanh toán bằng thẻ thử của sandbox |
| 6 | VNPay → callback và IPN của BE | UI: trang kết quả thành công. DB: booking `PAID_ONLINE`, hóa đơn `PARTIALLY_PAID`, số đã thanh toán = `depositAmount`. Sự kiện `PaymentSuccess` |
| 7 | `CUS-STD` mở chi tiết booking | API: `GET /api/me/bookings/{id}` → `status = PAID_ONLINE`, trường cọc như bước 2, `approvalReasonCode = null`. UI: "Đã cọc" kèm phần còn lại |
| 8 | Kiểm K-SC6 | 0 vi phạm |

**Biến thể thẻ lỗi (P6-03):** lặp bước 1–5 với booking mới, ở trang VNPay sandbox chọn hủy giao dịch (hoặc thẻ thử báo lỗi).

| # | Tác nhân → thao tác | Kỳ vọng |
| :---: | :--- | :--- |
| V1 | VNPay → callback và IPN thất bại | UI: trang kết quả thất bại có "Thanh toán lại" và "Hủy đơn". DB: booking vẫn `PENDING`, Payment `FAILED`, `expiresAt` ≤ thời điểm IPN + 5 phút. Sự kiện `PaymentFailed` |
| V2 | `CUS-STD` bấm "Thanh toán lại", thanh toán thành công | API: `POST /api/me/bookings/{id}/retry-payment` → 200 với số tiền = `depositAmount`. DB: booking `PAID_ONLINE`; hai Payment (`FAILED`, `SUCCESS`) |
| V3 | (booking khác) lặp V1 rồi bấm "Hủy đơn" | DB: booking `CANCELLED`, `cancel_reason = CUSTOMER_CANCELLED`, slot nhả ngay, không Refund |

**Bản ghi TADSS kỳ vọng:** một quyết định `APPROVED`; không override; chưa có outcome (outcome `COMPLETED` kiểm ở E2E-09).

**Minh chứng:** ảnh ở bước 3, 5, 7; panel "Quyết định theo trạng thái", "Độ trễ p95 theo endpoint".

**Tiêu chí đạt:** số cọc giống nhau ở bốn nơi — UI, response BE, VNPay, `decision_executions.deposit_amount`; trạng thái cuối `PAID_ONLINE`/`PARTIALLY_PAID`; K-SC6 đạt.

**Liên quan:** KPI SC6 · BE-UT-02, BE-UT-07, BE-UT-08, BE-IT-02, BE-IT-21, CT-04 · FE-04, FE-09, FE-11 · câu hỏi mở: không.

### E2E-02 — NEED_APPROVAL và Manager duyệt

**Mục tiêu:** đơn cần duyệt không đi tới VNPay; Manager duyệt kèm mức cọc dạng phần trăm; khách nhận kết quả và thanh toán đúng số cọc Manager chọn.

**Điều kiện trước:** `CUS-REVIEW` đăng nhập; slot `CN-A` thỏa điều kiện N, giờ bắt đầu ≥ T0 + 150 phút; `MGR-A1` mở trang hàng chờ ở ngữ cảnh trình duyệt riêng.

| # | Tác nhân → thao tác | Kỳ vọng |
| :---: | :--- | :--- |
| 1 | `CUS-REVIEW` đặt slot | API: 201, `status = PENDING_APPROVAL`, `paymentUrl = null`, `expiresAt = null`, bốn trường cọc `null`, `approvalExpiresAt` ≈ T0 + 30 phút (tính từ lúc TADSS đánh giá; giờ VN). UI: trang "Chờ duyệt cọc" có `bookingCode` và đồng hồ đếm; không chuyển sang VNPay |
| 2 | Kiểm DB và TADSS | DB: booking `PENDING_APPROVAL`, `tadss_decision_id` khác `null`, slot lock hiệu lực tới `approvalExpiresAt`, không có Payment. TADSS: K-DEC với `status = NEED_APPROVAL`, `approval_expires_at` trùng thời điểm của BE, `reason_code` ∈ {`HIGH_VALUE_REVIEW`, `NO_SHOW_HISTORY_REVIEW`} theo điều kiện N đã chọn |
| 3 | `CUS-STD` thử đặt đúng slot đó | API: bị từ chối bằng lỗi slot hiện có (`SLOT_UNAVAILABLE` hoặc `SLOT_UNAVAILABLE_NOTIFY_REGISTERED`); time grid hiện slot đã giữ với nhãn chờ duyệt |
| 4 | — (trang của `MGR-A1` đang mở) | Sự kiện `BookingPendingApproval` và `PendingApprovalQueueChanged` (`ADDED`/`NEW`); FE gọi lại `GET /api/bookings/pending-approval`; UI: đơn xuất hiện với tên khách, sân, giờ, F, lý do cần duyệt (Q6), hạn duyệt |
| 5 | `MGR-A1` mở hộp thoại duyệt, nhập 30 (%) và lý do ≥ 5 ký tự, bấm duyệt | API: `POST /api/bookings/{bookingId}/approve` với body `{ "depositRate": 0.3, "reason": "…" }` (URL và body không có `decisionId`) → 200, `approvalResult = APPROVED`, `status = PENDING`, `depositSource = MANAGER`, `depositRate = 0.3`, `depositAmount = round(F × 0.3)`, `remainingAmount = F − depositAmount`, `expiresAt` = thời điểm duyệt + 15 phút (Q2). UI: hộp thoại đóng, đơn rời hàng chờ (`PendingApprovalQueueChanged` `REMOVED`/`APPROVED`). DB: **không** có Payment (duyệt không tạo Payment/URL VNPay, DEPOSIT_APPROVAL_API P6-10), hóa đơn `deposit_source = MANAGER`, slot lock gia hạn tới `expiresAt` |
| 6 | Kiểm TADSS | K-OVR(1): `action = APPROVE`, `deposit_rate = 0.3`, `deposit_amount = depositAmount` của BE, `actor_id` = `userId` của `MGR-A1`, `idempotency_key = {decisionId}:APPROVE`. Dòng `decision_executions` không đổi (vẫn `NEED_APPROVAL`, bất biến) |
| 7 | — (trang chờ của `CUS-REVIEW` đang mở) | Sự kiện `BookingApprovalResolved` (`APPROVED`); FE gọi lại `GET /api/me/bookings/{id}`; UI: đã duyệt, số cọc, nút thanh toán. Email gửi khách (chỉ ghi nhận có gửi) |
| 8 | `CUS-REVIEW` bấm thanh toán | API: `POST /api/me/bookings/{id}/retry-payment` → 200, `status = PENDING`, `paymentUrl` khác `null`. UI (VNPay): số tiền = `depositAmount`. DB: đúng một Payment `PENDING` với số tiền = `depositAmount`; `expiresAt` **không đổi** (đã bằng hạn giữ chỗ tuyệt đối, DEPOSIT_APPROVAL_API 3.3) |
| 9 | Thanh toán sandbox; callback và IPN | DB: booking `PAID_ONLINE`, hóa đơn `PARTIALLY_PAID`; Payment của bước 8 `SUCCESS`. Sự kiện `PaymentSuccess` |
| 10 | Kiểm K-SC6 | 0 vi phạm (kể cả lúc đơn còn `PENDING_APPROVAL`: điều 2 — có `decisionId` và slot lock) |

**Bản ghi TADSS kỳ vọng:** một quyết định `NEED_APPROVAL`, một override `APPROVE`, chưa có outcome.

**Minh chứng:** ảnh ở bước 1, 4, 5, 7, 8; panel "Quyết định chờ duyệt theo chi nhánh", "Override theo hành động".

**Tiêu chí đạt:** không có Payment nào trước bước 8 (lúc chờ duyệt và ngay sau khi duyệt); sau bước 8 đúng một Payment; hạn thanh toán không bị kéo dài khi bấm thanh toán; cọc = round(F × tỷ lệ Manager nhập) ở UI, response, VNPay và `decision_overrides`; đúng một override; K-SC6 đạt.

**Liên quan:** KPI SC6 · BE-UT-03, BE-IT-03, BE-IT-05, BE-IT-06, BE-IT-07, BE-IT-18 · FE-04, FE-05, FE-08, FE-10 · câu hỏi mở: không (Q2, Q3, Q5, Q6 đã chốt).

### E2E-03 — Manager từ chối

**Mục tiêu:** Manager từ chối đơn chờ duyệt; khách thấy bị từ chối; slot được giải phóng và đặt lại được.

**Điều kiện trước:** một booking `PENDING_APPROVAL` của `CUS-REVIEW` ở `CN-A` (tạo như E2E-02 bước 1–2); `MGR-A1` mở hàng chờ; trang chờ của `CUS-REVIEW` đang mở.

| # | Tác nhân → thao tác | Kỳ vọng |
| :---: | :--- | :--- |
| 1 | `MGR-A1` bấm từ chối, nhập lý do ≥ 5 ký tự | API: `POST /api/bookings/{bookingId}/reject` với body `{ "reason": "…" }` → 200, `approvalResult = REJECTED`, `status = CANCELLED`, trường cọc `null`. UI: đơn rời hàng chờ (`REMOVED`/`REJECTED`) |
| 2 | Kiểm DB và TADSS | DB: booking `CANCELLED`, `cancel_source = STAFF`, `cancel_reason = APPROVAL_REJECTED`, `cancelled_at` khác `null`, slot lock đã xóa, không có Payment hay Refund. TADSS: K-OVR(1) với `action = REJECT`, `deposit_rate`/`deposit_amount` `null`, `idempotency_key = {decisionId}:REJECT` |
| 3 | — (trang chờ của khách) | Sự kiện `BookingApprovalResolved` (`REJECTED`); UI: đơn bị từ chối/đã hủy. Email gửi khách (chỉ ghi nhận) |
| 4 | — (time grid của chi nhánh) | Sự kiện `BookingCancelled`; `GET /api/branches/{branchId}/courts/{courtId}/time-grid` hiện slot trống |
| 5 | `CUS-STD` đặt đúng slot đó | API: 201 (slot đã được giải phóng, không phải `SLOT_UNAVAILABLE`). `status` theo cách tạo điều kiện N đã chọn (Mục 3.3): N2 ⇒ `PENDING`, cọc theo hạng của `CUS-STD` (`depositSource = TADSS`); N1/N3 ⇒ `PENDING_APPROVAL` (F của slot vẫn ≥ ngưỡng). Dọn: booking `PENDING` để hết hạn thanh toán theo job hiện có; booking `PENDING_APPROVAL` do `MGR-A1` từ chối |
| 6 | Chờ job outcome | K-OUT(`REJECTED`) (ánh xạ chốt ở spec Phase 6 Mục 4.4.1) |
| 7 | Kiểm K-SC6 | 0 vi phạm |

**Bản ghi TADSS kỳ vọng:** quyết định `NEED_APPROVAL`, một override `REJECT`, một outcome.

**Minh chứng:** ảnh ở bước 1, 3, 4; panel "Override theo hành động".

**Tiêu chí đạt:** booking `CANCELLED` với `STAFF`; slot đặt lại được ở bước 5; đúng một override `REJECT`; K-SC6 đạt.

**Liên quan:** KPI SC6 · BE-IT-06, BE-IT-09, BE-IT-18 · FE-06, FE-08, FE-09 · use case: UC-BOOKING-01 · câu hỏi mở: Q5.

### E2E-04 — Hết hạn duyệt

**Mục tiêu:** Manager không xử lý trước hạn ⇒ job hủy booking, giải phóng sân; không bao giờ tự duyệt; duyệt sau hạn bị từ chối.

**Điều kiện trước:** slot `CN-A` thỏa điều kiện N, giờ bắt đầu sao cho giờ bắt đầu − 120 phút − T0 ∈ [3; 10] phút (Mục 2.3); `MGR-A1` mở hàng chờ.

| # | Tác nhân → thao tác | Kỳ vọng |
| :---: | :--- | :--- |
| 1 | `CUS-REVIEW` đặt slot | API: 201, `status = PENDING_APPROVAL`, `approvalExpiresAt` = giờ bắt đầu − 120 phút. TADSS: K-DEC với `status = NEED_APPROVAL` (không phải `APPROVAL_WINDOW_UNAVAILABLE`) |
| 2 | `MGR-A1` mở hộp thoại duyệt của đơn, nhập 30 % và lý do, **không bấm gửi** | UI: hộp thoại mở, đơn có đồng hồ đếm |
| 3 | Chờ tới `approvalExpiresAt` + tối đa 2 phút (một chu kỳ job 1 phút cộng biên) | DB: booking `CANCELLED`, `cancel_source = SYSTEM`, `cancel_reason = APPROVAL_EXPIRED`, nguồn cọc `null`, slot lock đã xóa, không có Payment. Sự kiện `BookingExpired` và `PendingApprovalQueueChanged` (`REMOVED`/`EXPIRED`); UI hàng chờ: đơn biến mất sau khi FE tải lại |
| 4 | `MGR-A1` bấm gửi ở hộp thoại đã mở từ bước 2. Nếu thiết kế UI đóng hộp thoại khi đơn rời hàng chờ (T6.11) nên không còn nút gửi, gửi cùng request `POST /api/bookings/{bookingId}/approve` (30 %, cùng lý do) bằng API của Playwright trong cùng phiên, như E2E-12 bước 3 | API: 409 `APPROVAL_EXPIRED` (kể cả khi job chưa chạy, vì BE kiểm `approvalExpiresAt ≤ now` trước khi gọi TADSS). UI (khi gửi từ hộp thoại): thông báo hết hạn duyệt, tải lại hàng chờ. TADSS: K-OVR(0) |
| 5 | `CUS-REVIEW` mở chi tiết | API: `GET /api/me/bookings/{id}` → `status = CANCELLED`; UI: đã hủy |
| 6 | Chờ job outcome | K-OUT(`APPROVAL_EXPIRED`) |
| 7 | Kiểm "không tự duyệt" và K-SC6 | Trong suốt kịch bản: không lúc nào booking ở `PENDING`, không có nguồn cọc `MANAGER`, không có Payment, K-OVR(0); K-SC6 0 vi phạm |

**Bản ghi TADSS kỳ vọng:** quyết định `NEED_APPROVAL`, không override, outcome `APPROVAL_EXPIRED`.

**Minh chứng:** ảnh ở bước 2, 3, 4; mốc thời gian `approvalExpiresAt` và thời điểm booking chuyển `CANCELLED`; panel "Quyết định chờ duyệt theo chi nhánh".

**Tiêu chí đạt:** booking bị hủy trong vòng 2 phút sau hạn; 0 override; 409 `APPROVAL_EXPIRED` ở bước 4; outcome đúng; K-SC6 đạt.

**Liên quan:** KPI SC6 · BE-UT-05, BE-IT-08, BE-IT-09, BE-IT-12 · FE-06, FE-08 · câu hỏi mở: không.

### E2E-05 — Tắt cờ EnableTadssDecision

**Mục tiêu:** tắt `EnableTadssDecision` khi hệ thống đang chạy ⇒ booking mới cọc 100 %, không gọi TADSS, khách không thấy lỗi; bật lại thì quay về luồng TADSS.

**Điều kiện trước:** `EnableTadssDecision = true`; `CUS-STD`, `CUS-REVIEW` và `MGR-A1` đăng nhập; cách đổi cờ lúc chạy (nạp lại qua `IOptionsMonitor`, không khởi động lại BE) chốt ở T6.2/T7.2; bốn slot `CN-A` riêng: slot Q thỏa điều kiện N (giờ bắt đầu ≥ T0 + 150 phút), slot cho bước 2 và bước 6 thỏa điều kiện A, slot cho bước 3 thỏa điều kiện N cùng `CUS-REVIEW` (Mục 3.3). **Trước `t_off`**, `CUS-REVIEW` đặt slot Q để có booking `bQ` ở `PENDING_APPROVAL` (như E2E-02 bước 1–2, K-DEC `NEED_APPROVAL`), nên mọi lượt chạy đều có đơn chờ duyệt cho bước 5.

| # | Tác nhân → thao tác | Kỳ vọng |
| :---: | :--- | :--- |
| 1 | SV1/SV2 đặt `EnableTadssDecision = false`, ghi `t_off` | BE không khởi động lại |
| 2 | Chờ 2 giây; `CUS-STD` đặt slot thỏa điều kiện A | API: 201, `status = PENDING`, `depositSource = FULL_PAYMENT_FALLBACK`, `depositRate = 1`, `depositAmount = F`, `remainingAmount = 0`. UI: luồng thanh toán bình thường, không có thông báo lỗi. TADSS: K-NODEC |
| 3 | `CUS-REVIEW` đặt slot của bước 3 (cặp khách + slot thỏa điều kiện N theo cách đã chọn, nên khi cờ bật sẽ là `NEED_APPROVAL`) | API: 201, `status = PENDING` (không phải `PENDING_APPROVAL`), cọc 100 % như bước 2. TADSS: K-NODEC |
| 4 | Thanh toán booking của bước 2 trên VNPay sandbox | UI (VNPay): số tiền = F. DB: `PAID_ONLINE` |
| 5 | (Q7) Cờ vẫn tắt: `MGR-A1` duyệt `bQ` (tạo trước `t_off`) với 30 % và lý do R | Theo đề xuất Q7: BE vẫn gọi override của TADSS → 200, `status = PENDING`, `depositSource = MANAGER`. TADSS: K-OVR(1) cho `bQ` với `action = APPROVE` |
| 6 | Đặt `EnableTadssDecision = true`, chờ 2 giây; `CUS-STD` đặt một slot thỏa điều kiện A | API: `depositSource = TADSS`; TADSS: K-DEC |
| 7 | Kiểm K-SC6 | 0 vi phạm |

**Bản ghi TADSS kỳ vọng:** không có quyết định nào cho các booking tạo khi cờ tắt; `bQ` có một quyết định `NEED_APPROVAL` (tạo trước `t_off`) và một override `APPROVE`; có quyết định cho booking ở bước 6.

**Minh chứng:** `t_off`, thời điểm bật lại, `bookingId` của `bQ` và các bước 2, 3, 6; panel "Request/giây (mọi endpoint)" quanh `t_off`.

**Tiêu chí đạt:** mọi booking tạo khi cờ tắt cọc 100 % `FULL_PAYMENT_FALLBACK` và K-NODEC; `bQ` duyệt được khi cờ tắt (200, K-OVR(1)); không lỗi hiển thị; bật lại có hiệu lực; K-SC6 đạt. Kịch bản này minh họa SC5 trên giao diện; số đo SC5 (< 2 giây) lấy từ FI-13.

**Liên quan:** KPI SC5 (minh họa), SC6 · BE-IT-04, BE-IT-19, FI-13 · FE-04 · câu hỏi mở: Q7.

### E2E-06 — TADSS dừng khi đặt sân

**Mục tiêu:** TADSS dừng hẳn ⇒ booking vẫn tạo được với cọc 100 %, giao diện không báo lỗi; TADSS chạy lại thì luồng trở lại bình thường.

**Điều kiện trước:** `CUS-STD` đăng nhập; hai slot `CN-A` thỏa điều kiện A.

| # | Tác nhân → thao tác | Kỳ vọng |
| :---: | :--- | :--- |
| 1 | `docker compose stop tadss-runtime` trong `TADSS-Runtime/`, ghi thời điểm | `/health/live` không phản hồi |
| 2 | `CUS-STD` đặt slot thứ nhất | API: 201, `status = PENDING`, `depositSource = FULL_PAYMENT_FALLBACK`, `depositRate = 1`, `depositAmount = F`. UI: không có thông báo lỗi, chuyển VNPay với số tiền F. Log BE: lỗi gọi TADSS kèm correlation id |
| 3 | `docker compose start tadss-runtime`; chờ `/health/ready` 200 | — |
| 4 | `CUS-STD` đặt slot thứ hai | API: `depositSource = TADSS`; TADSS: K-DEC |
| 5 | Kiểm TADSS và K-SC6 | K-NODEC cho booking ở bước 2; K-SC6 0 vi phạm |

**Bản ghi TADSS kỳ vọng:** không có quyết định cho booking ở bước 2.

**Minh chứng:** ảnh ở bước 2; thời điểm dừng/chạy lại; panel "Request/giây (mọi endpoint)" (khoảng trống lúc TADSS dừng); log của BE trong khoảng dừng có lỗi gọi TADSS kèm correlation id — BE ghi log ra console của container, lấy bằng `docker compose logs smashcourt-be` trong `SmashCourt-BE/` (không dùng panel "Log mức Error" của TADSS vì lúc đó TADSS đang dừng nên không ghi log).

**Tiêu chí đạt:** bước 2 trả 201, không có lỗi hiển thị, cọc 100 %; bước 4 qua TADSS; K-SC6 đạt.

**Liên quan:** KPI SC6 · BE-IT-04, FI-11 · FE-04 · câu hỏi mở: không.

### E2E-07 — Hai Manager duyệt cùng đơn

**Mục tiêu:** hai Manager cùng chi nhánh gửi duyệt gần như cùng lúc ⇒ đúng một thành công, người còn lại thấy thông báo xung đột; TADSS có đúng một override.

**Điều kiện trước:** một booking `PENDING_APPROVAL` của `CUS-REVIEW` ở `CN-A`; `MGR-A1` và `MGR-A2` đăng nhập ở hai ngữ cảnh trình duyệt riêng, cùng mở hộp thoại duyệt của đơn đó.

| # | Tác nhân → thao tác | Kỳ vọng |
| :---: | :--- | :--- |
| 1 | `MGR-A1` nhập 30 % và lý do R1; `MGR-A2` nhập 40 % và lý do R2 | — |
| 2 | Hai người bấm gửi cùng lúc (Playwright kích hoạt song song) | API: đúng một response 200 và một 409 `APPROVAL_ALREADY_RESOLVED` |
| 3 | — | UI người thắng: duyệt thành công. UI người thua: "Đơn đã được xử lý trước đó…", hàng chờ tải lại, đơn không còn |
| 4 | Kiểm DB và TADSS | DB: booking `PENDING`, nguồn cọc `MANAGER`, `depositRate` = tỷ lệ của người thắng, `resolvedBy` = người thắng, không có Payment (duyệt không tạo Payment, P6-10). TADSS: K-OVR(1) với `actor_id` và `deposit_rate` của người thắng |
| 5 | — | Cả hai trang nhận `PendingApprovalQueueChanged` (`REMOVED`/`APPROVED`) |
| 6 | Lặp bước 1–5 với booking mới: `MGR-A1` duyệt, `MGR-A2` từ chối, gửi cùng lúc | Như trên: một 200, một 409 `APPROVAL_ALREADY_RESOLVED`; trạng thái booking theo người thắng; K-OVR(1) |
| 7 | Kiểm K-SC6 | 0 vi phạm |

Người thua có thể bị chặn ở bước kiểm DB của BE, ở TADSS (`IDEMPOTENCY_KEY_REUSE` hoặc `ALREADY_OVERRIDDEN`) hay ở câu lệnh cập nhật có điều kiện; mọi đường đều trả cùng mã (DEPOSIT_APPROVAL_API Mục 7). Ghi nhận đường nào xảy ra (từ log) chỉ để mô tả.

**Minh chứng:** trace của cả hai ngữ cảnh; ảnh màn hình người thua; panel "Override theo hành động".

**Tiêu chí đạt:** ở cả hai lượt, đúng một 200, một 409 `APPROVAL_ALREADY_RESOLVED` và đúng một override; booking khớp người thắng; K-SC6 đạt.

**Liên quan:** KPI SC6 · BE-UT-04, BE-IT-13, BE-IT-15, IT-07 · FE-06 · câu hỏi mở: Q9 (chỉ khi booking còn chờ mà TADSS báo xung đột).

### E2E-08 — Phạm vi chi nhánh

**Mục tiêu:** Manager chỉ thấy và xử lý đơn chờ của chi nhánh được gán; `OWNER` thấy mọi chi nhánh; `STAFF`/`CUSTOMER` không vào được chức năng duyệt.

**Điều kiện trước:** booking `PENDING_APPROVAL` `bA` ở `CN-A` và `bB` ở `CN-B` (đều của `CUS-REVIEW`); `MGR-A1`, `MGR-B1`, `OWNER-1`, `STAFF-A1`, `CUS-STD` đăng nhập ở các ngữ cảnh riêng.

| # | Tác nhân → thao tác | Kỳ vọng |
| :---: | :--- | :--- |
| 1 | `MGR-A1` mở hàng chờ | API: `GET /api/bookings/pending-approval` (không `branchId` hoặc `branchId` = `CN-A`) → có `bA`, không có `bB`. UI: không có bộ chọn chi nhánh |
| 2 | `MGR-A1` gọi API cùng phiên với `branchId` = `CN-B` | 403 envelope, `code = FORBIDDEN` |
| 3 | `MGR-A1` gọi API cùng phiên `POST /api/bookings/{bB}/approve` với body hợp lệ | 403 envelope `FORBIDDEN`. DB: `bB` không đổi. TADSS: K-OVR(0) cho `bB` |
| 4 | Khi trang của `MGR-A1` và `MGR-B1` đang mở, `CUS-REVIEW` tạo thêm `bB2` ở `CN-B` | Trang `MGR-B1`: `bB2` xuất hiện. Trang `MGR-A1`: không đổi (không nhận sự kiện của `branch_{CN-B}`) |
| 5 | `MGR-B1` mở hàng chờ | Có `bB`, `bB2`; không có `bA` |
| 6 | `OWNER-1` mở hàng chờ không lọc, rồi lọc `CN-B` | Không lọc: có `bA`, `bB`, `bB2`. Lọc `CN-B`: chỉ `bB`, `bB2`. Ảnh chụp chỉ lấy ở trạng thái đã lọc (Mục 3.5) |
| 7 | `STAFF-A1` và `CUS-STD` tìm chức năng duyệt; gọi API hàng chờ cùng phiên | UI: không có menu/trang hàng chờ (RoleGuard). API: 403 **không** theo envelope (`{ "message": "Forbidden", … }`) |
| 8 | Gọi API hàng chờ không đăng nhập | 401 không theo envelope |
| 9 | Dọn: `MGR-A1` từ chối `bA`, `MGR-B1` từ chối `bB`, `bB2`; kiểm K-SC6 | 0 vi phạm |

**Minh chứng:** trace các ngữ cảnh; response 403/401 ở bước 2, 3, 7, 8; ảnh hàng chờ của `MGR-A1`, `MGR-B1`, `OWNER-1` (đã lọc).

**Tiêu chí đạt:** không ngữ cảnh nào thấy hay xử lý được đơn ngoài phạm vi; mã/HTTP status đúng từng bước; K-SC6 đạt.

**Liên quan:** KPI SC6 · BE-IT-05, BE-IT-11, BE-IT-18 · FE-07, FE-10 · câu hỏi mở: Q5, Q11.

### E2E-09 — Staff thu phần còn lại

**Mục tiêu:** booking đã cọc, khách đến chơi; Staff check-in rồi checkout, thu đúng phần còn lại; TADSS nhận outcome `COMPLETED`.

**Điều kiện trước:** một booking của `CUS-STD` ở `CN-A` theo E2E-01 bước 1–6 (`PAID_ONLINE`, tỷ lệ 0.5), giờ bắt đầu trong khoảng cho phép check-in khi tới bước 3 (Mục 2.3); `STAFF-A1` đăng nhập.

| # | Tác nhân → thao tác | Kỳ vọng |
| :---: | :--- | :--- |
| 1 | `STAFF-A1` mở chi tiết booking | API: `GET /api/bookings/{bookingId}` → `depositAmount` D, `remainingAmount` R = F − D, `paymentStatus = PARTIALLY_PAID`. UI: hiện D và R |
| 2 | (Biến thể) `STAFF-A1` thêm một dịch vụ giá S | API: `POST /api/bookings/{id}/services` thành công; chi tiết mới: `remainingAmount = F + S − D` (tính trên `finalTotal` hiện tại); D không đổi |
| 3 | `STAFF-A1` check-in | API: `POST /api/bookings/{id}/check-in` → booking `IN_PROGRESS`; sự kiện `BookingCheckedIn` |
| 4 | `STAFF-A1` checkout | UI: hộp xác nhận hiện số cần thu = `remainingAmount` hiện tại. API: `POST /api/bookings/{id}/checkout` thành công; sự kiện `BookingCheckedOut` |
| 5 | Kiểm DB | Booking `COMPLETED`, hóa đơn `PAID`, tổng đã thu (VNPay + tiền mặt) = `finalTotal` hiện tại |
| 6 | Chờ job outcome | K-OUT(`COMPLETED`) (booking có thời điểm check-in, P6-26) |
| 7 | Kiểm K-SC6 | 0 vi phạm |

**Minh chứng:** ảnh ở bước 1, 4; bản ghi Payment của booking.

**Tiêu chí đạt:** số thu tại quầy = `remainingAmount`; hóa đơn `PAID`; booking `COMPLETED`; một outcome; K-SC6 đạt.

**Liên quan:** KPI SC6 · BE-UT-07, BE-IT-09, BE-IT-10 · FE-09 · use case: UC-BOOKING-05, UC-BOOKING-06 · câu hỏi mở: không.

### E2E-10 — Hàng chờ realtime

**Mục tiêu:** hàng chờ của Manager cập nhật không cần tải lại trang khi có đơn mới hoặc khi đơn được xử lý; khi mất kết nối, FE tải lại qua REST nên không mất đơn.

**Điều kiện trước:** `MGR-A1` mở hàng chờ `CN-A`, kết nối SignalR đã thiết lập; `MGR-A2` đăng nhập ở ngữ cảnh khác; `STAFF-A1` mở màn hình của mình.

| # | Tác nhân → thao tác | Kỳ vọng |
| :---: | :--- | :--- |
| 1 | Ghi số đơn N và badge trên trang `MGR-A1` | — |
| 2 | `CUS-REVIEW` tạo đơn chờ duyệt ở `CN-A` | Trang `MGR-A1` (không tải lại): nhận `BookingPendingApproval`, `PendingApprovalQueueChanged` (`ADDED`/`NEW`), gọi lại `GET /api/bookings/pending-approval`; đơn mới xuất hiện, badge = N + 1 (từ `totalItems`) |
| 3 | `MGR-A2` duyệt đơn đó | Trang `MGR-A1` (không tải lại): đơn biến mất (`REMOVED`/`APPROVED`), badge = N |
| 4 | Làm đứt kết nối SignalR của ngữ cảnh `MGR-A1` và **xác nhận đã đứt** trước khi đi tiếp: chế độ offline của Playwright có thể không đóng WebSocket đang mở, nên đóng hẳn kết nối tới `/hubs/notifications` (ví dụ `page.routeWebSocket` đóng kết nối, kèm chế độ offline để chặn kết nối lại); quan sát được ở chỉ báo kết nối trên UI (trạng thái "đang kết nối lại"/mất kết nối, chốt ở T6.11) hoặc, nếu UI không có chỉ báo, ở trace (khung WebSocket đóng). Khi đã đứt: `CUS-REVIEW` tạo đơn chờ duyệt khác; rồi bật mạng/bỏ chặn | Sau khi kết nối lại (UI về trạng thái đã kết nối, hoặc trace có lần negotiate/WebSocket mới), FE gọi lại `GET /api/bookings/pending-approval` — request này nằm trong trace **sau** mốc kết nối lại; đơn mới xuất hiện, badge tăng 1, dù sự kiện phát lúc mất kết nối đã mất. Không quan sát được việc đứt kết nối thì bước này không đạt (không kết luận từ chế độ offline đơn thuần) |
| 5 | — (màn hình `STAFF-A1`) | Không có thay đổi giao diện do hai sự kiện hàng chờ (Q5) |
| 6 | Dọn: `MGR-A1` từ chối đơn ở bước 4; kiểm K-SC6 | 0 vi phạm |

**Minh chứng:** trace có khung WebSocket/SignalR (gồm lúc đóng và lúc kết nối lại ở bước 4) và các request REST gọi lại; ảnh chỉ báo kết nối lúc mất và lúc có lại kết nối (nếu UI có); mốc thời gian sự kiện và lúc đơn hiện trên UI.

**Tiêu chí đạt:** bước 2, 3, 4 đúng mà không tải lại trang thủ công; K-SC6 đạt.

**Liên quan:** KPI SC6 · BE-IT-18 · FE-08, FE-10 · câu hỏi mở: Q5.

### E2E-11 — TADSS dừng khi duyệt

**Mục tiêu:** khi Manager duyệt mà TADSS không phản hồi ⇒ không có đường lùi cọc 100 %, Manager được báo thử lại, booking vẫn chờ duyệt; TADSS chạy lại thì thử lại thành công với đúng một override.

**Lý do thêm vào danh mục:** quyết định "khi duyệt/từ chối không có đường lùi 100 %" (DEPOSIT_APPROVAL_API, quyết định 4; Mục 5 dòng 13–16) chưa có E2E nào phủ; E2E-06 chỉ dừng TADSS lúc đặt sân.

**Điều kiện trước:** một booking `PENDING_APPROVAL` ở `CN-A` với cửa sổ 30 phút; `MGR-A1` mở hàng chờ; trang chờ của `CUS-REVIEW` đang mở. Toàn bộ kịch bản phải xong trước `approvalExpiresAt`.

| # | Tác nhân → thao tác | Kỳ vọng |
| :---: | :--- | :--- |
| 1 | `docker compose stop tadss-runtime` | — |
| 2 | `MGR-A1` duyệt 30 % với lý do R | API: `POST /api/bookings/{bookingId}/approve` → 503 `TADSS_UNAVAILABLE`. UI: thông điệp "hệ thống duyệt cọc tạm thời không phản hồi, đơn vẫn chờ duyệt" do FE chọn theo `code` (không phải thông báo lỗi chung); hộp thoại giữ nguyên 30 % và R; có nút "Thử lại" |
| 3 | Kiểm DB và giao diện khách | DB: booking vẫn `PENDING_APPROVAL`, nguồn cọc `null`, không có Payment, slot lock không đổi. Trang khách: vẫn "Chờ duyệt cọc", không nhận `BookingApprovalResolved`. Hàng chờ (tải lại) vẫn có đơn |
| 4 | `docker compose start tadss-runtime`; chờ `/health/ready` 200 | — |
| 5 | `MGR-A1` bấm "Thử lại" | API: cùng body (`depositRate = 0.3`, cùng R) → 200, `status = PENDING`, `depositSource = MANAGER`. UI: đơn rời hàng chờ; trang khách nhận `BookingApprovalResolved` |
| 6 | Kiểm TADSS | K-OVR(1) với `actor_id` của `MGR-A1`, `idempotency_key = {decisionId}:APPROVE` |
| 7 | Kiểm K-SC6 | 0 vi phạm |

**Minh chứng:** ảnh ở bước 2, 3, 5; thời điểm dừng/chạy lại TADSS; panel "Override theo hành động"; log của BE ở bước 2 có lỗi gọi override kèm correlation id (`docker compose logs smashcourt-be` trong `SmashCourt-BE/`, như E2E-06; TADSS đang dừng nên Loki của TADSS không có log cho bước này).

**Tiêu chí đạt:** bước 2 là 503 `TADSS_UNAVAILABLE`, booking không đổi, không cọc 100 %; bước 5 thành công; đúng một override; K-SC6 đạt.

**Liên quan:** KPI SC6 · BE-UT-04, BE-IT-14 · FE-06 · câu hỏi mở: không.

### E2E-12 — Bấm duyệt lần hai

**Mục tiêu:** cùng Manager gửi duyệt lần hai từ một tab cũ sau khi lần đầu đã thành công ⇒ 409 `APPROVAL_ALREADY_RESOLVED`, BE không gọi TADSS lần hai, kết quả lần đầu giữ nguyên.

**Lý do thêm vào danh mục:** quy tắc "bấm lần hai sau khi thành công ⇒ 409, không trả lại kết quả cũ" (DEPOSIT_APPROVAL_API 4.2 và Mục 7) chưa có E2E; E2E-07 chỉ phủ hai Manager khác nhau.

**Điều kiện trước:** một booking `PENDING_APPROVAL` ở `CN-A`; `MGR-A1` mở hàng chờ ở hai tab của cùng một phiên đăng nhập.

| # | Tác nhân → thao tác | Kỳ vọng |
| :---: | :--- | :--- |
| 1 | Tab 2: mở hộp thoại duyệt của đơn, nhập 30 % và lý do R, chưa gửi | — |
| 2 | Tab 1: duyệt 30 % với lý do R | API: 200, `status = PENDING` |
| 3 | Tab 2: bấm gửi. Nếu thiết kế UI đóng hộp thoại khi đơn rời hàng chờ (T6.11), gửi cùng request bằng API của Playwright trong cùng phiên | API: 409 `APPROVAL_ALREADY_RESOLVED`. UI: "Đơn đã được xử lý trước đó…", tải lại hàng chờ/chi tiết thấy kết quả của bước 2 |
| 4 | Kiểm DB và TADSS | DB: booking chỉ chuyển trạng thái một lần, không có Payment (duyệt không tạo Payment, P6-10). TADSS: K-OVR(1); log của TADSS (Loki) hoặc trace (Tempo) trong khoảng thời gian của kịch bản có đúng một request override cho `decisionId` này |
| 5 | Kiểm K-SC6 | 0 vi phạm |

**Minh chứng:** trace của hai tab; truy vấn log/trace TADSS ở bước 4.

**Tiêu chí đạt:** 409 ở bước 3; đúng một request override tới TADSS và một override; K-SC6 đạt.

**Liên quan:** KPI SC6 · BE-IT-13 · FE-05, FE-06 · câu hỏi mở: không.

### E2E-13 — Đặt sân tại quầy không qua TADSS

**Mục tiêu:** booking tại quầy (`WALK_IN`) giữ nguyên hành vi Đồ án 1 khi tích hợp TADSS đang bật: không quyết định cọc, không chờ duyệt.

**Lý do thêm vào danh mục:** quy tắc "chỉ booking `ONLINE` đi qua TADSS; `WALK_IN` không bao giờ có quyết định cọc" (INTEGRATION_SPEC §4.2; DEPOSIT_APPROVAL_API, quyết định 6) cần hồi quy luồng UC-BOOKING-02 trên hệ thống đã tích hợp; E2E-01…E2E-10 chỉ có booking online.

**Điều kiện trước:** `EnableTadssDecision = true`, TADSS `/health/ready` 200; `STAFF-A1` đăng nhập; một slot trống ở `CN-A`.

| # | Tác nhân → thao tác | Kỳ vọng |
| :---: | :--- | :--- |
| 1 | `STAFF-A1` tạo booking tại quầy cho khách vãng lai (tên, số điện thoại thử) theo UC-BOOKING-02 | API: `POST /api/bookings/walk-in` → thành công, `BookingDto` có `status = CONFIRMED`, `source = WALK_IN`, `depositRate`, `depositAmount`, `remainingAmount`, `depositSource`, `approvalExpiresAt` đều `null`. UI: không có thông tin cọc hay trạng thái chờ duyệt |
| 2 | Kiểm TADSS | K-NODEC |
| 3 | `MGR-A1` mở hàng chờ | Booking không có trong hàng chờ |
| 4 | `MGR-A1` gọi API cùng phiên `POST /api/bookings/{bookingId}/approve` với body hợp lệ | 409 `BOOKING_NOT_PENDING_APPROVAL`; booking không đổi |
| 5 | — (time grid) | Sự kiện `BookingCreated`; slot hiện đã đặt |
| 6 | Kiểm K-SC6 | 0 vi phạm |

**Minh chứng:** ảnh ở bước 1; kết quả K-NODEC.

**Tiêu chí đạt:** booking `CONFIRMED` không trường cọc; K-NODEC; 409 ở bước 4; K-SC6 đạt.

**Liên quan:** KPI SC6 · BE-UT-05, BE-UT-07, BE-IT-16 · FE-09 · use case: UC-BOOKING-02 · câu hỏi mở: không.

---

### E2E-14 — Đặt sân bị từ chối theo chính sách

**Mục tiêu:** khách lặp lại việc giữ chỗ rồi để hết hạn thanh toán bị từ chối ngay lúc đặt; không có gì để thanh toán, không giữ slot, không vào hàng chờ duyệt.

**Lý do thêm vào danh mục:** nhánh `REJECTED` của luồng đặt sân (DEPOSIT_APPROVAL_API, Q1) chưa có kịch bản kiểm; strategy mặc định 2.1.0 là nơi đầu tiên sinh ra trạng thái này.

**Điều kiện trước:** `CUS-EXPIRY` thỏa điều kiện R (Mục 3.3); `EnableTadssDecision = true`, TADSS `/health/ready` 200; một slot trống ở `CN-A`; `MGR-A1` mở hàng chờ.

| # | Tác nhân → thao tác | Kỳ vọng |
| :---: | :--- | :--- |
| 1 | `CUS-EXPIRY` đặt slot | API: 400 `BOOKING_REJECTED`, `data = null`. UI: thông báo không nhận đơn, gợi ý chọn khung giờ khác hoặc liên hệ chi nhánh; không hiển thị lý do hay lịch sử của khách |
| 2 | Kiểm TADSS | K-DEC với `status = REJECTED`, `reason_code = REPEATED_PAYMENT_EXPIRY`, `deposit_rate`/`deposit_amount`/`approval_expires_at` rỗng, `strategy_version = 2.1.0`; K-OVR(0) |
| 3 | Kiểm DB | Booking `CANCELLED` (hủy trước khi trả lỗi), không có slot lock, không có Payment |
| 4 | `MGR-A1` mở hàng chờ | Không có đơn của `CUS-EXPIRY`; không nhận `BookingPendingApproval` |
| 5 | `CUS-STD` đặt đúng slot đó | 201 như bình thường (slot đã được nhả) |
| 6 | Sau chu kỳ job outcome: K-OUT(`REJECTED`) | `decision_outcomes` có đúng một dòng `REJECTED` |
| 7 | Kiểm K-SC6 | 0 vi phạm |

**Minh chứng:** ảnh ở bước 1; kết quả K-DEC, K-OUT.

**Tiêu chí đạt:** 400 `BOOKING_REJECTED`; K-DEC đúng; booking `CANCELLED` không Payment; slot đặt lại được; K-SC6 đạt.

## 6. Ma trận truy vết

Endpoint TADSS là endpoint BE gọi (FE không bao giờ gọi TADSS). Use case theo `docs/use-cases/` của repo SmashCourt-BE.

| E2E | Endpoint BE | Sự kiện SignalR | Endpoint TADSS | BE | FE | Use case |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| E2E-01 | `POST /api/bookings/online`; `GET /api/payments/vnpay/callback`, `/ipn`; `GET /api/me/bookings/{id}` | `BookingCreated`, `PaymentSuccess` | `POST /api/v2/decisions` | BE-UT-02, BE-UT-07, BE-IT-02, CT-04 | FE-04, FE-09 | UC-BOOKING-01, UC-PAYMENT-01 |
| E2E-02 | `POST /api/bookings/online`; `GET /api/bookings/pending-approval`; `POST /api/bookings/{bookingId}/approve`; `GET /api/me/bookings/{id}`; `POST /api/me/bookings/{id}/retry-payment`; VNPay | `BookingPendingApproval`, `PendingApprovalQueueChanged`, `BookingApprovalResolved`, `PaymentSuccess` | `POST /api/v2/decisions`; `POST /api/v2/decisions/{decisionId}/override` | BE-UT-03, BE-IT-03, BE-IT-05, BE-IT-06, BE-IT-07, BE-IT-18 | FE-04, FE-05, FE-08, FE-10 | UC-BOOKING-01, UC-PAYMENT-01 |
| E2E-03 | `POST /api/bookings/{bookingId}/reject`; `GET /api/me/bookings/{id}`; `GET /api/branches/{branchId}/courts/{courtId}/time-grid`; `POST /api/bookings/online` | `BookingApprovalResolved`, `BookingCancelled`, `PendingApprovalQueueChanged` | `.../override`; `.../outcome` | BE-IT-06, BE-IT-09, BE-IT-18 | FE-06, FE-08, FE-09 | UC-BOOKING-01 |
| E2E-04 | `POST /api/bookings/online`; `POST /api/bookings/{bookingId}/approve` (sau hạn); `GET /api/me/bookings/{id}` | `BookingExpired`, `PendingApprovalQueueChanged` | `POST /api/v2/decisions`; `.../outcome` | BE-UT-05, BE-IT-08, BE-IT-09, BE-IT-12 | FE-06, FE-08 | UC-BOOKING-01 |
| E2E-05 | `POST /api/bookings/online` (nhánh d khi cờ tắt; nhánh b trước `t_off`, nhánh a sau khi bật lại); `POST /api/bookings/{bookingId}/approve` (Q7); VNPay | `BookingCreated`, `PaymentSuccess`, `BookingApprovalResolved` | không gọi `POST /api/v2/decisions` khi cờ tắt (chỉ gọi trước `t_off` và sau khi bật lại); `.../override` cho `bQ` (Q7) | BE-IT-04, BE-IT-19 | FE-04 | UC-BOOKING-01, UC-PAYMENT-01 |
| E2E-06 | `POST /api/bookings/online` (nhánh d) | `BookingCreated` | `POST /api/v2/decisions` (lỗi kết nối) | BE-IT-04 | FE-04 | UC-BOOKING-01 |
| E2E-07 | `POST /api/bookings/{bookingId}/approve` (hai phiên), `/reject` | `BookingApprovalResolved`, `PendingApprovalQueueChanged` | `.../override` | BE-UT-04, BE-IT-13, BE-IT-15 | FE-06 | — |
| E2E-08 | `GET /api/bookings/pending-approval` (không/đúng/sai `branchId`); `POST /api/bookings/{bookingId}/approve` (chi nhánh khác) | `BookingPendingApproval` (chỉ `branch_{branchId}` của mình) | không gọi khi bị chặn | BE-IT-05, BE-IT-11, BE-IT-18 | FE-07, FE-10 | — |
| E2E-09 | `GET /api/bookings/{bookingId}`; `POST /api/bookings/{id}/services`; `POST /api/bookings/{id}/check-in`; `POST /api/bookings/{id}/checkout` | `BookingCheckedIn`, `BookingCheckedOut` | `.../outcome` | BE-UT-07, BE-IT-09, BE-IT-10 | FE-09 | UC-BOOKING-05, UC-BOOKING-06 |
| E2E-10 | `GET /api/bookings/pending-approval` (tải lại khi có sự kiện, khi kết nối lại) | `BookingPendingApproval`, `PendingApprovalQueueChanged` | — | BE-IT-18 | FE-08, FE-10 | — |
| E2E-11 | `POST /api/bookings/{bookingId}/approve` (503 rồi 200) | `BookingApprovalResolved`, `PendingApprovalQueueChanged` (chỉ sau lần thành công) | `.../override` (lỗi kết nối, rồi thành công) | BE-UT-04, BE-IT-14 | FE-06 | — |
| E2E-12 | `POST /api/bookings/{bookingId}/approve` (hai tab) | `BookingApprovalResolved`, `PendingApprovalQueueChanged` (một lần) | `.../override` (đúng một lần) | BE-IT-13 | FE-05, FE-06 | — |
| E2E-14 | `POST /api/bookings/online` (nhánh c: bị từ chối); `GET /api/bookings/pending-approval` | không có `BookingPendingApproval` | `POST /api/v2/decisions` (`REJECTED`); `.../outcome` | BE-IT-02, BE-IT-09 | FE-04 | UC-BOOKING-01 |
| E2E-13 | `POST /api/bookings/walk-in`; `POST /api/bookings/{bookingId}/approve` | `BookingCreated` | không gọi | BE-UT-05, BE-UT-07, BE-IT-16 | FE-09 | UC-BOOKING-02 |

KPI: mọi dòng liên quan SC6; E2E-05 thêm SC5 (minh họa, số đo ở FI-13). Bảng truy vết E2E ↔ endpoint ↔ sự kiện ở phía hợp đồng BE ↔ FE: DEPOSIT_APPROVAL_API Mục 8.

---

## 7. Hành vi không tách thành E2E riêng

| Hành vi | Vì sao không thành E2E | Được kiểm ở |
| :--- | :--- | :--- |
| Fallback của runtime (`APPROVED` 100 %, `fallbackUsed = true`) và `APPROVAL_WINDOW_UNAVAILABLE` | Giao diện giống E2E-01 với `depositRate = 1`, `depositSource = TADSS`; tiêm lỗi strategy chỉ có ở môi trường `Testing` | FI-02…FI-04, UT-10 (TADSS); BE-IT-02 |
| TADSS chậm hơn `TimeoutMs`, đứt mạng BE ↔ TADSS | Kịch bản tiêm lỗi mạng của SV1 trên E-STAGING | FI-12, FI-14; BE-IT-04 |
| Thử lại sau 503 khi TADSS thực ra đã ghi override | Cần chặn đúng lúc sau khi TADSS ghi, khó tạo ổn định trên E-STAGING | BE-IT-13 |
| Khách hủy khi đang chờ duyệt hoặc chưa thanh toán | Đã chốt (Q8, P6-09); thao tác ngắn, không cần TADSS | BE-IT-23, FE-09 |
| Thanh toán thất bại, ân hạn 5 phút, hạn giữ chỗ tuyệt đối (P6-02, P6-03) | Thẻ lỗi kiểm ở biến thể của E2E-01; chờ hết ân hạn và bấm thanh toán lại liên tục tốn thời gian, kiểm ở mức BE | E2E-01 (biến thể), BE-UT-08, BE-IT-21, FE-11 |
| Cọc 0 % và cọc dưới mức tối thiểu (Q4, P6-06) | Đã chốt; không qua VNPay nên không có gì thêm để quan sát ở UI ngoài trang kết quả | BE-IT-07, BE-IT-24, FE-05, FE-11 |
| Tiền về sau khi đơn đã hủy (P6-07) | Trước P6-27 tái hiện được bằng tay (mở trang VNPay, chờ Job-01 hủy rồi trả trong khoảng 15 phút trang còn sống; đã làm 28/10, spec 10.1 bước 2) nhưng tốn khoảng 12 phút mỗi lần; từ 6A-6 link có hạn nên ca này chỉ còn xảy ra khi IPN về muộn hơn khoảng đệm | BE-IT-22 |

---

## 8. Việc chốt khi hiện thực (Phase 6–7)

| Việc | Khi nào | Ai |
| :--- | :--- | :--- |
| Quy ước selector (ví dụ thuộc tính `data-testid`) và cấu trúc thư mục test Playwright | T5.25, T6.15 | SV2 |
| Câu SQL chỉ đọc của K-DEC, K-NODEC, K-OVR, K-OUT, K-SC6 (theo tên cột chốt ở T6.1) | T6.10, T7.3 | SV1 |
| Cách đổi `EnableTadssDecision` lúc chạy trên E-STAGING | T6.2, T7.2 | SV1 |
| Chọn điều kiện N1/N2/N3 và ghi vào ghi chú dựng lại | T7.2 | SV1 |
| ~~Chu kỳ job outcome và ánh xạ trạng thái → outcome~~ | Đã chốt 27/10 (spec Phase 6 Mục 4.4.1, 7.11, P6-26) | SV1 |
| Lưới khung giờ của `CN-A` để lên lịch E2E-04 (hết hạn sau 3–10 phút) và E2E-09 (trong cửa sổ check-in) | T7.2 | SV1 |
| Cách giả lập SignalR và trang VNPay trên MSW | T6.15 | SV2 |
| Chỉ báo trạng thái kết nối SignalR trên UI và cách làm đứt kết nối ở E2E-10 bước 4 | T6.11, T6.15 | SV2 |
| Cập nhật tài liệu này theo kết luận Q1–Q11 của SV2 | T5.29 | SV2, SV1 |
