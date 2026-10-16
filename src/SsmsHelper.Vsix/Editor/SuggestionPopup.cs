using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using SsmsHelper.Core.Joins;

namespace SsmsHelper.Editor;

/// <summary>
/// List of suggestions anchored under a buffer position. Never takes keyboard focus: the command
/// filter drives selection, so typing keeps going to the editor. Colors follow the SSMS theme.
/// </summary>
internal sealed class SuggestionPopup
{
    public const int PageSize = 8;

    private readonly IWpfTextView _view;
    private readonly Popup _popup;
    private readonly ListBox _list;

    public SuggestionPopup(IWpfTextView view)
    {
        _view = view;

        _list = new ListBox
        {
            BorderThickness = new Thickness(0),
            Focusable = false,
            MaxHeight = 240,
            MinWidth = 280,
            ItemContainerStyle = CreateItemStyle(),
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.SetResourceReference(Control.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
        _list.SetResourceReference(Control.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        _list.MouseDoubleClick += (_, _) => ItemChosen?.Invoke(this, EventArgs.Empty);

        var border = new Border { BorderThickness = new Thickness(1), Child = _list };
        border.SetResourceReference(Border.BorderBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);

        _popup = new Popup
        {
            Child = border,
            Placement = PlacementMode.Relative,
            PlacementTarget = view.VisualElement,
            StaysOpen = true,
            Focusable = false,
            AllowsTransparency = false,
        };
    }

    /// <summary>Raised when the user double-clicks an item.</summary>
    public event EventHandler? ItemChosen;

    public bool IsOpen => _popup.IsOpen;

    public JoinSuggestion? Selected => (_list.SelectedItem as ListBoxItem)?.Tag as JoinSuggestion;

    public void SetItems(IReadOnlyList<JoinSuggestion> suggestions)
    {
        var (fontFamily, fontSize) = EditorFont();
        _list.Items.Clear();
        foreach (var suggestion in suggestions)
        {
            _list.Items.Add(new ListBoxItem { Content = Row(suggestion, fontFamily, fontSize), Tag = suggestion });
        }

        _list.SelectedIndex = 0;
        _list.ScrollIntoView(_list.SelectedItem);
    }

    public void MoveSelection(int delta)
    {
        if (_list.Items.Count == 0)
        {
            return;
        }

        var index = Math.Max(0, Math.Min(_list.Items.Count - 1, _list.SelectedIndex + delta));
        _list.SelectedIndex = index;
        _list.ScrollIntoView(_list.SelectedItem);
    }

    /// <summary>Opens or moves the popup under <paramref name="anchor"/>. Returns false if it is not visible.</summary>
    public bool Show(SnapshotPoint anchor)
    {
        if (_view.IsClosed || _view.TextViewLines is null)
        {
            return false;
        }

        var line = _view.TextViewLines.GetTextViewLineContainingBufferPosition(anchor);
        if (line is null || line.VisibilityState == VisibilityState.Unattached || line.VisibilityState == VisibilityState.Hidden)
        {
            return false;
        }

        var bounds = line.GetCharacterBounds(anchor);
        var zoom = _view.ZoomLevel / 100.0;
        _popup.HorizontalOffset = (bounds.Left - _view.ViewportLeft) * zoom;
        _popup.VerticalOffset = (bounds.Bottom - _view.ViewportTop) * zoom;
        _popup.IsOpen = true;
        return true;
    }

    public void Hide() => _popup.IsOpen = false;

    private (FontFamily Family, double Size) EditorFont()
    {
        var properties = _view.FormattedLineSource?.DefaultTextProperties;
        return (properties?.Typeface.FontFamily ?? new FontFamily("Consolas"), properties?.FontRenderingEmSize ?? 13);
    }

    private static UIElement Row(JoinSuggestion suggestion, FontFamily fontFamily, double fontSize)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = suggestion.Condition,
            FontFamily = fontFamily,
            FontSize = fontSize,
            FontStyle = suggestion.Kind == JoinSuggestionKind.NameMatch ? FontStyles.Italic : FontStyles.Normal,
        });
        panel.Children.Add(new TextBlock
        {
            Text = suggestion.Description,
            Margin = new Thickness(16, 0, 0, 0),
            Opacity = 0.65,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return panel;
    }

    /// <summary>Item template with theme colors; the default template ignores Background when selected.</summary>
    private static Style CreateItemStyle()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));

        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(UIElement.FocusableProperty, false));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 2, 10, 2)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(ListBoxItem)) { VisualTree = border }));

        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(EnvironmentColors.SystemHighlightBrushKey)));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(EnvironmentColors.SystemHighlightTextBrushKey)));
        style.Triggers.Add(selected);
        return style;
    }
}
