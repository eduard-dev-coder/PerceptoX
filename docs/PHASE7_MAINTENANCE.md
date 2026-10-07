# PerceptoX — întreținere, diagnostic și căutare rapidă

Increment implementat la 2026-10-03. Funcțiile extinse din PRODUCT_DIRECTIVES_2026-10-03.md
nu sunt toate închise; starea verificată este descrisă mai jos.

**Notă istorică:** verificările de mai jos reprezintă incrementul din 3 octombrie.
ICC/CMYK, anularea parțială, junction între eliminări, benchmark, codecuri opționale,
comparația, feedback-ul și Smart Cleanup au avansat în [DELIVERY_2026-10-04.md](DELIVERY_2026-10-04.md).

## Funcții disponibile

- Settings → Întreținere: măsurarea indexului (inclusiv WAL/SHM) și cache-ului.
- Cache: previzualizare și confirmare, eliminarea miniaturilor eligibile, păstrarea
  hash-urilor. Rezultatele vechi sunt invalidate, iar următoarea căutare regenerează
  miniaturile candidaților fără recalcularea hash-urilor sau decodare la scroll.
- Resetare: lista bibliotecilor afectate, index și miniaturi; excluderea operațiilor
  concurente și verificarea fișierelor blocate înaintea eliminării. Originalele rămân intacte.
- Rapoarte: report.csv/report.html din exporturi marcate PerceptoX. Asset-urile și alte
  fișiere rămân. Rapoartele vechi fără marcaj nu sunt adoptate automat.
- Progres: procesate, neschimbate, erori, corupte, nesuportate, inaccesibile, imagini/secundă.
- Skip & Log: o enumerare incompletă păstrează intrările anterioare nevăzute; nu le dezactivează.
- Căutare individuală externă și integrare drag-and-drop pe indexul existent, fără reindexare.
- Caption buttons: culori explicit setate pentru active/inactive/hover/pressed pe fundal Light.

## Siguranță și contracte

Application/Maintenance definește contractele. Infrastructure/Maintenance implementează
operațiile, Presentation deține starea, iar WinUI furnizează confirmarea.
Preview-ul expiră în 10 minute; o previzualizare nouă o înlocuiește pe cea veche.
Execuția recitește inventarul și refuză modificările între preview și confirmare.
Contabilitatea SHM, modificată de SQLite chiar la citire, este verificată separat.

WorkspaceLease exclude indexarea, căutarea, exportul desktop și întreținerea pe aceleași
căi DB/cache, inclusiv între procese. Scan lock-ul existent este verificat suplimentar.
Conexiunile SQLite folosesc Pooling=false. Preflight-ul are un număr limitat de handle-uri,
inclusiv pentru cache-uri mari. Un proces extern fără lease poate modifica/bloca ulterior
un fișier; rezultatul este atunci parțial, cu eroarea raportată și oprirea operației.

Rădăcinile referințelor și toate LibraryRoots din DB sunt protejate. Root-uri largi,
suprapuneri și link-uri/junctions sunt refuzate. Se elimină fișierele exact inventariate,
apoi doar directoarele goale; nu se șterge recursiv un folder arbitrar. Fișierele necunoscute
din cache sunt păstrate. În cache-uri vechi fără marcaj se curăță numai miniaturile eligibile
referite de DB; restul este numărat în preview. Fișierele .lock pot rămâne după curățare.

Anularea după eliminarea indexului permite finalizarea eliminării sidecar-urilor SQLite.
Dacă rămân sidecar-uri fără DB, o indexare nouă este refuzată până la reluarea resetării.
Eliminarea mai multor fișiere nu este o tranzacție atomică. Regenerarea verifică versiunea
originalului; un original modificat necesită reindexare.

## Diagnostic și SQLite

Microsoft.Extensions.Logging și Serilog scriu JSONL în %LocalAppData%/PerceptoX/logs,
cu rotație zilnică, prag de 10 MB/fișier și retenție de 14 fișiere. Evenimente: scanare,
rate, erori, căutări, durate batch și întreținere. Logurile folosesc nume/căi relative,
fără bytes de imagine sau căi absolute. Erorile de curățare arată căile concrete în UI.

Migrarea v1→v2 adaugă ScanRuns.DiscoveryComplete și păstrează datele/hash-urile.
O scanare cu rezultate publicate, dar enumerare incompletă, are Status=completed și
DiscoveryComplete=0; LastCompletedScanId nu este avansat.

## Verificări și limite

- 114 teste trecute: Core 31, Application 14, Presentation 15, Infrastructure 54.
- Build Debug/Release x64 fără avertismente sau erori.
- Teste noi: cache/hash-uri/regenerare, reset/reindexare/originale intacte, SQLite blocat,
  concurență, preview modificat, anulare înainte de execuție, confirmare anulată,
  workflow pornit în timpul confirmării, rapoarte străine, migrare și enumerare incompletă.
- 1×1 PNG/WebP, alpha și fișiere corupte trec pipeline-ul/testele. HEIC/HEIF/AVIF sunt
  numărate ca nesuportate; niciun codec nou pentru acestea nu este declarat implementat.
- Căutarea externă trece testul cu adapterul real fără modificarea indexului.
- Captură Settings la 100%: ../artifacts/maintenance/settings/preview.png.
- Helper-ul Computer Use nu pornește: “failed to launch codex app-server: The system
  cannot find the path specified. (os error 3)”. Captura XAML nu include caption buttons;
  contrastul native activ/inactiv nu este încă validat vizual.
- Ollama 127.0.0.1:11434 a refuzat conexiunea; nu a produs o revizie în acest increment.
  Runner-ul a fost corectat pentru raportarea erorii reale în StrictMode.

Rămân: ICC/CMYK pe fixture-uri reale, anulare în mijlocul curățării și schimbări de
reparse point în timpul execuției; dialoguri/pickere/native caption/drag-and-drop testate
interactiv; benchmark Channels/Dataflow și downsampling versionat; codecuri HEIC/AVIF,
zoom/heatmap, feedback/calibrare și Smart Cleanup recuperabil. Cererile drop sunt refuzate
în timpul unei operații; comportamentul „ultima cerere câștigă” rămâne deschis.

Referințe: [title bar WinUI](https://learn.microsoft.com/en-us/windows/apps/develop/title-bar?tabs=winui3),
[Serilog](https://www.nuget.org/packages/Serilog/4.3.1),
[adapter logging](https://www.nuget.org/packages/Serilog.Extensions.Logging/10.0.0),
[file sink](https://github.com/serilog/serilog-sinks-file).
