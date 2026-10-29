using SmashCourt_BE.Services.Deposit;
using SmashCourt_BE.Common;
using SmashCourt_BE.Common.Constants;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.DTOs.Booking;
using SmashCourt_BE.DTOs.Payment;
using SmashCourt_BE.DTOs.SignalR;
using SmashCourt_BE.Factories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using SmashCourt_BE.Configurations;

namespace SmashCourt_BE.Services
{
    /// <summary>
    /// Endpoint thanh toán của khách: thanh toán lại, IPN và trình duyệt quay lại của VNPay. Nghiệp vụ dùng chung cho mọi cổng nằm ở
    /// <see cref="IOnlinePaymentStarter"/> và <see cref="IPaymentResultService"/>; ở đây chỉ còn phần riêng của từng endpoint.
    /// </summary>
    public class PaymentService : IPaymentService
    {
        private readonly IPaymentRepository _paymentRepo;
        private readonly IBookingRepository _bookingRepo;
        private readonly IPaymentGatewayResolver _gateways;
        private readonly IOnlinePaymentStarter _paymentStarter;
        private readonly IPaymentResultService _paymentResults;
        private readonly ILogger<PaymentService> _logger;
        private readonly IOptionsMonitor<BookingPaymentSettings> _paymentSettings;
        private readonly TimeProvider _timeProvider;

        public PaymentService(
            IPaymentRepository paymentRepo,
            IBookingRepository bookingRepo,
            IPaymentGatewayResolver gateways,
            IOnlinePaymentStarter paymentStarter,
            IPaymentResultService paymentResults,
            ILogger<PaymentService> logger,
            IOptionsMonitor<BookingPaymentSettings> paymentSettings,
            TimeProvider timeProvider)
        {
            _paymentRepo = paymentRepo;
            _bookingRepo = bookingRepo;
            _gateways = gateways;
            _paymentStarter = paymentStarter;
            _paymentResults = paymentResults;
            _logger = logger;
            _paymentSettings = paymentSettings;
            _timeProvider = timeProvider;
        }

        public async Task<OnlineBookingResponse> RetryPaymentAsync(Guid bookingId, Guid customerId)
        {
            var booking = await _bookingRepo.GetByIdWithDetailsAsync(bookingId);
            if (booking == null)
                throw new AppException(404, "Không tìm thấy đơn đặt sân", ErrorCodes.NotFound);

            if (!booking.CustomerId.HasValue || booking.CustomerId.Value != customerId)
                throw new AppException(403, "Bạn không có quyền thực hiện thao tác này", ErrorCodes.Forbidden);

            if (booking.Status != BookingStatus.PENDING)
                throw new AppException(400,
                    "Chỉ có thể thanh toán lại cho đơn đang chờ thanh toán",
                    ErrorCodes.BadRequest);
            var invoice = booking.Invoice
                ?? throw new AppException(500, "Không tìm thấy hóa đơn", ErrorCodes.InternalError);

            // Hạn giữ chỗ tuyệt đối kiểm trước hạn thanh toán, vì hạn thanh toán không bao giờ vượt hạn giữ chỗ.
            //    Đơn cũ không có hold_deadline ⇒ coi là giờ tạo + PaymentHoldMaxMinutes
            var settings = _paymentSettings.CurrentValue;
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var holdDeadline = invoice.HoldDeadline ?? booking.CreatedAt.AddMinutes(settings.PaymentHoldMaxMinutes);
            if (now >= holdDeadline)
                throw new AppException(400, "Đã hết thời gian giữ chỗ, vui lòng đặt lại", ErrorCodes.PaymentHoldExpired);

            // Còn trong hạn giữ chỗ nhưng quá hạn thanh toán: job hủy đơn quá hạn sẽ dọn
            if (!invoice.ExpiresAt.HasValue || invoice.ExpiresAt.Value <= now)
                throw new AppException(400,
                    "Đơn đặt sân đã hết hạn thanh toán, vui lòng đặt lại",
                    ErrorCodes.BadRequest);

            // Hạn mới không rút ngắn hạn hiện tại và không vượt hạn giữ chỗ
            if (!PaymentHoldPolicy.TryForRetry(now, invoice.ExpiresAt.Value, holdDeadline, settings.RetryExtendMinutes, out var window))
                throw new AppException(400, "Đã hết thời gian giữ chỗ, vui lòng đặt lại", ErrorCodes.PaymentHoldExpired);
            var newExpiresAt = window.ExpiresAt;

            // Link phải hết hạn trước hạn thanh toán một khoảng đệm và còn sống tối thiểu 1' (P6-27); không đủ thì không gia hạn, không tạo link
            if (!PaymentHoldPolicy.TryGetPaymentLinkExpiry(now, newExpiresAt, settings.PaymentLinkBufferMinutes, out var linkExpiresAt))
                throw new AppException(400, "Không còn đủ thời gian thanh toán, vui lòng đặt lại", ErrorCodes.PaymentHoldExpired);

            // Thu online đúng tiền cọc đã chốt; đơn cũ chưa có cọc thì thu toàn bộ
            var amount = invoice.DepositAmount ?? invoice.FinalTotal;
            var courtNames = string.Join(", ",
                booking.BookingCourts.Select(bc => bc.Court?.Name).Where(n => n != null).Distinct());
            if (string.IsNullOrEmpty(courtNames))
                courtNames = "San the thao";
            // Thanh toán lại qua cùng cổng khách đã chọn lúc đặt
            var method = invoice.Payments
                .Where(p => p.Method != PaymentTxMethod.CASH)
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => (PaymentTxMethod?)p.Method)
                .FirstOrDefault() ?? PaymentTxMethod.VNPAY;

