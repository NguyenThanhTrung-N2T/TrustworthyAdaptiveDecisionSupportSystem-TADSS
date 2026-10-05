namespace TADSS.Application.Options;

/// <summary>
/// Một rule định tuyến (<c>Tadss:Routing:Rules:{i}</c>): khi mọi điều kiện của <see cref="When"/> khớp (AND) thì request dùng <see cref="Strategy"/>.
/// </summary>
public sealed class RoutingRuleOptions
{
    /// <summary>
    /// Tên rule (bắt buộc, tối đa 64 ký tự, duy nhất không phân biệt hoa thường); ghi vào log tóm tắt quyết định (A-04).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Tên strategy được chọn khi rule khớp (bắt buộc, phải có trong registry, không phân biệt hoa thường).
    /// </summary>
    public string Strategy { get; set; } = string.Empty;

    /// <summary>
    /// Điều kiện của rule; phải có ít nhất một điều kiện (rule không điều kiện khớp mọi request và che các rule sau).
    /// </summary>
    public RoutingConditionOptions When { get; set; } = new();
}
