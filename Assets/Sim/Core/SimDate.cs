using System;

namespace KpopManager.Core
{
    // A point in simulation time: a year and a week within that year.
    //
    // The sim runs on weeks, never on dates. There is no DateTime anywhere in Core, and there
    // never will be. A year is exactly WeeksPerYear weeks; we are not modelling a real calendar,
    // leap weeks, or ISO week numbering.
    //
    // Weeks are 1-based, so a year runs W1 to W52 inclusive. Note that default(SimDate) is Y0 W0,
    // which is not a valid date; use Start for the beginning of a run.
    public readonly struct SimDate : IComparable<SimDate>, IEquatable<SimDate>
    {
        public const int WeeksPerYear = 52;

        // The canonical start of a new game: Y1 W1.
        public static readonly SimDate Start = new SimDate(1, 1);

        public int Year { get; }

        // 1 to 52 inclusive.
        public int Week { get; }

        // Throws ArgumentOutOfRangeException if week is outside 1 to 52.
        public SimDate(int year, int week)
        {
            if (week < 1 || week > WeeksPerYear)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(week), week, "Week must be between 1 and " + WeeksPerYear + " inclusive.");
            }

            Year = year;
            Week = week;
        }

        // Total weeks since the notional Y0 W1. The single ordinal that every comparison and
        // every span calculation is derived from.
        public int TotalWeeks => Year * WeeksPerYear + (Week - 1);

        // Negative values move backwards.
        public SimDate AdvanceWeeks(int weeks)
        {
            return FromTotalWeeks(TotalWeeks + weeks);
        }

        public SimDate AdvanceYears(int years)
        {
            return new SimDate(Year + years, Week);
        }

        // Rebuilds a date from the ordinal produced by TotalWeeks.
        public static SimDate FromTotalWeeks(int totalWeeks)
        {
            // C# integer division truncates towards zero, which would be wrong for negative
            // ordinals, so floor the division explicitly. Negative ordinals are not expected in
            // normal play but they show up whenever something asks "what was the date 5 years
            // before the start of the run".
            int year = totalWeeks / WeeksPerYear;
            int remainder = totalWeeks % WeeksPerYear;

            if (remainder < 0)
            {
                remainder += WeeksPerYear;
                year -= 1;
            }

            return new SimDate(year, remainder + 1);
        }

        // Weeks from a to b. Positive when b is later, negative when it is earlier.
        public static int WeeksBetween(SimDate a, SimDate b)
        {
            return b.TotalWeeks - a.TotalWeeks;
        }

        public int CompareTo(SimDate other) => TotalWeeks.CompareTo(other.TotalWeeks);

        public bool Equals(SimDate other) => Year == other.Year && Week == other.Week;

        public override bool Equals(object obj) => obj is SimDate other && Equals(other);

        public override int GetHashCode() => TotalWeeks;

        // "Y3 W17"
        public override string ToString() => "Y" + Year + " W" + Week;

        public static bool operator ==(SimDate a, SimDate b) => a.Equals(b);
        public static bool operator !=(SimDate a, SimDate b) => !a.Equals(b);
        public static bool operator <(SimDate a, SimDate b) => a.TotalWeeks < b.TotalWeeks;
        public static bool operator >(SimDate a, SimDate b) => a.TotalWeeks > b.TotalWeeks;
        public static bool operator <=(SimDate a, SimDate b) => a.TotalWeeks <= b.TotalWeeks;
        public static bool operator >=(SimDate a, SimDate b) => a.TotalWeeks >= b.TotalWeeks;
    }
}
