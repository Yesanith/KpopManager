using System;
using System.Collections.Generic;
using KpopManager.Core;
using NUnit.Framework;

namespace KpopManager.Tests
{
    [TestFixture]
    public class SimRandomTests
    {
        private const int DistributionSamples = 100000;

        [Test]
        public void NextInt_IsUniformWithinTwoPercentPerBucket()
        {
            SimRandom random = new SimRandom(4242UL);
            int[] buckets = new int[10];

            for (int i = 0; i < DistributionSamples; i++)
            {
                buckets[random.NextInt(0, 10)]++;
            }

            double expected = DistributionSamples / 10.0;
            double tolerance = expected * 0.02;

            for (int i = 0; i < buckets.Length; i++)
            {
                Assert.That(buckets[i], Is.EqualTo(expected).Within(tolerance),
                    "Bucket " + i + " is outside 2% of uniform.");
            }
        }

        [Test]
        public void NextInt_StaysWithinItsRange()
        {
            SimRandom random = new SimRandom(9UL);

            for (int i = 0; i < 10000; i++)
            {
                int value = random.NextInt(-5, 5);
                Assert.That(value, Is.InRange(-5, 4));
            }
        }

        [Test]
        public void NextInt_RejectsAnEmptyRange()
        {
            SimRandom random = new SimRandom(1UL);

            Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(3, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(3, 2));
        }

        [Test]
        public void NextFloat_StaysInTheUnitInterval()
        {
            SimRandom random = new SimRandom(31337UL);

            for (int i = 0; i < DistributionSamples; i++)
            {
                float value = random.NextFloat();
                Assert.That(value, Is.GreaterThanOrEqualTo(0f));
                Assert.That(value, Is.LessThan(1f));
            }
        }

        [Test]
        public void NextFloat_Ranged_StaysWithinBounds()
        {
            SimRandom random = new SimRandom(8UL);

            for (int i = 0; i < 10000; i++)
            {
                float value = random.NextFloat(-2.5f, 7.5f);
                Assert.That(value, Is.GreaterThanOrEqualTo(-2.5f));
                Assert.That(value, Is.LessThan(7.5f));
            }
        }

        [Test]
        public void NextGaussian_HasTheRequestedMeanAndDeviation()
        {
            SimRandom random = new SimRandom(555UL);

            double sum = 0d;
            double sumOfSquares = 0d;

            for (int i = 0; i < DistributionSamples; i++)
            {
                double value = random.NextGaussian(0f, 1f);
                sum += value;
                sumOfSquares += value * value;
            }

            double mean = sum / DistributionSamples;
            double variance = sumOfSquares / DistributionSamples - mean * mean;
            double stdDev = Math.Sqrt(variance);

            Assert.That(mean, Is.EqualTo(0d).Within(0.02d), "Gaussian mean drifted.");
            Assert.That(stdDev, Is.EqualTo(1d).Within(0.02d), "Gaussian standard deviation drifted.");
        }

        [Test]
        public void NextGaussian_ScalesWithMeanAndDeviation()
        {
            SimRandom random = new SimRandom(556UL);

            double sum = 0d;
            for (int i = 0; i < DistributionSamples; i++)
            {
                sum += random.NextGaussian(50f, 10f);
            }

            Assert.That(sum / DistributionSamples, Is.EqualTo(50d).Within(0.2d));
        }

        [Test]
        public void StateRoundTrip_ReplaysTheSameValues()
        {
            SimRandom random = new SimRandom(123456UL);

            // Burn a few draws first so the captured state is not the freshly seeded one.
            for (int i = 0; i < 37; i++)
            {
                random.NextUInt();
            }

            SimRandomState snapshot = random.GetState();

            List<uint> first = new List<uint>(100);
            for (int i = 0; i < 100; i++)
            {
                first.Add(random.NextUInt());
            }

            random.SetState(snapshot);

            for (int i = 0; i < 100; i++)
            {
                Assert.That(random.NextUInt(), Is.EqualTo(first[i]), "Draw " + i + " differed after restore.");
            }
        }

