# Gói 5.1 — Nền móng kiểm thử: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Toàn bộ integration test của TADSS chạy trên PostgreSQL 16 / Redis 7 / Toxiproxy thật qua Testcontainers (cả local lẫn CI), không còn EF InMemory trong `TADSS.IntegrationTests`, có `docker compose` E-LOCAL chạy một lệnh, và sửa hai lỗi đã phát hiện khi phân tích (V-1 Redis trong compose, V-8 mất kết nối PostgreSQL bị ánh xạ thành 400).

**Architecture:** Một `TadssInfrastructureFixture` (xUnit collection fixture) khởi động container một lần cho cả lượt chạy và tạo database mẫu đã migrate; mỗi test nhận một database riêng nhân bản từ mẫu (`CREATE DATABASE … TEMPLATE`) qua `IntegrationTestBase`. `TadssWebApplicationFactory` trỏ app tới container. Lỗi hạ tầng được tiêm bằng Toxiproxy qua REST API.

**Tech Stack:** .NET 8, xUnit 2.5.3, FluentAssertions 6.12.1, `Microsoft.AspNetCore.Mvc.Testing` 8.0.11, Testcontainers 4.15.0 (PostgreSql, Redis, Toxiproxy), Npgsql 8, Docker Compose.

**Spec:** [`PHASE5_DESIGN_SPEC.md`](PHASE5_DESIGN_SPEC.md) — Mục 4.1 (thiết kế gói 5.1), Mục 1 (D-04, D-07, D-08, D-10), Mục 2 (V-1, V-8).

## Global Constraints

- Build chuẩn: `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror` → **0 warning, 0 error** sau mỗi task.
- Version package chỉ khai báo trong `Directory.Packages.props`; csproj chỉ `<PackageReference Include="X" />`.
- Builder Testcontainers phải dùng constructor có tham số image (constructor không tham số bị `[Obsolete]` → hỏng `--warnaserror`).
- Image ghim: `postgres:16.15-alpine`, `redis:7.4-alpine`, `ghcr.io/shopify/toxiproxy:2.12.0`, `grafana/otel-lgtm:0.34.0`.
- Không viết test mới dùng `UseInMemoryDatabase`; không `Skip`; không nới assertion; không `#pragma warning disable`; không `!` null-forgiving để qua build.
- Không sửa `docs/contracts/baseline_public_api.txt` và không thêm public member vào `Shared.DecisionContractSDK` (contract v1 vẫn đóng băng tới gói 5.2).
- Không đọc/in/sửa `appsettings.Development.json`, `.env`, User Secrets. Không chạy migration lên Neon.
- Mọi test mang `[Trait("CatalogId", "<mã>")]` theo danh mục ở `docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`.
- Tên test `{Method}_{Scenario}_{Expected}`; XML summary tiếng Việt trên class test; comment tiếng Việt.
- **Commit chỉ khi người dùng cho phép** (rule `git-safety`): bước "Commit" trong plan = đề xuất message và hỏi người dùng.
- Điều kiện chạy: **Docker Desktop đang chạy** (`docker info` trả về phiên bản server).

---

## Cấu trúc file

| File | Hành động | Trách nhiệm |
| :--- | :--- | :--- |
| `Directory.Packages.props` | Sửa | Thêm `Testcontainers.PostgreSql`, `Testcontainers.Redis`, `Testcontainers.Toxiproxy` 4.15.0 |
| `tests/TADSS.IntegrationTests/TADSS.IntegrationTests.csproj` | Sửa | Thêm 3 package Testcontainers; gỡ `Microsoft.EntityFrameworkCore.InMemory` (Task 2) |
| `tests/TADSS.IntegrationTests/Infrastructure/TadssInfrastructureFixture.cs` | Tạo | Network + container + database mẫu; tạo database cho từng test; (Task 4) Toxiproxy |
| `tests/TADSS.IntegrationTests/Infrastructure/TadssInfrastructureCollection.cs` | Tạo | Định nghĩa collection xUnit dùng chung fixture |
| `tests/TADSS.IntegrationTests/Infrastructure/IntegrationTestBase.cs` | Tạo | Database + factory + client cho từng test |
| `tests/TADSS.IntegrationTests/Infrastructure/ToxiproxyControl.cs` | Tạo (Task 4) | Client REST tối giản của Toxiproxy |
| `tests/TADSS.IntegrationTests/Infrastructure/InfrastructureGuardTests.cs` | Tạo (Task 2) | Chặn tái xuất hiện EF InMemory |
| `tests/TADSS.IntegrationTests/TadssWebApplicationFactory.cs` | Sửa | Nhận connection string container; môi trường `Testing`; `ReplaceDbContext` dùng chung |
| `tests/TADSS.IntegrationTests/Migrations/MigrationIntegrationTests.cs` | Tạo | IT-15 |
| `tests/TADSS.IntegrationTests/{Cache,DecisionsEndpoint,GovernanceE2E,IdempotencyIntegration}Tests.cs`, `FaultInjection/FaultInjectionTests.cs` | Sửa | Chuyển sang `IntegrationTestBase` + `CatalogId` |
| `tests/TADSS.IntegrationTests/FaultInjection/PersistenceFaultInjectionTests.cs` | Tạo (Task 4) | FI-08 qua Toxiproxy |
| `src/TADSS.Runtime.Api/Common/RuntimeErrorCodes.cs` | Tạo (Task 3) | Mã `PERSISTENCE_UNAVAILABLE` |
| `src/TADSS.Runtime.Api/Middlewares/GlobalExceptionHandler.cs` | Sửa (Task 3) | Map lỗi PostgreSQL tạm thời → 503 |
| `tests/TADSS.UnitTests/Infrastructure/MiddlewareAndInterceptorTests.cs` | Sửa (Task 3) | UT-08 cho 503 |
| `src/TADSS.Runtime.Api/Configuration/DatabaseOptions.cs` | Tạo (Task 5) | Cờ `ApplyMigrationsOnStartup` |
| `src/TADSS.Runtime.Api/Configuration/DependencyInjection.cs`, `Program.cs`, `appsettings.json` | Sửa (Task 5) | Bind và dùng `DatabaseOptions` |
| `docker-compose.yml`, `docker/toxiproxy/toxiproxy.json`, `.env.example` | Thay / Tạo (Task 6) | Môi trường E-LOCAL |
| `.github/workflows/ci.yml` | Sửa (Task 7) | Tách bước unit / integration |
| `README.md`, `CLAUDE.md`, `docs/planning/BACKLOG_SPRINTS.md`, `docs/architecture/RESILIENCE_AND_FAULT_TAXONOMY.md`, `docs/architecture/SOFTWARE_ARCHITECTURE_SPECIFICATION.md`, `../.claude/skills/tadss-architecture/SKILL.md` | Sửa (Task 8) | Đồng bộ tài liệu |

Số test kỳ vọng sau từng task (unit / integration): T1 473/28 · T2 473/29 · T3 475/29 · T4 475/30 · T5 475/32.

---

### Task 1: Fixture Testcontainers + chuyển 27 integration test sang PostgreSQL/Redis thật

**Files:**

- Modify: `Directory.Packages.props`
- Modify: `tests/TADSS.IntegrationTests/TADSS.IntegrationTests.csproj`
- Create: `tests/TADSS.IntegrationTests/Infrastructure/TadssInfrastructureFixture.cs`
- Create: `tests/TADSS.IntegrationTests/Infrastructure/TadssInfrastructureCollection.cs`
- Create: `tests/TADSS.IntegrationTests/Infrastructure/IntegrationTestBase.cs`
- Modify: `tests/TADSS.IntegrationTests/TadssWebApplicationFactory.cs` (viết lại toàn bộ)
- Create: `tests/TADSS.IntegrationTests/Migrations/MigrationIntegrationTests.cs`
- Modify: `tests/TADSS.IntegrationTests/CacheIntegrationTests.cs:10-22`, `DecisionsEndpointTests.cs:12-24`, `GovernanceE2ETests.cs:17-32`, `IdempotencyIntegrationTests.cs:13-25`, `FaultInjection/FaultInjectionTests.cs` (khai báo class, F1–F7, `F6`)

**Interfaces:**

