using Alexpivo394.Revit.ParamCheck.Core;
using Alexpivo394.Revit.ParamCheck.Reports;
using OfficeOpenXml;
using Shouldly;

namespace Alexpivo394.Revit.ParamCheck.Tests;

public class SharedHistoryTests
{
    private static ModelOutcome Outcome(string model) =>
        new("ОВ", model, $"RSN://rs/{model}.rvt", ModelStatus.Failed, TimeSpan.FromSeconds(1), Error: "нет вида");

    [Test]
    public async Task ParallelWriters_LoseNoRows_AndLeaveNoLock()
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"pc_{Guid.NewGuid():N}")).FullName;
        var path = Path.Combine(folder, "База.xlsx");
        try
        {
            var run = new DateTime(2026, 10, 1, 9, 0, 0);
            await Task.WhenAll(Enumerable.Range(1, 6).Select(i =>
                Task.Run(() => HistoryWriter.AppendShared(path, run, [Outcome($"Модель_{i}")], TimeSpan.FromMinutes(1)))));

            using (var package = new ExcelPackage(new FileInfo(path)))
                package.Workbook.Worksheets["Модели"].Tables["Models"].Address.Rows.ShouldBe(1 + 6);
            File.Exists(path + ".lock").ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Test]
    public void BusyLock_FailsAfterWait()
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"pc_{Guid.NewGuid():N}")).FullName;
        var path = Path.Combine(folder, "База.xlsx");
        try
        {
            using (new FileStream(path + ".lock", FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                Should.Throw<IOException>(() => HistoryWriter.AppendShared(path, DateTime.Now, [Outcome("Дом")], TimeSpan.FromSeconds(1)))
                    .Message.ShouldContain("занята");
            }

            File.Exists(path).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
