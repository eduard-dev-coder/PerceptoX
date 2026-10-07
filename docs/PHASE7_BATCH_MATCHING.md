# PerceptoX — potrivire dosare, operații și redesign

Actualizare: 2026-09-29.

Extindere de plan 2026-10-03: [PRODUCT_DIRECTIVES_2026-10-03.md](PRODUCT_DIRECTIVES_2026-10-03.md).
Întreținerea manuală, Smart Cleanup, codecurile noi, tuning-ul și heatmap-ul sunt cerințe viitoare,
nu funcții declarate implementate în situația de mai jos. Defectul butoanelor native în focus rămâne deschis.

## Implementat

- Două dosare separate: referințe externe și biblioteca de originale; indexare incrementală înainte de căutare.
- Miniatură cache a referinței, alăturată cu până la trei candidați. Toți candidații întorși de motor sunt păstrați pentru comparație/export.
- Nume de originale evidențiate; cale completă disponibilă la hover; scorul nu este o probabilitate.
- Bifare individuală; selectarea primului candidat din rezultatele găsite/aproape identice; rezultatele incerte se selectează manual.
- Copierea căilor unice în dosarul ales. Numele identice primesc sufix `(1)`, `(2)` etc. Publicare după copiere completă, fără suprascriere. Anularea păstrează fișierele deja copiate și raportează numărul lor.
- CSV: totaluri, toate referințele, statut, rang și cale pentru fiecare candidat, scor și erori; escaping și neutralizare de formule.
- HTML: totaluri, comparații alăturate, miniaturi copiate în `assets`, căi și scoruri; raport portabil, HTML-encoding.
- Selecția nu limitează raportul: exportul conține întreaga scanare.
- Profiluri și praguri ajustabile înainte de scanare. Fără mutare sau ștergere a fotografiilor.
- Redesign după `graphics/UI.png`, identitate integrată, iconiță în executabil, animație discretă.

## Verificare

- Build Debug și Release x64: zero erori și zero avertismente la verificarea redesignului.
- 102 teste trecute: Core 31, Application 14, Presentation 12, Infrastructure 45.
- Test integrat pe adapterul desktop real: indexare, referință externă, fișier corupt, două potriviri, miniaturile ambelor părți, HTML portabil, copiere și egalitatea conținutului.
- Teste de coliziuni la nume, deduplicare căi, sursă lipsă, anulare, escaping CSV/HTML și selecție/resetare.
- Captură XAML Light la 100%, 1424×891: `artifacts/design/empty/preview.png`.
- Noua listă: 1.000 de rezultate sintetice UX, maximum 10 containere realizate. Cele cinci salturi măsurate au durat 6.79–42.46 ms până la următorul frame. Nu reprezintă un benchmark ETW sau o garanție pe orice hardware.
- Controlul extern Computer Use nu a pornit (`app-server` lipsă, os error 3). Capturile sunt realizate de aplicație prin RenderTargetBitmap; pickerele native nu sunt retestate interactiv în această rulare.

## Calibrare: măsurători și limite

Evaluare reproductibilă pe setul sintetic existent de 10.000 imagini: 1.000 originale-query,
9 variante relevante/query, TopN=50, index phase5. Aceasta este direcția original→variante,
nu validarea completă referință/crop→original din noul UI, care are TopN=3.

| Praguri p/d/regiune/min. regiuni | Precizie | Recall |
| --- | ---: | ---: |
| 2 / 2 / 1 / 1 | 61.54% | 74.18% |
| 2 / 2 / 1 / 2 | 64.56% | 56.94% |
| 6 / 6 / 2 / 2 | 15.39% | 61.97% |

Creșterea pragurilor nu este o îmbunătățire universală; rezultatele nu justifică declararea
profilurilor drept calibrate pe fotografii reale. Pragurile implicite conservative sunt păstrate.
Profilul exploratoriu este experimental. Calibrarea finală necesită corpus real etichetat,
inclusiv negative vizual apropiate, crop-uri și screenshot-uri în direcția de utilizare reală.

## Separarea responsabilităților

`BatchMatchViewModel` deține starea și comenzile. `IBatchResultActions` separă operațiile de UI.
Adapterul desktop face maparea, iar `MatchedFileCopier` / `BatchReportExporter` execută I/O
în afara firului UI. Testele de integrare compilează adapterele reale prin link, fără WinUI.

Ollama local `qwen2.5-coder:14b` a furnizat o revizie scurtă de cazuri-limită (coliziuni,
ambiguități, fișiere indisponibile). Implementarea și verificarea au fost făcute de agentul principal.

Rămân pentru livrare extinsă: calibrare pe corpus real, verificarea interactivă completă a
pickerelor/tastaturii, packaging, teste de scară 100k/1M și profilarea sesiunilor reale lungi.
