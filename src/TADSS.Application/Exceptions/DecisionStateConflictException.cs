using Shared.DecisionContractSDK.Common;

namespace TADSS.Application.Exceptions;

/// <summary>
/// Quyết định đang ở trạng thái không cho phép thao tác (override/outcome): không còn chờ duyệt, đã được override, quá hạn duyệt hoặc đã có
/// outcome khác. Được map thành 409 với <see cref="ErrorCode"/> tương ứng (INTEGRATION_SPEC §3.8); message (tiếng Việt, chỉ chứa
/// <c>decisionId</c> do client gửi) được phép trả cho client. Tạo qua các factory để mã lỗi luôn lấy từ <see cref="ErrorCodes"/> của SDK.
/// </summary>
public sealed class DecisionStateConflictException : Exception
{
    private DecisionStateConflictException(string errorCode, Guid decisionId, string message)
        : base(message)
    {
        ErrorCode = errorCode;
        DecisionId = decisionId;
    }

    /// <summary>Mã lỗi của contract (một trong <see cref="ErrorCodes"/>), trả trong trường <c>code</c>.</summary>
    public string ErrorCode { get; }

    /// <summary>Định danh quyết định gây xung đột.</summary>
    public Guid DecisionId { get; }

    /// <summary>Quyết định không ở trạng thái chờ duyệt (hoặc đã có outcome) nên không thể override.</summary>
    /// <param name="decisionId">Định danh quyết định.</param>
    public static DecisionStateConflictException NotPendingApproval(Guid decisionId) =>
        new(ErrorCodes.NotPendingApproval, decisionId, $"Quyết định '{decisionId}' không ở trạng thái chờ duyệt.");

    /// <summary>Quyết định đã được override bằng một yêu cầu khác.</summary>
    /// <param name="decisionId">Định danh quyết định.</param>
    public static DecisionStateConflictException AlreadyOverridden(Guid decisionId) =>
        new(ErrorCodes.AlreadyOverridden, decisionId, $"Quyết định '{decisionId}' đã được ghi đè.");

    /// <summary>Đã quá hạn duyệt của quyết định.</summary>
    /// <param name="decisionId">Định danh quyết định.</param>
    public static DecisionStateConflictException ApprovalExpired(Guid decisionId) =>
        new(ErrorCodes.ApprovalExpired, decisionId, $"Quyết định '{decisionId}' đã quá hạn duyệt.");

    /// <summary>Quyết định đã có outcome với nội dung khác.</summary>
    /// <param name="decisionId">Định danh quyết định.</param>
    public static DecisionStateConflictException OutcomeAlreadyRecorded(Guid decisionId) =>
        new(ErrorCodes.OutcomeAlreadyRecorded, decisionId, $"Quyết định '{decisionId}' đã có kết cục khác.");
}