- Produces:
  - `TadssInfrastructureFixture`: `string RedisConnectionString { get; }`, `Task<string> CreateDatabaseAsync(bool fromTemplate = true)` (trả **tên** database), `Task DropDatabaseAsync(string database)`, `string BuildConnectionString(string database)`.
  - `TadssInfrastructureCollection.Name` = `"TadssInfrastructure"`.
  - `IntegrationTestBase(TadssInfrastructureFixture infrastructure)`: `Infrastructure`, `DatabaseName`, `ConnectionString`, `Factory` (`TadssWebApplicationFactory`), `Client` (`HttpClient`), `WebApplicationFactory<Program> CreateFactory(Action<IWebHostBuilder> configure)`, `Task<string> CreateAdditionalDatabaseAsync(bool fromTemplate = true)` (database phụ, tự xóa khi test kết thúc), `TadssDbContext CreateDbContext(string? connectionString = null)`, `virtual Task InitializeAsync()`, `virtual Task DisposeAsync()`.
  - Vòng đời database của mỗi test: tạo (nhân bản từ mẫu) → test → xóa (`DROP DATABASE … WITH (FORCE)`) trong `DisposeAsync`.
  - `TadssWebApplicationFactory(string connectionString, string redisConnectionString)`, `const string EnvironmentName = "Testing"`, `static void ReplaceDbContext(IServiceCollection services, string connectionString, params IInterceptor[] additionalInterceptors)`.

- [ ] **Step 1: Kiểm tra Docker**

Run: `docker info --format '{{.ServerVersion}}'`
Expected: in ra phiên bản (ví dụ `29.x`). Nếu lỗi `failed to connect to the docker API` → yêu cầu người dùng bật Docker Desktop, dừng lại.

- [ ] **Step 2: Thêm package**

Trong `Directory.Packages.props`, nhóm `<!-- Test Packages -->`, thêm sau dòng `GitHubActionsTestLogger`:

```xml
    <PackageVersion Include="Testcontainers.PostgreSql" Version="4.15.0" />
    <PackageVersion Include="Testcontainers.Redis" Version="4.15.0" />
    <PackageVersion Include="Testcontainers.Toxiproxy" Version="4.15.0" />
```

Trong `tests/TADSS.IntegrationTests/TADSS.IntegrationTests.csproj`, trong `ItemGroup` chứa `PackageReference`, thêm:

```xml
    <PackageReference Include="Testcontainers.PostgreSql" />
    <PackageReference Include="Testcontainers.Redis" />
    <PackageReference Include="Testcontainers.Toxiproxy" />
```

(Giữ `Microsoft.EntityFrameworkCore.InMemory` ở task này; gỡ ở Task 2.)

- [ ] **Step 3: Viết test IT-15 (test thất bại)**

Create `tests/TADSS.IntegrationTests/Migrations/MigrationIntegrationTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using TADSS.IntegrationTests.Infrastructure;
using TADSS.Runtime.Api.Infrastructure.Data;
using Xunit;

namespace TADSS.IntegrationTests.Migrations;

/// <summary>
/// IT-15: migration EF Core áp dụng được trên PostgreSQL trống và model snapshot khớp model hiện tại
/// (không có thay đổi model nào chưa được tạo migration).
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
public class MigrationIntegrationTests : IntegrationTestBase
{
    public MigrationIntegrationTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    [Fact]
    [Trait("CatalogId", "IT-15")]
    public async Task Migrate_EmptyDatabase_AppliesAllMigrationsWithoutPendingModelChanges()
    {
        // Database của test được nhân bản từ template mà fixture đã migrate trên DB trống
        await using var db = CreateDbContext();

        var applied = await db.Database.GetAppliedMigrationsAsync();

        applied.Should().Equal(db.Database.GetMigrations());
        HasPendingModelChanges(db).Should().BeFalse();
    }

    private static bool HasPendingModelChanges(TadssDbContext db)
    {
        var snapshotModel = db.GetService<IMigrationsAssembly>().ModelSnapshot?.Model;
        if (snapshotModel is IMutableModel mutableModel)
        {
            snapshotModel = mutableModel.FinalizeModel();
        }

        if (snapshotModel is not null)
        {
            snapshotModel = db.GetService<IModelRuntimeInitializer>().Initialize(snapshotModel);
        }

        var designTimeModel = db.GetService<IDesignTimeModel>().Model;
        return db.GetService<IMigrationsModelDiffer>()
            .HasDifferences(snapshotModel?.GetRelationalModel(), designTimeModel.GetRelationalModel());
    }
}
```

- [ ] **Step 4: Chạy để xác nhận thất bại**

Run: `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror`
Expected: FAIL — `CS0246` không tìm thấy `TadssInfrastructureCollection`, `IntegrationTestBase`, `TadssInfrastructureFixture`.

- [ ] **Step 5: Tạo fixture, collection, base, factory**

Create `tests/TADSS.IntegrationTests/Infrastructure/TadssInfrastructureFixture.cs`:

```csharp
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TADSS.Runtime.Api.Infrastructure.Data;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace TADSS.IntegrationTests.Infrastructure;

/// <summary>
/// Hạ tầng thật dùng chung cho cả lượt chạy integration test: một Docker network, PostgreSQL 16 và Redis 7.
/// Áp migration một lần vào database mẫu; mỗi test nhân bản database riêng từ mẫu để cô lập dữ liệu.
/// </summary>
public sealed class TadssInfrastructureFixture : IAsyncLifetime
{
    public const string PostgresImage = "postgres:16.15-alpine";
    public const string RedisImage = "redis:7.4-alpine";
    private const string TemplateDatabase = "tadss_template";

    private readonly INetwork _network;
    private readonly PostgreSqlContainer _postgres;
    private readonly RedisContainer _redis;

    public TadssInfrastructureFixture()
    {
        _network = new NetworkBuilder().Build();
        _postgres = new PostgreSqlBuilder(PostgresImage)
            .WithNetwork(_network)
            .WithNetworkAliases("postgres")
            .Build();
        _redis = new RedisBuilder(RedisImage)
            .WithNetwork(_network)
            .WithNetworkAliases("redis")
            .Build();
    }

    /// <summary>Chuỗi kết nối Redis (host:port) trỏ thẳng tới container.</summary>
    public string RedisConnectionString => _redis.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _network.CreateAsync();
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        await ExecuteAdminAsync($"CREATE DATABASE {TemplateDatabase}");

        var options = new DbContextOptionsBuilder<TadssDbContext>()
            .UseNpgsql(BuildConnectionString(TemplateDatabase),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "tadss"))
            .Options;

        await using (var db = new TadssDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        // CREATE DATABASE ... TEMPLATE yêu cầu không còn kết nối nào tới database mẫu
        NpgsqlConnection.ClearAllPools();
    }

    /// <summary>Tạo database mới (mặc định nhân bản từ mẫu đã migrate) và trả về tên database.</summary>
    public async Task<string> CreateDatabaseAsync(bool fromTemplate = true)
    {
        var name = $"tadss_test_{Guid.NewGuid():N}";
        var sql = fromTemplate
            ? $"CREATE DATABASE {name} TEMPLATE {TemplateDatabase}"
            : $"CREATE DATABASE {name}";
        await ExecuteAdminAsync(sql);
        return name;
    }

    /// <summary>
    /// Xóa database của một test. <c>WITH (FORCE)</c> (PostgreSQL ≥ 13) ngắt các kết nối còn mở,
    /// kể cả kết nối đi qua Toxiproxy.
    /// </summary>
    public async Task DropDatabaseAsync(string database)
    {
        // Bỏ các kết nối trong pool của database này để không giữ kết nối chết sau khi xóa
        await using (var pooled = new NpgsqlConnection(BuildConnectionString(database)))
        {
            NpgsqlConnection.ClearPool(pooled);
        }

        await ExecuteAdminAsync($"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
    }

    /// <summary>Chuỗi kết nối trực tiếp tới một database trong container PostgreSQL.</summary>
    public string BuildConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = database }.ConnectionString;

    private async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await _redis.DisposeAsync();
        await _postgres.DisposeAsync();
        await _network.DisposeAsync();
    }
}
```

Create `tests/TADSS.IntegrationTests/Infrastructure/TadssInfrastructureCollection.cs`:

```csharp
using Xunit;

namespace TADSS.IntegrationTests.Infrastructure;

/// <summary>
/// Collection xUnit dùng chung <see cref="TadssInfrastructureFixture"/>: container khởi động một lần,
/// các test class trong collection chạy tuần tự (an toàn khi test bật/tắt Toxiproxy).
/// </summary>
[CollectionDefinition(Name)]
public class TadssInfrastructureCollection : ICollectionFixture<TadssInfrastructureFixture>
{
    public const string Name = "TadssInfrastructure";
}
```

