using Alexpivo394.Revit.ParamCheck.Summaries;

namespace Alexpivo394.Revit.ParamCheck.Core;

public static class CheckSummaries
{
    /// <summary>Итог проверенной модели для аналитики: счётчики модели, правил и параметров, без строк элементов.</summary>
    /// <param name="outcome">Проверенная модель — с <see cref="ModelOutcome.Report" />.</param>
    /// <param name="profileFingerprint">Отпечаток правил профиля, по которому проверяли.</param>
    /// <exception cref="ArgumentException">Модель не проверена — итога нет.</exception>
    public static CheckSummary From(ModelOutcome outcome, string profileFingerprint)
    {
        var report = outcome.Report ?? throw new ArgumentException("Модель не проверена — итога нет", nameof(outcome));
        return new CheckSummary
        {
            ModelName = outcome.ModelName,
            ModelPath = outcome.SourcePath,
            ProfileName = outcome.ProfileName,
            ProfileFingerprint = profileFingerprint,
            ViewName = report.ViewName,
            CheckedAt = new DateTimeOffset(report.CheckedAt),
            DurationSeconds = Math.Round(outcome.Duration.TotalSeconds, 1),
            ReportPath = outcome.ReportPath,
            Elements = report.Elements,
            CompleteElements = report.CompleteElements,
            Values = report.Values,
            Filled = report.Filled,
            Empty = report.Empty,
            Missing = report.Missing,
            Rules = report.Rules.Select(rule => new RuleSummary
            {
                Title = rule.Title,
                Elements = rule.Elements,
                CompleteElements = rule.CompleteElements,
                Parameters = rule.Stats.Select(stats => new ParameterSummary
                {
                    Name = stats.Name,
                    Filled = stats.Filled,
                    Empty = stats.Empty,
                    Missing = stats.Missing
                }).ToList()
            }).ToList()
        };
    }
}
