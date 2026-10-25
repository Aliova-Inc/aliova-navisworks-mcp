# 15 - Animer une COUPE dans une simulation : QUESTION CLOSE

> **Verdict : impossible dans Navisworks Manage 2026.** Établi le 2026-08-06 par quatre preuves
> indépendantes, chacune vérifiée à l'écran ou dans le code compilé. **Ne pas rouvrir cette piste.**
> Si un jour Autodesk exporte l'allocateur manquant, la seule ligne à retester est la §C.

---

## La demande

Faire **balayer un plan de coupe** pendant une simulation 4D TimeLiner, pour passer de façon fluide
d'une vue extérieure à une vue en coupe, sur le projet pilote.

## Ce qui MARCHE, et qu'il faut réutiliser

| Capacité | État | Comment |
|---|---|---|
| Piloter les plans de coupe par code | **OK** | `Viewpoint.ClipPlanes`, `ClipPlaneSet.Translate(i, Vector3D)` |
| Le rendu tient compte de la coupe | **OK** | `Document.GenerateImage`, images différentes vérifiées |
| Créer un conteneur d'animation de coupe | **OK** | `new LcAnClipAnim()`, constructeur public |
| **Cloner** une image-clé de coupe existante | **OK** | `LcAnClipKeyFrame.CreateCopy()`, public. 16 clés générées et relues |
| Régler une image-clé clonée | **OK** | `SetPlanes(ClipPlaneSet)`, `SetTime`, `SetInterpolate`, tous publics |
| Piloter le curseur de simulation | **OK** | `LcTlSimulationHelper.SetSimulationTime` |
| Animations de caméra et d'objet en 4D | **OK** | colonne `Animation` d'une tâche |

## Ce qui NE MARCHE PAS, avec la preuve

### A. Animation de points de vue portant des plans de coupe
`SavedViewpointAnimation` + N points de vue dont un plan est déplacé. **TimeLiner ignore les plans de
coupe portés par un point de vue.** Vérifié **deux fois** par comparaison d'images rendues : images
rigoureusement identiques. Ajouter des `SavedViewpointAnimationCut` (délais) aggrave : on retombe sur
la vue large non sectionnée. Outil conservé mais marqué abandonné : `viewpoint_build_section_sweep`.

### B. Boîte de coupe au lieu d'un plan
Même verrou, aucun gain. `ClipPlaneSet.Mode = Box` ne change rien au problème d'animation.

### C. Créer une image-clé de coupe par code
`LcAnClipKeyFrame` n'a **aucun constructeur public**, contrairement à ses sœurs. Comparaison des
fonctions globales C++/CLI dans `navisworks.animator.clr` :

```
LcAnCameraKeyFrame.new(UInt64)                       présent
LcAnCameraKeyFrame.{ctor}(LcAnCameraKeyFrame*)       présent
LcAnCameraKeyFrame.delete(Void*)                     présent

LcAnClipKeyFrame.SetPlanes / EditPlanes / GetPlanes  présents
LcAnClipKeyFrame.new / .{ctor} / .delete             ABSENTS
```

Constructeur public de la sœur caméra, lu en IL :
```
LcAnCameraKeyFrame.new(0x100)          alloue 256 octets natifs
LcAnCameraKeyFrame.{ctor}(ptr)         exécute le constructeur C++
NativeHandle.InternalInit(ptr, ...)    rattache l'enveloppe gérée
```
`LcAnClipKeyFrame.InternalFactory`, lu en IL : `newobj ..ctor(NativeHandleInit&)` **et rien d'autre**,
donc une enveloppe gérée sur un handle **nul**. La forcer par réflexion produit un objet qui
déréférence du vide au premier appel. **La sonde correspondante n'a jamais été exécutée et a été
retirée de la surface MCP.**

Autodesk a exporté de quoi **lire et modifier** une image-clé de coupe, pas de quoi en **créer** une.

### D. Animation Animator de coupe attachée à une tâche
Contournement réussi de la §C : l'utilisateur capture **une** image-clé à la main dans l'Animator, puis
`animator_build_section_sweep_from_keyframe` la clone. Résultat obtenu et vérifié :

```
Coupe Scene 3 / Plan de coupe   <LcAnClipAnim>   16 clés   t=0..60 s
   clé  1  t= 0.0 s   plan 2  d=-280.070
   clé  6  t=20.0 s   plan 2  d=-262.493
   clé 11  t=40.0 s   plan 2  d=-244.915
   clé 16  t=60.0 s   plan 2  d=-227.337
```
Durée `CalcDuration` = 60 s, comportement `Scale`, `DocumentPartId` identiques aux animations de caméra
qui fonctionnent. **Et pourtant, en lecture réelle, le plan reste figé à −280,070 du début à la fin :**