Create `tests/TADSS.IntegrationTests/Infrastructure/IntegrationTestBase.cs`:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using TADSS.Runtime.Api.Infrastructure.Data;
using Xunit;

namespace TADSS.IntegrationTests.Infrastructure;

/// <summary>
/// Lớp cơ sở cho integration test: mỗi test nhận một database PostgreSQL riêng (nhân bản từ mẫu đã migrate),
/// một <see cref="TadssWebApplicationFactory"/> trỏ tới container thật và một <see cref="HttpClient"/>.
/// Vòng đời database: tạo → test → xóa.
/// </summary>
public abstract class IntegrationTestBase : IAsyncLifetime
{
    private readonly List<string> _ownedDatabases = new();
    private TadssWebApplicationFactory? _factory;
    private HttpClient? _client;

    protected IntegrationTestBase(TadssInfrastructureFixture infrastructure)
    {
        Infrastructure = infrastructure;
    }

    protected TadssInfrastructureFixture Infrastructure { get; }

    protected string DatabaseName { get; private set; } = string.Empty;

    protected string ConnectionString { get; private set; } = string.Empty;

    protected TadssWebApplicationFactory Factory =>
        _factory ?? throw new InvalidOperationException("Factory chỉ dùng được sau InitializeAsync.");

    protected HttpClient Client => _client ??= Factory.CreateClient();

    /// <summary>Tạo factory dẫn xuất để thay DI cho riêng một test; được giải phóng cùng <see cref="Factory"/>.</summary>
    protected WebApplicationFactory<Program> CreateFactory(Action<IWebHostBuilder> configure) =>
        Factory.WithWebHostBuilder(configure);

    /// <summary>Tạo thêm một database cho riêng test này (ví dụ database trống); được xóa trong <see cref="DisposeAsync"/>.</summary>
    protected async Task<string> CreateAdditionalDatabaseAsync(bool fromTemplate = true)
    {
        var name = await Infrastructure.CreateDatabaseAsync(fromTemplate);
        _ownedDatabases.Add(name);
        return name;
    }

    /// <summary>DbContext đọc trực tiếp database của test (mặc định) hoặc một database khác.</summary>
    protected TadssDbContext CreateDbContext(string? connectionString = null)
    {
        var options = new DbContextOptionsBuilder<TadssDbContext>()
            .UseNpgsql(connectionString ?? ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "tadss"))
            .Options;
        return new TadssDbContext(options);
    }

    public virtual async Task InitializeAsync()
    {
        DatabaseName = await CreateAdditionalDatabaseAsync();
        ConnectionString = Infrastructure.BuildConnectionString(DatabaseName);
        _factory = new TadssWebApplicationFactory(ConnectionString, Infrastructure.RedisConnectionString);
    }

    public virtual async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            // WebApplicationFactory tự giải phóng các factory dẫn xuất từ WithWebHostBuilder
            await _factory.DisposeAsync();
        }

        // Xóa sau khi app đã dừng để không còn kết nối mới tới database
        foreach (var database in _ownedDatabases)
        {
            await Infrastructure.DropDatabaseAsync(database);
        }
    }
}
```

Replace toàn bộ `tests/TADSS.IntegrationTests/TadssWebApplicationFactory.cs`:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TADSS.Runtime.Api.Infrastructure.Caching;
using TADSS.Runtime.Api.Infrastructure.Data;
using TADSS.Runtime.Api.Infrastructure.Data.Interceptors;

namespace TADSS.IntegrationTests;

/// <summary>
/// Host TADSS cho integration test, trỏ tới PostgreSQL và Redis thật (container Testcontainers).
/// Chạy ở môi trường <c>Testing</c> nên không nạp <c>appsettings.Development.json</c> hay User Secrets.
/// </summary>
public class TadssWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string EnvironmentName = "Testing";

    private readonly string _connectionString;
    private readonly string _redisConnectionString;

    public TadssWebApplicationFactory(string connectionString, string redisConnectionString)
    {
        _connectionString = connectionString;
        _redisConnectionString = redisConnectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.ConfigureServices(services =>
        {
            ReplaceDbContext(services, _connectionString);
            services.PostConfigure<TadssCacheOptions>(options =>
            {
                options.Provider = "Redis";
                options.RedisConnectionString = _redisConnectionString;
            });
        });
    }

    /// <summary>
    /// Thay đăng ký DbContext của app bằng Npgsql trỏ tới <paramref name="connectionString"/>,
    /// giữ <see cref="AuditTimestampInterceptor"/> và thêm interceptor riêng của test nếu có.
    /// </summary>
    public static void ReplaceDbContext(IServiceCollection services, string connectionString, params IInterceptor[] additionalInterceptors)
    {
        services.RemoveAll<DbContextOptions<TadssDbContext>>();
        services.AddDbContext<TadssDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "tadss"));
            options.AddInterceptors(sp.GetRequiredService<AuditTimestampInterceptor>());
            options.AddInterceptors(additionalInterceptors);
        });
    }
}
```

- [ ] **Step 6: Build lại — xác nhận các test cũ phải chuyển**

Run: `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror`
Expected: FAIL — các test class cũ báo lỗi biên dịch vì `TadssWebApplicationFactory` không còn constructor không tham số (`CS7036`). Đó là dấu hiệu cần Step 7.

- [ ] **Step 7: Chuyển 5 test class sang `IntegrationTestBase` và gắn `CatalogId`**

Với `CacheIntegrationTests`, `DecisionsEndpointTests`, `IdempotencyIntegrationTests`: thay phần khai báo class + field `_client` + constructor bằng mẫu sau (đổi tên class tương ứng), giữ nguyên `JsonOptions`, rồi thay mọi `_client` trong file bằng `Client`:

```csharp
[Collection(TadssInfrastructureCollection.Name)]
public class CacheIntegrationTests : IntegrationTestBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public CacheIntegrationTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }
```

Với `GovernanceE2ETests`: cùng mẫu, xóa thêm field `_factory`; thay `_factory.Services` → `Factory.Services`, `_client` → `Client`.

Thêm `using TADSS.IntegrationTests.Infrastructure;` vào cả 5 file.

Với `FaultInjection/FaultInjectionTests.cs`:

- Khai báo class: `[Collection(TadssInfrastructureCollection.Name)] public class FaultInjectionTests : IntegrationTestBase` + constructor `public FaultInjectionTests(TadssInfrastructureFixture infrastructure) : base(infrastructure) { }`.
- Thay mọi `new TadssWebApplicationFactory().WithWebHostBuilder(builder =>` bằng `CreateFactory(builder =>`.
- Trong `F6_DatabasePersistenceFailure_...`: xóa biến `dbName` và thân `ConfigureServices` cũ (gỡ `DbContextOptions` + `UseInMemoryDatabase`), thay bằng:

```csharp
        var factory = CreateFactory(builder =>
        {
            builder.ConfigureServices(services =>
                TadssWebApplicationFactory.ReplaceDbContext(services, ConnectionString, new FailingSaveChangesInterceptor()));
        });
```

Gắn `[Trait("CatalogId", …)]` ngay dưới `[Fact]` theo bảng (mã gần nhất trong danh mục v2; các test này được viết lại theo v2 ở gói 5.2–5.4):

| File | Test | CatalogId |
| :--- | :--- | :--- |
| `CacheIntegrationTests` | `PostDecision_DifferentIdempotencyKeysSamePayload_…`, `PostDecision_SameIdempotencyKeySamePayload_TakesPrecedence…` | IT-13, IT-13 |
| `DecisionsEndpointTests` | `PostDecision_ValidRequest_…` | IT-01 |
| | `PostDecision_MissingIdempotencyHeader_…` | IT-11 |
| | `GetDecisionDetail_ExistingDecision_…` | IT-01 |
| | `OverrideDecision_EligibleDecision_…` | IT-06 |
| | `EvaluateDecision_IdempotencyKeyExceeds128Chars_…` | IT-11 |
| | `PostDecision_InvalidWireEnum_…` | UT-02 |
| `GovernanceE2ETests` | 3 test `CorrelationId_…` | IT-01 |
| | `PostDecision_MissingIdempotencyKey_…` | IT-11 |
| | `GetDecisionDetail_ExistingDecision_…`, `GetDecisionDetail_NonExistentGuid_…` | IT-01 |
| | `GetPendingApprovals_FiltersOnly…`, `GetPendingApprovals_PaginationParameters_…` | IT-09 |
| | `OverrideDecision_PreservesMachineSnapshotImmutability_…` | IT-06 |
| `IdempotencyIntegrationTests` | `…SameIdempotencyKeySamePayload_Returns200…`, `…ReplayAfterOverride_…` | IT-02 |
| | `…SameIdempotencyKeyDifferentPayload_Returns409Conflict` | IT-03 |
| `FaultInjectionTests` | F1, F2, F3, F4, F5, F6, F7 | FI-01, FI-02, FI-03, FI-04, FI-06, FI-08, FI-10 |

