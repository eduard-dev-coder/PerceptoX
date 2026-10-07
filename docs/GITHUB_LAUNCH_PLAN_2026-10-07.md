# PerceptoX — GitHub launch plan

Owner: `eduard-dev-coder` · Repository: `PerceptoX` · Target: public GitHub repository and GitHub Pages.

## Deliverables and sequence

1. Verify account access and repository creation capability. The connector authenticates successfully; it supports repository contents and commits, but does not expose repository creation, Releases or Pages administration. Browser automation is currently policy-blocked. The owner has been asked to create a public repository with an initial README. No credentials should be pasted into chat.
2. Prepare a professional English README, Romanian overview, installation/user/developer guides, contribution templates, changelog and clearly stated limitations. References: [Czkawka](https://github.com/qarmin/czkawka) (34,037 stars), [ShareX](https://github.com/ShareX/ShareX) (39,920 stars) and [PowerToys](https://github.com/microsoft/PowerToys), verified on 2026-10-07. Borrow information structure, not text, screenshots or branding.
3. Capture the actual English WinUI application in Light and Dark, its comparison dialog, similar-image groups and Settings. Use a separate demonstration dataset and the production matching/grouping workflows. Preserve original screenshots, capture provenance, input attribution and actual result summaries. No AI-generated UI or fabricated detection results.
4. Build a responsive static download site with local assets, product explanation, screenshots, installation instructions, source/license links and donation link. Downloads point to GitHub Releases; until real release assets exist the page must clearly say that downloads are not yet published.
5. Export a clean repository snapshot. Exclude local dependency caches, personal configuration, databases, thumbnails, logs, build outputs, private images, temporary reports and downloaded binary/source archives. Scan exported text for accidental credentials without echoing secret values. Include source, tests, benchmarks, build scripts, languages, graphics, public documentation and website.
6. Publish source in a single coherent commit to the newly created repository, then verify remote files. Add automated Windows test/build validation and a Pages workflow. CI results are not claimed before the runs complete.
7. Prepare the first release as a preview while clean-machine and redistribution checks remain open. Installer and portable ZIP belong in Release assets, not Git history. Include checksums, PerceptoX corresponding sources and the exact ImageMagick/delegate corresponding-source archive. Never label an unsigned installer as signed or a development-PC smoke test as a clean-machine certification.
8. Publish the release and configure Pages through an authenticated tool that actually exposes these operations. If unavailable, provide ready-to-run publication commands and distinguish prepared materials from live publication. Verify release assets and the HTTPS Pages URL before calling the launch complete.

## Release inputs

The validated local build is `artifacts/distribution/20261007-161321-0cdcdb56`: application version 1.0.0, Windows x64. The installer and ZIP were previously tested locally; this launch will re-check hashes and record their provenance. Newly edited documentation/capture tooling does not imply that an existing installer contains those later changes.

## License boundary

PerceptoX-owned code is GPL-3.0-only. Dependencies retain their own terms; free distribution is not blanket permission to relicense them. ImageSharp 3.1.12 grants Apache-2.0 for this open-source usage under its version-specific split license. Existing binary redistribution gates (Microsoft runtime terms, Win2D package-license provenance and native-delegate corresponding sources/replacement conditions) must be disclosed and reviewed; publishing our own source and documentation is distinct from clearing a combined binary release.

## Completion criteria

- Public repository contains complete, buildable source and tested publication material.
- Screenshots are genuine English application renders with traceable demo inputs.
- Releases contain verified installer, ZIP, sources, notices and checksums, or are explicitly held with an exact documented reason.
- Download page is live, responsive and links only to published artifacts.
- Publication report names remote URLs/commit and any uncompleted steps without overstating progress.