```
t=J0 00:00     ePLAYING           distance=-280.070
t=J1 13:24     ePLAYING           distance=-280.070
t=J2 09:48     ePLAYING           distance=-280.070
t=J4 00:00     eSTOPPED_AT_END    distance=-280.070
```
**TimeLiner n'applique pas les animations de coupe attachées à une tâche.**

### E. Réglage `Animation` au niveau de la simulation
Le second mécanisme, distinct de la colonne des tâches. Aucun accès par code : ni `TimelinerSettings`
(managé) ni aucun type `LcTl*` (interop) n'expose ce réglage ; seul `DisplaySettingsDialog()` existe.
Ouvert à la main, le menu déroulant ne liste **que des animations de caméra** :

```
Aucun lien
Animation des points de vue enregistrés
Zoom Cabine->Camera
Transition Scene 1->Camera
Transition Scene 2->Camera
Transition Scene 3->Camera
```
Ni les animations de sélection (cabine) qui existent et fonctionnent, ni l'animation de coupe. Ce
réglage pilote le **point de vue**, et n'accepte que des caméras.

---

## La solution de repli, validée

**Séquence d'images rendue**, où l'on pilote soi-même les deux choses image par image :

```csharp
h.SetSimulationTime(t);                   // avance le 4D
Coupe(distance);                          // avance le plan de coupe
doc.GenerateImage(ImageGenerationStyle.ScenePlusOverlay, 1600, 900, true);
```
Les deux mécanismes sont prouvés séparément. Coût mesuré : **83 ms par image en 1600x900**, donc une
séquence de 700 images en environ une minute. Assemblage :
`ffmpeg -framerate 25 -i IMG_%04d.png -c:v libx264 -pix_fmt yuv420p -crf 18 sequence.mp4`

Précautions pour ce rendu :
- garder un `ClipPlaneSet` de **référence pristine** et poser chaque distance en **absolu**
  (`CopyFrom(reference)` puis `Translate`), jamais en cumulant les décalages ;
- pendant la partie 4D, ne réimposer l'ouverture de la coupe **que si elle a dérivé**, sinon on lutte
  contre l'animation de caméra à chaque image ;
- adoucir le balayage : `e = u*u*(3-2*u)` ;
- restaurer la coupe de l'utilisateur à la fin.

---

## Procédure de capture manuelle, quand une image-clé de coupe est nécessaire

Trois tentatives ont échoué avant qu'on comprenne pourquoi : **le panneau Animator n'était pas ouvert.**

1. Ruban **`Animation`** → bouton **`Animator`**.
2. Dans l'arbre, sélectionner la ligne **du jeu d'animation** (ex. `Plan de coupe`), **pas** la scène
   qui le contient. C'est l'erreur classique : le bouton de capture ne vise alors rien.
3. Sectionnement actif (`Outils de coupe` → `Activer la coupe`).
4. Curseur de temps à 0, puis icône **clé** `Capturer l'image clé`.

Une seule capture suffit : `CreateCopy()` fait le reste.

---

## Leçons portables, indépendantes de la coupe

1. **Vérifier l'instrument avant de conclure sur la mesure.** Une sonde qui renvoie zéro ne prouve
   rien tant qu'elle n'a pas renvoyé un résultat non nul sur un cas connu bon. Faute de cette
   contre-épreuve, l'utilisateur a refait trois fois une capture qui n'était pas en cause.
2. **Un nom court ne suffit jamais à identifier un type.** Neuf assemblies Navisworks déclarent des
   structures **vides et sans espace de noms** portant le nom des classes du noyau
   (`LcOpSectionKeyFrame`, `LcOaClipPlaneSet`…). Ce sont des étiquettes pour nommer des pointeurs
   natifs. Résoudre par **nom complet + assembly**, et privilégier le candidat qui a des membres.
   La vraie `LcOpSectionKeyFrame` est publique, dans `Autodesk.Navisworks.Api.dll`.
3. **Lire l'IL vaut mieux qu'essayer.** `MethodBase.GetMethodBody().GetILAsByteArray()` +
   `Module.ResolveMethod(token)` révèle ce que fait un constructeur sans rien exécuter. C'est ce qui a
   évité d'exécuter une sonde qui aurait planté Navisworks.
4. **Une capacité absente de la surface publique n'est pas forcément absente de l'API** (le TimeLiner
   était dans une assembly satellite), **mais elle peut l'être vraiment** : ici l'allocateur natif
   n'existe simplement pas. Chercher la fonction de création, pas seulement le constructeur.
