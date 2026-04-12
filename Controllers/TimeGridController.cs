using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Booking;
using System.ComponentModel.DataAnnotations;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Controllers
{
    [ApiController]
    [Route("api/branches/{branchId:guid}/courts/{courtId:guid}/time-grid")]
    public class TimeGridController : ControllerBase
    {
        private readonly ITimeGridService _service;

        public TimeGridController(ITimeGridService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> GetTimeGrid(
            Guid branchId, Guid courtId,
            [FromQuery][Required] DateOnly date)
        {
            var result = await _service.GetTimeGridAsync(branchId, courtId, date);
            return Ok(ApiResponse<List<TimeGridSlotDto>>.Ok(result));
        }
    }
}
