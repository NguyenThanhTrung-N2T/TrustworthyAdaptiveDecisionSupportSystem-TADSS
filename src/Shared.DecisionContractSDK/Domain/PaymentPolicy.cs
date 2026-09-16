using Shared.DecisionContractSDK.Enums;

namespace Shared.DecisionContractSDK.Domain;

/// <summary>
/// Value Object đại diện cho chính sách giá, tiền cọc và trạng thái thanh toán của phiên đặt sân.
/// </summary>
public record PaymentPolicy
{
    /// <summary>
    /// Tổng chi phí thuê sân (VND). Luôn &gt;= 0.
    /// </summary>
    public decimal TotalPrice { get; init; }

    /// <summary>
    /// Số tiền cọc khách hàng đã thanh toán (VND). Luôn thỏa mãn: 0 &lt;= DepositAmount &lt;= TotalPrice.
    /// </summary>
    public decimal DepositAmount { get; init; }

    /// <summary>
    /// Trạng thái thanh toán hiện tại của đơn đặt sân.
    /// </summary>
    public PaymentStatus PaymentStatus { get; init; }

    /// <summary>
    /// Phần trăm tiền cọc so với tổng giá trị (0% đến 100%). Trả về 100% nếu TotalPrice == 0.
    /// </summary>
    public decimal DepositPercentage => TotalPrice > 0 ? (DepositAmount / TotalPrice) * 100m : 100m;

    /// <summary>
    /// Kiểm tra xem đơn đặt sân đã được thanh toán 100% chi phí hay chưa.
    /// </summary>
    public bool IsFullyPaid => DepositAmount >= TotalPrice && TotalPrice >= 0;

    /// <summary>
    /// Khởi tạo PaymentPolicy với kiểm tra Invariants về tính hợp lệ của số tiền.
    /// </summary>
    /// <param name="totalPrice">Tổng tiền đơn đặt sân (&gt;= 0).</param>
    /// <param name="depositAmount">Số tiền cọc (0 &lt;= depositAmount &lt;= totalPrice).</param>
    /// <param name="paymentStatus">Trạng thái thanh toán.</param>
    /// <exception cref="DomainInvariantException">Ném ra khi giá trị tiền âm hoặc tiền cọc vượt quá tổng tiền.</exception>
    public PaymentPolicy(decimal totalPrice, decimal depositAmount, PaymentStatus paymentStatus)
    {
        if (totalPrice < 0)
        {
            throw new DomainInvariantException(
                "INVALID_TOTAL_PRICE",
                "TotalPrice must be greater than or equal to 0.",
                nameof(TotalPrice));
        }

        if (depositAmount < 0)
        {
            throw new DomainInvariantException(
                "INVALID_DEPOSIT_AMOUNT",
                "DepositAmount must be greater than or equal to 0.",
                nameof(DepositAmount));
        }

        if (depositAmount > totalPrice)
        {
            throw new DomainInvariantException(
                "DEPOSIT_EXCEEDS_TOTAL_PRICE",
                $"DepositAmount ({depositAmount}) cannot exceed TotalPrice ({totalPrice}).",
                nameof(DepositAmount));
        }

        TotalPrice = totalPrice;
        DepositAmount = depositAmount;
        PaymentStatus = paymentStatus;
    }
}
