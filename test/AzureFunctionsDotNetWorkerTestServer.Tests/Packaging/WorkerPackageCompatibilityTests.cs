using System.Xml.Linq;

namespace AzureFunctionsDotNetWorkerTestServer.Tests.Packaging;

[Trait("Suite", "Packaging")]
public class WorkerPackageCompatibilityTests
{
    [Fact]
    public void WorkerPackageMajors_ArePinnedToSupportedRange()
    {
        var repoRoot = FindRepositoryRoot();
        var csprojPaths = new[]
        {
            Path.Combine(repoRoot, "demo", "ExampleFunction", "ExampleFunction.csproj"),
            Path.Combine(repoRoot, "src", "AzureFunctionsDotNetWorkerTestServer", "AzureFunctionsDotNetWorkerTestServer.csproj"),
            Path.Combine(repoRoot, "src", "AzureFunctionsDotNetWorkerTestServer.Abstractions", "AzureFunctionsDotNetWorkerTestServer.Abstractions.csproj"),
            Path.Combine(repoRoot, "test", "AzureFunctionsDotNetWorkerTestServer.Tests", "AzureFunctionsDotNetWorkerTestServer.Tests.csproj"),
        };

        foreach (var path in csprojPaths)
        {
            var workerVersion = GetPackageVersion(path, "Microsoft.Azure.Functions.Worker");
            Assert.Equal(2, ParseMajor(workerVersion));

            if (Path.GetFileName(path).Equals("ExampleFunction.csproj", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(path).Equals("AzureFunctionsDotNetWorkerTestServer.csproj", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(path).Equals("AzureFunctionsDotNetWorkerTestServer.Tests.csproj", StringComparison.OrdinalIgnoreCase))
            {
                var httpVersion = GetPackageVersion(path, "Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore");
                Assert.Equal(2, ParseMajor(httpVersion));
            }
        }
    }

    [Fact]
    public void WorkerPackageVersions_AreAlignedAcrossProjects()
    {
        var repoRoot = FindRepositoryRoot();

        var appProject = Path.Combine(repoRoot, "demo", "ExampleFunction", "ExampleFunction.csproj");
        var libProject = Path.Combine(repoRoot, "src", "AzureFunctionsDotNetWorkerTestServer", "AzureFunctionsDotNetWorkerTestServer.csproj");
        var abstractionsProject = Path.Combine(repoRoot, "src", "AzureFunctionsDotNetWorkerTestServer.Abstractions", "AzureFunctionsDotNetWorkerTestServer.Abstractions.csproj");
        var testProject = Path.Combine(repoRoot, "test", "AzureFunctionsDotNetWorkerTestServer.Tests", "AzureFunctionsDotNetWorkerTestServer.Tests.csproj");

        var expectedWorkerVersion = GetPackageVersion(appProject, "Microsoft.Azure.Functions.Worker");
        Assert.Equal(expectedWorkerVersion, GetPackageVersion(libProject, "Microsoft.Azure.Functions.Worker"));
        Assert.Equal(expectedWorkerVersion, GetPackageVersion(abstractionsProject, "Microsoft.Azure.Functions.Worker"));
        Assert.Equal(expectedWorkerVersion, GetPackageVersion(testProject, "Microsoft.Azure.Functions.Worker"));

        var expectedHttpVersion = GetPackageVersion(appProject, "Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore");
        Assert.Equal(expectedHttpVersion, GetPackageVersion(libProject, "Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore"));
        Assert.Equal(expectedHttpVersion, GetPackageVersion(testProject, "Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore"));
    }

    private static string GetPackageVersion(string csprojPath, string packageId)
    {
        var doc = XDocument.Load(csprojPath);
        var packageReference = doc.Descendants("PackageReference")
            .FirstOrDefault(e =>
                string.Equals((string?)e.Attribute("Include"), packageId, StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(packageReference);
        var version = (string?)packageReference!.Attribute("Version");
        Assert.False(string.IsNullOrWhiteSpace(version), $"Package '{packageId}' in '{csprojPath}' has no Version.");
        return version!;
    }

    private static int ParseMajor(string version) => int.Parse(version.Split('.')[0]);

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root from test base directory.");
    }
}
