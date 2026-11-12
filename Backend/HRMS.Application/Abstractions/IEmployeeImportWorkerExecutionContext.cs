namespace HRMS.Application.Abstractions;

/// <summary>Scoped service identity enabled only while processing an already authorized durable import.</summary>
public interface IEmployeeImportWorkerExecutionContext : ICurrentAuthorizationContext
{
    Guid? UserId { get; }
    bool IsActive { get; }
    void Activate(Guid initiatingUserId);
    void Deactivate();
}
