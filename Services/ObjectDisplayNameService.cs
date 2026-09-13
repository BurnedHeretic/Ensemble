using Ensemble.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Ensemble.Services
{
    /// <summary>
    /// Converts Halo Wars' internal scenario / art object identifiers into
    /// editor-facing names that match the terminology players see in-game.
    /// The exact internal identifier remains available on ObjectCatalogEntry
    /// so search, import and UGX resolution still use the original data.
    /// </summary>
    internal static class ObjectDisplayNameService
    {
        private static readonly Regex TrailingInstanceNumbers =
            new(
                @"(?:[_\- ]\d+){1,3}$",
                RegexOptions.Compiled |
                RegexOptions.CultureInvariant);

        private static readonly Regex CamelBoundary =
            new(
                @"(?<=[a-z0-9])(?=[A-Z])",
                RegexOptions.Compiled |
                RegexOptions.CultureInvariant);

        private static readonly Regex MultiSpace =
            new(
                @"\s+",
                RegexOptions.Compiled |
                RegexOptions.CultureInvariant);

        public static string GetFriendlyName(
            ObjectCatalogLayer layer,
            string rawName,
            string type)
        {
            string safeRaw =
                rawName?.Trim()
                ??
                string.Empty;

            string safeType =
                type?.Trim()
                ??
                string.Empty;

            if (layer ==
                ObjectCatalogLayer.ImportedMesh)
            {
                return Humanize(
                    safeRaw);
            }

            string combined =
                (safeRaw + " " + safeType)
                    .ToLowerInvariant();

            // Neutral / gameplay hooks. These are the names players normally
            // use for the objects rather than the scenario implementation IDs.
            if (ContainsAny(
                    combined,
                    "game_base_socket",
                    "base_socket",
                    "basesocket"))
            {
                return "Base Site";
            }

            if (ContainsAny(
                    combined,
                    "sniperplatform",
                    "sniper_platform"))
            {
                return "Sniper Tower";
            }

            if (combined.Contains(
                    "reactor",
                    StringComparison.Ordinal))
            {
                return "Reactor";
            }

            if (ContainsAny(
                    combined,
                    "supply3hole",
                    "supply_spawner",
                    "supplyhook"))
            {
                return "Supply Pad";
            }

            if (combined.Contains(
                    "supplypad",
                    StringComparison.Ordinal))
            {
                return "Supply Pad";
            }

            if (combined.Contains(
                    "supplydepot",
                    StringComparison.Ordinal))
            {
                return "Supply Depot";
            }

            if (ContainsAny(
                    combined,
                    "teleporter2way",
                    "teleporter_2way",
                    "twowayteleporter"))
            {
                return "Teleporter";
            }

            if (ContainsAny(
                    combined,
                    "teleporter1way",
                    "teleporter_1way",
                    "onewayteleporter"))
            {
                return "One-Way Teleporter";
            }

            if (combined.Contains(
                    "teleporter",
                    StringComparison.Ordinal))
            {
                return "Teleporter";
            }

            if (ContainsAny(
                    combined,
                    "rebelmarker_sniper",
                    "rebel_marker_sniper"))
            {
                return "Rebel Sniper Spawn";
            }

            if (combined.Contains(
                    "rebelmarker",
                    StringComparison.Ordinal))
            {
                return "Rebel Infantry Spawn";
            }

            if (ContainsAny(
                    combined,
                    "unitstart",
                    "unit_start"))
            {
                return "Unit Start";
            }

            if (combined.Contains(
                    "globalrally",
                    StringComparison.Ordinal))
            {
                return "Global Rally Point";
            }

            if (combined.Contains(
                    "rally",
                    StringComparison.Ordinal))
            {
                return "Rally Point";
            }

            if (combined.Contains(
                    "baseobstruction",
                    StringComparison.Ordinal))
            {
                return "Base Obstruction";
            }

            if (combined.Contains(
                    "parkinglotcov",
                    StringComparison.Ordinal))
            {
                return "Covenant Parking Area";
            }

            if (combined.Contains(
                    "parkinglotunsc",
                    StringComparison.Ordinal))
            {
                return "UNSC Parking Area";
            }

            if (combined.Contains(
                    "crate",
                    StringComparison.Ordinal))
            {
                string variant =
                    TryGetCrateVariant(
                        combined);

                return string.IsNullOrWhiteSpace(
                        variant)
                    ? "Supply Crate"
                    : $"Supply Crate {variant}";
            }

            if (combined.Contains(
                    "highlight",
                    StringComparison.Ordinal))
            {
                return "Highlight Marker";
            }

            // Faction names and common Halo Wars unit / building identifiers
            // are kept readable even when there is no explicit special case.
            string preferred =
                !string.IsNullOrWhiteSpace(
                    safeType)
                    ? safeType
                    : safeRaw;

            string humanized =
                Humanize(
                    preferred);

            if (!string.IsNullOrWhiteSpace(
                    humanized))
            {
                return humanized;
            }

            return string.IsNullOrWhiteSpace(
                    safeRaw)
                ? "Object"
                : safeRaw;
        }

        private static string Humanize(
            string value)
        {
            if (string.IsNullOrWhiteSpace(
                    value))
            {
                return string.Empty;
            }

            string leaf =
                value
                    .Replace('\\', '/')
                    .Split(
                        '/',
                        StringSplitOptions.RemoveEmptyEntries)
                    .LastOrDefault()
                ??
                value;

            int extensionIndex =
                leaf.LastIndexOf(
                    ".",
                    StringComparison.Ordinal);

            if (extensionIndex >
                0)
            {
                leaf =
                    leaf[..extensionIndex];
            }

            leaf =
                TrailingInstanceNumbers.Replace(
                    leaf,
                    string.Empty);

            leaf =
                CamelBoundary.Replace(
                    leaf,
                    " ");

            leaf =
                leaf
                    .Replace('_', ' ')
                    .Replace('-', ' ')
                    .Replace('.', ' ');

            string[] tokens =
                MultiSpace
                    .Replace(
                        leaf,
                        " ")
                    .Trim()
                    .Split(
                        ' ',
                        StringSplitOptions.RemoveEmptyEntries);

            List<string> readable =
                new();

            foreach (string token
                     in tokens)
            {
                string lower =
                    token.ToLowerInvariant();

                // Implementation / asset hierarchy words add noise in the UI.
                if (lower is
                    "hook" or
                    "game" or
                    "system" or
                    "object" or
                    "obj" or
                    "marker" or
                    "spawner" or
                    "bldg" or
                    "building" or
                    "prop" or
                    "art" or
                    "scenario" or
                    "scenery" or
                    "socket")
                {
                    continue;
                }

                if (lower ==
                    "unsc")
                {
                    readable.Add(
                        "UNSC");

                    continue;
                }

                if (lower is
                    "cov" or
                    "covenant")
                {
                    readable.Add(
                        "Covenant");

                    continue;
                }

                if (lower ==
                    "forerunner")
                {
                    readable.Add(
                        "Forerunner");

                    continue;
                }

                if (lower ==
                    "odst")
                {
                    readable.Add(
                        "ODST");

                    continue;
                }

                if (int.TryParse(
                        lower,
                        out _))
                {
                    continue;
                }

                readable.Add(
                    CultureInfo.InvariantCulture
                        .TextInfo
                        .ToTitleCase(
                            lower));
            }

            return string.Join(
                " ",
                readable);
        }

        private static bool ContainsAny(
            string source,
            params string[] values)
        {
            foreach (string value
                     in values)
            {
                if (source.Contains(
                        value,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string TryGetCrateVariant(
            string combined)
        {
            Match match =
                Regex.Match(
                    combined,
                    @"crate(?:12|3a|3b|3c|5)?[_\- ]?(\d+)",
                    RegexOptions.CultureInvariant);

            return match.Success
                ? match.Groups[1].Value
                : string.Empty;
        }
    }
}
