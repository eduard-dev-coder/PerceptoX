# PerceptoX — distribuție standalone Windows x64

## Utilizare

PerceptoX regăsește originale dintr-o bibliotecă locală pornind de la referințe,
permite comparația alăturată și copierea imaginilor selectate. Limba se alege în
Setări → General sau din sidebar și se aplică instantaneu. Păstrați folderul `app/languages`;
româna și engleza sunt incluse. [Adăugarea unei limbi JSON](LOCALIZATION.md).

Extrageți întregul ZIP într-un folder local și porniți `PerceptoX.exe` din root.
Runtime-urile și resursele sunt în `app`, licențele în `licenses`, iar documentația în `docs`.
Launcher-ul nativ pornește copilul WinUI folosind calea absolută; nu caută DLL-uri în PATH.
Nu copiați numai executabilul: DLL-urile, fișierele `.pri`, grafica și folderul
`codecs` sunt parte din aplicație. Utilizatorul nu instalează .NET, Windows App
SDK, SQLite, ImageMagick, Python, Ollama sau un SDK de dezvoltare. Windows x64
compatibil rămâne o cerință de sistem. Gate-ul pe Windows curat este încă deschis.

Nu sunt descărcate/instalate automat componente la pornire. `app/codecs/imagemagick`
este payload-ul offline. Decoderul se activează dintr-un slot verificat după
instalarea confirmată în panoul Module și repornire; Release ignoră `PERCEPTOX_MAGICK_PATH`.
Datele existente ale utilizatorului nu sunt incluse în pachet. Logurile și
calibrarea sunt în `%LocalAppData%/PerceptoX`; locațiile indexului/cache-ului
rămân cele selectate în aplicație. Standalone nu înseamnă „fără urme” sau un EXE unic.

## Construire repetabilă (numai pentru dezvoltator)

Necesită SDK-ul fixat prin `global.json`, toolchain-ul WinUI și pachetele NuGet.
Acestea nu sunt cerințe pentru utilizatorul final.

1. Pregătiți o singură dată bundle-ul x64 din distribuția ImageMagick aleasă:
   `./eng/prepare-codec-bundle.ps1 -SourceDirectory 'C:/cale/absoluta/ImageMagick'`.
   Nu rulează installer și nu schimbă registry/PATH. Sunt păstrate DLL-urile,
   modulele, configurația, profilul ICC, License și NOTICE; nu sunt incluse
   dezinstalatorul, headerele, import libraries sau documentația web.
2. Colectați sursele native oficiale: `./eng/fetch-codec-sources.ps1` (796 MB,
   versiune și SHA-256 fixate). Rulați `./eng/publish-standalone.ps1`.
3. Pentru cache local și lipsa accesului NuGet: `./eng/publish-standalone.ps1 -OfflineRestore`.
   Această opțiune dezactivează auditul NuGet numai pentru comanda respectivă;
   nu este o verificare de vulnerabilități și nu permite eliberarea gate-ului de release.
4. Rezultatul este un folder nou în `artifacts/distribution/<build-id>` cu
   folderul aplicației, ZIP și SHA-256, arhiva surselor PerceptoX,
   `ThirdParty-sources` și kitul `Validation` compatibil cu Windows PowerShell 5.1.
   Publicările nu suprascriu rezultatele vechi. Materialele terțe sunt descrise în
   [THIRD_PARTY_SOURCES.md](THIRD_PARTY_SOURCES.md).
5. `./eng/verify-standalone.ps1 -PackageDirectory 'D:/cale/pachet'` verifică
   structura, runtimeconfig, codecurile declarate și inventarul SHA-256.
6. `./eng/smoke-standalone.ps1 -PackageDirectory 'D:/cale/pachet'` pornește
   numai procesul de test propriu, cu PATH restrâns, verifică runtime-urile
   încărcate app-local și închide fereastra la final. Nu scanează fotografii.
7. Pentru setup: `./eng/prepare-inno-tools.ps1`, apoi
   `./eng/build-installer.ps1 -PackageDirectory 'D:/Aplicatii/SimilarPhoto/artifacts/distribution/<build-id>/PerceptoX-win-x64'`.
   Compilatorul oficial Inno Setup 7.1.0 este pregătit portabil în `tools`, cu
   SHA-256 și semnătură verificate. Traducerea românească upstream are hash fixat;
   completările PerceptoX sunt în `eng/installer/Messages.ro.isl`. Nu este necesar
   Inno Setup la utilizatorul final. Setup-ul și SHA-256 sunt în `Setup`.
8. `./eng/test-installer.ps1 -SetupPath 'D:/Aplicatii/SimilarPhoto/artifacts/distribution/<build-id>/Setup/PerceptoX-1.0.0-Setup-win-x64.exe'`
   creează o instalare izolată în workspace, verifică refuzul fără acord,
   instalarea cu acord, activarea codecurilor, pornirea și dezinstalarea.
   Modul de validare nu creează scurtături sau intrări de dezinstalare în registry.
   Wizard-ul normal oferă scurtături Desktop și Start și instalare per-user.