- [ ] **Step 8: Build và chạy toàn bộ integration test**

Run: `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror`
Expected: 0 Warning(s), 0 Error(s).

Run: `dotnet test tests/TADSS.IntegrationTests/TADSS.IntegrationTests.csproj -c Release --no-build -nologo`
Expected: `Passed: 28` (27 cũ + IT-15), `Failed: 0`. Lần đầu chạy kéo image nên mất vài phút.

- [ ] **Step 9: Nếu có test fail — sửa tận gốc**

Test fail ở bước này là lỗi mà EF InMemory đã che (constraint, kiểu `timestamptz`, `jsonb`, Redis thật). Với mỗi test fail:

1. Đọc lỗi đầu tiên; xác định nguyên nhân nằm ở mã nguồn (`src/`) hay ở giả định của test về InMemory.
2. Lỗi ở `src/` → sửa mã nguồn, không sửa assertion. Giả định InMemory sai (ví dụ test đọc dữ liệu của test khác) → sửa phần sắp xếp dữ liệu của test, giữ nguyên điều được kiểm chứng.
3. Ghi một dòng vào mục "Phát hiện khi chuyển hạ tầng" trong mô tả PR (test, nguyên nhân, cách sửa).
4. Chạy lại Step 8 cho tới khi 28/28 đạt.

- [ ] **Step 10: Chạy unit test (không được ảnh hưởng)**

Run: `dotnet test tests/TADSS.UnitTests/TADSS.UnitTests.csproj -c Release --no-build -nologo`
Expected: `Passed: 473`, `Failed: 0`.

- [ ] **Step 11: Commit (hỏi người dùng trước)**

```bash
git add Directory.Packages.props tests/TADSS.IntegrationTests
git commit -m "test(integration): chạy integration test trên PostgreSQL 16 và Redis 7 thật qua Testcontainers"
```

---

### Task 2: Gỡ EF InMemory khỏi integration test + test chặn tái xuất hiện

**Files:**

- Create: `tests/TADSS.IntegrationTests/Infrastructure/InfrastructureGuardTests.cs`
- Modify: `tests/TADSS.IntegrationTests/TADSS.IntegrationTests.csproj`

**Interfaces:**

- Consumes: không.
- Produces: không.

- [ ] **Step 1: Viết test chặn (test thất bại)**

Create `tests/TADSS.IntegrationTests/Infrastructure/InfrastructureGuardTests.cs`:

```csharp
using FluentAssertions;
using Xunit;

namespace TADSS.IntegrationTests.Infrastructure;

/// <summary>
/// Cổng chất lượng G3: integration test chỉ chạy trên hạ tầng thật — provider EF Core InMemory
/// không được có mặt trong output của project integration test.
/// </summary>
public class InfrastructureGuardTests
{
    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void IntegrationTestOutput_DoesNotContainEfCoreInMemoryProvider()
    {
        var inMemoryProvider = Type.GetType(
            "Microsoft.EntityFrameworkCore.InMemoryDbContextOptionsExtensions, Microsoft.EntityFrameworkCore.InMemory",
            throwOnError: false);

        inMemoryProvider.Should().BeNull("integration test phải chạy trên PostgreSQL thật qua Testcontainers");
    }
}
```

- [ ] **Step 2: Chạy để xác nhận thất bại**

Run: `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror && dotnet test tests/TADSS.IntegrationTests/TADSS.IntegrationTests.csproj -c Release --no-build -nologo --filter "FullyQualifiedName~InfrastructureGuardTests"`
Expected: FAIL — `Expected inMemoryProvider to be <null> … but found Microsoft.EntityFrameworkCore.InMemoryDbContextOptionsExtensions`.

- [ ] **Step 3: Gỡ package**

Trong `tests/TADSS.IntegrationTests/TADSS.IntegrationTests.csproj` xóa dòng:

```xml
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" />
```

(Không xóa `PackageVersion` trong `Directory.Packages.props` — `TADSS.UnitTests` và `TADSS.BenchmarkRunner` còn dùng tới gói 5.2.)

- [ ] **Step 4: Build sạch và chạy lại**

Run: `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror --no-incremental`
Expected: 0 Warning(s), 0 Error(s). Nếu báo `UseInMemoryDatabase` còn được dùng → còn sót ở Task 1 Step 7, sửa tại chỗ.

Run: `dotnet test tests/TADSS.IntegrationTests/TADSS.IntegrationTests.csproj -c Release --no-build -nologo`
Expected: `Passed: 29`, `Failed: 0`.

- [ ] **Step 5: Commit (hỏi người dùng trước)**

```bash
git add tests/TADSS.IntegrationTests
git commit -m "test(integration): gỡ EF Core InMemory và thêm test chặn tái xuất hiện"
```

---

### Task 3: Lỗi PostgreSQL tạm thời → 503 `PERSISTENCE_UNAVAILABLE` (UT-08, sửa V-8 phần ánh xạ)

**Files:**

- Create: `src/TADSS.Runtime.Api/Common/RuntimeErrorCodes.cs`
- Modify: `src/TADSS.Runtime.Api/Middlewares/GlobalExceptionHandler.cs`
- Test: `tests/TADSS.UnitTests/Infrastructure/MiddlewareAndInterceptorTests.cs` (region `3. GlobalExceptionHandler Unit Tests`)

**Interfaces:**

- Produces: `RuntimeErrorCodes.PersistenceUnavailable` = `"PERSISTENCE_UNAVAILABLE"` (Task 4 dùng).

- [ ] **Step 1: Viết test thất bại**

Trong `MiddlewareAndInterceptorTests.cs` thêm `using System.Net.Sockets;` và `using Npgsql;` ở đầu file; thêm vào cuối region 3 (trước `#endregion` của region này):

```csharp
    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task GlobalExceptionHandler_TransientNpgsqlFailureWrappedByEfCore_Returns503PersistenceUnavailable()
    {
        // Npgsql execution strategy mặc định bọc lỗi kết nối tạm thời trong InvalidOperationException
        var connectionFailure = new NpgsqlException(
            "Failed to connect to 10.0.0.5:5432",
            new SocketException((int)SocketError.ConnectionRefused));
        var wrapped = new InvalidOperationException(
            "An exception has been raised that is likely due to a transient failure.", connectionFailure);

        var (statusCode, body) = await ExecuteExceptionHandlerAsync(wrapped);

        statusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        body.GetProperty("code").GetString().Should().Be(RuntimeErrorCodes.PersistenceUnavailable);
        body.GetProperty("retryAfterSeconds").GetInt32().Should().Be(5);
        body.GetProperty("detail").GetString().Should().NotContain("10.0.0.5");
    }

    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task GlobalExceptionHandler_NonTransientPostgresError_DoesNotMapTo503()
    {
        var uniqueViolation = new DbUpdateException(
            "Unique violation",
            new PostgresException("duplicate key value", "ERROR", "ERROR", "23505"));

        var (statusCode, _) = await ExecuteExceptionHandlerAsync(uniqueViolation);

        statusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }
```

- [ ] **Step 2: Chạy để xác nhận thất bại**

Run: `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror`
Expected: FAIL — `CS0103: The name 'RuntimeErrorCodes' does not exist`.

- [ ] **Step 3: Hiện thực**

Create `src/TADSS.Runtime.Api/Common/RuntimeErrorCodes.cs`:

```csharp
namespace TADSS.Runtime.Api.Common;

/// <summary>
/// Mã lỗi của runtime chưa có trong contract SDK v1 (đóng băng). Chuyển vào <c>ErrorCodes</c> của SDK v2 ở gói 5.2.
/// </summary>
public static class RuntimeErrorCodes
{
    /// <summary>Không đọc/ghi được audit do PostgreSQL tạm thời không khả dụng (fail-closed).</summary>
    public const string PersistenceUnavailable = "PERSISTENCE_UNAVAILABLE";
}
```

Trong `GlobalExceptionHandler.cs`:

