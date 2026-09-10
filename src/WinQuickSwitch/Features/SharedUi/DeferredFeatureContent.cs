using System.Windows;
using System.Windows.Controls;

namespace WinQuickSwitch.Features.SharedUi;

/// <summary>
/// Builds a hidden widget's full shared view only when layout first needs it.
/// Measurement (including content fitting) always sees the real child, and
/// subsequent refreshes, visibility changes and measures reuse that instance.
/// </summary>
internal sealed class DeferredFeatureContent : Decorator
{
    private Func<UIElement>? _createContent;

    public DeferredFeatureContent(Func<UIElement> createContent)
    {
        ArgumentNullException.ThrowIfNull(createContent);
        _createContent = createContent;
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (_createContent is not null)
        {
            Child = _createContent() ?? throw new InvalidOperationException(
                "The shared feature factory returned no content.");
            _createContent = null;
        }

        return base.MeasureOverride(constraint);
    }
}
