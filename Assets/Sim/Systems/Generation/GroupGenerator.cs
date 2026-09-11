using System;
using System.Collections.Generic;

namespace KpopManager.Core.Systems.Generation
{
    /// <summary>
    /// Builds one fully-populated <see cref="Group"/>: rolls a member count, generates each member
    /// via <see cref="PersonGenerator"/> with a non-duplicated primary archetype, tags a Leader and
    /// a Maknae, names it, and seeds its <see cref="Fandom"/>.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="PersonGenerator"/>, this registers everything it creates directly onto
    /// <paramref name="state"/> as it goes — a group's members only make sense already linked to
    /// their group, so there is no useful "just return the data" mode to offer here.
    /// </remarks>
    public static class GroupGenerator
    {
        private const int MinMembers = 4;
        private const int MaxMembers = 9;

        /// <summary>Must appear at most once per group — the four singular performance archetypes.</summary>
        private static readonly Position[] UniquePrimaries =
        {
            Position.MainVocal, Position.MainRapper, Position.MainDancer, Position.Visual
        };

        /// <summary>May repeat to fill out a group beyond the four unique primaries.</summary>
        private static readonly Position[] SecondaryPool =
        {
            Position.LeadVocal, Position.LeadRapper, Position.LeadDancer, Position.AllRounder
        };

        /// <summary>
        /// Generates and registers a group under <paramref name="centerId"/>.
        /// </summary>
        /// <param name="gender">Pin the group's gender, or leave null to roll one (50/50) — DESIGN.md's
        /// girl-group/boy-group/co-ed question is still open, so nothing here decides it.</param>
        public static Group Generate(GameState state, int centerId, int tier, SimDate debutDate, Gender? gender = null)
        {
            SimRandom rng = state.Random;
            WorldData data = state.WorldData;

            Gender groupGender = gender ?? (rng.Chance(0.5f) ? Gender.Female : Gender.Male);
            int memberCount = RollMemberCount(rng);

            List<Position> archetypes = BuildArchetypeList(rng, memberCount);
            List<Person> members = GenerateMembers(state, rng, data, centerId, tier, debutDate, groupGender, archetypes);

            TagLeaderAndMaknae(members);

            Group group = new Group
            {
                Id = state.AllocateEntityId(),
                Name = PickGroupName(rng, data, state),
                CenterId = centerId,
                DebutDate = debutDate,
                ContractExpiry = debutDate.AdvanceYears(7),
                Tier = (GroupTier)tier,
                IsActive = true,
                Gender = groupGender
            };

            for (int i = 0; i < members.Count; i++)
            {
                group.MemberIds.Add(members[i].Id);
                members[i].GroupId = group.Id;
            }

            SeedFandom(rng, group, tier);
            state.AddGroup(group);

            return group;
        }

        /// <summary>
        /// Picks a group name that isn't already in use elsewhere in <paramref name="state"/> —
        /// two unrelated companies both running a group called "Ivory Tower" reads as a generator
        /// bug, not a coincidence, so a straight <c>rng.Pick</c> isn't enough once the world has a
        /// few dozen groups in it. Retries a bounded number of times, then falls back to a
        /// numbered suffix rather than looping forever if the pool is ever exhausted.
        /// </summary>
        private static string PickGroupName(SimRandom rng, WorldData data, GameState state)
        {
            if (data == null || data.GroupNames.Count == 0) return "Unnamed Group " + (state.Groups.Count + 1);

            const int maxAttempts = 20;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                string candidate = rng.Pick(data.GroupNames);
                if (!IsGroupNameTaken(state, candidate)) return candidate;
            }

            // Every attempt collided — extremely unlikely at MVP scale, but draw once more and
            // disambiguate rather than give up and accept a duplicate.
            string fallback = rng.Pick(data.GroupNames);
            int suffix = 2;
            while (IsGroupNameTaken(state, fallback + " " + suffix)) suffix++;
            return fallback + " " + suffix;
        }

        private static bool IsGroupNameTaken(GameState state, string name)
        {
            for (int i = 0; i < state.Groups.Count; i++)
            {
                if (state.Groups[i].Name == name) return true;
            }
            return false;
        }

        /// <summary>
        /// DESIGN: average of two uniform draws over [4,9] — a cheap way to bias toward 5–7
        /// without a real triangular distribution. Simple, deterministic, revisit if balancing
        /// wants a sharper peak.
        /// </summary>
        private static int RollMemberCount(SimRandom rng)
        {
            int a = rng.NextInt(MinMembers, MaxMembers + 1);
            int b = rng.NextInt(MinMembers, MaxMembers + 1);
            return (a + b) / 2;
        }

