# Gói 6A-1 — Nền BE cho luồng cọc TADSS: tổng quan

**Mục tiêu:** SmashCourt-BE có đủ nền để 6A-2…6A-6 xây luồng cọc: DB và model khớp schema Phase 6, client TADSS gọi đúng contract v2, bộ dựng request quyết định có lịch sử khách đúng nghĩa. **Không đổi hành vi nào người dùng thấy**: chưa service nào gọi TADSS (nối vào ở 6A-2), cờ `EnableTadssDecision` mặc định `false`.

**Spec:** [PHASE6_DESIGN_SPEC.md](PHASE6_DESIGN_SPEC.md) Mục 5.1 (6A-1), 6, 7.1–7.4, 7.12, 7.14, 9.1–9.2. **Test:** methodology Mục 4.7–4.8 (BE-UT-01, 02, 06, 09, 11, 12; BE-IT-01; CT-04).

**Nhánh:** `SmashCourt-BE` `feat/phase-6-deposit` (tách từ `develop` 20/10/2026). DB: Neon `tadss-dev` đã áp `phase6_deposit.sql` (19/10).

## Ba plan con, theo thứ tự

| Plan | Nội dung | Task spec | Phụ thuộc |
| :--- | :--- | :--- | :--- |
| [6A-1a](PHASE6_PLAN_6A-1a_DB_MODEL.md) | Enum, entity, fixture test trên schema thật, phân loại trạng thái | T6.1 | — |
| [6A-1b](PHASE6_PLAN_6A-1b_TADSS_CLIENT.md) | Cấu hình, DTO contract v2, `TadssClient` | T6.2 | — (làm song song 1a được) |
| [6A-1c](PHASE6_PLAN_6A-1c_DECISION_REQUEST.md) | Lịch sử khách, dựng request, `BookingDepositService` | T6.3 | 1a (entity, fixture), 1b (DTO, client) |

## Ràng buộc chung (mọi task)

- Kiến trúc và tên file theo skill `smashcourt-be-architecture`; mỗi service/repo mới đăng ký DI trong `Program.cs`.
- Thời gian theo spec 7.14: mã **mới** nhận `TimeProvider` khi cần "bây giờ"; mốc ghi DB `Kind = Utc`; giờ chơi đổi bằng `DateTimeHelper.ToUtcFromVietnam`; DTO TADSS chỉ dùng `DateTimeOffset`.
- Mã cũ chỉ sửa đúng chỗ plan nêu; không refactor lan. Lỗi có sẵn ngoài phạm vi ⇒ ghi lại, không tự sửa.
- Test mới gắn mã danh mục bằng `[Trait("Catalog", "BE-UT-xx")]` cạnh `[Trait("Category", ...)]` hiện có; tên test `{Method}_When{Condition}_{Expected}`.
- Không commit/push: người dùng tự commit một lần cho cả gói (gợi ý ở cuối).
- Không đọc/in giá trị bí mật; khóa API TADSS chỉ nằm ở User Secrets / biến môi trường.

## Kiểm tra cuối gói (Definition of Done)

- [x] `dotnet build` 0 lỗi; toàn bộ test hiện có (226) + test mới xanh, gồm integration (cần Docker Desktop). *(21/10: 501 test)*
- [x] *(chạy BE khởi động sạch; hồi quy tay luồng đặt/thanh toán/hủy chưa làm)* Chạy BE với `tadss-dev`, cờ tắt: khởi động không lỗi (DI, `ValidateOnStart`); đặt sân online, thanh toán sandbox, hủy chạy như trước.
- [x] Mỗi mã test của 6A-1 có ít nhất một test gắn trait tương ứng.
- [x] Tài liệu đồng bộ (bảng dưới); `python tools/docs/check_docs.py` (TADSS) đạt.

## Đồng bộ tài liệu trong gói

| Tài liệu | Sửa |
| :--- | :--- |
| Spec 9.2 | Fixture nạp **bản sao** `SmashCourt-BE.Tests/TestData/Database/schema_smashcourt.sql` (CI của BE chỉ có repo BE); test so khớp với bản ở TADSS khi có thư mục anh em |
| Spec 7.3 | Đã ghi 20/10: loại lỗi `InvalidResponse` (không thử lại, log Error kèm trích đoạn body) |
| `SmashCourt-BE/CLAUDE.md` | Mục test: integration chạy trên schema thật (PostgreSQL 17), thời gian fixture đo được, cách cập nhật bản sao schema |

## Rủi ro

| Rủi ro | Ứng phó |
| :--- | :--- |
| Dữ liệu seed của test hiện có vi phạm ràng buộc thật (`CHECK`, exclusion) khi bỏ `EnsureCreated` | Sửa dữ liệu seed cho đúng nghiệp vụ, **không** nới ràng buộc; ghi lại test nào phải sửa |
| Thêm `PENDING_APPROVAL` vào danh sách trạng thái đổi kết quả truy vấn cũ | Chưa có dòng nào mang trạng thái này cho tới 6A-2; BE-UT-11 khóa việc phân loại |
| Bản sao schema/JSON schema lệch nguồn ở TADSS | Test đồng bộ chạy khi có thư mục `../TADSS-Runtime` (máy dev), bỏ qua trên CI |

**Commit gợi ý (người dùng tự chạy, một commit cho cả gói):**
`feat(booking): Phase 6A-1 foundation — deposit schema model, TADSS v2 client, decision request builder`
