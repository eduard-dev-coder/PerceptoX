# PerceptoX — plan pentru Find similar / Imagini similare

Stare: plan final de produs aprobat, 7 octombrie 2026; implementare în etape, cu validare înainte de livrare.

## Deciziile finale confirmate

- Detectăm aceeași fotografie: fișiere identice, resize și recomprimare; nu fotografii semantic asemănătoare. Crop/screenshot nu intră în profilul implicit.
- Unul sau mai multe dosare cu subdosare; scop explicit: dosarele selectate sau comparație cu toate bibliotecile active indexate.
- Prag implicit afișat 97%, scor perceptual orientativ, nu procent de pixeli identici. Profilul inițial este experimental până la calibrarea pe fotografii etichetate.
- Exemplarul implicit selectat este cel de păstrat/copiat: aria în pixeli descrescătoare, apoi dimensiunea fișierului, apoi ID stabil. Nu există dosar preferat. Dimensiunea în bytes este numai departajare, nu dovadă de calitate.
- Selecție inversă disponibilă explicit, cu numărul fișierelor selectate afișat. Inversarea nu mută și nu șterge nimic.
- Grupuri virtuale cu validare directă la reprezentant; fără unirea tranzitivă a familiilor. Copiere sigură și rapoarte, fără ștergere automată.
- Identitatea binară necesită hash de conținut verificat; egalitatea pHash/dHash se numește numai amprentă identică.
- Etapele de mai jos sunt criterii de implementare, nu funcționalități deja livrate. Persistența reluării și profilul crop rămân extensii ulterioare.

## 1. Obiectiv și delimitare

Un tab distinct de potrivirea referință → originale și de căutarea unei imagini: utilizatorul selectează o bibliotecă, analizează similitudinile din interiorul ei și primește grupuri de fotografii. Propunere: RO „Imagini similare”, EN „Find similar”. Tema și limba respectă mecanismele live existente.

Recomandarea pentru prima versiune: variante ale aceleiași fotografii — copii, resize, recomprimare, modificări moderate. Crop-urile și capturile de ecran sunt un profil separat, mai costisitor și mai dificil. Fotografii diferite ale aceluiași subiect/scenă reprezintă similaritate semantică, nu o garanție a pHash/dHash; un eventual model local pentru acest scop trebuie evaluat și distribuit separat. Ollama nu devine o dependență a aplicației.

„Grupare” înseamnă inițial grupuri virtuale în aplicație, nu mutarea fișierelor în Windows. Nicio ștergere sau mutare automată. Sugestia unei imagini reprezentative nu autorizează eliminarea celorlalte.

## 2. Ce există, verificat în cod

- `SearchIndexSnapshot`: date compacte cu ID, pHash, dHash, rezoluție și dimensiune, plus versiunea scanării/profilul. Nu păstrează toate imaginile decodate.
- `SimilaritySearchService`: căutare pHash, validare dHash, scor și integrare multi-region.
- `LinearHammingSearchIndex`: parcurgere liniară cu PopCount; aceasta este strategia implicită. Nu trebuie repetată pentru toate imaginile unei biblioteci mari.
- `FingerprintCandidateGroupService.BuildPreview`: sortează o copie a snapshot-ului și grupează egalitatea pHash+dHash, cu limite de preview. Nu demonstrează egalitatea fișierelor și nu este încă un motor complet de grupare perceptuală.
- SQLite: identități, imagini, amprente versionate, miniaturi, scanări și publicare a datelor. `SqliteIndexReader` încarcă metadatele detaliate numai pentru ID-urile cerute.
- Workflow-ul căutării unei imagini încarcă snapshot-urile la cerere. Este necesară o reutilizare controlată pentru tabul nou, nu un presupus cache deja existent.
- UI: selecție, comparație, copiere, rapoarte, Light/Dark și localizare JSON reutilizabile. Modulul de arhivare are propriile condiții de validare; nu este conectat automat la grupurile noi.
- Benchmark-ul existent pentru căutare folosește hash-uri sintetice aleatoare; nu dovedește performanța grupării întregii biblioteci sau a hash-urilor perceptuale reale.

