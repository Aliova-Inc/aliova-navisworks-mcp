# Skills Navisworks MCP

Ces fichiers de skills sont des workflows operationnels pour piloter Navisworks via MCP.
Chaque skill indique quand l'utiliser, quels outils appeler, les parametres importants et le format de sortie attendu.

## Utilisation rapide

1. Verifier que Navisworks est ouvert et que le bouton **MCP server** de l'onglet Aliova affiche *MCP ouvert*.
2. Confirmer l'adresse et le port dans **About** (par defaut `http://127.0.0.1:8766/mcp`, jeton exige).
3. Choisir le skill correspondant a la tache. Ces fichiers sont aussi embarques dans le plugin :
   `get_skill` les sert a n'importe quel client MCP, sans copie a faire.
4. Appeler les outils dans l'ordre du workflow.
5. Produire la synthese avec les tableaux ou indicateurs demandes par le skill.

## Index

| Skill | Usage principal |
|---|---|
| `00_pieges_navisworks.md` | **A lire en premier** : pieges du modele, performance, FR/EN, ACC/SVF |
| `01_clash_detection.md` | Analyse des clashs et conflits interdisciplinaires |
| `02_isolation_discipline.md` | Isolation d'une discipline ou d'un modele |
| `03_revue_modele.md` | Revue globale du modele et statistiques |
| `04_navigation_viewpoints.md` | Navigation, viewpoints, sauvegarde de vues |
| `05_recherche_elements.md` | Recherche et inspection d'elements |
| `06_verification_coherence.md` | Verification de coherence interdisciplinaire |
| `07_suivi_resolution.md` | Suivi d'avancement des clashs |
| `08_preparation_reunion.md` | Preparation de reunion de synthese |
| `09_controle_qualite_maquette.md` | Controle qualite de la maquette |
| `10_rapport_coordination.md` | Generation d'un rapport de coordination |
| `11_selection_inspection.md` | Inspection interactive de la selection courante |
| `12_verification_reservations.md` | Verification des reservations et traversees |
| `13_timeliner_4d.md` | Montage 4D TimeLiner par phase, pieges verifies |
| `14_navis_query_modify.md` | **Code C# a chaud** : `navis_query` (lecture verifiee) et `navis_modify` (ecriture approuvee) |
| `15_coupe_animee_verdict.md` | **QUESTION CLOSE** : animer une coupe en simulation est impossible. Preuves + repli |

## Regles critiques (lecons session 2026-04-27)

### EXPLORER AVANT D'AGIR
Ne jamais deviner les noms de proprietes ou la structure du modele.
Toujours : echantillonner un element → lire ses proprietes → puis construire la requete.

Depuis 2026-08-06, l'exploration ne demande plus d'ajouter un outil ni de reinstaller le plugin :
`navis_query` compile et execute du C# **a chaud** en lecture seule verifiee, `navis_modify` ecrit apres
approbation de l'utilisateur, `lookup_navis_api` lit les signatures sans rien executer. Protocole,
restrictions du langage (C# 5) et pieges verifies dans `14_navis_query_modify.md`. Toujours **lire avant
d'ecrire**, et faire enregistrer le `.nwf` avant la premiere ecriture.

### NE PAS CONCLURE A L'ABSENCE SANS CONTRE-EPREUVE
Une sonde qui renvoie zero ne prouve rien tant qu'elle n'a pas renvoye un resultat non nul sur un cas
connu pour etre bon. Verifier l'instrument avant de conclure sur la mesure.
Corollaire cote types : un nom court ne suffit jamais a identifier une classe, neuf assemblies
Navisworks declarent des homonymes VIDES sans espace de noms. Resoudre par nom complet + assembly.

### NE JAMAIS POMPER LA BOUCLE DE MESSAGES
`Application.DoEvents()` + `Thread.Sleep` dans du code execute sur le fil d'interface fait reentrer
Navisworks dans lui-meme. Suspect principal du plantage du 2026-08-06. Pour observer une operation
qui dure : rendre la main et rappeler ensuite, jamais attendre sur place.

### QUESTIONS DEFINITIVEMENT CLOSES
- **Animer une coupe pendant une simulation** : impossible, quatre preuves, `15_coupe_animee_verdict.md`.
  Le repli valide est la sequence d'images rendue (83 ms/image en 1600x900).

### Noms FR vs EN
Les proprietes varient selon la langue du modele Revit source.
Toujours chercher dans les deux langues (Identity Data + Données d'identification, etc.).
Voir le tableau complet dans `02_isolation_discipline.md`.

### Performance
- **JAMAIS** iterer/cacher 400k+ elements individuellement → crash
- Isoler au niveau **branche** (top-level children du model root)
- Echantillonner max 200 resultats de recherche pour identifier les branches
- **UN SEUL appel** d'isolation a la fois — pas de requetes paralleles

### Workflow d'isolation fiable
```
1. show_all                          # reset la vue
2. search_by_property (exploration)  # trouver la bonne propriete/categorie
3. get_item_properties (diagnostic)  # valider sur un element
4. isolate_by_search ou             # isolation simple
   isolate_by_multi_search           # isolation multi-filtres + branches
```

### Outils d'isolation disponibles
| Outil | Usage |
|---|---|
| `isolate_by_search` | 1 propriete, 1+ valeurs (OR) |
| `isolate_by_multi_search` | N filtres propriete (OR) + branches par nom (optionnel) |
| `isolate_items` | Par GUIDs explicites (petits ensembles) |
| `show_all` | Reset visibilite complete |

### Modeles ACC/SVF
- `Element > Type` = toujours "Geometry" (inutile)
- `get_item_properties` echoue sur `LcSvfGroup` → descendre aux feuilles `Geometry`
- Les partitions ont des GUIDs nuls → isoler par nom (`isolate_by_multi_search` avec `branches`), pas par GUID
- Le fichier source SVF != le fichier RVT original
