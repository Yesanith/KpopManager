using System.Runtime.CompilerServices;

// Lets KpopManager.Tests exercise a handful of ChartSystem's internal formula pieces directly
// (competition modifier, raw BuzzScore) rather than re-deriving the same formula inside the test
// itself, which would only prove the test agrees with itself.
[assembly: InternalsVisibleTo("KpopManager.Tests")]
