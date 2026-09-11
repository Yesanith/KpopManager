using System;
using System.Collections.Generic;

namespace KpopManager.Core.Systems.Generation
{
    /// <summary>
    /// Builds one <see cref="Person"/> from scratch: archetype first, attributes second.
    /// </summary>
    /// <remarks>
    /// Every numeric choice below is a balancing knob, not a considered final value — each is
    /// flagged <c>// DESIGN:</c> at the point it's used. Nothing here reads or writes
    /// <see cref="GameState"/> directly; callers own id allocation and registration, so this stays
    /// usable both from <see cref="GroupGenerator"/>/<see cref="WorldGenerator"/> and from a bare
    /// unit test with no world at all.
    /// </remarks>
    public static class PersonGenerator
    {
        /// <summary>Number of quality tiers <paramref name="tier"/> can take, mirroring <see cref="GroupTier"/>'s ordinals.</summary>
        public const int TierCount = 5;

        // DESIGN: five means spanning "raw trainee-pool talent" to "generational once-a-decade
        // idol", shifting the Gaussian mean rather than the cap — a Rookie-tier roll can still hit
        // 90 on a lucky draw. Revisit heavily once Phase 3's chart sim gives real balancing signal.
        private static readonly float[] TierMean = { 32f, 42f, 54f, 68f, 82f };

        private const float AttributeStdDev = 13f;
        private const float HiddenStdDev = 15f;

        /// <summary>The eight performance archetypes a person can be generated around. Excludes
        /// <see cref="Position.Leader"/> and <see cref="Position.Maknae"/>, which are group-role
        /// tags assigned afterward by <see cref="GroupGenerator"/>, not performance archetypes.</summary>
        public static readonly Position[] PrimaryArchetypes =
        {
            Position.MainVocal, Position.LeadVocal, Position.MainRapper, Position.LeadRapper,
            Position.MainDancer, Position.LeadDancer, Position.Visual, Position.AllRounder
        };

        // DESIGN: American/Other given and family names aren't asked for as content JSON (~5% of
        // the population combined) — a small embedded fallback keeps that slice varied without a
        // whole extra data file for it. Revisit if overseas expansion (out of MVP scope) ever
        // makes American nationality more than a rounding error.
        private static readonly string[] AmericanGivenMale = { "James", "Michael", "Ethan", "Noah", "Lucas", "Mason", "Logan", "Aiden", "Jackson", "Caleb" };
        private static readonly string[] AmericanGivenFemale = { "Olivia", "Emma", "Ava", "Sophia", "Isabella", "Mia", "Amelia", "Harper", "Evelyn", "Chloe" };
        private static readonly string[] AmericanFamily = { "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller", "Davis", "Rodriguez", "Martinez" };
        private static readonly string[] OtherGivenMale = { "Alex", "Sam", "Chris", "Jordan", "Kai", "Milo", "Theo", "Eli" };
        private static readonly string[] OtherGivenFemale = { "Ari", "Sasha", "Robin", "Skylar", "Rowan", "Nico", "Quinn", "Reese" };
        private static readonly string[] OtherFamily = { "Andersen", "Novak", "Lindgren", "Moreau", "Kowalski", "Petrov", "Silva", "Costa" };

        /// <summary>
        /// Generates a person with a random gender and a random primary archetype. This is the
        /// literal Phase 2 signature; <see cref="GroupGenerator"/> uses the fuller overload below
        /// so it can pin both to keep a group internally consistent.
        /// </summary>
        public static Person Generate(SimRandom rng, int birthYear, int tier, PersonStatus status, SimDate now)
        {
            return Generate(rng, birthYear, tier, status, now, null, null, null);
        }

