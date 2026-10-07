# Screenshot provenance and demo-image attribution

The WebP files are high-quality re-encodings of `RenderTargetBitmap` captures of **the actual running PerceptoX WinUI/XAML application** in English, Light and Dark. They contain genuine results from `DesktopPerceptoXWorkflow` and `DesktopGroupingWorkflow`, not synthetic-result adapters. They capture the application surface/dialogs, not the desktop's native window chrome. The local PNG captures remain unchanged; only the published previews are compressed for faster loading and reliable source distribution.

The opt-in driver exists only in Debug (`DocumentationCapture.cs`). Each run uses its own index/cache under `artifacts/`; it does not use personal image libraries or overwrite the user's index/preferences. `eng/capture-documentation.ps1` reproduces the captures; `capture-*-en.json` records engine counts and theme/language.

## Demonstration photograph

- Source: [NASA Science — Blue Marble](https://science.nasa.gov/resource/blue-marble/).
- Credit: NASA Goddard Space Flight Center; Reto Stöckli (land/water/clouds), Robert Simmon (ocean/compositing/visualization), supporting MODIS/USGS teams listed on the source page.
- Download: [NASA image](https://assets.science.nasa.gov/dynamicimage/assets/science/esd/climate/2023/12/ImageWall5_1920x1200-80.jpg?crop=faces%2Cfocalpoint&fit=clip&h=1200&w=1920).
- Terms: [NASA media guidelines](https://www.nasa.gov/nasa-brand-center/images-and-media/). Shown as an attributed factual software demonstration; no NASA endorsement is implied. NASA trademarks/logos are not included as product branding.
- Variants: a 480-pixel reference JPEG, a recompressed small JPEG and an exact file copy generated locally for the demo. These variants are not a natural-image benchmark or a claim of universal detection accuracy.

NASA imagery is not relicensed as GPL. Repository-owned source/assets and third-party materials retain their respective terms. No real user photographs, usernames or private library paths are used.
