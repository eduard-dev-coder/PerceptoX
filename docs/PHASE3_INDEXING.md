# Faza 3 — SQLite și indexare incrementală

Actualizare 2026-10-03: schema curentă este v2 (migrare v1→v2 fără pierderea hash-urilor),
cu ScanRuns.DiscoveryComplete. Miniaturile lipsă nu mai forțează rehash; sunt regenerate
la cerere în workflow-ul desktop. Problemele locale de acces sunt raportate, iar o
enumerare incompletă păstrează intrările nevăzute. Descrierea istorică de mai jos pentru
v1 este completată de [incrementul de întreținere](PHASE7_MAINTENANCE.md).

Faza 3 păstrează indexul activ separat de scanarea în curs. `ImageIndexer` enumeră fișierele suportate, pregătește loturi de câte 64 de identități, trimite doar fișierele schimbate prin două canale bounded (capacitate 128) către procesor și writer, apoi publică rezultatele într-o singură tranzacție SQLite. Procesorul ImageSharp are propria limită de workeri și memorie decodată estimată. `SqliteIndexStore` folosește o singură conexiune de scriere, serializată, iar `SqliteIndexReader` deschide conexiuni separate de citire. [Documentația Microsoft.Data.Sqlite privind concurența](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/database-errors) recomandă să nu fie folosite simultan aceleași obiecte de conexiune.

## Persistență și publicare

Migrația explicită v1 folosește `PRAGMA user_version`, fără ștergere automată la o versiune necunoscută. Schema separă `LibraryRoots`, `ScanRuns`, `ImageIdentities`, `Images`, `ImageFingerprints`, `Thumbnails`, `ScanSeen`, tabelele `Pending*` și `ScanErrors`; `Settings` și `SearchHistory` sunt pregătite pentru faze ulterioare. Fingerprint-urile sunt BLOB-uri cu lungime explicită, deci nu presupun numai hash-uri de 64 biți. Cheile compuse din staging folosesc `WITHOUT ROWID`; cheia numerică `Images.Id` rămâne rowid normal, în acord cu [recomandările SQLite](https://www.sqlite.org/withoutrowid.html).

La începutul scanării se rezervă identitățile de cale și se marchează căile văzute. Schimbările și erorile sunt persistate în staging; cititorii văd în continuare doar indexul activ. La succes, o tranzacție aplică fișierele schimbate, înlocuiește hash-urile și miniaturile lor, dezactivează fișierele nevăzute și mută `LastCompletedScanId`. Anularea sau o eroare de enumerare/publicare lasă active datele anterioare; o scanare rămasă `running` după un crash este marcată `abandoned` și staging-ul său este curățat la următoarea scanare. Un lock de fișier lângă DB împiedică doi writeri în procese diferite. [Tranzacțiile SQLite prin Microsoft.Data.Sqlite](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions) oferă unitatea atomică de publicare.

`WAL` și `synchronous=NORMAL` sunt folosite pentru un index local regenerabil. Tranzacțiile rămân atomice, dar o pierdere bruscă de alimentare poate pierde ultima tranzacție confirmată; acest compromis nu este potrivit pentru date neregenerabile. [SQLite PRAGMA](https://www.sqlite.org/pragma.html), [WAL](https://www.sqlite.org/wal.html).

## Identitate și incrementalitate

`WindowsPathKey` v1 aplică cale absolută, Unicode NFC și majuscule invariant-culture. Nu se bazează pe `COLLATE NOCASE` din SQLite pentru identitatea Windows. O coliziune de cheie în aceeași scanare produce eroare și împiedică publicarea, nu fuzionează silențios două fișiere. Aceasta este o politică versionată, nu o echivalență completă cu toate particularitățile NTFS (de exemplu aliasuri 8.3 sau directoare case-sensitive); înaintea suportului pentru biblioteci speciale trebuie testată pe corpusul țintă.

Un fișier este considerat neschimbat când mărimea, `LastWriteTimeUtc`, profilul de procesare și existența miniaturii coincid. Profilul este derivat din descriptorii algoritmilor și profilul miniaturii; o schimbare de algoritm declanșează reindexarea. Mărime + timestamp pot rata o înlocuire care păstrează deliberat ambele valori; o politică de verificare prin conținut/USN poate fi adăugată ulterior. Dacă procesarea unui fișier eșuează, vechea intrare activă rămâne disponibilă, iar eroarea este înregistrată sumar; snapshot-ul de căutare filtrează strict profilul curent pentru a nu amesteca hash-uri incompatibile.

Rădăcina bibliotecii trebuie să existe și să nu fie reparse point. Enumerarea ignoră reparse points și nu ignoră erorile de acces: o traversare incompletă nu poate dezactiva fișiere ca „dispărute”. Baza de date și cache-ul de miniaturi trebuie să fie lexical în afara rădăcinii indexate, astfel încât indexerul să nu-și reindexeze propriile rezultate. Protecția împotriva schimbării de tip reparse între verificare și deschidere rămâne de întărit înainte de operații de fișiere sau de scanări pe surse ostile.

`SqliteIndexReader.LoadSnapshot(root, profile)` citește într-o tranzacție o structură compactă cu `ImageId`, pHash și dHash de 64 biți, ordonată după ID. Nu materializează căi, thumbnail-uri sau restul metadatelor. În Faza 4, strategia de matching va consuma acest snapshot sau o extensie versionată a lui; snapshot-ul nu este încă un motor de căutare.

## Comenzi și verificări

```powershell
& .\eng\dotnet-sandbox.ps1 run --no-build --project src\PerceptoX.Cli\PerceptoX.Cli.csproj -- index C:\Imagini D:\PerceptoX\index.db D:\PerceptoX\thumbs
& .\eng\dotnet-sandbox.ps1 run --no-build --project src\PerceptoX.Cli\PerceptoX.Cli.csproj -- status C:\Imagini D:\PerceptoX\index.db
```

Pe setul procedural de 10.000 de imagini, indexarea completă a produs 10.000 de intrări active, 10.000 de miniaturi și zero erori. Scanarea următoare a raportat `unchanged=10000, processed=0`; snapshot-ul a încărcat 10.000 de intrări. Testele folosesc baze SQLite reale și acoperă anularea, staging-ul invizibil, rollback-ul prin fault injection, reluarea după crash simulat, fișierele dispărute, profilurile incompatibile, identitatea căilor și constrângerile FK. Acesta este un test funcțional la volum, nu un benchmark pentru un milion de imagini sau un test de recall.

Dependența este `Microsoft.Data.Sqlite` 10.0.12. Varianta 10.0.9 a fost respinsă de auditul NuGet din cauza unei dependențe `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 raportate ca vulnerabile; restore-ul cu 10.0.12 și audit activ a reușit. [Advisory](https://github.com/advisories/GHSA-2m69-gcr7-jv3q), [pachetul 10.0.12](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.12).

Deschise: profilare și integritate la 100k–1M imagini reale, verificarea unui cache corupt, curățarea miniaturilor orfane, fișiere modificate cu același mărime/timestamp, hardening pentru schimbări de reparse point în cursul scanării și politica de durabilitate pentru release.
