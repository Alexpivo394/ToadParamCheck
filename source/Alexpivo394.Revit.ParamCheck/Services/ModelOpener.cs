using System.IO;
using Alexpivo394.Revit.ParamCheck.Core;
using Alexpivo394.Revit.ParamCheck.Profiles;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Nice3point.Revit.Toolkit;
using RevitApplication = Autodesk.Revit.ApplicationServices.Application;

namespace Alexpivo394.Revit.ParamCheck.Services;

/// <summary>
///     Открывает модель с отсоединением, не трогая исходник. С «Не загружать связи» модель сначала копируется
///     во временную папку (<c>CopyModel</c> умеет и Revit Server, и файл), а в копии всем связям RVT
///     ставится «не загружать» — модель открывается без связей, и закрывать ради этого их рабочие наборы не нужно.
/// </summary>
public sealed class ModelOpener(RevitApplication application)
{
    /// <param name="sourcePath">Путь к модели на Revit Server (<c>RSN://…</c>) или к файлу.</param>
    /// <exception cref="CheckException">Модель сохранена в версии Revit, в которой её здесь открывать нельзя.</exception>
    /// <exception cref="OperationCanceledException">Открытие попросили прервать.</exception>
    public Document Open(string sourcePath, CheckOptions options, TempWorkspace temp, ICheckProgress progress, ICheckLog log)
    {
        var path = sourcePath.Trim();
        var name = ModelNames.FromPath(path);
        var isServer = ModelNames.IsServerPath(path);
        var workshared = isServer; // на Revit Server лежат только модели совместной работы

        if (options.SkipLinks)
        {
            progress.Status("Копирование модели");
            log.Step(name, "копирование во временную папку");
            var copy = Path.Combine(temp.Directory, ModelNames.ToFileName(name) + ".rvt");
            using (new RevitProgressRelay(application, progress, "Копирование модели"))
            {
                application.CopyModel(ModelPathUtils.ConvertUserVisiblePathToModelPath(path), copy, true);
            }

            if (progress.ShouldAbort) throw new OperationCanceledException();

            log.Step(name, $"связей отключено: {UnloadLinks(copy)}");
            path = copy;
        }

        // Локальный файл есть — по нему узнаём совместную работу и версию. Путь Revit Server без копии так не прочитать:
        // версию там гарантирует сам сервер, он обслуживает одну версию Revit.
        if (!isServer || options.SkipLinks)
        {
            var info = BasicFileInfo.Extract(path);
            workshared = info.IsWorkshared;
            VersionGuard.EnsureCanOpen(name, ParseYear(info.Format), int.Parse(application.VersionNumber), options.AllowUpgrade);
        }

        var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(path);
        var openOptions = new OpenOptions();
        if (workshared)
        {
            openOptions.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;
            openOptions.SetOpenWorksetsConfiguration(Worksets(modelPath, options.WorksetKeyword));
        }

        var keyword = options.WorksetKeyword.Trim();
        progress.Status("Открытие модели");
        log.Step(name, keyword.Length > 0 ? $"открытие без РН с «{keyword}»" : "открытие");

        Document document;
        using (RevitApiContext.BeginFailureSuppressionScope())
        using (RevitContext.BeginDialogSuppressionScope(args => OnDialogBoxShowing(args, options.AllowUpgrade)))
        using (new RevitProgressRelay(application, progress, "Открытие модели"))
        {
            document = application.OpenDocumentFile(modelPath, openOptions)
                       ?? throw new InvalidOperationException("Revit не смог открыть модель");
        }

        // Revit не дал прервать открытие и дождался его конца — закрываем уже открытую модель.
        if (progress.ShouldAbort)
        {
            document.Close(false);
            throw new OperationCanceledException();
        }

        log.Step(name, "открыта");
        var links = new FilteredElementCollector(document).OfClass(typeof(RevitLinkType)).ToElementIds();
        if (links.Count > 0)
            log.Step(name, $"связей загружено: {links.Count(id => RevitLinkType.IsLoaded(document, id))} из {links.Count}");

        return document;
    }

    /// <summary>
    ///     Помечает в копии все связи RVT «не загружать». Revit учитывает это только у файла с пометкой «передан»,
    ///     поэтому копия помечается переданной — копия временная, сохранять её не нужно. Возвращает число связей.
    /// </summary>
    private static int UnloadLinks(string filePath)
    {
        var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(filePath);
        var data = TransmissionData.ReadTransmissionData(modelPath);
        if (data is null) return 0;

        var count = 0;
        foreach (var id in data.GetAllExternalFileReferenceIds())
        {
            var reference = data.GetLastSavedReferenceData(id);
            if (reference.ExternalFileReferenceType != ExternalFileReferenceType.RevitLink) continue;

            data.SetDesiredReferenceData(id, reference.GetPath(), reference.PathType, false);
            count++;
        }

        if (count == 0) return 0;

        data.IsTransmitted = true;
        TransmissionData.WriteTransmissionData(modelPath, data);
        return count;
    }

    private static int? ParseYear(string? format) => int.TryParse(format, out var year) ? year : null;

    private static WorksetConfiguration Worksets(ModelPath modelPath, string keyword)
    {
        keyword = keyword.Trim();
        if (keyword.Length == 0) return new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets);

        var open = WorksharingUtils.GetUserWorksetInfo(modelPath)
            .Where(workset => workset.Name.IndexOf(keyword, StringComparison.CurrentCultureIgnoreCase) < 0)
            .Select(workset => workset.Id)
            .ToList();

        var configuration = new WorksetConfiguration(WorksetConfigurationOption.CloseAllWorksets);
        configuration.Open(open);
        return configuration;
    }

    /// <summary>
    ///     Отсоединить — да, остальное — ответ по умолчанию. Обновить версию — да, только если обновление разрешено:
    ///     проверку версии до открытия Revit может и не пропустить к этому диалогу, это страховка.
    /// </summary>
    private static void OnDialogBoxShowing(DialogBoxShowingEventArgs args, bool allowUpgrade)
    {
        switch (args.DialogId)
        {
            case "Dialog_Revit_DocUpgrade":
                args.OverrideResult((int)(allowUpgrade ? TaskDialogResult.Ok : TaskDialogResult.Cancel));
                break;
            case "TaskDialog_DetachModel":
                args.OverrideResult((int)TaskDialogResult.Yes);
                break;
            default:
                args.OverrideResult(1001);
                break;
        }
    }
}
