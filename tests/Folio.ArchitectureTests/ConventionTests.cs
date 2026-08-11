using System.Reflection;
using System.Text.RegularExpressions;
using Folio.Domain.Model;

namespace Folio.ArchitectureTests;

public sealed partial class ConventionTests
{
    private static readonly Assembly Api = typeof(Program).Assembly;

    private static readonly Assembly Domain = typeof(Snapshot).Assembly;

    private static readonly string Root =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Test]
    public async Task No_Slice_File_Exceeds_Two_Hundred_And_Fifty_Lines()
    {
        string[] oversized =
        [
            .. Directory.EnumerateFiles(Path.Combine(Root, "src", "Folio.Api", "Features"), "*.cs", SearchOption.AllDirectories)
                .Where(file => File.ReadAllLines(file).Length > 250)
                .Select(Path.GetFileName)
                .Select(name => name!),
        ];

        await Assert.That(oversized).IsEmpty();
    }

    [Test]
    public async Task Every_Slice_Namespace_Is_Declared_In_Exactly_One_File()
    {
        Dictionary<string, List<string>> filesByNamespace = new(StringComparer.Ordinal);

        foreach (string file in Directory.EnumerateFiles(
            Path.Combine(Root, "src", "Folio.Api", "Features"), "*.cs", SearchOption.AllDirectories))
        {
            foreach (Match match in SliceNamespace().Matches(File.ReadAllText(file)))
            {
                string declared = match.Groups[1].Value;

                if (!filesByNamespace.TryGetValue(declared, out List<string>? files))
                {
                    files = [];
                    filesByNamespace[declared] = files;
                }

                files.Add(Path.GetFileName(file));
            }
        }

        string[] split =
        [
            .. filesByNamespace
                .Where(entry => entry.Value.Count > 1)
                .Select(entry => $"{entry.Key}: {string.Join(", ", entry.Value)}"),
        ];

        await Assert.That(filesByNamespace).IsNotEmpty();
        await Assert.That(split).IsEmpty();
    }

    [Test]
    public async Task No_Response_Type_Exposes_A_Domain_Type()
    {
        Assembly domain = typeof(Snapshot).Assembly;

        string[] leaking =
        [
            .. Api.GetTypes()
                .Where(type => type.Name is "Response" or "Request")
                .SelectMany(type => type.GetProperties())
                .Where(property => Exposes(property.PropertyType, domain))
                .Select(property => $"{property.DeclaringType!.FullName}.{property.Name}"),
        ];

        await Assert.That(leaking).IsEmpty();
    }

    [Test]
    public async Task No_Mocking_Library_Is_Referenced()
    {
        string[] mocking = ["Moq", "NSubstitute", "FakeItEasy", "Rhino.Mocks"];

        string[] offenders =
        [
            .. Directory.EnumerateFiles(Root, "*.props", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateFiles(Root, "*.csproj", SearchOption.AllDirectories))
                .SelectMany(file => mocking.Where(name =>
                    File.ReadAllText(file).Contains($"\"{name}\"", StringComparison.OrdinalIgnoreCase))),
        ];

        await Assert.That(offenders).IsEmpty();
    }

    [Test]
    public async Task The_Scanners_Find_The_Repository()
    {
        await Assert.That(Directory.Exists(Path.Combine(Root, "src", "Folio.Api", "Features"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(Root, "AGENTS.md"))).IsTrue();
    }

    [Test]
    public async Task Routes_Are_Registered_In_One_File()
    {
        string[] elsewhere =
        [
            .. Directory.EnumerateFiles(Path.Combine(Root, "src", "Folio.Api"), "*.cs", SearchOption.AllDirectories)
                .Where(file => !file.Contains("obj", StringComparison.Ordinal))
                .Where(file => Path.GetFileName(file) is not "FolioEndpoints.cs")
                .Where(file => MapRoute().IsMatch(File.ReadAllText(file)))
                .Select(Path.GetFileName)
                .Select(name => name!),
        ];

        await Assert.That(elsewhere).IsEmpty();
    }

    [Test]
    public async Task No_Page_Type_Names_A_Route()
    {
        string[] urlShaped = ["Route", "Path", "Url", "Href"];

        string[] offenders =
        [
            .. new[] { Domain, Api }
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => type.Name.Contains("Page", StringComparison.Ordinal))
                .SelectMany(type => type.GetProperties().Select(property => (Type: type, Property: property)))
                .Where(entry => urlShaped.Any(name =>
                    entry.Property.Name.Contains(name, StringComparison.Ordinal)))
                .Select(entry => $"{entry.Type.Name}.{entry.Property.Name}")
                .Order(StringComparer.Ordinal),
        ];

        await Assert.That(offenders).IsEmpty();
    }

    private static bool Exposes(Type type, Assembly domain)
    {
        if (type.Assembly == domain)
        {
            return true;
        }

        return type.IsGenericType && type.GetGenericArguments().Any(argument => argument.Assembly == domain);
    }

    [GeneratedRegex(@"\.Map(Get|Post|Put|Delete|Patch)\s*\(")]
    private static partial Regex MapRoute();

    [GeneratedRegex(@"namespace\s+(Folio\.Api\.Features\.[A-Za-z0-9_.]+)\s*;")]
    private static partial Regex SliceNamespace();
}
