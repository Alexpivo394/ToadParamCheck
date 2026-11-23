using System.Globalization;
using System.Text;

namespace Alexpivo394.Revit.ParamCheck.Profiles;

/// <summary>
///     Каноническая запись правил, от которой считается отпечаток профиля. Она повторяет то, что до версии 1.1 давал
///     Newtonsoft.Json (без отступов, перечисления именем, без <c>null</c>): отпечатки уже лежат в истории проверок на
///     сервере, и любое расхождение в байте сделало бы прежние проверки несравнимыми с новыми.
///     <para>
///         Формат записан здесь явно и от библиотеки JSON не зависит. Менять его нельзя: новое поле правила, которое
///         должно влиять на отпечаток, дописывается в конец своего объекта.
///     </para>
/// </summary>
internal static class RulesFingerprint
{
    public static string Canonical(List<CheckRule>? rules)
    {
        var text = new StringBuilder();
        if (rules is null) return "null";

        Array(text, rules, Rule);
        return text.ToString();
    }

    private static void Rule(StringBuilder text, CheckRule rule)
    {
        var first = true;
        text.Append('{');
        if (rule.Title is not null) Name(text, "Title", ref first).String(rule.Title);
        if (rule.Filter is not null) Filter(Name(text, "Filter", ref first), rule.Filter);
        if (rule.Parameters is not null) Array(Name(text, "Parameters", ref first), rule.Parameters, (t, parameter) => t.String(parameter));
        text.Append('}');
    }

    private static void Filter(StringBuilder text, RuleFilter filter)
    {
        var first = true;
        text.Append('{');
        if (filter.Categories is not null) Array(Name(text, "Categories", ref first), filter.Categories, (t, category) => t.String(category));
        Name(text, "Mode", ref first).Enum(filter.Mode);
        Name(text, "Logic", ref first).Enum(filter.Logic);
        if (filter.Conditions is not null) Array(Name(text, "Conditions", ref first), filter.Conditions, Condition);
        text.Append('}');
    }

    private static void Condition(StringBuilder text, Condition condition)
    {
        var first = true;
        text.Append('{');
        if (condition.Parameter is not null) Name(text, "Parameter", ref first).String(condition.Parameter);
        if (condition.Operator is { } op) Name(text, "Operator", ref first).Enum(op);
        if (condition.Value is not null) Name(text, "Value", ref first).String(condition.Value);
        if (condition.Group is not null) Array(Name(text, "Group", ref first), condition.Group, Condition);
        text.Append('}');
    }

    /// <summary>Массив; пустой элемент списка пишется как <c>null</c>.</summary>
    private static void Array<T>(StringBuilder text, List<T> items, Action<StringBuilder, T> write) where T : class
    {
        text.Append('[');
        for (var i = 0; i < items.Count; i++)
        {
            if (i > 0) text.Append(',');
            if (items[i] is null) text.Append("null");
            else write(text, items[i]);
        }

        text.Append(']');
    }

    private static StringBuilder Name(StringBuilder text, string name, ref bool first)
    {
        if (!first) text.Append(',');
        first = false;
        return text.Append('"').Append(name).Append("\":");
    }

    /// <summary>Перечисление именем; значение, которому имени нет, — числом.</summary>
    private static void Enum<T>(this StringBuilder text, T value) where T : struct, Enum
    {
        if (System.Enum.IsDefined(typeof(T), value)) text.Append('"').Append(value.ToString()).Append('"');
        else text.Append(Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    ///     Строка JSON. Экранируются кавычка, обратная черта, управляющие символы (короткой записью или <c>\u00xx</c>
    ///     строчными), U+0085, U+2028 и U+2029; всё остальное, включая кириллицу, пишется как есть.
    /// </summary>
    private static void String(this StringBuilder text, string value)
    {
        text.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': text.Append("\\\""); break;
                case '\\': text.Append("\\\\"); break;
                case '\b': text.Append("\\b"); break;
                case '\f': text.Append("\\f"); break;
                case '\n': text.Append("\\n"); break;
                case '\r': text.Append("\\r"); break;
                case '\t': text.Append("\\t"); break;
                case (char)0x0085: text.Append("\\u0085"); break;
                case (char)0x2028: text.Append("\\u2028"); break;
                case (char)0x2029: text.Append("\\u2029"); break;
                default:
                    if (c < ' ') text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else text.Append(c);
                    break;
            }
        }

        text.Append('"');
    }
}
