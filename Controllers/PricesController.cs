using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.Common;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.DTOs.PriceConfig;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Services.IService;
using System.Security.Claims;

namespace SmashCourt_BE.Controllers
{
    [ApiController]
    [Route("api/prices")]
    public class PricesController : ControllerBase
    {
        private readonly IBranchPriceService _service;

        public PricesController(IBranchPriceService service)
        {
            _service = service;
        }

        /// <summary>
        /// GET /api/prices - Returns effective pricing snapshot for a branch on a specific date.
        /// 
        /// Authorization: Owner, Manager, Staff
        /// 
        /// Branch Resolution:
        /// - Owner must provide branchId
        /// - Manager and Staff use their assigned branch automatically
        /// 
        /// Query Parameters:
        /// - branchId (optional): Required for Owner, ignored for Manager/Staff
        /// - date (optional): Target date in yyyy-MM-dd format, defaults to today
        /// - courtTypeId (optional): Filter by specific court type
        /// 
        /// Pricing Resolution:
        /// 1. Get latest BranchPriceOverride where effective_from &lt;= requestedDate
        /// 2. Get latest SystemPrice where effective_from &lt;= requestedDate
        /// 3. For each (court_type_id + time_slot_id): Branch override wins if exists, otherwise fallback to system price
        /// 4. Combine WEEKDAY and WEEKEND rows into a single DTO
        /// 5. Merge consecutive time slots with same weekdayPrice and weekendPrice
        /// 6. Group by court type
        /// </summary>
        [HttpGet]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetEffectivePrices(
            [FromQuery] Guid? branchId,
            [FromQuery] string? date,
            [FromQuery] Guid? courtTypeId)
        {
            try
            {
                // 1. Parse and validate date parameter
                DateOnly targetDate;
                if (string.IsNullOrWhiteSpace(date))
                {
                    targetDate = DateTimeHelper.GetTodayInVietnam();
                }
                else
                {
                    if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out targetDate))
                    {
                        return BadRequest(ApiResponse<EffectivePricesResponse>.Fail(
                            "Định dạng ngày không hợp lệ. Sử dụng yyyy-MM-dd",
                            ErrorCodes.BadRequest));
                    }
                }

                // 2. Get current user info from claims
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!Guid.TryParse(userIdClaim, out var currentUserId))
                {
                    return Unauthorized(ApiResponse<EffectivePricesResponse>.Fail(
                        "Không xác định được người dùng",
                        ErrorCodes.Unauthorized));
                }

                var currentUserRole = User.FindFirst(ClaimTypes.Role)?.Value;
                if (string.IsNullOrEmpty(currentUserRole))
                {
                    return Unauthorized(ApiResponse<EffectivePricesResponse>.Fail(
                        "Không xác định được vai trò người dùng",
                        ErrorCodes.Unauthorized));
                }

                // 3. Call service to get effective prices
                // Service handles: branch resolution, pricing merge logic, slot merging, grouping
                var response = await _service.GetEffectivePricesAsync(
                    branchId,
                    targetDate,
                    courtTypeId,
                    currentUserId,
                    currentUserRole);

