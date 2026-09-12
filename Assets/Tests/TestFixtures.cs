using KpopManager.Core;

namespace KpopManager.Tests
{
    // Small, hand-built content shared by the generation tests — real JSON files live under
    // Assets/SimData and are read only by the Editor, so Core-only tests build their own minimal
    // fixture instead of depending on file content.
    internal static class TestFixtures
    {
        public static WorldData BuildWorldData()
        {
            WorldData data = new WorldData();

            data.KoreanGivenNames.Add(new NameEntry { Name = "Ji-woo", Gender = Gender.Female });
            data.KoreanGivenNames.Add(new NameEntry { Name = "Min-jun", Gender = Gender.Male });
            data.KoreanGivenNames.Add(new NameEntry { Name = "Seo-yeon", Gender = Gender.Female });
            data.KoreanGivenNames.Add(new NameEntry { Name = "Dong-hyun", Gender = Gender.Male });

            data.KoreanFamilyNames.Add(new WeightedName { Name = "Kim", Weight = 20f });
            data.KoreanFamilyNames.Add(new WeightedName { Name = "Lee", Weight = 15f });
            data.KoreanFamilyNames.Add(new WeightedName { Name = "Park", Weight = 8f });

            data.StageNames.AddRange(new[] { "Nova", "Aria", "Luna", "Echo", "Vega" });

            data.GroupNames.AddRange(new[]
            {
                "Starlight", "Neon Pulse", "Velvet Echo", "Crimson Bloom", "Halcyon",
                "Aurora Line", "Silver Wave", "Paper Moon", "Wildflower", "Moonchild",
                "Zero Gravity", "Chrome Heart", "Radiant Muse", "Secret Order", "Ivory Tower"
            });

            data.JapaneseGivenNames.Add(new NameEntry { Name = "Haruto", Gender = Gender.Male });
            data.JapaneseGivenNames.Add(new NameEntry { Name = "Sakura", Gender = Gender.Female });
            data.ChineseGivenNames.Add(new NameEntry { Name = "Wei", Gender = Gender.Male });
            data.ChineseGivenNames.Add(new NameEntry { Name = "Mei", Gender = Gender.Female });
            data.ThaiGivenNames.Add(new NameEntry { Name = "Somchai", Gender = Gender.Male });
            data.ThaiGivenNames.Add(new NameEntry { Name = "Suda", Gender = Gender.Female });

            return data;
        }

        public static GameState BuildState(ulong seed, WorldData data)
        {
            return new GameState
            {
                Date = SimDate.Start,
                Random = new SimRandom(seed),
                Log = new SimLog(),
                WorldData = data
            };
        }
    }
}
