using System.Net.Sockets;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Domain;
using TADSS.Application.Exceptions;
using TADSS.Runtime.Api.ErrorHandling;
using Xunit;

namespace TADSS.UnitTests.Api.ErrorHandling;

/// <summary>
/// UT-08: <see cref="ExceptionClassifier"/> hiện thực đúng bảng map lỗi v2 (spec 4.2.3) — lỗi persistence được
/// kiểm tra trước các nhánh khác, lỗi phía server không bao giờ thành lỗi client và không lộ message kỹ thuật.
/// Classifier chỉ biết exception của SDK và Application: exception thô của Npgsql/Polly đã được adapter hạ tầng dịch (E-04).
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

    public static TheoryData<Exception, int, string, bool, bool> MappingRows => new()
    {
        { new ContractValidationException(ErrorCodes.ValidationError, "sai định dạng", "items"), 400, ErrorCodes.ValidationError, true, false },
        { new DomainInvariantException("ANY_CODE", "vi phạm bất biến", "endTime"), 400, ErrorCodes.DomainInvariantViolation, true, false },
        { new IdempotencyConflictException("key-01", "key dùng lại"), 409, ErrorCodes.IdempotencyKeyReuse, true, false },
        { new PersistenceUnavailableException("mất kết nối", new TimeoutException()), 503, ErrorCodes.PersistenceUnavailable, false, true },
        { new DecisionTimeoutException("vượt deadline", new TimeoutException()), 504, ErrorCodes.GatewayTimeout, false, true },
        { new InvalidOperationException("lỗi nội bộ"), 500, ErrorCodes.InternalError, false, false },
        { new ArgumentException("lỗi tham số"), 500, ErrorCodes.InternalError, false, false },
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
        Exception exception, int expectedStatus, string expectedCode, bool expectedExposeMessage, bool expectedHasRetryAfter)
    {
        var result = ExceptionClassifier.Classify(exception, requestAborted: false);

        var classification = Require(result);
        classification.StatusCode.Should().Be(expectedStatus);
        classification.Code.Should().Be(expectedCode);
        classification.ExposeMessage.Should().Be(expectedExposeMessage);
        classification.HasRetryAfter.Should().Be(expectedHasRetryAfter);
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
        classification.HasRetryAfter.Should().BeFalse();
        classification.Title.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("CatalogId", "UT-08")]
    public void Classify_DecisionNotFound_Is404()
    {
        var result = ExceptionClassifier.Classify(new DecisionNotFoundException(Guid.NewGuid()), requestAborted: false);

        var classification = Require(result);
        classification.StatusCode.Should().Be(404);
        classification.Code.Should().Be(ErrorCodes.DecisionNotFound);
        classification.ExposeMessage.Should().BeTrue();
        classification.HasRetryAfter.Should().BeFalse();
        classification.Title.Should().NotBeNullOrWhiteSpace();
    }

    private static readonly Guid ConflictDecisionId = Guid.Parse("0f9c7e5a-0000-4000-8000-00000000d001");

    public static TheoryData<DecisionStateConflictException, string> StateConflicts => new()
    {
        { DecisionStateConflictException.NotPendingApproval(ConflictDecisionId), ErrorCodes.NotPendingApproval },
        { DecisionStateConflictException.AlreadyOverridden(ConflictDecisionId), ErrorCodes.AlreadyOverridden },
        { DecisionStateConflictException.ApprovalExpired(ConflictDecisionId), ErrorCodes.ApprovalExpired },
        { DecisionStateConflictException.OutcomeAlreadyRecorded(ConflictDecisionId), ErrorCodes.OutcomeAlreadyRecorded },
    };

    /// <summary>Mỗi factory của <see cref="DecisionStateConflictException"/> thành 409 mang đúng mã của nó; message (chỉ có decisionId) được phép trả cho client.</summary>
    [Theory]
    [Trait("CatalogId", "UT-08")]
    [MemberData(nameof(StateConflicts))]
    public void Classify_DecisionStateConflict_Is409WithItsCode_Theory(DecisionStateConflictException exception, string expectedCode)
    {
        var result = ExceptionClassifier.Classify(exception, requestAborted: false);

        var classification = Require(result);
        classification.StatusCode.Should().Be(409);
        classification.Code.Should().Be(expectedCode);
        classification.ExposeMessage.Should().BeTrue();
        classification.HasRetryAfter.Should().BeFalse();
        classification.Title.Should().NotBeNullOrWhiteSpace();
        exception.ErrorCode.Should().Be(expectedCode);
        exception.DecisionId.Should().Be(ConflictDecisionId);
        exception.Message.Should().Contain(ConflictDecisionId.ToString());
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

    public static TheoryData<Exception> PersistenceUnavailableShapes => new()
    {
        // Trực tiếp
        new PersistenceUnavailableException("mất kết nối", new TimeoutException()),
        // Bọc trong exception khác (nhiều tầng)
        new InvalidOperationException("wrapper", new DbUpdateException("save failed", new PersistenceUnavailableException("mất kết nối", new TimeoutException()))),
        // Nằm ở nhánh thứ hai của AggregateException (InnerException chỉ trỏ nhánh đầu)
        new AggregateException(new InvalidOperationException("nhánh 1"), new PersistenceUnavailableException("mất kết nối", new TimeoutException())),
    };

    public static TheoryData<Exception> RawNpgsqlShapes => new()
    {
        TransientNpgsql(),
        new InvalidOperationException("wrapper", new DbUpdateException("save failed", TransientNpgsql())),
        new DbUpdateException("save failed", Postgres("40001")),
        new DbUpdateException("save failed", Postgres("40P01")),
    };

    [Fact]
    [Trait("CatalogId", "UT-08")]
    public void Classify_DecisionTimeout_Is504WithRetryAfter()
    {
        var result = ExceptionClassifier.Classify(
            new DecisionTimeoutException("vượt deadline", new TimeoutException()), requestAborted: false);

        var classification = Require(result);
        classification.StatusCode.Should().Be(504);
        classification.Code.Should().Be(ErrorCodes.GatewayTimeout);
        classification.ExposeMessage.Should().BeFalse();
        classification.HasRetryAfter.Should().BeTrue();
        classification.Title.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [Trait("CatalogId", "UT-08")]
    [MemberData(nameof(PersistenceUnavailableShapes))]
    public void Classify_PersistenceUnavailableNestedOrAggregate_Is503_Theory(Exception exception)
    {
        var result = ExceptionClassifier.Classify(exception, requestAborted: false);

        var classification = Require(result);
        classification.StatusCode.Should().Be(503);
        classification.Code.Should().Be(ErrorCodes.PersistenceUnavailable);
        classification.ExposeMessage.Should().BeFalse();
        classification.HasRetryAfter.Should().BeTrue();
    }

    [Theory]
    [Trait("CatalogId", "UT-08")]
    [MemberData(nameof(RawNpgsqlShapes))]
    public void Classify_RawNpgsqlException_IsInternalError(Exception exception)
    {
        // Store chịu trách nhiệm dịch lỗi Npgsql thành PersistenceUnavailableException; exception thô lọt tới đây là lỗi lập trình
        var result = ExceptionClassifier.Classify(exception, requestAborted: false);

        var classification = Require(result);
        classification.StatusCode.Should().Be(500);
        classification.Code.Should().Be(ErrorCodes.InternalError);
        classification.ExposeMessage.Should().BeFalse();
        classification.HasRetryAfter.Should().BeFalse();
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
        classification.HasRetryAfter.Should().BeFalse();
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
