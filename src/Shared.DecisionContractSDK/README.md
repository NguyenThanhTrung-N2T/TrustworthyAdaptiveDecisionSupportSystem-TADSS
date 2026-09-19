# TADSS Shared Decision Contract SDK

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://opensource.org/licenses/MIT)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

`TADSS.Shared.DecisionContractSDK` is a standalone, pure .NET 8 BCL (0 external NuGet dependencies) library providing canonical contracts, rich domain models, and bi-directional mappings for the **TADSS (Trustworthy Adaptive Decision Support System)** and its consumers (such as `SmashCourt-BE`).

---

## 🏛️ Architecture & Principles

- **Zero External Dependencies:** Built strictly on the .NET 8 Base Class Library (BCL).
- **Strict Directed Acyclic Graph (DAG):**
  - `Common & Enums` $\rightarrow$ `Domain` / `DTOs` $\rightarrow$ `Mappings`
  - `Mappings` depends on both `Domain` and `DTOs`.
  - The `Domain` layer strictly contains 0 references to `DTOs` or `Mappings`.
- **Strict Canonical Wire Format (Option A):** All wire enum strings are strictly validated against `SCREAMING_SNAKE_CASE` (rejecting non-canonical casing and numeric strings).
- **Rich Domain Invariants:** Value Objects and Aggregate Roots (`Reservation`) fail-fast upon instantiation to guarantee business invariants.

---

## 📦 Installation & Usage

### 1. Direct Reference / NuGet Package
```bash
dotnet add package TADSS.Shared.DecisionContractSDK --version 1.0.0
```

### 2. Creating Domain Entities
```csharp
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.Enums;

var reservation = Reservation.Create(
    reservationId: Guid.NewGuid(),
    customerId: Guid.NewGuid(),
    customerTier: "GOLD",
    courtId: Guid.NewGuid(),
    branchId: Guid.NewGuid(),
    courtTypeId: null,
    startTime: DateTime.UtcNow.AddDays(1),
    endTime: DateTime.UtcNow.AddDays(1).AddHours(2),
    totalPrice: 200000m,
    depositAmount: 100000m,
    paymentStatus: PaymentStatus.PARTIALLY_PAID,
    bookingType: "SINGLE",
    priority: BookingPriority.NORMAL,
    requestedAt: DateTime.UtcNow);

// Business Invariant Evaluation
decimal ratio = reservation.CalculateDepositRatio(); // 0.50
bool canCancel = reservation.CanBeCancelledWithoutPenalty(DateTime.UtcNow);
```

### 3. Bi-directional DTO Mapping
```csharp
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.DTOs.Mappings;

// Wire DTO -> Rich Domain Entity (Validates Invariants)
DecisionRequestDto dto = GetWirePayload();
Reservation domain = dto.ToDomain();

// Domain Entity -> Wire DTO (Lossless Serialization)
DecisionRequestDto roundtripDto = domain.ToDto();
```

---

## 📄 License
MIT License. Copyright (c) 2026 SmashCourt - TADSS Research.
