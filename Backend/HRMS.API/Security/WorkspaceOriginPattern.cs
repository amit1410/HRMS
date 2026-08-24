namespace HRMS.API.Security;

/// <summary>
/// One whitelabel origin shape: a fixed scheme, port and base domain, with exactly one workspace label in
/// front of it. <c>https://{workspace}.hrms.example</c> admits <c>https://demo01.hrms.example</c> and
/// nothing else.
/// <para>
/// Matching is structural — scheme, port and host compared as parsed values — rather than a regular
/// expression over the raw <c>Origin</c> header. A regex has to get anchoring, dot-escaping and the label
/// character class all right at once, and the cost of getting any of them wrong is credentialed CORS for
/// whoever noticed: <c>hrms.example</c> unescaped matches <c>hrmsXexample</c>, an unanchored pattern matches
/// <c>demo01.hrms.example.attacker.test</c>, and neither fails at startup.
/// </para>
/// <para>
/// Deliberately not <c>SetIsOriginAllowedToAllowWildcardSubdomains</c>, which reads like this and is looser:
/// it admits nested subdomains, so one compromised host under a customer's label becomes a trusted origin.
/// </para>
/// </summary>
public sealed record WorkspaceOriginPattern(string Scheme, string BaseDomain, int Port)
{
    /// <summary>What a template writes where the workspace label goes.</summary>
    public const string Placeholder = "{workspace}";

    /// <summary>
    /// Stands in for the placeholder while the template is parsed by <see cref="Uri"/>, which cannot parse a
    /// host containing braces. Deliberately long and hyphenated: it has to be a legal DNS label, and it must
    /// not be something a real base domain could start with.
    /// </summary>
    private const string PlaceholderLabel = "workspace-label-placeholder";

    /// <summary>The longest a DNS label may be.</summary>
    private const int MaxLabelLength = 63;

    /// <summary>
    /// Parses a configured template, or throws with the template quoted. Startup is the right place to fail:
    /// a template that silently did not parse would leave every workspace origin refused, which looks like a
    /// browser problem and not like a configuration one.
    /// </summary>
    public static WorkspaceOriginPattern Parse(string template)
    {
        var value = template?.Trim() ?? string.Empty;

        var placeholders = value.Split(Placeholder, StringSplitOptions.None).Length - 1;
        if (placeholders == 0)
        {
            throw new InvalidOperationException(
                $"'{CorsSettings.SectionName}:{nameof(CorsSettings.WorkspaceOriginTemplates)}' entry "
                + $"'{template}' has no '{Placeholder}' placeholder. An entry with a fixed host belongs in "
                + $"'{nameof(CorsSettings.AllowedOrigins)}' instead.");
        }

        // More than one is never what was meant, and it would parse: the second occurrence would end up
        // inside the base domain, giving a pattern that matches nothing while looking like it should.
        if (placeholders > 1)
        {
            throw new InvalidOperationException(
                $"'{CorsSettings.SectionName}:{nameof(CorsSettings.WorkspaceOriginTemplates)}' entry "
                + $"'{template}' repeats '{Placeholder}'. Exactly one workspace label varies per template.");
        }

        var probe = value.Replace(PlaceholderLabel, string.Empty, StringComparison.Ordinal)
            .Replace(Placeholder, PlaceholderLabel, StringComparison.Ordinal);

        if (!CorsOriginPolicy.TryParseOrigin(probe, out var uri))
        {
            throw new InvalidOperationException(
                $"'{CorsSettings.SectionName}:{nameof(CorsSettings.WorkspaceOriginTemplates)}' entry "
                + $"'{template}' is not an origin. Expected scheme, host and optional port only — "
                + $"for example 'https://{Placeholder}.hrms.example'.");
        }

        var host = uri.IdnHost;
        if (!host.StartsWith(PlaceholderLabel + '.', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"'{CorsSettings.SectionName}:{nameof(CorsSettings.WorkspaceOriginTemplates)}' entry "
                + $"'{template}' must put '{Placeholder}' at the start of the host, as its own label. "
                + "A placeholder anywhere else would match parts of a label, which is how "
                + "'hrms.example' comes to admit 'evil-hrms.example'.");
        }

        return new WorkspaceOriginPattern(uri.Scheme, host[(PlaceholderLabel.Length + 1)..], uri.Port);
    }

    /// <summary>
    /// Whether an already-parsed origin is one workspace under this pattern. The origin is parsed by
    /// <see cref="CorsOriginPolicy.TryParseOrigin"/>, so scheme and host arrive lowercased and punycoded and
    /// the port is the effective one — the raw header's casing and elided default port are already gone.
    /// </summary>
    public bool Matches(Uri origin)
    {
        ArgumentNullException.ThrowIfNull(origin);

        return string.Equals(origin.Scheme, Scheme, StringComparison.Ordinal)
               && origin.Port == Port
               && IsOneWorkspaceLabelDeep(origin.IdnHost);
    }

    /// <summary>
    /// The host is <c>&lt;label&gt;.&lt;BaseDomain&gt;</c> with exactly one label in front. Nesting is
    /// refused because the label would contain a dot, and suffix confusion is refused because the base
    /// domain has to end the host rather than merely appear in it.
    /// </summary>
    private bool IsOneWorkspaceLabelDeep(string host)
    {
        // Shorter than "x." + base domain cannot be a workspace host — and equal length would mean an empty
        // label, or the base domain on its own, which is the apex and belongs in the exact list.
        var boundary = host.Length - BaseDomain.Length - 1;
        if (boundary < 1)
        {
            return false;
        }

        return host[boundary] == '.'
               && host.AsSpan(boundary + 1).SequenceEqual(BaseDomain)
               && IsDnsLabel(host.AsSpan(0, boundary));
    }

    /// <summary>
    /// A single DNS label: letters, digits and inner hyphens. The character class is what stops a dot, so
    /// this is where nested subdomains are actually refused.
    /// </summary>
    private static bool IsDnsLabel(ReadOnlySpan<char> label)
    {
        if (label.Length is 0 or > MaxLabelLength || label[0] == '-' || label[^1] == '-')
        {
            return false;
        }

        foreach (var character in label)
        {
            if (character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
            {
                return false;
            }
        }

        return true;
    }
}
