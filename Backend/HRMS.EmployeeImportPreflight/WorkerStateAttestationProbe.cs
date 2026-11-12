using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using HRMS.API.Controllers;

namespace HRMS.EmployeeImportPreflight;

public sealed record ApiProcessForAttestation(
    int ProcessId,
    long? ProcessStartIdentity,
    IReadOnlyList<int> ListeningPorts,
    string? ListenerDiscoveryDiagnostic = null,
    string? ListenerDiscoveryDetail = null);

/// <summary>Attests existing API processes as normal APIs during either readiness mode.</summary>
public static class WorkerStateAttestationProbe
{
    public static async Task<IReadOnlyList<ReadinessProcess>> AttestProcessesAsync(
        IReadOnlyList<ApiProcessForAttestation> discovered,
        string bearerToken,
        HttpClient client,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ReadinessProcess>(discovered.Count);
        foreach (var process in discovered)
        {
            var diagnostic = process.ProcessStartIdentity is null
                ? "process.start_identity_unavailable"
                : process.ListeningPorts.Count == 0
                    ? process.ListenerDiscoveryDiagnostic ?? "listener.no_ports_discovered"
                    : "attestation.not_attempted";
            ReadinessProcess result = new(process.ProcessId, process.ProcessStartIdentity,
                AttestationDiagnostic: diagnostic,
                ListenerDiscoveryDiagnostic: process.ListenerDiscoveryDiagnostic,
                ListenerDiscoveryDetail: process.ListenerDiscoveryDetail);
            if (process.ProcessStartIdentity is null || process.ListeningPorts.Count == 0)
            {
                results.Add(result);
                continue;
            }

            foreach (var port in process.ListeningPorts)
            {
                var challenge = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                using var request = CreateRequest(port, bearerToken, challenge);
                try
                {
                    using var response = await client.SendAsync(request, cancellationToken);
                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        diagnostic = response.StatusCode switch
                        {
                            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "authentication.rejected",
                            _ => $"http.unexpected_status_{(int)response.StatusCode}"
                        };
                        continue;
                    }

                    WorkerStateAttestation? attestation;
                    try
                    {
                        attestation = await JsonSerializer.DeserializeAsync<WorkerStateAttestation>(
                            await response.Content.ReadAsStreamAsync(cancellationToken),
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);
                    }
                    catch (JsonException)
                    {
                        diagnostic = "response.parse_failed";
                        continue;
                    }
                    if (attestation is null)
                    {
                        diagnostic = "response.empty";
                        continue;
                    }

                    var echoedChallenge = response.Headers.TryGetValues(
                        WorkerStateAttestationController.ChallengeResponseHeader, out var values)
                        ? values.SingleOrDefault()
                        : null;
                    var processIdMatches = attestation.ProcessId == process.ProcessId;
                    var startIdentityMatches = attestation.ProcessStartIdentity == process.ProcessStartIdentity;
                    var challengeMatches = WorkerStateAttestationRules.IsValidChallenge(challenge)
                        && string.Equals(challenge, echoedChallenge, StringComparison.Ordinal);
                    var identityMatches = DeploymentReadinessRules.AttestationIdentityMatches(
                        new ReadinessProcess(process.ProcessId, process.ProcessStartIdentity),
                        attestation, challenge, echoedChallenge);
                    diagnostic = !processIdMatches ? "attestation.pid_mismatch"
                        : !startIdentityMatches ? "attestation.start_identity_mismatch"
                        : !challengeMatches ? "attestation.challenge_mismatch"
                        : !attestation.AttestationValid ? "attestation.invalid"
                        : "attestation.verified";
                    result = new ReadinessProcess(
                        process.ProcessId,
                        process.ProcessStartIdentity,
                        identityMatches ? attestation.EmployeeBulkImportWorkerEnabled : null,
                        identityMatches && attestation.AttestationValid,
                        AttestationAvailable: identityMatches,
                        AttestationDiagnostic: diagnostic,
                        ListenerDiscoveryDiagnostic: process.ListenerDiscoveryDiagnostic,
                        ListenerDiscoveryDetail: process.ListenerDiscoveryDetail);
                    break;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    diagnostic = "transport.timeout";
                }
                catch (HttpRequestException exception)
                {
                    diagnostic = ClassifyTransportFailure(exception);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    diagnostic = "attestation.probe_failed";
                }
            }
            result = result with { AttestationDiagnostic = diagnostic };
            results.Add(result);
        }
        return results;
    }

    private static string ClassifyTransportFailure(HttpRequestException exception)
    {
        // Keep diagnostics allow-listed. Never include exception text, which can contain
        // host names, proxy configuration, or other machine-specific details.
        var socketError = exception.InnerException switch
        {
            SocketException socket => socket.SocketErrorCode,
            IOException { InnerException: SocketException socket } => socket.SocketErrorCode,
            _ => (SocketError?)null
        };

        return socketError switch
        {
            SocketError.ConnectionRefused => "transport.connection_refused",
            SocketError.HostUnreachable or SocketError.NetworkUnreachable => "transport.network_unreachable",
            SocketError.AddressNotAvailable => "transport.address_unavailable",
            SocketError.ConnectionReset or SocketError.ConnectionAborted or SocketError.Shutdown => "transport.connection_closed",
            SocketError.TimedOut => "transport.timeout",
            SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoRecovery => "transport.name_resolution_failed",
            null => "transport.connection_failed",
            _ => "transport.connection_failed"
        };
    }

    public static HttpRequestMessage CreateRequest(int port, string bearerToken, string challenge)
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"http://127.0.0.1:{port}/api/deployment/worker-state?profile=normal");
        request.Headers.Host = "web1.localhost";
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        request.Headers.TryAddWithoutValidation(WorkerStateAttestationController.ChallengeHeader, challenge);
        return request;
    }
}
