# Skill 10 — Generation de Rapport de Coordination

## Objectif
Generer un rapport de coordination complet a partir des donnees du modele federe : etat des clashs, observations, actions.

## Quand l'utiliser
- Apres une reunion de synthese
- Rapport periodique (hebdomadaire/mensuel)
- Livrable BIM de coordination
- Reporting au MOA/MOE

## Workflow

### 1. Collecte des donnees
Executer sequentiellement :
```
1. get_document_info → identite du modele
2. list_clash_tests → synthese des clashs
3. get_clash_results (par test) → details
4. list_viewpoints → vues preparees
```

### 2. Compilation du rapport

## Structure du rapport

```
RAPPORT DE COORDINATION BIM
===========================
Projet : [Nom du projet]
Date : [Date]
Modele : [Titre du modele federe]
Redacteur : [Gestionnaire BIM]

1. FICHE D'IDENTITE DU MODELE
   - Titre, unites, nb modeles, nb elements
   - Liste des modeles federes par discipline
   - Date de derniere mise a jour par lot

2. SYNTHESE DE LA COORDINATION
   - Tableau recapitulatif des tests de clash
   - Taux de resolution global
   - Evolution depuis le dernier rapport

3. ANALYSE DES CLASHS PAR PAIRE
   Pour chaque test :
   - Nb total, nb new, nb active, nb resolved
   - Clashs critiques identifies
   - Viewpoints associes

4. OBSERVATIONS ET REMARQUES
   - Points d'attention par zone/niveau
   - Anomalies detectees
   - Risques identifies

5. ACTIONS EN COURS
   | # | Action | Lot | Echeance | Statut |
   |---|---|---|---|---|

6. DECISIONS PRISES
   Resume des decisions de la reunion

7. PROCHAINES ETAPES
   - Date prochaine livraison maquette
   - Date prochaine reunion
   - Points a preparer
```

## Indicateurs du rapport

### KPIs de coordination
- **Taux de resolution** : % clashs resolus/approuves
- **Nouveaux clashs** : nb de clashs apparus depuis derniere revue
- **Clashs stagnants** : nb de clashs actifs depuis > 2 semaines
- **Lot le plus impacte** : discipline avec le plus de clashs non resolus

### Graphiques recommandes (a generer manuellement)
- Evolution du nb de clashs dans le temps
- Repartition des clashs par statut (camembert)
- Clashs par paire de disciplines (histogramme)
- Taux de resolution par lot (barres)
