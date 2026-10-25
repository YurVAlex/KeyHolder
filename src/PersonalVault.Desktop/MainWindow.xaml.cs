using System.Windows;
using PersonalVault.Desktop.ViewModels;

namespace PersonalVault.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}