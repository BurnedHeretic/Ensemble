using Ensemble.Models;

namespace Ensemble.Services
{
    internal static class CrossEraObjectCatalogService
    {
        public static ObjectCatalogLoadResult Load(
            string eraPath)
        {
            if (string.IsNullOrWhiteSpace(
                    eraPath))
            {
                throw new ArgumentException(
                    "ERA path cannot be empty.",
                    nameof(eraPath));
            }

            EraArchiveInfo archive =
                EraArchiveService.Open(
                    eraPath);

            ObjectCatalogLoadResult result =
                new ObjectCatalogLoadResult();

            foreach (EraChunkInfo chunk
                     in archive.Chunks)
            {
                if (chunk.Index <=
                    0)
                {
                    continue;
                }

                string fileName =
                    chunk.FileName
                        .Replace(
                            '/',
                            '\\');

                bool isScenario =
                    fileName.EndsWith(
                        ".scn.xmb",
                        StringComparison.OrdinalIgnoreCase);

                bool isArtObjects =
                    fileName.EndsWith(
                        ".sc2.xmb",
                        StringComparison.OrdinalIgnoreCase);

                if (!isScenario &&
                    !isArtObjects)
                {
                    continue;
                }

                try
                {
                    byte[] xmb =
                        EraExtractionService
                            .ExtractChunk(
                                archive,
                                chunk);

                    if (isScenario)
                    {
                        string xml =
                            XmbDocumentService.Read(
                                xmb);

                        ScenarioMap map =
                            ScenarioParserService.Parse(
                                xml);

                        foreach (ScenarioObject obj
                                 in map.Objects)
                        {
                            ObjectCatalogEntry entry =
                                new ObjectCatalogEntry
                                {
                                    Layer =
                                        ObjectCatalogLayer
                                            .Scenario,

                                    DonorEraPath =
                                        eraPath,

                                    SourceFileName =
                                        fileName,

                                    Id =
                                        obj.Id,

                                    Name =
                                        string.IsNullOrWhiteSpace(
                                            obj.EditorName)
                                            ? obj.Type
                                            : obj.EditorName,

                                    Type =
                                        obj.Type,

                                    Position =
                                        obj.Position,

                                    SourceXmbData =
                                        xmb,

                                    ScenarioObject =
                                        obj
                                };

                            entry.PreviewImage =
                                ObjectPreviewService
                                    .Create(
                                        entry.Layer,
                                        entry.Name,
                                        entry.Type);

                            result.Entries.Add(
                                entry);
                        }
                    }
                    else
                    {
                        foreach (
                            ScenarioArtObject obj
                            in ScenarioArtObjectsService
                                .ReadArtObjects(
                                    xmb))
                        {
                            ObjectCatalogEntry entry =
                                new ObjectCatalogEntry
                                {
                                    Layer =
                                        ObjectCatalogLayer
                                            .ArtObject,

                                    DonorEraPath =
                                        eraPath,

                                    SourceFileName =
                                        fileName,

                                    Id =
                                        obj.Id,

                                    Name =
                                        obj.DisplayName,

                                    Type =
                                        obj.Type,

                                    Position =
                                        obj.Position,

                                    SourceXmbData =
                                        xmb,

                                    ArtObject =
                                        obj
                                };

                            entry.PreviewImage =
                                ObjectPreviewService
                                    .Create(
                                        entry.Layer,
                                        entry.Name,
                                        entry.Type);

                            result.Entries.Add(
                                entry);
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Warnings.Add(
                        $"{fileName}: {ex.Message}");
                }
            }

            foreach (ImportedMeshEntry mesh
                     in CustomMeshImportService
                         .LoadAll())
            {
                ObjectCatalogEntry entry =
                    new ObjectCatalogEntry
                    {
                        Layer =
                            ObjectCatalogLayer
                                .ImportedMesh,

                        DonorEraPath =
                            string.Empty,

                        SourceFileName =
                            mesh.FileName,

                        Id =
                            0,

                        Name =
                            mesh.DisplayName,

                        Type =
                            mesh.Extension
                                .TrimStart('.')
                                .ToUpperInvariant() +
                            " mesh",

                        ImportedMesh =
                            mesh
                    };

                entry.PreviewImage =
                    ObjectPreviewService
                        .Create(
                            entry.Layer,
                            entry.Name,
                            entry.Type);

                result.Entries.Add(
                    entry);
            }


            result.Entries.Sort(
                (
                    a,
                    b) =>
                {
                    int layer =
                        a.Layer.CompareTo(
                            b.Layer);

                    if (layer !=
                        0)
                    {
                        return layer;
                    }

                    int name =
                        string.Compare(
                            a.Name,
                            b.Name,
                            StringComparison.OrdinalIgnoreCase);

                    if (name !=
                        0)
                    {
                        return name;
                    }

                    return
                        a.Id.CompareTo(
                            b.Id);
                });

            return result;
        }
    }
}
