using Moq;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.Payments;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Services;

/// <summary>
/// Chọn cổng theo phương thức và tạo giao dịch online: link lấy từ đúng cổng, Payment PENDING ghi đúng phương thức và mã giao dịch.
/// </summary>
[Trait("Category", TestCategories.Payment)]
public class OnlinePaymentStarterTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 2, 0, 0, DateTimeKind.Utc);

    private static Mock<IPaymentGateway> Gateway(PaymentTxMethod method, string url)
    {
        var gateway = new Mock<IPaymentGateway>();
        gateway.Setup(x => x.Method).Returns(method);
        gateway.Setup(x => x.CreatePaymentLink(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>()))
            .Returns(new GatewayPaymentLink(url, $"{method}-REF"));
        return gateway;
    }

    [Fact]
    public void Resolver_PicksTheGatewayOfTheMethod()
    {
        var vnPay = Gateway(PaymentTxMethod.VNPAY, "https://vnpay");
        var momo = Gateway(PaymentTxMethod.MOMO, "https://momo");
        var resolver = new PaymentGatewayResolver([vnPay.Object, momo.Object]);

        Assert.Same(momo.Object, resolver.Get(PaymentTxMethod.MOMO));
        Assert.Same(vnPay.Object, resolver.Get(PaymentTxMethod.VNPAY));
    }

    [Fact]
    public void Resolver_MethodWithoutGateway_IsAConfigurationError()
    {
        var resolver = new PaymentGatewayResolver([Gateway(PaymentTxMethod.VNPAY, "https://vnpay").Object]);

        var error = Assert.Throws<InvalidOperationException>(() => resolver.Get(PaymentTxMethod.MOMO));
        Assert.Contains("MOMO", error.Message);
    }

    [Fact]
    public async Task Start_CreatesTheLinkAndAPendingPaymentForThatGateway()
    {
        var momo = Gateway(PaymentTxMethod.MOMO, "https://momo/pay");
        var payments = new Mock<IPaymentRepository>();
        var starter = new OnlinePaymentStarter(
            new PaymentGatewayResolver([Gateway(PaymentTxMethod.VNPAY, "https://vnpay").Object, momo.Object]), payments.Object);
        var invoiceId = Guid.NewGuid();

        var url = await starter.StartAsync(invoiceId, "booking-1", 60_000m, "Dat san", PaymentTxMethod.MOMO, Now);

        Assert.Equal("https://momo/pay", url);
        momo.Verify(x => x.CreatePaymentLink("booking-1", 60_000m, "Dat san"), Times.Once);
        payments.Verify(x => x.CreateAsync(It.Is<Payment>(p =>
            p.InvoiceId == invoiceId && p.Method == PaymentTxMethod.MOMO && p.Amount == 60_000m
            && p.Status == PaymentTxStatus.PENDING && p.TransactionRef == "MOMO-REF" && p.CreatedAt == Now)), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Start_NonPositiveAmount_IsRejectedBeforeCallingTheGateway(int amount)
    {
        var vnPay = Gateway(PaymentTxMethod.VNPAY, "https://vnpay");
        var starter = new OnlinePaymentStarter(new PaymentGatewayResolver([vnPay.Object]), Mock.Of<IPaymentRepository>());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => starter.StartAsync(Guid.NewGuid(), "booking-1", amount, "Dat san", PaymentTxMethod.VNPAY, Now));
        vnPay.Verify(x => x.CreatePaymentLink(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>()), Times.Never);
    }
}
