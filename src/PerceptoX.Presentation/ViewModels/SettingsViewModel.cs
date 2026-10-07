using CommunityToolkit.Mvvm.ComponentModel;
using PerceptoX.Presentation.Localization;

namespace PerceptoX.Presentation.ViewModels;

public sealed partial class SettingsViewModel : LocalizedViewModel
{
    public MaintenanceViewModel? Maintenance { get; internal set; }
    public CalibrationViewModel? Calibration { get; internal set; }
    [ObservableProperty] public partial string CodecStatus { get; set; } = "";
    public void SetCodecStatus(Func<string> render) => SetLocalized(nameof(CodecStatus), value => CodecStatus = value, render);
    [ObservableProperty]
    public partial int TopN { get; set; } = 50;

    [ObservableProperty]
    public partial int MaxPerceptualDistance { get; set; } = 2;

    [ObservableProperty]
    public partial int MaxDifferenceDistance { get; set; } = 2;

    [ObservableProperty]
    public partial int MaxMultiRegionDistance { get; set; } = 1;

    [ObservableProperty]
    public partial int MinimumMatchedRegions { get; set; } = 1;

    public void Normalize()
    {
        TopN = Math.Clamp(TopN, 1, 1000);
        MaxPerceptualDistance = Math.Clamp(MaxPerceptualDistance, 0, 64);
        MaxDifferenceDistance = Math.Clamp(MaxDifferenceDistance, 0, 64);
        MaxMultiRegionDistance = Math.Clamp(MaxMultiRegionDistance, 0, 64);
        MinimumMatchedRegions = Math.Clamp(MinimumMatchedRegions, 1, 9);
    }
}
