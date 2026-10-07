# Surse terțe pentru decoderul Windows

Arhiva oficială este colectată de `eng/fetch-codec-sources.ps1`. Nu rulează un
installer și nu execută codul din arhivă. Download-ul este fixat la versiunea
7.1.2-26; dimensiunea și SHA-256 provin din metadata release-ului upstream.

- Release: https://github.com/ImageMagick/ImageMagick/releases/tag/7.1.2-26
- Arhivă: `ImageMagick-7.1.2-26-Windows.7z`, 796.180.195 bytes.
- SHA-256: `CC94B39F81CEDB0AB4991E510EBCAE13F891E038210DDC6402D15562C9448C17`.
- Conține `ImageMagick`, `Dependencies` și `Configure`, inclusiv surse, licențe,
  fișierele de build și workflows upstream. Nu este doar arhiva surselor Unix.
- Verificate în CMakeLists: libheif 1.23.0 și libde265 1.1.1, aceleași versiuni
  declarate în NOTICE al payload-ului binar.

Publicarea locală copiază arhiva fără recomprimare, NOTICE și proveniența în
`ThirdParty-sources`, lângă ZIP-ul aplicației și ZIP-ul surselor PerceptoX.
Sursele nu trebuie instalate pe calculatorul utilizatorului pentru a rula aplicația.
La distribuire furnizați și materialele de sursă, nu numai executabilul.

## Modificări și înlocuirea bibliotecilor

PerceptoX nu modifică sursele native ImageMagick/delegaților. Singura modificare
din payload este `policy.xml`, păstrată în sursele PerceptoX ca
`eng/codec-policy.xml`. Nu sunt eliminate atribuirile și licențele upstream.

Pentru reconstruire, extrageți arhiva pe un calculator de dezvoltare și urmați
`Dependencies/Readme.md` și workflows Windows din `ImageMagick/.github`.
Toolchain-ul de dezvoltare nu reprezintă o dependență pentru utilizatorul final.
Revizuiți opțiunile x64/Q16/HDRI, ABI-ul și runtime-ul C++ înainte de înlocuire.

Pentru un payload propriu/reconstruit, folosiți `eng/prepare-codec-bundle.ps1`
într-un checkout nou, apoi `eng/publish-standalone.ps1`. Manifestul offline este
un inventar SHA-256, nu o restricție bazată pe o cheie secretă a autorului.
Instalarea/repararea verifică noul payload și creează un slot nou; după restart,
acesta este activ. Nu modificați DLL-urile unui slot activ: verificarea de
integritate va respinge slotul. Nu suprascrieți fotografii sau baze de date.

## Limita verificării

Arhiva oficială de aceeași versiune și sursele celor doi delegați HEIF sunt
verificate; aceasta nu certifică singură fiecare ABI, flag de compilare sau
condiție LGPL pentru toate DLL-urile din bundle. Rebuild-ul/relink-ul întregului
bundle nu a fost executat. Analiza componentelor native/Microsoft și compatibilitatea
pachetului complet cu GPL rămân gate-uri distincte înainte de distribuția publică.
