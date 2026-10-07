# PerceptoX — armonizare UI și localizare, 5 octombrie 2026

## Implementat

- Bara de module a fost mutată în partea de jos, pe o singură linie: bulină de stare, etichetă, sumar și acțiune. Un text lung este trunchiat vizual și poate fi citit integral în tooltip.
- Paginile, dialogurile de progres/comparație/module, confirmările și flyout-urile folosesc tema PerceptoX: fundal albastru foarte deschis, carduri albe, borduri discrete, colțuri rotunjite și aceleași stiluri de acțiune. Dialogurile native Windows rămân native.
- Informațiile dialogurilor sunt grupate în carduri: activitate/procesare/timp/sumar, runtime/decoder/acord sau imagini/metadate/instrumente. Opțiunile de actualizare online din Module sunt pliabile; acordul utilizatorului rămâne explicit. Titlul și butonul X din comparație au fost verificate prin randare.
- Inspectorul adaptează înălțimea imaginilor la fereastră, păstrează numele evidențiate și informațiile originale; detaliile suplimentare se accesează prin scroll. Lupa și logica existentă de anulare/confirmare nu au fost înlocuite.
- Setările sunt împărțite în General, Căutare, Calibrare, Întreținere și Module. General conține selectorul limbii și descrierea aplicației; Module deschide managerul existent.
- Textele UI au fost extrase și revizuite în cataloage JSON RO/EN: 366 de chei comune. A fost clarificat fluxul „alegi referințele → cauți în originale → verifici → copiezi/exportezi”, fără a prezenta scorurile drept certitudini.
- Limbile suplimentare se descoperă la pornire din `languages/*.json`, fără recompilare. Preferința este salvată atomic și aplicată după repornire; fallback română, validare de schemă/limite și resurse încorporate. [Instrucțiuni pentru traduceri](LOCALIZATION.md).
- Traducerea preliminară EN a fost delegată modelului local Ollama `gemma4:latest`, în loturi mici cu verificarea parametrilor, apoi revizuită. Ollama nu este o dependență a aplicației livrate.

## Verificare

Validatorul `eng/validate-languages.ps1` a confirmat cele 366 de chei RO/EN, parametrii și referințele UI. Build WinUI Debug x64: 0 erori, 0 avertismente. Teste: Core 31, Application 25, Infrastructure 144, Presentation 50 — 250 trecute, 0 eșecuri, 0 ignorate. Ultimele două cazuri verifică și versiuni JSON fracționare/de tip șir: sunt respinse fără eroare necontrolată la pornire.

Capturile sunt produse de propriul arbore XAML al aplicației, în modul Debug opt-in. Nu sunt machete HTML și nu rulează instalări, curățări sau mutări asupra colecțiilor utilizatorului. Imaginile comparației și cele 1.000 de rezultate sunt sintetice.

Capturi în `artifacts/ui-refresh/`: General RO/EN, pagina fără rezultate, rezultate sintetice, Întreținere, Module, comparație și sumarul scanării. Pentru General/comparație, capturile finale sunt în `release-preview-ro-*` și `release-preview-en-*`; Module și scanarea sunt în `final-en-*`. Acestea verifică randarea, nu certifică toate interacțiunile native sau precizia căutării.

## Limite și gate-uri păstrate

Schimbarea limbii necesită repornire. Mesajele brute de eroare și dialogurile Windows pot avea limba sistemului; CLI/exporturile nu sunt localizate aici. Layout RTL nu este certificat. Temele întunecate, DPI suplimentar și Narrator nu sunt adăugate prin această cerere.

Rămân separate verificarea interactivă a lupei/zoom-ului, confirmărilor și butoanelor native active/inactive, validarea pe Windows curat și gate-ul pentru redistribuire publică a dependențelor. Nu sunt declarate închise prin simpla compilare sau aceste capturi.

## Livrare și verificare post-build

Pachet final: `artifacts/distribution/20261005-121536-deb70dba/PerceptoX-win-x64.zip`, 120.517.489 bytes.

SHA-256: `31158652AC4BA116643B046F8D4C9B4F56A38131C70EAF3BF0B7A9CE738D226E`.

În aceeași livrare există `PerceptoX-sources.zip` și `ThirdParty-sources/`. Parserul, cataloagele RO/EN și inspectorul din snapshot au fost comparate cu sursele finale prin SHA-256 și coincid. Acest paragraf este o înregistrare post-build; snapshot-ul conține raportul anterior acestei verificări, nu această completare.

ZIP-ul a fost extras într-o cale nouă cu spații și pornit prin Windows PowerShell 5.1. Raport: `artifacts/validation/ui-localization-20261005-121536/startup/startup-result.json`, rezultat `StartupPassed`. DLL-urile hostfxr, hostpolicy, coreclr, Microsoft.WindowsAppRuntime și Microsoft.UI.Xaml au fost încărcate app-local. Integritatea manifestului și declarațiile codec-ului HEIC/HEIF/AVIF au trecut. Rețeaua nu a fost dezactivată și sistemul nu este un Windows curat; raportul nu certifică acele gate-uri și nici toate interacțiunile UI.

Livrarea intermediară `20261005-121151-8a430c3f` este depășită de pachetul final de mai sus, care include și întărirea parserului de versiune JSON.
