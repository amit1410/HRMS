namespace HRMS.Application.Abstractions;

public enum EmailDeliveryFailureKind
{
    Timeout,
    Authentication,
    Network,
    Server
}

/// <summary>Safe classification of an email transport failure. The message never contains credentials.</summary>
public sealed class EmailDeliveryException(
    EmailDeliveryFailureKind kind,
    string message,
    string? host,
    int port,
    Exception innerException) : Exception(message, innerException)
{
    public EmailDeliveryFailureKind Kind { get; } = kind;
    public string? Host { get; } = host;
    public int Port { get; } = port;
}
