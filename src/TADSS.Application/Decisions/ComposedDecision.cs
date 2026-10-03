using Shared.DecisionContractSDK.DTOs.Decisions;
using TADSS.Application.Abstractions.Persistence;

namespace TADSS.Application.Decisions;

/// <summary>
/// Kết quả ghép: response trả cho client và bản ghi audit cùng dữ liệu nghiệp vụ.
/// </summary>
/// <param name="Response">Response trả cho client.</param>
/// <param name="Record">Bản ghi <c>PRIMARY</c> sẽ lưu vào audit.</param>
public sealed record ComposedDecision(DecisionResultDto Response, DecisionExecutionRecord Record);
