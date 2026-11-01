using System.Reflection;
using System.Text.Json;

namespace SmashCourt_BE.Tests.TestData;

/// <summary>
/// Chỉ mục <c>{Namespace.Class.Method → [mã danh mục]}</c> của mọi test (trait <c>Catalog</c> ở phương thức, lớp hoặc lớp cơ sở), ghi ra
/// <c>catalog-index.{AssemblyName}.json</c> khi có biến <see cref="IndexDirectoryVariable"/> (Epic 6C, task 2.3). File trx không giữ trait nên
/// dashboard "Kiểm thử" (<c>TADSS-Runtime/perf/board.py</c>) ghép trx với chỉ mục này để gom kết quả theo mã; cùng định dạng với CatalogIndex của TADSS.
/// </summary>
public static class CatalogIndex
{
    public const string IndexDirectoryVariable = "SMASHCOURT_CATALOG_INDEX_DIR";
    public const string TraitName = "Catalog";

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Build(Assembly assembly)
    {
        var index = new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var type in assembly.GetTypes().Where(type => type.IsClass))
        {
            var classIds = IdsOf(HierarchyAttributes(type)).ToList();
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (!IsTestMethod(method))
                    continue;
                index[$"{type.FullName}.{method.Name}"] = classIds.Concat(IdsOf(method.GetCustomAttributesData()))
                    .Distinct().Order(StringComparer.Ordinal).ToList();
            }
        }
        return index;
    }

    /// <summary>Ghi chỉ mục vào <paramref name="directory"/> (tạo nếu chưa có); trả đường dẫn file, hoặc <c>null</c> khi không yêu cầu xuất.</summary>
    public static string? Export(Assembly assembly, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return null;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"catalog-index.{assembly.GetName().Name}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(Build(assembly), new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    public static string? ExportIfRequested(Assembly assembly) =>
        Export(assembly, Environment.GetEnvironmentVariable(IndexDirectoryVariable));

    // [Fact], [Theory] và các thuộc tính kế thừa FactAttribute (vd StagingFact)
    private static bool IsTestMethod(MethodInfo method) =>
        method.GetCustomAttributesData().Any(data => IsFact(data.AttributeType));

    private static bool IsFact(Type? type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.FullName == "Xunit.FactAttribute")
                return true;
        }
        return false;
    }

    private static IEnumerable<CustomAttributeData> HierarchyAttributes(Type type)
    {
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            foreach (var data in current.GetCustomAttributesData())
                yield return data;
        }
    }

    private static IEnumerable<string> IdsOf(IEnumerable<CustomAttributeData> attributes) =>
        attributes
            .Where(data => data.AttributeType.FullName == "Xunit.TraitAttribute" && data.ConstructorArguments.Count == 2)
            .Where(data => data.ConstructorArguments[0].Value as string == TraitName)
            .Select(data => data.ConstructorArguments[1].Value as string)
            .OfType<string>();
}
