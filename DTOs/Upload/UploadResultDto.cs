namespace SmashCourt_BE.DTOs.Upload;

/// <summary>
/// Kết quả trả về sau khi upload file lên Cloudinary.
/// </summary>
public class UploadResultDto
{
    /// <summary>URL công khai của file (dùng để hiển thị / lưu vào DB)</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Public ID trong Cloudinary (dùng để xóa / transform sau này)</summary>
    public string PublicId { get; set; } = string.Empty;

    /// <summary>Loại resource: "image" | "video" | "raw"</summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>Định dạng file (png, jpg, pdf, …)</summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>Kích thước file tính bằng byte</summary>
    public long Bytes { get; set; }
}
