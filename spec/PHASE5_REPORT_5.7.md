# Báo cáo gói 5.7 — Lượt đánh giá đầy đủ và Gate M5

> **Trạng thái 18/10/2026:** lượt đầy đủ `EV-20261017-EVAL-full-r1` đã chạy trọn sáu bước (a…f). Gate M5 **đạt kèm một sai lệch đã giải thích** (bước (b): 0,04 % / 0,11 % response 504 ở LT-02 / LT-05 do máy bận, Mục 3). Evidence Pack chính thức **chưa** chép vào `docs/research/evidence-packs/` theo quyết định của người dùng: chỉ lưu trữ một lần cho lượt đầy đủ cuối cùng trên strategy 2.1.0 (Mục 7). Task 10 của `PHASE5_PLAN_5.7_M5_FREEZE.md`.

## 1. Kết luận Gate M5

| Tiêu chí Gate M5 (MASTER_EXECUTION_PLAN) | Trạng thái | Minh chứng |
| :--- | :--- | :--- |
| Toàn bộ test case TADSS (UT, IT, CT, FI, LT mức TADSS, MT) đạt trên hạ tầng thật; không EF InMemory | **Đạt**, trừ sai lệch LT-02/LT-05 (Mục 3) | `full-r1`: 73 mã danh mục đạt, 0 trượt; SC8 PASS; package InMemory đã gỡ từ gói 5.1 |
| Contract v2 đóng băng | **Đạt** (11/10/2026, `2.0.0`) | `docs/contracts/baseline_public_api.txt`, `openapi-v2.yaml`, `schemas/v2/` không đổi tới nay |
| Công cụ đánh giá chạy bằng một lệnh và đã có lượt đầy đủ sinh `evidence/<run_id>/` | **Đạt** | `evidence/EV-20261017-EVAL-full-r1/` (manifest SHA-256 `2d9fe86503e56e853470f5a3ff7d8a481fe9f45328c389d9d8c5dca751d996c8`, EV-1 khớp 346 file) |
| Danh mục kiểm thử toàn hệ thống viết xong; API contract BE ↔ FE đã chốt | **Đạt** (18/10/2026) | Danh mục BE/FE/E2E-01…14 (methodology 4.7–4.8); `SmashCourt-BE/docs/api/DEPOSIT_APPROVAL_API.md` đã review (T5.29), Q1…Q12 chốt |

Không nới tiêu chí nào. Sai lệch ở bước (b) được giữ nguyên trong Evidence Pack (mã thoát evaluator 1) và giải thích ở Mục 3; lượt đầy đủ chạy lại khi máy rảnh sẽ thay thế.

## 2. Lượt `EV-20261017-EVAL-full-r1`

Commit TADSS `906baa6`, `dirty = false`, strategy mặc định **2.0.0**. Máy: Docker Desktop 8 CPU, 7,6 GB. Chạy 17/10/2026, 18:47–22:13 (giờ máy).

| Bước | Nội dung | Kết quả | Thời lượng đo thật |
| :---: | :--- | :--- | ---: |
| a | Test gộp (unit, IT), 3 script verify, pytest ba bộ, `check_docs`, OB-2 | đạt; 2130 kết quả test → 2164 dòng theo 58 mã | 550 s |
| b | k6 LT-01 ×3, LT-02, LT-05 | **trượt** (504: LT-02 55, LT-05 195) | 6681 s |
| c | SCALE-2 rồi LT-03 tiêm sự cố | đạt (SCALE-2 0 lỗi 5xx / 9501 request; FI-02, 03, 07, 08 đạt) | 1069 s |
| d | Schemathesis (500 ví dụ/operation), coverage, Stryker | đạt (CT-03 6905 ca, 0 lỗi; dòng 98,5 %, nhánh 89,5 %) | 2705 s |
| e | RP (215 booking) và PT (118.674 request) | đạt (0 lỗi contract) | 762 s |
| f | Evidence Pack, KPI, báo cáo | đạt | 517 s |
| | **Tổng** | | **12.284 s ≈ 3 giờ 25 phút** |

Ước tính cũ 2,8 giờ thấp hơn thực tế; tài liệu đã cập nhật số đo (Mục 6).

| KPI | Kết quả | Giá trị |
| :--- | :--- | :--- |
| SC1 | PASS | min tỷ lệ server ≤ 150 ms 99,10 %; p95 client lớn nhất 56,2 ms (3 lượt LT-01) |
| SC2 | PASS | fallback `APPROVED` cọc 100 %, pipeline ≤ 500 ms, ≥ 99 % ≤ 700 ms (LT-03 FI-02/03) |
| SC3 | PASS | 0 thiếu, 0 thừa (467.731 khóa chính + 118.674 khóa PT) |
| SC4 | PASS | 0 vi phạm |
| SC5, SC6 | NOT_APPLICABLE | thuộc SmashCourt-BE (Phase 6–8) |
| SC7 | PASS | 118.674 bản ghi PT, 0 lỗi contract, 0 lần thử lại |
| SC8 | PASS | 2164/2164 ca; MT-01 85,0 %, MT-02 98,3 %, MT-03 73,9 % (ngưỡng 70 %) |

