using Autodesk.Revit.DB;

namespace Alexpivo394.Revit.ParamCheck.Services;

/// <summary>Категория для выбора в фильтре: имя <c>BuiltInCategory</c> хранится в профиле, подпись — на языке Revit.</summary>
public sealed record CategoryInfo(string Key, string Name, bool IsModel);

/// <summary>Категории, к которым можно привязать параметры, с подписями из открытого документа.</summary>
public sealed class CategoryService
{
    private readonly Dictionary<string, CategoryInfo> _categories = new(StringComparer.Ordinal);

    public CategoryService(Document document)
    {
        foreach (Category category in document.Settings.Categories)
        {
            var id = RevitIds.Value(category.Id);
            if (!category.AllowsBoundParameters || id >= 0) continue;
            // Значение — само перечисление: с Revit 2024 его базовый тип long, до того — int, а IsDefined требует точный тип.
            var builtIn = (BuiltInCategory)id;
            if (!Enum.IsDefined(typeof(BuiltInCategory), builtIn)) continue;
            // Аналитические модели и служебное — не то, что проверяют на заполнение.
            if (category.CategoryType is not (CategoryType.Model or CategoryType.Annotation)) continue;

            var key = builtIn.ToString();
            _categories[key] = new CategoryInfo(key, category.Name, category.CategoryType == CategoryType.Model);
        }

        All = _categories.Values.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public IReadOnlyList<CategoryInfo> All { get; }

    /// <summary>Подпись категории; категории нет в этой версии Revit — её ключ.</summary>
    public string NameOf(string key) => _categories.TryGetValue(key, out var info) ? info.Name : key;
}
