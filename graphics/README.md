# PerceptoX — grafică și identitate

Referință furnizată: `UI.png`. Identitate furnizată: `icon.png`, `logo.png`, `logo.ai`.
Aceste patru fișiere sunt păstrate nemodificate.

## Fișiere folosite de aplicație

| Fișier | Utilizare |
| --- | --- |
| `icon.png` | simbolul din bara laterală |
| `logo.png` | logotipul din bara laterală |
| `PerceptoX.ico` | resursa icon a executabilului și iconița ferestrei/taskbar |
| `header-illustration.png` | ilustrație decorativă transparentă, antet |
| `sidebar-ribbons.svg` | panglici albastre vectoriale, bara laterală |
| `empty-state.svg` | ilustrație vectorială pentru starea fără rezultate |
| `PerceptoX.Theme.xaml` | paletă, gradiente și stiluri WinUI reutilizabile |

`PerceptoX.ico` se regenerează cu `eng/build-app-icon.ps1` din icon.png, fără deformare,
cu transparență și cadre PNG 16, 24, 32, 48, 64, 128 și 256 px. Nu se generează o identitate nouă.
Proiectul WinUI include explicit asseturile necesare; UI.png și logo.ai nu sunt copiate în distribuție.

## Ilustrația generată

Tool: built-in ImageGen, fundal transparent. Referință de stil/compoziție: UI.png.
Output copiat fără modificarea canalului alpha în `header-illustration.png`.

Prompt:

> Use case: illustration-story. Asset type: transparent decorative header illustration for the real Windows desktop application PerceptoX. The attached image is a STYLE AND COMPOSITION REFERENCE only; generate ONLY the illustration in its upper-right header, NOT the app UI. Two slightly tilted white-bordered photo cards with a realistic alpine lake and mountain photo; a pale glass-blue image tile with a bright blue circular checkmark at right; a small glossy blue magnifying-glass bubble at left connected by a short blue dotted line. Restrained premium soft 3D depth, clean white bevels, blue/cyan gradients and soft translucent blue geometric ribbons immediately behind the cards. Wide horizontal composition approx 2:1, isolated on fully transparent background, generous transparent outer margins, no text, no letters, no logos, no windows or app controls. Match the supplied screenshot's airy blue visual style faithfully. The illustration will occupy the right portion of a light-blue header; artwork only, no background rectangle.

SVG-urile sunt desenate determinist și rămân editabile. Controalele, textele și rezultatele
sunt WinUI native, nu o captură de ecran folosită drept interfață.

## Adaptări față de referință

- Lista afișează rezultate reale după scanare; exemplele fictive din mockup nu apar la pornire.
- Fiecare candidat poate fi bifat individual, inclusiv când o referință are mai multe potriviri.
- Nu sunt afișate butoane decorative fără acțiune (de exemplu comutatorul de vizualizare din mockup).
- Butoanele de scanare/copiere/export se activează când sunt disponibile datele necesare.
- Tema vizuală implicită este Light, conform referinței. Dark nu a fost reproiectată în această etapă.
- Animația de intrare durează 180 ms și respectă opțiunea Windows pentru animații.

Capturile randării reale și măsurătorile listei sunt în `artifacts/design/`.
