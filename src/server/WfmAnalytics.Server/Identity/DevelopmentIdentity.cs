namespace WfmAnalytics.Server.Identity;

public static class DevelopmentIdentity
{
    public const string PrincipalHeader = "X-Development-Principal";
    public const string ScopesHeader = "X-Development-Scopes";
    public const string ScopeClaimType = "scope";
}
