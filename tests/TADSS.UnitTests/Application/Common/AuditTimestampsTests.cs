using FluentAssertions;
using TADSS.Application.Common;
using Xunit;

namespace TADSS.UnitTests.Application.Common;

/// <summary>
/// UT-11: <see cref="AuditTimestamps.TruncateToMicroseconds"/> cắt phần dưới micro giây để mốc ghi vào <c>timestamptz</c> khớp độ chính xác
/// của PostgreSQL và bản đọc lại trùng từng tick với bản đã trả.
/// </summary>
public class AuditTimestampsTests
{
    /// <summary>Tick lẻ 0..9 bị bỏ: kết quả là bội số 10 tick, offset 0, cùng thời điểm với đầu vào trừ phần dư.</summary>
    [Theory]
    [Trait("CatalogId", "UT-11")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void TruncateToMicroseconds_DropsSubMicrosecondTicks_Theory(int extraTicks)
    {
        var baseTime = new DateTimeOffset(2026, 11, 18, 9, 15, 0, TimeSpan.FromHours(7)).AddTicks(1_234_560);
        var input = baseTime.AddTicks(extraTicks);

        var truncated = AuditTimestamps.TruncateToMicroseconds(input);

        truncated.Offset.Should().Be(TimeSpan.Zero);
        (truncated.UtcTicks % TimeSpan.TicksPerMicrosecond).Should().Be(0);
        truncated.UtcTicks.Should().Be(input.UtcTicks - (input.UtcTicks % TimeSpan.TicksPerMicrosecond));
        (input.UtcTicks - truncated.UtcTicks).Should().BeLessThan(TimeSpan.TicksPerMicrosecond);
    }
}
