using HRMS.API.Security;

namespace HRMS.Tests;

/// <summary>
/// The CORS origin predicate, on its own. This is where the accept/refuse matrix belongs: the pipeline tests
/// prove the predicate is wired to the policy, but they can only ask about the one configuration the test
/// host runs, and the interesting cases are the ones a wildcard or a mis-anchored regex would get wrong.
/// <para>
/// Every refusal here is a real attack shape rather than a tidy edge case. A predicate that answered "yes" to
/// any of them would hand a credentialed cross-origin grant to a host the deployment does not own, and the
/// browser would then let that page read every response — including one carrying an access token.
/// </para>
/// </summary>
public class CorsOriginPolicyTests
{
    private const string ExactOrigin = "https://app.hrms.example";
    private const string WorkspaceTemplate = "https://{workspace}.hrms.example";

    private static CorsOriginPolicy Policy(
        string[]? exactOrigins = null, string[]? workspaceTemplates = null) =>
        CorsOriginPolicy.FromSettings(new CorsSettings
        {
            AllowedOrigins = exactOrigins ?? [ExactOrigin],
            WorkspaceOriginTemplates = workspaceTemplates ?? [WorkspaceTemplate]
        });

    /// <summary>
    /// The shipped default. An API with no CORS configuration allows no cross-origin caller at all, which is
    /// the only safe reading of "nobody said" — the alternative, reflecting whatever origin asks, is how a
    /// deployment ends up trusting the internet.
    /// </summary>
    [Fact]
    public void An_unconfigured_policy_allows_nothing()
    {
        var policy = CorsOriginPolicy.FromSettings(new CorsSettings());

        Assert.True(policy.IsEmpty);
        Assert.False(policy.IsAllowed(ExactOrigin));
        Assert.False(policy.IsAllowed("https://demo01.hrms.example"));
    }

    [Fact]
    public void A_configured_policy_is_not_empty()
    {
        Assert.False(Policy().IsEmpty);
        Assert.False(Policy(exactOrigins: []).IsEmpty);
        Assert.False(Policy(workspaceTemplates: []).IsEmpty);
    }

    [Theory]
    [InlineData("https://app.hrms.example")]
    // Origins are compared as parsed values, so casing and an explicitly-written default port are the same
    // origin. A browser may send either, and a string comparison would refuse both.
    [InlineData("HTTPS://APP.HRMS.EXAMPLE")]
    [InlineData("https://App.Hrms.Example")]
    [InlineData("https://app.hrms.example:443")]
    [InlineData("https://app.hrms.example/")]
    public void An_exact_origin_is_allowed_however_it_is_spelled(string origin)
    {
        Assert.True(Policy(workspaceTemplates: []).IsAllowed(origin));
    }

    [Theory]
    [InlineData("http://app.hrms.example")]        // scheme downgrade
    [InlineData("https://app.hrms.example:8443")]  // different port
    [InlineData("https://hrms.example")]           // the parent, not the configured host
    [InlineData("https://x.app.hrms.example")]     // a label below it
    [InlineData("https://app.hrms.example.evil.test")]
    public void An_exact_origin_admits_nothing_around_it(string origin)
    {
        Assert.False(Policy(workspaceTemplates: []).IsAllowed(origin));
    }

    /// <summary>
    /// The point of the whole change: an organization's address is allowed without anyone having configured
    /// it. Nothing in this list appears in <see cref="WorkspaceTemplate"/> or anywhere else.
    /// </summary>
    [Theory]
    [InlineData("https://demo01.hrms.example")]
    [InlineData("https://a.hrms.example")]
    [InlineData("https://onboarded-yesterday.hrms.example")]
    [InlineData("https://123.hrms.example")]
    [InlineData("HTTPS://DEMO01.HRMS.EXAMPLE")]
    [InlineData("https://demo01.hrms.example:443")]
    public void One_workspace_label_under_the_template_is_allowed(string origin)
    {
        Assert.True(Policy().IsAllowed(origin));
    }

    [Fact]
    public void A_label_may_be_as_long_as_dns_allows_and_no_longer()
    {
        var policy = Policy();

        Assert.True(policy.IsAllowed($"https://{new string('a', 63)}.hrms.example"));
        Assert.False(policy.IsAllowed($"https://{new string('a', 64)}.hrms.example"));
    }

