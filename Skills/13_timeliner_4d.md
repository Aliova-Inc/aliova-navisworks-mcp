# 13 - Montage 4D TimeLiner

## Quand l'utiliser
Simulation de sequence (montage ou demolition) liee aux phases Revit : 4D, TimeLiner, phasage,
animation de chantier, « faire bouger le modele dans le temps ».

## Pieges verifies (en complement du skill 14, section TimeLiner)
- **La phase n'est pas cherchable** par `search_by_property` (propriete NamedConstant) :
  `count_by_phase` et `build_phase_search_sets` resolvent la valeur en code. Tri naturel des phases :
  `Position 2` avant `Position 10`.
- **`TaskAddCopy` insere une COPIE** : remplir la tache (attachement compris) AVANT l'ajout, puis la
  relire depuis `tl.Tasks` pour la modifier.
- **Les taches deja dans le document sont en lecture seule** : redater passe par `CreateCopy` +
  `TaskEdit(parent, index, valeurs)`, qui conserve l'attachement. `timeliner_set_task_dates` le fait.
- `TimelinerTask.IsGroup` vaut **true meme sans enfant** : ce n'est pas un indicateur de tache
  recapitulative.
- **Demolition par niveau** : un jeu par niveau emporte les equipements qui traversent les etages
  (plateforme, abri, ascenseur de chantier, grue), qui sont alors demolis au lieu de descendre.
  Les exclure avec `build_level_demolition_sets` (`excludeFamilies`, `excludeTypes`), puis les animer
  avec `animator_build_level_descent`.
- **Attacher le JEU d'animation a la tache, pas la scene** (reference `"scene\njeu"`) : attacher la
  scene ne produit aucun mouvement.

## Workflow minimal
```
1. count_by_phase()                                   # distribution creation
2. count_by_phase(category="Phase de demolition")     # distribution demolition
3. timeliner_list_task_types()                        # noms REELS des types (localises !)
4. timeliner_build_from_phases(startDate=..., daysPerStep=...)
5. timeliner_list_tasks(resolveCounts=true)            # verification independante
6. Ouvrir TimeLiner dans l'UI et jouer la simulation   # verification visuelle, obligatoire
```

## Les 3 choses a ne pas oublier
1. **Les noms des types de tache sont LOCALISES** (sur le projet pilote : `Construire`, `Demolir`,
   `Temporaire`). Un nom inconnu ne leve aucune erreur, il donne une **simulation muette**.
   Toujours appeler `timeliner_list_task_types` avant de creer des taches.
2. **Une tache non attachee est silencieuse** : `attachment` peut etre vide sans erreur. Verifier
   `hasSelectionSources` et `attachedCount`, et `attachMethod` au retour de `build_from_phases`
   (`SelectionSources` = lien vif, voulu ; `ExplicitSelection` = repli fige).
3. **Re-export Revit = comptes qui changent.** Relancer `count_by_phase` apres chaque re-export,
   ne jamais reutiliser les chiffres d'une session precedente.

## Tools
| Tool | Usage |
|---|---|
| `count_by_phase` | distribution des elements par phase (creation ou demolition) |
| `build_phase_search_sets` | sets `CREE -` / `DEMO -` par phase, sans les taches |
| `list_selection_sets` / `clear_sets_folder` | verifier / regenerer proprement |
| `timeliner_list_task_types` | types de simulation + apparences + auto-detection |
| `timeliner_build_from_phases` | **geste principal** : sets + taches + types + dates en 1 appel |
| `timeliner_list_tasks` | verification (`resolveCounts=true` pour les comptes exacts) |
| `timeliner_add_task` | une tache attachee a un set |
| `timeliner_set_task_dates` | redater une tache existante |
| `timeliner_clear_tasks` | vider les taches (les sets ne sont pas touches) |

## Note d'architecture
L'API TimeLiner managee est dans une assembly **separee**, `Autodesk.Navisworks.Timeliner.dll`, et non
dans `Autodesk.Navisworks.Api.dll`. Lecon portable : une fonctionnalite absente de l'API principale
est probablement dans une assembly satellite `Autodesk.Navisworks.<Feature>.dll` (meme schema que
Clash).
