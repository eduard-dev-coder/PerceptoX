# PerceptoX — Imagini similare / Find similar

7 octombrie 2026. Prima versiune funcțională; calitatea pe colecții reale nu este declarată calibrată.

## Utilizare

1. Deschide „Imagini similare”. Adaugă unul sau mai multe dosare prin „Răsfoiește” sau calea completă + „Adaugă dosar”. Subdosarele sunt incluse; rădăcinile suprapuse sunt normalizate înainte de indexare.
2. Lasă scorul minim la 97 sau alege până la 100. Opțiunea „Compară toate bibliotecile active din index” extinde analiza la toate rădăcinile deja indexate, nu doar dosarele adăugate. Indexarea incrementală actualizează dosarele selectate; celelalte rădăcini folosesc starea deja publicată, iar sursele sunt reverificate la validare/copiere.
3. Apasă „Analizează”. Indexarea și gruparea rulează în fundal, cu progres limitat înainte de postarea în UI și anulare. O analiză incompletă nu este prezentată drept completă.
4. Selectează un grup din coloana stângă. Coloana dreaptă afișează membri, nume evidențiat, cale, rezoluție, dimensiune, scor și dovadă. „Compară / Zoom” deschide inspectorul existent alături de reprezentant.
5. Exemplarul recomandat este selectat implicit: aria în pixeli, apoi bytes, apoi ID stabil. Nu există dosar preferat. Dimensiunea fișierului este un criteriu de departajare, nu dovadă că un upscale/recompresie este mai bun.
6. Selecția inversă inversează întreaga selecție, inclusiv membrii de pe alte pagini. „Selectează cele mai bune” revine la reprezentanți. Bifele reprezintă fișierele de păstrat/copiat; nu există ștergere automată.
7. „Copiază” cere destinația, refuză suprapunerea cu bibliotecile/datele aplicației, reverifică mărimea/timestamp-ul sursei, nu suprascrie și publică numai copii complete. Rapoartele CSV/HTML includ toți membrii, nu numai pagina curentă; HTML are miniaturi portabile și texte encodate, CSV protecție împotriva formulelor.

## Modificări și verificarea codului anterior

- Motorul inițial avea postări dictionary/list și aloca liste inclusiv pentru singleton-uri. Acum postările sunt tablouri numerice, listele apar numai pentru grupuri și căutarea sare peste prefixele deja vizitate.
- Testul PNG→JPEG a prins o pierdere reală în filtrul pHash strict. Am introdus candidați pHash≤3 SAU dHash≤3 și verificare RGB; testul de regresie rămâne în suită, nu a fost înlocuit pentru a ascunde problema.
- Variantele non-binare sunt verificate pe miniaturi RGB 32×32; diferența RMS normalizată este ≤0,03 la prag 97. Aspect ratio este verificat cu toleranță 2%. Imaginile uniforme cer identitate de conținut, evitând familiile fals pozitive de culori solide.
- SHA-256 se calculează în flux pentru candidații de aceeași mărime și se reutilizează după ID/mărime/timestamp. Validitatea acestui cache presupune că modificarea sursei schimbă mărimea sau timestamp-ul; nu este o protecție împotriva editării intenționate cu metadate conservate.
- Publicarea rezultatelor folosește o tranzacție SQLite și vectorul generațiilor pe rădăcini. Anularea face rollback la publicarea neterminată; indexările deja comise și checksum-urile valide pot rămâne. Se păstrează numai ultima analiză completă pentru a limita creșterea bazei.
- Am întărit serviciul de copiere reutilizat și de fluxul anterior: reject links/junctions, verificare versiune înainte și după citire, fără overwrite. Suitele de regresie pentru motorul existent, indexare, export, localizare și setări sunt rulate din nou.
- Galeria materializează cel mult 50 de grupuri și 50 membri, fără colecții de copii pentru grupurile închise. Copierea/exportul citesc membrii în flux; nu generează liste cu toate căile în UI.
- Rândurile folosesc texte notificabile pentru schimbarea instantanee a limbii și format numeric specific limbii; cultura motorului nu este modificată.
- Bibliotecile cu profil incompatibil sunt numărate explicit ca excluse și trebuie reindexate. Erorile procesării/verificării nu sunt ascunse ca „fără duplicate”.

## Dovezi automate și vizuale

- `tests/PerceptoX.Application.Tests/SimilarityGroupingTests.cs`: selecție deterministă, evitare lanțuri, acoperire baseline, bugete, anulare, validare RGB, recuperare prin dHash și grupuri peste TopN.
- `tests/PerceptoX.Infrastructure.Tests/SimilarityGroupingIntegrationTests.cs`: SQLite și adaptori desktop reali pe imagini generate; copii între dosare, resize, recomprimare, culori solide, cache lipsă, generații vechi, surse schimbate, rollback, schema v1→v2, checksum-uri, rapoarte portabile, rădăcini suprapuse și index global.
- `tests/PerceptoX.Presentation.Tests/SimilarityGroupsViewModelTests.cs` și `LiveLocalizationTests.cs`: selecție implicită/inversă/manuală, păstrare la reload, anulare, gate comun, prag invalid, localizare live.
- `artifacts/grouping-ux-20261007-live`: aceeași pagină trece Light/RO→Dark/EN→Dark/RO cu aceleași obiecte și 1.001 selecții. După 20 pagini din 1.000 de grupuri sintetice, selecția inversă dă 1.999/3.000 membri. `groups.json` dovedește numai 50 grupuri și 3 membri materializați pentru aceste fixture-uri.
- Capturile XAML nu certifică randarea nativă a butoanelor de caption, gesturile reale mouse/tastatură sau timpii pe fotografii reale.

