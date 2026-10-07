# Faza 4 — matching, grupuri candidate și export

Faza 4 implementează căutarea „similar cu imaginea selectată” peste snapshot-ul compact al Fazei 3. Motorul folosește `IHammingSearchIndex`; implementarea activă este `LinearHammingSearchIndex`, care parcurge structura contiguă și calculează distanța pHash prin `BitOperations.PopCount`. Numai candidații aflați în raza pHash trec la validarea dHash, penalizările metadata și Top-N. `PriorityQueue` păstrează numai cele mai bune N rezultate, iar ordinea finală este scor descrescător și `ImageId` crescător pentru rezultate deterministe.

Scorul curent combină 60% pHash și 40% dHash, apoi aplică penalizări mici pentru diferențe de aspect ratio, rezoluție și mărime. Metadatele sunt **soft**, nu elimină un candidat identic ca hash. Formula este un baseline necalibrat; nu este probabilitate, nu dovedește că două fișiere sunt duplicate și nu autorizează ștergere/mutare automată.

Profilul conservator inițial folosește `pHash <= 2`, `dHash <= 2`, ales numai după măsurarea setului procedural PerceptoX. Pragurile rămân configurabile. `SimilaritySearchOptions.Broad` există pentru experimente controlate, nu ca implicit. Rezultatele acestui set nu se generalizează la fotografii reale.

## Rezultate măsurate

Evaluarea are 1.000 de query-uri originale și nouă variante etichetate per query. La Top-10:

| Prag pHash/dHash | Precision | Recall global |
|---|---:|---:|
| 16/20 | 49,44% | 54,93% |
| 8/8 | 49,52% | 54,92% |
| 4/4 | 55,60% | 54,86% |
| 2/2 | 73,41% | 53,59% |
| 0/0 | 95,89% | 27,98% |

La pragul 2/2: compressed-40 92,30%, compressed-75 98,00%, resized-large 98,10%, resized-small 98,30% și thumbnail 95,50% recall. Crop-center, crop-corner și screenshot-frame sunt 0%; overlay este 0,10%. Aceste rezultate confirmă limita cunoscută a pHash/dHash și justifică Faza 5, nu ajustarea artificială a gate-ului.

BenchmarkDotNet pentru Top-50, rază 8/8 și hash-uri pseudo-aleatoare:

| Intrări | Medie | Alocări/query |
|---:|---:|---:|
| 100.000 | 93,45 µs | 584 B |
| 500.000 | 480,64 µs | 584 B |
| 1.000.000 | 1,502 ms | 584 B |

Profilul hardware este raportat `Unknown processor` în sandbox deoarece WMI este refuzat, iar run-ul are numai cinci iterații. Timpul include căutarea în snapshot, nu încărcarea snapshot-ului din SQLite. Distribuția pseudo-aleatoare are puțini candidați în rază; cifrele nu reprezintă garantat o bibliotecă foto reală. Baseline-ul liniar este deja suficient de rapid pentru ținta de un milion în această probă, astfel că BK-tree nu este activat: ar adăuga memorie și complexitate fără un câștig demonstrat.

## Batch și grupuri

`FindForSelectedImages` oferă evaluare batch lazy și anulabilă. Nu materializează toate rezultatele tuturor query-urilor. CLI expune deocamdată query-ul individual; API-ul batch este pregătit pentru joburile și UI-ul din fazele următoare.

`FingerprintCandidateGroupService` grupează determinist numai imaginile cu aceeași pereche pHash+dHash. Limitează atât numărul grupurilor, cât și preview-ul membrilor. Grupurile sunt etichetate explicit **candidate**, nu duplicate exacte: o coliziune perceptuală nu dovedește identitate de bytes, iar similitudinea nu este tratată ca tranzitivă. Persistența grupurilor near-duplicate rămâne oprită până există reguli calibrate; în acest fel nu salvăm automat false positives ca adevăr de produs.

## Export sigur

CSV-ul citează fiecare celulă conform regulilor RFC 4180 și neutralizează celulele al căror prim caracter semnificativ este `=`, `+`, `-` sau `@` printr-un tab în interiorul valorii citate, pentru a reduce riscul de formula injection descris de [OWASP](https://community.owasp.org/attacks/CSV_Injection). HTML-ul este static, fără JavaScript; toate căile trec prin [`HtmlEncoder`](https://learn.microsoft.com/en-us/dotnet/api/system.text.encodings.web.htmlencoder?view=net-10.0), iar miniaturile sunt copiate numai din cache-ul validat în directorul `assets`.

Exporturile nu suprascriu. CSV-ul este publicat prin fișier temporar + move; raportul HTML este construit într-un director temporar și apoi mutat la destinația nouă. Anularea curăță staging-ul. Raportul include avertismentul că scorurile sunt necalibrate.

## Comenzi

```powershell
& .\eng\dotnet-sandbox.ps1 run --no-build --project src\PerceptoX.Cli\PerceptoX.Cli.csproj -- find-similar C:\Imagini D:\PerceptoX\index.db C:\Imagini\selectata.jpg 50
& .\eng\dotnet-sandbox.ps1 run --no-build --project src\PerceptoX.Cli\PerceptoX.Cli.csproj -- groups C:\Imagini D:\PerceptoX\index.db 100
& .\eng\dotnet-sandbox.ps1 run --no-build --project src\PerceptoX.Cli\PerceptoX.Cli.csproj -- export C:\Imagini D:\PerceptoX\index.db D:\PerceptoX\thumbs C:\Imagini\selectata.jpg D:\Rapoarte\rezultat.csv D:\Rapoarte\rezultat-html 50
& .\eng\dotnet-sandbox.ps1 run --no-build --project src\PerceptoX.Cli\PerceptoX.Cli.csproj -- evaluate-synthetic C:\Dataset\images D:\PerceptoX\index.db C:\Dataset\manifest.csv 10 2 2
& .\eng\run-benchmark.ps1 -Filter '*SearchBenchmarks*'
```

## Starea gate-ului

Implementarea tehnică este funcțională și verificată. Limita crop/screenshot a baseline-ului a fost adresată experimental în [Faza 5](PHASE5_CROP_RESISTANT.md), fără a rescrie acest baseline. Validarea pe un corpus foto real și persistarea grupurilor near-duplicate ca rezultat de produs rămân deschise.
