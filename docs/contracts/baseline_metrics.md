# 📊 Milestone 2 — Baseline Metrics & Pre-flight Audit Report

> **Thời điểm thực hiện (Timestamp):** 2026-09-18 03:16:00 UTC (10:16:00 GMT+7)  
> **Git Commit SHA:** `c490171915eada475f5f8716d0bef7f8dd3a422d`  
> **Git Branch:** `feat/phase-2-contract-sdk`  
> **.NET SDK Version:** `10.0.401` (Targeting `net8.0` Runtime)  
> **Operating System:** Microsoft Windows 11 Home Single Language 64-bit  
> **Test Frameworks & Tooling:** xUnit 2.9.2, FluentAssertions 7.0.0, coverlet.collector 6.0.0, Microsoft.NET.Test.Sdk 17.11.1  
> **Phạm vi kiểm định:** Thư viện dùng chung `Shared.DecisionContractSDK` (v1.0.0)  
> **Người thực hiện:** TADSS Core Engineering Team (Sinh viên 1 & Sinh viên 2)  
> **Trạng thái:** ✅ **ALL PRE-FLIGHT CHECKS, QUALITY GATES & REPRODUCIBILITY AUDITS PASSED**

---

## 1. Package Purity & Dependency Audit

- **Lệnh thực thi:** `dotnet list src/Shared.DecisionContractSDK/Shared.DecisionContractSDK.csproj package --include-transitive`
- **Kết quả kiểm tra:**
  ```text
  Project 'Shared.DecisionContractSDK' has the following package references
     [net8.0]: No packages were found for this framework.
  ```
- **Kết luận:** Thư viện `Shared.DecisionContractSDK` hoàn toàn thuần BCL (.NET 8.0), **0 external NuGet dependencies**, **0 circular dependencies**. Đạt chuẩn độc lập biên giới hệ thống.
- **Kiểm thử tự động thực thi:** `SdkDependencyPurityTests.cs` (PASS).

---

## 2. Public API Surface Verification Gate

- **Artifact:** [`baseline_public_api.txt`](./baseline_public_api.txt) (Frozen v1.0.0 Baseline)
- **Cơ chế kiểm định:** `PublicApiSurfaceTests.cs`
  - **Quy tắc Gate:** Test chạy ở chế độ **Strict Verification**, đọc trực tiếp từ `baseline_public_api.txt` và so sánh với reflection metadata sinh từ assembly `Shared.DecisionContractSDK.dll`. Test **KHÔNG** tự động ghi đè file baseline trong quá trình CI/CD.
  - **Mô phỏng Breaking Change:** Đã tích hợp `SharedDecisionContractSdk_BreakingChangeSimulation_DetectsMismatch()` chứng minh bất kỳ thay đổi nào ngoài ý muốn trên Public API sẽ lập tức làm FAIL test suite.
- **Tổng số Public Types được bảo toàn:** 32 types (24 primary architectural types + 8 auxiliary types: `DecisionSummaryDto`, `ExecutionSummaryDto`, `GovernanceSummaryDto`, `TimestampSummaryDto`, `OverrideTargetPayloadDto`, `DecisionMappingExtensions`, `ApiResponse` (non-generic), `ReasonCode` (static class)).

---

## 3. Tiered Code Coverage Report (Release Build)

| Phân tầng SDK | Covered / Total Lines | Tỷ lệ Coverage thực tế | Ngưỡng cam kết (Target) | Kết quả nghiệm thu |
| :--- | :---: | :---: | :---: | :---: |
| **Domain Layer** (`Reservation`, `ValueObjects`) | 181 / 183 | **98.91%** | $\ge 95\%$ | 🟢 **PASS** |
| **DTOs & Mappings** (`DecisionMappingExtensions`) | 166 / 166 | **100.00%** | $\ge 85\%$ | 🟢 **PASS** |
| **Common & Enums** (`ApiResponse`, `PagedResult`) | 46 / 46 | **100.00%** | $\ge 60\%$ | 🟢 **PASS** |
| **TOÀN BỘ SDK (`Shared.DecisionContractSDK`)** | 393 / 395 | **99.49%** | $\ge 80\%$ | 🟢 **PASS** |

