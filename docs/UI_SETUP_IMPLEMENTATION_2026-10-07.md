# PerceptoX — corecții UI și distribuție offline

7 octombrie 2026. Implementarea cerințelor este separată de gate-urile de publicare.

## Ce s-a implementat

- Mesajele de donație folosesc o zonă de dimensiune stabilă; schimbarea textului nu mută butonul sau centrul mesajului. Pauza este de 6 secunde după tranziție; animația verticală de 700 ms folosește fade și un blur discret GPU. Rotația se oprește când fereastra este inactivă, la hover/focus sau când este deschis un dialog. Respectă dezactivarea animațiilor Windows; dacă efectul nu este disponibil rămâne fade-ul.
- Sidebar-ul arată tema activă: „Temă Light” / „Temă Dark”, cu actualizare instantanee și traducere RO/EN.
- Linkul „Despre” de sub Donate deschide un dialog armonizat Light/Dark: logo, versiune din assembly (în build-ul curent 1.0.0), autor Prepelita Eduard, `eduard.condact.dev@gmail.com`, descrierea aplicației și Închide. Nu se deschide automat browserul sau clientul de mail.
- Pachetul are `PerceptoX.exe` în root, `app` pentru runtime-uri/resurse, `licenses` pentru notificări și `docs` pentru documentație. DLL-urile native WinUI nu sunt mutate arbitrar. Launcher-ul x64 are CRT static, folosește calea absolută, transmite argumentele fără shell și propagă codul de ieșire.
- Setup-ul Inno instalează per-user, cu .NET/WinUI/SQLite și payload HEIC/HEIF/AVIF offline. Nu descarcă la instalare și nu modifică PATH. GPL și textele terțe sunt prezentate înainte de copiere. Wizard-ul normal oferă Start Menu și Desktop; instalarea silent fără `/ACCEPTLICENSES=YES` este refuzată. Activarea codecurilor are încă un gate explicit și publică atomic slotul verificat.
- Dezinstalarea elimină fișierele urmărite de installer, nu șterge recursiv fișiere necunoscute sau biblioteci/indexuri/rapoarte ale utilizatorului. Starea generată ulterior în `app/modules/installed` poate rămâne după dezinstalare; curățarea ei completă nu este pretinsă.

## Dovezi tehnice

### UI

`artifacts/ui-setup-20261007-v2/sidebar-verification.json`:

- Zonă donație: X=502, lățime=520, stabilă în toate stările RO/EN și Light/Dark capturate.
- Cinci finalizări de animație și revenirea la primul mesaj verificate; poziția mesajului care intră rămâne identică după tranziție.
- Blur disponibil; pauză configurată 6 secunde. Intervalele timerului observate: 6,000 și 6,765 secunde (al doilea include tranziția).
- 1.000 de rezultate și 1.000 de selecții păstrate la schimbarea preferințelor; aceeași pagină de setări păstrată.
- Capturi `about-Light-ro/preview.png`, `about-Dark-en/preview.png` și dialoguri de donație inspectate vizual.
- Capturile XAML nu certifică randarea/hit-testing-ul nativ al caption buttons sau toate interacțiunile cu mouse-ul.

### Build și pachet

- Build Debug WinUI: fără erori sau avertismente.
- Teste automate: 295 trecute — Core 31, Application 37, Infrastructure 158, Presentation 69.
- Cataloage aplicație: 429 chei RO/EN și placeholders verificați; palete Light/Dark verificate.
- Setup: 281 mesaje RO/EN și placeholders verificați. Gemma 4 `gemma4:31b` a fost apelat pe `127.0.0.1:11434` pentru draftul a 23 de traduceri, apoi acestea au fost revizuite; niciun model cloud nu a fost folosit pentru această sarcină.
- ZIP extras în cale cu spații și pornit prin launcher: `artifacts/validation/organized-startup-20261007/startup/startup-result.json`. Au fost încărcate din `app`: hostfxr, hostpolicy, coreclr, Microsoft.WindowsAppRuntime și Microsoft.UI.Xaml.

### Instalare / dezinstalare

Prima validare completă: `artifacts/validation/setup-20261007-160929-06aaf29f/setup-result.json`.

- Fără acord silent: exit 1, fără instalarea aplicației.
- Cu acord: exit 0; codecuri 7.1.2.26 activate în `app/modules/installed`, fără instalare ImageMagick globală.
- Apelul launcher-ului pentru instalarea codecurilor fără acord: exit 2 și pointer activ nemodificat.
- Pornire aplicație instalată și runtime-uri app-local: trecută.
- Dezinstalare copie de test: exit 0; un fișier personal necunoscut din folderul instalării și un marker de bibliotecă externă au supraviețuit.
- PATH și prezența intrării de uninstall existente nu au fost modificate în modul de validare. Acest mod dezactivează intenționat scurtăturile și înregistrarea în registry; nu reprezintă un test interactiv al wizard-ului normal.
- Încercările inițiale eșuate sunt păstrate pentru diagnostic, nu sunt prezentate drept livrări acceptate. Au evidențiat o corecție pentru acordul silent în pagina de licențe și calea greșită din scriptul de test, nu din managerul de module.

## Reproducere

1. `eng/publish-standalone.ps1` — folder/ZIP nou, surse proprii și materialele de sursă terțe; fără suprascrierea livrărilor anterioare.
2. `eng/prepare-inno-tools.ps1` — compilator oficial 7.1.0 pregătit portabil în workspace, SHA-256 și Authenticode verificate; traducere upstream păstrată, cu o copie compatibilă în care sunt eliminate doar trei chei vechi.
3. `eng/build-installer.ps1 -PackageDirectory <folder>` — installer, SHA-256, termeni terți și jurnal de compilare. `-OutputDirectory` permite o încercare nouă, fără suprascriere.
4. `eng/test-installer.ps1 -SetupPath <setup>` — refuz fără acord, instalare cu acord, codecuri, startup și uninstall într-un director nou de validare.

## Ce NU este certificat încă

- Windows x64 curat, offline, fără runtime-uri preinstalate: nu există încă proba cerută. Calculatorul de dezvoltare nu o înlocuiește.
- Semnătură Authenticode PerceptoX: setup-ul și launcher-ul sunt nesemnate. Semnătura compilatorului Inno nu semnează installer-ul nostru; pot apărea avertismente SmartScreen.
- Distribuție publică: surse/build/relink pentru delegații ImageMagick, termeni Microsoft și drepturi grafice rămân gate-uri distincte. În plus, Win2D 1.4.0 are un URL NuGet de licență vechi, nefuncțional: păstrăm nuspec-ul și includem notificarea MIT din sursa oficială cu proveniență, fără să declarăm reconcilierea juridică a binarului finalizată.
- Textele GPL nu adaugă o interdicție comercială pentru utilizatorii PerceptoX. Dependențele își păstrează termenii proprii. Acceptarea unui wizard nu remediază obligațiile de redistribuire ale autorului.

Surse primare pentru verificarea componentelor: [Inno Setup oficial](https://jrsoftware.org/isdl.php), [verificarea semnăturilor Inno](https://jrsoftware.org/isdl-verify.php), [fișierele de limbă Inno](https://jrsoftware.org/ishelp/topic_languagessection.htm), [licența Win2D upstream](https://github.com/microsoft/Win2D/blob/winappsdk/main/LICENSE.txt), [istoricul licenței Win2D](https://github.com/microsoft/Win2D/blob/winappsdk/main/CHANGELOG.md).
