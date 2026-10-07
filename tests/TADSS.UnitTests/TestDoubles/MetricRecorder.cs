using System.Diagnostics.Metrics;

namespace TADSS.UnitTests.TestDoubles;

/// <summary>
/// Bộ ghi số đo metric cho test: <see cref="MeterListener"/> chỉ lọc theo <b>đúng instance</b> <see cref="Meter"/> được truyền vào
/// (không theo tên) nên các test chạy song song không lẫn số đếm của nhau. Public và được project IntegrationTests compile-link trực tiếp (không qua tham chiếu project) để dùng chung một nguồn.
/// <para>
/// Counter (<see cref="Sum"/>), histogram (<see cref="Count"/>, <see cref="Values"/>) được ghi liên tục từ lúc tạo;
/// gauge là instrument quan sát nên chỉ có số đo khi gọi <see cref="ObserveGauge"/> (ép lấy giá trị hiện tại).
/// </para>
/// </summary>
public sealed class MetricRecorder : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly List<(string Instrument, long Value, KeyValuePair<string, object?>[] Tags)> _measurements = [];
    private readonly List<(string Instrument, double Value, KeyValuePair<string, object?>[] Tags)> _histograms = [];
    private readonly List<GaugeSample> _gauges = [];
    private readonly List<string> _gaugeNames = [];

    /// <summary>Bắt đầu ghi mọi instrument của <paramref name="meter"/> (kể cả instrument đã tạo trước đó).</summary>
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
            if (instrument.IsObservable)
            {
                AddGauge(instrument.Name, value, tags);
                return;
            }

            lock (_measurements)
            {
                _measurements.Add((instrument.Name, value, tags.ToArray()));
            }
        });
        _listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) => AddGauge(instrument.Name, value, tags));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
        {
            lock (_histograms)
            {
                _histograms.Add((instrument.Name, value, tags.ToArray()));
            }
        });
        _listener.Start();
    }

    /// <summary>Một số đo của gauge tại thời điểm quan sát: nhãn (giá trị chuyển thành chuỗi) và giá trị.</summary>
    public readonly record struct GaugeSample(IReadOnlyDictionary<string, string?> Tags, long Value);

    /// <summary>
    /// Tổng giá trị đã ghi của counter <paramref name="instrumentName"/> mà mọi nhãn trong <paramref name="tags"/> đều khớp
    /// (không truyền nhãn thì cộng mọi số đo của instrument).
    /// </summary>
    public long Sum(string instrumentName, params (string Key, string Value)[] tags)
    {
        lock (_measurements)
        {
            return _measurements
                .Where(m => m.Instrument == instrumentName && HasTags(m.Tags, tags))
                .Sum(m => m.Value);
        }
    }

    /// <summary>Số lần ghi của histogram <paramref name="instrumentName"/> mà mọi nhãn trong <paramref name="tags"/> đều khớp.</summary>
    public int Count(string instrumentName, params (string Key, string Value)[] tags) => Values(instrumentName, tags).Count;

    /// <summary>Các giá trị đã ghi của histogram <paramref name="instrumentName"/> mà mọi nhãn trong <paramref name="tags"/> đều khớp.</summary>
    public IReadOnlyList<double> Values(string instrumentName, params (string Key, string Value)[] tags)
    {
        lock (_histograms)
        {
            return _histograms
                .Where(m => m.Instrument == instrumentName && HasTags(m.Tags, tags))
                .Select(m => m.Value)
                .ToList();
        }
    }

    /// <summary>Ép mọi gauge báo giá trị hiện tại rồi trả các số đo của <paramref name="instrumentName"/> (rỗng nếu gauge không phát số đo).</summary>
    public IReadOnlyList<GaugeSample> ObserveGauge(string instrumentName)
    {
        lock (_gauges)
        {
            _gauges.Clear();
            _gaugeNames.Clear();
        }

        _listener.RecordObservableInstruments();

        lock (_gauges)
        {
            return _gauges.Where((_, index) => _gaugeNames[index] == instrumentName).ToList();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _listener.Dispose();

    private static bool HasTags(KeyValuePair<string, object?>[] actual, (string Key, string Value)[] expected) =>
        expected.All(tag => actual.Any(t => t.Key == tag.Key && Equals(t.Value, tag.Value)));

    private void AddGauge(string instrumentName, long value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var map = tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value?.ToString());
        lock (_gauges)
        {
            _gauges.Add(new GaugeSample(map, value));
            _gaugeNames.Add(instrumentName);
        }
    }
}
