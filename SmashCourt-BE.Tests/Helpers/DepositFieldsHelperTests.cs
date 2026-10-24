using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.ViewModels;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Helpers;

/// <summary>BE-UT-07 (phần hàm thuần): trường cọc hiển thị cho FE đọc nguyên từ hóa đơn, không tự quy đổi.</summary>
[TestCategory(TestCategories.Helper)]
[Trait("Catalog", "BE-UT-07")]
public class DepositFieldsHelperTests
{
    private static Invoice Invoice(decimal finalTotal, decimal? rate, decimal? amount, DepositSource? source)
    {
        var invoice = TestDataFactory.CreateInvoice(finalTotal: finalTotal);
        invoice.DepositRate = rate;
        invoice.DepositAmount = amount;
        invoice.DepositSource = source;
        return invoice;
    }

    [Fact]
    public void TadssDeposit_RemainingIsTotalMinusDeposit()
    {
        var fields = DepositFieldsHelper.FromInvoice(BookingStatus.PENDING, Invoice(200_000m, 0.5m, 100_000m, DepositSource.TADSS));

        Assert.Equal(new DepositFields(0.5m, 100_000m, 100_000m, DepositSource.TADSS), fields);
    }

    [Fact]
    public void FullPaymentFallback_IsRateOneAndNothingRemains()
    {
        var fields = DepositFieldsHelper.FromInvoice(
            BookingStatus.PENDING, Invoice(228_000m, 1m, 228_000m, DepositSource.FULL_PAYMENT_FALLBACK));

        Assert.Equal(new DepositFields(1m, 228_000m, 0m, DepositSource.FULL_PAYMENT_FALLBACK), fields);
    }

    [Theory]
    [InlineData(DepositSource.TADSS)]
    [InlineData(DepositSource.FULL_PAYMENT_FALLBACK)]
    [InlineData(DepositSource.MANAGER)]
    public void ZeroDeposit_WholeTotalRemains_WhateverTheSource(DepositSource source)
    {
        // a′ và duyệt 0 %: thu toàn bộ tại quầy
        var fields = DepositFieldsHelper.FromInvoice(BookingStatus.CONFIRMED, Invoice(3_000m, 0m, 0m, source));

        Assert.Equal(new DepositFields(0m, 0m, 3_000m, source), fields);
    }

    [Fact]
    public void StoredNumbersAreReturnedAsIs_WithoutRecomputingFromTheRate()
    {
        // TADSS đã làm tròn; BE không nhân lại rate × total (0.3333 × 200.000 = 66.660)
        var fields = DepositFieldsHelper.FromInvoice(BookingStatus.PENDING, Invoice(200_000m, 0.3333m, 66_667m, DepositSource.TADSS));

        Assert.Equal(0.3333m, fields?.Rate);
        Assert.Equal(66_667m, fields?.Amount);
        Assert.Equal(133_333m, fields?.RemainingAmount);
    }

    [Fact]
    public void Remaining_UsesTheCurrentTotal_IncludingServicesAddedLater()
    {
        var invoice = Invoice(200_000m, 0.5m, 100_000m, DepositSource.MANAGER);
        invoice.ServiceFee = 50_000m;
        invoice.FinalTotal = 250_000m;

        Assert.Equal(150_000m, DepositFieldsHelper.FromInvoice(BookingStatus.PAID_ONLINE, invoice)?.RemainingAmount);
    }

    [Fact]
    public void Remaining_IsNeverNegative()
    {
        Assert.Equal(0m, DepositFieldsHelper.FromInvoice(
            BookingStatus.PAID_ONLINE, Invoice(80_000m, 0.5m, 100_000m, DepositSource.TADSS))?.RemainingAmount);
        Assert.Equal(0m, DepositFieldsHelper.RemainingAmount(100m, 100m));
        Assert.Equal(1m, DepositFieldsHelper.RemainingAmount(101m, 100m));
    }

    [Fact]
    public void PendingApproval_HasNoDepositFields_EvenIfTheInvoiceStillCarriesValues()
    {
        Assert.Null(DepositFieldsHelper.FromInvoice(
            BookingStatus.PENDING_APPROVAL, Invoice(200_000m, 0.5m, 100_000m, DepositSource.TADSS)));
    }

    [Theory]
    [InlineData(null, null, null)]                                   // đơn tại quầy, đơn online trước Phase 6
    [InlineData(1.0, 200000.0, null)]                                // thiếu nguồn
    [InlineData(null, 200000.0, (int)DepositSource.TADSS)]           // thiếu tỷ lệ
    [InlineData(1.0, null, (int)DepositSource.FULL_PAYMENT_FALLBACK)] // thiếu số tiền
    public void IncompleteDepositData_MeansNotApplicable(double? rate, double? amount, int? source)
    {
        var invoice = Invoice(200_000m, (decimal?)rate, (decimal?)amount, (DepositSource?)source);

        Assert.Null(DepositFieldsHelper.FromInvoice(BookingStatus.CONFIRMED, invoice));
    }

    [Fact]
    public void NoInvoice_MeansNotApplicable()
    {
        Assert.Null(DepositFieldsHelper.FromInvoice(BookingStatus.PENDING, null));
    }

    public static TheoryData<BookingStatus> StatusesOtherThanPendingApproval()
    {
        var data = new TheoryData<BookingStatus>();
        foreach (var status in Enum.GetValues<BookingStatus>().Where(s => s != BookingStatus.PENDING_APPROVAL))
            data.Add(status);
        return data;
    }

    [Theory]
    [MemberData(nameof(StatusesOtherThanPendingApproval))]
    public void EveryOtherStatus_KeepsTheDepositFields_IncludingCancelledOnes(BookingStatus status)
    {
        Assert.NotNull(DepositFieldsHelper.FromInvoice(status, Invoice(200_000m, 0.5m, 100_000m, DepositSource.TADSS)));
    }
}