- Thêm `using Npgsql;` và `using TADSS.Runtime.Api.Common;`.
- Thêm nhánh **đầu tiên** trong `exception switch` (trước `DomainInvariantException`):

```csharp
            Exception ex when IsPersistenceUnavailable(ex) => (StatusCodes.Status503ServiceUnavailable, "Persistence Unavailable", RuntimeErrorCodes.PersistenceUnavailable),
```

- Thay khối tính `detail` bằng:

```csharp
        var detail = statusCode switch
        {
            StatusCodes.Status500InternalServerError => "An unexpected internal server error occurred.",
            StatusCodes.Status503ServiceUnavailable => "Decision audit storage is temporarily unavailable. Please retry later.",
            _ when exception is DbUpdateConcurrencyException => "The target decision record was modified concurrently by another request. Please refresh and retry.",
            _ => exception.Message
        };
```

- Thay điều kiện thêm `retryAfterSeconds`:

```csharp
        if (statusCode is StatusCodes.Status504GatewayTimeout or StatusCodes.Status503ServiceUnavailable)
        {
            problemDetails.Extensions["retryAfterSeconds"] = 5;
        }
```

- Thêm phương thức cuối class:

```csharp
    /// <summary>
    /// Lỗi tạm thời của PostgreSQL (mất kết nối, timeout, server quá tải) ở bất kỳ tầng InnerException nào.
    /// EF Core/Npgsql có thể bọc lỗi này trong <see cref="InvalidOperationException"/> hoặc <see cref="DbUpdateException"/>.
    /// </summary>
    private static bool IsPersistenceUnavailable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is NpgsqlException { IsTransient: true })
            {
                return true;
            }
        }

        return false;
    }
```

- [ ] **Step 4: Chạy test**

Run: `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror && dotnet test tests/TADSS.UnitTests/TADSS.UnitTests.csproj -c Release --no-build -nologo`
Expected: 0 warning; `Passed: 475`, `Failed: 0`.

Run: `dotnet test tests/TADSS.IntegrationTests/TADSS.IntegrationTests.csproj -c Release --no-build -nologo`
Expected: `Passed: 29` (F6 vẫn 500 vì `DbUpdateException` của interceptor không chứa `NpgsqlException`).

- [ ] **Step 5: Commit (hỏi người dùng trước)**

```bash
git add src/TADSS.Runtime.Api/Common/RuntimeErrorCodes.cs src/TADSS.Runtime.Api/Middlewares/GlobalExceptionHandler.cs tests/TADSS.UnitTests/Infrastructure/MiddlewareAndInterceptorTests.cs
git commit -m "fix(runtime): trả 503 PERSISTENCE_UNAVAILABLE khi PostgreSQL tạm thời không khả dụng"
```

---

### Task 4: Toxiproxy trong fixture + FI-08 trên hạ tầng thật

**Files:**

- Create: `tests/TADSS.IntegrationTests/Infrastructure/ToxiproxyControl.cs`
- Modify: `tests/TADSS.IntegrationTests/Infrastructure/TadssInfrastructureFixture.cs`
- Create: `tests/TADSS.IntegrationTests/FaultInjection/PersistenceFaultInjectionTests.cs`

**Interfaces:**

- Consumes: `RuntimeErrorCodes.PersistenceUnavailable` (Task 3); `IntegrationTestBase` (Task 1).
- Produces (gói 5.5 dùng cho FI-06, FI-07, FI-09):
  - `TadssInfrastructureFixture.ToxiproxyImage`, `PostgresProxy` = `"postgres"`, `RedisProxy` = `"redis"`, `ToxiproxyControl Toxiproxy { get; }`, `string BuildProxiedConnectionString(string database)`, `string ProxiedRedisConnectionString { get; }`.
  - `ToxiproxyControl(Uri apiBaseAddress)`: `Task CreateProxyAsync(string name, int listenPort, string upstream)`, `Task SetEnabledAsync(string name, bool enabled)`, `Task ResetAsync()`.

- [ ] **Step 1: Viết test FI-08 (test thất bại)**

Create `tests/TADSS.IntegrationTests/FaultInjection/PersistenceFaultInjectionTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Shared.DecisionContractSDK.DTOs.Decisions;
using TADSS.IntegrationTests.Infrastructure;
using TADSS.Runtime.Api.Common;
using Xunit;

namespace TADSS.IntegrationTests.FaultInjection;

/// <summary>
/// FI-08: PostgreSQL mất kết nối (Toxiproxy tắt proxy) → TADSS fail-closed với 503 PERSISTENCE_UNAVAILABLE,
/// không trả quyết định và không để lại bản ghi audit dở dang.
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
public class PersistenceFaultInjectionTests : IntegrationTestBase
{
    public PersistenceFaultInjectionTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    public override async Task DisposeAsync()
    {
        // Luôn khôi phục proxy để không ảnh hưởng test khác trong collection
        await Infrastructure.Toxiproxy.ResetAsync();
        await base.DisposeAsync();
    }

    [Fact]
    [Trait("CatalogId", "FI-08")]
    public async Task PostDecision_PostgresConnectionLost_Returns503PersistenceUnavailableWithoutAuditRecord()
    {
        // Arrange: app kết nối PostgreSQL qua Toxiproxy
        await using var proxiedFactory = new TadssWebApplicationFactory(
            Infrastructure.BuildProxiedConnectionString(DatabaseName),
            Infrastructure.RedisConnectionString);
        using var client = proxiedFactory.CreateClient();

        var healthyKey = "FI08-OK-" + Guid.NewGuid();
        using var healthyResponse = await PostDecisionAsync(client, healthyKey);
        healthyResponse.StatusCode.Should().Be(HttpStatusCode.OK, "đường đi qua Toxiproxy phải hoạt động trước khi tiêm lỗi");

        // Act: cắt kết nối PostgreSQL
        await Infrastructure.Toxiproxy.SetEnabledAsync(TadssInfrastructureFixture.PostgresProxy, enabled: false);
        var failedKey = "FI08-DOWN-" + Guid.NewGuid();
        using var failedResponse = await PostDecisionAsync(client, failedKey);

        // Assert: fail-closed
        failedResponse.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        failedResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var problem = await failedResponse.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be(RuntimeErrorCodes.PersistenceUnavailable);

        // Khôi phục và kiểm tra không có bản ghi dở dang
        await Infrastructure.Toxiproxy.ResetAsync();
        await using var db = CreateDbContext();
        (await db.DecisionExecutions.CountAsync(e => e.IdempotencyKey == healthyKey)).Should().Be(1);
        (await db.DecisionExecutions.CountAsync(e => e.IdempotencyKey == failedKey)).Should().Be(0);
    }

    private static async Task<HttpResponseMessage> PostDecisionAsync(HttpClient client, string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/decisions")
        {
            Content = JsonContent.Create(new DecisionRequestDto
            {
                ReservationId = Guid.NewGuid(),
                CustomerId = Guid.NewGuid(),
                CustomerTier = "VIP",
                CourtId = Guid.NewGuid(),
                BranchId = Guid.NewGuid(),
                CourtTypeId = null,
                StartTime = DateTime.UtcNow.AddHours(2),
                EndTime = DateTime.UtcNow.AddHours(4),
                TotalPrice = 300000m,
                DepositAmount = 150000m,
                PaymentStatus = "PARTIALLY_PAID",
                BookingType = "SINGLE",
                Priority = "NORMAL",
                RequestedAt = DateTime.UtcNow
            })
        };
        request.Headers.Add("X-Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }
}
```

- [ ] **Step 2: Chạy để xác nhận thất bại**

Run: `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror`
Expected: FAIL — `CS1061: 'TadssInfrastructureFixture' does not contain a definition for 'Toxiproxy'` (và `BuildProxiedConnectionString`, `PostgresProxy`).

- [ ] **Step 3: Tạo `ToxiproxyControl`**

Create `tests/TADSS.IntegrationTests/Infrastructure/ToxiproxyControl.cs`:

