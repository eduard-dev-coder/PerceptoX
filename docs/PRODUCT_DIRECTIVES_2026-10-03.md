# PerceptoX — directive noi și ordine de implementare

Data: 2026-10-03. Stare: **planificat, nu implementat prin această actualizare**.
Completează `DEVELOPMENT_PLAN.md`. Cerințele repetate pentru Smart Cleanup,
drag-and-drop, codecuri și paralelism sunt consolidate aici.

## 1. Scop revizuit

Fluxul principal rămâne: referințe → bibliotecă → căutare → comparație → copiere/export.
Se adaugă două module distincte:

- **Întreținere**: curățarea manuală a datelor generate de PerceptoX.
- **Smart Cleanup**: propunerea și executarea, după confirmare, a mutării variantelor
  redundante în Recycle Bin sau arhivă. Nicio ștergere definitivă a fotografiilor.

Această cerere actualizează planul; nu autorizează executarea acum a unor curățări sau
mutări pe fișierele utilizatorului. Restricția anterioară „fără ștergere în versiunea
curentă” rămâne adevărată pentru codul livrat; nu mai exclude aceste funcții viitoare.
Nu se adaugă curățare programată sau neasistată. „Automat” înseamnă executarea lotului
selectat numai după preview și confirmarea utilizatorului.

## 2. Situația constatată în cod

| Zonă | Implementare existentă | Extensie necesară |
| --- | --- | --- |
| Indexare | `ImageIndexer`: două Channels bounded, capacitate 128, workers configurați (implicit 2), batch-uri DB de 64 | Configurație unificată workers/memorie, progres live, benchmark Channels vs Dataflow |
| SQLite | `SqliteIndexStore`: WAL, conexiune writer cu `Pooling=false`, lock de scanare, acces serializat prin `_gate`, staging/publicare | Coordonare exclusivă între întreținere, scanări, cititori, UI și alte instanțe |
| Memorie | `ImageSharpImageProcessor`: buget implicit 512 MiB și semafor; `MaximumPixels` 100 MP | Decodare redusă unde decoderul o permite, măsurarea memoriei reale |
| Miniaturi | Cache JPEG versionat; calea lipsă produce acum lipsa previzualizării | Regenerare la cerere, fără reindexare completă obligatorie |
| Formate | Discovery permite JPG/JPEG, PNG, BMP, GIF, WebP, TIF/TIFF; max. un frame în pipeline | Teste reale WebP și validarea separată HEIC/AVIF, sincronizarea pickerelor |
| Erori | Erorile de procesare sunt per fișier; discovery folosește `IgnoreInaccessible=false` | Enumerare tolerantă cu raportarea subarborilor neparcurși, fără dezactivări false |
| Cazuri-limită | Există test de compunere alpha pe alb, orientare și vectori hash | Extindere end-to-end la 1×1, alpha parțial, ICC/CMYK și fișiere corupte |
| Rezultate | Comparație alăturată, candidați selectabili, copiere fără overwrite, CSV/HTML | Zoom sincronizat, heatmap și feedback de calibrare |
| Logging | Serilog apare în arhitectura propusă; pipeline-ul inspectat nu are logger structurat injectat | Integrare efectivă, retenție, clasificare erori și raport diagnostic |
| Title bar | Conținut extins în bara de titlu, fără culori explicite pentru caption buttons | Remediere contrast activ/inactiv înaintea funcțiilor noi |

Aceasta este o inspecție de cod, nu o nouă rulare a testelor sau o validare a tuturor codecurilor.

## 3. Întreținere manuală: cele trei niveluri

Pagina Settings afișează locațiile exacte, bibliotecile afectate, dimensiunile indexului
și cache-ului, plus starea „ocupat/liber”. Calculul spațiului rulează asincron, anulabil;
totalurile inaccesibile sunt marcate incomplete, nu raportate drept zero.

