using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Enums;

namespace TADSS.Runtime.Api.V2.Infrastructure.Caching;

/// <summary>
/// Hình dạng JSON của <see cref="StrategyDecision"/> trong Redis (serialize bằng <c>TadssJson.Options</c>).
/// <see cref="StrategyDecision"/> có constructor private nên được dựng lại qua factory đúng <see cref="Status"/>,
/// nhờ đó mọi invariant được kiểm tra lại khi đọc; payload không nhất quán bị từ chối bằng ngoại lệ.
/// </summary>
internal sealed class CachedStrategyDecision
{
    /// <summary>Trạng thái quyết định (chuỗi đúng tên thành viên trên wire).</summary>
    public required DecisionStatus Status { get; init; }

    /// <summary>Tỷ lệ cọc; chỉ có khi <see cref="DecisionStatus.APPROVED"/>.</summary>
    public required decimal? DepositRate { get; init; }

    /// <summary>Mã lý do.</summary>
    public required string ReasonCode { get; init; }

    /// <summary>Lý do cho người đọc.</summary>
    public required List<string> Reasons { get; init; }

    /// <summary>Khuyến nghị kèm theo.</summary>
    public required List<CachedRecommendation> Recommendations { get; init; }

    /// <summary>Chụp lại <paramref name="decision"/> để ghi vào Redis.</summary>
    public static CachedStrategyDecision From(StrategyDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        return new CachedStrategyDecision
        {
            Status = decision.Status,
            DepositRate = decision.DepositRate,
            ReasonCode = decision.ReasonCode,
            Reasons = [.. decision.Reasons],
            Recommendations = [.. decision.Recommendations.Select(item => new CachedRecommendation
            {
                Type = item.Type,
                Description = item.Description,
            })],
        };
    }

    /// <summary>
    /// Dựng lại <see cref="StrategyDecision"/> qua factory đúng trạng thái.
    /// </summary>
    /// <exception cref="InvalidDataException">Khi payload tự mâu thuẫn (tỷ lệ cọc không khớp trạng thái, khuyến nghị ngoài <c>NEED_APPROVAL</c>, phần tử null).</exception>
    /// <exception cref="Shared.DecisionContractSDK.Domain.DomainInvariantException">Khi vi phạm invariant của <see cref="StrategyDecision"/>.</exception>
    public StrategyDecision ToDecision()
    {
        if (Reasons is null || Recommendations is null)
        {
            throw new InvalidDataException("Payload thiếu reasons hoặc recommendations.");
        }

        if (Recommendations.Any(item => item is null))
        {
            throw new InvalidDataException("Recommendations chứa phần tử null.");
        }

        switch (Status)
        {
            case DecisionStatus.APPROVED:
                RequireNoRecommendations();
                return StrategyDecision.Approve(
                    DepositRate ?? throw new InvalidDataException("Quyết định APPROVED thiếu depositRate."),
                    ReasonCode,
                    Reasons);

            case DecisionStatus.NEED_APPROVAL:
                RequireNoDepositRate();
                return StrategyDecision.RequireApproval(
                    ReasonCode,
                    Reasons,
                    [.. Recommendations.Select(item => new DecisionRecommendation(item.Type, item.Description))]);

            case DecisionStatus.REJECTED:
                RequireNoDepositRate();
                RequireNoRecommendations();
                return StrategyDecision.Reject(ReasonCode, Reasons);

            default:
                throw new InvalidDataException($"Trạng thái '{Status}' không được hỗ trợ.");
        }
    }

    private void RequireNoDepositRate()
    {
        if (DepositRate is not null)
        {
            throw new InvalidDataException($"Quyết định {Status} không được có depositRate.");
        }
    }

    private void RequireNoRecommendations()
    {
        if (Recommendations.Count > 0)
        {
            throw new InvalidDataException($"Quyết định {Status} không được có recommendations.");
        }
    }
}

/// <summary>Hình dạng JSON của <see cref="DecisionRecommendation"/> trong Redis.</summary>
internal sealed class CachedRecommendation
{
    /// <summary>Loại khuyến nghị (chuỗi đúng tên thành viên trên wire).</summary>
    public required RecommendationType Type { get; init; }

    /// <summary>Mô tả cho người đọc.</summary>
    public required string Description { get; init; }
}
