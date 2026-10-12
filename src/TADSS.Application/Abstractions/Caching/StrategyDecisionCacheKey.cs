namespace TADSS.Application.Abstractions.Caching;

/// <summary>
/// Dựng khóa cache của <c>StrategyDecision</c> theo spec 4.2.3:
/// <c>v2:{strategyName}:{strategyVersion}:{optionsFingerprint}:{requestHash}</c>.
/// Không gồm tiền tố namespace: cache cộng <c>CacheOptions.InstanceName</c> (mặc định <c>tadss:</c>) khi ghi/đọc nên khóa đầy đủ mặc định là <c>tadss:v2:...</c>.
/// </summary>
public static class StrategyDecisionCacheKey
{
    private const string Prefix = "v2:";

    /// <summary>Dựng khóa cache từ định danh strategy, dấu vân tay tham số và hash của request.</summary>
    /// <param name="strategyName">Tên strategy.</param>
    /// <param name="strategyVersion">Phiên bản strategy.</param>
    /// <param name="optionsFingerprint">Dấu vân tay tham số strategy và lịch cao điểm.</param>
    /// <param name="requestHash">Hash chuẩn hóa của request.</param>
    /// <exception cref="ArgumentException">Khi một thành phần rỗng hoặc chỉ gồm khoảng trắng.</exception>
    public static string Build(string strategyName, string strategyVersion, string optionsFingerprint, string requestHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(optionsFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);

        return $"{Prefix}{strategyName}:{strategyVersion}:{optionsFingerprint}:{requestHash}";
    }
}
