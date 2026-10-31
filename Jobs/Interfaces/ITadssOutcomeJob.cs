namespace SmashCourt_BE.Jobs.Interfaces
{
    public interface ITadssOutcomeJob
    {
        // Báo kết quả các đơn đã kết thúc về TADSS: tuần tự, tối đa 20 đơn mỗi chu kỳ; chạy cả khi tắt cờ đánh giá
        Task ReportOutcomesAsync();
    }
}
