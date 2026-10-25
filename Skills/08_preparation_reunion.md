# Skill 08 — Preparation de Reunion de Synthese/Coordination

## Objectif
Preparer automatiquement les elements necessaires pour une reunion de coordination BIM : etat des clashs, vues preparees, points d'attention, ordre du jour technique.

## Quand l'utiliser
- Avant chaque reunion de synthese (hebdomadaire ou bi-mensuelle)
- Avant une revue de coordination avec les lots
- Preparation d'une revue de conception

## Workflow

### 1. Etat du modele
```
Outil : get_document_info
```
Verifier que le modele est a jour (date, versions des modeles federes).

### 2. Synthese des clashs
```
Outil : list_clash_tests
```
Tableau recapitulatif de tous les tests.

### 3. Clashs prioritaires a discuter
```
Outil : get_clash_results (pour chaque test critique)
```
Identifier les clashs New et Active a presenter en reunion.

### 4. Preparation des vues
Pour chaque sujet a discuter :
```
1. isolate_by_multi_search (branches) ou isolate_items → isoler la zone
2. goto_viewpoint → aller a la vue appropriee
3. save_current_viewpoint → sauvegarder si nouvelle vue
```

### 5. Viewpoints existants
```
Outil : list_viewpoints
```
Lister les vues preparees pour la navigation en reunion.

## Ordre du jour type

### 1. Tour de table — Mises a jour des modeles
- Quels lots ont mis a jour leur maquette ?
- Modeles presents vs attendus

### 2. Bilan des clashs
- Synthese globale (taux de resolution)
- Nouveaux clashs apparus
- Clashs resolus depuis la derniere reunion
- Clashs bloques (> 2 semaines)

### 3. Points par discipline

#### Structure
- Clashs STR vs CVC
- Clashs STR vs PLB
- Reservations demandees

#### CVC
- Clashs CVC vs ARC (faux-plafonds)
- Dimensionnement gaines

#### Plomberie
- Clashs PLB vs STR
- Traversees

#### Electricite
- Chemins de cables vs gaines
- Locaux techniques

### 4. Actions et prochaines etapes
- Qui fait quoi pour quand
- Date de prochaine livraison maquette
- Prochaine reunion

## Compte-rendu type

| # | Sujet | Lots concernes | Decision | Responsable | Echeance |
|---|---|---|---|---|---|
| 1 | Clash gaine N+2 | CVC + STR | Deplacer gaine | LOT CVC | JJ/MM |
| 2 | Reservation mur | PLB + ARC | Ajouter reservation | LOT ARC | JJ/MM |
