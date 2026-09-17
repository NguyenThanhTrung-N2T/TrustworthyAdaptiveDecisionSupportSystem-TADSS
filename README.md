# TADSS Runtime

**Trustworthy-Adaptive-Decision-Support-System (TADSS)**

TADSS Runtime is a decision-support runtime designed to provide **adaptive, explainable, resilient, and trustworthy decision-making** for service reservation platforms.

The project is developed as the technical foundation for **Đồ án 2 / Khóa luận**, with the goal of evolving into a reusable decision-support architecture that can be integrated with different reservation domains.

---

## 🎯 Objectives

TADSS focuses on four main capabilities:

* **Adaptive Decision** — make decisions based on contextual and behavioral data.
* **Trust & Fairness** — incorporate trust scoring while considering fairness constraints.
* **Explainability** — record and expose decision traces for transparency.
* **Resilience** — maintain service availability when AI/decision components fail.

The architecture is designed to separate the **decision strategy** from the **runtime**, allowing different decision-making algorithms or policies to be plugged in without changing the core runtime.

---

## 🏗️ Architecture

```text
TADSS Runtime
│
├── Shared.DecisionContractSDK
│   ├── Domain
│   ├── Contracts
│   ├── Enums
│   └── Interfaces
│
├── TADSS.Runtime.Api
│   ├── Controllers
│   ├── Services
│   ├── Middlewares
│   ├── Configuration
│   └── Infrastructure
│       ├── Data
│       ├── Resilience
│       ├── Caching
│       └── Events
│
├── TADSS.Strategies.Default
│
├── TADSS.Strategies.MockAlternative
│
└── Tests
    ├── TADSS.UnitTests
    │   ├── Contracts
    │   ├── Domain
    │   ├── Services
    │   ├── Infrastructure
    │   └── TestData
    └── TADSS.IntegrationTests
```

### Core principle

```text
Client
   │
   ▼
Runtime API
   │
   ▼
Decision Service
   │
   ▼
Strategy
   │
   ├── Default Strategy
   ├── Alternative Strategy
   └── Future AI/ML Strategy
   │
   ▼
Decision Result
   │
   ├── Decision Trace
   ├── Audit Log
   └── Resilience / Fallback
```

The runtime is responsible for **orchestration, resilience, persistence, and observability**.

The strategy is responsible for **decision-making logic**.

---

## 🛠️ Technology Stack

### Backend

* **.NET 8**
* ASP.NET Core Web API
* C#
* Entity Framework Core
* PostgreSQL / Npgsql

### Decision & Runtime

* MediatR
* Polly
* Redis / StackExchange.Redis
* Serilog

### API

* REST API
* Swagger / OpenAPI

### Testing

* xUnit
* FluentAssertions
* Moq
* ASP.NET Core Integration Testing

### Architecture

* Modular architecture
* Strategy Pattern
* Dependency Injection
* Domain-Driven Design principles
* Resilience patterns
* Event-driven components where required

---

## 📦 Project Structure

| Project                            | Responsibility                                            |
| ---------------------------------- | --------------------------------------------------------- |
| `Shared.DecisionContractSDK`       | Shared domain primitives, contracts, enums and interfaces |
| `TADSS.Runtime.Api`                | Main runtime and Web API                                  |
| `TADSS.Strategies.Default`         | Default decision strategy                                 |
| `TADSS.Strategies.MockAlternative` | Alternative/mock strategy for testing                     |
| `TADSS.UnitTests`                  | Unit tests                                                |
| `TADSS.IntegrationTests`           | API and runtime integration tests                         |

---

## 🚀 Getting Started

### Requirements

* .NET 8 SDK
* PostgreSQL
* Redis *(required when caching functionality is enabled)*

### Clone

```powershell
git clone <repository-url>
cd TADSS-Runtime
```

### Restore

```powershell
dotnet restore
```

### Build

```powershell
dotnet build
```

### Test

```powershell
dotnet test
```

