using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.Auth
{
    public class GoogleCallbackDto
    {
        [Required(ErrorMessage = "Code không được để trống")]
        public string Code { get; set; } = null!;

        [Required(ErrorMessage = "State không được để trống")]
        public string State { get; set; } = null!;
    }
}
