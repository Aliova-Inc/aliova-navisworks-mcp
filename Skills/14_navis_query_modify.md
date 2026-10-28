# 14 - Code C# a chaud dans Navisworks (`navis_query` / `navis_modify`)

## Quand l'utiliser

Des qu'une tache demande une capacite que **aucun outil MCP existant ne couvre**, et en particulier :

- explorer une API non documentee (Animator, couche interop `LcAn*` / `LcOp*`, internes TimeLiner) ;
- verifier une hypothese sur le modele avant d'ecrire un outil dedie ;
- faire une manipulation ponctuelle qui ne merite pas un outil permanent ;
- reproduire un bug pour l'isoler.

**Ne pas l'utiliser** quand un outil dedie existe : `timeliner_add_task`, `build_level_demolition_sets`,
`isolate_by_search`, etc. sont testes, nommes et reutilisables. Ces outils sont un **atelier**, pas un
remplacant. Ce qui marche et resservira doit finir en outil MCP.

**Avant d'ecrire une sonde de reflexion**, essayer `lookup_navis_api` : il renvoie les signatures reelles
d'un type (constructeurs, proprietes get/set, methodes, evenements, valeurs d'enum) sans rien compiler,
et ecarte d'office les homonymes vides (piege 2). Les recettes 1 et 2 ci-dessous restent utiles pour
les cas qu'il ne couvre pas.

---

## Deux outils, deux contrats (depuis 2026-10-06)

`navis_eval` n'existe plus. Il est remplace par deux outils, sur le modele du runtime agent de pyRevit 7
(`run_query` / `run_modify`) :

| | `navis_query` | `navis_modify` |
|---|---|---|
| Usage | lire, sonder, compter, explorer | modifier la maquette |
| Verification avant execution | tout appel d'ecriture est **refuse**, rien ne s'execute | les appels d'ecriture sont **listes** |
| Approbation | aucune | l'utilisateur voit titre, appels d'ecriture et code dans Navisworks, et clique **Appliquer** ou **Refuser** AVANT execution |
| Parametre en plus | | `title` obligatoire, montre a l'utilisateur |
| Annulation | sans objet | changements regroupes dans une transaction `Agent: <title>` quand l'API l'offre (`UndoGroup` dans la reponse) |

### Comment la lecture seule est verifiee

