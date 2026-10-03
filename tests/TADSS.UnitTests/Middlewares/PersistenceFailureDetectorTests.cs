using System.Net.Sockets;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TADSS.Application.Exceptions;
using TADSS.Runtime.Api.Middlewares;
using Xunit;

namespace TADSS.UnitTests.Middlewares;

/// <summary>
/// UT-08: <see cref="PersistenceFailureDetector"/> nhận diện lỗi lưu trữ có thể thử lại ở mọi tầng InnerException,
/// kiểm tường minh theo SqlState (40001, 40P01) và không coi vi phạm ràng buộc là lỗi tạm thời.
/// </summary>
public class PersistenceFailureDetectorTests
{
    [Fact]
    [Trait("CatalogId", "UT-08")]
    public void IsRetryable_PersistenceUnavailableException_ReturnsTrue()
    {
        var exception = new PersistenceUnavailableException("mất kết nối", new TimeoutException());

        PersistenceFailureDetector.IsRetryable(exception).Should().BeTrue();
    }

    [Fact]
    [Trait("CatalogId", "UT-08")]
    public void IsRetryable_TransientNpgsqlNested_ReturnsTrue()
    {
        var transient = new NpgsqlException("connect", new SocketException((int)SocketError.ConnectionRefused));
        var nested = new InvalidOperationException("outer", new DbUpdateException("mid", transient));

        PersistenceFailureDetector.IsRetryable(nested).Should().BeTrue();
    }

    [Theory]
    [Trait("CatalogId", "UT-08")]
    [InlineData("40001", true)]
    [InlineData("40P01", true)]
    [InlineData("23505", false)]
    [InlineData("23503", false)]
    [InlineData("42P01", false)]
    public void IsRetryable_PostgresSqlState_Theory(string sqlState, bool expected)
    {
        var exception = new DbUpdateException("save", new PostgresException("pg", "ERROR", "ERROR", sqlState));

        PersistenceFailureDetector.IsRetryable(exception).Should().Be(expected);
    }

    [Fact]
    [Trait("CatalogId", "UT-08")]
    public void IsRetryable_UnrelatedException_ReturnsFalse()
    {
        PersistenceFailureDetector.IsRetryable(new InvalidOperationException("x", new ArgumentException("y")))
            .Should().BeFalse();
    }

    [Fact]
    [Trait("CatalogId", "UT-08")]
    public void IsRetryable_AggregateWithTransientNpgsql_ReturnsTrue()
    {
        var transient = new NpgsqlException("connect", new SocketException((int)SocketError.ConnectionRefused));
        var aggregate = new AggregateException(
            new InvalidOperationException("nhánh đầu"),
            new DbUpdateException("nhánh hai", transient));

        PersistenceFailureDetector.IsRetryable(aggregate).Should().BeTrue();
    }

    [Fact]
    [Trait("CatalogId", "UT-08")]
    public void IsRetryable_AggregateWithUniqueViolation_ReturnsFalse()
    {
        var aggregate = new AggregateException(
            new InvalidOperationException("nhánh đầu"),
            new DbUpdateException("nhánh hai", new PostgresException("pg", "ERROR", "ERROR", "23505")));

        PersistenceFailureDetector.IsRetryable(aggregate).Should().BeFalse();
    }

    [Fact]
    [Trait("CatalogId", "UT-08")]
    public void IsRetryable_AggregateNestedInsideWrapper_ReturnsTrue()
    {
        var aggregate = new AggregateException(
            new InvalidOperationException("nhánh đầu"),
            new PersistenceUnavailableException("mất kết nối", new TimeoutException()));
        var wrapper = new InvalidOperationException("bọc ngoài", aggregate);

        PersistenceFailureDetector.IsRetryable(wrapper).Should().BeTrue();
    }
}
