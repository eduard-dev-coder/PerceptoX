# Faza 2 — procesarea imaginii și miniatura

`IImageProcessor` primește un `ImageProcessingRequest` cu identitatea imaginii, versiunea sursei și rădăcina cache-ului. `ImageSharpImageProcessor` întoarce împreună fingerprint-urile și `ThumbnailRecord`; codul de indexare nu are nevoie de obiecte `ImageSharp.Image`.

Fluxul este `Identify` fără încărcarea metadatelor → admisie în limita de workeri/memorie → deschidere și `Identify` pe același stream → o singură decodare a primului frame → AutoOrient și fundal alb → pHash/dHash (sau algoritmii injectați) → JPEG max 256 px → `Dispose`. Pentru thumbnail păstrăm aspect ratio, nu facem upscale, folosim bicubic și JPEG quality 85. Encoderul omite metadata, iar profilurile EXIF/ICC/IPTC/XMP sunt eliminate înainte de salvare. [Resize ImageSharp](https://docs.sixlabors.com/articles/imagesharp/resize.html), [metadata](https://docs.sixlabors.com/articles/imagesharp/stripmetadata.html).

Admisia implicită este de maximum **2 workeri** și **512 MiB buget estimat**. Fiecare imagine rezervă `8 × width × height + 1 MiB` înainte de decodare; limita hard de dimensiune rămâne 100 megapixeli. Bugetul este conservator, dar nu este o măsurătoare exactă a memoriei native/interne ImageSharp. Imaginile care nu încap singure în buget sunt respinse clar; limita poate fi configurată. Verificarea dimensiunilor se repetă pe streamul deschis pentru a evita ca o schimbare a fișierului între preflight și decodare să depășească rezervarea.

Cache-ul folosește `ImageId` și SHA-256 peste `ProfileId + SourceVersion`. Fișierul este scris în același director pe un nume temporar unic, închis și publicat prin `File.Move` fără overwrite. La anulare/eroare înainte de publicare, temporarul este șters; o oprire brutală a procesului poate lăsa un temporar orfan, nu un JPEG publicat incomplet. Nu forțăm flush pe disc pentru acest cache regenerabil. Un cache existent pentru aceeași cheie este reutilizat. `SourceVersion` trebuie stabilită de indexerul din Faza 3; perechea mărime+mtime folosită de runnerul sintetic este doar un exemplu și nu detectează orice modificare posibilă.

Anularea este verificată între etape. Decoderul și encoderul sincrone nu pot fi întrerupte instantaneu; anularea este cooperativă. În Faza 3, enumerarea și canalele de rezultate vor fi de asemenea bounded: limita acestui serviciu singur nu limitează numărul cererilor pregătite de apelant.

## Verificare

```powershell
& .\eng\dotnet-sandbox.ps1 test PerceptoX.sln --no-restore
& .\eng\dotnet-sandbox.ps1 run --no-build --project tools\PerceptoX.Dataset\PerceptoX.Dataset.csproj -- --verify .bench-data\synthetic-10k --with-thumbnails
```

Setul sintetic verifică decodarea, hash-urile și publicarea miniaturilor la volum. Nu demonstrează precision/recall sau latența pe fotografii reale.

Verificare la 19 septembrie 2026: build cu zero warnings/errors, `dotnet format --verify-no-changes` curat, **49/49 teste** și **10.000/10.000 imagini** cu miniaturi, zero erori, checksum `477C158A2C9C4E7D`. Rularea repetată cu cache-ul deja populat a durat 8,41 s; această durată nu reprezintă costul indexării la rece.

Benchmark-urile folosesc [BenchmarkDotNet 0.15.8](https://www.nuget.org/packages/BenchmarkDotNet/0.15.8) cu `MemoryDiagnoser`:

```powershell
& .\eng\dotnet-sandbox.ps1 build benchmarks\PerceptoX.Benchmarks\PerceptoX.Benchmarks.csproj -c Release --no-restore
& .\eng\dotnet-sandbox.ps1 benchmarks\PerceptoX.Benchmarks\bin\Release\net10.0\PerceptoX.Benchmarks.dll --list flat
& .\eng\run-benchmark.ps1 -Filter '*FingerprintBenchmarks*'
& .\eng\run-benchmark.ps1 -Filter '*ImagingBenchmarks*'
```

Rezultatele sunt în `BenchmarkDotNet.Artifacts/`, ignorat de Git. Rulăm benchmark-urile separat de teste și nu impunem praguri de timp pe CI partajat. Scriptul dezactivează `NuGetAudit` numai în procesul de benchmark deoarece restore-ul proiectului temporar BenchmarkDotNet lovește eroarea TLS/SSPI a sandbox-ului; restore-ul normal al soluției rămâne separat și nemodificat. Sub sandbox, BenchmarkDotNet poate afișa `Unknown processor` deoarece citirea Win32_Processor prin WMI este refuzată; măsurătorile se execută totuși, dar profilul hardware trebuie completat într-un mediu autorizat înainte de decizii de performanță.

Baseline local orientativ: dHash ~57 ns/96 B, pHash ~11,84 µs/96 B; pentru imagini de 512 px, extracția ~9,75 ms și extracția cu miniatură ~17,75 ms; la 2048 px, ~54,25 ms și ~72,13 ms. Aceste rulări `ShortRun` au variație și mediu hardware incomplet identificat; nu le folosim pentru a afirma un câștig de viteză sau pentru dimensionarea finală.
