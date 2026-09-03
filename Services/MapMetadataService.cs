using Ensemble.Models;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Ensemble.Services
{
    public static class MapMetadataService
    {
        private static readonly JsonSerializerOptions
            JsonOptions =
                new JsonSerializerOptions
                {
                    WriteIndented =
                        true,

                    PropertyNamingPolicy =
                        JsonNamingPolicy.CamelCase,

                    PropertyNameCaseInsensitive =
                        true
                };


        public static string GetSidecarPath(
            string eraPath)
        {
            string directory =
                Path.GetDirectoryName(
                    eraPath)
                ?? string.Empty;


            string basename =
                Path.GetFileNameWithoutExtension(
                    eraPath);


            return Path.Combine(
                directory,
                basename +
                ".ensemble.json");
        }


        public static MapMetadata? Load(
            string eraPath)
        {
            string path =
                GetSidecarPath(
                    eraPath);


            if (!File.Exists(
                    path))
            {
                return null;
            }


            try
            {
                string json =
                    File.ReadAllText(
                        path);


                MapMetadata? result =
                    JsonSerializer.Deserialize<MapMetadata>(
                        json,
                        JsonOptions);


                if (result == null)
                {
                    throw new InvalidDataException(
                        "Map metadata file is empty.");
                }


                if (result.FormatVersion !=
                    1)
                {
                    throw new InvalidDataException(
                        $"Unsupported Ensemble metadata version: " +
                        $"{result.FormatVersion}");
                }


                result.DisplayName =
                    result.DisplayName?
                        .Trim()
                    ?? string.Empty;


                result.Description =
                    result.Description?
                        .Trim()
                    ?? string.Empty;


                return result;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException(
                    "Unable to read Ensemble map metadata.\n\n" +
                    path,
                    ex);
            }
        }


        public static MapMetadata CreateDefault(
            string eraPath)
        {
            string basename =
                Path.GetFileNameWithoutExtension(
                    eraPath);


            return new MapMetadata
            {
                DisplayName =
                    BuildDefaultDisplayName(
                        basename),

                Description =
                    "Custom map created with Ensemble."
            };
        }


        public static void Save(
            string eraPath,
            MapMetadata metadata)
        {
            if (metadata == null)
            {
                throw new ArgumentNullException(
                    nameof(metadata));
            }


            if (string.IsNullOrWhiteSpace(
                    metadata.DisplayName))
            {
                throw new InvalidDataException(
                    "Map display name cannot be empty.");
            }


            string path =
                GetSidecarPath(
                    eraPath);


            string tempPath =
                path +
                ".tmp";


            string json =
                JsonSerializer.Serialize(
                    metadata,
                    JsonOptions);


            try
            {
                File.WriteAllText(
                    tempPath,
                    json,
                    new System.Text.UTF8Encoding(
                        encoderShouldEmitUTF8Identifier: false));


                File.Copy(
                    tempPath,
                    path,
                    overwrite: true);
            }
            finally
            {
                if (File.Exists(
                        tempPath))
                {
                    File.Delete(
                        tempPath);
                }
            }
        }


        private static string BuildDefaultDisplayName(
            string basename)
        {
            string value =
                basename
                    .Replace(
                        '_',
                        ' ')
                    .Replace(
                        '-',
                        ' ');


            while (value.Contains(
                       "  ",
                       StringComparison.Ordinal))
            {
                value =
                    value.Replace(
                        "  ",
                        " ",
                        StringComparison.Ordinal);
            }


            value =
                value.Trim();


            if (value.Length ==
                0)
            {
                return "Custom Map";
            }


            return CultureInfo
                .InvariantCulture
                .TextInfo
                .ToTitleCase(
                    value.ToLowerInvariant());
        }
    }
}