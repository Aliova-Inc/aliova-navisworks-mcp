# Navisworks MCP — Guide Claude Code

## Depot PUBLIC : ce qui ne doit jamais y entrer

Ce depot est public. Tout ce qui y entre (fichiers, binaires, messages de commit, noms de branches,
issues, PR, Releases) est lisible par tous, et reste dans l'historique meme apres suppression.

Ne jamais y mettre :
- **Donnees de projets clients** : noms de clients ou de projets, adresses, numeros de projet, noms
  reels de fichiers ou de modeles (`.rvt`, `.nwd`, `.nwf`, `.ifc`), noms de niveaux, de partitions, de
  jeux de selection ou de familles propres a un projet, intervenants rattaches a un projet
  (architectes, ingenieurs, entrepreneurs, fabricants), dates de planning, hauteurs, altitudes ou
  nombre d'etages qui permettent de reconnaitre un batiment, captures d'ecran ou exports d'un modele
  client.
- **Donnees personnelles** : noms, courriels, telephones, chemins locaux contenant un nom
  d'utilisateur (`C:\Users\<nom>\...`). Pour les commits, configurer l'adresse no-reply GitHub
  (`<id>+<login>@users.noreply.github.com`) plutot qu'un courriel personnel ou professionnel.
- **Informations internes** : noms de depots, de dossiers ou d'outils internes, adresses internes,
  liens vers des sessions, des conversations ou des documents prives.
- **Secrets** : cles d'API, jetons (dont le `token.txt` du plugin), mots de passe, identifiants reels
  de comptes ou de projets cloud (GUID de hub ou de projet ACC, par exemple).
- **Binaires et sorties de build** : l'installateur `MCP Navisworks.exe` se publie dans les Releases
  GitHub, jamais dans le depot. Pas de `bin/`, `obj/`, journaux, ni modeles.

