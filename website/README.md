# PerceptoX download site

Static HTML/CSS/JavaScript; no npm packages, remote fonts, analytics or cookies. Images are local genuine WinUI captures. One bounded request to the public GitHub Releases API discovers existing matching installer/ZIP assets; if none exist or the request fails, normal Releases links remain available and no fictitious download is advertised.

GitHub Pages target: `https://eduard-dev-coder.github.io/PerceptoX/`. This is a deployment target, not a claim the page is already live. Enable **Settings → Pages → Source: GitHub Actions**, then run the `Pages` workflow. The workflow copies local graphics into the website artifact. Browser JavaScript is progressive enhancement; core content/navigation work without it.

To preview, serve this directory with a local static HTTP server. Copy `graphics/icon.png` to `website/assets/icon.png` or use the publication-preparation script. Inspect desktop/mobile widths and both screenshot-theme states. See [image credits](assets/SCREENSHOTS.md).
