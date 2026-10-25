# Skill 03 — Revue de Modele et Statistiques

## Objectif
Obtenir une vue d'ensemble du modele federe : composition, taille, disciplines presentes, etat general.

## Quand l'utiliser
- Reception d'une nouvelle version de maquette
- Debut de revue de coordination
- Rapport d'avancement de modelisation
- Audit de la maquette federee

## Workflow

### 1. Informations generales du document
```
Outil : get_document_info
```
Retourne : titre, unites, nombre de modeles, nombre total d'elements, sources.

### 2. Inventaire des disciplines
A partir des sources du modele, dresser un tableau :

| Discipline | Fichier source | Statut |
|---|---|---|
| Architecture | PRJ_A.rvt | Present |
| Structure | PRJ_S_SA/SB/SC_ENT.rvt | Present |
| MEP | PRJ_M.rvt | Present |
| Piping | PRJ_Piping.rvt | Present |

### 3. Verification de completude
Rechercher les categories d'elements attendues :
```
Outil : search_by_name
Params : name = "Walls" / "Doors" / "Pipes" / "Ducts" / "Cable Trays" etc.
```

### 4. Verification des viewpoints existants
```
Outil : list_viewpoints
```
Verifier qu'il existe des vues de coordination, des vues par niveau, par zone.

### 5. Etat des clash tests
```
Outil : list_clash_tests
```
Vue d'ensemble de la coordination.

## Rapport attendu

### Fiche d'identite de la maquette

| Champ | Valeur |
|---|---|
| Titre | ... |
| Nb modeles federes | ... |
| Nb elements total | ... |
| Unites | ... |
| Source | ACC / Local |
| Date de revue | AAAA-MM-JJ |

### Matrice de presence des disciplines

| Discipline | Modele source | Nb elements | Complet |
|---|---|---|---|
| ARC | ... | ... | Oui/Non |
| STR | ... | ... | Oui/Non |
| CVC | ... | ... | Oui/Non |
| PLB | ... | ... | Oui/Non |
| ELE | ... | ... | Oui/Non |

### Etat de la coordination

| Nb tests clash | Nb clashs ouverts | Taux resolution |
|---|---|---|
| ... | ... | ...% |
