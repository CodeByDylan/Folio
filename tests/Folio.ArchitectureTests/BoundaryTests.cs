using System.Reflection;
using Folio.Domain.Model;
using NetArchTest.Rules;
using ArchTestResult = NetArchTest.Rules.TestResult;

namespace Folio.ArchitectureTests;

public sealed class BoundaryTests
{
    private static readonly Assembly Domain = typeof(Snapshot).Assembly;
    private static readonly Assembly Api = typeof(Program).Assembly;

    private static readonly string[] DomainAllowList =
    [
        "Folio.Domain",
        "Loom.Results",
        "Markdig",
        "Tomlyn",
        "System",
        "netstandard",
        "mscorlib",
    ];

    [Test]
    public async Task Domain_References_Nothing_Outside_Its_Allow_List()
    {
        string[] offenders = ReferencedAssemblies(Domain)
            .Where(name => !DomainAllowList.Any(allowed =>
                name.Equals(allowed, StringComparison.Ordinal)
                || name.StartsWith(allowed + ".", StringComparison.Ordinal)))
            .ToArray();

        await Assert.That(offenders).IsEmpty();
    }

    [Test]
    public async Task Domain_Performs_No_IO()
    {
        ArchTestResult result = Types.InAssembly(Domain)
            .Should()
            .NotHaveDependencyOnAny(
                "System.IO.File",
                "System.IO.Directory",
                "System.IO.FileStream",
                "System.Net.Http",
                "System.Net.Sockets")
            .GetResult();

        await Assert.That(result.IsSuccessful).IsTrue();
    }

    [Test]
    public async Task Api_References_Neither_Octokit_Nor_Redis()
    {
        string[] offenders = ReferencedAssemblies(Api)
            .Where(name =>
                name.StartsWith("Octokit", StringComparison.Ordinal)
                || name.StartsWith("StackExchange.Redis", StringComparison.Ordinal))
            .ToArray();

        await Assert.That(offenders).IsEmpty();
    }

    [Test]
    public async Task IConfiguration_Is_A_Constructor_Parameter_Of_Nothing()
    {
        string[] offenders = new[] { Domain, Api, typeof(Folio.Ingestion.Snapshots.ISnapshotStore).Assembly }
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            .Where(constructor => constructor.GetParameters()
                .Any(parameter => parameter.ParameterType == typeof(Microsoft.Extensions.Configuration.IConfiguration)))
            .Select(constructor => constructor.DeclaringType!.FullName!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(offenders).IsEmpty();
    }

    [Test]
    public async Task No_Slice_Depends_On_Another_Slice()
    {
        // A slice is Folio.Api.Features.<Aggregate>.<Operation>; the aggregate namespace itself is not one.
        const string root = "Folio.Api.Features.";

        string[] slices = Api.GetTypes()
            .Select(type => type.Namespace)
            .Where(ns => ns is not null
                && ns.StartsWith(root, StringComparison.Ordinal)
                && ns[root.Length..].Contains('.', StringComparison.Ordinal))
            .Select(ns => ns!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        List<string> offenders = [];

        foreach (string slice in slices)
        {
            string aggregate = slice[..slice.LastIndexOf('.')];

            string[] forbidden =
            [
                .. slices.Where(other => !string.Equals(other, slice, StringComparison.Ordinal)),
                .. Api.GetTypes()
                    .Select(type => type.Namespace)
                    .Where(ns => ns is not null
                        && ns.StartsWith(root, StringComparison.Ordinal)
                        && !ns[root.Length..].Contains('.', StringComparison.Ordinal)
                        && !string.Equals(ns, aggregate, StringComparison.Ordinal))
                    .Select(ns => ns!)
                    .Distinct(StringComparer.Ordinal),
            ];

            if (forbidden.Length == 0)
            {
                continue;
            }

            ArchTestResult result = Types.InAssembly(Api)
                .That().ResideInNamespace(slice)
                .Should().NotHaveDependencyOnAny(forbidden)
                .GetResult();

            if (!result.IsSuccessful)
            {
                offenders.AddRange(result.FailingTypeNames ?? []);
            }
        }

        await Assert.That(offenders).IsEmpty();
    }

    private static IEnumerable<string> ReferencedAssemblies(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(reference => reference.Name!);
}