## 3. Flux recomandat

Selectare dosar(e) → descoperire/indexare incrementală → snapshot coerent → agregare amprente egale → generare candidați → validare → grupuri virtuale → încărcare paginată în UI.

1. Un dosar cu subdosare implicit; extensie pregătită pentru mai multe dosare, de confirmat. Canonicalizarea căilor elimină suprapunerile între rădăcini și scanarea dublă a acelorași căi. Identitatea fizică/hardlink nu se deduce doar din numele fișierului.
2. Reutilizarea amprentelor numai pentru fișiere neschimbate și profiluri compatibile. Fișiere noi/modificate sunt procesate; fișiere dispărute devin inactive după o descoperire completă, nu după o scanare anulată.
3. Analiza citește o generație coerentă. Pentru mai multe rădăcini, versiunea analizei include vectorul generațiilor lor, nu un singur ScanId presupus global. Citirea hash-urilor și metadatelor necesare folosește aceeași tranzacție/sesiune protejată.
4. Indexarea, curățarea cache/indexului, arhivarea și analiza nu pot modifica simultan aceeași generație. Dacă biblioteca se schimbă, rezultatele sunt marcate ca vechi, nu actualizate parțial în tăcere.
5. Grupurile finale sunt publicate atomic. Rezultatele unei analize anterioare valide pot rămâne vizibile pe durata recalculării, cu generația afișată clar.

## 4. Strategia de căutare: fără N × N implicit

Un milion de imagini înseamnă aproximativ 500 de miliarde de perechi neordonate. Un apel liniar per imagine sau un JOIN brut între toate hash-urile nu este acceptabil ca motor implicit.

### 4.1 Fast path pentru amprente egale

Agregăm întâi perechile egale pHash+dHash, păstrând liste compacte de ID-uri. Comparațiile ulterioare pot lucra pe reprezentanți, dar metadatele membrilor și rezultatele validării rămân individuale. În special, egalitatea amprentelor nu permite ignorarea dimensiunilor/profilului sau clasificarea drept duplicate binare.

Dacă se dorește eticheta „duplicat exact”: grupare după FileSize și SHA-256 calculat în flux numai pentru candidați plauzibili, cu verificarea versiunii fișierului înainte/după citire. Hash-ul de conținut se păstrează versionat. Egalitatea pHash/dHash singură primește eticheta „amprente identice”, nu „fișiere identice”.

### 4.2 Index de candidați Hamming

Prototipăm Multi-Index Hashing pe segmente ale pHash-ului. Postările conțin indici numerici în structuri compacte, nu câte un obiect/dictionary per fotografie. Interogările sunt urmate de calculul distanței complete pHash și dHash, nu de încredere în egalitatea unui segment.

Acoperirea razei Hamming trebuie demonstrată pentru configurația de segmente/probe și comparată cu baseline-ul exact pe dataset-uri mici. „Un segment egal” nu este o regulă corectă pentru orice prag. Numărul de segmente și probe se alege după distribuția reală, prag și buget; nu presupunem că 4 × 16 biți este universal optim.

Fiecare pereche este verificată cel mult o dată prin ordonarea ID-urilor/indicilor. Deduplicarea candidaților folosește buffer numeric reutilizabil sau marcaje de generație, nu un HashSet nou mare pentru fiecare query.

Comparăm această strategie cu linear scan și, dacă merită, BK-tree. Alegerea se bazează pe timpi, RAM, recall și distribuții reale. Nu adoptăm BK-tree doar pentru că există în arhitectura inițială. MIH are și el cazuri nefavorabile; nu promitem complexitate subliniară pe toate imaginile.

