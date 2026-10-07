# Limbi și texte în PerceptoX

PerceptoX regăsește originalele imaginilor într-o bibliotecă locală. Referințele pot fi miniaturi, decupaje, capturi de ecran sau alte versiuni ale fotografiilor. Utilizatorul verifică potrivirile alăturat și poate copia originalele selectate ori exporta un raport. Scorurile sunt estimări de similaritate, nu probabilități și nu autorizează operații automate asupra fotografiilor.

## Schimbarea limbii

În **Setări → General → Limba interfeței**, selectează limba. Schimbarea este imediată, fără restart. Româna și engleza sunt incluse. Se schimbă textele paginilor, acțiunilor și mesajele localizate; dialogurile deschise ulterior folosesc limba selectată. Nu se schimbă pragurile, profilurile de procesare, indexul sau fișierele fotografiilor.

Preferința este salvată atomic în `%LOCALAPPDATA%\PerceptoX\language.json`, apoi catalogul din memorie devine activ. Binding-urile XAML și mesajele din ViewModel se actualizează în aceeași fereastră, fără reconstruirea engine-ului și fără pierderea rezultatelor sau a selecțiilor. În timpul unui dialog modal, Setări nu sunt accesibile: o confirmare activă nu este înlocuită sau acceptată prin schimbarea limbii. Nu sunt necesare servicii online, Ollama sau un SDK pentru localizare.

## Adăugarea unei limbi

1. Copiază `languages/en.json` din folderul aplicației într-un fișier nou, de exemplu `languages/fr.json`.
2. Modifică `language` la `fr` și `displayName` la `Français`.
3. Tradu valorile din `strings`, **fără a schimba cheile**.
4. Păstrează parametrii precum `{0}`, `{1}`, `{0:F2}`, spațiile necesare fragmentelor concatenate și secvențele JSON pentru linii noi (`\n`). Pentru acolade literale în texte formatate folosește `{{` și `}}`.
5. Repornește PerceptoX. Limba apare în selector, fără recompilarea aplicației.

Structură minimală validă (o traducere parțială, nu un catalog complet):

```json
{
  "schemaVersion": 1,
  "language": "fr",
  "displayName": "Français",
  "strings": {
    "MainWindow.Text004": "Paramètres",
    "Common.On": "Activé",
    "Common.Off": "Désactivé"
  }
}
```

Cheile lipsă folosesc româna inclusă în aplicație. Dacă nici catalogul de rezervă nu conține o cheie, este afișată cheia, ca diagnostic. O referință de parametru invalidă într-o traducere personalizată revine la textul românesc, fără să întrerupă scanarea.

Folosește un cod de limbă/cultură recunoscut de .NET, cu litere și cratime, de exemplu `en`, `fr`, `de`, `pt-BR`. O singură traducere per cod de limbă; fișierele cu același cod nu trebuie duplicate.

## Validare și limite

- Schema este versiunea 1. Cheile trebuie să fie unice, iar valorile șiruri JSON.
- La pornire sunt citite cel mult 100 de fișiere JSON din `languages/`, fiecare de maximum 2 MB. Linkurile simbolice/junctions sunt respinse.
- Catalogul este încărcat o singură dată; procesarea imaginilor nu citește JSON de pe disc pentru fiecare rezultat.
- Catalogul acceptă maximum 10.000 de texte, chei de maximum 160 de caractere și valori de maximum 8.192 de caractere. Numele limbii are maximum 80 de caractere.
- Un pachet invalid este ignorat și apare o notificare în General. Româna și engleza încorporate rămân disponibile dacă fișierele externe lipsesc sau nu sunt valide.
- Ferestrele native Windows pentru alegerea fișierelor/directoarelor folosesc limba sistemului. Numele fișierelor, codurile tehnice, extensiile și mesajele brute ale excepțiilor/codec-urilor nu sunt traduse automat. CLI-ul și rapoartele exportate nu fac parte din această localizare a interfeței.
- Aspectul poate fi Light sau Dark, selectat instantaneu în General. Traducerile complete în limbi cu scriere de la dreapta la stânga necesită și verificarea/adaptarea layout-ului; simpla adăugare a JSON nu certifică un layout RTL.

## Pentru dezvoltatori

`PerceptoX.Presentation.Localization` conține catalogul și `UiText.T(key, args)`. WinUI folosește extensia `UiTextExtension` pentru XAML și `LanguageManager` pentru descoperire/preferințe. Catalogul românesc și cel englezesc sunt resurse încorporate și sunt copiate în pachetul standalone. Cultura globală a engine-ului nu este modificată.

`UiTextExtension` furnizează un binding către o intrare observabilă per cheie. `LocalizedViewModel` păstrează numai renderer-ul mesajului curent pentru fiecare proprietate, pentru a reformata contoarele și fragmentele la schimbarea limbii. Mesajele tehnice brute și căile sunt păstrate ca atare. `ShellViewModel.RefreshLanguage()` reîmprospătează prezentarea; nu resetează operații, rezultate sau setări. Abonamentele ferestrei la evenimentul de limbă sunt eliberate la închidere.

La adăugarea unui text, introdu aceeași cheie în ambele cataloage, apoi folosește `UiText.T(...)` sau `{loc:UiTextExtension Key=...}`. Nu redenumi cheile existente doar pentru a îmbunătăți formularea; schimbă valorile. Evită concatenarea propozițiilor și preferă parametri în aceeași traducere.

```powershell
./eng/validate-languages.ps1
./eng/dotnet-sandbox.ps1 -DotNetArguments @('test', 'tests/PerceptoX.Presentation.Tests/PerceptoX.Presentation.Tests.csproj', '--no-restore')
```

Validatorul de livrare verifică egalitatea cheilor RO/EN, numărul argumentelor și referințele UI. Testele verifică parserul, fallback-ul, formatarea culturală, parametrii, respingerea intrărilor invalide și o a treia limbă. `extract-ui-text.ps1` a fost folosit doar pentru migrarea inițială și nu trebuie rerulat peste cataloagele existente. `translate-ui-catalog.ps1` este un instrument opțional de dezvoltare pentru traduceri asistate de Ollama; rezultatele trebuie revizuite înainte de livrare.
