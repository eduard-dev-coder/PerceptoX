# Pasul 1 — livrare standalone și verificări

Data: 2026-10-05. Stare: implementarea și pachetul de validare sunt livrate;
acceptarea pe Windows curat și gate-ul de redistribuire publică nu sunt declarate închise.

## Finalizat tehnic

- Folder/ZIP Windows x64 unpackaged self-contained: .NET, WinUI, SQLite nativ,
  PRI/XBF, iconiță/logo/grafică și payload decoder incluse.
- Instalare/reparare app-local HEIC/HEIF/AVIF cu acord și anulare; slot verificat,
  activare la restart. Fără instalări globale, elevare sau PATH obligatoriu.
- Backend de update HTTPS cu catalog RSA-PSS și verificări de integritate;
  startup check opt-in. Nicio descărcare automată și niciun server inventat.
- Surse proprii GPL-3.0-only, NOTICE și licențe terțe; Apache-2.0 pentru
  ImageSharp 3.1.12 pe criteriul utilizării în proiectul GPL open-source.
- Arhiva oficială Windows ImageMagick 7.1.2-26 (796 MB), SHA-256 fixat și
  proveniență, surse libheif 1.23.0/libde265 1.1.1 și fișiere de build.
- Inventarul de licențe acoperă și WinAppSDK Base/DWrite/Runtime, care nu apăreau
  în deps.json. Nativ/transitiv nu este tratat ca fiind relicențiat de wrapper.
- Kit de validare fără SDK/PowerShell 7, compatibil cu Windows PowerShell 5.1.
- Publicare în directoare noi, fără suprascrierea livrărilor anterioare;
  SHA-256 pentru ZIP-uri, arhivă surse separată și kit de acceptare offline.

## Verificări efective

- **209 teste trecute, zero skipped**: Core 31, Application 25, Presentation 17,
  Infrastructure 136. Testul formatării scorului verifică separat en-US/ro-RO;
  aplicația păstrează formatarea regională, nu forțează separatorul englezesc.
- Sandbox: fixture-urile .NET au TEMP/TMP în `.sandbox-profile/Temp`, proces-local.
  Au dispărut erorile de rename din folderul temporar Windows virtualizat.
  Două teste care creează junction-uri au fost validate într-o rulare explicită
  în afara sandbox-ului, exclusiv cu fixture-uri temporare, fără modificarea ACL.
- Build/publish Release online reușit; fără dezactivarea auditului NuGet.
- Interogare online NuGet pentru dependențe directe/tranzitive: fără advisories
  raportate. Nu certifică DLL-urile native sau vulnerabilități necunoscute.
  Raport: `artifacts/validation/20261005-nuget-audit/nuget-advisories.json`.
- Scriptul offline rulat efectiv în PowerShell 5.1: `StartupPassed`, .NET/WinUI
  app-local și declarații HEIC/HEIF/AVIF verificate. `cleanMachineVerified=false`
  și `operatorConfirmedOffline=false`; calculatorul dezvoltatorului nu este VM curată.
- Ollama local `gemma4:31b` a furnizat șase propuneri de acceptare. Au fost revizuite:
  testul copierii cere explicit originale nemutate și SHA-256 neschimbat.
  Scriptul de delegare acceptă Gemma și respinge modele cloud sau endpoint non-local.

## Condiții rămase pentru închiderea completă

| Condiție | De ce nu poate fi marcată închisă |
| --- | --- |
| Windows x64 curat, offline și interacțiune reală | Windows Sandbox nu este disponibil aici; pornirea pe PC de dezvoltare nu înlocuiește matricea din OFFLINE_ACCEPTANCE.md |
| Update online în producție | Nu există endpoint HTTPS/cheie publică aprobate; funcția rămâne dezactivată, aplicația funcționează offline |
| Redistribuire publică | Rebuild/relink complet terț, drepturi asupra graficii și compatibilitate GPL/termeni Microsoft pentru destinatari nu sunt certificate |

Nu se aplică automat o excepție de linking la GPL, nu se schimbă licența aleasă,
nu se publică server/repository și nu se activează componente Windows/reboot
fără autorizare. Nu se promite o certificare juridică printr-un simplu NOTICE.

Materiale: [STANDALONE.md](STANDALONE.md),
[THIRD_PARTY_SOURCES.md](THIRD_PARTY_SOURCES.md),
[OFFLINE_ACCEPTANCE.md](OFFLINE_ACCEPTANCE.md),
[LICENSING_PERSONAL_OPEN_SOURCE.md](LICENSING_PERSONAL_OPEN_SOURCE.md).

## Artefacte și verificarea finală

Livrare: `artifacts/distribution/20261005-050703-27aaefe8/`.

- `PerceptoX-win-x64.zip`: 120.454.461 bytes;
  SHA-256 `C0268DC26BCC653580BC95E9936833206FB300FFC0AB165227838A3457E36F6C`.
- `PerceptoX-sources.zip`: 1.640.071 bytes;
  SHA-256 `32EDE6FD6BC0714D0E5A4E49C27444EA3027DCB76E8E83D546A08986FE8C21F1`.
- `ThirdParty-sources/`: arhiva upstream, proveniență, NOTICE și instrucțiuni.
- `Validation/`: script PowerShell 5.1 și matricea de acceptare.

ZIP-ul final a fost extras într-un folder nou cu spații și caractere românești.
Scriptul PowerShell 5.1 a reverificat inventarul și a pornit fereastra;
hostfxr/hostpolicy/coreclr/WindowsAppRuntime/WinUI provin din folderul extras.
Raport: `artifacts/validation/20261005-final-ps51-smoke/startup-result.json`.
Rezultat `StartupPassed`, nu certificare Windows curat/offline.
Arhiva surselor este snapshot-ul de la publicare; acest paragraf documentar,
adăugat după verificarea ZIP-ului final, nu schimbă binarele sau sursele aplicației.
