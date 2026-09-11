using Newtonsoft.Json;

namespace KpopManager.Core
{
    /// <summary>
    /// The only door between raw JSON text and <see cref="WorldData"/>.
    /// </summary>
    /// <remarks>
    /// Core has no file I/O and no engine references, so it cannot read
    /// <c>Assets/SimData/*.json</c> itself. <c>KpopManager.Editor</c> reads the five content files
    /// off disk, combines them into one JSON object shaped exactly like <see cref="WorldData"/>,
    /// and hands the resulting string to <see cref="FromJson"/>. This method takes a string and
    /// only a string — never a path — so Core stays engine-reference-free while still being able
    /// to depend on the plain managed <c>Newtonsoft.Json</c> assembly.
    /// </remarks>
    public static class WorldDataLoader
    {
        /// <summary>Deserialises a combined content JSON string into <see cref="WorldData"/>.</summary>
        public static WorldData FromJson(string json)
        {
            return JsonConvert.DeserializeObject<WorldData>(json) ?? new WorldData();
        }
    }
}