        [Test]
        public void StateRoundTrip_PreservesTheCachedGaussianSpare()
        {
            SimRandom random = new SimRandom(654321UL);

            // An odd number of Gaussian draws leaves a spare cached. Restoring without it would
            // desynchronise every later Gaussian, which is exactly the save/load bug this guards.
            random.NextGaussian(0f, 1f);

            SimRandomState snapshot = random.GetState();
            Assert.That(snapshot.HasGaussianSpare, Is.True, "Expected a cached spare after one draw.");

            List<float> first = new List<float>(100);
            for (int i = 0; i < 100; i++)
            {
                first.Add(random.NextGaussian(0f, 1f));
            }

            random.SetState(snapshot);

            for (int i = 0; i < 100; i++)
            {
                Assert.That(random.NextGaussian(0f, 1f), Is.EqualTo(first[i]), "Gaussian " + i + " differed after restore.");
            }
        }

        [Test]
        public void RestoringIntoAFreshInstance_ContinuesTheSameStream()
        {
            SimRandom original = new SimRandom(31UL);
            for (int i = 0; i < 500; i++)
            {
                original.NextUInt();
            }

            SimRandom restored = new SimRandom(original.GetState());

            for (int i = 0; i < 100; i++)
            {
                Assert.That(restored.NextUInt(), Is.EqualTo(original.NextUInt()));
            }
        }

        [Test]
        public void Chance_ClampsAtBothEnds()
        {
            SimRandom random = new SimRandom(11UL);

            for (int i = 0; i < 1000; i++)
            {
                Assert.That(random.Chance(0f), Is.False);
                Assert.That(random.Chance(1f), Is.True);
                Assert.That(random.Chance(-3f), Is.False);
                Assert.That(random.Chance(3f), Is.True);
            }
        }

        [Test]
        public void Chance_ApproximatesItsProbability()
        {
            SimRandom random = new SimRandom(12UL);
            int hits = 0;

            for (int i = 0; i < DistributionSamples; i++)
            {
                if (random.Chance(0.25f)) hits++;
            }

            Assert.That(hits / (double)DistributionSamples, Is.EqualTo(0.25d).Within(0.01d));
        }

        [Test]
        public void Pick_SelectsEveryItemAndNothingElse()
        {
            SimRandom random = new SimRandom(13UL);
            List<string> items = new List<string> { "a", "b", "c" };
            Dictionary<string, int> counts = new Dictionary<string, int> { { "a", 0 }, { "b", 0 }, { "c", 0 } };

            for (int i = 0; i < 3000; i++)
            {
                counts[random.Pick(items)]++;
            }

            foreach (string key in items)
            {
                Assert.That(counts[key], Is.GreaterThan(0), "Pick never returned " + key + ".");
            }

            Assert.That(counts["a"] + counts["b"] + counts["c"], Is.EqualTo(3000));
        }

        [Test]
        public void Pick_RejectsAnEmptyList()
        {
            SimRandom random = new SimRandom(14UL);
            Assert.Throws<ArgumentException>(() => random.Pick(new List<int>()));
        }

        [Test]
        public void Shuffle_PreservesEveryElement()
        {
            SimRandom random = new SimRandom(15UL);
            List<int> items = new List<int>();
            for (int i = 0; i < 100; i++)
            {
                items.Add(i);
            }

            random.Shuffle(items);

            items.Sort();
            for (int i = 0; i < 100; i++)
            {
                Assert.That(items[i], Is.EqualTo(i), "Shuffle lost or duplicated an element.");
            }
        }

        [Test]
        public void Shuffle_ActuallyReorders()
        {
            SimRandom random = new SimRandom(16UL);
            List<int> items = new List<int>();
            for (int i = 0; i < 100; i++)
            {
                items.Add(i);
            }

            random.Shuffle(items);

            bool moved = false;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != i)
                {
                    moved = true;
                    break;
                }
            }

            Assert.That(moved, Is.True, "Shuffle left a 100-element list in its original order.");
        }

        [Test]
        public void Shuffle_IsDeterministicForASeed()
        {
            List<int> a = new List<int>();
            List<int> b = new List<int>();
            for (int i = 0; i < 50; i++)
            {
                a.Add(i);
                b.Add(i);
            }

            new SimRandom(99UL).Shuffle(a);
            new SimRandom(99UL).Shuffle(b);

            Assert.That(b, Is.EqualTo(a));
        }
    }
}
