# 🔒 CONTRACT_V1_FREEZE — Decision Contract v1.0.0 Formal Governance Manifest

> **Status:** 🟢 **FROZEN / LOCKED (MILESTONE 2 COMPLETED)**  
> **Package ID:** `TADSS.Shared.DecisionContractSDK`  
> **Frozen Package Version:** `1.0.0`  
> **API Contract Version:** `v1.0.0`  
> **Effective Freeze Date:** 2026-09-19  
> **Git Target Commit SHA:** `4b8b1124a2a0d492a868a5f85b0c798bc7a8d3d5` (Branch: `feat/phase-2-contract-sdk`)  
> **Target Runtime:** .NET 8.0 (SDK `10.0.401`)  
> **Authors & Sign-off:** TADSS Core Engineering Team & SmashCourt Architecture Board  

---

## 1. Executive Summary & Freeze Declaration

Văn bản này đánh dấu thời điểm **chính thức đóng băng (Contract Freeze)** toàn bộ giao diện tích hợp wire contract, cấu trúc dữ liệu truyền tin (DTOs), định dạng JSON Schema, tập luật bất biến miền (Domain Invariants), và thư viện dùng chung `TADSS.Shared.DecisionContractSDK` phiên bản **1.0.0**.

### 1.1 Nguyên Tắc Đóng Băng (Freeze Scope Principle)
> **IMPORTANT:**  
> **Quy định Đóng băng Hợp đồng áp dụng đối với Bề mặt Hợp đồng Wire Bên ngoài (Externally Observable Wire Contract & Schemas), không áp dụng cho các cải tiến/tối ưu hóa triển khai nội bộ (Internal Runtime Implementation).**  
> Các hoạt động trong Phase 3 như bổ sung chiến lược quyết định mới, tối ưu hóa thuật toán, cải thiện logging/telemetry, cấu hình resilience pipeline đều được thực hiện trên nền tảng Contract v1.0.0 mà không làm thay đổi ranh giới hợp đồng đã đóng băng.

