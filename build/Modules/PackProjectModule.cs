using Build.Options;
using EnumerableAsyncProcessor.Extensions;
using Microsoft.Extensions.Options;
using ModularPipelines.Attributes;
using ModularPipelines.Context;
using ModularPipelines.DotNet.Extensions;
using ModularPipelines.DotNet.Options;
using ModularPipelines.Git.Extensions;
using ModularPipelines.Models;
using ModularPipelines.Modules;
using Sourcy.DotNet;

namespace Build.Modules;

/// <summary>
///     Pack one Profiles package and one Alexpivo394.Revit.ParamCheck package per Revit year, versioned {year}.{release}.
///     The Revit package depends on the Profiles package of the same release.
/// </summary>
[DependsOn<ResolveVersioningModule>]
[DependsOn<CleanProjectModule>(Optional = true)]
public sealed class PackProjectModule(IOptions<BuildOptions> buildOptions) : Module
{
    private static readonly int[] RevitYears = [2020, 2021, 2022, 2023, 2024, 2025, 2026, 2027];

    protected override async Task ExecuteModuleAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var versioningResult = await context.GetModule<ResolveVersioningModule>();
        var versioning = versioningResult.ValueOrDefault!;
        var outputFolder = context.Git().RootDirectory.GetFolder(buildOptions.Value.OutputDirectory);

        await context.DotNet().Pack(new DotNetPackOptions
        {
            ProjectSolution = Projects.Alexpivo394_Revit_ParamCheck_Profiles.FullName,
            Configuration = "Release",
            Output = outputFolder,
            Properties = new List<KeyValue> { ("Version", versioning.Version) }
        }, cancellationToken: cancellationToken);

        await RevitYears
            .SelectAsync(async year => await context.DotNet().Pack(new DotNetPackOptions
            {
                ProjectSolution = Projects.Alexpivo394_Revit_ParamCheck.FullName,
                Configuration = $"Release.R{year % 100}",
                Output = outputFolder,
                Properties = new List<KeyValue> { ("Version", $"{year}.{versioning.Version}"), ("ProfilesVersion", versioning.Version) }
            }, cancellationToken: cancellationToken), cancellationToken)
            .ProcessOneAtATime();
    }
}