        /// <summary>Builds one archetype per member: the four unique primaries first (every group
        /// has at least <see cref="MinMembers"/> = 4, so all four always fit), then secondary
        /// archetypes (which may repeat) for the rest, then shuffles so archetype doesn't
        /// correlate with member index.</summary>
        private static List<Position> BuildArchetypeList(SimRandom rng, int memberCount)
        {
            List<Position> archetypes = new List<Position>(memberCount);

            for (int i = 0; i < UniquePrimaries.Length && archetypes.Count < memberCount; i++)
            {
                archetypes.Add(UniquePrimaries[i]);
            }

            while (archetypes.Count < memberCount)
            {
                archetypes.Add(rng.Pick(SecondaryPool));
            }

            rng.Shuffle(archetypes);
            return archetypes;
        }

        private static List<Person> GenerateMembers(
            GameState state, SimRandom rng, WorldData data, int centerId, int tier, SimDate debutDate,
            Gender groupGender, List<Position> archetypes)
        {
            List<Person> members = new List<Person>(archetypes.Count);
            int debutYear = debutDate.Year;

            for (int i = 0; i < archetypes.Count; i++)
            {
                // DESIGN: debut ages spread 16–23 — matches typical K-pop debut age ranges.
                // Revisit during balancing.
                int debutAge = 16 + rng.NextInt(0, 8);
                int birthYear = debutYear - debutAge;

                Person person = PersonGenerator.Generate(
                    rng, birthYear, tier, PersonStatus.Active, state.Date,
                    groupGender, archetypes[i], data);

                person.Id = state.AllocateEntityId();
                person.CenterId = centerId;
                state.AddPerson(person);
                members.Add(person);
            }

            return members;
        }

        /// <summary>
        /// Leader: highest combined Professionalism + WorkEthic + Charisma — a proxy for "who the
        /// company trusts to represent the group publicly." Maknae: the youngest member (latest
        /// birth year), chosen from everyone except the leader. Both are added as an extra
        /// <see cref="Position"/> tag alongside the member's existing performance archetype.
        /// DESIGN: the leader and maknae are always two different people — a group's designated
        /// leader being simultaneously its babied youngest member would collapse two distinct
        /// mechanics into one, and isn't how real groups are structured. If the youngest member
        /// also happens to be the best leader candidate, maknae falls back to the next-youngest
        /// rather than the leader tag being skipped or doubled up. Revisit Leader's scoring
        /// formula during balancing — Ambition or age might read better than Charisma.
        /// </summary>
        private static void TagLeaderAndMaknae(List<Person> members)
        {
            Person leader = members[0];
            float bestScore = ScoreForLeader(leader);
            for (int i = 1; i < members.Count; i++)
            {
                float score = ScoreForLeader(members[i]);
                if (score > bestScore)
                {
                    bestScore = score;
                    leader = members[i];
                }
            }
            leader.Positions.Add(Position.Leader);

            Person maknae = null;
            for (int i = 0; i < members.Count; i++)
            {
                if (ReferenceEquals(members[i], leader)) continue;
                if (maknae == null || members[i].BirthYear > maknae.BirthYear) maknae = members[i];
            }

            // members.Count is always >= MinMembers (4), so excluding the leader still leaves at
            // least three candidates — maknae is never actually null here.
            maknae?.Positions.Add(Position.Maknae);
        }

        private static float ScoreForLeader(Person person)
        {
            return person.Professionalism + person.WorkEthic + person.Charisma;
        }

        /// <summary>
        /// DESIGN: fandom size scales roughly geometrically with tier so the gap between a Rookie
        /// and a Legendary group is large, matching the power-law shape DESIGN.md's chart-formula
        /// validation targets. Heavily revisited once Phase 3's chart sim gives real signal.
        /// </summary>
        private static void SeedFandom(SimRandom rng, Group group, int tier)
        {
            long[] tierSizeMean = { 8_000L, 40_000L, 150_000L, 600_000L, 2_500_000L };
            float sizeNoise = rng.NextFloat(0.6f, 1.4f);
            group.Fandom.Size = (long)(tierSizeMean[tier] * sizeNoise);

            group.Fandom.Sentiment = Clamp(rng.NextGaussian(60f + tier * 5f, 12f), 0f, 100f);
            group.Fandom.PublicAwareness = Clamp(rng.NextGaussian(30f + tier * 12f, 15f), 0f, 100f);
        }

        private static float Clamp(float value, float min, float max)
        {
            return value < min ? min : (value > max ? max : value);
        }
    }
}
