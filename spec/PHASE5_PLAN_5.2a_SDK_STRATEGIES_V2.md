# Gói 5.2a — SDK v2 + Strategies v2: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Thêm contract SDK v2 (kiểu domain, quyết định, DTO wire, mapping, JSON schema) và hai strategy v2 song song với v1, mỗi task build `--warnaserror` và test xanh, kết thúc bằng điểm dừng để người dùng review bề mặt SDK v2 (D-06).

**Architecture:** Kiểu v2 nằm ở namespace tạm `Shared.DecisionContractSDK.V2.*` (thư mục `src/Shared.DecisionContractSDK/V2/`), strategy v2 ở `TADSS.Strategies.{Name}.V2`; v1 giữ nguyên và runtime vẫn chạy v1. Enum và thời gian trên wire được kiểm tra chặt bằng converter gắn trên kiểu/thuộc tính. Mỗi task chủ ý thêm kiểu vào SDK cập nhật `baseline_public_api.txt` trong chính task đó. Runtime chưa dùng gì của v2 (gói 5.2b).

**Tech Stack:** .NET 8, System.Text.Json (BCL), xUnit 2.5.3, FluentAssertions 6.12.1, JsonSchema.Net 9.4.0 (chỉ project test).

**Spec:** [`PHASE5_DESIGN_SPEC.md`](PHASE5_DESIGN_SPEC.md) — Mục 4.2.0 (chia gói, cùng tồn tại v1/v2, điểm dừng), 4.2.1 (SDK), 4.2.2 (strategies), 6, 6.1. Contract: [`../docs/contracts/INTEGRATION_SPEC.md`](../docs/contracts/INTEGRATION_SPEC.md) §3.1–3.7, §4.2. Bảng quyết định strategy: [`../docs/architecture/SOFTWARE_ARCHITECTURE_SPECIFICATION.md`](../docs/architecture/SOFTWARE_ARCHITECTURE_SPECIFICATION.md) Mục 4.3.

## Global Constraints

- Build chuẩn: `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror` → **0 warning, 0 error** sau mỗi task.
- `Shared.DecisionContractSDK` chỉ phụ thuộc BCL (System.Text.Json là BCL); `TADSS.Strategies.*` chỉ phụ thuộc SDK. Không thêm NuGet vào hai loại project này.
- XML doc `///` tiếng Việt cho mọi public member ở SDK và Strategies (`TreatWarningsAsErrors` → thiếu doc = build lỗi).
- Kiểu v2 ở `Shared.DecisionContractSDK.V2.{Common,Enums,Domain,Decisions,Interfaces,DTOs.Decisions,DTOs.Governance,DTOs.Outcomes,DTOs.Mappings}`; strategy v2 ở `TADSS.Strategies.Default.V2`, `TADSS.Strategies.MockAlternative.V2`; test v2 ở `tests/TADSS.UnitTests/V2/` namespace `TADSS.UnitTests.V2.*`. **Không sửa, không xóa kiểu v1.** Dùng lại `Shared.DecisionContractSDK.Domain.DomainInvariantException` và `Shared.DecisionContractSDK.Common.ContractValidationException` của v1, không nhân bản.
- Converter enum chặt và converter thời gian là `internal`. `TadssJson` **không** thêm `JsonStringEnumConverter` (converter toàn cục ghi đè converter gắn trên kiểu — đã kiểm chứng).
- Baseline public API: task nào chủ ý thêm kiểu vào SDK thì cập nhật `docs/contracts/baseline_public_api.txt` trong chính task đó theo đúng quy trình "Cập nhật baseline" trong task; diff baseline chỉ được **thêm** dòng thuộc `Shared.DecisionContractSDK.V2.`; dán diff vào báo cáo task. Không bao giờ sửa baseline để che một thay đổi ngoài ý muốn.
- Version package chỉ khai báo trong `Directory.Packages.props`; csproj chỉ `<PackageReference Include="X" />`.
- Test: tên `{Method}_{Scenario}_{Expected}`; mỗi test có `[Trait("CatalogId", "<mã>")]`; XML summary tiếng Việt trên class test; không `Skip`, không nới assertion, không `#pragma warning disable`, không `!` null-forgiving. Dùng mẫu `?? throw new InvalidOperationException(...)` khi cần bỏ null.
- Không đọc/in/sửa `.env`, `appsettings.Development.json`, User Secrets. Không chạy migration. Gói này không đụng `src/TADSS.Runtime.Api`.
- **Commit chỉ khi người dùng cho phép** (rule `git-safety`): bước "Commit" = đề xuất message và hỏi người dùng.
- Nếu một hành vi .NET hoặc API thư viện khác với code trong plan: sửa tối thiểu để giữ đúng **ý định** của bước, ghi lại trong báo cáo task là "sai lệch"; không đổi assertion để test xanh.

## Chuẩn bị (người dùng thực hiện, trước Task 1)

1. Commit hai file spec đang staged nhưng chưa vào commit `19b33a5` cùng các tài liệu đã sửa trong phiên thiết kế 5.2: `spec/PHASE5_DESIGN_SPEC.md`, `spec/PHASE5_PLAN_5.1_TEST_FOUNDATION.md`, `spec/PHASE5_PLAN_5.2a_SDK_STRATEGIES_V2.md`, `docs/contracts/INTEGRATION_SPEC.md`, `docs/architecture/DECISION_PERSISTENCE_INVARIANTS.md`, `docs/research/DATASET_SCHEMA_SPECIFICATION_V1.md`. Gợi ý: `docs(spec): thiết kế gói 5.2 (chia 5.2a–c, tín hiệu SmashCourt, wire v2) và plan 5.2a`.
2. Cập nhật `develop` cục bộ theo remote (5.1 đã merge) và tạo nhánh `feat/phase-5.2a-sdk-strategies-v2` từ `develop` (D-08). Agent chỉ chạy các lệnh git này khi người dùng cho phép.

## Cấu trúc file

| File | Hành động | Task |
| :--- | :--- | :--- |
| `tests/TADSS.UnitTests/Architecture/PublicApiSurfaceTests.cs` | Sửa: lệch thì ghi `TestResults/baseline_public_api.actual.txt`; thêm `CatalogId` CT-02 | 1 |
| `src/Shared.DecisionContractSDK/V2/Common/{StrictEnumConverterFactory,TadssJson,ErrorCodes,ReasonCodes,PagedResult}.cs` | Tạo | 2 |
| `src/Shared.DecisionContractSDK/V2/Enums/*.cs` (7 enum) | Tạo | 2 |
| `tests/TADSS.UnitTests/V2/Common/{WireEnumTests,PagedResultTests}.cs` | Tạo | 2 |
| `src/Shared.DecisionContractSDK/V2/Domain/{ReservationItem,CustomerHistory,CustomerProfile,DemandSnapshot,Reservation,PeakWindow,BranchCalendar}.cs` | Tạo | 3 |
| `tests/TADSS.UnitTests/V2/TestData/V2TestData.cs` | Tạo (Task 3), bổ sung (Task 4) | 3, 4 |
| `tests/TADSS.UnitTests/V2/Domain/{ReservationItemTests,CustomerTests,ReservationTests,BranchCalendarTests}.cs`, `tests/TADSS.UnitTests/V2/Architecture/SdkV2LayeringTests.cs` | Tạo | 3 |
| `src/Shared.DecisionContractSDK/V2/Decisions/{DecisionRecommendation,StrategyDecision,DecisionContext,StrategyCachePolicy}.cs`, `V2/Interfaces/IReservationDecisionStrategy.cs` | Tạo | 4 |
| `tests/TADSS.UnitTests/V2/Decisions/DecisionTypesTests.cs` | Tạo | 4 |
| `src/Shared.DecisionContractSDK/V2/Common/OffsetDateTimeConverter.cs`, `V2/DTOs/**`, `V2/DTOs/Mappings/DecisionRequestMapping.cs` | Tạo | 5 |
| `docs/contracts/schemas/v2/{decision-request,decision-result}.schema.json` | Tạo | 5 |
| `Directory.Packages.props`, `tests/TADSS.UnitTests/TADSS.UnitTests.csproj` | Sửa: thêm `JsonSchema.Net` 9.4.0 | 5 |
| `tests/TADSS.UnitTests/V2/TestData/{V2Samples,RepositoryPaths,ContractSchemas}.cs`, `tests/TADSS.UnitTests/V2/Contracts/{DecisionWireContractTests,DecisionRequestMappingTests,DecisionSchemaTests}.cs` | Tạo | 5 |
| `src/TADSS.Strategies.Default/V2/{DefaultStrategyOptions,DefaultDecisionStrategy}.cs`, `tests/TADSS.UnitTests/V2/Strategies/DefaultDecisionStrategyTests.cs` | Tạo | 6 |
| `src/TADSS.Strategies.MockAlternative/V2/{MockAlternativeStrategyOptions,MockAlternativeDecisionStrategy}.cs`, `tests/TADSS.UnitTests/V2/Strategies/MockAlternativeDecisionStrategyTests.cs` | Tạo | 7 |
| `docs/contracts/baseline_public_api.txt` | Cập nhật có chủ đích | 2, 3, 4, 5 |
| `docs/planning/BACKLOG_SPRINTS.md`, `../.claude/skills/tadss-architecture/SKILL.md` | Sửa | 8 |

Số test kỳ vọng (unit / integration) sau từng task: T1 475/32 · T2 494/32 · T3 556/32 · T4 575/32 · T5 619/32 · T6 642/32 · T7 658/32 · T8 658/32.

### Quy trình "Cập nhật baseline" (dùng ở Task 2–5)

1. Build, rồi chạy: `dotnet test tests/TADSS.UnitTests/TADSS.UnitTests.csproj -c Release --no-build -nologo --filter "FullyQualifiedName~PublicApiSurfaceTests"` → kỳ vọng FAIL 1 test và có file `TestResults/baseline_public_api.actual.txt`.
2. `cp TestResults/baseline_public_api.actual.txt docs/contracts/baseline_public_api.txt`
3. Kiểm tra diff chỉ **thêm** kiểu V2:
   - `git diff -U0 docs/contracts/baseline_public_api.txt | grep -E '^-[^-]' || echo "không có dòng bị xóa"` → phải in `không có dòng bị xóa`.
   - `git diff -U0 docs/contracts/baseline_public_api.txt | grep -E '^\+\[' | grep -v 'Shared.DecisionContractSDK.V2\.' || echo "chỉ có kiểu V2"` → phải in `chỉ có kiểu V2`.
   - Nếu một trong hai lệnh in dòng khác → **DỪNG**, không ghi đè; báo cáo controller.
4. Chạy lại bước 1 → PASS. Dán `git diff --stat docs/contracts/baseline_public_api.txt` và danh sách dòng `+[...]` vào báo cáo task.

---

### Task 1: `PublicApiSurfaceTests` ghi bề mặt thực tế khi lệch baseline

**Files:**

- Modify: `tests/TADSS.UnitTests/Architecture/PublicApiSurfaceTests.cs`

**Interfaces:**

- Produces: khi baseline lệch, file `TestResults/baseline_public_api.actual.txt` ở thư mục gốc solution (đã gitignore) chứa bề mặt thực tế — Task 2–5 dùng trong quy trình "Cập nhật baseline".

- [ ] **Step 1: Sửa test**

Trong `SharedDecisionContractSdk_PublicApiSurface_StrictlyMatchesFrozenBaseline`:

- Thêm `[Trait("CatalogId", "CT-02")]` dưới `[Fact]`.
- Thay khối bước 4 (assertion cuối) bằng:

```csharp
        // 4. Strict assertion against frozen baseline (VERIFICATION ONLY - NEVER OVERWRITES IN TEST)
        //    Khi lệch: ghi bề mặt thực tế ra TestResults/ để người phát triển so sánh và cập nhật baseline có chủ đích.
        var testResultsDirectory = Path.GetFullPath(Path.Combine(baselinePath, "..", "..", "..", "TestResults"));
        var actualSnapshotPath = Path.Combine(testResultsDirectory, "baseline_public_api.actual.txt");
        if (normalizedActual != normalizedExpected)
        {
            Directory.CreateDirectory(testResultsDirectory);
            File.WriteAllText(actualSnapshotPath, actualSurface, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        normalizedActual.Should().Be(
            normalizedExpected,
            "the public API surface of Shared.DecisionContractSDK must strictly match the frozen baseline v1.0.0. " +
            "Any change indicates a breaking change or unauthorized contract modification. " +
            $"Actual surface written to '{actualSnapshotPath}' for comparison.");
```

(`baselinePath` = `<gốc>/docs/contracts/baseline_public_api.txt`, nên `../../../TestResults` là `<gốc>/TestResults`; không dùng lại `dir` để tránh cảnh báo nullable và không thêm `!` mới.)

- Thêm `[Trait("CatalogId", "CT-02")]` cho `SharedDecisionContractSdk_BreakingChangeSimulation_DetectsMismatch`.

- [ ] **Step 2: Build và chạy**

Run: `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror && dotnet test tests/TADSS.UnitTests/TADSS.UnitTests.csproj -c Release --no-build -nologo`
Expected: `Passed: 475`, `Failed: 0`. File `TestResults/baseline_public_api.actual.txt` **không** được tạo (baseline đang khớp): `ls TestResults/baseline_public_api.actual.txt` báo không tồn tại.

- [ ] **Step 3: Commit (hỏi người dùng trước)**

```bash
git add tests/TADSS.UnitTests/Architecture/PublicApiSurfaceTests.cs
git commit -m "test(architecture): PublicApiSurfaceTests ghi bề mặt thực tế khi lệch baseline"
```

---

### Task 2: V2 Common + Enums (JSON chặt)

**Files:**

- Create: `src/Shared.DecisionContractSDK/V2/Common/StrictEnumConverterFactory.cs`, `TadssJson.cs`, `ErrorCodes.cs`, `ReasonCodes.cs`, `PagedResult.cs`
- Create: `src/Shared.DecisionContractSDK/V2/Enums/{DecisionStatus,ExecutionType,CustomerTier,Channel,OverrideAction,DecisionOutcome,RecommendationType}.cs`
- Create: `tests/TADSS.UnitTests/V2/Common/WireEnumTests.cs`, `tests/TADSS.UnitTests/V2/Common/PagedResultTests.cs`
- Modify: `docs/contracts/baseline_public_api.txt` (quy trình "Cập nhật baseline")

**Interfaces:**

- Produces: `TadssJson.Options` (`JsonSerializerOptions`, chỉ đọc), `TadssJson.Apply(JsonSerializerOptions)`; `ErrorCodes.*`, `ReasonCodes.*` (hằng `string`); `PagedResult<T>.Create(IReadOnlyList<T> items, int page, int pageSize, int totalItems)`; enum `DecisionStatus {APPROVED, NEED_APPROVAL, REJECTED}`, `ExecutionType {AUTOMATED, FALLBACK, CACHE_HIT}`, `CustomerTier {STANDARD, SILVER, GOLD, PLATINUM, DIAMOND}`, `Channel {ONLINE, WALK_IN}`, `OverrideAction {APPROVE, REJECT}`, `DecisionOutcome {COMPLETED, NO_SHOW, CANCELLED_BY_CUSTOMER, CANCELLED_BY_STAFF, PAYMENT_EXPIRED, APPROVAL_EXPIRED, REJECTED}`, `RecommendationType {REQUIRE_APPROVAL, ALTERNATIVE_SLOT, ALTERNATIVE_RESOURCE, PRIORITY_ESCALATION}`; `internal StrictEnumConverterFactory` (namespace `Shared.DecisionContractSDK.V2.Common`).

- [ ] **Step 1: Viết test (thất bại)**

Create `tests/TADSS.UnitTests/V2/Common/WireEnumTests.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Shared.DecisionContractSDK.V2.Common;
using Shared.DecisionContractSDK.V2.Enums;
using Xunit;

namespace TADSS.UnitTests.V2.Common;

/// <summary>
/// UT-02: enum v2 trên wire chỉ nhận đúng tên đã định nghĩa (phân biệt hoa thường), không nhận số hay chuỗi số;
/// <see cref="TadssJson"/> không đăng ký converter enum toàn cục.
/// </summary>
public class WireEnumTests
{
    private static readonly Type[] V2EnumTypes =
    [
        typeof(DecisionStatus), typeof(ExecutionType), typeof(CustomerTier), typeof(Channel),
        typeof(OverrideAction), typeof(DecisionOutcome), typeof(RecommendationType)
    ];

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void Deserialize_ExactName_ReturnsValue()
    {
        JsonSerializer.Deserialize<Channel>("\"WALK_IN\"", TadssJson.Options).Should().Be(Channel.WALK_IN);
    }

    [Theory]
    [Trait("CatalogId", "UT-02")]
    [InlineData("1")]
    [InlineData("\"1\"")]
    [InlineData("\"walk_in\"")]
    [InlineData("\"PHONE\"")]
    [InlineData("\" ONLINE\"")]
    [InlineData("null")]
    public void Deserialize_InvalidToken_ThrowsJsonException(string json)
    {
        var act = () => JsonSerializer.Deserialize<Channel>(json, TadssJson.Options);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void Serialize_DefinedValue_WritesExactName()
    {
        JsonSerializer.Serialize(Channel.WALK_IN, TadssJson.Options).Should().Be("\"WALK_IN\"");
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void Serialize_UndefinedValue_ThrowsJsonException()
    {
        var act = () => JsonSerializer.Serialize((Channel)99, TadssJson.Options);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void RoundTrip_EveryMemberOfEveryV2Enum_PreservesValue()
    {
        foreach (var enumType in V2EnumTypes)
        {
            foreach (var value in Enum.GetValues(enumType))
            {
                var json = JsonSerializer.Serialize(value, enumType, TadssJson.Options);

                json.Should().Be($"\"{value}\"");
                JsonSerializer.Deserialize(json, enumType, TadssJson.Options).Should().Be(value);
            }
        }
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void Options_IsReadOnlyCamelCaseWithoutGlobalConverters()
    {
        TadssJson.Options.IsReadOnly.Should().BeTrue();
        TadssJson.Options.Converters.Should().BeEmpty("converter enum toàn cục sẽ ghi đè converter chặt gắn trên kiểu");
        TadssJson.Options.PropertyNamingPolicy.Should().Be(JsonNamingPolicy.CamelCase);
        TadssJson.Options.UnmappedMemberHandling.Should().Be(JsonUnmappedMemberHandling.Disallow);
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void Apply_ExistingOptions_RejectsUnmappedMembersAndKeepsStrictEnums()
    {
        var options = new JsonSerializerOptions();
        TadssJson.Apply(options);

        var parsed = JsonSerializer.Deserialize<ChannelProbe>("{\"channel\":\"ONLINE\"}", options)
            ?? throw new InvalidOperationException("Deserialize trả null.");
        var withExtra = () => JsonSerializer.Deserialize<ChannelProbe>("{\"channel\":\"ONLINE\",\"extra\":1}", options);
        var withNumericString = () => JsonSerializer.Deserialize<ChannelProbe>("{\"channel\":\"0\"}", options);

        parsed.Channel.Should().Be(Channel.ONLINE);
        withExtra.Should().Throw<JsonException>();
        withNumericString.Should().Throw<JsonException>();
    }

    private sealed class ChannelProbe
    {
        public Channel Channel { get; set; }
    }
}
```

Create `tests/TADSS.UnitTests/V2/Common/PagedResultTests.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Shared.DecisionContractSDK.V2.Common;
using Xunit;

namespace TADSS.UnitTests.V2.Common;

/// <summary>
/// UT-02: <see cref="PagedResult{T}"/> tính trường suy ra đúng và có hình dạng wire của INTEGRATION_SPEC §3.5.
/// </summary>
public class PagedResultTests
{
    [Theory]
    [Trait("CatalogId", "UT-02")]
    [InlineData(1, 20, 0, 0, false, false)]
    [InlineData(1, 20, 42, 3, true, false)]
    [InlineData(3, 20, 42, 3, false, true)]
    public void Create_ValidArguments_ComputesDerivedFields(
        int page, int pageSize, int totalItems, int expectedTotalPages, bool expectedHasNext, bool expectedHasPrev)
    {
        var result = PagedResult<string>.Create(["a"], page, pageSize, totalItems);

        result.TotalPages.Should().Be(expectedTotalPages);
        result.HasNext.Should().Be(expectedHasNext);
        result.HasPrev.Should().Be(expectedHasPrev);
    }

    [Theory]
    [Trait("CatalogId", "UT-02")]
    [InlineData(0, 20, 0)]
    [InlineData(1, 0, 0)]
    [InlineData(1, 20, -1)]
    public void Create_InvalidArguments_ThrowsArgumentOutOfRange(int page, int pageSize, int totalItems)
    {
        var act = () => PagedResult<string>.Create([], page, pageSize, totalItems);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void Serialize_TadssJson_WritesIntegrationSpecShape()
    {
        var json = JsonSerializer.Serialize(PagedResult<string>.Create(["a"], 1, 20, 1), TadssJson.Options);
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new InvalidOperationException("JSON rỗng.");

        root.Select(property => property.Key).Should().BeEquivalentTo(
            "items", "page", "pageSize", "totalItems", "totalPages", "hasNext", "hasPrev");
    }
}
```

- [ ] **Step 2: Chạy để xác nhận thất bại**

Run: `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror`
Expected: FAIL — `CS0234: The type or namespace name 'V2' does not exist in the namespace 'Shared.DecisionContractSDK'`.

- [ ] **Step 3: Hiện thực Common**

Create `src/Shared.DecisionContractSDK/V2/Common/StrictEnumConverterFactory.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared.DecisionContractSDK.V2.Common;

/// <summary>
/// Converter JSON cho enum của contract v2: chỉ nhận đúng tên đã định nghĩa (phân biệt hoa thường),
/// không nhận số hay chuỗi số. <see cref="JsonStringEnumConverter"/> của .NET nhận cả <c>"1"</c> và chữ thường
/// nên không dùng được cho contract. Gắn trên từng enum bằng <see cref="JsonConverterAttribute"/>.
/// </summary>
internal sealed class StrictEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)(Activator.CreateInstance(typeof(StrictEnumConverter<>).MakeGenericType(typeToConvert))
            ?? throw new InvalidOperationException($"Không tạo được converter cho {typeToConvert}."));
}

/// <summary>Converter enum chặt cho một kiểu enum cụ thể.</summary>
internal sealed class StrictEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private static readonly Dictionary<string, TEnum> ValuesByName =
        Enum.GetValues<TEnum>().ToDictionary(value => value.ToString(), value => value, StringComparer.Ordinal);

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Giá trị của {typeof(TEnum).Name} phải là chuỗi.");
        }

        var name = reader.GetString();
        if (name is null || !ValuesByName.TryGetValue(name, out var value))
        {
            throw new JsonException($"'{name}' không phải giá trị hợp lệ của {typeof(TEnum).Name}.");
        }

        return value;
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        if (!Enum.IsDefined(value))
        {
            throw new JsonException($"Giá trị {value} không được định nghĩa trong {typeof(TEnum).Name}.");
        }

        writer.WriteStringValue(value.ToString());
    }
}
```

Create `src/Shared.DecisionContractSDK/V2/Common/TadssJson.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared.DecisionContractSDK.V2.Common;

/// <summary>
/// Cấu hình JSON duy nhất của contract v2, dùng chung cho API, audit payload và test.
/// Enum và thời gian được kiểm tra chặt bằng converter gắn trên kiểu/thuộc tính, nên cấu hình này
/// <b>không</b> được thêm <see cref="JsonStringEnumConverter"/> toàn cục (converter toàn cục ghi đè converter trên kiểu).
/// </summary>
public static class TadssJson
{
    /// <summary>Bản cấu hình chỉ đọc, dùng trực tiếp khi serialize/deserialize.</summary>
    public static JsonSerializerOptions Options { get; } = CreateReadOnlyOptions();

    /// <summary>
    /// Áp cấu hình contract v2 lên một <see cref="JsonSerializerOptions"/> có sẵn (ví dụ options của ASP.NET Core).
    /// </summary>
    /// <param name="options">Options cần cấu hình.</param>
    public static void Apply(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    }

    private static JsonSerializerOptions CreateReadOnlyOptions()
    {
        var options = new JsonSerializerOptions();
        Apply(options);
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
```

Create `src/Shared.DecisionContractSDK/V2/Common/ErrorCodes.cs`:

