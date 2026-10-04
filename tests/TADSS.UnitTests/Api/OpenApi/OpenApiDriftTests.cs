using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Swagger;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Api.OpenApi;

/// <summary>
/// CT-01 (tài liệu OpenAPI): <c>docs/contracts/openapi-v2.yaml</c> được SINH từ Swashbuckle của chính app (spec 4.2.1) và test này
/// chỉ KIỂM TRA lệch, không bao giờ ghi đè file chuẩn. Khi lệch, bản thực tế được ghi ra <c>TestResults/openapi-v2.actual.yaml</c>
/// (thư mục gốc repo, cạnh <c>baseline_public_api.actual.txt</c>) rồi test fail.
/// Cách sinh lại có chủ đích: chạy <c>dotnet test --filter OpenApi_GeneratedDocument_MatchesCommittedFile</c>, xem lại bản
/// <c>TestResults/openapi-v2.actual.yaml</c>, rồi <c>cp TestResults/openapi-v2.actual.yaml docs/contracts/openapi-v2.yaml</c>.
/// JSON schema viết tay (<c>docs/contracts/schemas/v2</c>) vẫn là nguồn chuẩn; YAML này chỉ là tài liệu mô tả.
/// </summary>
public class OpenApiDriftTests : IClassFixture<DecisionsApiFactory>
{
    private const string DocumentName = "v2";
    private const string PostPath = "/api/v2/decisions";
    private const string GetPath = "/api/v2/decisions/{decisionId}";
    private const string PendingPath = "/api/v2/decisions/pending-approvals";
    private const string OverridePath = "/api/v2/decisions/{decisionId}/override";
    private const string OutcomePath = "/api/v2/decisions/{decisionId}/outcome";
    private const string ProblemMediaType = "application/problem+json";

    private readonly DecisionsApiFactory _factory;

    public OpenApiDriftTests(DecisionsApiFactory factory)
    {
        _factory = factory;
    }

    private OpenApiDocument GenerateDocument() =>
        _factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger(DocumentName);

    private static string Serialize(OpenApiDocument document) => document.SerializeAsYaml(OpenApiSpecVersion.OpenApi3_0);