## Performanță — scopul măsurătorii

BenchmarkDotNet `Dry`, in-process, 3 măsurători, Ryzen 5900X, .NET 10.0.9. Date: hash-uri sintetice, cu o copie la fiecare cinci intrări. Verificatorul este un stub; nu se măsoară decodarea, SHA-256, SQLite, I/O sau timpul total al scanării.

Rezultatele configurației finale sunt în `artifacts/grouping-benchmark-20261007-accepted/results/PerceptoX.Benchmarks.GroupingBenchmarks-report-github.md` și JSON. Pentru 100k/500k/1M intrări: aproximativ 75 ms / 652 ms / 2,38 s și 17 / 70 / 136 MiB alocate per operație. Acestea sunt alocări cumulative managed, nu peak RAM/working set. Trei măsurători Dry nu sunt un benchmark statistic extins; intervalele de eroare sunt largi.

Limite: maximum 1M intrări compatibile și 128M vizite de postări. Datele adversariale pot opri analiza explicit; nicio promisiune de comportament subliniar pe toate distribuțiile. Configurația inițială depășea bugetul la 1M; eliminarea prefixelor deja vizitate a permis finalizarea fără mărirea bugetului. Artefactele încercărilor precedente sunt păstrate, dar nu reprezintă rezultate acceptate.

Copiile proiectelor din ZIP-urile sursă produc o ambiguitate în generarea out-of-process BenchmarkDotNet; `eng/run-benchmark.ps1 -InProcess` oferă alternativa explicită. Nu ștergem livrările anterioare pentru a ocoli problema.

## Gate-uri deschise / extensii

- Calibrare precision/recall pe minimum 10k fotografii reale etichetate, separate de setul de tuning; nu am scanat dosare personale nespecificate.
- Peak RAM și performanță end-to-end pe biblioteci reale mari, HDD/network/fișiere blocate. Prima indexare și verificarea checksum-urilor au cost I/O real.
- Dacă ambele distanțe pHash și dHash depășesc 3, fotografia nu este candidat în acest profil. Limita este explicită, nu o garanție universală de găsire a tuturor variantelor.
- Apartenență principală unică greedy, deterministă; fiecare membru este verificat direct cu reprezentantul. Nu detectăm/persistăm încă toate apartenențele alternative ambigue sau excluderile manuale.
- Persistarea rezultatului în SQLite nu înseamnă restaurarea selecțiilor/pauză/relua­re la restart; acele extensii și partiționarea disk-backed nu sunt implementate.
- Crop, screenshot cu cadrare diferită, rotații fără normalizare și similaritate semantică nu sunt obiective ale acestui tab. Fluxul anterior de identificare a referințelor rămâne separat.
- Pachetul este pentru validare/uz personal; gate-urile existente privind redistribuirea publică și Windows curat rămân deschise.

## Verificarea finală a surselor

- 290 teste trecute, fără eșecuri: Core 31, Application 37, Infrastructure 158, Presentation 64.
- Build WinUI Debug x64: 0 avertismente, 0 erori. Pachetul Release folosește același mecanism standalone existent; identitatea și verificarea startup sunt consemnate separat după generare.
- Validarea RO/EN: 407 chei, referințe și placeholder-e compatibile. Paletele Light/Dark au trecut validarea existentă.
- Nu au fost modificate/șterse fotografii personale. Testele au lucrat numai pe fixture-uri generate și directoare temporare proprii; livrările anterioare au fost păstrate.

## Livrarea verificată

- Folder: `artifacts/distribution/20261006-230908-e0dc4379/PerceptoX-win-x64`.
- ZIP: `artifacts/distribution/20261006-230908-e0dc4379/PerceptoX-win-x64.zip`, 120.590.105 bytes.
- SHA-256: `192ED6A3AF3BC1CB41403D29D6F8045583B6ACF77E7F900139EA78AE7EC83D0F`.
- Surse GPL-3.0-only: `PerceptoX-sources.zip` și materialele terțe alături. Timestamp-ul dosarului este UTC; raportul folosește data locală 7 octombrie.
- Release standalone publicat fără erori. ZIP extras separat într-o cale cu spații; integritatea verificată și startup trecut cu cinci runtime-uri DLL app-local, fără PATH extern.
- Dovadă: `artifacts/validation/grouping-20261007-e0dc4379/startup/startup-result.json`, status `StartupPassed`, `error: null`.
- `operatorConfirmedOffline: false`, `cleanMachineVerified: false`: testul nu certifică offline strict, Windows curat sau conformitate juridică pentru redistribuire publică.
