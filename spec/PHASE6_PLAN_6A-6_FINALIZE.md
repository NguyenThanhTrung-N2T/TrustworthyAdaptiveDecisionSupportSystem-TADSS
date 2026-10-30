# Gói 6A-6 — Hoàn thiện BE: hạn link thanh toán, kết quả thanh toán theo trạng thái đơn, lỗi thời gian còn lại, bộ kiểm SC6, kịch bản tải LT-04

**Mục tiêu (spec 5.1, 6A-6):** gói cuối của Epic 6A. Hoàn thành khi: **mọi BE-UT/BE-IT trong danh mục đạt (gồm BE-IT-20, BE-IT-25); không còn đường khách trả tiền vào một link VNPay đã quá hạn giữ chỗ mà BE không chặn được ở phía BE; trang kết quả thanh toán không báo "thành công" cho đơn đã hủy.** Sau gói này BE sẵn sàng cho FE 6B và Phase 7 (E-STAGING); bật cờ `EnableTadssDecision` vẫn là quyết định riêng của người dùng.

**Phạm vi (chốt 29/10, người dùng duyệt):** P6-27 kèm T-5 (Phần A); N-36 (Phần B); T-9…T-11, phần còn lại của BE-IT-25 (Phần C); BE-IT-20 bằng bộ kiểm SC6 viết bằng C# (Phần D); hộp thư giả Mailpit, lỗi SMTP trong container, kịch bản k6 LT-04 (Phần E); tài liệu và kiểm cuối gói (Phần F). **P6-21 không làm** trong Phase 6.

**Spec:** 2.1, 3.2 (N-15, N-36), 3.3 (T-5, T-9…T-11), 4.2 (P6-07, P6-22, **P6-27…P6-30**), 4.3, 5.1 ("Đầu vào cho plan 6A-6"), 7.5, 7.8, 7.14, 8.2, 9.1, 9.3, 10.1 bước 2, Mục 11, 12. API: DEPOSIT_APPROVAL_API 3.1, 3.3, 3.6, Mục 5. Methodology: Mục 4.6 (LT-04), Mục 5 (bộ truy vấn bất biến), BE-IT-20, BE-IT-25.

**Test (danh mục):** BE-UT-08 (dòng hạn link), BE-UT-12 (giờ GMT+7 của VNPay); BE-IT-20, BE-IT-25 (T-9, T-10, T-11); LT-04 (chỉ chạy thử, lượt chính thức ở Phase 7).

**Nhánh:** `SmashCourt-BE` `feat/phase-6-deposit` (nền `8cd53b8`); `TADSS-Runtime` `develop` (k6 LT-04, tài liệu). TADSS không đổi mã nguồn `src/`.

**Trạng thái:** plan bản 3 (29/10/2026). Đang làm: Task 0 ✅, Task 1 ✅, Task 2 ✅, Task 3 + 4 ✅, Task 5 ✅, Task 16 ✅, Task 6 ✅, Task 7 ✅, Task 8 ✅ (30/10); Task 8a, 8b thêm 30/10 (T-15, T-16); kết quả ở mục "Kết quả thực hiện". Bản 3 thêm bốn tiêu chí nghiệm thu từ vòng review thứ hai (mục 0.6, R26–R31): cách xác nhận S1 không phụ thuộc BE, hành vi khi cổng không hỗ trợ hạn link, tập dữ liệu và báo cáo cố định của SC6 staging, nguồn gốc giá trị kỳ vọng của LT-04; rà `updated_at` cho phạm vi tự kiểm. Bản 2 sửa theo hai bản review ngoài cùng ngày (kết quả từng điểm ở mục 0.6): tiêu chí đối chứng URL ba mức, câu hoàn tiền của N-36 dựa trên Payment của chính giao dịch, SC6-4 tách hai điều, bộ kiểm đọc trong một snapshot, phạm vi tự kiểm gồm cả đơn bị sửa, kết quả staging có trạng thái "không kết luận được", LT-04 có mức cọc kỳ vọng độc lập, ca T-10 theo ràng buộc `end_time > start_time`, thứ tự làm và ước lượng.

**Cách đọc mỗi task:** *Vấn đề* → *Giải pháp* → *Vì sao* (kèm phương án đã cân nhắc) → *Ảnh hưởng* → *Kiểm*.

---

## 0. Rà soát

### 0.1. Mã đã có, dùng lại được

| Thứ đã có | Dùng cho |
| :--- | :--- |
| `IPaymentGateway` (`CreatePaymentLink`, `ReadResult`), `IPaymentGatewayResolver`, `OnlinePaymentStarter` (một nơi tạo Payment `PENDING` cho mọi cổng) | Phần A: đổi chữ ký để truyền hạn link, không đổi kiến trúc nhiều cổng |
| `VnPayService.IsSignatureValid` + `HmacSha512` + `VnPayCompare` (kiểm chữ ký IPN, đã đúng thư viện mẫu của VNPay) | Phần A: tách hàm ký dùng chung cho cả chiều tạo link |
| `VnPayService.ResolveClientIp`, `NormalizeIpAddress`, `NormalizeOrderInfo` (đang có, hai hàm đầu hiện không ai gọi) | `vnp_IpAddr`, `vnp_OrderInfo` của URL tự dựng |
| `PaymentHoldPolicy` (hàm thuần, mốc UTC; `TryForRetry`, `TryForApproval`, `ForNewBooking`) + `BookingPaymentSettings` + validator | Phần A: thêm hàm hạn link, khóa cấu hình khoảng đệm |
| `ErrorCodes.PaymentHoldExpired` (400, đã dùng ở `retry-payment`) | Từ chối khi không còn đủ thời gian (không thêm mã lỗi) |
| `DateTimeHelper.ToVietnamTime`, `ToUtcFromVietnam`, `GetTodayInVietnam` | T-5, T-9, T-11 |
| `PaymentService.HandleVnPayReturnAsync` đã đọc lại trạng thái đơn từ DB (`GetBookingStatusAsync`) | Phần B: chỉ cần trả trạng thái và chọn câu theo nó |
| Repository dashboard nhận `now` (giờ VN) làm tham số (`GetManagerDashboardKpisAsync`, `GetManagerDashboardLiveCourtsAsync`) | T-10 test được bằng `now = 23:45`, **không cần `TimeProvider`** (bỏ câu hỏi số 3 của spec 5.1) |
| `BookingStatusTransition` (`GetActiveStatuses`, `GetCancelledStatuses`, …, khóa bởi BE-UT-11) | Phần D: bộ kiểm SC6 dùng lại, không chép danh sách trạng thái |
| `PostgreSqlIntegrationFixture` (schema thật, `MapSmashCourtEnums()`), `IntegrationTestBase` (rollback), `HttpTestBase` (host thật, dữ liệu commit), `TadssStub`, `OnlineBookingTestBase` (IPN giả có chữ ký) | BE-IT-20, BE-IT-25, test HTTP của Phần A, B |
| k6 `tests/load/` của TADSS (lib `run.js`, `output.js`; evaluator chỉ chạy `lt01`, `lt02`, `lt03`, `lt05`, `scale2` theo danh sách cứng) | Phần E: thêm `lt04.js` không ảnh hưởng evaluator |

### 0.2. Mọi thay đổi của gói

| Mã | Thay đổi | Loại | Task |
| :---: | :--- | :--- | :---: |
| A1 | Link VNPay có hạn `vnp_ExpireDate = expiresAt − khoảng đệm`; còn ít hơn khoảng đệm + 1 phút ⇒ 400 `PAYMENT_HOLD_EXPIRED` | Lỗi thật N-15 (P6-27) | 1, 3, 4 |
| A2 | `vnp_CreateDate`, `vnp_ExpireDate` theo GMT+7 | T-5 (P6-27) | 2, 3 |
| A3 | BE tự dựng URL đã ký; gỡ `VNPAY.NET` khỏi BE (giữ trong project test làm đối chứng chữ ký) | Hệ quả của A1 | 2, 3 |
| A4 | Response đặt sân và `retry-payment` thêm `paymentLinkExpiresAt` | Thêm trường (không phá hợp đồng) | 4 |
| B1 | `vnpay/confirm` trả `bookingStatus` và câu theo trạng thái đơn | Lỗi thật N-36 (P6-28) | 5 |
| C1 | Báo cáo khách mới tính theo ngày VN; `toDate` mặc định là hôm nay giờ VN | Lỗi thật T-9 | 6 |
| C2 | "Sắp bắt đầu" không quay vòng qua nửa đêm | Lỗi thật T-10 | 7 |
| C3 | Lọc giao dịch điểm theo ngày VN, không 500 | Lỗi thật T-11 | 8 |
| D1 | Bộ kiểm SC6 bằng C# (chỉ đọc), tự chạy sau mỗi test HTTP; chạy được trên staging; điều 5 qua API TADSS | BE-IT-20 (P6-29) | 9, 10, 11 |
| E1 | Khóa `Smtp:EnableSsl` (mặc định `true`) + file compose phụ Mailpit cho E-STAGING và LT-04 | Hạ tầng test (P6-30) | 12 |
| E2 | Điều tra lỗi `Network unreachable` khi gửi email trong container | Môi trường | 13 |
| E3 | Kịch bản k6 LT-04 (đặt rồi hủy, mỗi khách một tập sân riêng) | Methodology 4.6 (P6-30) | 14 |

### 0.3. Quyết định đã chốt (29/10)

| # | Vấn đề | Chốt |
| :---: | :--- | :--- |
| **D1** | Khoảng đệm của link | **2 phút**, khóa `BookingPayment:PaymentLinkBufferMinutes`. Lý do: IPN thành công đo được 4–11 giây, IPN thất bại tới 53 giây (10.1 bước 2); 2 phút gấp hơn 2 lần trường hợp xấu nhất đã đo mà khách chỉ mất 2 trong 10 phút |
| **D2** | Còn quá ít thời gian | **Phương án A:** thời gian tối thiểu của một link là 1 phút (hằng `PaymentHoldPolicy.MinPaymentLinkLifetime`); `expiresAt − khoảng đệm − now < 1 phút` ⇒ không tạo link, 400 `PAYMENT_HOLD_EXPIRED`. Đơn vẫn `PENDING` tới khi Job-01 hủy theo hạn cũ |
| **D3** | Cách tạo URL | BE tự dựng và ký (thư viện không có `vnp_ExpireDate`). Hai mốc theo GMT+7. `vnp_TxnRef = Guid.NewGuid().ToString("N")` (32 ký tự hex chữ thường, duy nhất; cột `transaction_ref` 255 ký tự, đã có `uniq_payments_transaction_ref`). Gỡ `VNPAY.NET` khỏi BE; project test giữ gói này làm đối chứng. **Bằng chứng ba mức** (URL mới có thêm `vnp_ExpireDate` nên không thể trùng nguyên URL của thư viện): (1) cùng tập tham số ⇒ chuỗi ký, chữ ký và URL của helper trùng thư viện từng ký tự; (2) các tham số chung giữa `VnPayService` và thư viện trùng giá trị, cách mã hóa, thứ tự (trừ ba tham số khác có chủ đích: `vnp_CreateDate` GMT+7, `vnp_TxnRef` GUID, `vnp_ExpireDate` mới); (3) phần mở rộng: unit test kiểm GMT+7 và chữ ký, rồi URL do **mã C#** sinh ra được sandbox chấp nhận và trả thành công (Task 16, làm ngay sau Phần A + B). Task 0 dùng script Python nên chỉ chứng minh cách ký, chưa chứng minh mã C# |
| **D4** | FE biết hạn link thế nào | Thêm `paymentLinkExpiresAt` vào `OnlineBookingResponse` (null khi không có link). FE không cần biết khoảng đệm; 8.2 bỏ câu "FE khóa nút khi `expiresAt` còn dưới khoảng đệm" |
| **D5** | N-36 | `VnPayReturnResult` thêm `bookingStatus` (tên enum như `OnlineBookingResponse.status`). `isSuccess` **giữ nghĩa** "cổng báo giao dịch thành công" (không đổi nghĩa trường cũ). Câu thông báo chọn ở **một hàm thuần** theo (kết quả cổng, trạng thái đơn, trạng thái Payment của chính giao dịch đọc từ DB); câu "sẽ được hoàn đủ tại chi nhánh" **chỉ** khi đơn `CANCELLED_PENDING_REFUND` **và** Payment của giao dịch này đã `SUCCESS` (IPN đã ghi, P6-07 đã tạo Refund); các ca hủy khác dùng câu trung tính (Task 5) |
| **D6** | T-10 | Không tiêm `TimeProvider`; test gọi repository với `now = 23:45` |
| **D7** | SC6 | Bộ kiểm C# trong project test, dùng `BookingStatusTransition`; không có file SQL chứa quy tắc; mọi câu đọc của một lượt kiểm chạy trong **một transaction chỉ đọc `REPEATABLE READ`** (một snapshot, không đọc trạng thái nửa chừng khi job đang ghi). Tự chạy sau **mỗi** test HTTP (đơn sinh ra **hoặc bị sửa** trong test đó). Kết quả staging tách `violations` và `inconclusiveChecks`; còn mục không kết luận được thì lượt kiểm **không đạt** (không bao giờ coi lỗi gọi API là đạt). Staging chạy đúng bộ kiểm qua một test chỉ bật khi có biến môi trường. **Điều 5 gọi `GET /api/v2/decisions/{id}` của TADSS** (contract công khai) thay truy vấn chéo schema; báo cáo "quyết định mồ côi" (không phải điều kiện đạt) không có API nên đọc chỉ-đọc `tadss.decision_executions` (cột `decision_id`, `reservation_id`, `role`, `created_at`), logic so khớp ở C# |
| **D8** | LT-04 | Mỗi lượt: đặt ⇒ kiểm ⇒ **khách hủy ngay** (tránh `PAYMENT_EXPIRED` làm strategy 2.1.0 từ chối khách, tránh hết sân); mỗi khách ảo một tập sân và giờ riêng ở ngày xa; email vào Mailpit. **Hai lượt chứng minh hai điều khác nhau:** chạy thử ở 6A-6 (cờ TADSS tắt, 1 khách × 3 lượt) chỉ chứng minh script, xác thực, đặt/hủy, Mailpit và SC6, **không** chứng minh mức cọc của TADSS; lượt chính thức ở Phase 7 (cờ bật, 20 khách × 10 phút) so mức cọc với **giá trị kỳ vọng độc lập** (`LT04_EXPECTED_RATE` lấy từ cấu hình strategy của lượt chạy cho hạng STANDARD, khách mới không lịch sử), không lấy `depositRate` của chính response làm chuẩn |
| **D9** | P6-21 | Không làm trong Phase 6, ghi vào hướng phát triển (spec 4.2, Mục 12) |

### 0.4. Ngoài phạm vi

| Vấn đề | Lý do |
| :--- | :--- |
| Job đối soát bất biến chạy hằng đêm ở production | Thêm mã production, Gate M6 không cần; ghi vào hướng phát triển (spec Mục 11) |
| Đổi nghĩa `isSuccess` của `vnpay/confirm` | Phá nghĩa trường cũ; FE đọc `bookingStatus` |
| `ReportService` dùng `TimeProvider` | Spec 7.14: mã cũ giữ đồng hồ hệ thống; chỉ đổi cách lấy "hôm nay" |
| Bật `EnableTadssDecision`, chạm `tadss-dev`, áp script lên Neon | Không thuộc gói |

### 0.5. Việc người dùng làm

| Khi | Việc | Thời gian |
| :--- | :--- | :---: |
| Task 0 (trước khi code Phần A) | Mở 3–4 link VNPay sandbox do mình tạo, làm theo hướng dẫn từng bước (một link phải đợi qua hạn rồi mới nhập thẻ) | ~10 phút |
| Task 13, 14 | Cho phép chạy BE bằng Docker kèm Mailpit; chạy thử LT-04 trên `tadss-dev` (tạo 1 tài khoản `lt04-smoke-…@lt04.test` và 3 đơn đã hủy, ghi chú "LT04") | — |
| Task 16 (ngay sau Phần A + B) | Trả thử bằng link do BE mới sinh; mở một link sau hạn link; xem `confirm` | ~15 phút |
| Cuối gói | Review, commit (một commit mỗi repo) | — |

### 0.6. Kết quả xét hai bản review ngoài (29/10)