```csharp
namespace Shared.DecisionContractSDK.V2.Common;

/// <summary>
/// Mã lỗi của contract v2, trả trong trường <c>code</c> của RFC 7807 (INTEGRATION_SPEC §3.8).
/// </summary>
public static class ErrorCodes
{
    /// <summary>Payload sai cấu trúc, sai định dạng, enum lạ, trường thừa hoặc thiếu trường bắt buộc (400).</summary>
    public const string ValidationError = "VALIDATION_ERROR";

    /// <summary>Payload đúng định dạng nhưng vi phạm bất biến domain (400).</summary>
    public const string DomainInvariantViolation = "DOMAIN_INVARIANT_VIOLATION";

    /// <summary>Thiếu header bắt buộc (400).</summary>
    public const string MissingHeader = "MISSING_HEADER";

    /// <summary>Thiếu hoặc sai <c>X-Api-Key</c> (401).</summary>
    public const string Unauthorized = "UNAUTHORIZED";

    /// <summary>Không có <c>decisionId</c> (404).</summary>
    public const string DecisionNotFound = "DECISION_NOT_FOUND";

    /// <summary>Dùng lại idempotency key với payload khác (409).</summary>
    public const string IdempotencyKeyReuse = "IDEMPOTENCY_KEY_REUSE";

    /// <summary>Quyết định không ở trạng thái chờ duyệt (409).</summary>
    public const string NotPendingApproval = "NOT_PENDING_APPROVAL";

    /// <summary>Quyết định đã được override (409).</summary>
    public const string AlreadyOverridden = "ALREADY_OVERRIDDEN";

    /// <summary>Đã quá hạn duyệt (409).</summary>
    public const string ApprovalExpired = "APPROVAL_EXPIRED";

    /// <summary>Quyết định đã có outcome khác (409).</summary>
    public const string OutcomeAlreadyRecorded = "OUTCOME_ALREADY_RECORDED";

    /// <summary>Không ghi được audit, fail-closed (503).</summary>
    public const string PersistenceUnavailable = "PERSISTENCE_UNAVAILABLE";

    /// <summary>Vượt deadline toàn cục (504).</summary>
    public const string GatewayTimeout = "GATEWAY_TIMEOUT";

    /// <summary>Lỗi không mong đợi, ẩn chi tiết kỹ thuật (500).</summary>
    public const string InternalError = "INTERNAL_ERROR";
}
```

Create `src/Shared.DecisionContractSDK/V2/Common/ReasonCodes.cs`:

```csharp
namespace Shared.DecisionContractSDK.V2.Common;

/// <summary>
/// Mã lý do của quyết định (trường <c>reasonCode</c>, tối đa 64 ký tự) — SOFTWARE_ARCHITECTURE_SPECIFICATION 4.3
/// và INTEGRATION_SPEC §1.1.
/// </summary>
public static class ReasonCodes
{
    /// <summary>Cọc theo hạng thành viên (<c>DefaultDecisionStrategy</c>).</summary>
    public const string TierDeposit = "TIER_DEPOSIT";

    /// <summary>Đơn giá trị lớn cần Manager duyệt.</summary>
    public const string HighValueReview = "HIGH_VALUE_REVIEW";

    /// <summary>Khách có lịch sử không đến cần Manager duyệt.</summary>
    public const string NoShowHistoryReview = "NO_SHOW_HISTORY_REVIEW";

    /// <summary>Cọc khung giờ cao điểm (<c>MockAlternativeDecisionStrategy</c>).</summary>
    public const string PeakHourDeposit = "PEAK_HOUR_DEPOSIT";

    /// <summary>Cọc ngoài giờ cao điểm (<c>MockAlternativeDecisionStrategy</c>).</summary>
    public const string OffPeakDeposit = "OFF_PEAK_DEPOSIT";

    /// <summary>Cần duyệt nhưng không còn đủ thời gian duyệt trước giờ chơi → cọc 100%.</summary>
    public const string ApprovalWindowUnavailable = "APPROVAL_WINDOW_UNAVAILABLE";

    /// <summary>Fallback do strategy vượt timeout.</summary>
    public const string FallbackStrategyTimeout = "FALLBACK_STRATEGY_TIMEOUT";

    /// <summary>Fallback do strategy ném lỗi.</summary>
    public const string FallbackStrategyException = "FALLBACK_STRATEGY_EXCEPTION";

    /// <summary>Fallback do circuit breaker đang mở.</summary>
    public const string FallbackCircuitOpen = "FALLBACK_CIRCUIT_OPEN";
}
```

Create `src/Shared.DecisionContractSDK/V2/Common/PagedResult.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Shared.DecisionContractSDK.V2.Common;

/// <summary>
/// Kết quả phân trang của contract v2 (INTEGRATION_SPEC §3.5).
/// </summary>
/// <typeparam name="T">Kiểu phần tử.</typeparam>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PagedResult<T>
{
    /// <summary>Các phần tử của trang hiện tại.</summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>Số trang hiện tại, bắt đầu từ 1.</summary>
    public required int Page { get; init; }

    /// <summary>Số phần tử tối đa mỗi trang.</summary>
    public required int PageSize { get; init; }

    /// <summary>Tổng số phần tử của mọi trang.</summary>
    public required int TotalItems { get; init; }

    /// <summary>Tổng số trang.</summary>
    public required int TotalPages { get; init; }

    /// <summary>Còn trang sau.</summary>
    public required bool HasNext { get; init; }

    /// <summary>Có trang trước.</summary>
    public required bool HasPrev { get; init; }

    /// <summary>Tạo một trang và tính các trường suy ra.</summary>
    /// <param name="items">Các phần tử của trang.</param>
    /// <param name="page">Số trang, bắt đầu từ 1.</param>
    /// <param name="pageSize">Số phần tử tối đa mỗi trang (lớn hơn 0).</param>
    /// <param name="totalItems">Tổng số phần tử (không âm).</param>
    public static PagedResult<T> Create(IReadOnlyList<T> items, int page, int pageSize, int totalItems)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(totalItems);

        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        return new PagedResult<T>
        {
            Items = items.ToArray(),
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = totalPages,
            HasNext = page < totalPages,
            HasPrev = page > 1
        };
    }
}
```

- [ ] **Step 4: Hiện thực Enums**

Mỗi enum một file trong `src/Shared.DecisionContractSDK/V2/Enums/`, cùng khuôn dưới đây (giá trị số tường minh từ 0 theo đúng thứ tự liệt kê).

`DecisionStatus.cs`:

```csharp
using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.V2.Common;

namespace Shared.DecisionContractSDK.V2.Enums;

/// <summary>
/// Trạng thái quyết định trả cho client (INTEGRATION_SPEC §3.3).
/// Trên wire là chuỗi đúng tên thành viên; số, chuỗi số và chữ thường bị từ chối.
/// </summary>
[JsonConverter(typeof(StrictEnumConverterFactory))]
public enum DecisionStatus
{
    /// <summary>Được duyệt; luôn kèm mức cọc.</summary>
    APPROVED = 0,

    /// <summary>Cần Manager duyệt; luôn kèm hạn duyệt.</summary>
    NEED_APPROVAL = 1,

    /// <summary>Bị từ chối theo chính sách.</summary>
    REJECTED = 2
}
```

`ExecutionType.cs`:

```csharp
using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.V2.Common;

namespace Shared.DecisionContractSDK.V2.Enums;

/// <summary>
/// Cách quyết định được tạo ra.
/// Trên wire là chuỗi đúng tên thành viên; số, chuỗi số và chữ thường bị từ chối.
/// </summary>
[JsonConverter(typeof(StrictEnumConverterFactory))]
public enum ExecutionType
{
    /// <summary>Strategy chạy và trả kết quả trong ngân sách thời gian.</summary>
    AUTOMATED = 0,

    /// <summary>Strategy lỗi, timeout hoặc circuit mở; dùng FallbackDecisionStrategy.</summary>
    FALLBACK = 1,

    /// <summary>Dùng lại StrategyDecision đã cache.</summary>
    CACHE_HIT = 2
}
```

`CustomerTier.cs`:

```csharp
using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.V2.Common;

namespace Shared.DecisionContractSDK.V2.Enums;

/// <summary>
/// Hạng thành viên chuẩn hóa; client ánh xạ hạng nội bộ theo thứ hạng (SmashCourt: <c>MinPoints</c> tăng dần).
/// Trên wire là chuỗi đúng tên thành viên; số, chuỗi số và chữ thường bị từ chối.
/// </summary>
[JsonConverter(typeof(StrictEnumConverterFactory))]
public enum CustomerTier
{
    /// <summary>Hạng cơ bản (khách vãng lai cũng dùng hạng này).</summary>
    STANDARD = 0,

    /// <summary>Hạng thứ 2.</summary>
    SILVER = 1,

    /// <summary>Hạng thứ 3.</summary>
    GOLD = 2,

    /// <summary>Hạng thứ 4.</summary>
    PLATINUM = 3,

    /// <summary>Hạng thứ 5 trở lên.</summary>
    DIAMOND = 4
}
```

`Channel.cs`:

```csharp
using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.V2.Common;

namespace Shared.DecisionContractSDK.V2.Enums;

/// <summary>
/// Kênh tạo booking (P-02) — trùng tên với <c>BookingSource</c> của SmashCourt-BE.
/// Trên wire là chuỗi đúng tên thành viên; số, chuỗi số và chữ thường bị từ chối.
/// </summary>
[JsonConverter(typeof(StrictEnumConverterFactory))]
public enum Channel
{
    /// <summary>Khách tự đặt online.</summary>
    ONLINE = 0,

    /// <summary>Đặt tại quầy (SmashCourt chưa gọi TADSS cho kênh này — INTEGRATION_SPEC §4.2).</summary>
    WALK_IN = 1
}
```

`OverrideAction.cs`:

```csharp
using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.V2.Common;

namespace Shared.DecisionContractSDK.V2.Enums;

/// <summary>
/// Hành động của Manager trên quyết định chờ duyệt.
/// Trên wire là chuỗi đúng tên thành viên; số, chuỗi số và chữ thường bị từ chối.
/// </summary>
[JsonConverter(typeof(StrictEnumConverterFactory))]
public enum OverrideAction
{
    /// <summary>Duyệt kèm mức cọc.</summary>
    APPROVE = 0,

    /// <summary>Từ chối.</summary>
    REJECT = 1
}
```

`DecisionOutcome.cs`:

```csharp
using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.V2.Common;

namespace Shared.DecisionContractSDK.V2.Enums;

/// <summary>
/// Kết quả thực tế của booking, client báo về sau khi booking kết thúc (INTEGRATION_SPEC §2.3).
/// Trên wire là chuỗi đúng tên thành viên; số, chuỗi số và chữ thường bị từ chối.
/// </summary>
[JsonConverter(typeof(StrictEnumConverterFactory))]
public enum DecisionOutcome
{
    /// <summary>Khách đến và hoàn tất.</summary>
    COMPLETED = 0,

    /// <summary>Khách không đến.</summary>
    NO_SHOW = 1,

    /// <summary>Khách hủy.</summary>
    CANCELLED_BY_CUSTOMER = 2,

    /// <summary>Nhân viên hủy.</summary>
    CANCELLED_BY_STAFF = 3,

    /// <summary>Hết hạn thanh toán cọc.</summary>
    PAYMENT_EXPIRED = 4,

    /// <summary>Hết hạn duyệt mà Manager chưa xử lý.</summary>
    APPROVAL_EXPIRED = 5,

    /// <summary>Bị từ chối (bởi strategy hoặc Manager).</summary>
    REJECTED = 6
}
```

`RecommendationType.cs`:

```csharp
using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.V2.Common;

namespace Shared.DecisionContractSDK.V2.Enums;

/// <summary>
/// Loại khuyến nghị kèm quyết định.
/// Trên wire là chuỗi đúng tên thành viên; số, chuỗi số và chữ thường bị từ chối.
/// </summary>
[JsonConverter(typeof(StrictEnumConverterFactory))]
public enum RecommendationType
{
    /// <summary>Cần Manager duyệt.</summary>
    REQUIRE_APPROVAL = 0,

    /// <summary>Gợi ý khung giờ khác.</summary>
    ALTERNATIVE_SLOT = 1,

    /// <summary>Gợi ý tài nguyên (sân) khác.</summary>
    ALTERNATIVE_RESOURCE = 2,

    /// <summary>Gợi ý ưu tiên xử lý.</summary>
    PRIORITY_ESCALATION = 3
}
```

- [ ] **Step 5: Build và chạy test mới**

Run: `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror && dotnet test tests/TADSS.UnitTests/TADSS.UnitTests.csproj -c Release --no-build -nologo --filter "FullyQualifiedName~TADSS.UnitTests.V2"`
Expected: build 0 warning; `Passed: 19`.

- [ ] **Step 6: Cập nhật baseline**

Theo quy trình "Cập nhật baseline". Kỳ vọng các dòng `+[...]` mới: 4 kiểu Common public (`ErrorCodes`, `PagedResult\`1`, `ReasonCodes`, `TadssJson`) và 7 enum (`StrictEnumConverterFactory` là internal nên **không** xuất hiện).

- [ ] **Step 7: Chạy toàn bộ unit test**

Run: `dotnet test tests/TADSS.UnitTests/TADSS.UnitTests.csproj -c Release --no-build -nologo`
Expected: `Passed: 494`, `Failed: 0`.

- [ ] **Step 8: Commit (hỏi người dùng trước)**

```bash
git add src/Shared.DecisionContractSDK/V2 tests/TADSS.UnitTests/V2 docs/contracts/baseline_public_api.txt
git commit -m "feat(sdk-v2): Common và Enums v2 với JSON enum chặt"
```

---

### Task 3: V2 Domain (reservation nhiều item, khách, lịch sử, nhu cầu, lịch cao điểm)

**Files:**

- Create: `src/Shared.DecisionContractSDK/V2/Domain/{ReservationItem,CustomerHistory,CustomerProfile,DemandSnapshot,Reservation,PeakWindow,BranchCalendar}.cs`
- Create: `tests/TADSS.UnitTests/V2/TestData/V2TestData.cs`
- Create: `tests/TADSS.UnitTests/V2/Domain/{ReservationItemTests,CustomerTests,ReservationTests,BranchCalendarTests}.cs`, `tests/TADSS.UnitTests/V2/Architecture/SdkV2LayeringTests.cs`
- Modify: `docs/contracts/baseline_public_api.txt`

**Interfaces:**

- Consumes: `Channel`, `CustomerTier` (Task 2); `DomainInvariantException(string invariantCode, string message, string? fieldName)` của v1.
- Produces (chữ ký constructor — Task 4–7 dùng đúng tên tham số):
  - `ReservationItem(Guid resourceId, Guid resourceTypeId, DateTimeOffset start, DateTimeOffset end, decimal price)`; thuộc tính `ResourceId`, `ResourceTypeId`, `Start`, `End`, `Price`, `Duration`.
  - `CustomerHistory(int completedCount, int noShowCount, int cancelledCount, int lateCancelledCount, int paymentExpiredCount, DateTimeOffset? lastCompletedAt)`.
  - `CustomerProfile(bool isGuest, Guid? customerId, CustomerTier tier, DateTimeOffset? memberSince, CustomerHistory? history)`.
  - `DemandSnapshot(decimal occupancyRate, int waitlistCount)`.
  - `Reservation(Guid reservationId, Guid branchId, TimeZoneInfo branchTimeZone, IReadOnlyList<ReservationItem> items, CustomerProfile customer, decimal totalPrice, string currency, Channel channel, DateTimeOffset requestedAt, DemandSnapshot? demand = null, IReadOnlyDictionary<string, string>? metadata = null)`; `const int MaxItems = 20`; `EarliestStart`; `ToBranchLocal(DateTimeOffset)`.
  - `PeakWindow(DayOfWeek day, TimeOnly start, TimeOnly end)`; `Contains(DateTimeOffset branchLocal)`.
  - `BranchCalendar(IEnumerable<PeakWindow> peakWindows)`; `IsPeak(Reservation)`.
  - Test helper `TADSS.UnitTests.V2.TestData.V2TestData`: `HoChiMinh`, `Lisbon`, `VietnamOffset`, `FridayPeakStart`, `StandardCalendar()`, `CreateItem(...)`, `CreateMember(...)`, `CreateGuest(...)`, `CreateHistory(...)`, `CreateReservation(...)`.
- Mã bất biến (`InvariantCode`): `INVALID_RESOURCE_ID`, `INVALID_RESOURCE_TYPE_ID`, `INVALID_ITEM_RANGE`, `INVALID_ITEM_PRICE`, `INVALID_CUSTOMER_HISTORY`, `GUEST_WITH_CUSTOMER_ID`, `GUEST_WITH_MEMBER_SINCE`, `MISSING_CUSTOMER_ID`, `INVALID_CUSTOMER_TIER`, `INVALID_OCCUPANCY_RATE`, `INVALID_WAITLIST_COUNT`, `INVALID_RESERVATION_ID`, `INVALID_BRANCH_ID`, `INVALID_ITEM_COUNT`, `INVALID_TOTAL_PRICE`, `INVALID_CURRENCY`, `INVALID_CHANNEL`, `INVALID_PEAK_WINDOW`.

- [ ] **Step 1: Viết test helper và test (thất bại)**

Create `tests/TADSS.UnitTests/V2/TestData/V2TestData.cs`:

```csharp
using Shared.DecisionContractSDK.V2.Domain;
using Shared.DecisionContractSDK.V2.Enums;

namespace TADSS.UnitTests.V2.TestData;

/// <summary>Dữ liệu mẫu dùng chung cho test SDK v2 và strategy v2.</summary>
internal static class V2TestData
{
    public static readonly TimeZoneInfo HoChiMinh = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
    public static readonly TimeZoneInfo Lisbon = TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon");
    public static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    /// <summary>Thứ Sáu 20/11/2026 18:00 giờ Việt Nam (giờ cao điểm theo lịch mặc định).</summary>
    public static readonly DateTimeOffset FridayPeakStart = new(2026, 11, 20, 18, 0, 0, TimeSpan.FromHours(7));

    /// <summary>Lịch cao điểm mặc định: thứ Hai–Sáu 17:00–21:00, thứ Bảy–Chủ Nhật 08:00–21:00.</summary>
    public static BranchCalendar StandardCalendar()
    {
        var weekdays = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }
            .Select(day => new PeakWindow(day, new TimeOnly(17, 0), new TimeOnly(21, 0)));
        var weekend = new[] { DayOfWeek.Saturday, DayOfWeek.Sunday }
            .Select(day => new PeakWindow(day, new TimeOnly(8, 0), new TimeOnly(21, 0)));
        return new BranchCalendar(weekdays.Concat(weekend));
    }

    public static ReservationItem CreateItem(DateTimeOffset start, double hours = 2, decimal price = 240_000m) =>
        new(Guid.NewGuid(), Guid.NewGuid(), start, start.AddHours(hours), price);

    public static CustomerHistory CreateHistory(int noShowCount = 0) =>
        new(completedCount: 12, noShowCount: noShowCount, cancelledCount: 2, lateCancelledCount: 0, paymentExpiredCount: 0, lastCompletedAt: null);

    public static CustomerProfile CreateMember(CustomerTier tier = CustomerTier.GOLD, CustomerHistory? history = null) =>
        new(isGuest: false, customerId: Guid.NewGuid(), tier: tier,
            memberSince: new DateTimeOffset(2025, 3, 14, 8, 0, 0, TimeSpan.FromHours(7)), history: history);

    public static CustomerProfile CreateGuest(CustomerHistory? history = null) =>
        new(isGuest: true, customerId: null, tier: CustomerTier.STANDARD, memberSince: null, history: history);

    public static Reservation CreateReservation(
        IReadOnlyList<ReservationItem>? items = null,
        CustomerProfile? customer = null,
        decimal totalPrice = 228_000m,
        TimeZoneInfo? timeZone = null,
        string currency = "VND",
        Channel channel = Channel.ONLINE,
        Guid? reservationId = null,
        Guid? branchId = null,
        DemandSnapshot? demand = null,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(
            reservationId: reservationId ?? Guid.NewGuid(),
            branchId: branchId ?? Guid.NewGuid(),
            branchTimeZone: timeZone ?? HoChiMinh,
            items: items ?? new[] { CreateItem(FridayPeakStart) },
            customer: customer ?? CreateMember(),
            totalPrice: totalPrice,
            currency: currency,
            channel: channel,
            requestedAt: new DateTimeOffset(2026, 11, 18, 9, 15, 0, TimeSpan.FromHours(7)),
            demand: demand,
            metadata: metadata);
}
```

Create `tests/TADSS.UnitTests/V2/Domain/ReservationItemTests.cs`:

```csharp
using FluentAssertions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.V2.Domain;
using TADSS.UnitTests.V2.TestData;
using Xunit;

namespace TADSS.UnitTests.V2.Domain;

/// <summary>
/// UT-01: bất biến của <see cref="ReservationItem"/> — chỉ <c>End &gt; Start</c> là bất biến thời gian của SDK
/// (giới hạn thời lượng là chính sách runtime, P-01).
/// </summary>
public class ReservationItemTests
{
    private static readonly DateTimeOffset Start = V2TestData.FridayPeakStart;

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Constructor_ValidInput_ExposesValuesAndDuration()
    {
        var resourceId = Guid.NewGuid();
        var resourceTypeId = Guid.NewGuid();

        var item = new ReservationItem(resourceId, resourceTypeId, Start, Start.AddHours(2), 240_000m);

        item.ResourceId.Should().Be(resourceId);
        item.ResourceTypeId.Should().Be(resourceTypeId);
        item.Price.Should().Be(240_000m);
        item.Duration.Should().Be(TimeSpan.FromHours(2));
    }

    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData(0)]
    [InlineData(-30)]
    public void Constructor_EndNotAfterStart_ThrowsInvalidItemRange(int endOffsetMinutes)
    {
        var act = () => new ReservationItem(Guid.NewGuid(), Guid.NewGuid(), Start, Start.AddMinutes(endOffsetMinutes), 1m);

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_ITEM_RANGE");
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Constructor_ZeroPrice_IsAllowed()
    {
        var act = () => new ReservationItem(Guid.NewGuid(), Guid.NewGuid(), Start, Start.AddHours(1), 0m);

        act.Should().NotThrow();
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Constructor_NegativePrice_ThrowsInvalidItemPrice()
    {
        var act = () => new ReservationItem(Guid.NewGuid(), Guid.NewGuid(), Start, Start.AddHours(1), -1m);

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_ITEM_PRICE");
    }

    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData(true, "INVALID_RESOURCE_ID")]
    [InlineData(false, "INVALID_RESOURCE_TYPE_ID")]
    public void Constructor_EmptyIdentifier_ThrowsInvariant(bool emptyResource, string expectedCode)
    {
        var act = () => new ReservationItem(
            emptyResource ? Guid.Empty : Guid.NewGuid(),
            emptyResource ? Guid.NewGuid() : Guid.Empty,
            Start, Start.AddHours(1), 1m);

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be(expectedCode);
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Constructor_MultiDayStay_IsAllowedBySdk()
    {
        // P-01: lưu trú nhiều đêm (PT-01 Hotel Booking Demand) không bị SDK chặn
        var item = new ReservationItem(Guid.NewGuid(), Guid.NewGuid(), Start, Start.AddHours(70), 300m);

        item.Duration.Should().Be(TimeSpan.FromHours(70));
    }
}
```

Create `tests/TADSS.UnitTests/V2/Domain/CustomerTests.cs`:

```csharp
using FluentAssertions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.V2.Domain;
using Shared.DecisionContractSDK.V2.Enums;
using Xunit;

namespace TADSS.UnitTests.V2.Domain;

/// <summary>
/// UT-01: bất biến của <see cref="CustomerProfile"/>, <see cref="CustomerHistory"/> và <see cref="DemandSnapshot"/>
/// (khách vãng lai không có định danh và thâm niên; số đếm không âm; tỷ lệ kín sân trong [0, 1]).
/// </summary>
public class CustomerTests
{
    private static readonly DateTimeOffset MemberSince = new(2025, 3, 14, 8, 0, 0, TimeSpan.FromHours(7));

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Constructor_RegisteredMember_ExposesValues()
    {
        var customerId = Guid.NewGuid();

        var profile = new CustomerProfile(false, customerId, CustomerTier.SILVER, MemberSince, null);

        profile.IsGuest.Should().BeFalse();
        profile.CustomerId.Should().Be(customerId);
        profile.Tier.Should().Be(CustomerTier.SILVER);
        profile.MemberSince.Should().Be(MemberSince);
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Constructor_GuestWithoutIdentity_IsAllowed()
    {
        var profile = new CustomerProfile(true, null, CustomerTier.STANDARD, null, null);

        profile.IsGuest.Should().BeTrue();
        profile.CustomerId.Should().BeNull();
    }

    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData("guest-with-id", "GUEST_WITH_CUSTOMER_ID")]
    [InlineData("guest-with-member-since", "GUEST_WITH_MEMBER_SINCE")]
    [InlineData("member-without-id", "MISSING_CUSTOMER_ID")]
    [InlineData("member-with-empty-id", "MISSING_CUSTOMER_ID")]
    [InlineData("undefined-tier", "INVALID_CUSTOMER_TIER")]
    public void Constructor_InvalidProfile_ThrowsInvariant(string caseName, string expectedCode)
    {
        Action act = caseName switch
        {
            "guest-with-id" => () => _ = new CustomerProfile(true, Guid.NewGuid(), CustomerTier.STANDARD, null, null),
            "guest-with-member-since" => () => _ = new CustomerProfile(true, null, CustomerTier.STANDARD, MemberSince, null),
            "member-without-id" => () => _ = new CustomerProfile(false, null, CustomerTier.GOLD, MemberSince, null),
            "member-with-empty-id" => () => _ = new CustomerProfile(false, Guid.Empty, CustomerTier.GOLD, MemberSince, null),
            "undefined-tier" => () => _ = new CustomerProfile(false, Guid.NewGuid(), (CustomerTier)42, MemberSince, null),
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be(expectedCode);
    }

    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData(-1, 0, 0, 0, 0)]
    [InlineData(0, -1, 0, 0, 0)]
    [InlineData(0, 0, -1, 0, 0)]
    [InlineData(0, 0, 0, -1, 0)]
    [InlineData(0, 0, 0, 0, -1)]
    public void History_NegativeCount_ThrowsInvalidCustomerHistory(
        int completed, int noShow, int cancelled, int lateCancelled, int paymentExpired)
    {
        var act = () => new CustomerHistory(completed, noShow, cancelled, lateCancelled, paymentExpired, null);

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_CUSTOMER_HISTORY");
    }

    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void Demand_BoundaryOccupancy_IsAllowed(double occupancyRate)
    {
        var demand = new DemandSnapshot((decimal)occupancyRate, 0);

        demand.OccupancyRate.Should().Be((decimal)occupancyRate);
    }

    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData(-0.01, 0, "INVALID_OCCUPANCY_RATE")]
    [InlineData(1.01, 0, "INVALID_OCCUPANCY_RATE")]
    [InlineData(0.5, -1, "INVALID_WAITLIST_COUNT")]
    public void Demand_OutOfRange_ThrowsInvariant(double occupancyRate, int waitlistCount, string expectedCode)
    {
        var act = () => new DemandSnapshot((decimal)occupancyRate, waitlistCount);

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be(expectedCode);
    }
}
```

Create `tests/TADSS.UnitTests/V2/Domain/ReservationTests.cs`:

```csharp
using FluentAssertions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.V2.Domain;
using Shared.DecisionContractSDK.V2.Enums;
using TADSS.UnitTests.V2.TestData;
using Xunit;

namespace TADSS.UnitTests.V2.Domain;

/// <summary>
/// UT-01: bất biến của <see cref="Reservation"/> v2 (1..20 item, định danh, tổng tiền, tiền tệ, kênh)
/// và tính bất biến sau khi khởi tạo.
/// </summary>
public class ReservationTests
{
    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Constructor_ValidItems_CopiesItemsAndComputesEarliestStart()
    {
        var later = V2TestData.CreateItem(V2TestData.FridayPeakStart.AddHours(2));
        var earlier = V2TestData.CreateItem(V2TestData.FridayPeakStart);
        var source = new List<ReservationItem> { later, earlier };

        var reservation = V2TestData.CreateReservation(items: source);
        source.Add(V2TestData.CreateItem(V2TestData.FridayPeakStart.AddHours(4)));

        reservation.Items.Should().HaveCount(2, "danh sách item được sao chép khi khởi tạo");
        reservation.EarliestStart.Should().Be(V2TestData.FridayPeakStart);
    }

    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(20, true)]
    [InlineData(21, false)]
    public void Constructor_ItemCount_EnforcesOneToTwenty(int count, bool isValid)
    {
        var items = Enumerable.Range(0, count)
            .Select(index => V2TestData.CreateItem(V2TestData.FridayPeakStart.AddDays(index)))
            .ToArray();

        var act = () => V2TestData.CreateReservation(items: items);

        if (isValid)
        {
            act.Should().NotThrow();
        }
        else
        {
            act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_ITEM_COUNT");
        }
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Constructor_NullItem_ThrowsInvalidItemCount()
    {
        // Cast không kiểm tra null nên mảng kết quả giữ phần tử null mà không cần toán tử '!'
        var items = new ReservationItem?[] { V2TestData.CreateItem(V2TestData.FridayPeakStart), null }
            .Cast<ReservationItem>()
            .ToArray();

        var act = () => V2TestData.CreateReservation(items: items);

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_ITEM_COUNT");
    }

    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData(true, "INVALID_RESERVATION_ID")]
    [InlineData(false, "INVALID_BRANCH_ID")]
    public void Constructor_EmptyIdentifier_ThrowsInvariant(bool emptyReservation, string expectedCode)
    {
        var act = () => V2TestData.CreateReservation(
            reservationId: emptyReservation ? Guid.Empty : Guid.NewGuid(),
            branchId: emptyReservation ? Guid.NewGuid() : Guid.Empty);

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be(expectedCode);
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Constructor_NegativeTotalPrice_ThrowsInvalidTotalPrice()
    {
        var act = () => V2TestData.CreateReservation(totalPrice: -1m);

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_TOTAL_PRICE");
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Constructor_ZeroTotalPrice_IsAllowed()
    {
        var act = () => V2TestData.CreateReservation(totalPrice: 0m);

        act.Should().NotThrow();
    }

    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData("vnd")]
    [InlineData("VN")]
    [InlineData("VNDX")]
    [InlineData("")]
    [InlineData("V1D")]
    public void Constructor_InvalidCurrency_ThrowsInvalidCurrency(string currency)
    {
        var act = () => V2TestData.CreateReservation(currency: currency);

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_CURRENCY");
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Constructor_UndefinedChannel_ThrowsInvalidChannel()
    {
        var act = () => V2TestData.CreateReservation(channel: (Channel)7);

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_CHANNEL");
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Constructor_Metadata_IsCopiedAndDefaultsToEmpty()
    {
        var source = new Dictionary<string, string> { ["promotionCode"] = "WINTER" };

        var withMetadata = V2TestData.CreateReservation(metadata: source);
        source["promotionCode"] = "CHANGED";
        var withoutMetadata = V2TestData.CreateReservation();

        withMetadata.Metadata["promotionCode"].Should().Be("WINTER");
        withoutMetadata.Metadata.Should().BeEmpty();
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void ToBranchLocal_UtcInstant_ConvertsToBranchTimeZone()
    {
        var reservation = V2TestData.CreateReservation();

        var local = reservation.ToBranchLocal(new DateTimeOffset(2026, 11, 20, 11, 0, 0, TimeSpan.Zero));

        local.Offset.Should().Be(TimeSpan.FromHours(7));
        local.Hour.Should().Be(18);
    }
}
```

Create `tests/TADSS.UnitTests/V2/Domain/BranchCalendarTests.cs`:

```csharp
using System.Globalization;
using FluentAssertions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.V2.Domain;
using TADSS.UnitTests.V2.TestData;
using Xunit;

namespace TADSS.UnitTests.V2.Domain;

/// <summary>
/// UT-05: giờ cao điểm xét theo thời điểm bắt đầu sớm nhất, quy về múi giờ chi nhánh
/// (biên 16:59/17:00/20:59/21:00, cuối tuần, thời điểm UTC đổi ngày, múi giờ khác).
/// </summary>
public class BranchCalendarTests
{
    [Theory]
    [Trait("CatalogId", "UT-05")]
    [InlineData("2026-11-20T16:59:00+07:00", false)]
    [InlineData("2026-11-20T17:00:00+07:00", true)]
    [InlineData("2026-11-20T20:59:00+07:00", true)]
    [InlineData("2026-11-20T21:00:00+07:00", false)]
    [InlineData("2026-11-21T07:59:00+07:00", false)]
    [InlineData("2026-11-21T08:00:00+07:00", true)]
    [InlineData("2026-11-22T20:59:00+07:00", true)]
    [InlineData("2026-11-20T10:00:00Z", true)]
    [InlineData("2026-11-20T09:59:00Z", false)]
    [InlineData("2026-11-20T20:00:00Z", false)]
    public void IsPeak_HoChiMinhBranch_UsesBranchLocalTime(string startIso, bool expectedPeak)
    {
        // 2026-11-20T20:00:00Z là thứ Sáu 20:00 theo UTC nhưng là thứ Bảy 03:00 giờ chi nhánh → không cao điểm
        var start = DateTimeOffset.Parse(startIso, CultureInfo.InvariantCulture);
        var reservation = V2TestData.CreateReservation(items: [V2TestData.CreateItem(start, hours: 1)]);

        V2TestData.StandardCalendar().IsPeak(reservation).Should().Be(expectedPeak);
    }

    [Theory]
    [Trait("CatalogId", "UT-05")]
    [InlineData("2026-07-03T16:00:00Z", true)]
    [InlineData("2026-07-03T15:59:00Z", false)]
    public void IsPeak_LisbonBranchInSummer_UsesDaylightOffset(string startIso, bool expectedPeak)
    {
        // Lisbon mùa hè UTC+01:00: 16:00Z = 17:00 thứ Sáu giờ chi nhánh
        var start = DateTimeOffset.Parse(startIso, CultureInfo.InvariantCulture);
        var reservation = V2TestData.CreateReservation(
            items: [V2TestData.CreateItem(start, hours: 1)], timeZone: V2TestData.Lisbon, currency: "EUR");

        V2TestData.StandardCalendar().IsPeak(reservation).Should().Be(expectedPeak);
    }

    [Fact]
    [Trait("CatalogId", "UT-05")]
    public void IsPeak_MultipleItems_UsesEarliestStartRegardlessOfOrder()
    {
        var offPeak = V2TestData.CreateItem(new DateTimeOffset(2026, 11, 20, 16, 0, 0, V2TestData.VietnamOffset), hours: 1);
        var peak = V2TestData.CreateItem(V2TestData.FridayPeakStart, hours: 1);
        var reservation = V2TestData.CreateReservation(items: [peak, offPeak]);

        V2TestData.StandardCalendar().IsPeak(reservation).Should().BeFalse();
    }

    [Fact]
    [Trait("CatalogId", "UT-05")]
    public void IsPeak_EmptyCalendar_ReturnsFalse()
    {
        new BranchCalendar([]).IsPeak(V2TestData.CreateReservation()).Should().BeFalse();
    }

    [Theory]
    [Trait("CatalogId", "UT-05")]
    [InlineData(17, 17)]
    [InlineData(21, 17)]
    public void PeakWindow_EndNotAfterStart_ThrowsInvalidPeakWindow(int startHour, int endHour)
    {
        var act = () => new PeakWindow(DayOfWeek.Friday, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0));

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_PEAK_WINDOW");
    }

    [Fact]
    [Trait("CatalogId", "UT-05")]
    public void PeakWindow_UndefinedDay_ThrowsInvalidPeakWindow()
    {
        var act = () => new PeakWindow((DayOfWeek)9, new TimeOnly(17, 0), new TimeOnly(21, 0));

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_PEAK_WINDOW");
    }
}
```

Create `tests/TADSS.UnitTests/V2/Architecture/SdkV2LayeringTests.cs`:

```csharp
using System.Reflection;
using FluentAssertions;
using Shared.DecisionContractSDK.V2.Domain;
using Xunit;

namespace TADSS.UnitTests.V2.Architecture;

/// <summary>
/// UT-09: phân tầng của SDK v2 — Domain/Decisions/Interfaces không phụ thuộc DTO; Common/Enums không phụ thuộc
/// Domain, Decisions hay DTO (cùng nguyên tắc với <c>SdkDependencyPurityTests</c> của v1).
/// </summary>
public class SdkV2LayeringTests
{
    private const string V2 = "Shared.DecisionContractSDK.V2.";

    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void DomainDecisionsAndInterfaces_DoNotReference_Dtos()
    {
        AssertNoReferences(
            sourceNamespaces: [V2 + "Domain", V2 + "Decisions", V2 + "Interfaces"],
            forbiddenPrefixes: [V2 + "DTOs"]);
    }

    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void CommonAndEnums_DoNotReference_DomainDecisionsOrDtos()
    {
        AssertNoReferences(
            sourceNamespaces: [V2 + "Common", V2 + "Enums"],
            forbiddenPrefixes: [V2 + "Domain", V2 + "Decisions", V2 + "DTOs"]);
    }

    private static void AssertNoReferences(string[] sourceNamespaces, string[] forbiddenPrefixes)
    {
        var types = typeof(Reservation).Assembly.GetTypes()
            .Where(type => type.Namespace is not null && sourceNamespaces.Contains(type.Namespace))
            .ToList();

        types.Should().NotBeEmpty();

        foreach (var type in types)
        {
            var violations = ReferencedTypes(type)
                .Where(referenced => referenced.Namespace is { } ns
                    && forbiddenPrefixes.Any(prefix => ns.StartsWith(prefix, StringComparison.Ordinal)))
                .Select(referenced => referenced.FullName)
                .Distinct()
                .ToList();

            violations.Should().BeEmpty($"'{type.FullName}' không được phụ thuộc {string.Join(", ", forbiddenPrefixes)}");
        }
    }

    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var methods = type.GetMethods(all);
        var direct = type.GetProperties(all).Select(property => property.PropertyType)
            .Concat(type.GetFields(all).Select(field => field.FieldType))
            .Concat(methods.Select(method => method.ReturnType))
            .Concat(methods.SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType)))
            .Concat(type.GetConstructors(all).SelectMany(ctor => ctor.GetParameters().Select(parameter => parameter.ParameterType)));

        return direct.SelectMany(Flatten);
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;

        var element = type.HasElementType ? type.GetElementType() : null;
        if (element is not null)
        {
            foreach (var inner in Flatten(element))
            {
                yield return inner;
            }
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments().SelectMany(Flatten))
            {
                yield return argument;
            }
        }
    }
}
```

- [ ] **Step 2: Chạy để xác nhận thất bại**

Run: `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror`
Expected: FAIL — `CS0234`/`CS0246` không tìm thấy `Shared.DecisionContractSDK.V2.Domain`, `ReservationItem`, `CustomerProfile`…

- [ ] **Step 3: Hiện thực Domain**

Create `src/Shared.DecisionContractSDK/V2/Domain/ReservationItem.cs`:

```csharp
using Shared.DecisionContractSDK.Domain;

namespace Shared.DecisionContractSDK.V2.Domain;

/// <summary>
/// Một tài nguyên (sân, phòng…) được đặt trong một khoảng thời gian. Bất biến phổ quát duy nhất về thời gian
/// là <c>End &gt; Start</c>; giới hạn thời lượng là chính sách của runtime (P-01).
/// </summary>
public sealed class ReservationItem
{
    /// <summary>Định danh tài nguyên (SmashCourt: <c>courtId</c>).</summary>
    public Guid ResourceId { get; }

    /// <summary>Định danh loại tài nguyên (SmashCourt: <c>courtTypeId</c>).</summary>
    public Guid ResourceTypeId { get; }

    /// <summary>Thời điểm bắt đầu (có offset).</summary>
    public DateTimeOffset Start { get; }

    /// <summary>Thời điểm kết thúc (có offset), luôn sau <see cref="Start"/>.</summary>
    public DateTimeOffset End { get; }

    /// <summary>Giá của item (SmashCourt: phí sân trước giảm giá), không âm.</summary>
    public decimal Price { get; }

    /// <summary>Thời lượng của item.</summary>
    public TimeSpan Duration => End - Start;

    /// <summary>Khởi tạo item và kiểm tra bất biến.</summary>
    /// <param name="resourceId">Định danh tài nguyên, khác rỗng.</param>
    /// <param name="resourceTypeId">Định danh loại tài nguyên, khác rỗng.</param>
    /// <param name="start">Thời điểm bắt đầu.</param>
    /// <param name="end">Thời điểm kết thúc, sau <paramref name="start"/>.</param>
    /// <param name="price">Giá, không âm.</param>
    /// <exception cref="DomainInvariantException">Khi vi phạm bất biến.</exception>
    public ReservationItem(Guid resourceId, Guid resourceTypeId, DateTimeOffset start, DateTimeOffset end, decimal price)
    {
        if (resourceId == Guid.Empty)
        {
            throw new DomainInvariantException("INVALID_RESOURCE_ID", "ResourceId must not be empty.", nameof(ResourceId));
        }

        if (resourceTypeId == Guid.Empty)
        {
            throw new DomainInvariantException("INVALID_RESOURCE_TYPE_ID", "ResourceTypeId must not be empty.", nameof(ResourceTypeId));
        }

        if (end <= start)
        {
            throw new DomainInvariantException(
                "INVALID_ITEM_RANGE",
                $"End ({end:O}) must be greater than Start ({start:O}).",
                nameof(End));
        }

        if (price < 0)
        {
            throw new DomainInvariantException("INVALID_ITEM_PRICE", "Price must be greater than or equal to 0.", nameof(Price));
        }

        ResourceId = resourceId;
        ResourceTypeId = resourceTypeId;
        Start = start;
        End = end;
        Price = price;
    }
}
```

Create `src/Shared.DecisionContractSDK/V2/Domain/CustomerHistory.cs`:

```csharp
using Shared.DecisionContractSDK.Domain;

namespace Shared.DecisionContractSDK.V2.Domain;

/// <summary>
/// Lịch sử đặt chỗ của khách, do client tính từ các booking đã kết thúc (INTEGRATION_SPEC §3.3).
/// </summary>
public sealed class CustomerHistory
{
    /// <summary>Số booking đã hoàn tất.</summary>
    public int CompletedCount { get; }

    /// <summary>Số lần không đến.</summary>
    public int NoShowCount { get; }

    /// <summary>Số lần khách tự hủy và được hoàn đủ theo chính sách hủy.</summary>
    public int CancelledCount { get; }

    /// <summary>Số lần khách tự hủy nhưng không được hoàn đủ (hủy muộn).</summary>
    public int LateCancelledCount { get; }

    /// <summary>Số booking bị hệ thống hủy do hết hạn thanh toán.</summary>
    public int PaymentExpiredCount { get; }

    /// <summary>Thời điểm hoàn tất gần nhất (nếu có).</summary>
    public DateTimeOffset? LastCompletedAt { get; }

    /// <summary>Khởi tạo lịch sử khách; mọi số đếm không âm.</summary>
    /// <param name="completedCount">Số booking đã hoàn tất.</param>
    /// <param name="noShowCount">Số lần không đến.</param>
    /// <param name="cancelledCount">Số lần tự hủy được hoàn đủ.</param>
    /// <param name="lateCancelledCount">Số lần hủy muộn.</param>
    /// <param name="paymentExpiredCount">Số lần hết hạn thanh toán.</param>
    /// <param name="lastCompletedAt">Thời điểm hoàn tất gần nhất.</param>
    /// <exception cref="DomainInvariantException">Khi có số đếm âm.</exception>
    public CustomerHistory(
        int completedCount,
        int noShowCount,
        int cancelledCount,
        int lateCancelledCount,
        int paymentExpiredCount,
        DateTimeOffset? lastCompletedAt)
    {
        if (completedCount < 0 || noShowCount < 0 || cancelledCount < 0 || lateCancelledCount < 0 || paymentExpiredCount < 0)
        {
            throw new DomainInvariantException(
                "INVALID_CUSTOMER_HISTORY",
                "History counts must be greater than or equal to 0.",
                nameof(CustomerHistory));
        }

        CompletedCount = completedCount;
        NoShowCount = noShowCount;
        CancelledCount = cancelledCount;
        LateCancelledCount = lateCancelledCount;
        PaymentExpiredCount = paymentExpiredCount;
        LastCompletedAt = lastCompletedAt;
    }
}
```

Create `src/Shared.DecisionContractSDK/V2/Domain/CustomerProfile.cs`:

```csharp
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.V2.Enums;

namespace Shared.DecisionContractSDK.V2.Domain;

/// <summary>
/// Hồ sơ khách tại thời điểm đánh giá. Khách vãng lai không có <see cref="CustomerId"/> và <see cref="MemberSince"/>;
/// khách thành viên bắt buộc có <see cref="CustomerId"/>.
/// </summary>
public sealed class CustomerProfile
{
    /// <summary>Khách vãng lai.</summary>
    public bool IsGuest { get; }

    /// <summary>Định danh khách; <c>null</c> khi <see cref="IsGuest"/>.</summary>
    public Guid? CustomerId { get; }

    /// <summary>Hạng thành viên chuẩn hóa.</summary>
    public CustomerTier Tier { get; }

    /// <summary>Thời điểm tạo tài khoản (thâm niên); <c>null</c> khi khách vãng lai hoặc client không gửi.</summary>
    public DateTimeOffset? MemberSince { get; }

    /// <summary>Lịch sử đặt chỗ (tùy chọn).</summary>
    public CustomerHistory? History { get; }

    /// <summary>Khởi tạo hồ sơ khách và kiểm tra bất biến.</summary>
    /// <param name="isGuest">Khách vãng lai.</param>
    /// <param name="customerId">Bắt buộc (khác rỗng) khi không phải khách vãng lai; <c>null</c> khi là khách vãng lai.</param>
    /// <param name="tier">Hạng thành viên.</param>
    /// <param name="memberSince">Thời điểm tạo tài khoản; phải <c>null</c> với khách vãng lai.</param>
    /// <param name="history">Lịch sử đặt chỗ (tùy chọn).</param>
    /// <exception cref="DomainInvariantException">Khi vi phạm bất biến.</exception>
    public CustomerProfile(bool isGuest, Guid? customerId, CustomerTier tier, DateTimeOffset? memberSince, CustomerHistory? history)
    {
        if (isGuest && customerId is not null)
        {
            throw new DomainInvariantException("GUEST_WITH_CUSTOMER_ID", "CustomerId must be null for a guest customer.", nameof(CustomerId));
        }

        if (isGuest && memberSince is not null)
        {
            throw new DomainInvariantException("GUEST_WITH_MEMBER_SINCE", "MemberSince must be null for a guest customer.", nameof(MemberSince));
        }

        if (!isGuest && (customerId is null || customerId == Guid.Empty))
        {
            throw new DomainInvariantException("MISSING_CUSTOMER_ID", "CustomerId is required for a registered customer.", nameof(CustomerId));
        }

        if (!Enum.IsDefined(tier))
        {
            throw new DomainInvariantException("INVALID_CUSTOMER_TIER", $"Tier '{tier}' is not defined.", nameof(Tier));
        }

        IsGuest = isGuest;
        CustomerId = customerId;
        Tier = tier;
        MemberSince = memberSince;
        History = history;
    }
}
```

Create `src/Shared.DecisionContractSDK/V2/Domain/DemandSnapshot.cs`:

```csharp
using Shared.DecisionContractSDK.Domain;

namespace Shared.DecisionContractSDK.V2.Domain;

/// <summary>
/// Ảnh chụp nhu cầu tại khung giờ của reservation, do client tính khi tạo booking (INTEGRATION_SPEC §3.3).
/// Không thuộc <c>request_hash</c> vì thay đổi giữa các lần retry.
/// </summary>
public sealed class DemandSnapshot
{
    /// <summary>Tỷ lệ tài nguyên hoạt động của chi nhánh đã có booking giao với khung giờ, trong <c>[0, 1]</c>.</summary>
    public decimal OccupancyRate { get; }

    /// <summary>Số người đăng ký chờ slot còn hạn.</summary>
    public int WaitlistCount { get; }

    /// <summary>Khởi tạo ảnh chụp nhu cầu.</summary>
    /// <param name="occupancyRate">Tỷ lệ kín trong <c>[0, 1]</c>.</param>
    /// <param name="waitlistCount">Số người chờ, không âm.</param>
    /// <exception cref="DomainInvariantException">Khi giá trị ngoài miền.</exception>
    public DemandSnapshot(decimal occupancyRate, int waitlistCount)
    {
        if (occupancyRate is < 0m or > 1m)
        {
            throw new DomainInvariantException("INVALID_OCCUPANCY_RATE", "OccupancyRate must be between 0 and 1.", nameof(OccupancyRate));
        }

        if (waitlistCount < 0)
        {
            throw new DomainInvariantException("INVALID_WAITLIST_COUNT", "WaitlistCount must be greater than or equal to 0.", nameof(WaitlistCount));
        }

        OccupancyRate = occupancyRate;
        WaitlistCount = waitlistCount;
    }
}
```

Create `src/Shared.DecisionContractSDK/V2/Domain/Reservation.cs`:

```csharp
using System.Text.RegularExpressions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.V2.Enums;

namespace Shared.DecisionContractSDK.V2.Domain;

/// <summary>
/// Aggregate Root của một yêu cầu đánh giá v2: một hoặc nhiều item, khách, giá, tiền tệ, kênh, nhu cầu và múi giờ
/// chi nhánh. Bất biến phổ quát được kiểm tra trong constructor (ADR-004); chính sách nghiệp vụ (thời lượng tối
/// thiểu/tối đa) thuộc runtime (P-01).
/// </summary>
public sealed class Reservation
{
    /// <summary>Số item tối đa của một reservation.</summary>
    public const int MaxItems = 20;

    private static readonly Regex CurrencyPattern = new("^[A-Z]{3}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Định danh đơn phía client (SmashCourt: <c>bookingId</c>).</summary>
    public Guid ReservationId { get; }

    /// <summary>Định danh chi nhánh.</summary>
    public Guid BranchId { get; }

    /// <summary>Múi giờ của chi nhánh; mọi quy tắc theo khung giờ tính theo múi giờ này.</summary>
    public TimeZoneInfo BranchTimeZone { get; }

    /// <summary>Các item (1..<see cref="MaxItems"/>), theo thứ tự client gửi.</summary>
    public IReadOnlyList<ReservationItem> Items { get; }

    /// <summary>Hồ sơ khách.</summary>
    public CustomerProfile Customer { get; }

    /// <summary>Tổng tiền sau giảm giá, không âm; mức cọc tính trên số này.</summary>
    public decimal TotalPrice { get; }

    /// <summary>Mã tiền tệ ISO 4217 (3 chữ cái in hoa).</summary>
    public string Currency { get; }

    /// <summary>Kênh tạo booking.</summary>
    public Channel Channel { get; }

    /// <summary>Thời điểm client gửi yêu cầu.</summary>
    public DateTimeOffset RequestedAt { get; }

    /// <summary>Ảnh chụp nhu cầu tại khung giờ (tùy chọn).</summary>
    public DemandSnapshot? Demand { get; }

    /// <summary>Metadata không ảnh hưởng quyết định.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>Thời điểm bắt đầu sớm nhất trong các item.</summary>
    public DateTimeOffset EarliestStart { get; }

    /// <summary>Khởi tạo reservation và kiểm tra bất biến.</summary>
    /// <param name="reservationId">Định danh đơn, khác rỗng.</param>
    /// <param name="branchId">Định danh chi nhánh, khác rỗng.</param>
    /// <param name="branchTimeZone">Múi giờ chi nhánh.</param>
    /// <param name="items">1..<see cref="MaxItems"/> item, không có phần tử null.</param>
    /// <param name="customer">Hồ sơ khách.</param>
    /// <param name="totalPrice">Tổng tiền, không âm.</param>
    /// <param name="currency">Mã ISO 4217 in hoa.</param>
    /// <param name="channel">Kênh tạo booking.</param>
    /// <param name="requestedAt">Thời điểm client gửi yêu cầu.</param>
    /// <param name="demand">Ảnh chụp nhu cầu (tùy chọn).</param>
    /// <param name="metadata">Metadata (tùy chọn).</param>
    /// <exception cref="DomainInvariantException">Khi vi phạm bất biến.</exception>
    public Reservation(
        Guid reservationId,
        Guid branchId,
        TimeZoneInfo branchTimeZone,
        IReadOnlyList<ReservationItem> items,
        CustomerProfile customer,
        decimal totalPrice,
        string currency,
        Channel channel,
        DateTimeOffset requestedAt,
        DemandSnapshot? demand = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(branchTimeZone);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(customer);

        if (reservationId == Guid.Empty)
        {
            throw new DomainInvariantException("INVALID_RESERVATION_ID", "ReservationId must not be empty.", nameof(ReservationId));
        }

        if (branchId == Guid.Empty)
        {
            throw new DomainInvariantException("INVALID_BRANCH_ID", "BranchId must not be empty.", nameof(BranchId));
        }

        if (items.Count is 0 or > MaxItems || items.Any(item => item is null))
        {
            throw new DomainInvariantException("INVALID_ITEM_COUNT", $"Items must contain 1..{MaxItems} non-null elements.", nameof(Items));
        }

        if (totalPrice < 0)
        {
            throw new DomainInvariantException("INVALID_TOTAL_PRICE", "TotalPrice must be greater than or equal to 0.", nameof(TotalPrice));
        }

        if (currency is null || !CurrencyPattern.IsMatch(currency))
        {
            throw new DomainInvariantException("INVALID_CURRENCY", $"Currency '{currency}' must be an ISO 4217 code in upper case.", nameof(Currency));
        }

        if (!Enum.IsDefined(channel))
        {
            throw new DomainInvariantException("INVALID_CHANNEL", $"Channel '{channel}' is not defined.", nameof(Channel));
        }

        ReservationId = reservationId;
        BranchId = branchId;
        BranchTimeZone = branchTimeZone;
        Items = items.ToArray();
        Customer = customer;
        TotalPrice = totalPrice;
        Currency = currency;
        Channel = channel;
        RequestedAt = requestedAt;
        Demand = demand;
        Metadata = metadata is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(metadata, StringComparer.Ordinal);
        EarliestStart = Items.Min(item => item.Start);
    }

    /// <summary>Quy một thời điểm về giờ địa phương của chi nhánh.</summary>
    /// <param name="instant">Thời điểm cần quy đổi.</param>
    public DateTimeOffset ToBranchLocal(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, BranchTimeZone);
}
```

Create `src/Shared.DecisionContractSDK/V2/Domain/PeakWindow.cs`:

```csharp
using Shared.DecisionContractSDK.Domain;

namespace Shared.DecisionContractSDK.V2.Domain;

/// <summary>
/// Một khung giờ cao điểm theo giờ địa phương của chi nhánh: <c>[Start, End)</c> trong ngày <see cref="Day"/>.
/// </summary>
public sealed class PeakWindow
{
    /// <summary>Ngày trong tuần.</summary>
    public DayOfWeek Day { get; }

    /// <summary>Giờ bắt đầu (tính cả).</summary>
    public TimeOnly Start { get; }

    /// <summary>Giờ kết thúc (không tính), sau <see cref="Start"/>.</summary>
    public TimeOnly End { get; }

    /// <summary>Khởi tạo khung giờ cao điểm.</summary>
    /// <param name="day">Ngày trong tuần.</param>
    /// <param name="start">Giờ bắt đầu.</param>
    /// <param name="end">Giờ kết thúc, sau <paramref name="start"/> trong cùng ngày.</param>
    /// <exception cref="DomainInvariantException">Khi ngày không hợp lệ hoặc <paramref name="end"/> không sau <paramref name="start"/>.</exception>
    public PeakWindow(DayOfWeek day, TimeOnly start, TimeOnly end)
    {
        if (!Enum.IsDefined(day) || end <= start)
        {
            throw new DomainInvariantException(
                "INVALID_PEAK_WINDOW",
                $"Peak window {day} {start:HH\\:mm}-{end:HH\\:mm} is invalid; End must be after Start on the same day.",
                nameof(PeakWindow));
        }

        Day = day;
        Start = start;
        End = end;
    }

    /// <summary>Thời điểm (đã quy về giờ chi nhánh) có nằm trong khung giờ này không.</summary>
    /// <param name="branchLocal">Thời điểm theo giờ địa phương của chi nhánh.</param>
    public bool Contains(DateTimeOffset branchLocal)
    {
        var time = TimeOnly.FromTimeSpan(branchLocal.TimeOfDay);
        return branchLocal.DayOfWeek == Day && time >= Start && time < End;
    }
}
```

Create `src/Shared.DecisionContractSDK/V2/Domain/BranchCalendar.cs`:

```csharp
namespace Shared.DecisionContractSDK.V2.Domain;

/// <summary>
/// Lịch cao điểm của chi nhánh — nơi duy nhất định nghĩa "giờ cao điểm", dùng chung cho router và strategy.
/// Giá trị mặc định do runtime cấu hình (<c>Tadss:Calendar:PeakWindows</c>, gói 5.2b).
/// </summary>
public sealed class BranchCalendar
{
    /// <summary>Các khung giờ cao điểm.</summary>
    public IReadOnlyList<PeakWindow> PeakWindows { get; }

    /// <summary>Khởi tạo lịch từ danh sách khung giờ cao điểm (có thể rỗng: không có giờ cao điểm).</summary>
    /// <param name="peakWindows">Các khung giờ cao điểm, không có phần tử null.</param>
    public BranchCalendar(IEnumerable<PeakWindow> peakWindows)
    {
        ArgumentNullException.ThrowIfNull(peakWindows);

        var windows = peakWindows.ToArray();
        if (windows.Any(window => window is null))
        {
            throw new ArgumentException("Peak windows must not contain null elements.", nameof(peakWindows));
        }

        PeakWindows = windows;
    }

    /// <summary>
    /// Reservation có rơi vào giờ cao điểm không: xét thời điểm bắt đầu sớm nhất, quy về giờ chi nhánh.
    /// </summary>
    /// <param name="reservation">Reservation cần xét.</param>
    public bool IsPeak(Reservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        var branchLocal = reservation.ToBranchLocal(reservation.EarliestStart);
        return PeakWindows.Any(window => window.Contains(branchLocal));
    }
}
```

- [ ] **Step 4: Build và chạy test V2**

Run: `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror && dotnet test tests/TADSS.UnitTests/TADSS.UnitTests.csproj -c Release --no-build -nologo --filter "FullyQualifiedName~TADSS.UnitTests.V2"`
Expected: build 0 warning; `Passed: 81` (19 của Task 2 + 62 mới).

- [ ] **Step 5: Cập nhật baseline**

Theo quy trình "Cập nhật baseline". Kỳ vọng thêm 7 kiểu `Shared.DecisionContractSDK.V2.Domain.*`.

- [ ] **Step 6: Chạy toàn bộ unit test**

Run: `dotnet test tests/TADSS.UnitTests/TADSS.UnitTests.csproj -c Release --no-build -nologo`
Expected: `Passed: 556`, `Failed: 0`.

- [ ] **Step 7: Commit (hỏi người dùng trước)**

```bash
git add src/Shared.DecisionContractSDK/V2/Domain tests/TADSS.UnitTests/V2 docs/contracts/baseline_public_api.txt
git commit -m "feat(sdk-v2): domain v2 — nhiều item, khách vãng lai, lịch sử, nhu cầu, lịch cao điểm theo chi nhánh"
```

---

### Task 4: V2 Decisions + interface strategy

**Files:**

- Create: `src/Shared.DecisionContractSDK/V2/Decisions/{DecisionRecommendation,StrategyDecision,DecisionContext,StrategyCachePolicy}.cs`
- Create: `src/Shared.DecisionContractSDK/V2/Interfaces/IReservationDecisionStrategy.cs`
- Create: `tests/TADSS.UnitTests/V2/Decisions/DecisionTypesTests.cs`
- Modify: `tests/TADSS.UnitTests/V2/TestData/V2TestData.cs` (thêm `CreateContext`)
- Modify: `docs/contracts/baseline_public_api.txt`

**Interfaces:**

- Consumes: `DecisionStatus`, `RecommendationType` (Task 2); `BranchCalendar`, `Reservation` (Task 3).
- Produces:
  - `DecisionRecommendation(RecommendationType type, string description)`.
  - `StrategyDecision.Approve(decimal depositRate, string reasonCode, IReadOnlyList<string> reasons)`, `StrategyDecision.RequireApproval(string reasonCode, IReadOnlyList<string> reasons, IReadOnlyList<DecisionRecommendation>? recommendations = null)`, `StrategyDecision.Reject(string reasonCode, IReadOnlyList<string> reasons)`; thuộc tính `Status`, `DepositRate` (`decimal?`), `ReasonCode`, `Reasons`, `Recommendations`; `const int MaxReasonCodeLength = 64`.
  - `DecisionContext(DateTimeOffset evaluatedAtUtc, BranchCalendar calendar)`.
  - `StrategyCachePolicy.Disabled`, `StrategyCachePolicy.Enabled(TimeSpan ttl)`; `IsEnabled`, `Ttl`.
  - `Shared.DecisionContractSDK.V2.Interfaces.IReservationDecisionStrategy`: `StrategyName`, `StrategyVersion`, `CachePolicy`, `Task<StrategyDecision> EvaluateAsync(Reservation reservation, DecisionContext context, CancellationToken cancellationToken)`.
  - `V2TestData.CreateContext(DateTimeOffset? evaluatedAtUtc = null, BranchCalendar? calendar = null)`.
- Mã bất biến: `INVALID_RECOMMENDATION_TYPE`, `INVALID_RECOMMENDATION_DESCRIPTION`, `INVALID_REASON_CODE`, `INVALID_DEPOSIT_RATE`.

- [ ] **Step 1: Viết test (thất bại)**

Thêm vào `tests/TADSS.UnitTests/V2/TestData/V2TestData.cs` — thêm `using Shared.DecisionContractSDK.V2.Decisions;` và method:

```csharp
    /// <summary>Ngữ cảnh đánh giá; mặc định 18/11/2026 02:15:00Z với lịch cao điểm chuẩn.</summary>
    public static DecisionContext CreateContext(DateTimeOffset? evaluatedAtUtc = null, BranchCalendar? calendar = null) =>
        new(evaluatedAtUtc ?? new DateTimeOffset(2026, 11, 18, 2, 15, 0, TimeSpan.Zero), calendar ?? StandardCalendar());
```

Create `tests/TADSS.UnitTests/V2/Decisions/DecisionTypesTests.cs`:

```csharp
using FluentAssertions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.V2.Decisions;
using Shared.DecisionContractSDK.V2.Enums;
using TADSS.UnitTests.V2.TestData;
using Xunit;

namespace TADSS.UnitTests.V2.Decisions;

/// <summary>
/// UT-01: bất biến của kiểu quyết định v2 — <see cref="StrategyDecision"/> chỉ tạo qua factory, tỷ lệ cọc trong
/// [0, 1], mã lý do 1..64 ký tự; <see cref="DecisionContext"/> ở UTC; <see cref="StrategyCachePolicy"/> TTL dương.
/// </summary>
public class DecisionTypesTests
{
    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void Approve_RateWithinBounds_CreatesApprovedDecision(double rate)
    {
        var decision = StrategyDecision.Approve((decimal)rate, "TIER_DEPOSIT", ["lý do"]);

        decision.Status.Should().Be(DecisionStatus.APPROVED);
        decision.DepositRate.Should().Be((decimal)rate);
        decision.Recommendations.Should().BeEmpty();
    }

    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Approve_RateOutOfBounds_ThrowsInvalidDepositRate(double rate)
    {
        var act = () => StrategyDecision.Approve((decimal)rate, "TIER_DEPOSIT", []);

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_DEPOSIT_RATE");
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void RequireApproval_Inputs_AreCopiedAndHaveNoDepositRate()
    {
        var reasons = new List<string> { "giá trị lớn" };
        var recommendations = new List<DecisionRecommendation> { new(RecommendationType.REQUIRE_APPROVAL, "Manager xem xét.") };

        var decision = StrategyDecision.RequireApproval("HIGH_VALUE_REVIEW", reasons, recommendations);
        reasons.Add("thêm sau");
        recommendations.Clear();

        decision.Status.Should().Be(DecisionStatus.NEED_APPROVAL);
        decision.DepositRate.Should().BeNull();
        decision.Reasons.Should().ContainSingle();
        decision.Recommendations.Should().ContainSingle();
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Reject_CreatesRejectedDecisionWithoutDepositRate()
    {
        var decision = StrategyDecision.Reject("POLICY_REJECT", ["không phục vụ"]);

        decision.Status.Should().Be(DecisionStatus.REJECTED);
        decision.DepositRate.Should().BeNull();
    }

    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData(0)]
    [InlineData(65)]
    public void Factory_ReasonCodeLengthOutOfRange_ThrowsInvalidReasonCode(int length)
    {
        var act = () => StrategyDecision.Reject(new string('A', length), []);

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_REASON_CODE");
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Factory_WhitespaceReasonCode_ThrowsInvalidReasonCode()
    {
        var act = () => StrategyDecision.RequireApproval("   ", []);

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_REASON_CODE");
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Factory_ReasonCodeAtMaxLength_IsAllowed()
    {
        var decision = StrategyDecision.Reject(new string('A', StrategyDecision.MaxReasonCodeLength), []);

        decision.ReasonCode.Should().HaveLength(64);
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Recommendation_EmptyDescription_ThrowsInvariant()
    {
        var act = () => new DecisionRecommendation(RecommendationType.ALTERNATIVE_SLOT, " ");

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_RECOMMENDATION_DESCRIPTION");
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Recommendation_UndefinedType_ThrowsInvariant()
    {
        var act = () => new DecisionRecommendation((RecommendationType)42, "mô tả");

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_RECOMMENDATION_TYPE");
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Context_NonUtcInstant_ThrowsArgumentException()
    {
        var act = () => new DecisionContext(V2TestData.FridayPeakStart, V2TestData.StandardCalendar());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Context_UtcInstant_ExposesValues()
    {
        var context = V2TestData.CreateContext();

        context.EvaluatedAtUtc.Offset.Should().Be(TimeSpan.Zero);
        context.Calendar.PeakWindows.Should().HaveCount(7);
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void CachePolicy_Disabled_HasNoTtl()
    {
        StrategyCachePolicy.Disabled.IsEnabled.Should().BeFalse();
        StrategyCachePolicy.Disabled.Ttl.Should().BeNull();
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void CachePolicy_EnabledWithPositiveTtl_ExposesTtl()
    {
        var policy = StrategyCachePolicy.Enabled(TimeSpan.FromMinutes(10));

        policy.IsEnabled.Should().BeTrue();
        policy.Ttl.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData(0)]
    [InlineData(-1)]
    public void CachePolicy_EnabledWithNonPositiveTtl_ThrowsArgumentOutOfRange(int minutes)
    {
        var act = () => StrategyCachePolicy.Enabled(TimeSpan.FromMinutes(minutes));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
```

- [ ] **Step 2: Chạy để xác nhận thất bại**

Run: `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror`
Expected: FAIL — `CS0234`/`CS0246` không tìm thấy `Shared.DecisionContractSDK.V2.Decisions`.

- [ ] **Step 3: Hiện thực**

Create `src/Shared.DecisionContractSDK/V2/Decisions/DecisionRecommendation.cs`:

```csharp
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.V2.Enums;

namespace Shared.DecisionContractSDK.V2.Decisions;

/// <summary>
/// Khuyến nghị kèm quyết định (ví dụ cần duyệt, gợi ý khung giờ khác).
/// </summary>
public sealed class DecisionRecommendation
{
    /// <summary>Loại khuyến nghị.</summary>
    public RecommendationType Type { get; }

    /// <summary>Mô tả cho người đọc.</summary>
    public string Description { get; }

    /// <summary>Khởi tạo khuyến nghị.</summary>
    /// <param name="type">Loại khuyến nghị đã định nghĩa.</param>
    /// <param name="description">Mô tả, không rỗng.</param>
    /// <exception cref="DomainInvariantException">Khi loại không hợp lệ hoặc mô tả rỗng.</exception>
    public DecisionRecommendation(RecommendationType type, string description)
    {
        if (!Enum.IsDefined(type))
        {
            throw new DomainInvariantException("INVALID_RECOMMENDATION_TYPE", $"Recommendation type '{type}' is not defined.", nameof(Type));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new DomainInvariantException("INVALID_RECOMMENDATION_DESCRIPTION", "Description must not be empty.", nameof(Description));
        }

        Type = type;
        Description = description;
    }
}
```

Create `src/Shared.DecisionContractSDK/V2/Decisions/StrategyDecision.cs`:

```csharp
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.V2.Enums;

namespace Shared.DecisionContractSDK.V2.Decisions;

/// <summary>
/// Quyết định nghiệp vụ do strategy trả về (D-03). Strategy chỉ quyết định <i>cái gì</i>; runtime lo định danh,
/// thời gian, quy đổi số tiền cọc, approval window và telemetry. Chỉ tạo qua <see cref="Approve"/>,
/// <see cref="RequireApproval"/>, <see cref="Reject"/>.
/// </summary>
public sealed class StrategyDecision
{
    /// <summary>Độ dài tối đa của mã lý do (cột <c>reason_code</c>).</summary>
    public const int MaxReasonCodeLength = 64;

    /// <summary>Trạng thái quyết định.</summary>
    public DecisionStatus Status { get; }

    /// <summary>Tỷ lệ cọc trong <c>[0, 1]</c>; chỉ có khi <see cref="DecisionStatus.APPROVED"/>.</summary>
    public decimal? DepositRate { get; }

    /// <summary>Mã lý do (xem <c>ReasonCodes</c>).</summary>
    public string ReasonCode { get; }

    /// <summary>Lý do cho người đọc.</summary>
    public IReadOnlyList<string> Reasons { get; }

    /// <summary>Khuyến nghị kèm theo.</summary>
    public IReadOnlyList<DecisionRecommendation> Recommendations { get; }

    private StrategyDecision(
        DecisionStatus status,
        decimal? depositRate,
        string reasonCode,
        IReadOnlyList<string> reasons,
        IReadOnlyList<DecisionRecommendation> recommendations)
    {
        if (string.IsNullOrWhiteSpace(reasonCode) || reasonCode.Length > MaxReasonCodeLength)
        {
            throw new DomainInvariantException("INVALID_REASON_CODE", $"ReasonCode must be 1..{MaxReasonCodeLength} characters.", nameof(ReasonCode));
        }

        ArgumentNullException.ThrowIfNull(reasons);
        ArgumentNullException.ThrowIfNull(recommendations);

        Status = status;
        DepositRate = depositRate;
        ReasonCode = reasonCode;
        Reasons = reasons.ToArray();
        Recommendations = recommendations.ToArray();
    }

    /// <summary>Duyệt với tỷ lệ cọc <paramref name="depositRate"/>.</summary>
    /// <param name="depositRate">Tỷ lệ cọc trong <c>[0, 1]</c>.</param>
    /// <param name="reasonCode">Mã lý do.</param>
    /// <param name="reasons">Lý do cho người đọc.</param>
    /// <exception cref="DomainInvariantException">Khi tỷ lệ ngoài <c>[0, 1]</c> hoặc mã lý do không hợp lệ.</exception>
    public static StrategyDecision Approve(decimal depositRate, string reasonCode, IReadOnlyList<string> reasons)
    {
        if (depositRate is < 0m or > 1m)
        {
            throw new DomainInvariantException("INVALID_DEPOSIT_RATE", $"DepositRate ({depositRate}) must be between 0 and 1.", nameof(DepositRate));
        }

        return new StrategyDecision(DecisionStatus.APPROVED, depositRate, reasonCode, reasons, []);
    }

    /// <summary>Yêu cầu Manager duyệt.</summary>
    /// <param name="reasonCode">Mã lý do.</param>
    /// <param name="reasons">Lý do cho người đọc.</param>
    /// <param name="recommendations">Khuyến nghị kèm theo (tùy chọn).</param>
    public static StrategyDecision RequireApproval(
        string reasonCode,
        IReadOnlyList<string> reasons,
        IReadOnlyList<DecisionRecommendation>? recommendations = null) =>
        new(DecisionStatus.NEED_APPROVAL, null, reasonCode, reasons, recommendations ?? []);

    /// <summary>Từ chối theo chính sách.</summary>
    /// <param name="reasonCode">Mã lý do.</param>
    /// <param name="reasons">Lý do cho người đọc.</param>
    public static StrategyDecision Reject(string reasonCode, IReadOnlyList<string> reasons) =>
        new(DecisionStatus.REJECTED, null, reasonCode, reasons, []);
}
```

Create `src/Shared.DecisionContractSDK/V2/Decisions/DecisionContext.cs`:

```csharp
using Shared.DecisionContractSDK.V2.Domain;

namespace Shared.DecisionContractSDK.V2.Decisions;

/// <summary>
/// Ngữ cảnh runtime cung cấp cho strategy. Strategy lấy thời điểm đánh giá từ đây, không đọc đồng hồ hệ thống,
/// để kết quả tất định và test được.
/// </summary>
public sealed class DecisionContext
{
    /// <summary>Thời điểm đánh giá (UTC, offset 0).</summary>
    public DateTimeOffset EvaluatedAtUtc { get; }

    /// <summary>Lịch cao điểm của chi nhánh.</summary>
    public BranchCalendar Calendar { get; }

    /// <summary>Khởi tạo ngữ cảnh.</summary>
    /// <param name="evaluatedAtUtc">Thời điểm đánh giá, offset phải bằng 0.</param>
    /// <param name="calendar">Lịch cao điểm của chi nhánh.</param>
    /// <exception cref="ArgumentException">Khi <paramref name="evaluatedAtUtc"/> không ở UTC.</exception>
    public DecisionContext(DateTimeOffset evaluatedAtUtc, BranchCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        if (evaluatedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("EvaluatedAtUtc must have a zero offset (UTC).", nameof(evaluatedAtUtc));
        }

        EvaluatedAtUtc = evaluatedAtUtc;
        Calendar = calendar;
    }
}
```

