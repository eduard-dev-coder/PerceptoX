# PerceptoX — plan de arhitectură și dezvoltare

Status: **Redesign WinUI după graphics/UI.png, potrivire între dosare, selecție/copiere și rapoarte CSV/HTML implementate; 102 teste verzi. Lista nouă este verificată cu 1.000 rezultate la 100%. Narrator și DPI 150%/200% sunt în afara cerințelor utilizatorului.**  
Țintă: aplicație desktop Windows pentru indexarea, gruparea și căutarea imaginilor similare, proiectată pentru până la 1.000.000 de imagini.

**Actualizare de plan — 2026-10-03:** [directivele noi](PRODUCT_DIRECTIVES_2026-10-03.md) introduc întreținere manuală pe trei niveluri, Smart Cleanup recuperabil și confirmat, formate moderne, Live Search, tuning, comparație/heatmap, progres și logging. Sunt planificate, nu implementate prin această actualizare. Cele 102 teste de mai sus reprezintă ultima validare de implementare, nu o rulare nouă pentru aceste funcții. Defectul de contrast al butoanelor native ale ferestrei, în starea activă, este deschis ca P0.

## 1. Principii și limite

PerceptoX va fi construit în ordinea **motor CLI → teste și benchmark-uri → WinUI 3**. Interfața nu începe înainte ca motorul să funcționeze pe minimum 10.000 de imagini reprezentative.

Prima versiune utilizabilă va:

- indexa incremental imagini fără a materializa întreaga bibliotecă în memorie;
- calcula și păstra cel puțin pHash și dHash, fără a lega arhitectura de acești doi algoritmi;
- păstra separat metadatele, fingerprint-urile și miniaturile;
- genera în timpul indexării un thumbnail JPEG de maximum 256 px și va elibera imediat imaginea decodată;
- căuta „similar cu imaginea selectată” și va procesa batch-uri de query-uri;
- forma grupuri de duplicate/similare fără a pierde fișierele care au același hash;
- exporta CSV și un raport HTML cu miniaturi;
- oferi progres, anulare, erori per fișier și logging structurat;
- păstra UI-ul deconectat de ImageSharp, SQLite și operațiile de fișiere.

pHash și dHash acoperă bine duplicate, resize, recomprimare și thumbnail, dar nu sunt suficiente pentru crop-uri semnificative, screenshot-uri parțiale, meme-uri sau text suprapus. PerceptoX nu va promite aceste cazuri până când un algoritm dedicat — de exemplu BlockHash, WaveletHash sau CropResistantHash — trece un set de teste etichetat.

## 2. Toolchain și mediu

- SDK: **.NET 10 LTS**, fixat la `10.0.301` prin `global.json`; nu folosim implicit SDK-ul preview instalat.
- Restore: `NuGet.Config` din repository folosește numai `nuget.org` și cache-ul local `.packages/`.
- Sandbox: comenzile .NET rulează prin `eng/dotnet-sandbox.ps1`, care izolează `APPDATA`, `DOTNET_CLI_HOME` și cache-urile în workspace. Nu acordăm sandbox-ului acces la configurația NuGet personală, care poate conține feed-uri sau credențiale private.
- AI local: integrare prin runner controlat; Ollama clasic folosește `http://127.0.0.1:11434`, iar DeepSeek Harness poate fi folosit prin `http://127.0.0.1:3080` după configurarea autorizării. Executabilele din profilul Windows nu trebuie lansate de sandbox.
- WinUI 3: se validează separat printr-un proiect minimal înainte de începerea UI-ului; existența componentelor Visual Studio nu este echivalentă cu un build WinUI verde.

Comenzi standard:

```powershell
& .\eng\dotnet-sandbox.ps1 --version
& .\eng\dotnet-sandbox.ps1 restore PerceptoX.sln --configfile .\NuGet.Config
& .\eng\dotnet-sandbox.ps1 build PerceptoX.sln --no-restore
& .\eng\dotnet-sandbox.ps1 test PerceptoX.sln --no-build
```

## 3. Structura soluției

```text
PerceptoX.sln
├─ src/
│  ├─ PerceptoX.Core/
│  ├─ PerceptoX.Application/
│  ├─ PerceptoX.Infrastructure/
│  ├─ PerceptoX.Presentation/
│  ├─ PerceptoX.Cli/
│  └─ PerceptoX.WinUI/
├─ tests/
│  ├─ PerceptoX.Core.Tests/
│  ├─ PerceptoX.Application.Tests/
│  ├─ PerceptoX.Infrastructure.Tests/
│  └─ PerceptoX.Presentation.Tests/
├─ benchmarks/
│  └─ PerceptoX.Benchmarks/
├─ docs/
│  └─ decisions/
├─ eng/
├─ Directory.Build.props
└─ Directory.Packages.props
```

Dependențe:

