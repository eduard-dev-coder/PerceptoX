# Vectori golden și comparația cu ImageHash

Testele din `tests/PerceptoX.Infrastructure.Tests/ImageHashOracleTests.cs` generează două imagini PNG deterministe, apoi calculează pHash/dHash atât cu PerceptoX, cât și cu pachetul de referință `CoenM.ImageSharp.ImageHash` 1.3.6. Rezultatele sunt fixate ca vectori golden; testul nu presupune egalitate între profiluri diferite.

| Pattern | PerceptoX pHash | PerceptoX dHash | ImageHash pHash | ImageHash dHash |
|---|---|---|---|---|
| 0 | `12047DD53F421FE1` | `0103030303073EE6` | `9135299A2A7A8E3F` | `FEFCECECFCF8C119` |
| 1 | `09023B2A7F2A7F2A` | `0A4D52A54AB54EAD` | `802A7D2ABD0A7FAA` | `B592A55AB54AB152` |

Referința inspectată: [CoenM/ImageHash](https://github.com/coenm/ImageHash), branch `develop`, commit `80fd83ac27011031f54faccbd34c6158bd7027d4`; testele rulează pachetul public [1.3.6](https://www.nuget.org/packages/CoenM.ImageSharp.ImageHash/1.3.6) împreună cu ImageSharp 3.1.12 rezolvat de soluția noastră. Biblioteca este o dependență numai de test, nu de runtime.

Diferențele sunt intenționate și cer descriptor/profil distinct:

- PerceptoX pHash reduce la 32×32, folosește grayscale Bt709, mediana celor 63 coeficienți AC și lasă bitul DC nefolosit. ImageHash folosește 64×64, Bt601 și mediana a 64 de coeficienți, incluzând DC.
- PerceptoX dHash setează bitul când pixelul din stânga este **mai luminos** decât cel din dreapta; ImageHash îl setează când stânga este **mai întunecată**. Și aici Bt709 versus Bt601 și resampling-ul pot schimba rezultatul.
- PerceptoX aplică AutoOrient și fundal alb înainte de clonele de calcul. Implementările ImageHash au altă ordine a procesării. Nu comparăm numeric fingerprint-uri din cele două profiluri ca și când ar aparține aceluiași algoritm.

Vectorii detectează schimbări accidentale. Ei nu demonstrează precizie/recall și nu calibrează pragurile de potrivire; acestea cer un corpus real, licențiat și etichetat.
