# Skill 11 — Selection et Inspection Interactive

## Objectif
Interagir avec les elements selectionnes par l'utilisateur dans Navisworks pour inspection, diagnostic ou documentation.

## Quand l'utiliser
- L'utilisateur a selectionne un element dans Navisworks et veut ses proprietes
- Investigation d'un element specifique
- Diagnostic d'un probleme signale

## Workflow

### 1. Lire la selection actuelle
```
Outil : get_selection
```
Retourne : nom, classe, GUID, bounding box de chaque element selectionne.

### 2. Inspecter les proprietes
```
Outil : get_item_properties
Params : guid = "GUID du premier element"
```
Toutes les proprietes groupees par categorie.

### 3. Actions possibles

#### Isoler l'element
```
Outil : isolate_items
Params : guids = "GUID de l'element"
```

#### Chercher des elements similaires
```
Outil : search_by_name
Params : name = "nom du type de l'element"
```

#### Sauvegarder la vue
```
Outil : save_current_viewpoint
Params : name = "INSP_[description]"
```

#### Vider la selection
```
Outil : clear_selection
```

## Cas d'usage

### "Qu'est-ce que c'est ?"
1. L'utilisateur selectionne un element dans Navisworks
2. `get_selection` → identifier l'element
3. `get_item_properties` → toutes ses proprietes
4. Resumer : type, categorie, niveau, materiau, dimensions

### "Montre-moi tous les elements comme celui-ci"
1. `get_selection` → identifier le type
2. `search_by_name` → chercher les similaires
3. `isolate_items` → les isoler tous
4. Compter et resumer

### "Selectionne les elements du clash"
1. `get_clash_results` → GUIDs des elements A et B
2. `select_by_guids` → les selectionner dans Navisworks
3. L'utilisateur peut inspecter visuellement