        /// <summary>Generates a person, optionally pinning gender and/or primary archetype rather than rolling them.</summary>
        public static Person Generate(
            SimRandom rng, int birthYear, int tier, PersonStatus status, SimDate now,
            Gender? gender, Position? primaryPosition, WorldData worldData)
        {
            tier = Clamp(tier, 0, TierCount - 1);

            Gender resolvedGender = gender ?? (rng.Chance(0.5f) ? Gender.Female : Gender.Male);
            Position archetype = primaryPosition ?? rng.Pick(PrimaryArchetypes);

            Person person = new Person
            {
                Gender = resolvedGender,
                BirthYear = birthYear,
                Status = status,
                Nationality = RollNationality(rng)
            };

            AssignNames(rng, person, worldData);

            int age = Math.Max(0, now.Year - birthYear);
            float baseMean = TierMean[tier] * AgeFactor(age);

            GetArchetypeBonuses(archetype, out float vocalBonus, out float rapBonus, out float danceBonus,
                out float stagePresenceBonus, out float visualBonus, out float charismaBonus, out float varianceScale);

            float stdDev = AttributeStdDev * varianceScale;

            person.Vocal = RollClamped(rng, baseMean + vocalBonus, stdDev);
            person.Rap = RollClamped(rng, baseMean + rapBonus, stdDev);
            person.Dance = RollClamped(rng, baseMean + danceBonus, stdDev);
            person.StagePresence = RollClamped(rng, baseMean + stagePresenceBonus, stdDev);

            person.Visual = RollClamped(rng, baseMean + visualBonus, stdDev);
            person.Charisma = RollClamped(rng, baseMean + charismaBonus, stdDev);
            person.Variety = RollClamped(rng, baseMean, stdDev);
            person.FanConnection = RollClamped(rng, baseMean, stdDev);

            // DESIGN: creative skills sit below performance skills on average — most idols aren't
            // songwriters or composers, and the ones who are stand out because of it. Revisit once
            // Phase 4's comeback cycle can reward these directly.
            person.Songwriting = RollClamped(rng, baseMean - 15f, stdDev);
            person.Composition = RollClamped(rng, baseMean - 18f, stdDev);
            person.Choreography = RollClamped(rng, baseMean - 12f, stdDev);

            person.WorkEthic = RollClamped(rng, TierMean[tier] * 0.90f, HiddenStdDev);
            person.MentalResilience = RollClamped(rng, TierMean[tier] * 0.85f, HiddenStdDev);
            person.Ambition = RollClamped(rng, TierMean[tier] * 0.95f, HiddenStdDev);
            person.Professionalism = RollClamped(rng, TierMean[tier] * 0.85f, HiddenStdDev);

            AssignPotential(rng, person, tier, age);

            person.Morale = RollClamped(rng, 72f, 10f);
            person.Fatigue = RollClamped(rng, 12f, 8f);
            person.Health = RollClamped(rng, 92f, 6f);
            person.InGroupPopularity = RollClamped(rng, 50f, 15f);

            AssignLanguages(rng, person);
            AssignTraining(rng, person, now, age);

            person.Positions.Add(archetype);

            return person;
        }

        /// <summary>
        /// Per-archetype attribute bonuses (additive, in points on the 0–100 scale) and a variance
        /// multiplier. DESIGN: a MainVocal/MainRapper pair are pushed apart directly opposite each
        /// other (a MainVocal should read as a weak rapper, not merely an average one); a Visual
        /// gets no penalty elsewhere ("middling everything else" per DESIGN.md, not "worse at
        /// everything else"); AllRounder trades a lower Potential ceiling and tighter spread for
        /// having no glaring weakness. Revisit heavily during balancing.
        /// </summary>
        private static void GetArchetypeBonuses(
            Position archetype,
            out float vocalBonus, out float rapBonus, out float danceBonus,
            out float stagePresenceBonus, out float visualBonus, out float charismaBonus,
            out float varianceScale)
        {
            vocalBonus = rapBonus = danceBonus = stagePresenceBonus = visualBonus = charismaBonus = 0f;
            varianceScale = 1f;

            switch (archetype)
            {
                case Position.MainVocal:
                    vocalBonus = 25f; rapBonus = -10f;
                    break;
                case Position.LeadVocal:
                    vocalBonus = 15f; rapBonus = -5f;
                    break;
                case Position.MainRapper:
                    rapBonus = 25f; vocalBonus = -10f;
                    break;
                case Position.LeadRapper:
                    rapBonus = 15f; vocalBonus = -5f;
                    break;
                case Position.MainDancer:
                    danceBonus = 25f; stagePresenceBonus = 10f;
                    break;
                case Position.LeadDancer:
                    danceBonus = 15f; stagePresenceBonus = 6f;
                    break;
                case Position.Visual:
                    visualBonus = 25f; charismaBonus = 10f;
                    break;
                case Position.AllRounder:
                    varianceScale = 0.6f;
                    break;
            }
        }

