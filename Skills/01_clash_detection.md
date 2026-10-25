# Skill 01 — Analyse des Clashs (Clash Detection)

## Objectif
Identifier, analyser et rapporter les interferences geometriques entre disciplines du modele federe.

## Quand l'utiliser
- Revue de coordination interdisciplinaire
- Avant une reunion de synthese
- Validation avant soumission de maquette
- Suivi de resolution des clashs

## Workflow

### 1. Lister les tests de clash existants
```
Outil : list_clash_tests
```
Recupere tous les tests avec leurs compteurs : New, Active, Resolved, Approved.

### 2. Analyser un test specifique
```
Outil : get_clash_results
Params : testName = "nom du test", maxResults = 200
```
Pour chaque clash : elements A et B impliques, distance, statut.

### 3. Inspecter un element en conflit
```
Outil : get_item_properties
Params : guid = "GUID de l'element"
```
Recuperer les proprietes (discipline, niveau, categorie) pour comprendre la nature du conflit.

### 4. Isoler les elements en conflit
```
Outil : isolate_items
Params : guids = "guidA,guidB"
```
Visualiser uniquement les 2 elements en conflit dans la vue 3D.

### 5. Sauvegarder un point de vue
```
Outil : save_current_viewpoint
Params : name = "CLASH_ARC-MEP_Niveau2_001"
```
Creer un viewpoint pour documenter le clash.

## Rapport attendu

| # | Test | Discipline A | Discipline B | Nb New | Nb Active | Nb Resolved | Nb Approved | Total |
|---|---|---|---|---|---|---|---|---|
| 1 | ARC vs MEP | Architecture | Mecanique | ... | ... | ... | ... | ... |

### Detail des clashs critiques

| Clash | Element A | Element B | Distance | Statut | Niveau | Action |
|---|---|---|---|---|---|---|
| C001 | Mur X | Gaine Y | -0.05m | New | N+2 | A resoudre par MEP |

## Indicateurs cles
- **Taux de resolution** : (Resolved + Approved) / Total x 100
- **Clashs critiques** : distance < -0.01m (penetration reelle)
- **Clashs par niveau** : repartition verticale des conflits
- **Clashs par discipline** : identification du lot le plus impacte
