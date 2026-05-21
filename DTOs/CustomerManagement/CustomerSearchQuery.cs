using Microsoft.AspNetCore.Mvc;
namespace SmashCourt_BE.DTOs.CustomerManagement
{
    /// <summary>
    /// Query parameters cho danh sách khách hàng
    /// </summary>

    public class CustomerSearchQuery
    {
        /// <summary>
        /// Tìm kiếm theo tên, SĐT, email
        /// </summary>
        public string? SearchTerm { get; set; }
        public int Limit { get; set; } = 10;
    }
}