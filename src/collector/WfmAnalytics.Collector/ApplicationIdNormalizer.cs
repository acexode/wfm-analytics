using System.Text.RegularExpressions;

namespace WfmAnalytics.Collector;

public static partial class ApplicationIdNormalizer
{
    public static string? NormalizeExecutableName(string? processPathOrName)
    {
        if (string.IsNullOrWhiteSpace(processPathOrName))
        {
            return null;
        }

        var fileName = Path.GetFileName(processPathOrName.Trim());
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var normalized = fileName.ToLowerInvariant();
        return ApprovedApplicationId().IsMatch(normalized) ? normalized : null;
    }

    [GeneratedRegex("^[a-z0-9_.-]{1,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex ApprovedApplicationId();
}
