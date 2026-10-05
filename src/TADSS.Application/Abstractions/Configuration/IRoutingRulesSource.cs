using TADSS.Application.Options;

namespace TADSS.Application.Abstractions.Configuration;

/// <summary>
/// Nguồn cấu hình định tuyến strategy (<see cref="StrategyRoutingOptions.SectionName"/>) cho <c>StrategyRouter</c> (A-01, A-02): đọc bản hiện tại và báo khi
/// nguồn đổi. Đọc file cấu hình và theo dõi thay đổi là việc của hạ tầng; Application không tự giữ <c>IConfiguration</c>.
/// </summary>
public interface IRoutingRulesSource
{
    /// <summary>
    /// Đọc bản cấu hình định tuyến hiện tại. Lỗi mà bộ bind nuốt (giá trị đơn sai kiểu làm mất cả rule, phần tử <c>BranchIds</c>/<c>Channels</c>/<c>Tiers</c>
    /// rỗng hoặc sai kiểu bị bỏ) được thêm vào <paramref name="failures"/>, mỗi lỗi chỉ nêu đường dẫn khóa, <b>không</b> kèm giá trị cấu hình.
    /// Không ném vì lỗi cấu hình; kiểm tra nghiệp vụ (validator, tên strategy) do caller làm.
    /// </summary>
    /// <param name="failures">Danh sách nhận lỗi bind (chỉ thêm vào, không xóa).</param>
    /// <returns>Bản cấu hình vừa đọc (phần bind được).</returns>
    StrategyRoutingOptions Read(ICollection<string> failures);

    /// <summary>Gọi <paramref name="onChanged"/> mỗi khi nguồn cấu hình đổi (có thể từ luồng khác); dispose kết quả để hủy theo dõi.</summary>
    /// <param name="onChanged">Callback; caller tự đảm bảo nó không ném.</param>
    /// <returns>Đăng ký theo dõi; dispose để hủy.</returns>
    IDisposable Subscribe(Action onChanged);
}
