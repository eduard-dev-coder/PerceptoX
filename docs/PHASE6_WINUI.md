# Faza 6 — MVVM și WinUI 3

## Stare

MVP-ul desktop este implementat ca aplicație WinUI 3 unpackaged pentru x64. Build-ul trece fără avertismente, toate cele 86 de teste ale soluției trec, iar executabilul rămâne activ într-un smoke test de cinci secunde și se închide controlat.

Gate-ul de inginerie este trecut pentru separarea MVVM și threading. Profilarea reproductibilă cu 1.000 de rezultate trece în Light și Dark la scala curentă 100%, inclusiv virtualizarea și deplasarea focusului între carduri. Gate-ul UX rămâne parțial până la verificarea manuală completă cu tastatura și Narrator și la testarea DPI 150%/200%.

## Structură

- `PerceptoX.Presentation` conține contractele UI și ViewModels testabile fără WinUI;
- `PerceptoX.WinUI` este composition root-ul desktop și conține Views, pickerele și adaptorul către motor;
- `DesktopPerceptoXWorkflow` conectează ViewModels la `ImageIndexer`, SQLite și serviciul de matching;
- Views nu cunosc ImageSharp, SQLite sau algoritmii de hashing;
- code-behind-ul este limitat la navigare, asocierea DataContext și lifecycle-ul ferestrei.

## Fluxuri disponibile

### Indexare

1. Utilizatorul alege biblioteca prin `FolderPicker` desktop inițializat cu HWND.
2. Căile implicite pentru SQLite și cache sunt sub `%LOCALAPPDATA%\PerceptoX` și în afara bibliotecii.
3. Comanda asincronă rulează întregul workflow CPU/I/O pe worker thread.
4. Anularea păstrează ultimul index publicat, conform tranzacției existente din motor.

### Căutare similară

1. Utilizatorul alege o imagine activă din index.
2. Snapshot-urile compacte pHash+dHash și multi-region sunt încărcate pe worker thread.
3. UI primește numai detaliile candidaților finali și URI-uri validate din cache.
4. `GridView` folosește `ItemsWrapGrid`; imaginile sunt decodate la maximum 256 px.
5. Scorurile sunt marcate explicit ca necalibrate și nu declanșează acțiuni distructive.

## Threading și lifecycle

- `IndexingViewModel` și `SearchViewModel` expun comenzi asincrone și anulare;
- adaptorul desktop folosește `Task.Run` la granița către motor, astfel încât enumerarea, decodarea, SQLite și matching-ul nu pornesc pe UI thread;
- actualizarea `ObservableCollection` se face după revenirea pe contextul UI;
- închiderea ferestrei face `Dispose` pe ViewModels și anulează operațiile active;
- procesorul ImageSharp este eliberat după fiecare operație de nivel înalt.

## Siguranța miniaturilor

Calea relativă persistată este combinată cu rădăcina cache-ului și normalizată. Orice cale absolută, traversal în afara rădăcinii sau fișier lipsă produce un thumbnail nul, nu acces arbitrar la filesystem. UI-ul nu încarcă originalele pentru rezultate.

## Build și rulare

```powershell
& .\eng\dotnet-sandbox.ps1 restore src\PerceptoX.WinUI\PerceptoX.WinUI.csproj --configfile .\NuGet.Config
dotnet build src\PerceptoX.WinUI\PerceptoX.WinUI.csproj -p:Platform=x64 --no-restore
& .\src\PerceptoX.WinUI\bin\x64\Debug\net10.0-windows10.0.19041.0\PerceptoX.WinUI.exe
```

Prima restaurare a `Microsoft.WindowsAppSDK` poate necesita aprobarea rețelei în afara sandboxului din cauza erorii TLS/SSPI observate pe acest computer. Nu este necesar accesul la configurația NuGet personală.

## Delegare locală

Harness-ul `eng/ollama-delegate.ps1` verifică mai întâi catalogul local și refuză clar modelele lipsă. Pentru Ollama clasic, serviciul de pe `http://127.0.0.1:11434` are disponibile `qwen2.5-coder:14b` și `qwen2.5-coder:32b`. Delegarea a fost dovedită cu `qwen2.5-coder:14b`, care a răspuns corect la un test scurt.

DeepSeek Harness este detectat pe `http://127.0.0.1:3080`, dar endpoint-ul cere autorizare. Runner-ul acceptă acum `-BaseUri 'http://127.0.0.1:3080'` și token prin `PERCEPTOX_LOCAL_AI_API_KEY` sau `-ApiKey`. Nu se descarcă modele automat și nu se trimit fișiere către harness până când autentificarea este configurată explicit.

## Lucru rămas pentru gate-ul complet

1. Test de tastatură complet pentru navigare, pickere și toate comenzile; focusul direcțional în rezultate este deja verificat.
2. Narrator a fost eliminat explicit de utilizator din cerințe.
3. DPI 150% și 200% au fost eliminate explicit de utilizator; Light/Dark la 100% au fost profilate pentru UI-ul anterior. Redesignul curent vizează Light conform referinței UI.png.
4. Captură ETW XAML opțională după remedierea politicii WPR a sistemului (`0xc5585011`); profilul intern produce între timp măsurători reproductibile.
5. Adăugarea progresului numeric numai după ce motorul expune raportare bounded fără contendență.

`ItemsRepeater` rămâne o optimizare condiționată de profiling. `GridView` este păstrat pentru MVP deoarece oferă virtualizare, selecție și accesibilitate integrate.

Rezultatele, comenzile și limitele măsurătorii sunt documentate în `docs/PHASE6_UX_VALIDATION.md`.
