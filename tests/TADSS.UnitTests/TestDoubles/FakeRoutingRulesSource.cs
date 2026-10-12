using TADSS.Application.Abstractions.Configuration;
using TADSS.Application.Options;

namespace TADSS.UnitTests.TestDoubles;

/// <summary>
/// <see cref="IRoutingRulesSource"/> giả viết tay: luôn trả cùng một <see cref="StrategyRoutingOptions"/>, không có lỗi bind và không bao giờ báo đổi
/// (dùng cho test router chỉ quan tâm bảng định tuyến lúc khởi động). Kịch bản nạp lại / lỗi binder dùng adapter thật trên cấu hình trong bộ nhớ.
/// </summary>
internal sealed class FakeRoutingRulesSource(StrategyRoutingOptions options) : IRoutingRulesSource
{
    /// <summary>Số lần <see cref="Subscribe"/> được gọi.</summary>
    public int SubscribeCount { get; private set; }

    public StrategyRoutingOptions Read(ICollection<string> failures) => options;

    public IDisposable Subscribe(Action onChanged)
    {
        SubscribeCount++;
        return NoopRegistration.Instance;
    }

    private sealed class NoopRegistration : IDisposable
    {
        public static readonly NoopRegistration Instance = new();

        public void Dispose()
        {
        }
    }
}
