using Ensemble.Models;
using Ensemble.Services;

namespace Ensemble
{
    /// <summary>
    /// Treats spatially optimized Halo BSP chunks as one logical custom mesh
    /// for Delete. Deleting any *_chunk_XX_ZZ member removes every chunk in
    /// that finalized BSP group, not merely one visible cell.
    /// </summary>
    public partial class MainWindow
    {
        internal bool TryDeleteCustomMeshPlacementOrBspGroupV363(
            ScenarioArtObject artObject)
        {
            ArgumentNullException.ThrowIfNull(
                artObject);

            if (_currentScenarioChunk ==
                    null ||
                _currentArchive ==
                    null)
            {
                return false;
            }

            string scenarioFile =
                _currentScenarioChunk.FileName;

            CustomMeshPendingAssetService.EmbeddedCustomMeshRecord?
                selectedRecord =
                    null;

            bool selectedIsPending =
                CustomMeshPendingAssetService
                    .TryGetPendingRecordForArtObject(
                        scenarioFile,
                        artObject.Id,
                        out selectedRecord)
                &&
                selectedRecord !=
                    null;

            IReadOnlyList<CustomMeshPendingAssetService.EmbeddedCustomMeshRecord>
                embedded =
                    Array.Empty<CustomMeshPendingAssetService.EmbeddedCustomMeshRecord>();

            if (!selectedIsPending)
            {
                try
                {
                    embedded =
                        CustomMeshPendingAssetService
                            .LoadEmbedded(
                                _currentArchive.FilePath);

                    selectedRecord =
                        embedded
                            .FirstOrDefault(
                                record =>
                                    record.ArtObjectId ==
                                    artObject.Id);
                }
                catch
                {
                    selectedRecord =
                        null;
                }
            }

            if (selectedRecord ==
                    null ||
                !TryGetHaloBspGroupPrefixV363(
                    selectedRecord.DisplayName,
                    out string groupPrefix))
            {
                return TryDeleteCustomMeshPlacementFromKeyboard(
                    artObject);
            }

            // ---------------------------------------------------------
            // Pending/current-session members.
            // ---------------------------------------------------------
            List<CustomMeshPendingAssetService.EmbeddedCustomMeshRecord>
                pendingGroup =
                    new();

            foreach (ScenarioArtObject candidate
                     in _currentArtObjects.ToArray())
            {
                if (!CustomMeshPendingAssetService
                        .TryGetPendingRecordForArtObject(
                            scenarioFile,
                            candidate.Id,
                            out CustomMeshPendingAssetService.EmbeddedCustomMeshRecord?
                                record)
                    ||
                    record ==
                        null)
                {
                    continue;
                }

                if (TryGetHaloBspGroupPrefixV363(
                        record.DisplayName,
                        out string candidatePrefix) &&
                    candidatePrefix.Equals(
                        groupPrefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    pendingGroup.Add(
                        record);
                }
            }

            // ---------------------------------------------------------
            // Already-saved/embedded members.
            // ---------------------------------------------------------
            try
            {
                if (embedded.Count == 0)
                {
                    embedded =
                        CustomMeshPendingAssetService
                            .LoadEmbedded(
                                _currentArchive.FilePath);
                }
            }
            catch
            {
                embedded =
                    Array.Empty<CustomMeshPendingAssetService.EmbeddedCustomMeshRecord>();
            }

            List<CustomMeshPendingAssetService.EmbeddedCustomMeshRecord>
                embeddedGroup =
                    embedded
                        .Where(
                            record =>
                                TryGetHaloBspGroupPrefixV363(
                                    record.DisplayName,
                                    out string candidatePrefix) &&
                                candidatePrefix.Equals(
                                    groupPrefix,
                                    StringComparison.OrdinalIgnoreCase))
                        .ToList();

            HashSet<int> embeddedIds =
                embeddedGroup
                    .Select(
                        record =>
                            record.ArtObjectId)
                    .Where(
                        id =>
                            id >
                            0)
                    .ToHashSet();

            int affected =
                pendingGroup
                    .Select(
                        record =>
                            record.ArtObjectId)
                    .Concat(
                        embeddedIds)
                    .Where(
                        id =>
                            id >
                            0)
                    .Distinct()
                    .Count();

            if (affected <= 1)
            {
                return TryDeleteCustomMeshPlacementFromKeyboard(
                    artObject);
            }

            // The existing embedded-delete path performs BOTH important jobs:
            // remove SC2 placements and queue restoration of the stock UGX
            // borrowed by every generated mesh.
            if (embeddedGroup.Count >
                0)
            {
                ApplyEmbeddedCustomMeshDeletionFromBrowser(
                    embeddedGroup);
            }

            // Pending members have never reached the on-disk registry. Remove
            // their live SC2 placements and cancel their generated UGX queues.
            foreach (CustomMeshPendingAssetService.EmbeddedCustomMeshRecord record
                     in pendingGroup)
            {
                if (record.ArtObjectId <=
                        0 ||
                    embeddedIds.Contains(
                        record.ArtObjectId))
                {
                    continue;
                }

                RemoveCustomMeshArtObjectLive(
                    record.ArtObjectId);

                CustomMeshPendingAssetService
                    .CancelPendingPlacement(
                        scenarioFile,
                        record.ArtObjectId);

                CustomMeshViewportPreviewService
                    .Remove(
                        scenarioFile,
                        record.ArtObjectId);
            }

            ApplyCustomMeshViewportPreviews();

            UpdateDirtyState();

            StatusText.Text =
                $"Deleted Halo BSP group '{groupPrefix}' | " +
                $"{affected:N0} spatial chunk(s) removed | save required";

            return true;
        }

        private static bool TryGetHaloBspGroupPrefixV363(
            string? displayName,
            out string prefix)
        {
            prefix =
                string.Empty;

            if (string.IsNullOrWhiteSpace(
                    displayName))
            {
                return false;
            }

            int marker =
                displayName.LastIndexOf(
                    "_chunk_",
                    StringComparison.OrdinalIgnoreCase);

            if (marker <= 0)
                return false;

            string suffix =
                displayName[
                    (marker + "_chunk_".Length)..];

            string[] parts =
                suffix.Split(
                    '_',
                    StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length !=
                    2 ||
                !int.TryParse(
                    parts[0],
                    out _) ||
                !int.TryParse(
                    parts[1],
                    out _))
            {
                return false;
            }

            prefix =
                displayName[..marker];

            return !string.IsNullOrWhiteSpace(
                prefix);
        }
    }
}
