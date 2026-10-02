using System.Windows;
using CodexQuotaHub.Core;

namespace CodexQuotaHub.Desktop;

public partial class ResetCreditsWindow : Window
{
    public ResetCreditsWindow(AccountCardViewModel card)
    {
        InitializeComponent();
        var snapshot = card.Snapshot;
        AccountText.Text = $"{card.Email} · {card.PlanLabel} · {card.WorkspaceText}";
        UpdatedText.Text = card.UpdatedText;
        CountText.Text = snapshot.AvailableResetCredits is long count
            ? UiPresentation.Text("CreditsCount", count) : UiPresentation.Text("Unknown");

        var details = snapshot.ResetCredits;
        SummaryText.Text = snapshot.AvailableResetCredits switch
        {
            null => UiPresentation.Text("CreditsUnavailable"),
            0 => UiPresentation.Text("CreditsZero"),
            _ when details is null =>
                UiPresentation.Text("CreditsCountOnly"),
            _ when details.Count == 0 =>
                UiPresentation.Text("CreditsNoDetails"),
            long available when details.Count < available =>
                UiPresentation.Text("CreditsPartial", details.Count, available),
            long available when details.Count > available =>
                UiPresentation.Text("CreditsOvercount", details.Count, available),
            _ => UiPresentation.Text("CreditsAll", details.Count)
        };
        CreditList.ItemsSource = details?.Select(ToDisplay).ToArray() ?? [];
    }

    private static ResetCreditDisplay ToDisplay(ResetCredit credit) => new(
        string.IsNullOrWhiteSpace(credit.Title) ? UiPresentation.Text("NameUnavailable") : credit.Title,
        string.IsNullOrWhiteSpace(credit.Description) ? UiPresentation.Text("DescriptionUnavailable") : credit.Description,
        credit.Status switch
        {
            "available" => UiPresentation.Text("CreditAvailable"),
            "redeeming" => UiPresentation.Text("CreditRedeeming"),
            "redeemed" => UiPresentation.Text("CreditRedeemed"),
            _ => UiPresentation.Text("CreditUnknown")
        },
        credit.ExpiresAtUtc is DateTimeOffset expiry
            ? UiPresentation.Text("ExpiresAt",
                expiry.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", UiPresentation.Culture))
            : UiPresentation.Text("NeverExpires"));

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}

public sealed record ResetCreditDisplay(
    string Title, string Description, string StatusText, string ExpirationText);
