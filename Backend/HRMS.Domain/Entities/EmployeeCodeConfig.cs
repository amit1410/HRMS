using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

/// <summary>
/// How a tenant's employee codes are produced. One row per tenant, created on demand.
/// <para>
/// When <see cref="AutoGenerate"/> is true the service issues the next code as
/// <c>Prefix + NextNumber</c> (<c>"WE" + 34567 = "WE34567"</c>) and advances the counter. When it is false
/// the client supplies the employee code on each create (as the pre-existing behaviour did) and this row
/// is ignored.
/// </para>
/// </summary>
public class EmployeeCodeConfig : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>When true, the backend issues employee codes automatically; when false the client provides them.</summary>
    public bool AutoGenerate { get; set; } = true;
    public EmployeeCodeAssignmentMode AssignmentMode { get; set; } = EmployeeCodeAssignmentMode.Auto;
    public EmployeeCodeGenerationMethod? GenerationMethod { get; set; } = EmployeeCodeGenerationMethod.Simple;

    /// <summary>The fixed prefix of an auto-generated code, e.g. "WE".</summary>
    public string Prefix { get; set; } = "EMP";

    /// <summary>The next number to hand out; the counter is never reused.</summary>
    public long NextNumber { get; set; } = 1;

    /// <summary>Zero-padding width for the numeric portion, e.g. 4 produces "EMP0001".</summary>
    public int Padding { get; set; } = 0;

    public string Separator { get; set; } = "-";
    public DateOnly EffectiveFrom { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public DateOnly? EffectiveTo { get; set; }

    public ICollection<EmployeeCodeRule> Rules { get; set; } = new List<EmployeeCodeRule>();

    public Tenant? Tenant { get; set; }
}
