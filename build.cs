#:package Bullseye
#:package SimpleExec
// A file-based app publishes with Native AOT by default. This script is only ever run, so
// turning that off saves restoring the AOT compiler's packages.
#:property PublishAot=false
// SonarQube Cloud analyzes the solution's projects, not this script, which a PR build compiles
// while the analysis is collecting what MSBuild builds.
#:property SonarQubeExclude=true

using System.Xml.Linq;
using static Bullseye.Targets;
using static SimpleExec.Command;

// GroundWerk.InnerSource's build, run the same way on a dev machine and in a pipeline (CICD-2).
// dotnet run build.cs runs every target. Name targets to run fewer: dotnet run build.cs -- test.
// The paths below are from the repo root, wherever the script is started.
Directory.SetCurrentDirectory((string)AppContext.GetData("EntryPointFileDirectoryPath")!);

const string Solution = "GroundWerk.InnerSource.slnx";
const string Configuration = "Release";
const string TestResults = "artifacts/test-results";
const string Packages = "artifacts/packages";

// Every project under src ships as a NuGet package (README.md)
var packageProjects = Directory.GetFiles("src", "*.csproj", SearchOption.AllDirectories).Order().ToArray();

Target("restore", "Restore the solution's NuGet packages",
    () => RunAsync("dotnet", $"restore {Solution}"));

Target("build", "Compile the solution in Release", dependsOn: ["restore"], async () =>
{
    // MinVer counts the commits since the latest version tag, so a shallow clone, which is a
    // new pipeline's default checkout, would get the wrong version without any warning.
    var (shallow, _) = await ReadAsync("git", "rev-parse --is-shallow-repository");
    if (shallow.Trim() == "true")
    {
        throw new InvalidOperationException("This clone is shallow, so its version would be wrong. Check out with fetchDepth: 0, or run git fetch --unshallow.");
    }

    if (Directory.Exists("artifacts"))
    {
        Directory.Delete("artifacts", recursive: true);
    }

    await RunAsync("dotnet", $"build {Solution} --configuration {Configuration} --no-restore");
});

Target("test", "Run every test project, with TRX results and Cobertura coverage in artifacts/test-results", dependsOn: ["build"],
    () => RunAsync("dotnet", $"test --solution {Solution} --configuration {Configuration} --no-build --results-directory {TestResults} --report-xunit-trx --coverage --coverage-output-format cobertura"));

Target("pack", "Pack each project under src into artifacts/packages", dependsOn: ["build"], forEach: packageProjects,
    project => RunAsync("dotnet", $"pack {project} --configuration {Configuration} --no-build --output {Packages}"));

Target("default", "Run test and pack, then print what two runs of a commit should share", dependsOn: ["test", "pack"], async () =>
{
    var (sdk, _) = await ReadAsync("dotnet", "--version");
    var counters = Directory.GetFiles(TestResults, "*.trx", SearchOption.AllDirectories)
        .Select(file => XDocument.Load(file).Descendants().Single(element => element.Name.LocalName == "Counters"))
        .ToList();
    int Count(string outcome) => counters.Sum(element => (int)element.Attribute(outcome)!);
    var packed = Directory.GetFiles(Packages, "*.nupkg").Select(Path.GetFileName).Order().ToList();

    Console.WriteLine($"SDK: {sdk.Trim()}");
    Console.WriteLine($"Tests: {Count("total")} total, {Count("passed")} passed, {Count("failed")} failed");
    Console.WriteLine($"Packages: {string.Join(", ", packed)}");
});

await RunTargetsAndExitAsync(args, ex => ex is SimpleExec.ExitCodeException);
