# AGENTS.md

How to work in GroundWerk.InnerSource, for people and coding agents. A PR that changes a convention changes this file too. Work items, their board states and the Definitions of Done and Ready are the project's, in the GroundWerk repo's AGENTS.md and on the wiki page Definitions of Done and Ready.

## Layout

- `GroundWerk.InnerSource.slnx` is the one solution. Add each new project to it with `dotnet sln GroundWerk.InnerSource.slnx add`.
- `src/GroundWerk.<Name>`: one project per package, named after it, such as `src/GroundWerk.Telemetry`. build.cs packs every project under src, so code that isn't a package doesn't go there, and each one packs the repo's README.md as its readme, as GroundWerk.Telemetry's project file does.
- `tests/GroundWerk.<Name>.Tests`: each package's tests.
- `build.cs`: the build script, which .NET 10 runs as a file-based app. Its Bullseye targets restore, build, test and pack, so the pipeline's YAML only runs it. Its `#:package` lines carry no versions, since those live in Directory.Packages.props.
- `pipelines/innersource-ci.yml`: pipeline innersource-ci, from the ci stage template in GroundWerk.PipelineTemplates at a release tag. Its definition, the innersource feed and main's PR build are in the GroundWerk repo's infra/azdo Terraform (docs/adr/0009 there).

## Build and test

- Build and test with `dotnet run build.cs`, the same command the pipeline runs. It builds in Release, runs every test with code coverage, packs each project under src into artifacts/packages, and ends by printing the SDK, the test count and the packages. It must pass with no warnings before a PR. `dotnet run build.cs -- test` stops after the tests, and `dotnet run build.cs -- --list-tree` shows every target and what it depends on. global.json pins the .NET 10 SDK and runs the tests on Microsoft Testing Platform, which is why `dotnet test` takes `--solution`.
- build.cs writes TRX results and a Cobertura file to artifacts/test-results for SonarQube Cloud. Every test project references Microsoft.Testing.Extensions.CodeCoverage, since one without it fails on `--coverage`. Coverage counts only the assemblies a test loads, so give each package tests that use it.
- Versions come from git tags through MinVer, which needs the whole history and the tags, so build.cs stops in a shallow clone. README.md says how versions are chosen and released.

## Code

- Directory.Build.props sets .NET 10, nullable reference types, implicit usings, the recommended analyzers and warnings as errors for every C# project, so project files don't repeat them. Fix a warning rather than suppress it, and never lower those settings.
- Package versions live only in Directory.Packages.props. Add a package with `dotnet add <project> package <name>`, which records its version there.
- A package's public API is a contract with every service that uses it. Each public member has an XML doc comment, which the build requires, and removing or changing one breaks callers, so it needs a new major version.
- A package depends only on widely used open-source packages, so a service never gets a dependency it wouldn't have chosen itself.
- A package reads its settings from the app's configuration, such as the standard `OTEL_` settings, so each service and environment sets its own.
- File-scoped namespaces that match the folder.
- Generic names only: no company, product or customer names in code, configuration or docs.
- No secrets in the repo: no tokens, keys, passwords or connection strings that hold them. The dev container's pre-commit hook refuses a commit that holds one, so take the secret out rather than skip the hook with `--no-verify`.

## Tests

- Every behavior change comes with tests.
- Tests use xUnit v3. Test names say what they check, in PascalCase.

## Branches, commits and PRs

- Branch from main as `story/<ID>-<short-name>` or `bug/<ID>-<short-name>`, with the work item's ID. main refuses direct pushes.
- End each commit message on the branch with `#<ID>`.
- Title the PR `<Type> <ID>: <what it does>`, in sentence case, with no `#ID` and no period, and link it to its work item.
- Complete it with a squash merge whose message is `Merged PR <N>: ` followed by the PR's title, and delete the branch. Completing a PR doesn't close its work item.
- Every PR into main runs pipeline innersource-ci unless it changes only Markdown files: gitleaks scans each of its commits for secrets, then build.cs runs inside SonarQube Cloud's analysis, and a secret, a failing test or a failed quality gate fails it and blocks the PR. A passing run pushes the PR's packages to the innersource feed as prereleases. The scanner turns warnings-as-errors off, so build.cs must pass before the PR.
- A PR needs an approval from someone other than its author and its last pusher, and its comments resolved. Coding agents never approve or vote on a PR, bypass a policy or run a pipeline, and push a version tag only when a person asks for that release.
