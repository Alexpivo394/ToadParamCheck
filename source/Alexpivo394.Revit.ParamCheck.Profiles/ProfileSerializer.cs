using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace Alexpivo394.Revit.ParamCheck.Profiles;

/// <summary>
///     Чтение и запись профилей. Читает и файлы прежних версий ParamChecker: там корнем был массив профилей,
///     а фильтр и параметры правила лежали JSON-строками внутри JSON.
/// </summary>
public static class ProfileSerializer
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        Formatting = Formatting.Indented,
        Converters = [new StringEnumConverter()],
        NullValueHandling = NullValueHandling.Ignore
    };

    private static readonly JsonSerializer Serializer = JsonSerializer.Create(Settings);

    public static string Serialize(ProfileSet set) => JsonConvert.SerializeObject(set, Settings);

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
                JArray legacy => Normalize(new ProfileSet { Profiles = legacy.OfType<JObject>().Select(p => LegacyProfile(p, categoryName)).ToList() }),
                JObject current when current["Profiles"] is JArray => Normalize(current.ToObject<ProfileSet>(Serializer)!),
                _ => throw new InvalidDataException("это не файл профилей ParamChecker")
            };
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"не удалось прочитать профили: {exception.Message}", exception);
        }
    }

    /// <summary>Один профиль в JSON — в том же виде, что внутри файла профилей.</summary>
    public static string SerializeProfile(CheckProfile profile) => JsonConvert.SerializeObject(profile, Settings);

    /// <summary>Один профиль из JSON, записанного <see cref="SerializeProfile" />.</summary>
    /// <exception cref="InvalidDataException">Это не профиль ParamChecker.</exception>
    public static CheckProfile DeserializeProfile(string json)
    {
        if (Parse(json) is not JObject root) throw new InvalidDataException("это не профиль ParamChecker");
        try
        {
            return NormalizeProfile(root.ToObject<CheckProfile>(Serializer) ?? throw new InvalidDataException("это не профиль ParamChecker"));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"не удалось прочитать профиль: {exception.Message}", exception);
        }
    }

    /// <summary>
    ///     Отпечаток правил профиля — 16 шестнадцатеричных знаков. Меняется, только когда меняются правила: имя профиля, модели,
    ///     вид и рабочие наборы на него не влияют. По нему аналитика отмечает, что проверки до и после правки профиля не сравнимы.
    /// </summary>
    public static string Fingerprint(CheckProfile profile)
    {
        var rules = JsonConvert.SerializeObject(profile.Rules, Formatting.None, new JsonSerializerSettings
        {
            Converters = [new StringEnumConverter()],
            NullValueHandling = NullValueHandling.Ignore
        });
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(rules));
        return string.Concat(hash.Take(8).Select(b => b.ToString("x2")));
    }

    /// <summary>Одно правило в JSON — для копирования через буфер обмена.</summary>
    public static string SerializeRule(CheckRule rule) =>
        JsonConvert.SerializeObject(new JObject { ["ParamCheckerRule"] = JObject.FromObject(rule, Serializer) }, Settings);

    /// <summary>Правило из буфера обмена; null — в буфере не правило ParamChecker.</summary>
    public static CheckRule? DeserializeRule(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text!.TrimStart().StartsWith("{")) return null;
        try
        {
            var rule = JObject.Parse(text)["ParamCheckerRule"]?.ToObject<CheckRule>(Serializer);
            return rule is null ? null : NormalizeRule(rule);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Глубокая копия правила.</summary>
    public static CheckRule Clone(CheckRule rule) => DeserializeRule(SerializeRule(rule))!;

    private static JToken Parse(string json)
    {
        try
        {
            return JToken.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"файл повреждён: {exception.Message}", exception);
        }
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

    private static CheckProfile LegacyProfile(JObject json, Func<int, string?> categoryName)
    {
        var models = (json["Models"] as JArray ?? []).OfType<JObject>()
            .Select(model => new ModelSource
            {
                Path = (string?)model["ServerPath"] ?? "",
                ViewName = (string?)model["ViewName"] ?? "",
                WorksetKeyword = (string?)model["WorksetKeyword"] ?? ""
            })
            .ToList();

        var profile = new CheckProfile
        {
            Name = (string?)json["ProfileName"] ?? "",
            IsEnabled = (bool?)json["IsChecked"] ?? true,
            Models = models,
            Rules = (json["Rules"] as JArray ?? []).OfType<JObject>().Select(rule => LegacyRule(rule, categoryName)).ToList()
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

    private static CheckRule LegacyRule(JObject json, Func<int, string?> categoryName)
    {
        var rule = new CheckRule { Title = (string?)json["Title"] ?? "" };

        if (ParseEmbedded((string?)json["FilterConfigJson"]) is JObject filter)
        {
            rule.Filter = new RuleFilter
            {
                Categories = (filter["SelectedCategories"] as JArray ?? [])
                    .Select(token => token.Type == JTokenType.Integer ? categoryName((int)token) : (string?)token)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Distinct()
                    .ToList()!,
                Mode = (SelectionMode)((int?)filter["CategoryParameterLogic"] ?? 0),
                Logic = (ConditionLogic)((int?)filter["ParameterLogic"] ?? 0),
                Conditions = LegacyConditions(filter["Conditions"] as JArray)
            };
        }

        if (ParseEmbedded((string?)json["ParameterConfigJson"]) is JObject parameters)
        {
            rule.Parameters = (parameters["Parameters"] as JArray ?? []).OfType<JObject>()
                .Select(p => ((string?)p["Value"])?.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList()!;
        }

        return rule;
    }

    private static List<Condition> LegacyConditions(JArray? conditions) =>
        (conditions ?? []).OfType<JObject>()
        .Select(condition => (string?)condition["Type"] == "Group"
            ? new Condition { Group = LegacyConditions(condition["Children"] as JArray) }
            : Condition.Simple((string?)condition["ParameterName"] ?? "",
                (ConditionOperator)((int?)condition["SelectedLogic"] ?? 0),
                (string?)condition["Value"] ?? ""))
        .ToList();

    /// <summary>Вложенный JSON старого формата; пустая строка или мусор — null.</summary>
    private static JToken? ParseEmbedded(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JToken.Parse(json!);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
