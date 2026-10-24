namespace Alexpivo394.Revit.ParamCheck.Core;

/// <summary>Не даёт открыть модель в Revit не той версии: новую — нельзя вообще, старую — только с разрешения обновить.</summary>
public static class VersionGuard
{
    /// <param name="modelYear">Год версии, в которой сохранена модель; null — неизвестен, проверка пропускается.</param>
    /// <param name="revitYear">Год запущенного Revit.</param>
    /// <exception cref="CheckException">Модель этой версии здесь открывать нельзя.</exception>
    public static void EnsureCanOpen(string modelName, int? modelYear, int revitYear, bool allowUpgrade)
    {
        if (modelYear is not { } year) return;

        if (year > revitYear)
            throw new CheckException($"модель {modelName} сохранена в Revit {year} — в Revit {revitYear} её не открыть");

        if (year < revitYear && !allowUpgrade)
            throw new CheckException($"модель {modelName} сохранена в Revit {year}; открытие в Revit {revitYear} обновит её — обновление запрещено");
    }
}
