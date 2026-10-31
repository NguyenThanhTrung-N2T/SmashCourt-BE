using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Integrations.AI;

/// <summary>
/// AI-CT-01 (Epic 6C, gói 6C-1): hợp đồng BE ↔ SmashCourt-AI cho 6 lời gọi của <c>AIService</c> (BE gửi JSON snake_case qua <c>FastApiClient</c>).
/// So kiểu BE (reflection) với Pydantic model trong <c>SmashCourt-AI/app/models/ai_schemas.py</c>, kể cả kiểu lồng:
/// request — trường bắt buộc của AI phải có ở BE (thiếu ⇒ AI trả 422), trường BE gửi phải có ở AI (thiếu ⇒ bị bỏ âm thầm);
/// response — trường BE đọc phải có ở AI (thiếu ⇒ BE luôn nhận mặc định). Kiểm thêm path tồn tại ở router AI với đúng kiểu request/response.
/// Chỉ chạy khi có thư mục SmashCourt-AI cạnh repo BE (máy dev); trên CI chỉ checkout BE nên bỏ qua.
/// </summary>
[Trait("Category", TestCategories.Configuration)]
[Trait("Catalog", "AI-CT-01")]
public class AiContractSyncTests
{
    // Đúng 6 lời gọi PostAsync trong Services/AIService.cs: path, kiểu BE gửi, kiểu Python nhận, kiểu Python trả, kiểu BE đọc
    public static TheoryData<string, string, string, string, string> Calls => new()
    {
        { "/api/v1/ai/chat", "ChatRequestForAiDto", "ChatRequest", "ChatResponse", "AiChatResponseDto" },
        { "/api/v1/ai/suggest/booking", "FastApiBookingSuggestionRequest", "BookingSuggestionRequest", "BookingSuggestionResponse", "AiBookingSuggestionResponseDto" },
        { "/api/v1/ai/suggest/pricing", "FastApiPricingSuggestionRequest", "PricingSuggestionRequest", "PricingSuggestionResponse", "AiPricingSuggestionResponseDto" },
        { "/api/v1/ai/suggest/promotions", "FastApiPromotionSuggestionRequest", "PromotionSuggestionRequest", "PromotionSuggestionResponse", "AiPromotionSuggestionResponseDto" },
        { "/api/v1/ai/analytics/summary", "FastApiAnalyticsRequest", "AnalyticsRequest", "AnalyticsSummaryResponse", "AiAnalyticsSummaryResponseDto" },
        { "/api/v1/ai/analytics/strategic", "FastApiStrategicSuggestionRequest", "StrategicSuggestionRequest", "StrategicSuggestionResponse", "AiStrategicSuggestionResponseDto" },
    };

    [Theory]
    [MemberData(nameof(Calls))]
    public void Call_WhenAiRepoIsPresent_MatchesPydanticModels(string path, string beRequest, string pyRequest, string pyResponse, string beResponse)
    {
        var aiRoot = FindAiRepository();
        if (aiRoot == null)
            return; // Không có SmashCourt-AI cạnh repo BE (CI)

        var schema = PythonSchema.Parse(File.ReadAllText(Path.Combine(aiRoot, "app", "models", "ai_schemas.py")));
        var problems = new List<string>();
        Compare(BeType(beRequest), pyRequest, schema, isRequest: true, pyRequest, problems, []);
        Compare(BeType(beResponse), pyResponse, schema, isRequest: false, pyResponse, problems, []);

        Assert.True(problems.Count == 0, $"{path} lệch hợp đồng BE ↔ AI:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
    }

    [Theory]
    [MemberData(nameof(Calls))]
    public void Call_WhenAiRepoIsPresent_PathExistsWithTheseModels(string path, string beRequest, string pyRequest, string pyResponse, string beResponse)
    {
        _ = beRequest;
        _ = beResponse;
        var aiRoot = FindAiRepository();
        if (aiRoot == null)
            return;

        var routes = PythonRoutes.Parse(aiRoot);

        Assert.True(routes.TryGetValue(path, out var route), $"Router AI không có POST {path}");
        Assert.Equal(pyRequest, route.RequestModel);
        Assert.Equal(pyResponse, route.ResponseModel);
    }

    private static Type BeType(string name) =>
        typeof(Program).Assembly.GetTypes().Single(type => type.Name == name);

    private static void Compare(Type beType, string pyClass, PythonSchema schema, bool isRequest, string path, List<string> problems, HashSet<string> visited)
    {
        if (!visited.Add($"{beType.FullName}|{pyClass}"))
            return;
        if (!schema.Classes.TryGetValue(pyClass, out var pyFields))
        {
            problems.Add($"  {path}: không có class Python {pyClass}");
            return;
        }

        var beProperties = beType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(JsonName, property => property);

        foreach (var name in beProperties.Keys.Where(name => !pyFields.ContainsKey(name)))
            problems.Add($"  {path}.{name}: BE có ({beType.Name}) nhưng AI không có");
        if (isRequest)
        {
            foreach (var (name, field) in pyFields.Where(pair => pair.Value.Required && !beProperties.ContainsKey(pair.Key)))
                problems.Add($"  {path}.{name}: AI bắt buộc nhưng BE không gửi");
        }

        foreach (var (name, property) in beProperties)
        {
            if (pyFields.TryGetValue(name, out var field)
                && NestedBeType(property.PropertyType) is { } nestedBe
                && field.NestedClass is { } nestedPy
                && schema.Classes.ContainsKey(nestedPy))
            {
                Compare(nestedBe, nestedPy, schema, isRequest, $"{path}.{name}", problems, visited);
            }
        }
    }

    private static string JsonName(PropertyInfo property) =>
        property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name);

