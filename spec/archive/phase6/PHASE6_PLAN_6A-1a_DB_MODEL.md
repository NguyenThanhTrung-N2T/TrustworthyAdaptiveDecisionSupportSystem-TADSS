# Gói 6A-1a — DB, model và phân loại trạng thái (T6.1)

**Mục tiêu:** model EF khớp schema Phase 6 đã áp trên `tadss-dev`; integration test chạy trên đúng schema đó; `PENDING_APPROVAL` được phân loại ở mọi danh sách trạng thái. Tổng quan và ràng buộc chung: [6A-1](PHASE6_PLAN_6A-1_FOUNDATION.md).

**Spec:** 6.2 (script), 7.2 (enum, entity), 7.12 (phân loại), 9.2 (fixture), P6-13, P6-15, P6-16. **Test:** BE-IT-01, BE-UT-06, BE-UT-11.

---

### Task 1 — Ánh xạ enum dùng chung và enum mới

**File:** `Models/Enums/BookingStatus.cs`, `Models/Enums/{DepositSource,ApprovalResult,BookingCancelReason}.cs` (mới), `Extensions/NpgsqlEnumMappingExtensions.cs` (mới), `Program.cs`, `Data/SmashCourtContext.cs`.

**Giải pháp:**
- `BookingStatus.PENDING_APPROVAL = 10`, đặt cuối (FE dùng giá trị số, N-4).
- Ba enum mới, giá trị và thứ tự đúng y `CREATE TYPE` của script 6.2; tên PostgreSQL `deposit_source`, `approval_result`, `booking_cancel_reason`.
- Gom 22 dòng `MapEnum` của `Program.cs` thành **một** extension method trên `NpgsqlDataSourceBuilder` (thêm 3 enum mới). `Program.cs` và fixture test cùng gọi hàm này ⇒ test không bao giờ lệch ánh xạ với app (fixture hiện **không** map enum).
- `HasPostgresEnum` cho 3 enum mới trong `SmashCourtContext`. Giữ nguyên `PaymentTiming` thừa (không phải lỗi, spec 6.1).

**Xong khi:** build xanh; app khởi động với `tadss-dev` (Npgsql nạp được kiểu enum mới).

### Task 2 — Entity, mapping và slot lock nhiều sân

**File:** `Models/Entities/Booking.cs`, `Models/Entities/Invoice.cs`, `Data/SmashCourtContext.cs`.

**Giải pháp:**
- `Booking` thêm 10 thuộc tính, `Invoice` thêm 4 thuộc tính theo spec 7.2; `HasColumnName` đúng tên cột script; độ dài `HasMaxLength(64)`/`(500)`; `ApprovalReasons` là `List<string>?` (Npgsql map `text[]` sẵn); tiền `decimal`, tỷ lệ `decimal`.
- `ApprovalResolvedBy` chỉ là khóa ngoại `Guid?`, **không** thêm navigation (chưa ai cần, YAGNI).
- P6-13: `Booking.SlotLock` ⇒ `ICollection<SlotLock> SlotLocks`; mapping `HasOne/WithOne` ⇒ `HasMany/WithOne`. Đã kiểm 20/10: navigation cũ chỉ xuất hiện ở mapping nên không có chỗ gọi nào phải sửa. Đổi này sửa lỗi có sẵn: EF xóa lock "mồ côi" khi đặt online nhiều sân (spec 6.1).
- Mọi cột mới cho phép NULL, không default ở EF ⇒ mã cũ không đổi câu INSERT/UPDATE.

**Xong khi:** build xanh; test hiện có xanh trên fixture cũ (trước Task 3) để tách bạch lỗi model với lỗi schema.

### Task 3 — Fixture integration trên schema thật (P6-16)

**File:** `SmashCourt-BE.Tests/Integration/PostgreSqlIntegrationFixture.cs`, `SmashCourt-BE.Tests/TestData/Database/schema_smashcourt.sql` (bản sao, mới), `SmashCourt-BE.Tests/SmashCourt-BE.Tests.csproj`, test đồng bộ bản sao (mới).

