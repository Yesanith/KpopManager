using System.Collections.Generic;
using KpopManager.Core;
using KpopManager.Core.Systems.Generation;
using NUnit.Framework;

namespace KpopManager.Tests
{
    [TestFixture]
    public class PersonGeneratorTests
    {
        private static readonly SimDate Now = new SimDate(20, 1);

        [Test]
        public void GeneratedAttributes_AlwaysStayWithinZeroToHundred_Over10000Generations()
        {
            SimRandom rng = new SimRandom(1UL);

            for (int i = 0; i < 10000; i++)
            {
                int birthYear = Now.Year - rng.NextInt(13, 30);
                int tier = rng.NextInt(0, PersonGenerator.TierCount);
                PersonStatus status = rng.Chance(0.3f) ? PersonStatus.Trainee : PersonStatus.Active;

                Person person = PersonGenerator.Generate(rng, birthYear, tier, status, Now);

                foreach (float value in AllZeroToHundredAttributes(person))
                {
                    Assert.That(value, Is.InRange(0f, 100f), "attribute out of range on generation " + i);
                }
            }
        }

        [Test]
        public void Potential_IsNeverBelowCoreAttributeMax_Over10000Generations()
        {
            SimRandom rng = new SimRandom(321UL);

            for (int i = 0; i < 10000; i++)
            {
                int birthYear = Now.Year - rng.NextInt(13, 30);
                int tier = rng.NextInt(0, PersonGenerator.TierCount);

                Person person = PersonGenerator.Generate(rng, birthYear, tier, PersonStatus.Active, Now);

                Assert.That(person.Potential, Is.GreaterThanOrEqualTo(person.CoreAttributeMax()),
                    "a person can't have already exceeded their own ceiling (generation " + i + ")");
            }
        }

        [Test]
        public void MainVocal_MeanVocalSignificantlyExceedsMeanRap_Across1000Generations()
        {
            SimRandom rng = new SimRandom(7UL);
            int birthYear = Now.Year - 20;

            double vocalSum = 0d, rapSum = 0d;
            const int n = 1000;

            for (int i = 0; i < n; i++)
            {
                Person person = PersonGenerator.Generate(rng, birthYear, 2, PersonStatus.Active, Now, null, Position.MainVocal, null);
                vocalSum += person.Vocal;
                rapSum += person.Rap;
            }

            double vocalMean = vocalSum / n;
            double rapMean = rapSum / n;

            Assert.That(vocalMean - rapMean, Is.GreaterThan(15d),
                "MainVocal mean Vocal (" + vocalMean + ") should clear mean Rap (" + rapMean + ") by a healthy margin");
        }

        [Test]
        public void MainRapper_MeanRapSignificantlyExceedsMeanVocal_Across1000Generations()
        {
            SimRandom rng = new SimRandom(8UL);
            int birthYear = Now.Year - 20;

            double vocalSum = 0d, rapSum = 0d;
            const int n = 1000;

            for (int i = 0; i < n; i++)
            {
                Person person = PersonGenerator.Generate(rng, birthYear, 2, PersonStatus.Active, Now, null, Position.MainRapper, null);
                vocalSum += person.Vocal;
                rapSum += person.Rap;
            }

            Assert.That(rapSum / n - vocalSum / n, Is.GreaterThan(15d), "MainRapper should read as the inverse of MainVocal");
        }

        [TestCase(0, 32f)]
        [TestCase(1, 42f)]
        [TestCase(2, 54f)]
        [TestCase(3, 68f)]
        [TestCase(4, 82f)]
        public void Distribution_MeanAndStdDev_FallInExpectedBandPerTier(int tier, float expectedMean)
        {
            // Fixed at age 23 so AgeFactor is exactly 1.0 and doesn't confound the tier signal.
            // Variety carries no archetype bonus, so it's a clean read on the tier mean alone.
            SimRandom rng = new SimRandom(42UL + (ulong)tier);
            int birthYear = Now.Year - 23;

            const int n = 3000;
            double sum = 0d, sumSq = 0d;

            for (int i = 0; i < n; i++)
            {
                Person person = PersonGenerator.Generate(rng, birthYear, tier, PersonStatus.Active, Now);
                sum += person.Variety;
                sumSq += (double)person.Variety * person.Variety;
            }

            double mean = sum / n;
            double variance = sumSq / n - mean * mean;
            double stdDev = System.Math.Sqrt(System.Math.Max(0d, variance));

            Assert.That(mean, Is.EqualTo(expectedMean).Within(5.0), "tier " + tier + " mean drifted");
            Assert.That(stdDev, Is.InRange(8.0, 18.0), "tier " + tier + " stddev outside a sane band");
        }

        [Test]
        public void YoungerPeople_HaveLowerCurrentAttributesThanOlderPeopleAtTheSameTier()
        {
            SimRandom rng = new SimRandom(99UL);
            const int n = 1000;

            double youngSum = 0d, oldSum = 0d;
            for (int i = 0; i < n; i++)
            {
                Person young = PersonGenerator.Generate(rng, Now.Year - 15, 2, PersonStatus.Trainee, Now);
                Person old = PersonGenerator.Generate(rng, Now.Year - 24, 2, PersonStatus.Active, Now);
                youngSum += young.Variety;
                oldSum += old.Variety;
            }

            Assert.That(oldSum / n, Is.GreaterThan(youngSum / n), "a 24-year-old should read as more realised than a 15-year-old at the same tier");
        }

        [Test]
        public void WithoutWorldData_StillProducesAUsablePerson()
        {
            SimRandom rng = new SimRandom(5UL);
            Person person = PersonGenerator.Generate(rng, Now.Year - 18, 1, PersonStatus.Trainee, Now);

            Assert.That(person.GivenName, Is.Not.Null.And.Not.Empty);
            Assert.That(person.DisplayName, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void WithWorldData_KoreanPeople_GetNamesFromTheKoreanPools()
        {
            WorldData data = TestFixtures.BuildWorldData();
            SimRandom rng = new SimRandom(6UL);

            bool foundAny = false;
            for (int i = 0; i < 200; i++)
            {
                Person person = PersonGenerator.Generate(rng, Now.Year - 18, 1, PersonStatus.Trainee, Now, null, null, data);
                if (person.Nationality != Nationality.Korean) continue;

                foundAny = true;
                bool matches = false;
                foreach (NameEntry entry in data.KoreanGivenNames)
                {
                    if (entry.Name == person.GivenName) { matches = true; break; }
                }
                Assert.That(matches, Is.True, "Korean person's given name should come from the Korean given-name pool");
            }

            Assert.That(foundAny, Is.True, "expected at least one Korean person across 200 draws");
        }

        private static IEnumerable<float> AllZeroToHundredAttributes(Person p)
        {
            yield return p.Vocal; yield return p.Rap; yield return p.Dance; yield return p.StagePresence;
            yield return p.Visual; yield return p.Charisma; yield return p.Variety; yield return p.FanConnection;
            yield return p.Songwriting; yield return p.Composition; yield return p.Choreography;
            yield return p.WorkEthic; yield return p.MentalResilience; yield return p.Ambition; yield return p.Professionalism;
            yield return p.Potential;
            yield return p.Morale; yield return p.Fatigue; yield return p.Health; yield return p.InGroupPopularity;
        }
    }
}