| Nivel | Ținte | Efect și criteriu de acceptare |
| --- | --- | --- |
| 1 — Cache / Safe | Numai rădăcina administrată `thumbs`, inclusiv miniaturile referințelor | DB și fingerprint-urile rămân intacte; căutarea folosește indexul existent; miniaturile vizibile se refac la cerere |
| 2 — Resetare index / Deep Clean | Baza configurată (`index.db`) și cache-ul asociat; fișierele auxiliare SQLite numai în protocolul de mai jos | Toate bibliotecile din acea bază necesită reindexare; snapshot-urile și rezultatele UI sunt invalidate; fotografiile, copiile, rapoartele și setările externe sunt păstrate |
| 3 — Rapoarte / Optional | CSV/HTML enumerate în folderul ales și confirmate în preview | Nu afectează indexul sau fotografiile; implicit numai rapoarte identificate drept PerceptoX; alte CSV/HTML doar selectate explicit |

„Resetare completă” înseamnă resetarea **datelor de indexare**, nu ștergerea tuturor
preferințelor aplicației sau a datelor utilizatorului. UI-ul precizează dacă DB conține
mai multe biblioteci. Nu presupunem că numele sau locația DB sunt mereu cele implicite.

### 3.1 Protocol de execuție și SQLite blocat

1. Construire plan imutabil: tip, căi canonice, număr fișiere, spațiu estimat, consecințe.
2. Preview și confirmare; rădăcinile sursă/originale, home, drive-root și directoarele
   prea largi nu pot deveni ținte recursive. Se resping suprapunerile periculoase și
   reparse points/junctions. Căile configurabile trebuie verificate ca date ale aplicației.
3. Obținere acces exclusiv de întreținere; nu pornesc scanări/căutări/exporturi noi.
   Operațiile active sunt așteptate sau anulate controlat; utilizatorul poate anula.
4. Pentru Deep Clean: închiderea cititorilor, writer-ului, tranzacțiilor și handle-urilor;
   checkpoint WAL controlat când este posibil, apoi închiderea conexiunilor. Se golește
   doar pool-ul bazei vizate dacă există conexiuni pooled; writer-ul actual nu folosește pooling.
5. Blocarea unei alte instanțe se respectă. Nu se șterge `.scan.lock` ca metodă de a
   forța accesul și nu se închid procese externe. Dacă exclusivitatea nu poate fi obținută,
   operația se oprește fără a raporta succes.
6. `index.db-wal` și `index.db-shm` se tratează împreună cu DB, după închiderea completă;
   nu se elimină în timpul utilizării sau după un checkpoint eșuat cu cititori activi.
7. Pentru cache: se opresc și joburile de regenerare, astfel încât folderul să nu fie
   recreat concurent în timpul curățării. După operație, se reia încărcarea la cerere.
8. Erori `IOException`/acces refuzat: retry limitat pentru situații tranzitorii, mesaj
   utilizabil și raport pe fișier. Fără buclă infinită sau succes fictiv la execuție parțială.

Operația pe mai multe fișiere nu este presupusă atomică. Se jurnalizează progresul;
după resetare incompletă, aplicația păstrează o stare explicită de recuperare, nu
combină o bază nouă cu WAL-ul vechi. Se documentează ce s-a eliminat și ce a rămas.
Curățarea datelor generate nu promite ștergere criminalistică/irecuperabilă a urmelor.

### 3.2 Regenerarea miniaturilor — dependență obligatorie a Nivelului 1

- Serviciu de miniaturi reutilizat de căutare, batch, comparație și export.
- Cache miss → job unic pe cheia imagine/versiune, coadă bounded și buget de memorie.
- Generare numai pentru elementele solicitate; fără decodarea tuturor originalelor la scroll.
- Verificarea versiunii sursei; imagine lipsă/blocată → placeholder și eroare recuperabilă.
- Hash-urile nu se recalculează inutil pentru o simplă lipsă de miniatură.
- Acceptare: după Safe Clean, hash-urile și totalurile indexului rămân neschimbate;
  o nouă căutare funcționează și reface doar miniaturile necesare.

### 3.3 Rapoarte și fișiere assets

Exporturile sunt în subfoldere unice; preview-ul poate enumera aceste subfoldere în
rădăcina aleasă, fără traversarea linkurilor. Se introduce manifest de proveniență
pentru rapoartele noi. Cele vechi sau fișierele externe nu se șterg doar pe baza extensiei.
Fișierele `assets/*.jpg` ale unui HTML sunt o opțiune separată, bifată explicit numai
pentru pachetul de raport identificat; nu se șterg generic fotografii din folderul ales.
Retenția logurilor și feedback-ul de tuning nu intră implicit în cele trei niveluri.

