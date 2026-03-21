using Oaza.Domain.Enums;

namespace Oaza.Functions.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class RequireRoleAttribute : Attribute
{
    public UserRole[] Roles { get; }

    public RequireRoleAttribute(params UserRole[] roles)
    {
        Roles = roles ?? throw new ArgumentNullException(nameof(roles));
    }
}
