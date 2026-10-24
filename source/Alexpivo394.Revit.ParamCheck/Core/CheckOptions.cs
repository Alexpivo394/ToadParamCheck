namespace Alexpivo394.Revit.ParamCheck.Core;

/// <summary>Как открыть модель и что положить в отчёт. Правила проверки — в профиле, куда класть отчёт — параметр запуска.</summary>
public sealed record CheckOptions
{
    /// <summary>Открывать модель без связей RVT — из временной копии, где связи отключены.</summary>
    public bool SkipLinks { get; init; } = true;

    /// <summary>Рабочие наборы с этим словом в имени не открываются; пусто — открываются все.</summary>
    public string WorksetKeyword { get; init; } = "";

    /// <summary>Вид, с которого собираются элементы; пусто — вся модель.</summary>
    public string ViewName { get; init; } = "";

    /// <summary>В листах правил только элементы с незаполненными параметрами — отчёты по большим моделям в разы меньше.</summary>
    public bool ProblemsOnly { get; init; }

    /// <summary>
    ///     Можно ли открыть модель более старой версии — Revit обновит её в памяти. При <c>false</c> такая модель
    ///     не открывается: <see cref="VersionGuard" />.
    /// </summary>
    public bool AllowUpgrade { get; init; } = true;
}