```csharp
using System.Net.Http.Json;

namespace TADSS.IntegrationTests.Infrastructure;

/// <summary>
/// Client tối giản cho REST API của Toxiproxy (cổng điều khiển 8474). Module Testcontainers.Toxiproxy
/// chỉ dựng container nên test tự gọi API để tạo proxy, bật/tắt và reset.
/// </summary>
public sealed class ToxiproxyControl : IDisposable
{
    private readonly HttpClient _http;

    public ToxiproxyControl(Uri apiBaseAddress)
    {
        _http = new HttpClient { BaseAddress = apiBaseAddress };
    }

    public async Task CreateProxyAsync(string name, int listenPort, string upstream)
    {
        using var response = await _http.PostAsJsonAsync("/proxies",
            new { name, listen = $"0.0.0.0:{listenPort}", upstream, enabled = true });
        response.EnsureSuccessStatusCode();
    }

    public async Task SetEnabledAsync(string name, bool enabled)
    {
        using var response = await _http.PostAsJsonAsync($"/proxies/{name}", new { enabled });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Bật lại mọi proxy và gỡ mọi toxic.</summary>
    public async Task ResetAsync()
    {
        using var response = await _http.PostAsync("/reset", content: null);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose() => _http.Dispose();
}
```

- [ ] **Step 4: Thêm Toxiproxy vào fixture**

Trong `TadssInfrastructureFixture.cs`:

- Thêm `using Testcontainers.Toxiproxy;`.
- Thêm hằng và field:

```csharp
    public const string ToxiproxyImage = "ghcr.io/shopify/toxiproxy:2.12.0";
    public const string PostgresProxy = "postgres";
    public const string RedisProxy = "redis";
    private const int PostgresProxyPort = 8666;
    private const int RedisProxyPort = 8667;

    private readonly ToxiproxyContainer _toxiproxy;
    private ToxiproxyControl? _toxiproxyControl;
```

- Trong constructor, sau `_redis = …`:

```csharp
        _toxiproxy = new ToxiproxyBuilder(ToxiproxyImage)
            .WithNetwork(_network)
            .WithPortBinding(PostgresProxyPort, true)
            .WithPortBinding(RedisProxyPort, true)
            .Build();
```

- Thêm thuộc tính:

```csharp
    /// <summary>Điều khiển Toxiproxy để tiêm lỗi mạng tới PostgreSQL/Redis.</summary>
    public ToxiproxyControl Toxiproxy =>
        _toxiproxyControl ?? throw new InvalidOperationException("Toxiproxy chỉ dùng được sau InitializeAsync.");

    /// <summary>Chuỗi kết nối Redis đi qua Toxiproxy.</summary>
    public string ProxiedRedisConnectionString =>
        $"{_toxiproxy.Hostname}:{_toxiproxy.GetMappedPublicPort(RedisProxyPort)}";

    /// <summary>Chuỗi kết nối PostgreSQL đi qua Toxiproxy; timeout kết nối 5 giây để lỗi được phát hiện nhanh.</summary>
    public string BuildProxiedConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Host = _toxiproxy.Hostname,
            Port = _toxiproxy.GetMappedPublicPort(PostgresProxyPort),
            Database = database,
            Timeout = 5
        }.ConnectionString;
```

- Trong `InitializeAsync`, thay `await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());` bằng:

```csharp
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync(), _toxiproxy.StartAsync());

        _toxiproxyControl = new ToxiproxyControl(new Uri(
            $"http://{_toxiproxy.Hostname}:{_toxiproxy.GetMappedPublicPort(ToxiproxyBuilder.ToxiproxyControlPort)}"));
        await _toxiproxyControl.CreateProxyAsync(PostgresProxy, PostgresProxyPort, "postgres:5432");
        await _toxiproxyControl.CreateProxyAsync(RedisProxy, RedisProxyPort, "redis:6379");
        await VerifyProxiedPathsAsync();
```

- Thêm `using StackExchange.Redis;` (package có sẵn qua project reference tới `TADSS.Runtime.Api`) và method kiểm tra đường đi qua proxy. Đường không thông thì dừng cả lượt chạy với thông báo rõ, thay vì để mọi test FI lỗi theo kiểu khó hiểu. Chiều ngược lại (tắt proxy thì mất kết nối) được FI-08 chứng minh cho PostgreSQL và FI-06 (gói 5.5) cho Redis:

```csharp
    /// <summary>
    /// Kiểm tra máy chạy test đi được tới PostgreSQL và Redis qua Toxiproxy
    /// (host → cổng ánh xạ của Toxiproxy → container upstream trong Docker network).
    /// </summary>
    private async Task VerifyProxiedPathsAsync()
    {
        try
        {
            // Dùng database hệ thống "postgres" để không giữ kết nối tới database mẫu
            await using var connection = new NpgsqlConnection(BuildProxiedConnectionString("postgres"));
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync();

            await using var redis = await ConnectionMultiplexer.ConnectAsync(ProxiedRedisConnectionString);
            await redis.GetDatabase().PingAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Không đi được tới PostgreSQL/Redis qua Toxiproxy — kiểm tra cấu hình proxy và Docker network.", ex);
        }
    }
```

- Trong `DisposeAsync`, thêm ở đầu:

```csharp
        _toxiproxyControl?.Dispose();
        await _toxiproxy.DisposeAsync();
```

- [ ] **Step 5: Chạy FI-08**

Run: `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror && dotnet test tests/TADSS.IntegrationTests/TADSS.IntegrationTests.csproj -c Release --no-build -nologo --filter "FullyQualifiedName~PersistenceFaultInjectionTests"`
Expected: `Passed: 1`. Nếu fixture dừng với "Không đi được tới PostgreSQL/Redis qua Toxiproxy" → đọc inner exception (thường do cổng ánh xạ hoặc alias network), sửa cấu hình rồi chạy lại. Nếu nhận `400` thay vì `503` → Task 3 chưa được áp; nếu nhận `500` → in `problem` ra log, kiểm tra chuỗi `InnerException` có `NpgsqlException` với `IsTransient = true` không và sửa `IsPersistenceUnavailable` theo kiểu exception thực tế (giữ nguyên tắc chỉ lỗi tạm thời → 503).

- [ ] **Step 6: Chạy toàn bộ integration test**

Run: `dotnet test tests/TADSS.IntegrationTests/TADSS.IntegrationTests.csproj -c Release --no-build -nologo`
Expected: `Passed: 30`, `Failed: 0`.

- [ ] **Step 7: Commit (hỏi người dùng trước)**

```bash
git add tests/TADSS.IntegrationTests
git commit -m "test(fault-injection): FI-08 mất kết nối PostgreSQL qua Toxiproxy trả 503 không ghi audit dở dang"
```

---

### Task 5: Cờ tự áp migration khi khởi động (dùng cho E-LOCAL)

**Files:**

- Create: `src/TADSS.Runtime.Api/Configuration/DatabaseOptions.cs`
- Modify: `src/TADSS.Runtime.Api/Configuration/DependencyInjection.cs` (nhóm 2, sau `AddDbContext`)
- Modify: `src/TADSS.Runtime.Api/Program.cs` (sau dòng `app.Services.GetRequiredService<IStrategyResolver>();`)
- Modify: `src/TADSS.Runtime.Api/appsettings.json` (section `Tadss`)
- Test: `tests/TADSS.IntegrationTests/Migrations/MigrationIntegrationTests.cs`

**Interfaces:**

- Produces: `DatabaseOptions.SectionName` = `"Tadss:Database"`, `bool ApplyMigrationsOnStartup` (mặc định `false`). Task 6 đặt biến môi trường `Tadss__Database__ApplyMigrationsOnStartup=true`.

- [ ] **Step 1: Viết test thất bại**

Thêm vào `MigrationIntegrationTests` (thêm `using Microsoft.AspNetCore.Hosting;`):

```csharp
    [Fact]
    [Trait("CatalogId", "IT-15")]
    public async Task Startup_ApplyMigrationsOnStartupEnabled_MigratesEmptyDatabase()
    {
        var emptyDatabase = await CreateAdditionalDatabaseAsync(fromTemplate: false);
        var connectionString = Infrastructure.BuildConnectionString(emptyDatabase);
        await using var factory = new TadssWebApplicationFactory(connectionString, Infrastructure.RedisConnectionString);
        var configured = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Tadss:Database:ApplyMigrationsOnStartup", "true"));

        _ = configured.Server; // khởi động host

        await using var db = CreateDbContext(connectionString);
        (await db.Database.GetAppliedMigrationsAsync()).Should().Equal(db.Database.GetMigrations());
    }

    [Fact]
    [Trait("CatalogId", "IT-15")]
    public async Task Startup_ApplyMigrationsOnStartupDefault_DoesNotTouchDatabase()
    {
        var emptyDatabase = await CreateAdditionalDatabaseAsync(fromTemplate: false);
        var connectionString = Infrastructure.BuildConnectionString(emptyDatabase);
        await using var factory = new TadssWebApplicationFactory(connectionString, Infrastructure.RedisConnectionString);

        _ = factory.Server; // khởi động host với cấu hình mặc định

        await using var db = CreateDbContext(connectionString);
        (await db.Database.GetAppliedMigrationsAsync()).Should().BeEmpty();
    }
```

