namespace WinQuickSwitch.Features.Overlay;

/// <summary>
/// Packs only generated first-run/reset layouts using their fitted dimensions.
/// Never used for restoring or moving user cards. If a small work area cannot
/// fit every minimum-sized card, use a bounded cascade rather than shrink and
/// then accidentally push cards into one another during minimum enforcement.
/// </summary>
internal static class OverlayInitialLayoutArranger
{
    public static IReadOnlyList<OverlayWorkspaceWidgetLayout> Arrange(
        IReadOnlyList<OverlayWorkspaceWidgetLayout> layouts,
        double width,
        double height,
        double gap = 12)
    {
        List<OverlayWorkspaceWidgetLayout> placed = [];
        foreach (OverlayWorkspaceWidgetLayout layout in layouts
                     .OrderByDescending(item => item.Height)
                     .ThenByDescending(item => item.Width)
                     .ThenBy(item => item.ZIndex)
                     .ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            double[] xs = placed.Select(item => item.Left + item.Width + gap)
                .Prepend(gap).Distinct().Order().ToArray();
            double[] ys = placed.Select(item => item.Top + item.Height + gap)
                .Prepend(gap).Distinct().Order().ToArray();
            OverlayWorkspaceWidgetLayout? result = null;
            foreach (double y in ys)
            {
                foreach (double x in xs)
                {
                    if (x + layout.Width > width - gap ||
                        y + layout.Height > height - gap)
                    {
                        continue;
                    }
                    OverlayWorkspaceWidgetLayout candidate = layout with { Left = x, Top = y };
                    if (placed.All(other =>
                            candidate.Left >= other.Left + other.Width + gap ||
                            candidate.Left + candidate.Width + gap <= other.Left ||
                            candidate.Top >= other.Top + other.Height + gap ||
                            candidate.Top + candidate.Height + gap <= other.Top))
                    {
                        result = candidate;
                        break;
                    }
                }
                if (result is not null) break;
            }

            placed.Add(result ?? layout with
            {
                Left = Math.Clamp(gap + placed.Count * 36, 0, Math.Max(0, width - layout.Width)),
                Top = Math.Clamp(gap + placed.Count * 36, 0, Math.Max(0, height - layout.Height)),
            });
        }

        return layouts.Select(layout => placed.Single(item => item.Id == layout.Id)).ToArray();
    }
}
