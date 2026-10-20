namespace SmashCourt_BE.Integrations.Tadss.DTOs;

/// <summary>Gợi ý đi kèm quyết định. <c>Type</c> giữ dạng chuỗi để giá trị lạ không làm hỏng việc đọc kết quả.</summary>
public class TadssRecommendationDto
{
    public required string Type { get; set; }
    public required string Description { get; set; }
}