- [ ] **Step 2: Chạy để xác nhận thất bại**

Run: `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror && dotnet test tests/TADSS.IntegrationTests/TADSS.IntegrationTests.csproj -c Release --no-build -nologo --filter "FullyQualifiedName~Startup_"`
Expected: `Startup_ApplyMigrationsOnStartupEnabled_…` FAIL (không có migration nào được áp); `…Default_…` PASS.

- [ ] **Step 3: Hiện thực**

Create `src/TADSS.Runtime.Api/Configuration/DatabaseOptions.cs`:

```csharp
namespace TADSS.Runtime.Api.Configuration;

/// <summary>
/// Cấu hình vận hành cơ sở dữ liệu của TADSS.
/// </summary>
public class DatabaseOptions
{
    /// <summary>
    /// Tên section cấu hình trong appsettings.json.
    /// </summary>
    public const string SectionName = "Tadss:Database";

    /// <summary>
    /// Tự áp migration EF Core khi khởi động. Chỉ bật cho môi trường E-LOCAL (docker compose) dùng database riêng;
    /// không bật cho Neon hay staging — migration ở đó do người vận hành chủ động chạy.
    /// </summary>
    public bool ApplyMigrationsOnStartup { get; set; }
}
```

Trong `DependencyInjection.cs`, ngay sau khối `services.AddDbContext<TadssDbContext>(…);` (nhóm 2), thêm:

```csharp
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
```

Trong `Program.cs`:

- Thêm `using Microsoft.EntityFrameworkCore;`, `using Microsoft.Extensions.Options;`, `using TADSS.Runtime.Api.Infrastructure.Data;`.
- Sau `app.Services.GetRequiredService<IStrategyResolver>();` thêm:

```csharp

// Tự áp migration khi khởi động — chỉ bật ở E-LOCAL (docker compose); Neon/staging giữ mặc định false
if (app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.ApplyMigrationsOnStartup)
{
    using var migrationScope = app.Services.CreateScope();
    await migrationScope.ServiceProvider.GetRequiredService<TadssDbContext>().Database.MigrateAsync();
}
```

Trong `appsettings.json`, section `Tadss`, thêm trước `"Strategy"`:

```json
    "Database": {
      // Chỉ bật trong docker compose (E-LOCAL); không bật cho Neon/staging
      "ApplyMigrationsOnStartup": false
    },
```

- [ ] **Step 4: Chạy test**

Run: `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror && dotnet test TADSS.slnx -c Release --no-build -nologo`
Expected: 0 warning; unit `Passed: 475`; integration `Passed: 32`.

- [ ] **Step 5: Commit (hỏi người dùng trước)**

```bash
git add src/TADSS.Runtime.Api tests/TADSS.IntegrationTests/Migrations
git commit -m "feat(runtime): cờ Tadss:Database:ApplyMigrationsOnStartup cho môi trường docker compose"
```

---

### Task 6: `docker compose` E-LOCAL (sửa V-1)

**Files:**

- Replace: `docker-compose.yml`
- Create: `docker/toxiproxy/toxiproxy.json`
- Create: `.env.example`

**Interfaces:**

- Consumes: `Tadss__Database__ApplyMigrationsOnStartup` (Task 5).
- Produces (gói 5.5 và SV2/k6 dùng): TADSS `http://localhost:8081`, Toxiproxy API `http://localhost:8474` (proxy `postgres`, `redis`), Grafana `http://localhost:3001`, OTLP `localhost:4317` (gRPC) / `4318` (HTTP), PostgreSQL `localhost:5433`, Redis `localhost:6380`.

- [ ] **Step 1: Ghi nhận lỗi V-1 trước khi sửa**

Run: `grep -n "Redis__ConnectionString" docker-compose.yml && grep -n "SectionName" src/TADSS.Runtime.Api/Infrastructure/Caching/TadssCacheOptions.cs`
Expected: compose dùng `Redis__ConnectionString`, trong khi section là `Tadss:Cache` → biến môi trường không bao giờ tới `TadssCacheOptions.RedisConnectionString`.

- [ ] **Step 2: Viết file**

Replace `docker-compose.yml`:

```yaml
# Môi trường E-LOCAL của TADSS (Phase 5): TADSS + PostgreSQL + Redis + Toxiproxy + Grafana LGTM.
# Chạy: docker compose up -d --build   ·   Dừng và xóa dữ liệu: docker compose down -v
# TADSS kết nối PostgreSQL/Redis QUA Toxiproxy để kịch bản FI tiêm lỗi được mà không phải khởi động lại.
# Nối vào smashcourt-network của SmashCourt-BE làm ở Phase 7 bằng file override.
name: tadss-local

services:
  tadss-postgres:
    image: postgres:16.15-alpine
    environment:
      POSTGRES_DB: tadss
      POSTGRES_USER: ${TADSS_PG_USER:-tadss}
      POSTGRES_PASSWORD: ${TADSS_PG_PASSWORD:-tadss_local}
    ports:
      - "5433:5432"
    volumes:
      - tadss-pg-data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U $${POSTGRES_USER} -d $${POSTGRES_DB}"]
      interval: 5s
      timeout: 3s
      retries: 20

  tadss-redis:
    image: redis:7.4-alpine
    ports:
      - "6380:6379"
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 5s
      timeout: 3s
      retries: 20

  toxiproxy:
    image: ghcr.io/shopify/toxiproxy:2.12.0
    command: ["-host=0.0.0.0", "-config=/config/toxiproxy.json"]
    volumes:
      - ./docker/toxiproxy/toxiproxy.json:/config/toxiproxy.json:ro
    ports:
      - "8474:8474"
    depends_on:
      tadss-postgres:
        condition: service_healthy
      tadss-redis:
        condition: service_healthy

  tadss-runtime:
    build:
      context: .
      dockerfile: Dockerfile
    ports:
      - "8081:8080"
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      ConnectionStrings__DefaultConnection: "Host=toxiproxy;Port=15432;Database=tadss;Username=${TADSS_PG_USER:-tadss};Password=${TADSS_PG_PASSWORD:-tadss_local}"
      Tadss__Cache__Provider: Redis
      Tadss__Cache__RedisConnectionString: "toxiproxy:16379"
      Tadss__Database__ApplyMigrationsOnStartup: "true"
    depends_on:
      toxiproxy:
        condition: service_started

  otel-lgtm:
    image: grafana/otel-lgtm:0.34.0
    ports:
      - "3001:3000"
      - "4317:4317"
      - "4318:4318"

volumes:
  tadss-pg-data:
```

Create `docker/toxiproxy/toxiproxy.json`:

```json
[
  {
    "name": "postgres",
    "listen": "0.0.0.0:15432",
    "upstream": "tadss-postgres:5432",
    "enabled": true
  },
  {
    "name": "redis",
    "listen": "0.0.0.0:16379",
    "upstream": "tadss-redis:6379",
    "enabled": true
  }
]
```

Create `.env.example`:

```dotenv
# Sao chép thành .env (không commit) nếu muốn đổi thông tin đăng nhập PostgreSQL của môi trường E-LOCAL.
# Đây là database cục bộ dùng một lần trong docker compose — KHÔNG dùng thông tin của Neon ở đây.
TADSS_PG_USER=tadss
TADSS_PG_PASSWORD=tadss_local
```

- [ ] **Step 3: Kiểm tra cú pháp**

Run: `docker compose config --quiet && echo OK`
Expected: `OK`.

- [ ] **Step 4: Khởi động và kiểm tra sức khỏe**

Run: `docker compose up -d --build`
Expected: 5 container `Started`/`Running` (`docker compose ps`).

Run (lặp tối đa 60 giây cho tới khi thành công): `curl -s -o /dev/null -w "%{http_code}" http://localhost:8081/health/ready`
Expected: `200`.

- [ ] **Step 5: Kiểm chứng V-1 đã sửa (Redis thật được dùng)**

Run:

