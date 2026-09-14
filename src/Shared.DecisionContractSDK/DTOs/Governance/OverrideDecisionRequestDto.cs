using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Shared.DecisionContractSDK.DTOs.Governance;

/// <summary>
/// Yêu cầu can thiệp ghi đè kết quả của Quản lý sân (Human-in-the-loop).
/// </summary>
public class OverrideDecisionRequestDto
{
    [Required(ErrorMessage = "Thông tin decision không được để trống")]
    [JsonPropertyName("decision")]
    public OverrideTargetPayloadDto Decision { get; set; } = new();

    [Required(ErrorMessage = "Lý do ghi đè không được để trống")]
    [MinLength(5, ErrorMessage = "Lý do ghi đè phải có ít nhất 5 ký tự")]
    [MaxLength(500, ErrorMessage = "Lý do ghi đè tối đa 500 ký tự")]
    [JsonPropertyName("overrideReason")]
    public string OverrideReason { get; set; } = string.Empty;

    [Required(ErrorMessage = "Người ghi đè không được để trống")]
    [MinLength(1)]
    [MaxLength(100)]
    [JsonPropertyName("overriddenBy")]
    public string OverriddenBy { get; set; } = string.Empty;
}

public class OverrideTargetPayloadDto
{
    [Required(ErrorMessage = "Trạng thái mới không được để trống")]
    [JsonPropertyName("status")]
    public string Status { get; set; } = "APPROVED";
}
