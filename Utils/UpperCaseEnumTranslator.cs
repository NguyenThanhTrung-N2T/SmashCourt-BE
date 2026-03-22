using Npgsql;

namespace SmashCourt_BE.Utils
{
    public class UpperCaseEnumTranslator : INpgsqlNameTranslator
    {
        public string TranslateMemberName(string clrName) => clrName; // Giữ nguyên
        public string TranslateTypeName(string clrName) => clrName;
    }
}