Create `src/Shared.DecisionContractSDK/V2/Decisions/StrategyCachePolicy.cs`:

```csharp
namespace Shared.DecisionContractSDK.V2.Decisions;

/// <summary>
/// Chính sách cache do strategy khai báo: runtime chỉ cache <see cref="StrategyDecision"/> khi strategy cho phép.
/// </summary>
public sealed class StrategyCachePolicy
{
    /// <summary>Không cache.</summary>
    public static StrategyCachePolicy Disabled { get; } = new(isEnabled: false, ttl: null);

    /// <summary>Cho phép cache.</summary>
    public bool IsEnabled { get; }

    /// <summary>Thời gian sống của mục cache; <c>null</c> khi không cache.</summary>
    public TimeSpan? Ttl { get; }

    private StrategyCachePolicy(bool isEnabled, TimeSpan? ttl)
    {
        IsEnabled = isEnabled;
        Ttl = ttl;
    }

    /// <summary>Cho phép cache với thời gian sống <paramref name="ttl"/>.</summary>
    /// <param name="ttl">Thời gian sống, lớn hơn 0.</param>
    /// <exception cref="ArgumentOutOfRangeException">Khi <paramref name="ttl"/> không dương.</exception>
    public static StrategyCachePolicy Enabled(TimeSpan ttl)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);
        return new StrategyCachePolicy(isEnabled: true, ttl);
    }
}
```

Create `src/Shared.DecisionContractSDK/V2/Interfaces/IReservationDecisionStrategy.cs`:

```csharp
using Shared.DecisionContractSDK.V2.Decisions;
using Shared.DecisionContractSDK.V2.Domain;

namespace Shared.DecisionContractSDK.V2.Interfaces;

/// <summary>
/// Hợp đồng strategy v2 (SOFTWARE_ARCHITECTURE_SPECIFICATION 4.1). Strategy phải stateless, thread-safe,
/// không I/O ngoài SDK và không đọc đồng hồ hệ thống.
/// </summary>
public interface IReservationDecisionStrategy
{
    /// <summary>Tên định danh trong registry (bằng tên class).</summary>
    string StrategyName { get; }

    /// <summary>Phiên bản SemVer.</summary>
    string StrategyVersion { get; }

    /// <summary>Chính sách cache của strategy.</summary>
    StrategyCachePolicy CachePolicy { get; }

    /// <summary>Đánh giá reservation và trả quyết định nghiệp vụ.</summary>
    /// <param name="reservation">Reservation đã qua kiểm tra bất biến.</param>
    /// <param name="context">Ngữ cảnh runtime (thời điểm đánh giá, lịch cao điểm).</param>
    /// <param name="cancellationToken">Token hủy.</param>
    Task<StrategyDecision> EvaluateAsync(Reservation reservation, DecisionContext context, CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Build và chạy test V2**

Run: `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror && dotnet test tests/TADSS.UnitTests/TADSS.UnitTests.csproj -c Release --no-build -nologo --filter "FullyQualifiedName~TADSS.UnitTests.V2"`
Expected: `Passed: 100` (81 + 19).

- [ ] **Step 5: Cập nhật baseline** — kỳ vọng thêm 4 kiểu `V2.Decisions.*` và `V2.Interfaces.IReservationDecisionStrategy`.

- [ ] **Step 6: Chạy toàn bộ unit test** — Expected: `Passed: 575`, `Failed: 0`.

- [ ] **Step 7: Commit (hỏi người dùng trước)**

```bash
git add src/Shared.DecisionContractSDK/V2 tests/TADSS.UnitTests/V2 docs/contracts/baseline_public_api.txt
git commit -m "feat(sdk-v2): StrategyDecision, DecisionContext, StrategyCachePolicy và interface strategy v2"
```

---

### Task 5: V2 DTO wire, mapping và JSON schema v2

**Files:**

- Create: `src/Shared.DecisionContractSDK/V2/Common/OffsetDateTimeConverter.cs`
- Create: `src/Shared.DecisionContractSDK/V2/DTOs/Decisions/{DecisionRequestDto,DecisionResultDto,DecisionDetailDto}.cs`
- Create: `src/Shared.DecisionContractSDK/V2/DTOs/Governance/{OverrideRequestDto,OverrideResponseDto,PendingApprovalItemDto}.cs`
- Create: `src/Shared.DecisionContractSDK/V2/DTOs/Outcomes/{OutcomeRequestDto,OutcomeResponseDto}.cs`
- Create: `src/Shared.DecisionContractSDK/V2/DTOs/Mappings/DecisionRequestMapping.cs`
- Create: `docs/contracts/schemas/v2/decision-request.schema.json`, `docs/contracts/schemas/v2/decision-result.schema.json`
- Modify: `Directory.Packages.props`, `tests/TADSS.UnitTests/TADSS.UnitTests.csproj`
- Create: `tests/TADSS.UnitTests/V2/TestData/{V2Samples,RepositoryPaths,ContractSchemas}.cs`
- Create: `tests/TADSS.UnitTests/V2/Contracts/{DecisionWireContractTests,DecisionRequestMappingTests,DecisionSchemaTests}.cs`
- Modify: `docs/contracts/baseline_public_api.txt`

**Interfaces:**

- Consumes: Task 2–4.
- Produces:
  - DTO (namespace `Shared.DecisionContractSDK.V2.DTOs.Decisions`): `DecisionRequestDto`, `ReservationItemDto`, `CustomerDto`, `CustomerHistoryDto`, `DemandDto`, `DecisionResultDto`, `DepositDto`, `DecisionRecommendationDto`, `DecisionDetailDto`, `DecisionDetailResultDto`, `ExecutionDetailDto` — thuộc tính đúng tên trường wire ở INTEGRATION_SPEC §3.3–3.4 (PascalCase; camelCase do `TadssJson`).
  - `...DTOs.Governance`: `OverrideRequestDto`, `OverrideDepositDto`, `OverrideResponseDto`, `PendingApprovalItemDto`, `PendingApprovalCustomerDto`. `...DTOs.Outcomes`: `OutcomeRequestDto`, `OutcomeResponseDto`.
  - `DecisionRequestMapping.ToDomain(this DecisionRequestDto request)` → `Reservation`; múi giờ không phải IANA → `ContractValidationException` mã `INVALID_TIME_ZONE`.
  - `internal OffsetDateTimeConverter`.
  - Schema `docs/contracts/schemas/v2/decision-request.schema.json`, `decision-result.schema.json` (JSON Schema 2020-12).
  - Test helper: `V2Samples.RequestJson`, `V2Samples.ResultJson`, `V2Samples.MutateRequest(Action<JsonObject>)`, `V2Samples.MutateResult(Action<JsonObject>)`, `V2Samples.DeserializeRequest(string)`, `V2Samples.Object(JsonNode?)`, `V2Samples.Required<T>(T?)`; `RepositoryPaths.Resolve(params string[])`; `ContractSchemas.Evaluate(string schemaFile, string json)`, `ContractSchemas.Describe(EvaluationResults)`.

- [ ] **Step 1: Thêm package test**

Trong `Directory.Packages.props`, nhóm test, thêm sau `Testcontainers.Toxiproxy`:

```xml
    <PackageVersion Include="JsonSchema.Net" Version="9.4.0" />
```

Trong `tests/TADSS.UnitTests/TADSS.UnitTests.csproj`, thêm `<PackageReference Include="JsonSchema.Net" />` vào `ItemGroup` package.

- [ ] **Step 2: Viết test helper và test (thất bại)**

Create `tests/TADSS.UnitTests/V2/TestData/RepositoryPaths.cs`:

```csharp
namespace TADSS.UnitTests.V2.TestData;

/// <summary>Tìm đường dẫn trong repo bằng cách đi ngược từ thư mục chạy test tới thư mục chứa <c>TADSS.slnx</c>.</summary>
internal static class RepositoryPaths
{
    public static string Resolve(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TADSS.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Không tìm thấy thư mục gốc chứa TADSS.slnx.");
        }

        return Path.Combine([directory.FullName, .. relativeParts]);
    }
}
```

Create `tests/TADSS.UnitTests/V2/TestData/V2Samples.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using Shared.DecisionContractSDK.V2.Common;
using Shared.DecisionContractSDK.V2.DTOs.Decisions;

namespace TADSS.UnitTests.V2.TestData;

/// <summary>Payload mẫu của INTEGRATION_SPEC §3.3 và các hàm biến đổi để dựng ca sai.</summary>
internal static class V2Samples
{
    public const string RequestJson = """
        {
          "reservationId": "5b0a6f0e-7c0d-4a0e-9a64-0c2e7f1b9a11",
          "branchId": "1f2e3d4c-0000-4000-8000-000000000001",
          "branchTimeZone": "Asia/Ho_Chi_Minh",
          "items": [
            {
              "resourceId": "c0a8012e-0000-4000-8000-0000000000aa",
              "resourceTypeId": "c0a8012e-0000-4000-8000-0000000000bb",
              "startTime": "2026-11-20T18:00:00+07:00",
              "endTime": "2026-11-20T20:00:00+07:00",
              "price": 240000
            }
          ],
          "customer": {
            "customerId": "9d3f1c2a-0000-4000-8000-000000000123",
            "isGuest": false,
            "tier": "GOLD",
            "memberSince": "2025-03-14T08:00:00+07:00",
            "history": {
              "completedCount": 12,
              "noShowCount": 1,
              "cancelledCount": 2,
              "lateCancelledCount": 1,
              "paymentExpiredCount": 0,
              "lastCompletedAt": "2026-11-02T20:00:00+07:00"
            }
          },
          "totalPrice": 228000,
          "currency": "VND",
          "channel": "ONLINE",
          "demand": { "occupancyRate": 0.75, "waitlistCount": 2 },
          "requestedAt": "2026-11-18T09:15:00+07:00",
          "metadata": { "promotionCode": "WINTER" }
        }
        """;

    public const string ResultJson = """
        {
          "decisionId": "0f9c7e5a-0000-4000-8000-00000000d001",
          "reservationId": "5b0a6f0e-7c0d-4a0e-9a64-0c2e7f1b9a11",
          "status": "APPROVED",
          "deposit": { "rate": 0.5, "amount": 114000, "currency": "VND" },
          "strategyName": "MockAlternativeDecisionStrategy",
          "strategyVersion": "2.0.0",
          "executionType": "AUTOMATED",
          "fallbackUsed": false,
          "reasonCode": "PEAK_HOUR_DEPOSIT",
          "reasons": ["Khung giờ cao điểm (18:00 thứ Sáu, giờ chi nhánh) áp dụng cọc 50%."],
          "recommendations": [],
          "approvalExpiresAt": null,
          "evaluatedAt": "2026-11-18T02:15:00.041Z"
        }
        """;

    public static string MutateRequest(Action<JsonObject> mutate) => Mutate(RequestJson, mutate);

    public static string MutateResult(Action<JsonObject> mutate) => Mutate(ResultJson, mutate);

    public static DecisionRequestDto DeserializeRequest(string json) =>
        JsonSerializer.Deserialize<DecisionRequestDto>(json, TadssJson.Options)
            ?? throw new InvalidOperationException("Deserialize trả null.");

    /// <summary>Bỏ null có kiểm tra: ném lỗi rõ ràng thay vì để assertion '?.' bị bỏ qua âm thầm.</summary>
    public static T Required<T>(T? value)
        where T : class =>
        value ?? throw new InvalidOperationException($"Giá trị {typeof(T).Name} bắt buộc nhưng là null.");

    /// <summary>Ép một nút JSON về object (ném lỗi rõ ràng nếu không phải object).</summary>
    public static JsonObject Object(JsonNode? node) =>
        node?.AsObject() ?? throw new InvalidOperationException("Nút JSON không phải object.");

    private static string Mutate(string json, Action<JsonObject> mutate)
    {
        var root = Object(JsonNode.Parse(json));
        mutate(root);
        return root.ToJsonString();
    }
}
```

Create `tests/TADSS.UnitTests/V2/TestData/ContractSchemas.cs`:

```csharp
using System.Collections.Concurrent;
using System.Text.Json;
using Json.Schema;

namespace TADSS.UnitTests.V2.TestData;

/// <summary>Nạp JSON schema v2 từ <c>docs/contracts/schemas/v2/</c> (một lần mỗi file) và đánh giá payload.</summary>
internal static class ContractSchemas
{
    private static readonly ConcurrentDictionary<string, JsonSchema> Cache = new(StringComparer.Ordinal);

    public static EvaluationResults Evaluate(string schemaFile, string json)
    {
        var schema = Cache.GetOrAdd(schemaFile, file =>
            JsonSchema.FromText(File.ReadAllText(RepositoryPaths.Resolve("docs", "contracts", "schemas", "v2", file))));
        var instance = JsonSerializer.Deserialize<JsonElement>(json);

        return schema.Evaluate(instance, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
            RequireFormatValidation = true
        });
    }

    /// <summary>Mô tả lỗi để hiển thị khi assertion thất bại.</summary>
    public static string Describe(EvaluationResults results) =>
        string.Join("; ", (results.Details ?? [])
            .Where(detail => detail.HasErrors)
            .SelectMany(detail => (detail.Errors ?? new Dictionary<string, string>())
                .Select(error => $"{detail.InstanceLocation}: {error.Value}")));
}
```

> Nếu kiểu `EvaluationResults` của JsonSchema.Net 9.4.0 có tên thành viên khác (`Details`, `HasErrors`, `Errors`, `InstanceLocation`), chỉ sửa thân `Describe` cho khớp API — `Describe` chỉ tạo thông báo, không ảnh hưởng assertion.

Create `tests/TADSS.UnitTests/V2/Contracts/DecisionWireContractTests.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Shared.DecisionContractSDK.V2.Common;
using Shared.DecisionContractSDK.V2.DTOs.Decisions;
using Shared.DecisionContractSDK.V2.Enums;
using TADSS.UnitTests.V2.TestData;
using Xunit;

namespace TADSS.UnitTests.V2.Contracts;