| # | Điểm review | Xét | Xử lý trong plan |
| :---: | :--- | :--- | :--- |
| R1 | Kiểm biên đúng 1 phút và nhỏ hơn một đơn vị thời gian | Nhận. So sánh chạy trong bộ nhớ trên `DateTime` (tick 100 ns); mốc đọc từ DB đã cắt về µs nhưng vẫn so ở tick | Task 1: ca đúng 3 phút và 3 phút − 1 tick |
| R2 | URL mới có `vnp_ExpireDate` nên không thể trùng nguyên URL thư viện; tách đối chứng và xác minh phần mở rộng | Nhận. Bản 1 đối chứng helper với **cùng** tham số (làm được), nhưng chưa nói phần mở rộng được chứng minh ở đâu; Task 0 dùng Python nên chưa chứng minh mã C# | D3 ba mức; Task 3 thêm test tham số chung; Task 16 dời lên ngay sau Phần A + B |
| R3 | S1 phải phân biệt "mở được trang" và "trả thành công" | Nhận | Task 0: S1 chỉ đạt khi trả thành công |
| R4 | Giữ ý "khoảng đệm không phải bảo đảm tuyệt đối"; ghi giới hạn của S2 | Nhận (đã có ở P6-27, spec Mục 11) | Task 0; Task 15 thêm dòng vào bảng ca của E2E_TEST_SPECIFICATION |
| R5 | Câu "hoàn đủ tại chi nhánh" chỉ khi thật sự áp dụng | Nhận, là lỗi của bảng câu bản 1: đơn `CANCELLED` có thể chưa nhận IPN, `CANCELLED_REFUNDED` có thể đã hoàn xong | D5; Task 5 bảng câu theo Payment của giao dịch |
| R6 | T-9 nhóm trong bộ nhớ: lưu ý quy mô | Nhận, chỉ ghi chú: mỗi dòng là một mốc `created_at` của khách mới trong khoảng lọc (cỡ trăm dòng); không tối ưu khi chưa có số liệu | Task 6 |
| R7 | T-10: dữ liệu test phải đúng quy ước domain | Nhận, **bản 1 sai**: `chk_booking_courts_valid_time` buộc `end_time > start_time` nên không có slot 23:55–00:00 | Task 7: slot 23:50–23:59 |
| R8 | SC6-1: chỉ Payment của đúng hóa đơn, chỉ `SUCCESS`, cọc 0 | Nhận | Task 9: thêm ca `PENDING`/`FAILED`/`REFUNDED` không được tính, cọc 0 |
| R9 | Đọc nhất quán trong một lượt kiểm | Nhận | D7: một transaction `REPEATABLE READ` chỉ đọc |
| R10 | SC6-4 có thể báo nhầm; `PENDING_PAYMENT` nhả sân sau giờ chơi, không dựa vào giả định "quá khứ không chồng được" | Nhận. Tách SC6-4a (đơn đang giữ sân có dòng sân bị tắt) và SC6-4b (chồng giờ giữa các dòng đang giữ sân); "`PENDING_PAYMENT` đã nhả sân" ghi tường minh, không suy từ thời gian | Task 9, bảng điều và 9 ca của SC6-4 |
| R11 | Tự kiểm chỉ đơn mới có thể bỏ sót tác động lên đơn cũ | Nhận | Task 10: phạm vi = đơn tạo mới ∪ đơn có `updated_at ≥` lúc test bắt đầu; BE-IT-20 thêm ca tương tác với đơn có sẵn |
| R12 | Lỗi trong `DisposeAsync` không được che lỗi gốc của test | Nhận | Task 10: kiểm một lần bằng test tạm, ghi vào kết quả |
| R13 | Hạn chế bỏ qua tự kiểm; test kiểm DB từ chối không cần bỏ qua | Nhận | Task 10: chỉ bỏ qua khi test cố ý để lại dữ liệu vi phạm; liệt kê từng test bỏ qua trong kết quả |
| R14 | 404 chỉ là vi phạm khi đúng `DECISION_NOT_FOUND`; lỗi khác là không kết luận được; JSON tách hai loại | Nhận | D7; Task 11 |
| R15 | Đọc `tadss.decision_executions` chỉ để chẩn đoán | Nhận (đã vậy), ghi rõ thêm | Task 11 |
| R16 | Kiểm SC6-5 song song có giới hạn | Nhận phần song song: tối đa 8 request cùng lúc, timeout 5 giây mỗi request | Task 11 |
| R17 | Lấy mẫu 20–30 quyết định khi nhiều; timeout thì chỉ log, không fail | **Không nhận.** SC6 là "0 vi phạm" trên toàn bộ phạm vi, lấy mẫu làm yếu kết luận; nuốt timeout trái với R14. Đoạn mã mẫu của review còn thêm vào một `List` từ nhiều task song song (đua dữ liệu) | — |
| R18 | Chạy thử với cờ tắt không chứng minh mức cọc TADSS; lượt chính thức cần giá trị kỳ vọng độc lập | Nhận. Bản 1 chỉ kiểm `depositAmount = round(finalTotal × depositRate)` với rate của chính response, gần như luôn đúng | D8; Task 14 |
| R19 | Hủy ngay: không tính hủy muộn, không sinh Refund, nhả sân, thông báo đúng | Nhận, đã đối chiếu mã: hủy muộn chỉ khi **đã trả tiền** mà hoàn < 100 % (`CustomerHistoryCalculator.IsLateCancellation`); Refund chỉ tạo khi số hoàn > 0 (`BookingCancellationService`, dòng 249); strategy 2.1.0 không dùng số lần hủy | Task 14: `check` + SC6 |
| R20 | Mailpit chỉ bật qua compose phụ, production giữ SSL, email không ảnh hưởng transaction | Nhận (đã vậy: email gửi sau commit, lỗi bị bắt) | Task 12 ghi rõ |
| R21 | Gắn nhãn đơn LT-04 để dễ dọn | Nhận: `CreateOnlineBookingDto` có `Note` | Task 14: `note = "LT04 {runId}"` |
| R22 | Ước lượng: Phần A và D rủi ro cao | Nhận | Ước lượng 9 ngày, không cắt test để giữ mốc |
| R23 | Không tái hiện được vẫn áp dụng cách tắt IPv6 | **Không nhận**: chỉ sửa khi tái hiện và xác định nguyên nhân (quy tắc "lỗi thật mới sửa") | Task 13 giữ nguyên |
| R24 | Commit message đề xuất ghi `Co-Authored-By: Claude Sonnet 4.5` | **Không dùng**: dòng ghi công của dự án là `Claude Opus 5.5` | — |
| R25 | Lệnh kiểm cuối ghi "cần Docker cho Postgres + Redis" | Sửa: test BE chỉ cần PostgreSQL (Testcontainers), không có Redis | — |
| R26 | (vòng 2) S1 đòi "quay về `ReturnUrl`" trong khi BE tắt: giao dịch thành công vẫn có thể ra trang lỗi | Nhận | Task 0: xác nhận bằng tham số VNPay ký trên thanh địa chỉ (`vnp_ResponseCode = 00`, `vnp_TransactionStatus = 00`, chữ ký đúng), không phụ thuộc BE |
| R27 | (vòng 2) Cổng không hỗ trợ hạn link ném `NotSupportedException`: phải có hành vi kiểm soát, không tạo Payment/link ngoài dự kiến, không quay về link không hạn | Nhận. Đây là lỗi cấu hình (cổng vi phạm hợp đồng), cùng loại với "chưa đăng ký cổng" mà `PaymentGatewayResolver` đang ném `InvalidOperationException`; khách không tự sửa được nên **không** đổi thành lỗi 4xx; `ExceptionMiddleware` đã trả 500 `INTERNAL_ERROR` dạng envelope, không lộ chi tiết | Task 3: không bắt ở starter; hai nơi gọi nằm trong transaction nên không ghi gì; test HTTP với cổng giả ném |
| R28 | (vòng 2) SC6-5: cố định tập dữ liệu và khoảng thời gian; báo cáo có số đếm; bị gián đoạn thì không ghi `PASS` dở dang | Nhận | Task 11 |
| R29 | (vòng 2) `LT04_EXPECTED_RATE` phải đúng strategy đang chạy; summary ghi phiên bản strategy, giả định khách, nguồn cấu hình | Nhận; kết quả chỉ xác minh mức cọc của kịch bản, không chứng minh strategy giảm no-show | Task 14 |
| R30 | (vòng 2) Phạm vi theo `updated_at` chỉ đúng nếu mọi đường ghi cập nhật trường này | Nhận, **có thật**: rà nhanh 29/10 thấy khoảng 14/17 câu `ExecuteUpdateAsync` trên `bookings` trong `BookingRepository*` có ghi `UpdatedAt` | Task 10: bước rà soát + test các đường ghi chính |
| R31 | (vòng 2) Ghi chú lấy mẫu có ngưỡng cho job đối soát production sau này | **Không nhận**: job đó ngoài phạm vi (0.4). Lập luận kèm theo ("lấy 30/100 phát hiện ~90 % vi phạm") không đúng: với một vi phạm trong 100 đơn, lấy 30 chỉ có 30 % cơ hội gặp nó | — |

---

## Phần A — Hạn link thanh toán (P6-27, T-5)

### Task 0 — Thử sandbox trước khi code (bắt buộc, chặn Task 2–4)

**Vấn đề.** Hai điều chưa biết quyết định giá trị của Phần A:

1. **VNPay có kiểm `vnp_ExpireDate` lúc khách bấm trả, khi trang đã mở từ trước mốc đó không?** Ngày 28/10 mới thấy VNPay từ chối link **mở** sau hạn (mã 15). Nếu VNPay chỉ kiểm lúc mở, khoảng đệm chỉ chặn được khách mở link muộn; khách mở sớm vẫn trả được trong ~15 phút sống của trang.
2. **Link tự dựng có mở được tới trang trả tiền không?** Commit `2c26ef7` (28/04, "fix : load UI for VNPAY") từng bỏ cách tự dựng để chuyển sang thư viện, không ghi lý do. Ngày 28/10 link tự ký mới chỉ ra trang lỗi mã 15 (chứng tỏ chữ ký đúng), chưa lần nào tới được trang nhập thẻ.

**Cách làm.** Script Python tạm ở `%TEMP%\vnpay6a6\` ký URL theo đúng cách của Task 2 (sắp xếp khóa, mã hóa kiểu form, HMAC-SHA512), đọc `TmnCode`/`HashSecret`/`CallbackUrl` từ `SmashCourt-BE/.env` mà không in giá trị. Không cần BE chạy (IPN của các giao dịch này không khớp Payment nào; BE tắt thì VNPay chỉ gửi lại, không ảnh hưởng dữ liệu). Vì BE tắt nên trình duyệt quay về `ReturnUrl` sẽ gặp trang lỗi của ngrok; **trang lỗi đó không có nghĩa giao dịch thất bại**. Kết quả giao dịch đọc từ chính URL VNPay chuyển về: người dùng chép nguyên URL trên thanh địa chỉ, script kiểm `vnp_ResponseCode`, `vnp_TransactionStatus`, `vnp_Amount`, `vnp_TxnRef` và **chữ ký `vnp_SecureHash`** bằng HashSecret (chứng minh chính VNPay báo kết quả). Không chép được URL thì tra giao dịch theo `vnp_TxnRef` trên trang quản trị merchant sandbox.

| Ca | Link | Người dùng làm | Kỳ vọng / quyết định |
| :---: | :--- | :--- | :--- |
| S1 | `ExpireDate = now + 15'` (GMT+7), 50.000 đ | Mở, trả hết bằng thẻ test NCB, chép URL trên thanh địa chỉ sau khi VNPay chuyển về | Ba mức: (a) VNPay từ chối ⇒ chưa xác minh, **dừng Phần A**; (b) tới trang nhập thẻ nhưng không trả xong ⇒ đạt một phần, chưa đủ để code; (c) URL chuyển về có `vnp_ResponseCode = 00`, `vnp_TransactionStatus = 00`, đúng số tiền, đúng `TxnRef` và chữ ký đúng ⇒ **đạt**. Callback tới BE không thuộc S1 (Task 16 kiểm) |
| S2 | `ExpireDate = now + 3'`, 50.000 đ | Mở ngay, dừng ở trang nhập thẻ, đợi tới **sau** mốc hạn ~30 giây, rồi nhập thẻ và OTP | VNPay từ chối ⇒ khoảng đệm có tác dụng đầy đủ. VNPay vẫn nhận ⇒ vẫn làm Phần A (chặn mở link muộn) nhưng ghi giới hạn vào spec Mục 11: khe hở còn lại tối đa bằng thời gian sống của trang, lưới an toàn vẫn là P6-07 |
| S3 | 5.000 đ | Mở | Mở được tới trang trả ⇒ đóng mục "mức tiền tối thiểu" còn mở ở 10.1 bước 2; `MinOnlinePaymentAmount` giữ 5.000 đ |
| S4 | 4.000 đ | Mở | VNPay từ chối ⇒ xác nhận mức tối thiểu là 5.000 đ |

**Kiểm.** Ghi kết quả từng ca (giờ mở, giờ nhấn trả, mã trang lỗi) vào spec 10.1 bước 2. Dù S2 ra sao, khoảng đệm chỉ giảm rủi ro dựa trên độ trễ IPN đã đo, không bảo đảm không có tiền về muộn (P6-07 giữ nguyên). Script Python chỉ chứng minh **cách ký**; URL do mã C# sinh ra được chứng minh ở Task 16. Xóa thư mục tạm sau khi xong.

### Task 1 — Chính sách hạn link (A1, D1, D2)

**Vấn đề.** Link VNPay hiện không có hạn: khách trả được sau khi Job-01 hủy đơn (tái hiện 28/10: trả 41 giây sau khi hủy). Cần một hạn link sớm hơn hạn thanh toán của đơn, và một luật thống nhất cho trường hợp đơn còn quá ít thời gian.

**Giải pháp.**

- `Configurations/BookingPaymentSettings.cs`: thêm `PaymentLinkBufferMinutes` (mặc định 2), comment "Link thanh toán hết hạn trước hạn thanh toán của đơn bấy nhiêu phút (bù độ trễ IPN)". `appsettings.json` thêm khóa với giá trị 2.
- `BookingPaymentSettingsValidator`: `PaymentLinkBufferMinutes ≥ 0`; `InitialHoldMinutes ≥ PaymentLinkBufferMinutes + 1` và `RetryExtendMinutes ≥ PaymentLinkBufferMinutes + 1` (đơn mới và lần thanh toán lại đầu luôn tạo được link).
- `Services/Deposit/PaymentHoldPolicy.cs`:

```csharp
// Link cổng thanh toán phải còn ít nhất bấy nhiêu để khách kịp trả
public static readonly TimeSpan MinPaymentLinkLifetime = TimeSpan.FromMinutes(1);

// Hạn link = hạn thanh toán − khoảng đệm. False khi link sống dưới MinPaymentLinkLifetime (caller trả PAYMENT_HOLD_EXPIRED)
public static bool TryGetPaymentLinkExpiry(
    DateTime nowUtc, DateTime paymentExpiresAtUtc, int bufferMinutes, out DateTime linkExpiresAtUtc)
```

  Kiểm `Kind = Utc` như các hàm cùng lớp; `bufferMinutes < 0` ⇒ `ArgumentOutOfRangeException`.

**Vì sao.** Đặt luật ở `PaymentHoldPolicy` (nghiệp vụ, không gắn cổng) để mọi cổng sau này dùng chung; mỗi cổng chỉ nhận một mốc UTC. Phương án B (tạo link không trừ đệm khi còn ít thời gian) bị loại vì đúng lúc sát hạn là lúc dễ có tiền về sau khi hủy nhất.

**Ảnh hưởng.** Không đổi hạn giữ chỗ, không đổi Job-01. Khách thấy link hết hạn sớm hơn đơn 2 phút.

**Kiểm (unit, `Unit/Services/Deposit/PaymentHoldPolicyLinkTests.cs`, Catalog BE-UT-08):**

- `TryGetPaymentLinkExpiry_NewBookingWindow_LinkEndsTwoMinutesEarlier`: now 10:00, hạn 10:10 ⇒ 10:08.
- `…_ExactlyThreeMinutesLeft_StillOk` (link sống đúng 1 phút) / `…_OneTickLess_False` (3 phút − 1 tick; so sánh trên `DateTime` trong bộ nhớ nên tick là đơn vị nhỏ nhất).
- `…_ExactlyBufferLeft_False` (còn đúng 2 phút ⇒ link sống 0).
- `…_ExpiryAlreadyPassed_False`; `…_BufferZero_LinkEqualsExpiry`.
- `…_KeepsSubSecondPrecision`; `…_NonUtcInput_Throws` (theory 2 tham số); `…_NegativeBuffer_Throws`.
- Validator: `Validate_BufferNegative_Fails`, `Validate_InitialHoldNotAboveBuffer_Fails` (2 và 2), `Validate_RetryExtendNotAboveBuffer_Fails`, `Validate_Defaults_Succeed`.

### Task 2 — Ký và dựng URL VNPay (A2, A3, D3)

**Vấn đề.** Thư viện `VNPAY.NET` 2.1.0 không gửi `vnp_ExpireDate` và gửi `vnp_CreateDate` theo UTC (T-5). URL đã ký nên không thêm tham số sau được.

