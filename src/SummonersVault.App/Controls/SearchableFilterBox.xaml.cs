using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using SummonersVault.Core.Services;

namespace SummonersVault.App.Controls;

public partial class SearchableFilterBox : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(SearchableFilterBox),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, TextChanged));

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable<string>),
        typeof(SearchableFilterBox),
        new PropertyMetadata(null, ItemsSourceChanged));

    private INotifyCollectionChanged? _observedCollection;
    private bool _isCommitting;
    private Window? _ownerWindow;

    private readonly ObservableCollection<SearchSuggestionItem> _visibleSuggestions = [];

    public string Text
    {
        get => (string?)GetValue(TextProperty) ?? string.Empty;
        set => SetValue(TextProperty, value);
    }

    public IEnumerable<string>? ItemsSource
    {
        get => (IEnumerable<string>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public SearchableFilterBox()
    {
        InitializeComponent();
        SuggestionList.ItemsSource = _visibleSuggestions;
        Loaded += SearchableFilterBox_Loaded;
        Unloaded += SearchableFilterBox_Unloaded;
    }

    private static void TextChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs _) =>
        ((SearchableFilterBox)dependencyObject).RefreshSuggestions(openWhenFocused: true);

    private static void ItemsSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var control = (SearchableFilterBox)dependencyObject;
        control.ObserveCollection(args.OldValue as INotifyCollectionChanged, args.NewValue as INotifyCollectionChanged);
        control.RefreshSuggestions(openWhenFocused: false);
    }

    private void SearchableFilterBox_Loaded(object sender, RoutedEventArgs e)
    {
        _ownerWindow = Window.GetWindow(this);
        if (_ownerWindow is not null)
        {
            _ownerWindow.PreviewMouseDown += OwnerWindow_PreviewMouseDown;
            _ownerWindow.Deactivated += OwnerWindow_Deactivated;
        }

        string automationName = AutomationProperties.GetName(this);
        if (!string.IsNullOrWhiteSpace(automationName))
        {
            AutomationProperties.SetName(Input, automationName);
            AutomationProperties.SetName(DropDownToggle, $"{automationName} suggestions");
        }
        RefreshSuggestions(openWhenFocused: false);
    }

    private void SearchableFilterBox_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_ownerWindow is null)
        {
            return;
        }

        _ownerWindow.PreviewMouseDown -= OwnerWindow_PreviewMouseDown;
        _ownerWindow.Deactivated -= OwnerWindow_Deactivated;
        _ownerWindow = null;
    }

    private void ObserveCollection(INotifyCollectionChanged? oldCollection, INotifyCollectionChanged? newCollection)
    {
        oldCollection?.CollectionChanged -= SuggestionsChanged;

        _observedCollection = newCollection;
        _observedCollection?.CollectionChanged += SuggestionsChanged;
    }

    private void SuggestionsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RefreshSuggestions(openWhenFocused: false);

    private void RefreshSuggestions(bool openWhenFocused)
    {
        if (SuggestionList is null)
        {
            return;
        }

        IReadOnlyList<SearchSuggestionItem> matches = SearchSuggestionMatcher.Match(ItemsSource ?? [], Text);
        _visibleSuggestions.Clear();
        foreach (SearchSuggestionItem match in matches)
        {
            _visibleSuggestions.Add(match);
        }

        SuggestionList.SelectedIndex = _visibleSuggestions.Count > 0 ? 0 : -1;

        if (_visibleSuggestions.Count == 0)
        {
            ClosePopup();
            return;
        }

        if (openWhenFocused && Input.IsKeyboardFocusWithin && !_isCommitting)
        {
            OpenPopup();
        }
    }

    private void Input_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F4 || e.Key == Key.Down && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            TogglePopup();
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            bool wasOpen = SuggestionPopup.IsOpen;
            EnsurePopupOpen();
            if (wasOpen)
            {
                MoveActiveSuggestion(1);
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            bool wasOpen = SuggestionPopup.IsOpen;
            EnsurePopupOpen();
            if (wasOpen)
            {
                MoveActiveSuggestion(-1);
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Enter && SuggestionPopup.IsOpen && SuggestionList.SelectedItem is SearchSuggestionItem item)
        {
            Commit(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && SuggestionPopup.IsOpen)
        {
            ClosePopup();
            e.Handled = true;
        }
        else if (e.Key == Key.Tab)
        {
            ClosePopup();
        }
    }

    private void DropDownToggle_Click(object sender, RoutedEventArgs e)
    {
        Input.Focus();
        TogglePopup();
    }

    private void OwnerWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (SuggestionPopup.IsOpen && !IsMouseOver)
        {
            ClosePopup();
        }
    }

    private void OwnerWindow_Deactivated(object? sender, EventArgs e) => ClosePopup();

    private void SuggestionList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if (ItemsControl.ContainerFromElement(SuggestionList, source) is ListBoxItem { DataContext: SearchSuggestionItem item })
        {
            Commit(item);
        }
    }

    private void SuggestionPopup_Closed(object? sender, EventArgs e) => DropDownToggle.IsChecked = false;

    private void TogglePopup()
    {
        if (SuggestionPopup.IsOpen)
        {
            ClosePopup();
        }
        else
        {
            EnsurePopupOpen();
        }
    }

    private void EnsurePopupOpen()
    {
        RefreshSuggestions(openWhenFocused: false);
        if (_visibleSuggestions.Count > 0)
        {
            OpenPopup();
        }
    }

    private void OpenPopup()
    {
        SuggestionPopup.IsOpen = true;
        DropDownToggle.IsChecked = true;
        if (SuggestionList.SelectedIndex < 0 && _visibleSuggestions.Count > 0)
        {
            SuggestionList.SelectedIndex = 0;
        }

        SuggestionList.ScrollIntoView(SuggestionList.SelectedItem);
    }

    private void ClosePopup()
    {
        if (SuggestionPopup is null)
        {
            return;
        }

        SuggestionPopup.IsOpen = false;
        DropDownToggle?.IsChecked = false;
    }

    private void MoveActiveSuggestion(int delta)
    {
        if (_visibleSuggestions.Count == 0)
        {
            return;
        }

        int current = SuggestionList.SelectedIndex < 0 ? 0 : SuggestionList.SelectedIndex;
        SuggestionList.SelectedIndex = Math.Clamp(current + delta, 0, _visibleSuggestions.Count - 1);
        SuggestionList.ScrollIntoView(SuggestionList.SelectedItem);
    }

    private void Commit(SearchSuggestionItem item)
    {
        _isCommitting = true;
        try
        {
            SetCurrentValue(TextProperty, item.Value);
            ClosePopup();
            Input.Focus();
            Input.CaretIndex = Input.Text.Length;
        }
        finally
        {
            _isCommitting = false;
        }
    }
}
