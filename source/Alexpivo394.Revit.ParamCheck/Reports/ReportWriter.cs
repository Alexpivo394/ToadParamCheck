using System.IO;
using ClosedXML.Excel;
using Alexpivo394.Revit.ParamCheck.Core;

namespace Alexpivo394.Revit.ParamCheck.Reports;

/// <summary>
///     Отчёт по модели: лист «Сводка» с итогами по правилам и параметрам и по листу на правило,
///     где у каждого элемента подсвечено, что заполнено, что пусто и какого параметра нет.
/// </summary>
public static class ReportWriter
{
    public const string SummarySheet = "Сводка";
    public const string MissingText = "нет параметра";

    /// <summary>Строк, по которым подбирается ширина колонок: на больших листах подбор по всем — минуты.</summary>
    private const int AutoFitRows = 500;

    private static readonly string[] ElementColumns = ["ID", "Категория", "Семейство", "Тип", "Рабочий набор"];

    // Стандартные цвета Excel «Хороший / Нейтральный / Плохой»: мягче чистых зелёного, жёлтого и красного.
    private static readonly Paint Filled = new(XLColor.FromArgb(0xC6, 0xEF, 0xCE), XLColor.FromArgb(0x00, 0x61, 0x00));
    private static readonly Paint Empty = new(XLColor.FromArgb(0xFF, 0xEB, 0x9C), XLColor.FromArgb(0x9C, 0x57, 0x00));
    private static readonly Paint Missing = new(XLColor.FromArgb(0xFF, 0xC7, 0xCE), XLColor.FromArgb(0x9C, 0x00, 0x06));
    private static readonly Paint HeaderPaint = new(XLColor.FromArgb(0x44, 0x54, 0x6A), XLColor.White);

    private static readonly XLColor Muted = XLColor.DimGray;

    /// <param name="report">Итог проверки модели.</param>
    /// <param name="filePath">Куда сохранить отчёт.</param>
    /// <param name="problemsOnly">В листах правил только элементы с незаполненными параметрами.</param>
    /// <returns>Путь, по которому отчёт сохранён: если файл занят (открыт в Excel), рядом с временем в имени.</returns>
    public static string Write(ModelReport report, string filePath, bool problemsOnly)
    {
        using var workbook = new XLWorkbook();

        var summary = workbook.Worksheets.Add(SummarySheet);
        var names = new SheetNames(SummarySheet);
        var sheets = report.Rules.Select((rule, i) => names.Next(rule.Title, i + 1)).ToList();

        var ruleSheets = new List<IXLWorksheet>(sheets.Count);
        for (var i = 0; i < report.Rules.Count; i++)
        {
            var sheet = workbook.Worksheets.Add(sheets[i]);
            WriteRule(sheet, report.Rules[i]);
            ruleSheets.Add(sheet);
        }

        WriteSummary(summary, report, ruleSheets, problemsOnly);

        return Save(workbook, filePath);
    }

