using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.Common;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.DTOs.CustomerManagement;
using SmashCourt_BE.Services.IService;
using System.Security.Claims;

/// <summary>
/// Controller tìm kiếm khách hàng
/// Dành cho OWNER, MANAGER và STAFF
/// </summary>

namespace SmashCourt_BE.Controllers
{
    [ApiController]
    [Route("api/customers/search")]
    [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
    public class CustomerSearchController : ControllerBase
    {
        private readonly ICustomerManagementService _service;

        public CustomerSearchController(ICustomerManagementService service)
        {
            _service = service;
        }
        [HttpGet]
        public async Task<IActionResult> SearchCustomers([FromQuery] CustomerSearchQuery query)
        {
            var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

            var result = await _service.SearchCustomersAsync(query, currentUserId, currentUserRole);

            return Ok(ApiResponse<List<CustomerSearchDto>>.Ok(
                result,
                "Search customers success"
            ));
        }
    }
}