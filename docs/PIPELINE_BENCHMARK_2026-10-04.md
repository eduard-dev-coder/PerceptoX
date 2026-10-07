# PerceptoX — profil Channels vs TPL Dataflow, 2026-10-04

Rezultatul acestui profil scurt nu justifică migrarea pipeline-ului de producție la Dataflow. Cu doi workers, mediile sunt practic egale; cu patru workers, Channels a fost mai rapid în această rulare. Rezultatul este local, pe un corpus sintetic de 192 imagini, și **nu validează scara de 1.000.000 imagini**.

## Implementare și reproducere

Fișier nou: `benchmarks/PerceptoX.Benchmarks/PipelineBenchmarks.cs`. Nu au fost modificate modulele din `src/`, proiectul benchmark existent sau `Directory.Packages.props`. `System.Threading.Tasks.Dataflow.dll` este disponibil în referințele framework-ului net10.0 (`Microsoft.NETCore.App.Ref/10.0.9`); nu a fost necesar un pachet Dataflow.

```powershell
./eng/dotnet-sandbox.ps1 -DotNetArguments @(
    'build',
    'benchmarks/PerceptoX.Benchmarks/PerceptoX.Benchmarks.csproj',
    '-c', 'Release', '--no-restore'
)
./eng/run-benchmark.ps1 -Filter '*PipelineBenchmarks*'
```

Buildul complet și apoi buildul benchmarkului cu `-p:BuildProjectReferences=false` au trecut cu zero avertismente și zero erori. Acest ultim flag a permis izolarea rebuildului benchmarkului în timp ce alte modificări erau în lucru; pentru reproducere după modificări ale procesorului, se folosește buildul complet de mai sus. Pachetele erau deja restaurate local. Nu s-a rulat restore extern.

BenchmarkDotNet folosește `InProcessNoEmitToolchain`, astfel încât nu generează și nu restaurează un proiect copil. Configurația `PipelineShort` pornește de la `Job.Dry`, cu trei iterații măsurate, o singură invocare pe iterație și strategie `ColdStart`. Deși jobul afișează `WarmupCount=1`, această strategie nu a emis o secțiune `WorkloadWarmup`; setup-ul execută explicit câte un preflight Channels și Dataflow, în afara timpilor măsurați. Aceasta este o măsurare cu procesul și cache-ul de fișiere deja încălzite, nu o probă de disk cold start.

## Sarcina măsurată

- 192 fișiere diferite generate determinist: 96 PNG și 96 JPEG; câte 64 imagini pătrate de 256, 1024 și 2048 pixeli. Conținutul este un model de culori determinist, nu fotografii reale. Volum total surse: **44.782.560 bytes**.
- Discovery prin `Directory.EnumerateFiles`, fără materializarea întregii colecții, cu citirea mărimii și timestampului fiecărui fișier. IDs provin din numele fixture-urilor. Corpusul depășește capacitatea cozii, dar adâncimea efectivă a cozilor nu a fost instrumentată.
- Channels: coadă input bounded 128, doi/patru consumatori de procesare, coadă output bounded 128 și un singur consumator de persistență.
- Dataflow: `BufferBlock` bounded 128 → `TransformBlock` paralel, bounded la numărul de workers, `EnsureOrdered=false` → `ActionBlock` bounded 128, cu un singur consumator de persistență. Limita transformului evită încă o coadă de 128 intrări. Definițiile de capacitate Dataflow includ intrările aflate în procesare/ieșirile reținute, deci cele două mecanisme nu au un buget de buffering identic la ultimul obiect.
- Același `ImageSharpImageProcessor`, aceeași limită de workers și același buget implicit de memorie decodată de 512 MiB. Se execută identificarea, decodarea, orientarea, toate cele trei amprente implicite și generarea miniaturilor pe disc. Procesarea este cea completă implicită, fără reducerea opt-in a dimensiunii de decodare.
- Același sink SQLite temporar pentru ambele variante: WAL, synchronous NORMAL, foreign keys ON, un singur writer și tranzacții batch 64. Se persistă dimensiunile imaginii, source version, calea miniaturii și descriptorii/bytes ai celor trei amprente.
- Fiecare invocare creează o bază SQLite și un cache de miniaturi noi. Timpul include pipeline-ul, crearea bazei și schema simplificată, scrierea miniaturilor, persistența și verificarea rezultatelor. Generarea corpusului este exclusă.

Acest sink comparativ nu este `SqliteIndexStore` din producție: nu include `PrepareBatch`, detecția imaginilor neschimbate, staging/publish atomic, istoric, scan errors, actualizarea identităților sau lockul workspace-ului. Prin urmare, timpii nu reprezintă întreaga operație de indexare a aplicației. În producție, constantele inspectate sunt Channels 128, batch 64, workers impliciți 2 (`ImageIndexer`).

## Mediu și rezultate observate

