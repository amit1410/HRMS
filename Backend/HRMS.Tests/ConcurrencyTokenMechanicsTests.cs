using System.Text.Json;
using HRMS.Application.Abstractions;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace HRMS.Tests;

public sealed class ConcurrencyTokenMechanicsTests
{
    [Fact]
    public void SqlServer_rowversion_properties_remain_database_generated_concurrency_tokens()
    {
        using var context = CreateSqlServerContext();

        foreach (var type in TokenTypes())
        {
            var property = context.Model.FindEntityType(type)!.FindProperty(nameof(LeaveRequest.RowVersion))!;
            Assert.Equal(typeof(byte[]), property.ClrType);
            Assert.True(property.IsConcurrencyToken);
            Assert.Equal(ValueGenerated.OnAddOrUpdate, property.ValueGenerated);
        }
    }

    [Fact]
    public void MySql_rowversion_properties_are_fixed_binary_application_tokens()
    {
        using var context = CreateMySqlContext();

        foreach (var type in TokenTypes())
        {
            var property = context.Model.FindEntityType(type)!.FindProperty(nameof(LeaveRequest.RowVersion))!;
            Assert.Equal(typeof(byte[]), property.ClrType);
            Assert.True(property.IsConcurrencyToken);
            Assert.Equal(ValueGenerated.Never, property.ValueGenerated);
            Assert.Equal("binary(16)", property.GetColumnType());
        }
    }

    [Fact]
    public void MySql_interceptor_assigns_and_rotates_tokens_without_replacing_original_value()
    {
        using var context = CreateMySqlContext();
        var original = Enumerable.Repeat((byte)7, 16).ToArray();
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            LeaveTypeId = Guid.NewGuid(),
            LeavePeriodId = Guid.NewGuid(),
            LeavePolicyVersionId = Guid.NewGuid(),
            LeavePolicyRuleId = Guid.NewGuid(),
            EmployeeEmploymentHistoryId = Guid.NewGuid(),
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 1, 1),
            RequestedQuantity = 1,
            ChargeableQuantity = 1,
            IdempotencyKey = "concurrency-token-test",
            PayloadFingerprint = "concurrency-token-test-fingerprint",
            RowVersion = original.ToArray()
        };
        context.Attach(request);
        context.Entry(request).State = EntityState.Modified;

        new MySqlConcurrencyTokenInterceptor(new MySqlConcurrencyTokenGenerator()).Apply(context);

        var property = context.Entry(request).Property(nameof(LeaveRequest.RowVersion));
        Assert.Equal(original, property.OriginalValue);
        Assert.Equal(16, ((byte[])property.CurrentValue!).Length);
        Assert.False(original.SequenceEqual((byte[])property.CurrentValue!));
    }

    [Fact]
    public void MySql_interceptor_assigns_tokens_to_added_balance_and_sequence_but_not_unrelated_entities()
    {
        using var context = CreateMySqlContext();
        var balance = new EmployeeLeaveBalance();
        var sequence = new EmployeeCodeSequence();
        var tenant = new Tenant();
        context.AddRange(balance, sequence, tenant);

        new MySqlConcurrencyTokenInterceptor(new MySqlConcurrencyTokenGenerator()).Apply(context);

        Assert.Equal(16, balance.RowVersion.Length);
        Assert.Equal(16, sequence.RowVersion.Length);
        Assert.Equal(DatabaseProviderType.SqlServer, tenant.DatabaseProvider);
    }

    [Fact]
    public void SqlServer_interceptor_does_not_manually_assign_tokens()
    {
        using var context = CreateSqlServerContext();
        var request = new LeaveRequest();
        context.Add(request);
        var before = request.RowVersion.ToArray();

        new MySqlConcurrencyTokenInterceptor(new MySqlConcurrencyTokenGenerator()).Apply(context);

        Assert.Equal(before, request.RowVersion);
    }

    [Fact]
    public void LeaveBalanceSnapshot_keeps_byte_array_base64_wire_shape()
    {
        var token = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        var json = JsonSerializer.Serialize(new LeaveBalanceSnapshot(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 0, 0, 1, token));

        Assert.Contains($"\"RowVersion\":\"{Convert.ToBase64String(token)}\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Token_generator_returns_distinct_fixed_length_tokens()
    {
        var generator = new MySqlConcurrencyTokenGenerator();
        var tokens = Enumerable.Range(0, 4).Select(_ => generator.Create()).ToArray();

        Assert.All(tokens, token => Assert.Equal(16, token.Length));
        Assert.NotEqual(tokens[0], tokens[1]);
    }

    private static Type[] TokenTypes() =>
    [
        typeof(LeaveRequest),
        typeof(EmployeeLeaveBalance),
        typeof(EmployeeCodeSequence)
    ];

    private static HrmsDbContext CreateMySqlContext() =>
        new(new DbContextOptionsBuilder<HrmsDbContext>()
            .UseMySQL("Server=not-opened;Database=not-opened;")
            .Options, new TestTenantContext());

    private static HrmsDbContext CreateSqlServerContext() =>
        new(new DbContextOptionsBuilder<HrmsDbContext>()
            .UseSqlServer("Server=not-opened;Database=not-opened;")
            .Options, new TestTenantContext());

    private sealed class TestTenantContext : ITenantContext
    {
        public Guid? TenantId => null;
        public Guid? UserId => null;
        public bool HasTenant => false;
    }
}
