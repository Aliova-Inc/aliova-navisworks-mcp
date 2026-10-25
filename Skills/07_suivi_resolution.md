# Skill 07 — Suivi de Resolution des Clashs

## Objectif
Suivre l'evolution de la resolution des interferences entre versions de maquette. Mesurer la progression et identifier les blocages.

## Quand l'utiliser
- Suivi hebdomadaire de coordination
- Preparation de reunion de synthese
- Reporting au maitre d'ouvrage
- Identification des lots en retard

## Workflow

### 1. Etat actuel des clashs
```
Outil : list_clash_tests
```
Pour chaque test, noter :
- New : nouveaux clashs (regression ou nouvel ajout)
- Active : en cours de traitement
- Resolved : resolus par le lot responsable
- Approved : valides par le BIM Manager

### 2. Analyse detaillee des clashs actifs
```
Outil : get_clash_results
Params : testName = "...", maxResults = 500
```
Filtrer par statut pour identifier :
- **New** : a trier et assigner
- **Active** : en attente de resolution — depuis combien de temps ?

### 3. Inspection des clashs bloquants
Pour chaque clash critique non resolu :
```
Outil : get_item_properties → identifier les elements
Outil : isolate_items → visualiser
Outil : save_current_viewpoint → documenter
```

## Tableau de bord

### Synthese globale

| Test | New | Active | Resolved | Approved | Total | Taux resolution |
|---|---|---|---|---|---|---|
| ARC vs CVC | 5 | 12 | 45 | 30 | 92 | 81% |
| STR vs PLB | 0 | 3 | 20 | 15 | 38 | 92% |
| ... | | | | | | |

### Evolution (a remplir a chaque revue)

| Date | Total clashs | New | Active | Resolved | Approved | Taux |
|---|---|---|---|---|---|---|
| 2026-04-27 | ... | ... | ... | ... | ... | ...% |
| 2026-04-20 | ... | ... | ... | ... | ... | ...% |

### Clashs critiques non resolus (> 2 semaines)

| # | Test | Elements | Lot responsable | Depuis | Bloquant |
|---|---|---|---|---|---|
| 1 | ... | ... | ... | ... | Oui/Non |

## Indicateurs
- **Taux de resolution global** : (Resolved + Approved) / Total
- **Tendance** : amelioration / stagnation / regression
- **Lot le plus impacte** : discipline avec le plus de clashs actifs
- **Delai moyen de resolution** : temps entre New et Resolved