            string paymentUrl;
            using (var transaction = TransactionScopeHelper.ReadCommitted())
            {
                // Đơn vừa bị hủy hoặc vừa được thanh toán thì không kéo hạn, không tạo link
                if (!await _bookingRepo.ExtendPaymentWindowAsync(bookingId, newExpiresAt, now))
                    throw new AppException(409, "Đơn đặt sân vừa thay đổi trạng thái, vui lòng tải lại", ErrorCodes.Conflict);

                await _paymentRepo.FailPendingByInvoiceAsync(invoice.Id, exceptPaymentId: null, now);
                paymentUrl = await _paymentStarter.StartAsync(
                    invoice.Id, booking.Id.ToString(), amount, $"Thanh toan lai {StringHelper.RemoveDiacritics(courtNames)}", method, now,
                    linkExpiresAt);
                transaction.Complete();
            }

            _logger.LogInformation(
                "Thanh toán lại booking {BookingId} của khách {CustomerId}: {Amount} đ qua {Method}, hạn mới {ExpiresAt}",
                bookingId, customerId, amount, method, newExpiresAt);

            // Cùng DTO với đặt sân online (luôn PENDING); trường cọc đọc từ hóa đơn, đơn cũ là null
            var deposit = DepositFieldsMapper.FromInvoice(booking.Status, invoice);
            return new OnlineBookingResponse
            {
                BookingId = booking.Id,
                BookingCode = booking.BookingCode,
                Status = booking.Status.ToString(),
                PaymentUrl = paymentUrl,
                ExpiresAt = newExpiresAt,
                PaymentLinkExpiresAt = linkExpiresAt,
                FinalTotal = invoice.FinalTotal,
                DepositRate = deposit?.Rate,
                DepositAmount = deposit?.Amount,
                RemainingAmount = deposit?.RemainingAmount,
                DepositSource = deposit?.Source.ToString(),
                ApprovalExpiresAt = booking.ApprovalExpiresAt
            };
        }

        public Task HandleVnPayIpnAsync(IQueryCollection query, HttpRequest request) =>
            _paymentResults.ApplyAsync(_gateways.Get(PaymentTxMethod.VNPAY).ReadResult(query));

        public async Task<VnPayReturnResult> HandleVnPayReturnAsync(IQueryCollection query)
        {
            var result = _gateways.Get(PaymentTxMethod.VNPAY).ReadResult(query);
            var transactionRef = result.TransactionRef;
            if (!result.IsSignatureValid)
            {
                _logger.LogWarning("Invalid VNPay return signature: {Ref}", transactionRef);
                return new VnPayReturnResult
                {
                    IsSuccess = false,
                    Message = "Chữ ký không hợp lệ",
                    ResponseCode = "97"
                };
            }

            var isSuccess = result.IsSuccess;
            var responseCode = result.ResponseCode;
            var amount = result.Amount ?? 0;

            // Tìm payment để lấy bookingId (chỉ đọc)
            //    Dùng AsNoTracking để tránh EF identity cache trả về entity cũ
            var payment = await _paymentRepo.GetByTransactionRefAsync(transactionRef, asNoTracking: true);
            var bookingId = payment?.Invoice?.Booking?.Id;
            var bookingCode = payment?.Invoice?.Booking?.BookingCode;

            // Đọc lại trạng thái trực tiếp từ DB để tránh entity cũ trong bộ nhớ của EF
            //    HandleVnPayIpnAsync dùng raw ExecuteUpdateAsync — EF identity cache
            //    vẫn giữ entity PENDING cũ nếu dùng navigation property
            BookingStatus? freshStatus = null;
            if (bookingId.HasValue)
            {
                try { freshStatus = await _bookingRepo.GetBookingStatusAsync(bookingId.Value); }
                catch { /* không ném lỗi nếu booking không tồn tại */ }
            }

            return new VnPayReturnResult
            {
                IsSuccess = isSuccess,
                Message = isSuccess
                    ? (freshStatus == BookingStatus.PAID_ONLINE
                        ? "Thanh toán thành công! Đặt sân của bạn đã được xác nhận."
                        : "Thanh toán thành công! Vui lòng đợi hệ thống xác nhận.")
                    : GetVnPayErrorMessage(responseCode),
                BookingId = bookingId?.ToString(),
                BookingCode = bookingCode,
                TransactionRef = transactionRef,
                Amount = amount,
                ResponseCode = responseCode
            };
        }

        private static string GetVnPayErrorMessage(string responseCode)
        {
            return responseCode switch
            {
                "07" => "Giao dịch bị nghi ngờ gian lận",
                "09" => "Thẻ chưa đăng ký dịch vụ Internet Banking",
                "10" => "Xác thực thông tin thẻ không đúng quá số lần quy định",
                "11" => "Đã hết hạn chờ thanh toán",
                "12" => "Thẻ bị khóa",
                "13" => "Sai mật khẩu xác thực giao dịch (OTP)",
                "24" => "Khách hàng hủy giao dịch",
                "51" => "Tài khoản không đủ số dư",
                "65" => "Tài khoản đã vượt quá hạn mức giao dịch trong ngày",
                "75" => "Ngân hàng thanh toán đang bảo trì",
                "79" => "Giao dịch vượt quá số lần nhập sai mật khẩu",
                _ => "Giao dịch thất bại"
            };
        }

    }
}
