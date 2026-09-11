using System;
using System.Collections.Generic;
using KpopManager.Core;
using KpopManager.Core.Systems.Generation;
using NUnit.Framework;

namespace KpopManager.Tests
{
    [TestFixture]
    public class GroupGeneratorTests
    {
        private static readonly Position[] MustBeUniquePerGroup =
        {
            Position.MainVocal, Position.MainRapper, Position.MainDancer, Position.Visual
        };

        [Test]
        public void MemberCount_AlwaysLandsInFourToNine_Across200Groups()
        {
            GameState state = TestFixtures.BuildState(2024UL, TestFixtures.BuildWorldData());

            for (int i = 0; i < 200; i++)
            {
                Group group = GroupGenerator.Generate(state, 0, i % PersonGenerator.TierCount, DebutDate(i));
                Assert.That(group.MemberIds.Count, Is.InRange(4, 9), "group " + i + " member count out of range");
            }
        }

        [Test]
        public void PrimaryArchetypes_AreNeverDuplicatedWithinAGroup_Across200Groups()
        {
            GameState state = TestFixtures.BuildState(2025UL, TestFixtures.BuildWorldData());

            for (int i = 0; i < 200; i++)
            {
                Group group = GroupGenerator.Generate(state, 0, i % PersonGenerator.TierCount, DebutDate(i));

                Dictionary<Position, int> counts = new Dictionary<Position, int>();
                foreach (int memberId in group.MemberIds)
                {
                    Position primary = state.GetPerson(memberId).Positions[0];
                    if (Array.IndexOf(MustBeUniquePerGroup, primary) < 0) continue;

                    counts[primary] = counts.TryGetValue(primary, out int c) ? c + 1 : 1;
                }

                foreach (KeyValuePair<Position, int> entry in counts)
                {
                    Assert.That(entry.Value, Is.LessThanOrEqualTo(1),
                        "group " + i + " has " + entry.Value + " members with primary " + entry.Key);
                }
            }
        }

        [Test]
        public void ContractExpiry_IsExactlyDebutDatePlusSevenYears()
        {
            GameState state = TestFixtures.BuildState(1UL, TestFixtures.BuildWorldData());

            for (int i = 0; i < 50; i++)
            {
                SimDate debut = DebutDate(i);
                Group group = GroupGenerator.Generate(state, 0, i % PersonGenerator.TierCount, debut);

                Assert.That(group.ContractExpiry, Is.EqualTo(debut.AdvanceYears(7)));
            }
        }

        [Test]
        public void EveryGroup_HasExactlyOneLeaderAndOneMaknae()
        {
            GameState state = TestFixtures.BuildState(3UL, TestFixtures.BuildWorldData());

            for (int i = 0; i < 50; i++)
            {
                Group group = GroupGenerator.Generate(state, 0, i % PersonGenerator.TierCount, DebutDate(i));

                int leaders = 0, maknaes = 0;
                foreach (int memberId in group.MemberIds)
                {
                    List<Position> positions = state.GetPerson(memberId).Positions;
                    if (positions.Contains(Position.Leader)) leaders++;
                    if (positions.Contains(Position.Maknae)) maknaes++;
                }

                Assert.That(leaders, Is.EqualTo(1), "group " + i + " should have exactly one Leader");
                Assert.That(maknaes, Is.EqualTo(1), "group " + i + " should have exactly one Maknae");
            }
        }

        [Test]
        public void GroupNames_DoNotRepeatWithinOneWorld()
        {
            GameState state = TestFixtures.BuildState(4UL, TestFixtures.BuildWorldData());
            HashSet<string> seen = new HashSet<string>();

            for (int i = 0; i < 30; i++)
            {
                Group group = GroupGenerator.Generate(state, 0, i % PersonGenerator.TierCount, DebutDate(i));
                Assert.That(seen.Add(group.Name), Is.True, "duplicate group name: " + group.Name);
            }
        }

        [Test]
        public void AllMembers_ShareTheGroupsGenderAndCenterId()
        {
            GameState state = TestFixtures.BuildState(9UL, TestFixtures.BuildWorldData());
            Group group = GroupGenerator.Generate(state, 42, 2, new SimDate(5, 1), Gender.Female);

            foreach (int memberId in group.MemberIds)
            {
                Person member = state.GetPerson(memberId);
                Assert.That(member.Gender, Is.EqualTo(Gender.Female));
                Assert.That(member.CenterId, Is.EqualTo(42));
                Assert.That(member.GroupId, Is.EqualTo(group.Id));
            }
        }

        private static SimDate DebutDate(int offset) => new SimDate(1, 1).AdvanceWeeks(-offset * 3);
    }
}
