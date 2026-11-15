using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TableForge.ViewModels;

namespace TableForge.Views;

public partial class RollView : UserControl
{
    public RollView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SizeChanged += (_, _) => UpdateCompleteResultHeight();
        RollControls.SizeChanged += (_, _) => UpdateCompleteResultHeight();
    }

    // ---- Complete Result panel: bounded, so the trail and the Roll controls always keep their room ----------------------

    /// <summary>The panel's body takes at most this share of the Roll screen's height…</summary>
    public const double CompleteResultShare = 0.30;

    /// <summary>…and never more than this, however tall the window.</summary>
    public const double CompleteResultMaxBody = 240;

    /// <summary>The smallest body worth showing (about one caption and one line of result). Below that the panel still opens,
    /// but scrolls within this height.</summary>
    public const double CompleteResultMinBody = 44;

    /// <summary>Room the trail keeps beyond the Roll controls themselves, so they are never the only thing in view.</summary>
    public const double TrailReserve = 48;

    /// <summary>
    /// Keyboard scrolling for the Complete Result, without making its body another Tab stop: while its toggle or Copy Complete
    /// Result has focus, Page Up / Page Down scroll the body a page, Home / End go to its start / end. Neither control uses
    /// these keys itself. Nothing happens while the panel is collapsed.
    /// </summary>
    private void OnCompleteResultKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || !CompleteResultScroller.IsVisible) return;
        switch (e.Key)
        {
            case Key.PageDown: CompleteResultScroller.PageDown(); break;
            case Key.PageUp: CompleteResultScroller.PageUp(); break;
            case Key.Home: CompleteResultScroller.ScrollToTop(); break;
            case Key.End: CompleteResultScroller.ScrollToEnd(); break;
            default: return;
        }
        e.Handled = true;
    }

    /// <summary>
    /// Bounds the Complete Result's body: <see cref="CompleteResultShare"/> of the screen, at most <see cref="CompleteResultMaxBody"/>,
    /// and — in a small window, or with Recent Rolls open — only what is left once the trail has room for the whole Roll
    /// controls block plus <see cref="TrailReserve"/>. The body shrinks first; the trail is never squeezed for it.
    /// </summary>
    private void UpdateCompleteResultHeight()
    {
        var available = ActualHeight;
        if (available <= 0) return;
        var header = CompleteResultPanel.Padding.Top + CompleteResultPanel.Padding.Bottom + CompleteResultPanel.BorderThickness.Bottom
            + Math.Max(CompleteResultExpander.ActualHeight - CompleteResultScroller.ActualHeight - CompleteResultScroller.Margin.Top, 30)
            + CompleteResultScroller.Margin.Top;
        var trailNeeds = RollControls.ActualHeight + RollControls.Margin.Top + TrailContent.Margin.Top + TrailContent.Margin.Bottom + TrailReserve;
        var body = Math.Min(Math.Min(available * CompleteResultShare, CompleteResultMaxBody), available - header - trailNeeds);
        CompleteResultScroller.MaxHeight = Math.Max(CompleteResultMinBody, Math.Floor(body));
    }

    private int _stepCount = 1;

    // ---- Modifier box: arriving at it selects its whole value, so typing replaces the "0" instead of joining it ("+20") ----

    /// <summary>Tab, Shift+Tab or any other way in selects the whole value.</summary>
    private void OnModifierGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => ModifierBox.SelectAll();

    /// <summary>
    /// The aligned table's own scroll area only scrolls sideways, but a ScrollViewer takes every mouse-wheel turn over it: pass
    /// the turn on to the page, so the wheel scrolls the Roll screen wherever the pointer is.
    /// </summary>
    private void OnAlignedMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled) return;
        e.Handled = true;
        TrailScroller.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = sender });
    }

    /// <summary>
    /// The first click into the box only focuses it (selecting the whole value), instead of also placing the caret beside the
    /// "0". Once it has focus, clicks place the caret as usual so the value can be edited.
    /// </summary>
    private void OnModifierPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ModifierBox.IsKeyboardFocusWithin) return;
        ModifierBox.Focus();
        e.Handled = true;
    }

    /// <summary>The view model reset the value (a roll used it up) while the box still has focus: select it, ready to be typed over.</summary>
    private void OnModifierTargetUpdated(object? sender, DataTransferEventArgs e)
    {
        if (ModifierBox.IsKeyboardFocused) ModifierBox.SelectAll();
    }

    /// <summary>Export… opens its menu under the button, by mouse or keyboard alike.</summary>
    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        ExportMenu.PlacementTarget = ExportButton;
        ExportMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        ExportMenu.IsOpen = true;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged old) old.PropertyChanged -= OnViewModelChanged;
        if (e.NewValue is INotifyPropertyChanged now) now.PropertyChanged += OnViewModelChanged;

        // This view is reused when another table is opened, so start that table at its top, ready to roll.
        TrailScroller.ScrollToTop();
        CompleteResultScroller.ScrollToTop();
        _stepCount = (e.NewValue as RollViewModel)?.Steps.Count ?? 1;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => RollButton.Focus());
    }

    /// <summary>A new roll or a followed link changes <see cref="RollViewModel.Results"/>; bring the new content into view.</summary>
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The Complete Result changes before Results, so its newest part is brought into view first, then the trail scrolls.
        if (e.PropertyName == nameof(RollViewModel.CompleteResult))
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => CompleteResultScroller.ScrollToEnd());
        if (e.PropertyName == nameof(RollViewModel.Results))
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ScrollToLatest); // after the new visuals have been laid out
    }

    /// <summary>
    /// Scrolls so the newest roll (or, right after following a link, the new step) starts at the top of the view,
    /// unless that would push the Roll controls out of sight, in which case the controls stay visible.
    /// Older steps are left exactly as they are.
    /// </summary>
    private void ScrollToLatest()
    {
        if (DataContext is not RollViewModel vm || vm.Steps.Count == 0) return;
        UpdateLayout();

        // After following a link, focus goes to Roll for the new table. After an ordinary roll it stays where it was,
        // so someone typing several manual rolls in a row keeps their cursor in the box.
        if (vm.Steps.Count != _stepCount)
        {
            _stepCount = vm.Steps.Count;
            RollButton.Focus();
        }

        var step = vm.Current;
        FrameworkElement? stepElement = FindContainer(StepsList, step);
        var anchor = step.Outcomes.Count > 0 && stepElement is not null
            ? FindContainer(stepElement, step.Outcomes[^1]) ?? stepElement
            : stepElement;
        if (anchor is null) return;

        var anchorTop = TopWithin(anchor, TrailContent);
        var controlsBottom = TopWithin(RollControls, TrailContent) + RollControls.ActualHeight + TrailContent.Margin.Bottom;
        var offset = Math.Max(anchorTop, controlsBottom - TrailScroller.ViewportHeight);
        TrailScroller.ScrollToVerticalOffset(Math.Max(0, offset));
    }

    private static double TopWithin(UIElement element, UIElement ancestor) =>
        element.TransformToAncestor(ancestor).Transform(new Point(0, 0)).Y;

    /// <summary>Finds the generated container (the element wrapping one item) below <paramref name="root"/>.</summary>
    private static FrameworkElement? FindContainer(DependencyObject root, object item)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ContentPresenter presenter && ReferenceEquals(presenter.Content, item)) return presenter;
            if (FindContainer(child, item) is { } found) return found;
        }
        return null;
    }
}
