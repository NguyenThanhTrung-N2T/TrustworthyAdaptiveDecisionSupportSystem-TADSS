# TADSS Shared Decision Contract SDK

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://opensource.org/licenses/MIT)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

`TADSS.Shared.DecisionContractSDK` is a standalone, pure .NET 8 BCL (0 external NuGet dependencies) library providing the **contract v2** of the **TADSS (Trustworthy Adaptive Decision Support System)**: domain model, strategy contract, wire DTOs and the DTO to domain mapping. Version `2.0.0-draft` (baseline pending freeze at milestone M5).

---

## Architecture & Principles

- **Zero External Dependencies:** Built strictly on the .NET 8 Base Class Library (BCL).
- **Layout (root namespace `Shared.DecisionContractSDK`):**
  - `Common` - error and reason codes, `PagedResult`, `TadssJson` (shared `JsonSerializerOptions`)
  - `Enums` - wire enums (strict names only: case-sensitive, no numeric strings)
  - `Domain` - `Reservation` aggregate, `ReservationItem`, `CustomerProfile`, `CustomerHistory`, `DemandSnapshot`, `BranchCalendar`, `PeakWindow`
  - `Decisions` - `StrategyDecision`, `StrategyCachePolicy`, `DecisionContext`, `DecisionRecommendation`
  - `Interfaces` - `IReservationDecisionStrategy`
  - `DTOs` - `Decisions`, `Governance`, `Outcomes` wire DTOs and `Mappings` (`DecisionRequestMapping`: DTO to domain)
- **Dependency direction:** `Common` and `Enums` -> `Domain` / `Decisions` / `DTOs` -> `Mappings`. `Domain` has no reference to `DTOs`.
- **Strict wire format:** unknown fields, unknown enum values and date-times without an explicit offset are rejected.
- **Rich domain invariants:** domain types fail fast on construction (`DomainInvariantException`).

The wire contract is specified in `docs/contracts/INTEGRATION_SPEC.md`.

---

## Usage

Reference the project (or the `TADSS.Shared.DecisionContractSDK` package) and map a request DTO to the domain:

```csharp
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.DTOs.Mappings;

var request = JsonSerializer.Deserialize<DecisionRequestDto>(json, TadssJson.Options)
    ?? throw new InvalidOperationException("Empty request body.");
Reservation reservation = request.ToDomain(); // throws ContractValidationException / DomainInvariantException on invalid input
```

---

## License
MIT License. Copyright (c) 2026 SmashCourt - TADSS Research.
