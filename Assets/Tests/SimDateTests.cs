using System;
using KpopManager.Core;
using NUnit.Framework;

namespace KpopManager.Tests
{
    [TestFixture]
    public class SimDateTests
    {
        [Test]
        public void AdvanceWeeks_RollsOverFromWeek52ToWeek1()
        {
            SimDate end = new SimDate(3, 52);
            SimDate next = end.AdvanceWeeks(1);

            Assert.That(next.Year, Is.EqualTo(4));
            Assert.That(next.Week, Is.EqualTo(1));
        }

        [Test]
        public void AdvanceWeeks_RollsBackwardsAcrossAYearBoundary()
        {
            SimDate start = new SimDate(4, 1);
            SimDate previous = start.AdvanceWeeks(-1);

            Assert.That(previous.Year, Is.EqualTo(3));
            Assert.That(previous.Week, Is.EqualTo(52));
        }

        [Test]
        public void AdvanceWeeks_CrossesMultipleYears()
        {
            SimDate date = new SimDate(1, 1).AdvanceWeeks(52 * 3 + 16);

            Assert.That(date.Year, Is.EqualTo(4));
            Assert.That(date.Week, Is.EqualTo(17));
        }

        [Test]
        public void AdvanceWeeks_ByZero_ReturnsTheSameDate()
        {
            SimDate date = new SimDate(7, 23);
            Assert.That(date.AdvanceWeeks(0), Is.EqualTo(date));
        }

        [Test]
        public void WeeksBetween_IsPositiveForwardAndNegativeBackward()
        {
            SimDate a = new SimDate(2, 50);
            SimDate b = new SimDate(3, 3);

            Assert.That(SimDate.WeeksBetween(a, b), Is.EqualTo(5));
            Assert.That(SimDate.WeeksBetween(b, a), Is.EqualTo(-5));
            Assert.That(SimDate.WeeksBetween(a, a), Is.EqualTo(0));
        }

        [Test]
        public void WeeksBetween_SpansWholeYearsExactly()
        {
            Assert.That(SimDate.WeeksBetween(new SimDate(1, 9), new SimDate(6, 9)), Is.EqualTo(260));
        }

        [Test]
        public void WeeksBetween_IsTheInverseOfAdvanceWeeks()
        {
            SimDate start = new SimDate(12, 44);

            for (int weeks = -200; weeks <= 200; weeks += 7)
            {
                SimDate moved = start.AdvanceWeeks(weeks);
                Assert.That(SimDate.WeeksBetween(start, moved), Is.EqualTo(weeks));
            }
        }

        [Test]
        public void Comparisons_OrderByYearThenWeek()
        {
            SimDate early = new SimDate(2, 10);
            SimDate late = new SimDate(2, 11);
            SimDate nextYear = new SimDate(3, 1);
            SimDate earlyAgain = new SimDate(2, 10);

            Assert.That(early < late, Is.True);
            Assert.That(late > early, Is.True);
            Assert.That(late < nextYear, Is.True);
            Assert.That(early <= earlyAgain, Is.True);
            Assert.That(early >= earlyAgain, Is.True);
            Assert.That(early < nextYear, Is.True);

            Assert.That(early.CompareTo(late), Is.LessThan(0));
            Assert.That(late.CompareTo(early), Is.GreaterThan(0));
            Assert.That(early.CompareTo(early), Is.EqualTo(0));
        }

        [Test]
        public void Equality_ComparesByValue()
        {
            SimDate a = new SimDate(5, 5);
            SimDate b = new SimDate(5, 5);
            SimDate c = new SimDate(5, 6);

            Assert.That(a == b, Is.True);
            Assert.That(a != c, Is.True);
            Assert.That(a.Equals(b), Is.True);
            Assert.That(a.Equals((object)b), Is.True);
            Assert.That(a.Equals(c), Is.False);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }

        [Test]
        public void ToString_UsesTheYearWeekShorthand()
        {
            Assert.That(new SimDate(3, 17).ToString(), Is.EqualTo("Y3 W17"));
            Assert.That(new SimDate(1, 1).ToString(), Is.EqualTo("Y1 W1"));
        }

        [Test]
        public void Constructor_RejectsWeeksOutsideTheYear()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimDate(1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimDate(1, 53));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimDate(1, -4));
        }

        [Test]
        public void FromTotalWeeks_RoundTripsThroughTotalWeeks()
        {
            for (int ordinal = -104; ordinal <= 520; ordinal++)
            {
                SimDate date = SimDate.FromTotalWeeks(ordinal);
                Assert.That(date.TotalWeeks, Is.EqualTo(ordinal));
                Assert.That(date.Week, Is.InRange(1, SimDate.WeeksPerYear));
            }
        }

        [Test]
        public void AdvanceYears_KeepsTheWeek()
        {
            SimDate date = new SimDate(2, 31).AdvanceYears(7);

            Assert.That(date.Year, Is.EqualTo(9));
            Assert.That(date.Week, Is.EqualTo(31));
        }
    }
}
