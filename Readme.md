<h1 align="center">ToadParamCheck</h1>

<p align="center">
  Checks that the parameters of a Revit model are filled in (LOI), as a library.<br>
  Revit 2020–2027.
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/Alexpivo394.Revit.ParamCheck"><img src="https://img.shields.io/nuget/v/Alexpivo394.Revit.ParamCheck?label=Revit.ParamCheck" alt="Alexpivo394.Revit.ParamCheck on NuGet"></a>
  <a href="https://www.nuget.org/packages/Alexpivo394.Revit.ParamCheck.Profiles"><img src="https://img.shields.io/nuget/v/Alexpivo394.Revit.ParamCheck.Profiles?label=Revit.ParamCheck.Profiles" alt="Alexpivo394.Revit.ParamCheck.Profiles on NuGet"></a>
  <a href="https://github.com/Alexpivo394/ToadParamCheck/actions/workflows/Build.yml"><img src="https://img.shields.io/github/actions/workflow/status/Alexpivo394/ToadParamCheck/Build.yml?label=build" alt="Build"></a>
  <a href="License.md"><img src="https://img.shields.io/badge/license-MIT-blue" alt="MIT license"></a>
</p>

---

ToadParamCheck is the check core behind the ParamChecker add-in in [ToadTools](https://github.com/Alexpivo394/ToadTools) and the scheduled LOI checks of [ToadFlow](https://github.com/Alexpivo394/ToadFlow). It has no UI: you give it a model and a profile, it gives you an Excel report and the numbers behind it.

The model is simple:

- A **profile** is a list of rules.
- A **rule** says which elements to take and which parameters they must have filled. Elements are selected by category, by conditions on parameter values, or by both.
- A **check** runs a profile against one model. Every value lands in one of three buckets: filled, empty, or the parameter does not exist on the element.

The result is an Excel workbook with a summary sheet and one sheet per rule, each row an element with its ID. Results can also be appended to a shared history workbook, which is what a Power BI or Excel dashboard reads to show progress over time.

## Packages

| Package | Targets | Revit API | What it does |
|---|---|---|---|
| [`Alexpivo394.Revit.ParamCheck`](https://www.nuget.org/packages/Alexpivo394.Revit.ParamCheck) | per Revit year | yes | Checks one model inside the Revit API context and writes the report |
| [`Alexpivo394.Revit.ParamCheck.Profiles`](https://www.nuget.org/packages/Alexpivo394.Revit.ParamCheck.Profiles) | `netstandard2.0`, `net8.0` | no | The rule model, its JSON format, and validation before a run |

The version of `Alexpivo394.Revit.ParamCheck` starts with the Revit year: use `2024.*` for Revit 2024.

## Writing a profile

```xml
<PackageReference Include="Alexpivo394.Revit.ParamCheck.Profiles" Version="1.*"/>
```

`Profiles` has no Revit dependency, so a server or a rule editor can build, store, and validate profiles without Revit.

```csharp
var profile = new CheckProfile
{
    Name = "HVAC",
    Rules =
    [
        new CheckRule
        {
            Title = "Supply ducts",
            Filter = new RuleFilter
            {
                Categories = ["OST_DuctCurves"],
                Mode = SelectionMode.CategoriesAndParameters,
                Conditions = [Condition.Simple("System Name", ConditionOperator.Contains, "Supply")]
            },
            Parameters = ["Classifier Code", "Fire Rating", "Mark"]
        }
    ]
};
```

Profiles are stored as JSON, in the same format the ParamChecker add-in exports. Files written by its older versions load too.

```csharp
ProfileSet set = ProfileSerializer.Deserialize(File.ReadAllText("profiles.json"), categoryName: _ => null);
IReadOnlyList<string> problems = RunValidator.Problems(set.Profiles, needsModels: false);
```

## Checking a model

```xml
<PackageReference Include="Alexpivo394.Revit.ParamCheck" Version="$(RevitVersion).*"/>
```

```csharp
var inspector = new ModelInspector(application);

ModelOutcome outcome = inspector.Check(
    @"\\nas\projects\HVAC.rvt",
    profile,
    new CheckOptions { ViewName = "Navisworks", ProblemsOnly = true, AllowUpgrade = false },
    reportPath: @"\\nas\reports\HVAC_LOIReport.xlsx",
    progress,   // ICheckProgress
    log);       // ICheckLog

HistoryWriter.AppendShared(@"\\nas\reports\LOI.xlsx", runStart, [outcome], wait: TimeSpan.FromMinutes(2));
```

`Check` opens the model detached, runs the profile, writes the report, and closes the model. `ModelInspector.CheckOpen` does the same for a document that is already open.

| Option | Default | Effect |
|---|---|---|
| `SkipLinks` | `true` | Opens a temporary copy with RVT links unloaded |
| `WorksetKeyword` | empty | Worksets with this word in the name stay closed |
| `ViewName` | empty | Takes only elements visible on this view; empty means the whole model |
| `ProblemsOnly` | `false` | Rule sheets list only elements with unfilled parameters, which keeps reports of large models small |
| `AllowUpgrade` | `true` | When `false`, a model saved in another Revit release is refused instead of upgraded in memory |

The outcome has a `Status` of `Checked`, `Failed`, or `Skipped`. `RevitFailure` marks a run that died on an internal Revit error, not on the model; a retry in a new Revit process usually passes.

`AppendShared` is safe to call from several machines writing to the same history workbook: a writer waits for the file up to the given time. Each append reads the workbook and saves it whole: sheets, data and tables stay, charts added by hand do not — build dashboards in a separate file that reads the history.

`RevitCatalog.Read` lists the categories and parameter names of a Revit session or a document. A rule editor uses it to offer real names instead of free text.

## Building

Requires the .NET 10 SDK.

```powershell
dotnet test --project tests/Alexpivo394.Revit.ParamCheck.Tests
dotnet build source/Alexpivo394.Revit.ParamCheck -c Release.R24
```

Each Revit year has its own configuration, from `Release.R20` to `Release.R27`. The tests cover everything that runs without Revit.

To pack locally, run the build pipeline. It writes `0.0.0-dev` packages to `output/`:

```powershell
cd build
$env:DOTNET_ENVIRONMENT = "Development"; dotnet run -c Release -- pack
```

## Releasing

A tag `X.Y.Z` publishes `Alexpivo394.Revit.ParamCheck` as `{year}.X.Y.Z` for every Revit year from 2020 to 2027 and `Alexpivo394.Revit.ParamCheck.Profiles` as `X.Y.Z`, then creates a GitHub release with the packages attached.

## Related projects

| Project | Role |
|---|---|
| [ToadTools](https://github.com/Alexpivo394/ToadTools) | Revit add-ins; ParamChecker is the UI for this library |
| [ToadFlow](https://github.com/Alexpivo394/ToadFlow) | Runs checks on a schedule and keeps the analytics |
| [ToadExport](https://github.com/Alexpivo394/ToadExport) | The sibling core for RVT and NWC export |

Missing something? [Open an issue](https://github.com/Alexpivo394/ToadParamCheck/issues/new).

## License

[MIT](License.md)
