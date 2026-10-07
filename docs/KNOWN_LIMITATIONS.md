# Known limitations

- Scores are heuristic, not probabilities. The 97 grouping threshold is not a claimed 97% accuracy.
- Partial crops/screenshots, overlays and large edits are harder than resized copies. Multi-region matching and heatmap/alignment are experimental.
- Comparison previews are bounded to 1024 pixels, not every original pixel.
- HEIC/HEIF/AVIF require an activated compatible codec; every encoding/profile/HDR/orientation variant is not certified.
- Million-image scalability and scroll performance have not been universally measured. Resource budgets and paging do not replace benchmarking.
- Combined-binary redistribution still has provenance/compatibility/relink review gates; see [release readiness](RELEASE_READINESS.md).
- Validation installers are unsigned; clean-Windows and normal interactive shortcut verification remain open.
- Demo captures show one attributed image and a few variants, not recall/false-positive measurements.
