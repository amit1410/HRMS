using Xunit;

namespace HRMS.Tests;

[CollectionDefinition("Provider acceptance environment", DisableParallelization = true)]
public sealed class ProviderAcceptanceCollection : ICollectionFixture<object>;
