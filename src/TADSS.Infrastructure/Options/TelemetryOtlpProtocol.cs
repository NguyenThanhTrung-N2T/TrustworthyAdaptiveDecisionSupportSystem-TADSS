namespace TADSS.Infrastructure.Options;

/// <summary>Giao thức OTLP mà exporter dùng để gửi trace, metric và log tới collector.</summary>
public enum TelemetryOtlpProtocol
{
    /// <summary>gRPC (cổng 4317 của collector).</summary>
    Grpc = 0,

    /// <summary>HTTP/protobuf (cổng 4318 của collector).</summary>
    HttpProtobuf = 1,
}
