using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TADSS.Application.Common;
using TADSS.Infrastructure.Options;
using TADSS.Infrastructure.Resilience;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace TADSS.UnitTests.Infrastructure.Resilience;

/// <summary>
/// UT-07 (giết mutant của MT-03): nội dung lý do mà Manager và nhật ký audit đọc cho từng nguyên nhân fallback, nguyên nhân không định nghĩa bị từ chối,
/// kiểm tra đối số null của <see cref="StrategyPipeline"/>.
/// </summary>
public class FallbackDecisionPolicyMessagesTests
{
    [Theory]
    [Trait("CatalogId", "UT-07")]
    [InlineData(FallbackCause.Timeout, "Chế độ an toàn tự động kích hoạt: Chiến lược đánh giá không phản hồi kịp thời hạn cho phép.")]
    [InlineData(FallbackCause.Exception, "Chế độ an toàn tự động kích hoạt: Chiến lược đánh giá gặp lỗi trong quá trình xử lý.")]
    [InlineData(FallbackCause.CircuitOpen, "Chế độ an toàn tự động kích hoạt: Chiến lược đánh giá đang tạm ngưng do liên tục gặp sự cố.")]
    public void Create_EachCause_ReasonDescribesTheCauseThenFullDepositPolicy(FallbackCause cause, string expectedFirstReason)
    {
        var decision = FallbackDecisionPolicy.Create(cause);

        decision.Reasons.Should().Equal(expectedFirstReason, "Áp dụng đặt cọc 100% theo chính sách an toàn.");
    }

    [Fact]
    [Trait("CatalogId", "UT-07")]
    public void Create_UndefinedCause_ThrowsArgumentOutOfRangeWithMessage()
    {
        var create = () => FallbackDecisionPolicy.Create((FallbackCause)99);

        create.Should().Throw<ArgumentOutOfRangeException>().Which.Message.Should().Contain("Nguyên nhân fallback không hợp lệ.");
    }

    [Fact]
    [Trait("CatalogId", "UT-07")]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        using var metrics = new TadssMetrics();
        var logger = NullLogger<StrategyPipeline>.Instance;
        var options = MsOptions.Create(new ResilienceOptions());

        var withoutOptions = () => new StrategyPipeline(null!, TimeProvider.System, metrics, logger);
        var withoutTime = () => new StrategyPipeline(options, null!, metrics, logger);
        var withoutMetrics = () => new StrategyPipeline(options, TimeProvider.System, null!, logger);
        var withoutLogger = () => new StrategyPipeline(options, TimeProvider.System, metrics, null!);

        withoutOptions.Should().Throw<ArgumentNullException>().WithParameterName("options");
        withoutTime.Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
        withoutMetrics.Should().Throw<ArgumentNullException>().WithParameterName("metrics");
        withoutLogger.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }
}
