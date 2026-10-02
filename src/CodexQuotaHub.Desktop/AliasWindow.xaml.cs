using System.Windows;

namespace CodexQuotaHub.Desktop;

public partial class AliasWindow : Window
{
    public AliasWindow(string identityText, string? alias)
    {
        InitializeComponent();
        AccountIdentityText.Text = identityText;
        AliasText.Text = alias ?? "";
        Loaded += (_, _) =>
        {
            AliasText.Focus();
            AliasText.SelectAll();
        };
    }

    public string ResultAlias => AliasText.Text.Trim();

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
