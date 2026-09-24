namespace HRMS.Domain.Enums;

public enum SeparationDocumentType
{
    RelievingLetter = 1,
    ExperienceLetter = 2,
    ServiceCertificate = 3,
    CustomSeparationDocument = 4
}

public enum SeparationDocumentTemplateVersionStatus
{
    Draft = 1,
    Published = 2,
    Retired = 3
}

public enum SeparationGeneratedDocumentStatus
{
    DraftGenerated = 1,
    PendingApproval = 2,
    Approved = 3,
    Issued = 4,
    Superseded = 5,
    Cancelled = 6
}

public enum SeparationDocumentEventType
{
    TemplateCreated = 1,
    TemplateVersionCreated = 2,
    TemplateVersionPublished = 3,
    DocumentGenerated = 4,
    DocumentApprovalRequested = 5,
    DocumentApproved = 6,
    DocumentIssued = 7,
    DocumentGenerationFailed = 8,
    DocumentSuperseded = 9,
    DocumentCancelled = 10,
    DocumentViewed = 11,
    DocumentDownloaded = 12
}
