using Hangfire;
using SmashCourt_BE.Jobs.Interfaces;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Jobs
{
    /// <summary>
    /// Hủy đơn chờ duyệt quá hạn duyệt (spec 7.11, gói 6A-5): tối đa 100 đơn mỗi lần, hạn sớm nhất trước. Mỗi đơn đi qua dịch vụ hủy chung
    /// trong transaction riêng: khóa booking, kiểm lại hạn, hủy có điều kiện (SYSTEM, APPROVAL_EXPIRED), nhả sân, hóa đơn ⇒ EXPIRED; sau commit
    /// báo rời hàng chờ và gửi email cho khách. Job không nhận TADSS client hay dịch vụ duyệt nên không có đường nào tự duyệt;
    /// outcome APPROVAL_EXPIRED do job báo kết quả gửi TADSS.
    /// </summary>
    public class BookingApprovalJob : IBookingApprovalJob
    {
        /// <summary>Số đơn tối đa mỗi lần chạy; còn thì lần chạy sau (mỗi phút) làm tiếp.</summary>
        private const int BatchSize = 100;

        private readonly IBookingRepository _bookingRepo;
        private readonly IBookingCancellationService _cancellationService;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<BookingApprovalJob> _logger;

        public BookingApprovalJob(
            IBookingRepository bookingRepo,
            IBookingCancellationService cancellationService,
            TimeProvider timeProvider,
            ILogger<BookingApprovalJob> logger)
        {
            _bookingRepo = bookingRepo;
            _cancellationService = cancellationService;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        [DisableConcurrentExecution(timeoutInSeconds: 10)]
        public Task ExpireApprovalsAsync() =>
            ExpiryBatch.RunAsync("ApprovalExpiry",
                () => _bookingRepo.GetExpiredApprovalIdsAsync(_timeProvider.GetUtcNow().UtcDateTime, BatchSize),
                _cancellationService.ExpireApprovalAsync,
                _logger);
    }
}
