namespace Oaza.Application.Auth;

public static class AuthConstants
{
    public const string ClaimUserId = "sub";
    public const string ClaimEmail = "email";
    public const string ClaimRole = "role";
    public const string ClaimHouseId = "houseId";
    public const string ClaimAuthMethod = "authMethod";

    public const string HttpContextUserKey = "AuthenticatedUser";
}
