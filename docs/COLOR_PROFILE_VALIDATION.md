# ICC, CMYK/YCCK și codecuri locale — verificare 2026-10-04

## Fixture-uri ICC cu proveniență

Au fost adăugate două profiluri ICC reale, nemodificate, din **Compact-ICC-Profiles**,
autor PhotoSauce / saucecontrol. Colecția declară explicit profilurile **CC0 1.0**.
Commit fixat: `bdd84663061bc4ae95ca70decff54f581e27f702`.
Surse: [README la commit](https://github.com/saucecontrol/Compact-ICC-Profiles/blob/bdd84663061bc4ae95ca70decff54f581e27f702/readme.md),
[licență CC0 la commit](https://github.com/saucecontrol/Compact-ICC-Profiles/blob/bdd84663061bc4ae95ca70decff54f581e27f702/license).

| Fixture în `tests/PerceptoX.Infrastructure.Tests/Fixtures/ColorProfiles/` | Bytes | SHA256 |
| --- | ---: | --- |
| `sRGB-v2-micro.icc` | 456 | `0A8A33AEA66A6F154A5642EBE168EF287E73265D9F7B51C42A45E6EEDBACDA7A` |
| `CGATS001Compat-v2-micro.icc` | 8464 | `73E1BA37D2BAD5BAB2A964F40A9EED96209666EFC067C3322626214BBEF234A0` |

Profilul CMYK este unul de afișare, cu mapping perceptual A2B0; nu este profil de
producție pentru separație RGB→CMYK și nu trebuie prezentat drept înlocuitor exact
al unui profil comercial SWOP. [Descrierea autorului](https://github.com/saucecontrol/Compact-ICC-Profiles/blob/bdd84663061bc4ae95ca70decff54f581e27f702/readme.md#cmyk).

Fișierele provin din `profiles/` la commitul fixat. Testele verifică SHA256 înainte de
folosire și le citesc din checkout (`bin/{Configuration}/net10.0/../../../Fixtures/`),
fără modificarea proiectului/pachetelor sau descărcări în timpul testării.
Pixelii sunt un pattern RGB generat local; JPEG-urile RGB, CMYK și YCCK sunt generate
cu encoderul instalat ImageSharp 3.1.12. Nu sunt redistribuite fotografii externe.

ImageSharp are fixture-uri CMYK și teste ICC, dar tabelul ICC inspectat conține în
principal date sintetice/random; în acest increment am ales profilurile CC0 cu
proveniență explicită, în locul unui profil gol sau al unei imagini externe cu drepturi
insuficient documentate. [ICC test data, ImageSharp 3.1.12](https://github.com/SixLabors/ImageSharp/blob/v3.1.12/tests/ImageSharp.Tests/TestDataIcc/IccTestDataProfiles.cs).

## Ce demonstrează testele

`ColorProfileFixtureTests` verifică trei encodări JPEG și capabilitățile ImageSharp:

- Profilul citit este valid (`CheckIsValid`), are tag-uri și color space RGB/CMYK potrivit.
- JPEG-ul rezultat declară encoding-ul cerut, păstrează un ICC valid și bytes ai profilului.
- Procesorul existent produce toate cele trei hash-uri și un thumbnail 256×128 decodabil.
- Thumbnail-ul nu păstrează ICC/EXIF/XMP/IPTC; sursa rămâne byte-identică.
- Configurația ImageSharp instalată nu înregistrează extensiile HEIC/HEIF/AVIF.

Aceste verificări dovedesc robustețea decodării/metadatelor și fluxul hash→thumbnail.
**Nu dovedesc fidelitate colorimetrică, aplicarea corectă a transformării ICC, ΔE,
echivalența culorilor CMYK cu originalul sau calitatea tuturor profilurilor/camerelor.**
Pattern-ul CMYK/YCCK generat prin encoder este un test de compatibilitate cu codec-ul,
nu o fotografie reală de tipar și nu un oracle pentru managementul culorii.

Comandă:

```powershell
dotnet test tests\PerceptoX.Infrastructure.Tests\PerceptoX.Infrastructure.Tests.csproj --no-restore --filter FullyQualifiedName~ColorProfileFixtureTests --verbosity minimal
```

Rezultat verificat: **4/4 teste trecute**, 0 eșuate, 0 skipped, durată test runner
247 ms; build fără erori/avertismente. Rularea folosește numai filtrul acestui fișier.

## HEIC/AVIF: capabilitate locală versus aplicație

Observații pe hostul curent, fără instalare de OS sau pachete:

| Componentă | AVIF | HEIC/HEIF |
| --- | --- | --- |
| ImageSharp 3.1.12, configurație existentă PerceptoX | decoder neînregistrat | decoder neînregistrat |
| ImageMagick instalat 7.1.2-26 Q16-HDRI x64, libheif 1.23.0 | `rw+`, encode și decode reușite în probă | `r--`, decode reușit în probă; fără encoder anunțat |
| PerceptoX livrat prin acest increment | nu se adaugă suport | nu se adaugă suport |

`magick -list format` nu a fost tratat ca dovadă suficientă. Probele executate:

1. Pattern 64×32 generat prin `magick -size 64x32 gradient:red-blue generated.avif`;
   output 308 bytes, identify 64×32/12-bit; decodare în PNG reușită.
2. Fișier extern `libheif/examples/example.heic`, commit
   `7dde89307b55616a71dda198f07c2629dfd51130`, 718114 bytes,
   SHA256 `7F8B363E4936C0666A25F64F3A92FDA10BD8E5453BE4592530B65A55DD98F3F2`.
   Identify: două imagini 1280×854/8-bit; decodare și resize în două PNG-uri reușite.
   [Proveniență fișier](https://github.com/strukturag/libheif/blob/7dde89307b55616a71dda198f07c2629dfd51130/examples/example.heic).
   Fișierul a fost folosit temporar pentru diagnostic, nu inclus în repository sau produs.

Rezultatul confirmă decodarea locală a acestui exemplu HEIC. O mențiune „HEIC no”
dintr-un inventar anterior nu poate fi reutilizată drept dovadă că acest decoder local
nu funcționează; encoding-ul și distribuția sunt întrebări distincte.

O cale mică de prototip, neimplementată aici: adapter către `magick` instalat,
inputul cu selecție explicită a imaginii `[0]` → imagine RGB/PNG intermediară → pipeline
actual. Validarea separată trebuie să acopere orientarea/metadata, ICC, alpha, HDR/bit depth,
limitele de memorie/timp, anularea, procesul extern și fișiere corupte. Folosirea unei
instalări locale nu constituie validarea distribuirii dependințelor native cu aplicația.
Licențele/dependințele codec-urilor trebuie inspectate înainte de alegerea adapterului;
nu a fost modificat catalogul de formate sau instalat vreun codec în acest increment.

Sursă pentru distribuția componentelor: [ImageMagick license](https://imagemagick.org/license/),
[libheif COPYING](https://github.com/strukturag/libheif/blob/7dde89307b55616a71dda198f07c2629dfd51130/COPYING).
