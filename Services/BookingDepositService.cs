using SmashCourt_BE.Models.Deposit;
using SmashCourt_BE.Services.Deposit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Integrations.Tadss;
using SmashCourt_BE.Integrations.Tadss.DTOs;
using SmashCourt_BE.Models.ViewModels;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services
{
    public class BookingDepositService : IBookingDepositService
    {
        private readonly IBookingRepository _bookingRepo;
        private readonly ICustomerHistoryRepository _historyRepo;
        private readonly ITadssClient _tadssClient;
        private readonly IOptionsMonitor<TadssSettings> _settings;
        private readonly IOptionsMonitor<BookingPaymentSettings> _paymentSettings;
        private readonly ILogger<BookingDepositService> _logger;

        public BookingDepositService(
            IBookingRepository bookingRepo,
            ICustomerHistoryRepository historyRepo,
            ITadssClient tadssClient,
            IOptionsMonitor<TadssSettings> settings,
            IOptionsMonitor<BookingPaymentSettings> paymentSettings,
            ILogger<BookingDepositService> logger)
        {
            _bookingRepo = bookingRepo;
            _historyRepo = historyRepo;
            _tadssClient = tadssClient;
            _settings = settings;
            _paymentSettings = paymentSettings;
            _logger = logger;
        }

        public async Task<DepositDecision> DecideAsync(Guid bookingId, decimal finalTotal, CancellationToken cancellationToken = default)
        {
            // Đọc cờ ở mỗi lần gọi để đổi cấu hình có hiệu lực ngay; tắt thì TADSS không nhận request nào
            TadssCallResult<TadssDecisionResultDto>? tadssResult = null;
            Exception? evaluateError = null;
            if (_settings.CurrentValue.EnableTadssDecision)
            {
                try
                {
                    tadssResult = await EvaluateAsync(bookingId, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Đơn đã ghi: lỗi khi dựng request là sự cố nên cọc 100 % như TADSS lỗi, không ném để đơn khỏi kẹt PENDING không cọc.
                    // Hủy chủ động vẫn ném.
                    evaluateError = ex;
                }
            }

            var decision = DepositDecisionResolver.Resolve(tadssResult, finalTotal, _paymentSettings.CurrentValue.MinOnlinePaymentAmount);
            if (evaluateError != null)
            {
                decision = decision with
                {
                    LogLevel = LogLevel.Error,
                    LogMessage = $"Không gọi được TADSS do lỗi khi dựng request ({evaluateError.GetType().Name}); dùng phương án dự phòng. " +
                                 decision.LogMessage
                };
            }

            if (decision.LogLevel != LogLevel.None)
                _logger.Log(decision.LogLevel, evaluateError, "Quyết định cọc {Branch} cho booking {BookingId}: {Message}",
                    decision.Branch, bookingId, decision.LogMessage);

            return decision;
        }

        // Đọc DB xong mới gọi HTTP để không giữ kết nối DB trong lúc chờ TADSS
        public async Task<TadssCallResult<TadssDecisionResultDto>> EvaluateAsync(Guid bookingId, CancellationToken cancellationToken = default)
        {
            var snapshot = await _bookingRepo.GetDecisionSnapshotAsync(bookingId, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Không dựng được request quyết định cọc: booking {bookingId} không tồn tại, không có khách hoặc chưa có hóa đơn.");

            // Lịch sử tính tại thời điểm tạo booking (không phải "bây giờ") nên request tất định, gọi lại không bị 409
            var entries = await _historyRepo.GetEntriesAsync(snapshot.CustomerId, bookingId, cancellationToken);
            var history = CustomerHistoryCalculator.Calculate(entries, snapshot.CreatedAt);

            var request = TadssDecisionRequestMapper.Map(snapshot, history, _settings.CurrentValue);
            return await _tadssClient.EvaluateAsync(request, bookingId, cancellationToken);
        }
    }
}
