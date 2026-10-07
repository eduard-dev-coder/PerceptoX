namespace PerceptoX.Application.Calibration;

/// <summary>Immutable selection state. Persistence and explicit user commands belong to the caller.</summary>
public sealed record ProfileSelection(ThresholdProfile Active, ThresholdProfile? Previous = null)
{
    public ProfileSelection Apply(CalibrationProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        Active.Validate();
        if (proposal.Status != CalibrationStatus.Validated || proposal.ProposedProfile is null)
            throw new InvalidOperationException("Only a supported, validated proposal can be applied.");
        if (proposal.BaselineProfile != Active)
            throw new InvalidOperationException("Proposal is stale: its baseline is no longer active.");
        ThresholdProfile profile = proposal.ProposedProfile;
        profile.Validate();
        if (profile.ProcessingProfileId != Active.ProcessingProfileId || profile.Id == Active.Id ||
            profile.Version != checked(Active.Version + 1))
            throw new InvalidOperationException("Proposal identity/version is incompatible with the active profile.");
        return new(profile, Active);
    }

    public ProfileSelection Rollback()
    {
        if (Previous is null) throw new InvalidOperationException("No previous profile is available.");
        Previous.Validate();
        Active.Validate();
        if (Previous.ProcessingProfileId != Active.ProcessingProfileId)
            throw new InvalidOperationException("Rollback profile uses another processing profile.");
        return new(Previous, Active);
    }

    public ProfileSelection Reset(ThresholdProfile preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        preset.Validate();
        Active.Validate();
        if (preset.ProcessingProfileId != Active.ProcessingProfileId)
            throw new InvalidOperationException("Preset uses another processing profile.");
        return preset == Active ? this : new(preset, Active);
    }
}
