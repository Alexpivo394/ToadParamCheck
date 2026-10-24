using Alexpivo394.Revit.ParamCheck.Profiles;

namespace Alexpivo394.Revit.ParamCheck.Core;

public static class ReportNames
{
    /// <summary>Имя отчёта по модели: <c>АР_К1_LOIReport.xlsx</c> — как всегда называл его ParamChecker.</summary>
    public static string ForModel(string modelName) => ModelNames.ToFileName(ModelNames.FromPath(modelName)) + "_LOIReport.xlsx";
}
