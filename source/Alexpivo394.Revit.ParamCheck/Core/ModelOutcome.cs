namespace Alexpivo394.Revit.ParamCheck.Core;

public enum ModelStatus
{
    Checked,
    Failed,
    Skipped
}

/// <summary>Чем закончилась одна модель запуска.</summary>
/// <param name="Report">Итог проверки; есть только у проверенной модели.</param>
/// <param name="ReportPath">Куда сохранён отчёт Excel.</param>
/// <param name="RevitFailure">
///     Проверка упала на внутренней ошибке Revit (<c>Autodesk.Revit.Exceptions.InternalException</c>), а не из-за самой модели:
///     повтор в новом процессе Revit обычно проходит.
/// </param>
public sealed record ModelOutcome(
    string ProfileName,
    string ModelName,
    string SourcePath,
    ModelStatus Status,
    TimeSpan Duration,
    ModelReport? Report = null,
    string? Error = null,
    string? ReportPath = null,
    bool RevitFailure = false);