EV-1…EV-5 đạt. RP: 215/215, `APPROVED` 173, `NEED_APPROVAL` 42, 86 đơn lệch do approval window (Q-04). PT-03/PT-04: 66.860 đơn so sánh, đối soát shadow `RECONCILED`.

## 3. Sai lệch bước (b): response 504 ở LT-02 và LT-05

**Hiện tượng.** LT-02 (quá tải 10 → 400 RPS): 55 response 504 trên 139.568 (0,04 %). LT-05 (50 RPS, 60 phút): 195 response 504 trên 179.196 (0,11 %), kèm 52 quyết định fallback. 504 là Outer timeout của TADSS (`GATEWAY_TIMEOUT`), không phải nginx. Trong audit, các bản fallback có thời gian pipeline 532–3.052 ms trong khi Inner timeout 120 ms và Outer timeout 500 ms, tức bộ hẹn giờ chạy trễ: tiến trình thiếu CPU.

**Nghi vấn và kiểm chứng.** Mọi lượt trước commit `42261aa` (chạy strategy trên luồng riêng `LongRunning`, sửa FI-05 trên CI 2 CPU) không có 504, nên nghi thay đổi này. Thí nghiệm A/B cùng máy, cùng tải LT-02 quá ngưỡng, xen kẽ hai bản (17/10/2026, 22:38–22:58):

| Lượt | Bản | Tải đạt tối đa | p95 ở bậc cao nhất | 504 |
| :--- | :--- | :--- | ---: | ---: |
| a1 | `LongRunning` (hiện tại) | 379 RPS | 2,2 s | 0 |
| b1 | `Task.Run` (trước `42261aa`) | sập ở ~185 RPS | 8–12 s | 0 |
| a2 | `LongRunning` | sập ở ~173 RPS | 12–16 s | 7 |
| b2 | `Task.Run` | 373 RPS | 1,4 s | 0 |

Cùng một bản mà hai lượt khác hẳn nhau, và cả hai bản cùng sập trong 22:43–22:53: kết quả do tình trạng máy quyết định, không do code. Khi máy ổn định (a1, b2) hai bản tương đương. Kết luận: **giữ `LongRunning`** (vẫn cần cho FI-05 trên CI 2 CPU).

**LT-05 không trôi.** Độ trễ theo cửa sổ 10 phút: p50 10,6–16,3 ms suốt 60 phút; lỗi chỉ dồn ở phút 10–40 rồi về bình thường (phút 40–50: p95 18,7 ms, 0 lỗi). Đây là các đợt khựng tạm thời của máy, không phải rò rỉ.

| Phút | Request | p50 (ms) | p95 (ms) | p99 (ms) | 504 | Fallback |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: |
| 0–10 | 30.004 | 12,6 | 41,4 | 124,8 | 0 | 0 |
| 10–20 | 29.523 | 15,7 | 183,5 | 727,6 | 30 | 5 |
| 20–30 | 29.685 | 16,3 | 1.513,2 | 13.174,5 | 159 | 47 |
| 30–40 | 30.004 | 11,5 | 65,9 | 429,0 | 6 | 0 |
| 40–50 | 30.002 | 10,6 | 18,7 | 51,1 | 0 | 0 |
| 50–60 | 29.978 | 12,7 | 49,9 | 173,5 | 0 | 0 |

**Ý nghĩa nghiệp vụ.** Tiến trình bị đóng băng lâu hơn ngân sách 500 ms thì trả 504 là đúng thiết kế; BE coi 504 là TADSS lỗi và áp cọc 100 % (D-13), nên an toàn nghiệp vụ không đổi. Người dùng xác nhận đã mở nhiều ứng dụng trong lúc chạy. **Không nới tiêu chí** "không có 5xx ở bước tải"; lượt đầy đủ chạy lại trên máy rảnh (tắt ứng dụng nặng, tắt chế độ ngủ) sẽ thay thế.

**Giới hạn của phân tích:** chỉ một lượt có 504; log container phủ đoạn LT-05 đã bị xoay vòng (log chỉ còn từ 21:00), nên không đối chiếu được log tiến trình lúc khựng; snapshot dashboard không có metric luồng/GC.

## 4. Giới hạn dữ liệu

