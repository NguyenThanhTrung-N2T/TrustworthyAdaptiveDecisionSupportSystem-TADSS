using Shared.DecisionContractSDK.Interfaces;

namespace TADSS.Application.Strategies;

/// <summary>
/// Một strategy v2 đã đăng ký cùng dấu vân tay tham số của nó (gồm cả lịch cao điểm, B-08). Dấu vân tay đi vào khóa
/// cache và audit để đổi tham số/lịch thì không dùng lại kết quả cũ.
/// </summary>
/// <param name="Strategy">Strategy v2.</param>
/// <param name="OptionsFingerprint">16 ký tự hex thường, tất định theo tham số và lịch cao điểm.</param>
public sealed record RegisteredStrategy(IReservationDecisionStrategy Strategy, string OptionsFingerprint);
