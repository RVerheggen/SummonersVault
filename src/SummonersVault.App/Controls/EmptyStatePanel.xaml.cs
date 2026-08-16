using System.Windows;
using System.Windows.Controls;

namespace SummonersVault.App.Controls;

public partial class EmptyStatePanel : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(EmptyStatePanel),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message),
        typeof(string),
        typeof(EmptyStatePanel),
        new PropertyMetadata(string.Empty));

    public EmptyStatePanel()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }
}
