using System;

namespace KpopManager.Core
{
    /// <summary>
    /// A point in simulation time: a year and a week within that year.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sim runs on weeks, never on dates. There is no <c>DateTime</c> anywhere in Core, and
    /// there never will be. A year is exactly <see cref="WeeksPerYear"/> weeks; we are not
    /// modelling a real calendar, leap weeks, or ISO week numbering.
    /// </para>
    /// <para>
    /// Weeks are 1-based, so a year runs W1 to W52 inclusive. Note that <c>default(SimDate)</c>
    /// is Y0 W0, which is not a valid date; use <see cref="Start"/> for the beginning of a run.
    /// </para>
    /// </remarks>
    public readonly struct SimDate : IComparable<SimDate>, IEquatable<SimDate>
    {
        /// <summary>Weeks in a simulation year. Exactly 52, by design.</summary>
        public const int WeeksPerYear = 52;

        /// <summary>The canonical start of a new game: Y1 W1.</summary>
        public static readonly SimDate Start = new SimDate(1, 1);

        /// <summary>The year. Year 1 is the first year of a new game.</summary>
        public int Year { get; }

        /// <summary>The week within the year, 1 to 52 inclusive.</summary>
        public int Week { get; }

        /// <summary>Creates a date.</summary>
        /// <exception cref="ArgumentOutOfRangeException">If <paramref name="week"/> is outside 1 to 52.</exception>
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

        /// <summary>
        /// Total weeks since the notional Y0 W1. The single ordinal that every comparison and
        /// every span calculation is derived from.
        /// </summary>
        public int TotalWeeks => Year * WeeksPerYear + (Week - 1);

        /// <summary>Returns a date <paramref name="weeks"/> later. Negative values move backwards.</summary>
        public SimDate AdvanceWeeks(int weeks)
        {
            return FromTotalWeeks(TotalWeeks + weeks);
        }

        /// <summary>Returns a date <paramref name="years"/> later, keeping the same week.</summary>
        public SimDate AdvanceYears(int years)
        {
            return new SimDate(Year + years, Week);
        }

        /// <summary>Rebuilds a date from the ordinal produced by <see cref="TotalWeeks"/>.</summary>
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

        /// <summary>
        /// Weeks from <paramref name="a"/> to <paramref name="b"/>. Positive when
        /// <paramref name="b"/> is later, negative when it is earlier.
        /// </summary>
        public static int WeeksBetween(SimDate a, SimDate b)
        {
            return b.TotalWeeks - a.TotalWeeks;
        }

        public int CompareTo(SimDate other) => TotalWeeks.CompareTo(other.TotalWeeks);

        public bool Equals(SimDate other) => Year == other.Year && Week == other.Week;

        public override bool Equals(object obj) => obj is SimDate other && Equals(other);

        public override int GetHashCode() => TotalWeeks;

        /// <summary>Formats as <c>"Y3 W17"</c>.</summary>
        public override string ToString() => "Y" + Year + " W" + Week;

        public static bool operator ==(SimDate a, SimDate b) => a.Equals(b);
        public static bool operator !=(SimDate a, SimDate b) => !a.Equals(b);
        public static bool operator <(SimDate a, SimDate b) => a.TotalWeeks < b.TotalWeeks;
        public static bool operator >(SimDate a, SimDate b) => a.TotalWeeks > b.TotalWeeks;
        public static bool operator <=(SimDate a, SimDate b) => a.TotalWeeks <= b.TotalWeeks;
        public static bool operator >=(SimDate a, SimDate b) => a.TotalWeeks >= b.TotalWeeks;
    }
}