        /// <summary>
        /// DESIGN: current ability ramps with age — a 15-year-old trainee reads as raw even at a
        /// good tier, a 23-year-old idol reads as fully realised, and a couple more years buy a
        /// small extra polish before flattening out. Revisit during balancing.
        /// </summary>
        private static float AgeFactor(int age)
        {
            if (age <= 14) return 0.35f;
            if (age <= 23) return 0.35f + (age - 14) / 9f * 0.65f;
            if (age >= 26) return 1.10f;
            return 1.0f + (age - 23) / 3f * 0.10f;
        }

        /// <summary>
        /// DESIGN: how much of the gap between current skill and 100 counts as genuine remaining
        /// headroom. A 15-year-old might still have most of it ahead of them; a 26-year-old idol
        /// has largely already become who they're going to be. This is the whole point of the
        /// scouting fog Phase 5 builds on top — a weak-but-young trainee with a high roll here is
        /// exactly the "does she bloom at 19?" case DESIGN.md calls out. Revisit during balancing.
        /// </summary>
        private static float PotentialHeadroomFactor(int age)
        {
            if (age <= 16) return 0.85f;
            if (age >= 26) return 0.10f;
            return 0.85f - (age - 16) / 10f * 0.75f;
        }

        private static void AssignPotential(SimRandom rng, Person person, int tier, int age)
        {
            float coreMax = person.CoreAttributeMax();
            float headroom = Math.Max(0f, 100f - coreMax);
            float headroomFactor = PotentialHeadroomFactor(age);

            // DESIGN: better-tier pipelines find people with more genuine ceiling, but only a
            // little — a small multiplier, not a second big mean shift stacked on top of the
            // attribute tier bonus already applied. Revisit during balancing.
            float tierPotentialFactor = 0.9f + tier * 0.05f;

            float potentialMean = coreMax + headroom * headroomFactor * tierPotentialFactor;

            // DESIGN: wider spread for younger people (more headroom factor => bigger stdDev) is
            // what makes "weak 16-year-old, high potential" and "polished 17-year-old who never
            // improves" both reachable outcomes, per the Phase 2 brief. Revisit during balancing.
            float potentialStdDev = 6f + headroomFactor * 10f;

            float raw = rng.NextGaussian(potentialMean, potentialStdDev);
            person.Potential = Clamp(raw, coreMax, 100f);
        }

        /// <summary>
        /// DESIGN: roughly 80% Korean; the remaining 20% weighted toward the nationalities with
        /// the largest real overseas trainee pipelines (Japan, China), then a smaller Thai/American
        /// share, then a thin "Other" tail. Revisit once overseas expansion (out of MVP scope)
        /// makes this matter mechanically rather than just for flavour.
        /// </summary>
        private static Nationality RollNationality(SimRandom rng)
        {
            float r = rng.NextFloat();
            if (r < 0.80f) return Nationality.Korean;
            if (r < 0.86f) return Nationality.Japanese;
            if (r < 0.92f) return Nationality.Chinese;
            if (r < 0.95f) return Nationality.Thai;
            if (r < 0.98f) return Nationality.American;
            return Nationality.Other;
        }

