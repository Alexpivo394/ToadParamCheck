using Autodesk.Revit.DB;
using RevitApplication = Autodesk.Revit.ApplicationServices.Application;

namespace Alexpivo394.Revit.ParamCheck.Services;

/// <param name="Categories">Категории, к которым можно привязать параметры, с подписями на языке Revit.</param>
/// <param name="Parameters">Имена параметров для подсказок: ФОП, системные, проекта и семейств.</param>
/// <param name="HasSharedParameterFile">У этого Revit подключён ФОП — без него неизвестны все ADSK-параметры.</param>
public sealed record CatalogSnapshot(IReadOnlyList<CategoryInfo> Categories, IReadOnlyList<string> Parameters, bool HasSharedParameterFile);

/// <summary>
///     Категории и имена параметров для редактора правил там, где Revit нет под рукой (веб-интерфейс ToadFlow):
///     их снимает Revit на машине агента.
/// </summary>
public static class RevitCatalog
{
    /// <summary>Без открытого проекта: создаёт пустой проект в памяти, читает его и закрывает.</summary>
    public static CatalogSnapshot Read(RevitApplication application)
    {
        var document = application.NewProjectDocument(UnitSystem.Metric);
        try
        {
            return Read(application, document);
        }
        finally
        {
            document.Close(false);
        }
    }

    public static CatalogSnapshot Read(RevitApplication application, Document document)
    {
        var parameters = ParameterCatalog.Build(application, document);
        return new CatalogSnapshot(new CategoryService(document).All, parameters.Names, parameters.HasSharedParameterFile);
    }
}