```text
WinUI ──► Presentation
  │
  ├─────────────┐
  ▼             ▼
Application ◄── Infrastructure
  │             │
  └──────► Core ◄──────┘

CLI ──► Application + Infrastructure
```

Reguli:

- `Core` conține modele, contracte matematice și algoritmi fără WinUI, SQLite sau DI.
- `Application` orchestrează cazurile de utilizare și definește porturile de persistență, filesystem și export.
- `Infrastructure` implementează ImageSharp, SQLite, cache-ul, scanarea și operațiile de fișiere.
- `Cli` este primul composition root și bancul funcțional al motorului.
- `WinUI` este al doilea composition root; ViewModels nu accesează direct ImageSharp sau SQLite.
- interfețele sunt introduse la granițe reale, nu pentru fiecare clasă.

## 4. Organizarea internă

```text
PerceptoX.Core/
├─ Images/          ImageId, ImageRecord, ImageMetadata
├─ Fingerprints/    descriptor, valoare, HammingDistance, algoritmi
├─ Matching/        candidate, result, score, search profile
├─ Groups/          duplicate/similar group rules
└─ Common/

PerceptoX.Application/
├─ Indexing/        scan, staging, publish, progress
├─ Matching/        batch și FindSimilarToSelected
├─ Thumbnails/      contractul cache-ului
├─ Groups/          construire și actualizare grupuri
├─ Exporting/       CSV și HTML
├─ FileOperations/  plan/execute/report
└─ Settings/

PerceptoX.Infrastructure/
├─ Imaging/         ImageSharp și implementări fingerprint
├─ Search/          linear Hamming, BK-tree opțional, snapshot
├─ Persistence/     Microsoft.Data.Sqlite și migrații
├─ Indexing/        enumerare și pipeline bounded
├─ Thumbnails/      cache JPEG pe disc
├─ Exporting/       CSV/HTML streaming
├─ Logging/         Microsoft.Extensions.Logging + Serilog
└─ FileSystem/      copy/move/Explorer
```

## 5. Modele separate pentru un milion de imagini

Nu există un `List<ImageMetadata>` global care conține toate detaliile și thumbnail-urile.

- `ImageRecord`: identitate, rădăcină, `PathKey`, cale, nume, dimensiuni, aspect ratio, mărime, timestamp și stare.
- `FingerprintRecord`: `ImageId`, algoritm, versiune, profil, număr de biți și valoarea hash.
- `ThumbnailRecord`: `ImageId`, versiunea sursei, cale relativă, format și dimensiune.
- `SearchIndexEntry`: structură compactă pentru hot path, doar ID, hash-uri necesare și câteva bucket-uri numerice.
- detaliile complete se încarcă în lot numai pentru candidații finali și pentru elementele vizibile.

## 6. Contractul extensibil de fingerprint

Contractul public va fi `IImageFingerprintAlgorithm`, nu o clasă legată de pHash/dHash.

Fiecare implementare expune un descriptor persistent:

- `AlgorithmId`, de exemplu `phash`, `dhash`, `blockhash`;
- `AlgorithmVersion` și `ProfileId`;
- `BitLength` și tipul de stocare (`UInt64` sau bytes);
- orientarea EXIF aplicată sau nu;
- dimensiunile de resize și resampler-ul;
- formula de luminanță/grayscale;
- ordinea biților;
- versiunea relevantă ImageSharp/oracle;
- pragurile calibrate separat de hash.

Schimbarea oricăruia dintre acești parametri creează un profil nou și solicită reindexare controlată. Nu reinterpretăm hash-uri vechi cu o implementare nouă.

Algoritmi planificați:

| Nivel | Algoritmi | Scop |
|---|---|---|
| MVP | pHash 64, dHash 64; aHash doar ca baseline/oracle dacă este util | near-duplicate, resize, recomprimare, thumbnail |
| Extensie măsurată | BlockHash, WaveletHash | robustețe suplimentară fără schimbarea serviciilor |
| Mod crop | CropResistantHash și/sau descriptori locali | crop, screenshot parțial, compoziții |

`CoenM/ImageHash` este oracle comparativ MIT, nu dependență runtime obligatorie. Pipeline-ul, bit ordering-ul și vectorii noștri rămân expliciți și testați.

## 7. Pipeline ImageSharp

```text
FileStream
  → Identify/validare limite
  → Image.Load<TPixel>
  → AutoOrient conform profilului
  → o singură decodare
  ├─ Resize/Grayscale → pHash
  ├─ Resize/Grayscale → dHash
  ├─ algoritmi suplimentari activați
  └─ Resize max 256 px → thumbnail JPEG
  → ProcessPixelRows/Span
  → Dispose imediat
```

Reguli:

