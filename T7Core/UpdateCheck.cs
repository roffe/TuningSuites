using System;
using System.Linq;
using System.Text.Json;

namespace T7
{
    /// <summary>A published release: its version (four parts), tag, page and Windows installer (null without one).</summary>
    public sealed record Release(Version Version, string Tag, string Page, string Msi);

    /// <summary>
    /// frmMain's update check (msiupdater, which read develop.trionictuning.com) on GitHub releases, as the flasher's MsiUpdater
    /// does. The repo releases every suite, so GitHub's releases/latest is whichever suite went last: the newest published, non
    /// pre-release release tagged T7suite_vX.Y[.Z[.W]] is taken from the release list instead.
    /// </summary>
    public static class UpdateCheck
    {
        public const string Repo = "roffe/TuningSuites";
        public const string TagPrefix = "T7suite_v";
        public static readonly string ReleasesApi = $"https://api.github.com/repos/{Repo}/releases?per_page=100";
        public static readonly string ReleasesPage = $"https://github.com/{Repo}/releases";

        /// <summary>The newest release in a GitHub release list (JSON), null when there's none for this suite.</summary>
        public static Release Newest(string json, string tagPrefix = TagPrefix)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            Release best = null;
            foreach (JsonElement r in doc.RootElement.EnumerateArray())
            {
                if (r.GetProperty("draft").GetBoolean() || r.GetProperty("prerelease").GetBoolean()) continue;
                string tag = r.GetProperty("tag_name").GetString() ?? "";
                // a labelled tag (T7suite_v2.1.0-beta) doesn't parse and isn't offered
                if (!tag.StartsWith(tagPrefix, StringComparison.Ordinal) || !Version.TryParse(tag[tagPrefix.Length..], out Version v)) continue;
                v = Pad(v);
                if (best != null && v <= best.Version) continue;
                string msi = r.GetProperty("assets").EnumerateArray().Select(a => a.GetProperty("browser_download_url").GetString())
                    .FirstOrDefault(u => u != null && u.EndsWith(".msi", StringComparison.OrdinalIgnoreCase));
                best = new Release(v, tag, r.GetProperty("html_url").GetString() ?? ReleasesPage, msi);
            }
            return best;
        }

        /// <summary>2.0 → 2.0.0.0, comparable with an assembly's version (the tag padded to four parts).</summary>
        public static Version Pad(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
    }
}
