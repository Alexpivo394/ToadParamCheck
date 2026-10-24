namespace Alexpivo394.Revit.ParamCheck.Core;

/// <summary>Модель не проверить по понятной причине (например, нет нужного вида) — в журнал без стека вызовов.</summary>
public sealed class CheckException(string message) : Exception(message);