**Giải pháp:**
- Image `postgres:17-alpine` ghim minor cụ thể (≥ 17.6 để `psql` hiểu `\restrict`), khớp Neon 17.11; bản dump có `SET transaction_timeout` chỉ PostgreSQL 17 hiểu.
- Nạp schema bằng `psql -v ON_ERROR_STOP=1` **bên trong container** (chép file vào container rồi exec): xử lý đúng meta-command của pg_dump, lỗi nào cũng làm fixture đỏ. Tạo role `neondb_owner` trước (các dòng `OWNER TO`).
- Bỏ `EnsureCreatedAsync`. `DbContext` của test dựng từ `NpgsqlDataSource` có ánh xạ enum dùng chung (Task 1). Giữ cơ chế cô lập hiện có: mỗi test một transaction, rollback khi xong.
- Bản sao schema: nguồn chuẩn là `TADSS-Runtime/docs/database/schema_smashcourt.sql` (dump từ `tadss-dev`). Test đồng bộ so nội dung (bỏ dòng `\restrict`, CRLF) khi thư mục `../../TADSS-Runtime` tồn tại; không có thì skip (CI chỉ checkout BE). Không chạy `phase6_deposit.sql` trong fixture (spec 9.2).
- Dữ liệu seed vi phạm ràng buộc thật ⇒ sửa seed theo nghiệp vụ, ghi danh sách vào báo cáo gói.

**Xong khi:** toàn bộ integration test hiện có xanh trên schema thật; đo thời gian khởi động fixture và tổng thời gian bộ integration trước/sau, ghi vào `SmashCourt-BE/CLAUDE.md` (Mục test) cùng cách cập nhật bản sao schema.

### Task 4 — BE-IT-01: schema Phase 6 qua EF

**File:** `SmashCourt-BE.Tests/Integration/Phase6SchemaTests.cs` (mới).

**Ca kiểm:**
- Ghi rồi đọc lại booking `PENDING_APPROVAL` có đủ trường duyệt, `ApprovalReasons` nhiều phần tử, `CancelReason`; hóa đơn có đủ 4 trường cọc ⇒ giá trị giữ nguyên, thời điểm `Kind = Utc`.
- `PENDING_APPROVAL` thiếu `TadssDecisionId` ⇒ DB từ chối (`chk_bookings_pending_approval`); `CancelReason` trên đơn chưa hủy ⇒ từ chối (`chk_bookings_cancel_reason`); hóa đơn có `DepositAmount` mà thiếu `DepositSource` ⇒ từ chối.
- Một booking 2 sân ghi 2 slot lock qua `SlotLocks` ⇒ đọc lại đủ 2 (hồi quy của probe 19/10).

### Task 5 — Phân loại trạng thái (P6-15)

**File:** `Helpers/BookingStatusTransition.cs` và các danh sách ở spec 7.12: `BookingRepository` (heatmap, `GetActiveByCourtAndDateAsync`), `TimeGridService`, `BranchRepository`/`CourtRepository.HasActiveBookingsAsync`, `CourtRepository` (timeline), `CourtService` (active, nhãn "Chờ duyệt", timeline), `ReportRepository` (sân sắp tới, dự báo); test `SmashCourt-BE.Tests/Helpers/BookingStatusTransitionTests.cs`.

**Giải pháp:**
- `BookingStatusTransition` có các **tập có tên**: giữ sân (active), đã kết thúc, đã hủy, và "cố ý không giữ sân". `PENDING_APPROVAL` thuộc tập giữ sân.
- `CanTransition` thêm đúng các cạnh ở 7.12: `PENDING_APPROVAL → PENDING | CONFIRMED | CANCELLED`, `PENDING → CONFIRMED`, `CANCELLED → CANCELLED_PENDING_REFUND`, `PAID_ONLINE → CANCELLED`.
- Thêm `PENDING_APPROVAL` vào **đúng** những danh sách đang có `PENDING` theo 7.12; ưu tiên dùng tập của `BookingStatusTransition` thay vì liệt kê lại khi chỗ đó vốn dùng đúng tập active. **Không** thêm vào Job-01/02/04, `GetNoShowEligibleStatuses`, check-in.
- `TimeGridService`: ô của đơn `PENDING_APPROVAL` là `LOCKED`; đếm ngược lấy từ slot lock (đã gia hạn tới `approval_expires_at` ở 6A-2), không đọc cột mới ở đây.

**Test:** BE-UT-06 (thuộc tập active; chỉ đúng các cạnh của sơ đồ DEPOSIT_APPROVAL_API 2.1, không sang thẳng `PAID_ONLINE`); BE-UT-11 (duyệt mọi giá trị `BookingStatus`, mỗi giá trị nằm trong ít nhất một tập có tên; thêm giá trị mới mà không phân loại ⇒ đỏ).

**Xong khi:** test hiện có của các repository/service bị đụng vẫn xanh.
