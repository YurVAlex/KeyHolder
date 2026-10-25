namespace PersonalVault.IntegrationTests;

public sealed class SolutionSmokeTests
{
    [Fact]
    public void DomainAssemblyIsLoadable()
    {
        var assembly = typeof(Domain.AssemblyMarker).Assembly;

        Assert.Equal("PersonalVault.Domain", assembly.GetName().Name);
    }

    [Fact]
    public void CryptographyAssemblyIsLoadable()
    {
        var assembly = typeof(Cryptography.AssemblyMarker).Assembly;

        Assert.Equal("PersonalVault.Cryptography", assembly.GetName().Name);
    }

    [Fact]
    public void InfrastructureAssemblyIsLoadable()
    {
        var assembly = typeof(Infrastructure.AssemblyMarker).Assembly;

        Assert.Equal("PersonalVault.Infrastructure", assembly.GetName().Name);
    }
}
