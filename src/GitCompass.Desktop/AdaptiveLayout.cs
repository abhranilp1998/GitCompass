using System.Windows;
using System.Windows.Controls;

namespace GitCompass.Desktop;

internal sealed class AdaptiveColumnsGrid : Grid
{
    private readonly int columns;
    private readonly double breakpoint;
    public bool IsStacked { get; private set; }
    public AdaptiveColumnsGrid(int count, double stackBelow)
    {
        columns = count; breakpoint = stackBelow;
        for (var index = 0; index < count; index++) ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        Loaded += (_, _) => Reflow(); SizeChanged += (_, _) => Reflow();
    }
    private void Reflow()
    {
        if (ActualWidth <= 0) return;
        IsStacked = ActualWidth < breakpoint;
        RowDefinitions.Clear();
        if (IsStacked) for (var index = 0; index < columns; index++) RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (var index = 0; index < ColumnDefinitions.Count; index++)
            ColumnDefinitions[index].Width = index == 0 || !IsStacked ? new(1, GridUnitType.Star) : new(0);
        for (var index = 0; index < Children.Count; index++)
        {
            SetColumn(Children[index], IsStacked ? 0 : index);
            SetRow(Children[index], IsStacked ? index : 0);
        }
    }
}

internal sealed class AdaptiveSettingsGrid : Grid
{
    private readonly FrameworkElement description;
    private readonly FrameworkElement action;
    public bool IsStacked { get; private set; }
    public AdaptiveSettingsGrid(FrameworkElement description, FrameworkElement action)
    {
        this.description = description; this.action = action;
        ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new() { Height = GridLength.Auto });
        Children.Add(description); Children.Add(action); SetColumn(action, 1);
        Loaded += (_, _) => Reflow(); SizeChanged += (_, _) => Reflow();
    }
    private void Reflow()
    {
        if (ActualWidth <= 0) return;
        IsStacked = ActualWidth < 600;
        ColumnDefinitions[1].Width = IsStacked ? new(0) : GridLength.Auto;
        SetRow(action, IsStacked ? 1 : 0); SetColumn(action, IsStacked ? 0 : 1);
        description.Margin = IsStacked ? new(0) : new(0, 0, 18, 0);
        action.Margin = IsStacked ? new(0, 8, 0, 0) : new(0);
        action.VerticalAlignment = IsStacked ? VerticalAlignment.Top : VerticalAlignment.Center;
        action.HorizontalAlignment = HorizontalAlignment.Left;
        action.MaxWidth = ActualWidth;
    }
}

public sealed class StringContentTemplateSelector : DataTemplateSelector
{
    public DataTemplate? StringTemplate { get; set; }
    public override DataTemplate? SelectTemplate(object item, DependencyObject container) => item is string ? StringTemplate : null;
}
