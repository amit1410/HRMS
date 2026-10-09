using HRMS.Application.Common;
using HRMS.Application.DTOs.Confirmation;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

/// <summary>
/// Phase 9c-1 — Confirmation review-question &amp; rating-scale masters: CRUD, validation (type-specific
/// rules, scale bounds, option/rating-value integrity), and tenant isolation. These are configuration
/// masters only — nothing here reads or writes employee confirmation data.
/// </summary>
public sealed class ConfirmationReviewMasterTests
{
    private static ConfirmationRatingScaleService ScaleService(HrmsDbContext ctx, TestTenantContext tenant) => new(ctx, tenant);
    private static ConfirmationReviewQuestionService QuestionService(HrmsDbContext ctx, TestTenantContext tenant) => new(ctx, tenant);

    /// <summary>Creates a context for the tenant and seeds its <see cref="Tenant"/> row (FKs are enforced).</summary>
    private static HrmsDbContext NewContext(SqliteInMemoryDatabase db, TestTenantContext tenant)
    {
        var ctx = db.CreateContext(tenant);
        var id = tenant.TenantId!.Value;
        if (!ctx.Tenants.Any(t => t.Id == id))
        {
            ctx.Tenants.Add(new Tenant
            {
                Id = id, TenantCode = Guid.NewGuid().ToString("N")[..8], Host = Guid.NewGuid().ToString("N") + ".local",
                ShardKey = Guid.NewGuid().ToString("N"), TenantName = "Test"
            });
            ctx.SaveChanges();
        }
        return ctx;
    }

    private static ConfirmationRatingScaleRequest FivePointScale(string code = "PERF5") => new(
        Code: code, Name: "Five Point", Description: "Standard 1-5", MinRating: 1, MaxRating: 5,
        Values: new[]
        {
            new ConfirmationRatingScaleValueRequest(1, "Unsatisfactory", DisplayOrder: 1),
            new ConfirmationRatingScaleValueRequest(2, "Needs Improvement", DisplayOrder: 2),
            new ConfirmationRatingScaleValueRequest(3, "Meets Expectations", DisplayOrder: 3),
            new ConfirmationRatingScaleValueRequest(4, "Exceeds Expectations", DisplayOrder: 4),
            new ConfirmationRatingScaleValueRequest(5, "Outstanding", DisplayOrder: 5),
        });

    // ── Rating scale ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_rating_scale_persists_values_in_order()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = new TestTenantContext(Guid.NewGuid());
        using var ctx = NewContext(db, tenant);

        var result = await ScaleService(ctx, tenant).CreateScaleAsync(FivePointScale());

