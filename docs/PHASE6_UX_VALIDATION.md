# Faza 6 — validare UX

## Rezultat curent

Profilarea automată Debug a rulat cu 1.000 de rezultate și 1.000 de miniaturi JPEG distincte, fără a înlocui workflow-ul real la lansarea normală. Ambele teme trec pragul intern: minimum 1.000 de rezultate, sub 200 de containere realizate, p95 sub 100 ms și deplasare reușită a focusului de la primul card la al doilea.

| Temă | Scală | p95 până la cadrul următor | Maxim containere realizate | Delta working set | Focus dreapta | Rezultat |
|---|---:|---:|---:|---:|---:|---|
| Light | 100% | 20,41 ms | 60 | +5.652.480 B | index 1 | trecut |
| Dark | 100% | 22,08 ms | 40 | +7.929.856 B | index 1 | trecut |

Rapoarte brute:

- `artifacts/phase6/ux-profile-light-pass2.json`
- `artifacts/phase6/ux-profile-dark-pass.json`

## Remedieri făcute în timpul validării

- `GridView` permite acum selecția și focusul containerelor, astfel încât rezultatele pot fi parcurse cu tastatura;
- fiecare rezultat expune un rezumat accesibil cu numele fișierului, scorul și distanțele, iar miniatura decorativă nu dublează anunțul;
- titlurile paginilor sunt marcate ca heading nivel 1;
- stările dinamice de indexare și căutare folosesc regiuni live `Polite`;
- navigarea inițială dublă a fost eliminată; înainte, schimbarea `SelectedItem` și navigarea explicită creau două instanțe ale aceleiași pagini;
- un test de unitate protejează rezumatul accesibil al rezultatului.

## Rulare reproductibilă

Modul este disponibil numai în build-ul Debug și se activează explicit:

```powershell
dotnet build src\PerceptoX.WinUI\PerceptoX.WinUI.csproj -p:Platform=x64 --no-restore
& .\src\PerceptoX.WinUI\bin\x64\Debug\net10.0-windows10.0.19041.0\PerceptoX.WinUI.exe `
  --ux-validation `
  --theme=light `
  --ux-report=.\artifacts\phase6\ux-profile-light.json
```

Pentru tema Dark se schimbă numai `--theme=dark` și calea raportului. Lansarea fără `--ux-validation` folosește exclusiv `DesktopPerceptoXWorkflow`.

## Limite și gate rămas

- scala măsurată a fost `RasterizationScale = 1` (100%); utilizatorul a eliminat explicit DPI 150% și 200% din cerințe;
- metadata și focusul direcțional rămân disponibile; utilizatorul a eliminat Narrator din cerințe;
- controlul Windows extern nu a pornit deoarece helper-ul Codex `app-server` nu este disponibil în mediul curent;
- WPR oferă profilul `XAMLAppResponsiveness`, dar pornirea lui a fost refuzată de politica sistemului cu `0xc5585011`; rapoartele JSON sunt măsurători interne, nu înlocuiesc o urmă ETW.

Scop revizuit de utilizator: Narrator și DPI 150%/200% nu blochează continuarea autorizată.
Verificarea interactivă completă a tastaturii și pickerelor nu este declarată încheiată.
Redesignul și verificările noii liste sunt documentate în `PHASE7_BATCH_MATCHING.md`.
