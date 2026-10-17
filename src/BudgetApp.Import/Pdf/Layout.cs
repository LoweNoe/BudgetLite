namespace BudgetApp.Import.Pdf;

// A word as it sits on the page. PDF coordinates grow upwards, so a larger Bottom is higher up.
public sealed record LayoutWord(string Text, double Left, double Right, double Bottom, double Height)
{
    public double Center => (Left + Right) / 2;
}

public sealed record LayoutLine(int Page, double Y, IReadOnlyList<LayoutWord> Words)
{
    public string Text => string.Join(' ', Words.Select(w => w.Text));
    public double Height => Words.Count == 0 ? 0 : Words.Max(w => w.Height);
    public double Left => Words.Count == 0 ? 0 : Words[0].Left;

    // Words on one baseline (within half a line height) form a line, read left to right.
    public static IReadOnlyList<LayoutLine> Group(IEnumerable<LayoutWord> words, int page)
    {
        var lines = new List<LayoutLine>();
        var current = new List<LayoutWord>();
        var baseline = 0.0;

        foreach (var word in words.OrderByDescending(w => w.Bottom).ThenBy(w => w.Left))
        {
            var tolerance = Math.Max(1.5, Math.Min(word.Height, current.Count > 0 ? current[0].Height : word.Height) * 0.5);
            if (current.Count > 0 && Math.Abs(word.Bottom - baseline) > tolerance)
            {
                lines.Add(new LayoutLine(page, baseline, current.OrderBy(w => w.Left).ToList()));
                current = [];
            }

            if (current.Count == 0)
                baseline = word.Bottom;
            current.Add(word);
        }

        if (current.Count > 0)
            lines.Add(new LayoutLine(page, baseline, current.OrderBy(w => w.Left).ToList()));

        return lines;
    }
}
