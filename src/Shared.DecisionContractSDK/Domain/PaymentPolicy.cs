using Shared.DecisionContractSDK.Enums;

namespace Shared.DecisionContractSDK.Domain;

public record PaymentPolicy
{
    public decimal TotalPrice { get; init; }
    public decimal DepositAmount { get; init; }
    public PaymentStatus PaymentStatus { get; init; }

    public decimal DepositPercentage => TotalPrice > 0 ? (DepositAmount / TotalPrice) * 100m : 100m;
    public bool IsFullyPaid => DepositAmount >= TotalPrice && TotalPrice >= 0;

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