## 4. Smart Cleanup — propunere, confirmare, mutare recuperabilă

### Reguli pentru alegerea variantei păstrate

- Rezoluție maximă: dimensiuni orientate corect și număr de pixeli; nu garantează calitate mai bună.
- Cea mai nouă: `LastWriteTimeUtc`, explicat ca dată de modificare, nu data fotografierii.
- Compresie redusă/calitate estimată: numai când există o măsurătoare justificată pentru
  formatele comparate. Nu deducem calitatea din mărimea fișierului/extensie și nu comparăm
  arbitrar scoruri JPEG cu HEIC/AVIF. Dacă lipsesc dovezi, utilizatorul alege manual.
- Egalități: alegere deterministă, motiv afișat și posibilitate de override per grup.

### Flux obligatoriu

1. Grupare și verificare: duplicate byte-identice confirmate prin conținut sunt separate
   de similitudinile perceptuale. Nici coliziunea pHash/dHash, nici un scor mare nu sunt dovadă
   suficientă pentru a muta automat un original. Grupurile ambigue/crop se validează vizual.
2. Fiecare element eliminabil trebuie verificat față de varianta păstrată; A≈B și B≈C
   nu înseamnă automat A≈C. Se rezolvă grupurile suprapuse înainte de planul final.
3. Preview exact: păstrat, mutat, motiv, dimensiuni, dată, destinație, total și spațiu estimat.
   Nu se folosesc aceleași reguli de selecție ca la simpla copiere a rezultatelor.
4. Pop-up înaintea execuției: avertizare că se pot pierde fișiere/legături din locația inițială,
   cu **Anulează** implicit și **Continuă** explicit. Arhiva și Recycle Bin au efecte explicate.
5. Revalidarea existenței, identității și versiunii fișierelor la execuție; plan expirat →
   skip/raport, nu mutarea unei alte versiuni. Cel puțin o variantă păstrată trebuie să existe.
6. Destinație: Recycle Bin prin serviciu Windows sau arhivă aleasă, fără overwrite.
   Dacă volumul/fișierul nu suportă reciclare, se propune arhivarea sau se omite;
   **niciodată fallback la ștergere permanentă**.
7. Arhivare cross-volume: copiere verificată înainte de îndepărtarea sursei, jurnal cu
   mapare sursă/destinație și posibilitate de reluare; nicio promisiune de rollback atomic global.
8. Anulare între operații și raport al lotului parțial. Actualizarea indexului numai
   pentru mutările reușite; invalidează rezultatele vechi. Arhiva nu este reindexată automat.

Gate: teste cu aceleași nume, grupuri suprapuse, sursă schimbată, fișier blocat, volum fără
Recycle Bin, anulare/crash și recuperare din arhivă. Nu se livrează modul distructiv
până când preview-ul, jurnalul și această politică sunt verificate.

## 5. Formate moderne și imagini dificile

- Inventar de codecuri efectiv disponibile în versiunea instalată; fixture legal pentru
  fiecare variantă, nu simpla adăugare a extensiei în discovery.
- WebP: lossy/lossless/alpha și politica explicită pentru animații (primul frame).
- HEIC/HEIF și AVIF: probă decode→orientare→hash→thumbnail→UI→export; testare metadata,
  bit depth/HDR și dependențe native. Dacă nu sunt suportate, adapter dedicat, cu evaluare
  de licență, distribuție și arhitectură x64; decizia de codec se documentează înainte de integrare.
- Catalog unic de capabilități pentru discovery, picker și drag-and-drop; „nesuportat”
  se separă de „corupt”, fără a opri lotul.
- Teste 1×1, 1×N, N×1, foarte mici, alpha 0/parțial, EXIF rotit, ICC valid/invalid,
  CMYK, fișier trunchiat, extensie falsă și imagini apropiate de limita de pixeli.
- Politică de culoare explicită; schimbarea conversiei de culoare impune versionarea
  profilului de hashing și verificarea vectorilor golden.

## 6. Drag-and-drop / Live Search

