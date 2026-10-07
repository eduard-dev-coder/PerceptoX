# PerceptoX — navigare, preferințe rapide și donații

7 octombrie 2026.

## Modificări

- Navigarea este ordonată: **Identifică imaginile → Caută o imagine → Imagini similare → Indexare**. Rutarea folosește tag-uri; indexul inițial al căutării și preview-urile au fost actualizate.
- Sub Setări: același catalog de limbi JSON, un toggle albastru pentru Dark și un buton **Donate** cu inimă roșie. Sunt reutilizate `LanguageManager` și `ThemeManager`, inclusiv salvarea preferințelor pentru următoarea pornire.
- Setările și sidebar-ul se sincronizează în ambele sensuri fără navigare, restart sau rerularea motorului. Pagina Setări se abonează la schimbări numai cât este încărcată și se dezabonează la ieșire.
- Bara de titlu arată cinci mesaje RO/EN, cu inimă roșie. Pauză de 3 secunde între tranziții verticale de 360 ms. Mesajele sunt în JSON, nu în cod; limbile viitoare pot traduce aceleași chei.
- Rotația se oprește când fereastra nu este activă, la hover/focus pe mesaj și cât timp dialogul este deschis. Dacă animațiile Windows sunt dezactivate, mesajul se schimbă fără tranziție. Timer-ul și animația sunt oprite la închiderea ferestrei.
- Mesajele și butonul deschid același dialog mic, centrat, cu antet, inimă, explicație scurtă, donație opțională, buton PayPal și Închide. Light/Dark și limba se actualizează și în dialogul deja deschis; abonamentele sunt eliberate la închidere.
- Destinația este fixă: `https://www.paypal.com/donate/?hosted_button_id=NFBKSEW3T4SFS`. Nu există plăți în aplicație, acces la cont sau deschidere automată a browserului. PayPal se deschide numai la click explicit pe buton; URL-ul poate fi copiat dacă lansarea browserului eșuează.
- Caption-urile native își păstrează culorile. Zona PayPal primește input prin `InputNonClientPointerSource`, iar restul regiunii rămâne pentru drag. Spațiul caption-urilor este rezervat după inset-ul nativ și scala XAML; la ferestre foarte înguste mesajul este ascuns pentru a evita suprapunerea. Implementarea urmează [documentația Microsoft pentru conținut interactiv în title bar](https://learn.microsoft.com/en-us/windows/apps/develop/title-bar?tabs=winui3).

## Verificări

- Build Debug WinUI x64: 0 avertismente, 0 erori.
- 295 teste trecute: Core 31, Application 37, Infrastructure 158, Presentation 69. Cele cinci teste noi verifică destinația HTTPS, pauza, cele cinci texte, ciclul complet RO/EN și indici invalizi.
- JSON: 421 chei RO/EN, referințe și placeholder-e validate. Paletele au chei identice și au trecut verificările existente de contrast.
- Preview-ul dedicat schimbă preferințele din sidebar, apoi invers din Setări, păstrând aceeași pagină, 1.000 de rezultate și 1.000 de selecții. Verifică cinci tranziții complete și revenirea la început; testează separat două tick-uri automate.
- Dialogul este capturat Light/RO, Dark/EN și după schimbare live Light/RO → Dark/EN. Capturile și diagnosticele sunt în `artifacts/sidebar-donation-20261007-v2`; prima încercare v1 este păstrată pentru trasabilitate, dar antetul acesteia a fost corectat.

## Limitele verificării

Capturile sunt ale suprafeței XAML, cu fixture-uri sintetice. Nu certifică randarea nativă a caption-urilor, gesturile mouse/tastatură ale utilizatorului sau funcționarea browserului/contului PayPal. Browserul nu a fost deschis și nu a fost inițiată nicio donație. Nu au fost scanate sau modificate fotografii personale. Gate-urile existente pentru Windows curat și redistribuire publică nu se schimbă prin această modificare.

## Livrare verificată

- Pauza automată a fost măsurată: primul tick după 3,0005 s, al doilea la 3,4046 s după primul (include tranziția verticală de 360 ms și planificarea UI). Testul cu cinci tranziții separate a revenit corect la mesajul inițial.
- Centrarea dialogului: abatere orizontală 0–0,5 px și verticală 0 px în capturile acceptate. Inspectorul de rezultate a materializat maximum 11 containere pentru cele 1.000 de rezultate sintetice.
- Pachet Release: `artifacts/distribution/20261007-105249-86af9c6c/PerceptoX-win-x64.zip`, 120.598.330 bytes. Pachetele anterioare nu au fost suprascrise.
- SHA-256: `847D901E8993026A971A229195DE9FAA22E333B1C7C42933B5212E9AFA935868`.
- ZIP extras într-o cale separată cu spații, verificat cu Windows PowerShell 5.1 și PATH restrâns. Startup: `StartupPassed`, `error: null`, cinci runtime-uri DLL încărcate din pachet.
- Dovadă: `artifacts/validation/sidebar-20261007-86af9c6c/startup/startup-result.json`. `operatorConfirmedOffline` și `cleanMachineVerified` sunt false: nu este o certificare pentru Windows curat sau offline strict.
- Sursele și materialele terțe sunt alături de ZIP. Raportul din workspace include aceste rezultate de livrare adăugate după generarea arhivei de surse; codul livrat nu s-a schimbat.