    private static string Normalize(string text) =>
        string.Join("\n", text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n').Select(line => line.TrimEnd())).Trim();

    /// <summary>Tài liệu sinh từ app khớp file đã commit (sau chuẩn hóa xuống dòng); lệch thì ghi bản thực tế ra TestResults rồi fail.</summary>
    [Fact]
    [Trait("CatalogId", "CT-01")]
    public void OpenApi_GeneratedDocument_MatchesCommittedFile()
    {
        var actual = Serialize(GenerateDocument());
        var committedPath = RepositoryPaths.Resolve("docs", "contracts", "openapi-v2.yaml");
        var snapshotPath = RepositoryPaths.Resolve("TestResults", "openapi-v2.actual.yaml");

        var expected = File.Exists(committedPath) ? File.ReadAllText(committedPath, Encoding.UTF8) : string.Empty;
        if (Normalize(actual) != Normalize(expected))
        {
            Directory.CreateDirectory(RepositoryPaths.Resolve("TestResults"));
            File.WriteAllText(snapshotPath, actual, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        File.Exists(committedPath).Should().BeTrue(
            $"openapi-v2.yaml phải tồn tại tại '{committedPath}'; sinh lần đầu bằng cách sao chép '{snapshotPath}' vào đó.");
        Normalize(actual).Should().Be(
            Normalize(expected),
            $"tài liệu OpenAPI sinh từ app phải khớp docs/contracts/openapi-v2.yaml. Bản thực tế đã ghi ra '{snapshotPath}'; nếu thay đổi là có chủ đích thì sao chép sang docs/contracts.");
    }

    /// <summary>Tài liệu chỉ có đúng năm operation v2 (đánh giá, chi tiết, hàng chờ, override, outcome), cùng nằm dưới base path <c>/api/v2</c>; không có v1.</summary>
    [Fact]
    [Trait("CatalogId", "CT-01")]
    public void OpenApi_ContainsOnlyV2DecisionOperations()
    {
        var document = GenerateDocument();

        document.Paths.Keys.Should().BeEquivalentTo([PostPath, GetPath, PendingPath, OverridePath, OutcomePath]);
        document.Paths.Keys.Should().OnlyContain(path => path.StartsWith("/api/v2/", StringComparison.Ordinal));
        var operations = document.Paths.SelectMany(path => path.Value.Operations.Select(op => (path.Key, op.Key))).ToList();
        operations.Should().BeEquivalentTo(new[]
        {
            (PostPath, OperationType.Post),
            (GetPath, OperationType.Get),
            (PendingPath, OperationType.Get),
            (OverridePath, OperationType.Post),
            (OutcomePath, OperationType.Post)
        });
        document.Servers.Should().BeNullOrEmpty("tài liệu phải xác định, không chứa URL máy chủ riêng của môi trường sinh");
        document.Info.Version.Should().Be("2.0.0-draft");
        document.Paths.Keys.Should().NotContain(path => path.Contains("v1", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Header của contract đúng (idempotency bắt buộc ở mọi POST, actor bắt buộc ở override, correlation tùy chọn ở mọi operation), tham số
    /// hàng chờ có miền giá trị, và mọi mã lỗi đều là problem+json có thuộc tính <c>code</c> (trừ 415 do framework dựng).
    /// </summary>
    [Fact]
    [Trait("CatalogId", "CT-01")]
    public void OpenApi_DocumentsRequiredHeadersAndProblemResponses()
    {
        var document = GenerateDocument();
        var post = document.Paths[PostPath].Operations[OperationType.Post];
        var get = document.Paths[GetPath].Operations[OperationType.Get];
        var pending = document.Paths[PendingPath].Operations[OperationType.Get];
        var overrideOperation = document.Paths[OverridePath].Operations[OperationType.Post];
        var outcome = document.Paths[OutcomePath].Operations[OperationType.Post];
        var all = new[] { post, get, pending, overrideOperation, outcome };

        foreach (var operation in new[] { post, overrideOperation, outcome })
        {
            var idempotency = operation.Parameters.Should().ContainSingle(p => p.Name == "X-Idempotency-Key").Which;
            idempotency.In.Should().Be(ParameterLocation.Header);
            idempotency.Required.Should().BeTrue();
        }

        foreach (var operation in new[] { get, pending })
        {
            operation.Parameters.Should().NotContain(p => p.Name == "X-Idempotency-Key" || p.Name == "X-Actor-Id");
        }

        var actor = overrideOperation.Parameters.Should().ContainSingle(p => p.Name == "X-Actor-Id").Which;
        actor.In.Should().Be(ParameterLocation.Header);
        actor.Required.Should().BeTrue();
        actor.Schema.MaxLength.Should().Be(100);
        post.Parameters.Should().NotContain(p => p.Name == "X-Actor-Id");
        outcome.Parameters.Should().NotContain(p => p.Name == "X-Actor-Id");

        foreach (var operation in all)
        {
            var correlation = operation.Parameters.Should().ContainSingle(p => p.Name == "X-Correlation-ID").Which;
            correlation.In.Should().Be(ParameterLocation.Header);
            correlation.Required.Should().BeFalse();
        }

        pending.Parameters.Should().ContainSingle(p => p.Name == "branchId").Which.Required.Should().BeTrue();
        var pageSize = pending.Parameters.Should().ContainSingle(p => p.Name == "pageSize").Which;
        pageSize.Schema.Minimum.Should().Be(1);
        pageSize.Schema.Maximum.Should().Be(100);
        pending.Parameters.Should().ContainSingle(p => p.Name == "page").Which.Schema.Minimum.Should().Be(1);

        post.Responses.Keys.Should().BeEquivalentTo(["200", "400", "401", "409", "413", "415", "429", "500", "503", "504"]);
        get.Responses.Keys.Should().BeEquivalentTo(["200", "401", "404", "429", "500", "503"]);
        pending.Responses.Keys.Should().BeEquivalentTo(["200", "400", "401", "429", "500", "503"]);
        overrideOperation.Responses.Keys.Should().BeEquivalentTo(["200", "400", "401", "404", "409", "413", "415", "429", "500", "503"]);
        outcome.Responses.Keys.Should().BeEquivalentTo(["200", "400", "401", "404", "409", "413", "415", "429", "500", "503"]);
        foreach (var (status, response) in all.SelectMany(operation => operation.Responses).Where(r => r.Key != "200"))
        {
            response.Content.Keys.Should().Contain(ProblemMediaType, $"phản hồi {status} phải là problem+json");
            response.Content.Keys.Should().BeEquivalentTo([ProblemMediaType], "lỗi chỉ trả problem+json");
        }

        document.Components.Schemas["ProblemDetails"].Properties.Keys.Should().Contain(["code", "traceId"],
            "problem+json của TADSS có thuộc tính code (bảng mã lỗi §3.8) và traceId");
        foreach (var operation in all)
        {
            operation.Responses["200"].Content.Keys.Should().BeEquivalentTo(["application/json"]);
        }
    }
}
