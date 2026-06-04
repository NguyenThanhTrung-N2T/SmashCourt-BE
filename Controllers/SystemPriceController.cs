using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.Common;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.DTOs.PriceConfig;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Controllers
{
    [ApiController]
    [Route("api/system-prices")]
    public class SystemPriceController : ControllerBase
    {
        private readonly ISystemPriceService _service;

        public SystemPriceController(ISystemPriceService service)
        {
            _service = service;
        }

        /// <summary>
        /// GET /api/system-prices - Returns effective system pricing for a specific date.
        /// 
        /// Authorization: Staff and above
        /// 
        /// Query Parameters:
        /// - date (optional): Target date in yyyy-MM-dd format, defaults to today
        /// - courtTypeId (optional): Filter by specific court type
        /// 
        /// Process:
        /// 1. Parse date parameter or use today
        /// 2. Get latest SystemPrice where effective_from &lt;= requestedDate
        /// 3. Combine WEEKDAY and WEEKEND rows into single DTOs
        /// 4. Merge consecutive time slots with same prices
        /// 5. Return grouped by court type
        /// 
        /// Response:
        /// Returns List of CurrentPriceDto with:
        /// - CourtTypeId, CourtTypeName
        /// - StartTime, EndTime
        /// - WeekdayPrice, WeekendPrice
        /// - EffectiveFrom
        /// </summary>
        [HttpGet]
        [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetEffectivePrices(
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
                        return BadRequest(ApiResponse<List<CurrentPriceDto>>.Fail(
                            "Định dạng ngày không hợp lệ. Sử dụng yyyy-MM-dd",
                            ErrorCodes.BadRequest));
                    }
                }

                // 2. Call service to get effective system prices
                var result = await _service.GetEffectivePricesAsync(targetDate, courtTypeId);

                return Ok(ApiResponse<List<CurrentPriceDto>>.Ok(result));
            }
            catch (AppException ex)
            {
                return StatusCode(ex.StatusCode, ApiResponse<List<CurrentPriceDto>>.Fail(ex.Message, ex.ErrorCode));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<List<CurrentPriceDto>>.Fail(
                    $"Lỗi hệ thống: {ex.Message}",
                    ErrorCodes.InternalError));
            }
        }

        /// <summary>
        /// GET /api/system-prices/versions - Returns all system price versions for a court type.
        /// 
        /// Authorization: Owner only
        /// 
        /// Query Parameters:
        /// - courtTypeId (required): Court type to get versions for
        /// 
        /// Process:
        /// 1. Validate court type exists
        /// 2. Get distinct effective_from values for courtTypeId
        /// 3. Sort descending
        /// 4. Calculate status:
        ///    - ACTIVE: latest effective_from &lt;= today
        ///    - SCHEDULED: effective_from &gt; today
        ///    - EXPIRED: effective_from &lt; active version
        /// 5. If no version is effective yet, all versions remain SCHEDULED
        /// 
        /// Response:
        /// Returns SystemPriceVersionsResponse with:
        /// - CourtTypeId
        /// - Versions: List of { EffectiveFrom, Status }
        /// </summary>
        [HttpGet("versions")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetVersions([FromQuery] Guid? courtTypeId)
        {
            try
            {
                // 1. Validate required courtTypeId
                if (!courtTypeId.HasValue)
                {
                    return BadRequest(ApiResponse<SystemPriceVersionsResponse>.Fail(
                        "Vui lòng cung cấp courtTypeId",
                        ErrorCodes.BadRequest));
                }

                // 2. Call service to get system price versions
                // Service handles: court type validation, version retrieval, status calculation
                var response = await _service.GetVersionsAsync(courtTypeId.Value);

                return Ok(ApiResponse<SystemPriceVersionsResponse>.Ok(response));
            }
            catch (AppException ex)
            {
                return StatusCode(ex.StatusCode, ApiResponse<SystemPriceVersionsResponse>.Fail(ex.Message, ex.ErrorCode));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<SystemPriceVersionsResponse>.Fail(
                    $"Lỗi hệ thống: {ex.Message}",
                    ErrorCodes.InternalError));
            }
        }

        /// <summary>
        /// GET /api/system-prices/versions/{effectiveFrom} - Returns exact system price version configuration.
        /// 
        /// Authorization: Owner only
        /// 
        /// Path Parameters:
        /// - effectiveFrom (required): Effective date in yyyy-MM-dd format
        /// 
        /// Query Parameters:
        /// - courtTypeId (required): Court type to get version for
        /// 
        /// Query Rules:
        /// Uses exact match: court_type_id = ? AND effective_from = ?
        /// Does NOT use: effective_from &lt;= date
        /// 
        /// Process:
        /// 1. Validate court type exists
        /// 2. Query exact system price version for the specified date
        /// 3. Group WEEKDAY and WEEKEND rows into single slots
        /// 4. Calculate status (ACTIVE, SCHEDULED, EXPIRED)
        /// 5. Return 404 if version does not exist
        /// 
        /// Response:
        /// Returns SystemPriceVersionDetailDto with:
        /// - CourtTypeId, CourtTypeName, EffectiveFrom, Status
        /// - Slots: List of { StartTime, EndTime, WeekdayPrice, WeekendPrice }
        /// </summary>
        [HttpGet("versions/{effectiveFrom}")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetVersionDetail(
            [FromRoute] string effectiveFrom,
            [FromQuery] Guid? courtTypeId)
        {
            try
            {
                // 1. Validate required courtTypeId
                if (!courtTypeId.HasValue)
                {
                    return BadRequest(ApiResponse<SystemPriceVersionDetailDto>.Fail(
                        "Vui lòng cung cấp courtTypeId",
                        ErrorCodes.BadRequest));
                }

                // 2. Parse and validate effectiveFrom date
                if (!DateOnly.TryParseExact(effectiveFrom, "yyyy-MM-dd", out var effectiveFromDate))
                {
                    return BadRequest(ApiResponse<SystemPriceVersionDetailDto>.Fail(
                        "Định dạng ngày không hợp lệ. Sử dụng yyyy-MM-dd",
                        ErrorCodes.BadRequest));
                }

                // 3. Call service to get system price version detail
                // Service handles: court type validation, exact version query, status calculation
                var response = await _service.GetVersionDetailAsync(courtTypeId.Value, effectiveFromDate);

                return Ok(ApiResponse<SystemPriceVersionDetailDto>.Ok(response));
            }
            catch (AppException ex)
            {
                return StatusCode(ex.StatusCode, ApiResponse<SystemPriceVersionDetailDto>.Fail(ex.Message, ex.ErrorCode));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<SystemPriceVersionDetailDto>.Fail(
                    $"Lỗi hệ thống: {ex.Message}",
                    ErrorCodes.InternalError));
            }
        }

        /// <summary>
        /// PATCH /api/system-prices/versions/{effectiveFrom} - Create or update system price version.
        /// 
        /// Authorization: Owner only
        /// 
        /// Path Parameters:
        /// - effectiveFrom (required): Effective date in yyyy-MM-dd format
        /// 
        /// Query Parameters:
        /// - courtTypeId (required): Court type to create/update prices for
        /// 
        /// Request Body:
        /// UpsertSystemPriceRequest with:
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
        /// 2. Court type must exist
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
        /// - Returns SystemPriceVersionDetailDto with complete version details
        /// </summary>
        [HttpPatch("versions/{effectiveFrom}")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> UpsertVersion(
            [FromRoute] string effectiveFrom,
            [FromQuery] Guid? courtTypeId,
            [FromBody] UpsertPriceRequest request)
        {
            try
            {
                // 1. Validate required courtTypeId
                if (!courtTypeId.HasValue)
                {
                    return BadRequest(ApiResponse<SystemPriceVersionDetailDto>.Fail(
                        "Vui lòng cung cấp courtTypeId",
                        ErrorCodes.BadRequest));
                }

                // 2. Parse and validate effectiveFrom date
                if (!DateOnly.TryParseExact(effectiveFrom, "yyyy-MM-dd", out var effectiveFromDate))
                {
                    return BadRequest(ApiResponse<SystemPriceVersionDetailDto>.Fail(
                        "Định dạng ngày không hợp lệ. Sử dụng yyyy-MM-dd",
                        ErrorCodes.BadRequest));
                }

                // 3. Validate request body
                if (request.Slots == null || !request.Slots.Any())
                {
                    return BadRequest(ApiResponse<SystemPriceVersionDetailDto>.Fail(
                        "Danh sách giá không được rỗng",
                        ErrorCodes.BadRequest));
                }

                // 4. Call service to upsert system price version
                // Service handles: court type validation, all business logic validations, upsert logic
                var (response, isCreated) = await _service.UpsertVersionAsync(
                    courtTypeId.Value,
                    effectiveFromDate,
                    request);

                // 5. Return appropriate status code
                if (isCreated)
                {
                    return StatusCode(201, ApiResponse<SystemPriceVersionDetailDto>.Ok(
                        response,
                        "Tạo phiên bản giá hệ thống thành công"));
                }
                else
                {
                    return Ok(ApiResponse<SystemPriceVersionDetailDto>.Ok(
                        response,
                        "Cập nhật phiên bản giá hệ thống thành công"));
                }
            }
            catch (AppException ex)
            {
                return StatusCode(ex.StatusCode, ApiResponse<SystemPriceVersionDetailDto>.Fail(ex.Message, ex.ErrorCode));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<SystemPriceVersionDetailDto>.Fail(
                    $"Lỗi hệ thống: {ex.Message}",
                    ErrorCodes.InternalError));
            }
        }

        /// <summary>
        /// DELETE /api/system-prices/versions/{effectiveFrom} - Delete a system price version.
        /// 
        /// Authorization: Owner only
        /// 
        /// Path Parameters:
        /// - effectiveFrom (required): Effective date in yyyy-MM-dd format
        /// 
        /// Query Parameters:
        /// - courtTypeId (required): Court type of the version to delete
        /// 
        /// Delete Rules:
        /// 1. Only SCHEDULED (future) versions can be deleted
        /// 2. ACTIVE and EXPIRED versions are locked — they are historical records
        /// 3. effectiveFrom must be &gt; today
        /// 
        /// Process:
        /// 1. Validate court type exists
        /// 2. Validate effectiveFrom &gt; today
        /// 3. Delete all rows where court_type_id = ? AND effective_from = ?
        /// 4. Return 404 if no rows were deleted
        /// 
        /// Response:
        /// - 200 OK with success message if deleted
        /// - 400 Bad Request if version is not future-dated
        /// - 404 Not Found if version does not exist
        /// </summary>
        [HttpDelete("versions/{effectiveFrom}")]
        [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteVersion(
            [FromRoute] string effectiveFrom,
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

                // 3. Call service to delete system price version
                // Service handles: court type validation, date validation, deletion logic
                await _service.DeleteVersionAsync(courtTypeId.Value, effectiveFromDate);

                return Ok(ApiResponse<object>.Ok(null!, "Xóa phiên bản giá hệ thống thành công"));
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
    }
}
