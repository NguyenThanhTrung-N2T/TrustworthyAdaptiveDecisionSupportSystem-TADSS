# Gói 6A-1b — Cấu hình và client TADSS v2 (T6.2)

**Mục tiêu:** BE gọi được 4 endpoint TADSS đúng contract v2 (header, timeout, thử lại, phân loại lỗi), có cấu hình kiểm khi khởi động. Chưa service nào gọi client này (6A-2 nối vào). Tổng quan: [6A-1](PHASE6_PLAN_6A-1_FOUNDATION.md).

**Spec:** 7.1 (cấu hình), 7.3 (client), 7.14 (thời gian); contract `TADSS-Runtime/docs/contracts/INTEGRATION_SPEC.md` §3–4 và `schemas/v2/`. **Test:** BE-UT-01, CT-04.

**Quyết định kỹ thuật của plan:**
- **Không dùng Polly** cho client này: chỉ thử lại đúng một lần cho một hàm; vòng thử lại tường minh dễ kiểm chính xác "cùng idempotency key, không thử lại 4xx" hơn pipeline Polly của `FastApiClient`.
- **Thêm `TadssFailureKind.InvalidResponse`** (2xx nhưng body không parse được, thiếu trường bắt buộc hoặc `status` lạ) bên cạnh `Timeout`, `Network`, `Http`. Chốt 20/10 sau review, đã ghi spec 7.3:
  - **Không thử lại**: nguyên nhân thực tế là cố định (trang HTML của ngrok, `BaseUrl` trỏ nhầm dịch vụ), gọi lại cho cùng kết quả. Thử lại vẫn an toàn về idempotency (TADSS trả bản đã lưu cho cùng key + nội dung) nhưng vô ích.
  - Service xử lý như mọi lỗi TADSS: nhánh (d) cọc 100 %. Nếu TADSS đã ghi quyết định, quyết định đó không có `tadss_decision_id` phía BE nên không có outcome (như ca timeout sau khi TADSS ghi); log là căn cứ đối soát.
  - Log **Error**: correlation id, idempotency key, HTTP status, `Content-Type`, độ dài body, 200 ký tự đầu của body (bỏ xuống dòng). Response không chứa bí mật; quy tắc không log body **request** giữ nguyên.
- JSON schema của contract và response mẫu được **chép** vào project test (CI của BE không có repo TADSS), có test đồng bộ như bản sao schema DB ở 6A-1a.

---

### Task 1 — Cấu hình (7.1) và đồng hồ

**File:** `Configurations/TadssSettings.cs`, `Configurations/BookingPaymentSettings.cs`, validator tương ứng (`IValidateOptions<T>`) cùng thư mục, `Program.cs`, `appsettings.json`.

**Giải pháp:**
- Hai lớp settings đúng khóa, mặc định và ràng buộc của bảng 7.1 (section `TadssConfig`, `BookingPayment`). Đăng ký `AddOptions<T>().Bind(...).ValidateOnStart()` + validator; nơi dùng đọc qua `IOptionsMonitor<T>` (đổi cấu hình có hiệu lực ngay, BE-IT-19 ở 6A-2).
- Ràng buộc chéo: cờ bật ⇒ `BaseUrl` tuyệt đối và `ApiKey` không rỗng; `PaymentFailureGraceMinutes ≤ PaymentHoldMaxMinutes`; `PaymentHoldMaxMinutes ≥ InitialHoldMinutes`; `BranchTimeZone` là mã IANA tìm được.
- `appsettings.json` chỉ có giá trị mặc định, **không** có `ApiKey`. Khóa dev: User Secrets `TadssConfig:ApiKey` (người dùng giữ khóa hiện có, lấy từ `.env` của TADSS).
- Đăng ký `TimeProvider.System` (singleton) cho mã mới của Phase 6.

**Test:** validator: cờ tắt thiếu khóa ⇒ hợp lệ; cờ bật thiếu khóa / URL tương đối ⇒ lỗi có thông báo; từng ràng buộc khoảng và chéo.

### Task 2 — DTO contract v2 và kiểm theo JSON schema (CT-04)

**File:** `Integrations/Tadss/DTOs/*` (mới), `Integrations/Tadss/TadssJson.cs` (options serialize riêng), `SmashCourt-BE.Tests/TestData/Tadss/v2/` (schema + mẫu, mới), test `SmashCourt-BE.Tests/Integrations/TadssContractTests.cs`; package test `JsonSchema.Net` (draft 2020-12).

**Giải pháp:**
- Mỗi schema một nhóm DTO: decision request/result, override request/response, outcome request/response, decision detail; thêm DTO đọc problem+json (`code`, `title`, `detail`, `errors`). Tên trường camelCase đúng schema.
- Mọi trường thời gian là `DateTimeOffset`; tiền `decimal`; enum dạng chuỗi (`status`, `tier`, `channel`, `outcome`, `action`). `reasonCode` để `string` (mã lạ không làm lỗi, INTEGRATION_SPEC §6).
- `JsonSerializerOptions` riêng của TADSS (camelCase, enum chuỗi, bỏ null khi ghi ⇒ `REJECT` không có khóa `deposit`). **Không** đi qua `VietnamDateTimeConverter` của MVC (T-6).
- Bản sao schema/mẫu kèm test đồng bộ với `TADSS-Runtime/docs/contracts/schemas/v2/` (skip khi không có thư mục anh em).

**Test (CT-04):** mỗi mẫu response (`APPROVED` thường và fallback, `NEED_APPROVAL`, `REJECTED`, override `APPROVE`/`REJECT`, outcome) hợp lệ schema và parse đủ trường; mỗi dòng bảng lỗi INTEGRATION_SPEC §3.8 có một mẫu problem+json parse đúng `code`; request do DTO serialize (đánh giá, override hai loại, outcome) hợp lệ schema request; `reasonCode` lạ vẫn parse.