| Giới hạn | Ảnh hưởng | Xử lý |
| :--- | :--- | :--- |
| CSV SmashCourt là **dữ liệu thử của nhóm**: 215 booking, 10 tài khoản; 78 `NO_SHOW` (36 %), có khách 29 lần hết hạn thanh toán | RP chỉ minh họa cơ chế, không đại diện hành vi khách thật | Báo cáo RP ghi rõ; không hiệu chỉnh ngưỡng chính sách theo dữ liệu này |
| 12 booking có `member_since` sau ngày tạo đơn | Lịch sử hạng không nhất quán | Giữ nguyên, ghi nhận (không sửa dữ liệu nguồn) |
| 8 dòng sân có giá 0 | Tổng tiền thấp bất thường ở vài đơn | Giữ nguyên, ghi nhận |
| Dataset khách sạn không có khái niệm hết hạn thanh toán (`paymentExpiredCount = 0`) | PT không kiểm được quy tắc từ chối của strategy 2.1.0 | Ghi nhận; quy tắc chỉ thể hiện trên RP |
| Một lượt khởi động lạnh còn treo 9,3 s ngay sau khi build image (thí nghiệm khởi động lạnh, 16/10) | Chưa giải thích | Theo dõi ở lượt sau |
| Không đếm được log `ERR` của `eval-tadss-*` (yêu cầu 2 của Task 10) | Không kiểm lại M-01 trên lượt thật | Log đã xoay vòng và dữ liệu Docker đã xóa; kiểm ở lượt chạy lại |

## 5. Thay đổi sau Gate M5 (18/10/2026)

`full-r1` đo trên strategy **2.0.0**. Sau Gate, theo quyết định của người dùng:

1. **`DefaultDecisionStrategy` 2.1.0 (T5.31):** trả `REJECTED` (`REPEATED_PAYMENT_EXPIRY`) khi `paymentExpiredCount ≥ PaymentExpiryRejectThreshold` (mặc định 5, 0 = tắt) và lớn hơn `completedCount`; xét trước các quy tắc duyệt. Không đổi SDK, contract hay baseline. Unit 1914/1914, IT 244/244 đạt. Ngưỡng 5 chưa hiệu chỉnh; độ nhạy trên 134 đơn online của RP: ngưỡng 3 / 5 / 8 / 10 từ chối 50 % / 37 % / 28 % / 24 % (dữ liệu thử, chỉ minh họa). `policy_economics.py` đếm `REJECTED` riêng; `dataset_analysis.py` không còn tính đơn bị từ chối vào `no_decision`.
2. **Review API BE ↔ FE (T5.29):** đối chiếu `DEPOSIT_APPROVAL_API.md` với mã BE, sửa R-1…R-8; chốt Q1…Q12. Q12 giải quyết Q-6/B-1: hoàn = tổng đã thanh toán × % hoàn, nên `REFUND_OF_PAID` là chính sách của PT-03.
3. **Tài liệu cho báo cáo:** `docs/report/` (kiến trúc, use case UC-DEPOSIT-01…12 và UC-TADSS-01…08, tuần tự, hoạt động, trạng thái); E2E-14 (đặt sân bị từ chối).

## 6. Tài liệu cập nhật theo số đo

- `docs/guides/EVALUATION_RUN_GUIDE.md`, `docs/PHASE5_SUMMARY.md`: thời lượng bản đầy đủ đo thật ≈ 3 giờ 25 phút (b 1 giờ 51 phút, d 45 phút).
- `docs/PHASE5_SUMMARY.md`, BACKLOG (T5.21), MASTER_EXECUTION_PLAN (Gate M5): trạng thái Gate M5 và lượt `full-r1`.

## 7. Việc còn mở

| Việc | Ai | Ghi chú |
| :--- | :--- | :--- |
| Chạy lại lượt đầy đủ trên strategy 2.1.0, máy rảnh | Người dùng | Thay thế `full-r1`; xác nhận bước (b) sạch; đo lại MT-02 trên 2.1.0 |
| Chép Evidence Pack của lượt cuối vào `docs/research/evidence-packs/{run_id}/` (M-10) | SV1 | Cần sửa `check_docs` để coi thư mục pack là một mục kiểm kê (pack ~700 MB, 349 file); file > 20 MB đính kèm GitHub Release |
| Đổi nhãn "tạm thời" của biến thể hoàn tiền trong báo cáo evaluator | SV1 | Q-6 đã chốt `REFUND_OF_PAID` |
| T5.24 góp ý contract, T5.25 Playwright, NG-4 | SV2 / SV1 | Như kế hoạch |
| B-2 (nghi lệch +7 giờ khi xuất) | SV1 | Trace sau |

Docker: người dùng đã xóa toàn bộ dữ liệu Docker sau lượt chạy; không còn image/container/volume của stack đánh giá. Thí nghiệm A/B tự dọn container, volume và image của nó; log A/B còn ở `%TEMP%\tadss-ab` (xóa được).
