using System.IO;
using ClosedXML.Excel;
using Alexpivo394.Revit.ParamCheck.Core;

namespace Alexpivo394.Revit.ParamCheck.Reports;

/// <summary>
///     База для дашбордов (Power BI, Excel): один файл, в который каждый запуск дописывает строки. Три таблицы Excel
///     в «длинном» формате — каждая строка одно измерение, поэтому история копится, а срезы строятся без формул:
///     <list type="bullet">
///         <item><c>Models</c> — модель в запуске: статус, итоги;</item>
///         <item><c>Rules</c> — правило модели в запуске;</item>
///         <item><c>Parameters</c> — параметр правила модели в запуске.</item>
///     </list>
///     Все таблицы связываются по «Запуск» + «Профиль» + «Модель». Последний запуск модели — максимум «Запуск».
///     <para>
///         Книга при каждой записи читается и сохраняется целиком: в ней остаются листы, данные и таблицы, но не
///         диаграммы, добавленные вручную. Дашборды строятся в отдельном файле, который берёт данные из базы.
///     </para>
/// </summary>
public static class HistoryWriter
{
    private static readonly TableSpec Models = new("Models", "Модели",
        ["Запуск", "Профиль", "Модель", "Путь", "Статус", "Ошибка", "Длительность, с", "Элементов", "Заполнены полностью",
            "Полнота", "Значений", "Заполнено", "Пусто", "Нет параметра", "Заполненность"]);

    private static readonly TableSpec Rules = new("Rules", "Правила",
        ["Запуск", "Профиль", "Модель", "Правило", "Элементов", "Заполнены полностью", "Полнота", "Значений", "Заполнено",
            "Пусто", "Нет параметра", "Заполненность"]);

    private static readonly TableSpec Parameters = new("Parameters", "Параметры",
        ["Запуск", "Профиль", "Модель", "Правило", "Параметр", "Элементов", "Заполнено", "Пусто", "Нет параметра", "Заполненность"]);

    private static readonly TimeSpan LockRetry = TimeSpan.FromMilliseconds(500);

    /// <summary>Дописывает модели запуска в базу; файла нет — создаёт.</summary>
    /// <param name="run">Начало запуска — общее для всех его строк.</param>
    /// <exception cref="IOException">Файл занят, например открыт в Excel.</exception>
    public static void Append(string filePath, DateTime run, IReadOnlyList<ModelOutcome> outcomes)
    {
        if (outcomes.Count == 0) return;

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        using var workbook = File.Exists(filePath) ? new XLWorkbook(filePath) : new XLWorkbook();

        var models = new List<XLCellValue[]>();
        var rules = new List<XLCellValue[]>();
        var parameters = new List<XLCellValue[]>();
        foreach (var outcome in outcomes)
        {
            var report = outcome.Report;
            models.Add([
                run, outcome.ProfileName, outcome.ModelName, outcome.SourcePath, StatusText(outcome.Status), outcome.Error,
                Math.Round(outcome.Duration.TotalSeconds), report?.Elements, report?.CompleteElements, report?.Completeness,
                report?.Values, report?.Filled, report?.Empty, report?.Missing, report?.FillRate
            ]);
            if (report is null) continue;

            foreach (var rule in report.Rules)
            {
                rules.Add([
                    run, outcome.ProfileName, outcome.ModelName, rule.Title, rule.Elements, rule.CompleteElements,
                    rule.Completeness, rule.Values, rule.Filled, rule.Empty, rule.Missing, rule.FillRate
                ]);
                parameters.AddRange(rule.Stats.Select(stats => new XLCellValue[]
                {
                    run, outcome.ProfileName, outcome.ModelName, rule.Title, stats.Name, stats.Total, stats.Filled,
                    stats.Empty, stats.Missing, stats.FillRate
                }));
            }
        }

        AppendRows(workbook, Models, models);
        AppendRows(workbook, Rules, rules);
        AppendRows(workbook, Parameters, parameters);
        workbook.SaveAs(filePath);
    }