Windows 11 `10.0.26220.9587`; SDK **10.0.301**; runtime **.NET 10.0.9**, X64 RyuJIT x86-64-v3, Concurrent Workstation GC; BenchmarkDotNet **0.15.8**; ImageSharp **3.1.12**; Microsoft.Data.Sqlite **10.0.12**. CPU citit separat din registry: **AMD Ryzen 9 5900X 12-Core Processor**; `Environment.ProcessorCount` a raportat 24. BDN a afișat Unknown processor deoarece interogarea WMI a primit Access denied. Schimbarea power planului nu a fost permisă; profilul a rulat cu planul existent. Peak RAM nu a fost măsurat.

| Variantă | Workers | Timp mediu / 192 imagini | Deviație standard | Imagini/s, calculat din medie | Memorie managed alocată / invocare, BDN |
| --- | ---: | ---: | ---: | ---: | ---: |
| Channels | 2 | 2,951227 s | 0,2035 s | 65,06 | 48,78 MiB |
| Dataflow | 2 | 2,963310 s | 0,1370 s | 64,79 | 46,89 MiB |
| Channels | 4 | 1,658759 s | 0,0594 s | 115,75 | 46,98 MiB |
| Dataflow | 4 | 1,967773 s | 0,2135 s | 97,57 | 46,86 MiB |

BDN etichetează memoria `MB`, cu unități bazate pe 1024; tabelul o exprimă ca MiB. Alocările managed includ munca din acea invocare și nu reprezintă working set, peak RAM sau întregul consum al decoderului/poolurilor ImageSharp.

Valorile brute, în secunde, din cele trei iterații:

| Variantă / workers | Iterația 1 | Iterația 2 | Iterația 3 |
| --- | ---: | ---: | ---: |
| Channels / 2 | 2,9637547 | 3,1481723 | 2,7417542 |
| Dataflow / 2 | 3,1032751 | 2,8294587 | 2,9571950 |
| Channels / 4 | 1,6996104 | 1,6860001 | 1,5906667 |
| Dataflow / 4 | 1,7680834 | 1,9423390 | 2,1928965 |

Raportat direct la mediile măsurate, Dataflow are timpul cu aproximativ **0,41% mai mare la 2 workers** și **18,63% mai mare la 4 workers**. Diferența de 0,41% este mult sub variația observată. Doar trei eșantioane și intervalele BDN foarte largi nu permit o afirmație statistică generală că un mecanism câștigă pe toate sarcinile. La patru workers, datele acestei rulări favorizează Channels. Creșterea la patru workers nu a fost adoptată în producție: acest profil nu măsoară latența UI, competiția pentru CPU sau presiunea de memorie pe fotografii mari.

Durata totală raportată de BDN: **90,7 s**, patru cazuri executate, exit code 0. Fiecare caz include generarea corpusului și verificările preflight în durata totală a suitei, în afara mediei afișate.

## Verificări și limite

Setup-ul compară automat cele două variante pe exact același corpus. Fiecare invocare verifică 192 rânduri Images, 576 amprente și existența miniaturilor referențiate. Ordinea rezultatelor paralele nu influențează verificarea: bytes ai amprentelor sunt citiți din SQLite ordonați după ImageId/AlgorithmId și combinați într-un SHA-256. Toate cele patru cazuri și toate iterațiile au raportat aceeași valoare:

```text
E2F4E9E368B300F0717FE45A6888C277B89E94C85F07B5B883C7A5BF147A19C4
```

Verificarea acoperă amprentele persistate și existența fișierelor miniaturilor, nu comparația pixel cu pixel a miniaturilor sau întregul contract al bazei de producție. Benchmarkul include timeout/cancellation pentru a evita blocarea în cazul unui defect de etapă, dar acest profil nu este o testare dedicată fault injection.

Nu au fost citite, modificate sau șterse colecții reale. Fixture-urile sunt create în `benchmarks/PerceptoX.Benchmarks/bin/Release/net10.0/.pipeline-benchmark-artifacts/<GUID>`. Cleanup-ul verifică prefixul absolut al destinației înainte să șteargă exclusiv acel subdirector generat. După rulare, parentul fixture-urilor era gol.

Nu au fost măsurate: un milion de fișiere, discul rece, fotografii de cameră/EXIF/RAW/AVIF, rescan incremental, latența UI, fairness sub alt workload, peak RAM, queue occupancy sau variante alternative de batching/scheduler Dataflow. Un test de scară ar necesita un corpus reprezentativ, profilarea persistenței reale și a memoriei și mai multe runde cu ordinea cazurilor variată.

## Artefacte de evidență

- `BenchmarkDotNet.Artifacts/PerceptoX.Benchmarks.PipelineBenchmarks-20261004-055422.log`
- `BenchmarkDotNet.Artifacts/results/PerceptoX.Benchmarks.PipelineBenchmarks-report.csv`
- `BenchmarkDotNet.Artifacts/results/PerceptoX.Benchmarks.PipelineBenchmarks-report-github.md`
- `BenchmarkDotNet.Artifacts/results/PerceptoX.Benchmarks.PipelineBenchmarks-report.html`

Concluzie de implementare: se păstrează pipeline-ul Channels. Dataflow rămâne o variantă benchmark, fără dependență NuGet nouă și fără migrare de producție.
