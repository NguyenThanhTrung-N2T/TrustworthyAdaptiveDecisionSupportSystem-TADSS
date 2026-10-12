using System.Diagnostics;
using TADSS.Application.Common;

namespace TADSS.UnitTests.TestDoubles;

/// <summary>
/// Bộ ghi span cho test: <see cref="ActivityListener"/> nghe nguồn <c>TADSS.Runtime</c> (lấy mẫu mọi span) và giữ lại span đã kết thúc.
/// <see cref="ActivitySource"/> là đối tượng toàn cục nên các test chạy song song nhìn thấy span của nhau; test phải lọc theo giá trị thuộc tính duy nhất của chính mình
/// (tên strategy, correlation id) bằng <see cref="Where"/>. Public và được project IntegrationTests compile-link trực tiếp để dùng chung một nguồn.
/// </summary>
public sealed class ActivityRecorder : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly List<Activity> _stopped = [];

    /// <summary>Bắt đầu nghe nguồn <paramref name="sourceName"/> (mặc định nguồn của TADSS).</summary>
    public ActivityRecorder(string sourceName = TadssActivitySource.SourceName)
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (_stopped)
                {
                    _stopped.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>Các span đã kết thúc theo thứ tự kết thúc.</summary>
    public IReadOnlyList<Activity> Stopped
    {
        get
        {
            lock (_stopped)
            {
                return _stopped.ToArray();
            }
        }
    }

    /// <summary>Span đã kết thúc tên <paramref name="spanName"/> có thuộc tính <paramref name="tagKey"/> = <paramref name="tagValue"/>.</summary>
    public IReadOnlyList<Activity> Where(string spanName, string tagKey, string tagValue) =>
        Stopped.Where(activity => activity.OperationName == spanName && Equals(activity.GetTagItem(tagKey)?.ToString(), tagValue)).ToList();

    /// <inheritdoc />
    public void Dispose() => _listener.Dispose();
}
