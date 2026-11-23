using System.Security.Cryptography;
using System.Text;
using Alexpivo394.Revit.ParamCheck.Core;
using Alexpivo394.Revit.ParamCheck.Profiles;
using Alexpivo394.Revit.ParamCheck.Reports;
using ClosedXML.Excel;
using Newtonsoft.Json.Converters;
using Shouldly;

namespace Alexpivo394.Revit.ParamCheck.Tests;

/// <summary>
///     То, что версии до 1.1 писали через Newtonsoft.Json и EPPlus, остаётся в силе: отпечаток профиля тот же байт в
///     байт, файлы профилей читаются в обе стороны, база для дашбордов дописывается.
/// </summary>
public class CompatibilityTests
{
    /// <summary>Отпечаток, как его считали версии до 1.1.</summary>
    private static string NewtonsoftFingerprint(CheckProfile profile)
    {
        var rules = Newtonsoft.Json.JsonConvert.SerializeObject(profile.Rules, Newtonsoft.Json.Formatting.None, new Newtonsoft.Json.JsonSerializerSettings
        {
            Converters = [new StringEnumConverter()],
            NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
            // Вычисляемые свойства модели раньше прятал атрибут JsonIgnore; здесь — тот же набор полей.
            ContractResolver = new StoredOnlyResolver()
        });
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(rules)).Take(8).Select(b => b.ToString("x2")));
    }

    private sealed class StoredOnlyResolver : Newtonsoft.Json.Serialization.DefaultContractResolver
    {
        protected override IList<Newtonsoft.Json.Serialization.JsonProperty> CreateProperties(Type type, Newtonsoft.Json.MemberSerialization memberSerialization) =>
            base.CreateProperties(type, memberSerialization).Where(property => property.Writable).ToList();
    }

    private static CheckProfile Sample() => new()
    {
        Name = "ОВ",
        Rules =
        [
            new CheckRule
            {
                Title = "Воздуховоды «П1»",
                Parameters = ["ADSK_Марка", "ADSK_Код изделия"],
                Filter = new RuleFilter
                {
                    Categories = ["OST_DuctCurves", "OST_FlexDuctCurves"],
                    Mode = SelectionMode.CategoriesOrParameters,
                    Logic = ConditionLogic.Or,
                    Conditions =
                    [
                        Condition.Simple("ADSK_Наименование", ConditionOperator.Contains, "Воздуховод \"круглый\" 100\\200"),
                        Condition.AllOf(Condition.Simple("Имя системы", ConditionOperator.StartsWith, "П1"), Condition.Simple("Комментарии", ConditionOperator.IsEmpty)),
                        new Condition { Parameter = "Без оператора" },
                        new Condition { Group = [] }
                    ]
                }
            },
            new CheckRule { Title = "", Filter = new RuleFilter(), Parameters = [] }
        ]
    };

    /// <summary>Отпечаток — первые 8 байт SHA-256 от записи правил без отступов; запись закреплена здесь буквально.</summary>
    [Test]
    public void Fingerprint_SameAsBefore()
    {
        ProfileSerializer.Fingerprint(Sample()).ShouldBe(NewtonsoftFingerprint(Sample()));

        using var sha = SHA256.Create();
        var canonical = """[{"Title":"Стены","Filter":{"Categories":["OST_Walls"],"Mode":"CategoriesAndParameters","Logic":"And","Conditions":[]},"Parameters":["Марка"]}]""";
        ProfileSerializer.Fingerprint(new CheckProfile { Rules = [new CheckRule { Title = "Стены", Filter = new RuleFilter { Categories = ["OST_Walls"] }, Parameters = ["Марка"] }] })
            .ShouldBe(string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical)).Take(8).Select(b => b.ToString("x2"))));
    }

    [Test]
    public void Fingerprint_NullsAndUnknownEnum_SameAsBefore()
    {
        var profile = new CheckProfile
        {
            Rules =
            [
                new CheckRule { Title = null!, Filter = null!, Parameters = null! },
                new CheckRule
                {
                    Filter = new RuleFilter
                    {
                        Categories = null!, Mode = (SelectionMode)17, Logic = (ConditionLogic)(-3),
                        Conditions = [null!, new Condition { Operator = (ConditionOperator)99, Group = [null!] }]
                    },
                    Parameters = [null!, ""]
                },
                null!
            ]
        };

        ProfileSerializer.Fingerprint(profile).ShouldBe(NewtonsoftFingerprint(profile));
        ProfileSerializer.Fingerprint(new CheckProfile { Rules = null! }).ShouldBe(NewtonsoftFingerprint(new CheckProfile { Rules = null! }));
        ProfileSerializer.Fingerprint(new CheckProfile()).ShouldBe(NewtonsoftFingerprint(new CheckProfile()));
    }

    /// <summary>Любые символы в названиях и значениях: управляющие, кавычки, разделители строк Юникода, эмодзи, одиночные суррогаты.</summary>
    [Test]
    public void Fingerprint_AnyText_SameAsBefore()
    {
        var all = new StringBuilder();
        for (var c = 0; c <= 0xFFFF; c++) all.Append((char)c);
        Check(all.ToString());
        Check("\uD83D\uDE00 \uD83D lone \uDE00 \u0085\u2028\u2029 \0 \u001f \u007f \u00a0 </script> 'a' & \u200b");

        var random = new Random(20261123);
        for (var i = 0; i < 300; i++)
        {
            var text = new char[random.Next(0, 40)];
            for (var j = 0; j < text.Length; j++) text[j] = (char)random.Next(random.Next(3) == 0 ? 0x80 : 0x10000);
            Check(new string(text));
        }

        static void Check(string text)
        {
            var profile = new CheckProfile
            {
                Rules = [new CheckRule { Title = text, Parameters = [text], Filter = new RuleFilter { Categories = [text], Conditions = [Condition.Simple(text, ConditionOperator.Equals, text)] } }]
            };
            ProfileSerializer.Fingerprint(profile).ShouldBe(NewtonsoftFingerprint(profile));
        }
    }

    /// <summary>Файл профилей, записанный версией 1.0.1: читается, а записанный заново читается ею же.</summary>
    [Test]
    public void ProfilesFile_WrittenByOldVersion_ReadAndWrittenBack()
    {
        var old = Newtonsoft.Json.JsonConvert.SerializeObject(new ProfileSet { Profiles = [Sample()] }, new Newtonsoft.Json.JsonSerializerSettings
        {
            Formatting = Newtonsoft.Json.Formatting.Indented,
            Converters = [new StringEnumConverter()],
            NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
            ContractResolver = new StoredOnlyResolver()
        });

        var set = ProfileSerializer.Deserialize(old, _ => null);
        var written = ProfileSerializer.Serialize(set);

        ProfileSerializer.Fingerprint(set.Profiles[0]).ShouldBe(ProfileSerializer.Fingerprint(Sample()));
        written.ShouldBe(old);
        var readByOld = Newtonsoft.Json.JsonConvert.DeserializeObject<ProfileSet>(written, new Newtonsoft.Json.JsonSerializerSettings { Converters = [new StringEnumConverter()] })!;
        readByOld.Profiles[0].Rules[0].Filter.Conditions[1].Group![1].Operator.ShouldBe(ConditionOperator.IsEmpty);
    }

    [Test]
    public void ProfilesFile_NullsCommentsAndLooseTypes_Read()
    {
        var set = ProfileSerializer.Deserialize("""
            {
              // поправлено руками
              "version": 2,
              "Profiles": [
                {
                  "name": "ОВ", "IsEnabled": null, "ViewName": null, "Models": null,
                  "Rules": [ { "Title": 15, "Filter": { "Mode": 1, "Logic": "or", "Categories": null }, "Parameters": ["Марка", null], } ],
                },
                null
              ]
            }
            """, _ => null);

        var profile = set.Profiles.ShouldHaveSingleItem();
        (profile.Name, profile.IsEnabled, profile.ViewName).ShouldBe(("ОВ", true, ""));
        profile.Models.ShouldBeEmpty();
        var rule = profile.Rules.ShouldHaveSingleItem();
        rule.Title.ShouldBe("15");
        (rule.Filter.Mode, rule.Filter.Logic).ShouldBe((SelectionMode.ParametersOnly, ConditionLogic.Or));
        rule.Filter.Categories.ShouldBeEmpty();
        rule.Parameters.ShouldBe(["Марка"]);
    }

    [Test]
    public void ProfilesFile_ComputedPropertiesAreNotWritten()
    {
        var json = ProfileSerializer.Serialize(new ProfileSet { Profiles = [new CheckProfile { Name = "ОВ", Models = [new ModelSource { Path = "RSN://rs/Дом_ОВ.rvt" }], Rules = Sample().Rules }] });

        json.ShouldContain("Дом_ОВ.rvt");
        json.ShouldNotContain("\"Name\": \"Дом_ОВ\"");
        json.ShouldNotContain("UsesCategories");
        json.ShouldNotContain("IsGroup");
        json.ShouldNotContain("null");
        json.ShouldNotContain("\\u");
    }

    /// <summary>База, которую вели версии на EPPlus: новая запись дописывает строки в те же таблицы.</summary>
    [Test]
    public void History_WrittenByOldVersion_IsAppended()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pc_{Guid.NewGuid():N}.xlsx");
        try
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "TestData", "history-epplus.xlsx"), path);
            var failed = new ModelOutcome("ОВ", "Дом_АР", "RSN://rs/Дом_АР.rvt", ModelStatus.Failed, TimeSpan.FromSeconds(3), Error: "нет вида");

            HistoryWriter.Append(path, new DateTime(2026, 10, 3, 9, 0, 0), [failed]);

            using var workbook = new XLWorkbook(path);
            var models = workbook.Worksheet("Модели");
            models.Table("Models").RowCount().ShouldBe(1 + 4);
            models.Cell(2, 3).GetString().ShouldBe("Дом_ОВ");
            models.Cell(2, 1).GetDateTime().ShouldBe(new DateTime(2026, 10, 1, 9, 0, 0));
            models.Cell(2, 10).GetDouble().ShouldBe(1d / 3, 1e-9);
            models.Cell(5, 3).GetString().ShouldBe("Дом_АР");
            models.Cell(5, 6).GetString().ShouldBe("нет вида");
            models.Cell(5, 1).GetDateTime().ShouldBe(new DateTime(2026, 10, 3, 9, 0, 0));
            workbook.Worksheet("Правила").Table("Rules").RowCount().ShouldBe(1 + 2);
            workbook.Worksheet("Параметры").Table("Parameters").RowCount().ShouldBe(1 + 4);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Report_KeepsNumbersColoursAndLinks()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pc_{Guid.NewGuid():N}.xlsx");
        try
        {
            var report = new ModelReport { ProfileName = "ОВ", ModelName = "Дом_ОВ", SourcePath = "RSN://rs/Дом_ОВ.rvt", CheckedAt = new DateTime(2026, 10, 1, 9, 0, 0) };
            var rule = new RuleReport("Воздуховоды", ["Марка", "Код"]);
            var filled = new ParamValue(true, "ok");
            var empty = new ParamValue(true, "");
            foreach (var values in new[] { new[] { filled, filled }, [filled, empty], [ParamValue.Missing, empty] })
            {
                rule.Count(values);
                rule.Rows.Add(new ElementRow(5000000000, "Воздуховоды", "Сем", "Тип", "РН", values));
            }

            report.Rules.Add(rule);
            report.Rules.Add(new RuleReport("Пустое", ["Марка"]));
            ReportWriter.Write(report, path, problemsOnly: true);

            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheet("Воздуховоды");
            sheet.Cell(2, 1).GetValue<long>().ShouldBe(5000000000);
            sheet.Cell(2, 6).Style.Fill.BackgroundColor.Color.ToArgb().ShouldBe(System.Drawing.Color.FromArgb(0xC6, 0xEF, 0xCE).ToArgb());
            sheet.Cell(3, 7).Style.Fill.BackgroundColor.Color.ToArgb().ShouldBe(System.Drawing.Color.FromArgb(0xFF, 0xEB, 0x9C).ToArgb());
            sheet.Cell(4, 6).Style.Fill.BackgroundColor.Color.ToArgb().ShouldBe(System.Drawing.Color.FromArgb(0xFF, 0xC7, 0xCE).ToArgb());
            sheet.Cell(3, 7).IsEmpty().ShouldBeTrue();
            sheet.Cell(3, 8).GetDouble().ShouldBe(0.5);
            sheet.AutoFilter.IsEnabled.ShouldBeTrue();

            var summary = workbook.Worksheet(ReportWriter.SummarySheet);
            var cells = summary.CellsUsed().ToList();
            var link = cells.Single(cell => cell.HasHyperlink && cell.GetString() == "Воздуховоды");
            link.GetHyperlink().InternalAddress.ShouldContain("Воздуховоды");
            cells.ShouldContain(cell => cell.GetString() == "нет элементов");
            cells.ShouldContain(cell => cell.GetString().StartsWith("В листах правил — только элементы"));
            summary.ConditionalFormats.Count().ShouldBe(3);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