        private static void AssignNames(SimRandom rng, Person person, WorldData worldData)
        {
            if (worldData == null)
            {
                // No content loaded — e.g. a unit test generating attributes in isolation. A
                // clearly-synthetic placeholder keeps PersonGenerator usable without a WorldData
                // rather than throwing.
                person.GivenName = "Trainee";
                person.FamilyName = "Doe";
                return;
            }

            if (person.Nationality == Nationality.Korean)
            {
                person.FamilyName = PickWeighted(rng, worldData.KoreanFamilyNames);
                person.GivenName = PickByGender(rng, worldData.KoreanGivenNames, person.Gender);

                // DESIGN: roughly 30% of Korean idols perform under a stage name distinct from
                // their given name; the rest use their real given name. Revisit during balancing.
                if (rng.Chance(0.30f) && worldData.StageNames.Count > 0)
                {
                    person.StageName = rng.Pick(worldData.StageNames);
                }
            }
            else
            {
                List<NameEntry> pool = worldData.GetForeignGivenNames(person.Nationality);
                person.GivenName = pool != null && pool.Count > 0
                    ? PickByGender(rng, pool, person.Gender)
                    : PickFallbackGivenName(rng, person.Gender, person.Nationality);

                person.FamilyName = PickFallbackFamilyName(rng, person.Nationality);

                // Non-Koreans always debut under a stage name — the real-world pattern of
                // international trainees adopting a Korean-market-friendly one.
                if (worldData.StageNames.Count > 0)
                {
                    person.StageName = rng.Pick(worldData.StageNames);
                }
            }
        }

        private static string PickWeighted(SimRandom rng, List<WeightedName> entries)
        {
            if (entries == null || entries.Count == 0) return "Kim";

            float total = 0f;
            for (int i = 0; i < entries.Count; i++) total += Math.Max(0f, entries[i].Weight);

            if (total <= 0f) return entries[0].Name;

            float roll = rng.NextFloat(0f, total);
            float cumulative = 0f;
            for (int i = 0; i < entries.Count; i++)
            {
                cumulative += Math.Max(0f, entries[i].Weight);
                if (roll < cumulative) return entries[i].Name;
            }

            return entries[entries.Count - 1].Name; // floating-point edge case at the top of the range
        }

        private static string PickByGender(SimRandom rng, List<NameEntry> entries, Gender gender)
        {
            List<NameEntry> matching = new List<NameEntry>();
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Gender == gender) matching.Add(entries[i]);
            }

            if (matching.Count == 0) matching = entries; // fallback if a pool is single-gender by mistake

            return rng.Pick(matching).Name;
        }

        private static string PickFallbackGivenName(SimRandom rng, Gender gender, Nationality nationality)
        {
            string[] pool = nationality == Nationality.American
                ? (gender == Gender.Female ? AmericanGivenFemale : AmericanGivenMale)
                : (gender == Gender.Female ? OtherGivenFemale : OtherGivenMale);
            return rng.Pick(pool);
        }

        private static string PickFallbackFamilyName(SimRandom rng, Nationality nationality)
        {
            return rng.Pick(nationality == Nationality.American ? AmericanFamily : OtherFamily);
        }

        private static void AssignLanguages(SimRandom rng, Person person)
        {
            if (person.Nationality == Nationality.Korean)
            {
                person.LanguageProficiency[Nationality.Korean] = 100;
            }
            else
            {
                // DESIGN: just starting out at a Korean company — a small random range rather
                // than a flat number so the trainee pool isn't uniform. Revisit once Phase 5's
                // language training exists to raise this over time.
                person.LanguageProficiency[Nationality.Korean] = 12 + rng.NextInt(0, 16);
                person.LanguageProficiency[person.Nationality] = 100;
            }
        }

        /// <summary>
        /// DESIGN: years training bounded by how long someone could plausibly have trained (not
        /// before roughly age 11), tier-agnostic for now — Phase 5's TrainingSystem is what will
        /// give this real meaning. Revisit once that exists.
        /// </summary>
        private static void AssignTraining(SimRandom rng, Person person, SimDate now, int age)
        {
            int maxPlausible = Math.Max(0, age - 11);
            int years = Clamp(rng.NextInt(0, maxPlausible + 1), 0, 6);

            person.YearsTraining = years;
            person.JoinedDate = now.AdvanceWeeks(-years * SimDate.WeeksPerYear);
        }

        private static float RollClamped(SimRandom rng, float mean, float stdDev)
        {
            return Clamp(rng.NextGaussian(mean, stdDev), 0f, 100f);
        }

        private static float Clamp(float value, float min, float max)
        {
            return value < min ? min : (value > max ? max : value);
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : (value > max ? max : value);
        }
    }
}
