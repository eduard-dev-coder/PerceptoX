# Acceptare standalone pe Windows curat

Folosiți o VM/mașină Windows x64 compatibilă, nouă, fără runtime-uri dezvoltator,
ImageMagick sau Ollama. Nu se activează Windows Sandbox și nu se instalează
software pe calculatorul gazdă prin aceste scripturi. Salvați un snapshot înainte.

1. Copiați ZIP-ul și kitul de validare, apoi deconectați rețeaua VM-ului.
2. Extrageți întregul ZIP într-un folder cu spații și caractere românești.
3. Rulați în Windows PowerShell x64 (inclus în Windows):
   `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test-offline-package.ps1 -PackageDirectory 'C:\Test PerceptoX\PerceptoX-win-x64' -OfflineConfirmed`
4. Păstrați `startup-result.json` și datele despre imaginea Windows/snapshot.
   Scriptul verifică SHA-256, codecurile și runtime-urile app-local. Nu declară
   Windows curat sau întregul flux valid doar pentru că fereastra s-a deschis.
   Rularea explicită a scriptului autorizează proba developer a decoderului;
   aplicația utilizatorului continuă să ceară acord separat pentru instalarea lui.

## Matrice manuală obligatorie

Folosiți numai copii ale fotografiilor de test, fără o bibliotecă personală reală.

| Test | Rezultat necesar | Stare inițială |
| --- | --- | --- |
| Prima pornire offline | Fereastră, grafică și butoane native vizibile; fără cerere .NET/WinUI | Neexecutat |
| Module fără acord | Nu se instalează nimic; JPEG/PNG/WebP rămân utilizabile | Neexecutat |
| Instalează + acord | Instalează din payload, fără internet/elevare/PATH | Neexecutat |
| Anulare instalare | Slotul anterior intact, fără activare parțială | Neexecutat |
| Repornire | Modul activ; HEIC/HEIF/AVIF reale pot fi indexate și găsite | Neexecutat |
| Referințe + originale | JPEG/PNG/WebP și HEIF/AVIF: rezultate lângă referință | Neexecutat |
| Anulare scanare | Operația se oprește controlat; SQLite se poate redeschide | Neexecutat |
| Inspector | Numele/căile și imaginile afișate fără dependențe externe | Neexecutat |
| Copiere selecție | Destinație corectă; originale nemutate, SHA-256 neschimbate; fără overwrite | Neexecutat |
| CSV/HTML | Număr găsite/negăsite/ambigue corect, raport inspectabil offline | Neexecutat |
| Folder protejat | Eroare explicată, fără elevare automată/instalare parțială | Neexecutat |
| A doua pornire offline | Baza de date și modulul utilizabile fără instalări suplimentare | Neexecutat |

Marcați fiecare rând cu PASS/FAIL, includeți capturi/erori și `startup-result.json`.
Actualizările online sunt un test separat: endpoint HTTPS aprobat, catalog semnat,
cheie publică și bundle nou. Lipsa conexiunii nu trebuie să oprească aplicația.
