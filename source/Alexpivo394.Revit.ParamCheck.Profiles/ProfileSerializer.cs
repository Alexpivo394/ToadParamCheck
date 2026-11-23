using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Alexpivo394.Revit.ParamCheck.Profiles;

/// <summary>
///     Чтение и запись профилей. Читает и файлы прежних версий ParamChecker: там корнем был массив профилей,
///     а фильтр и параметры правила лежали JSON-строками внутри JSON.
/// </summary>
public static class ProfileSerializer
{
    /// <summary>
    ///     Формат файла: имена как в классах, перечисления именем, <c>null</c> и вычисляемые свойства не пишутся, кириллица
    ///     не экранируется. Читается терпимо: имена в любом регистре, комментарии, лишняя запятая, число на месте строки.
    /// </summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(), new LenientStringConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { StoredOnly } }
    };

    private static readonly JsonDocumentOptions DocumentOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static string Serialize(ProfileSet set) => JsonSerializer.Serialize(set, Options);

    /// <param name="json">Содержимое файла профилей любой версии.</param>
    /// <param name="categoryName">
    ///     Имя <c>BuiltInCategory</c> по числу — в старых файлах категории хранились числами. Без Revit (на сервере) — функция,
    ///     возвращающая null: такие категории отбрасываются.
    /// </param>
    /// <exception cref="InvalidDataException">Файл не похож на профили ParamChecker.</exception>
    public static ProfileSet Deserialize(string json, Func<int, string?> categoryName)
    {
        var root = Parse(json);
        try
        {
            return root switch
            {
                JsonArray legacy => Normalize(new ProfileSet { Profiles = legacy.OfType<JsonObject>().Select(p => LegacyProfile(p, categoryName)).ToList() }),
                JsonObject current when current["Profiles"] is JsonArray => Normalize(Read<ProfileSet>(current)!),
                _ => throw new InvalidDataException("это не файл профилей ParamChecker")
            };
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            throw new InvalidDataException($"не удалось прочитать профили: {exception.Message}", exception);
        }
    }

    /// <summary>Один профиль в JSON — в том же виде, что внутри файла профилей.</summary>
    public static string SerializeProfile(CheckProfile profile) => JsonSerializer.Serialize(profile, Options);

    /// <summary>Один профиль из JSON, записанного <see cref="SerializeProfile" />.</summary>
    /// <exception cref="InvalidDataException">Это не профиль ParamChecker.</exception>
    public static CheckProfile DeserializeProfile(string json)
    {
        if (Parse(json) is not JsonObject root) throw new InvalidDataException("это не профиль ParamChecker");
        try
        {
            return NormalizeProfile(Read<CheckProfile>(root) ?? throw new InvalidDataException("это не профиль ParamChecker"));
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            throw new InvalidDataException($"не удалось прочитать профиль: {exception.Message}", exception);
        }
    }

    /// <summary>
    ///     Отпечаток правил профиля — 16 шестнадцатеричных знаков. Меняется, только когда меняются правила: имя профиля, модели,
    ///     вид и рабочие наборы на него не влияют. По нему аналитика отмечает, что проверки до и после правки профиля не сравнимы.
    /// </summary>
    /// <remarks>
    ///     Отпечаток считается от канонической записи правил (<see cref="RulesFingerprint" />), а не от файла профиля: она обязана
    ///     оставаться той же байт в байт, иначе история проверок на сервере разойдётся с новыми запусками.
    /// </remarks>
    public static string Fingerprint(CheckProfile profile)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(RulesFingerprint.Canonical(profile.Rules)));
        return string.Concat(hash.Take(8).Select(b => b.ToString("x2")));
    }

    /// <summary>Одно правило в JSON — для копирования через буфер обмена.</summary>
    public static string SerializeRule(CheckRule rule) =>
        new JsonObject { ["ParamCheckerRule"] = JsonSerializer.SerializeToNode(rule, Options) }.ToJsonString(Options);

    /// <summary>Правило из буфера обмена; null — в буфере не правило ParamChecker.</summary>
    public static CheckRule? DeserializeRule(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text!.TrimStart().StartsWith("{")) return null;
        try
        {
            if (JsonNode.Parse(text, null, DocumentOptions) is not JsonObject root || root["ParamCheckerRule"] is not JsonObject body) return null;
            var rule = Read<CheckRule>(body);
            return rule is null ? null : NormalizeRule(rule);
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            return null;
        }
    }

    /// <summary>Глубокая копия правила.</summary>
    public static CheckRule Clone(CheckRule rule) => DeserializeRule(SerializeRule(rule))!;

    private static JsonNode? Parse(string json)
    {
        try
        {
            return JsonNode.Parse(json, null, DocumentOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"файл повреждён: {exception.Message}", exception);
        }
    }

    /// <summary>
    ///     Объект из разобранного JSON. <c>null</c> в файле не затирает значение по умолчанию — как и раньше, такие поля
    ///     просто пропускаются.
    /// </summary>
    private static T? Read<T>(JsonObject json) where T : class
    {
        DropNulls(json);
        return json.Deserialize<T>(Options);
    }

    private static void DropNulls(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject json:
                foreach (var name in json.Where(pair => pair.Value is null).Select(pair => pair.Key).ToList()) json.Remove(name);
                foreach (var pair in json) DropNulls(pair.Value);
                break;
            case JsonArray array:
                foreach (var item in array) DropNulls(item);
                break;
        }
    }

    /// <summary>Битый или чужой JSON: разбор, повторяющееся имя поля, значение не того вида.</summary>
    private static bool IsReadFailure(Exception exception) =>
        exception is JsonException or ArgumentException or InvalidOperationException or NotSupportedException or FormatException;

    /// <summary>В файл идут только свойства, которые из него читаются: вычисляемые (<c>Name</c>, <c>IsGroup</c>…) пропускаются.</summary>
    private static void StoredOnly(JsonTypeInfo type)
    {
        if (type.Kind != JsonTypeInfoKind.Object) return;
        for (var i = type.Properties.Count - 1; i >= 0; i--)
            if (type.Properties[i].Set is null)
                type.Properties.RemoveAt(i);
    }

    private static ProfileSet Normalize(ProfileSet set)
    {
        set.Profiles ??= [];
        set.Profiles.RemoveAll(profile => profile is null);
        foreach (var profile in set.Profiles) NormalizeProfile(profile);

        set.Version = ProfileSet.CurrentVersion;
        return set;
    }

    private static CheckProfile NormalizeProfile(CheckProfile profile)
    {
        profile.Name ??= "";
        profile.ViewName ??= "";
        profile.WorksetKeyword ??= "";
        profile.Models ??= [];
        profile.Rules ??= [];
        profile.Models.RemoveAll(model => model is null);
        profile.Rules.RemoveAll(rule => rule is null);
        foreach (var model in profile.Models) model.Path ??= "";
        foreach (var rule in profile.Rules) NormalizeRule(rule);
        return profile;
    }

    private static CheckRule NormalizeRule(CheckRule rule)
    {
        rule.Title ??= "";
        // Повторы параметра (в старых профилях встречаются) дали бы в отчёте одинаковые столбцы.
        rule.Parameters = (rule.Parameters ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct().ToList();
        rule.Filter ??= new RuleFilter();
        rule.Filter.Categories = (rule.Filter.Categories ?? []).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
        rule.Filter.Conditions = (rule.Filter.Conditions ?? []).Where(c => c is not null).ToList();
        foreach (var condition in rule.Filter.Conditions) NormalizeCondition(condition);
        return rule;
    }

    /// <summary>В группе не бывает пустых элементов: их оставляет только вручную испорченный файл.</summary>
    private static void NormalizeCondition(Condition condition)
    {
        if (condition.Group is null) return;
        condition.Group.RemoveAll(child => child is null);
        foreach (var child in condition.Group) NormalizeCondition(child);
    }

    private static CheckProfile LegacyProfile(JsonObject json, Func<int, string?> categoryName)
    {
        var models = Objects(json["Models"])
            .Select(model => new ModelSource
            {
                Path = Text(model["ServerPath"]) ?? "",
                ViewName = Text(model["ViewName"]) ?? "",
                WorksetKeyword = Text(model["WorksetKeyword"]) ?? ""
            })
            .ToList();

        var profile = new CheckProfile
        {
            Name = Text(json["ProfileName"]) ?? "",
            IsEnabled = Flag(json["IsChecked"]) ?? true,
            Models = models,
            Rules = Objects(json["Rules"]).Select(rule => LegacyRule(rule, categoryName)).ToList()
        };

        // Раньше вид и РН задавались у каждой модели. Если у всех они одинаковые — это настройка профиля.
        HoistCommon(models, m => m.ViewName, (m, v) => m.ViewName = v, v => profile.ViewName = v);
        HoistCommon(models, m => m.WorksetKeyword, (m, v) => m.WorksetKeyword = v, v => profile.WorksetKeyword = v);
        return profile;
    }

    private static void HoistCommon(List<ModelSource> models, Func<ModelSource, string?> get, Action<ModelSource, string?> set,
        Action<string> setProfile)
    {
        var values = models.Select(m => (get(m) ?? "").Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (values.Count != 1) return;

        setProfile(values[0]);
        foreach (var model in models) set(model, null);
    }

    private static CheckRule LegacyRule(JsonObject json, Func<int, string?> categoryName)
    {
        var rule = new CheckRule { Title = Text(json["Title"]) ?? "" };

        if (ParseEmbedded(Text(json["FilterConfigJson"])) is JsonObject filter)
        {
            rule.Filter = new RuleFilter
            {
                Categories = (filter["SelectedCategories"] as JsonArray ?? [])
                    .Select(token => Whole(token) is { } id && !IsText(token) ? categoryName(id) : Text(token))
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Distinct()
                    .ToList()!,
                Mode = (SelectionMode)(Whole(filter["CategoryParameterLogic"]) ?? 0),
                Logic = (ConditionLogic)(Whole(filter["ParameterLogic"]) ?? 0),
                Conditions = LegacyConditions(filter["Conditions"])
            };
        }

        if (ParseEmbedded(Text(json["ParameterConfigJson"])) is JsonObject parameters)
        {
            rule.Parameters = Objects(parameters["Parameters"])
                .Select(p => Text(p["Value"])?.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList()!;
        }

        return rule;
    }

    private static List<Condition> LegacyConditions(JsonNode? conditions) =>
        Objects(conditions)
            .Select(condition => Text(condition["Type"]) == "Group"
                ? new Condition { Group = LegacyConditions(condition["Children"]) }
                : Condition.Simple(Text(condition["ParameterName"]) ?? "",
                    (ConditionOperator)(Whole(condition["SelectedLogic"]) ?? 0),
                    Text(condition["Value"]) ?? ""))
            .ToList();

    /// <summary>Вложенный JSON старого формата; пустая строка или мусор — null.</summary>
    private static JsonNode? ParseEmbedded(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonNode.Parse(json!, null, DocumentOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Объекты массива; не массив — пусто.</summary>
    private static IEnumerable<JsonObject> Objects(JsonNode? node) => (node as JsonArray ?? []).OfType<JsonObject>();

    private static bool IsText(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out _);

    /// <summary>Значение строкой; число и логическое — их текстом; объект, массив и <c>null</c> — null.</summary>
    private static string? Text(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<string>(out var text)) return text;
        if (value.TryGetValue<bool>(out var flag)) return flag ? "True" : "False";
        return value.ToJsonString();
    }

    /// <summary>Целое из числа или строки с числом.</summary>
    private static int? Whole(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<int>(out var number)) return number;
        if (value.TryGetValue<double>(out var real)) return (int)real;
        return value.TryGetValue<string>(out var text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    /// <summary>Логическое значение; старые файлы писали его и строкой, и числом.</summary>
    private static bool? Flag(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<bool>(out var flag)) return flag;
        if (value.TryGetValue<string>(out var text) && bool.TryParse(text, out var parsed)) return parsed;
        return Whole(node) is { } number ? number != 0 : null;
    }

    /// <summary>Строка, на месте которой в файле число или логическое значение, читается их текстом.</summary>
    private sealed class LenientStringConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => Encoding.UTF8.GetString(reader.ValueSpan.ToArray()),
            JsonTokenType.True => "True",
            JsonTokenType.False => "False",
            _ => throw new JsonException("ожидалась строка")
        };

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
    }
}
