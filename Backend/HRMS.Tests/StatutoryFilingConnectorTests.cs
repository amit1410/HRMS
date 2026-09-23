using HRMS.Application.Abstractions;
using HRMS.Application.Services;
using HRMS.Domain.Enums;

namespace HRMS.Tests;

public sealed class StatutoryFilingConnectorTests
{
    [Fact]
    public void Registry_resolves_manual_download_and_test_connectors()
    {
        var registry = new StatutoryFilingConnectorRegistry(new IStatutoryFilingConnector[]
        {
            new ManualDownloadStatutoryFilingConnector(),
            new TestStatutoryFilingConnector()
        });

        Assert.IsType<ManualDownloadStatutoryFilingConnector>(registry.Resolve(StatutoryFilingConnectorType.ManualDownload));
        Assert.IsType<TestStatutoryFilingConnector>(registry.Resolve(StatutoryFilingConnectorType.Test));
    }

    [Fact]
    public void Registry_rejects_unknown_connector()
    {
        var registry = new StatutoryFilingConnectorRegistry(Array.Empty<IStatutoryFilingConnector>());

        Assert.Throws<InvalidOperationException>(() => registry.Resolve(StatutoryFilingConnectorType.ManualDownload));
    }
}
