namespace HRMS.Domain.Enums;

/// <summary>
/// Classification of a bank in the Bank master. Stored as a string so the column stays readable in the
/// database and in CSV/XLSX exports, and so a value can be added without renumbering the others.
/// </summary>
public enum BankType
{
    /// <summary>Public-sector / nationalised bank (e.g. State Bank of India).</summary>
    Public = 0,

    /// <summary>Private-sector bank (e.g. HDFC, ICICI).</summary>
    Private = 1,

    /// <summary>Co-operative bank.</summary>
    Cooperative = 2,

    /// <summary>Foreign bank operating in the country.</summary>
    Foreign = 3,

    /// <summary>Payments bank.</summary>
    Payments = 4,

    /// <summary>Small finance bank.</summary>
    SmallFinance = 5,

    /// <summary>Regional rural bank.</summary>
    RegionalRural = 6,

    /// <summary>Any bank that does not fit the categories above.</summary>
    Other = 7
}