Referință primară: [Norouzi, Punjani, Fleet — Fast Exact Search in Hamming Space with Multi-Index Hashing](https://www.cs.toronto.edu/~norouzi/research/mih/).

### 4.3 Profiluri de analiză

| Profil | Verificare | Delimitare |
| --- | --- | --- |
| Amprente identice | pHash+dHash egale; SHA opțional | Egalitatea perceptuală nu dovedește egalitate binară |
| Variante apropiate — implicit | pHash+dHash, metadate și prag versionat | Resize/recompresie/editări moderate; rezultate de verificat vizual |
| Crop / screenshot — opțional | index regional de candidați + verificare regională/aliniere | Cost și erori mai mari; nu activăm un full scan regional per imagine |

Rezoluția și dimensiunea fișierului nu sunt filtre eliminatorii universale: tocmai resize-ul și recomprimarea schimbă aceste date. Aspect ratio poate ajuta profilul de variante fără crop, dar nu elimină candidații din profilul crop. Un eșec dHash global nu trebuie să respingă automat un candidat regional valid. Scorurile profilelor nu sunt prezentate ca probabilități și nu se combină fără calibrare.

Imaginile uniforme, aproape goale sau cu foarte puțină informație pot produce bucket-uri enorme și coliziuni perceptuale. Ele au diagnostic/categorie separată și verificare mai strictă. Pragurile largi pot conduce la foarte multe perechi; o limită de resurse declanșează avertizare, checkpoint sau oprire explicită, nu o trunchiere ascunsă.

## 5. Gruparea: evitarea lanțurilor false

Similaritatea nu este tranzitivă. Connected components/Union-Find pe toate perechile nu sunt grupuri confirmate: A~B și B~C nu implică A~C.

Recomandare implicită: grup cu reprezentant stabil, iar fiecare membru este validat direct față de acesta. Reprezentantul se alege determinist: prioritate după aria imaginii, apoi o regulă de departajare stabilă. Fișierul mai mare nu este automat de calitate mai bună. Utilizatorul poate schimba reprezentantul fără a modifica originalele; dacă reprezentantul definește grupul, se revalidează membrii.

Aceasta garantează legătura membru → reprezentant, nu toate legăturile membru → membru. Dacă se dorește ca fiecare pereche să respecte pragul, este un mod complete-link separat, mai costisitor. Pentru grupuri mari nu executăm automat o verificare pătratică.

Ordinea și regulile sunt fixe și testate. Membrul care se potrivește mai multor grupuri este marcat ambiguu și nu provoacă unirea automată a familiilor. Apartenența principală este unică; candidaturile alternative pot fi afișate pentru revizuire. Un algoritm greedy poate depinde de ordinea reprezentanților: determinismul nu trebuie confundat cu adevărul fotografic absolut.

Excluderea manuală a unei potriviri trebuie păstrată pe pereche + versiunile surselor și ale profilului, nu numai pe ID-ul efemer al grupului. Recalcularea nu reintroduce automat o excludere încă validă. Persistența confirmărilor între analize și granularitatea lor sunt opțiuni de confirmat.

## 6. Resurse și performanță

- Hash-uri/metadate numerice în memorie; căi, EXIF și imagini decodate încărcate doar la nevoie. Nu duplicăm întregul snapshot pentru fiecare worker sau pagină.
- Cache-ul snapshot-ului este identificat de baza de date, rădăcini, generații și profil. Este limitat și invalidat la reindexare/curățare; un cache nelimitat de biblioteci nu este acceptabil.
- Hash-urile regionale sunt încărcate numai când profilul le cere. La un milion de imagini, numai 72 bytes regionali + un ID de 8 bytes pe imagine înseamnă aproximativ 76 MiB, fără copii/indexuri/runtime.
- Memoria pentru snapshot, sortări, postări, buffers și UI se măsoară separat. Buget inițial propus pentru lucru: 256–512 MiB, configurabil; reprezintă țintă de proiectare, nu consum total garantat al procesului.
- Dacă bugetul nu este suficient: partiții/procesare disk-backed cu probe la toate partițiile relevante. Nu împărțim arbitrar în loturi fără comparațiile dintre loturi și nu raportăm analiza drept completă.
- Pentru analiză, începem cu 2–4 workers, buffers limitați și un writer SQLite. Pe Ryzen 5900X creștem numai dacă benchmark-ul dovedește câștig; mai multe fire pot satura memoria sau discul.
- Păstrăm pipeline-ul existent de indexare și bugetul de memorie al decoderului. Nu schimbăm Channels cu Dataflow doar pentru acest tab. JPEG/HEIC mari pot necesita mai mult timp la prima indexare; reanaliza unui index valid nu redecodează originalele.
- Miniaturi din cache, nu originalele în galerie. Încărcare la cerere, limită pentru cererile concomitente, anularea încărcărilor ieșite din viewport, LRU pentru imagini decodate. Cache șters: regenerăm doar miniaturile vizibile.
- UI virtualizat, paginare după cheie stabilă, grupurile închise fără colecții de copii materializate. Primele 3–4 miniaturi ca preview, membrii paginați la extindere.
- Progres la aproximativ 4 actualizări/secundă, fără câte un event UI/log per pereche. Etape distincte, contoare relevante și ETA numai unde este estimabil.
- Anulare verificată în discovery, încărcare, probing, validare și scriere. Rollback pentru lotul/tranzacția nefinalizată; generațiile valide rămân. Nicio conexiune reader ținută deschisă inutil în timp ce utilizatorul inspectează galeria.
- RAM/CPU/disc scăzut, drive extern/network, fișiere blocate și lipsă de spațiu sunt stări explicite. Nu ascundem costul primei indexări în promisiuni despre viteza căutării.

Nu folosim TopN din căutarea unei imagini ca limită de membri ai unui grup: ar pierde variantele suplimentare. Un plafon de preview este diferit de totalul membrilor și trebuie afișat ca atare.

## 7. Persistență și reluare

Migrare SQLite versionată, tranzacțională, cu teste pe schema curentă. Propunere de entități:

- `SimilarityRuns`: status, profil/praguri/algoritm, semnătura scopului, checkpoint și contoare.
- `SimilarityRunRoots`: versiunile scanărilor pe rădăcini.
- `SimilarityGroups` și `SimilarityGroupMembers`: grup, reprezentant, membri, tipul dovezii/distanțe și revizuire.
- `SimilarityDecisions`: excluderi/confirmări versionate.
- `ContentHashes`: numai dacă se adoptă duplicatele binare.

Writer unic, scriere în loturi și finalizare atomică a statusului Complete. Nu salvăm toate perechile posibile într-o tabelă gigant. Sunt păstrate dovezile necesare explicației/revizuirii; volumele de relații ambigue au limite explicite. Checkpoint-ul trebuie să includă regulile și starea minimă pentru reluare deterministă; nu doar „am ajuns la fișierul 5000”. Reluarea este permisă numai dacă generațiile/profilul/opțiunile coincid.

La întrerupere, afișăm Incomplete/Cancelled. Exportul unui rezultat parțial îl marchează explicit. Recalcularea după modificări trebuie să reverifice grupurile afectate; ștergerea unui reprezentant sau schimbarea pragului poate modifica familia, deci nu promitem incrementalitate completă pentru grupare din prima versiune.

## 8. Interfață și acțiuni

Bară superioară: dosare, include subdosare, profil, Analizează/Oprește, stare index și contor. Nu pornește un job costisitor la fiecare modificare de slider; recalcularea se confirmă explicit.

Galerie: reprezentant, număr membri, tipul similitudinii, dimensiuni și badge „de verificat”. Extindere cu membri, nume evidente, comparație alăturată existentă și filtrare. Filtrarea după nume trebuie să poată găsi și membrii neîncărcați în UI, prin SQLite.

Opțiuni utile de confirmat:

1. Copiere în `Group_001`, `Group_002` etc., manifest de corespondență și preview; destinație în afara dosarelor analizate implicit. Fără overwrite, coliziunile primesc nume unice, sursa se reverifică înainte de copiere. Hardlink-urile nu sunt folosite ca „copii” implicit.
2. CSV/HTML cu grupuri, membri, găsite/negrupate/ambigue/erori, profilul și generația analizei. Copiile și rapoartele nu sunt reintroduse ca surse la următoarea scanare în tăcere.
3. Excluderi manuale și confirmări persistente; nu ajustează automat pragurile globale.
4. Sugestie „cea mai mare rezoluție” sau „cea mai recentă”, cu explicație, nu decizie automată despre calitate.
5. Filtru grupuri de minimum 2/3/5 membri, duplicate binare versus variante perceptuale, dosar și stare de revizuire.
6. Pauză/reluează sau păstrare la restart, numai după implementarea și testarea checkpoint-urilor.

Nu adăugăm automat integrarea Smart Cleanup/Recycle Bin. Aceasta este o extensie ulterioară, cu preview, confirmare și propriile gate-uri de siguranță.

## 9. Cazuri de test și criterii de acceptare

Corectitudine: copii/resize/recompresie, crop/screenshot cu fundal/text, watermark, fotografii diferite dar foarte asemănătoare, rafale, uniforme/negre/albe, transparență, orientare EXIF, rotații/mirror, 1×1, imagini corupte, formate neactivate, fișiere schimbate la citire, Unicode, căi lungi, root-uri suprapuse și unități externe.

Orientarea normalizată/profilul trebuie să rămână compatibile cu amprentele salvate. Rotația/mirror neacoperite de profil se documentează; un format sau transform nu este declarat suportat doar prin adăugarea extensiei.

Grupare: A~B~C dar A!~C; un candidat apropiat de două familii; grupuri cu mai mulți membri decât TopN/preview; același hash pentru mii de imagini; reprezentant dispărut; excludere persistentă; rezultat identic indiferent de workers/ordinea descoperirii.

Performanță: 10k imagini reale etichetate, apoi 100k/500k/1M hash-uri realiste și seturi adversariale. Măsurăm separat indexare rece, reindexare incrementală, construire index, analiză, publicare, primul ecran și scroll. Working set/peak RAM, alocări, GC, CPU, I/O, candidați/perechi, recall/precizie și grupuri false trebuie raportate. Nu se aleg praguri pe același set pe care se declară calitatea finală.

Ținte inițiale: UI fără blocare de procesare; feedback vizibil imediat, anulare observată în aproximativ o secundă în etapele controlate de noi; scroll cu 1.000 de grupuri sintetice fără încărcarea tuturor membrilor. Duratele totale pe biblioteca utilizatorului sunt stabilite numai după benchmark, nu promise acum. Operațiile OS/decoder pot întârzia anularea și sunt raportate.

Poarta de implementare a motorului: pe seturi mici, candidații MIH dau aceleași perechi ca baseline-ul exhaustiv pentru raza configurată; pe seturi mari, plafonul de memorie se respectă sau analiza oprește explicit ca incompletă. Nicio pierdere ascunsă prin TopN, loturi, cap pe bucket sau praguri de metadate nepotrivite profilului.

## 10. Ordinea de implementare

### Progres verificat al implementării

- Prima versiune funcțională este integrată în tabul WinUI „Imagini similare” / „Find similar”. Unul sau mai multe dosare, cu subdosare; opțional toate bibliotecile active indexate compatibile.
- `SimilarityGroupingService` folosește postări compacte pe patru segmente pentru fiecare dintre pHash și dHash. Profilul verificat admite candidați cu pHash <=3 SAU dHash <=3, apoi SHA-256 sau RGB normalizat. Raza 3 este acoperită exact pe fiecare metrică; nu este o garanție că toate fotografiile vizual apropiate intră în raza aleasă.
- Testul de recomprimare a demonstrat că filtrul inițial strict pHash+dHash pierde o variantă JPEG. În UI, scorul final este acum experimental `100*(1-RMS_RGB_normalizat)` pentru miniaturi 32×32, respectiv 100 pentru conținut binar identic verificat. Aspect ratio în limita 2%; imagini cu variație spațială foarte mică acceptate numai cu dovadă binară. Nu este procent de pixeli identici/probabilitate. Baseline-ul headless fără verificator păstrează formula inițială doar pentru testarea distanțelor.
- Bugete explicite: maximum 1.000.000 de intrări compatibile, 128.000.000 de vizite de postări. Depășirea oprește operația fără publicarea unor grupuri trunchiate. Alocările singleton inutile și re-parcurgerea prefixelor deja vizitate au fost eliminate.
- SQLite: vector coerent de generații, publicare tranzacțională, schemă de grupare v2 cu migrare v1, cache SHA-256 versionat, păstrarea numai a celei mai recente analize complete. Rezultatele vechi sunt invalidate la reindexare/resetare; această versiune nu oferă reluare la restart.
- UI: maximum 50 grupuri și 50 membri materializați pe pagină, selecție globală independentă de paginare, cel mai bun exemplar implicit, selecție inversă, comparație alăturată, copiere fără overwrite și rapoarte CSV/HTML în flux. Nu există acțiuni de ștergere/mutare automată în acest tab.
- Verificări: teste headless și integrare SQLite reală pentru copii, resize, recomprimare, uniforme, generații, rollback, surse schimbate, migrare, rapoarte și selecție; capturi Light/Dark, RO/EN, schimbare live și paginare pe 1.000 de grupuri sintetice.
- Raportul verificărilor și limitele rămase: [FIND_SIMILAR_IMPLEMENTATION_2026-10-07.md](FIND_SIMILAR_IMPLEMENTATION_2026-10-07.md).
- Încă deschise: calibrare pe o colecție reală etichetată, peak RAM și durată completă pe biblioteci mari, alternative de apartenență ambiguă/excluderi persistente, checkpoint/pauză/relua­re, partiționare pentru scopuri peste buget. Crop/semantic nu sunt activate în acest tab. Alegerea manuală a altui exemplar de păstrare se face prin selecție; nu schimbă reprezentantul care definește familia.

1. Confirmarea celor trei alegeri de produs și definirea `SimilarityGroupingOptions`, tipurilor de dovezi și regulii de apartenență. Fixture-uri de grupare înaintea algoritmului.
2. Motor headless pentru amprente egale și variante apropiate; baseline exact mic, prototip MIH, validare și benchmark. Alegerea strategiei abia după măsurare.
3. Persistență, migrare, snapshot-uri coerente, bugete, anulare/checkpoint și publicare. Validare pe SQLite real, inclusiv fault injection.
4. Tab WinUI virtualizat, încărcare paginată, comparație și revizuire, cu Light/Dark și schimbare de limbă live. Nu reconstruim engine-ul pentru aceste schimbări.
5. Copiere/rapoarte/confirmări conform alegerii utilizatorului; teste de coliziuni, versiuni de sursă, anulare și rapoarte parțiale.
6. Profil crop/screenshot numai după index regional de candidați și teste de calitate/performanță. Asemănarea semantică rămâne proiect opțional distinct dacă este cerută.
7. Test pe colecție reală controlată, gate UX și build/pachet standalone nou. Funcția nu este declarată completă numai pentru că tabul este vizibil.

Task-urile repetitive pot fi asistate de Ollama în dezvoltare după stabilirea contractelor: fixture-uri, texte RO/EN, cazuri de test și cod UI restrâns. Algoritmul, acoperirea candidaților, siguranța fișierelor și verificarea finală rămân revizuite direct. Nu lansăm zece agenți simultan fără sarcini independente și fără nevoie reală.