Procesul este repetabil pe aceleași intrări, nu promite ZIP-uri identice bit cu bit
(timestamp-urile/MSBuild pot diferi). Bundle-ul este fixat prin manifest SHA-256;
modificări neinventariate sau reparse points sunt respinse la publicare.

## Licențe: utilizare personală; distribuția publică are gate separat

La 2026-10-04 utilizatorul a confirmat distribuirea gratuită cu utilizare comercială
permisă și a ales GPL-3.0 pentru codul propriu. LICENSE și NOTICE sunt incluse,
iar sursele proprii au arhivă separată. Dependențele își păstrează licențele proprii.
Pachetul curent este pentru utilizare personală și validare, nu eliberează singur
gate-ul de redistribuire către terți.

Clarificarea și gate-ul personal/open-source sunt documentate în
[LICENSING_PERSONAL_OPEN_SOURCE.md](LICENSING_PERSONAL_OPEN_SOURCE.md).
Managerul de module cu instalare/reparare/actualizare confirmată de utilizator
este descris în [MODULE_MANAGER.md](MODULE_MANAGER.md). Online necesită o sursă
și o cheie aprobate; fără acestea nu se contactează internetul. Nu actualizăm independent
DLL-uri .NET/WinUI sau codecuri fără manifest de compatibilitate și verificarea
termenilor versiunii noi.

Setup-ul Inno poate activa codecurile offline în timpul instalării, numai după
acceptarea licențelor. Aplicația și payload-ul de codecuri sunt componente fixe;
utilizatorul poate accepta instalarea completă sau o poate anula. `--install-offline-codecs` fără
`--accept-codec-licenses` este refuzat; acest mod nu modifică PATH sau registry.

`licenses` conține declarațiile NuGet pentru dependențele efectiv publicate,
textele de licență incluse de furnizori, notificările disponibile și licențele
comune MIT/Apache. Nu se includ xUnit, BenchmarkDotNet sau Ollama. ImageMagick
Studio LLC este autorul decoderului ImageMagick; `License.txt` și `NOTICE.txt`
sunt păstrate în bundle. `policy.xml` este configurația modificată de PerceptoX:
delegates/filter externe și citiri indirecte sunt interzise; sunt permise numai
codecurile necesare conversiei HEIC/AVIF → PNG și output INFO.

Înainte de distribuție către terți trebuie închise explicit:

- **ImageSharp 3.1.12**: Apache-2.0 pe criteriul utilizării în proiectul open-source
  GPL-3.0-only. Păstrăm textul/atribuirile și reverificăm orice versiune nouă;
  nu presupunem o licență comercială sau eligibilitate numai pentru că e gratuit.
- **Delegați ImageMagick**: inventarul furnizorului include libheif/libde265 LGPL
  și alte componente. Simplul NOTICE nu dovedește îndeplinirea obligațiilor de
  sursă/relink pentru build-ul efectiv. Trebuie păstrată corespondența cu sursele
  și build-ul și satisfăcute condițiile aplicabile înainte de redistribuire.
- **Microsoft**: respectarea licențelor Windows App SDK/VC runtime și a termenilor
  ceruți pentru distribuție; includerea recipient terms în livrarea publică.
- **Win2D 1.4.0**: metadatele NuGet conțin un URL de licență vechi care nu mai
  livrează termenii. Includem notificarea MIT din sursa oficială, nuspec-ul original
  și proveniența verificării. Reconcilierea termenilor binarului rămâne necesară
  înainte de publicare; nu înlocuim automat declarația originală cu o presupunere.
- Audit NuGet online și revizuirea componentelor native livrate.

Acest document/inventar nu este o opinie juridică și nu certifică legal o livrare.

Surse primare:

- [Microsoft: .NET + Windows App SDK self-contained](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)
- [ImageSharp 3.1.12: licența exactă](https://github.com/SixLabors/ImageSharp/blob/v3.1.12/LICENSE)
- [ImageMagick: redistribuire](https://imagemagick.org/license/)
- [ImageMagick: distribuții Windows](https://imagemagick.org/download/)

## Gate separat: Windows curat, offline

Kitul și matricea de acceptare sunt în [OFFLINE_ACCEPTANCE.md](OFFLINE_ACCEPTANCE.md).
Testați un Windows x64 fără .NET/WinUI/VC redistributable/ImageMagick/Ollama
instalate separat: pornire, JPEG/PNG/WebP și fișiere HEIC/AVIF reale, indexare,
căutare, inspector, copiere, CSV/HTML, anulare și curățare/recuperare.
Verificați că runtime-urile încărcate provin din folderul livrat. Probe pe
calculatorul de dezvoltare și lista declarată a formatelor nu înlocuiesc acest test.
