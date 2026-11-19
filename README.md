# InnerSource

InnerSource holds the company-wide NuGet packages, the shared code that every team's services use the same way: the OpenTelemetry setup first, and later the auth package and the fake token issuer for tests (CODE-11). Any team can change a package through a PR, and each version goes to the innersource feed in Azure Artifacts, so a service takes a change only by moving to a new version of the package, never through a project reference (CODE-3).

## Packages

- `InnerSource.Telemetry`: one call, `builder.AddTelemetry()`, sets up OpenTelemetry traces, metrics and structured logs for ASP.NET Core and HttpClient (OBS-1), and names the service after the application unless `OTEL_SERVICE_NAME` names it. When configuration sets `OTEL_EXPORTER_OTLP_ENDPOINT`, all three go there over OTLP, with the headers and protocol from the other standard `OTEL_` settings, which an Aspire AppHost sets for each project it starts. Requests to `/health` aren't traced, since probes call it every few seconds.

## Using a package

Add the feed to the consuming repo's nuget.config and map `InnerSource.*` to it, so these packages come only from the feed and every other package only from nuget.org. Name nuget.org as its only audit source too, so a restore reads the feed only for a package that isn't in the package cache yet:

```xml
<packageSources>
  <clear />
  <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  <add key="innersource" value="https://pkgs.dev.azure.com/mikewicks-lab/GroundWerk/_packaging/innersource/nuget/v3/index.json" />
</packageSources>
<packageSourceMapping>
  <packageSource key="nuget.org">
    <package pattern="*" />
  </packageSource>
  <packageSource key="innersource">
    <package pattern="InnerSource.*" />
  </packageSource>
</packageSourceMapping>
<auditSources>
  <clear />
  <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
</auditSources>
```

A pipeline that extends the ci template in GroundWerk.PipelineTemplates signs in to the feed in its NuGetAuthenticate step. GroundWerk's dev container has the Azure Artifacts Credential Provider, so a restore there that needs the feed signs in with a token from your Azure CLI sign-in, for that command only: `VSS_NUGET_URI_PREFIXES=https://pkgs.dev.azure.com/mikewicks-lab/ VSS_NUGET_ACCESSTOKEN="$(az account get-access-token --resource 499b84ac-1321-427f-aa17-267ca6975798 --query accessToken --output tsv)" dotnet restore`.

## Versions and releases

Versions come from git tags through MinVer and follow semantic versioning: a new major version for a change that can break a caller, such as a removed or renamed public member, a minor version for a new feature, and a patch for a fix.

- Each PR build pushes its packages to the feed as prereleases named for the PR and the run, such as `1.0.1-pr.12.345.1`, so a consumer can try a change before it merges.
- To release, tag the PR's merge commit on main with the next version and push the tag, such as `git tag -a v1.1.0 -m "v1.1.0: what changed"` and `git push origin v1.1.0`. The tag's build pushes that version to the feed only if the tag is on main, so a tag on any other commit, such as your branch's last commit, fails its build and pushes nothing.
- A version can't be replaced once it's in the feed, so never move or delete a tag, and fix a bad release with the next patch.
- Once a version is in the feed, Renovate opens a PR in each repo that uses the package to move it there. To make every repo move past a version, such as one with a security flaw, add the package, the version to reach and a deadline to GroundWerk's infra/azdo/minimum-versions.tf through a PR. A build that uses an older version then warns until the deadline and fails from that day on, in UTC.

## Building

`dotnet run build.cs` builds and tests the solution, then packs each project under src into artifacts/packages. Name targets to run fewer, such as `dotnet run build.cs -- test`. Pipeline innersource-ci runs it on each change to main, on each version tag and as main's PR build.
