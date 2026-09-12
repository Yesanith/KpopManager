using Newtonsoft.Json;

namespace KpopManager.Core
{
    // The only door between raw JSON text and WorldData.
    //
    // Core has no file I/O and no engine references, so it cannot read Assets/SimData/*.json
    // itself. KpopManager.Editor reads the content files off disk, combines them into one JSON
    // object shaped exactly like WorldData, and hands the resulting string to FromJson. This
    // method takes a string and only a string — never a path — so Core stays engine-reference-free
    // while still being able to depend on the plain managed Newtonsoft.Json assembly.
    public static class WorldDataLoader
    {
        public static WorldData FromJson(string json)
        {
            return JsonConvert.DeserializeObject<WorldData>(json) ?? new WorldData();
        }
    }
}
