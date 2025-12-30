using System.Windows;
using Perigon.MiniDb.Client.ViewModels;

namespace Perigon.MiniDb.Client;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}