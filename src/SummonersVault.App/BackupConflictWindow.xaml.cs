using System.Windows;
using SummonersVault.App.Services;
using SummonersVault.Application.Abstractions;

namespace SummonersVault.App;

public partial class BackupConflictWindow : Window
{
    internal BackupConflictWindow(BackupConflict conflict)
    {
        InitializeComponent();
        DarkTitleBar.Attach(this);
        AccountNameText.Text = conflict.DisplayName;
        Loaded += (_, _) => System.Windows.Input.Keyboard.Focus(KeepCurrentButton);
    }

    internal BackupConflictChoice? Choice { get; private set; }

    private void KeepCurrent_Click(object sender, RoutedEventArgs e)
    {
        Choice = BackupConflictChoice.KeepCurrent;
        DialogResult = true;
    }

    private void UseImported_Click(object sender, RoutedEventArgs e)
    {
        Choice = BackupConflictChoice.UseImported;
        DialogResult = true;
    }
}
