namespace HRMS.API.Security;

/// <summary>
/// Binding for the "Cors" configuration section.
/// <para>
/// Two lists, and the difference between them is the point. <see cref="AllowedOrigins"/> is exact origins —
/// the apex host, the dev client, anything with no workspace label. <see cref="WorkspaceOriginTemplates"/>
/// is the whitelabel half: one template stands for every organization's own address, so onboarding a
/// customer does not mean editing a CORS list. A deployment needs both, and they are combined as a union.
/// </para>
/// </summary>
public sealed class CorsSettings
{
    public const string SectionName = "Cors";

    /// <summary>
    /// Origins allowed exactly as written, compared after normalization (case, default ports, IDN) rather
    /// than as strings.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];

    /// <summary>
    /// Origins allowed for any single workspace label, written with a <c>{workspace}</c> placeholder as the
    /// leading label — <c>https://{workspace}.hrms.example</c>. Scheme, base domain and port are pinned;
    /// only that one label varies, and it may not itself contain a dot.
    /// <para>
    /// Empty by default, so a deployment that has not thought about this refuses every workspace origin
    /// rather than reflecting whatever asks. The <c>{shardKey}</c> placeholder in
    /// <c>Sharding:ConnectionStringTemplate</c> works the same way for the same reason.
    /// </para>
    /// </summary>
    public string[] WorkspaceOriginTemplates { get; set; } = [];
}
