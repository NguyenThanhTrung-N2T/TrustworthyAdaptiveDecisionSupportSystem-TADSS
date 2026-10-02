using System.Net.Sockets;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Polly.Timeout;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Domain;
using TADSS.Runtime.Api.Middlewares;
using TADSS.Runtime.Api.V2.Middlewares;
using Xunit;

namespace TADSS.UnitTests.V2.Runtime.Middlewares;

/// <summary>
/// UT-08: <see cref="ExceptionClassifier"/> hiện thực đúng bảng map lỗi v2 (spec 4.2.3) — lỗi persistence được
/// kiểm tra trước các nhánh khác, lỗi phía server không bao giờ thành lỗi client và không lộ message kỹ thuật.
/// </summary>
public class ExceptionClassifierTests
{
    private static PostgresException Postgres(string sqlState) =>
        new("postgres error", "ERROR", "ERROR", sqlState);

    private static ErrorClassification Require(ErrorClassification? result)
    {
        result.Should().NotBeNull("exception phải được phân loại thành một phản hồi lỗi");
        return result ?? throw new InvalidOperationException("Classify trả null ngoài dự kiến.");
    }

    private static NpgsqlException TransientNpgsql() =>
        new("Failed to connect", new SocketException((int)SocketError.ConnectionRefused));

    public static TheoryData<Exception, int, string, bool, int?> MappingRows => new()
    {
        { new ContractValidationException(ErrorCodes.ValidationError, "sai định dạng", "items"), 400, ErrorCodes.ValidationError, true, null },
        { new DomainInvariantException("ANY_CODE", "vi phạm bất biến", "endTime"), 400, ErrorCodes.DomainInvariantViolation, true, null },
        { new IdempotencyConflictException("key-01", "key dùng lại"), 409, ErrorCodes.IdempotencyKeyReuse, true, null },
        { new PersistenceUnavailableException("mất kết nối", new TimeoutException()), 503, ErrorCodes.PersistenceUnavailable, false, 5 },
        { new TimeoutRejectedException("vượt deadline"), 504, ErrorCodes.GatewayTimeout, false, 5 },
        { new InvalidOperationException("lỗi nội bộ"), 500, ErrorCodes.InternalError, false, null },
        { new ArgumentException("lỗi tham số"), 500, ErrorCodes.InternalError, false, null },
    };

    public static TheoryData<Exception> ServerSideErrors => new()
    {
        new InvalidOperationException("x"),
        new ArgumentException("x"),
        new NullReferenceException("x"),
    };

    [Theory]
    [Trait("CatalogId", "UT-08")]
    [MemberData(nameof(MappingRows))]
    public void Classify_MappingTable_Theory(
        Exception exception, int expectedStatus, string expectedCode, bool expectedExposeMessage, int? expectedRetryAfter)
    {
        var result = ExceptionClassifier.Classify(exception, requestAborted: false);

        var classification = Require(result);
        classification.StatusCode.Should().Be(expectedStatus);
        classification.Code.Should().Be(expectedCode);
        classification.ExposeMessage.Should().Be(expectedExposeMessage);
        classification.RetryAfterSeconds.Should().Be(expectedRetryAfter);
        classification.Title.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("CatalogId", "UT-08")]
    public void Classify_ContractValidationWithMissingHeaderCode_Is400MissingHeader()
    {
        var result = ExceptionClassifier.Classify(
            new ContractValidationException(ErrorCodes.MissingHeader, "thiếu header Idempotency-Key", "Idempotency-Key"),
            requestAborted: false);

        var classification = Require(result);
        classification.StatusCode.Should().Be(400);
        classification.Code.Should().Be(ErrorCodes.MissingHeader);
        classification.ExposeMessage.Should().BeTrue();
        classification.RetryAfterSeconds.Should().BeNull();
        classification.Title.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [Trait("CatalogId", "UT-08")]
    [InlineData(ErrorCodes.InternalError)]
    [InlineData("INVALID_BOOKING_PRIORITY")]
    [InlineData("")]
    [InlineData("missing_header")]
    public void Classify_ContractValidationWithUnknownCode_FallsBackToValidationError(string errorCode)
    {
        var result = ExceptionClassifier.Classify(
            new ContractValidationException(errorCode, "sai hợp đồng", "field"), requestAborted: false);

        var classification = Require(result);
        classification.StatusCode.Should().Be(400);
        classification.Code.Should().Be(ErrorCodes.ValidationError);
        classification.ExposeMessage.Should().BeTrue();
    }

    [Fact]
    [Trait("CatalogId", "UT-08")]
    public void Classify_TransientNpgsqlNestedThreeLevels_Is503()
    {
        var nested = new InvalidOperationException(
            "wrapper", new DbUpdateException("save failed", TransientNpgsql()));

        var result = ExceptionClassifier.Classify(nested, requestAborted: false);

        var classification = Require(result);
        classification.StatusCode.Should().Be(503);
        classification.Code.Should().Be(ErrorCodes.PersistenceUnavailable);
        classification.RetryAfterSeconds.Should().Be(5);
    }

    [Theory]
    [Trait("CatalogId", "UT-08")]
    [InlineData("40001")]
    [InlineData("40P01")]
    public void Classify_SerializationFailureAndDeadlock_Is503_Theory(string sqlState)
    {
        var result = ExceptionClassifier.Classify(
            new DbUpdateException("save failed", Postgres(sqlState)), requestAborted: false);

        var classification = Require(result);
        classification.StatusCode.Should().Be(503);
        classification.Code.Should().Be(ErrorCodes.PersistenceUnavailable);
        classification.RetryAfterSeconds.Should().Be(5);
        classification.ExposeMessage.Should().BeFalse();
    }

    [Fact]
    [Trait("CatalogId", "UT-08")]
    public void Classify_UniqueViolation_IsNot503()
    {
        var result = ExceptionClassifier.Classify(
            new DbUpdateException("duplicate", Postgres("23505")), requestAborted: false);

        var classification = Require(result);
        classification.StatusCode.Should().Be(500);
        classification.Code.Should().Be(ErrorCodes.InternalError);
        classification.RetryAfterSeconds.Should().BeNull();
    }

    [Theory]
    [Trait("CatalogId", "UT-08")]
    [MemberData(nameof(ServerSideErrors))]
    public void Classify_ServerSideErrors_NeverClientError_Theory(Exception exception)
    {
        var result = ExceptionClassifier.Classify(exception, requestAborted: false);

        var classification = Require(result);
        classification.StatusCode.Should().Be(500);
        classification.ExposeMessage.Should().BeFalse();
    }

    [Fact]
    [Trait("CatalogId", "UT-08")]
    public void Classify_RequestAborted_ReturnsNull()
    {
        var result = ExceptionClassifier.Classify(new OperationCanceledException(), requestAborted: true);

        result.Should().BeNull();
    }

    [Fact]
    [Trait("CatalogId", "UT-08")]
    public void Classify_CancellationNotFromClient_Is500()
    {
        var result = ExceptionClassifier.Classify(new OperationCanceledException(), requestAborted: false);

        var classification = Require(result);
        classification.StatusCode.Should().Be(500);
        classification.Code.Should().Be(ErrorCodes.InternalError);
    }
}
