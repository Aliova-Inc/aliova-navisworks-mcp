# Skill 05 — Recherche et Inspection d'Elements

## Objectif
Rechercher des elements specifiques dans le modele federe par nom, propriete ou categorie, puis inspecter leurs proprietes.

## Lecons apprises (session 2026-04-27)

### Methode : echantillonner avant de filtrer
1. `search_by_name` pour trouver un element representatif
2. `get_item_properties` pour lire TOUTES ses proprietes
3. Identifier la propriete discriminante et sa categorie exacte
4. `search_by_property` pour compter les resultats
5. Ajuster si necessaire (langue FR/EN, variantes)

### Proprietes accessibles sur modeles ACC/SVF
Les modeles ACC exposent les proprietes Revit dans des categories specifiques :

| Categorie | Proprietes utiles | Modeles |
|---|---|---|
| `Identity Data` | `Workset`, `Type Name`, `Cost` | PRO (EN) |
| `Données d'identification` | `Sous-projet`, `Nom du type`, `Identifiant`, `Marque de type` | PRO/ENT (FR) |
| `Construction` | `Fonction` (Intérieur/Extérieur), `Largeur` | Murs (FR) |
| `Mechanical` | `System Classification`, `System Type`, `System Name` | MEP |
| `Electrical - Circuiting` | `Electrical Data` | Elec PRO (EN) |
| `Electricité - Circuit` | `Données électriques` | Elec ENT (FR) |
| `Paramètres IFC` / `IFC Parameters` | `IfcGUID`, `IfcElementAssembly`, `IfcMaterial`, `IfcSpatialContainer` | Tous |
| `Contraintes` / `Constraints` | `Niveau`, `Level`, `Élévation` | Tous |
| `Cotes` / `Dimensions` | `Longueur`, `Surface`, `Volume`, `Diamètre` | Tous |
| `Élément` | `Nom`, `Type`, `Fichier source`, `GUID` | Tous (natif Navisworks) |

### Limitations
- `get_item_properties` echoue sur les noeuds `LcSvfGroup` → descendre aux `Geometry` (feuilles avec [ID])
- `search_by_property` ne trouve rien si le nom de categorie/propriete n'est pas exact (accents compris)
- `search_by_name` parcourt tout l'arbre → peut etre lent sur 400k+ elements, utiliser `maxResults`
- La Search API fonctionne avec les noms AFFICHES (localisés), pas les noms internes

### Pattern pour trouver un element feuille explorable
```
1. get_model_tree(keyword="...", depth=4)
2. Dans le JSON, chercher un noeud className="Geometry" avec "[" dans le nom (= ID Revit)
3. get_item_properties(guid=...)
```

## Outils

### search_by_name
```
name = "texte"     # cherche dans le DisplayName (contient, case-insensitive)
maxResults = 100    # limiter sur gros modeles
```

### search_by_property
```
category = "Identity Data"    # nom exact de la categorie (sensible aux accents)
property = "Workset"          # nom exact de la propriete
op = "Contains" | "Equals"    # operateur
value = "PL_"                 # valeur a chercher
maxResults = 100
```

### get_item_properties
```
guid = "..."   # GUID d'un element Geometry (pas Group)
```
Retourne toutes les proprietes groupees par categorie. Wrap try/catch interne pour les valeurs non-lisibles sur SVF.

### get_model_tree
```
keyword = "..."   # cherche dans les DisplayName de l'arbre
depth = 3         # profondeur max (1-6)
```
Utile pour explorer la structure de l'arbre et trouver des elements feuilles.

## Cas d'usage

### Trouver la bonne propriete pour filtrer une discipline
1. `search_by_name` → name: "Round Duct", maxResults: 1
2. Prendre le GUID du resultat
3. `get_item_properties` → lire toutes les proprietes
4. Identifier : System Type? Workset? Category?
5. `search_by_property` → verifier le nombre total
6. Ajuster la recherche si trop large ou trop restrictif

### Verifier un element signale en clash
1. `get_clash_results` → recuperer les GUIDs des elements A et B
2. `get_item_properties` → inspecter les proprietes de chaque element
3. `isolate_items` → visualiser le conflit
4. `measure_between` → mesurer la distance
5. `save_current_viewpoint` → documenter

### Compter des elements par critere
```
search_by_property → category: "Construction", property: "Fonction", op: "Equals", value: "Extérieur"
```
Lire `totalFound` dans le retour.

### Explorer un modele inconnu
1. `get_document_info` → vue d'ensemble
2. `list_models` → voir toutes les partitions
3. `get_model_tree(keyword="...", depth=2)` → structure d'une partition
4. Trouver un element feuille, lire ses proprietes
5. Identifier les proprietes utiles pour la recherche/isolation