Pour un exemple, utiliser des noms generiques : prefixe `PRJ_` pour les modeles, `Level 12` pour un
niveau, "le projet pilote" pour un projet reel, une famille decrite par sa fonction ("ascenseur de
chantier") plutot que par sa marque. Tout contenu repris d'un autre depot ou d'un projet se relit avec
cette liste avant d'entrer.

Avant chaque push : relire le diff ET les messages de commit avec cette liste en tete. En cas de
doute, ne pas pousser et demander.

## Connexion au serveur

Le plugin Navisworks ecoute sur **127.0.0.1:8766** (port par defaut, repli sur les suivants si pris)
en HTTP + JSON-RPC 2.0. **Un jeton porteur est exige** sur `/mcp`.

```
Endpoint : http://127.0.0.1:8766/mcp   (adresse en service : %LOCALAPPDATA%\Aliova\NavisworksMcp\endpoint.json)
Jeton    : %LOCALAPPDATA%\Aliova\NavisworksMcp\token.txt
En-tete  : Authorization: Bearer <jeton>
Protocole : MCP (Model Context Protocol) via HTTP
```

> Declarer le serveur : onglet Aliova > **About** > **Oui** copie la commande `claude mcp add ... --header`.
> Verifier que Navisworks tourne : `netstat -ano | findstr 8766`
> Aucun en-tete CORS : un navigateur ne peut pas appeler le serveur, c'est voulu (`Mcp/AccessGuard.cs`).

---

## Outils MCP disponibles

### Document

| Outil | Description | Params |
|---|---|---|
| `get_document_info` | Titre, chemin, unites, nb modeles federes, nb elements total | — |

### Selection

| Outil | Description | Params |
|---|---|---|
| `get_selection` | Elements actuellement selectionnes (nom, classe, GUID, bbox) | — |
| `clear_selection` | Vider la selection | — |
| `select_by_guids` | Selectionner par GUIDs (remplace la selection) | `guids` (comma-separated) |

### Recherche

| Outil | Description | Params |
|---|---|---|
| `search_by_property` | Recherche par propriete (categorie + nom + operateur + valeur) | `category`, `property`, `op` (Contains/Equals), `value`, `maxResults` |
| `search_by_name` | Recherche par nom d'affichage (contient le texte) | `name`, `maxResults` |

> **Categories de proprietes courantes** (SVF/ACC) : `Item`, `Element`, `LcOaNode`, `LcRevitData_Element`
> Pour les modeles ACC, les proprietes Revit natives peuvent ne pas etre accessibles via `search_by_property`.
> Utiliser `search_by_name` comme fallback fiable.

### Isolation / Visibilite

| Outil | Description | Params |
|---|---|---|
| `isolate_items` | Isole par GUIDs — affiche uniquement les elements matches + descendants + ancetres | `guids` (comma-separated) |
| `isolate_by_search` | Isole par une propriete, une ou plusieurs valeurs (OR) | `category`, `property`, `op`, `value` |
| `isolate_by_multi_search` | Isole par filtres de proprietes et/ou par nom de modele/partition (OR logic, case-insensitive) | `filters` (`cat\|prop\|op\|val;...`, vide possible), `branches` (comma-separated) |
| `hide_items` | Cache des elements specifiques sans affecter le reste (inverse de isolate) | `guids` (comma-separated) |
| `hide_by_model` | Cache des modeles/partitions par mot-cle (inverse d'une isolation par `branches`) | `keywords` (comma-separated) |
| `show_all` | Retablit la visibilite complete (annule toute isolation/masquage) | — |

> **`isolate_by_multi_search` avec `filters: ""` et `branches`** est l'outil principal pour isoler par discipline :
> - Plomberie : `branches: "PL_Alimentation,PL_Evacuation,PL_Appareils,PR_PL_"`
> - Architecture : `branches: "PRJ_A"`
> - Structure : `branches: "PRJ_S,ACIER"`
> - CVC : `branches: "M_Chauffage,M_Ventilation,M_Refrigeration"`
> - Electricite : `branches: "EL_"`
> - Piping complet : `branches: "Piping"`

### Proprietes

| Outil | Description | Params |
|---|---|---|
| `get_item_properties` | Toutes les proprietes d'un element par GUID (groupees par categorie) | `guid` |

> **Limitation** : `get_item_properties` leve `"Not supported if '!IsDisplayString'"` sur certains elements SVF (groupes, geometries ACC). Fonctionne sur les elements Revit natifs.

### Clash Detection

| Outil | Description | Params |
|---|---|---|
| `list_clash_tests` | Liste tous les tests de clash (nom, statut, compteurs new/active/resolved/approved) | — |
| `get_clash_results` | Resultats detailles d'un test (item A/B, distance, statut) | `testName`, `maxResults` |

> Toujours appeler `list_clash_tests` d'abord pour obtenir les noms exacts des tests.

### Viewpoints

| Outil | Description | Params |
|---|---|---|
| `list_viewpoints` | Liste tous les points de vue sauvegardes (chemin hierarchique) | — |
| `goto_viewpoint` | Naviguer vers un point de vue par son chemin | `path` |
| `save_current_viewpoint` | Sauvegarder la position camera actuelle | `name` |

### Arborescence modele

| Outil | Description | Params |
|---|---|---|
| `list_models` | Liste tous les modeles/partitions federes (nom, source, nb elements, bbox) | — |
| `get_model_tree` | Arborescence hierarchique d'un modele/partition jusqu'a une profondeur donnee | `keyword`, `depth` (defaut 3) |

> `list_models` est essentiel avant d'isoler ou de cacher par nom (`branches`, `hide_by_model`) pour connaitre les noms exacts des partitions.

### Apparence / Couleurs

| Outil | Description | Params |
|---|---|---|
| `color_override` | Colorier des elements par GUID (hex: FF0000=rouge, 00FF00=vert) | `guids`, `color`, `transparency` (0-1) |
| `reset_colors` | Reinitialiser toutes les couleurs a l'apparence originale | — |

> Utile pour marquer visuellement : rouge=clash, vert=resolu, bleu=discipline.

### Navigation / Section Box

| Outil | Description | Params |
|---|---|---|
| `zoom_to_items` | Zoomer sur des elements sans changer la visibilite | `guids` (comma-separated) |
| `set_section_box_from_items` | Section box autour d'elements avec marge | `guids`, `margin` (defaut 2.0m) |
| `clear_section_box` | Supprimer tous les plans de coupe | — |

### Mesures

| Outil | Description | Params |
|---|---|---|
| `measure_between` | Distance entre 2 elements (centre-centre, bord-bord, vertical) | `guidA`, `guidB` |
| `get_dimensions` | Dimensions bbox d'un element (largeur, profondeur, hauteur, position) | `guid` |

### Export

| Outil | Description | Params |
|---|---|---|
| `export_image` | Capture PNG de la vue courante (pour rapports) | `fileName`, `outputDir`, `width`, `height` |

> Images exportees dans `Desktop/Navisworks_Exports/` par defaut.

### Code C# a chaud (modele pyRevit 7 run_query / run_modify)

| Outil | Description | Params |
|---|---|---|
| `lookup_navis_api` | Signatures reelles d'un type de l'API chargee (reflexion, rien n'est execute) | `query`, `maxResults`, `includeNonPublic` |
| `navis_query` | C# en **lecture seule** : l'IL est verifie avant execution, tout appel d'ecriture est refuse | `code`, `fullSource`, `extraReferences` |
| `navis_modify` | C# qui **ecrit** : appels d'ecriture + code montres a l'utilisateur, rien ne tourne sans **Appliquer** | `title`, `code`, `fullSource`, `extraReferences` |

> Toujours refuses dans les deux : SaveFile / Publish / Open / Close / Document.Clear, `Application.DoEvents`, `Process.Start`, `Environment.Exit`.
> Politique d'ecriture du poste : `ALIOVA_NAVISWORKS_MCP_MODIFY` = `ask` (defaut) | `allow` | `deny`.
> Detail, recettes et pieges : `get_skill('14')`. Verification dans `Tools/CodePolicy.cs`.

### Aide a l'agent

| Outil | Description | Params |
|---|---|---|
| `get_skill` | Skills `Skills/*.md` embarques dans la DLL. Sans argument : la liste | `name` (numero, fichier ou fragment) |
| `list_runs` | Historique des appels (14 jours). `phase = unfinished` = Navisworks tombe pendant l'appel | `limit`, `tool` |
| `get_run` | Arguments (code compris) et resultat d'un appel | `id` |

> Journal : `%LOCALAPPDATA%\Aliova\NavisworksMcp\runs\runs-AAAA-MM-JJ.jsonl`, ecrit avant puis apres chaque appel.

---

## Appel direct via curl (debug)

```bash
TOKEN=$(cat "$LOCALAPPDATA/Aliova/NavisworksMcp/token.txt")

# Initialize
curl -s -X POST http://127.0.0.1:8766/mcp \
  -H "Content-Type: application/json" -H "Authorization: Bearer $TOKEN" \
  -d '{"jsonrpc":"2.0","method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}},"id":1}'

# Lister les outils
curl -s -X POST http://127.0.0.1:8766/mcp \
  -H "Content-Type: application/json" -H "Authorization: Bearer $TOKEN" \
  -d '{"jsonrpc":"2.0","method":"tools/list","params":{},"id":2}'

# Appeler un outil
curl -s -X POST http://127.0.0.1:8766/mcp \
  -H "Content-Type: application/json" -H "Authorization: Bearer $TOKEN" \
  -d '{"jsonrpc":"2.0","method":"tools/call","params":{"name":"get_document_info","arguments":{}},"id":3}'
```

> Sans jeton : HTTP 401. `GET http://127.0.0.1:8766/` (etat du serveur) reste accessible sans jeton.

---

## Exemple de modele federe

Les mots-cles de ce guide (`PRJ_A`, `PRJ_S`, `PL_*`, `M_*`, `EL_*`...) suivent le nommage d'un modele
federe reel, anonymise. Les adapter aux partitions du modele ouvert : `list_models` donne les noms exacts.

| Info | Valeur |
|---|---|
| Titre | Maquette federee (PRO + ENT) |
| Unites | Metres |
| Modeles federes | ~50 |
| Elements totaux | ~400 000 |
| Source | ACC (Autodesk Construction Cloud) via nwacc:// |

### Modeles sources (exemple)

| Prefixe | Discipline | Fichier source |
|---|---|---|
| `PRJ_A` | Architecture | `PRJ_A.rvt` |
| `PRJ_S_SA/SB/SC_ENT` | Structure (beton) | `PRJ_S_SA_ENT.rvt`, etc. |
| `PRJ_S_*_ACIER` | Structure (acier) | `PRJ_S_SA_ENT_ACIER.rvt`, etc. |
| `PRJ_M`, vues `PL_*` | Plomberie | `PRJ_M.rvt` : vues PL_Alimentation, PL_Appareils, PL_Evacuation |
| `PRJ_M`, vues `PR_PL_*` | Piping (process) | `PRJ_M.rvt` : vues PR_PL_* |
| `PRJ_M`, vues `M_*` | CVC (mecanique) | `PRJ_M.rvt` : vues M_Chauffage, M_Ventilation, M_Refrigeration |
| `PRJ_M`, vues `EL_*` | Electricite | `PRJ_M.rvt` : vues EL_Eclairage, EL_Equipements |
| `PRJ_Piping`, vues `PR_PL_*` | Piping industriel | `PRJ_Piping.rvt` |
| `PRJ_ME_*_ENT` | MEP entreprise | `PRJ_ME_EL/VE/PL/TU/PI_ENT.rvt` |
| `PRJ_PR` | Process | `PRJ_PR.rvt` |
| `PRJ_S` (site) | Structure site | `PRJ_S_SITE.rvt` |

---

## Pieges et limitations connus

> Ces pieges sont aussi dans `Skills/00_pieges_navisworks.md`, servi a tout client MCP par `get_skill('00')`.
> Mettre a jour les deux ensemble.

### REGLE D'OR : Explorer avant d'agir
Ne JAMAIS deviner les proprietes. Toujours echantillonner un element, lire ses proprietes, puis construire la requete.
Pattern : `search_by_name` → `get_item_properties` → `search_by_property` → `isolate_by_search`

### Noms de proprietes FR vs EN
Les modeles PRO sont souvent en anglais, les ENT en francais. Toujours chercher dans les deux :
| EN | FR |
|---|---|
| `Identity Data > Workset` | `Données d'identification > Sous-projet` |
| `Construction > Function` | `Construction > Fonction` |
| `Electrical - Circuiting > Electrical Data` | `Electricité - Circuit > Données électriques` |

### GUIDs nuls sur les partitions ACC
Les noeuds `LcOaPartition` des modeles ACC ont un GUID `00000000-0000-0000-0000-000000000000`.
Utiliser `isolate_by_search` (par propriete) ou `isolate_by_multi_search` (avec branches) au lieu de `isolate_items` par GUID.

### Proprietes sur elements SVF
`get_item_properties` fonctionne sur les noeuds `Geometry` (feuilles avec [ID]) mais echoue sur les `LcSvfGroup`.
Toujours descendre aux feuilles pour lire les proprietes.
La Search API (`search_by_property`) fonctionne sur tous les elements si le nom de categorie/propriete est exact.

### Performance — CRITIQUE
- **JAMAIS** iterer/cacher 400k+ elements individuellement → crash/freeze Navisworks
- **JAMAIS** envoyer 2 requetes d'isolation en parallele → crash
- L'isolation fonctionne par **branche** (top-level children) : echantillonne 200 resultats pour identifier les branches, puis cache les branches sans match
- Toujours appeler `show_all` avant une nouvelle isolation

### Workset ≠ Discipline
Un coude de ventilation peut avoir `Workset = PL_Alimentation`. Un luminaire peut avoir `Sous-projet = A_INT`.
Les raccords/fittings sont souvent dans un Workset different de leur discipline.
**Solution** : filtrer par propriete metier (`System Classification`, `Fonction`, `Electrical Data`) plutot que par Workset seul.

### Redemarrage requis pour mise a jour du plugin
Le DLL est verrouille par Navisworks pendant l'execution. Pour deployer une mise a jour :
1. Fermer Navisworks
2. Reconstruire et reinstaller (voir ci-dessous)
3. Rouvrir Navisworks

---

## Build et deploiement

Prerequis : Windows, Navisworks Manage 2026 installe (le projet reference ses DLL), SDK .NET.

```powershell
# Tout le paquet : plugin, verification de la disposition, installateur
installer\build.ps1
# -> MCP Navisworks.exe a la racine du depot : ignore par git, publie dans les Releases GitHub

# Plugin seul, pour iterer (Navisworks ferme)
dotnet build NavisworksMcpPlugin\plugin -c Release
# puis copier bin\Release\net48\NavisworksMcpPlugin.dll vers
# %APPDATA%\Autodesk\ApplicationPlugins\Aliova.NavisworksMcp.bundle\Contents\2026\
```
