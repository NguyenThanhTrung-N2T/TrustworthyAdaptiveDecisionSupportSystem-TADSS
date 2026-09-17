using FluentAssertions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.Enums;
using Xunit;

namespace TADSS.UnitTests.Domain;

public class PaymentPolicyContractBoundaryTests
{
    [Theory]
    [InlineData(0, 0, 100, true)]
    [InlineData(100, 0, 0, false)]
    [InlineData(100, 25, 25, false)]
    [InlineData(100, 100, 100, true)]
    public void DepositPercentageAndIsFullyPaid_HandleZeroAndFullPaymentBoundaries(
        decimal totalPrice,
        decimal depositAmount,
        decimal expectedPercentage,
        bool expectedFullyPaid)
    {
        var policy = new PaymentPolicy(totalPrice, depositAmount, PaymentStatus.UNPAID);

        policy.DepositPercentage.Should().Be(expectedPercentage);
        policy.IsFullyPaid.Should().Be(expectedFullyPaid);
    }

    [Theory]
    [InlineData(0.0001)]
    [InlineData(0.5)]
    [InlineData(1)]
    public void Constructor_WhenDepositExceedsTotalByAnyAmount_Rejects(decimal excessAmount)
    {
        var act = () => new PaymentPolicy(
            totalPrice: 200000m,
            depositAmount: 200000m + excessAmount,
            paymentStatus: PaymentStatus.PAID);

        act.Should().Throw<DomainInvariantException>()
            .Where(exception => exception.InvariantCode == "DEPOSIT_EXCEEDS_TOTAL_PRICE");
    }

    [Theory]
    [InlineData(100, 25, 25)]
    [InlineData(100, 33.3333, 33.3333)]
    [InlineData(199999.9950, 99999.9975, 50.0)]
    public void DepositPercentage_PreservesDecimalPrecision(
        decimal totalPrice,
        decimal depositAmount,
        decimal expectedPercentage)
    {
        var policy = new PaymentPolicy(totalPrice, depositAmount, PaymentStatus.PARTIALLY_PAID);

        policy.DepositPercentage.Should().BeApproximately(expectedPercentage, 0.00000001m);
    }
}
