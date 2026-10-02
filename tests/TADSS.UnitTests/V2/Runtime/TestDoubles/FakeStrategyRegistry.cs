using TADSS.Runtime.Api.V2.Services.Strategies;

namespace TADSS.UnitTests.V2.Runtime.TestDoubles;

/// <summary>
/// <see cref="IStrategyRegistry"/> giả viết tay: trả một strategy cố định và đếm số lần <see cref="ResolveActive"/> được gọi;
/// có thể cấu hình ném lỗi để chứng minh một bước nào đó phải chạy trước khi chọn strategy.
/// </summary>
internal sealed class FakeStrategyRegistry : IStrategyRegistry
{
    private readonly RegisteredStrategy? _active;
    private int _resolveCount;

    /// <summary>Registry trả <paramref name="active"/> khi được hỏi.</summary>
    public FakeStrategyRegistry(RegisteredStrategy active)
    {
        _active = active;
    }

    /// <summary>Registry ném <see cref="InvalidOperationException"/> ngay khi bị hỏi strategy kích hoạt.</summary>
    public FakeStrategyRegistry()
    {
    }

    /// <summary>Số lần <see cref="ResolveActive"/> được gọi.</summary>
    public int ResolveCount => Volatile.Read(ref _resolveCount);

    public IReadOnlyList<RegisteredStrategy> All => _active is null ? [] : [_active];

    public RegisteredStrategy ResolveActive()
    {
        Interlocked.Increment(ref _resolveCount);
        return _active ?? throw new InvalidOperationException("Registry không được gọi trong kịch bản này.");
    }

    public RegisteredStrategy ResolveByName(string strategyName) =>
        throw new KeyNotFoundException(strategyName);
}
