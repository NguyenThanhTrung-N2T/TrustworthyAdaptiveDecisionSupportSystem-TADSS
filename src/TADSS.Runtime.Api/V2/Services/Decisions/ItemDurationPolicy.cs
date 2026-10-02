using System.Globalization;
using Microsoft.Extensions.Options;
using DomainInvariantException = Shared.DecisionContractSDK.Domain.DomainInvariantException;
using Shared.DecisionContractSDK.V2.Domain;
using TADSS.Runtime.Api.V2.Configuration;

namespace TADSS.Runtime.Api.V2.Services.Decisions;

/// <summary>
/// Chính sách thời lượng item (P-01): mỗi item phải dài từ <c>MinItemMinutes</c> đến <c>MaxItemMinutes</c> phút
/// (tính cả hai biên). SDK chỉ bảo đảm <c>End &gt; Start</c>; giới hạn cụ thể thuộc runtime.
/// </summary>
public sealed class ItemDurationPolicy
{
    private readonly int _minMinutes;
    private readonly int _maxMinutes;

    /// <summary>
    /// Khởi tạo chính sách từ cấu hình <c>Tadss:Validation</c> (đã được kiểm tra khi khởi động).
    /// </summary>
    public ItemDurationPolicy(IOptions<ValidationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _minMinutes = options.Value.MinItemMinutes;
        _maxMinutes = options.Value.MaxItemMinutes;
    }

    /// <summary>
    /// Bảo đảm mọi item của reservation nằm trong giới hạn thời lượng. Thời lượng tính trên instant
    /// (<c>End − Start</c> của <see cref="DateTimeOffset"/>), không phụ thuộc giờ địa phương hay đổi giờ mùa hè.
    /// </summary>
    /// <param name="reservation">Reservation cần kiểm tra.</param>
    /// <exception cref="DomainInvariantException">
    /// <c>INVALID_ITEM_DURATION</c> với <c>FieldName = "items[{index}]"</c> khi một item vi phạm giới hạn.
    /// </exception>
    public void EnsureWithinLimits(Reservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        for (var index = 0; index < reservation.Items.Count; index++)
        {
            var minutes = (reservation.Items[index].End - reservation.Items[index].Start).TotalMinutes;
            if (minutes < _minMinutes || minutes > _maxMinutes)
            {
                throw new DomainInvariantException(
                    "INVALID_ITEM_DURATION",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Thời lượng mỗi item phải từ {_minMinutes} đến {_maxMinutes} phút; item thứ {index + 1} dài {minutes:0.##} phút."),
                    $"items[{index}]");
            }
        }
    }
}
