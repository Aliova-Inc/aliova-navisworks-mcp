# Skill 12 — Verification des Reservations et Traversees

## Objectif
Verifier que les traversees de murs, dalles et poutres par les reseaux MEP sont correctement reservees et coordonnees.

## Quand l'utiliser
- Revue de coordination MEP vs Structure
- Verification avant coulage beton
- Audit des reservations demandees vs realisees
- Preparation du plan de reservations

## Workflow

### 1. Identifier les zones de traversee
Superposer structure + une discipline MEP :
```
Outil : isolate_by_multi_search
Params : filters = "", branches = "PRJ_S,M_Ventilation"    (ou PLB, ELE, etc.)
```

### 2. Analyser les clashs STR vs MEP
```
Outil : list_clash_tests → trouver le test STR vs discipline
Outil : get_clash_results → details des interferences
```

### 3. Classification des traversees

| Type | Description | Action requise |
|---|---|---|
| **Reservation existante** | Ouverture prevue dans la structure | Verifier dimensionnement |
| **Traversee sans reservation** | Reseau traverse un element structural | Demander reservation |
| **Clash reel** | Conflit non resolvable par reservation | Deplacer le reseau |

### 4. Pour chaque traversee critique
```
1. isolate_items → GUIDs du reseau + element structural
2. get_item_properties → dimensions, niveau, type
3. save_current_viewpoint → documenter
```

### 5. Verification par niveau
Pour chaque niveau :
```
1. isolate_by_multi_search (branches) → STR + discipline MEP
2. Inspection visuelle des traversees
3. Comparaison avec le plan de reservations
```

## Points de controle

### Murs porteurs
- [ ] Aucune gaine > 1/3 epaisseur du mur
- [ ] Distance entre reservations > 3x diametre
- [ ] Pas de reservation dans les zones de noeud

### Dalles
- [ ] Reservations alignees verticalement si possible
- [ ] Pas de reservation dans les zones de poutre
- [ ] Manchons prevus pour les traversees coupe-feu

### Poutres
- [ ] Aucune traversee dans le tiers central de la portee
- [ ] Diametre < 1/3 hauteur de poutre
- [ ] Distance au nu d'appui > hauteur de poutre

## Rapport

| # | Niveau | Element STR | Reseau MEP | Discipline | Reservation | Conforme | Action |
|---|---|---|---|---|---|---|---|
| 1 | N+2 | Poutre P12 | Gaine CVC 600x400 | CVC | Non | Non | Demander reservation |
| 2 | N+1 | Dalle | Colonne montante PLB DN100 | PLB | Oui | Oui | RAS |
