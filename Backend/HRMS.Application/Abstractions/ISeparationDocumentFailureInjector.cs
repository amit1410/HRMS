namespace HRMS.Application.Abstractions;

public interface ISeparationDocumentFailureInjector
{
    void BeforeGenerationCommit();
    void BeforeStorageCommit();
    void BeforeApprovalCommit();
}
