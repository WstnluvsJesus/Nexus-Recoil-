using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace Nexus_Recoil;

public partial class ChangelogWindow : Window
{
    public ChangelogWindow(string tagName, string body)
    {
        InitializeComponent();
        TitleText.Text = $"What's New — {tagName}";
        BodyText.Text  = body;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    private void DismissBtn_Click(object sender, RoutedEventArgs e) => Close();

    private void DownloadBtn_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo(
            "https://github.com/n1k0fr/Nexus-Recoil/releases/latest")
            { UseShellExecute = true });
        Close();
    }
}
