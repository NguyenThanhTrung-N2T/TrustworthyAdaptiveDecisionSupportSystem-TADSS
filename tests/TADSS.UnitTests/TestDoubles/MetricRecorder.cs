using System.Diagnostics.Metrics;

namespace TADSS.UnitTests.TestDoubles;

/// <summary>
/// Bộ ghi số đo metric cho test: <see cref="MeterListener"/> chỉ lọc theo <b>đúng instance</b> <see cref="Meter"/> được truyền vào
/// (không theo tên) nên các test chạy song song không lẫn số đếm của nhau. Public và được project IntegrationTests compile-link trực tiếp (không qua tham chiếu project) để dùng chung một nguồn.
/// </summary>
public sealed class MetricRecorder : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly List<(string Instrument, long Value, KeyValuePair<string, object?>[] Tags)> _measurements = [];

    /// <summary>Bắt đầu ghi mọi instrument <see cref="long"/> của <paramref name="meter"/> (kể cả instrument đã tạo trước đó).</summary>
    public MetricRecorder(Meter meter)
    {
        ArgumentNullException.ThrowIfNull(meter);

        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter, meter))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            lock (_measurements)
            {
                _measurements.Add((instrument.Name, value, tags.ToArray()));
            }
        });
        _listener.Start();
    }

    /// <summary>
    /// Tổng giá trị đã ghi của instrument <paramref name="instrumentName"/> mà mọi nhãn trong <paramref name="tags"/> đều khớp
    /// (không truyền nhãn thì cộng mọi số đo của instrument).
    /// </summary>
    public long Sum(string instrumentName, params (string Key, string Value)[] tags)
    {
        lock (_measurements)
        {
            return _measurements
                .Where(m => m.Instrument == instrumentName
                    && tags.All(tag => m.Tags.Any(t => t.Key == tag.Key && Equals(t.Value, tag.Value))))
                .Sum(m => m.Value);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _listener.Dispose();
}
