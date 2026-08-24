using System.Diagnostics.CodeAnalysis;

namespace HRMS.API.Security;

/// <summary>
/// Decides whether a browser origin may make credentialed cross-origin calls to this API.
/// <para>
/// Before whitelabel hosts, the answer was a frozen list: <c>Cors:AllowedOrigins</c> held the one dev client
/// and that was that. Every organization now has its own address, so a frozen list would mean a
/// configuration edit and a restart per customer — and the shortcut people reach for instead is
/// <c>AllowAnyOrigin</c>, which ASP.NET Core will not even combine with <c>AllowCredentials</c>, or a
/// reflect-the-header predicate, which is <c>AllowAnyOrigin</c> with the safety catch filed off.
/// </para>
/// <para>
/// So: a union of exact origins and workspace patterns, and nothing else is allowed. Both halves are
/// compared as parsed origins rather than as strings, and — this is the part worth keeping — the configured
/// side and the incoming side go through the same parser, so normalization can never disagree between them.
/// A list that lowercased one side only would refuse <c>HTTPS://Demo01.Hrms.Example</c>, which is a legal
/// thing for a browser to send.
/// </para>
/// </summary>
public sealed class CorsOriginPolicy
{
    /// <summary>Canonical origin keys, so comparison is by parsed value rather than by spelling.</summary>
    private readonly HashSet<string> _exactOrigins;

    private readonly WorkspaceOriginPattern[] _workspacePatterns;

    private CorsOriginPolicy(HashSet<string> exactOrigins, WorkspaceOriginPattern[] workspacePatterns)
    {
        _exactOrigins = exactOrigins;
        _workspacePatterns = workspacePatterns;
    }

    /// <summary>True when nothing at all is allowed, which is what an unconfigured deployment gets.</summary>
    public bool IsEmpty => _exactOrigins.Count == 0 && _workspacePatterns.Length == 0;

    /// <summary>Builds the policy from the "Cors" section, throwing on any entry that is not an origin.</summary>
    public static CorsOriginPolicy FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return FromSettings(
            configuration.GetSection(CorsSettings.SectionName).Get<CorsSettings>() ?? new CorsSettings());
    }

    /// <summary>
    /// Builds the policy from already-bound settings. Malformed entries throw here, at startup, rather than
    /// being dropped: a silently discarded origin presents as "the browser is blocking us" days later, with
    /// nothing in the logs pointing at the typo that caused it.
    /// </summary>
    public static CorsOriginPolicy FromSettings(CorsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var exactOrigins = new HashSet<string>(StringComparer.Ordinal);
        foreach (var configured in settings.AllowedOrigins ?? [])
        {
            // A blank entry is an editing artefact — a trailing comma in JSON, an unset environment
            // variable — and allows nothing, so it is skipped rather than treated as a mistake.
            if (string.IsNullOrWhiteSpace(configured))
            {
                continue;
            }

            if (!TryParseOrigin(configured, out var origin))
            {
                throw new InvalidOperationException(
                    $"'{CorsSettings.SectionName}:{nameof(CorsSettings.AllowedOrigins)}' entry "
                    + $"'{configured}' is not an origin. Expected scheme, host and optional port only — "
                    + "for example 'https://app.hrms.example'.");
            }

            exactOrigins.Add(CanonicalKey(origin));
        }

        var workspacePatterns = (settings.WorkspaceOriginTemplates ?? [])
            .Where(template => !string.IsNullOrWhiteSpace(template))
            .Select(WorkspaceOriginPattern.Parse)
            .ToArray();

        return new CorsOriginPolicy(exactOrigins, workspacePatterns);
    }

    /// <summary>
    /// Whether this origin may call the API with credentials. Anything that is not an origin this policy
    /// recognises is refused, which means no <c>Access-Control-Allow-Origin</c> header at all and the browser
    /// discarding the response — a refusal, not an error the caller can distinguish from a bad route.
    /// </summary>
    public bool IsAllowed(string? origin)
    {
        if (!TryParseOrigin(origin, out var parsed))
        {
            return false;
        }

        if (_exactOrigins.Contains(CanonicalKey(parsed)))
        {
            return true;
        }

        foreach (var pattern in _workspacePatterns)
        {
            if (pattern.Matches(parsed))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The one parser both sides use. Strict on purpose: an <c>Origin</c> header is only ever a scheme, a
    /// host and an optional port, so anything carrying a path, a query, a fragment or credentials is either
    /// not an origin or is trying to be read as two different things by two different comparisons.
    /// </summary>
    internal static bool TryParseOrigin(string? value, [NotNullWhen(true)] out Uri? origin)
    {
        origin = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();

        // Browsers send the literal string "null" for a sandboxed iframe, a file:// document, and some
        // cross-origin redirects. It is not a host, it is the absence of one, and granting it would grant
        // every one of those at once — including a local HTML file the user was talked into opening.
        if (string.Equals(trimmed, "null", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            && !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            return false;
        }

        // Uri normalizes both "http://host" and "http://host/" to an absolute path of "/", so a trailing
        // slash is tolerated while an actual path is not.
        if (parsed.AbsolutePath is not "/"
            || parsed.Query.Length > 0
            || parsed.Fragment.Length > 0
            || parsed.UserInfo.Length > 0)
        {
            return false;
        }

        if (parsed.HostNameType is UriHostNameType.Unknown or UriHostNameType.Basic)
        {
            return false;
        }

        origin = parsed;
        return true;
    }

    /// <summary>
    /// Scheme, host and effective port — the three things that make an origin, and nothing that makes two
    /// spellings of one origin look like two. <see cref="Uri.IdnHost"/> rather than
    /// <see cref="Uri.Host"/> so a Unicode host and its punycode form collapse to the same key; the port is
    /// always written out so an elided default cannot differ from an explicit one.
    /// </summary>
    private static string CanonicalKey(Uri origin) =>
        $"{origin.Scheme}://{origin.IdnHost}:{origin.Port}";
}
