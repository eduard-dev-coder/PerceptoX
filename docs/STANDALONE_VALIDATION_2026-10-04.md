# Standalone x64 — implementare și verificare, 2026-10-04

## Rezultat

Pachet folder/ZIP self-contained pentru utilizare personală implementat.
Nu se modifică registry/PATH global, nu se rulează instalatoare și nu se
descarcă componente la pornire. Nu declarăm încă validarea pe Windows curat
sau autorizarea unei redistribuiri publice.

## Intrări fixate și resurse

- SDK 10.0.301 prin `global.json`; runtime .NET inclus 10.0.9, Windows App SDK 2.5.1.
- ImageSharp 3.1.12, Microsoft.Data.Sqlite 10.0.12, e_sqlite3 nativ x64.
- ImageMagick 7.1.2-26 Q16-HDRI x64, bundle pregătit din distribuția Windows
  explicit selectată; magick.exe are semnătură Authenticode **Valid**, semnatar
  **ImageMagick Studio LLC**, verificată pe calculatorul de dezvoltare.
- 196 fișiere codec fixate prin SHA-256: DLL-uri, module și configurație,
  License/NOTICE și ICC. `policy.xml` modificat de PerceptoX: numai coders
  necesari HEIC/HEIF/AVIF/PNG/INFO, fără delegates/filter externe sau `@file`.
- Profilele Release nu sunt trimmed/single-file. Această alegere conservatoare
  păstrează resursele native și suportul WinUI/reflection.
- Resursele compilate `.xbf` și PRI ale aplicației, omise inițial din publish,
  sunt acum incluse explicit de target-ul MSBuild și verificate la packaging.
- Grafică, icon PNG/ICO și logo incluse; xUnit/benchmark/Ollama nu sunt runtime.

## Verificări executate

| Verificare | Rezultat |
| --- | --- |
| Publicare self-contained x64 | Reușită |
| Restore normal cu acces NuGet funcțional | Reușit; fără warning de sursă inaccesibilă |
| Restore din cache când rețeaua sandbox a eșuat | Reușit cu opțiune explicită OfflineRestore; nu este audit online |
| Probe format bundled cu PATH restrâns | HEIC, HEIF și AVIF declarate readable |
| AVIF sintetic → bundle cu policy restrictiv → PNG și pipeline | Test trecut; 48×96, sursă nemodificată |
| Localizare app-relative / lipsă bundle / cale relativă | 3 teste trecute |
| Toate testele automate existente + noi | 177 trecute: Core 31, Application 25, Presentation 16, Infrastructure 105; 0 skipped |
| Pornire executabil publicat cu PATH doar System32 | Fereastră creată; procesul rămâne activ după inițializare |
| Originea modulelor din procesul publicat | hostfxr, hostpolicy, coreclr, Microsoft.WindowsAppRuntime și Microsoft.UI.Xaml din folderul aplicației |
| ZIP extras în alt folder; hash-uri, codec probe și pornire | Reușit cu PATH restrâns; runtime-urile încărcate din folderul extras |
| Hash-uri și inventar de licențe | Generare automată inclusă în publicare |

Livrare verificată: `artifacts/distribution/20261004-180122-3ccb1dd0/PerceptoX-win-x64.zip`,
120.294.621 octeți (aproximativ 115 MiB); folderul aplicației și SHA-256 sunt
alături. Nu este necesară instalarea ImageMagick de pe calculatorul de dezvoltare.

Testul AVIF folosește ImageMagick instalat **numai la generarea fixture-ului de
test**; conversia și hashing-ul testate folosesc bundle-ul app-local. Asta nu
introduce o dependență de instalare în aplicația livrată. HEIC/HEIF reale nu au
încă probă de decodare end-to-end în acest increment. Testele Core/Application/
Presentation au fost rerulate fără rebuild, componentele lor nefiind modificate;
Infrastructure a fost recompilat și verificat.

## Licențe și decizii

Utilizatorul a confirmat folosirea personală/necomercială; MIT este o posibilitate
viitoare, nu o relicențiere automată a proiectului. Sunt păstrate textele și
attribution-urile dependențelor, inclusiv declarațiile NuGet, notificările .NET
și Windows App SDK și inventarul ImageMagick cu licențele delegaților.

Licența exactă [ImageSharp 3.1.12](https://github.com/SixLabors/ImageSharp/blob/v3.1.12/LICENSE)
prevede granturi diferite după criteriile sale; simplul „necomercial” nu
relicențiază componenta. O eventuală distribuție open-source/source-available
trebuie verificată față de criteriile licenței și să păstreze notificările.

[ImageMagick](https://imagemagick.org/license/) permite distribuția în condițiile
propriei licențe, dar delegații nu sunt acoperiți automat de același grant.
NOTICE-ul bundle-ului include, între altele, libheif/libde265 LGPL. Pentru
redistribuire trebuie închisă corespondența surselor/build-urilor și respectate
obligațiile aplicabile, nu doar copiat un text LICENSE. Termenii de recipient
pentru codul Microsoft trebuie de asemenea revizuiți înainte de livrare publică.

## Limite / ce nu este certificat

- Calculatorul de dezvoltare are runtime-uri instalate: testul modulelor
  app-local este probă utilă, nu înlocuiește Windows curat offline.
- Nu s-a executat un ciclu complet UX/indexare/copiere pe o mașină curată.
- Nu sunt revendicate fidelitate ICC/HDR, suport pentru toate variantele HEIC,
  rezultate calibrate pe un corpus real sau verificarea tuturor vulnerabilităților native.
- Build/publish repetabil nu înseamnă ZIP identic bit cu bit.

Instrucțiunile de publicare și gate-ul Windows curat sunt în [STANDALONE.md](STANDALONE.md).
