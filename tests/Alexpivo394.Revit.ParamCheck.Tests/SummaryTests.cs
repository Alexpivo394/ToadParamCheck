using System.Text.Json;
using Alexpivo394.Revit.ParamCheck.Core;
using Alexpivo394.Revit.ParamCheck.Profiles;
using Alexpivo394.Revit.ParamCheck.Summaries;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Shouldly;

namespace Alexpivo394.Revit.ParamCheck.Tests;

public class SummaryTests
{
    private static ModelOutcome Checked()
    {
        var report = new ModelReport { ProfileName = "ОВ", ModelName = "POL_OVV_RLP_S3_R22", SourcePath = "RSN://rs/POL/POL_OVV_RLP_S3_R22.rvt", CheckedAt = DateTime.Now };
        var rule = new RuleReport("Воздуховоды", ["Марка", "Код"]);
        rule.Count([new ParamValue(true, "В1"), new ParamValue(true, "К")]);
        rule.Count([new ParamValue(true, ""), ParamValue.Missing]);
        report.Rules.Add(rule);
        report.Rules.Add(new RuleReport("Пустое", ["Марка"]));
        return new ModelOutcome("ОВ", report.ModelName, report.SourcePath, ModelStatus.Checked, TimeSpan.FromSeconds(83.27), report,
            ReportPath: @"\\nas\loi\POL_OVV_RLP_S3_R22_LOIReport.xlsx");
    }

    [Test]
    public void FromOutcome_CountsModelRulesAndParameters()
    {
        var summary = CheckSummaries.From(Checked(), "abc");

        summary.ModelName.ShouldBe("POL_OVV_RLP_S3_R22");
        summary.ProfileFingerprint.ShouldBe("abc");
        summary.DurationSeconds.ShouldBe(83.3);
        summary.Elements.ShouldBe(2);
        summary.CompleteElements.ShouldBe(1);
        summary.Values.ShouldBe(4);
        summary.FillRate.ShouldBe(0.5);
        summary.Rules[0].Parameters.Select(p => (p.Name, p.Filled, p.Empty, p.Missing)).ShouldBe([("Марка", 1, 1, 0), ("Код", 1, 0, 1)]);
        summary.Rules[1].Completeness.ShouldBeNull(); // элементов нет — не «0 %»
    }

    [Test]
    public void NotChecked_HasNoSummary() =>
        Should.Throw<ArgumentException>(() => CheckSummaries.From(Checked() with { Status = ModelStatus.Failed, Report = null }, "abc"));

    /// <summary>Плагин пишет итог через Newtonsoft, сервер читает System.Text.Json — счётчики доходят, доли считаются заново.</summary>
    [Test]
    public void Json_FromNewtonsoftToSystemTextJson()
    {
        var json = JsonConvert.SerializeObject(CheckSummaries.From(Checked(), "abc"),
            new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() });

        var read = System.Text.Json.JsonSerializer.Deserialize<CheckSummary>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        json.ShouldContain("\"fillRate\":0.5");
        read.Rules[0].Parameters[1].Missing.ShouldBe(1);
        read.CheckedAt.ShouldBe(CheckSummaries.From(Checked(), "abc").CheckedAt, TimeSpan.FromSeconds(5));
        read.FillRate.ShouldBe(0.5);
    }

    [Test]
    public void Fingerprint_ChangesOnlyWithRules()
    {
        CheckProfile Profile(string name, string parameter) => new()
        {
            Name = name,
            ViewName = name,
            Models = [new ModelSource { Path = name }],
            Rules = [new CheckRule { Title = "Стены", Filter = new RuleFilter { Categories = ["OST_Walls"] }, Parameters = [parameter] }]
        };

        var fingerprint = ProfileSerializer.Fingerprint(Profile("ОВ", "Марка"));

        fingerprint.Length.ShouldBe(16);
        ProfileSerializer.Fingerprint(Profile("ВК", "Марка")).ShouldBe(fingerprint);
        ProfileSerializer.Fingerprint(Profile("ОВ", "Код")).ShouldNotBe(fingerprint);
        ProfileSerializer.Fingerprint(ProfileSerializer.DeserializeProfile(ProfileSerializer.SerializeProfile(Profile("ОВ", "Марка")))).ShouldBe(fingerprint);
    }
}
