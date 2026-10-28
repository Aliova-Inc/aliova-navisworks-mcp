# Aliova MCP for Navisworks

Connects **Navisworks Manage 2026** to **Claude** through a local MCP server.
Relie **Navisworks Manage 2026** à **Claude** par un serveur MCP local.

[English](#english) · [Français](#français)

---

## English

### What it does

Claude drives Navisworks directly: it reads the model, queries it, changes what is displayed,
measures, navigates, and writes back. **66 tools** across the whole application, not one
specialty:

| Area | What Claude can do |
|---|---|
| Model and tree | open the document, list models, walk the tree, sample any node |
| Search and properties | search by property or name in any language tab, read the full property set of an element |
| Selection and sets | read and set the current selection, create and reuse selection sets, build sets per level |
| Visibility | isolate by property, by several filters at once, by explicit GUIDs, restore everything |
| Appearance | override colour and transparency, reset overrides |
| Measurement | distances, bounding boxes, elevations |
| Navigation and viewpoints | move the camera, save and recall viewpoints, build viewpoint animations |
| Sections | drive clip planes and section boxes |
| Clash detection | read tests and results, follow resolution |
| 4D TimeLiner | create and edit tasks, attach sets, set dates and task types, drive the simulation cursor |
| Animator | object and camera animations, keyframes, attach them to tasks |
| Export | render images, export data |
| Live C# | `navis_query` reads (checked read-only before it runs), `navis_modify` writes (approved by you inside Navisworks), `lookup_navis_api` reads real API signatures |
| Agent support | `get_skill` serves the workflows and pitfalls to any MCP client, `list_runs` / `get_run` replay every call |

The last one matters more than it looks: it means a capability missing from the tool list can
be reached the same session, without shipping a new build.

### Install

One file, no administrator rights. Download it from the repository's **Releases** page, or
build it yourself (see *Build from source*).

```text
MCP Navisworks.exe
```

1. Close Navisworks.
2. Double-click the installer.
3. Click **Install**.
4. Open Navisworks: the **Aliova** tab is in the ribbon.

The MCP server starts by itself with Navisworks. Nothing to launch by hand.

The installer opens in English. The **FR / EN** switch in the header changes it, or force a
language at launch:

```text
MCP Navisworks.exe --lang=fr
```

Windows asks for administrator approval in one case only: if an installation from an earlier
version is still sitting in the Navisworks folder. The installer removes it, and the question
never comes back.

### The Aliova tab

| Button | Purpose |
|---|---|
| **MCP server** | shows state and port: *MCP open / port 8766*. Click to open or close the channel. The tooltip carries the full address. |
| **Claude chat** | shows or hides the chat pane |
| **About** | version, address, tool count, token file, write policy, log path. **Yes** copies the `claude mcp add` command, token included |

The tab is the only entry point. Nothing is added under the Add-ins tab.

### The MCP server

| | |
|---|---|
| Address | `http://127.0.0.1:8766/mcp` |
| Startup | automatic when Navisworks opens |
| Tools exposed | 66 |
| Access | bearer token required, no CORS |

Every request needs the token, stored in `%LOCALAPPDATA%\Aliova\NavisworksMcp\token.txt` (created on
first start, kept across restarts). The quickest setup: **About**, then **Yes**, then paste the
copied command in a terminal:

```text
claude mcp add --transport http navisworks http://127.0.0.1:8766/mcp --header "Authorization: Bearer <token>"
```

Any other MCP client:

```json
{
  "mcpServers": {
    "navisworks": {
      "type": "http",
      "url": "http://127.0.0.1:8766/mcp",
      "headers": { "Authorization": "Bearer <token>" }
    }
  }
}
```

Why a token: listening on `127.0.0.1` keeps the network out, not the browser. Without it, any web page
open on the machine could call the server and run code inside Navisworks. Delete `token.txt` to rotate
it (a new one is generated at the next start, update the client). To set it yourself, define
`ALIOVA_NAVISWORKS_MCP_TOKEN` before starting Navisworks.

### Code execution and safety

Modelled on the pyRevit 7 agent runtime:

| | |
|---|---|
| `navis_query` | read-only. The compiled IL is checked **before** running: any call that can change the document is refused and nothing runs |
| `navis_modify` | writes. The write calls found in the IL, the agent's title and the code are shown to you inside Navisworks: nothing runs until you click **Apply** |
| Always refused | save, publish, open, close or clear a document; `Application.DoEvents`; `Process.Start`; `Environment.Exit` |
| History | every tool call, its arguments (code included) and result, in `%LOCALAPPDATA%\Aliova\NavisworksMcp\runs\`, kept 14 days, readable with `list_runs` / `get_run` |

Write policy, set `ALIOVA_NAVISWORKS_MCP_MODIFY` before starting Navisworks: `ask` (default), `allow`
(no prompt, checks still apply) or `deny` (no writes at all).

The check reads the compiled code, it is not a sandbox: it stops an agent's honest mistakes, not code
written to fool it.

If port 8766 is already taken, the server falls back to the next one and **says so**: on the
ribbon button, in **About**, and in the log. Copy the displayed port into your client
configuration, or free 8766. To force a port, set `ALIOVA_NAVISWORKS_MCP_PORT` before
starting Navisworks.

The address in service is also published on disk, readable without opening Navisworks:

```text
%LOCALAPPDATA%\Aliova\NavisworksMcp\endpoint.json
```

### When something goes wrong

The plugin log, with the full exception chain and the process id:

```text
%LOCALAPPDATA%\Aliova\NavisworksMcp\journal.txt
```

The **About** button offers to open it.

### Skills

The [`Skills/`](Skills) folder holds the operating workflows to add on the Claude side.
Start with [`Skills/README.md`](Skills/README.md), which carries the index and the critical
rules. The same files are embedded in the plugin and served by `get_skill`, so any MCP client gets them. Three to read before any serious work:

- `00_pieges_navisworks.md`: model pitfalls and performance rules, read first
- `14_navis_query_modify.md`: run C# live inside Navisworks, with the verified pitfalls
- `15_coupe_animee_verdict.md`: a closed question, with the proofs

### Uninstall

Run the installer again and click **Uninstall**. It removes the plugin folder
`%APPDATA%\Autodesk\ApplicationPlugins\Aliova.NavisworksMcp.bundle` and the plugin's data folder
`%LOCALAPPDATA%\Aliova\NavisworksMcp` (token, log, and the call history, which holds the code and
results of every call). Removing by hand means deleting both folders.

### Build from source

```powershell
installer\build.ps1
```

Builds the plugin, assembles the Autodesk package, **verifies its layout**, compresses it and
compiles it into the installer. The verification is not decorative: Navisworks dictates where
the ribbon icons and XAML must sit, and a wrong path produces no load error at all — only a
silent tab. See [`NavisworksMcpPlugin/INSTALLATION.md`](NavisworksMcpPlugin/INSTALLATION.md)
for the technical detail.

### Known limits

- The package installs for the **current Windows account only**, under `%APPDATA%`. A shared
  workstation needs a `%PROGRAMDATA%` variant, which does require administrator rights.
- The manifest declares Navisworks **2026 only** (`Nw23`). On a 2025 machine the tab will not
  appear; the plugin has to be recompiled against that release's API.

---

## Français

### Ce que l'outil fait

Claude pilote Navisworks directement : il lit la maquette, l'interroge, change ce qui est
affiché, mesure, navigue, et écrit en retour. **66 outils** couvrant toute l'application, pas
une spécialité :

| Domaine | Ce que Claude peut faire |
|---|---|
| Maquette et arborescence | ouvrir le document, lister les modèles, parcourir l'arbre, échantillonner un nœud |
| Recherche et propriétés | chercher par propriété ou par nom dans n'importe quel onglet de langue, lire le jeu complet de propriétés d'un élément |
| Sélection et jeux | lire et poser la sélection courante, créer et réutiliser des jeux, construire des jeux par niveau |
| Visibilité | isoler par propriété, par plusieurs filtres à la fois, par GUID explicites, tout restaurer |
| Apparence | forcer couleur et transparence, annuler les forçages |
| Mesure | distances, boîtes englobantes, altitudes |
| Navigation et points de vue | déplacer la caméra, enregistrer et rappeler des points de vue, monter des animations de points de vue |
| Coupes | piloter les plans de coupe et les boîtes de coupe |
| Détection de conflits | lire les tests et les résultats, suivre la résolution |
| TimeLiner 4D | créer et modifier des tâches, y attacher des jeux, poser dates et types, piloter le curseur de simulation |
| Animator | animations d'objets et de caméra, images-clés, rattachement aux tâches |
| Export | rendre des images, exporter des données |
| C# à chaud | `navis_query` lit (lecture seule vérifiée avant exécution), `navis_modify` écrit (approuvé par vous dans Navisworks), `lookup_navis_api` lit les vraies signatures de l'API |
| Aide à l'agent | `get_skill` sert les workflows et les pièges à n'importe quel client MCP, `list_runs` / `get_run` relisent chaque appel |

Le dernier compte plus qu'il n'y paraît : une capacité absente de la liste d'outils peut être
atteinte dans la même séance, sans livrer une nouvelle version.

### Installation

Un seul fichier, aucun droit administrateur. À télécharger dans la page **Releases** du dépôt,
ou à fabriquer soi-même (voir *Reconstruire*).

```text
MCP Navisworks.exe
```

1. Fermer Navisworks.
2. Double-cliquer sur l'installateur.
3. Cliquer sur **Install**.
4. Ouvrir Navisworks : l'onglet **Aliova** est dans le ruban.

Le serveur MCP démarre tout seul avec Navisworks. Rien à lancer à la main.

L'installateur s'ouvre en anglais. La bascule **FR / EN** de l'en-tête change la langue, ou
on l'impose au lancement :

```text
MCP Navisworks.exe --lang=fr
```

Windows demande une autorisation administrateur dans un seul cas : si une installation d'une
version antérieure traîne dans le répertoire de Navisworks. L'installateur la retire, et la
question ne se repose plus.

### L'onglet Aliova

| Bouton | Rôle |
|---|---|
| **MCP server** | affiche l'état et le port : *MCP ouvert / port 8766*. Cliquer ouvre ou ferme le canal. L'infobulle porte l'adresse complète. |
| **Claude chat** | affiche ou masque le panneau de discussion |
| **About** | version, adresse, nombre d'outils, fichier du jeton, politique d'écriture, chemin du journal. **Oui** copie la commande `claude mcp add`, jeton compris |

L'onglet est le seul point d'entrée. Rien n'est ajouté sous Compléments d'outils.

### Le serveur MCP

| | |
|---|---|
| Adresse | `http://127.0.0.1:8766/mcp` |
| Démarrage | automatique à l'ouverture de Navisworks |
| Outils exposés | 66 |
| Accès | jeton porteur obligatoire, aucun CORS |

Chaque requête exige le jeton, rangé dans `%LOCALAPPDATA%\Aliova\NavisworksMcp\token.txt` (créé au
premier démarrage, conservé ensuite). Le plus simple : **About**, puis **Oui**, puis coller la commande
copiée dans un terminal :

```text
claude mcp add --transport http navisworks http://127.0.0.1:8766/mcp --header "Authorization: Bearer <jeton>"
```

Pour un autre client MCP :

```json
{
  "mcpServers": {
    "navisworks": {
      "type": "http",
      "url": "http://127.0.0.1:8766/mcp",
      "headers": { "Authorization": "Bearer <jeton>" }
    }
  }
}
```

Pourquoi un jeton : écouter sur `127.0.0.1` tient le réseau à l'écart, pas le navigateur. Sans jeton,
n'importe quelle page web ouverte sur le poste pouvait appeler le serveur et exécuter du code dans
Navisworks. Supprimer `token.txt` pour en changer (un nouveau est généré au démarrage suivant, mettre
le client à jour). Pour l'imposer, définir `ALIOVA_NAVISWORKS_MCP_TOKEN` avant de lancer Navisworks.

### Exécution de code et sécurité

Sur le modèle du runtime agent de pyRevit 7 :

| | |
|---|---|
| `navis_query` | lecture seule. L'IL compilé est vérifié **avant** exécution : tout appel qui peut modifier le document est refusé et rien ne s'exécute |
| `navis_modify` | écriture. Les appels d'écriture trouvés dans l'IL, le titre donné par l'agent et le code vous sont montrés dans Navisworks : rien ne s'exécute avant **Appliquer** |
| Toujours refusé | enregistrer, publier, ouvrir, fermer ou vider un document ; `Application.DoEvents` ; `Process.Start` ; `Environment.Exit` |
| Historique | chaque appel d'outil, ses arguments (code compris) et son résultat, dans `%LOCALAPPDATA%\Aliova\NavisworksMcp\runs\`, conservés 14 jours, lisibles par `list_runs` / `get_run` |

Politique d'écriture, définir `ALIOVA_NAVISWORKS_MCP_MODIFY` avant de lancer Navisworks : `ask`
(défaut), `allow` (sans demande, les vérifications restent) ou `deny` (aucune écriture).

La vérification lit le code compilé, ce n'est pas un bac à sable : elle arrête les erreurs de bonne foi
d'un agent, pas un code écrit pour la tromper.

Si le port 8766 est déjà pris, le serveur se replie sur le suivant et **le dit** : sur le
bouton du ruban, dans **About**, et dans le journal. Il faut alors reporter le port affiché
dans la configuration du client, ou libérer 8766. Pour imposer un port, définir
`ALIOVA_NAVISWORKS_MCP_PORT` avant de lancer Navisworks.

L'adresse en service est aussi publiée sur disque, lisible sans ouvrir Navisworks :

```text
%LOCALAPPDATA%\Aliova\NavisworksMcp\endpoint.json
```

### Quand quelque chose ne va pas

Le journal du plugin, avec la chaîne complète des exceptions et l'identifiant du processus :

```text
%LOCALAPPDATA%\Aliova\NavisworksMcp\journal.txt
```

Le bouton **About** propose de l'ouvrir.

### Les skills

Le dossier [`Skills/`](Skills) contient les workflows opérationnels à ajouter côté Claude.
Commencer par [`Skills/README.md`](Skills/README.md), qui porte l'index et les règles
critiques. Les mêmes fichiers sont embarqués dans le plugin et servis par `get_skill` à n'importe quel client MCP. Trois à lire avant tout travail sérieux :

- `00_pieges_navisworks.md` : pièges du modèle et règles de performance, à lire en premier
- `14_navis_query_modify.md` : exécuter du C# à chaud dans Navisworks, avec les pièges vérifiés
- `15_coupe_animee_verdict.md` : question close, avec les preuves

### Désinstaller

Relancer l'installateur, cliquer sur **Uninstall**. Il retire le dossier du plugin
`%APPDATA%\Autodesk\ApplicationPlugins\Aliova.NavisworksMcp.bundle` et son dossier de données
`%LOCALAPPDATA%\Aliova\NavisworksMcp` (jeton, journal, et l'historique des appels, qui contient le
code et le résultat de chaque appel). Pour désinstaller à la main, supprimer les deux dossiers.

### Reconstruire

```powershell
installer\build.ps1
```

Compile le plugin, assemble le paquet Autodesk, **vérifie sa disposition**, le compresse et
le compile dans l'installateur. La vérification n'est pas décorative : Navisworks impose les
chemins des icônes et du XAML du ruban, et un chemin faux ne produit aucune erreur au
chargement, seulement un onglet muet. Détail technique dans
[`NavisworksMcpPlugin/INSTALLATION.md`](NavisworksMcpPlugin/INSTALLATION.md).

### Limites connues

- Le paquet s'installe pour le **compte Windows courant** seulement, sous `%APPDATA%`. Un
  poste partagé demande une variante sous `%PROGRAMDATA%`, qui exige l'administrateur.
- Le manifeste ne déclare que Navisworks **2026** (`Nw23`). Sur un poste en 2025 l'onglet
  n'apparaîtra pas ; il faut recompiler contre l'API de cette version.

---

## Repository map · Carte du dépôt

| Path · Chemin | Purpose · Rôle |
|---|---|
| `MCP Navisworks.exe` | the installer, built by `installer/build.ps1` and published under Releases, not versioned · l'installateur, fabriqué par `installer/build.ps1` et publié dans Releases, non versionné |
| `installer/build.ps1` | builds the installer · fabrique l'installateur |
| `installer/AliovaMcpSetup/` | installer source · code de l'installateur |
| `bundle/PackageContents.xml` | Autodesk package manifest · manifeste du paquet Autodesk |
| `NavisworksMcpPlugin/plugin/` | plugin C# source · code C# du plugin |
| `NavisworksMcpPlugin/plugin/Resources/` | ribbon icons and per-language XAML · icônes du ruban et XAML par langue |
| `Skills/` | skills to add on the Claude side · skills à ajouter côté Claude |
| `assets/logo/` | brand sources · sources de marque |

Aliova · [github.com/Aliova-Inc/aliova-navisworks-mcp](https://github.com/Aliova-Inc/aliova-navisworks-mcp)
