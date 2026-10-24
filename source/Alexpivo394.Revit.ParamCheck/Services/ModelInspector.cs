using System.Diagnostics;
using Alexpivo394.Revit.ParamCheck.Core;
using Alexpivo394.Revit.ParamCheck.Profiles;
using Alexpivo394.Revit.ParamCheck.Reports;
using Autodesk.Revit.DB;
using RevitApplication = Autodesk.Revit.ApplicationServices.Application;

namespace Alexpivo394.Revit.ParamCheck.Services;

/// <summary>
///     Проверяет одну модель: открыть → проверить правила профиля → отчёт Excel → закрыть. Ошибка модели не бросается,
///     а возвращается в <see cref="ModelOutcome" />; просьба прервать (<see cref="ICheckProgress.ShouldAbort" />) закрывает
///     модель без отчёта. Очередь моделей, окно прогресса и база для дашбордов — забота вызывающего.
/// </summary>
public sealed class ModelInspector(RevitApplication application)
{
    private readonly ModelOpener _opener = new(application);

    /// <param name="sourcePath">Путь к модели на Revit Server (<c>RSN://…</c>) или к файлу.</param>
    /// <param name="reportPath">Куда сохранить отчёт; если файл занят (открыт в Excel), отчёт ляжет рядом с временем в имени.</param>
    public ModelOutcome Check(string sourcePath, CheckProfile profile, CheckOptions options, string reportPath, ICheckProgress progress, ICheckLog log)
    {
        var path = sourcePath.Trim();
        var name = ModelNames.FromPath(path);
        var stopwatch = Stopwatch.StartNew();
        TempWorkspace.CleanupUnused();

        // Копия модели без связей удаляется, когда документ уже закрыт.
        using var temp = TempWorkspace.Create();
        Document? document = null;
        try
        {
            document = _opener.Open(path, options, temp, progress, log);
            return CheckOpen(document, profile, name, path, options, reportPath, progress, log, stopwatch);
        }
        catch (Exception exception) when (progress.ShouldAbort || exception is OperationCanceledException)
        {
            // Revit прервал открытие по нашей просьбе или модель бросили посреди проверки — это решение пользователя.
            log.Step(name, "проверка прервана");
            return new ModelOutcome(profile.Name, name, path, ModelStatus.Skipped, stopwatch.Elapsed);
        }
        catch (CheckException exception)
        {
            log.Step(name, $"не проверена: {exception.Message}");
            return new ModelOutcome(profile.Name, name, path, ModelStatus.Failed, stopwatch.Elapsed, Error: exception.Message);
        }
        catch (Exception exception)
        {
            log.Error(name, "проверка не выполнена", exception);
            return new ModelOutcome(profile.Name, name, path, ModelStatus.Failed, stopwatch.Elapsed, Error: exception.Message,
                RevitFailure: exception is Autodesk.Revit.Exceptions.InternalException);
        }
        finally
        {
            Close(document, name, progress, log);
        }
    }

    /// <summary>Проверяет уже открытую модель и сохраняет отчёт; модель остаётся открытой.</summary>
    /// <param name="stopwatch">Отсчёт длительности, если модель открывал вызывающий; null — от начала проверки.</param>
    /// <exception cref="CheckException">В модели нет вида из настроек.</exception>
    /// <exception cref="OperationCanceledException">Проверку попросили прервать.</exception>
    public static ModelOutcome CheckOpen(Document document, CheckProfile profile, string modelName, string sourcePath, CheckOptions options,
        string reportPath, ICheckProgress progress, ICheckLog log, Stopwatch? stopwatch = null)
    {
        stopwatch ??= Stopwatch.StartNew();
        var report = ModelChecker.Check(document, profile, modelName, sourcePath, options.ViewName, options.ProblemsOnly, progress, log);

        progress.Status("Сохранение отчёта");
        var saved = ReportWriter.Write(report, reportPath, options.ProblemsOnly);
        log.Step(modelName, $"готово за {stopwatch.Elapsed:mm\\:ss}: полнота {ModelChecker.Percent(report.Completeness)}, " +
                            $"заполненность {ModelChecker.Percent(report.FillRate)}; отчёт {saved}" +
                            (saved == reportPath ? "" : " (прежний отчёт занят — открыт в Excel?)"));

        return new ModelOutcome(profile.Name, modelName, sourcePath, ModelStatus.Checked, stopwatch.Elapsed, report, ReportPath: saved);
    }

    private static void Close(Document? document, string name, ICheckProgress progress, ICheckLog log)
    {
        if (document is null || !document.IsValidObject) return;

        progress.Status("Закрытие модели");
        try
        {
            document.Close(false);
        }
        catch (Exception exception)
        {
            log.Error(name, "модель не закрылась", exception);
        }
    }
}
