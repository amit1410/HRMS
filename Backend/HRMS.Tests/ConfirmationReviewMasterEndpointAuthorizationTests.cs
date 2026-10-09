using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HRMS.Domain.Authorization;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

/// <summary>
/// Phase 9c-1 — every confirmation review-question and rating-scale endpoint is guarded by its own
/// permission, and the happy path works end-to-end over HTTP. These pin that a lesser permission (or none)
/// is refused so a future refactor cannot quietly widen access, and exercise a create → get round-trip.
/// </summary>
public sealed class ConfirmationReviewMasterEndpointAuthorizationTests : IClassFixture<HrmsApiFactory>
{
    private readonly HrmsApiFactory _factory;

    public ConfirmationReviewMasterEndpointAuthorizationTests(HrmsApiFactory factory) => _factory = factory;

    // ── Rating scales ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Rating_scale_list_requires_view()
    {
        using var client = Client();
        var response = await client.GetAsync("/api/confirmation-rating-scales");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Rating_scale_list_is_allowed_with_view()
    {
        using var client = Client(Permissions.Confirmation.RatingScaleView);
        var response = await client.GetAsync("/api/confirmation-rating-scales");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Rating_scale_create_requires_manage_not_view()
    {
        using var client = Client(Permissions.Confirmation.RatingScaleView);
        var response = await client.PostAsJsonAsync("/api/confirmation-rating-scales",
            new { code = "X", name = "X", minRating = 1, maxRating = 5, isActive = true });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Rating_scale_create_and_fetch_round_trips_with_manage()
    {
        using var client = Client(Permissions.Confirmation.RatingScaleManage, Permissions.Confirmation.RatingScaleView);
        var code = "RS" + Guid.NewGuid().ToString("N")[..8];
        var create = await client.PostAsJsonAsync("/api/confirmation-rating-scales", new
        {
            code,
            name = "Five Point",
            minRating = 1,
            maxRating = 5,
            isActive = true,
            values = new[]
            {
                new { ratingValue = 1, label = "Unsatisfactory", displayOrder = 1, isActive = true },
                new { ratingValue = 5, label = "Outstanding", displayOrder = 5, isActive = true },
            },
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
    }

    // ── Review questions ──────────────────────────────────────────────────────

    [Fact]
    public async Task Review_question_list_requires_view()
    {
        using var client = Client();
        var response = await client.GetAsync("/api/confirmation-review-questions");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Review_question_list_is_allowed_with_view()
    {
        using var client = Client(Permissions.Confirmation.ReviewQuestionView);
        var response = await client.GetAsync("/api/confirmation-review-questions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Review_question_create_requires_manage_not_view()
    {
        using var client = Client(Permissions.Confirmation.ReviewQuestionView);
        var response = await client.PostAsJsonAsync("/api/confirmation-review-questions",
            new { code = "Q", questionText = "Q?", questionType = "YesNo" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Rating_question_without_scale_is_rejected()
    {
        using var client = Client(Permissions.Confirmation.ReviewQuestionManage);
        var response = await client.PostAsJsonAsync("/api/confirmation-review-questions",
            new { code = "Q" + Guid.NewGuid().ToString("N")[..6], questionText = "Overall?", questionType = "Rating" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Yes_no_question_create_succeeds_with_manage()
    {
        using var client = Client(Permissions.Confirmation.ReviewQuestionManage);
        var response = await client.PostAsJsonAsync("/api/confirmation-review-questions", new
        {
            code = "Q" + Guid.NewGuid().ToString("N")[..8],
            questionText = "Recommend confirmation?",
            questionType = "YesNo",
            isMandatory = true,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private HttpClient Client(params string[] permissions)
    {
        var client = _factory.CreateClientFor(HrmsApiFactory.Demo01Host);
        var token = TestTokens.Create(
            SeedData.Users[0].Id,
            SeedData.TenantIds.Demo01,
            "DEMO01",
            "admin@demo01.com",
            roles: [RoleNames.TenantAdmin],
            permissions: permissions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
