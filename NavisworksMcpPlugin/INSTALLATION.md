# Installation · technical details / détails techniques

For everyday installation, see the [README](../README.md) at the root.
Pour l'installation courante, voir le [README](../README.md) à la racine.

This file explains **why** the installation has this shape, and exactly what it touches.
Ce fichier explique **pourquoi** l'installation a cette forme, et ce qu'elle touche exactement.

---

## English

### Where the files land

```text
%APPDATA%\Autodesk\ApplicationPlugins\Aliova.NavisworksMcp.bundle\
    PackageContents.xml
    Contents\2026\
        NavisworksMcpPlugin.dll
        System.Text.Json.dll  and its satellites
        mcp_16.ico  mcp_32.ico  chat_*.ico  about_*.ico
        fr-FR\AliovaMcpRibbon.xaml
        en-US\AliovaMcpRibbon.xaml
```

This is Autodesk's **application package** format, the one used by the commercial plugins on a
typical workstation — BIM Track, Newforma Konekt and BIM 360 Issues all follow this layout.
Navisworks scans this folder at startup, reads each `PackageContents.xml` and loads the
components whose declared release matches.

Nothing is written into the Navisworks installation folder, nothing into the registry. Three
consequences: no administrator, uninstall by deleting a folder, no trace left behind.

### The declared release

The manifest declares only `Nw23`, that is Navisworks 2026, because the DLL is compiled
against that API and has not been verified elsewhere. Declaring a wider range would produce a
load crash, not compatibility. Series codes, read off the third-party packages on the machine:
2021 `Nw18`, 2022 `Nw19`, 2023 `Nw20`, 2024 `Nw21`, 2025 `Nw22`, 2026 `Nw23`.

### Why dependencies travel inside the package

Navisworks loads the plugin, but the CLR then resolves its dependencies from the host
application folder and its private probing path — **not** from the plugin folder. The old
installation therefore copied `System.Text.Json` and its satellites into
`Navisworks\Dependencies`, which required administrator rights.

The plugin now resolves them itself, from its own folder
([`plugin/Plugin/DependencyResolver.cs`](plugin/Plugin/DependencyResolver.cs)). That is what
removes the need for elevation.

Verified, not assumed. A first test using `Assembly.LoadFrom` proved nothing: that load mode
probes the assembly's own folder by itself. Redone with `LoadFile`, which does not probe, then
with a negative control by removing the dependency from the folder:

```text
with the file    -> resolver ACTIVE
without it       -> FileNotFoundException: System.Text.Json 8.0.0.5
```

### The ribbon, and its dictated paths

An `AddInPlugin` can **never** carry an icon: neither `PluginAttribute` nor
`AddInPluginAttribute` exposes an icon property. A `CommandHandlerPlugin` is required, with
`[RibbonLayout]` and `[RibbonTab]`, and the icons declared on `[Command]`.

Navisworks dictates two paths, relative to the DLL folder:

| Resource | Required location |
|---|---|
| icons (`Icon` / `LargeIcon`) | flat, next to the DLL |
| ribbon XAML (`RibbonLayout`) | in a language sub-folder (`fr-FR`, `en-US` as fallback) |

A wrong path produces **no error** at load time: the tab appears, silent. Same symptom if a
XAML `Id` does not match a `[Command]` name: the button shows up and stays inert. That is why
[`installer/build.ps1`](../installer/build.ps1) reads back the compiled assembly's attributes
and refuses to produce an installer if an icon named in the code is missing from the package.

### Server startup

Startup relied on a plugin declared with `AddInLocation.None` and `LoadForCanExecute`. With no
presence in the interface at all, Navisworks never asks it for `CanExecute`: the server did
not start, silently. It is now carried by the ribbon plugin, which is provably loaded since
the tab is drawn.

No dialog is ever opened from `CanExecuteCommand`: that method runs while Navisworks is
**painting** its ribbon, and opening a modal window there pumps the message loop and makes
Navisworks re-enter itself. A failure is therefore logged and shown on the button; the
explanation appears only on a click.

### Rebuilding

```powershell
installer\build.ps1                    # everything
installer\build.ps1 -SkipPluginBuild   # without recompiling the plugin
```

---

## Français

### Où les fichiers sont posés

```text
%APPDATA%\Autodesk\ApplicationPlugins\Aliova.NavisworksMcp.bundle\
    PackageContents.xml
    Contents\2026\
        NavisworksMcpPlugin.dll
        System.Text.Json.dll  et ses satellites
        mcp_16.ico  mcp_32.ico  chat_*.ico  about_*.ico
        fr-FR\AliovaMcpRibbon.xaml
        en-US\AliovaMcpRibbon.xaml
```