                return Ok(ApiResponse<EffectivePricesResponse>.Ok(response));
            }
            catch (AppException ex)
            {
                return StatusCode(ex.StatusCode, ApiResponse<EffectivePricesResponse>.Fail(ex.Message, ex.ErrorCode));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<EffectivePricesResponse>.Fail(
                    $"Lỗi hệ thống: {ex.Message}",
                    ErrorCodes.InternalError));
            }
        }

        /// <summary>
        /// GET /api/prices/overrides - Returns all pricing override versions for a court type.
        /// 
        /// Authorization: Owner, Manager and Staff
        /// 
        /// Branch Resolution:
        /// - Owner must provide branchId
        /// - Manager uses their assigned branch automatically
        /// 
        /// Query Parameters:
        /// - branchId (optional): Required for Owner, ignored for Manager
        /// - courtTypeId (required): Court type to get versions for
        /// 
        /// Process:
        /// 1. Get distinct effective_from values for branchId + courtTypeId
        /// 2. Sort descending
        /// 3. Calculate status:
        ///    - ACTIVE: latest effective_from &lt;= today
        ///    - SCHEDULED: effective_from &gt; today
        ///    - EXPIRED: effective_from &lt; active version
        /// 4. If no version is effective yet, all versions remain SCHEDULED
        /// </summary>
        [HttpGet("overrides")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOrManager)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetPriceOverrideVersions(
            [FromQuery] Guid? branchId,
            [FromQuery] Guid? courtTypeId)
        {
            try
            {
                // 1. Validate required courtTypeId
                if (!courtTypeId.HasValue)
                {
                    return BadRequest(ApiResponse<PriceOverrideVersionsResponse>.Fail(
                        "Vui lòng cung cấp courtTypeId",
                        ErrorCodes.BadRequest));
                }

                // 2. Get current user info from claims
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!Guid.TryParse(userIdClaim, out var currentUserId))
                {
                    return Unauthorized(ApiResponse<PriceOverrideVersionsResponse>.Fail(
                        "Không xác định được người dùng",
                        ErrorCodes.Unauthorized));
                }

                var currentUserRole = User.FindFirst(ClaimTypes.Role)?.Value;
                if (string.IsNullOrEmpty(currentUserRole))
                {
                    return Unauthorized(ApiResponse<PriceOverrideVersionsResponse>.Fail(
                        "Không xác định được vai trò người dùng",
                        ErrorCodes.Unauthorized));
                }

                // 3. Call service to get price override versions
                // Service handles: branch resolution, version retrieval, status calculation
                var response = await _service.GetPriceOverrideVersionsAsync(
                    branchId,
                    courtTypeId.Value,
                    currentUserId,
                    currentUserRole);

                return Ok(ApiResponse<PriceOverrideVersionsResponse>.Ok(response));
            }
            catch (AppException ex)
            {
                return StatusCode(ex.StatusCode, ApiResponse<PriceOverrideVersionsResponse>.Fail(ex.Message, ex.ErrorCode));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<PriceOverrideVersionsResponse>.Fail(
                    $"Lỗi hệ thống: {ex.Message}",
                    ErrorCodes.InternalError));
            }
        }

        /// <summary>
        /// GET /api/prices/overrides/{effectiveFrom} - Returns exact override version configuration.
        /// 
        /// Authorization: Owner, Manager and Staff
        /// 
        /// Branch Resolution:
        /// - Owner must provide branchId
        /// - Manager uses their assigned branch automatically
        /// 
        /// Path Parameters:
        /// - effectiveFrom (required): Effective date in yyyy-MM-dd format
        /// 
        /// Query Parameters:
        /// - branchId (optional): Required for Owner, ignored for Manager
        /// - courtTypeId (required): Court type to get version for
        /// 
        /// Query Rules:
        /// Uses exact match: branch_id = ? AND court_type_id = ? AND effective_from = ?
        /// Does NOT use: effective_from &lt;= date
        /// 
        /// Process:
        /// 1. Resolve branch ID based on user role
        /// 2. Query exact override version for the specified date
        /// 3. Group WEEKDAY and WEEKEND rows into single slots
        /// 4. Calculate status (ACTIVE, SCHEDULED, EXPIRED)
        /// 5. Return 404 if version does not exist
        /// 
        /// Response:
        /// Returns PriceOverrideVersionDetailDto with:
        /// - BranchId, CourtTypeId, EffectiveFrom, Status
        /// - Slots: List of { StartTime, EndTime, WeekdayPrice, WeekendPrice }
        /// </summary>
        [HttpGet("overrides/{effectiveFrom}")]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetPriceOverrideVersionDetail(
            [FromRoute] string effectiveFrom,
            [FromQuery] Guid? branchId,
            [FromQuery] Guid? courtTypeId)
        {
            try
            {
                // 1. Validate required courtTypeId
                if (!courtTypeId.HasValue)
                {
                    return BadRequest(ApiResponse<PriceOverrideVersionDetailDto>.Fail(
                        "Vui lòng cung cấp courtTypeId",
                        ErrorCodes.BadRequest));
                }

                // 2. Parse and validate effectiveFrom date
                if (!DateOnly.TryParseExact(effectiveFrom, "yyyy-MM-dd", out var effectiveFromDate))
                {
                    return BadRequest(ApiResponse<PriceOverrideVersionDetailDto>.Fail(
                        "Định dạng ngày không hợp lệ. Sử dụng yyyy-MM-dd",
                        ErrorCodes.BadRequest));
                }

                // 3. Get current user info from claims
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!Guid.TryParse(userIdClaim, out var currentUserId))
                {
                    return Unauthorized(ApiResponse<PriceOverrideVersionDetailDto>.Fail(
                        "Không xác định được người dùng",
                        ErrorCodes.Unauthorized));
                }

                var currentUserRole = User.FindFirst(ClaimTypes.Role)?.Value;
                if (string.IsNullOrEmpty(currentUserRole))
                {
                    return Unauthorized(ApiResponse<PriceOverrideVersionDetailDto>.Fail(
                        "Không xác định được vai trò người dùng",
                        ErrorCodes.Unauthorized));
                }

                // 4. Call service to get price override version detail
                // Service handles: branch resolution, exact version query, status calculation
                var response = await _service.GetPriceOverrideVersionDetailAsync(
                    branchId,
                    courtTypeId.Value,
                    effectiveFromDate,
                    currentUserId,
                    currentUserRole);

                return Ok(ApiResponse<PriceOverrideVersionDetailDto>.Ok(response));
            }
            catch (AppException ex)
            {
                return StatusCode(ex.StatusCode, ApiResponse<PriceOverrideVersionDetailDto>.Fail(ex.Message, ex.ErrorCode));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<PriceOverrideVersionDetailDto>.Fail(
                    $"Lỗi hệ thống: {ex.Message}",
                    ErrorCodes.InternalError));
            }
        }

        /// <summary>
        /// PATCH /api/prices/overrides/{effectiveFrom} - Create or update branch pricing override version.
        /// 
        /// Authorization: Owner or Manager only
        /// 
        /// Branch Resolution:
        /// - Owner must provide branchId
        /// - Manager uses their assigned branch automatically
        /// 
        /// Path Parameters:
        /// - effectiveFrom (required): Effective date in yyyy-MM-dd format
        /// 
        /// Query Parameters:
        /// - branchId (optional): Required for Owner, ignored for Manager
        /// - courtTypeId (required): Court type to create/update prices for
        /// 
        /// Request Body:
        /// UpsertPriceOverrideRequest with:
        /// - Slots: List of { StartTime, EndTime, WeekdayPrice, WeekendPrice }
        /// 
        /// PATCH Semantics:
        /// Only submitted slots are touched — other slots in the version are left unchanged.
        /// This allows partial updates without re-submitting the entire version.
        /// 
        /// Large Time Span Support:
        /// A single slot input (e.g., 06:00–12:00) is automatically expanded into all
        /// constituent DB time slots.
        /// 
        /// Validations:
        /// 1. effectiveFrom &gt;= today (cannot modify past versions)
        /// 2. courtType must be enabled for branch
        /// 3. Time format must be HH:mm:ss
        /// 4. StartTime &lt; EndTime
        /// 5. Prices must be &gt;= 0
        /// 6. Time ranges must exactly match configured time slots (no gaps, no overruns)
        /// 7. No overlapping time ranges in request
        /// 
        /// Processing:
        /// Two-pass algorithm:
        /// 1. First pass: Validate and expand all input ranges, detect overlaps
        /// 2. Second pass: Build insert/update lists and persist to database
        /// 
        /// Response:
        /// - 201 Created if version was newly created
        /// - 200 OK if version was updated
        /// - Returns PriceOverrideVersionDetailDto with complete version details
        /// </summary>
        [HttpPatch("overrides/{effectiveFrom}")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOrManager)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> UpsertPriceOverrideVersion(
            [FromRoute] string effectiveFrom,
            [FromQuery] Guid? branchId,
            [FromQuery] Guid? courtTypeId,
            [FromBody] UpsertPriceRequest request)
        {
            try
            {
                // 1. Validate required courtTypeId
                if (!courtTypeId.HasValue)
                {
                    return BadRequest(ApiResponse<PriceOverrideVersionDetailDto>.Fail(
                        "Vui lòng cung cấp courtTypeId",
                        ErrorCodes.BadRequest));
                }

                // 2. Parse and validate effectiveFrom date
                if (!DateOnly.TryParseExact(effectiveFrom, "yyyy-MM-dd", out var effectiveFromDate))
                {
                    return BadRequest(ApiResponse<PriceOverrideVersionDetailDto>.Fail(
                        "Định dạng ngày không hợp lệ. Sử dụng yyyy-MM-dd",
                        ErrorCodes.BadRequest));
                }

                // 3. Validate request body
                if (request.Slots == null || !request.Slots.Any())
                {
                    return BadRequest(ApiResponse<PriceOverrideVersionDetailDto>.Fail(
                        "Danh sách giá không được rỗng",
                        ErrorCodes.BadRequest));
                }

                // 4. Get current user info from claims
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!Guid.TryParse(userIdClaim, out var currentUserId))
                {
                    return Unauthorized(ApiResponse<PriceOverrideVersionDetailDto>.Fail(
                        "Không xác định được người dùng",
                        ErrorCodes.Unauthorized));
                }

                var currentUserRole = User.FindFirst(ClaimTypes.Role)?.Value;
                if (string.IsNullOrEmpty(currentUserRole))
                {
                    return Unauthorized(ApiResponse<PriceOverrideVersionDetailDto>.Fail(
                        "Không xác định được vai trò người dùng",
                        ErrorCodes.Unauthorized));
                }

                // 5. Call service to upsert price override version
                // Service handles: branch resolution, validation, upsert logic
                var (response, isCreated) = await _service.UpsertPriceOverrideVersionAsync(
                    branchId,
                    courtTypeId.Value,
                    effectiveFromDate,
                    request,
                    currentUserId,
                    currentUserRole);

                // 6. Return appropriate status code
                if (isCreated)
                {
                    return StatusCode(201, ApiResponse<PriceOverrideVersionDetailDto>.Ok(
                        response,
                        "Tạo phiên bản giá override thành công"));
                }
                else
                {
                    return Ok(ApiResponse<PriceOverrideVersionDetailDto>.Ok(
                        response,
                        "Cập nhật phiên bản giá override thành công"));
                }
            }
            catch (AppException ex)
            {
                return StatusCode(ex.StatusCode, ApiResponse<PriceOverrideVersionDetailDto>.Fail(ex.Message, ex.ErrorCode));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<PriceOverrideVersionDetailDto>.Fail(
                    $"Lỗi hệ thống: {ex.Message}",
                    ErrorCodes.InternalError));
            }
        }

        /// <summary>
        /// DELETE /api/prices/overrides/{effectiveFrom} - Delete a branch price override version.
        /// 
        /// Authorization: Owner or Manager only
        /// 
        /// Branch Resolution:
        /// - Owner must provide branchId
        /// - Manager uses their assigned branch automatically
        /// 
        /// Path Parameters:
        /// - effectiveFrom (required): Effective date in yyyy-MM-dd format
        /// 
        /// Query Parameters:
        /// - branchId (optional): Required for Owner, ignored for Manager
        /// - courtTypeId (required): Court type of the version to delete
        /// 
        /// Delete Rules:
        /// 1. Only SCHEDULED (future) versions can be deleted
        /// 2. ACTIVE and EXPIRED versions are locked — they are historical records
        /// 3. effectiveFrom must be &gt; today
        /// 
        /// Process:
        /// 1. Resolve branch ID based on user role
        /// 2. Validate effectiveFrom &gt; today
        /// 3. Delete all rows where branch_id = ? AND court_type_id = ? AND effective_from = ?
        /// 4. Return 404 if no rows were deleted
        /// 
        /// Response:
        /// - 200 OK with success message if deleted
        /// - 400 Bad Request if version is not future-dated
        /// - 404 Not Found if version does not exist
        /// </summary>
        [HttpDelete("overrides/{effectiveFrom}")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOrManager)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeletePriceOverrideVersion(
            [FromRoute] string effectiveFrom,
            [FromQuery] Guid? branchId,
            [FromQuery] Guid? courtTypeId)
        {
            try
            {
                // 1. Validate required courtTypeId
                if (!courtTypeId.HasValue)
                {
                    return BadRequest(ApiResponse<object>.Fail(
                        "Vui lòng cung cấp courtTypeId",
                        ErrorCodes.BadRequest));
                }

                // 2. Parse and validate effectiveFrom date
                if (!DateOnly.TryParseExact(effectiveFrom, "yyyy-MM-dd", out var effectiveFromDate))
                {
                    return BadRequest(ApiResponse<object>.Fail(
                        "Định dạng ngày không hợp lệ. Sử dụng yyyy-MM-dd",
                        ErrorCodes.BadRequest));
                }

                // 3. Get current user info from claims
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!Guid.TryParse(userIdClaim, out var currentUserId))
                {
                    return Unauthorized(ApiResponse<object>.Fail(
                        "Không xác định được người dùng",
                        ErrorCodes.Unauthorized));
                }

                var currentUserRole = User.FindFirst(ClaimTypes.Role)?.Value;
                if (string.IsNullOrEmpty(currentUserRole))
                {
                    return Unauthorized(ApiResponse<object>.Fail(
                        "Không xác định được vai trò người dùng",
                        ErrorCodes.Unauthorized));
                }

                // 4. Call service to delete price override version
                // Service handles: branch resolution, date validation, deletion logic
                await _service.DeleteVersionAsync(
                    branchId,
                    courtTypeId.Value,
                    effectiveFromDate,
                    currentUserId,
                    currentUserRole);

                return Ok(ApiResponse<object>.Ok(null!, "Xóa phiên bản giá override thành công"));
            }
            catch (AppException ex)
            {
                return StatusCode(ex.StatusCode, ApiResponse<object>.Fail(ex.Message, ex.ErrorCode));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<object>.Fail(
                    $"Lỗi hệ thống: {ex.Message}",
                    ErrorCodes.InternalError));
            }
        }
        /// <summary>
        /// Tính giá theo slot khách chọn
        /// </summary>
        [HttpPost("calculate")]
        [AllowAnonymous] // Public — khách tính giá trước khi đặt sân
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Calculate(
            [FromQuery] Guid branchId,
            [FromBody] CalculatePriceDto dto)
        {
            var result = await _service.CalculateAsync(branchId, dto);
            return Ok(ApiResponse<CalculatePriceResultDto>.Ok(result));
        }
    }
}