### Task 3 — `TadssClient` và kết quả gọi

**File:** `Integrations/Tadss/ITadssClient.cs`, `Integrations/Tadss/TadssClient.cs`, `Integrations/Tadss/TadssCallResult.cs`, `Integrations/Tadss/TadssFailureKind.cs` (mới), `Program.cs`; test `SmashCourt-BE.Tests/Integrations/TadssClientTests.cs`.

**Giải pháp:**
- Bốn hàm theo bảng 7.3 (`EvaluateAsync`, `OverrideAsync`, `GetDecisionAsync`, `ReportOutcomeAsync`); typed `HttpClient` qua `AddHttpClient`, `BaseAddress` từ settings, `HttpClient.Timeout` vô hạn.
- Header: `X-Api-Key` (giá trị khóa), `X-Idempotency-Key` (`bookingId`; `{decisionId}:{APPROVE|REJECT}`; `{decisionId}:OUTCOME`), `X-Actor-Id` chỉ ở override, `X-Correlation-ID` (lấy từ request hiện tại nếu có, không thì sinh mới), `ngrok-skip-browser-warning: true`.
- Timeout **mỗi lần gọi** = `TimeoutMs` bằng `CancellationTokenSource` liên kết với token của caller; caller hủy thì trả về hủy, không đếm là timeout.
- Chỉ `EvaluateAsync` thử lại **đúng một lần**, cùng idempotency key, khi timeout / lỗi mạng / 5xx; không thử lại 4xx (gồm 401, 409, 413, 429).
- Không ném exception cho lỗi TADSS: trả `TadssCallResult<T>` (thành công + `T`, hoặc thất bại + kind, HTTP status, `code`, `Retry-After`). 415 không có `code` ⇒ phân loại theo status.
- Log: thời lượng, status, `code`, correlation id; **không bao giờ** log khóa API hay body request.

**Test (BE-UT-01, `HttpMessageHandler` giả):** đủ header từng hàm; timeout cắt request đúng `TimeoutMs`; retry một lần với cùng key ở timeout/5xx/lỗi mạng, không retry ở 400/401/409/413/429; problem+json ⇒ đúng `code`, `Retry-After` của 429/503; 2xx body hỏng (JSON sai, HTML, `status` lạ) ⇒ `InvalidResponse`, **không** retry, log Error có trích đoạn ≤ 200 ký tự; caller hủy ⇒ không retry; log không chứa khóa API (logger giả bắt message).

---

## Kết quả triển khai (20/10/2026)

3 task xong trong một buổi. BE: build Release 0 lỗi 0 cảnh báo, **390 test xanh** (260 trước đó + 130 mới, gồm integration trên PostgreSQL thật).

**Kiểm với TADSS thật (20/10, chạy TADSS từ mã nguồn với PostgreSQL cục bộ, không chạm Neon):** đã gọi đủ 4 hàm, gồm đơn thường, replay cùng key, `NEED_APPROVAL`, override APPROVE/REJECT (và replay, `ALREADY_OVERRIDDEN`), chi tiết quyết định, outcome (và replay, `OUTCOME_ALREADY_RECORDED`), sai khóa (401), không tồn tại (404), correlation id xuất hiện trong log TADSS. Lần kiểm này tìm ra 3 lỗi mà test giả không bắt được, đã sửa và có test:
- Validator chấp nhận mã múi giờ Windows (`SE Asia Standard Time`) trong khi TADSS từ chối 400 `VALIDATION_ERROR` ở mọi lần gọi: nay dùng cùng quy tắc với TADSS (IANA và tìm được trên máy).
- `X-Correlation-ID` do FE gửi có ký tự ngoài ASCII làm cả request hỏng thành lỗi mạng giả: nay chỉ nhận ASCII hiển thị ≤ 128 ký tự, không thì dùng `TraceIdentifier`, rồi GUID. `ApiKey` chứa khoảng trắng/xuống dòng bị validator từ chối.
- Client tự theo redirect: POST bị đổi thành GET và `X-Api-Key` bị gửi sang host khác. Nay tắt (`AllowAutoRedirect = false`), 3xx là lỗi HTTP. Đăng ký DI chuyển vào `Extensions/TadssExtensions.cs` (`AddTadssIntegration`) để test resolve client từ container. Chạy BE với `tadss-dev`: cờ tắt khởi động sạch; cờ bật thiếu khóa thì dừng ngay với thông báo của validator.

Khác plan (đã ghi vào spec 7.3):
- Client đọc `BaseUrl` / `ApiKey` / `TimeoutMs` từ `IOptionsMonitor` mỗi lần gọi thay vì đặt `BaseAddress` cố định lúc tạo `HttpClient`: `HttpClient` của typed client sống theo handler (2 phút), đọc mỗi lần mới đáp ứng "đổi cấu hình hiệu lực ngay" (BE-IT-19).
- Thêm kiểm INV-7 vào `EvaluateAsync` (APPROVED thiếu `deposit`, NEED_APPROVAL thiếu `approvalExpiresAt` ⇒ `InvalidResponse`) để service 6A-2 không gặp null.
- Caller hủy token ⇒ ném `OperationCanceledException` (không có loại lỗi riêng trong `TadssFailureKind`).
- Bản sao schema + mẫu nằm ở `SmashCourt-BE.Tests/TestData/Tadss/v2/`; test đồng bộ `TadssContractSyncTests` so với `TADSS-Runtime/docs/contracts/schemas/v2/` (bỏ qua khi không có thư mục anh em).
- Mẫu lỗi problem+json (`problems.json`) phủ đủ 15 mã của §3.8 + 415 không có `code`.