**Giải pháp.** `Helpers/VnPayHelper.cs` (static, theo bảng của skill BE: helper không thuộc service nào vì cả chiều tạo link lẫn chiều kiểm IPN đều dùng):

```csharp
public static class VnPayHelper
{
    // Chuỗi ký: tham số khác rỗng sắp theo khóa (VnPayCompare, ordinal), key=WebUtility.UrlEncode(value) nối bằng '&'
    public static string BuildSignData(IEnumerable<KeyValuePair<string, string>> parameters);
    // HMAC-SHA512, hex chữ hoa (VNPAY.NET 2.1.0 sinh hex hoa, kiểm 30/10; VNPay so không phân biệt hoa thường)
    public static string Sign(string signData, string hashSecret);
    // baseUrl?{signData}&vnp_SecureHash={chữ ký}
    public static string BuildPaymentUrl(string baseUrl, IReadOnlyDictionary<string, string> parameters, string hashSecret);
    // Mốc UTC (Kind = Utc, khác thì ném) → "yyyyMMddHHmmss" theo giờ VN
    public static string FormatVietnamTime(DateTime utc);
}
```

`VnPayCompare` chuyển vào cùng file (internal). `VnPayService.IsSignatureValid` dùng `BuildSignData` + `Sign` thay đoạn tự nối chuỗi hiện có (một cách ký cho hai chiều; giữ nguyên quy tắc bỏ `vnp_SecureHash`, `vnp_SecureHashType`, giá trị rỗng).

**Vì sao.** Một nơi ký duy nhất: lệch cách mã hóa giữa hai chiều là lỗi khó thấy nhất của tích hợp VNPay. Hex thường hay hoa: chọn đúng như thư viện đang sinh để URL trùng từng ký tự với **cùng tập tham số** (mức 1 của D3; VNPay so không phân biệt hoa thường).

**Ảnh hưởng.** Chiều IPN đổi cách dựng chuỗi nhưng phải cho kết quả y hệt; toàn bộ test IPN hiện có (chữ ký đúng, sai, thiếu) giữ xanh là điều kiện.

**Kiểm (unit, `Unit/Helpers/VnPayHelperTests.cs`, Catalog BE-UT-12 cho phần giờ):**

- **Đối chứng mức 1** `BuildPaymentUrl_SameParametersAsLibrary_ProducesIdenticalUrl`: project test thêm `PackageReference VNPAY.NET 2.1.0`; dựng `IVnpayClient` bằng `AddVnpayClient` với cấu hình test và `HttpContext` có IP `203.0.113.7`; gọi `CreatePaymentUrl(50000, "Dat san San Mot", BankCode.ANY)`; tách tham số của URL thư viện (bỏ `vnp_SecureHash`); `BuildPaymentUrl` với đúng các tham số đó phải ra **chuỗi URL trùng khớp**. Thêm ca `OrderInfo` có dấu cách, dấu chấm, dấu phẩy và `ReturnUrl` có `?a=1&b=2` (ký tự cần mã hóa).
- `Sign_KnownVector_MatchesFixedHash` (vector cố định tính một lần từ thư viện, khóa hồi quy khi gỡ gói khỏi BE).
- `BuildSignData_SkipsEmptyValues`, `…_SortsOrdinal` (`vnp_Amount` trước `vnp_BankCode`, chữ hoa trước chữ thường).
- `FormatVietnamTime_UtcMidnightBoundary`: `2026-11-20T16:59:59Z` ⇒ `20261120235959`; `17:00:00Z` ⇒ `20261121000000`; `…_NonUtc_Throws`.
- IPN: test chữ ký hiện có của `VnPayServiceTests` chạy lại không sửa.

### Task 3 — Cổng nhận hạn link; `VnPayService` tự dựng URL (A1, A2, A3)

**Giải pháp.**

- `Models/Payments/GatewayPaymentRequest.cs` (record, cạnh `GatewayPaymentLink`):

```csharp
// Yêu cầu tạo link: mốc thời gian UTC, mỗi cổng tự đổi sang định dạng của mình
public sealed record GatewayPaymentRequest(
    string Reference, decimal Amount, string OrderInfo, DateTime CreatedAtUtc, DateTime ExpiresAtUtc);
```

- `IPaymentGateway.CreatePaymentLink(GatewayPaymentRequest request)` thay chữ ký 3 tham số; comment: "Link phải hết hạn đúng `ExpiresAtUtc`; cổng không hỗ trợ hạn link thì ném `NotSupportedException`".
- `VnPayService.CreatePaymentLink`: bỏ `IVnpayClient`; tham số `vnp_Version`, `vnp_Command = pay`, `vnp_TmnCode`, `vnp_Amount = (long)(amount × 100)`, `vnp_CreateDate = FormatVietnamTime(CreatedAtUtc)`, `vnp_ExpireDate = FormatVietnamTime(ExpiresAtUtc)`, `vnp_CurrCode = VND`, `vnp_IpAddr = ResolveClientIp()`, `vnp_Locale = vn`, `vnp_OrderInfo = NormalizeOrderInfo(...)`, `vnp_OrderType`, `vnp_ReturnUrl = CallbackUrl`, `vnp_TxnRef = Guid.NewGuid().ToString("N")`. Danh sách đúng bằng danh sách thư viện đang gửi (đã kiểm các chuỗi `vnp_*` trong DLL) cộng `vnp_ExpireDate`. Kiểm đầu vào: `Amount > 0` và nguyên đồng, `ExpiresAtUtc > CreatedAtUtc`, cả hai `Kind = Utc`. Log Information: `RequestedRef`, `TxnRef`, hai mốc (không log URL đầy đủ vì có chữ ký).
- **Cổng không hỗ trợ hạn link (R27):** hợp đồng của `IPaymentGateway` bắt buộc tôn trọng `ExpiresAtUtc`; cổng không làm được thì ném `NotSupportedException`, **không** tạo link không hạn. `OnlinePaymentStarter` không bắt: lỗi cấu hình cùng loại "chưa đăng ký cổng" (resolver ném `InvalidOperationException`), khách không tự sửa được nên không đổi thành 4xx; `ExceptionMiddleware` trả 500 `INTERNAL_ERROR` dạng envelope. Cả hai nơi gọi nằm trong transaction nên không còn gì được ghi: `retry-payment` không gia hạn, không hủy Payment cũ, không tạo Payment; đặt sân thì transaction 2 rollback, đơn ở lại `PENDING` không Payment và Job-01 dọn với `SYSTEM_CLEANUP` (6A-5, B1). Log Error có phương thức và bookingId.
- **Gói kéo theo (phát hiện 30/10 ở Task 2):** `VNPAY.NET` 2.1.0 phụ thuộc `Microsoft.Extensions.{Options, Options.ConfigurationExtensions, Configuration.Abstractions, DependencyInjection.Abstractions}` 10.0.0 và `Microsoft.AspNetCore.Http(.Abstractions)` 2.3.0. Gỡ khỏi BE có thể làm đổi phiên bản các gói này mà BE đang nhận gián tiếp. Trước và sau khi gỡ chạy `dotnet list SmashCourt-BE.csproj package --include-transitive`, so sánh; phiên bản nào bị hạ thì giữ đúng bản cũ bằng `PackageReference` tường minh, kèm comment lý do.
- Gỡ `NormalizeTransactionRef` (không còn dùng); gỡ `builder.Services.AddVnpayClient(...)` khỏi `Program.cs`; gỡ `PackageReference VNPAY.NET` khỏi `SmashCourt-BE.csproj` (giữ ở project test, Task 2).

**Vì sao.** `TxnRef` do BE sinh thay `PaymentId` của thư viện; Guid đủ duy nhất, chỉ chữ-số theo yêu cầu của VNPay; IPN tìm Payment theo `transaction_ref` như cũ nên Payment cũ (mã số) không bị ảnh hưởng.

**Ảnh hưởng.** Payment mới có `transaction_ref` 32 ký tự hex. Không đổi schema. Test có `Mock<IPaymentGateway>` (`BookingServiceTestBuilder`, `PaymentServiceTestBuilder`, `OnlinePaymentStarterTests`, `BookingServiceOnlineBookingTests`, `BookingServiceTests`) đổi `Setup`/`Verify` sang `It.Is<GatewayPaymentRequest>(r => r.Reference == … && r.Amount == …)`: đổi cơ học, giữ nguyên ý của từng test.

**Kiểm (unit, `Unit/Services/VnPayServiceTests.cs`):**

- `CreatePaymentLink_SendsExpireAndCreateDateInVietnamTime` (UTC 16:58 ⇒ `…235800`, hạn 17:06 ⇒ ngày hôm sau `…000600`).
- `…_AmountTimesHundred`; `…_TxnRefIsUniqueAndAlphanumeric` (100 link khác nhau, regex `^[0-9a-f]{32}$`).
- `…_SignatureVerifiesWithReadResult` (tách query của URL vừa tạo, thêm `vnp_ResponseCode`… như IPN thật thì `ReadResult(...).IsSignatureValid = true`; đổi một ký tự của `vnp_ExpireDate` ⇒ false).
- **Đối chứng mức 2** `CreatePaymentLink_CommonParametersMatchLibrary`: cùng cấu hình, cùng IP, cùng số tiền và `OrderInfo`; tham số của `VnPayService` và của thư viện trùng nhau (giá trị và dạng đã mã hóa) ở mọi khóa trừ `vnp_CreateDate`, `vnp_TxnRef`, `vnp_ExpireDate`, `vnp_SecureHash`; tập khóa của `VnPayService` = tập khóa của thư viện ∪ {`vnp_ExpireDate`}.
- `…_ExpiryNotAfterCreate_Throws`; `…_NonUtc_Throws`; `…_FractionalAmount_Throws`.
- R27, unit `OnlinePaymentStarterTests`: cổng ném `NotSupportedException` ⇒ exception lan ra, `IPaymentRepository.CreateAsync` không được gọi.
- R27, HTTP (`PaymentLinkExpiryFlowTests`): host test đăng ký thêm một cổng giả cho `PaymentTxMethod.MOMO` luôn ném `NotSupportedException`; đơn `PENDING` có Payment cũ phương thức MOMO gọi `retry-payment` ⇒ 500 `INTERNAL_ERROR`; DB không đổi (`expires_at`, Payment cũ vẫn `PENDING`, không có Payment mới).

### Task 4 — Hai nơi tạo link dùng hạn link; response có `paymentLinkExpiresAt` (A1, A4, D2, D4)

**Giải pháp.**

- `IOnlinePaymentStarter.StartAsync(Guid invoiceId, string reference, decimal amount, string orderInfo, PaymentTxMethod method, DateTime nowUtc, DateTime linkExpiresAtUtc)`: dựng `GatewayPaymentRequest(reference, amount, orderInfo, nowUtc, linkExpiresAtUtc)`; kiểm `linkExpiresAtUtc > nowUtc` (ngược lại `ArgumentOutOfRangeException`, lỗi lập trình vì caller đã qua `TryGetPaymentLinkExpiry`).
- **Đặt sân** (`BookingService.CreateDepositPaymentAsync`): nhận thêm `hold.ExpiresAt`; `TryGetPaymentLinkExpiry(now, hold.ExpiresAt, buffer, out link)`; false ⇒ `InvalidOperationException` (validator Task 1 bảo đảm không xảy ra với cấu hình hợp lệ). `OnlineDecisionApplied` mang thêm `PaymentLinkExpiresAt`.
- **Thanh toán lại** (`PaymentService`, sau `TryForRetry`, **trước** transaction): `TryGetPaymentLinkExpiry(now, window.ExpiresAt, buffer, out link)`; false ⇒ `AppException(400, "Không còn đủ thời gian thanh toán, vui lòng đặt lại", ErrorCodes.PaymentHoldExpired)`; không gia hạn, không đổi Payment cũ.
- `DTOs/Booking/OnlineBookingResponse` thêm `public DateTime? PaymentLinkExpiresAt { get; set; }` (null khi không có `paymentUrl`), cùng cách serialize với `ExpiresAt`.

**Vì sao.** Kiểm ở `retry-payment` trước transaction để không để lại hạn đã kéo dài mà không có link. Đơn duyệt sát giờ chơi đi qua `retry-payment` nên cùng luật, không cần chỗ kiểm riêng ở duyệt.

**Ảnh hưởng.** API thêm một trường (DEPOSIT_APPROVAL_API 3.1, 3.3); `retry-payment` có thêm một trường hợp 400 `PAYMENT_HOLD_EXPIRED` (còn dưới 3 phút) với câu thông báo khác.

**Kiểm.**

- Unit `OnlinePaymentStarterTests`: request truyền đúng hai mốc; `linkExpiresAtUtc ≤ nowUtc` ⇒ ném, không tạo Payment.
- Unit `BookingServiceOnlineBookingTests`: link của đơn mới hết hạn đúng `expiresAt − 2'`; response `PaymentLinkExpiresAt` bằng đúng mốc đó; nhánh không có link (a′, chờ duyệt) ⇒ null.
- Unit `PaymentServiceTests` (retry): còn 3'00" ⇒ 200, link = hạn − 2'; còn 2'59" ⇒ 400 `PAYMENT_HOLD_EXPIRED`, `ExtendPaymentWindowAsync` và `FailPendingByInvoiceAsync` **không** được gọi.
- HTTP (file mới `Integration/Http/PaymentLinkExpiryFlowTests.cs`, kế thừa `PaymentCancellationTestBase` như `PaymentFailureFlowTests`, Catalog BE-UT-08): đặt sân ⇒ `vnp_ExpireDate` trong URL = `FormatVietnamTime(expiresAt − 2')`; dời `invoice.expires_at` và `hold_deadline` (sửa DB như các test cũ) còn 2'30" rồi gọi `retry-payment` ⇒ 400 và DB không đổi; còn 4' ⇒ 200, `paymentLinkExpiresAt` đúng.

---

## Phần B — Kết quả thanh toán theo trạng thái đơn (N-36, P6-28)

### Task 5 — `vnpay/confirm` trả `bookingStatus`

**Vấn đề.** Ca 4 ngày 28/10: khách trả trên trang VNPay sau khi Job-01 hủy đơn; `confirm` vẫn trả `isSuccess = true` và câu "Thanh toán thành công! Vui lòng đợi hệ thống xác nhận." trong khi đơn đã `CANCELLED_PENDING_REFUND`.

**Giải pháp.** `DTOs/Payment/VnPayReturnResult` thêm `public string? BookingStatus { get; set; }` (comment: "Trạng thái đơn đọc từ DB lúc gọi; FE rẽ nhánh theo trường này, `IsSuccess` chỉ là kết quả của cổng"). Câu thông báo chọn ở **một hàm thuần** `private static string ReturnMessage(bool gatewaySuccess, string responseCode, BookingStatus? bookingStatus, PaymentTxStatus? paymentStatus)` trong `PaymentService`; `paymentStatus` là trạng thái Payment **của chính giao dịch** (đã đọc bằng `GetByTransactionRefAsync(..., asNoTracking: true)`), `bookingStatus` đọc lại từ DB như hiện tại:

| Cổng | Trạng thái đơn | Payment của giao dịch | Câu |
| :--- | :--- | :--- | :--- |
| Chữ ký sai | — | — | Giữ nguyên ("Chữ ký không hợp lệ", `97`), `bookingStatus` null |
| Thất bại | bất kỳ | bất kỳ | `GetVnPayErrorMessage(responseCode)` như cũ; `bookingStatus` vẫn trả |
| Thành công | `PAID_ONLINE`, `CONFIRMED`, `IN_PROGRESS`, `PENDING_PAYMENT`, `COMPLETED` | bất kỳ | "Thanh toán thành công! Đặt sân của bạn đã được xác nhận." |
| Thành công | `PENDING` | bất kỳ (IPN chưa về) | "Đã nhận kết quả thanh toán, hệ thống đang xác nhận đơn đặt sân." |
| Thành công | `CANCELLED_PENDING_REFUND` | `SUCCESS` | "Đã nhận thanh toán nhưng đơn đặt sân đã hết thời gian giữ chỗ trước khi tiền về. Khoản tiền này sẽ được hoàn đủ tại chi nhánh." |
| Thành công | thuộc `GetCancelledStatuses()`, trừ dòng trên (gồm `CANCELLED` khi IPN chưa về, `CANCELLED_REFUNDED`, hoặc Payment chưa `SUCCESS`) | khác | "Đơn đặt sân đã bị hủy. Hệ thống đang đối soát giao dịch này, vui lòng xem chi tiết trong lịch sử đặt sân." |
| Thành công | khác (`NO_SHOW`, `PENDING_APPROVAL`) hoặc không tìm thấy đơn/Payment | — | "Đã nhận kết quả thanh toán, vui lòng xem trạng thái trong lịch sử đặt sân." |

