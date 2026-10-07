# User guide

## Identify images

Choose **Reference images** (thumbnails, resized copies, crops or screenshots) and **Original images** (the library to search). Use separate, non-nested folders. Index/cache paths must remain outside the library. Click **Scan**.

The dialog shows discovery, fingerprinting, persistence and search when information is available. Unknown totals use indeterminate progress; ETA is an estimate. Cancellation rolls back unfinished publication while retaining data already published correctly. Review the summary and dismiss with **OK**.

References appear alongside matching candidates. Inspect **Compare / Zoom**, filenames and paths before selecting images to copy into a separate destination. Copying retains source photos. CSV provides structured results; HTML provides a thumbnail-based review.

## Find an image

Select or drop a supported file and search the prepared library/index. Results depend on the processing profile and thresholds. Reindex changed or archived libraries before relying on old results.

## Similar images

Add one or more folders. Scan those roots, or explicitly include compatible indexed libraries. The default minimum score is 97, not a measured probability. Exact byte equality and visual similarity are distinct evidence.

Groups have a representative, with preference for higher resolution and then file-size ranking. File size is not a universal quality measure. The selected exemplar is the image to **keep/copy**. Choose another member, select best again or invert selection. Pagination preserves off-page selection. No preferred original folder is required.

Compare members before copying/exporting. Grouping is conservative; a chain of matches is not a guarantee of mutual equivalence.

## Visual comparison

Reference on the left, candidate on the right, with name/path/dimensions/file size. Hover over either preview for the synchronized x2/x4 loupe. Relative-position synchronization is not automatic crop alignment. Previews are bounded to 1024 pixels; inspect an original separately for pixel-level verification.

## Settings

- **General:** immediate English/Romanian and Light/Dark switching; sidebar controls mirror them.
- **Search:** matching thresholds and processing settings. Greater tolerance can increase false positives.
- **Calibration:** deliberate feedback/tuning; thresholds are not universally correct for all libraries.
- **Maintenance:** clear thumbnails, reset index/cache or clean selected report output. Reset requires reindexing and is distinct from deleting photos.
- **Modules:** inspect/install/repair/update compatible codec bundles after license consent.

Recoverable archive/restore is separate from search/copy, requires review and confirmation, and should be used with backups. There is no permanent-delete feature for original photos.

## Troubleshooting

For missing results, check roots/profile, rejected/unsupported/error counters, codec activation and thresholds. Structured logs are in `%LocalAppData%\PerceptoX\logs`. Remove private paths/identifiers before sharing a relevant excerpt. Do not clear an index during an active operation.
