namespace HRMS.Domain.Enums;

public enum WelcomeInvitationStatus
{
    NotSent = 1,
    Queued = 2,
    Processing = 3,
    Sent = 4,
    Failed = 5,
    Activated = 6
}

public enum WelcomeInvitationAttemptType
{
    Initial = 1,
    Resend = 2,
    Retry = 3
}

public enum WelcomeInvitationBatchStatus
{
    Queued = 1,
    Processing = 2,
    Completed = 3,
    CompletedWithFailures = 4
}
