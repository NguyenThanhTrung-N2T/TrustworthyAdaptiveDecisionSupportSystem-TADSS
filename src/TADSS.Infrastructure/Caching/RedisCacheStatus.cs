namespace TADSS.Infrastructure.Caching;

/// <summary>Kết quả một thao tác Redis của <see cref="RedisDecisionCache"/>.</summary>
internal enum RedisCacheStatus
{
    /// <summary>Đọc thấy quyết định hợp lệ.</summary>
    Hit,

    /// <summary>Đọc không thấy khóa, hoặc payload hỏng (coi như không có).</summary>
    Miss,

    /// <summary>Ghi thành công.</summary>
    Written,

    /// <summary>Circuit breaker Redis đang mở nên không gọi Redis.</summary>
    Bypassed,

    /// <summary>Redis lỗi hoặc quá hạn (đã ghi log, chưa chắc làm breaker mở).</summary>
    Failed,
}
