using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.Common
{
    public class PaginationQuery
    {
        [Range(1, int.MaxValue, ErrorMessage = "Page phải >= 1")]
        public int Page { get; set; } = 1;

        [Range(1, 50, ErrorMessage = "PageSize phải từ 1 đến 50")]
        public int PageSize { get; set; } = 10;
    }
}
