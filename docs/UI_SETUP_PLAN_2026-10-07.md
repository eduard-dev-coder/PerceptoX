# Plan — corecții UI, Despre și setup offline

7 octombrie 2026. Cerințele utilizatorului sunt aprobate pentru implementare.

1. **Titlebar:** eliminăm măsurarea după lungimea textului; zonă stabilă, centrată, cu elipsă la ferestre înguste. Pauză 6 secunde; mișcare verticală, fade și blur discret accelerat GPU. Respectăm dezactivarea animațiilor Windows și fallback fără blur dacă GPU-ul nu permite efectul. Verificăm geometria înainte/după fiecare mesaj, inclusiv la RO/EN.
2. **Tema:** eticheta arată tema activă, „Temă Light” / „Temă Dark”, și se schimbă simultan cu toggle-ul și setările.
3. **Despre:** link sub Donate, dialog din aceeași familie vizuală, logo adaptat Light/Dark, versiune din metadatele build-ului, autor Prepelita Eduard, `eduard.condact.dev@gmail.com`, descriere scurtă și Închide. Limba/tema se schimbă live; mailul nu se deschide automat.
4. **Pachet ordonat:** `PerceptoX.exe` în root; `app` păstrează împreună DLL/PRI/XBF/runtime-uri și resursele, `licenses` păstrează licențele, `docs` documentația. Launcher nativ x64, CRT static, fără dependență .NET proprie, cale absolută și argumente transmise fără shell. Nu mutăm DLL-urile arbitrar și nu ștergem fișiere din livrările existente. Adaptăm manifestul și testele de startup.
5. **Inno Setup:** build repetabil, per-user implicit, fără UAC și fără download la instalare. Pachet complet .NET/WinUI/SQLite și codecuri offline. GPL și licențele terțe sunt prezentate înainte de instalare, cu confirmări explicite. Codecurile se activează app-local numai după acord. Scurtături Start Menu și Desktop configurabile; dezinstalatorul nu șterge biblioteci, rapoarte sau datele utilizatorului. Nu modificăm PATH și nu instalăm ImageMagick global.
6. **Verificare:** build/teste, JSON/teme, capturi XAML Light/RO și Dark/EN, timer/poziție/fade, dialog Despre și startup ZIP din cale cu spații. Compilăm setup-ul cu un Inno Setup oficial verificat; testăm instalarea într-un director propriu de validare, fără a atinge o instalare existentă. Instalarea automată în test necesită confirmarea explicită a licențelor în parametrul de test; utilizatorul final primește wizard-ul interactiv.

## Limite și gate-uri păstrate

- Instalatorul nu transformă inventarul de licențe în conformitate juridică. Gate-urile pentru surse/relink ale delegaților, termeni Microsoft, drepturi grafice, semnarea executabilului și Windows curat rămân distincte. Pachetul de validare nu este declarat autorizat pentru redistribuire publică.
- Semnătura Authenticode a setup-ului PerceptoX necesită certificatul autorului; fără certificat livrăm explicit un build nesemnat, nu pretindem eliminarea avertismentelor SmartScreen.
- Capturile XAML nu certifică interacțiunile native Windows. Nu scanăm fotografii personale nespecificate și nu inițiem donații.

## Surse tehnice primare

- [Microsoft — efecte Composition](https://learn.microsoft.com/en-us/windows/apps/develop/composition/composition-effects)
- [Microsoft — CompositionEffectBrush și GaussianBlur](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.composition.compositioneffectbrush)
- [Inno Setup — distribuții oficiale](https://jrsoftware.org/isdl.php)
- [Inno Setup — verificarea semnăturilor](https://jrsoftware.org/isdl-verify.php)

## Starea implementării

- Corecții titlebar/temă și dialog Despre: implementate; geometrie și capturi Light/RO, Dark/EN verificate.
- Livrare ordonată și launcher nativ: implementate; ZIP pornit din cale cu spații, runtime-uri încărcate app-local.
- Setup offline și acord pentru licențe: implementate; instalare, activare codecuri și dezinstalare izolate trecute.
- Traduceri setup: 281 mesaje RO/EN cu paritate de placeholders; draftul completărilor a fost delegat către Gemma 4 local și verificat înainte de integrare.
- Gate-urile distincte pentru Windows curat, semnare și redistribuire publică rămân deschise; nu sunt ascunse de succesul testelor tehnice.
