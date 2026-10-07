# PerceptoX — uz personal și posibilă distribuție open-source

Verificare: 2026-10-04. Scopul confirmat este distribuție gratuită, cu surse
disponibile și utilizare comercială permisă, cu respectarea GPL și licențelor terțe.
Utilizatorul a ales explicit **GPL-3.0**; codul propriu este GPL-3.0-only.
Nu sunt echivalente „utilizare privată”, „distribuție
gratuită” și „open-source”. Acceptul instalării nu înlocuiește drepturile sau
obligațiile licențelor. Acest inventar nu este o certificare juridică.

## Decizii și limite

- Păstrăm dependențele fixate în versiuni; nu instalăm automat „ultima versiune”.
- Păstrăm LICENSE/NOTICE și atribuirile componentelor în folderul livrat.
- Managerul de module este implementat pentru instalare/reparare app-local cu confirmare;
  actualizările online au nevoie de sursă/cheie configurate și cer
  verificări de compatibilitate, autenticitate și licență înainte de aplicare.
- Un update major al unei biblioteci poate avea alți termeni: nu se moștenește
  automat analiza pentru ImageSharp 3.1.12 sau bundle-ul ImageMagick actual.
- MIT a fost respinsă. LICENSE GPL-3.0-only și NOTICE sunt incluse, iar arhiva
  surselor proprii este generată. Nu s-a publicat proiectul pe un server/repository.
- GPL pentru PerceptoX nu transformă DLL-urile terțe în componente GPL; în special
  termenii Microsoft și compatibilitatea pachetului binar complet cu GPL trebuie
  verificați separat înainte de redistribuire. Nu adăugăm automat o excepție de linking.

## Verificări pe componente

| Componentă | Rezultat / acțiune înainte de redistribuire |
| --- | --- |
| ImageSharp **3.1.12** | Aplicăm Apache-2.0 pe criteriul utilizării în PerceptoX open-source GPL-3.0-only, ales explicit. Păstrăm textul Apache, atribuirea și metadatele upstream. Gratuitatea singură nu este criteriul folosit; licența versiunilor viitoare trebuie reverificată. |
| ImageMagick | Licența proprie permite redistribuire cu condiții; License/NOTICE sunt deja păstrate. Configurația `policy.xml` modificată este identificată ca modificare PerceptoX. |
| Delegații ImageMagick | Bundle-ul actual include multe biblioteci, nu doar HEIC/AVIF. NOTICE identifică libheif, libde265 și alte biblioteci LGPL. Înainte de redistribuirea binarelor trebuie verificată fiecare componentă efectiv inclusă și furnizate sursele corespunzătoare și celelalte materiale/condiții aplicabile build-ului, inclusiv posibilitatea de înlocuire/relink unde este cerută. |
| .NET, CommunityToolkit, Serilog, SQLitePCLRaw | Sunt incluse metadatele pachetelor efectiv publicate, atribuiri și textele/notificările disponibile. Licențele componentelor native/transitive nu sunt înlocuite de eticheta NuGet a unui wrapper. |
| Windows App SDK / VC runtime | Se păstrează termenii Microsoft, iar distribuția respectă drepturile/condițiile furnizorului; nu se declară componente MIT ale PerceptoX. |

Nu există obligația generică de a publica sursele doar pentru folosirea privată
a unui program GPL, dar redistribuirea schimbă analiza. Această regulă nu este
o derogare de la condițiile Six Labors sau Microsoft.

## Gate pentru un release open-source

1. Confirmarea licenței codului propriu și a titularului drepturilor; excluderea
   materialelor terțe din grantul propriu, inventar și atribuiri.
2. Pregătirea surselor PerceptoX, fără secrete, date personale, colecții de
   fotografii, cache, loguri sau artefacte de dezvoltare inutile. Publicarea
   efectivă necesită autorizare separată.
3. Colectarea și verificarea surselor/build-urilor exacte pentru componentele
   redistribuite cu obligații de sursă. Un link generic către upstream și
   un simplu NOTICE nu certifică îndeplinirea tuturor obligațiilor.
4. Verificarea drepturilor asupra graficii și fixture-urilor incluse, precum și
   a condițiilor Microsoft pentru destinatari.
5. Manifest de release revizuit, inventar de licențe și verificarea dependențelor.

**Stare (2026-10-05):** scopul gratuit/open-source și GPL-3.0 sunt confirmate. Managerul și
sursele proprii sunt implementate; arhiva oficială Windows ImageMagick 7.1.2-26
este colectată și verificată SHA-256, inclusiv versiunile HEIF/de265 și fișierele
de build. Sunt livrate materiale terțe separat; verificarea completă sursă/build/relink,
sursa de update,
verificarea graficii și compatibilitatea pachetului complet cu GPL rămân deschise.
Gate-ul public nu este declarat închis.

## Surse primare

- [Licența exactă ImageSharp v3.1.12](https://github.com/SixLabors/ImageSharp/blob/v3.1.12/LICENSE)
- [OSI: textul licenței MIT](https://opensource.org/license/mit)
- [ImageMagick: License](https://imagemagick.org/license/)
- [libheif: repository și COPYING](https://github.com/strukturag/libheif)
- [GNU FAQ: uz privat și distribuirea surselor](https://www.gnu.org/licenses/gpl-faq.html#GPLRequireSourcePostedPublic)
- Termenii exact incluși în `licenses` și `codecs/imagemagick/NOTICE.txt` ale pachetului publicat.
