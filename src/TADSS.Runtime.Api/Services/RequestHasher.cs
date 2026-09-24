using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.DTOs.Decisions;

namespace TADSS.Runtime.Api.Services;

/// <summary>
/// Tiện ích chuẩn hóa và băm SHA-256 (Canonical Request Hasher) theo chuẩn RFC 8785.
/// </summary>
public static class RequestHasher
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string ComputeHash(DecisionRequestDto request)
    {
        // Trích xuất các trường nghiệp vụ quan trọng theo thứ tự xác định (RFC 8785 canonical style)
        // Metadata được sắp xếp theo key (Lexicographical order) để đảm bảo tính tất định
        var sortedMetadata = request.Metadata != null && request.Metadata.Count > 0
            ? new SortedDictionary<string, string>(request.Metadata, StringComparer.Ordinal)
            : null;

        var canonicalObject = new
        {
            request.ReservationId,
            request.CourtId,
            request.BranchId,
            CourtTypeId = request.CourtTypeId?.ToString() ?? string.Empty,
            request.CustomerId,
            CustomerTier = request.CustomerTier?.Trim().ToUpperInvariant() ?? TADSS.Runtime.Api.Common.CustomerTierConstants.Standard,
            StartTime = request.StartTime.ToUniversalTime().ToString("O"),
            EndTime = request.EndTime.ToUniversalTime().ToString("O"),
            BookingType = request.BookingType.Trim().ToUpperInvariant(),
            TotalPrice = request.TotalPrice.ToString("F2", CultureInfo.InvariantCulture),
            DepositAmount = request.DepositAmount.ToString("F2", CultureInfo.InvariantCulture),
            PaymentStatus = request.PaymentStatus.Trim().ToUpperInvariant(),
            Priority = request.Priority?.Trim().ToUpperInvariant() ?? Shared.DecisionContractSDK.Enums.BookingPriority.NORMAL.ToString(),
            Metadata = sortedMetadata
        };

        var json = JsonSerializer.Serialize(canonicalObject, SerializerOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        var hashBytes = SHA256.HashData(bytes);

        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
