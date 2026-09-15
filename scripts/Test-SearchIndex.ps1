param([switch]$Launch)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$probe = Join-Path $repo 'artifacts/search-index-probe'
New-Item -ItemType Directory -Path $probe -Force | Out-Null
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows10.0.26100.0</TargetFramework></PropertyGroup>
  <ItemGroup><ProjectReference Include="../../src/GlassDock.Windows/GlassDock.Windows.csproj" /></ItemGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $probe 'SearchIndexProbe.csproj')
@'
using System.Diagnostics;
using GlassDock.Core.Applications;
using GlassDock.Windows.Applications;

using var index = new WindowsApplicationIndex();
var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
index.Changed += (_, _) => { if (!index.Snapshot.IsIndexing) ready.TrySetResult(); };
var clock = Stopwatch.StartNew();
index.Start(); index.Start(); // Repeated opens must not start another worker.
await ready.Task.WaitAsync(TimeSpan.FromSeconds(90));
var snapshot = index.Snapshot;
Console.WriteLine($"Indexed {snapshot.Applications.Count} entries in {clock.ElapsedMilliseconds} ms. Warning: {snapshot.Warning ?? "none"}");
if (snapshot.Applications.Count == 0) throw new Exception("No applications discovered.");
var entries = snapshot.Applications.Concat(WindowsSettingsCatalog.Entries.Select(entry => entry.ToSearchResult())).ToArray();
foreach (var query in new[] { "notepad", "calc", "expl", "code", "bluetooth", "display", "wifi", "sound", "startup", "update" })
{
    var results = GlassSearch.Find(entries, query);
    if (results.Count == 0) throw new Exception($"No local result for {query}.");
    Console.WriteLine($"{query}: {string.Join(" | ", results.Select(result => result.Title))}");
}
clock.Restart();
for (var i = 0; i < 1000; i++) GlassSearch.Find(entries, i % 2 == 0 ? "cal" : "bluetooth");
Console.WriteLine($"1000 cached queries: {clock.ElapsedMilliseconds} ms");
if (GlassSearch.Find(entries, "unknown-zxq-app-123").Count != 0 || GlassSearch.Find(entries, " ").Count != 0)
    throw new Exception("Empty/unknown query regression.");
var iconDeadline = Stopwatch.StartNew();
while (!index.Snapshot.Applications.Any(entry => entry.Icon is not null) && iconDeadline.Elapsed < TimeSpan.FromSeconds(30))
    await Task.Delay(100);
Console.WriteLine($"Native icons observed: {index.Snapshot.Applications.Count(entry => entry.Icon is not null)}");
if (args.Contains("--launch"))
{
    var launcher = new WindowsApplicationLauncher();
    foreach (var query in new[] { "notepad", "bluetooth" })
    {
        var result = GlassSearch.Find(entries, query)[0];
        var success = await launcher.LaunchTargetAsync(result.LaunchTarget);
        Console.WriteLine($"Shell launch {result.Title}: {success}");
        if (!success) throw new Exception($"Launch failed: {result.Title}");
    }
    if (await launcher.LaunchTargetAsync(Path.Combine(AppContext.BaseDirectory, "missing-glassdock-search-test.exe")))
        throw new Exception("Missing executable unexpectedly launched.");
    Console.WriteLine("Missing executable returned false without crashing.");
}
'@ | Set-Content -LiteralPath (Join-Path $probe 'Program.cs')
$probeArgs = @('run', '--project', (Join-Path $probe 'SearchIndexProbe.csproj'), '-c', 'Release')
if ($Launch) { $probeArgs += @('--', '--launch') }
& dotnet @probeArgs
if ($LASTEXITCODE -ne 0) { throw 'Search index probe failed.' }
