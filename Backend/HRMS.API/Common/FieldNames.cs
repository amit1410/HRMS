namespace HRMS.API.Common;

/// <summary>
/// Turns a CLR property path into the camelCase field name the API serializes, so a client can map an error
/// back onto the JSON property (or query-string key) it actually sent.
/// <para>
/// Both error paths spell field names through here. FluentValidation reports "DateOfJoining"; model binding
/// reports "Status" for a query value it could not convert. With two spellings in one envelope, a client
/// matching errors to form inputs would silently miss whichever half it did not expect.
/// </para>
/// </summary>
internal static class FieldNames
{
    public static string ToCamelCase(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName) || char.IsLower(propertyName[0]))
        {
            return propertyName;
        }

        // Every segment of a path is a property of its own: "Contacts[0].PhoneNumber" is reported to the
        // client as "contacts[0].phoneNumber", matching how the payload was serialized on the way in.
        var segments = propertyName.Split('.');
        for (var i = 0; i < segments.Length; i++)
        {
            if (segments[i].Length > 0)
            {
                segments[i] = char.ToLowerInvariant(segments[i][0]) + segments[i][1..];
            }
        }

        return string.Join('.', segments);
    }
}