- Zonă de drop în WinUI pentru o referință externă, folosind indexul publicat existent;
  nu reindexează biblioteca la fiecare drop.
- Stări: fără index (îndrumare), pregătire, căutare, rezultate, nesuportat, eroare.
- Decode și matching pe worker, anulare și regulă „cea mai recentă cerere câștigă”;
  o căutare lentă nu poate înlocui rezultatul unei referințe mai noi.
- Prima versiune acceptă un fișier local; multiple fișiere și obiecte virtuale din Explorer
  au comportament explicit, fără extragere/tentative de descărcare ascunse.
- Se reutilizează pipeline-ul de referințe externe și cache-ul; rezultate responsive,
  latență p50/p95 măsurată. „Instantaneu” nu este promis fără măsurători pe biblioteci reale.

## 7. Paralelism, progres și toleranță la erori

Țintă: Discovery → Processing paralel → Persistence serializat, bounded pe toate legăturile.
Forma există deja cu Channels. Se realizează un prototip TPL Dataflow cu
`System.Threading.Tasks.Dataflow`, `BoundedCapacity`, `MaxDegreeOfParallelism`, propagare
de completion/cancel și writer unic. Migrarea este adoptată prin decizie tehnică după
compararea throughput-ului, RAM, latenței UI, anulării și complexității cu varianta actuală.
Nu se introduce și `Parallel.ForEach` în interiorul workerilor, multiplicând paralelismul.

- Bugetul efectiv ține cont de nuclee, storage, memoria decodată și paralelismul intern
  al codec-ului. Nu se lansează necondiționat o decodare mare pe fiecare thread CPU.
- Configurație comună pentru numărul workerilor și semaforul procesorului, evitând un
  plafon ascuns de 2 când UI-ul cere mai mulți workers.
- Progres live agregat de aproximativ 4–5 ori/secundă: descoperite, procesate, neschimbate,
  corupte, nesuportate, blocate/inaccesibile, imagini/secundă, timp; fără procent fals
  când totalul discovery nu este cunoscut. ETA doar după o estimare stabilă.
- Skip & Log continuă fișierele/surorile accesibile. Dacă un subarbore nu a putut fi
  enumerat, scanarea este marcată incompletă: versiunea inițială nu publică snapshot-ul
  parțial și nu dezactivează fișierele nevăzute. Rezultatul explică această limită.
- Benchmark pe SSD/HDD și corpus mixt, inclusiv 40 MP; păstrarea staging-ului atomic
  și teste de deadlock/backpressure/anulare sunt gate-uri obligatorii.

## 8. Downsampling la decodare

Se evaluează reducerea rezoluției în decoder, unde formatul/API-ul instalat o permite;
un resize după încărcarea completă nu este echivalent cu reducerea memoriei de decodare.
Nu presupunem economie identică pentru toate codecurile și păstrăm `DecodedMemoryBudget`.

Dimensiunea de lucru trebuie să satisfacă și miniaturile de 256 px și algoritmii regionali,
nu doar hash-ul final de 64 biți. pHash folosește o probă intermediară pentru DCT, nu
pur și simplu 8×8 pixeli de imagine. Dimensiunile originale și orientarea rămân metadata
autentice, separate de dimensiunile imaginii decodate redus.

Gate: benchmark RAM/timp, golden tests și precision/recall pe corpus etichetat. Dacă
hash-urile se schimbă, profil nou și reindexare explicită; fără amestecarea versiunilor.

## 9. Tuning prin feedback

- Comenzi „Similar” / „Nu este similar”, etichete editabile, legate de pereche,
  versiunea fișierelor, bibliotecă și profilul de procesare.
- Set etichetat separat pentru validare; număr minim de exemple pozitive și negative,
  controlul dezechilibrului și împărțire pe surse originale pentru a evita leakage.
- Ajustare bounded a pragurilor Hamming și parametrilor de validare, per profil/set;
  puține etichete produc o sugestie provizorie, nu un model declarat calibrat.
- Afișare înainte/după a preciziei, recall-ului, false positives/negatives și suportului
  statistic. Aplicare explicită, profil versionat, reset la preset și rollback.
