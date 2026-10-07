# Dark Mode și preferințe instantanee

## Utilizare

În **Setări → General → Aspectul aplicației**, alege Light sau Dark. În **Limba interfeței**, alege Română sau English. Ambele se aplică imediat și sunt păstrate la următoarea pornire. Nu sunt necesare module, descărcări sau servicii online pentru aceste funcții.

Preferințele sunt salvate atomic separat în `%LOCALAPPDATA%\PerceptoX\theme.json` și `language.json`. Fișierele prea mari, invalide ori prin linkuri sunt respinse; tema revine la Light dacă preferința nu este validă. În caz de eroare la salvare, selectorul revine la valoarea activă și afișează eroarea. Setări nu sunt accesibile în timpul unui dialog modal, deci o confirmare activă nu este înlocuită sau aprobată implicit.

## Implementare

- Palete Light/Dark în `graphics/PerceptoX.Theme.xaml`, cu chei identice și referințe `ThemeResource` pentru suprafețe, texte, linii, selecție, hover, avertismente și scoruri.
- Pagini, cinci taburi de Setări, flyout-uri, carduri, câmpuri și dialoguri folosesc aceeași paletă. Butoanele accent au fundal albastru și text alb, inclusiv în dialoguri și stările hover/pressed; dezactivarea rămâne distinctă.
- `ThemeManager` modifică `RequestedTheme` pe rădăcina ferestrei existente. Bara de titlu primește explicit culorile active, inactive, hover și pressed. Nu se schimbă tema Windows.
- Grafica transparentă este reutilizată; în Dark, panglicile laterale sunt discrete, wordmark-ul are text luminos și păstrează simbolul X din logo-ul existent. Nu sunt adăugate imagini generate sau dependențe grafice noi.
- Localizarea XAML folosește binding-uri observabile per cheie. Mesajele curente din ViewModel rețin renderer-ul și argumentele necesare reformatarei, nu un istoric al evenimentelor de scanare. Shell-ul actualizează prezentarea fără să recreeze engine-ul.
- Dosarele, pragurile, rezultatele și selecțiile sunt păstrate. Schimbarea nu pornește o nouă scanare, nu modifică indexul și nu operează asupra fotografiilor. Cultura globală a engine-ului nu este schimbată. Căile și mesajele tehnice brute rămân ca atare.
- Catalogul JSON este încărcat în memorie; o limbă deja descoperită se activează imediat. Adăugarea unui fișier nou în `languages/` necesită încă o repornire pentru descoperire, nu pentru schimbarea între limbile disponibile.
- Abonamentele ferestrei la schimbarea temei și limbii sunt eliberate la închidere. Instrumentarea de captură este exclusă din Release și nu salvează preferințele utilizatorului.

## Verificări

Build Debug Windows x64: 0 avertismente, 0 erori. Teste: Core 31, Application 25, Infrastructure 144, Presentation 55 — **255 trecute, 0 eșecuri, 0 ignorate**. Cele cinci teste noi verifică notificarea catalogului, înlocuirea mesajului curent, reformatarea fragmentelor, păstrarea stării și fallback-ul unei a treia limbi.

Validatorul limbilor: 374 chei RO/EN, referințe UI și parametri validați. `eng/validate-theme.ps1` verifică paritatea paletelor, eliminarea referințelor statice la culorile principale și contrastul de minimum 4,5:1 pentru perechile de text normal selectate. Contrastul Dark: text principal/card 13,22:1; text secundar/card 7,24:1; text accent/card 6,90:1. Nu reprezintă certificare completă de accesibilitate.

Testul XAML in-process schimbă Light/RO → Dark/EN → Light/RO → Dark/RO prin controalele din Setări. Verifică aceeași rădăcină și aceeași pagină de Setări, aceleași 1.000 de rezultate, aceleași 1.000 de selecții și aceleași căi. Capturile inițiale și `live-switch.json` sunt în `artifacts/dark-live-20261006`; verificarea finală după armonizarea butoanelor este în `artifacts/dark-live-accepted-20261006`.

Capturile folosesc date sintetice. Nu certifică acuratețea căutării, comportamentul tuturor interacțiunilor reale de mouse/tastatură sau randarea nativă a caption buttons în toate mediile. Culorile caption buttons sunt înregistrate ca diagnostic în testul de schimbare live. Distribuția publică/licențele terțe și verificarea pe Windows curat rămân gate-uri distincte.

## Referințe de implementare

Schimbarea runtime folosește mecanismul WinUI documentat pentru [ThemeResource](https://learn.microsoft.com/en-us/windows/apps/develop/platform/xaml/themeresource-markup-extension) și [ThemeDictionaries](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.resourcedictionary.themedictionaries). Binding-urile și stările native ale butoanelor au fost verificate și față de `generic.xaml` din pachetul WinUI folosit de proiect.

## Livrare verificată

Pachetul nou: `artifacts/distribution/20261006-175540-918effa1/PerceptoX-win-x64.zip`, 120.529.675 bytes, SHA-256 `4BE50A48E5C4CFF1EF645A2C61E587697E2CD09A32BEE021011A247A673C73FB`. Include versiunea Release x64, cataloagele JSON și resursele Light/Dark. Sursele proprii și materialele terțe sunt în aceeași livrare.

Verificarea pachetului extras într-o cale nouă cu spații a trecut: `StartupPassed`, cu cele cinci DLL-uri de runtime verificate app-local. Raport: `artifacts/validation/dark-live-20261006-918effa1/startup/startup-result.json`. Rețeaua nu a fost dezactivată, iar calculatorul nu este un Windows curat.

Capturile finale au fost inspectate: paginile principale, cele cinci taburi și dialogurile comparație/progres/module. Șapte verificări numerice ale cardurilor din Setări și trei verificări ale centrajului dialogurilor/acțiunii unice au trecut, cu abatere maximum 1 px. Lista sintetică de 1.000 de rezultate a realizat maximum 11 containere în verificarea de scroll; aceasta demonstrează virtualizarea în acest scenariu, nu certifică performanța pe toate configurațiile.

Această secțiune este evidență post-build; arhiva de surse conține raportul înainte de completarea cu identificatorul ZIP și rezultatul pornirii. Livrările precedente nu au fost înlocuite.
