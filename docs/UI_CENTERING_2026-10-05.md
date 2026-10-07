# PerceptoX — corecții de centraj și acțiuni UI

## Modificări

- Toate cele cinci taburi din Setări folosesc același container de carduri, cu lățime legată de viewport, maximum 900 px și aliniere centrală. Textele nu mai determină poziții orizontale diferite; conținutul lung rămâne accesibil prin scroll vertical.
- Limita de lățime a dialogurilor este aplicată cardului intern (`ContentDialogMaxWidth`), nu întregului control modal. Suprafața modală ocupă întregul XamlRoot, astfel încât „Caută / Scanează” și comparația sunt centrate față de aplicație, inclusiv bara laterală.
- Acțiunea unică a unui dialog este centrată, păstrând butonul nativ WinUI, focusul, evenimentele, anularea și comportamentul existent de închidere. Alinierea se aplică după materializarea template-ului și după recalcularea dimensiunii cardului. Abonamentele temporare sunt eliberate la închidere.
- „Răsfoiește” are fundal albastru și text/icon alb în potrivirea dosarelor, indexare și căutarea unei imagini.
- Selectorul lupei este înlocuit cu două RadioButton, x2 implicit și x4, pe aceeași linie cu „Lupă sincronizată”, într-o bară discretă centrată. Schimbarea selecției folosește aceeași logică a lupei; încărcarea imaginilor nu se repetă la schimbarea factorului.
- A fost localizat și mesajul informativ privind pragurile experimentale din Setări. Cataloagele RO/EN au 368 de chei, cu aceiași parametri.

## Verificări

Build Debug x64: 0 avertismente, 0 erori. Teste Core 31, Application 25, Infrastructure 144, Presentation 50: 250 trecute, 0 eșecuri, 0 ignorate. Validatorul cataloagelor a trecut.

Modul Debug de captură măsoară pozițiile din arborele XAML real și salvează `settings-layout.json`/`dialog-layout.json`, pe lângă PNG. Capturile finale sunt în `artifacts/ui-centering/accepted-*`. La suprafața verificată de 1424 × 891 px, cardurile taburilor au eroare orizontală 0 px; dialogurile au eroare orizontală 0 px și verticală de maximum 0,5 px. Acțiunea unică are abatere de maximum 1 px din layout-ul nativ. Bara lupei este centrată în conținut.

Capturile folosesc imagini/rezultate sintetice și nu execută instalări, curățări sau mutări ale fotografiilor utilizatorului. Instrumentarea este exclusă din Release. Aceste verificări de randare nu certifică toate interacțiunile reale de mouse/tastatură, un Windows curat sau redistribuirea publică a dependențelor.

## Livrare verificată

Pachet: `artifacts/distribution/20261005-124937-772ed42a/PerceptoX-win-x64.zip`, 120.519.264 bytes. SHA-256: `4D0C668464563F314A60B4835267388C29871E80E04553C2F055CBB53A53249B`.

Sursele proprii și materialele terțe sunt în aceeași livrare. Toate cele opt verificări numerice ale capturilor finale au trecut. Pachetul extras într-o cale nouă cu spații a trecut verificarea `StartupPassed` prin Windows PowerShell 5.1; cele cinci DLL-uri de runtime verificate sunt app-local. Raport: `artifacts/validation/ui-centering-20261005-124937/startup/startup-result.json`. Rețeaua nu a fost dezactivată și calculatorul nu este un Windows curat.

Această secțiune este evidență post-build; arhiva de surse conține raportul înaintea completării cu identificatorul pachetului și rezultatul pornirii.
