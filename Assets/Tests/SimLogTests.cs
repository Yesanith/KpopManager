using System.Collections.Generic;
using KpopManager.Core;
using NUnit.Framework;

namespace KpopManager.Tests
{
    [TestFixture]
    public class SimLogTests
    {
        [Test]
        public void Add_AppendsInOrder()
        {
            SimLog log = new SimLog();
            log.Add(new SimDate(1, 1), LogCategory.System, LogSeverity.Info, "first");
            log.Add(new SimDate(1, 2), LogCategory.Chart, LogSeverity.Major, "second", 42);

            Assert.That(log.Count, Is.EqualTo(2));
            Assert.That(log.Entries[0].Message, Is.EqualTo("first"));
            Assert.That(log.Entries[1].Message, Is.EqualTo("second"));
            Assert.That(log.Entries[1].RelatedEntityId, Is.EqualTo(42));
            Assert.That(log.Entries[0].RelatedEntityId, Is.EqualTo(SimLogEntry.NoEntity));
        }

        [Test]
        public void GetForWeek_ReturnsOnlyThatWeek()
        {
            SimLog log = new SimLog();
            log.Add(new SimDate(1, 1), LogCategory.System, LogSeverity.Info, "w1");
            log.Add(new SimDate(1, 2), LogCategory.System, LogSeverity.Info, "w2 a");
            log.Add(new SimDate(1, 2), LogCategory.Group, LogSeverity.Notable, "w2 b");
            log.Add(new SimDate(1, 3), LogCategory.System, LogSeverity.Info, "w3");

            List<SimLogEntry> week2 = log.GetForWeek(new SimDate(1, 2));

            Assert.That(week2.Count, Is.EqualTo(2));
            Assert.That(week2[0].Message, Is.EqualTo("w2 a"));
            Assert.That(week2[1].Message, Is.EqualTo("w2 b"));
        }

        [Test]
        public void GetForWeek_ReturnsEmptyForAQuietWeek()
        {
            SimLog log = new SimLog();
            log.Add(new SimDate(1, 1), LogCategory.System, LogSeverity.Info, "only");

            Assert.That(log.GetForWeek(new SimDate(1, 2)), Is.Empty);
        }

        [Test]
        public void Clear_EmptiesTheLog()
        {
            SimLog log = new SimLog();
            log.Add(new SimDate(1, 1), LogCategory.System, LogSeverity.Info, "gone");
            log.Clear();

            Assert.That(log.Count, Is.EqualTo(0));
            Assert.That(log.Entries, Is.Empty);
        }

        [Test]
        public void ToStableString_IncludesEveryField()
        {
            SimLogEntry entry = new SimLogEntry(
                new SimDate(3, 17), LogCategory.Finance, LogSeverity.Major, "payday", 7);

            Assert.That(entry.ToStableString(), Is.EqualTo("Y3 W17|Finance|Major|7|payday"));
        }

        [Test]
        public void NullMessage_BecomesEmptyRatherThanThrowing()
        {
            SimLogEntry entry = new SimLogEntry(
                new SimDate(1, 1), LogCategory.System, LogSeverity.Debug, null);

            Assert.That(entry.Message, Is.EqualTo(string.Empty));
        }
    }
}
