namespace Alexpivo394.Revit.ParamCheck.Summaries;

/// <summary>
///     Итог проверки одной модели для аналитики — без элементов, только счётчики модели, правил и параметров. Один формат
///     и у сервиса ToadFlow, и у плагина ParamChecker: оба отдают его в JSON (camelCase), сервер складывает его в базу.
///     Доли (<see cref="Completeness" />, <see cref="FillRate" />) считаются из счётчиков и при чтении не нужны.
/// </summary>
public sealed class CheckSummary
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Имя модели — имя файла без расширения.</summary>
    public string ModelName { get; set; } = "";

    /// <summary>Путь на Revit Server (<c>RSN://…</c>) или к файлу.</summary>
    public string ModelPath { get; set; } = "";

    public string ProfileName { get; set; } = "";

    /// <summary>
    ///     Отпечаток правил профиля (<c>ProfileSerializer.Fingerprint</c>): у проверок с разными отпечатками правила разные,
    ///     и их доли напрямую не сравнить.
    /// </summary>
    public string ProfileFingerprint { get; set; } = "";

    /// <summary>Вид, с которого собраны элементы; пусто — вся модель.</summary>
    public string ViewName { get; set; } = "";

    public DateTimeOffset CheckedAt { get; set; }

    /// <summary>Длительность проверки, секунды; 0 — неизвестна.</summary>
    public double DurationSeconds { get; set; }

    /// <summary>Где лежит отчёт Excel; null — не сохранялся.</summary>
    public string? ReportPath { get; set; }

    public int Elements { get; set; }
    public int CompleteElements { get; set; }
    public int Values { get; set; }
    public int Filled { get; set; }
    public int Empty { get; set; }
    public int Missing { get; set; }

    public List<RuleSummary> Rules { get; set; } = [];

    /// <summary>Доля элементов, у которых заполнено всё, что проверялось; null — элементов нет.</summary>
    public double? Completeness => Ratio(CompleteElements, Elements);

    /// <summary>Доля заполненных значений среди всех проверенных (элементы × параметры); null — значений нет.</summary>
    public double? FillRate => Ratio(Filled, Values);

    internal static double? Ratio(int part, int total) => total == 0 ? null : (double)part / total;
}

/// <summary>Итог одного правила.</summary>
public sealed class RuleSummary
{
    public string Title { get; set; } = "";
    public int Elements { get; set; }
    public int CompleteElements { get; set; }
    public List<ParameterSummary> Parameters { get; set; } = [];

    public int Values => Parameters.Sum(p => p.Total);
    public int Filled => Parameters.Sum(p => p.Filled);
    public int Empty => Parameters.Sum(p => p.Empty);
    public int Missing => Parameters.Sum(p => p.Missing);
    public double? Completeness => CheckSummary.Ratio(CompleteElements, Elements);
    public double? FillRate => CheckSummary.Ratio(Filled, Values);
}

/// <summary>Итог одного параметра правила: у скольких отобранных элементов он заполнен, пуст или отсутствует.</summary>
public sealed class ParameterSummary
{
    public string Name { get; set; } = "";
    public int Filled { get; set; }
    public int Empty { get; set; }
    public int Missing { get; set; }

    public int Total => Filled + Empty + Missing;
    public double? FillRate => CheckSummary.Ratio(Filled, Total);
}