- Nu modifică rezultatele deja afișate sau un plan Smart Cleanup confirmat. Nu declanșează
  mutări. Scorul procentual nu devine automat o probabilitate prin schimbarea pragului.

## 10. Compare Mode, zoom sincronizat și heatmap

- Referință și candidat alăturate, zoom/pan sincronizate în coordonate normalizate,
  buton de decuplare și reset. Decodare detaliată la cerere, cu limite și anulare.
- Orientare și spațiu de culoare comune; pentru crop/screenshot este necesară alinierea
  geometrică înaintea diferenței pixelilor. Zona fără corespondență este distinctă de
  o diferență de conținut; se arată regiunea estimată din original.
- Overlay roșu/verde cu legendă, slider de opacitate și explicația dovezii potrivirii
  (hash global/regiuni). Nu susținem că o hartă pixel explică exact decizia pHash.
- Dacă alinierea nu este sigură, UI afișează acest lucru și comparația simplă; nu produce
  o hartă care confundă resize/recompresia cu un watermark sau crop.
- Gate: perechi etichetate resize, crop, watermark/text, EXIF și imagini fără legătură.

## 11. Logging și contracte

`Microsoft.Extensions.Logging` în servicii și **Serilog** la composition root pentru
fișiere structurate cu rotație/retenție; nu introducem concomitent NLog.
Evenimente: jobId, etapa, durată, rate, rezultat, categorie eroare, retry, anulare,
curățare/mutare și rezultat per fișier. Identificarea fișierului folosește biblioteca
și calea relativă; căile absolute și exportul diagnosticului se controlează explicit.
Nu se loghează imagini sau secrete. Jurnalul de operații recuperabile este separat de
logul diagnostic rotativ.

Contracte propuse (nume orientative, introduse numai la implementare):

- Application: `MaintenancePlan`, `MaintenanceResult`, `IMaintenanceService`,
  `StorageUsage`, `IThumbnailService`, `IndexingProgress`, `CleanupPlan`, `CleanupResult`.
- Infrastructure: coordonator exclusiv DB/cache, servicii filesystem/Recycle Bin,
  adapters codec, logging; fără operații de fișiere în code-behind.
- Presentation: stări Preview/Confirm/Running/Cancelled/Partial/Failed/Complete;
  comenzi incompatibile dezactivate pe durata întreținerii.

## 12. Ordine și gate-uri de livrare

| Etapă | Livrabil | Condiție de închidere |
| --- | --- | --- |
| P0 — remediere curentă | Butoanele minimize/maximize/close vizibile în focus și fără focus | Verificare native caption activ/inactiv, hover/pressed; o captură XAML singură nu dovedește remedierea |
| 7A — diagnostic și robustețe | Logging, progres, storage usage, teste edge, Skip & Log | Nicio dezactivare falsă la scanare incompletă; erori identificabile, UI responsive |
| 7B — întreținere | Regenerare miniaturi, cele trei niveluri de curățare | Izolare căi, SQLite închis/exclusiv, fișiere blocate și anulare testate, originale intacte |
| 7C — eficiență și formate | Benchmark/prototip Dataflow, tuning workers, downsampling, WebP/HEIC/AVIF | Avantaj măsurat, buget RAM respectat, codecuri/profiluri validate, licențe documentate |
| 8A — căutare și comparație | Drag-and-drop, zoom sincronizat, aliniere și heatmap | Fără rezultate stale, explicații corecte și regresii UI |
| 8B — calibrare | Feedback și profiluri versionate | Set de validare separat și metrici pe fotografii reale |
| 9 — Smart Cleanup | Reguli de păstrare, preview, pop-up, Recycle Bin/arhivă, jurnal | Lot exact confirmat, nicio ștergere definitivă, recuperare/parțial/anulare testate |

După P0, primul increment recomandat este **7A + fundația 7B**: storage usage,
coordonatorul operațiilor și regenerarea miniaturilor. Acestea fac posibil Safe Clean
fără să lase utilizatorul cu rezultate fără previzualizare.

Delegare Ollama: liste de fixture-uri, texte UI și schelete de teste pe câte un modul.
Politicile de curățare, tranzacțiile/concurența și validarea finală rămân la agentul principal.
Nu sunt necesare zece rulări de agenți pentru actualizarea acestui document.
