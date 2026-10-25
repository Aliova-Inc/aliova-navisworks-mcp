# Skill 09 — Controle Qualite de la Maquette

## Objectif
Verifier la qualite et la conformite de la maquette federee selon les exigences BIM du projet (convention BIM, cahier des charges).

## Quand l'utiliser
- Reception d'une nouvelle version de maquette d'un lot
- Audit periodique qualite
- Avant livraison / jalon projet
- Verification de conformite a la convention BIM

## Workflow

### 1. Verification structurelle du modele
```
Outil : get_document_info
```
- [ ] Unites correctes (Metres)
- [ ] Tous les modeles attendus sont presents
- [ ] Pas de modele en double

### 2. Verification des elements par discipline
```
Outil : search_by_name (pour chaque categorie)
```
Verifier la presence des categories d'elements attendues :

| Discipline | Categories attendues | Recherche |
|---|---|---|
| ARC | Walls, Doors, Windows, Floors, Ceilings, Stairs, Railings | `search_by_name` |
| STR | Structural Columns, Structural Framing, Structural Foundations, Floors | `search_by_name` |
| CVC | Ducts, Duct Fittings, Mechanical Equipment, Air Terminals | `search_by_name` |
| PLB | Pipes, Pipe Fittings, Plumbing Fixtures, Pipe Accessories | `search_by_name` |
| ELE | Cable Trays, Conduits, Electrical Equipment, Lighting Fixtures | `search_by_name` |

### 3. Verification de la georeferencement
Verifier via les bounding boxes que les modeles sont bien positionnes :
```
Outil : search_by_name → lire les BoundingBox
```
- Tous les modeles doivent avoir des coordonnees coherentes
- Pas de modele a l'origine (0,0,0) si le projet est georefence

### 4. Verification des niveaux
```
Outil : search_by_name
Params : name = "Level" ou nom du niveau
```
- Les niveaux sont coherents entre disciplines
- Nomenclature conforme a la convention

### 5. Detection d'anomalies visuelles
```
Outil : isolate_by_multi_search (branches par discipline)
```
Pour chaque discipline isolee, verifier :
- [ ] Pas d'elements flottants ou deconnectes
- [ ] Pas d'elements hors emprise du batiment
- [ ] Coherence geometrique generale

## Checklist qualite

### Conformite modele

| Critere | Statut | Commentaire |
|---|---|---|
| Unites correctes | OK/KO | |
| Modeles complets | OK/KO | |
| Pas de doublons | OK/KO | |
| Georeferencement | OK/KO | |
| Niveaux coherents | OK/KO | |
| Nomenclature conforme | OK/KO | |

### Conformite par lot

| Lot | Modele present | Categories completes | Proprietes renseignees | Anomalies |
|---|---|---|---|---|
| ARC | OK/KO | OK/KO | OK/KO | ... |
| STR | OK/KO | OK/KO | OK/KO | ... |
| CVC | OK/KO | OK/KO | OK/KO | ... |
| PLB | OK/KO | OK/KO | OK/KO | ... |
| ELE | OK/KO | OK/KO | OK/KO | ... |

### Anomalies detectees

| # | Lot | Type | Description | Gravite | Action |
|---|---|---|---|---|---|
| 1 | ... | Geometrique | ... | Bloquant/Mineur | ... |
