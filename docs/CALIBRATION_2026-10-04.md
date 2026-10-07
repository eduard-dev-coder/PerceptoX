# Calibrare prin feedback — backend, 2026-10-04

`PerceptoX.Application.Calibration` propune profiluri versionate și păstrează o selecție
imutabilă cu rollback. Backend-ul nu accesează filesystem, nu schimbă rezultate deja
afișate și nu mută imagini. Persistența locală și comenzile explicite din UI sunt
responsabilitatea composition root.

## Contracte de integrare

- `FeedbackSample(SourceKey, QueryKey, CandidateKey, PerceptualDistance,
  DifferenceDistance, MultiRegionBestDistance, MultiRegionMatchedRegions, IsSimilar,
  RegionMeasurementDistance = 1)`. Cheile includ identitatea și versiunea fișierelor;
  `SourceKey` identifică originalul comun variantelor de query. Caller-ul filtrează
  biblioteca și profilul de procesare și înlocuiește eticheta aceleiași perechi la editare.
- `ThresholdProfile(Id, Version, CreatedAtUtc, ProcessingProfileId,
  MaxPerceptualDistance = 2, MaxDifferenceDistance = 2, MaximumRegionDistance = 1,
  MinimumMatchedRegions = 1)`. `CreateDefault(processingProfileId)` construiește presetul.
  `ToSearchOptions(topN)` și `ToMultiRegionOptions(topN)` adaptează pragurile căutării.
- `ThresholdCalibrator.Propose(samples, baseline, candidateId, createdAtUtc,
  options = null, cancellationToken = default)` este static și întoarce
  `CalibrationProposal`. Câmpurile: `Status`, `BaselineProfile`, `ProposedProfile`,
  `BaselineHoldout`, `ProposedHoldout`, `TrainingQueryKeys`, `HoldoutQueryKeys`, `Warnings`.
- `CalibrationStatus`: `InsufficientData`, `Rejected`, `Validated`.
- `CalibrationEvaluation`: `Samples`, `Queries`, `TruePositives`, `FalsePositives`,
  `TrueNegatives`, `FalseNegatives`; `Positives`, `Negatives`, `Precision`, `Recall`.
  Precision/recall sunt nullable când numitorul lipsește, nu „100%” implicit.
- `ProfileSelection(Active, Previous = null)`: `Apply(proposal)`, `Rollback()`,
  `Reset(preset)` produc o selecție nouă. Apply respinge date insuficiente, regresii,
  propuneri expirate și profiluri incompatibile. Rollback schimbă Active/Previous;
  al doilea rollback revine la profilul care tocmai a fost înlocuit. Profilurile vechi
  nu sunt modificate sau șterse. Reset păstrează profilul activ anterior pentru rollback.

## Separarea datelor și porțile de suport

Ordinea surselor folosește SHA256 determinist. Aproximativ 25% dintre surse formează
holdout; toate query-urile și variantele unui original rămân în aceeași partiție.
Niciun QueryKey nu poate aparține mai multor SourceKey. Perechile duplicate sunt
respinse: stocarea trebuie să facă upsert înainte de calibrare.

Pragurile minime sunt explicite și pot fi doar înăsprite:

| Condiție | Training | Holdout |
| --- | ---: | ---: |
| Perechi etichetate | 100 | 40 |
| Query-uri distincte | 10 | 5 |
| Exemple Similar | 20 | 20 |
| Exemple Nu similar | 20 | 20 |
| Raportul clasei dominante | cel mult 10:1 | cel mult 10:1 |

Sub aceste praguri, `InsufficientData` și `ProposedProfile = null`: etichetele rămân
utile pentru acumulare, dar nu produc un profil activabil. Pragurile sunt protecții
conservatoare de suport, nu o garanție statistică sau o validare pe fotografii reale.

## Propunere și evaluare

Grid search folosește numai training: pHash/dHash în intervalul baseline ±4 implicit,
limitat la 0–64; ajustarea maximă configurabilă este 8. Obiectivul F0.5 favorizează
precizia. Egalitățile preferă mai puține false positives, praguri p/d mai mici și
mai multe regiuni. Holdout nu alege candidații: este evaluat o singură dată după alegere.
Apply este disponibil numai dacă precizia și recall-ul holdout nu regresează și
numărul false positives nu crește față de baseline. Propunerea poate păstra pragurile
baseline când acestea sunt deja preferate. Nicio propunere nu se aplică automat.

Predicția reproduce filtrul existent: `(p ≤ maxP AND d ≤ maxD) OR potrivire regională`.
Metricile se referă exclusiv la perechile etichetate holdout, înainte de top-N și ordonare.
Ele nu estimează probabilitatea unei potriviri, recall-ul întregii biblioteci sau efectul
limitei top-N. Un set etichetat doar din rezultatele afișate poate omite false negatives;
evaluarea reală necesită și perechi relevante care nu au fost returnate. Refolosirea
holdout pentru tuning repetat manual poate introduce bias; folosiți etichete independente
noi și evaluați separat fotografii reale înainte de a afirma calitatea produsului.

## Limitarea MultiRegion

`MultiRegionMatchedRegions` depinde de `RegionMeasurementDistance`. BestDistance și
un singur count nu permit reconstruirea count la alt prag. Tuner-ul păstrează
`MaximumRegionDistance` la valoarea baseline și respinge măsurători regionale cu alt prag.
Poate ajusta `MinimumMatchedRegions` (1–9) numai cu cel puțin 20 exemple regionale
pozitive și 20 negative în training, iar activarea schimbării cere același suport în
holdout. Fără suport regional, parametrul rămâne baseline. Ajustarea distanței regionale
va necesita măsurători regionale complete/recalcularea hash-urilor, nu extrapolarea count.

## Verificare

`CalibrationTests` acoperă reducerea false positives, holdout care respinge regresia
fără schimbarea pragurilor selectate, separarea query/originalelor, lipsa suportului,
etichete numai pozitive, tuning regional și pragul de măsurare, duplicate, determinism,
apply explicit, stale proposal, reset și rollback imutabil. Sunt teste sintetice funcționale;
validarea de calitate pe fotografii reale rămâne o activitate separată.
