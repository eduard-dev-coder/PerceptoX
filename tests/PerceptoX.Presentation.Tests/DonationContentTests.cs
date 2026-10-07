using PerceptoX.Presentation.Localization;
using PerceptoX.Presentation.Services;

namespace PerceptoX.Presentation.Tests;

[Collection("Live language")]
public sealed class DonationContentTests
{
    [Fact]
    public void DestinationAndPauseMatchUserRequest()
    {
        Assert.Equal("https", DonationContent.PayPalUri.Scheme);
        Assert.Equal("www.paypal.com", DonationContent.PayPalUri.Host);
        Assert.Equal("/donate/", DonationContent.PayPalUri.AbsolutePath);
        Assert.Equal("?hosted_button_id=NFBKSEW3T4SFS", DonationContent.PayPalUri.Query);
        Assert.Equal(TimeSpan.FromSeconds(6), DonationContent.MessagePause);
        Assert.Equal(5, DonationContent.MessageCount);
    }

    [Theory]
    [InlineData("ro")]
    [InlineData("en")]
    public void FiveDistinctMessagesCycleAndSwitchLanguageLive(string language)
    {
        try
        {
            UiText.Initialize(UiText.LoadBuiltIn(language));
            var messages = Enumerable.Range(0, 5).Select(DonationContent.Message).ToArray();
            Assert.Equal(5, messages.Distinct(StringComparer.Ordinal).Count());
            Assert.All(messages, text => { Assert.NotEmpty(text); Assert.DoesNotContain("Donation.", text); Assert.InRange(text.Length, 10, 85); });
            int index = 0;
            for (int count = 0; count < 5; count++) index = DonationContent.Next(index);
            Assert.Equal(0, index);
            Assert.Equal(UiText.T("Donation.Message1"), DonationContent.Message(index));
            Assert.Contains(language == "ro" ? "opțională" : "optional", UiText.T("Donation.Optional"));
        }
        finally { UiText.Initialize(UiText.LoadBuiltIn("ro")); }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void InvalidMessageIndicesAreRejected(int index)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DonationContent.Message(index));
        Assert.Throws<ArgumentOutOfRangeException>(() => DonationContent.Next(index));
    }
}
