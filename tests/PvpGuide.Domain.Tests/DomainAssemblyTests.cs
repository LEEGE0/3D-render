using PvpGuide.Domain;
using Xunit;

namespace PvpGuide.Domain.Tests;

public sealed class DomainAssemblyTests
{
    [Fact]
    public void Name_identifies_the_domain_assembly()
    {
        Assert.Equal("PvpGuide.Domain", typeof(DomainAssembly).Assembly.GetName().Name);
    }
}