    // Kiểu lồng cần so: lớp của chính BE, hoặc phần tử của một tập hợp lớp của BE
    private static Type? NestedBeType(Type type)
    {
        var element = type.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(type)
            ? type.GetGenericArguments()[0]
            : type;
        return element.Assembly == typeof(Program).Assembly && element.IsClass ? element : null;
    }

    private static string? FindAiRepository()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "SmashCourt-AI");
            if (File.Exists(Path.Combine(candidate, "app", "models", "ai_schemas.py")))
                return candidate;
        }

        return null;
    }

    private sealed record PythonField(bool Required, string? NestedClass);

    // Đọc Pydantic model theo cú pháp đang dùng trong ai_schemas.py: "class X(Base):" rồi các dòng "    ten: kieu [= mac_dinh]"
    private sealed class PythonSchema
    {
        public Dictionary<string, Dictionary<string, PythonField>> Classes { get; } = [];

        public static PythonSchema Parse(string source)
        {
            var schema = new PythonSchema();
            var bases = new Dictionary<string, string>();
            Dictionary<string, PythonField>? current = null;
            foreach (var line in source.Split('\n').Select(l => l.TrimEnd('\r')))
            {
                var classMatch = Regex.Match(line, @"^class (\w+)\((\w+)\):");
                if (classMatch.Success)
                {
                    current = [];
                    schema.Classes[classMatch.Groups[1].Value] = current;
                    bases[classMatch.Groups[1].Value] = classMatch.Groups[2].Value;
                    continue;
                }

                var fieldMatch = Regex.Match(line, @"^    (\w+)\s*:\s*([^=#]+?)\s*(=[^#]*)?(#.*)?$");
                if (current != null && fieldMatch.Success)
                {
                    var annotation = fieldMatch.Groups[2].Value;
                    var nested = Regex.Match(annotation, @"list\[(\w+)\]|^(\w+)");
                    var nestedName = nested.Groups[1].Success ? nested.Groups[1].Value : nested.Groups[2].Value;
                    current[fieldMatch.Groups[1].Value] = new PythonField(!fieldMatch.Groups[3].Success, nestedName);
                }
            }

            foreach (var (name, baseName) in bases)
            {
                if (schema.Classes.TryGetValue(baseName, out var inherited))
                {
                    foreach (var (field, info) in inherited)
                        schema.Classes[name].TryAdd(field, info);
                }
            }

            return schema;
        }
    }

    private sealed record PythonRoute(string RequestModel, string ResponseModel);

    // Đọc router FastAPI: PREFIX trong main.py + prefix của APIRouter + đường dẫn của @router.post; kiểu request là tham số đầu, response_model trong decorator
    private static class PythonRoutes
    {
        public static Dictionary<string, PythonRoute> Parse(string aiRoot)
        {
            var main = File.ReadAllText(Path.Combine(aiRoot, "app", "main.py"));
            var appPrefix = Regex.Match(main, "PREFIX\\s*=\\s*\"([^\"]*)\"").Groups[1].Value;
            var routes = new Dictionary<string, PythonRoute>();
            foreach (var file in Directory.GetFiles(Path.Combine(aiRoot, "app", "routers"), "*.py"))
            {
                var source = File.ReadAllText(file);
                var routerPrefix = Regex.Match(source, "APIRouter\\(\\s*prefix\\s*=\\s*\"([^\"]*)\"").Groups[1].Value;
                foreach (Match post in Regex.Matches(source,
                             // Tham số decorator lấy không tham lam tới "async def": summary có thể chứa dấu ")" (vd "(Owner)")
                             "@router\\.post\\(\\s*\"(?<path>[^\"]*)\"(?<args>.*?)\\)\\s*\\n\\s*async def \\w+\\(\\s*\\w+\\s*:\\s*(?<request>\\w+)",
                             RegexOptions.Singleline))
                {
                    var response = Regex.Match(post.Groups["args"].Value, @"response_model\s*=\s*(?<model>\w+)").Groups["model"].Value;
                    routes[appPrefix + routerPrefix + post.Groups["path"].Value] = new PythonRoute(post.Groups["request"].Value, response);
                }
            }

            return routes;
        }
    }
}
