namespace PersonalVault.Desktop.ViewModels;

/// <summary>
/// Provides the initial locked shell. Unlock behavior belongs to a later milestone.
/// </summary>
public sealed class MainWindowViewModel
{
    public MainWindowViewModel()
    {
        IsLocked = true;
        StatusText = "Vault locked";
    }

    public bool IsLocked { get; }

    public string StatusText { get; }
}