---

## 4. Mechanical Compilation Gate

- **Lệnh thực thi:** `dotnet build -c Release --warnaserror`
- **Cấu hình Compiler:**
  - `<GenerateDocumentationFile>true</GenerateDocumentationFile>`
  - `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
- **Kết quả:** **0 Warning(s), 0 Error(s)** (100% Public Members có XML Documentation comment đầy đủ, 0 CS1591 warning).

---

## 5. Test Count Evolution & Suite Summary

### 📈 Quá trình tiến hóa Test Suite (Day 0 $\rightarrow$ Day 6 Freeze):
- **Day 0 Baseline:** **331 tests** (322 Unit + 9 Integration)
- **Day 1 – 2 Bổ sung:** **+17 tests** (`PublicApiSurfaceTests`, `DtoValidationContractTests`, `ReservationContractBoundaryTests`, `CrossCultureInvariantTests`, `ConcurrentReadSafetyTests`)
- **Day 3 – 4 Bổ sung:** **+7 tests** (`SdkDependencyPurityTests`, `ContractSchemaCompatibilityTests`, `AdversarialContractMappingTests`, `DomainInvariantsTests` boundary precision)
- **Day 5 – 6 Bổ sung:** **+4 tests**
  - `SmashCourtConsumerReferenceTests.cs` (+2 tests): Kiểm chứng tính tương thích khi Consumer thực tế (`SmashCourt-BE`) sử dụng SDK DTOs, Rich Domain Model, Enums và bọc `ApiResponse<T>`, `PagedResult<T>`.
  - `AdversarialContractMappingTests.cs` (+2 tests): Kiểm chứng tính mở rộng của `ReasonCode` (Consumer deserialize an toàn chuỗi mã lý do tương lai/custom mà không bị crash) và đối soát schema type mở `primaryReason`.
- **Tổng cộng nghiệm thu đóng băng:** **359 tests GREEN (100% PASS)** (350 Unit Tests + 9 Integration Tests).

---

## 6. Coverage Gap Analysis (Domain Layer)

### Chi tiết các dòng chưa thực thi trong Domain Layer (2 / 183 lines = 1.09%):

1. **`Reservation.cs: Line 170`**
   - **Đoạn mã:** `throw new DomainInvariantException("INVALID_SCHEDULE", "Schedule must not be null.", nameof(Schedule));`
   - **Vị trí:** Phương thức `ValidateSchedule()` bên trong `EnsureValid()`.
   - **Nguyên nhân kỹ thuật:** Constructor của `Reservation` đã bảo vệ `Schedule = schedule ?? throw new ArgumentNullException(nameof(schedule))` ở dòng 83. Vì `Schedule` là thuộc tính read-only (`get;`), một instance `Reservation` được khởi tạo hợp lệ không thể có `Schedule == null`.
   - **Quyết định thẩm định:** **Chấp nhận là mã phòng vệ (Defensive Programming / Invariant Guard)** cho các kịch bản gọi qua reflection hoặc uninitialized state. Không loại bỏ vì tính toàn vẹn của Aggregate Root.

2. **`Reservation.cs: Line 197`**
   - **Đoạn mã:** `throw new DomainInvariantException("INVALID_PAYMENT", "Payment policy must not be null.", nameof(Payment));`
   - **Vị trí:** Phương thức `ValidatePayment()` bên trong `EnsureValid()`.
   - **Nguyên nhân kỹ thuật:** Tương tự, constructor đã guard `Payment = payment ?? throw new ArgumentNullException(nameof(payment))` ở dòng 84.
   - **Quyết định thẩm định:** **Chấp nhận là mã phòng vệ (Defensive Programming / Invariant Guard)**.
