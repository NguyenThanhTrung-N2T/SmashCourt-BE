using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmashCourt_BE.Common;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.DTOs.Upload;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Controllers;

/// <summary>
/// Upload file lên Cloudinary.
/// POST /api/uploads/image  — Upload ảnh (JPEG, PNG, WEBP, GIF — tối đa 10 MB)
/// POST /api/uploads/raw    — Upload tài liệu (PDF, Word — tối đa 20 MB)
/// DELETE /api/uploads/{publicId} — Xóa file đã upload
/// </summary>
[ApiController]
[Route("api/uploads")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
public class UploadController : ControllerBase
{
    private readonly ICloudinaryService _cloudinary;

    public UploadController(ICloudinaryService cloudinary)
    {
        _cloudinary = cloudinary;
    }

    /// <summary>
    /// Upload một file ảnh lên Cloudinary.
    /// Content-Type: multipart/form-data
    /// Form fields:
    ///   - file   (required) — file ảnh
    ///   - folder (optional) — thư mục đích trên Cloudinary (vd: "branches", "avatars")
    /// </summary>
    [HttpPost("image")]
    [EnableRateLimiting("sensitive")]
    [ProducesResponseType(typeof(ApiResponse<UploadResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UploadImage(
        IFormFile file,
        [FromForm] string? folder = null)
    {
        var result = await _cloudinary.UploadImageAsync(file, folder);
        return Ok(ApiResponse<UploadResultDto>.Ok(result, "Upload ảnh thành công"));
    }

    /// <summary>
    /// Upload một file tài liệu (PDF, Word) lên Cloudinary.
    /// Content-Type: multipart/form-data
    /// Form fields:
    ///   - file   (required) — file tài liệu
    ///   - folder (optional) — thư mục đích trên Cloudinary
    /// </summary>
    [HttpPost("raw")]
    [EnableRateLimiting("sensitive")]
    [ProducesResponseType(typeof(ApiResponse<UploadResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UploadRaw(
        IFormFile file,
        [FromForm] string? folder = null)
    {
        var result = await _cloudinary.UploadRawAsync(file, folder);
        return Ok(ApiResponse<UploadResultDto>.Ok(result, "Upload tài liệu thành công"));
    }

    /// <summary>
    /// Xóa file đã upload khỏi Cloudinary theo publicId.
    /// Query param resourceType: "image" | "video" | "raw" (mặc định "image")
    /// </summary>
    [HttpDelete("{publicId}")]
    [Authorize(Policy = AuthorizationPolicies.StaffAndAbove)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(
        string publicId,
        [FromQuery] string resourceType = "image")
    {
        await _cloudinary.DeleteAsync(publicId, resourceType);
        return Ok(ApiResponse<object>.Ok(null!, "Xóa file thành công"));
    }
}
