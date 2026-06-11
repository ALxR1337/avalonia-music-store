using System.Text;

namespace MusicApp.Services;

/// <summary>
/// Shared slug rule for matching bundled-asset filenames: lowercase, every run
/// of non-alphanumeric characters collapses to a single hyphen, and there are
/// no leading or trailing hyphens. Must stay in sync with the avatar/cover
/// fetch scripts that name the files under <c>Assets/</c>.
/// </summary>
internal static class AssetSlug
{
    public static string Of(string value)
    {
        var sb = new StringBuilder(value.Length);
        var prevDash = false;
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
            {
                sb.Append(ch);
                prevDash = false;
            }
            else if (sb.Length > 0 && !prevDash)
            {
                sb.Append('-');
                prevDash = true;
            }
        }

        return sb.ToString().TrimEnd('-');
    }
}
