using System.Collections.Generic;

namespace KpopManager.Core
{
    // What part of the game an entry came from. Drives filtering in the harness and, from
    // Phase 7, the news feed.
    public enum LogCategory
    {
        System,
        Chart,
        Group,
        Person,
        Finance,
        Board,
        Event,
        Rival
    }

    // How much the player should care.
    //
    // DESIGN: four levels rather than a numeric importance score, because the news feed needs
    // discrete visual treatments (hidden / plain line / highlighted / headline) and a float would
    // just get bucketed into four anyway. Debug entries are harness-only and are never shown to
    // the player. Revisit if the feed starts feeling too noisy or too sparse.
    public enum LogSeverity
    {
        Debug,
        Info,
        Notable,
        Major
    }

    // One line of simulation output. Immutable.
    public readonly struct SimLogEntry
    {
        // Used for RelatedEntityId when the entry is not about a specific entity.
        public const int NoEntity = -1;

        public SimDate Date { get; }
        public LogCategory Category { get; }
        public LogSeverity Severity { get; }
        public string Message { get; }
        public int RelatedEntityId { get; }

        public SimLogEntry(
            SimDate date,
            LogCategory category,
            LogSeverity severity,
            string message,
            int relatedEntityId = NoEntity)
        {
            Date = date;
            Category = category;
            Severity = severity;
            Message = message ?? string.Empty;
            RelatedEntityId = relatedEntityId;
        }

        // A flat, fully-specified rendering of this entry, used by the determinism test to
        // compare two runs by value. Every field takes part, so a divergence anywhere shows up.
        public string ToStableString()
        {
            return Date.ToString() + "|" + Category + "|" + Severity + "|" + RelatedEntityId + "|" + Message;
        }

        public override string ToString() => ToStableString();
    }

    // The simulation's only output channel.
    //
    // Core cannot call Debug.Log and would not compile if it tried. Everything a system wants to
    // say goes here; the Editor harness prints it now and the news feed renders it from Phase 7.
    // Entries are appended in tick order, so the list is always sorted by date.
    public sealed class SimLog
    {
        private readonly List<SimLogEntry> _entries = new List<SimLogEntry>();

        public IReadOnlyList<SimLogEntry> Entries => _entries;

        public int Count => _entries.Count;

        public void Add(
            SimDate date,
            LogCategory category,
            LogSeverity severity,
            string message,
            int relatedEntityId = SimLogEntry.NoEntity)
        {
            _entries.Add(new SimLogEntry(date, category, severity, message, relatedEntityId));
        }

        public void Add(SimLogEntry entry)
        {
            _entries.Add(entry);
        }

        // Returns every entry recorded on the given week, oldest first.
        public List<SimLogEntry> GetForWeek(SimDate date)
        {
            var result = new List<SimLogEntry>();

            // Entries are appended in tick order and therefore already sorted by date, so the
            // scan can stop as soon as it passes the week being asked about.
            for (int i = 0; i < _entries.Count; i++)
            {
                SimLogEntry entry = _entries[i];
                if (entry.Date < date) continue;
                if (entry.Date > date) break;

                result.Add(entry);
            }

            return result;
        }

        // Discards every entry. Does not affect the simulation in any way.
        public void Clear()
        {
            _entries.Clear();
        }
    }
}
