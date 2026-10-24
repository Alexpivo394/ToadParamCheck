using Alexpivo394.Revit.ParamCheck.Core;
using Alexpivo394.Revit.ParamCheck.Profiles;
using Shouldly;

namespace Alexpivo394.Revit.ParamCheck.Tests;

/// <summary>То, что нужно сервису ToadFlow: профиль без моделей хранится и передаётся отдельно от файла профилей.</summary>
public class ServiceProfileTests
{
    private static CheckProfile Profile() => new()
    {
        Name = "ОВ",
        ViewName = "Navisworks",
        Rules =
        [
            new CheckRule
            {
                Title = "Воздуховоды",
                Filter = new RuleFilter
                {
                    Categories = ["OST_DuctCurves"],
                    Logic = ConditionLogic.Or,
                    Conditions = [Condition.Simple("Система", ConditionOperator.StartsWith, "П"), Condition.AllOf(Condition.Simple("Длина", ConditionOperator.GreaterThan, "100"))]
                },
                Parameters = ["Марка", " Марка ", "Код"]
            }
        ]
    };

    [Test]
    public void SingleProfile_RoundTrips()
    {
        var json = ProfileSerializer.SerializeProfile(Profile());
        json.ShouldContain("\"StartsWith\"");

        var restored = ProfileSerializer.DeserializeProfile(json);
        restored.Name.ShouldBe("ОВ");
        restored.ViewName.ShouldBe("Navisworks");
        restored.Models.ShouldBeEmpty();
        restored.Rules.ShouldHaveSingleItem().Parameters.ShouldBe(["Марка", "Код"]);
        restored.Rules[0].Filter.Conditions[1].Group.ShouldNotBeNull().ShouldHaveSingleItem().Operator.ShouldBe(ConditionOperator.GreaterThan);
    }

    [Test]
    public void SingleProfile_RejectsOtherJson()
    {
        Should.Throw<InvalidDataException>(() => ProfileSerializer.DeserializeProfile("[1, 2]"));
        Should.Throw<InvalidDataException>(() => ProfileSerializer.DeserializeProfile("{ не json"));
    }

    [Test]
    public void SingleProfile_NormalizesBrokenGroups()
    {
        var restored = ProfileSerializer.DeserializeProfile("""{ "Name": null, "Rules": [ { "Filter": { "Categories": ["", "OST_Walls", "OST_Walls"], "Conditions": [ { "Group": [ null ] } ] } } ] }""");

        restored.Name.ShouldBe("");
        restored.Rules[0].Title.ShouldBe("");
        restored.Rules[0].Filter.Categories.ShouldBe(["OST_Walls"]);
        restored.Rules[0].Filter.Conditions[0].Group.ShouldBeEmpty();
    }

    [Test]
    public void ProfilesFile_WithoutRevit_DropsNumericCategories()
    {
        var set = ProfileSerializer.Deserialize("""[ { "ProfileName": "ВК", "Rules": [ { "Title": "Трубы", "FilterConfigJson": "{\"SelectedCategories\":[-2008044]}" } ] } ]""", _ => null);

        set.Profiles.ShouldHaveSingleItem().Rules.ShouldHaveSingleItem().Filter.Categories.ShouldBeEmpty();
    }

    [Test]
    public void RuleProblems_IgnoreModels()
    {
        RunValidator.RuleProblems(Profile()).ShouldBeEmpty();
        RunValidator.Problems([Profile()], needsModels: false).ShouldBeEmpty();
        RunValidator.Problems([Profile()]).ShouldContain(p => p.Contains("нет моделей"));
    }

    [Test]
    [Arguments("АР_К1.rvt", "АР_К1_LOIReport.xlsx")]
    [Arguments("RSN://rs/ЖК/АР_К1.rvt", "АР_К1_LOIReport.xlsx")]
    [Arguments(@"\\nas\ЖК\АР:К1.rvt", "АР_К1_LOIReport.xlsx")]
    public void ReportName(string model, string report) => ReportNames.ForModel(model).ShouldBe(report);

    [Test]
    public void VersionGuard_RefusesUpgradeWhenForbidden()
    {
        Should.NotThrow(() => VersionGuard.EnsureCanOpen("Дом", null, 2024, false));
        Should.NotThrow(() => VersionGuard.EnsureCanOpen("Дом", 2024, 2024, false));
        Should.NotThrow(() => VersionGuard.EnsureCanOpen("Дом", 2022, 2024, true));
        Should.Throw<CheckException>(() => VersionGuard.EnsureCanOpen("Дом", 2022, 2024, false)).Message.ShouldContain("обновление запрещено");
        Should.Throw<CheckException>(() => VersionGuard.EnsureCanOpen("Дом", 2025, 2024, true)).Message.ShouldContain("не открыть");
    }
}