C'est le format de **paquet d'application** d'Autodesk, celui des plugins commerciaux
installés sur un poste type — BIM Track, Newforma Konekt, BIM 360 Issues suivent tous cette
disposition. Navisworks parcourt ce dossier au démarrage, lit chaque `PackageContents.xml` et
charge les composants dont le millésime déclaré correspond.

Rien n'est écrit dans le répertoire d'installation de Navisworks, rien dans le registre.
Trois conséquences : pas d'administrateur, désinstallation par simple suppression du dossier,
aucune trace laissée derrière.

### Le millésime déclaré

Le manifeste ne déclare que `Nw23`, c'est-à-dire Navisworks 2026, parce que la DLL est
compilée contre cette API et n'a pas été vérifiée ailleurs. Déclarer une plage plus large
donnerait un plantage au chargement, pas une compatibilité.

Codes de série relevés sur les paquets tiers du poste :

| Millésime | Code |
|---|---|
| 2021 | `Nw18` |
| 2022 | `Nw19` |
| 2023 | `Nw20` |
| 2024 | `Nw21` |
| 2025 | `Nw22` |
| 2026 | `Nw23` |

### Pourquoi les dépendances voyagent dans le paquet

Navisworks charge le plugin, mais le CLR résout ensuite ses dépendances depuis le dossier de
l'application hôte et son chemin de sondage privé — **pas** depuis le dossier du plugin.
L'ancienne installation copiait donc `System.Text.Json` et ses satellites dans
`Navisworks\Dependencies`, ce qui exigeait les droits administrateur.

Le plugin les résout maintenant lui-même, depuis son propre dossier
([`plugin/Plugin/DependencyResolver.cs`](plugin/Plugin/DependencyResolver.cs)). C'est ce qui
supprime le besoin d'élévation.

Vérifié, pas supposé. Une première épreuve avec `Assembly.LoadFrom` ne prouvait rien : ce
mode de chargement sonde de lui-même le dossier de l'assembly. Refaite avec `LoadFile`, qui
ne sonde pas, puis avec un contrôle négatif en retirant la dépendance du dossier :

```text
avec le fichier    -> résolveur ACTIF
sans le fichier    -> FileNotFoundException : System.Text.Json 8.0.0.5
```

### Le ruban, et ses chemins imposés

Un `AddInPlugin` ne peut **jamais** porter d'icône : ni `PluginAttribute` ni
`AddInPluginAttribute` n'exposent de propriété d'icône. Il faut un `CommandHandlerPlugin`,
avec `[RibbonLayout]` et `[RibbonTab]`, et les icônes déclarées sur `[Command]`.

Deux chemins sont imposés par Navisworks, relatifs au dossier de la DLL :

| Ressource | Emplacement obligatoire |
|---|---|
| icônes (`Icon` / `LargeIcon`) | à plat, à côté de la DLL |
| XAML du ruban (`RibbonLayout`) | dans un sous-dossier de langue (`fr-FR`, `en-US` en repli) |

Un chemin faux ne produit **aucune erreur** au chargement : l'onglet s'affiche, muet. Même
symptôme si un `Id` du XAML ne correspond pas au nom d'un `[Command]` : le bouton apparaît et
reste inerte. C'est pour cela que [`installer/build.ps1`](../installer/build.ps1) relit les
attributs de l'assembly compilée et refuse de produire un installateur si une icône nommée
dans le code manque du paquet.

### Le démarrage du serveur

Le démarrage reposait sur un plugin déclaré en `AddInLocation.None` avec `LoadForCanExecute`.
Sans aucune présence dans l'interface, Navisworks ne lui demande jamais son `CanExecute` :
le serveur ne démarrait pas, en silence. Il est maintenant porté par le plugin de ruban, dont
on peut prouver qu'il est chargé puisque l'onglet se dessine.

Aucune boîte de dialogue n'est ouverte depuis `CanExecuteCommand` : cette méthode tourne
pendant que Navisworks **dessine** son ruban, et y ouvrir une fenêtre modale fait pomper la
boucle de messages et réentrer Navisworks dans lui-même. Un échec est donc journalisé et
affiché sur le bouton ; l'explication n'apparaît que sur un clic.

### Pour reconstruire

```powershell
installer\build.ps1            # tout
installer\build.ps1 -SkipPluginBuild   # sans recompiler le plugin
```