    /// <summary>
    /// The refusals a wildcard would get wrong, and the ones a regex gets wrong when a dot is unescaped or an
    /// anchor is missing. Each is named for the mistake it detects, because a bare list of hosts here reads
    /// like arbitrary trivia six months from now.
    /// </summary>
    [Theory]
    // Nested subdomains: one compromised host under a customer's own label must not become a trusted origin.
    [InlineData("https://a.b.hrms.example")]
    [InlineData("https://demo01.internal.hrms.example")]
    // Suffix confusion — the base domain appears, but the origin belongs to someone else.
    [InlineData("https://demo01.hrms.example.evil.test")]
    [InlineData("https://hrms.example.evil.test")]
    // Prefix confusion: the boundary character is not a dot, so this is one label, not two.
    [InlineData("https://evil-hrms.example")]
    [InlineData("https://notdemo01hrms.example")]
    // What an unescaped dot in a regex would match.
    [InlineData("https://demo01Xhrms.example")]
    [InlineData("https://demo01.hrmsXexample")]
    // The apex itself is not a workspace. It belongs in the exact list if it is wanted at all.
    [InlineData("https://hrms.example")]
    // A label cannot be empty, nor start or end with a hyphen.
    [InlineData("https://.hrms.example")]
    [InlineData("https://-demo01.hrms.example")]
    [InlineData("https://demo01-.hrms.example")]
    // Scheme and port are pinned by the template, not free.
    [InlineData("http://demo01.hrms.example")]
    [InlineData("https://demo01.hrms.example:8443")]
    // A different base domain entirely, however similar.
    [InlineData("https://demo01.hrms.example.co")]
    [InlineData("https://demo01.hrms-example")]
    public void Anything_that_is_not_one_label_under_the_template_is_refused(string origin)
    {
        Assert.False(Policy().IsAllowed(origin));
    }

    /// <summary>
    /// Not origins at all. <c>"null"</c> is the one that matters: browsers send it for a sandboxed iframe, a
    /// <c>file://</c> document and some cross-origin redirects, so allowing it allows all of them at once —
    /// including a local HTML file a user was talked into opening.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    [InlineData("NULL")]
    [InlineData("file:///c:/tmp/page.html")]
    [InlineData("ftp://demo01.hrms.example")]
    [InlineData("chrome-extension://abcdefghijklmnop")]
    [InlineData("app.hrms.example")]
    [InlineData("//app.hrms.example")]
    // An origin is a scheme, a host and a port. Anything more is two things being compared as one.
    [InlineData("https://app.hrms.example/admin")]
    [InlineData("https://app.hrms.example?x=1")]
    [InlineData("https://app.hrms.example#f")]
    [InlineData("https://user:secret@app.hrms.example")]
    public void What_is_not_an_origin_is_refused(string? origin)
    {
        Assert.False(Policy().IsAllowed(origin));
    }

    [Fact]
    public void Exact_origins_and_workspace_templates_are_a_union()
    {
        var policy = Policy();

        Assert.True(policy.IsAllowed(ExactOrigin));
        Assert.True(policy.IsAllowed("https://demo01.hrms.example"));
        Assert.False(policy.IsAllowed("https://elsewhere.test"));
    }

    [Fact]
    public void Several_templates_can_coexist()
    {
        var policy = Policy(
            exactOrigins: [],
            workspaceTemplates: ["https://{workspace}.hrms.example", "http://{workspace}.localhost:5173"]);

        Assert.True(policy.IsAllowed("https://demo01.hrms.example"));
        Assert.True(policy.IsAllowed("http://demo01.localhost:5173"));
        Assert.False(policy.IsAllowed("http://demo01.hrms.example"));
        Assert.False(policy.IsAllowed("https://demo01.localhost:5173"));
    }

    [Fact]
    public void Blank_entries_are_ignored_rather_than_treated_as_mistakes()
    {
        var policy = Policy(exactOrigins: [ExactOrigin, "", "   "], workspaceTemplates: ["", WorkspaceTemplate]);

        Assert.True(policy.IsAllowed(ExactOrigin));
        Assert.True(policy.IsAllowed("https://demo01.hrms.example"));
    }

    /// <summary>
    /// Configuration mistakes stop startup. Dropping a malformed entry would leave a deployment that refuses
    /// the origin it thinks it allows, and the only symptom is the browser blaming CORS days later.
    /// </summary>
    [Theory]
    [InlineData("app.hrms.example")]
    [InlineData("https://app.hrms.example/base")]
    [InlineData("not a url at all")]
    public void A_malformed_exact_origin_stops_startup(string configured)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Policy(exactOrigins: [configured], workspaceTemplates: []));

        Assert.Contains(configured, error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(CorsSettings.AllowedOrigins), error.Message, StringComparison.Ordinal);
    }

    [Theory]
    // No placeholder at all: this is an exact origin written in the wrong list, and it would silently allow
    // nothing rather than allowing that one host.
    [InlineData("https://app.hrms.example")]
    // A placeholder that is not the leading label. Both of these would match parts of a label, which is
    // exactly how 'hrms.example' comes to admit 'evil-hrms.example'.
    [InlineData("https://app.{workspace}.hrms.example")]
    [InlineData("https://{workspace}-app.hrms.example")]
    [InlineData("https://app-{workspace}.hrms.example")]
    [InlineData("https://{workspace}.{workspace}.hrms.example")]
    [InlineData("{workspace}.hrms.example")]
    [InlineData("https://{workspace}.hrms.example/base")]
    public void A_malformed_template_stops_startup(string configured)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Policy(exactOrigins: [], workspaceTemplates: [configured]));

        Assert.Contains(configured, error.Message, StringComparison.Ordinal);
    }
}
