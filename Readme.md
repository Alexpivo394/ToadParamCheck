<p align="center">
    <h1 align="center">ToadParamCheck</h1>
    <p align="center">Revit parameter fill (LOI) check for Revit 2020–2027</p>
</p>

## Libraries for Revit model LOI checks

This repository contains the check core used by the [ToadTools](https://github.com/Alexpivo394/ToadTools) ParamChecker add-in and by the [ToadFlow](https://github.com/Alexpivo394/ToadFlow) Revit automation service. The core has no UI and is packed into separate NuGet packages for easy connection to your add-in or service.

A profile is a set of rules: which elements to take (by category, by parameter conditions or both) and which parameters must be filled in them. A check gives an Excel report per model and, optionally, appends the results to a history workbook for dashboards (Power BI, Excel).

The Revit package version matches the Revit release, for example use `2024.*` for Revit 2024.

Didn't find the feature you need? [Tell me](https://github.com/Alexpivo394/ToadParamCheck/issues/new)


## Alexpivo394.Revit.ParamCheck

[![Nuget](https://img.shields.io/nuget/vpre/Alexpivo394.Revit.ParamCheck?style=for-the-badge)](https://www.nuget.org/packages/Alexpivo394.Revit.ParamCheck)
[![Downloads](https://img.shields.io/nuget/dt/Alexpivo394.Revit.ParamCheck?style=for-the-badge)](https://www.nuget.org/packages/Alexpivo394.Revit.ParamCheck)

Check of one model inside the Revit API context: detached opening with link unloading, workset filtering, the version guard, element selection by rules, Excel report, history workbook shared by several writers, Revit category and parameter catalog for rule editors.

Installation:

```text
<PackageReference Include="Alexpivo394.Revit.ParamCheck" Version="$(RevitVersion).*"/>
```

Usage:

```csharp
var inspector = new ModelInspector(application);
var outcome = inspector.Check(
    @"\\nas\projects\AR.rvt",
    profile,                                    // CheckProfile
    new CheckOptions { ViewName = "Navisworks", AllowUpgrade = false },
    @"\\nas\reports\AR_LOIReport.xlsx",
    progress,                                   // ICheckProgress
    log);                                       // ICheckLog

HistoryWriter.AppendShared(@"\\nas\reports\LOI.xlsx", runStart, [outcome], TimeSpan.FromMinutes(2));
```

## Alexpivo394.Revit.ParamCheck.Profiles

[![Nuget](https://img.shields.io/nuget/vpre/Alexpivo394.Revit.ParamCheck.Profiles?style=for-the-badge)](https://www.nuget.org/packages/Alexpivo394.Revit.ParamCheck.Profiles)
[![Downloads](https://img.shields.io/nuget/dt/Alexpivo394.Revit.ParamCheck.Profiles?style=for-the-badge)](https://www.nuget.org/packages/Alexpivo394.Revit.ParamCheck.Profiles)

Profiles without the Revit API (`netstandard2.0`): the rule model, the JSON format of the ParamChecker add-in (including files of its older versions) and validation before a run.

Installation:

```text
<PackageReference Include="Alexpivo394.Revit.ParamCheck.Profiles" Version="1.*"/>
```

Usage:

```csharp
var set = ProfileSerializer.Deserialize(File.ReadAllText("profiles.json"), categoryName: _ => null);
var problems = RunValidator.Problems(set.Profiles, needsModels: false);
```

## Releases

A tag `X.Y.Z` publishes `Alexpivo394.Revit.ParamCheck` as `{year}.X.Y.Z` for every Revit year 2020–2027 and `Alexpivo394.Revit.ParamCheck.Profiles` as `X.Y.Z`, then creates a GitHub release with the packages.

Local pack: `cd build && DOTNET_ENVIRONMENT=Development dotnet run -c Release -- pack` writes `0.0.0-dev` packages to `output/`.
