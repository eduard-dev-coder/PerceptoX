# Livrare: module cu confirmare și surse GPL — 2026-10-04

## Implementat

- Panou persistent în colțul dreapta-jos, pe rând propriu (fără acoperirea rezultatelor).
- Listarea bazei .NET/WinUI/SQLite active și a decoderului HEIC/HEIF/AVIF.
- Checkbox de acord, instalare/reparare offline din payload, progres și Anulare.
- Slot nou app-local, SHA-256, probe codec și pointer atomic; slot anterior păstrat.
- Activare numai la repornire; fără elevare/registry/PATH sau schimbarea DLL-urilor active.
- Online: catalog semnat RSA-PSS, cheie publică minimum 3072-bit, HTTPS fără redirect,
  protocol/arhitectură, expirare, versiune și dimensiune/digest verificate, ZIP limitat
  fără traversal/ADS/symlinks; acord nou înainte de download și staging.
- Verificarea la pornire este opt-in și doar notifică, nu instalează.
- GPL-3.0-only pentru codul propriu, LICENSE/NOTICE, metadate build și source ZIP.
- Arhiva surselor conține NuGet.Config public curat, nu config-ul privat al dezvoltatorului.

## Verificări efective

31 teste noi de module: consent, payload modificat, sloturi distincte, anulare,
probe failure, lease concurent, slot modificat, traversal/dispozitive Windows,
catalog semnat valid, cheie greșită, catalog incompatibil/expirat, oferte fabricate,
ZIP traversal, opt-in la startup și întreg traseul catalog → download simulat →
digest → extragere → activare. Download-ul simulat nu contactează internetul.
Testul bundle-ului real instalează copia și probează HEIC/AVIF în noul slot.

Total automate: **208 teste**, Core 31, Application 25, Presentation 16,
Infrastructure 136; zero skipped. Proiectele nemodificate au fost rerulate
fără rebuild; Infrastructure și WinUI au fost recompilate.

Publicare Release x64 self-contained reușită, inclusiv PRI/XBF/grafică,
payload, notificări și arhiva surselor. Pornirea din ZIP relocat a creat fereastra
și a încărcat .NET/WinUI din propriul folder, cu PATH restrâns.

Ollama local a raportat `gemma4:31b` fără `remote_model`; apelul local a furnizat
șase cazuri de test pentru instalarea offline. Propunerile au fost validate de
agentul principal; Ollama nu este o dependență a aplicației.

## Artefacte

- `artifacts/distribution/20261004-183247-55caaa1b/PerceptoX-win-x64.zip`
- `artifacts/distribution/20261004-183247-55caaa1b/PerceptoX-sources.zip`
- SHA-256 lângă fiecare ZIP; inventarul fișierelor în pachet.

## Gate-uri rămase, nu declarate complete

1. Nu există încă endpoint și cheie de update aprobate; online rămâne dezactivat.
   Nu am creat sau publicat un repository/server ori o cheie privată.
2. Interacțiunea completă Windows în dialog (click/consent/Anulare/repornire) și
   testul pe Windows curat offline nu au fost certificate în această livrare.
3. Source ZIP este numai pentru PerceptoX, nu sursele corespunzătoare tuturor
   componentelor native terțe. Materialele LGPL, drepturile graficii și
   compatibilitatea pachetului complet GPL/termeni Microsoft sunt gate-uri publice.
4. Nu există încă UI pentru rollback/ștergerea sloturilor vechi; nu se șterg
   automat sloturi sau fotografii. La un folder protejat apare eroare de acces.
5. Un modul activ schimbat poate necesita reindexare; relansați Caută/Scanează
   după activarea noului modul pentru a reconcilia profilul de procesare.

Instrucțiuni: [MODULE_MANAGER.md](MODULE_MANAGER.md). Licențe:
[LICENSING_PERSONAL_OPEN_SOURCE.md](LICENSING_PERSONAL_OPEN_SOURCE.md).
