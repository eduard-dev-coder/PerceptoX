# Manager module PerceptoX

## Comportament

Panoul din colțul dreapta-jos deschide „Module PerceptoX”. Nu acoperă rezultatele:
are rând propriu în shell. Baza .NET/WinUI/SQLite este inclusă și funcționează
fără instalare globală. Aceste biblioteci se actualizează împreună cu aplicația,
nu prin înlocuirea arbitrară a DLL-urilor în timpul rulării.

Decoderul HEIC/HEIF/AVIF este livrat în `codecs/imagemagick`, **ca payload**.
La prima pornire nu este activat/executat automat. JPEG/PNG/WebP și celelalte
formate native rămân utilizabile. Utilizatorul deschide Module, citește
descrierea/licențele, bifează confirmarea și apasă „Instalează / Repară din pachet”.
Nu sunt instalatoare externe, modificări PATH/registry sau elevare automată.

Instalarea verifică manifestul și SHA-256, copiază într-un staging GUID,
verifică din nou și probează runtime-ul, apoi publică un pointer atomic spre
un slot nou. Progresul și Anulare sunt disponibile. Anularea/eșecul înainte
de publicare păstrează pointerul anterior. Niciun DLL activ nu este suprascris.
La final se cere închiderea și redeschiderea aplicației; nu repornim forțat și
nu închidem operații/fotografii ale utilizatorului. Sloturile anterioare sunt
păstrate, nu există încă buton UI de rollback/ștergere a sloturilor.

Runtime-ul activ este validat la pornire înainte de execuție. Un slot corupt
nu este folosit; interfața oferă repararea. SHA-256 local detectează modificări,
dar nu este o semnătură a distribuției împotriva unui atacator care poate modifica
și inventarul. Sursa bundle-ului de release trebuie verificată de distribuitor.

Locația app-local trebuie să fie inscriptibilă pentru instalare/actualizare.
Pentru un folder protejat Windows se afișează eroare; mutați folderul într-o
locație permisă. Aplicația nu instalează silențios în altă locație.

## Online: disabled până la configurarea unei surse aprobate

Nu am inventat o adresă de server sau o cheie de semnare. Configurația necesară
este `modules/update-source.json` în folderul aplicației:

```json
{
  "catalogUrl": "https://DOMENIUL-APROBAT/catalog.json",
  "publicKeyPem": "-----BEGIN PUBLIC KEY-----\nCHEIA-PUBLICA-RSA-3072...\n-----END PUBLIC KEY-----"
}
```

Acesta este un exemplu, nu un endpoint funcțional sau o cheie reală.
Până la configurare, butoanele online sunt dezactivate și nu se contactează
internetul. După configurare, utilizatorul poate verifica manual ori bifa
„Permit verificarea online la pornirile următoare”. Preferința este implicit
oprită. Verificarea la pornire **doar notifică**, nu descarcă/instalează un modul.

Catalogul semnat are payload base64 și semnătură RSA-PSS/SHA-256. Se verifică:
cheie publică de minimum 3072 biți, protocol `perceptox-module-v1`, modulul
`imagemagick`, arhitectura `win-x64`, expirare, versiune mai nouă și HTTPS.
Pachetul trebuie să fie pe același host/port ca sursa; redirect-urile nu sunt
urmate. Pachetele nu provin din „ultima versiune” arbitrară de pe upstream.

Actualizarea oferită afișează versiunea, dimensiunea, host-ul și notificarea
de licență. Checkbox-ul de confirmare se resetează la fiecare ofertă. Descărcarea
începe numai după o confirmare nouă. Se verifică dimensiunea exactă și digestul
semnat, apoi se extrage în staging cu limite, fără path traversal/symlinks/ADS,
se verifică manifestul fișierelor și se folosește aceeași instalare pe sloturi.
Limite: catalog 64 KiB, download 128 MiB, output 256 MiB, 1024 intrări ZIP,
512 fișiere în manifest. Nu se dezactivează TLS pentru a ocoli erorile.
Verificarea catalogului are termen total de 60 secunde, iar descărcarea și
pregătirea update-ului 3 minute; Anulare poate opri operația înainte de publicare.

`eng/build-module-update.ps1` construiește local ZIP și catalog semnat din
bundle-ul revizuit, cu cheie privată **furnizată separat**. Nu generează sau
publică automat o cheie, nu încarcă fișiere pe internet și nu include cheia
privată în pachet. Publicarea serverului/catalogului necesită alegere și autorizare.

## Licențe și surse

Codul propriu PerceptoX: **GPL-3.0-only**, conform alegerii utilizatorului.
`LICENSE` și `NOTICE` sunt incluse. Arhiva `PerceptoX-sources.zip` este generată
dintr-o listă de directoare/extensii, fără bin/obj/vendor/.packages și fără
chei private, cache, loguri sau colecții personale de imagini. Sursele native
Windows ale ImageMagick/delegaților sunt livrate separat în `ThirdParty-sources`;
rebuild/relink și analiza completă a condițiilor sunt gate separat, împreună cu drepturile graficii
și compatibilitatea distribuției GPL cu termenii componentelor Microsoft.

Nu declarăm închis gate-ul public doar pentru că avem un LICENSE sau o confirmare
de instalare. Pentru ImageSharp se verifică licența exactă a versiunii folosite;
un update poate schimba termenii. Dependențele nu sunt relicențiate GPL prin
alegerea licenței proiectului.
