# Skill 02 — Isolation par Discipline

## Objectif
Isoler visuellement une ou plusieurs disciplines du modele federe pour inspection, revue ou capture.

## Quand l'utiliser
- Revue d'une discipline specifique (plomberie, CVC, electricite...)
- Verification de coherence d'un lot
- Preparation de captures pour rapport
- Comparaison entre disciplines

## Lecons apprises (session 2026-04-27)

### Principe fondamental : EXPLORER AVANT D'ISOLER
Ne JAMAIS deviner les proprietes. Toujours :
1. Trouver un element de la discipline ciblee (`search_by_name` ou `get_model_tree`)
2. Lire ses proprietes (`get_item_properties`)
3. Identifier la propriete discriminante (Workset, Fonction, System Type, etc.)
4. Verifier le nombre de resultats (`search_by_property`)
5. PUIS isoler

### Noms de proprietes FR vs EN
Les modeles Revit en francais ont des noms de categories/proprietes differents :
| EN | FR |
|---|---|
| `Identity Data > Workset` | `Données d'identification > Sous-projet` |
| `Construction > Function` | `Construction > Fonction` |
| `Electrical - Circuiting > Electrical Data` | `Electricité - Circuit > Données électriques` |
| `Mechanical > System Classification` | `Mechanical > System Classification` (reste EN) |

**Toujours chercher dans les DEUX langues** pour couvrir PRO et ENT.

### Modeles ACC/SVF : pas de proprietes standard
- `Element > Type` = toujours `Geometry` (inutile pour filtrer)
- `Element > Source File` = nom du SVF, pas le RVT (ex: `PL_Alimentation.svf`)
- Les proprietes Revit natives sont dans des categories specifiques, pas dans `Element`
- `get_item_properties` peut echouer sur les `LcSvfGroup` — descendre aux `Geometry` (feuilles)

### Workset ≠ Discipline
Un element peut etre dans la branche `M_Ventilation` mais avoir `Workset = PL_Alimentation`.
Les coudes/raccords sont souvent dans un Workset different de leur discipline reelle.
**Solution** : combiner filtre par propriete + filtre par branche si necessaire.

### Performance : isolation par branche, jamais par element
- Un modele federe peut avoir 400k+ elements
- Iterer/cacher element par element → crash Navisworks
- **Toujours isoler au niveau branche** (enfants directs du model root = ~50 noeuds)
- L'outil `IsolateFromSearches` echantillonne 200 resultats pour identifier les branches

## Outils disponibles

### `isolate_by_search` — filtre simple
```
category = "Identity Data"
property = "Workset"
op = "Contains"
value = "PL_Alimentation,PL_Evacuation"  (comma-separated = OR)
```

### `isolate_by_multi_search` — filtres combines + branches
```
filters = "cat1|prop1|op1|val1;cat2|prop2|op2|val2"  (semicolon-separated = OR)
branches = "PRJ_ME_EL,EL_"  (optional, comma-separated keywords, case-insensitive)
```

### `show_all` — retablir la visibilite complete

## Workflows par discipline

### Plomberie
```
Methode : isolate_by_search
category = "Identity Data"
property = "Workset"
op = "Contains"
value = "PL_Alimentation,PL_Evacuation,PL_Appareils"
```
Resultat attendu : ~7 000 elements, ~1 branche

### Structure complete (beton + acier)
```
Methode : isolate_by_multi_search
filters = "Paramètres IFC|IfcElementAssembly|Contains|ACIER;IFC Parameters|IfcSpatialContainer|Contains|Zone;Paramètres IFC|IfcSpatialContainer|Contains|Zone"
```
Resultat attendu : ~111 000 elements, ~3 branches

### Ventilation (gaines + coudes + accessoires)
Le Workset seul manque les raccords. Utiliser System Classification :
```
Methode : isolate_by_multi_search
filters = "Mechanical|System Classification|Contains|Supply Air;Mechanical|System Classification|Contains|Return Air;Mechanical|System Classification|Contains|Exhaust Air;Identity Data|Workset|Contains|M_Ventilation;Données d'identification|Sous-projet|Contains|M_Ventilation"
```
Resultat attendu : ~10 000 elements, ~8 branches

### Electricite (equipements + conduits + raccords)
Les elements elec n'ont pas de Workset `EL_`. Combiner :
```
Methode : isolate_by_multi_search
filters = "Electrical - Circuiting|Electrical Data|Contains|V;Electricité - Circuit|Données électriques|Contains|V;Données d'identification|Alimentation|Contains|Main"
branches = "PRJ_ME_EL,EL_"
```
Resultat attendu : ~7 000+ elements

### Murs exterieurs (enveloppe)
```
Methode : isolate_by_search
category = "Construction"
property = "Fonction"
op = "Equals"
value = "Extérieur"
```
Resultat attendu : ~278 elements

### Architecture complete
```
Methode : isolate_by_multi_search
filters = "Données d'identification|Sous-projet|Contains|A_;Identity Data|Workset|Contains|A_"
branches = "PRJ_A"
```

### CVC (chauffage + refrigeration)
```
Methode : isolate_by_search
category = "Identity Data"
property = "Workset"
op = "Contains"
value = "M_Chauffage,M_Refrigeration"
```

## Regles critiques

1. **Toujours `show_all` avant d'isoler** — sinon les filtres se cumulent sur une vue deja masquee
2. **Un seul appel a la fois** — ne jamais envoyer 2 requetes d'isolation en parallele → crash
3. **Verifier le port** : `netstat -ano | findstr 8766` (ou le port affiche dans About) avant d'appeler
4. **Attendre le chargement** : le modele ACC peut prendre 1-2 min a charger, le MCP n'est actif qu'apres