Le code est compile, puis son **IL** est lu avant execution : chaque methode appelee est identifiee
exactement, lambdas et methodes auxiliaires comprises, et en suivant les appels vers les outils de ce
plugin (appeler un outil d'isolation depuis `navis_query` est refuse).

Classe **lecture** sur l'API Navisworks :
- accesseurs `get`, constructeurs, operateurs ;
- methodes dont le premier MOT est `Get`, `Find`, `Is`, `Has`, `Can`, `Contains`, `To`, `Compare`,
  `Count`, `Try`, `Equals`, `Intersects`, `Distance`, `BoundingBox`... (`IsHidden` est une lecture,
  `Isolate` non) ;
- tout ce qui touche un type valeur ou construit localement : `ModelItemCollection`, `Search`, la
  `Selection` d'un `Search` (`s.Selection.SelectAll()`), `SearchCondition`, `Point3D`, `BoundingBox3D`,
  `Color`, `VariantData`...

Tout le reste de l'API est classe **ecriture** : setters, `add_` d'evenement, methodes des parties du
document (`DocumentModels.SetHidden`, `CurrentSelection.CopyFrom`...), interop `LcOp*` hors `Get`.

Refuse aussi en `navis_query` : `MethodBase.Invoke`, `PropertyInfo.SetValue`, `Activator`,
`Delegate.CreateDelegate`, `Reflection.Emit`, `dynamic`, `calli`, `Marshal` en ecriture, ecriture de
fichiers. **Lire les metadonnees par reflexion reste libre** : les recettes 1 a 3 passent.

**Ce n'est pas un bac a sable.** Un accesseur de l'API qui aurait un effet de bord passerait. Le contrat
couvre les erreurs d'un agent de bonne foi, pas un code concu pour tromper la verification.

### Toujours refuse, dans les deux outils

| Appel | Pourquoi |
|---|---|
| `SaveFile`, `SaveAs`, `Publish*` | l'utilisateur garde la main sur son fichier |
| `OpenFile`, `AppendFile`, `MergeFile`, `Close`, `Exit`, `Document.Clear` | un run ne change pas de document |
| `Application.DoEvents`, `Dispatcher.PushFrame` | reentrance, plantage du 2026-08-06 (regle 6 ci-dessous) |
| `Environment.Exit`, `Process.Start` / `Kill` | ne pas sortir du perimetre de Navisworks |

Un refus renvoie `Ok = false`, la liste `Violations` et un `Hint`. **Rien n'a ete execute.**

### Politique d'ecriture du poste

Variable d'environnement `ALIOVA_NAVISWORKS_MCP_MODIFY`, lue a chaque appel :
`ask` (defaut) demander a chaque fois, `allow` ne pas demander (verification et blocages restent),
`deny` refuser toute ecriture. Le bouton **About** du ruban affiche la politique en vigueur.

### Historique

Chaque appel est journalise avec son code et son resultat : `list_runs` puis `get_run(id)`.
Un appel en `phase = unfinished` n'a jamais rendu la main : Navisworks est probablement tombe
pendant son execution, et `get_run` donne le code exact qui l'a fait tomber.

---

## Le contrat

### Mode par defaut : corps de methode

`code` est un **corps de methode qui doit se terminer par un `return`**.

Deja disponible, sans rien ecrire :

| Element | Ce que c'est |
|---|---|
| `doc` | `Document` actif (`Application.ActiveDocument`) |
| `P(objet)` | journalise une ligne |
| `P(format, a, b)` | journalise une ligne formatee (`string.Format`, indices `{0}`) |
| espaces de noms | `Autodesk.Navisworks.Api`, `.Interop`, `.Timeliner`, `System`, `System.Linq`, `System.Reflection`, `System.Text`, `System.Collections.Generic`, `NavisworksMcpPlugin.Tools` |

```csharp
P("modeles : {0}", doc.Models.Count);
return doc.Models.RootItemDescendantsAndSelf.Count();
```

Le **journal `P(...)` est renvoye meme si le code leve ensuite**. Sur une sonde, les lignes ecrites
avant le plantage sont souvent l'information la plus utile : journaliser a chaque etape, pas seulement
a la fin.

### Mode `fullSource=true` : fichier complet

Obligatoire des qu'il faut des **methodes auxiliaires ou des types**, C# 5 n'ayant pas de fonctions
locales. Le fichier doit declarer exactement ceci :

```csharp
using System; using System.Linq; using System.Text; using System.Collections.Generic;
using System.Reflection; using System.Globalization;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Interop;

public static class __Snippet
{
    public static readonly StringBuilder __log = new StringBuilder();
    static void P(object o) { __log.AppendLine(o == null ? "(null)" : o.ToString()); }

    static string Helper(ModelItem it) { return it.DisplayName; }   // methodes libres ici

    public static object Run()
    {
        var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
        return "ok";
    }
}
```

Le champ `__log` doit garder ce nom exact, l'outil le relit par reflexion.

---

## C# 5 : ce qui ne compile PAS

Le compilateur integre au .NET Framework est utilise. **Langage C# 5.** Les erreurs les plus frequentes
viennent de la, pas de l'API.

| Interdit | Ecrire a la place |
|---|---|
| `$"x = {v}"` | `string.Format("x = {0}", v)` ou `P("x = {0}", v)` |
| `obj?.Membre` | `obj == null ? null : obj.Membre` |
| `nameof(x)` | `"x"` |
| `int F() => 1;` | `int F() { return 1; }` |
| `public int P { get; } = 3;` | champ + affectation dans un constructeur |
| `using static ...` | nom complet |
| fonction locale | `fullSource=true` et methode a part |
| `out var x` | `Type x; ... out x;` |

Fonctionnent normalement : `var`, LINQ, lambdas, generiques, initialiseurs d'objet et de collection,
`async`.

**Les numeros de ligne des erreurs comptent a partir de la premiere ligne du code envoye**, pas du
prologue genere.

---

## Discipline de securite

Ces outils executent du code dans le processus Navisworks. Un mauvais appel vers la couche native
**fait tomber l'application, sans boite de dialogue**, en lecture comme en ecriture.

1. **Lecture d'abord.** Toute nouvelle piste commence par une sonde `navis_query` qui ne fait que lire
   et journaliser. Ne jamais decouvrir une API en ecrivant dedans.
2. **Enregistrer avant toute ecriture.** Demander explicitement a l'utilisateur d'enregistrer le
   `.nwf`, et attendre sa confirmation. Ne pas enchainer sonde et ecriture dans le meme envoi. Le
   plugin refuse d'enregistrer a sa place.
3. **Un pas a la fois.** Construire l'objet, le relire, verifier, et seulement apres l'inserer dans le
   document.
4. **Reflexion sur les metadonnees = sans risque.** Lister constructeurs et methodes n'execute rien.
   **Invoquer** un constructeur non public alloue un objet natif par une voie non prevue : c'est la
   seule categorie qui merite un avertissement explicite a l'utilisateur avant execution.
5. Les assemblies compilees **ne se dechargent pas**. Ne pas compiler en boucle : regrouper les sondes
   dans un seul envoi.
6. **NE JAMAIS pomper la boucle de messages.** `Application.DoEvents()` combine a `Thread.Sleep` dans
   un snippet fait REENTRER Navisworks dans son propre fil d'interface pendant qu'il execute deja du
   code a nous. C'est le **suspect principal du plantage du 2026-08-06** (snippet qui lancait la
   lecture de simulation puis pompait les messages pour l'observer). Pour observer une operation
   asynchrone : rendre la main, puis rappeler `navis_query` dans un second temps. Ne jamais attendre
   dans le snippet. `DoEvents` est desormais refuse a la verification.
7. **Un long snippet gele l'interface.** Le code s'execute sur le fil d'interface : une boucle de
   plusieurs centaines de rendus fige Navisworks tout du long. Prevenir l'utilisateur, et decouper si
   la duree depasse une minute ou deux.

---

## Recette 1 : trouver la vraie classe

Le reflexe fondamental. **Ne jamais deduire l'existence ou l'absence d'une API sans avoir liste les
types.**

```csharp
var needle = "SectionKeyFrame";
foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
{
    var n = a.GetName().Name;
    if (n == null || n.IndexOf("navisworks", StringComparison.OrdinalIgnoreCase) < 0) continue;
    Type[] ts;
    try { ts = a.GetTypes(); }
    catch (ReflectionTypeLoadException ex) { ts = ex.Types.Where(t => t != null).ToArray(); }
    foreach (var t in ts)
        if (t.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
            P("{0,-8} {1,-60} [{2}]  membres={3}",
              t.IsPublic ? "public" : "internal", t.FullName, n,
              t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic |
                           BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Length);
}
return "fin";
```

Lire la sortie avec **trois** colonnes en tete : la visibilite, **l'espace de noms complet**, et
**l'assembly**. Voir le piege des homonymes plus bas.

## Recette 2 : lister constructeurs et methodes

```csharp
var t = typeof(Autodesk.Navisworks.Api.Interop.LcAnClipAnim);
var f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
foreach (var c in t.GetConstructors(f))
    P("{0} ctor({1})", c.IsPublic ? "public" : c.IsFamily ? "protected" : "private",
      string.Join(", ", c.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name)));
foreach (var m in t.GetMethods(f).Where(x => !x.IsSpecialName).OrderBy(x => x.Name))
    P("{0}{1} {2} {3}({4})", m.IsPublic ? "public" : "internal", m.IsStatic ? " static" : "",
      m.ReturnType.Name, m.Name,
      string.Join(", ", m.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name)));
return t.FullName;
```

Ajouter `| BindingFlags.DeclaredOnly` pour ne voir que ce que le type declare lui-meme, l'enlever pour
voir l'heritage. **Une methode de creation se trouve souvent sur la classe de BASE**, pas sur le type
concret : ne pas conclure sur le seul type concret.

## Recette 3 : LIRE LE CODE COMPILE plutot que d'essayer

Quand une classe n'a pas de constructeur public, ne pas forcer : **lire ce que fait le constructeur
public d'une classe soeur**. Cette lecture n'execute rien.

```csharp
var m = typeof(Autodesk.Navisworks.Api.Interop.LcAnCameraKeyFrame)
        .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
        .FirstOrDefault(c => c.GetParameters().Length == 0);
var il = m.GetMethodBody().GetILAsByteArray();
var mod = m.Module;
for (int i = 0; i + 4 < il.Length; i++)
{
    byte op = il[i];
    if (op != 0x28 && op != 0x6F && op != 0x73) continue;   // call / callvirt / newobj
    try
    {
        var t = mod.ResolveMethod(BitConverter.ToInt32(il, i + 1));
        if (t != null) P("[{0,3}] {1}.{2}", i, t.DeclaringType == null ? "<Module>" : t.DeclaringType.Name, t.Name);
    }
    catch { }   // un octet d'operande mal interprete ne resout pas : on l'ignore
}
return "fin";
```

Le balayage est naif (un octet d'operande peut ressembler a un opcode) mais sur un constructeur court
la sequence resolue est limpide. Pour trouver les fonctions globales C++/CLI d'un type :
`assembly.GetModules()[0].GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)`
puis filtrer sur le nom (`"LcAnCameraKeyFrame.new"`, `".{ctor}"`, `".delete"`).
**L'absence de `.new` et `.{ctor}` pour un type prouve qu'il n'est pas constructible.**

## Recette 4 : echantillonner le modele avant de filtrer

```csharp
int n = 0;
foreach (var it in doc.Models.RootItemDescendantsAndSelf)
{
    if (!it.HasGeometry) continue;
    if (n++ > 3) break;
    P("--- {0}", it.DisplayName);
    foreach (var cat in it.PropertyCategories)
        foreach (var p in cat.Properties)
            P("   [{0}] {1} = {2}", cat.DisplayName, p.DisplayName,
              p.Value.IsDisplayString ? p.Value.ToDisplayString() : p.Value.ToString());   // ToDisplayString leve sinon
}
return n + " elements echantillonnes";
```

---

## Pieges verifies

Chacun a coute une erreur reelle. Les relire avant de sonder une zone nouvelle.

### Sur la structure des assemblies

1. **Assemblies satellites.** Une fonctionnalite absente de `Autodesk.Navisworks.Api.dll` est
   probablement dans `Autodesk.Navisworks.<Feature>.dll` (`Timeliner`, `Clash`). Dans l'API principale,
   `IDocumentTimeliner` n'est qu'une interface marqueur vide : conclure « pas d'API » a partir d'elle
   est faux.
2. **Structures marqueurs homonymes.** **NEUF** assemblies Navisworks declarent des **structures vides,
   sans espace de noms**, portant le nom des classes du noyau (`LcOpSectionKeyFrame`, `LcOaClipPlaneSet`,
   `LcOpKeyFrame`, `LcAnClipKeyFrame`...). Ce sont des etiquettes pour nommer des pointeurs natifs.
   Une resolution par nom court tombe dessus et fait conclure a tort « type sans constructeur ni
   methode ». Cette erreur a coute trois tours de sonde le 2026-08-06.
   **Toujours resoudre par nom complet, verifier l'assembly, et privilegier le candidat qui a des
   membres.** Exemple : `LcOpSectionKeyFrame` existe en 5 exemplaires vides et **une seule fois pour
   de vrai**, publique, dans `Autodesk.Navisworks.Api.dll`, espace de noms
   `Autodesk.Navisworks.Api.Interop`.
2bis. **La couche `LcOp*` n'est PAS dans l'assembly de l'Animator.** `LcAn*` (couche document) vit dans
   `navisworks.animator.clr` ; `LcOp*` (couche noyau) vit dans `Autodesk.Navisworks.Api`. Ne sonder
   qu'une assembly conduit systematiquement a une fausse conclusion : balayer
   `AppDomain.CurrentDomain.GetAssemblies()` filtre sur « navisworks ».
3. **L'Animator n'a aucune API managee.** `Document` n'expose ni scene ni animation. Tout passe par
   `LcAn*` dans `Autodesk.Navisworks.Api.Interop`.

### Sur la couche interop `LcAn*`

4. `LcAnSavedAnimationsElement.Get(state)` **renvoie null tant qu'aucune scene Animator n'existe**
   (initialisation paresseuse). Tester le null avant de conclure a une absence de capacite.
5. Les **mutations sont des methodes STATIQUES** : `LcAnSavedAnimationsElement.InsertItemCopy(state,
   parent, index, item)` et `DeleteItem(state, parent, index)`. `Get`, `GetNum`, `MakePath` sont
   d'instance. `Initialise` / `IsInitialised` sont **statiques** (enregistrement de classe, pas etat
   d'instance).
6. Ctors **publics** : `LcAnScene`, `LcAnSelectionAnim`, `LcAnSelectionKeyFrame`, `LcAnCameraAnim`,
   `LcAnCameraKeyFrame`, `LcAnClipAnim`, `LcOpSectionAnimation`. Ctors **non publics** :
   `LcAnClipKeyFrame`, `LcOpSectionKeyFrame`, `LcOpKeyFrame`, `LcAnScript`, `LcAnAction`, `LcAnEvent`.
   Pour ces derniers, chercher une methode de creation sur la classe de base
   (`LcAnAnimation.AddCoreKeyFrame`, `LcOpSectionAnimation.AddKeyFrameOwn`) plutot que de forcer le
   constructeur. **Verifier ensuite que l'allocateur natif existe** (recette 3) : pour la coupe il
   n'existe pas, et aucune de ces methodes n'est utilisable faute d'objet a leur passer.
6bis. **`SavedItem.CreateCopy()` est public sur tout.** C'est la porte de sortie quand un type n'est pas
   constructible : obtenir UN exemplaire par l'interface, puis le cloner autant de fois que voulu et
   le regler par ses accesseurs. C'est ainsi que le balayage de coupe a pu etre genere.
7. `SavedItemId` d'un `SavedItemReference` est un **chemin hierarchique separe par des sauts de ligne**
   (`"scene\nanim"`), pas un identifiant plat.
8. Le `DisplayName` pose sur une image-cle **avant** `InsertItemCopy` **ne persiste pas**.

### Sur TimeLiner

9. **Les noms des types de tache sont LOCALISES** (installation FR : `Construire`, `Demolir`,
   `Temporaire`). Un nom inconnu ne leve aucune erreur : la simulation devient **muette**.
10. Sur une tache **neuve**, lire `task.Selection.SelectionSources` leve
    « Invalid operation when '!HasSelectionSources' ». Passer par
    `task.Selection.CopyFrom(new SelectionSourceCollection{...})`.
11. **Reconstruire un jeu de selection casse les rattachements** : la source pointe l'objet, pas le nom.
    Ordre obligatoire : **jeux d'abord, taches ensuite**.
12. Un element portant **a la fois** une tache `Construire` et une tache `Demolir` est **visible des le
    depart** de la simulation. Pour un equipement temporaire, utiliser le type `Temporaire`.
13. `LcTlSimulationHelper.Initialise(state)` **reinitialise** le helper (dates a 1970,
    `TimeControlsValid=false`). N'appeler que si `Instance` est null. Le curseur reste dormant tant que
    l'onglet `Simulate` n'a pas ete ouvert au moins une fois depuis le demarrage de Navisworks.

### Sur les vues et la camera

14. `InternalViewpoint.GetCamera()` renvoie une camera **partagee**, pas un instantane. Copier
    `Position`, `Rotation`, `HeightField` et `Projection` dans un `LcNvCamera` neuf, sinon toutes les
    images-cles sont identiques.
15. En projection **orthographique**, deplacer la camera ne zoome pas : seul `HeightField` agit.
16. Sans verrouillage du vecteur haut, une transition de camera roule et donne le vertige.
17. **Aucune animation de COUPE n'est jouable en simulation.** Ni par animation de points de vue, ni
    par la colonne `Animation` d'une tache, ni par le reglage de simulation qui n'accepte que des
    cameras. Question close, quatre preuves : `15_coupe_animee_verdict.md`. **Ne pas rouvrir.**
18. **Ne jamais capturer tout l'ecran.** `Graphics.CopyFromScreen` sur le bureau virtuel ramene les
    fenetres personnelles de l'utilisateur. Capturer la SEULE fenetre Navisworks, par
    `PrintWindow(hwnd, hdc, 2)` avec `PW_RENDERFULLCONTENT`, qui fonctionne meme si elle est masquee.

### Sur le modele pilote (source Revit FR)

18. Categorie de proprietes : **`Elément`** (accent sur le 2e e seulement). Niveau : **`Calque`**.
    Marqueur d'absence : `<Pas de niveau>`.
19. Filtrer sur la **famille** ne suffit pas : les plateformes ont pour famille `Sol`, seul le **type**
    (`Plateforme metallique - tole 13 mm`) les distingue. Prevoir exclusion par famille **et** par type.
20. Un re-export Revit change les comptes. Recompter, ne jamais reutiliser les chiffres d'une session
    precedente.

---

## Lire un echec

| Retour | Sens | Reflexe |
|---|---|---|
| `CompileErrors` non vide | erreur de langage | verifier la table C# 5 avant de suspecter l'API |
| `Violations` non vide | refuse avant execution | en query, passer a `navis_modify` si l'ecriture est voulue ; sinon retirer l'appel |
| `Approved = false` | l'utilisateur a refuse | lui demander ce qu'il veut changer, ne pas reproposer tel quel |
| `Error` + `StackTrace` | exception a l'execution | lire `Log` : les lignes `P(...)` disent ou ca s'est arrete |
| `Ok=true`, `Result` vide | le code n'a rien trouve | ce n'est **pas** une preuve d'absence : verifier la sonde elle-meme sur un cas connu qui marche |
| Navisworks tombe | appel natif invalide | retrouver le snippet exact avec `list_runs` (phase `unfinished`) puis `get_run`, ne pas le rejouer |

**Regle de preuve.** Un compteur a zero n'etablit rien tant que la meme sonde n'a pas renvoye un
resultat non nul sur un cas dont on sait qu'il est bon. Cette contre-epreuve a deja evite une conclusion
fausse : trois captures manuelles ont ete demandees a l'utilisateur avant que la sonde soit elle-meme
verifiee.

---

## Apres coup

Ce qui a marche ne reste pas dans un snippet :

1. le transformer en **outil MCP** dans `plugin/Tools/` s'il resservira ;
2. consigner tout nouveau piege **ici** ;
3. si le comportement contredit une ligne de ce skill, **corriger la ligne**, ne pas ajouter une
   exception a cote.
