using PerceptoX.Presentation.Localization;

namespace PerceptoX.Presentation.Services;

/// <summary>Fixed voluntary donation destination and localized, bounded title-bar campaign.</summary>
public static class DonationContent
{
    public static Uri PayPalUri { get; } = new("https://www.paypal.com/donate/?hosted_button_id=NFBKSEW3T4SFS");
    public static TimeSpan MessagePause { get; } = TimeSpan.FromSeconds(6);
    public const int MessageCount = 5;

    public static string Message(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, MessageCount);
        return UiText.T("Donation.Message" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public static int Next(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, MessageCount);
        return (index + 1) % MessageCount;
    }
}