**Vì sao.** Thêm trường không phá FE cũ; tập "đã hủy" lấy từ `BookingStatusTransition`. Chỉ hứa hoàn tiền khi DB **đã** ghi đúng điều đó: đơn `CANCELLED_PENDING_REFUND` và Payment của giao dịch này `SUCCESS` (P6-07 tạo Refund trong cùng lần xử lý IPN). Đơn `CANCELLED` mà IPN chưa về, hoặc `CANCELLED_REFUNDED` (có thể đã hoàn khoản trước), không suy ra được số tiền sẽ hoàn ⇒ câu trung tính; FE hỏi lại chi tiết đơn như 8.2. Bản 1 hứa hoàn tiền cho mọi trạng thái hủy (R5).

**Kiểm.**

- Unit `PaymentServiceTests`: một test cho mỗi dòng bảng (theory theo bộ ba cổng / trạng thái đơn / trạng thái Payment), gồm: `CANCELLED_PENDING_REFUND` + Payment `PENDING` ⇒ câu trung tính (không hứa hoàn); `CANCELLED` + Payment `PENDING` ⇒ câu trung tính; `CANCELLED_REFUNDED` ⇒ câu trung tính; không có Payment; kiểm `BookingStatus` đúng tên enum và `IsSuccess` giữ theo cổng.
- HTTP: mở rộng `PaymentFailureFlowTests.MoneyArrivesAfterTheOrderExpired_RefundsAllOfItAtTheBranch` (6A-4): gọi `POST /api/payments/vnpay/confirm` **trước** IPN (đơn đã `CANCELLED`) ⇒ câu trung tính; **sau** IPN ⇒ `isSuccess = true`, `bookingStatus = CANCELLED_PENDING_REFUND`, câu hoàn tiền; đơn trả bình thường ⇒ `PAID_ONLINE`.

---

## Phần C — Lỗi thời gian còn lại (T-9…T-11, BE-IT-25)

### Task 6 — Khách mới theo ngày VN (T-9)

**Vấn đề.** Biên ngày dựng bằng `ToDateTime(..., DateTimeKind.Utc)` tại `ReportRepository.cs:103-104` (`GetDashboardSummaryAsync`), `:1347-1348` (`GetCustomerStatisticsReportAsync`), `:1457-1458` (`GetCustomerAcquisitionTrendAsync`); xu hướng nhóm theo `CreatedAt.Date` UTC (`:1476`); `ReportService.cs:45` mặc định `toDate` theo ngày UTC. Khách đăng ký 00:00–06:59 giờ VN bị tính sang ngày trước.

**Giải pháp.** Ba chỗ dùng nửa khoảng `[ToUtcFromVietnam(fromDate, 00:00), ToUtcFromVietnam(toDate.AddDays(1), 00:00))` (`>=` và `<`); xu hướng lấy `u.CreatedAt` rồi nhóm trong bộ nhớ theo `DateOnly.FromDateTime(DateTimeHelper.ToVietnamTime(createdAt))`; `ReportService.cs:45` dùng `DateTimeHelper.GetTodayInVietnam()`. Nhóm trong bộ nhớ giữ như mã hiện có (EF không dịch được đổi múi giờ sang ngày): mỗi dòng là một mốc `created_at` của khách mới trong khoảng lọc, cỡ trăm dòng; không tối ưu khi chưa có số liệu (R6).

**Kiểm (BE-IT-25 phần T-9, `Integration/ReportTimeZoneTests.cs`, schema thật):** seed khách tạo lúc 17:30 UTC ngày 19/11 (= 00:30 VN 20/11) và 16:59:59 UTC ngày 20/11 (= 23:59:59 VN 20/11), 17:00 UTC 20/11 (= 00:00 VN 21/11): khoảng 20/11–20/11 đếm đúng 2 ở cả `GetDashboardSummaryAsync` và `GetCustomerStatisticsReportAsync`; xu hướng có đúng một nhóm `2026-11-20` = 2 và `2026-11-21` = 1. `ReportService.cs:45` không test được (đồng hồ hệ thống, 7.14): kiểm bằng review, ghi vào kết quả.

### Task 7 — "Sắp bắt đầu" qua nửa đêm (T-10)

**Vấn đề.** `upcomingLimit = TimeOnly.FromDateTime(now.AddMinutes(30))` (`ReportRepository.cs:277, 344`) quay về 00:xx khi `now` từ 23:30, nên đơn bắt đầu 23:45–23:59 không vào danh sách.

**Giải pháp.** Nếu `DateOnly.FromDateTime(now.AddMinutes(30)) > DateOnly.FromDateTime(now)` thì `upcomingLimit = TimeOnly.MaxValue`; dùng chung một hàm private `UpcomingLimit(DateTime now)` cho hai chỗ.

**Phạm vi thực tế.** Slot không thể qua nửa đêm: `chk_booking_courts_valid_time` buộc `end_time > start_time` trên cùng `date`. Lỗi chỉ xảy ra với chi nhánh có slot **bắt đầu** sau 23:30; vẫn là lỗi thật của truy vấn nên sửa (P6-22).

