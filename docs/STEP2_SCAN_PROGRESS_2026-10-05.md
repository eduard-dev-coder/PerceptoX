# Pasul 2 — dialog Caută / Scanează

## Implementat

- Butonul principal deschide un ContentDialog dedicat; motorul este pornit din
  evenimentul Opened, astfel încât și o operație instantanee are o stare finală inspectabilă.
- Etape: descoperire, decodare/calcul pHash/dHash/miniaturi, staging SQLite,
  publicare SQLite, încărcare index, căutare și pregătire rezultate.
- Numele fișierului, contoare procesate/rămase când totalul este cunoscut,
  erori/nesuportate/inaccesibile, staging, timp scurs și ETA estimativ.
- Total necunoscut: bară nedeterminată și ETA indisponibil, fără procent inventat.
  Total cunoscut: bară determinată și ETA bazat pe ritmul fazei, resetat între
  originale/referințe. Discovery și căutarea enumeră în streaming; totalul exact
  nu este cunoscut înainte de terminarea enumerării. Nu se face un pre-scan sau
  o listă suplimentară cu toate fișierele doar pentru progres.
- Procesare paralelă: numele afișat reprezintă unul dintre lucrători, nu o ordine
  globală fictivă. Valorile din staging nu sunt numite date deja publicate.
- Anulare cere CancellationToken și rămâne în „Anulare în curs” până când
  backend-ul s-a oprit/curățat; nu declară rollback finalizat doar la click.
- Succes/anulare/eroare: sumar, progres/fișier curent ascunse, buton OK.
- Escape nu închide o scanare activă. La Unloaded se cere anulare, iar dialogul
  este eliberat; la închidere/finally timerul și handler-ele sunt detașate.

## Frecvență și lifecycle

Backend-ul raportează periodic la 250 ms. ScanProgressViewModel păstrează o singură
stare recentă, thread-safe; rapoartele worker-ilor nu trimit evenimente PropertyChanged
sau elemente individuale în dispatcher. Dialogul consumă starea la 250 ms pe UI.
Granițele de etapă, anularea și starea finală au actualizare imediată.
Timerul UI se oprește la final, iar rapoartele întârziate sunt ignorate.
Fiecare scanare are o sesiune nouă; o scanare veche nu poate actualiza dialogul următor.

## Siguranță SQLite

PrepareBatch și StageBatch verifică anularea înainte/în buclă/înainte de commit;
tranzacția nefinalizată este rollback prin Dispose. Indexer-ul abandonează staging-ul
fără a șterge snapshot-ul activ anterior. Publish verifică tokenul înainte de commit.
După commit, anularea căutării nu încearcă să „anuleze” datele deja publicate.

## Verificări

Rezultat: **224 teste trecute**, zero skipped — Core 31, Application 25,
Presentation 28, Infrastructure 140. Build WinUI fără warnings/erori;
publish Release standalone reușit și pornire din ZIP relocat în PowerShell 5.1
cu runtime-uri .NET/WinUI app-local. Au fost adăugate 15 teste față de livrarea pasului 1.

- Burst de 10.000 de rapoarte: zero notificări UI până la Refresh, apoi o singură
  actualizare cu starea cea mai recentă; fără coadă de 10.000 callbacks.
- ETA/fază, total necunoscut, stări finale înghețate, late progress, anulare cu
  backend cleanup întârziat, eroare și Dispose/unsubscribe.
- SQLite real: anulare deterministă după inserările primului fișier într-o
  tranzacție de staging → zero rânduri pending după rollback.
- Anulare la publicare → snapshot/scan anterior păstrat și staging eliminat;
  o scanare ulterioară funcționează.
- Anulare după commit → index nou păstrat și ScanRuns completed.
- Progres cu fișier/total/staging și niciun callback timer după finalizare;
  progresul fluxului desktop real și sumar cu fișier corupt.

Interacțiunea umană cu dialogul (click Anulare, Escape, OK, închiderea ferestrei)
nu este certificată de testele ViewModel/SQLite sau de simpla compilare WinUI.
Verificați aceste acțiuni pe copii ale imaginilor, inclusiv o scanare foarte rapidă
și una suficient de lungă pentru a observa ETA/progresul. Gate-ul separat Windows
curat/licențe/update din pasul 1 rămâne neschimbat.

## Livrare

Pachet: `artifacts/distribution/20261005-052829-894da9cc/PerceptoX-win-x64.zip`
(120.464.797 bytes).
SHA-256: `5705AF5213E7DC7E7D94EE7C7531C046E088B35090097ADD408FC99AB0F5A465`.
Sursele proprii/terțe și kitul de acceptare sunt alături, în același director.
Raport startup: `artifacts/validation/20261005-step2-startup/startup-result.json`.
Pornirea nu certifică interacțiunea cu dialogul. Paragraful de rezultate/livrare
a fost adăugat după publicare și nu schimbă sursele sau binarele aplicației.

Ollama: Gemma 31B local a eșuat la inițializarea contextului; fallback-ul local
`gemma4:latest` a furnizat cinci cazuri de lifecycle/anulare pentru revizuire.
Niciun model cloud nu a fost folosit; ideile nu sunt tratate drept teste executate.
