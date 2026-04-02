using SmashCourt_BE.Jobs.Interfaces;
using SmashCourt_BE.Repositories.IRepository;

namespace SmashCourt_BE.Jobs
{
    public class PromotionJob : IPromotionJob
    {
        private readonly IPromotionRepository _repo;
        private readonly ILogger<PromotionJob> _logger;

        public PromotionJob(IPromotionRepository repo, ILogger<PromotionJob> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        // Job 1 — Cập nhật status của khuyến mãi theo ngày (ACTIVE, EXPIRED)
        public async Task UpdateStatusAsync()
        {
            _logger.LogInformation("Promotion status job started at {Time}", DateTime.UtcNow);

            try
            {
                await _repo.UpdateExpiredStatusAsync();
                _logger.LogInformation("Promotion status job completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi cập nhật trạng thái khuyến mãi");
            }
        }
    }
}