- limite explicite pentru dimensiuni/pixeli și tratare per fișier a imaginilor corupte;
- concurrency limitată și după memoria decodată estimată, nu doar după numărul de nuclee;
- buffer-e și `ArrayPool<T>` numai după benchmark; fără LINQ în hot path;
- nu se păstrează obiecte `Image` între etape și nu ajung în UI.

## 8. Indexare incrementală și publicare atomică

```text
Directory.EnumerateFiles
  → extensii + reparse/permission policy
  → metadate și PathKey
  → skip dacă este neschimbat
  → Channel bounded
  → workers decode/hash/thumbnail
  → Channel rezultate
  → writer SQLite unic în staging
  → publish atomic numai la scanare reușită
  → snapshot de căutare nou, publicat atomic
```

- un singur writer SQLite reutilizează comenzile parametrizate în tranzacții batch;
- anularea sau căderea lasă snapshot-ul anterior activ; nu marchează fișierele lipsă pe baza unei scanări incomplete;
- fișierele dispărute devin inactive numai la publicarea scanării complete;
- progresul este agregat/throttled, nu emis pentru fiecare imagine;
- erorile per fișier sunt persistate sumar și nu opresc implicit lotul.

## 9. Thumbnail cache obligatoriu

- format implicit: JPEG, latura maximă 256 px, profil/quality versionat;
- cale: `.cache/thumbs/<prefix>/<ImageId>-<SourceVersion>.jpg`;
- scriere în fișier temporar și rename atomic;
- UI-ul încarcă numai thumbnail-uri; originalul se deschide doar la cererea explicită a utilizatorului;
- regenerarea la cerere și curățarea manuală Safe sunt planificate în etapa 7B; regenerarea automată la cache miss trebuie implementată înainte de livrarea curățării cache-ului;
- o limită de spațiu cu evacuare automată rămâne o extensie separată, nu face parte din cererea de curățare manuală;
- cache-ul este reconstruibil și nu este sursă de adevăr.

## 10. Căutare în etape

```text
Query fingerprint
  → profil de căutare
  → bucket-uri metadata soft
  → IHammingSearchIndex (linear sau BK-tree dovedit)
  → validare dHash și metadata
  → verificare crop-resistant opțională
  → scor calibrat
  → Top-N bounded
  → încărcare detalii/thumbnail numai pentru rezultate
```

Decizii:

- `IHammingSearchIndex` ascunde strategia; implementarea de bază este un scan compact cu `BitOperations.PopCount`.
- BK-tree nu este presupus câștigător în spațiul Hamming de 64 biți. Se activează numai dacă bate baseline-ul la 100k, 500k și 1M pentru razele reale și nu degradează memoria/recall-ul.
- lățimea, înălțimea, aspect ratio și file size reduc candidații prin bucket-uri; nu sunt automat filtre dure.
- profilul „near duplicate” poate folosi toleranțe stricte. Profilul „crop/screenshot” lărgește bucket-urile și are fallback global pentru a nu distruge recall-ul.
- dHash validează near-duplicates; nu este etichetat drept algoritm crop-resistant.
- indexul este construit complet și publicat ca snapshot imuabil; query-urile sunt read-only.

Formula ponderată inițială este doar baseline:

```text
similarity(distance, bitLength) = 100 × (1 - distance / bitLength)
final = Σ(weight_i × calibratedSimilarity_i)
```

Ponderile și pragurile sunt calibrate pe dataset, versionate și separate pe tip de transformare.

## 11. Duplicate groups

- duplicatele exacte și near-duplicates sunt categorii distincte;
- coliziunile de hash nu produc ștergere sau unificare automată;
- gruparea folosește muchii validate și reguli versionate; similitudinea nu este presupusă tranzitivă fără test;
- fiecare grup păstrează toate fișierele, un reprezentant ales determinist și scorurile de legătură;
- reconstruirea grupurilor este un job separat, anulabil, care publică atomic rezultatul.

## 12. Schema SQLite

Tabelele logice:

| Tabel | Conținut |
|---|---|
| `SchemaInfo` | versiunea schemei și migrațiilor |
| `LibraryRoots` | rădăcini indexate și politici |
| `ScanRuns` | început, final, stare, statistici |
| `Images` | identitate și metadata, fără hash-uri sau thumbnail bytes |
| `PendingImages` | modificările unei scanări înainte de publish |
| `ImageFingerprints` | toate hash-urile/profilurile per imagine |
| `Thumbnails` | metadata cache-ului, nu imaginea originală |
| `DuplicateGroups` / `DuplicateGroupMembers` | grupuri versionate |
| `SearchHistory` | istoric opțional și limitat, fără a loga implicit conținut sensibil |
| `Settings` | setări persistente versionate |

Fragment orientativ:

```sql
CREATE TABLE Images (
    Id INTEGER PRIMARY KEY,
    RootId INTEGER NOT NULL,
    PathKey TEXT NOT NULL,
    FilePath TEXT NOT NULL,
    FileName TEXT NOT NULL,
    Width INTEGER NOT NULL CHECK (Width > 0),
    Height INTEGER NOT NULL CHECK (Height > 0),
    AspectRatio REAL NOT NULL CHECK (AspectRatio > 0),
    AspectBucket INTEGER NOT NULL,
    FileSize INTEGER NOT NULL CHECK (FileSize >= 0),
    LastWriteTimeUtcTicks INTEGER NOT NULL,
    IsActive INTEGER NOT NULL DEFAULT 1,
    UNIQUE (RootId, PathKey)
);

CREATE TABLE ImageFingerprints (
    ImageId INTEGER NOT NULL,
    AlgorithmId TEXT NOT NULL,
    AlgorithmVersion INTEGER NOT NULL,
    ProfileId TEXT NOT NULL,
    BitLength INTEGER NOT NULL,
    Value64 INTEGER NULL,
    HashBytes BLOB NULL,
    PRIMARY KEY (ImageId, AlgorithmId, AlgorithmVersion, ProfileId),
    CHECK ((Value64 IS NULL) <> (HashBytes IS NULL)),
    FOREIGN KEY (ImageId) REFERENCES Images(Id) ON DELETE CASCADE
) WITHOUT ROWID;
```

Observații:

- `ulong` se mapează bit-cu-bit la `long` numai la granița repository-ului;
- nu indexăm implicit pHash/dHash ca valori SQL: Hamming distance nu folosește util un B-tree obișnuit;
- indexăm `RootId + PathKey`, stare/scan și bucket-urile dovedite de query planner;
- `WAL` + `synchronous=NORMAL` este acceptabil pentru indexul reconstruibil local, cu checkpoint-uri controlate;
- migrațiile sunt explicite și testate; baza nu este ștearsă automat la upgrade.

## 13. UI și MVVM

- `CommunityToolkit.Mvvm`: `[ObservableProperty]`, `[RelayCommand]`, `AsyncRelayCommand`, `[NotifyCanExecuteChangedFor]`;
- stări explicite: `Idle`, `Indexing`, `Searching`, `Cancelling`, `Faulted`;
- ViewModels primesc servicii prin constructor și rămân testabile fără WinUI;
- progresul și colecțiile se actualizează în loturi pe UI thread;
- `GridView` este alegerea MVP pentru selecție, focus și accesibilitate mature;
- `ItemsRepeater` se adoptă numai dacă prototipul măsurat justifică implementarea manuală a selecției, tastaturii, focusului și automatizării;
- lista folosește virtualizare și miniaturi; nicio decodare full-resolution la scroll.

## 14. Export, filesystem și siguranță

- CSV: escaping RFC 4180 și neutralizarea celulelor care pot produce formula injection;
- HTML: scriere streaming într-un director raport cu assets/miniaturi, nu Base64 nelimitat într-un singur fișier;
- copy/move: mai întâi plan imutabil și preview, apoi execuție cu politică explicită de coliziune și raport per fișier;
- nicio suprascriere implicită și nicio ștergere a fotografiilor în versiunea curentă;
- extensie planificată: întreținere manuală pentru datele generate și Smart Cleanup cu preview, confirmare și mutare în Recycle Bin/arhivă; fără ștergere definitivă a fotografiilor. Detalii și gate-uri în [directivele 2026-10-03](PRODUCT_DIRECTIVES_2026-10-03.md);
- reparse points/junctions sunt ignorate implicit pentru a evita cicluri și ieșirea din rădăcinile aprobate;
- toate căile sunt normalizate centralizat și validate din nou la execuție;
- fișierele temporare și exporturile sunt publicate atomic când este posibil.

## 15. Logging

- implementat în incrementul 7A: `Microsoft.Extensions.Logging` în servicii și Serilog pentru JSONL cu rotație/retenție; detalii în [PHASE7_MAINTENANCE.md](PHASE7_MAINTENANCE.md);
- rolling file logs cu retenție și limită de dimensiune;
- evenimente: scan start/finish, rate, durată hash, query latency, candidați, cache hit, erori și anulare;
- nu se loghează bytes de imagine, hash-uri complete sau căi absolute la nivel normal; diagnosticul extins este opt-in;
- metricile agregate pot fi exportate fără date personale.

## 16. Teste și benchmark-uri

```text
tests/TestImages/
├─ Original/
├─ Thumbnail/
├─ Resize/
├─ Crop/
├─ Screenshot/
├─ Watermark/
└─ Compressed/
```

Setul este copyright-safe, mic în repository și are un manifest etichetat. Teste obligatorii:

- vectori golden pentru pHash/dHash și profilurile persistate;
- determinism, orientare EXIF, bit ordering și mapping `ulong ↔ long`;
- `Should_Find_Thumbnail`, `Should_Find_Compressed_Copy`, `Should_Find_Cropped_Image` doar după algoritmul potrivit;
- anulare la fiecare etapă și publish atomic;
- fișiere corupte, inaccesibile, duplicate, junctions și căi lungi;
- migrații SQLite, restart și scanare repetată fără rehash inutil;
- export CSV/HTML și coliziuni la copy/move.

BenchmarkDotNet măsoară separat:

- hash speed/alocări pe dimensiuni și formate;
- scan linear `PopCount` versus BK-tree la 100k/500k/1M și raze reale;
- încărcare snapshot și batch SQLite;
- generare thumbnail și memorie decodată;
- precision@N/recall@N pe fiecare transformare.

## 17. Delegare locală cu AI local

Modele țintă pentru delegare locală:

| Model | Sarcini delegate |
|---|---|
| `qwen2.5-coder:14b` | model zilnic pentru boilerplate limitat, teste repetitive, documentație, refactor local simplu |
| `qwen2.5-coder:32b` | review mai dificil, alternative de design, verificarea unui diff mai mare |
| `deepseek-coder-v2:latest` | opțional pentru review algoritmic, cazuri-limită și propuneri de benchmark, dacă este instalat ulterior |

Reguli pentru economie reală de tokeni și controlul calității:

1. O cerere acoperă un singur modul sau o singură decizie și are limită de output.
2. Se trimit doar fișierele necesare; niciodată secrete, config personal, baze reale sau colecții de imagini private.
3. Ollama nu editează direct workspace-ul și nu decide arhitectura finală.
4. Rezultatul este tratat ca propunere neîncrezută; Codex verifică sursele, diff-ul, build-ul, testele și benchmark-urile.
5. Sarcinile grele sau cu risc — contracte publice, concurență, SQLite atomic, securitate, migrații — rămân la Codex/review uman.
6. Un răspuns generic sau incorect se respinge; nu se consumă alte runde doar pentru a-l „convinge”.

Runner-ul controlat pentru Ollama clasic:

```powershell
& .\eng\ollama-delegate.ps1 `
  -Model 'qwen2.5-coder:14b' `
  -Prompt 'Revizuiește un singur modul și returnează maximum 6 riscuri.' `
  -MaxTokens 900
```

Pentru DeepSeek Harness autentificat:

```powershell
$env:PERCEPTOX_LOCAL_AI_API_KEY = '<token local>'
& .\eng\ollama-delegate.ps1 `
  -BaseUri 'http://127.0.0.1:3080' `
  -Model 'deepseek-coder-v2:latest' `
  -Prompt 'Revizuiește un singur modul și returnează maximum 6 riscuri.' `
  -MaxTokens 900
