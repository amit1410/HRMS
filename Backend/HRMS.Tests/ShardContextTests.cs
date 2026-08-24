using HRMS.Application.Abstractions;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Sharding;

namespace HRMS.Tests;

/// <summary>
/// The shard is write-once per scope. That rule is the whole safety property of the type, so it is asserted
/// rather than left to the comment beside it.
/// </summary>
public class ShardContextTests
{
    private static readonly ShardDescriptor Demo01 =
        new(Guid.NewGuid(), "DEMO01", "demo01.localhost", "demo01", TenantStatus.Active);

    private static readonly ShardDescriptor Demo02 =
        new(Guid.NewGuid(), "DEMO02", "demo02.localhost", "demo02", TenantStatus.Active);

    [Fact]
    public void Starts_with_no_organization_selected()
    {
        var context = new ShardContext();

        Assert.Null(context.Current);
        Assert.False(context.HasShard);
    }

    [Fact]
    public void Records_the_organization_it_is_given()
    {
        var context = new ShardContext();

        context.Use(Demo01);

        Assert.Same(Demo01, context.Current);
        Assert.True(context.HasShard);
    }

    /// <summary>
    /// Descriptors compare by value, so re-resolving the same organization within one scope is a no-op rather
    /// than a conflict.
    /// </summary>
    [Fact]
    public void Selecting_the_same_organization_again_is_allowed()
    {
        var context = new ShardContext();
        var sameOrganizationAgain = Demo01 with { };

        context.Use(Demo01);
        context.Use(sameOrganizationAgain);

        Assert.Equal(Demo01, context.Current);
    }

    /// <summary>
    /// The failure this type exists to prevent: the scope's <c>DbContext</c> may already hold a connection to
    /// the first organization's database and be tracking its entities, so moving the shard mid-scope would
    /// send subsequent writes to a different customer's database while the tenant stamp still said the first.
    /// </summary>
    [Fact]
    public void Switching_to_a_different_organization_is_refused()
    {
        var context = new ShardContext();
        context.Use(Demo01);

        var exception = Assert.Throws<InvalidOperationException>(() => context.Use(Demo02));

        Assert.Contains("demo01", exception.Message);
        Assert.Contains("demo02", exception.Message);
        Assert.Equal(Demo01, context.Current);
    }

    [Fact]
    public void Refuses_a_missing_descriptor()
    {
        var context = new ShardContext();

        Assert.Throws<ArgumentNullException>(() => context.Use(null!));
    }
}