### Run API

```powershell
dotnet run --project src/TADSS.Runtime.Api
```

Swagger will be available when the API is running.

---

## 🧪 Testing

The solution contains two test projects:

```text
TADSS.UnitTests
TADSS.IntegrationTests
```

Run all tests:

```powershell
dotnet test
```

Unit tests focus on isolated decision logic and services.

Integration tests verify the interaction between the API, runtime components, and infrastructure.

---

## 🔐 Design Principles

TADSS follows several architectural principles:

### 1. Strategy decides, Runtime orchestrates

Decision policies must remain inside strategy implementations.

The runtime should not contain domain-specific policy logic.

### 2. Technology must solve a problem

Infrastructure is introduced only when it provides a concrete architectural or operational benefit.

Examples:

* Redis → reduce latency for suitable stable data.
* Polly → handle transient failures and service degradation.
* MediatR → in-process notifications and decoupling.
* RabbitMQ → introduced only when asynchronous cross-service communication is actually required.

### 3. Resilience by design

AI or external decision components should not become a single point of failure.

The runtime should support:

```text
Primary Decision
      │
      ▼
   Failure?
   /      \
 No        Yes
 │          │
 ▼          ▼
Result    Fallback
```

### 4. Explainable decisions

Important decisions should produce a trace containing enough information to understand:

* What decision was made
* Which strategy produced it
* Relevant decision factors
* Confidence / scores where applicable
* Fallback status
* Execution metadata

---

## 🔬 Research Direction

The runtime will be evaluated as part of the thesis research on an:

> **Adaptive Trust Decision Architecture for Service Reservation Platforms**

The research focuses on:

* Adaptive Trust Score
* Time-decay behavioral modeling
* Fairness constraints
* Expected Business Utility
* Explainable decision-making
* AI failure resilience
* Human-in-the-loop feedback
* Concept/data drift
* Continuous model improvement

---

## 📈 Planned Evaluation

The project will use both **algorithm-level** and **system-level** evaluation.

### Algorithm Ablation

Potential comparison:

```text
Random Forest
XGBoost
LightGBM
CatBoost
```

Metrics may include:

* Accuracy
* Precision
* Recall
* F1-score
* ROC-AUC
* Fairness metrics

### System Ablation

Configurations will be compared from a simple baseline to the complete TADSS architecture.

```text
Baseline
   ↓
+ Adaptive Trust
   ↓
+ Explainability
   ↓
+ Resilience
   ↓
+ Human-in-the-loop
   ↓
Full TADSS
```

Evaluation will focus on:

* Decision quality
* Business utility
* Fairness
* Latency
* Availability
* Failure recovery
* Operational overhead

---

## 🗺️ Development Roadmap

### Phase 1 — Runtime Foundation

* [x] Solution structure
* [x] Project references
* [x] Dependency setup
* [x] Build pipeline
* [ ] Decision contracts
* [ ] Strategy interface
* [ ] Default strategy
* [ ] Decision API
* [ ] Initial tests

### Phase 2 — Decision Runtime

* [ ] Decision execution logging
* [ ] Decision Trace
* [ ] Persistence
* [ ] Strategy registry
* [ ] Resilience policies
* [ ] Fallback mechanism
* [ ] Redis caching

### Phase 3 — Adaptive Trust

* [ ] Trust model
* [ ] Time decay
* [ ] Trust score calculation
* [ ] Decision context
* [ ] Fairness constraints

### Phase 4 — AI/ML

* [ ] No-show prediction
* [ ] Model serving
* [ ] Model evaluation
* [ ] Data drift detection
* [ ] Human feedback loop

### Phase 5 — Production & Research

* [ ] Docker deployment
* [ ] Monitoring
* [ ] Observability
* [ ] Performance testing
* [ ] Failure testing
* [ ] Ablation experiments
* [ ] Thesis evaluation

---

## 📄 License

This project is developed for academic research and educational purposes.
