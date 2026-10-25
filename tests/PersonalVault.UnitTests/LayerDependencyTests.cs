using System.Reflection;

namespace PersonalVault.UnitTests;

public sealed class LayerDependencyTests
{
    private static readonly string[] OuterLayerNames =
    [
        "PersonalVault.Desktop",
        "PersonalVault.Infrastructure",
    ];

    [Fact]
    public void DomainDoesNotReferenceOuterLayers()
    {
        AssertDoesNotReference(
            typeof(Domain.AssemblyMarker).Assembly,
            OuterLayerNames.Append("PersonalVault.Cryptography"));
    }

    [Fact]
    public void CryptographyDoesNotReferenceOuterLayers()
    {
        AssertDoesNotReference(
            typeof(Cryptography.AssemblyMarker).Assembly,
            OuterLayerNames);
    }

    [Fact]
    public void InfrastructureDoesNotReferenceDesktop()
    {
        AssertDoesNotReference(
            typeof(Infrastructure.AssemblyMarker).Assembly,
            ["PersonalVault.Desktop"]);
    }

    private static void AssertDoesNotReference(
        Assembly assembly,
        IEnumerable<string> forbiddenAssemblyNames)
    {
        var references = assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name);

        Assert.Empty(references.Intersect(forbiddenAssemblyNames, StringComparer.Ordinal));
    }
}
