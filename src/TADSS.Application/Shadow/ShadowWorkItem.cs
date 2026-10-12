using System.Diagnostics;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.DTOs.Decisions;

namespace TADSS.Application.Shadow;

/// <summary>
/// Một việc shadow: đủ dữ kiện để chạy lại strategy ứng viên trên đúng ngữ cảnh của bản <c>PRIMARY</c> vừa lưu và ghép cặp với nó
/// (cùng <c>reservation_id</c>, <c>request_hash</c>, <c>created_at</c>, <c>correlation_id</c>).
/// </summary>
/// <param name="Request">Request gốc (serialize nguyên vẹn vào <c>request_payload</c> của bản <c>SHADOW</c>).</param>
/// <param name="Reservation">Reservation đã dựng từ request.</param>
/// <param name="RequestHash">Hash của request đã chuẩn hóa.</param>
/// <param name="NowUtc">Mốc <c>nowUtc</c> của bản <c>PRIMARY</c>, dùng lại để hai bản so sánh được trên cùng ngữ cảnh.</param>
/// <param name="CorrelationId">Mã tương quan; <c>null</c> khi không có.</param>
/// <param name="RequestActivityContext">Ngữ cảnh trace của request gốc (span <c>tadss.decision.evaluate</c>) để span shadow liên kết tới; <c>null</c> khi không có trace.</param>
public sealed record ShadowWorkItem(
    DecisionRequestDto Request,
    Reservation Reservation,
    string RequestHash,
    DateTimeOffset NowUtc,
    string? CorrelationId,
    ActivityContext? RequestActivityContext = null);
