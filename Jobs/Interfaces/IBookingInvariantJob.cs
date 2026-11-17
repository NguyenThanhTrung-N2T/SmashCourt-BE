namespace SmashCourt_BE.Jobs.Interfaces
{
    public interface IBookingInvariantJob
    {
        // Kiểm tính đúng dữ liệu các đơn tạo / sửa trong 24 giờ (bỏ đơn vừa đổi dưới 2 phút); chỉ đọc, ghi metric và log lỗi kèm mã đơn
        Task CheckRecentBookingsAsync();
    }
}
