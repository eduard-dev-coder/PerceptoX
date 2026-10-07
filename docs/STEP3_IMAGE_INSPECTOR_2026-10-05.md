# Pasul 3 — Inspector de comparație

## Implementare

- Deschidere din „Compară / Zoom” sau prin dublu-click pe miniatura originalului găsit.
- Dialog cu referința în stânga, originalul găsit în dreapta; numele evidențiat,
  calea completă selectabilă, rezoluția și dimensiunea fișierului sub fiecare imagine.
- Metadatele descriu fișierele originale, nu PNG-urile temporare reduse. Dimensiunile
  sunt cele codificate în sursă; previzualizarea respectă orientarea EXIF.
- Selector x2/x4, implicit x2. Lupe sincronizate pe coordonate relative normalizate;
  benzile libere produse de `Stretch.Uniform` nu declanșează lupa.
- Actualizări coalesced la maximum aproximativ 30/secundă pe dispatcher, numai
  când există mișcare; fără acces la disc/decodare în handler-ul PointerMoved.
- Zoom-ul general/pan-ul sincronizat și heatmap/alinierea experimentală sunt păstrate.
- „X” sus, „OK” jos, Escape; închiderea/unload anulează pregătirea, așteaptă worker-ul,
  oprește timer-ul, dezabonează evenimentele și eliberează imaginile/fișierele temporare.
- Dialogul se deschide cu indicator de încărcare, nu după terminarea decodării.
  O eroare apare în același dialog; blocarea operațiilor concurente este eliberată în finally.

## Limite explicite

Lupa mărește previzualizări cu latura maximă de 1024 px, nu rezoluția integrală a
originalului. Limita protejează memoria, dar nu permite inspecția pixel-perfect a
fotografiilor mari. Mesajul este afișat în dialog. Inspectorul existent păstrează
limita de 32 MP pe imagine; o extindere la rezoluții mai mari necesită decodare
regională/downsampling măsurat separat.

Coordonatele relative nu sunt înregistrare geometrică: pentru un crop pot arăta
detalii diferite. Alinierea este opțională, experimentală, translație/scalare, fără
rotație/perspectivă; un hash apropiat nu este dovadă de aliniere sau probabilitate.

## Verificări automate și gate manual

Rezultat: **237 teste trecute**, zero skipped — Core 31, Application 25,
Presentation 37, Infrastructure 144 (13 cazuri noi față de pasul 2).
Testele pentru 1×1/EXIF au identificat și au condus la corectarea unui upscale
inutil existent: sursele mici nu mai sunt interpolate automat la 1024 px.

Geometria are teste pentru letterboxing, extreme de aspect ratio, puncte
fracționare, x2/x4, margini fără deplasarea punctului inspectat, 1×1 px și intrări
invalide. Infrastructure verifică metadatele originalelor versus preview redus,
orientarea EXIF, transparența 1×1, anularea înainte de pregătire, păstrarea surselor
și eliminarea preview-urilor după Dispose.

Ollama local `gemma4:latest` a propus cinci cazuri-limită; sugestiile au fost
revizuite și transformate în verificări concrete, nu tratate ca rezultate de test.

De verificat manual în aplicație, fără a confunda build-ul cu validarea interacțiunii:

1. Deschidere din buton și dublu-click; metadate cu spații/diacritice/cale lungă.
2. Hover pe ambele imagini, inclusiv benzi libere și colțuri; x2 → x4 fără mișcare.
3. Referință landscape versus original portrait; Ctrl+rotiță și pan în ambele sensuri.
4. Heatmap/aliniere pornite și oprite; revenire la original și centru stabil al lupei.
5. X, OK, Escape, închidere în timpul încărcării, redeschidere repetată; fără dialog
   rămas, blocare a operațiilor sau fișiere temporare ținute deschise.
6. Fișier lipsă/corupt sau peste limita de memorie: eroare lizibilă și revenire la căutare.

Gate-urile Windows curat/licențe/update online din pasul 1 rămân separate.

## Livrare verificată

Pachet: `artifacts/distribution/20261005-054420-d5589221/PerceptoX-win-x64.zip`
(120.475.024 bytes).
SHA-256: `81AF7290A4E3B07FF2781B4FA8295538FE64ACE437C0039A1A5329D1DB9BE14D`.
Surse proprii: `PerceptoX-sources.zip` (1.661.250 bytes); materialele terțe și
kitul de acceptare sunt alături. Publish Release reușit.

Raport: `artifacts/validation/20261005-step3-startup/startup-result.json`.
Pornire din ZIP relocat într-un folder cu spații, PowerShell 5.1, PATH restrâns:
`StartupPassed`, cinci DLL-uri .NET/WinUI verificate app-local. Nu este test
Windows curat/offline confirmat și nu validează interacțiunea cu inspectorul.

Secțiunea de livrare a fost adăugată după publicare; arhivele/manifestele nu au
fost modificate ulterior. Rezumatul este livrat separat în `VALIDATION.md`.
