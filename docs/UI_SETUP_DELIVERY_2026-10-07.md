# PerceptoX 1.0.0 — livrarea validată

7 octombrie 2026. Build pentru utilizare personală și validare, nesemnat; nu este declarat autorizat pentru redistribuire publică.

## Fișiere de folosit

- [Setup offline Windows x64](D:/Aplicatii/SimilarPhoto/artifacts/distribution/20261007-161321-0cdcdb56/Setup/PerceptoX-1.0.0-Setup-win-x64.exe)
- [Pachet standalone ZIP](D:/Aplicatii/SimilarPhoto/artifacts/distribution/20261007-161321-0cdcdb56/PerceptoX-win-x64.zip)
- [Executabilul din folderul standalone](D:/Aplicatii/SimilarPhoto/artifacts/distribution/20261007-161321-0cdcdb56/PerceptoX-win-x64/PerceptoX.exe)
- [Sursele PerceptoX, inclusiv launcher și scripturi Inno](D:/Aplicatii/SimilarPhoto/artifacts/distribution/20261007-161321-0cdcdb56/PerceptoX-sources.zip)
- [Raport detaliat de implementare și limite](D:/Aplicatii/SimilarPhoto/docs/UI_SETUP_IMPLEMENTATION_2026-10-07.md)

Nu combinați fișierele cu o livrare mai veche. Pentru ZIP, păstrați întregul folder:

```text
PerceptoX-win-x64/
  PerceptoX.exe
  package-manifest.json
  app/
  licenses/
  docs/
```

În ZIP, activarea codecurilor se confirmă din panoul Module. În setup, acceptarea licențelor declanșează instalarea și activarea offline. Wizard-ul normal oferă scurtături Desktop/Start. Nu trebuie instalate separat .NET, WinUI, SQLite sau ImageMagick. Setup-ul nu descarcă module și nu modifică PATH.

## Identitate SHA-256

- Setup: `946573B928BDA0A480F85E0C682EF8B6814D29EBC9015EAAD8A78B63A1C5AFFC`
- ZIP: `AD9ABEA24D13B79664A91D13C7B3C7F17E23FEA08C210068BC3F843E61469FD1`

## Validarea exactă a acestei livrări

- Compilare Inno reușită, fără avertismente de compilare; jurnal în `Setup/compiler.log`.
- 281 mesaje Inno RO/EN și placeholders verificați; 429 chei de aplicație RO/EN verificate.
- Teste automate: 295 trecute, niciun test eșuat sau omis.
- ZIP final extras din arhivă într-o cale cu spații, pornit prin `PerceptoX.exe`: `artifacts/validation/final-zip-20261007-161321/startup/startup-result.json`, status `StartupPassed`. Toate cele cinci runtime-uri verificate au fost încărcate din `app`.
- Setup exact identificat de hash-ul de mai sus: `artifacts/validation/setup-20261007-161607-4d598725/setup-result.json`, status `Passed`.
- Fără acord silent: exit 1. Cu acord: exit 0. Codecurile 7.1.2.26 au fost activate app-local și aplicația instalată a pornit.
- Apelul de instalare a codecurilor fără acord, transmis prin launcher: exit 2, pointer activ nemodificat.
- Dezinstalare: exit 0; fișierele personale de test au fost păstrate; PATH și prezența înregistrării de uninstall au rămas neschimbate în modul de validare.
- Verificările UI folosesc `artifacts/ui-setup-20261007-v2`; includ stabilitatea poziției mesajelor, pauza de 6 secunde, Light/Dark, dialogul Despre și păstrarea celor 1.000 de rezultate/selecții. Aceste capturi nu certifică toate interacțiunile native Windows.
- Gemma 4 local a furnizat draftul pentru 23 de mesaje românești Inno; draftul a fost revizuit și verificat înainte de integrare.

Copiile de instalare folosite în test au fost dezinstalate. Nu a fost dezinstalată aplicația utilizatorului și nu au fost scanate/șterse fotografii personale. Materialele din încercările anterioare sunt păstrate, nu suprascrise.

## Gate-uri încă deschise

1. Test complet pe un Windows x64 curat, offline, fără runtime-uri preinstalate. Testele curente sunt pe calculatorul de dezvoltare.
2. Certificat și semnare Authenticode PerceptoX; în prezent pot apărea avertismente SmartScreen.
3. Închiderea obligațiilor de redistribuire pentru componentele native și drepturile grafice, inclusiv reconcilierea metadatelor de licență Win2D. Inventarul și acceptarea unui wizard nu constituie autorizare juridică de publicare.
4. Verificarea interactivă a wizard-ului normal și a scurtăturilor. Testul automat folosește intenționat `/VALIDATIONMODE=YES`, fără scurtături sau înregistrare în registry, pentru a nu modifica instalarea utilizatorului.

Dezinstalatorul nu șterge recursiv fișiere necunoscute. Starea generată ulterior în `app/modules/installed` poate rămâne; nu pretindem eliminarea tuturor urmelor sau o curățare completă a datelor utilizatorului.
