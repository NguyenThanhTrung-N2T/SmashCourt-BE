using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.DTOs.Payment;
using SmashCourt_BE.Factories;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace SmashCourt_BE.Services
{
    public class PaymentService : IPaymentService
    {
        private readonly IPaymentRepository _paymentRepo;
        private readonly IBookingRepository _bookingRepo;
        private readonly IInvoiceRepository _invoiceRepo;
        private readonly ISlotLockRepository _slotLockRepo;
        private readonly ICourtRepository _courtRepo;
        private readonly IVnPayService _vnPayService;
        private readonly EmailService _emailService;
        private readonly ILogger<PaymentService> _logger;
        private readonly IConfiguration _config;

        public PaymentService(
            IPaymentRepository paymentRepo,
            IBookingRepository bookingRepo,
            IInvoiceRepository invoiceRepo,
            ISlotLockRepository slotLockRepo,
            ICourtRepository courtRepo,
            IVnPayService vnPayService,
            EmailService emailService,
            ILogger<PaymentService> logger,
            IConfiguration config)
        {
            _paymentRepo = paymentRepo;
            _bookingRepo = bookingRepo;
            _invoiceRepo = invoiceRepo;
            _slotLockRepo = slotLockRepo;
            _courtRepo = courtRepo;
            _vnPayService = vnPayService;
            _emailService = emailService;
            _logger = logger;
            _config = config;
        }

        public async Task HandleVnPayIpnAsync(IQueryCollection query, HttpRequest request)
        {
            // 1. Verify signature và parse thông tin từ VNPay
            var isValid = _vnPayService.VerifyIpn(
                query, out var transactionRef, out var isSuccess, out var rawPayload);

            // 2. Log IPN request - luôn log dù signature có hợp lệ hay không (NGOÀI transaction)
            var payment = await _paymentRepo.GetByTransactionRefAsync(transactionRef);
            await _paymentRepo.CreateIpnLogAsync(new PaymentIpnLog
            {
                PaymentId = payment?.Id,
                Provider = IpnProvider.VNPAY,
                ProviderTransactionId = transactionRef,
                RawPayload = rawPayload,
                IsValid = isValid,
                ProcessedAt = DateTime.UtcNow
            });

            if (!isValid)
            {
                _logger.LogWarning("Invalid VNPay IPN signature: {Ref}", transactionRef);
                return;
            }

            if (payment == null)
            {
                _logger.LogWarning("Payment not found for ref: {Ref}", transactionRef);
                return;
            }

            var booking = payment.Invoice?.Booking;
            if (booking == null) return;

            // 3. Idempotent check - bỏ qua nếu đã xử lý rồi
            if (booking.Status == BookingStatus.PAID_ONLINE ||
                booking.Status == BookingStatus.CANCELLED)
            {
                _logger.LogInformation("IPN already processed for booking {BookingId}, status: {Status}", 
                    booking.Id, booking.Status);
                return;
            }

            // 4. Verify amount từ VNPay - bảo vệ khỏi tampered callback
            if (query.TryGetValue("vnp_Amount", out var vnpAmountStr) &&
                long.TryParse(vnpAmountStr, out var vnpRawAmount))
            {
                var vnpAmount = (decimal)(vnpRawAmount / 100);
                if (vnpAmount != payment.Amount)
                {
                    _logger.LogWarning(
                        "VNPay amount mismatch for ref {Ref}: expected {Expected}, received {Actual}",
                        transactionRef, payment.Amount, vnpAmount);
                    return;
                }
            }

            var now = DateTime.UtcNow;

            // 5. Bọc toàn bộ business updates trong TransactionScope
            //    để đảm bảo DB không ở trạng thái inconsistent nếu 1 update fail giữa chừng
            using var transaction = new System.Transactions.TransactionScope(
                System.Transactions.TransactionScopeAsyncFlowOption.Enabled);

            if (isSuccess)
            {
                // 5A. Xử lý thanh toán thành công
                booking.Status = BookingStatus.PAID_ONLINE;
                booking.ExpiresAt = null;   // Clear ExpiresAt vì không còn ý nghĩa sau khi paid
                booking.UpdatedAt = now;

                // Generate cancel token TRƯỚC KHI update để update cùng lúc
                var (cancelToken, cancelTokenHash, cancelTokenExpiry) = GenerateCancelTokenData(booking);

                booking.CancelTokenHash = cancelTokenHash;
                booking.CancelTokenExpiresAt = cancelTokenExpiry;
                await _bookingRepo.UpdateAsync(booking);

                payment.Status = PaymentTxStatus.SUCCESS;
                payment.PaidAt = now;
                payment.UpdatedAt = now;
                await _paymentRepo.UpdateAsync(payment);

                var invoice = payment.Invoice!;
                invoice.PaymentStatus = InvoicePaymentStatus.PARTIALLY_PAID;
                invoice.UpdatedAt = now;
                await _invoiceRepo.UpdateAsync(invoice);

                // Xóa slot locks vì booking đã được confirm
                await _slotLockRepo.DeleteByBookingIdAsync(booking.Id);

                // Fetch court names TRƯỚC KHI complete transaction để gửi email sau
                // NOTE: Court status sẽ được update bởi scheduled job khi đến StartTime
                //       Không update court status ở đây để tránh conflict với job logic
                var courtNames = await GetCourtNamesAsync(booking);

                transaction.Complete();

                _logger.LogInformation("IPN processed successfully for booking {BookingId}", booking.Id);

                // Gửi email xác nhận NGOÀI transaction
                // Lỗi email không được rollback payment transaction
                try
                {
                    _logger.LogInformation("Attempting to send confirmation email for booking {BookingId}", booking.Id);
                    await SendConfirmationEmailAsync(booking, cancelToken, courtNames);
                    _logger.LogInformation("Confirmation email sent successfully for booking {BookingId}", booking.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send confirmation email for booking {BookingId}", booking.Id);
                }

                // TODO: Broadcast SignalR notification
            }
            else
            {
                // 5B. Xử lý thanh toán thất bại
                booking.Status = BookingStatus.CANCELLED;
                booking.ExpiresAt = null;
                booking.CancelledAt = now;
                booking.CancelSource = CancelSourceEnum.SYSTEM;
                booking.UpdatedAt = now;
                await _bookingRepo.UpdateAsync(booking);

                // Deactivate booking courts - nhất quán với CancelByStaffAsync
                await _bookingRepo.UpdateCourtActiveStatusAsync(booking.Id, false);

                payment.Status = PaymentTxStatus.FAILED;
                payment.UpdatedAt = now;
                await _paymentRepo.UpdateAsync(payment);

                // Xóa slot locks
                await _slotLockRepo.DeleteByBookingIdAsync(booking.Id);

                // Update tất cả courts về AVAILABLE
                foreach (var bc in booking.BookingCourts ?? [])
                {
                    var court = await _courtRepo.GetByIdAsync(bc.CourtId, booking.BranchId);
                    if (court != null)
                    {
                        court.Status = CourtStatus.AVAILABLE;
                        court.UpdatedAt = now;
                        await _courtRepo.UpdateAsync(court);
                    }
                }

                transaction.Complete();

                _logger.LogInformation("IPN processed (payment failed) for booking {BookingId}", booking.Id);

                // TODO: Broadcast SignalR notification
            }
        }

        /// <summary>
        /// Xử lý Return URL từ VNPay (browser redirect)
        /// ⚠️ READ-ONLY - KHÔNG UPDATE DATABASE
        /// Chỉ verify signature và trả về thông tin để FE hiển thị
        /// </summary>
        public async Task<VnPayReturnResult> HandleVnPayReturnAsync(IQueryCollection query)
        {
            // 1. Verify signature từ VNPay
            var isValid = _vnPayService.VerifyIpn(
                query, out var transactionRef, out var isSuccess, out _);

            if (!isValid)
            {
                _logger.LogWarning("Invalid VNPay return signature: {Ref}", transactionRef);
                return new VnPayReturnResult
                {
                    IsSuccess = false,
                    Message = "Chữ ký không hợp lệ",
                    ResponseCode = "97"
                };
            }

            // 2. Parse thông tin từ query params
            var responseCode = query["vnp_ResponseCode"].ToString();
            var amount = query.TryGetValue("vnp_Amount", out var amountStr) && 
                         long.TryParse(amountStr, out var rawAmount)
                ? (decimal)(rawAmount / 100)
                : 0;

            // 3. Tìm payment để lấy bookingId (READ-ONLY - không update)
            var payment = await _paymentRepo.GetByTransactionRefAsync(transactionRef);
            var bookingId = payment?.Invoice?.BookingId.ToString();

            // 4. Trả về kết quả để FE hiển thị
            return new VnPayReturnResult
            {
                IsSuccess = isSuccess,
                Message = isSuccess 
                    ? "Thanh toán thành công! Vui lòng đợi hệ thống xác nhận." 
                    : GetVnPayErrorMessage(responseCode),
                BookingId = bookingId,
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

        /// <summary>
        /// Lấy danh sách tên sân từ booking (TRONG transaction)
        /// </summary>
        private async Task<string> GetCourtNamesAsync(Booking booking)
        {
            var courts = booking.BookingCourts ?? [];
            var courtNamesBuilder = new List<string>();
            
            foreach (var bc in courts)
            {
                var court = await _courtRepo.GetByIdAsync(bc.CourtId, booking.BranchId);
                if (court != null) 
                {
                    courtNamesBuilder.Add(court.Name);
                }
            }
            
            return string.Join(", ", courtNamesBuilder);
        }

        /// <summary>
        /// Generate cancel token data (không update DB)
        /// Trả về: (rawToken, tokenHash, tokenExpiry)
        /// </summary>
        private (string rawToken, string tokenHash, DateTime tokenExpiry) GenerateCancelTokenData(Booking booking)
        {
            var rawToken = GenerateCancelToken();
            var tokenHash = HashToken(rawToken);
            
            // Lấy first court slot để tính token expiry
            // Safe vì booking luôn có ít nhất 1 court
            var firstCourtSlot = booking.BookingCourts?.FirstOrDefault();
            if (firstCourtSlot == null)
            {
                throw new InvalidOperationException("Booking must have at least one court");
            }
            
            var startTime = firstCourtSlot.StartTime;

            // Token expiry = min(booking start time, 24h from now)
            var tokenExpiry = new DateTime[]
            {
                booking.BookingDate.ToDateTime(startTime),
                DateTimeHelper.GetNowInVietnam().AddHours(24)
            }.Min();

            return (rawToken, tokenHash, tokenExpiry);
        }

        /// <summary>
        /// Gửi email xác nhận booking (không query DB - dùng data đã fetch)
        /// </summary>
        private async Task SendConfirmationEmailAsync(Booking booking, string rawToken, string courtNames)
        {
            var email = booking.Customer?.Email ?? booking.GuestEmail;
            var name = booking.Customer?.FullName ?? booking.GuestName;
            
            _logger.LogInformation("SendConfirmationEmailAsync - Email: {Email}, Name: {Name}", email, name);
            
            if (string.IsNullOrEmpty(email))
            {
                _logger.LogWarning("Cannot send email: email is null or empty for booking {BookingId}", booking.Id);
                return;
            }
            
            if (string.IsNullOrEmpty(name))
            {
                _logger.LogWarning("Cannot send email: name is null or empty for booking {BookingId}", booking.Id);
                return;
            }

            var firstCourtSlot = booking.BookingCourts?.FirstOrDefault();
            if (firstCourtSlot == null)
            {
                _logger.LogWarning("Cannot send email: no court slots found for booking {BookingId}", booking.Id);
                return;
            }

            _logger.LogInformation("Calling EmailService.SendBookingConfirmationAsync for {Email}", email);
            
            // Lấy frontend base URL từ config
            var frontendBaseUrl = _config["FrontendBaseUrl"] ?? "http://localhost:3000";
            
            // Build email model using Factory
            var emailModel = BookingEmailFactory.Build(booking, rawToken, frontendBaseUrl);
            
            // Send email using new method
            await _emailService.SendBookingConfirmationAsync(emailModel);
                
            _logger.LogInformation("EmailService.SendBookingConfirmationAsync completed for {Email}", email);
        }

        /// <summary>
        /// Generate random cancel token (URL-safe base64)
        /// </summary>
        private static string GenerateCancelToken()
        {
            var bytes = new byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToBase64String(bytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .Replace("=", "");
        }

        /// <summary>
        /// Hash token bằng SHA256 để lưu vào DB
        /// </summary>
        private static string HashToken(string rawToken)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
            return Convert.ToHexString(bytes).ToLower();
        }
    }
}
