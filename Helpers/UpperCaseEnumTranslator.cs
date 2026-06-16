using Npgsql;

namespace SmashCourt_BE.Helpers
{
    public class UpperCaseEnumTranslator : INpgsqlNameTranslator
    {
        public string TranslateMemberName(string clrName) => clrName; // Giữ nguyên
        public string TranslateTypeName(string clrName) => clrName;
    }
}