    private static void WriteSummary(IXLWorksheet sheet, ModelReport report, IReadOnlyList<IXLWorksheet> sheets, bool problemsOnly)
    {
        sheet.Cell(1, 1).Value = $"Заполнение параметров — {report.ModelName}";
        sheet.Cell(1, 1).Style.Font.FontSize = 16;
        sheet.Cell(1, 1).Style.Font.Bold = true;

        var row = 3;
        void Info(string label, XLCellValue value)
        {
            sheet.Cell(row, 1).Value = label;
            sheet.Cell(row, 1).Style.Font.FontColor = Muted;
            sheet.Cell(row, 2).Value = value;
            row++;
        }

        Info("Модель", report.ModelName);
        Info("Путь", report.SourcePath);
        Info("Профиль", report.ProfileName);
        Info("Элементы", report.ViewName.Length == 0 ? "вся модель" : $"вид «{report.ViewName}»");
        Info("Проверено", report.CheckedAt);
        sheet.Cell(row - 1, 2).Style.NumberFormat.Format = "dd.mm.yyyy hh:mm";
        sheet.Cell(row - 1, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

        row++;
        var kpi = row;
        Kpi(sheet, row++, "Элементов проверено", report.Elements, null);
        Kpi(sheet, row++, "Заполнены полностью", report.CompleteElements, report.Completeness);
        Kpi(sheet, row++, "Значений проверено", report.Values, null);
        Kpi(sheet, row++, "Заполнено", report.Filled, report.FillRate);
        Kpi(sheet, row++, "Пусто", report.Empty, ModelReport.Ratio(report.Empty, report.Values));
        Kpi(sheet, row++, "Нет параметра", report.Missing, ModelReport.Ratio(report.Missing, report.Values));
        sheet.Range(kpi + 1, 1, kpi + 1, 3).Style.Font.Bold = true;
        sheet.Range(kpi + 3, 1, kpi + 3, 3).Style.Font.Bold = true;

        row++;
        Note(sheet, row++, "Полнота — доля элементов, у которых заполнены все параметры правила. " +
                           "Заполненность — доля заполненных значений среди всех проверенных (элементы × параметры).");
        if (problemsOnly)
            Note(sheet, row++, "В листах правил — только элементы с незаполненными параметрами; итоги — по всем элементам.");

        row++;
        sheet.Cell(row++, 1).Value = "По правилам";
        sheet.Cell(row - 1, 1).Style.Font.Bold = true;
        var rulesHeader = row;
        Header(sheet, row++, "Правило", "Элементов", "Заполнены полностью", "Полнота", "Значений", "Заполнено", "Пусто",
            "Нет параметра", "Заполненность");
        for (var i = 0; i < report.Rules.Count; i++, row++)
        {
            var rule = report.Rules[i];
            var title = sheet.Cell(row, 1);
            title.Value = DisplayTitle(rule, sheets[i].Name);
            title.SetHyperlink(new XLHyperlink(sheets[i].Cell(1, 1)));
            title.Style.Font.Underline = XLFontUnderlineValues.Single;
            title.Style.Font.FontColor = XLColor.FromArgb(0x05, 0x63, 0xC1);
            if (rule.Elements == 0)
            {
                // Пустое правило — не «0 % заполнения»: элементов под него в модели просто нет.
                sheet.Cell(row, 2).Value = "нет элементов";
                sheet.Cell(row, 2).Style.Font.FontColor = Muted;
                continue;
            }

            Numbers(sheet, row, 2, rule.Elements, rule.CompleteElements, rule.Completeness, rule.Values, rule.Filled, rule.Empty,
                rule.Missing, rule.FillRate);
        }

        Percent(sheet, rulesHeader + 1, row - 1, 4);
        Percent(sheet, rulesHeader + 1, row - 1, 9);

        row++;
        sheet.Cell(row++, 1).Value = "По параметрам";
        sheet.Cell(row - 1, 1).Style.Font.Bold = true;
        var parametersHeader = row;
        Header(sheet, row++, "Правило", "Параметр", "Элементов", "Заполнено", "Пусто", "Нет параметра", "Заполненность");
        for (var i = 0; i < report.Rules.Count; i++)
        {
            var rule = report.Rules[i];
            if (rule.Elements == 0) continue;

            foreach (var stats in rule.Stats)
            {
                sheet.Cell(row, 1).Value = DisplayTitle(rule, sheets[i].Name);
                sheet.Cell(row, 2).Value = stats.Name;
                Numbers(sheet, row, 3, stats.Total, stats.Filled, stats.Empty, stats.Missing, stats.FillRate);
                row++;
            }
        }

        Percent(sheet, parametersHeader + 1, row - 1, 7);

        row++;
        sheet.Cell(row, 1).Value = "Цвета в листах правил:";
        sheet.Cell(row, 2).Value = "заполнено";
        Filled.Apply(sheet.Cell(row, 2).Style);
        sheet.Cell(row, 3).Value = "пусто";
        Empty.Apply(sheet.Cell(row, 3).Style);
        sheet.Cell(row, 4).Value = MissingText;
        Missing.Apply(sheet.Cell(row, 4).Style);

        sheet.Column(1).Width = 34;
        sheet.Column(2).Width = 28;
        for (var column = 3; column <= 10; column++) sheet.Column(column).Width = 16;
        sheet.ShowGridLines = false;
    }

    private static string DisplayTitle(RuleReport rule, string sheet) => rule.Title.Trim().Length == 0 ? sheet : rule.Title.Trim();

    private static void Note(IXLWorksheet sheet, int row, string text)
    {
        sheet.Cell(row, 1).Value = text;
        sheet.Cell(row, 1).Style.Font.Italic = true;
        sheet.Cell(row, 1).Style.Font.FontColor = Muted;
    }

    private static void Kpi(IXLWorksheet sheet, int row, string label, int value, double? share)
    {
        sheet.Cell(row, 1).Value = label;
        sheet.Cell(row, 2).Value = value;
        sheet.Cell(row, 2).Style.NumberFormat.Format = "# ##0";
        sheet.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        if (share is null) return;

        sheet.Cell(row, 3).Value = share.Value;
        sheet.Cell(row, 3).Style.NumberFormat.Format = "0.0%";
    }

    private static void Header(IXLWorksheet sheet, int row, params string[] titles)
    {
        for (var i = 0; i < titles.Length; i++) sheet.Cell(row, i + 1).Value = titles[i];

        var style = sheet.Range(row, 1, row, titles.Length).Style;
        HeaderPaint.Apply(style);
        style.Font.Bold = true;
        style.Alignment.WrapText = true;
        style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    }

    /// <summary>Счётчики и доли подряд: целое — с разделителем тысяч, доля — в процентах, <c>null</c> — пустая ячейка.</summary>
    private static void Cells(IXLWorksheet sheet, int row, int column, params double?[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            var cell = sheet.Cell(row, column + i);
            if (values[i] is { } value) cell.Value = value;
            cell.Style.NumberFormat.Format = "# ##0";
        }
    }

    private static void Numbers(IXLWorksheet sheet, int row, int column, int elements, int complete, double? completeness, int values, int filled,
        int empty, int missing, double? fillRate)
    {
        Cells(sheet, row, column, elements, complete, null, values, filled, empty, missing, null);
        Share(sheet.Cell(row, column + 2), completeness);
        Share(sheet.Cell(row, column + 7), fillRate);
    }

    private static void Numbers(IXLWorksheet sheet, int row, int column, int total, int filled, int empty, int missing, double? fillRate)
    {
        Cells(sheet, row, column, total, filled, empty, missing, null);
        Share(sheet.Cell(row, column + 4), fillRate);
    }

    private static void Share(IXLCell cell, double? share)
    {
        if (share is { } value) cell.Value = value;
        cell.Style.NumberFormat.Format = share is null ? "# ##0" : "0.0%";
    }

    /// <summary>Гистограмма в ячейках доли — видно, где провал, не вчитываясь в цифры.</summary>
    private static void Percent(IXLWorksheet sheet, int fromRow, int toRow, int column)
    {
        if (toRow < fromRow) return;

        sheet.Range(fromRow, column, toRow, column).AddConditionalFormat()
            .DataBar(XLColor.FromArgb(0x63, 0xBE, 0x7B))
            .Minimum(XLCFContentType.Number, 0)
            .Maximum(XLCFContentType.Number, 1);
    }

    private static void WriteRule(IXLWorksheet sheet, RuleReport rule)
    {
        var columns = ElementColumns.Concat(rule.Parameters).Append("Заполнено").ToArray();
        Header(sheet, 1, columns);

        var row = 2;
        foreach (var element in rule.Rows)
        {
            sheet.Cell(row, 1).Value = element.Id;
            sheet.Cell(row, 2).Value = element.Category;
            sheet.Cell(row, 3).Value = element.Family;
            sheet.Cell(row, 4).Value = element.Type;
            sheet.Cell(row, 5).Value = element.Workset;

            var filled = 0;
            for (var i = 0; i < element.Values.Count; i++)
            {
                var value = element.Values[i];
                var cell = sheet.Cell(row, ElementColumns.Length + 1 + i);
                switch (value.Status)
                {
                    case CellStatus.Filled:
                        cell.Value = value.Text;
                        filled++;
                        break;
                    case CellStatus.Empty:
                        break;
                    default:
                        cell.Value = MissingText;
                        break;
                }
            }

            sheet.Cell(row, columns.Length).Value = element.Values.Count == 0 ? 1d : (double)filled / element.Values.Count;
            row++;
        }

        var last = Math.Max(row - 1, 1);
        if (row > 2)
        {
            PaintStatuses(sheet, rule);
            sheet.Range(2, columns.Length, last, columns.Length).Style.NumberFormat.Format = "0%";
        }

        sheet.Range(1, 1, last, columns.Length).SetAutoFilter();
        sheet.SheetView.Freeze(1, ElementColumns.Length);
        sheet.Columns(1, columns.Length).AdjustToContents(1, Math.Min(last, AutoFitRows), 8, 50);
    }

    /// <summary>
    ///     Цвет по состоянию значения. Красится не каждая ячейка, а подряд идущие в столбце ячейки одного состояния —
    ///     на листе в сотни тысяч ячеек это в разы быстрее.
    /// </summary>
    private static void PaintStatuses(IXLWorksheet sheet, RuleReport rule)
    {
        for (var i = 0; i < rule.Parameters.Count; i++)
        {
            var column = ElementColumns.Length + 1 + i;
            var start = 0;
            while (start < rule.Rows.Count)
            {
                var status = Status(rule.Rows[start], i);
                var end = start;
                while (end + 1 < rule.Rows.Count && Status(rule.Rows[end + 1], i) == status) end++;

                var paint = status switch
                {
                    CellStatus.Filled => Filled,
                    CellStatus.Empty => Empty,
                    CellStatus.Missing => Missing,
                    _ => null
                };
                paint?.Apply(sheet.Range(start + 2, column, end + 2, column).Style);
                start = end + 1;
            }
        }
    }

    /// <summary>Состояние значения в строке; у строки короче списка параметров значения нет — ячейка не красится.</summary>
    private static CellStatus? Status(ElementRow row, int index) => index < row.Values.Count ? row.Values[index].Status : null;

    private static string Save(XLWorkbook workbook, string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        try
        {
            workbook.SaveAs(filePath);
            return filePath;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            // Чаще всего прошлый отчёт открыт в Excel — не теряем новый, кладём рядом.
            var alternative = Path.Combine(Path.GetDirectoryName(filePath)!,
                $"{Path.GetFileNameWithoutExtension(filePath)} ({DateTime.Now:HH-mm-ss}){Path.GetExtension(filePath)}");
            workbook.SaveAs(alternative);
            return alternative;
        }
    }

    /// <summary>Заливка и цвет текста.</summary>
    private sealed record Paint(XLColor Background, XLColor Text)
    {
        public void Apply(IXLStyle style)
        {
            style.Fill.BackgroundColor = Background;
            style.Font.FontColor = Text;
        }
    }
}