    /// <summary>
    ///     <see cref="Append" /> под замком: в одну базу пишут несколько экземпляров Revit, в том числе на разных машинах.
    ///     Замок — файл <c>база.xlsx.lock</c> рядом, открытый без совместного доступа; он удаляется, когда запись закончена.
    /// </summary>
    /// <param name="wait">Сколько ждать, пока базу дописывает кто-то другой.</param>
    /// <exception cref="IOException">База занята дольше <paramref name="wait" /> или открыта в Excel.</exception>
    public static void AppendShared(string filePath, DateTime run, IReadOnlyList<ModelOutcome> outcomes, TimeSpan wait)
    {
        if (outcomes.Count == 0) return;

        var full = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using (AcquireLock(full + ".lock", wait)) Append(full, run, outcomes);
    }

    private static FileStream AcquireLock(string lockPath, TimeSpan wait)
    {
        var until = DateTime.UtcNow + wait;
        while (true)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            // Пока прошлый владелец удаляет замок, Windows отвечает «нет доступа», а не «занят».
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && DateTime.UtcNow < until)
            {
                Thread.Sleep(LockRetry);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new IOException($"база занята другой проверкой дольше {wait.TotalSeconds:0} с", exception);
            }
        }
    }

    public static string StatusText(ModelStatus status) => status switch
    {
        ModelStatus.Checked => "Проверена",
        ModelStatus.Skipped => "Пропущена",
        _ => "Ошибка"
    };

    private static void AppendRows(XLWorkbook workbook, TableSpec spec, IReadOnlyList<XLCellValue[]> rows)
    {
        var sheet = workbook.Worksheets.TryGetWorksheet(spec.Sheet, out var existing) ? existing : workbook.Worksheets.Add(spec.Sheet);
        var table = sheet.Tables.FirstOrDefault(t => string.Equals(t.Name, spec.Table, StringComparison.OrdinalIgnoreCase)) ?? CreateTable(sheet, spec);
        if (rows.Count == 0) return;

        var top = table.RangeAddress.FirstAddress;
        var bottom = table.RangeAddress.LastAddress;
        var column0 = top.ColumnNumber;

        // Пустая таблица хранит одну пустую строку данных — её занимает первая новая строка.
        var empty = bottom.RowNumber - top.RowNumber == 1 && sheet.Cell(bottom.RowNumber, column0).IsEmpty();
        var first = empty ? bottom.RowNumber : bottom.RowNumber + 1;
        var last = first + rows.Count - 1;

        for (var r = 0; r < rows.Count; r++)
        for (var c = 0; c < rows[r].Length; c++)
            sheet.Cell(first + r, column0 + c).Value = rows[r][c];

        table.Resize(sheet.Range(top.RowNumber, column0, last, bottom.ColumnNumber));

        for (var column = 0; column < spec.Columns.Length; column++)
        {
            var format = Format(spec.Columns[column]);
            if (format is not null) sheet.Range(first, column0 + column, last, column0 + column).Style.NumberFormat.Format = format;
        }
    }

    private static IXLTable CreateTable(IXLWorksheet sheet, TableSpec spec)
    {
        for (var i = 0; i < spec.Columns.Length; i++) sheet.Cell(1, i + 1).Value = spec.Columns[i];
        var table = sheet.Range(1, 1, 2, spec.Columns.Length).CreateTable(spec.Table);
        table.Theme = XLTableTheme.TableStyleMedium2;
        sheet.SheetView.FreezeRows(1);
        for (var i = 1; i <= spec.Columns.Length; i++) sheet.Column(i).Width = spec.Columns[i - 1] is "Путь" or "Ошибка" ? 50 : 18;
        return table;
    }

    private static string? Format(string column) => column switch
    {
        "Запуск" => "dd.mm.yyyy hh:mm",
        "Полнота" or "Заполненность" => "0.0%",
        _ => null
    };

    private sealed record TableSpec(string Table, string Sheet, string[] Columns);
}