```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:8081/api/v1/decisions \
  -H "Content-Type: application/json" -H "X-Idempotency-Key: compose-smoke-$(date +%s)" \
  -d '{"reservationId":"5b0a6f0e-7c0d-4a0e-9a64-0c2e7f1b9a11","customerId":"9d3f1c2a-0000-4000-8000-000000000123","customerTier":"GOLD","courtId":"c0a8012e-0000-4000-8000-0000000000aa","branchId":"1f2e3d4c-0000-4000-8000-000000000001","courtTypeId":null,"startTime":"2026-12-01T10:00:00Z","endTime":"2026-12-01T12:00:00Z","totalPrice":300000,"depositAmount":150000,"paymentStatus":"PARTIALLY_PAID","bookingType":"SINGLE","priority":"NORMAL","requestedAt":"2026-11-30T10:00:00Z"}'
docker compose exec tadss-redis redis-cli --scan --pattern 'tadss:*'
```

Expected: `200`, sau đó ít nhất một key bắt đầu bằng `tadss:` → TADSS trong container đã ghi cache vào Redis thật.

- [ ] **Step 6: Kiểm chứng audit nằm trong PostgreSQL của compose**

Run: `docker compose exec tadss-postgres psql -U tadss -d tadss -c "select count(*) from tadss.decision_executions;"`
Expected: `count` ≥ 1.

- [ ] **Step 7: Dọn môi trường**

Run: `docker compose down -v`
Expected: các container và volume `tadss-local_tadss-pg-data` bị xóa.

- [ ] **Step 8: Commit (hỏi người dùng trước)**

```bash
git add docker-compose.yml docker/toxiproxy/toxiproxy.json .env.example
git commit -m "chore(docker): môi trường E-LOCAL với PostgreSQL, Redis, Toxiproxy, Grafana LGTM; sửa biến Redis của TADSS"
```

---

### Task 7: CI chạy unit và integration test (Testcontainers) thành hai bước

**Files:**

- Modify: `.github/workflows/ci.yml` (bước `# 5. Chạy toàn bộ Tests…`)

**Interfaces:** không.

- [ ] **Step 1: Thay bước 5**

Thay toàn bộ bước có `name: Run Automated Tests` bằng:

```yaml
      # 5a. Unit test (không cần Docker)
      - name: Run Unit Tests
        run: >
          dotnet test tests/TADSS.UnitTests/TADSS.UnitTests.csproj
          --no-build
          --configuration Release
          --verbosity normal
          --logger "GitHubActions"
          --logger "trx;LogFilePrefix=unit"
          --collect:"XPlat Code Coverage"
          --results-directory ./TestResults

      # 5b. Integration test trên PostgreSQL/Redis/Toxiproxy thật (Testcontainers dùng Docker có sẵn của runner)
      - name: Run Integration Tests (Testcontainers)
        run: >
          dotnet test tests/TADSS.IntegrationTests/TADSS.IntegrationTests.csproj
          --no-build
          --configuration Release
          --verbosity normal
          --logger "GitHubActions"
          --logger "trx;LogFilePrefix=integration"
          --collect:"XPlat Code Coverage"
          --results-directory ./TestResults
```

(Bước `Publish Test Results Summary` đọc `TestResults/**/*.trx` nên nhận cả hai file, không cần đổi.)

- [ ] **Step 2: Kiểm tra cú pháp YAML**

Run: `python -c "import yaml,sys;yaml.safe_load(open('.github/workflows/ci.yml',encoding='utf-8'));print('OK')"`
Expected: `OK` (nếu thiếu PyYAML: `python -m pip install pyyaml` chỉ khi người dùng đồng ý; nếu không, bỏ qua và dựa vào lượt chạy CI ở Step 4).

- [ ] **Step 3: Commit (hỏi người dùng trước)**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: tách bước unit test và integration test chạy trên Testcontainers"
```

- [ ] **Step 4: Kiểm chứng trên CI (sau khi người dùng cho phép push)**

Run: `git push` rồi `gh run list --branch feat/phase-5-contract-v2-test-infra --limit 1`; đợi xong: `gh run view --log-failed` nếu thất bại.
Expected: `completed success`; job summary có hai báo cáo `unit*.trx`, `integration*.trx`.

---

### Task 8: Đồng bộ tài liệu + kiểm chứng cuối gói

**Files:**

- Modify: `README.md` (mục lệnh test / trạng thái), `CLAUDE.md` (TADSS), `docs/planning/BACKLOG_SPRINTS.md` (T5.12, T5.13), `docs/architecture/RESILIENCE_AND_FAULT_TAXONOMY.md` (Mục 5), `docs/architecture/SOFTWARE_ARCHITECTURE_SPECIFICATION.md` (Mục 2, dòng `TADSS.IntegrationTests`), `../.claude/skills/tadss-architecture/SKILL.md` (bảng project, mục Test)

**Interfaces:** không.

- [ ] **Step 1: Cập nhật nội dung**

| File | Nội dung cần ghi |
| :--- | :--- |
| `README.md` | Integration test cần Docker Desktop; lệnh `docker compose up -d --build` / `docker compose down -v`; các cổng ở Task 6; số test hiện tại (475 unit + 32 integration) |
| `CLAUDE.md` (TADSS) | Trong mục Lệnh: chú thích dòng integration test "cần Docker (Testcontainers)"; thêm `docker compose up -d --build`; bảng cấu trúc: `TADSS.IntegrationTests` = "Testcontainers PostgreSQL 16 / Redis 7 / Toxiproxy" |
| `BACKLOG_SPRINTS.md` | T5.12, T5.13: ✅ kèm "(gói 5.1)"; ghi chú V-1, V-8 đã sửa |
| `RESILIENCE_AND_FAULT_TAXONOMY.md` Mục 5 | Phase 5: FI-08 đã chạy trên PostgreSQL thật qua Toxiproxy (503 `PERSISTENCE_UNAVAILABLE`); F6 cũ (interceptor) vẫn trả 500, được thay ở gói 5.2 |
| `SOFTWARE_ARCHITECTURE_SPECIFICATION.md` Mục 2 | `TADSS.IntegrationTests/  # API qua WebApplicationFactory trên Testcontainers (PostgreSQL 16, Redis 7, Toxiproxy)` |
| `SKILL.md` (tadss-architecture) | Bảng project: `tests/TADSS.IntegrationTests` = Testcontainers; mục Test: kế thừa `IntegrationTestBase`, `[Collection(TadssInfrastructureCollection.Name)]`, `[Trait("CatalogId", …)]` |

- [ ] **Step 2: Kiểm tra link tài liệu**

Run:

```bash
for f in README.md docs/planning/BACKLOG_SPRINTS.md docs/architecture/RESILIENCE_AND_FAULT_TAXONOMY.md docs/architecture/SOFTWARE_ARCHITECTURE_SPECIFICATION.md spec/*.md; do d=$(dirname "$f"); grep -oE "\]\(([^)#]+)" "$f" | sed -E 's/\]\(//' | grep -v '^http' | while read l; do [ -e "$d/$l" ] || echo "HỎNG $f -> $l"; done; done; echo done
```

Expected: chỉ in `done`.

- [ ] **Step 3: Kiểm chứng cuối gói**

Run: `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror --no-incremental`
Expected: 0 Warning(s), 0 Error(s).

Run: `dotnet test TADSS.slnx -c Release --no-build -nologo`
Expected: `TADSS.UnitTests` Passed 475, `TADSS.IntegrationTests` Passed 32, Failed 0, Skipped 0.

Run: `grep -rn "UseInMemoryDatabase" tests/TADSS.IntegrationTests || echo "sạch"`
Expected: `sạch`.

Run: `git status --short`
Expected: chỉ các file tài liệu của task này (nếu các task trước đã commit).

- [ ] **Step 4: Commit (hỏi người dùng trước)**

```bash
git add README.md CLAUDE.md docs spec
git commit -m "docs: cập nhật tài liệu sau gói 5.1 — integration test trên Testcontainers, môi trường E-LOCAL"
```

(`../.claude/skills/tadss-architecture/SKILL.md` nằm ngoài repo, không commit.)

- [ ] **Step 5: Đề xuất PR vào `develop` (D-08)**

Gợi ý tiêu đề: `Phase 5 — gói 5.1: nền móng kiểm thử trên hạ tầng thật`. Mô tả gồm: danh sách task, số test trước/sau (473+27 → 475+32), mục "Phát hiện khi chuyển hạ tầng" (V-1, V-8 và kết quả chuyển hạ tầng: 27 IT không lộ lỗi mới; FI-08 xác nhận chuỗi exception thật), cách chạy (`docker compose up -d --build`). Người dùng tạo/merge PR.
