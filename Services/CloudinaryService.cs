using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;
using SmashCourt_BE.Common;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.DTOs.Upload;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services;

/// <summary>
/// Triển khai upload / xóa file qua Cloudinary SDK.
/// </summary>
public class CloudinaryService : ICloudinaryService
{
    private readonly Cloudinary _cloudinary;

    // ─── Giới hạn upload ──────────────────────────────────────────────────
    private const long MaxImageBytes = 10 * 1024 * 1024;   // 10 MB
    private const long MaxRawBytes   = 20 * 1024 * 1024;   // 20 MB

    private static readonly HashSet<string> AllowedImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png", "image/webp", "image/gif"
    };

    private static readonly HashSet<string> AllowedRawTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    };

    public CloudinaryService(IOptions<CloudinarySettings> options)
    {
        var cfg = options.Value;
        var account = new Account(cfg.CloudName, cfg.ApiKey, cfg.ApiSecret);
        _cloudinary = new Cloudinary(account) { Api = { Secure = true } };
    }

    // ─── Upload ảnh ───────────────────────────────────────────────────────

    public async Task<UploadResultDto> UploadImageAsync(IFormFile file, string? folder = null)
    {
        ValidateFile(file, AllowedImageTypes, MaxImageBytes, "Ảnh");

        await using var stream = file.OpenReadStream();

        var uploadParams = new ImageUploadParams
        {
            File           = new FileDescription(file.FileName, stream),
            Folder         = folder,
            UseFilename    = false,
            UniqueFilename = true,
            Overwrite      = false,
            // Tự động nén / convert sang webp để tiết kiệm băng thông
            Transformation = new Transformation().Quality("auto").FetchFormat("auto")
        };

        var result = await _cloudinary.UploadAsync(uploadParams);

        if (result.Error != null)
            throw new AppException(500, result.Error.Message, ErrorCodes.InternalError);

        return MapToDto(result);
    }

    // ─── Upload file thô (PDF, Word …) ───────────────────────────────────

    public async Task<UploadResultDto> UploadRawAsync(IFormFile file, string? folder = null)
    {
        ValidateFile(file, AllowedRawTypes, MaxRawBytes, "Tài liệu");

        await using var stream = file.OpenReadStream();

        var uploadParams = new RawUploadParams
        {
            File           = new FileDescription(file.FileName, stream),
            Folder         = folder,
            UseFilename    = false,
            UniqueFilename = true,
            Overwrite      = false,
        };

        var result = await _cloudinary.UploadAsync(uploadParams);

        if (result.Error != null)
            throw new AppException(500, result.Error.Message, ErrorCodes.InternalError);

        return new UploadResultDto
        {
            Url          = result.SecureUrl?.ToString() ?? string.Empty,
            PublicId     = result.PublicId,
            ResourceType = "raw",
            Format       = result.Format ?? string.Empty,
            Bytes        = result.Bytes
        };
    }

    // ─── Xóa file ─────────────────────────────────────────────────────────

    public async Task DeleteAsync(string publicId, string resourceType = "image")
    {
        var resType = resourceType.ToLowerInvariant() switch
        {
            "image" => ResourceType.Image,
            "video" => ResourceType.Video,
            _       => ResourceType.Raw
        };

        var deleteParams = new DeletionParams(publicId) { ResourceType = resType };
        var result = await _cloudinary.DestroyAsync(deleteParams);

        if (result.Error != null)
            throw new AppException(500, result.Error.Message, ErrorCodes.InternalError);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private static void ValidateFile(
        IFormFile file,
        HashSet<string> allowedTypes,
        long maxBytes,
        string label)
    {
        if (file == null || file.Length == 0)
            throw new AppException(400, $"{label} không được để trống", ErrorCodes.ValidationError);

        if (!allowedTypes.Contains(file.ContentType))
            throw new AppException(
                400,
                $"Định dạng {label.ToLower()} không hợp lệ. Chấp nhận: {string.Join(", ", allowedTypes)}",
                ErrorCodes.ValidationError);

        if (file.Length > maxBytes)
            throw new AppException(
                400,
                $"{label} vượt quá kích thước tối đa ({maxBytes / 1024 / 1024} MB)",
                ErrorCodes.ValidationError);
    }

    private static UploadResultDto MapToDto(ImageUploadResult result) => new()
    {
        Url          = result.SecureUrl?.ToString() ?? string.Empty,
        PublicId     = result.PublicId,
        ResourceType = "image",
        Format       = result.Format ?? string.Empty,
        Bytes        = result.Bytes
    };
}
