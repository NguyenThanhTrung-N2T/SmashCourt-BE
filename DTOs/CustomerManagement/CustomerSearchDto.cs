namespace SmashCourt_BE.DTOs.CustomerManagement
{
    /// <summary>
    /// DTO trả về thông tin khách hàng cơ bản (dùng cho tìm kiếm)
    /// </summary>
    public class CustomerSearchDto
    {
        public Guid? Id { get; set; }
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
    }
}