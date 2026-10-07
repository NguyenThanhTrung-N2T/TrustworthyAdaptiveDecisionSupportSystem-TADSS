using TADSS.Infrastructure.Options;

namespace TADSS.Infrastructure.FaultInjection;

/// <summary>
/// Bản chụp bất biến của phần cấu hình tiêm lỗi đổi được lúc chạy (O-17): <see cref="Profile"/> và <see cref="DelayMs"/> luôn đi cùng nhau.
/// <see cref="FaultInjectionControl"/> thay <b>nguyên</b> tham chiếu khi nạp lại thành công nên không lời gọi nào thấy profile mới đi với <c>DelayMs</c> cũ.
/// </summary>
/// <param name="Profile">Kiểu lỗi được tiêm.</param>
/// <param name="DelayMs">Thời gian chờ/quay vòng (ms) của các profile <c>Delay</c>, <c>IgnoreToken</c>, <c>BusyCpu</c>.</param>
public sealed record FaultInjectionSnapshot(FaultInjectionProfile Profile, int DelayMs);
