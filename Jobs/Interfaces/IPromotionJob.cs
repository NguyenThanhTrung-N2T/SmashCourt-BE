namespace SmashCourt_BE.Jobs.Interfaces
{
    public interface IPromotionJob
    {
        // Job 1 — Cập nhật status của khuyến mãi theo ngày (ACTIVE, EXPIRED)
        Task UpdateStatusAsync();
    }
}