### 1.2 Hiệu Lực Đóng Băng
Kể từ thời điểm công bố văn bản này:
1. **Public API Surface** của `Shared.DecisionContractSDK` được cố định bất biến theo snapshot tại [`baseline_public_api.txt`](./baseline_public_api.txt).
2. **7 JSON Schemas** tại thư mục [`schemas/`](./schemas/) là chuẩn mực bắt buộc (canonical immutable wire contract) cho mọi giao tiếp HTTP giữa `SmashCourt-BE` và `TADSS Decision Runtime`.
3. **Mọi thay đổi cấu trúc wire JSON** bị nghiêm cấm trên phiên bản `1.x.x` để bảo toàn tính tương thích của các client có chế độ validate nghiêm ngặt (`additionalProperties: false`). Bất kỳ thay đổi wire format nào đều phải tuân thủ [Quy trình Quản trị Thay đổi (Section 6)](#6-breaking-change--evolution-policy) và nâng cấp lên `v2.0.0`.

---

## 2. Milestone 2 Quality Gate Audit (9/9 Gates PASSED)

Tất cả các tiêu chí nghiệm thu đã lập kế hoạch cho Milestone 2 đều được thực thi và kiểm chứng đầy đủ thông qua bộ kiểm thử tự động và các chốt chặn kỹ thuật (quality gates):

```text
             ┌─────────────────────────┐
             │   Common & Enums Layer  │
             └────────────┬────────────┘
                          │
                ┌─────────┴─────────┐
                ▼                   ▼
      ┌──────────────────┐  ┌──────────────────┐
      │   Domain Layer   │  │    DTO Layer     │
      │ (Business Model) │  │  (Wire Models)   │
      └─────────┬────────┘  └────────┬─────────┘
                │                    │
                └─────────┬──────────┘
                          ▼
              ┌──────────────────────┐
              │    Mappings Layer    │
              │ (Two-Way Converters) │
              └──────────────────────┘
```

| Gate # | Tiêu chí đánh giá (Quality Gate Criteria) | Ngưỡng yêu cầu (Target) | Bằng chứng & Kết quả thực tế (Actual Evidence) | Trạng thái |
| :---: | :--- | :--- | :--- | :---: |
| **Gate 1** | **Architectural Purity & Dependency DAG** | 0 external NuGet dependencies, pure .NET 8 BCL. Strict DAG: `Common/Enums` $\rightarrow$ `Domain` / `DTOs` $\rightarrow$ `Mappings`. `Domain` không phụ thuộc `DTOs` hay `Mappings`. | Kiểm chứng bởi `SdkDependencyPurityTests.cs` (3/3 tests PASS). 0 external NuGet packages. | 🟢 **PASS** |
| **Gate 2** | **Mechanical Compilation & Zero Warnings** | `<GenerateDocumentationFile>true</GenerateDocumentationFile>` + `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`. 100% XML docstrings. | `dotnet build -c Release --warnaserror` $\rightarrow$ 0 Warning(s), 0 Error(s). 0 CS1591 warnings. | 🟢 **PASS** |
| **Gate 3** | **Contract Stability & Public API Surface** | Cố định baseline API surface. CI/CD tự động phát hiện breaking changes. | Kiểm chứng bởi `PublicApiSurfaceTests.cs` đối chiếu với `baseline_public_api.txt` (Strict read-only verification + breaking change simulation PASS). | 🟢 **PASS** |
| **Gate 4** | **Wire Protocol & Schema Conformance** | 7 JSON Schemas khớp chặt chẽ DTOs. Strict `SCREAMING_SNAKE_CASE` (Option A). Từ chối non-canonical casing và numeric strings. | Kiểm chứng bởi `ContractSchemaCompatibilityTests.cs` (12 tests) & `AdversarialContractMappingTests.cs` (12 tests) bao phủ required fields, types, formats, enums, additionalProperties. | 🟢 **PASS** |
| **Gate 5** | **Domain Invariant Enforcement** | Fail-fast tại Aggregate Root (`Reservation`) và Value Objects (`PaymentPolicy`, `ReservationSchedule`, `CustomerInfo`, `ResourceInfo`). | Kiểm chứng bởi `DomainInvariantsTests.cs` (17 tests) bao gồm boundary precision: 29m59s fail / 30m pass, deposit 50.0%, 0-price free slots. | 🟢 **PASS** |
| **Gate 6** | **DTO Validation & Boundary Safety** | DataAnnotations đầy đủ trên DTOs. Null safety & generic `ApiResponse<T>`, `PagedResult<T>` boundary pagination. | Kiểm chứng bởi `DtoValidationContractTests.cs` (18 tests). | 🟢 **PASS** |
| **Gate 7** | **Tiered Code Coverage & Hardening** | Overall SDK $\ge 80\%$, Domain $\ge 95\%$, DTOs $\ge 85\%$. An toàn đa văn hóa (vi-VN, fr-FR) & đa luồng (100 parallel tasks). | **SDK Line Coverage: 99.49%** (Domain: 98.91%, DTOs: 100%, Common: 100%). Tests: `CrossCultureInvariantTests.cs` & `ConcurrentReadSafetyTests.cs` PASS. | 🟢 **PASS** |
| **Gate 8** | **NuGet Packaging & Consumer Testing** | Đóng gói `.nupkg` chuẩn hóa metadata (README, License MIT, Tags). Consumer test độc lập xác thực tính khả dụng. | `dotnet pack -c Release` sinh `TADSS.Shared.DecisionContractSDK.1.0.0.nupkg`. `SmashCourtConsumerReferenceTests.cs` (2 tests PASS). | 🟢 **PASS** |
| **Gate 9** | **Independent CI/CD Isolation** | Tách rời hoàn toàn pipeline CI giữa 2 repository `SmashCourt-BE` và `TADSS-Runtime`. Zero relative path dependencies. | CI workflow chạy độc lập trên GitHub Actions, cả 2 repository build & test 100% green. | 🟢 **PASS** |

---

## 3. Inventory of Frozen Artifacts

### 3.1 Frozen JSON Schemas (7 Files)

Tất cả Schemas đều tuân thủ JSON Schema Draft `2020-12`, thiết lập `additionalProperties: false` và định danh canonical URI `https://tadss.smashcourt.vn/schemas/v1/`:

| # | File Name | Schema Title | Mô tả nghiệp vụ |
|---|-----------|--------------|-----------------|
| 1 | [`decision-request.schema.json`](./schemas/decision-request.schema.json) | `DecisionRequest` | Payload `SmashCourt-BE` gửi sang để yêu cầu đánh giá đặt sân tự động |
| 2 | [`decision-result.schema.json`](./schemas/decision-result.schema.json) | `DecisionResult` | Kết quả đánh giá tự động do TADSS Engine phản hồi |
| 3 | [`decision-recommendation.schema.json`](./schemas/decision-recommendation.schema.json) | `DecisionRecommendation` | Cấu trúc gợi ý khung giờ, sân thay thế, hoặc điều chỉnh giá khi xung đột/chưa đủ cọc |
| 4 | [`decision-detail-response.schema.json`](./schemas/decision-detail-response.schema.json) | `DecisionDetailResponse` | Dữ liệu kiểm toán (Audit Trail) chi tiết của một quyết định đã lưu |
| 5 | [`pending-approvals-response.schema.json`](./schemas/pending-approvals-response.schema.json) | `PendingApprovalsResponse` | Danh sách phân trang các ca đặt sân đang chờ Quản lý phê duyệt |
| 6 | [`override-decision-request.schema.json`](./schemas/override-decision-request.schema.json) | `OverrideDecisionRequest` | Lệnh can thiệp ghi đè (Manual Override) từ Quản lý |
| 7 | [`override-decision-response.schema.json`](./schemas/override-decision-response.schema.json) | `OverrideDecisionResponse` | Phản hồi xác nhận can thiệp ghi đè thành công kèm vết kiểm toán |

---

### 3.2 Exported Public API Types (32 Types)

Danh mục 32 kiểu dữ liệu công khai được bảo toàn bởi `PublicApiSurfaceTests.cs` và lưu tại [`baseline_public_api.txt`](./baseline_public_api.txt):

#### A. Core Domain Models & Invariants (7 Types)
- `Shared.DecisionContractSDK.Domain.Reservation` *(Aggregate Root)*
- `Shared.DecisionContractSDK.Domain.CustomerInfo` *(Value Object Record)*
- `Shared.DecisionContractSDK.Domain.ResourceInfo` *(Value Object Record)*
- `Shared.DecisionContractSDK.Domain.ReservationSchedule` *(Value Object Record)*
- `Shared.DecisionContractSDK.Domain.PaymentPolicy` *(Value Object Record)*
- `Shared.DecisionContractSDK.Domain.ReservationMetadata` *(Value Object Record)*
- `Shared.DecisionContractSDK.Domain.DomainInvariantException` *(Domain Exception)*

#### B. API Contract DTOs & Mappings (13 Types)
- `Shared.DecisionContractSDK.DTOs.Decisions.DecisionRequestDto`
- `Shared.DecisionContractSDK.DTOs.Decisions.DecisionResultDto`
- `Shared.DecisionContractSDK.DTOs.Decisions.DecisionRecommendationDto`
- `Shared.DecisionContractSDK.DTOs.Decisions.DecisionDetailDto`
- `Shared.DecisionContractSDK.DTOs.Decisions.DecisionSummaryDto`
- `Shared.DecisionContractSDK.DTOs.Decisions.ExecutionSummaryDto`
- `Shared.DecisionContractSDK.DTOs.Decisions.GovernanceSummaryDto`
- `Shared.DecisionContractSDK.DTOs.Decisions.TimestampSummaryDto`
- `Shared.DecisionContractSDK.DTOs.Governance.OverrideDecisionRequestDto`
- `Shared.DecisionContractSDK.DTOs.Governance.OverrideTargetPayloadDto`
- `Shared.DecisionContractSDK.DTOs.Governance.OverrideDecisionResponseDto`
- `Shared.DecisionContractSDK.DTOs.Governance.PendingApprovalItemDto`
- `Shared.DecisionContractSDK.DTOs.Mappings.DecisionMappingExtensions` *(Strict Bidirectional Converters)*

#### C. Common Infrastructure & Protocols (5 Types)
- `Shared.DecisionContractSDK.Common.ApiResponse<T>` *(Generic Wire Envelope)*
- `Shared.DecisionContractSDK.Common.ApiResponse` *(Non-generic Envelope Helper)*
- `Shared.DecisionContractSDK.Common.PagedResult<T>` *(Pagination Envelope)*
- `Shared.DecisionContractSDK.Common.ContractValidationException` *(Wire Validation Exception)*
- `Shared.DecisionContractSDK.Common.ErrorCodes` *(Machine-readable Error Constants)*

#### D. Canonical Enums & Reason Codes (6 Types)
- `Shared.DecisionContractSDK.Enums.BookingPriority` (`NORMAL`, `HIGH`, `VIP`, `URGENT`)
- `Shared.DecisionContractSDK.Enums.DecisionStatus` (`APPROVED`, `REJECTED`, `NEED_APPROVAL`)
- `Shared.DecisionContractSDK.Enums.ExecutionType` (`AUTOMATED`, `FALLBACK`, `CACHE_HIT`)
- `Shared.DecisionContractSDK.Enums.PaymentStatus` (`UNPAID`, `PARTIALLY_PAID`, `PAID`, `REFUNDED`, `EXPIRED`)
- `Shared.DecisionContractSDK.Enums.RecommendationType` (`ALTERNATIVE_SLOT`, `ALTERNATIVE_COURT`, `PRICE_ADJUSTMENT`, `REQUIRE_DEPOSIT`, `REQUIRE_APPROVAL`, `PRIORITY_ESCALATION`)
- `Shared.DecisionContractSDK.Enums.ReasonCode` *(Canonical Strategy Reason Constants)*

#### E. Extension Strategy Interfaces (1 Type)
- `Shared.DecisionContractSDK.Interfaces.IReservationDecisionStrategy`

---

## 4. Canonical Wire Enum Specification (Option A - Strict)

Tất cả các trường Enum truyền tải trên wire HTTP JSON bắt buộc phải ở định dạng **`SCREAMING_SNAKE_CASE`**. Bộ parser `DecisionMappingExtensions` thực thi kiểm tra nghiêm ngặt:

```text
Wire JSON Payload ("PARTIALLY_PAID") 
       │
       ▼ (Strict String Match)
DecisionMappingExtensions.ParsePaymentStatus()
       │
       ├─► Match EXACT: PaymentStatus.PARTIALLY_PAID  ✅
       ├─► Case Mismatch ("PartiallyPaid" / "partially_paid"): Throws ContractValidationException ❌
       └─► Numeric String ("1"): Throws ContractValidationException ❌
```

| Enum Name | Canonical Wire Values (SCREAMING_SNAKE_CASE) | Default / Fallback | Mô Tả Ngữ Nghĩa Kỹ Thuật |
| :--- | :--- | :--- | :--- |
| **`PaymentStatus`** | `"UNPAID"`, `"PARTIALLY_PAID"`, `"PAID"`, `"REFUNDED"`, `"EXPIRED"` | `"UNPAID"` | Trạng thái thanh toán từ SmashCourt-BE |
| **`BookingPriority`**| `"NORMAL"`, `"HIGH"`, `"VIP"`, `"URGENT"` | `"NORMAL"` | Mức độ ưu tiên thẩm định đơn đặt sân |
| **`DecisionStatus`** | `"APPROVED"`, `"REJECTED"`, `"NEED_APPROVAL"` | `"NEED_APPROVAL"` | Kết quả đánh giá nghiệp vụ của Decision Engine |
| **`ExecutionType`**  | `"AUTOMATED"`, `"FALLBACK"`, `"CACHE_HIT"` | `"AUTOMATED"` | **Hình thức thực thi kỹ thuật của Engine (Telemetry)** |
| **`RecommendationType`** | `"ALTERNATIVE_SLOT"`, `"ALTERNATIVE_COURT"`, `"PRICE_ADJUSTMENT"`, `"REQUIRE_DEPOSIT"`, `"REQUIRE_APPROVAL"`, `"PRIORITY_ESCALATION"` | *None (Strict parse)* | Loại gợi ý điều chỉnh khi chưa đủ điều kiện duyệt |

### 4.1 Phân Định Rõ Ràng: Telemetry Thực Thi vs. Quản Trị Ghi Đè (Human Override)
> **ARCHITECTURAL CLARITY:**  
> - `ExecutionType` chỉ phản ánh **cơ chế thực thi kỹ thuật lúc sinh quyết định** (`AUTOMATED`: chạy qua chiến lược thuật toán; `FALLBACK`: chạy qua chiến lược cứu cánh an toàn khi timeout/lỗi; `CACHE_HIT`: lấy từ idempotency cache).  
> - Hành vi can thiệp của Quản lý sân (Human Override) được quản lý độc lập tại khối **`GovernanceSummaryDto`** (`is_overridden: true`, `overridden_by: "..."`, `override_reason: "..."`, `overridden_at: "..."`).  
> - Thiết kế này bảo toàn nguyên vẹn dữ liệu nghiên cứu (Research Telemetry), cho phép truy vấn chính xác: *"Trong số các quyết định sinh bởi AUTOMATED, có bao nhiêu % bị Quản lý ghi đè?"* (`WHERE execution_type = 'AUTOMATED' AND is_overridden = true`).

---

## 5. Automated Test Suite Evidence Summary

```text
================================================================================
TADSS TEST SUITE VERIFICATION REPORT — RELEASE CONFIGURATION
================================================================================
Test Projects Executed:
  1. TADSS.UnitTests.csproj        : 350 / 350 PASSED (100% GREEN, Duration ~1.0s)
  2. TADSS.IntegrationTests.csproj :   9 /   9 PASSED (100% GREEN, Duration ~7.0s)
--------------------------------------------------------------------------------
TOTAL SUITE PASSED: 359 / 359 AUTOMATED TESTS (0 FAILED, 0 SKIPPED)
--------------------------------------------------------------------------------
SDK Code Coverage Summary:
  - Shared.DecisionContractSDK Total Line Coverage   : 99.49% (393 / 395 lines)
  - Shared.DecisionContractSDK Total Branch Coverage : 97.32%
  - Domain Layer Line Coverage                       : 98.91% (181 / 183 lines)
  - DTOs & Mappings Layer Line Coverage              : 100.00% (166 / 166 lines)
  - Common & Enums Layer Line Coverage               : 100.00% (46 / 46 lines)
================================================================================
```

---

## 6. Breaking Change & Evolution Policy

Để đảm bảo tính độc lập và khả năng vận hành liên tục giữa `SmashCourt-BE` và `TADSS`, quy trình quản trị tiến hóa hợp đồng được quy định chặt chẽ theo nguyên tắc **Immutable Wire Schemas**:

### 6.1 Chính Sách Bất Biến của Frozen Wire Schemas (Option A — Immutable v1)
1. **7 JSON Schemas v1.0.0 đã đóng băng là bất biến:** Do toàn bộ schemas sử dụng `"additionalProperties": false`, bất kỳ việc bổ sung thêm trường dữ liệu nào trên wire format (kể cả trường tùy chọn `optional`) cũng sẽ bị client validate nghiêm ngặt từ chối.
2. **Quy định đối với phiên bản `1.x.y`:**
   - **Được phép:** Các cải tiến nội bộ trong SDK (tối ưu hóa hiệu năng, sửa lỗi logic parser, bổ sung phương thức extension tiện ích, bổ sung XML docstrings) **với điều kiện không làm thay đổi cấu trúc wire JSON**.
   - **Bổ sung `ReasonCode`:** `ReasonCode` đóng vai trò là từ vựng máy (machine-readable constants). SDK có thể đăng ký thêm hằng số lý do mới trong `1.x.y` phục vụ chiến lược nội bộ, nhưng các consumer được khuyến nghị xử lý giá trị string linh hoạt mà không bị crash nếu gặp mã mới.
3. **Quy định đối với phiên bản `v2.0.0` (Breaking / Structural Changes):**
   - Mọi thay đổi về cấu trúc wire JSON (thêm trường mới, xóa trường, đổi tên trường, đổi kiểu dữ liệu, thay đổi ranh giới invariant) **BẮT BUỘC** phải phát hành phiên bản `v2.0.0`.
   - **Quy trình phát hành v2:**
     - Tạo namespace mới `/api/v2/` trên API Gateway / Controller.
     - Tạo bộ schema mới trong thư mục `docs/contracts/schemas/v2/`.
     - Duy trì `/api/v1/` hoạt động song song tối thiểu 6 tháng (Deprecation Grace Period).

---

## 7. Sign-off & Handoff to Phase 3

Hội đồng kỹ thuật và nhóm phát triển xác nhận toàn bộ 9 Quality Gates của Milestone 2 đã hoàn thành đầy đủ theo đúng chuẩn mực kỹ thuật và tiêu chí đề án. Hợp đồng v1.0.0 chính thức **ĐÓNG BĂNG**.

| Vai trò | Đại diện | Quyết định | Chữ ký điện tử |
| :--- | :--- | :---: | :--- |
| **TADSS Core Lead** | Sinh viên thực hiện 1 | ✅ **APPROVED** | `SHA: 4b8b1124...` |
| **SmashCourt Integration Lead** | Sinh viên thực hiện 2 | ✅ **APPROVED** | `SHA: 4b8b1124...` |
| **System Architecture** | TADSS Engine Board | ✅ **FROZEN** | `v1.0.0-RELEASE` |

> **Chuyển giao (Handoff):** Toàn bộ nền tảng Contract SDK v1.0.0 đã sẵn sàng làm nền móng vững chắc để bước sang **Phase 3: Core Decision Strategies & Rule Engine Implementation** (bắt đầu từ Deterministic Baseline Rule Strategy trước khi mở rộng sang Adaptive Trust).
