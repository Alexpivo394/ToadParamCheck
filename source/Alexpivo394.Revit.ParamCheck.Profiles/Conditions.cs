namespace Alexpivo394.Revit.ParamCheck.Profiles;

/// <summary>Что в условиях отбора считается дописанным — общее для проверки модели, редакторов и проверки перед запуском.</summary>
public static class Conditions
{
    /// <summary>
    ///     В фильтре есть хоть одно дописанное условие. Без условий «категории ИЛИ условия» — это только категории:
    ///     иначе пустой список условий пропускал бы все элементы модели.
    /// </summary>
    public static bool HasConditions(RuleFilter filter) => filter.Conditions.Any(IsComplete);

    /// <summary>Оператору не нужно значение — поле значения можно спрятать.</summary>
    public static bool NeedsValue(ConditionOperator op) =>
        op is not (ConditionOperator.Exists or ConditionOperator.NotExists or ConditionOperator.IsFilled or ConditionOperator.IsEmpty);

    /// <summary>Недописанное условие (без параметра или пустая группа) не учитывается.</summary>
    public static bool IsComplete(Condition condition) =>
        condition.IsGroup ? condition.Group!.Any(IsComplete) : !string.IsNullOrWhiteSpace(condition.Parameter) && condition.Operator is not null;
}
