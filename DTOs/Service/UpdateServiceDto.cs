using SmashCourt_BE.Models.Enums;
using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.Service
{
    public class UpdateServiceDto
    {
        [StringLength(255, ErrorMessage = "Tên dịch vụ max 255 ký tự")]
        public string? Name { get; set; }

        public string? Description { get; set; }

        [StringLength(50, ErrorMessage = "Đơn vị tính max 50 ký tự")]
        public string? Unit { get; set; }

        public decimal? DefaultPrice { get; set; }

        public ServiceStatus? Status { get; set; }
    }
}

