# Faza 5 — profil multi-region pentru crop și screenshot

Faza 5 adaugă un al treilea fingerprint activ, `multiregion`, fără a modifica schema SQLite generică din Faza 3. Profilul este experimental și completează pHash/dHash; nu le înlocuiește și nu este o dovadă că două fișiere sunt duplicate.

## Alegerea spike-ului

Au fost analizate trei direcții: BlockHash, WaveletHash și multi-hash crop-resistant. BlockHash și WaveletHash rămân hash-uri globale și nu rezolvă singure pierderea unei zone mari din imagine. Implementarea de referință `imagehash.crop_resistant_hash` segmentează imaginea în regiuni luminoase/întunecate, produce un hash pentru fiecare segment și compară hash-urile între regiuni. Rezultatul are lungime variabilă, ceea ce complică snapshot-ul compact și căutarea la un milion de imagini. Referință: [JohannesBuchner/imagehash](https://github.com/JohannesBuchner/imagehash/blob/master/imagehash/__init__.py).

Profilul PerceptoX v1 folosește deliberat o variantă fixă și măsurabilă:

- imagine grayscale `128×128`, aceeași orientare/flattening și același resize ImageSharp ca restul pipeline-ului;
- nouă regiuni normalizate: imagine completă, centru, patru colțuri și trei inset-uri;
- câte un dHash de 64 biți pentru fiecare regiune;
- 576 biți / 72 bytes per imagine, persistați în `ImageFingerprints` sub `AlgorithmId=multiregion`;
- comparație cross-region cu `BitOperations.PopCount`;
- prag implicit: distanță regională maximă `1`, minimum o regiune potrivită.

Aceasta nu este o portare compatibilă binar a algoritmului Python și nu trebuie numită implementarea lucrării ARES. Este un profil bounded propriu, ales pentru schema și ținta de memorie PerceptoX.

## Integrare

`MultiRegionSearchIndexSnapshot` stochează ID-urile ordonate și toate hash-urile într-un buffer contiguu. Nu creează câte un obiect sau `byte[]` pentru fiecare imagine. `MultiRegionSearchService` păstrează numai Top-N într-un `PriorityQueue`, verifică anularea periodic și are praguri independente de pHash/dHash.

Căutarea hibridă unește Top-N-ul baseline cu Top-N-ul multi-region, recalculează distanțele pHash/dHash pentru candidații recuperați prin regiuni și returnează un scor comun **necalibrat**. CSV și HTML includ distanța regională și numărul de regiuni potrivite. O schimbare a descriptorului intră automat în `ProcessingProfileId` și forțează reindexarea, astfel încât fingerprint-uri incompatibile nu sunt amestecate.

## Calibrare procedurală

Set: 1.000 query-uri originale, nouă variante per query, Top-10, pHash/dHash `2/2`.

| Prag multi-region / regiuni minime | Precision | Recall global |
|---|---:|---:|
| dezactivat, baseline Faza 4 | 73,41% | 53,59% |
| 0 / 1 | 74,66% | 60,51% |
| **1 / 1, profil implicit** | **73,33%** | **69,20%** |
| 2 / 1 | 69,94% | 75,08% |
| 2 / 2 | 75,64% | 61,06% |

Recall per transform pentru profilul implicit `1/1`:

| Transformare | Recall |
|---|---:|
| compressed-40 | 99,10% |
| compressed-75 | 99,90% |
| crop-center | 48,40% |
| crop-corner | 24,70% |
| overlay | 9,50% |
| resized-large | 100,00% |
| resized-small | 99,70% |
| screenshot-frame | 44,70% |
| thumbnail | 96,80% |

Profilul `1/1` a fost ales deoarece păstrează precision aproape identic cu baseline-ul și adaugă 15,61 puncte procentuale de recall. Cifrele provin din imagini procedurale cu pattern-uri repetitive; nu se generalizează la fotografii reale.

## Performanță

BenchmarkDotNet, Top-50, hash-uri pseudo-aleatoare, cinci iterații, încărcarea SQLite exclusă:

| Intrări | Medie | Alocări/query |
|---:|---:|---:|
| 100.000 | 30,75 ms | 384 B |
| 1.000.000 | 295,30 ms | 384 B |

Snapshot-ul necesită aproximativ 80 MB la un milion de imagini: 72 MB hash-uri și 8 MB ID-uri, fără overhead per intrare. Căutarea este încă liniară. Latența este acceptabilă pentru un fallback interactiv experimental, dar o structură de candidate generation trebuie măsurată înainte de extinderea numărului de regiuni.

## Riscuri rămase

- o singură regiune foarte asemănătoare poate produce false positives, mai ales pe texturi repetitive;
- rotația, perspectiva, deformarea și modificările mari de iluminare nu sunt compensate;
- overlay-ul/textul peste o zonă mare rămâne slab;
- regiunile fixe favorizează crop-urile apropiate de ferestrele predefinite;
- un corpus foto real trebuie să includă scene întunecate, texturi uniforme/repetitive, rotații, watermark, zgomot și imagini fără legătură dar cu layout similar;
- nicio potrivire nu autorizează ștergerea sau mutarea automată.

Ollama a fost folosit numai pentru enumerarea cazurilor-limită. Alegerea profilului și pragurilor provine din build, teste, evaluarea etichetată și benchmark-urile locale.

## Gate

Gate-ul de inginerie al Fazei 5 este trecut: 81/81 teste trec, inclusiv `ShouldFindCroppedImage` la pragul implicit; pipeline-ul indexează 10.000/10.000 imagini fără erori, precision procedural nu regresează material față de baseline, iar costul la 1.000.000 de intrări este măsurat. Gate-ul de produs/calitate rămâne deschis până la validarea pe un corpus foto real reprezentativ.