/// <summary>
/// UT-02: serialization contract v2 — round-trip không mất dữ liệu; trường lạ, thiếu trường bắt buộc, enum lạ/số,
/// thời gian thiếu offset bị từ chối; trường <c>deposit</c>/<c>approvalExpiresAt</c> luôn có mặt trên wire.
/// </summary>
public class DecisionWireContractTests
{
    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void Request_IntegrationSpecSample_RoundTripsWithoutLoss()
    {
        var dto = V2Samples.DeserializeRequest(V2Samples.RequestJson);

        var roundTrip = JsonSerializer.Serialize(dto, TadssJson.Options);

        JsonNode.DeepEquals(JsonNode.Parse(V2Samples.RequestJson), JsonNode.Parse(roundTrip)).Should().BeTrue(roundTrip);
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void Request_IntegrationSpecSample_DeserializesAllFields()
    {
        var dto = V2Samples.DeserializeRequest(V2Samples.RequestJson);

        dto.Items.Should().ContainSingle();
        dto.Items[0].StartTime.Should().Be(new DateTimeOffset(2026, 11, 20, 18, 0, 0, TimeSpan.FromHours(7)));
        dto.Customer.Tier.Should().Be(CustomerTier.GOLD);
        V2Samples.Required(dto.Customer.History).LateCancelledCount.Should().Be(1);
        V2Samples.Required(dto.Demand).OccupancyRate.Should().Be(0.75m);
        dto.Channel.Should().Be(Channel.ONLINE);
        dto.Metadata.Should().ContainKey("promotionCode");
    }

    [Theory]
    [Trait("CatalogId", "UT-02")]
    [InlineData("extra-root")]
    [InlineData("extra-nested")]
    [InlineData("channel-number")]
    [InlineData("channel-numeric-string")]
    [InlineData("channel-lowercase")]
    [InlineData("channel-unknown")]
    [InlineData("tier-unknown")]
    [InlineData("missing-currency")]
    [InlineData("start-without-offset")]
    [InlineData("price-as-string")]
    public void Request_InvalidWire_ThrowsJsonException(string caseName)
    {
        var json = V2Samples.MutateRequest(root =>
        {
            var item = V2Samples.Object(root["items"]?[0]);
            var customer = V2Samples.Object(root["customer"]);
            switch (caseName)
            {
                case "extra-root": root["unexpected"] = 1; break;
                case "extra-nested": item["unexpected"] = 1; break;
                case "channel-number": root["channel"] = 1; break;
                case "channel-numeric-string": root["channel"] = "1"; break;
                case "channel-lowercase": root["channel"] = "online"; break;
                case "channel-unknown": root["channel"] = "PHONE"; break;
                case "tier-unknown": customer["tier"] = "VIP"; break;
                case "missing-currency": root.Remove("currency"); break;
                case "start-without-offset": item["startTime"] = "2026-11-20T18:00:00"; break;
                case "price-as-string": root["totalPrice"] = "228000"; break;
                default: throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null);
            }
        });

        var act = () => V2Samples.DeserializeRequest(json);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void Request_OptionalSignalsOmitted_DeserializesWithDefaults()
    {
        var json = V2Samples.MutateRequest(root =>
        {
            root.Remove("demand");
            root.Remove("metadata");
            var customer = V2Samples.Object(root["customer"]);
            customer.Remove("memberSince");
            var history = V2Samples.Object(customer["history"]);
            history.Remove("lateCancelledCount");
            history.Remove("paymentExpiredCount");
            history.Remove("lastCompletedAt");
        });

        var dto = V2Samples.DeserializeRequest(json);

        dto.Demand.Should().BeNull();
        dto.Metadata.Should().BeNull();
        dto.Customer.MemberSince.Should().BeNull();
        V2Samples.Required(dto.Customer.History).LateCancelledCount.Should().Be(0);
        V2Samples.Required(dto.Customer.History).PaymentExpiredCount.Should().Be(0);
    }

    [Theory]
    [Trait("CatalogId", "UT-02")]
    [InlineData("2026-11-18T09:15:00+07:00")]
    [InlineData("2026-11-18T02:15:00Z")]
    [InlineData("2026-11-18T02:15:00.041Z")]
    public void Request_RequestedAtWithOffset_IsAccepted(string requestedAt)
    {
        var json = V2Samples.MutateRequest(root => root["requestedAt"] = requestedAt);

        V2Samples.DeserializeRequest(json).RequestedAt.UtcDateTime.Date.Should().Be(new DateTime(2026, 11, 18));
    }

    [Theory]
    [Trait("CatalogId", "UT-02")]
    [InlineData("2026-11-18T09:15:00")]
    [InlineData("2026-11-18")]
    [InlineData("18/11/2026 09:15")]
    [InlineData("2026-11-18T09:15:00+0700")]
    public void Request_RequestedAtWithoutExplicitOffset_ThrowsJsonException(string requestedAt)
    {
        var json = V2Samples.MutateRequest(root => root["requestedAt"] = requestedAt);

        var act = () => V2Samples.DeserializeRequest(json);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void Result_IntegrationSpecSample_Deserializes()
    {
        var result = JsonSerializer.Deserialize<DecisionResultDto>(V2Samples.ResultJson, TadssJson.Options)
            ?? throw new InvalidOperationException("Deserialize trả null.");

        result.Status.Should().Be(DecisionStatus.APPROVED);
        V2Samples.Required(result.Deposit).Amount.Should().Be(114_000m);
        result.ApprovalExpiresAt.Should().BeNull();
        result.EvaluatedAt.Should().Be(new DateTimeOffset(2026, 11, 18, 2, 15, 0, 41, TimeSpan.Zero));
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void Result_NeedApproval_SerializesNullDepositAndExpiry()
    {
        var result = new DecisionResultDto
        {
            DecisionId = Guid.NewGuid(),
            ReservationId = Guid.NewGuid(),
            Status = DecisionStatus.NEED_APPROVAL,
            Deposit = null,
            StrategyName = "DefaultDecisionStrategy",
            StrategyVersion = "2.0.0",
            ExecutionType = ExecutionType.AUTOMATED,
            FallbackUsed = false,
            ReasonCode = ReasonCodes.HighValueReview,
            Reasons = ["Giá trị lớn."],
            Recommendations = [new DecisionRecommendationDto { Type = RecommendationType.REQUIRE_APPROVAL, Description = "Manager xem xét." }],
            ApprovalExpiresAt = new DateTimeOffset(2026, 11, 18, 2, 45, 0, TimeSpan.Zero),
            EvaluatedAt = new DateTimeOffset(2026, 11, 18, 2, 15, 0, TimeSpan.Zero)
        };

        var root = V2Samples.Object(JsonNode.Parse(JsonSerializer.Serialize(result, TadssJson.Options)));

        root.ContainsKey("deposit").Should().BeTrue();
        root["deposit"].Should().BeNull();
        V2Samples.Required(root["approvalExpiresAt"]).GetValue<string>().Should().Be("2026-11-18T02:45:00+00:00");
        V2Samples.Required(root["status"]).GetValue<string>().Should().Be("NEED_APPROVAL");
    }
}
```

Create `tests/TADSS.UnitTests/V2/Contracts/DecisionRequestMappingTests.cs`:

```csharp
using FluentAssertions;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.V2.DTOs.Mappings;
using Shared.DecisionContractSDK.V2.Enums;
using TADSS.UnitTests.V2.TestData;
using Xunit;

namespace TADSS.UnitTests.V2.Contracts;

/// <summary>
/// UT-02: ánh xạ <c>DecisionRequestDto</c> → <c>Reservation</c> — múi giờ phải là IANA (lỗi định dạng →
/// <see cref="ContractValidationException"/>), vi phạm bất biến → <see cref="DomainInvariantException"/>.
/// </summary>
public class DecisionRequestMappingTests
{
    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void ToDomain_IntegrationSpecSample_MapsAllFields()
    {
        var reservation = V2Samples.DeserializeRequest(V2Samples.RequestJson).ToDomain();

        reservation.BranchTimeZone.BaseUtcOffset.Should().Be(TimeSpan.FromHours(7));
        reservation.Items.Should().ContainSingle();
        reservation.EarliestStart.Should().Be(new DateTimeOffset(2026, 11, 20, 18, 0, 0, TimeSpan.FromHours(7)));
        reservation.Customer.Tier.Should().Be(CustomerTier.GOLD);
        reservation.Customer.MemberSince.Should().Be(new DateTimeOffset(2025, 3, 14, 8, 0, 0, TimeSpan.FromHours(7)));
        V2Samples.Required(reservation.Customer.History).LateCancelledCount.Should().Be(1);
        V2Samples.Required(reservation.Demand).WaitlistCount.Should().Be(2);
        reservation.Metadata["promotionCode"].Should().Be("WINTER");
    }

    [Theory]
    [Trait("CatalogId", "UT-02")]
    [InlineData("SE Asia Standard Time")]
    [InlineData("Mars/Base")]
    [InlineData("")]
    [InlineData(" ")]
    public void ToDomain_NonIanaTimeZone_ThrowsContractValidation(string timeZone)
    {
        var dto = V2Samples.DeserializeRequest(V2Samples.MutateRequest(root => root["branchTimeZone"] = timeZone));

        var act = () => dto.ToDomain();

        act.Should().Throw<ContractValidationException>().Which.ErrorCode.Should().Be("INVALID_TIME_ZONE");
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void ToDomain_GuestWithoutIdentity_MapsGuest()
    {
        var dto = V2Samples.DeserializeRequest(V2Samples.MutateRequest(root =>
        {
            var customer = V2Samples.Object(root["customer"]);
            customer.Remove("customerId");
            customer.Remove("memberSince");
            customer["isGuest"] = true;
            customer["tier"] = "STANDARD";
        }));

        var reservation = dto.ToDomain();

        reservation.Customer.IsGuest.Should().BeTrue();
        reservation.Customer.CustomerId.Should().BeNull();
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void ToDomain_GuestWithCustomerId_ThrowsDomainInvariant()
    {
        var dto = V2Samples.DeserializeRequest(V2Samples.MutateRequest(root =>
        {
            var customer = V2Samples.Object(root["customer"]);
            customer.Remove("memberSince");
            customer["isGuest"] = true;
        }));

        var act = () => dto.ToDomain();

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("GUEST_WITH_CUSTOMER_ID");
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void ToDomain_EndBeforeStart_ThrowsDomainInvariant()
    {
        var dto = V2Samples.DeserializeRequest(V2Samples.MutateRequest(root =>
            V2Samples.Object(root["items"]?[0])["endTime"] = "2026-11-20T17:00:00+07:00"));

        var act = () => dto.ToDomain();

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_ITEM_RANGE");
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void ToDomain_OptionalSignalsOmitted_UsesDefaults()
    {
        var dto = V2Samples.DeserializeRequest(V2Samples.MutateRequest(root =>
        {
            root.Remove("demand");
            var history = V2Samples.Object(V2Samples.Object(root["customer"])["history"]);
            history.Remove("lateCancelledCount");
            history.Remove("paymentExpiredCount");
        }));

        var reservation = dto.ToDomain();

        reservation.Demand.Should().BeNull();
        V2Samples.Required(reservation.Customer.History).LateCancelledCount.Should().Be(0);
        V2Samples.Required(reservation.Customer.History).PaymentExpiredCount.Should().Be(0);
    }
}
```

Create `tests/TADSS.UnitTests/V2/Contracts/DecisionSchemaTests.cs`:

```csharp
using System.Text.Json;
using FluentAssertions;
using Shared.DecisionContractSDK.V2.Common;
using Shared.DecisionContractSDK.V2.DTOs.Decisions;
using Shared.DecisionContractSDK.V2.Enums;
using TADSS.UnitTests.V2.TestData;
using Xunit;

namespace TADSS.UnitTests.V2.Contracts;

/// <summary>
/// CT-01 (phần schema): JSON schema v2 ở <c>docs/contracts/schemas/v2/</c> chấp nhận payload mẫu và payload do SDK
/// serialize, từ chối payload sai (trường lạ, thời gian thiếu offset, enum sai, trạng thái và cọc/hạn duyệt không khớp).
/// </summary>
public class DecisionSchemaTests
{
    private const string RequestSchema = "decision-request.schema.json";
    private const string ResultSchema = "decision-result.schema.json";

    [Fact]
    [Trait("CatalogId", "CT-01")]
    public void RequestSchema_IntegrationSpecSample_IsValid()
    {
        var results = ContractSchemas.Evaluate(RequestSchema, V2Samples.RequestJson);

        results.IsValid.Should().BeTrue(ContractSchemas.Describe(results));
    }

    [Fact]
    [Trait("CatalogId", "CT-01")]
    public void RequestSchema_SdkSerializedGuestRequest_IsValid()
    {
        var guestJson = V2Samples.MutateRequest(root =>
        {
            var customer = V2Samples.Object(root["customer"]);
            customer.Remove("customerId");
            customer.Remove("memberSince");
            customer["isGuest"] = true;
            customer["tier"] = "STANDARD";
            root.Remove("demand");
        });
        var serialized = JsonSerializer.Serialize(V2Samples.DeserializeRequest(guestJson), TadssJson.Options);

        var results = ContractSchemas.Evaluate(RequestSchema, serialized);

        results.IsValid.Should().BeTrue(ContractSchemas.Describe(results));
    }

    [Fact]
    [Trait("CatalogId", "CT-01")]
    public void ResultSchema_IntegrationSpecSample_IsValid()
    {
        var results = ContractSchemas.Evaluate(ResultSchema, V2Samples.ResultJson);

        results.IsValid.Should().BeTrue(ContractSchemas.Describe(results));
    }

    [Fact]
    [Trait("CatalogId", "CT-01")]
    public void ResultSchema_SdkSerializedNeedApproval_IsValid()
    {
        var result = new DecisionResultDto
        {
            DecisionId = Guid.NewGuid(),
            ReservationId = Guid.NewGuid(),
            Status = DecisionStatus.NEED_APPROVAL,
            Deposit = null,
            StrategyName = "DefaultDecisionStrategy",
            StrategyVersion = "2.0.0",
            ExecutionType = ExecutionType.FALLBACK,
            FallbackUsed = true,
            ReasonCode = ReasonCodes.FallbackStrategyTimeout,
            Reasons = [],
            Recommendations = [],
            ApprovalExpiresAt = new DateTimeOffset(2026, 11, 18, 2, 45, 0, TimeSpan.Zero),
            EvaluatedAt = new DateTimeOffset(2026, 11, 18, 2, 15, 0, TimeSpan.Zero)
        };

        var results = ContractSchemas.Evaluate(ResultSchema, JsonSerializer.Serialize(result, TadssJson.Options));

        results.IsValid.Should().BeTrue(ContractSchemas.Describe(results));
    }

    [Theory]
    [Trait("CatalogId", "CT-01")]
    [InlineData("extra-root")]
    [InlineData("start-without-offset")]
    [InlineData("channel-lowercase")]
    [InlineData("member-without-customer-id")]
    [InlineData("occupancy-above-one")]
    [InlineData("items-empty")]
    public void RequestSchema_InvalidPayload_IsRejected(string caseName)
    {
        var json = V2Samples.MutateRequest(root =>
        {
            switch (caseName)
            {
                case "extra-root": root["unexpected"] = 1; break;
                case "start-without-offset": V2Samples.Object(root["items"]?[0])["startTime"] = "2026-11-20T18:00:00"; break;
                case "channel-lowercase": root["channel"] = "online"; break;
                case "member-without-customer-id": V2Samples.Object(root["customer"]).Remove("customerId"); break;
                case "occupancy-above-one": V2Samples.Object(root["demand"])["occupancyRate"] = 1.5; break;
                case "items-empty": root["items"] = new System.Text.Json.Nodes.JsonArray(); break;
                default: throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null);
            }
        });

        ContractSchemas.Evaluate(RequestSchema, json).IsValid.Should().BeFalse();
    }

    [Theory]
    [Trait("CatalogId", "CT-01")]
    [InlineData("approved-without-deposit")]
    [InlineData("need-approval-without-expiry")]
    [InlineData("rejected-with-deposit")]
    public void ResultSchema_StatusInconsistentWithDepositOrExpiry_IsRejected(string caseName)
    {
        var json = V2Samples.MutateResult(root =>
        {
            switch (caseName)
            {
                case "approved-without-deposit": root["deposit"] = null; break;
                case "need-approval-without-expiry": root["status"] = "NEED_APPROVAL"; root["deposit"] = null; break;
                case "rejected-with-deposit": root["status"] = "REJECTED"; break;
                default: throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null);
            }
        });

        ContractSchemas.Evaluate(ResultSchema, json).IsValid.Should().BeFalse();
    }
}
```

- [ ] **Step 3: Chạy để xác nhận thất bại**

Run: `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror`
Expected: FAIL — `CS0234` không tìm thấy `Shared.DecisionContractSDK.V2.DTOs`.

- [ ] **Step 4: Hiện thực converter thời gian**

Create `src/Shared.DecisionContractSDK/V2/Common/OffsetDateTimeConverter.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Shared.DecisionContractSDK.V2.Common;

/// <summary>
/// Converter thời gian của contract v2: chỉ nhận ISO 8601 có offset tường minh (<c>Z</c> hoặc <c>±hh:mm</c>).
/// Thời gian thiếu offset bị từ chối thay vì để .NET gán múi giờ của máy chủ (INV-3).
/// Gắn trên thuộc tính <see cref="DateTimeOffset"/> hoặc <see cref="Nullable{DateTimeOffset}"/>.
/// </summary>
internal sealed class OffsetDateTimeConverter : JsonConverter<DateTimeOffset>
{
    private static readonly Regex OffsetPattern = new(
        @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Thời gian phải là chuỗi ISO 8601 có offset.");
        }

        var text = reader.GetString();
        if (text is null
            || !OffsetPattern.IsMatch(text)
            || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
        {
            throw new JsonException($"'{text}' không phải thời gian ISO 8601 có offset (ví dụ 2026-11-20T18:00:00+07:00).");
        }

        return value;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
```

- [ ] **Step 5: Hiện thực DTO Decisions**

Create `src/Shared.DecisionContractSDK/V2/DTOs/Decisions/DecisionRequestDto.cs`:

```csharp
using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.V2.Common;
using Shared.DecisionContractSDK.V2.Enums;

namespace Shared.DecisionContractSDK.V2.DTOs.Decisions;

/// <summary>
/// Request <c>POST /api/v2/decisions</c> (INTEGRATION_SPEC §3.3). Thiếu trường bắt buộc, trường lạ, enum lạ
/// hoặc thời gian thiếu offset làm deserialize thất bại (400 <c>VALIDATION_ERROR</c>).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class DecisionRequestDto
{
    /// <summary>Định danh đơn phía client.</summary>
    public required Guid ReservationId { get; init; }

    /// <summary>Định danh chi nhánh.</summary>
    public required Guid BranchId { get; init; }

    /// <summary>Mã múi giờ IANA của chi nhánh.</summary>
    public required string BranchTimeZone { get; init; }

    /// <summary>Các item (1..20).</summary>
    public required IReadOnlyList<ReservationItemDto> Items { get; init; }

    /// <summary>Thông tin khách.</summary>
    public required CustomerDto Customer { get; init; }

    /// <summary>Tổng tiền sau giảm giá.</summary>
    public required decimal TotalPrice { get; init; }

    /// <summary>Mã tiền tệ ISO 4217.</summary>
    public required string Currency { get; init; }

    /// <summary>Kênh tạo booking.</summary>
    public required Channel Channel { get; init; }

    /// <summary>Ảnh chụp nhu cầu tại khung giờ (tùy chọn; không thuộc <c>request_hash</c>).</summary>
    public DemandDto? Demand { get; init; }

    /// <summary>Thời điểm client gửi yêu cầu (có offset).</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public required DateTimeOffset RequestedAt { get; init; }

    /// <summary>Metadata không ảnh hưởng quyết định (tùy chọn).</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>Một item của request.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ReservationItemDto
{
    /// <summary>Định danh tài nguyên.</summary>
    public required Guid ResourceId { get; init; }

    /// <summary>Định danh loại tài nguyên.</summary>
    public required Guid ResourceTypeId { get; init; }

    /// <summary>Thời điểm bắt đầu (có offset).</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public required DateTimeOffset StartTime { get; init; }

    /// <summary>Thời điểm kết thúc (có offset).</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public required DateTimeOffset EndTime { get; init; }

    /// <summary>Giá của item (SmashCourt: phí sân trước giảm giá).</summary>
    public required decimal Price { get; init; }
}

/// <summary>Thông tin khách của request.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CustomerDto
{
    /// <summary>Định danh khách; bắt buộc khi <see cref="IsGuest"/> là <c>false</c>, <c>null</c> khi là khách vãng lai.</summary>
    public Guid? CustomerId { get; init; }

    /// <summary>Khách vãng lai.</summary>
    public required bool IsGuest { get; init; }

    /// <summary>Hạng thành viên chuẩn hóa.</summary>
    public required CustomerTier Tier { get; init; }

    /// <summary>Thời điểm tạo tài khoản (tùy chọn, có offset); <c>null</c> với khách vãng lai.</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public DateTimeOffset? MemberSince { get; init; }

    /// <summary>Lịch sử đặt chỗ (tùy chọn).</summary>
    public CustomerHistoryDto? History { get; init; }
}

/// <summary>Lịch sử đặt chỗ của khách (INTEGRATION_SPEC §3.3).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CustomerHistoryDto
{
    /// <summary>Số booking đã hoàn tất.</summary>
    public required int CompletedCount { get; init; }

    /// <summary>Số lần không đến.</summary>
    public required int NoShowCount { get; init; }

    /// <summary>Số lần khách tự hủy và được hoàn đủ.</summary>
    public required int CancelledCount { get; init; }

    /// <summary>Số lần hủy muộn (tùy chọn, mặc định 0).</summary>
    public int LateCancelledCount { get; init; }

    /// <summary>Số lần hết hạn thanh toán (tùy chọn, mặc định 0).</summary>
    public int PaymentExpiredCount { get; init; }

    /// <summary>Thời điểm hoàn tất gần nhất (tùy chọn, có offset).</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public DateTimeOffset? LastCompletedAt { get; init; }
}

/// <summary>Ảnh chụp nhu cầu tại khung giờ.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class DemandDto
{
    /// <summary>Tỷ lệ kín sân trong <c>[0, 1]</c>.</summary>
    public required decimal OccupancyRate { get; init; }

    /// <summary>Số người chờ slot.</summary>
    public required int WaitlistCount { get; init; }
}
```

Create `src/Shared.DecisionContractSDK/V2/DTOs/Decisions/DecisionResultDto.cs`:

```csharp
using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.V2.Common;
using Shared.DecisionContractSDK.V2.Enums;

namespace Shared.DecisionContractSDK.V2.DTOs.Decisions;

/// <summary>
/// Response 200 của <c>POST /api/v2/decisions</c> (INTEGRATION_SPEC §3.3).
/// <see cref="Deposit"/> có ⇔ <see cref="DecisionStatus.APPROVED"/>; <see cref="ApprovalExpiresAt"/> có ⇔
/// <see cref="DecisionStatus.NEED_APPROVAL"/> (INV-7). Hai trường này luôn xuất hiện trên wire, có thể là <c>null</c>.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class DecisionResultDto
{
    /// <summary>Định danh quyết định do runtime cấp.</summary>
    public required Guid DecisionId { get; init; }

    /// <summary>Định danh đơn phía client.</summary>
    public required Guid ReservationId { get; init; }

    /// <summary>Trạng thái quyết định.</summary>
    public required DecisionStatus Status { get; init; }

    /// <summary>Mức cọc; chỉ có khi <see cref="DecisionStatus.APPROVED"/>.</summary>
    public required DepositDto? Deposit { get; init; }

    /// <summary>Tên strategy đã chạy.</summary>
    public required string StrategyName { get; init; }

    /// <summary>Phiên bản strategy.</summary>
    public required string StrategyVersion { get; init; }

    /// <summary>Cách quyết định được tạo ra.</summary>
    public required ExecutionType ExecutionType { get; init; }

    /// <summary>Có dùng fallback.</summary>
    public required bool FallbackUsed { get; init; }

    /// <summary>Mã lý do.</summary>
    public required string ReasonCode { get; init; }

    /// <summary>Lý do cho người đọc.</summary>
    public required IReadOnlyList<string> Reasons { get; init; }

    /// <summary>Khuyến nghị kèm theo.</summary>
    public required IReadOnlyList<DecisionRecommendationDto> Recommendations { get; init; }

    /// <summary>Hạn duyệt; chỉ có khi <see cref="DecisionStatus.NEED_APPROVAL"/>.</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public required DateTimeOffset? ApprovalExpiresAt { get; init; }

    /// <summary>Thời điểm đánh giá.</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public required DateTimeOffset EvaluatedAt { get; init; }
}

/// <summary>Mức cọc đã quy đổi: <c>amount = round(totalPrice × rate)</c> theo số chữ số thập phân của tiền tệ.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class DepositDto
{
    /// <summary>Tỷ lệ cọc trong <c>[0, 1]</c>.</summary>
    public required decimal Rate { get; init; }

    /// <summary>Số tiền cọc.</summary>
    public required decimal Amount { get; init; }

    /// <summary>Mã tiền tệ ISO 4217.</summary>
    public required string Currency { get; init; }
}

/// <summary>Khuyến nghị kèm quyết định trên wire: <c>{ type, description }</c>.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class DecisionRecommendationDto
{
    /// <summary>Loại khuyến nghị.</summary>
    public required RecommendationType Type { get; init; }

    /// <summary>Mô tả cho người đọc.</summary>
    public required string Description { get; init; }
}
```

Create `src/Shared.DecisionContractSDK/V2/DTOs/Decisions/DecisionDetailDto.cs`:

```csharp
using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.V2.Common;
using Shared.DecisionContractSDK.V2.DTOs.Governance;
using Shared.DecisionContractSDK.V2.DTOs.Outcomes;
using Shared.DecisionContractSDK.V2.Enums;

namespace Shared.DecisionContractSDK.V2.DTOs.Decisions;

/// <summary>
/// Response <c>GET /api/v2/decisions/{decisionId}</c> (INTEGRATION_SPEC §3.4).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class DecisionDetailDto
{
    /// <summary>Định danh quyết định.</summary>
    public required Guid DecisionId { get; init; }

    /// <summary>Định danh đơn phía client.</summary>
    public required Guid ReservationId { get; init; }

    /// <summary>Request v2 đã chuẩn hóa tại thời điểm đánh giá.</summary>
    public required DecisionRequestDto Request { get; init; }

    /// <summary>Kết quả máy.</summary>
    public required DecisionDetailResultDto Result { get; init; }

    /// <summary>Telemetry thực thi.</summary>
    public required ExecutionDetailDto Execution { get; init; }

    /// <summary>Override của Manager (tối đa một — INV-5), hoặc <c>null</c>.</summary>
    public required OverrideResponseDto? Override { get; init; }

    /// <summary>Kết quả thực tế, hoặc <c>null</c>.</summary>
    public required OutcomeResponseDto? Outcome { get; init; }

    /// <summary>Thời điểm ghi audit.</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Correlation id của request đánh giá (nếu có).</summary>
    public required string? CorrelationId { get; init; }
}

/// <summary>Kết quả máy trong chi tiết quyết định, gồm trạng thái gốc của strategy.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class DecisionDetailResultDto
{
    /// <summary>Trạng thái trả cho client (sau approval window).</summary>
    public required DecisionStatus Status { get; init; }

    /// <summary>Trạng thái gốc do strategy/fallback trả.</summary>
    public required DecisionStatus StrategyStatus { get; init; }

    /// <summary>Mức cọc; chỉ có khi <see cref="DecisionStatus.APPROVED"/>.</summary>
    public required DepositDto? Deposit { get; init; }

    /// <summary>Mã lý do.</summary>
    public required string ReasonCode { get; init; }

    /// <summary>Lý do cho người đọc.</summary>
    public required IReadOnlyList<string> Reasons { get; init; }

    /// <summary>Khuyến nghị kèm theo.</summary>
    public required IReadOnlyList<DecisionRecommendationDto> Recommendations { get; init; }

    /// <summary>Hạn duyệt; chỉ có khi <see cref="DecisionStatus.NEED_APPROVAL"/>.</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public required DateTimeOffset? ApprovalExpiresAt { get; init; }
}

/// <summary>Telemetry thực thi của một quyết định.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ExecutionDetailDto
{
    /// <summary>Tên strategy.</summary>
    public required string StrategyName { get; init; }

    /// <summary>Phiên bản strategy.</summary>
    public required string StrategyVersion { get; init; }

    /// <summary>Cách quyết định được tạo ra.</summary>
    public required ExecutionType ExecutionType { get; init; }

    /// <summary>Thời gian thực thi (ms).</summary>
    public required int ExecutionTimeMs { get; init; }

    /// <summary>Có dùng fallback.</summary>
    public required bool FallbackUsed { get; init; }

    /// <summary>Lý do lỗi khi fallback (nếu có).</summary>
    public required string? FailureReason { get; init; }

    /// <summary>Lấy từ cache.</summary>
    public required bool CacheHit { get; init; }
}
```

- [ ] **Step 6: Hiện thực DTO Governance và Outcomes**

Create `src/Shared.DecisionContractSDK/V2/DTOs/Governance/OverrideRequestDto.cs`:

```csharp
using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.V2.Enums;

namespace Shared.DecisionContractSDK.V2.DTOs.Governance;

/// <summary>
/// Request <c>POST /api/v2/decisions/{decisionId}/override</c> (INTEGRATION_SPEC §3.6). <c>APPROVE</c> bắt buộc
/// đúng một trong <c>deposit.rate</c>/<c>deposit.amount</c>; <c>REJECT</c> không gửi <c>deposit</c> — runtime kiểm tra (gói 5.3).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class OverrideRequestDto
{
    /// <summary>Hành động của Manager.</summary>
    public required OverrideAction Action { get; init; }

    /// <summary>Mức cọc khi <see cref="OverrideAction.APPROVE"/>.</summary>
    public OverrideDepositDto? Deposit { get; init; }

    /// <summary>Lý do (5..500 ký tự).</summary>
    public required string Reason { get; init; }
}

/// <summary>Mức cọc Manager chọn: theo tỷ lệ hoặc theo số tiền.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class OverrideDepositDto
{
    /// <summary>Tỷ lệ cọc trong <c>[0, 1]</c>.</summary>
    public decimal? Rate { get; init; }

    /// <summary>Số tiền cọc.</summary>
    public decimal? Amount { get; init; }
}
```

Create `src/Shared.DecisionContractSDK/V2/DTOs/Governance/OverrideResponseDto.cs`:

```csharp
using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.V2.Common;
using Shared.DecisionContractSDK.V2.DTOs.Decisions;
using Shared.DecisionContractSDK.V2.Enums;

namespace Shared.DecisionContractSDK.V2.DTOs.Governance;

/// <summary>Response 200 của override (INTEGRATION_SPEC §3.6); cũng là phần <c>override</c> của chi tiết quyết định.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class OverrideResponseDto
{
    /// <summary>Định danh quyết định.</summary>
    public required Guid DecisionId { get; init; }

    /// <summary>Hành động của Manager.</summary>
    public required OverrideAction Action { get; init; }

    /// <summary>Mức cọc đã quy đổi (rate và amount) khi duyệt; <c>null</c> khi từ chối.</summary>
    public required DepositDto? Deposit { get; init; }

    /// <summary>Định danh Manager (<c>X-Actor-Id</c>).</summary>
    public required string ActorId { get; init; }

    /// <summary>Lý do.</summary>
    public required string Reason { get; init; }

    /// <summary>Thời điểm override.</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public required DateTimeOffset OverriddenAt { get; init; }
}
```

Create `src/Shared.DecisionContractSDK/V2/DTOs/Governance/PendingApprovalItemDto.cs`:

```csharp
using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.V2.Common;
using Shared.DecisionContractSDK.V2.Enums;

namespace Shared.DecisionContractSDK.V2.DTOs.Governance;

/// <summary>Một mục trong hàng chờ duyệt (INTEGRATION_SPEC §3.5).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PendingApprovalItemDto
{
    /// <summary>Định danh quyết định.</summary>
    public required Guid DecisionId { get; init; }

    /// <summary>Định danh đơn phía client.</summary>
    public required Guid ReservationId { get; init; }

    /// <summary>Định danh chi nhánh.</summary>
    public required Guid BranchId { get; init; }

    /// <summary>Tổng tiền.</summary>
    public required decimal TotalPrice { get; init; }

    /// <summary>Mã tiền tệ.</summary>
    public required string Currency { get; init; }

    /// <summary>Thời điểm bắt đầu sớm nhất.</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public required DateTimeOffset EarliestStart { get; init; }

    /// <summary>Thông tin khách rút gọn.</summary>
    public required PendingApprovalCustomerDto Customer { get; init; }

    /// <summary>Mã lý do cần duyệt.</summary>
    public required string ReasonCode { get; init; }

    /// <summary>Quyết định đến từ fallback.</summary>
    public required bool FallbackUsed { get; init; }

    /// <summary>Hạn duyệt.</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public required DateTimeOffset ApprovalExpiresAt { get; init; }

    /// <summary>Thời điểm tạo quyết định.</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Thông tin khách rút gọn trong hàng chờ.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PendingApprovalCustomerDto
{
    /// <summary>Khách vãng lai.</summary>
    public required bool IsGuest { get; init; }

    /// <summary>Hạng thành viên.</summary>
    public required CustomerTier Tier { get; init; }
}
```

Create `src/Shared.DecisionContractSDK/V2/DTOs/Outcomes/OutcomeRequestDto.cs`:

```csharp
using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.V2.Common;
using Shared.DecisionContractSDK.V2.Enums;

namespace Shared.DecisionContractSDK.V2.DTOs.Outcomes;

/// <summary>Request <c>POST /api/v2/decisions/{decisionId}/outcome</c> (INTEGRATION_SPEC §3.7).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class OutcomeRequestDto
{
    /// <summary>Kết quả thực tế.</summary>
    public required DecisionOutcome Outcome { get; init; }

    /// <summary>Thời điểm xảy ra (có offset).</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public required DateTimeOffset OccurredAt { get; init; }
}
```

Create `src/Shared.DecisionContractSDK/V2/DTOs/Outcomes/OutcomeResponseDto.cs`:

```csharp
using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.V2.Common;
using Shared.DecisionContractSDK.V2.Enums;

namespace Shared.DecisionContractSDK.V2.DTOs.Outcomes;

/// <summary>Response 200 của outcome (INTEGRATION_SPEC §3.7); cũng là phần <c>outcome</c> của chi tiết quyết định.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class OutcomeResponseDto
{
    /// <summary>Định danh quyết định.</summary>
    public required Guid DecisionId { get; init; }

    /// <summary>Kết quả thực tế.</summary>
    public required DecisionOutcome Outcome { get; init; }

    /// <summary>Thời điểm xảy ra.</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>Thời điểm TADSS ghi nhận.</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public required DateTimeOffset ReportedAt { get; init; }
}
```

- [ ] **Step 7: Hiện thực mapping**

Create `src/Shared.DecisionContractSDK/V2/DTOs/Mappings/DecisionRequestMapping.cs`:

```csharp
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.V2.Domain;
using Shared.DecisionContractSDK.V2.DTOs.Decisions;

namespace Shared.DecisionContractSDK.V2.DTOs.Mappings;

/// <summary>
/// Ánh xạ request wire v2 sang domain. Lỗi định dạng (múi giờ không phải IANA, item null) →
/// <see cref="ContractValidationException"/>; vi phạm bất biến → <c>DomainInvariantException</c> từ constructor domain.
/// </summary>
public static class DecisionRequestMapping
{
    /// <summary>Dựng <see cref="Reservation"/> từ request v2.</summary>
    /// <param name="request">Request đã deserialize.</param>
    /// <exception cref="ContractValidationException">Khi múi giờ không phải mã IANA hoặc danh sách item có phần tử null.</exception>
    public static Reservation ToDomain(this DecisionRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var timeZone = ResolveIanaTimeZone(request.BranchTimeZone);

        if (request.Items is null || request.Items.Any(item => item is null))
        {
            throw new ContractValidationException("INVALID_ITEMS", "items must be a non-null array without null elements.", "items");
        }

        var items = request.Items
            .Select(item => new ReservationItem(item.ResourceId, item.ResourceTypeId, item.StartTime, item.EndTime, item.Price))
            .ToArray();

        var history = request.Customer.History is { } h
            ? new CustomerHistory(h.CompletedCount, h.NoShowCount, h.CancelledCount, h.LateCancelledCount, h.PaymentExpiredCount, h.LastCompletedAt)
            : null;

        var customer = new CustomerProfile(
            request.Customer.IsGuest,
            request.Customer.CustomerId,
            request.Customer.Tier,
            request.Customer.MemberSince,
            history);

        var demand = request.Demand is { } d ? new DemandSnapshot(d.OccupancyRate, d.WaitlistCount) : null;

        return new Reservation(
            request.ReservationId,
            request.BranchId,
            timeZone,
            items,
            customer,
            request.TotalPrice,
            request.Currency,
            request.Channel,
            request.RequestedAt,
            demand,
            request.Metadata);
    }

    private static TimeZoneInfo ResolveIanaTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)
            || !TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out _)
            || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var timeZone))
        {
            throw new ContractValidationException(
                "INVALID_TIME_ZONE",
                $"branchTimeZone '{timeZoneId}' is not a valid IANA time zone identifier.",
                "branchTimeZone");
        }

        return timeZone;
    }
}
```

- [ ] **Step 8: Viết JSON schema v2**

Create `docs/contracts/schemas/v2/decision-request.schema.json`:

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://tadss.local/schemas/v2/decision-request.schema.json",
  "title": "DecisionRequest v2",
  "description": "POST /api/v2/decisions — INTEGRATION_SPEC §3.3",
  "type": "object",
  "additionalProperties": false,
  "required": ["reservationId", "branchId", "branchTimeZone", "items", "customer", "totalPrice", "currency", "channel", "requestedAt"],
  "properties": {
    "reservationId": { "$ref": "#/$defs/uuid" },
    "branchId": { "$ref": "#/$defs/uuid" },
    "branchTimeZone": { "type": "string", "minLength": 1 },
    "items": { "type": "array", "minItems": 1, "maxItems": 20, "items": { "$ref": "#/$defs/item" } },
    "customer": { "$ref": "#/$defs/customer" },
    "totalPrice": { "type": "number", "minimum": 0 },
    "currency": { "type": "string", "pattern": "^[A-Z]{3}$" },
    "channel": { "enum": ["ONLINE", "WALK_IN"] },
    "demand": { "oneOf": [{ "$ref": "#/$defs/demand" }, { "type": "null" }] },
    "requestedAt": { "$ref": "#/$defs/dateTime" },
    "metadata": { "oneOf": [{ "type": "object", "additionalProperties": { "type": "string" } }, { "type": "null" }] }
  },
  "$defs": {
    "uuid": { "type": "string", "format": "uuid" },
    "dateTime": { "type": "string", "format": "date-time" },
    "count": { "type": "integer", "minimum": 0 },
    "item": {
      "type": "object",
      "additionalProperties": false,
      "required": ["resourceId", "resourceTypeId", "startTime", "endTime", "price"],
      "properties": {
        "resourceId": { "$ref": "#/$defs/uuid" },
        "resourceTypeId": { "$ref": "#/$defs/uuid" },
        "startTime": { "$ref": "#/$defs/dateTime" },
        "endTime": { "$ref": "#/$defs/dateTime" },
        "price": { "type": "number", "minimum": 0 }
      }
    },
    "customer": {
      "type": "object",
      "additionalProperties": false,
      "required": ["isGuest", "tier"],
      "properties": {
        "customerId": { "oneOf": [{ "$ref": "#/$defs/uuid" }, { "type": "null" }] },
        "isGuest": { "type": "boolean" },
        "tier": { "enum": ["STANDARD", "SILVER", "GOLD", "PLATINUM", "DIAMOND"] },
        "memberSince": { "oneOf": [{ "$ref": "#/$defs/dateTime" }, { "type": "null" }] },
        "history": { "oneOf": [{ "$ref": "#/$defs/history" }, { "type": "null" }] }
      },
      "if": { "properties": { "isGuest": { "const": false } } },
      "then": { "required": ["customerId"], "properties": { "customerId": { "$ref": "#/$defs/uuid" } } },
      "else": { "properties": { "customerId": { "type": "null" }, "memberSince": { "type": "null" } } }
    },
    "history": {
      "type": "object",
      "additionalProperties": false,
      "required": ["completedCount", "noShowCount", "cancelledCount"],
      "properties": {
        "completedCount": { "$ref": "#/$defs/count" },
        "noShowCount": { "$ref": "#/$defs/count" },
        "cancelledCount": { "$ref": "#/$defs/count" },
        "lateCancelledCount": { "$ref": "#/$defs/count" },
        "paymentExpiredCount": { "$ref": "#/$defs/count" },
        "lastCompletedAt": { "oneOf": [{ "$ref": "#/$defs/dateTime" }, { "type": "null" }] }
      }
    },
    "demand": {
      "type": "object",
      "additionalProperties": false,
      "required": ["occupancyRate", "waitlistCount"],
      "properties": {
        "occupancyRate": { "type": "number", "minimum": 0, "maximum": 1 },
        "waitlistCount": { "$ref": "#/$defs/count" }
      }
    }
  }
}
```

Create `docs/contracts/schemas/v2/decision-result.schema.json`:

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://tadss.local/schemas/v2/decision-result.schema.json",
  "title": "DecisionResult v2",
  "description": "Response 200 của POST /api/v2/decisions — INTEGRATION_SPEC §3.3; INV-7: APPROVED ⇔ deposit, NEED_APPROVAL ⇔ approvalExpiresAt",
  "type": "object",
  "additionalProperties": false,
  "required": ["decisionId", "reservationId", "status", "deposit", "strategyName", "strategyVersion", "executionType", "fallbackUsed", "reasonCode", "reasons", "recommendations", "approvalExpiresAt", "evaluatedAt"],
  "properties": {
    "decisionId": { "$ref": "#/$defs/uuid" },
    "reservationId": { "$ref": "#/$defs/uuid" },
    "status": { "enum": ["APPROVED", "NEED_APPROVAL", "REJECTED"] },
    "deposit": { "oneOf": [{ "$ref": "#/$defs/deposit" }, { "type": "null" }] },
    "strategyName": { "type": "string", "minLength": 1 },
    "strategyVersion": { "type": "string", "pattern": "^\\d+\\.\\d+\\.\\d+$" },
    "executionType": { "enum": ["AUTOMATED", "FALLBACK", "CACHE_HIT"] },
    "fallbackUsed": { "type": "boolean" },
    "reasonCode": { "type": "string", "minLength": 1, "maxLength": 64 },
    "reasons": { "type": "array", "items": { "type": "string" } },
    "recommendations": { "type": "array", "items": { "$ref": "#/$defs/recommendation" } },
    "approvalExpiresAt": { "oneOf": [{ "$ref": "#/$defs/dateTime" }, { "type": "null" }] },
    "evaluatedAt": { "$ref": "#/$defs/dateTime" }
  },
  "allOf": [
    {
      "if": { "properties": { "status": { "const": "APPROVED" } } },
      "then": { "properties": { "deposit": { "$ref": "#/$defs/deposit" } } },
      "else": { "properties": { "deposit": { "type": "null" } } }
    },
    {
      "if": { "properties": { "status": { "const": "NEED_APPROVAL" } } },
      "then": { "properties": { "approvalExpiresAt": { "$ref": "#/$defs/dateTime" } } },
      "else": { "properties": { "approvalExpiresAt": { "type": "null" } } }
    }
  ],
  "$defs": {
    "uuid": { "type": "string", "format": "uuid" },
    "dateTime": { "type": "string", "format": "date-time" },
    "deposit": {
      "type": "object",
      "additionalProperties": false,
      "required": ["rate", "amount", "currency"],
      "properties": {
        "rate": { "type": "number", "minimum": 0, "maximum": 1 },
        "amount": { "type": "number", "minimum": 0 },
        "currency": { "type": "string", "pattern": "^[A-Z]{3}$" }
      }
    },
    "recommendation": {
      "type": "object",
      "additionalProperties": false,
      "required": ["type", "description"],
      "properties": {
        "type": { "enum": ["REQUIRE_APPROVAL", "ALTERNATIVE_SLOT", "ALTERNATIVE_RESOURCE", "PRIORITY_ESCALATION"] },
        "description": { "type": "string", "minLength": 1 }
      }
    }
  }
}
```

- [ ] **Step 9: Build và chạy test V2**

Run: `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror && dotnet test tests/TADSS.UnitTests/TADSS.UnitTests.csproj -c Release --no-build -nologo --filter "FullyQualifiedName~TADSS.UnitTests.V2"`
Expected: `Passed: 144` (100 + 44). Nếu một ca của `DecisionSchemaTests` fail: in `ContractSchemas.Describe(...)`, sửa **schema** cho đúng INTEGRATION_SPEC (không sửa payload mẫu, không bỏ ca). Nếu `Request_IntegrationSpecSample_RoundTripsWithoutLoss` fail: so sánh chuỗi round-trip in trong thông báo với mẫu và sửa DTO/converter cho khớp wire.

- [ ] **Step 10: Cập nhật baseline** — kỳ vọng thêm các kiểu `V2.DTOs.*` (18 lớp DTO) và `V2.DTOs.Mappings.DecisionRequestMapping`; `OffsetDateTimeConverter` là internal nên không xuất hiện.

- [ ] **Step 11: Chạy toàn bộ unit test** — Expected: `Passed: 619`, `Failed: 0`.

- [ ] **Step 12: Commit (hỏi người dùng trước)**

```bash
git add Directory.Packages.props tests/TADSS.UnitTests src/Shared.DecisionContractSDK/V2 docs/contracts/schemas/v2 docs/contracts/baseline_public_api.txt
git commit -m "feat(sdk-v2): DTO wire v2, mapping sang domain và JSON schema v2 cho request/result"
```

---

### Task 6: `DefaultDecisionStrategy` 2.0.0 — cọc theo hạng thành viên

**Files:**

- Create: `src/TADSS.Strategies.Default/V2/DefaultStrategyOptions.cs`, `src/TADSS.Strategies.Default/V2/DefaultDecisionStrategy.cs`
- Create: `tests/TADSS.UnitTests/V2/Strategies/DefaultDecisionStrategyTests.cs`

**Interfaces:**

- Consumes: `IReservationDecisionStrategy`, `StrategyDecision`, `StrategyCachePolicy`, `DecisionRecommendation`, `DecisionContext` (Task 4); `Reservation`, `CustomerProfile` (Task 3); `ReasonCodes`, `CustomerTier`, `RecommendationType` (Task 2); `V2TestData.*` (Task 3–4).
- Produces: `TADSS.Strategies.Default.V2.DefaultDecisionStrategy(DefaultStrategyOptions options)` (`StrategyName = "DefaultDecisionStrategy"`, `StrategyVersion = "2.0.0"`); `DefaultStrategyOptions` (`HighValueThreshold = 2_000_000`, `NoShowReviewThreshold = 2`, `StandardDepositRate = 0.5`, `SilverDepositRate = 0.4`, `GoldDepositRate = 0.3`, `PremiumDepositRate = 0.2`, `CacheTtlMinutes = 60`, `Validate()`) — gói 5.2b bind từ `Tadss:Strategies:Default`.

Bảng quyết định (SOFTWARE_ARCHITECTURE_SPECIFICATION 4.3, kiểm tra theo thứ tự): `totalPrice ≥ HighValueThreshold` → `NEED_APPROVAL`/`HIGH_VALUE_REVIEW`; `history.noShowCount ≥ NoShowReviewThreshold` → `NEED_APPROVAL`/`NO_SHOW_HISTORY_REVIEW`; khách vãng lai hoặc `STANDARD` → 50%; `SILVER` 40%; `GOLD` 30%; `PLATINUM`, `DIAMOND` 20% — `APPROVED`/`TIER_DEPOSIT`.

- [ ] **Step 1: Viết test (thất bại)**

Create `tests/TADSS.UnitTests/V2/Strategies/DefaultDecisionStrategyTests.cs`:

```csharp
using System.Globalization;
using FluentAssertions;
using Shared.DecisionContractSDK.V2.Common;
using Shared.DecisionContractSDK.V2.Enums;
using TADSS.Strategies.Default.V2;
using TADSS.UnitTests.V2.TestData;
using Xunit;

namespace TADSS.UnitTests.V2.Strategies;

/// <summary>
/// UT-03: bảng quyết định <c>DefaultDecisionStrategy</c> 2.0.0 — mọi ô và biên (giá trị lớn, lịch sử không đến,
/// từng hạng, khách vãng lai), tham số cấu hình được, không phụ thuộc thời điểm đánh giá.
/// </summary>
public class DefaultDecisionStrategyTests
{
    private static readonly DefaultDecisionStrategy Strategy = new(new DefaultStrategyOptions());

    [Theory]
    [Trait("CatalogId", "UT-03")]
    [InlineData(CustomerTier.STANDARD, false, 228_000, 0, DecisionStatus.APPROVED, "0.5", ReasonCodes.TierDeposit)]
    [InlineData(CustomerTier.SILVER, false, 228_000, 0, DecisionStatus.APPROVED, "0.4", ReasonCodes.TierDeposit)]
    [InlineData(CustomerTier.GOLD, false, 228_000, 0, DecisionStatus.APPROVED, "0.3", ReasonCodes.TierDeposit)]
    [InlineData(CustomerTier.PLATINUM, false, 228_000, 0, DecisionStatus.APPROVED, "0.2", ReasonCodes.TierDeposit)]
    [InlineData(CustomerTier.DIAMOND, false, 228_000, 0, DecisionStatus.APPROVED, "0.2", ReasonCodes.TierDeposit)]
    [InlineData(CustomerTier.STANDARD, true, 228_000, 0, DecisionStatus.APPROVED, "0.5", ReasonCodes.TierDeposit)]
    [InlineData(CustomerTier.GOLD, false, 1_999_999, 0, DecisionStatus.APPROVED, "0.3", ReasonCodes.TierDeposit)]
    [InlineData(CustomerTier.GOLD, false, 2_000_000, 0, DecisionStatus.NEED_APPROVAL, null, ReasonCodes.HighValueReview)]
    [InlineData(CustomerTier.GOLD, false, 228_000, 1, DecisionStatus.APPROVED, "0.3", ReasonCodes.TierDeposit)]
    [InlineData(CustomerTier.GOLD, false, 228_000, 2, DecisionStatus.NEED_APPROVAL, null, ReasonCodes.NoShowHistoryReview)]
    [InlineData(CustomerTier.DIAMOND, false, 2_000_000, 5, DecisionStatus.NEED_APPROVAL, null, ReasonCodes.HighValueReview)]
    public async Task EvaluateAsync_DecisionTable_ReturnsExpectedDecision(
        CustomerTier tier, bool isGuest, long totalPrice, int noShowCount,
        DecisionStatus expectedStatus, string? expectedRate, string expectedReason)
    {
        var history = V2TestData.CreateHistory(noShowCount);
        var customer = isGuest ? V2TestData.CreateGuest(history) : V2TestData.CreateMember(tier, history);
        var reservation = V2TestData.CreateReservation(customer: customer, totalPrice: totalPrice);

        var decision = await Strategy.EvaluateAsync(reservation, V2TestData.CreateContext(), CancellationToken.None);

        decision.Status.Should().Be(expectedStatus);
        decision.ReasonCode.Should().Be(expectedReason);
        decision.DepositRate.Should().Be(expectedRate is null ? null : decimal.Parse(expectedRate, CultureInfo.InvariantCulture));
        decision.Reasons.Should().NotBeEmpty();
    }

    [Fact]
    [Trait("CatalogId", "UT-03")]
    public async Task EvaluateAsync_NoHistory_TreatsAsZeroNoShows()
    {
        var reservation = V2TestData.CreateReservation(customer: V2TestData.CreateMember(CustomerTier.GOLD, history: null));

        var decision = await Strategy.EvaluateAsync(reservation, V2TestData.CreateContext(), CancellationToken.None);

        decision.Status.Should().Be(DecisionStatus.APPROVED);
        decision.DepositRate.Should().Be(0.3m);
    }

    [Fact]
    [Trait("CatalogId", "UT-03")]
    public async Task EvaluateAsync_NeedApproval_RecommendsManagerApproval()
    {
        var reservation = V2TestData.CreateReservation(totalPrice: 2_500_000m);

        var decision = await Strategy.EvaluateAsync(reservation, V2TestData.CreateContext(), CancellationToken.None);

        decision.Recommendations.Should().ContainSingle().Which.Type.Should().Be(RecommendationType.REQUIRE_APPROVAL);
    }

    [Fact]
    [Trait("CatalogId", "UT-03")]
    public async Task EvaluateAsync_DifferentEvaluationTimes_ReturnsSameDecision()
    {
        var reservation = V2TestData.CreateReservation(customer: V2TestData.CreateMember(CustomerTier.SILVER));

        var early = await Strategy.EvaluateAsync(reservation, V2TestData.CreateContext(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)), CancellationToken.None);
        var late = await Strategy.EvaluateAsync(reservation, V2TestData.CreateContext(new DateTimeOffset(2035, 6, 1, 12, 0, 0, TimeSpan.Zero)), CancellationToken.None);

        late.Status.Should().Be(early.Status);
        late.DepositRate.Should().Be(early.DepositRate);
        late.ReasonCode.Should().Be(early.ReasonCode);
    }

    [Fact]
    [Trait("CatalogId", "UT-03")]
    public async Task EvaluateAsync_CustomHighValueThreshold_IsApplied()
    {
        // Ngưỡng tiền tệ chỉnh bằng cấu hình (PT-01 Hotel EUR) — không sửa mã
        var strategy = new DefaultDecisionStrategy(new DefaultStrategyOptions { HighValueThreshold = 500m });
        var reservation = V2TestData.CreateReservation(totalPrice: 600m, currency: "EUR");

        var decision = await strategy.EvaluateAsync(reservation, V2TestData.CreateContext(), CancellationToken.None);

        decision.ReasonCode.Should().Be(ReasonCodes.HighValueReview);
    }

    [Fact]
    [Trait("CatalogId", "UT-03")]
    public void Identity_NameAndVersion_MatchV2Contract()
    {
        Strategy.StrategyName.Should().Be("DefaultDecisionStrategy");
        Strategy.StrategyVersion.Should().Be("2.0.0");
    }

    [Fact]
    [Trait("CatalogId", "UT-03")]
    public void CachePolicy_DefaultOptions_EnabledForSixtyMinutes()
    {
        Strategy.CachePolicy.IsEnabled.Should().BeTrue();
        Strategy.CachePolicy.Ttl.Should().Be(TimeSpan.FromMinutes(60));
    }

    [Fact]
    [Trait("CatalogId", "UT-03")]
    public void CachePolicy_ZeroTtl_IsDisabled()
    {
        var strategy = new DefaultDecisionStrategy(new DefaultStrategyOptions { CacheTtlMinutes = 0 });

        strategy.CachePolicy.IsEnabled.Should().BeFalse();
    }

    [Theory]
    [Trait("CatalogId", "UT-03")]
    [InlineData("rate-above-one")]
    [InlineData("negative-threshold")]
    [InlineData("zero-no-show-threshold")]
    [InlineData("negative-ttl")]
    public void Constructor_InvalidOptions_ThrowsArgumentException(string caseName)
    {
        var options = caseName switch
        {
            "rate-above-one" => new DefaultStrategyOptions { GoldDepositRate = 1.1m },
            "negative-threshold" => new DefaultStrategyOptions { HighValueThreshold = -1m },
            "zero-no-show-threshold" => new DefaultStrategyOptions { NoShowReviewThreshold = 0 },
            "negative-ttl" => new DefaultStrategyOptions { CacheTtlMinutes = -1 },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

        var act = () => new DefaultDecisionStrategy(options);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    [Trait("CatalogId", "UT-03")]
    public async Task EvaluateAsync_CancelledToken_ThrowsOperationCanceled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => Strategy.EvaluateAsync(V2TestData.CreateReservation(), V2TestData.CreateContext(), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
```

- [ ] **Step 2: Chạy để xác nhận thất bại**

Run: `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror`
Expected: FAIL — `CS0234` không tìm thấy `TADSS.Strategies.Default.V2`.

- [ ] **Step 3: Hiện thực**

Create `src/TADSS.Strategies.Default/V2/DefaultStrategyOptions.cs`:

```csharp
namespace TADSS.Strategies.Default.V2;

/// <summary>
/// Tham số của <see cref="DefaultDecisionStrategy"/> 2.0.0 (SOFTWARE_ARCHITECTURE_SPECIFICATION 4.3),
/// runtime bind từ <c>Tadss:Strategies:Default</c>. Giá trị mặc định là chính sách SmashCourt.
/// </summary>
public sealed class DefaultStrategyOptions
{
    /// <summary>Tổng tiền từ ngưỡng này trở lên cần Manager duyệt (đơn vị theo tiền tệ của request).</summary>
    public decimal HighValueThreshold { get; init; } = 2_000_000m;

    /// <summary>Số lần không đến từ ngưỡng này trở lên cần Manager duyệt (≥ 1).</summary>
    public int NoShowReviewThreshold { get; init; } = 2;

    /// <summary>Tỷ lệ cọc cho khách vãng lai và hạng <c>STANDARD</c>.</summary>
    public decimal StandardDepositRate { get; init; } = 0.5m;

    /// <summary>Tỷ lệ cọc cho hạng <c>SILVER</c>.</summary>
    public decimal SilverDepositRate { get; init; } = 0.4m;

    /// <summary>Tỷ lệ cọc cho hạng <c>GOLD</c>.</summary>
    public decimal GoldDepositRate { get; init; } = 0.3m;

    /// <summary>Tỷ lệ cọc cho hạng <c>PLATINUM</c> và <c>DIAMOND</c>.</summary>
    public decimal PremiumDepositRate { get; init; } = 0.2m;

    /// <summary>Thời gian sống của cache (phút); 0 = không cache.</summary>
    public int CacheTtlMinutes { get; init; } = 60;

    /// <summary>Kiểm tra tham số; ném <see cref="ArgumentException"/> khi không hợp lệ.</summary>
    public void Validate()
    {
        if (HighValueThreshold < 0m)
        {
            throw new ArgumentException("HighValueThreshold phải ≥ 0.", nameof(HighValueThreshold));
        }

        if (NoShowReviewThreshold < 1)
        {
            throw new ArgumentException("NoShowReviewThreshold phải ≥ 1.", nameof(NoShowReviewThreshold));
        }

        foreach (var (name, rate) in new[]
                 {
                     (nameof(StandardDepositRate), StandardDepositRate),
                     (nameof(SilverDepositRate), SilverDepositRate),
                     (nameof(GoldDepositRate), GoldDepositRate),
                     (nameof(PremiumDepositRate), PremiumDepositRate)
                 })
        {
            if (rate is < 0m or > 1m)
            {
                throw new ArgumentException($"{name} phải trong [0, 1].", name);
            }
        }

        if (CacheTtlMinutes < 0)
        {
            throw new ArgumentException("CacheTtlMinutes phải ≥ 0.", nameof(CacheTtlMinutes));
        }
    }
}
```

Create `src/TADSS.Strategies.Default/V2/DefaultDecisionStrategy.cs`:

```csharp
using System.Globalization;
using Shared.DecisionContractSDK.V2.Common;
using Shared.DecisionContractSDK.V2.Decisions;
using Shared.DecisionContractSDK.V2.Domain;
using Shared.DecisionContractSDK.V2.Enums;
using Shared.DecisionContractSDK.V2.Interfaces;

namespace TADSS.Strategies.Default.V2;

/// <summary>
/// Strategy mặc định 2.0.0 — cọc theo hạng thành viên (SOFTWARE_ARCHITECTURE_SPECIFICATION 4.3).
/// Không đọc đồng hồ hệ thống, không I/O, stateless và thread-safe.
/// </summary>
public sealed class DefaultDecisionStrategy : IReservationDecisionStrategy
{
    private readonly DefaultStrategyOptions _options;

    /// <summary>Khởi tạo strategy với tham số đã kiểm tra.</summary>
    /// <param name="options">Tham số strategy.</param>
    /// <exception cref="ArgumentException">Khi tham số không hợp lệ.</exception>
    public DefaultDecisionStrategy(DefaultStrategyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _options = options;
        CachePolicy = options.CacheTtlMinutes > 0
            ? StrategyCachePolicy.Enabled(TimeSpan.FromMinutes(options.CacheTtlMinutes))
            : StrategyCachePolicy.Disabled;
    }

    /// <inheritdoc />
    public string StrategyName => nameof(DefaultDecisionStrategy);

    /// <inheritdoc />
    public string StrategyVersion => "2.0.0";

    /// <inheritdoc />
    public StrategyCachePolicy CachePolicy { get; }

    /// <inheritdoc />
    public Task<StrategyDecision> EvaluateAsync(Reservation reservation, DecisionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Decide(reservation));
    }

    private StrategyDecision Decide(Reservation reservation)
    {
        if (reservation.TotalPrice >= _options.HighValueThreshold)
        {
            return StrategyDecision.RequireApproval(
                ReasonCodes.HighValueReview,
                [$"Giá trị đơn {FormatAmount(reservation.TotalPrice)} {reservation.Currency} đạt ngưỡng cần duyệt {FormatAmount(_options.HighValueThreshold)} {reservation.Currency}."],
                new[] { new DecisionRecommendation(RecommendationType.REQUIRE_APPROVAL, "Manager xem xét đơn giá trị lớn và quyết định mức cọc.") });
        }

        var noShowCount = reservation.Customer.History?.NoShowCount ?? 0;
        if (noShowCount >= _options.NoShowReviewThreshold)
        {
            return StrategyDecision.RequireApproval(
                ReasonCodes.NoShowHistoryReview,
                [$"Khách có {noShowCount} lần không đến (ngưỡng {_options.NoShowReviewThreshold})."],
                new[] { new DecisionRecommendation(RecommendationType.REQUIRE_APPROVAL, "Manager xem xét lịch sử không đến của khách và quyết định mức cọc.") });
        }

        var (rate, segment) = ResolveTierRate(reservation.Customer);
        return StrategyDecision.Approve(rate, ReasonCodes.TierDeposit, [$"Áp dụng cọc {FormatPercent(rate)} cho {segment}."]);
    }

    private (decimal Rate, string Segment) ResolveTierRate(CustomerProfile customer)
    {
        if (customer.IsGuest)
        {
            return (_options.StandardDepositRate, "khách vãng lai");
        }

        return customer.Tier switch
        {
            CustomerTier.STANDARD => (_options.StandardDepositRate, "hạng STANDARD"),
            CustomerTier.SILVER => (_options.SilverDepositRate, "hạng SILVER"),
            CustomerTier.GOLD => (_options.GoldDepositRate, "hạng GOLD"),
            CustomerTier.PLATINUM or CustomerTier.DIAMOND => (_options.PremiumDepositRate, $"hạng {customer.Tier}"),
            _ => throw new ArgumentOutOfRangeException(nameof(customer), customer.Tier, "Hạng thành viên không được hỗ trợ.")
        };
    }

    private static string FormatAmount(decimal amount) => amount.ToString("#,0.##", CultureInfo.InvariantCulture);

    private static string FormatPercent(decimal rate) => (rate * 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%";
}
```

- [ ] **Step 4: Build và chạy test** — Run: `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror && dotnet test tests/TADSS.UnitTests/TADSS.UnitTests.csproj -c Release --no-build -nologo` — Expected: 0 warning; `Passed: 642`, `Failed: 0`. `StrategyDependencyPurityTests` (v1) vẫn xanh vì project strategy chỉ tham chiếu SDK.

- [ ] **Step 5: Commit (hỏi người dùng trước)**

```bash
git add src/TADSS.Strategies.Default/V2 tests/TADSS.UnitTests/V2/Strategies/DefaultDecisionStrategyTests.cs
git commit -m "feat(strategies-v2): DefaultDecisionStrategy 2.0.0 — cọc theo hạng, duyệt khi giá trị lớn hoặc lịch sử không đến"
```

---

### Task 7: `MockAlternativeDecisionStrategy` 2.0.0 — cọc theo khung giờ chi nhánh

**Files:**

- Create: `src/TADSS.Strategies.MockAlternative/V2/MockAlternativeStrategyOptions.cs`, `src/TADSS.Strategies.MockAlternative/V2/MockAlternativeDecisionStrategy.cs`
- Create: `tests/TADSS.UnitTests/V2/Strategies/MockAlternativeDecisionStrategyTests.cs`

**Interfaces:**

- Consumes: như Task 6, cộng `BranchCalendar.IsPeak`, `Reservation.ToBranchLocal`, `Reservation.EarliestStart`.
- Produces: `TADSS.Strategies.MockAlternative.V2.MockAlternativeDecisionStrategy(MockAlternativeStrategyOptions options)` (`StrategyName = "MockAlternativeDecisionStrategy"`, `StrategyVersion = "2.0.0"`); `MockAlternativeStrategyOptions` (`HighValueThreshold = 1_500_000`, `PeakDepositRate = 0.5`, `OffPeakDepositRate = 0.3`, `CacheTtlMinutes = 60`, `Validate()`) — gói 5.2b bind từ `Tadss:Strategies:MockAlternative`.

Bảng quyết định: giờ cao điểm và `totalPrice ≥ HighValueThreshold` → `NEED_APPROVAL`/`HIGH_VALUE_REVIEW`; giờ cao điểm → `APPROVED` 50% `PEAK_HOUR_DEPOSIT`; ngoài giờ cao điểm → `APPROVED` 30% `OFF_PEAK_DEPOSIT`. Giờ cao điểm lấy từ `context.Calendar.IsPeak(reservation)` (giờ chi nhánh, thời điểm bắt đầu sớm nhất). Lý do ghi giờ địa phương dạng `"Khung giờ cao điểm (18:00 thứ Sáu, giờ chi nhánh) áp dụng cọc 50%."` (khớp mẫu INTEGRATION_SPEC §3.3).

- [ ] **Step 1: Viết test (thất bại)**

Create `tests/TADSS.UnitTests/V2/Strategies/MockAlternativeDecisionStrategyTests.cs`:

```csharp
using System.Globalization;
using FluentAssertions;
using Shared.DecisionContractSDK.V2.Common;
using Shared.DecisionContractSDK.V2.Domain;
using Shared.DecisionContractSDK.V2.Enums;
using TADSS.Strategies.MockAlternative.V2;
using TADSS.UnitTests.V2.TestData;
using Xunit;

namespace TADSS.UnitTests.V2.Strategies;

/// <summary>
/// UT-03 / UT-05: bảng quyết định <c>MockAlternativeDecisionStrategy</c> 2.0.0 theo giờ cao điểm của chi nhánh
/// (sửa lỗi v1 so sánh trên giờ UTC), ngưỡng giá trị lớn chỉ áp dụng giờ cao điểm, lịch lấy từ ngữ cảnh.
/// </summary>
public class MockAlternativeDecisionStrategyTests
{
    private static readonly MockAlternativeDecisionStrategy Strategy = new(new MockAlternativeStrategyOptions());

    [Theory]
    [Trait("CatalogId", "UT-03")]
    [InlineData("2026-11-20T18:00:00+07:00", 228_000, DecisionStatus.APPROVED, "0.5", ReasonCodes.PeakHourDeposit)]
    [InlineData("2026-11-20T16:00:00+07:00", 228_000, DecisionStatus.APPROVED, "0.3", ReasonCodes.OffPeakDeposit)]
    [InlineData("2026-11-20T18:00:00+07:00", 1_500_000, DecisionStatus.NEED_APPROVAL, null, ReasonCodes.HighValueReview)]
    [InlineData("2026-11-20T18:00:00+07:00", 1_499_999, DecisionStatus.APPROVED, "0.5", ReasonCodes.PeakHourDeposit)]
    [InlineData("2026-11-20T16:00:00+07:00", 5_000_000, DecisionStatus.APPROVED, "0.3", ReasonCodes.OffPeakDeposit)]
    [InlineData("2026-11-20T11:00:00Z", 228_000, DecisionStatus.APPROVED, "0.5", ReasonCodes.PeakHourDeposit)]
    [InlineData("2026-11-21T09:00:00+07:00", 228_000, DecisionStatus.APPROVED, "0.5", ReasonCodes.PeakHourDeposit)]
    public async Task EvaluateAsync_DecisionTable_ReturnsExpectedDecision(
        string startIso, long totalPrice, DecisionStatus expectedStatus, string? expectedRate, string expectedReason)
    {
        var start = DateTimeOffset.Parse(startIso, CultureInfo.InvariantCulture);
        var reservation = V2TestData.CreateReservation(items: [V2TestData.CreateItem(start)], totalPrice: totalPrice);

        var decision = await Strategy.EvaluateAsync(reservation, V2TestData.CreateContext(), CancellationToken.None);

        decision.Status.Should().Be(expectedStatus);
        decision.ReasonCode.Should().Be(expectedReason);
        decision.DepositRate.Should().Be(expectedRate is null ? null : decimal.Parse(expectedRate, CultureInfo.InvariantCulture));
    }

    [Fact]
    [Trait("CatalogId", "UT-05")]
    public async Task EvaluateAsync_PeakReason_UsesBranchLocalTimeAndVietnameseDay()
    {
        var reservation = V2TestData.CreateReservation(
            items: [V2TestData.CreateItem(new DateTimeOffset(2026, 11, 20, 11, 0, 0, TimeSpan.Zero))]);

        var decision = await Strategy.EvaluateAsync(reservation, V2TestData.CreateContext(), CancellationToken.None);

        decision.Reasons.Should().ContainSingle()
            .Which.Should().Be("Khung giờ cao điểm (18:00 thứ Sáu, giờ chi nhánh) áp dụng cọc 50%.");
    }

    [Fact]
    [Trait("CatalogId", "UT-03")]
    public async Task EvaluateAsync_HighValueAtPeak_RecommendsApprovalAndAlternativeSlot()
    {
        var reservation = V2TestData.CreateReservation(totalPrice: 2_000_000m);

        var decision = await Strategy.EvaluateAsync(reservation, V2TestData.CreateContext(), CancellationToken.None);

        decision.Recommendations.Select(recommendation => recommendation.Type).Should().Equal(
            RecommendationType.REQUIRE_APPROVAL, RecommendationType.ALTERNATIVE_SLOT);
    }

    [Fact]
    [Trait("CatalogId", "UT-05")]
    public async Task EvaluateAsync_CalendarWithoutPeakWindows_TreatsAsOffPeak()
    {
        var context = V2TestData.CreateContext(calendar: new BranchCalendar([]));

        var decision = await Strategy.EvaluateAsync(V2TestData.CreateReservation(), context, CancellationToken.None);

        decision.ReasonCode.Should().Be(ReasonCodes.OffPeakDeposit);
    }

    [Fact]
    [Trait("CatalogId", "UT-03")]
    public void Identity_NameAndVersion_MatchV2Contract()
    {
        Strategy.StrategyName.Should().Be("MockAlternativeDecisionStrategy");
        Strategy.StrategyVersion.Should().Be("2.0.0");
    }

    [Fact]
    [Trait("CatalogId", "UT-03")]
    public void CachePolicy_DefaultOptions_EnabledForSixtyMinutes()
    {
        Strategy.CachePolicy.IsEnabled.Should().BeTrue();
        Strategy.CachePolicy.Ttl.Should().Be(TimeSpan.FromMinutes(60));
    }

    [Theory]
    [Trait("CatalogId", "UT-03")]
    [InlineData("peak-rate-above-one")]
    [InlineData("negative-threshold")]
    [InlineData("negative-ttl")]
    public void Constructor_InvalidOptions_ThrowsArgumentException(string caseName)
    {
        var options = caseName switch
        {
            "peak-rate-above-one" => new MockAlternativeStrategyOptions { PeakDepositRate = 1.5m },
            "negative-threshold" => new MockAlternativeStrategyOptions { HighValueThreshold = -1m },
            "negative-ttl" => new MockAlternativeStrategyOptions { CacheTtlMinutes = -1 },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

        var act = () => new MockAlternativeDecisionStrategy(options);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    [Trait("CatalogId", "UT-03")]
    public async Task EvaluateAsync_CancelledToken_ThrowsOperationCanceled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => Strategy.EvaluateAsync(V2TestData.CreateReservation(), V2TestData.CreateContext(), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
```

- [ ] **Step 2: Chạy để xác nhận thất bại**

Run: `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror`
Expected: FAIL — `CS0234` không tìm thấy `TADSS.Strategies.MockAlternative.V2`.

- [ ] **Step 3: Hiện thực**

Create `src/TADSS.Strategies.MockAlternative/V2/MockAlternativeStrategyOptions.cs`:

```csharp
namespace TADSS.Strategies.MockAlternative.V2;

/// <summary>
/// Tham số của <see cref="MockAlternativeDecisionStrategy"/> 2.0.0 (SOFTWARE_ARCHITECTURE_SPECIFICATION 4.3),
/// runtime bind từ <c>Tadss:Strategies:MockAlternative</c>.
/// </summary>
public sealed class MockAlternativeStrategyOptions
{
    /// <summary>Tổng tiền từ ngưỡng này trở lên vào giờ cao điểm cần Manager duyệt.</summary>
    public decimal HighValueThreshold { get; init; } = 1_500_000m;

    /// <summary>Tỷ lệ cọc giờ cao điểm.</summary>
    public decimal PeakDepositRate { get; init; } = 0.5m;

    /// <summary>Tỷ lệ cọc ngoài giờ cao điểm.</summary>
    public decimal OffPeakDepositRate { get; init; } = 0.3m;

    /// <summary>Thời gian sống của cache (phút); 0 = không cache.</summary>
    public int CacheTtlMinutes { get; init; } = 60;

    /// <summary>Kiểm tra tham số; ném <see cref="ArgumentException"/> khi không hợp lệ.</summary>
    public void Validate()
    {
        if (HighValueThreshold < 0m)
        {
            throw new ArgumentException("HighValueThreshold phải ≥ 0.", nameof(HighValueThreshold));
        }

        if (PeakDepositRate is < 0m or > 1m)
        {
            throw new ArgumentException("PeakDepositRate phải trong [0, 1].", nameof(PeakDepositRate));
        }

        if (OffPeakDepositRate is < 0m or > 1m)
        {
            throw new ArgumentException("OffPeakDepositRate phải trong [0, 1].", nameof(OffPeakDepositRate));
        }

        if (CacheTtlMinutes < 0)
        {
            throw new ArgumentException("CacheTtlMinutes phải ≥ 0.", nameof(CacheTtlMinutes));
        }
    }
}
```

Create `src/TADSS.Strategies.MockAlternative/V2/MockAlternativeDecisionStrategy.cs`:

```csharp
using System.Globalization;
using Shared.DecisionContractSDK.V2.Common;
using Shared.DecisionContractSDK.V2.Decisions;
using Shared.DecisionContractSDK.V2.Domain;
using Shared.DecisionContractSDK.V2.Enums;
using Shared.DecisionContractSDK.V2.Interfaces;

namespace TADSS.Strategies.MockAlternative.V2;

/// <summary>
/// Strategy thay thế 2.0.0 — cọc theo khung giờ của chi nhánh (SOFTWARE_ARCHITECTURE_SPECIFICATION 4.3), dùng để
/// đối chứng với <c>DefaultDecisionStrategy</c>. Giờ cao điểm lấy từ lịch trong <see cref="DecisionContext"/>.
/// Không đọc đồng hồ hệ thống, không I/O, stateless và thread-safe.
/// </summary>
public sealed class MockAlternativeDecisionStrategy : IReservationDecisionStrategy
{
    private readonly MockAlternativeStrategyOptions _options;

    /// <summary>Khởi tạo strategy với tham số đã kiểm tra.</summary>
    /// <param name="options">Tham số strategy.</param>
    /// <exception cref="ArgumentException">Khi tham số không hợp lệ.</exception>
    public MockAlternativeDecisionStrategy(MockAlternativeStrategyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _options = options;
        CachePolicy = options.CacheTtlMinutes > 0
            ? StrategyCachePolicy.Enabled(TimeSpan.FromMinutes(options.CacheTtlMinutes))
            : StrategyCachePolicy.Disabled;
    }

    /// <inheritdoc />
    public string StrategyName => nameof(MockAlternativeDecisionStrategy);

    /// <inheritdoc />
    public string StrategyVersion => "2.0.0";

    /// <inheritdoc />
    public StrategyCachePolicy CachePolicy { get; }

    /// <inheritdoc />
    public Task<StrategyDecision> EvaluateAsync(Reservation reservation, DecisionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Decide(reservation, context.Calendar));
    }

    private StrategyDecision Decide(Reservation reservation, BranchCalendar calendar)
    {
        var isPeak = calendar.IsPeak(reservation);
        var slot = DescribeSlot(reservation.ToBranchLocal(reservation.EarliestStart));

        if (isPeak && reservation.TotalPrice >= _options.HighValueThreshold)
        {
            return StrategyDecision.RequireApproval(
                ReasonCodes.HighValueReview,
                [$"Đơn giá trị lớn ({FormatAmount(reservation.TotalPrice)} {reservation.Currency}) vào giờ cao điểm ({slot}, giờ chi nhánh)."],
                new[]
                {
                    new DecisionRecommendation(RecommendationType.REQUIRE_APPROVAL, "Manager xem xét đơn giá trị lớn vào giờ cao điểm."),
                    new DecisionRecommendation(RecommendationType.ALTERNATIVE_SLOT, "Gợi ý khách chuyển sang khung giờ thấp điểm để được cọc thấp hơn.")
                });
        }

        return isPeak
            ? StrategyDecision.Approve(
                _options.PeakDepositRate,
                ReasonCodes.PeakHourDeposit,
                [$"Khung giờ cao điểm ({slot}, giờ chi nhánh) áp dụng cọc {FormatPercent(_options.PeakDepositRate)}."])
            : StrategyDecision.Approve(
                _options.OffPeakDepositRate,
                ReasonCodes.OffPeakDeposit,
                [$"Khung giờ thấp điểm ({slot}, giờ chi nhánh) áp dụng cọc {FormatPercent(_options.OffPeakDepositRate)}."]);
    }

    private static string DescribeSlot(DateTimeOffset branchLocal) =>
        $"{branchLocal.ToString("HH:mm", CultureInfo.InvariantCulture)} {VietnameseDayName(branchLocal.DayOfWeek)}";

    private static string VietnameseDayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "thứ Hai",
        DayOfWeek.Tuesday => "thứ Ba",
        DayOfWeek.Wednesday => "thứ Tư",
        DayOfWeek.Thursday => "thứ Năm",
        DayOfWeek.Friday => "thứ Sáu",
        DayOfWeek.Saturday => "thứ Bảy",
        _ => "Chủ Nhật"
    };

    private static string FormatAmount(decimal amount) => amount.ToString("#,0.##", CultureInfo.InvariantCulture);

    private static string FormatPercent(decimal rate) => (rate * 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%";
}
```

- [ ] **Step 4: Build và chạy test** — Run: `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror && dotnet test tests/TADSS.UnitTests/TADSS.UnitTests.csproj -c Release --no-build -nologo` — Expected: 0 warning; `Passed: 658`, `Failed: 0`.

- [ ] **Step 5: Commit (hỏi người dùng trước)**

```bash
git add src/TADSS.Strategies.MockAlternative/V2 tests/TADSS.UnitTests/V2/Strategies/MockAlternativeDecisionStrategyTests.cs
git commit -m "feat(strategies-v2): MockAlternativeDecisionStrategy 2.0.0 — cọc theo giờ cao điểm của chi nhánh"
```

---

### Task 8: Đồng bộ tài liệu, kiểm chứng cuối gói và điểm dừng D-06

**Files:**

- Modify: `docs/planning/BACKLOG_SPRINTS.md` (dòng T5.1, T5.2 — ghi tiến độ "5.2a xong: SDK v2 + strategies v2 song song v1, chưa nối runtime"; không đổi task khác)
- Modify: `../.claude/skills/tadss-architecture/SKILL.md` (ngoài repo): mục Contract ghi "v2 đang hiện thực ở namespace tạm `Shared.DecisionContractSDK.V2.*` (đổi về gốc ở 5.2c); enum v2 dùng converter chặt, không thêm `JsonStringEnumConverter` toàn cục"; mục Test ghi test v2 ở `tests/TADSS.UnitTests/V2/`.

- [ ] **Step 1: Cập nhật tài liệu** theo danh sách trên; giữ tiếng Việt và style hiện có.

- [ ] **Step 2: Kiểm tra link**

```bash
for f in docs/planning/BACKLOG_SPRINTS.md docs/contracts/INTEGRATION_SPEC.md spec/*.md; do d=$(dirname "$f"); grep -oE "\]\(([^)#]+)" "$f" | sed -E 's/\]\(//' | grep -v '^http' | while read l; do [ -e "$d/$l" ] || echo "HỎNG $f -> $l"; done; done; echo done
```

Expected: chỉ in `done`.

- [ ] **Step 3: Kiểm chứng cuối gói**

Run: `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror --no-incremental` → 0 warning, 0 error.
Run: `dotnet test TADSS.slnx -c Release --no-build -nologo` → `TADSS.UnitTests` Passed 658, `TADSS.IntegrationTests` Passed 32 (cần Docker), Failed 0, Skipped 0.
Run: `git diff --stat src/TADSS.Runtime.Api` → rỗng (gói này không đụng runtime).
Run: `grep -rn "JsonStringEnumConverter" src/Shared.DecisionContractSDK/V2 | grep -v "///\|<see" || echo "không đăng ký"` → `không đăng ký`.

- [ ] **Step 4: ĐIỂM DỪNG D-06 — người dùng review bề mặt SDK v2**

Tạo báo cáo cho người dùng (trong báo cáo task, không tạo file md mới):

- `git diff --stat docs/contracts/baseline_public_api.txt` (so với nhánh `develop`).
- Danh sách mọi dòng `+[...]` của baseline (kiểu public V2) gom theo namespace.
- Các quyết định wire cần người dùng xác nhận lần cuối trước khi 5.2b dựa vào: `recommendations` `{type, description}`; `DecisionDetailDto.override` là object; tín hiệu mới `memberSince`, `lateCancelledCount`, `paymentExpiredCount`, `demand`; `channel` `ONLINE | WALK_IN`.

**Dừng ở đây. Không commit, không mở PR cho tới khi người dùng xác nhận bề mặt SDK.** Nếu người dùng yêu cầu đổi tên/kiểu: sửa, chạy lại Step 3 và quy trình "Cập nhật baseline".

- [ ] **Step 5: Commit và đề xuất PR (sau khi người dùng xác nhận)**

```bash
git add docs/planning/BACKLOG_SPRINTS.md
git commit -m "docs: cập nhật tiến độ gói 5.2a (SDK v2 + strategies v2)"
```

Gợi ý PR `feat/phase-5.2a-sdk-strategies-v2` → `develop`: "Phase 5 — gói 5.2a: SDK v2 + strategies v2 (song song v1)"; mô tả gồm danh sách task, số test 475 → 658 unit, 32 integration không đổi, diff baseline (chỉ thêm kiểu V2), các quyết định wire. Người dùng tạo/merge PR; CI phải xanh trước khi merge.

---

## Ghi chú cho gói 5.2b (không làm ở 5.2a)

- `DecisionContext` do runtime dựng: `EvaluatedAtUtc` từ `TimeProvider`, `Calendar` từ `Tadss:Calendar:PeakWindows`.
- Cache v2: key gồm `strategyName`, `strategyVersion`, `optionsFingerprint`, `requestHash`. Với strategy dùng lịch (`MockAlternative`), fingerprint phải gồm cả lịch cao điểm; với strategy tương lai dùng `demand`, `demand` phải vào key cache (nhưng không vào `request_hash`) hoặc strategy khai báo `StrategyCachePolicy.Disabled`.
- Kiểm tra thời lượng item (P-01: `Tadss:Validation:MinItemMinutes/MaxItemMinutes`, `ValidateOnStart`) ở runtime, vi phạm → 400 `DOMAIN_INVARIANT_VIOLATION`.
- `TadssJson.Apply` thay cho `JsonStringEnumConverter` trong `AddJsonOptions` khi endpoint v2 xuất hiện (5.2c).
