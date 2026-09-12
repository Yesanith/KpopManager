using System.IO;
using KpopManager.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KpopManager.Editor.Generation
{
    /// <summary>
    /// Reads the content JSON files under <c>Assets/SimData/</c>, combines them into one JSON
    /// object shaped like <see cref="WorldData"/>, and hands the resulting string to
    /// <see cref="WorldDataLoader.FromJson"/>.
    /// </summary>
    /// <remarks>
    /// This is where file I/O happens — Core cannot do it. Each source file's shape matches one
    /// (or, for <c>foreign-names.json</c>, three) <see cref="WorldData"/> properties directly, so
    /// combining them is a plain JSON merge with no reshaping.
    /// </remarks>
    public static class ContentLoader
    {
        private const string DataFolder = "Assets/SimData";

        /// <summary>Loads and parses every content file, returning a ready-to-use <see cref="WorldData"/>.</summary>
        public static WorldData LoadWorldData()
        {
            JArray koreanGivenNames = JArray.Parse(ReadFile("korean-given-names.json"));
            JArray koreanFamilyNames = JArray.Parse(ReadFile("korean-family-names.json"));
            JArray stageNames = JArray.Parse(ReadFile("stage-names.json"));
            JArray groupNames = JArray.Parse(ReadFile("group-names.json"));
            JObject foreignNames = JObject.Parse(ReadFile("foreign-names.json"));
            JObject trackTitles = JObject.Parse(ReadFile("track-titles.json"));

            JObject combined = new JObject
            {
                ["KoreanGivenNames"] = koreanGivenNames,
                ["KoreanFamilyNames"] = koreanFamilyNames,
                ["StageNames"] = stageNames,
                ["GroupNames"] = groupNames,
                ["JapaneseGivenNames"] = foreignNames["japanese"] ?? new JArray(),
                ["ChineseGivenNames"] = foreignNames["chinese"] ?? new JArray(),
                ["ThaiGivenNames"] = foreignNames["thai"] ?? new JArray(),
                ["TrackTitlePrefixes"] = trackTitles["prefixes"] ?? new JArray(),
                ["TrackTitleSuffixes"] = trackTitles["suffixes"] ?? new JArray()
            };

            return WorldDataLoader.FromJson(combined.ToString(Formatting.None));
        }

        private static string ReadFile(string fileName)
        {
            string path = Path.Combine(DataFolder, fileName);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Missing KpopManager content file: " + path);
            }

            return File.ReadAllText(path);
        }
    }
}
