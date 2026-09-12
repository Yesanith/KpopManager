using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace KpopManager.Core
{
    // Parses the compact "F"/"M" gender tag the content JSON uses (also accepts the full enum
    // names) into Gender. A tiny custom converter rather than asking the content files to spell
    // out "Female"/"Male" on every one of ~750 entries.
    public sealed class GenderJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(Gender);

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            string text = reader.Value as string;
            if (string.IsNullOrEmpty(text))
            {
                throw new JsonSerializationException("Missing gender tag.");
            }

            switch (text.Trim().ToUpperInvariant())
            {
                case "F":
                case "FEMALE":
                    return Gender.Female;
                case "M":
                case "MALE":
                    return Gender.Male;
                default:
                    throw new JsonSerializationException("Unrecognised gender tag: \"" + text + "\"");
            }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteValue(value is Gender gender && gender == Gender.Female ? "F" : "M");
        }
    }

    // One named entry tagged with the gender it reads as, e.g. a given name.
    public sealed class NameEntry
    {
        public string Name { get; set; }

        [JsonConverter(typeof(GenderJsonConverter))]
        public Gender Gender { get; set; }
    }

    // One named entry with a relative frequency weight, e.g. a Korean family name.
    public sealed class WeightedName
    {
        public string Name { get; set; }

        // Relative, not a percentage — PersonGenerator normalises against the sum of all weights
        // in the pool, so these don't need to add to any particular total.
        public float Weight { get; set; }
    }

    // All loaded name-bank content, as plain data. Built once by WorldDataLoader.FromJson from
    // JSON the Editor reads off disk (Core has no file I/O), then attached to GameState.WorldData
    // for the life of a run.
    //
    // Explicit named lists rather than a dictionary keyed by content type, per CLAUDE.md's
    // convention — the count is small and fixed, and a name beats a key you have to remember.
    public sealed class WorldData
    {
        public List<NameEntry> KoreanGivenNames { get; set; } = new List<NameEntry>();
        public List<WeightedName> KoreanFamilyNames { get; set; } = new List<WeightedName>();
        public List<string> StageNames { get; set; } = new List<string>();
        public List<string> GroupNames { get; set; } = new List<string>();

        public List<NameEntry> JapaneseGivenNames { get; set; } = new List<NameEntry>();
        public List<NameEntry> ChineseGivenNames { get; set; } = new List<NameEntry>();
        public List<NameEntry> ThaiGivenNames { get; set; } = new List<NameEntry>();

        // Track titles are assembled as "{Prefix} {Suffix}" (Systems.Chart.TrackGenerator)
        // rather than stored whole — prefixCount x suffixCount combinations comfortably covers a
        // 50-year run without every title needing to be hand-typed individually.
        public List<string> TrackTitlePrefixes { get; set; } = new List<string>();
        public List<string> TrackTitleSuffixes { get; set; } = new List<string>();

        // The given-name pool for a foreign nationality, or null if none is loaded for it.
        // American and Other aren't covered by content JSON at this scope (~5% of the generated
        // population combined) — PersonGenerator falls back to a small embedded list for those two.
        public List<NameEntry> GetForeignGivenNames(Nationality nationality)
        {
            switch (nationality)
            {
                case Nationality.Japanese: return JapaneseGivenNames;
                case Nationality.Chinese: return ChineseGivenNames;
                case Nationality.Thai: return ThaiGivenNames;
                default: return null;
            }
        }
    }
}
