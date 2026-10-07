namespace PerceptoX.Application.Matching;

public sealed record FingerprintCandidateGroup(
    long RepresentativeImageId,
    int TotalMembers,
    IReadOnlyList<long> PreviewImageIds)
{
    public bool IsVerifiedExactDuplicate { get; }
}
