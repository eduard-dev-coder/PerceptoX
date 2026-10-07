# Decodare opțională HEIC/HEIF/AVIF — verificare 2026-10-04

`OptionalMagickDecoder` permite folosirea unui ImageMagick 7 instalat separat,
selectat prin cale absolută. Adaptorul este opțional: PerceptoX nu distribuie
ImageMagick, nu instalează codecuri și nu adaugă un decoder HEIC/AVIF în
configurația nativă ImageSharp. Testul existent
`InstalledImageSharpConfigurationHasNoHeicOrAvifDecoder` confirmă această limită.

## Activare și traseul imaginii

```csharp
OptionalMagickDecoder decoder = await OptionalMagickDecoder.CreateAsync(
    @"C:\Program Files\ImageMagick-7.1.2-Q16-HDRI\magick.exe", cancellationToken);
using ImageSharpImageProcessor processor = new(modernDecoder: decoder);
```

Adaptorul acceptă numai extensiile HEIC, HEIF și AVIF pentru care runtime-ul
selectat declară citire. `CreateAsync` verifică versiunea și lista formatelor;
căile relative, inclusiv simplul `magick.exe`, sunt respinse. Nu există căutare
automată în `PATH`. Injectarea decoderului în `DesktopPerceptoXWorkflow` permite
și descoperirea formatelor suplimentare în indexare și în căutarea batch.

Pentru o imagine eligibilă, procesul extern identifică dimensiunile, decodează
primul cadru, aplică `-auto-orient -colorspace sRGB -depth 8 -strip`, apoi produce
un PNG temporar. Pipeline-ul ImageSharp folosește acel PNG pentru pHash64,
dHash64, MultiRegionHash576 și miniatura JPEG. Rezultatul temporar este
`IDisposable`; procesarea și regenerarea miniaturilor îl eliberează la final.
Originalul este deschis pentru citire și nu este rescris.

Activarea runtime-ului schimbă `ProcessingProfileId`. `RuntimeProfileId` este
un SHA-256 care include calea executabilului, versiunea, formatele relevante,
digestul binarului și operațiile/limitele de conversie. Astfel, configurația
opțională nu reutilizează profilul de procesare al configurației native.

## Runtime verificat efectiv

| Element | Rezultat local |
| --- | --- |
| Executabil selectat explicit | `C:\Program Files\ImageMagick-7.1.2-Q16-HDRI\magick.exe` |
| Versiune raportată | ImageMagick **7.1.2-26**, Q16-HDRI x64 |
| AVIF | `rw+`, delegate HEIC/libheif **1.23.0** |
| HEIC | `r--`, delegate HEIC/libheif **1.23.0** |
| HEIF | `r--`, delegate HEIC/libheif **1.23.0** |
| Probă efectivă | AVIF sintetic → adaptor → PNG → amprente și miniatură |

Lista formatelor confirmă disponibilitatea declarată; proba AVIF de mai sus
confirmă și executarea conversiei. Acest increment nu include o probă efectivă
HEIC sau HEIF: runtime-ul instalat nu oferă scriere pentru aceste două formate,
iar testele nu descarcă fotografii sau alte fixture-uri străine.

## Protecții implementate

Fiecare operație externă are termen de 60 de secunde, limite ImageMagick de
256 MiB pentru memory, 512 MiB pentru map, zero disk și două thread-uri.
Dimensiunile sunt validate înainte și după conversie, cu plafon de 100 MP.
Semnătura PNG și dimensiunile efective identificate prin ImageSharp trebuie să
corespundă rezultatului. Acestea sunt limite configurate și verificări în cod;
nu reprezintă o măsurătoare a consumului maxim RSS al întregului proces extern.

Pornirea folosește argumente separate, fără shell, și directoare temporare
unice `PerceptoX-OptionalCodec-<guid>`. Adaptorul respinge căile prin reparse
points, păstrează fișierele deschise pentru citire și verifică identitatea
originalului/executabilului pe durata operației. Cleanup-ul acceptă numai
directorul temporar propriu; subdirectoarele neașteptate sunt respinse.
La anulare în timpul procesului, implementarea încearcă să oprească arborele
procesului și să aștepte închiderea lui înainte de cleanup.

Decoderul extern este serializat separat în `ImageSharpImageProcessor`; PNG-ul
rezultat intră apoi în controlul existent al concurenței și al bugetului de
memorie ImageSharp. Regenerarea miniaturii folosește aceeași versiune și aceeași
cale de cache ca procesarea inițială.
Versiunea returnată în `ThumbnailRecord` rămâne versiunea originalului, cerută
de contractul de persistare SQLite; cheia internă de cache include identitatea
runtime-ului opțional.

