using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.CustomerManagement
{
    /// <summary>
    /// Query parameters cho chức năng tìm nhanh/autocomplete khách hàng.
    /// </summary>
    public class CustomerSearchQuery
    {
        /// <summary>
        /// Tìm theo tên, số điện thoại hoặc email. Chỉ thực hiện lookup khi giá trị sau trim có ít nhất 2 ký tự.
        /// </summary>
        [StringLength(100, ErrorMessage = "Từ khóa tìm kiếm không được vượt quá 100 ký tự")]
        public string? SearchTerm { get; set; }

        /// <summary>
        /// Số lượng kết quả tối đa trả về.
        /// </summary>
        [Range(1, 50, ErrorMessage = "Limit phải nằm trong khoảng từ 1 đến 50")]
        public int Limit { get; set; } = 10;
    }
}
