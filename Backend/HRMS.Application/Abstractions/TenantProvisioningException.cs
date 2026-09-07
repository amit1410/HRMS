namespace HRMS.Application.Abstractions;

/// <summary>Safe operator-facing classification for a tenant provisioning failure.</summary>
public sealed class TenantProvisioningException : InvalidOperationException
{
    public TenantProvisioningException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    public string Code { get; }
}
