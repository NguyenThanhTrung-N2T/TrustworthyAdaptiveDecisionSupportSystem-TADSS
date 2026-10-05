using Shared.DecisionContractSDK.Domain;
using TADSS.Application.Routing;
using TADSS.Application.Strategies;

namespace TADSS.UnitTests.TestDoubles;

/// <summary>
/// <see cref="IStrategyRouter"/> giả viết tay: trả một kết quả định tuyến cố định, đếm số lần <see cref="Route"/> được gọi và ghi lại reservation cuối;
/// có thể cấu hình ném lỗi để chứng minh một bước nào đó phải chạy trước khi định tuyến (hoặc định tuyến không được gọi).
/// </summary>
internal sealed class FakeStrategyRouter : IStrategyRouter
{
    private readonly StrategyRoute? _route;
    private int _routeCount;

    /// <summary>Router luôn trả <paramref name="strategy"/> kèm <paramref name="ruleName"/> (<c>null</c> = strategy mặc định).</summary>
    public FakeStrategyRouter(RegisteredStrategy strategy, string? ruleName = null)
    {
        _route = new StrategyRoute(strategy, ruleName);
    }

    /// <summary>Router ném <see cref="InvalidOperationException"/> ngay khi được gọi.</summary>
    public FakeStrategyRouter()
    {
    }

    /// <summary>Số lần <see cref="Route"/> được gọi.</summary>
    public int RouteCount => Volatile.Read(ref _routeCount);

    /// <summary>Reservation của lần gọi cuối.</summary>
    public Reservation? LastReservation { get; private set; }

    public StrategyRoute Route(Reservation reservation)
    {
        Interlocked.Increment(ref _routeCount);
        LastReservation = reservation;
        return _route ?? throw new InvalidOperationException("Router không được gọi trong kịch bản này.");
    }
}
