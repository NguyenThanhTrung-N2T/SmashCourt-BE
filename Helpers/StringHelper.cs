namespace SmashCourt_BE.Helpers
{
    public static class StringHelper
    {
        /// <summary>
        /// Xóa dấu tiếng Việt để tạo chuỗi ASCII-safe (ví dụ: orderInfo cho VNPay)
        /// </summary>
        public static string RemoveDiacritics(string text)
        {
            text = text.Replace("Đ", "D").Replace("đ", "d");
            var normalized = text.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder();
            foreach (var c in normalized)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                    != System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
        }
    }
}