## Teste și rezultate

Fișier: `tests/PerceptoX.Infrastructure.Tests/OptionalMagickDecoderTests.cs`.
Fixture-ul este generat integral local: un pattern RGB de 48×96 pixeli este
scris în PNG, apoi convertit în AVIF de executabilul explicit selectat.
Numele AVIF include spații și paranteze pătrate. Directoarele de test au GUID
unic; cleanup-ul verifică directorul părinte și GUID-ul înainte de ștergere și
șterge numai acel director.

| Verificare | Rezultat |
| --- | --- |
| Trei forme de cale relativă către runtime | Respinse fără căutare PATH |
| Executabil absolut inexistent | Respingere explicită |
| Formate eligibile, case-insensitive, și profil stabil | Confirmate |
| AVIF sintetic, PNG valid, dimensiuni și pixeli | Confirmate |
| Original: aceiași octeți și aceeași dată de modificare | Confirmat |
| Eliminarea PNG/directorului temporar, `Dispose` repetat | Confirmată |
| Token anulat înainte de decodare | Anulare și niciun director temporar nou |
| AVIF corupt | Respingere, original identic și niciun PNG publicat |
| Extensie sursă neeligibilă | Respingere înainte de output |
| Pipeline opțional, trei amprente, regenerare miniatură | Confirmate; cache/version și octeți JPEG identici |
| Workflow desktop → descoperire AVIF → index SQLite | Confirmat: calea originalului, 48×96, trei amprente persistate și a doua scanare `SkippedUnchanged=1` |

Comenzile executate fără restore sau instalări:

```powershell
& .\eng\dotnet-sandbox.ps1 test tests\PerceptoX.Infrastructure.Tests\PerceptoX.Infrastructure.Tests.csproj --no-restore --filter FullyQualifiedName~OptionalMagickDecoderTests --verbosity minimal

& .\eng\dotnet-sandbox.ps1 test tests\PerceptoX.Infrastructure.Tests\PerceptoX.Infrastructure.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~OptionalMagickDecoderTests|FullyQualifiedName~ColorProfileFixtureTests' --verbosity minimal
```

Rezultate reale la 2026-10-04:

- **11** teste opționale trecute în setul final, inclusiv indexarea desktop/SQLite reală.
- **15/15** teste opționale + ICC trecute, zero eșecuri, **zero skipped**, aproximativ 2 s.
- Compilarea proiectului de teste a reușit cu analizorii și warnings-as-errors active.

Pe un calculator fără acest executabil explicit, testele care cer runtime-ul
sunt marcate **skipped** prin `InstalledMagickFactAttribute`; nu returnează
silențios succes. Testele pentru respingerea căilor relative și pentru
executabilul inexistent continuă să ruleze. Un executabil prezent, dar cu
versiune/codecuri incompatibile, produce eșecuri de test vizibile.

## Limitele acestei validări

Pattern-ul AVIF verificat este portret și are orientarea TopLeft. Nu validează
exhaustiv combinațiile EXIF/irot/imir; proba manuală de encoding cu Orientation
RightTop a fost normalizată de runtime la TopLeft. Parametrul `-auto-orient`
este implementat, dar nu este revendicat aici un test AVIF de rotație metadata.
Documentația oficială descrie opțiunile de orientare HEIC în
[ImageMagick Defines](https://imagemagick.org/defines/).

Anularea testată este cea cerută înainte de decodare. Termenul de 60 s,
oprirea unui proces aflat în execuție, limitele de memorie, respingerea
reparse points și înlocuirea executabilului nu au fost provocate prin fault
injection în aceste teste. Un AVIF corupt este verificat; un runtime care
raportează succes dar emite deliberat un PNG corupt nu a fost simulat.

Testele ICC existente verifică citirea unor profile valide RGB/CMYK/YCCK,
amprentele, miniatura și eliminarea metadatelor; ele nu certifică o conversie
colorimetrică cu toleranțe măsurate. Nici proba AVIF nu certifică HDR, gamut
larg, profiluri ICC arbitrare sau echivalență vizuală cu toate aplicațiile de
referință. Conversia canonică pe 8 biți poate pierde informație HDR.

Pentru distribuirea ulterioară a unui runtime împreună cu aplicația trebuie
analizate separat licențele runtime-ului și delegaților. Implementarea actuală
folosește exclusiv instalarea externă selectată. Surse oficiale:
[ImageMagick Formats](https://imagemagick.org/formats/) și
[ImageMagick License](https://imagemagick.org/license/).
