using System.Net.Sockets;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TADSS.Application.Exceptions;
using TADSS.Infrastructure.Persistence;
using Xunit;

namespace TADSS.UnitTests.Infrastructure.Persistence;

/// <summary>
/// UT-08: <see cref="PersistenceFailureTranslator"/> dịch lỗi lưu trữ có thể thử lại thành 503 (<see cref="PersistenceUnavailableException"/>),
/// nhưng khi caller đã hủy thì thành <see cref="OperationCanceledException"/> gắn token (không còn 503 giả khi client đóng kết nối).
/// </summary>
public class PersistenceFailureTranslatorTests
{
    public enum Expectation
    {
        Unavailable,
        CanceledWithToken,
        Unchanged,
    }

    public static TheoryData<string, Exception, bool, Expectation> Cases => new()
    {
        { "transient, token chưa hủy", Transient(), false, Expectation.Unavailable },
        { "transient, token đã hủy", Transient(), true, Expectation.CanceledWithToken },
        { "OperationCanceledException", new OperationCanceledException(), true, Expectation.Unchanged },
        { "lỗi CHECK 23514", new DbUpdateException("save", new PostgresException("pg", "ERROR", "ERROR", "23514")), false, Expectation.Unchanged },
        { "lỗi CHECK 23514, token đã hủy", new DbUpdateException("save", new PostgresException("pg", "ERROR", "ERROR", "23514")), true, Expectation.Unchanged },
    };

    private static NpgsqlException Transient() =>
        new("connect", new SocketException((int)SocketError.ConnectionRefused));

    /// <summary>Bảng quyết định của <see cref="PersistenceFailureTranslator.Translate"/>.</summary>
    [Theory]
    [Trait("CatalogId", "UT-08")]
    [MemberData(nameof(Cases))]
    public void Translate_Theory(string scenario, Exception exception, bool tokenCanceled, Expectation expectation)
    {
        using var cts = new CancellationTokenSource();
        if (tokenCanceled)
        {
            cts.Cancel();
        }

        var translated = PersistenceFailureTranslator.Translate(exception, cts.Token);

        switch (expectation)
        {
            case Expectation.Unavailable:
                translated.Should().BeOfType<PersistenceUnavailableException>(scenario).Which.InnerException.Should().BeSameAs(exception);
                break;
            case Expectation.CanceledWithToken:
                var canceled = translated.Should().BeOfType<OperationCanceledException>(scenario).Which;
                canceled.CancellationToken.Should().Be(cts.Token);
                canceled.InnerException.Should().BeSameAs(exception);
                break;
            default:
                translated.Should().BeNull(scenario);
                break;
        }
    }

    /// <summary><see cref="PersistenceFailureTranslator.ExecuteAsync{T}"/> ném đúng exception đã dịch và giữ nguyên lỗi không dịch.</summary>
    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task ExecuteAsync_TransientFailure_ThrowsTranslatedException()
    {
        var transient = Transient();

        var unavailable = async () => await PersistenceFailureTranslator.ExecuteAsync<int>(
            () => throw transient, NullLogger.Instance, CancellationToken.None);
        var unchanged = async () => await PersistenceFailureTranslator.ExecuteAsync<int>(
            () => throw new InvalidOperationException("lỗi khác"), NullLogger.Instance, CancellationToken.None);

        await unavailable.Should().ThrowAsync<PersistenceUnavailableException>();
        await unchanged.Should().ThrowAsync<InvalidOperationException>().WithMessage("lỗi khác");
    }
}
