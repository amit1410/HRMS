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
        services.AddScoped<ICountryService, CountryService>();
        services.AddScoped<IStateService, StateService>();
        services.AddScoped<ICityService, CityService>();
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddScoped<IEmployeeCodeConfigurationService, EmployeeCodeConfigurationService>();
        services.AddScoped<IEmployeeCodeSequenceService, EmployeeCodeSequenceService>();
        services.AddSingleton<EmployeeCodes.EmployeeCodeRuleMatcher>();
        services.AddSingleton<EmployeeCodes.EmployeeCodeRenderer>();

        // Employee sub-entity services
        services.AddScoped<IEmployeeContactService, EmployeeContactService>();
        services.AddScoped<IEmployeeAddressService, EmployeeAddressService>();
        services.AddScoped<IEmployeeFamilyService, EmployeeFamilyService>();
        services.AddScoped<IEmployeeEducationService, EmployeeEducationService>();
        services.AddScoped<IEmployeePreviousEmploymentService, EmployeePreviousEmploymentService>();
        services.AddScoped<IEmployeeBankDetailService, EmployeeBankDetailService>();
        services.AddScoped<IEmployeeSupervisorService, EmployeeSupervisorService>();
        services.AddScoped<IEmployeeAdditionalInfoService, EmployeeAdditionalInfoService>();
        services.AddScoped<IEmployeeEmploymentService, EmployeeEmploymentService>();
        services.AddScoped<IEmployeeAuditService, EmployeeAuditService>();
        services.AddScoped<IEmployeeDocumentService, EmployeeDocumentService>();
        services.AddScoped<IImportBatchService, ImportBatchService>();
        services.AddScoped<IMasterLookupService, MasterLookupService>();

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
