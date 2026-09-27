namespace Oaza.Domain.Time;

/// <summary>
/// A closed interval of calendar days <c>[From, To]</c> — both ends included, as
/// every period in the association's accounting is (rule 6). A one-day range has
/// <c>From == To</c>.
/// </summary>
public readonly record struct DateRange
{
    public DateOnly From { get; }
    public DateOnly To { get; }

    public DateRange(DateOnly from, DateOnly to)
    {
        if (from > to)
            throw new ArgumentException($"Období musí začínat nejpozději v den konce ({from:yyyy-MM-dd} > {to:yyyy-MM-dd}).", nameof(from));
        From = from;
        To = to;
    }

    /// <summary>Number of days, both ends included (<c>To − From + 1</c>).</summary>
    public int Days => To.DayNumber - From.DayNumber + 1;

    public bool Contains(DateOnly day) => day >= From && day <= To;

    /// <summary>True when the ranges share at least one day.</summary>
    public bool Overlaps(DateRange other) => From <= other.To && other.From <= To;

    /// <summary>The common days of both ranges, or null when they do not overlap.</summary>
    public DateRange? Intersect(DateRange other)
    {
        var from = From > other.From ? From : other.From;
        var to = To < other.To ? To : other.To;
        return from <= to ? new DateRange(from, to) : null;
    }

    /// <summary>
    /// Splits at a cut day: <c>[From, cut − 1]</c> and <c>[cut, To]</c>. A cut on
    /// <see cref="From"/> or outside the range leaves the range whole (one part).
    /// </summary>
    public IReadOnlyList<DateRange> SplitAt(DateOnly cut)
    {
        if (cut <= From || cut > To)
            return [this];
        return [new DateRange(From, cut.AddDays(-1)), new DateRange(cut, To)];
    }

    public override string ToString() => $"{From:yyyy-MM-dd}–{To:yyyy-MM-dd}";
}
