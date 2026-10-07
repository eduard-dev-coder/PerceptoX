"use strict";
const shotDescriptions = {
  comparison: "PerceptoX comparison inspector with English labels and metadata",
  groups: "PerceptoX grouping near-identical photos using the real engine",
  settings: "PerceptoX Settings with immediate language and theme selection"
};
let previewShot = "comparison";
let previewDark = false;
function updatePreview() {
  const image = document.getElementById("detail-shot");
  image.src = `assets/${previewShot}-${previewDark ? "dark" : "light"}-en.webp`;
  image.alt = `${shotDescriptions[previewShot]}, ${previewDark ? "Dark" : "Light"} theme`;
  document.querySelectorAll("[data-shot]").forEach(button => {
    const active = button.dataset.shot === previewShot;
    button.classList.toggle("active", active);
    button.setAttribute("aria-pressed", String(active));
  });
  const themeButton = document.getElementById("preview-theme");
  themeButton.setAttribute("aria-pressed", String(previewDark));
  themeButton.textContent = previewDark ? "Preview Light theme" : "Preview Dark theme";
}
document.querySelectorAll("[data-shot]").forEach(button => button.addEventListener("click", () => {
  previewShot = button.dataset.shot;
  updatePreview();
}));
document.getElementById("preview-theme").addEventListener("click", () => { previewDark = !previewDark; updatePreview(); });

// One bounded public GitHub request; no tracking, cookies, keys or invented release links.
async function loadPublishedDownloads() {
  const repository = "eduard-dev-coder/PerceptoX";
  const timeout = new AbortController();
  const timer = setTimeout(() => timeout.abort(), 6000);
  try {
    const response = await fetch(`https://api.github.com/repos/${repository}/releases?per_page=10`, {
      signal: timeout.signal, headers: { Accept: "application/vnd.github+json" }
    });
    if (!response.ok) return;
    const releases = await response.json();
    if (!Array.isArray(releases)) return;
    const candidates = releases.filter(release => !release.draft && Array.isArray(release.assets));
    const release = candidates.find(item => item.assets.some(asset => /^PerceptoX-.*-Setup-win-x64\.exe$/.test(asset.name))
      && item.assets.some(asset => asset.name === "PerceptoX-win-x64.zip"));
    if (!release) return;
    const installer = release.assets.find(asset => /^PerceptoX-.*-Setup-win-x64\.exe$/.test(asset.name));
    const zip = release.assets.find(asset => asset.name === "PerceptoX-win-x64.zip");
    const allowedPrefix = `https://github.com/${repository}/releases/download/`;
    if (![installer, zip].every(asset => typeof asset.browser_download_url === "string"
        && asset.browser_download_url.startsWith(allowedPrefix))) return;
    document.getElementById("installer-link").href = installer.browser_download_url;
    document.getElementById("installer-link").textContent = "Download installer ↓";
    document.getElementById("zip-link").href = zip.browser_download_url;
    document.getElementById("zip-link").textContent = "Download portable ZIP ↓";
    const signingNotice = /\*\*This release is unsigned\.\*\*/.test(release.body || "") ? " · Unsigned — Windows may show a publisher warning" : "";
    document.getElementById("release-status").textContent = `${release.tag_name} · ${release.prerelease ? "Preview release — review release notes before using" : "Published release"}${signingNotice}`;
  } catch {
    // Leave working Releases links and the honest unpublished/unknown state intact.
  } finally { clearTimeout(timer); }
}
loadPublishedDownloads();