**Kiểm (BE-IT-25 phần T-10, cùng file; dữ liệu theo ràng buộc của schema):** đơn `CONFIRMED` hôm nay 23:50–23:59, gọi hai hàm với `now = hôm nay 23:45` ⇒ có trong số "sắp bắt đầu" của KPI và trong thẻ sân; `now = 23:15` ⇒ không (23:50 ngoài 30'); `now = 23:50` ⇒ không (điều kiện `StartTime > nowTime` giữ nguyên); `now = 10:00` với đơn 10:20 ⇒ có, đơn 10:31 ⇒ không (hồi quy ca thường).

### Task 8 — Lọc giao dịch điểm theo ngày (T-11)

**Vấn đề.** `LoyaltyTransactionQuery.FromDate/ToDate` (`DateTime`, `Kind = Unspecified`) so trực tiếp với `timestamptz` (`CustomerManagementRepository.cs:264-271`) ⇒ Npgsql ném ⇒ 500.

**Giải pháp.** Lấy phần ngày: `from = ToUtcFromVietnam(DateOnly.FromDateTime(FromDate), 00:00)`, `toExclusive = ToUtcFromVietnam(DateOnly.FromDateTime(ToDate).AddDays(1), 00:00)`; lọc `>= from` và `< toExclusive`. Không đổi kiểu của DTO (giữ wire).

**Kiểm (BE-IT-25 phần T-11):** HTTP `GET /api/customers/{id}/loyalty-transactions?fromDate=2026-11-20&toDate=2026-11-20` (vai trò được phép theo controller) ⇒ 200; giao dịch lúc 23:30 VN 20/11 có, lúc 00:10 VN 21/11 không, lúc 23:50 VN 19/11 không.

### Task 8a — Giờ tạo đơn trong hàng việc của dashboard (T-15, thêm 30/10)

**Vấn đề.** `ToManagerDashboardActionItem` trả `CreatedAt = booking.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss")`: mốc UTC không offset, trong khi `StartTime`, `EndTime` của cùng DTO là giờ tường VN. FE `ActionQueueItem` hiện "Tạo lúc" sớm 7 giờ.

**Giải pháp.** `DateTimeHelper.ToVietnamTime(booking.CreatedAt).ToString(...)`; giữ định dạng (wire không đổi).

**Kiểm (BE-IT-25 phần T-15):** đơn `PENDING_PAYMENT` tạo lúc 13:58 UTC (20:58 VN) ⇒ `createdAt = "…T20:58:00"`; đơn tạo 17:30 UTC ngày 19 (00:30 VN ngày 20) ⇒ đúng ngày 20; `startTime`/`endTime` không đổi.

### Task 8b — Kỳ so sánh của dashboard (T-16, thêm 30/10)

**Vấn đề.** `ReportService.GetComparisonPeriods` tính số ngày bằng `(ToDate 23:59:59.9999999 − FromDate 00:00).TotalDays + 1` ⇒ kỳ trước dài hơn một ngày (1 so với 2, 7 so với 8, 30 so với 31) nên % thay đổi bị kéo xuống; đọc `filter.FromDate!.Value`, `filter.ToDate!.Value` thay khoảng đã chuẩn hóa ⇒ gọi chỉ với một trong hai ngày là 500.

**Giải pháp.** Hàm thuần nhận `(fromDate, toDate)` đã qua `ValidateDateRange`; `n = toDate.DayNumber − fromDate.DayNumber + 1`; kỳ trước `[fromDate − n; fromDate − 1]`, tính hoàn toàn bằng `DateOnly`. Dùng cho cả dashboard Owner và Manager.

**Kiểm:** unit test hàm (1, 7, 30, 365 ngày; qua tháng, năm, 29/02; kỳ trước liền kề và cùng độ dài); unit test service với repository giả: kỳ trước truyền vào repository đúng, chỉ `fromDate` hoặc chỉ `toDate` ⇒ không ném và so với kỳ đã chuẩn hóa; HTTP dashboard chỉ `toDate` ⇒ 200.

---

## Phần D — Bộ kiểm SC6 (BE-IT-20, P6-29)

### Task 9 — `BookingInvariantChecker`

**Vấn đề.** SC6 ("0 booking hỏng") cần một dụng cụ đo dùng được cả trong BE-IT lẫn trên staging; viết thành file SQL thì chép lại danh sách trạng thái của `BookingStatusTransition` ra nơi thứ hai, lệch khi thêm trạng thái.

**Giải pháp.** `SmashCourt-BE.Tests/Integration/Invariants/` (namespace `SmashCourt_BE.Tests.Integration.Invariants`):

```csharp
public sealed record InvariantViolation(string Rule, Guid BookingId, string Detail);

// Phạm vi: đơn theo danh sách id (BE-IT) hoặc theo created_at trong [FromUtc; ToUtc] (staging)
public sealed record BookingScope(IReadOnlyCollection<Guid>? BookingIds, DateTime? FromUtc, DateTime? ToUtc);

public static class BookingInvariantChecker
{
    // Không ghi gì (AsNoTracking); mọi câu đọc trong một transaction REPEATABLE READ (một snapshot).
    // DbContext đã có transaction (test rollback) thì dùng transaction đó. now là đồng hồ của lượt kiểm
    public static Task<IReadOnlyList<InvariantViolation>> CheckAsync(
        SmashCourtContext db, BookingScope scope, DateTime nowUtc, CancellationToken ct = default);
}
```

Phạm vi theo thời gian (staging) lấy đơn có `created_at` **hoặc** `updated_at` trong khoảng, để gồm cả đơn có sẵn bị kịch bản sửa.

**Đơn đang giữ sân.** Một dòng `booking_courts` được coi là **đang giữ sân** khi đơn thuộc `GetActiveStatuses()`, trừ trường hợp đơn `PENDING_PAYMENT` có dòng đã tắt: Job-02 cố ý nhả sân khi hết giờ chơi mà đơn chưa thanh toán đủ (P6-25). Tập ngoại lệ này khai báo một lần trong bộ kiểm (`CourtsReleasedWhileActive = [PENDING_PAYMENT]`, comment dẫn P6-25), kèm test buộc nó là tập con của `GetActiveStatuses()`; không suy từ "giờ đã qua".

| Điều | Định nghĩa (LINQ, chỉ đọc) | Ghi chú |
| :---: | :--- | :--- |
| SC6-1 | Đơn `CONFIRMED`/`PAID_ONLINE` có `invoice.DepositSource != null` ⇒ tổng `Payment.Amount` của **đúng hóa đơn đó**, chỉ Payment `Status = SUCCESS`, ≥ `invoice.DepositAmount` | Cọc 0 và (a′) có `DepositAmount = 0`; Payment `PENDING`, `FAILED`, `REFUNDED` không được tính; không cộng trùng vì `transaction_ref` duy nhất và mỗi Payment một dòng |
| SC6-2 | Đơn `PENDING_APPROVAL` có `ApprovalExpiresAt > now` ⇒ mỗi dòng `booking_courts` của đơn có slot lock cùng `BookingId`, `CourtId`, `Date`, `StartTime`, `EndTime` và `ExpiresAt > now` | Đơn quá hạn duyệt nhưng job chưa chạy (≤ 1 chu kỳ) không bị báo nhầm. Phần `decisionId` do `chk_bookings_pending_approval` bảo đảm ⇒ kiểm ràng buộc (Task 9, ca DB) |
| SC6-3 | Đơn thuộc `GetCancelledStatuses()` ⇒ không có slot lock `ExpiresAt > now` và không có dòng `booking_courts` `IsActive = true` | |
| SC6-4a | Đơn thuộc `GetActiveStatuses()` trừ `CourtsReleasedWhileActive` ⇒ mọi dòng `booking_courts` của đơn `IsActive = true` | Dòng sân bị tắt sai thì DB không còn chặn đơn khác đặt trùng (`excl_booking_courts_no_overlap` chỉ áp dòng `is_active`) |
| SC6-4b | Không có hai đơn khác nhau có dòng **đang giữ sân** cùng sân, cùng `date`, giờ chồng nhau theo nửa khoảng (`a.Start < b.End && b.Start < a.End`, nên 10:00–11:00 và 11:00–12:00 không chồng); ít nhất một đơn trong phạm vi; mỗi cặp báo một lần | Dòng đang giữ sân không lọc `IsActive` (trừ ngoại lệ `PENDING_PAYMENT` ở trên), nên bắt được hậu quả của SC6-4a; hai dòng `is_active` chồng nhau thì DB đã chặn |
| Phụ-1 | Đơn thuộc `GetCancelledStatuses()` có `CancelReason` | DB đã chặn bằng `chk_bookings_cancel_reason` ⇒ kiểm ràng buộc |
| Phụ-2 | Đơn `PENDING` ⇒ `invoice.HoldDeadline != null` | Phạm vi theo thời gian loại dữ liệu trước Phase 6 |

**Kiểm (BE-IT-20, `Integration/Invariants/BookingInvariantCheckerTests.cs`, `IntegrationTestBase`, mỗi test rollback):**

- Mỗi điều: một bộ dữ liệu hợp lệ ⇒ 0 vi phạm; một bộ vi phạm dựng cố ý ⇒ đúng vi phạm đúng `Rule`, đúng `BookingId`. Ví dụ SC6-3: đơn `CANCELLED` còn slot lock hạn tương lai.
- SC6-1: Payment `SUCCESS` đủ ⇒ 0; chỉ có Payment `PENDING` hoặc `FAILED` cùng số tiền ⇒ vi phạm; Payment `SUCCESS` của **hóa đơn khác** ⇒ vẫn vi phạm; cọc 0 không Payment ⇒ 0; đơn tại quầy (`DepositSource` null) ⇒ không xét.
- SC6-4 (9 ca): (1) hai đơn hiệu lực không chồng giờ ⇒ 0; (2) hai đơn `CONFIRMED` chồng giờ cùng sân, một dòng bị tắt sai (DB cho qua) ⇒ SC6-4a cho đơn có dòng tắt và SC6-4b cho cặp; (3) chồng giờ khác sân ⇒ 0; (4) đơn `CANCELLED` (dòng đã tắt) chồng giờ đơn hiệu lực ⇒ không có SC6-4; (5) đơn `PAID_ONLINE` có dòng tắt, không chồng ai ⇒ chỉ SC6-4a; (6) đơn `PENDING_PAYMENT` dòng đã tắt chồng giờ một đơn `CONFIRMED` ⇒ 0 (nhả sân đúng thiết kế); (7) liền kề 10:00–11:00 / 11:00–12:00 ⇒ 0; (8) cùng giờ 23:00–23:59 ở hai ngày liên tiếp ⇒ 0; (9) cặp vi phạm có một đơn ngoài phạm vi ⇒ báo cho đơn trong phạm vi; cả hai ngoài phạm vi ⇒ không báo.
- Tập ngoại lệ: `CourtsReleasedWhileActive ⊂ GetActiveStatuses()`.
- Snapshot: phần mở transaction tách thành `internal static Task<T> InSnapshotAsync<T>(SmashCourtContext db, Func<Task<T>> read)`; test gọi với `read` kiểm `db.Database.CurrentTransaction!.GetDbTransaction().IsolationLevel == IsolationLevel.RepeatableRead` khi context chưa có transaction, và kiểm không mở transaction mới khi context đã có (cùng đối tượng transaction).
- Biên SC6-2: `ApprovalExpiresAt = now − 1s` + slot lock hết hạn ⇒ 0 vi phạm; `now + 1s` + thiếu slot lock ⇒ vi phạm.
- Phạm vi: đơn vi phạm ngoài danh sách id / ngoài khoảng thời gian ⇒ không báo.
- Ràng buộc DB: chèn `PENDING_APPROVAL` thiếu `tadss_decision_id` ⇒ `PostgresException` tên `chk_bookings_pending_approval`; đơn hủy thiếu `cancel_reason` ⇒ `chk_bookings_cancel_reason`; hai dòng `is_active` chồng giờ ⇒ `excl_booking_courts_no_overlap`.

### Task 10 — Tự kiểm SC6 sau mỗi test HTTP

**Giải pháp.** `HttpTestBase` hiện thực `IAsyncLifetime` (phương thức `virtual`): `InitializeAsync` ghi `startedAt = DateTime.UtcNow` và chụp tập `bookings.id` hiện có; `DisposeAsync` lấy phạm vi = đơn **mới sinh** ∪ đơn có `updated_at ≥ startedAt − 1 giây` (đơn có sẵn bị test hủy, duyệt, check-out…), chạy `CheckAsync` với `now = DateTime.UtcNow` (host dùng đồng hồ hệ thống), có vi phạm thì fail kèm danh sách. Các test HTTP chạy tuần tự trong một collection nên phạm vi không lẫn test khác.

**Bỏ qua tự kiểm:** override `protected virtual string? SkipInvariantCheckReason => null`, **chỉ** khi test cố ý để lại dữ liệu vi phạm trong DB, và phải ghi lý do. Test kiểm DB từ chối một thao tác **không** bỏ qua (thao tác thất bại không để lại dữ liệu). Mọi test bỏ qua được liệt kê trong mục kết quả. Lớp con đã có `IAsyncLifetime` riêng thì gọi `base`.

**Không che lỗi gốc:** xUnit 2 gom lỗi của thân test và của `DisposeAsync` (`ExceptionAggregator`). Kiểm một lần bằng test tạm (thân test ném, dữ liệu vi phạm) rằng kết quả hiện cả hai lỗi; ghi vào kết quả rồi xóa test tạm. Không gom được thì đổi sang: ghi vi phạm vào output của test, fail bằng một exception riêng có tiền tố `[SC6]`.

**Vì sao.** Đúng ý BE-IT-20 ("0 vi phạm sau BE-IT-02…19, 21…24") mà không phụ thuộc thứ tự chạy test: mọi luồng HTTP (đặt, duyệt, hủy, IPN, job, check-out, tranh chấp) tự được kiểm.

**Ảnh hưởng.** Mỗi vi phạm lộ ra khi chạy toàn bộ là một trong hai: **lỗi thật** ⇒ sửa kèm test theo quy tắc "lỗi thật mới sửa", ghi spec; **dữ liệu test dựng có chủ đích** ⇒ override kèm lý do. Kết quả phân loại ghi vào mục kết quả.

**Rà `updated_at` (R30):** liệt kê mọi câu `ExecuteUpdateAsync`/`SaveChanges` đổi `bookings` trong `BookingRepository*`, `BookingCancellationService`, job; câu nào đổi trạng thái hoặc dữ liệu giữ sân mà không ghi `UpdatedAt` thì: là lỗi thật (job outcome chọn đơn theo `updated_at`, P6-26) ⇒ thêm `SetProperty(b => b.UpdatedAt, now)` kèm test; chỉ ghi cột phụ (ví dụ `tadss_decision_id` ngay sau câu hủy đã ghi `updated_at` trong cùng transaction) ⇒ để nguyên, ghi lý do. Danh sách vào mục kết quả.

**Kiểm.** Toàn bộ BE test xanh; một test (trong `BookingInvariantCheckerTests`) chứng minh hàm tự kiểm của base fail khi có vi phạm; test các đường ghi chính (hủy của khách, nhân viên hủy, IPN thành công, duyệt, Job-01 hết hạn, check-out) đưa đơn có sẵn vào phạm vi tự kiểm; hai ca tương tác với đơn có sẵn (R11): test hủy một đơn tạo trước khi test bắt đầu mà (giả lập lỗi) còn slot lock ⇒ bị bắt nhờ `updated_at`; đơn có sẵn không bị đụng ⇒ không nằm trong phạm vi.

### Task 11 — Chạy bộ kiểm trên staging (Phase 7)

**Giải pháp.** `Integration/Invariants/StagingInvariantTests.cs` với `[StagingFact]` (lớp con `FactAttribute`, đặt `Skip` khi thiếu `SC6_CONNECTION_STRING`; không cần package mới), `Trait("Category", "Staging")`, `Trait("Catalog", "BE-IT-20")`. Biến môi trường: `SC6_CONNECTION_STRING` (nên dùng role chỉ đọc của Neon), `SC6_FROM_UTC`, `SC6_TO_UTC` (ISO 8601), `TADSS_BASE_URL`, `TADSS_API_KEY`, `SC6_OUTPUT` (đường dẫn JSON). Test:

1. Dựng `SmashCourtContext` từ `NpgsqlDataSourceBuilder(...).MapSmashCourtEnums()` (như fixture).
2. `CheckAsync` theo khoảng thời gian.
3. **SC6-5:** mỗi đơn trong phạm vi có `TadssDecisionId` ⇒ `GET {TADSS_BASE_URL}/api/v2/decisions/{id}` với `X-Api-Key`, tối đa 8 request cùng lúc, timeout 5 giây mỗi request, kiểm **toàn bộ** (không lấy mẫu). Phân loại: 200 và `decisionId` trong body đúng id ⇒ đạt; 404 **và** body `application/problem+json` có `code = DECISION_NOT_FOUND` (INTEGRATION_SPEC §3.4, mã lỗi ở trường `code` theo §3.8) ⇒ **vi phạm**; mọi trường hợp khác (401, 429, 5xx, timeout, lỗi mạng, 404 không đúng dạng) ⇒ **không kết luận được**, ghi lý do. Kết quả gom vào collection an toàn luồng (`ConcurrentBag`) hoặc trả về từ từng task rồi gộp.
4. **Quyết định mồ côi (chỉ chẩn đoán, không phải điều kiện đạt của SC6-1…5):** đọc chỉ-đọc `decision_id`, `reservation_id` của `tadss.decision_executions` có `role = 'PRIMARY'` trong khoảng thời gian; so trong C# với đơn BE cùng id không lưu `tadss_decision_id`. Đây là phụ thuộc có chủ đích vào schema nội bộ của TADSS, **chỉ** trong công cụ staging, không nằm trong luồng nghiệp vụ của BE (TADSS không có API liệt kê theo `reservationId`).
5. **Tập dữ liệu cố định (R28):** `SC6_FROM_UTC`, `SC6_TO_UTC` bắt buộc, `SC6_TO_UTC ≤` lúc bắt đầu chạy (thiếu hoặc vượt ⇒ dừng, không kiểm); danh sách đơn và `decisionId` lấy một lần trong snapshot ở bước 2, bước 3 chỉ kiểm danh sách đó, nên đơn mới phát sinh trong lúc kiểm không làm đổi tập dữ liệu. Kết quả ghi `startedAt`, `finishedAt`, `checkedBookings`, `checkedDecisions`, số vi phạm theo điều, số mục không kết luận được.
6. Ghi JSON `{ window, startedAt, finishedAt, checkedBookings, checkedDecisions, status, violations[], inconclusiveChecks[], orphanDecisions[] }` vào `SC6_OUTPUT` **một lần, sau khi mọi bước xong** (ghi file tạm rồi đổi tên); lượt bị hủy hoặc lỗi giữa chừng không bao giờ để lại file `PASS`: bắt được lỗi thì ghi `status = INCONCLUSIVE` kèm lý do, không bắt được (tiến trình bị giết) thì không có file; `status = PASS` chỉ khi 0 vi phạm **và** 0 mục không kết luận được; `FAIL` khi có vi phạm; `INCONCLUSIVE` khi không có vi phạm nhưng còn mục không kết luận được. Test chỉ đạt khi `PASS`.

Lệnh (ghi vào E2E_TEST_SPECIFICATION): `dotnet test SmashCourt-BE.Tests --filter "Category=Staging"` kèm các biến trên.

**Kiểm (6A-6, không có staging):** không có biến ⇒ test hiện là Skipped trong lượt chạy thường. Phần gọi TADSS tách thành hàm nhận `HttpClient`, test bằng WireMock: 200 đúng id ⇒ đạt; 200 sai id ⇒ không kết luận được; 404 `DECISION_NOT_FOUND` ⇒ vi phạm; 404 thân trống hoặc HTML ⇒ không kết luận được; 401, 429, 503, chậm quá 5 giây ⇒ không kết luận được; 30 id với giới hạn 8 ⇒ không quá 8 request đồng thời (WireMock đo). Chạy thử một lần với `SC6_CONNECTION_STRING` trỏ DB Testcontainers đã có dữ liệu test và WireMock thay TADSS (ghi lệnh vào kết quả).

---

## Phần E — Mailpit, SMTP, LT-04 (P6-30)

### Task 12 — `Smtp:EnableSsl` và compose phụ Mailpit

**Vấn đề.** LT-04 và E2E tạo/hủy hàng trăm đơn, mỗi lần hủy gửi email hủy và email "slot đã trống" qua Gmail SMTP (giới hạn khoảng 500 thư/ngày, có thể bị khóa). `EmailService` cố định `EnableSsl = true`, Mailpit không có TLS.

**Giải pháp.**

- `EmailService`: `EnableSsl = _config.GetValue("Smtp:EnableSsl", true)`. Mặc định giữ hành vi cũ; `appsettings.json` không thêm khóa (vắng ⇒ `true`), nên production và chạy hằng ngày giữ SSL. Chỉ file compose phụ đặt `false`, cho Mailpit trong mạng Docker nội bộ.
- `SmashCourt-BE/docker-compose.mailpit.yml` (file phụ, dùng `docker compose -f docker-compose.yml -f docker-compose.mailpit.yml up -d --build`): service `mailpit` (image `axllent/mailpit` ghim tag phiên bản, `MP_SMTP_AUTH_ACCEPT_ANY=1`, `MP_SMTP_AUTH_ALLOW_INSECURE=1`, cổng `127.0.0.1:8025` cho giao diện và API, mạng `smashcourt-network`); `smashcourt-be` thêm `Smtp__Host=mailpit`, `Smtp__Port=1025`, `Smtp__EnableSsl=false`.
- `SmashCourt-BE/CLAUDE.md` và MASTER_EXECUTION_PLAN (mục E-STAGING) ghi cách dùng: mọi đợt E2E/LT-04 chạy kèm Mailpit; E2E kiểm nội dung email qua API Mailpit.

Email vẫn gửi sau khi transaction booking đã commit và lỗi gửi bị bắt (`RunSafelyAsync`), nên đổi máy chủ thư không ảnh hưởng transaction.

**Kiểm.** Unit `EmailServiceTests` không có (gửi thật qua `SmtpClient`); kiểm bằng chạy: BE Docker kèm Mailpit, gọi `POST /api/auth/register` cho email thử ⇒ thư OTP xuất hiện ở `http://localhost:8025`; chạy không có file phụ ⇒ cấu hình Gmail như cũ (đọc log, không gửi thư thật).

### Task 13 — Lỗi `Network unreachable` của SMTP trong container

**Vấn đề.** 28/10 một lần gửi email trong container BE lỗi `Network unreachable`; nghi container phân giải địa chỉ IPv6 của `smtp.gmail.com` mà mạng Docker không có IPv6.

**Cách làm (điều tra trước, sửa sau):**

1. Trong container: `getent ahosts smtp.gmail.com` (có AAAA không), `cat /proc/net/if_inet6`, `ip -6 route`.
2. Chạy 10 lần gửi OTP liên tiếp, đọc log `EmailService`.
3. Tái hiện được và nguyên nhân là IPv6 ⇒ thêm `DOTNET_SYSTEM_NET_DISABLEIPV6=1` vào `environment` của `smashcourt-be` trong `docker-compose.yml` (công tắc chính thức của .NET), chạy lại bước 2. Nguyên nhân khác ⇒ ghi vào spec 10.1 (môi trường) và Mục 11, không sửa lan man.

**Kiểm.** 10/10 lần gửi thành công sau khi sửa (hoặc ghi rõ không tái hiện được).

### Task 14 — Kịch bản k6 LT-04

**Vấn đề.** Methodology 4.6: "20 VU tạo booking trên các slot khác nhau trong 10 phút; đo độ trễ API tạo booking, tỷ lệ thành công, tính đúng của mức cọc". Nếu chỉ tạo đơn liên tục: đơn hết hạn làm `paymentExpiredCount` tăng ⇒ strategy 2.1.0 từ chối khách (`REPEATED_PAYMENT_EXPIRY`); sân bị giữ ⇒ `SLOT_UNAVAILABLE`; đơn ≥ 1,5–2 triệu ⇒ `NEED_APPROVAL`, ngập hàng chờ.

**Giải pháp (`TADSS-Runtime/tests/load/`):**

- `lib/smashcourt.js`: client BE: `register`, `readOtpFromMailpit` (`GET {MAILPIT_URL}/api/v1/search?query=to:<email>`, lấy mã 6 số từ thư mới nhất), `verifyEmail`, `login`, `createOnlineBooking`, `cancelMyBooking` (`POST /api/me/bookings/{id}/cancel`). Thân request đúng DTO BE (`RegisterDto`, `VerifyEmailDto`, `LoginDto`, DTO của `POST /api/bookings/online`), đọc khi viết.
- `scenarios/lt04.js`:
  - `setup()`: tài khoản lấy từ `LT04_ACCOUNTS` (`email:password`, phân cách dấu phẩy) nếu có, không thì đăng ký `lt04-{runId}-{i}@lt04.test` qua Mailpit; lấy danh sách sân của `LT04_BRANCH_ID`; chia cho mỗi VU một tập (sân, ngày, giờ) **không giao nhau** ở các ngày `D+20…D+26`, slot 60 phút (giá dưới ngưỡng duyệt).
  - Mỗi lượt: lấy slot kế tiếp của VU ⇒ `POST /api/bookings/online` với `note = "LT04 {runId}"` (đo bằng Trend `lt04_create_booking_ms`) ⇒ kiểm 201; `status ∈ {PENDING, CONFIRMED, PENDING_APPROVAL}`; `PENDING` ⇒ `paymentUrl` có `vnp_ExpireDate`, `paymentLinkExpiresAt` có; ⇒ kiểm mức cọc theo chế độ (dưới) ⇒ `cancel` (200) ⇒ đọc lại `GET /api/me/bookings/{id}`: `status = CANCELLED`, không có khoản hoàn (đơn chưa trả nên số hoàn 0, không sinh Refund) ⇒ nghỉ 2–4 giây.
  - **Kiểm mức cọc theo chế độ** (`LT04_EXPECT`): `fallback` (chạy thử, cờ tắt) ⇒ `depositSource = FULL_PAYMENT_FALLBACK`, `depositRate = 1`, `depositAmount = finalTotal`; `tadss` (lượt chính thức) ⇒ `depositSource` là nguồn TADSS, `depositRate = LT04_EXPECTED_RATE` (giá trị độc lập lấy từ cấu hình strategy của lượt chạy cho hạng STANDARD, khách mới không lịch sử), `depositAmount = round(finalTotal × LT04_EXPECTED_RATE)` tới đồng, làm tròn xa số 0 (INTEGRATION_SPEC §3.3 trường `deposit`; `DepositCalculator` của TADSS). Không lấy `depositRate` của chính response làm chuẩn (R18). Đơn ra `PENDING_APPROVAL` ở chế độ `tadss` ⇒ đếm riêng (slot đã chọn dưới ngưỡng duyệt nên kỳ vọng 0).
  - **Nguồn gốc giá trị kỳ vọng (R29):** chế độ `tadss` bắt buộc thêm `LT04_STRATEGY` (tên + phiên bản, ví dụ `DefaultDecisionStrategy 2.1.0`) và `LT04_EXPECTED_RATE_SOURCE` (file cấu hình và commit TADSS của lượt chạy, khóa `StandardDepositRate`); thiếu ⇒ `setup()` dừng, không chạy. Trước lượt chạy, runbook (E2E_TEST_SPECIFICATION) đối chiếu phiên bản strategy bằng một `GET /api/v2/decisions/{id}` của một đơn thử (trường `execution.strategyName`, `execution.strategyVersion`, INTEGRATION_SPEC §3.4). `handleSummary` ghi chế độ, `LT04_STRATEGY`, `LT04_EXPECTED_RATE`, `LT04_EXPECTED_RATE_SOURCE`, giả định khách (hạng STANDARD, tài khoản mới, không lịch sử) và `runId`. Kết quả chỉ xác minh mức cọc của kịch bản đã chọn, không chứng minh strategy giảm no-show hay tốt hơn chính sách cố định.
  - Hồ sơ `LT04_PROFILE=smoke` (1 VU × 3 lượt) / `full` (20 VU, 10 phút, warm-up 1 phút loại khỏi thống kê). Không thêm vào `config/modes.json` và `load_step.py` (LT-04 không thuộc evaluator).
  - `handleSummary` ghi summary JSON cùng định dạng `output.js` để đưa vào Evidence Pack.
- Ghi vào methodology 4.6: thiết kế đặt-rồi-hủy và lý do; **phạm vi chứng minh của từng lượt** (chạy thử cờ tắt: script, xác thực, đặt/hủy, Mailpit, SC6; lượt chính thức cờ bật: thêm độ trễ có TADSS và mức cọc so với giá trị kỳ vọng độc lập). Hủy ngay không làm lệch kết quả vì (đã đối chiếu mã): hủy muộn chỉ tính khi **đã trả tiền** mà hoàn < 100 % (`CustomerHistoryCalculator.IsLateCancellation`), đơn LT-04 chưa trả; Refund chỉ tạo khi số hoàn > 0; strategy 2.1.0 chỉ dùng `paymentExpiredCount`, `completedCount`, `noShowCount`, hạng và giá trị đơn, không dùng số lần hủy; lịch sử gửi TADSS do BE tính từ dữ liệu BE, không từ outcome. Job outcome sẽ báo dần `CUSTOMER_CANCELLED` (20 đơn/chu kỳ) sau lượt chạy, không ảnh hưởng số đo; nhả sân và thông báo kiểm bằng SC6-3 và Mailpit.

**Kiểm.** `k6 inspect scenarios/lt04.js` không lỗi; chạy `LT04_PROFILE=smoke LT04_EXPECT=fallback` trên BE Docker local kèm Mailpit: 3/3 lượt đạt mọi `check`, Mailpit có thư hủy của 3 đơn; chế độ `tadss` chỉ kiểm logic bằng một lần chạy với dữ liệu giả (`k6 run` có biến `LT04_DRY_RESPONSE` trỏ file JSON mẫu, không gọi BE) cho một ca đúng và một ca sai rate; sau đó chạy bộ kiểm SC6 (Task 11) trỏ `tadss-dev` cho khoảng thời gian của lượt chạy, 0 vi phạm (điều 5 bỏ qua vì cờ tắt, không có `decisionId`). pytest của `tools/evaluation` chạy lại, không đổi kết quả.

---

## Phần F — Tài liệu và kiểm cuối gói

### Task 15 — Đồng bộ tài liệu, danh mục test, build

| File | Sửa |
| :--- | :--- |
| `spec/PHASE6_DESIGN_SPEC.md` | Header + 5.1 (6A-6 ✅, bảng "Đầu vào" đổi thành "đã chốt 29/10" kèm link plan); 4.2: P6-27 giá trị chốt, **P6-28** (N-36), **P6-29** (SC6 bằng C#, điều 5 qua API), **P6-30** (LT-04, Mailpit), P6-21 "không làm trong Phase 6"; 4.3 thêm `PaymentLinkBufferMinutes`; 3.3 T-5 "đã sửa"; 7.8 (hạn link, retry còn dưới 3'); 8.2 (dùng `bookingStatus`, `paymentLinkExpiresAt`, bỏ câu khóa nút theo khoảng đệm); 9.3; 10.1 bước 2 (kết quả Task 0, Task 16); Mục 11 (giới hạn nếu S2 không chặn, job đối soát production là hướng sau); Mục 12 |
| `docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md` | Mục 5 điều 5 qua API; BE-IT-20 (tự kiểm sau mỗi test HTTP, ca ràng buộc DB); 4.6 LT-04 |
| `docs/research/E2E_TEST_SPECIFICATION.md` | Cách chạy bộ kiểm SC6 trên staging (biến môi trường, đọc `status` PASS/FAIL/INCONCLUSIVE); Mailpit cho mọi ca có email; dòng "trả trên trang VNPay đã mở" theo kết quả S2 (giới hạn của hạn link nếu VNPay không chặn lúc trả) |
| `docs/planning/MASTER_EXECUTION_PLAN.md`, `BACKLOG_SPRINTS.md` (T6.10) | Tiến độ; E-STAGING thêm Mailpit |
| `SmashCourt-BE/docs/api/DEPOSIT_APPROVAL_API.md` | 3.1, 3.3 `paymentLinkExpiresAt`; 3.3 thêm 400 khi còn dưới 3'; 3.6 `bookingStatus` + bảng câu; Mục 5 |
| `SmashCourt-BE/CLAUDE.md`, `TADSS-Runtime/CLAUDE.md`, root `CLAUDE.md` | Trạng thái 6A-6; lệnh compose kèm Mailpit; lệnh chạy SC6 staging |

**Danh mục test.** Đối chiếu mọi mã BE-UT/BE-IT của methodology Mục 4 với `Trait("Catalog", …)` trong `SmashCourt-BE.Tests` (lệnh `grep`); mã nào không có test ⇒ ghi lý do hoặc bổ sung.

**Kiểm.** `dotnet build SmashCourt-BE.sln` 0 warning mới; `dotnet test` toàn bộ (cần Docker cho PostgreSQL Testcontainers) xanh, nhóm `Category=Staging` hiện Skipped; `python tools/docs/check_docs.py` đạt; pytest `tools/evaluation` không đổi.

### Task 16 — Thử sandbox với BE thật (làm ngay sau Phần A + B, trước Phần C)

Mức 3 của D3: chứng minh URL do **mã C#** sinh ra được VNPay chấp nhận. Làm sớm để nếu hỏng thì sửa khi Phần A còn mới, không đợi cuối gói. BE Docker (`8cd53b8` + Phần A, B) qua ngrok như 28/10. Ca: (1) đặt sân, trả ngay ⇒ `PAID_ONLINE` (hồi quy với URL tự dựng); (2) đặt sân, đợi qua `paymentLinkExpiresAt` rồi mới mở link ⇒ trang lỗi mã 15, đơn vẫn `PENDING`, Job-01 hủy đúng hạn; (3) theo kết quả S2: mở link trước hạn link, trả sau hạn link ⇒ ghi quan sát; (4) `confirm` của ca (1) ⇒ `bookingStatus = PAID_ONLINE`. Ghi vào spec 10.1 bước 2; dọn đơn thử như 28/10 (ghi chú "TEST VNPAY 6A-6").

---

## Kết quả thực hiện

### Task 0 — Thử sandbox (30/10/2026, 00:04–00:17 giờ VN) ✅

Script Python tạm (đã xóa) ký URL theo cách của Task 2, đọc `.env` không in giá trị; BE và ngrok tắt; kết quả đọc từ URL VNPay chuyển về và kiểm chữ ký bằng HashSecret.

| Ca | Kết quả | Kết luận |
| :---: | :--- | :--- |
| S1 | 50.000 đ, hạn 15', tạo 00:04:16 (qua nửa đêm): trả thành công; `vnp_ResponseCode = 00`, `vnp_TransactionStatus = 00`, đúng số tiền và TxnRef, chữ ký VNPay đúng | **Đạt (mức c).** URL tự dựng có `vnp_ExpireDate`, giờ GMT+7 thanh toán được; Phần A được code |
| S2 | Hạn 3' (00:16:20); mở ngay, nhập thẻ sau hạn ⇒ "Giao dịch đã quá thời gian chờ thanh toán", chuyển về với mã 15, `TransactionStatus = 02`, chữ ký đúng, không trừ tiền | **VNPay chặn lúc trả.** Khoảng đệm có tác dụng đầy đủ; không phải ghi giới hạn "trang đã mở" vào spec Mục 11 |
| S3 | 5.000 đ: mở được trang chọn phương thức | Mức 5.000 đ được nhận lúc mở |
| S4 | 4.000 đ: mở được trang; bước nhập thẻ NCB báo "Số tiền giao dịch không hợp lệ… hạn mức tối thiểu và tối đa" | VNPay chặn dưới mức tối thiểu lúc trả; giữ `MinOnlinePaymentAmount = 5.000 đ` |

Quan sát thêm: đồng hồ "Giao dịch hết hạn sau" của trang VNPay đếm theo `vnp_ExpireDate`. Việc phải kiểm ở Task 16: VNPay có gửi IPN mã 15 cho link quá hạn không; nếu có, BE đi nhánh thất bại (P6-03: Payment `FAILED`, hạn đơn chỉ rút ngắn, không mở lại).

### Task 1 — Chính sách hạn link (30/10/2026) ✅

| Thay đổi | File |
| :--- | :--- |
| `PaymentLinkBufferMinutes` (mặc định 2) | `Configurations/BookingPaymentSettings.cs`, `appsettings.json` |
| `PaymentLinkBufferMinutes ≥ 0`; `InitialHoldMinutes`, `RetryExtendMinutes` ≥ khoảng đệm + số phút tối thiểu của link (lấy từ `PaymentHoldPolicy.MinPaymentLinkLifetime`, một nguồn) | `Configurations/BookingPaymentSettingsValidator.cs` |
| `MinPaymentLinkLifetime = 1'`, `TryGetPaymentLinkExpiry(nowUtc, paymentExpiresAtUtc, bufferMinutes, out linkExpiresAtUtc)` | `Services/Deposit/PaymentHoldPolicy.cs` |

Test (TDD: viết trước, 18 ca đỏ trên khung rỗng rồi mới viết mã): `PaymentHoldPolicyLinkTests` 17 ca (biên đúng 3' / 3' − 1 tick / đúng khoảng đệm / hạn bằng now / hạn đã qua, khoảng đệm 0 và cấu hình khác, giữ phần µs, qua nửa đêm giờ VN, mốc không UTC, khoảng đệm âm); `BookingPaymentSettingsValidatorTests` thêm 9 ca (mặc định 2, âm, bằng 0, `InitialHoldMinutes`/`RetryExtendMinutes` dưới và đúng biên). Đột biến 6/6 bị bắt (`<` thành `<=`, bỏ trừ khoảng đệm, cho khoảng đệm âm, bỏ kiểm `Kind` của hạn, validator bỏ "+1", bỏ luật `RetryExtendMinutes`). Build Release 0 warning; toàn bộ BE **2258/2258** xanh (2232 + 26 mới, 3 phút 26 giây). Kiểm trước: chỉ một unit test đặt `InitialHoldMinutes = 7`, host HTTP dùng mặc định, nên luật validator mới không làm host test nào không khởi động. Spec 7.1 thêm khóa. Chưa có nơi gọi hàm mới (Task 4).

### Task 2 — Ký và dựng URL VNPay (30/10/2026) ✅

| Thay đổi | File |
| :--- | :--- |
| `VnPayHelper`: `BuildSignData` (bỏ giá trị rỗng, sắp khóa ordinal, `WebUtility.UrlEncode`), `Sign` (HMAC-SHA512 **hex chữ hoa**), `BuildPaymentUrl`, `FormatVietnamTime` (UTC ⇒ `yyyyMMddHHmmss` GMT+7, cắt phần lẻ giây) | `Helpers/VnPayHelper.cs` (mới) |
| Chiều IPN ký bằng cùng helper; bỏ `VnPayCompare` và `HmacSha512` riêng (một cách ký cho hai chiều) | `Services/VnPayService.cs` |
| `VNPAY.NET` 2.1.0 vào project test, chỉ để đối chứng | `SmashCourt-BE.Tests.csproj` |

**Khác plan:** plan bản 3 ghi "hex thường"; chạy thử thư viện 30/10 thấy thư viện sinh **hex hoa**, nên helper sinh hex hoa để URL trùng từng ký tự (đúng ý "chọn như thư viện" của plan). Chiều IPN vẫn so không phân biệt hoa thường; S1 ký hex thường cũng được VNPay nhận.

**Test:** `VnPayHelperTests` 16 ca:
- đối chứng mức 1 với thư viện: 3 ca, URL trùng **từng ký tự**, gồm `ReturnUrl` có query và ca có `vnp_BankCode`;
- vector cố định do thư viện sinh (khóa giả): chuỗi ký và chữ ký;
- định dạng hex hoa;
- bỏ giá trị rỗng, sắp ordinal, mã hóa kiểu form (dấu cách, dấu câu, tiếng Việt UTF-8);
- `FormatVietnamTime`: biên 23:59:59 / 00:00:00, qua năm, cắt phần lẻ giây, mốc không UTC.

`VnPayServiceTests` thêm 3 ca khóa hành vi IPN trước khi refactor (xanh trên mã cũ, giữ xanh sau): chữ ký hex hoa từ helper, `vnp_SecureHashType` và khóa lạ không được ký, giá trị rỗng không được ký.

Đột biến 8/8 bị bắt: hex thường, bỏ sắp xếp, sắp không phân biệt hoa, không bỏ giá trị rỗng, mã hóa `Uri.EscapeDataString`, không đổi GMT+7, IPN ký cả `vnp_SecureHashType`, IPN ký cả khóa không tiền tố `vnp_`. Build Release 0 warning; toàn bộ BE **2277/2277** xanh (2258 + 19 mới, gồm mọi luồng IPN qua HTTP). Thư viện vẫn còn trong BE tới Task 3.

### Task 3 + 4 — Cổng nhận hạn link, VNPay tự dựng URL, hai nơi tạo link (30/10/2026) ✅

**Gộp hai task:** đổi chữ ký `IPaymentGateway.CreatePaymentLink` (Task 3) buộc `OnlinePaymentStarter` và hai nơi gọi phải truyền hạn link ngay (Task 4), nếu không thì không build; tách ra chỉ có cách truyền giá trị giả tạm thời nên làm một bước, dừng review một lần.

| Thay đổi | File |
| :--- | :--- |
| `GatewayPaymentRequest(Reference, Amount, OrderInfo, CreatedAtUtc, ExpiresAtUtc)` | `Models/Payments/GatewayPaymentRequest.cs` (mới) |
| `IPaymentGateway.CreatePaymentLink(GatewayPaymentRequest)`; hợp đồng: link hết hạn đúng `ExpiresAtUtc`, không hỗ trợ thì `NotSupportedException`, không bao giờ link không hạn | `Services/IService/IPaymentGateway.cs` |
| `StartAsync(..., nowUtc, linkExpiresAtUtc)`; hạn link không sau `nowUtc` ⇒ `ArgumentOutOfRangeException`; không bắt lỗi của cổng (R27) | `IOnlinePaymentStarter.cs`, `OnlinePaymentStarter.cs` |
| `CreatePaymentLink` tự dựng URL bằng `VnPayHelper`: bộ tham số như thư viện + `vnp_ExpireDate`, hai mốc GMT+7, `vnp_TxnRef` = GUID 32 hex, kiểm `Kind = Utc`, hạn sau lúc tạo, số tiền nguyên đồng > 0; log không có URL đầy đủ; bỏ `IVnpayClient`, `NormalizeTransactionRef` | `Services/VnPayService.cs` |
| Đặt sân: hạn link = hạn thanh toán của transaction 1 − khoảng đệm, tạo lúc `decidedAt`; response `paymentLinkExpiresAt` | `Services/BookingService.cs` |
| Thanh toán lại: kiểm hạn link **trước** transaction; còn dưới khoảng đệm + 1' ⇒ 400 `PAYMENT_HOLD_EXPIRED` "Không còn đủ thời gian thanh toán, vui lòng đặt lại", không gia hạn, không hủy Payment cũ | `Services/PaymentService.cs` |
| `OnlineBookingResponse.PaymentLinkExpiresAt` (ngay sau `ExpiresAt`) | `DTOs/Booking/OnlineBookingResponse.cs` |
| Gỡ `AddVnpayClient` và `using VNPAY.Extensions`; gỡ `PackageReference VNPAY.NET` | `Program.cs`, `SmashCourt-BE.csproj` |
| API doc 3.1 (trường mới), 3.3 (hạn link, trường hợp 400 mới), Mục 5 dòng 19; stack trong `CLAUDE.md` | `docs/api/DEPOSIT_APPROVAL_API.md`, `CLAUDE.md` |

**Gói kéo theo (so `dotnet list package --include-transitive` trước/sau):** không gói nào bị hạ phiên bản; `Microsoft.Extensions.*` vẫn 10.0.8 (đến từ `Microsoft.Extensions.Http.Polly`, không từ `VNPAY.NET`). Mất 8 gói đi cùng thư viện: `VNPAY.NET`, `Microsoft.AspNetCore.Http(.Abstractions, .Features)` 2.3.0, `Microsoft.AspNetCore.WebUtilities` 2.3.0, `Microsoft.Net.Http.Headers` 2.3.0, `Microsoft.Extensions.ObjectPool` 8.0.11, `System.Buffers`, `System.Text.Encodings.Web`; đều có sẵn trong shared framework ASP.NET Core 8 nên không ghim gì.

**Test:**

- `VnPayServiceTests` thêm 12 ca tạo link: GMT+7 qua nửa đêm, `vnp_Amount` × 100, `TxnRef` duy nhất và trả về, URL gốc và callback theo cấu hình, chữ ký kiểm được bằng `ReadResult` và hỏng khi sửa `vnp_ExpireDate`, **đối chứng mức 2** với thư viện (tập khóa = thư viện ∪ {`vnp_ExpireDate`}, mọi tham số chung trùng ở dạng đã mã hóa), đầu vào sai.
- `OnlinePaymentStarterTests`: request mang đúng hai mốc; hạn link không sau now; cổng ném `NotSupportedException` ⇒ lỗi lan ra, không Payment.
- `PaymentServiceRetryDepositTests` thêm 7 ca: link = hạn − 2', đúng 3' trước hạn giữ chỗ (link sống 1'), còn 2'59" / đúng 2' / 10 giây ⇒ 400 và không gọi gia hạn, hủy link cũ, tạo Payment; đơn duyệt sát giờ chơi; khoảng đệm cấu hình 5'.
- `BookingServiceOnlineBookingTests`: request tạo link có `CreatedAtUtc = decidedAt`, `ExpiresAtUtc = hạn − 2'`; `PaymentLinkExpiresAt` đúng, null ở nhánh a′ và chờ duyệt; cấu hình giữ chỗ 7' ⇒ link 5'.
- `HttpDepositDtoJsonTests`: danh sách và thứ tự khóa JSON có `paymentLinkExpiresAt`.
- **HTTP mới** `PaymentLinkExpiryFlowTests` 6 ca: đặt sân ⇒ `vnp_ExpireDate` = hạn − 2' và `vnp_CreateDate` giờ VN, `TxnRef` là mã IPN dùng; IPN vẫn xác nhận đơn; thanh toán lại còn 4' ⇒ link = hạn giữ chỗ − 2'; còn 2'30" và 30" ⇒ 400, hóa đơn, Payment, slot lock không đổi; **R27**: cổng lỗi khi tạo link ⇒ 500 `INTERNAL_ERROR`, transaction rollback, trả cấu hình thì thanh toán lại bình thường.

TDD: 24 test đỏ trên khung (stub chưa trừ khoảng đệm, chưa từ chối, VNPay trả link rỗng) rồi xanh. Đột biến 9/9 bị bắt: bỏ `vnp_ExpireDate`, `vnp_CreateDate` theo UTC, thanh toán lại bỏ khoảng đệm, đặt sân bỏ khoảng đệm, starter `<=` thành `<`, đặt sân không trả hạn link, `TxnRef` theo booking, cho số tiền lẻ, thanh toán lại không trả hạn link. Build Release 0 warning; toàn bộ BE **2305/2305** xanh (2277 + 28 mới, 4 phút 25 giây), gồm mọi luồng IPN qua HTTP.

**Khác plan, có lý do:**

1. Test HTTP của R27 không đăng ký cổng MOMO giả: host dùng chung có `HttpHostSmokeTests` khẳng định không có cổng MOMO. Thay bằng làm cổng VNPay lỗi lúc tạo link (`SetSetting("VnPay:TmnCode", "")`, cơ chế test rate limit đã dùng): cùng đường đi (exception từ `CreatePaymentLink` trong transaction). Riêng `NotSupportedException` kiểm ở unit test của starter.
2. Dữ liệu test "còn dưới 3' trước hạn giữ chỗ" phải có hạn thanh toán đã được kéo tới hạn giữ chỗ (như sau một lần thanh toán lại); để hạn 10' gốc thì luật có sẵn "đã hết hạn thanh toán" (400 `BAD_REQUEST`, 6A-4) chặn trước, đúng hành vi cũ.

**Ghi nhận:**

- `vnp_IpAddr` lấy qua `ResolveClientIp` có sẵn (ưu tiên `X-Forwarded-For`, IP riêng hoặc loopback ⇒ `127.0.0.1`), khác thư viện (gửi thẳng `RemoteIpAddress`) với IP riêng trong Docker; IP công khai thì trùng (đối chứng mức 2). Sandbox S1 nhận `127.0.0.1`.
- Luật validator `InitialHoldMinutes ≥ khoảng đệm + 1'` không tính thời gian chờ quyết định TADSS (tối đa khoảng 1,6 giây): ở cấu hình biên (`InitialHoldMinutes = 3`) link đơn mới có thể sống dưới 1' ⇒ `InvalidOperationException`, transaction 2 rollback, Job-01 dọn đơn. Cấu hình hiện tại (10') còn dư khoảng 7 phút nên không xảy ra; không đổi luật.
- Sửa comment lỗi thời trong `PaymentCancellationTestBase.PayBothLinksAsync` ("VNPAY.NET không gửi hạn phiên").

### Task 5 — `vnpay/confirm` trả `bookingStatus` (30/10/2026) ✅

| Thay đổi | File |
| :--- | :--- |
| `VnPayReturnResult.BookingStatus` (tên enum; null khi chữ ký sai hoặc không tìm thấy giao dịch) | `DTOs/Payment/VnPayReturnResult.cs` |
| Hàm thuần `ReturnMessage(gatewaySuccess, responseCode, bookingStatus, paymentStatus)` theo bảng của Task 5; `isSuccess` giữ nghĩa kết quả cổng; tập "đã hủy" lấy từ `BookingStatusTransition` | `Services/PaymentService.cs` |
| Mã VNPay `15` có câu riêng "Giao dịch đã quá thời gian chờ thanh toán, vui lòng tạo lại link thanh toán" (link quá hạn trả về mã này từ P6-27, sandbox S2) | `Services/PaymentService.cs` |
| API doc 3.6: bảng `isSuccess` / `bookingStatus` / `message`; dòng "tiền về sau khi hủy" nói rõ trước và sau 6A-6 | `docs/api/DEPOSIT_APPROVAL_API.md` |

**Test:** `PaymentServiceTests` thêm 19 ca:

- 12 ca theory theo (trạng thái đơn, trạng thái Payment), gồm `CANCELLED_PENDING_REFUND` + Payment chưa `SUCCESS` ⇒ không hứa hoàn, `CANCELLED` khi IPN chưa về, `CANCELLED_REFUNDED`;
- duyệt mọi trạng thái của `GetCancelledStatuses()` không bao giờ nhận câu "đã xác nhận";
- không tìm thấy giao dịch; cổng thất bại (`24`, `15`, `79`) vẫn trả `bookingStatus`; chữ ký sai không có `bookingStatus`.

HTTP:

- `PaymentFailureFlowTests.MoneyArrivesAfterTheOrderExpired_RefundsAllOfItAtTheBranch` (BE-IT-22) gọi `confirm` **trước** IPN (đơn `CANCELLED` ⇒ câu trung tính, không "thành công", không "hoàn") và **sau** IPN (`CANCELLED_PENDING_REFUND` ⇒ câu hoàn tiền): tái hiện đúng ca 4 ngày 28/10.
- Thêm `PaidNormally_ConfirmReportsTheConfirmedBooking` (hồi quy đơn trả bình thường).

TDD: 19 test đỏ trên khung (DTO chưa gán) rồi xanh. Đột biến 7/7 bị bắt: bỏ điều kiện Payment `SUCCESS`, không bao giờ hứa hoàn, bỏ `PENDING_PAYMENT` khỏi tập đã trả, không trả `bookingStatus`, bỏ qua kết quả cổng, bỏ mã `15`, chỉ xét `CANCELLED` thay tập hủy chung. Build Release 0 warning; toàn bộ BE **2324/2324** xanh (2305 + 19 mới, 3 phút 59 giây).

**Thêm ngoài bảng plan:** câu riêng cho mã `15`, vì từ Task 3 + 4 khách mở link quá hạn sẽ quay về với mã này; trước đó rơi vào câu chung "Giao dịch thất bại".

### Rà soát luồng booking – payment – invoice sau Task 1–5 (30/10/2026, người dùng yêu cầu) ✅

**Phạm vi diff từ `8cd53b8` (trước 6A-6):** không đổi repository, job, `PaymentResultService` (xử lý IPN), `BookingCancellationService`, entity, DB. Mã đổi chỉ ở: tạo link (`VnPayService`, `OnlinePaymentStarter`, hai nơi gọi), kiểm chữ ký IPN (tương đương mã cũ, kiểm vi sai 20.000 query ở Task 2), câu trả về của `confirm`, cấu hình và DTO (thêm trường).

| Bước nghiệp vụ | Hành vi sau 6A-6 | Kiểm |
| :--- | :--- | :--- |
| Đặt sân, transaction 1 | Booking `PENDING`, hóa đơn `UNPAID`/`PREPAID`, `expires_at = now + 10'`, `hold_deadline = now + 15'`, slot lock: **không đổi** | Test 6A-2 giữ xanh |
| Đặt sân, transaction 2 (nhánh a/d) | Ghi mức cọc rồi tạo Payment `PENDING` có `transaction_ref` GUID; link hết hạn `expires_at − 2'`; response thêm `paymentLinkExpiresAt`. Nhánh a′, chờ duyệt, từ chối: không link, không đổi | Unit + HTTP Task 3+4 |
| IPN thành công | `PENDING → PAID_ONLINE`, hóa đơn `PARTIALLY_PAID`, Payment khác của hóa đơn `FAILED`: **không đổi** (tìm Payment theo `transaction_ref` như cũ) | Mọi test IPN qua HTTP; `NewBooking_TheSignedLinkIsPaidThroughIpnAsBefore` |
| IPN thất bại (`24`, `79`, mới gặp `15`) | Payment `FAILED`, hạn rút về `min(expires_at, now + 5')`: **không đổi** | Test 6A-4 |
| Thanh toán lại | Luật hạn giữ chỗ, hạn thanh toán, gia hạn không đổi; thêm: còn dưới 3' ⇒ 400 `PAYMENT_HOLD_EXPIRED`, không ghi gì | Unit + HTTP Task 3+4 |
| Tiền về sau khi hủy, trả hai link, check-out thu phần còn lại | **Không đổi** (P6-07, chốt 24/10, R-3) | Test 6A-4 giữ xanh |
| Trang kết quả (`confirm`) | Chỉ đọc; thêm `bookingStatus`, câu theo trạng thái | Task 5 |

**Lỗi thật phát hiện khi rà, đã sửa:** email "đã duyệt cọc" (`DepositApproved.html`) ghi "thanh toán tiền cọc trước {PayBefore}" với `PayBefore = expires_at`; từ Task 3+4 `retry-payment` từ chối khi còn dưới 3' nên khách làm theo email ở phút chót sẽ bị từ chối. Sửa:

- `PaymentHoldPolicy.LatestPaymentStart(expiresAt, buffer) = expiresAt − buffer − MinPaymentLinkLifetime`, cùng nguồn với `TryGetPaymentLinkExpiry`; test biên chứng minh đúng mốc này còn tạo được link, trễ 1 tick thì không.
- `BookingEmailFactory.BuildApproval(..., paymentLinkBufferMinutes)` ghi `PayBefore` bằng mốc đó; hiển thị `HH:mm` cắt giây nên luôn sớm hơn hoặc bằng, không hứa quá.
- `BookingNotificationService` nhận `IOptionsMonitor<BookingPaymentSettings>` (một nguồn với `retry-payment`, có validator); 4 test builder và 2 file test truyền thêm tham số (cơ học).

Test thêm 9 ca:

- `PaymentHoldPolicyLinkTests` (mốc, đồng nhất với `TryGetPaymentLinkExpiry` ở 3 khoảng đệm, mốc không UTC);
- `DepositApprovalEmailTests` (khoảng đệm 0/5, hạn có giây);
- `BookingApprovalServiceTests`: email ghi 09:42 thay 09:45; khoảng đệm cấu hình 5' ⇒ 09:39.

Test cũ `Approve_Deposit_AnnouncesAfterCommitAndEmailsTheDepositDeadline` khẳng định hạn 09:45 nên đỏ với mã mới; đã sửa theo nghiệp vụ mới. Đột biến 4/4 bị bắt (email ghi hạn cũ, viết cứng khoảng đệm thay đọc cấu hình, factory bỏ qua khoảng đệm, quên 1' tối thiểu). Build 0 warning; toàn bộ BE **2333/2333** xanh.

**FE hiện tại (develop, trước 6B):** vẫn chạy được với BE mới vì mọi thay đổi API là thêm trường; việc 6B phải làm ghi ở spec 8.2 dòng "Tác động của 6A-6 lên FE". Ghi nhận không sửa: tiêu đề trang kết quả FE theo `isSuccess` nên ca tiền về sau khi hủy vẫn có tiêu đề "Thanh toán thành công!" (câu `message` bên dưới đã đúng) cho tới 6B.

### Task 16 — Thử sandbox với BE thật (30/10/2026, 20:58–21:14 giờ VN) ✅

BE Docker từ commit `9945121` (Phần A, B và sửa email) trên DB dev, cờ TADSS tắt nên mọi đơn cọc 100 %; IPN qua ngrok (cùng URL đã khai với terminal); đồng hồ máy và container khớp. Đơn thử BK-20261030-000154 (A), 000155 (B), 000156 (C), ghi chú "TEST VNPAY 6A-6"; tạo 20:58:17–20:58:23, hạn thanh toán 10', hạn giữ chỗ 15'. Kết quả `confirm` lấy bằng cách gửi lại đúng query của lần callback (log BE) tới `POST /api/payments/vnpay/confirm`, vì callback chuyển trình duyệt sang FE trên Vercel.

| Ca (plan) | Quan sát | Kết luận |
| :--- | :--- | :--- |
| URL do mã C# sinh | Ba link: `vnp_CreateDate` giờ VN (20261030205819…), `vnp_ExpireDate` = hạn thanh toán − 2' (21:06:17, 21:06:20, 21:06:22), `vnp_TxnRef` GUID 32 hex, `vnp_Amount` × 100 | Đúng thiết kế P6-27, T-5 |
| (1) A trả ngay | Trả bằng thẻ NCB lúc 21:05:39; IPN `00` tới 21:05:41, chữ ký đúng; đơn `PAID_ONLINE`, hóa đơn `PARTIALLY_PAID` | **Đạt.** Link tự dựng của BE thật được VNPay nhận (mức 3 của D3) |
| (4) `confirm` của A | `isSuccess = true`, `bookingStatus = PAID_ONLINE`, "Thanh toán thành công! Đặt sân của bạn đã được xác nhận." | **Đạt** (P6-28) |
| (3) B mở trước hạn link, trả sau hạn | Mở 21:05:54 (còn 26 giây), trả tiếp sau 21:06:20 ⇒ VNPay báo quá thời gian, chuyển về 21:06:26 với mã `15`; **không có IPN** (cả buổi chỉ 1 IPN, của A; kiểm tới 21:14:44, 8 phút sau); `confirm` ⇒ `isSuccess = false`, `bookingStatus = PENDING`, câu riêng của mã 15 | **Đạt.** Trùng S2; link quá hạn không sinh IPN nên BE không đi nhánh thất bại P6-03, đơn giữ hạn cũ |
| (2) Job-01 hủy đúng hạn | B: hạn 21:08:20, hủy 21:09:17 (`PAYMENT_EXPIRED`, hóa đơn `EXPIRED`); C: hạn 21:13:22, hủy 21:14:06 | **Đạt** (chu kỳ job khoảng 1') |
| Thêm: thanh toán lại (C, không mở link đầu) | 21:08:07 (còn 15 giây tới hạn thanh toán): 200, hạn = hạn giữ chỗ 21:13:22, link mới hết hạn 21:11:22. 21:10:27 (còn 2'55" tới hạn giữ chỗ): **400 `PAYMENT_HOLD_EXPIRED`** "Không còn đủ thời gian thanh toán, vui lòng đặt lại", đơn không đổi (`updatedAt` vẫn 21:08:07) | **Đạt** (Task 3+4 trên BE thật) |

**Khác plan:** ca (2) "đợi qua hạn link rồi mới mở" không làm riêng: người dùng mở link B lúc còn 26 giây và trả sau hạn, tức ca (3). Hành vi mở link đã quá hạn đã quan sát ngày 28/10 (trang lỗi mã 15 ngay khi mở, spec 10.1 bước 2), không đổi vì cùng tham số `vnp_ExpireDate`.

**Ghi nhận ngoài phạm vi:** lúc khởi động, Job-04 đánh `NO_SHOW` 3 đơn thử ngày 28/10 (đã trả, giờ chơi sáng 30/10): đúng thiết kế 6A-5. Đơn A (`PAID_ONLINE`, sân 08:00 ngày 31/10) cũng sẽ bị Job-04 đánh `NO_SHOW` nếu để nguyên; giữ hay xử lý đơn thử trên DB dev do người dùng quyết.

### Task 6 — Khách mới theo ngày VN (T-9) (30/10/2026) ✅

| Thay đổi | File |
| :--- | :--- |
| Hàm private `VietnamDaysToUtcRange(fromDate, toDate)` ⇒ nửa khoảng `[ToUtcFromVietnam(fromDate, 00:00); ToUtcFromVietnam(toDate + 1, 00:00))`; ba chỗ đếm khách mới (`GetDashboardSummaryAsync`, `GetCustomerStatisticsReportAsync`, `GetCustomerAcquisitionTrendAsync`) dùng `>=` và `<` thay `ToDateTime(..., DateTimeKind.Utc)` với `<=` | `Repositories/ReportRepository.cs` |
| Xu hướng khách mới đọc `CreatedAt` rồi nhóm trong bộ nhớ theo `DateOnly.FromDateTime(DateTimeHelper.ToVietnamTime(createdAt))` (trước: `CreatedAt.Date` theo UTC) | `Repositories/ReportRepository.cs` |
| `toDate` mặc định = `DateTimeHelper.GetTodayInVietnam()` (trước: ngày UTC; 00:00–06:59 VN báo cáo mặc định thiếu hôm nay) | `Services/ReportService.cs` |

Nhánh lọc theo chi nhánh đếm theo `BookingDate` (`DateOnly`, đã là ngày VN) nên không đổi. Không đổi DTO, route hay câu trả về.

**Test (TDD, đỏ đúng lý do trên mã cũ: đếm 3 thay 2, xu hướng gộp vào một ngày):** `Integration/ReportTimeZoneTests.cs` 4 ca trên schema thật, khách tạo lúc 23:59:59 VN ngày 19, 00:00 và 23:59:59,999999 VN ngày 20, 00:00 và 06:00 VN ngày 21:

- dashboard và thống kê khách, khoảng 20/11–20/11 ⇒ 2;
- xu hướng 20–21/11 ⇒ `2020-11-20` = 2, `2020-11-21` = 2;
- khoảng nhiều ngày 19–20/11 ⇒ 3, xu hướng 19 = 1, 20 = 2.

**Khác plan:** mốc dùng năm 2020 thay 2026 để không trùng khách mà test khác tạo bằng đồng hồ thật; dữ liệu thêm khách 06:00 VN ngày 21 vì với bộ mốc của plan mã cũ cũng đếm ra đúng 2 (trùng hợp), test không đỏ. `ReportService` không test được (đồng hồ hệ thống, 7.14): kiểm bằng review.

Đột biến 5/5 bị bắt: dashboard `<=` biên cuối, thống kê `<=` biên cuối, xu hướng nhóm theo ngày UTC, helper bỏ "+1 ngày", helper biên đầu theo UTC. Build Release 0 warning; toàn bộ BE **2337/2337** xanh (2333 + 4, 3 phút 24 giây).

**Rà lại toàn BE sau Task 6 (người dùng yêu cầu, 30/10).** Quét mọi chỗ đổi `DateOnly`/`TimeOnly`/`DateTime`, lấy "hôm nay", định dạng mốc thời gian ra chuỗi, lọc cột timestamptz theo tham số. Đúng: mọi "hôm nay" dùng `GetTodayInVietnam`; Job-04, check-in tính bằng mốc ngày giờ đầy đủ; các nhóm số liệu khác của báo cáo theo `BookingDate` (đã là ngày VN); dự báo lấp đầy dùng giờ tường VN cả hai phía. Hai lỗi thật mới, người dùng chốt sửa trong gói: **T-15** (Task 8a), **T-16** (Task 8b). Thấy nhưng không phải lỗi: `bookingDate` ra `"dd/MM/yyyy 07:00:00"` do converter coi `DateTime` không Kind là UTC (FE chỉ lấy phần ngày của trường này); log IPN in chuỗi ký (không có HashSecret). Ba gợi ý của một bản review ngoài (file tài nguyên đa ngôn ngữ cho câu VNPay, số liệu tỉ lệ link hết hạn, circuit breaker cho VNPay) không làm: BE chưa có hạ tầng đa ngôn ngữ và số liệu, tỉ lệ đơn không trả kịp đọc được từ DB (`cancel_reason = PAYMENT_EXPIRED`), và BE không gọi mạng tới VNPay (URL ký tại chỗ, IPN là chiều vào, hoàn tiền mặt) nên không có lời gọi để bọc.

### Task 7 — "Sắp bắt đầu" và "nguy cơ không đến" qua nửa đêm (T-10) (30/10/2026) ✅

| Thay đổi | File |
| :--- | :--- |
| Hằng `UPCOMING_WINDOW_MINUTES = 30`, `NoShowRiskWindow = 15'`; hàm private `UpcomingLimit(now)`: `now + 30'` sang ngày sau ⇒ `TimeOnly.MaxValue`; KPI (`GetManagerDashboardKpisAsync`) và thẻ sân (`GetManagerDashboardLiveCourtsAsync`) dùng chung | `Repositories/ReportRepository.cs` |
| Thẻ "nguy cơ không đến": `nowTime − StartTime ≤ 15'` (khi `StartTime ≤ nowTime`) thay `StartTime.AddMinutes(15) ≥ nowTime` | `Repositories/ReportRepository.cs` |

**Khác plan, có lý do:** plan chỉ nêu hai chỗ `now + 30'`. Đọc mã thấy chỗ thứ ba cùng lỗi: `StartTime.AddMinutes(15)` quay về 00:xx nên đơn bắt đầu sau 23:45 không bao giờ hiện thẻ "nguy cơ không đến"; sửa cùng task. Đã kiểm các chỗ còn lại dùng giờ: Job-04 (`NO_SHOW`) và check-in tính bằng mốc UTC đầy đủ nên không có lỗi này; danh sách "sắp tới" không có giới hạn trên nên không quay vòng. Test để ở file riêng `ManagerDashboardMidnightTests.cs` (khác bộ dữ liệu với T-9) thay vì chung `ReportTimeZoneTests.cs`.

**Test** (`Integration/ManagerDashboardMidnightTests.cs`, 27 ca trên schema thật, mỗi test một chi nhánh riêng, "now" là giờ tường VN truyền vào):

- sắp bắt đầu, đơn 23:50–23:59 lúc 23:30:00 / 23:30:01 / 23:45 / 23:49:59 ⇒ có trong KPI và thẻ `UPCOMING_CHECK_IN`;
- cửa sổ 30' (hồi quy, so cả KPI lẫn thẻ): 10:20 / đúng 10:30 có, 10:31 không, đã tới giờ không, 23:20 ⇒ 23:50 có, 23:19:59 không, đầu ngày 00:00 ⇒ 00:30 có, 00:31 không;
- sau 23:30 vẫn bỏ đơn đã check-in và đơn đã hủy;
- nguy cơ không đến, đơn 23:50 lúc 23:50 / 23:55 / 23:59, đơn 23:45 lúc 23:59 ⇒ `NO_SHOW_RISK`;
- cửa sổ 15' (hồi quy): đúng giờ bắt đầu và đúng 15' có, 15'01" không, trước giờ bắt đầu không, đầu ngày 00:00 ⇒ 00:15 có, 00:15:01 không; đơn đã check-in không.

TDD: 9 ca đỏ trên mã cũ (đúng các ca quay vòng), 18 ca hồi quy xanh trước và sau.

### Task 8 — Lọc giao dịch điểm theo ngày (T-11) (30/10/2026) ✅

| Thay đổi | File |
| :--- | :--- |
| `StartOfVietnamDayUtc(date)`, `EndOfVietnamDayUtcExclusive(date)` (ngày cuối của lịch ⇒ `DateTime.MaxValue` UTC, không tràn) | `Helpers/DateTimeHelper.cs` |
| Lọc `[StartOfVietnamDayUtc(FromDate); EndOfVietnamDayUtcExclusive(ToDate))` theo phần ngày của tham số, mỗi biên độc lập | `Repositories/CustomerManagementRepository.cs` |
| T-9 (Task 6) chuyển sang hai hàm trên: một nguồn biên ngày VN | `Repositories/ReportRepository.cs` |
| Comment tham số: ngày theo giờ VN, bỏ phần giờ, `ToDate` tính hết ngày (wire không đổi) | `DTOs/CustomerManagement/LoyaltyTransactionDto.cs` |

**Lỗi do Task 6 gây ra, phát hiện và sửa trong Task 8:** biên trên của Task 6 là `toDate.AddDays(1)`, tràn với `toDate = 9999-12-31` (khoảng hợp lệ ≤ 365 ngày vẫn chạm được) ⇒ báo cáo khách hàng từ 200 thành 500. Thấy khi thêm ca ngày cực trị cho T-11; sửa bằng `EndOfVietnamDayUtcExclusive`, thêm test cho cả báo cáo.

**Test:**

- `Integration/LoyaltyTransactionDateFilterTests.cs` 11 ca (schema thật; giao dịch 23:50 VN ngày 19, 00:00 / 23:30 / 23:59:59,999999 ngày 20, 00:00 / 00:10 ngày 21): một ngày ⇒ đúng 3 giao dịch của ngày 20; chỉ `FromDate` ⇒ từ 00:00 ngày 20; chỉ `ToDate` ⇒ tới hết ngày 20; không lọc ⇒ đủ; phần giờ và `Kind` của tham số không đổi kết quả (Unspecified 00:00 / 15:00, Utc 00:00 / 23:00, Local); khoảng ngược ⇒ rỗng; không lẫn giao dịch của khách khác.
- `Integration/Http/LoyaltyTransactionFilterHttpTests.cs` 6 ca qua model binding thật (OWNER): `?fromDate=2020-11-20&toDate=2020-11-20` ⇒ 200, đúng giao dịch, `createdAt` trả `"20/11/2020 23:30:00"`; chỉ `toDate` ⇒ gồm ngày cuối; không lọc; ngày cực trị `0001-01-01` / `9999-12-31` ⇒ 200 (mã trước khi sửa: 500).
- `Unit/Helpers/VietnamTimeZoneTests.cs` thêm 5 ca: hai biên là hai nửa đêm VN theo UTC, `Kind = Utc`, dài đúng một ngày, liền nhau (thường, qua năm, 29/02); ngày cuối của lịch không tràn; ngày đầu của lịch kẹp về `DateTime.MinValue`.
- `ReportTimeZoneTests` thêm 1 ca: khoảng chạm ngày cuối của lịch không tràn ở dashboard và thống kê khách.

TDD: 12/14 ca T-11 đỏ trên mã cũ đúng lý do (Npgsql từ chối `Kind = Unspecified`/`Local`, HTTP 500, `Kind = Utc` lọc lệch ngày).

**Đột biến Task 7 + 8: 12/12 bị bắt:** bỏ kiểm sang ngày sau, `>` thành `>=`, cửa sổ 31', KPI hoặc thẻ sân quay lại công thức cũ (từng chỗ), "nguy cơ không đến" công thức cũ, `<=` thành `<`; T-11 cận trên `<=`, cận dưới `>`, cận trên không cộng ngày; helper bỏ chặn ngày cuối lịch, helper đầu ngày theo UTC. Build Release 0 warning; toàn bộ BE **2387/2387** xanh (2337 + 50, 3 phút 9 giây).

---

## Ràng buộc chung

- Không SQL thô trong mã ghi của BE (`ExecuteUpdateAsync`); không đổi schema. Bộ kiểm SC6 chỉ đọc, viết bằng LINQ; duy nhất phần báo cáo quyết định mồ côi đọc bảng của TADSS bằng một phép chiếu chỉ đọc trong mã test.
- Mốc thời gian lưu và so sánh ở UTC; VNPay nhận GMT+7 qua `VnPayHelper.FormatVietnamTime`; mã mới lấy "bây giờ" từ `TimeProvider` (hai nơi tạo link đã có `now` từ `TimeProvider`).
- Nghiệp vụ thanh toán không gắn VNPay: khoảng đệm và luật "còn quá ít thời gian" ở `PaymentHoldPolicy`; cổng chỉ nhận mốc UTC.
- Thay đổi API chỉ là thêm trường (`paymentLinkExpiresAt`, `bookingStatus`) và một trường hợp mới của mã lỗi sẵn có; không mã lỗi mới.
- Không bật `EnableTadssDecision`; TADSS không đổi `src/`; không chạm Neon ngoài lượt chạy thử LT-04 đã nêu ở 0.5.
- Không in `TmnCode`, `HashSecret`, mật khẩu tài khoản thử; script tạm của Task 0 xóa sau khi dùng.
- Test: luật thuần ở `Unit/`, DB thật ở `Integration/`, HTTP ở `Integration/Http/`; mỗi test nêu được đột biến làm nó đỏ. Chỉ sửa lỗi thật, kèm test; quyết định mới ghi vào spec.
- Công cụ đo không bao giờ biến "không kiểm được" thành "đạt" (SC6 staging, LT-04).
- Vi phạm SC6 lộ ra là lỗi thật thì sửa kèm test và điều chỉnh ước lượng, không biến thành ngoại lệ để giữ suite xanh.

## Rủi ro

| Rủi ro | Xử lý |
| :--- | :--- |
| Link tự dựng không mở được trang trả tiền (lý do của `2c26ef7` chưa rõ) | Task 0 S1 chặn trước khi code; test đối chứng thư viện khóa cách ký từng ký tự |
| VNPay không kiểm hạn link khi trang đã mở (S2) | Vẫn chặn mở link muộn; ghi giới hạn vào spec Mục 11; P6-07 giữ tiền an toàn |
| Đổi chữ ký `IPaymentGateway` làm vỡ nhiều test mock | Đổi cơ học, giữ ý từng test; chạy toàn bộ unit trước khi sang Task 4 |
| Tự kiểm SC6 sau mỗi test HTTP làm đỏ hàng loạt test cũ | Phân loại từng vi phạm: lỗi thật thì sửa kèm test, dữ liệu dựng có chủ đích thì bỏ qua kèm lý do (chỉ khi để lại dữ liệu vi phạm); ghi số liệu vào kết quả; ước lượng Phần D đã cộng nửa ngày cho việc này |
| Bộ kiểm báo nhầm do đồng hồ (slot lock vừa hết hạn, job chưa chạy) | SC6-2 chỉ xét đơn còn hạn duyệt; SC6-3 chỉ xét slot lock còn hạn; staging chạy sau khi kịch bản kết thúc ít nhất một chu kỳ job (1 phút) |
| LT-04 chạy thử để lại dữ liệu trên `tadss-dev` | Chỉ 1 tài khoản và 3 đơn đã hủy, ghi chú "LT04"; người dùng quyết định giữ hay xóa |
| Mailpit làm lệch cấu hình chạy hằng ngày | File compose phụ, chỉ dùng khi chỉ định `-f`; mặc định giữ Gmail và SSL |
| URL do mã C# sinh ra khác script Python của Task 0 | Đối chứng mức 1, 2 với thư viện; Task 16 làm ngay sau Phần A + B |
| Lượt LT-04 chính thức hiểu nhầm là đã chứng minh từ lượt chạy thử | Methodology 4.6 ghi phạm vi chứng minh của từng lượt; chế độ `LT04_EXPECT` bắt buộc khai báo |
| Bộ kiểm SC6 staging gặp lỗi mạng/TADSS rồi báo đạt | `inconclusiveChecks` và `status = INCONCLUSIVE`; test chỉ đạt khi `PASS` |

## Thứ tự và ước lượng

Thứ tự làm: **Task 0** ⇒ (S1 đạt) **Task 1–4** + chạy lại toàn bộ test IPN ⇒ **Task 5** ⇒ **Task 16** (sandbox với mã C#) ⇒ **Task 6–8** ⇒ **Task 8a, 8b** ⇒ **Task 9–11** ⇒ **Task 12–14** ⇒ **Task 15**. LT-04 chạy thử chỉ sau khi Phần A đã qua Task 16 và bộ kiểm SC6 đã xong.

| Phần | Task | Ước lượng | Rủi ro |
| :--- | :--- | :---: | :--- |
| A | 0–4 | 2 ngày | Cao: phụ thuộc sandbox, đổi chữ ký cổng, hồi quy IPN |
| B + sandbox | 5, 16 | 1 ngày | Thấp–trung bình |
| C | 6–8, 8a, 8b | 1,5 ngày | Trung bình: biên ngày giờ trên schema thật |
| D | 9–11 | 2,5 ngày | Cao: có thể lộ lỗi dữ liệu và test cũ |
| E | 12–14 | 1,5 ngày | Trung bình–cao: môi trường, OTP qua Mailpit |
| F | 15 | 1 ngày | Trung bình: phụ thuộc số lỗi phát hiện |

Tổng khoảng 9 ngày (ước lượng kỹ thuật; không cắt test để giữ mốc). Phụ thuộc: Task 0 trước 2–4; Task 1 trước 4; Task 2 trước 3 trước 4; Task 4 và 5 trước 16; Task 4 trước 14 (LT-04 kiểm `paymentLinkExpiresAt`); Task 9 trước 10, 11; Task 12 trước 13, 14; Task 11 trước phần kiểm SC6 của Task 14; Task 6–8, 8a, 8b độc lập (8a, 8b thêm 30/10 sau khi rà lại ngày giờ toàn BE).