        Assert.Equal(ResultStatus.Success, result.Status);
        Assert.Equal(5, result.Value!.Values.Count);
        Assert.Equal("Unsatisfactory", result.Value.Values[0].Label);
        Assert.Equal(5, result.Value.Scale.MaxRating);
    }

    [Fact]
    public async Task Duplicate_scale_code_is_rejected_case_insensitively()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = new TestTenantContext(Guid.NewGuid());
        using var ctx = NewContext(db, tenant);
        await ScaleService(ctx, tenant).CreateScaleAsync(FivePointScale("PERF5"));

        var dup = await ScaleService(ctx, tenant).CreateScaleAsync(FivePointScale("perf5"));

        Assert.Equal(ResultStatus.Conflict, dup.Status);
    }

    [Fact]
    public async Task Scale_rejects_max_not_greater_than_min()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = new TestTenantContext(Guid.NewGuid());
        using var ctx = NewContext(db, tenant);

        var result = await ScaleService(ctx, tenant).CreateScaleAsync(
            new("BAD", "Bad", null, MinRating: 5, MaxRating: 5));

        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
        Assert.Contains(result.Errors!, e => e.Field == "maxRating");
    }

    [Fact]
    public async Task Scale_rejects_value_outside_bounds_and_duplicates()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = new TestTenantContext(Guid.NewGuid());
        using var ctx = NewContext(db, tenant);

        var result = await ScaleService(ctx, tenant).CreateScaleAsync(new(
            "S", "Scale", null, 1, 3,
            Values: new[]
            {
                new ConfirmationRatingScaleValueRequest(9, "Out of range"),
                new ConfirmationRatingScaleValueRequest(2, "Dup"),
                new ConfirmationRatingScaleValueRequest(2, "Dup again"),
            }));

        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
        Assert.Contains(result.Errors!, e => e.Field == "values[0].ratingValue");
        Assert.Contains(result.Errors!, e => e.Field == "values[2].ratingValue");
    }

    [Fact]
    public async Task Update_scale_replaces_value_set()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = new TestTenantContext(Guid.NewGuid());
        using var ctx = NewContext(db, tenant);
        var created = await ScaleService(ctx, tenant).CreateScaleAsync(FivePointScale());
        var id = created.Value!.Scale.Id;

        var updated = await ScaleService(ctx, tenant).UpdateScaleAsync(id, new(
            "PERF5", "Three Point", "Simplified", 1, 3,
            Values: new[]
            {
                new ConfirmationRatingScaleValueRequest(1, "Low"),
                new ConfirmationRatingScaleValueRequest(2, "Mid"),
                new ConfirmationRatingScaleValueRequest(3, "High"),
            }));

        Assert.Equal(ResultStatus.Success, updated.Status);
        Assert.Equal(3, updated.Value!.Values.Count);
        Assert.Equal("Three Point", updated.Value.Scale.Name);
    }

    [Fact]
    public async Task Rating_scales_are_tenant_isolated()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenantA = new TestTenantContext(Guid.NewGuid());
        var tenantB = new TestTenantContext(Guid.NewGuid());

        Guid scaleId;
        using (var ctxA = NewContext(db, tenantA))
            scaleId = (await ScaleService(ctxA, tenantA).CreateScaleAsync(FivePointScale())).Value!.Scale.Id;

        using var ctxB = NewContext(db, tenantB);
        var list = await ScaleService(ctxB, tenantB).GetScalesAsync(new ConfirmationRatingScaleQuery());
        var fetch = await ScaleService(ctxB, tenantB).GetScaleAsync(scaleId);

        Assert.Empty(list.Value!.Items);
        Assert.Equal(ResultStatus.NotFound, fetch.Status);
    }

    // ── Review question ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_rating_question_requires_existing_scale()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = new TestTenantContext(Guid.NewGuid());
        using var ctx = NewContext(db, tenant);

        var missing = await QuestionService(ctx, tenant).CreateQuestionAsync(new(
            "Q1", "Overall performance?", null, ReviewQuestionType.Rating,
            ConfirmationRatingScaleId: Guid.NewGuid()));
        Assert.Equal(ResultStatus.ValidationFailed, missing.Status);
        Assert.Contains(missing.Errors!, e => e.Field == "confirmationRatingScaleId");

        var scale = await ScaleService(ctx, tenant).CreateScaleAsync(FivePointScale());
        var ok = await QuestionService(ctx, tenant).CreateQuestionAsync(new(
            "Q1", "Overall performance?", null, ReviewQuestionType.Rating,
            IsMandatory: true, ConfirmationRatingScaleId: scale.Value!.Scale.Id));

        Assert.Equal(ResultStatus.Success, ok.Status);
        Assert.Equal(scale.Value.Scale.Id, ok.Value!.Question.ConfirmationRatingScaleId);
    }

    [Fact]
    public async Task Dropdown_question_requires_options_and_rejects_scale()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = new TestTenantContext(Guid.NewGuid());
        using var ctx = NewContext(db, tenant);

        var noOptions = await QuestionService(ctx, tenant).CreateQuestionAsync(new(
            "QD", "Pick one", null, ReviewQuestionType.Dropdown));
        Assert.Equal(ResultStatus.ValidationFailed, noOptions.Status);
        Assert.Contains(noOptions.Errors!, e => e.Field == "options");

        var ok = await QuestionService(ctx, tenant).CreateQuestionAsync(new(
            "QD", "Pick one", null, ReviewQuestionType.Dropdown,
            Options: new[]
            {
                new ConfirmationReviewQuestionOptionRequest("YES", "Yes", 1),
                new ConfirmationReviewQuestionOptionRequest("NO", "No", 2),
            }));
        Assert.Equal(ResultStatus.Success, ok.Status);
        Assert.Equal(2, ok.Value!.Options.Count);
    }

    [Fact]
    public async Task Text_question_rejects_options_and_scale()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = new TestTenantContext(Guid.NewGuid());
        using var ctx = NewContext(db, tenant);
        var scale = await ScaleService(ctx, tenant).CreateScaleAsync(FivePointScale());

        var withScale = await QuestionService(ctx, tenant).CreateQuestionAsync(new(
            "QT", "Comments", null, ReviewQuestionType.Text, ConfirmationRatingScaleId: scale.Value!.Scale.Id));
        Assert.Equal(ResultStatus.ValidationFailed, withScale.Status);
        Assert.Contains(withScale.Errors!, e => e.Field == "confirmationRatingScaleId");

        var withOptions = await QuestionService(ctx, tenant).CreateQuestionAsync(new(
            "QT", "Comments", null, ReviewQuestionType.Text,
            Options: new[] { new ConfirmationReviewQuestionOptionRequest("X", "X") }));
        Assert.Equal(ResultStatus.ValidationFailed, withOptions.Status);
        Assert.Contains(withOptions.Errors!, e => e.Field == "options");
    }

    [Fact]
    public async Task Dropdown_rejects_duplicate_option_codes()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = new TestTenantContext(Guid.NewGuid());
        using var ctx = NewContext(db, tenant);

        var result = await QuestionService(ctx, tenant).CreateQuestionAsync(new(
            "QD", "Pick", null, ReviewQuestionType.Dropdown,
            Options: new[]
            {
                new ConfirmationReviewQuestionOptionRequest("A", "A"),
                new ConfirmationReviewQuestionOptionRequest("a", "A again"),
            }));

        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
        Assert.Contains(result.Errors!, e => e.Field == "options[1].code");
    }

    [Fact]
    public async Task Review_questions_are_tenant_isolated()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenantA = new TestTenantContext(Guid.NewGuid());
        var tenantB = new TestTenantContext(Guid.NewGuid());

        Guid questionId;
        using (var ctxA = NewContext(db, tenantA))
            questionId = (await QuestionService(ctxA, tenantA).CreateQuestionAsync(new(
                "QY", "Recommend confirmation?", null, ReviewQuestionType.YesNo))).Value!.Question.Id;

        using var ctxB = NewContext(db, tenantB);
        var list = await QuestionService(ctxB, tenantB).GetQuestionsAsync(new ConfirmationReviewQuestionQuery());
        var fetch = await QuestionService(ctxB, tenantB).GetQuestionAsync(questionId);

        Assert.Empty(list.Value!.Items);
        Assert.Equal(ResultStatus.NotFound, fetch.Status);
    }

    [Fact]
    public async Task Update_question_switching_type_clears_stale_children()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = new TestTenantContext(Guid.NewGuid());
        using var ctx = NewContext(db, tenant);
        var created = await QuestionService(ctx, tenant).CreateQuestionAsync(new(
            "QD", "Pick", null, ReviewQuestionType.Dropdown,
            Options: new[] { new ConfirmationReviewQuestionOptionRequest("A", "A") }));
        var id = created.Value!.Question.Id;

        var updated = await QuestionService(ctx, tenant).UpdateQuestionAsync(id, new(
            "QD", "Free text now", null, ReviewQuestionType.Text));

        Assert.Equal(ResultStatus.Success, updated.Status);
        Assert.Empty(updated.Value!.Options);
        Assert.Equal(ReviewQuestionType.Text, updated.Value.Question.QuestionType);
    }
}
