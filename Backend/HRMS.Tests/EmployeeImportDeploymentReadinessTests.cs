using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HRMS.EmployeeImportPreflight;
using HRMS.API.Controllers;

namespace HRMS.Tests;

public sealed class EmployeeImportDeploymentReadinessTests
{
    [Theory]
    [InlineData(typeof(UnauthorizedAccessException), "diagnostic.access_denied")]
    [InlineData(typeof(TimeoutException), "diagnostic.timeout")]
    [InlineData(typeof(InvalidOperationException), "diagnostic.invalid_state")]
    [InlineData(typeof(ArgumentException), "diagnostic.invalid_input")]
    public void DiagnosticExceptionsExposeOnlySanitizedCategories(Type exceptionType, string expectedCategory)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, "sensitive detail")!;

        Assert.Equal(expectedCategory, DeploymentReadinessRules.SafeDiagnosticExceptionCategory(exception));
    }

    [Fact]
    public void DatabaseExceptionsUseSanitizedConnectionCategory()
    {
        Assert.Equal("database.connection_failed",
            DeploymentReadinessRules.SafeDiagnosticExceptionCategory(new TestDbException()));
    }

    private sealed class TestDbException() : System.Data.Common.DbException("sensitive detail");

    [Fact]
    public void ListenerInventoryRetriesTransientEmptyOrFailedSnapshotsButNotVerifiedOrAmbiguousOwners()
    {
        Assert.True(ListenerInventoryRetryPolicy.ShouldRetry(true, ["listener.no_ports_found"], attemptIndex: 0));
        Assert.True(ListenerInventoryRetryPolicy.ShouldRetry(true, ["listener.query_failed"], attemptIndex: 1));
        Assert.True(ListenerInventoryRetryPolicy.ShouldRetry(true, ["listener.snapshot_inconsistent"], attemptIndex: 0));
        Assert.False(ListenerInventoryRetryPolicy.ShouldRetry(true, ["listener.verified"], attemptIndex: 0));
        Assert.False(ListenerInventoryRetryPolicy.ShouldRetry(true, ["listener.owner_ambiguous"], attemptIndex: 0));
        Assert.False(ListenerInventoryRetryPolicy.ShouldRetry(true, ["listener.query_failed"], attemptIndex: ListenerInventoryRetryPolicy.MaximumAttempts - 1));
    }

    [Fact]
    public void WorkerConfigurationPassesOnlyForExactTargetAndAllOtherWorkersDisabled()
    {
        var valid = new ImportWorkerConfiguration(
            Enabled: true,
            TenantCode: "WEB1",
            OperationId: DeploymentReadinessRules.TargetOperationId,
            WelcomeInvitationsEnabled: false,
            LeaveRemindersEnabled: false,
            ConfirmationRemindersEnabled: false,
            LeaveAccrualEnabled: false,
            AttendanceDeviceWorkerEnabled: false);

        Assert.All(DeploymentReadinessRules.EvaluateWorkerConfiguration(valid), check => Assert.True(check.Passed));
    }

    [Theory]
    [InlineData("WEB2", false)]
    [InlineData("WEB1", true)]
    public void WorkerConfigurationRejectsWrongTargetOrUnrelatedWorker(string tenantCode, bool welcomeEnabled)
    {
        var invalid = new ImportWorkerConfiguration(
            Enabled: true,
            TenantCode: tenantCode,
            OperationId: DeploymentReadinessRules.TargetOperationId,
            WelcomeInvitationsEnabled: welcomeEnabled,
            LeaveRemindersEnabled: false,
            ConfirmationRemindersEnabled: false,
            LeaveAccrualEnabled: false,
            AttendanceDeviceWorkerEnabled: false);

        Assert.Contains(DeploymentReadinessRules.EvaluateWorkerConfiguration(invalid), check => !check.Passed);
    }

    [Fact]
    public void NormalApiConfigurationPassesOnlyWhenImportWorkerIsDisabled()
    {
        var disabled = new ImportWorkerConfiguration(
            Enabled: false,
            TenantCode: "WEB1",
            OperationId: DeploymentReadinessRules.TargetOperationId,
            WelcomeInvitationsEnabled: true,
            LeaveRemindersEnabled: false,
            ConfirmationRemindersEnabled: false,
            LeaveAccrualEnabled: false,
            AttendanceDeviceWorkerEnabled: false);

        Assert.All(DeploymentReadinessRules.EvaluateNormalApiConfiguration(disabled), check => Assert.True(check.Passed));

        var enabled = disabled with { Enabled = true };
        Assert.Contains(DeploymentReadinessRules.EvaluateNormalApiConfiguration(enabled), check => !check.Passed);
    }

    [Fact]
    public void ProcessInventoryBlocksUnknownWorkerStateAndUnavailableInventory()
    {
        var unknownProcess = DeploymentReadinessRules.EvaluateProcessInventory(true,
            [new ReadinessProcess(34236, 100, null, false, false)]);
        var enabledProcess = DeploymentReadinessRules.EvaluateProcessInventory(true,
            [new ReadinessProcess(34237, 101, true, true, true)]);
        var verifiedDisabled = DeploymentReadinessRules.EvaluateProcessInventory(true,
            [new ReadinessProcess(34238, 102, false, true, true)]);
        var staleAttestation = DeploymentReadinessRules.EvaluateProcessInventory(true,
            [new ReadinessProcess(34239, 103, false, false, true)]);
        var unavailable = DeploymentReadinessRules.EvaluateProcessInventory(false, []);

        Assert.False(unknownProcess.Passed);
        Assert.Equal("process.attestation_unavailable", unknownProcess.Category);
        Assert.False(enabledProcess.Passed);
        Assert.True(verifiedDisabled.Passed);
        Assert.False(staleAttestation.Passed);
        Assert.Equal("process.attestation_invalid_or_stale", staleAttestation.Category);
        Assert.False(unavailable.Passed);
        Assert.Equal("process.inventory_unavailable", unavailable.Category);
    }

    [Fact]
    public void AttestationRejectsUnavailableEndpointAndInvalidOrMismatchedResponse()
    {
        const string challenge = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var process = new ReadinessProcess(34236, 638975000000000000, false, true, true);
        var valid = new WorkerStateAttestation(34236, 638975000000000000, false, true);

        Assert.False(DeploymentReadinessRules.AttestationMatches(process, null, challenge, challenge));
        Assert.False(DeploymentReadinessRules.AttestationMatches(process, valid with { AttestationValid = false }, challenge, challenge));
        Assert.False(DeploymentReadinessRules.AttestationMatches(process, valid, challenge, new string('f', 64)));
        Assert.False(DeploymentReadinessRules.AttestationMatches(process, valid, challenge, challenge[..^1]));
    }

    [Fact]
    public void AttestationRejectsRestartAndPidReuseByComparingProcessStartIdentity()
    {
        const string challenge = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var currentProcess = new ReadinessProcess(34236, 200, false, true, true);
        var staleResponse = new WorkerStateAttestation(34236, 199, false, true);
        var reusedPidResponse = new WorkerStateAttestation(34236, 201, false, true);

        Assert.False(DeploymentReadinessRules.AttestationMatches(currentProcess, staleResponse, challenge, challenge));
        Assert.False(DeploymentReadinessRules.AttestationMatches(currentProcess, reusedPidResponse, challenge, challenge));
    }

    [Fact]
    public void AttestationRejectsMismatchedProcessIdentityAndConfiguration()
    {
        const string challenge = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var process = new ReadinessProcess(34236, 300, false, true, true);

        Assert.False(DeploymentReadinessRules.AttestationMatches(process,
            new WorkerStateAttestation(34237, 300, false, true), challenge, challenge));
        Assert.False(DeploymentReadinessRules.AttestationMatches(process,
            new WorkerStateAttestation(34236, 300, true, false), challenge, challenge));
    }

    [Theory]
    [InlineData("Normal")]
    [InlineData("Import")]
    public async Task BothReadinessModesAttestTheSameExistingNormalApi(string readinessMode)
    {
        const string bearerToken = "test-only-token";
        const int port = 5080;
        const int processId = 25972;
        const long processStartIdentity = 134379813028797053;
        var requestCount = 0;
        using var handler = new AttestationHandler(request =>
        {
            requestCount++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"http://127.0.0.1:{port}/api/deployment/worker-state?profile=normal", request.RequestUri!.ToString());
            Assert.Equal("web1.localhost", request.Headers.Host);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(bearerToken, request.Headers.Authorization?.Parameter);

            var challenge = Assert.Single(request.Headers.GetValues(WorkerStateAttestationController.ChallengeHeader));
            Assert.True(WorkerStateAttestationRules.IsValidChallenge(challenge));
            var payload = JsonSerializer.Serialize(new WorkerStateAttestation(
                processId, processStartIdentity, EmployeeBulkImportWorkerEnabled: false, AttestationValid: true));
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            response.Headers.Add(WorkerStateAttestationController.ChallengeResponseHeader, challenge);
            return response;
        });
        using var client = new HttpClient(handler);

        var results = await WorkerStateAttestationProbe.AttestProcessesAsync(
            [new ApiProcessForAttestation(processId, processStartIdentity, [port])], bearerToken, client);

        var result = Assert.Single(results);
        Assert.Contains(readinessMode, new[] { "Normal", "Import" });
        Assert.Equal(1, requestCount);
        Assert.Equal(processId, result.ProcessId);
        Assert.Equal(processStartIdentity, result.ProcessStartIdentity);
        Assert.False(result.WorkerEnabled);
        Assert.True(result.AttestationAvailable);
        Assert.True(result.AttestationValid);
        Assert.True(DeploymentReadinessRules.EvaluateProcessInventory(true, [result]).Passed);
    }

    [Theory]
    [InlineData("invalid_challenge")]
    [InlineData("mismatched_pid")]
    [InlineData("mismatched_start_identity")]
    [InlineData("worker_enabled")]
    public async Task AttestationProbeRejectsInvalidIdentityChallengeOrWorkerConfiguration(string failure)
    {
        const string bearerToken = "test-only-token";
        const int processId = 25972;
        const long processStartIdentity = 134379813028797053;
        using var handler = new AttestationHandler(request =>
        {
            var challenge = Assert.Single(request.Headers.GetValues(WorkerStateAttestationController.ChallengeHeader));
            var responseChallenge = failure == "invalid_challenge" ? "invalid" : challenge;
            var responsePid = failure == "mismatched_pid" ? processId + 1 : processId;
            var responseStart = failure == "mismatched_start_identity" ? processStartIdentity + 1 : processStartIdentity;
            var workerEnabled = failure == "worker_enabled";
            var payload = JsonSerializer.Serialize(new WorkerStateAttestation(
                responsePid, responseStart, workerEnabled, AttestationValid: !workerEnabled));
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            response.Headers.Add(WorkerStateAttestationController.ChallengeResponseHeader, responseChallenge);
            return response;
        });
        using var client = new HttpClient(handler);

        var results = await WorkerStateAttestationProbe.AttestProcessesAsync(
            [new ApiProcessForAttestation(processId, processStartIdentity, [5080])], bearerToken, client);

        var result = Assert.Single(results);
        var check = DeploymentReadinessRules.EvaluateProcessInventory(true, [result]);
        Assert.False(check.Passed);
        if (failure == "worker_enabled")
        {
            Assert.True(result.AttestationAvailable);
            Assert.True(result.WorkerEnabled);
            Assert.False(result.AttestationValid);
            Assert.Equal("process.attestation_invalid_or_stale", check.Category);
        }
        else
        {
            Assert.False(result.AttestationAvailable);
            Assert.Null(result.WorkerEnabled);
            Assert.False(result.AttestationValid);
            Assert.Equal("process.attestation_unavailable", check.Category);
            Assert.Equal(failure switch
            {
                "invalid_challenge" => "attestation.challenge_mismatch",
                "mismatched_pid" => "attestation.pid_mismatch",
                "mismatched_start_identity" => "attestation.start_identity_mismatch",
                _ => "attestation.verified"
            }, result.AttestationDiagnostic);
        }
    }

    [Fact]
    public async Task AttestationProbeReportsUnauthorizedAndMissingListenerStagesWithoutSensitiveDetails()
    {
        using var unauthorizedClient = new HttpClient(new AttestationHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("sensitive response body")
            }));
        var unauthorized = Assert.Single(await WorkerStateAttestationProbe.AttestProcessesAsync(
            [new ApiProcessForAttestation(25972, 134379813028797053, [5080])], "test-token", unauthorizedClient));
        Assert.Equal("authentication.rejected", unauthorized.AttestationDiagnostic);
        Assert.DoesNotContain("sensitive", unauthorized.AttestationDiagnostic);

        using var emptyListenerClient = new HttpClient(new AttestationHandler(_ =>
            throw new InvalidOperationException("request must not be sent")));
        var missingListener = Assert.Single(await WorkerStateAttestationProbe.AttestProcessesAsync(
            [new ApiProcessForAttestation(25972, 134379813028797053, [], "listener.fallback_no_ports_found")],
            "test-token", emptyListenerClient));
        Assert.Equal("listener.fallback_no_ports_found", missingListener.AttestationDiagnostic);
        Assert.False(missingListener.AttestationAvailable);
    }

    [Fact]
    public async Task AttestationProbeReportsResponseParsingAndTransportStages()
    {
        using var malformedClient = new HttpClient(new AttestationHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not-json sensitive body")
            }));
        var malformed = Assert.Single(await WorkerStateAttestationProbe.AttestProcessesAsync(
            [new ApiProcessForAttestation(25972, 134379813028797053, [5080])], "test-token", malformedClient));
        Assert.Equal("response.parse_failed", malformed.AttestationDiagnostic);

        using var transportClient = new HttpClient(new AttestationHandler(_ =>
            throw new HttpRequestException("sensitive transport detail")));
        var transport = Assert.Single(await WorkerStateAttestationProbe.AttestProcessesAsync(
            [new ApiProcessForAttestation(25972, 134379813028797053, [5080])], "test-token", transportClient));
        Assert.Equal("transport.connection_failed", transport.AttestationDiagnostic);
        Assert.DoesNotContain("sensitive", transport.AttestationDiagnostic);
    }

    [Theory]
    [InlineData(SocketError.ConnectionRefused, "transport.connection_refused")]
    [InlineData(SocketError.HostUnreachable, "transport.network_unreachable")]
    [InlineData(SocketError.AddressNotAvailable, "transport.address_unavailable")]
    [InlineData(SocketError.ConnectionReset, "transport.connection_closed")]
    [InlineData(SocketError.HostNotFound, "transport.name_resolution_failed")]
    public async Task AttestationProbeClassifiesSocketFailuresWithoutExposingExceptionText(
        SocketError socketError,
        string expectedDiagnostic)
    {
        using var client = new HttpClient(new AttestationHandler(_ =>
            throw new HttpRequestException("sensitive host and proxy details",
                new SocketException((int)socketError))));

        var result = Assert.Single(await WorkerStateAttestationProbe.AttestProcessesAsync(
            [new ApiProcessForAttestation(25972, 134379813028797053, [5080])], "test-token", client));

        Assert.Equal(expectedDiagnostic, result.AttestationDiagnostic);
        Assert.DoesNotContain("sensitive", result.AttestationDiagnostic);
    }

    [Theory]
    [InlineData("listener.no_ports_found", false)]
    [InlineData("listener.netstat_fallback", true)]
    [InlineData("listener.query_failed", true)]
    [InlineData("listener.snapshot_inconsistent", true)]
    [InlineData("listener.owner_ambiguous", false)]
    public async Task ListenerDiscoveryStageIsPreservedAndAttestationRemainsMandatory(
        string discoveryDiagnostic, bool hasFallbackPort)
    {
        const int processId = 25972;
        const long startIdentity = 134379813028797053;
        var requests = 0;
        using var client = new HttpClient(new AttestationHandler(request =>
        {
            requests++;
            var challenge = Assert.Single(request.Headers.GetValues(WorkerStateAttestationController.ChallengeHeader));
            var body = JsonSerializer.Serialize(new WorkerStateAttestation(processId, startIdentity, false, true));
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            response.Headers.Add(WorkerStateAttestationController.ChallengeResponseHeader, challenge);
            return response;
        }));
        var ports = hasFallbackPort ? new[] { 5080 } : Array.Empty<int>();

        var result = Assert.Single(await WorkerStateAttestationProbe.AttestProcessesAsync(
            [new ApiProcessForAttestation(processId, startIdentity, ports, discoveryDiagnostic)],
            "test-token", client));

        Assert.Equal(discoveryDiagnostic, result.ListenerDiscoveryDiagnostic);
        Assert.Equal(hasFallbackPort ? 1 : 0, requests);
        Assert.Equal(hasFallbackPort, result.AttestationAvailable);
        Assert.Equal(hasFallbackPort ? "attestation.verified" : discoveryDiagnostic, result.AttestationDiagnostic);
    }

    [Fact]
    public void ProcessInventoryPassesWhenNoApiProcessExists()
    {
        var check = DeploymentReadinessRules.EvaluateProcessInventory(true, []);

        Assert.True(check.Passed);
        Assert.Equal("process.none_found", check.Category);
    }

    [Fact]
    public void CapacityRequiresVerifiedLocalDatabaseAndPositiveFreeSpace()
    {
        Assert.True(DeploymentReadinessRules.EvaluateCapacity(true, "hrms_web1", 1024).Passed);
        Assert.False(DeploymentReadinessRules.EvaluateCapacity(false, "hrms_web1", 1024).Passed);
        Assert.False(DeploymentReadinessRules.EvaluateCapacity(true, "hrms_web1", 0).Passed);
        Assert.False(DeploymentReadinessRules.EvaluateCapacity(true, "other_db", 1024).Passed);
    }

    [Fact]
    public void ConfigurationPrecedencePutsMountedConfigurationLast()
    {
        var precedence = DeploymentReadinessRules.ConfigurationPrecedence(development: true, mountedConfigurationLoaded: true);

        Assert.Equal("appsettings.json", precedence[0]);
        Assert.Contains("UserSecrets", precedence);
        Assert.EndsWith("overrides prior providers)", precedence[^1], StringComparison.Ordinal);
    }

    private sealed class AttestationHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
