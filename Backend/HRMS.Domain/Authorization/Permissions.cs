namespace HRMS.Domain.Authorization;

/// <summary>
/// Canonical permission names in "Resource.Action" form. These are seeded into the Permission table,
/// granted to roles via RolePermission, and (from Phase 2) emitted as JWT claims so the API can
/// authorize by permission instead of hard-coded role checks.
/// </summary>
public static class Permissions
{
    public static class Employee
    {
        public const string View = "Employee.View";
        public const string Create = "Employee.Create";
        public const string Edit = "Employee.Edit";
        public const string Delete = "Employee.Delete";
        public const string Export = "Employee.Export";
    }

    public static class Department
    {
        public const string View = "Department.View";
        public const string Create = "Department.Create";
        public const string Edit = "Department.Edit";
        public const string Delete = "Department.Delete";
    }

    public static class Designation
    {
        public const string View = "Designation.View";
        public const string Create = "Designation.Create";
        public const string Edit = "Designation.Edit";
        public const string Delete = "Designation.Delete";
    }

    public static class User
    {
        public const string View = "User.View";
        public const string Create = "User.Create";
        public const string Edit = "User.Edit";
        public const string Delete = "User.Delete";
    }

    /// <summary>Every permission the system knows about. Used by the seeder and SuperAdmin/TenantAdmin grants.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Employee.View, Employee.Create, Employee.Edit, Employee.Delete, Employee.Export,
        Department.View, Department.Create, Department.Edit, Department.Delete,
        Designation.View, Designation.Create, Designation.Edit, Designation.Delete,
        User.View, User.Create, User.Edit, User.Delete
    };
}
