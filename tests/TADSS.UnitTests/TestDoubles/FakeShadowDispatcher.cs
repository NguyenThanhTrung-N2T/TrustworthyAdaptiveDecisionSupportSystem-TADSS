using TADSS.Application.Shadow;

namespace TADSS.UnitTests.TestDoubles;

/// <summary>
/// <see cref="IShadowDispatcher"/> giả viết tay: ghi lại mọi lần <see cref="Dispatch"/> (tên strategy chính và việc shadow) để test kiểm tra
/// <c>DecisionService</c> chỉ gửi việc khi chính request đó vừa ghi bản <c>PRIMARY</c> mới.
/// </summary>
internal sealed class FakeShadowDispatcher : IShadowDispatcher
{
    private readonly List<(string PrimaryStrategyName, ShadowWorkItem Item)> _calls = [];

    /// <summary>Các lần gửi việc, theo thứ tự.</summary>
    public IReadOnlyList<(string PrimaryStrategyName, ShadowWorkItem Item)> Calls
    {
        get
        {
            lock (_calls)
            {
                return _calls.ToArray();
            }
        }
    }

    public void Dispatch(string primaryStrategyName, ShadowWorkItem item)
    {
        lock (_calls)
        {
            _calls.Add((primaryStrategyName, item));
        }
    }
}
