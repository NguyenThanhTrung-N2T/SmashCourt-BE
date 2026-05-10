using SmashCourt_BE.DTOs.Upload;

namespace SmashCourt_BE.Services.IService;

/// <summary>
/// Dịch vụ upload file lên Cloudinary.
/// </summary>
public interface ICloudinaryService
{
    /// <summary>
    /// Upload một file ảnh lên Cloudinary.
    /// </summary>
    /// <param name="file">File được gửi lên từ form-data</param>
    /// <param name="folder">Thư mục lưu trên Cloudinary (ví dụ: "branches", "avatars")</param>
    Task<UploadResultDto> UploadImageAsync(IFormFile file, string? folder = null);

    /// <summary>
    /// Upload một file thô (pdf, doc, …) lên Cloudinary.
    /// </summary>
    /// <param name="file">File được gửi lên từ form-data</param>
    /// <param name="folder">Thư mục lưu trên Cloudinary</param>
    Task<UploadResultDto> UploadRawAsync(IFormFile file, string? folder = null);

    /// <summary>
    /// Xóa file đã upload khỏi Cloudinary bằng publicId.
    /// </summary>
    Task DeleteAsync(string publicId, string resourceType = "image");
}