```

Integrarea MCP este opțională după ce workflow-ul CLI/API se dovedește stabil. MCP nu schimbă regula că modelul local propune, iar Codex validează și aplică.

## 18. Plan de livrare cu gates

### Faza 0 — mediu și arhitectură

- [x] numele PerceptoX și planul modular;
- [x] SDK stabil fixat, NuGet/cache izolate, restore/build console dovedit în sandbox;
- [x] endpoint AI local verificat: Ollama clasic are `qwen2.5-coder:14b` și `qwen2.5-coder:32b` instalate, iar delegarea prin runner a fost dovedită cu `qwen2.5-coder:14b`; DeepSeek Harness pe `3080` răspunde, dar profilul headless cere provider credential separat;
- [x] restore și build minimal WinUI 3 (`net10.0-windows10.0.26100.0`, Windows App SDK 2.4.0, 0 warnings/errors); lansarea UI nu a fost testată;
- [ ] decizie licență/distribuție ImageSharp și MSIX/unpackaged;
- [ ] hardware și dataset reprezentativ.

Gate: utilizatorul a aprobat continuarea. ImageSharp 3.1.12 este folosit în dezvoltare sub Six Labors Split License; eligibilitatea pentru distribuție, forma finală de packaging și dataset-ul țintă rămân decizii de release/performance.

### Faza 1 — scaffold și motor CLI

- [x] soluție, proiecte Core/Application/Infrastructure/Cli și teste;
- [x] central package management, analyzers și warnings-as-errors;
- [x] fluxul CLI `compare <imagine1> <imagine2>` cu pHash/dHash și scor baseline etichetat necalibrat;
- [x] restore, build și 36 de teste; build/test și rularea CLI au funcționat în sandbox după restore;
- [x] contractele pentru `ImageRecord`, `FingerprintRecord` și `ThumbnailRecord`, necesare indexării;
- [x] vectori golden PerceptoX/ImageHash pe aceleași imagini procedurale, cu diferențele de profil documentate;
- [x] generator reproductibil pentru 10.000 de imagini sintetice, cu manifest etichetat; verificare streaming 10.000/10.000 imagini, zero erori; nu substituie un corpus real reprezentativ;
- [ ] comenzile `index`, `find-similar`, `export` se livrează împreună cu etapele de persistență și matching, nu în acest prim scaffold.

Gate Faza 1 (inginerie) trecut: `compare` rulează end-to-end, contractele sunt testate și cele două familii de vectori golden sunt fixate. Gate-ul complet al motorului așteaptă indexare, export și validarea pe corpus reprezentativ. Dataset-ul procedural verifică volum/reproductibilitate, nu recall în lumea reală.

### Faza 2 — fingerprint engine

- [x] pHash/dHash inițiale versionate, Hamming cu `BitOperations.PopCount`, contract extensibil pentru fingerprint-uri de lungime variabilă;
- [x] spike comparativ cu ImageHash și vectori golden; profilurile sunt intenționat incompatibile;
- [x] pipeline ImageSharp cu admisie bounded pentru workeri și memorie estimată, decodare unică și thumbnail de maximum 256 px în cache versionat;
- [x] teste pentru orientare, transparență, dimensiuni extreme, fișiere corupte, anulare, concurență și cache; BenchmarkDotNet pentru hash-uri și procesarea imaginii.

Gate Faza 2 (inginerie) trecut: vectorii golden rămân deterministici, 49/49 teste trec, iar verificarea sintetică este 10.000/10.000 cu zero erori și miniaturi. Benchmark-urile rulează și raportează alocări; cifrele sunt baseline-uri locale, nu praguri de performanță. Corpusul real reprezentativ și măsurarea precision/recall rămân deschise pentru gate-ul complet al motorului. Detalii în `docs/PHASE2_ENGINE.md`.

### Faza 3 — SQLite și indexare

- [x] schemă/migrație v1 SQLite, staging și publicare atomică într-o tranzacție;
- [x] indexare incrementală prin canale bounded, anulare, recuperarea unei scanări abandonate și dezactivarea fișierelor dispărute doar după traversare completă;
- [x] snapshot compact, ordonat și filtrat după profilul de fingerprint.

Gate Faza 3 (inginerie) trecut: a doua scanare de 10.000 de imagini a procesat 0 fișiere, iar testele dovedesc că anularea și un eșec de publicare păstrează indexul activ anterior. Snapshot-ul conține 10.000 de intrări. Acest gate nu validează încă performanța la 1.000.000 de fișiere sau calitatea matching-ului pe corpus real. Detalii și limite în `docs/PHASE3_INDEXING.md`.

### Faza 4 — matching și grupuri

- [x] linear Hamming baseline cu `BitOperations.PopCount`; BK-tree neactivat deoarece baseline-ul măsurat este ~1,50 ms la 1M intrări pseudo-aleatoare;
- [x] metadata soft, fallback broad explicit, scor **necalibrat** și Top-N bounded/determinist;
- [x] `Find Similar To Selected Image`, API batch lazy/anulabil și grupuri candidate pHash+dHash fără afirmație de identitate exactă;
- [x] export CSV protejat contra formula injection și raport HTML static cu encoding și miniaturi locale.

Gate parțial: latențele și precision/recall sunt raportate pe dataset-ul etichetat procedural, iar toate componentele tehnice funcționează. Baseline-ul conservator 2/2 și Top-10 rămâne la 73,41% precision / 53,59% recall global, cu 0% recall pentru crop și screenshot. Faza 5 adaugă fallback-ul multi-region; validarea pe un corpus foto real rămâne necesară înainte de calibrare și persistarea grupurilor near-duplicate. Detalii în `docs/PHASE4_MATCHING.md`.

### Faza 5 — crop-resistant

- [x] spike BlockHash/WaveletHash/multi-hash crop-resistant și alegerea unui profil bounded v1;
- [x] fingerprint fix `multiregion` 9×64 biți integrat prin același contract și aceeași schemă;
- [x] snapshot contiguu, căutare cross-region și integrare hibridă în CLI/export;
- [x] praguri separate pentru profilul multi-region și evaluare crop/screenshot/overlay.

Gate de inginerie trecut: `ShouldFindCroppedImage` trece la pragul implicit `1/1`; pe setul procedural precision este 73,33% față de 73,41% baseline, iar recall urcă de la 53,59% la 69,20%. Scanarea liniară multi-region are media 295,30 ms și 384 B alocați la 1M intrări pseudo-aleatoare. Gate-ul de produs rămâne deschis până la un corpus foto real. Detalii în `docs/PHASE5_CROP_RESISTANT.md`.

### Faza 6 — MVVM și WinUI 3

- [x] ViewModels și teste înaintea Views, fără referință la WinUI;
- [x] NavigationView cu pagini de indexare, căutare și setări;
- [x] progres, anulare și mesaje de eroare controlate de comenzi asincrone;
- [x] GridView virtualizat care decodează numai miniaturile la 256 px;
- [x] pickere desktop inițializate cu HWND, DPI PerMonitorV2 și resurse de temă;
- [x] motorul CPU/I/O este apelat pe worker thread prin composition root-ul desktop;
- [x] profilare reproductibilă a scroll-ului pe 1.000 de rezultate în Light/Dark la scala 100%; virtualizarea și focusul direcțional trec pragurile interne;
- [ ] parcurgere interactivă completă a tastaturii și pickerelor; Narrator și DPI 150%/200% au fost eliminate explicit din cerințe.

Validarea inginerească a UI-ului este trecută în scopul revizuit. Utilizatorul a autorizat continuarea cu operațiile și redesignul. Verificarea interactivă completă a pickerelor/tastaturii rămâne documentată separat; nu este confundată cu testele automate. Noua interfață Light și lista de 1.000 de rezultate au capturi și măsurători în `artifacts/design/`. Detalii curente în `docs/PHASE7_BATCH_MATCHING.md`.

### Faza 7 — operații și hardening

- [x] comparație referință/candidați, selecție, copiere fără suprascriere și rapoarte CSV/HTML;
- [x] redesign conform UI.png, logo și iconiță executabil; fără mutare/ștergere;
- [x] P0: culori native active/inactive/hover/pressed corectate în cod;
- [ ] P0 gate: verificare vizuală native în focus și fără focus; helper Computer Use blocat;
- [x] 7A: logging structurat, progres detaliat, storage usage și Skip & Log fără dezactivări false;
- [x] 7A: teste 1×1/alpha/WebP/corupte, enumerare incompletă și migrare v1→v2;
- [x] 7A: fixture-uri ICC valide RGB/CMYK/YCCK; fără afirmații de fidelitate colorimetrică;
- [ ] 7A gate: validare interactivă completă;
- [x] 7B: regenerare la cerere și întreținere manuală Cache / Deep Clean / Rapoarte, cu coordonare SQLite exclusivă;
- [x] 7B: anulare parțială Cache/Deep Clean și junction introdus între eliminări, cu fixture-uri exterioare păstrate;
- [ ] 7B gate: verificare interactivă a dialogurilor;
- [x] 7C: benchmark Channels/Dataflow efectuat; Channels păstrat pe baza măsurătorii;
- [x] 7C: downsampling JPEG opt-in, profil separat; decoder ImageMagick explicit opțional, AVIF testat prin SQLite;
- [ ] 7C gate: peak RAM/quality pentru downsampling; corpus HEIC/AVIF orientat/HDR și validare de distribuție;
- test end-to-end la 100k, apoi 1M de înregistrări sintetice/reale permise;
- packaging și documentație.

### Faza 8 — căutare interactivă, comparație și calibrare

- [x] 8A: căutare externă pe indexul existent și integrarea drag-and-drop;
- [ ] 8A gate: interacțiune Windows și comportament „ultima cerere câștigă”;
- [x] 8A: zoom/pan sincronizat și heatmap experimental cu aliniere translație/scalare pentru crop;
- [ ] 8A gate: interacțiune zoom/pan reală și acceptare pe corpus crop/screenshot/watermark;
- [x] 8B: feedback Similar/Nu este similar, profiluri versionate persistate, holdout cu gates, aplicare explicită și rollback;
- [ ] 8B gate: corpus real independent; splitul familiilor depinde de etichetarea corectă, scorul rămâne neprobabilistic.

### Faza 9 — Smart Cleanup recuperabil

- [x] reguli: rezoluție maximă și cea mai recentă modificare; fără proxy neverificat pentru calitate;
- [x] plan exact pe grupuri selectate și verificate manual, preview și confirmare explicită;
- [x] arhivă same-volume fără overwrite, jurnal durabil, hash și mutare prin handle Windows verificat;
- [x] recuperare, anulare parțială, grupuri suprapuse/fișiere blocate testate; indexul este blocat până la reindexarea bibliotecilor afectate;
- [x] nicio ștergere definitivă și niciun fallback distructiv; Recycle Bin nu este implementat;
- [ ] gate: validarea interactivă a preview/confirmare/Oprește/recuperare, fără operații pe colecții reale în teste automate.

Criteriile de acceptare și dependențele sunt în [PRODUCT_DIRECTIVES_2026-10-03.md](PRODUCT_DIRECTIVES_2026-10-03.md). Prioritate: P0 → 7A → 7B → 7C → 8A → 8B → 9. Nu activăm Smart Cleanup pe baza scorurilor necalibrate actuale.

Starea implementării și gate-urile rămase la 2026-10-04 sunt în [DELIVERY_2026-10-04.md](DELIVERY_2026-10-04.md).

Distribuția standalone și modulele: [MODULE_MANAGER_VALIDATION_2026-10-04.md](MODULE_MANAGER_VALIDATION_2026-10-04.md).
Actualizarea livrării și verificărilor pasului 1: [STEP1_DELIVERY_2026-10-05.md](STEP1_DELIVERY_2026-10-05.md).
Pasul 2, dialogul de progres: [STEP2_SCAN_PROGRESS_2026-10-05.md](STEP2_SCAN_PROGRESS_2026-10-05.md).
Pasul 3, inspectorul de comparație: [STEP3_IMAGE_INSPECTOR_2026-10-05.md](STEP3_IMAGE_INSPECTOR_2026-10-05.md).
Armonizarea interfeței, setări pe taburi și limbile JSON: [UI_LOCALIZATION_2026-10-05.md](UI_LOCALIZATION_2026-10-05.md), [LOCALIZATION.md](LOCALIZATION.md).
Corecții de centraj, butoane Răsfoiește și selector radio pentru lupă: [UI_CENTERING_2026-10-05.md](UI_CENTERING_2026-10-05.md).
Texte simplificate, acțiuni compacte și spațiu mărit pentru rezultate: [UI_COPY_2026-10-06.md](UI_COPY_2026-10-06.md).
Tema Dark și schimbarea instantanee a temei/limbii: [DARK_LIVE_SETTINGS_2026-10-06.md](DARK_LIVE_SETTINGS_2026-10-06.md).
Plan final aprobat și prima versiune funcțională a tabului Find similar / Imagini similare: [FIND_SIMILAR_PLAN_2026-10-07.md](FIND_SIMILAR_PLAN_2026-10-07.md). Implementare, dovezi și gate-uri încă deschise: [FIND_SIMILAR_IMPLEMENTATION_2026-10-07.md](FIND_SIMILAR_IMPLEMENTATION_2026-10-07.md). Calibrarea pe fotografii reale și validarea end-to-end a bibliotecilor mari nu sunt închise prin simpla livrare a tabului.
Managerul app-local este implementat cu acord și activare la repornire; update
online necesită endpoint/cheie aprobate. Codul propriu este GPL-3.0-only, iar
source ZIP este generat separat. Gate-ul de redistribuire publică nu este închis
până la materialele/compatibilitatea dependențelor terțe și Windows curat.

## 19. Decizii deschise pentru fazele următoare și distribuție

1. ImageSharp 3.1.12: Apache-2.0 pe criteriul PerceptoX open-source GPL-3.0-only. La upgrade se reverifică termenii. Compatibilitatea pachetului nativ complet GPL/Microsoft și obligațiile terțe rămân gate-uri separate.
2. Distribuție decisă: folder/ZIP standalone unpackaged Windows x64, .NET și Windows App SDK self-contained; decoder app-local fără instalări separate. Cod propriu GPL-3.0-only (MIT exclusă), surse proprii și arhivă oficială Windows a decoderului livrate separat. Implementarea și gate-urile sunt în [STANDALONE.md](STANDALONE.md); distribuția publică și Windows curat au verificări separate.
3. Hardware-ul minim și dataset-ul de benchmark, inclusiv proporția crop/screenshot/meme.
4. Opțiune viitoare pentru păstrarea structurii directoarelor la copiere; momentan copiere plată cu sufixe la coliziuni. Mutarea recuperabilă intră în planul Smart Cleanup, numai după preview și confirmare; ștergerea definitivă a fotografiilor rămâne exclusă.
5. Alegerea codec-urilor HEIC/AVIF și validarea lor end-to-end; adăugarea unei extensii nu dovedește suportul.
6. Adoptarea TPL Dataflow numai după comparație măsurată cu pipeline-ul Channels existent.

Recomandarea curentă: unpackaged în dezvoltare, fără operații automate asupra originalelor, junctions ignorate și toate variantele păstrate până la validarea modulului Smart Cleanup. Noile operații sunt planificate separat; curățarea nu este executată prin aprobarea acestui plan.

## 20. Referințe

- [CoenM/ImageHash](https://github.com/coenm/ImageHash)
- [ImageSharp](https://github.com/SixLabors/ImageSharp), [processing](https://docs.sixlabors.com/articles/imagesharp/processing.html), [pixel buffers](https://docs.sixlabors.com/articles/imagesharp/pixelbuffers.html)
- Neal Krawetz: [Looks Like It](https://www.hackerfactor.com/blog/index.php?/archives/432-Looks-Like-It.html), [Kind of Like That](https://www.hackerfactor.com/blog/index.php?/archives/529-Kind-of-Like-That.html)
- [WinUI 3](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/), [ItemsRepeater](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/items-repeater)
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/)
- [Microsoft.Data.Sqlite](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/), [SQLite WAL](https://www.sqlite.org/wal.html), [query planner](https://www.sqlite.org/queryplanner.html)
- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)
- [Ollama generate API](https://docs.ollama.com/api/generate), [installed model catalog](https://ollama.com/library?q=code)
