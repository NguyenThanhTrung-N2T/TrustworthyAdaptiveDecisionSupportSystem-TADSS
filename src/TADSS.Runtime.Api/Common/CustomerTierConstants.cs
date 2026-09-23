namespace TADSS.Runtime.Api.Common;

/// <summary>
/// Canonical customer loyalty tier constants and qualification point thresholds 
/// aligned strictly with the SmashCourt Loyalty System specification:
/// <list type="bullet">
///   <item><description><b>Bronze</b>: 0 points (Default tier)</description></item>
///   <item><description><b>Silver</b>: 1,000 points</description></item>
///   <item><description><b>Gold</b>: 3,000 points</description></item>
///   <item><description><b>Platinum</b>: 7,000 points</description></item>
///   <item><description><b>Diamond</b>: 15,000 points (Highest tier)</description></item>
/// </list>
/// </summary>
public static class CustomerTierConstants
{
    // ─── SmashCourt Official Loyalty Ranks & Point Thresholds ───────────

    /// <summary>Hạng Đồng (Bronze) — Điểm tối thiểu: 0 (Hạng mặc định khởi tạo).</summary>
    public const string Bronze = "BRONZE";
    public const int BronzeMinPoints = 0;

    /// <summary>Hạng Bạc (Silver) — Điểm tối thiểu: 1,000.</summary>
    public const string Silver = "SILVER";
    public const int SilverMinPoints = 1_000;

    /// <summary>Hạng Vàng (Gold) — Điểm tối thiểu: 3,000.</summary>
    public const string Gold = "GOLD";
    public const int GoldMinPoints = 3_000;

    /// <summary>Hạng Bạch kim (Platinum) — Điểm tối thiểu: 7,000.</summary>
    public const string Platinum = "PLATINUM";
    public const int PlatinumMinPoints = 7_000;

    /// <summary>Hạng Kim cương (Diamond) — Điểm tối thiểu: 15,000 (Hạng cao cấp nhất).</summary>
    public const string Diamond = "DIAMOND";
    public const int DiamondMinPoints = 15_000;

    // ─── Domain & Backward Compatibility Aliases ────────────────────────

    /// <summary>Hạng tiêu chuẩn (Standard — tương đương Bronze 0 điểm).</summary>
    public const string Standard = "STANDARD";

    /// <summary>Hạng VIP (tương đương nhóm khách cao cấp Platinum / Diamond).</summary>
    public const string Vip = "VIP";

    // ─── Helper Evaluation Methods ──────────────────────────────────────

    /// <summary>
    /// Kiểm tra xem rank có phải là nhóm khách hàng cao cấp (Platinum, Diamond, VIP) hay không.
    /// </summary>
    public static bool IsPremiumTier(string? tier)
    {
        if (string.IsNullOrWhiteSpace(tier)) return false;
        var normalized = tier.Trim().ToUpperInvariant();
        return normalized is Diamond or Platinum or Vip;
    }

    /// <summary>
    /// Kiểm tra xem rank có đạt từ Hạng Vàng (Gold - 3,000 pts) trở lên hay không.
    /// </summary>
    public static bool IsGoldOrHigher(string? tier)
    {
        if (string.IsNullOrWhiteSpace(tier)) return false;
        var normalized = tier.Trim().ToUpperInvariant();
        return normalized is Gold or Platinum or Diamond or Vip or "VÀNG";
    }
}
