using FluentValidation;
using HRMS.Application.Abstractions;
using HRMS.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HRMS.Application;

/// <summary>
/// DI registration for the Application layer: FluentValidation validators discovered in this assembly,
/// the application services that hold business logic, and the clock abstraction they use.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        // Injected rather than calling DateTime.UtcNow, so token lifetimes and audit stamps are testable.
        services.TryAddSingletonTimeProvider();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ITenantBrandingService, TenantBrandingService>();
        services.AddScoped<IDepartmentService, DepartmentService>();
        services.AddScoped<IDesignationService, DesignationService>();
        services.AddScoped<IEmployeeService, EmployeeService>();

        return services;
    }

    private static void TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        if (services.All(descriptor => descriptor.ServiceType != typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}
