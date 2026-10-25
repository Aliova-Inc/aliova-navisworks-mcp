# Skill 04 — Navigation et Gestion des Viewpoints

## Objectif
Naviguer dans le modele via les points de vue sauvegardes, en creer de nouveaux pour documenter des observations.

## Quand l'utiliser
- Navigation rapide entre zones du projet
- Documentation de problemes ou observations
- Preparation de presentations de coordination
- Sauvegarde de vues pour rapport

## Workflow

### 1. Lister les viewpoints existants
```
Outil : list_viewpoints
```
Retourne l'arborescence complete avec chemins hierarchiques.

### 2. Naviguer vers un viewpoint
```
Outil : goto_viewpoint
Params : path = "Dossier/SousDossier/NomVue"
```

### 3. Creer un viewpoint
```
Outil : save_current_viewpoint
Params : name = "NOM_DESCRIPTIF"
```

## Convention de nommage recommandee

### Format
```
[TYPE]_[DISCIPLINE]_[ZONE]_[DESCRIPTION]_[NUM]
```

### Types
| Prefixe | Usage |
|---|---|
| `REV_` | Revue de coordination |
| `CLASH_` | Documentation de clash |
| `OBS_` | Observation / remarque |
| `VALID_` | Vue validee |
| `PROG_` | Suivi d'avancement |

### Exemples
```
REV_ARC-MEP_N2_SalleElec_001
CLASH_PLB-STR_N1_Tremie_003
OBS_CVC_TOITURE_GaineSurdim_001
VALID_FACADE_EST_001
PROG_STR_N3_Coffrage_002
```

## Cas d'usage

### Revue par niveau
1. Lister les viewpoints → identifier les vues par niveau
2. Naviguer niveau par niveau
3. Creer des viewpoints pour chaque observation

### Documentation de clash
1. Analyser un clash (`get_clash_results`)
2. Isoler les elements (`isolate_items`)
3. Sauvegarder la vue (`save_current_viewpoint`)
4. Nommer selon la convention CLASH_
