using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Interop;
using Autodesk.Navisworks.Api.Timeliner;
using NavisworksMcpPlugin.Mcp;

namespace NavisworksMcpPlugin.Tools
{
    /// <summary>
    /// SONDE EXPLORATOIRE de l'Animator (déplacement d'objets dans une simulation 4D).
    ///
    /// Contexte : contrairement à TimeLiner, l'Animator n'a AUCUNE API managée. `Document` n'expose
    /// ni animation ni scène (vérifié : Timeliner, Clash, Takeoff, SavedViewpoints, Grids, Alignments,
    /// et rien d'autre). Le modèle de données n'existe que dans la couche interop `LcAn*` de
    /// navisworks.animator.clr.dll, et aucun exemple de création n'est publié.
    ///
    /// Ce que la réflexion a établi avant d'écrire cette sonde :
    ///  - `LcOpState.GetActiveInstance()` est public statique : la porte d'entrée existe sans COM.
    ///  - `LcAnSavedAnimationsElement.Get(LcOpState)` est public statique : la racine est atteignable.
    ///  - `LcAnSelectionKeyFrame` a un ctor PUBLIC sans argument (motif SWIG des types créables par
    ///    l'utilisateur) et sait SetTranslation / SetTime / SetInterpolate.
    ///  - `LcAnAnimation` et `LcAnAnimationGroup` n'ont qu'un ctor NON PUBLIC (NativeHandleInit&) :
    ///    Autodesk les a fermés délibérément. Seule échappatoire théorique : InternalFactory (public
    ///    statique) + le ctor non public par réflexion.
    ///
    /// ⚠️ La création (tryCreateAnimation=true) touche de la plomberie native non documentée et peut
    /// FAIRE PLANTER Navisworks. Enregistrer le document avant. Par défaut la sonde est en LECTURE
    /// SEULE et n'insère rien dans le document.
    /// </summary>
    [McpServerToolType]
    public static class AnimatorTools
    {
        private static void Step(List<ProbeStep> log, string name, Func<string> action)
        {
            try
            {
                var detail = action();
                log.Add(new ProbeStep { Step = name, Ok = true, Detail = detail });
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                log.Add(new ProbeStep { Step = name, Ok = false, Detail = inner.GetType().Name + ": " + inner.Message });
            }
        }

        [McpServerTool(ReadOnly = true), Description(
            "SONDE Animator (exploratoire) : teste si le déplacement d'objets en 4D est pilotable par code. " +
            "L'Animator n'a aucune API managée, cette sonde vérifie empiriquement ce qui est atteignable via " +
            "la couche interop : accès à LcOpState, à la racine des animations, énumération des animations " +
            "existantes, construction d'une image-clé de translation, et lecture du lien animation des tâches " +
            "TimeLiner. LECTURE SEULE par défaut, n'insère rien. " +
            "tryCreateAnimation=true tente en plus de créer un conteneur d'animation via la plomberie native " +
            "non documentée : PEUT FAIRE PLANTER NAVISWORKS, enregistrer le document avant.")]
        public static Task<AnimatorProbeResult> animator_probe(
            [Description("Tenter la création d'un conteneur d'animation (RISQUÉ, def false)")] bool tryCreateAnimation = false) =>
            UiThread.Run(() =>
            {
                var log = new List<ProbeStep>();
                var doc = Application.ActiveDocument;

                LcOpState state = null;
                LcAnSavedAnimationsElement root = null;
                FolderItem rootFolder = null;

                // 1. La porte d'entrée.
                Step(log, "LcOpState.GetActiveInstance()", () =>
                {
                    state = LcOpState.GetActiveInstance();
                    return state == null ? "NULL" : "obtenu (" + state.GetType().Name + ")";
                });

                // 2. La racine des animations.
                if (state != null)
                    Step(log, "LcAnSavedAnimationsElement.Get(state)", () =>
                    {
                        root = LcAnSavedAnimationsElement.Get(state);
                        // IsInitialised est STATIQUE (fonction d'enregistrement de classe SWIG),
                        // pas un état d'instance.
                        return root == null ? "NULL" : "obtenu, classe initialisée=" + LcAnSavedAnimationsElement.IsInitialised();
                    });

                // 3. Le dossier racine + énumération de l'existant.
                if (root != null)
                {
                    Step(log, "root.Get() -> FolderItem", () =>
                    {
                        rootFolder = root.Get();
                        return rootFolder == null ? "NULL" : "'" + rootFolder.DisplayName + "', enfants=" + rootFolder.Children.Count();
                    });

                    Step(log, "root.GetNum()", () => root.GetNum().ToString(CultureInfo.InvariantCulture));
                }

                // 4. Inventaire des animations présentes (nom, type, référence utilisable par TimeLiner).
                var found = new List<AnimationInfo>();
                if (root != null && rootFolder != null)
                {
                    Step(log, "Énumération des animations", () =>
                    {
                        void Walk(SavedItem item, string path)
                        {
                            var p = string.IsNullOrEmpty(path) ? item.DisplayName : path + " / " + item.DisplayName;
                            string reference = null;
                            try
                            {
                                var r = new SavedItemReference();
                                root.MakePath(item, r);
                                reference = r.IsNull ? null : r.DocumentPartId + " | " + r.SavedItemId;
                            }
                            catch (Exception ex) { reference = "MakePath a échoué: " + ex.Message; }

                            found.Add(new AnimationInfo
                            {
                                Path = p,
                                TypeName = item.GetType().Name,
                                IsGroup = item.IsGroup,
                                Reference = reference
                            });

                            if (item is GroupItem g)
                                foreach (var c in g.Children) Walk(c, p);
                        }
                        foreach (var child in rootFolder.Children) Walk(child, "");
                        return found.Count + " élément(s)";
                    });
                }

                // 5. L'image-clé de translation : ctor public, donc censé marcher.
                Step(log, "new LcAnSelectionKeyFrame() + SetTranslation/SetTime", () =>
                {
                    // NB: Initialise() est STATIQUE (enregistrement de classe), on ne l'appelle pas ici.
                    var kf = new LcAnSelectionKeyFrame();
                    kf.SetTranslation(new Vector3D(0, 0, -70.33));
                    kf.SetTime(10.0);
                    kf.SetInterpolate(true);
                    var back = kf.GetTranslation();
                    return "OK, relecture translation Z=" + back.Z.ToString("F3", CultureInfo.InvariantCulture) +
                           ", temps=" + kf.GetTime().ToString("F1", CultureInfo.InvariantCulture);
                });

                // 6. Le lien côté TimeLiner : que vaut AnimationPath aujourd'hui ?
                Step(log, "Lecture AnimationPath des tâches TimeLiner", () =>
                {
                    var tl = TimelinerDocumentExtensions.GetTimeliner(doc);
                    int withAnim = 0, total = 0;
                    foreach (var it in tl.Tasks)
                    {
                        if (!(it is TimelinerTask t)) continue;
                        total++;
                        if (t.AnimationPath != null && !t.AnimationPath.IsNull) withAnim++;
                    }
                    return total + " tâche(s), dont " + withAnim + " avec une animation liée";
                });

                // 7. Tentative de création (opt-in, non documentée).
                if (tryCreateAnimation)
                {
                    Step(log, "RISQUÉ: ctor non public de LcAnAnimation par réflexion", () =>
                    {
                        var ctor = typeof(LcAnAnimation).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
                            .FirstOrDefault();
                        if (ctor == null) return "aucun constructeur trouvé";
                        var ps = ctor.GetParameters();
                        return "ctor présent, signature: (" +
                               string.Join(", ", ps.Select(p => p.ParameterType.Name)) +
                               ") — non invoqué : exige un NativeHandleInit valide, qui exige un pointeur natif " +
                               "produit par InternalFactory. Voir l'étape suivante.";
                    });

                    Step(log, "RISQUÉ: LcAnAnimation.InternalFactory", () =>
                    {
                        var mi = typeof(LcAnAnimation).GetMethod("InternalFactory",
                            BindingFlags.Public | BindingFlags.Static);
                        if (mi == null) return "InternalFactory introuvable";
                        var ps = mi.GetParameters();
                        return "présent: (" + string.Join(", ", ps.Select(p => p.ParameterType.Name)) +
                               ") -> " + mi.ReturnType.Name +
                               " — NON INVOQUÉ par cette sonde (allocation native sans propriétaire = risque de " +
                               "corruption/plantage). À tenter seulement en connaissance de cause.";
                    });
                }

                return new AnimatorProbeResult
                {
                    Steps = log.ToArray(),
                    Animations = found.ToArray(),
                    AnimationCount = found.Count,
                    CreationAttempted = tryCreateAnimation
                };
            });

        /// <summary>Cherche un jeu de sélection par nom, partout dans l'arbre des Sets.</summary>
        private static SelectionSet FindSet(Document doc, string setName)
        {
            SelectionSet found = null;
            void Walk(SavedItem item)
            {
                if (found != null) return;
                if (item is FolderItem f) { foreach (var c in f.Children) Walk(c); }
                else if (item is SelectionSet s && s.DisplayName == setName) found = s;
            }
            foreach (var child in doc.SelectionSets.RootItem.Children) Walk(child);
            return found;
        }

        [McpServerTool, Description(
            "Crée une animation de TRANSLATION générique sur un jeu de sélection : de fromOffsetZ à " +
            "toOffsetZ, en steps images-clés. Les décalages sont RELATIFS à la position modélisée de " +
            "l'objet, donc 0 = là où il est dans le modèle. " +
            "Pour faire MONTER une cabine modélisée en haut, partir d'un fromOffsetZ négatif et finir à 0. " +
            "Pour faire descendre, partir de 0 vers un toOffsetZ négatif. " +
            "L'animation obtenue s'attache aux tâches avec timeliner_set_task_animation. " +
            "PRÉREQUIS : une scène doit avoir déjà existé dans l'Animator du document (racine créée " +
            "paresseusement) : vérifier avec animator_probe.")]
        public static Task<BuildDescentResult> animator_build_translation(
            [Description("Jeu de sélection à animer")] string setName,
            [Description("Nom de la scène à créer (remplace une homonyme)")] string sceneName,
            [Description("Nom du jeu d'animation (vide = nom du jeu de sélection)")] string animName = "",
            [Description("Décalage Z de départ, relatif à la position modélisée (def 0)")] double fromOffsetZ = 0,
            [Description("Décalage Z d'arrivée, relatif à la position modélisée (def 0)")] double toOffsetZ = 0,
            [Description("Durée totale en secondes (def 60)")] double durationSeconds = 60,
            [Description("Nombre d'images-clés de l'aller, minimum 2 (def 2 = translation linéaire)")] int steps = 2,
            [Description("true = ALLER-RETOUR : va jusqu'à toOffsetZ puis revient à fromOffsetZ (def false)")] bool returnToStart = false) =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                if (steps < 2) steps = 2;

                var state = LcOpState.GetActiveInstance();
                if (state == null) return new BuildDescentResult { Error = "LcOpState.GetActiveInstance() a renvoyé null." };
                var root = LcAnSavedAnimationsElement.Get(state);
                if (root == null)
                    return new BuildDescentResult { Error = "Racine des animations NULLE : créer une scène une fois dans l'Animator (Add Scene) puis relancer." };
                var rootFolder = root.Get();
                if (rootFolder == null) return new BuildDescentResult { Error = "root.Get() a renvoyé null." };

                var set = FindSet(doc, setName);
                if (set == null) return new BuildDescentResult { Error = "Jeu de sélection introuvable: '" + setName + "'" };
                var items = new ModelItemCollection();
                items.AddRange(set.GetSelectedItems());
                if (items.Count == 0) return new BuildDescentResult { Error = "Le jeu '" + setName + "' est vide." };

                for (int i = rootFolder.Children.Count() - 1; i >= 0; i--)
                    if (rootFolder.Children.ElementAt(i).DisplayName == sceneName)
                        try { LcAnSavedAnimationsElement.DeleteItem(state, rootFolder, i); } catch { }

                var sceneInDoc = LcAnSavedAnimationsElement.InsertItemCopy(state, rootFolder, rootFolder.Children.Count(),
                    new LcAnScene { DisplayName = sceneName }) as GroupItem;
                if (sceneInDoc == null) return new BuildDescentResult { Error = "Création de la scène refusée." };

                var anim = new LcAnSelectionAnim { DisplayName = string.IsNullOrWhiteSpace(animName) ? setName : animName };
                anim.SetSelection(new Selection(items));
                var animInDoc = LcAnSavedAnimationsElement.InsertItemCopy(state, sceneInDoc, 0, anim) as GroupItem;
                if (animInDoc == null) return new BuildDescentResult { Error = "Création du jeu d'animation refusée." };
                try { (animInDoc as LcAnSelectionAnim)?.PropogateImplicitCenter(state); } catch { }

                // Les décalages à parcourir : l'aller, puis le retour en miroir si demandé.
                var waypoints = new List<double>();
                for (int i = 0; i < steps; i++)
                    waypoints.Add(fromOffsetZ + (toOffsetZ - fromOffsetZ) * ((double)i / (steps - 1)));
                if (returnToStart)
                    for (int i = steps - 2; i >= 0; i--) waypoints.Add(waypoints[i]);

                var made = new List<DescentKeyFrame>();
                for (int i = 0; i < waypoints.Count; i++)
                {
                    double t = durationSeconds * ((double)i / (waypoints.Count - 1));
                    double z = waypoints[i];

                    string kindEarly;
                    if (i == 0) kindEarly = "depart";
                    else if (i == waypoints.Count - 1) kindEarly = returnToStart ? "retour" : "arrivee";
                    else if (returnToStart && i == steps - 1) kindEarly = "sommet";
                    else kindEarly = "intermediaire";

                    var kf = new LcAnSelectionKeyFrame();
                    // Nommer les images-clés : sans nom, l'arbre de l'Animator est illisible dès qu'on
                    // veut y travailler à la main.
                    kf.DisplayName = string.Format(CultureInfo.InvariantCulture,
                        "{0:00} - {1:F1}s - Z {2:F2} - {3}", i + 1, t, z, kindEarly);
                    kf.SetTime(Math.Round(t, 3));
                    kf.SetTranslation(new Vector3D(0, 0, Math.Round(z, 4)));
                    kf.SetInterpolate(true);
                    LcAnSavedAnimationsElement.InsertItemCopy(state, animInDoc, i, kf);

                    string kind;
                    if (i == 0) kind = "depart";
                    else if (i == waypoints.Count - 1) kind = returnToStart ? "retour" : "arrivee";
                    else if (returnToStart && i == steps - 1) kind = "sommet";
                    else kind = "intermediaire";

                    made.Add(new DescentKeyFrame { Time = Math.Round(t, 3), OffsetZ = Math.Round(z, 4), Level = null, Kind = kind });
                }

                string reference = null, partId = null, itemId = null;
                try
                {
                    var r = new SavedItemReference();
                    root.MakePath(sceneInDoc, r);
                    if (!r.IsNull) { partId = r.DocumentPartId; itemId = r.SavedItemId; reference = partId + " | " + itemId; }
                }
                catch (Exception ex) { reference = "MakePath a échoué: " + ex.Message; }

                return new BuildDescentResult
                {
                    SceneName = sceneName,
                    AnimationName = anim.DisplayName,
                    TargetSet = setName,
                    TargetElements = items.Count,
                    FromLevel = fromOffsetZ.ToString("F3", CultureInfo.InvariantCulture),
                    ToLevel = toOffsetZ.ToString("F3", CultureInfo.InvariantCulture),
                    TotalDropFeet = Math.Round(toOffsetZ - fromOffsetZ, 3),
                    DurationSeconds = durationSeconds,
                    Stepwise = false,
                    KeyFramesCreated = made.Count,
                    KeyFrames = made.ToArray(),
                    SceneReference = reference,
                    DocumentPartId = partId,
                    SavedItemId = itemId
                };
            });

        [McpServerTool, Description(
            "Crée une animation de CAMÉRA qui GLISSE d'un point de vue vers un autre, s'y maintient, puis " +
            "revient. Le mouvement se joue PENDANT la simulation et il est INTERPOLÉ, donc c'est un " +
            "glissement continu et non un basculement sec de vue. " +
            "Les deux cadrages sont des POINTS DE VUE ENREGISTRÉS : l'outil n'invente aucun angle, c'est " +
            "l'utilisateur qui décide en enregistrant ses vues. " +
            "À attacher à une tâche couvrant la fenêtre à mettre en valeur, DISTINCTE de celle qui porte " +
            "l'animation d'objet : les deux jouent en parallèle. " +
            "IMPORTANT : lancer la lecture depuis le point de vue de DÉPART, sinon la caméra saute d'un coup " +
            "au début de la tâche au lieu de glisser. " +
            "NB : les points de vue mémorisent les plans de coupe ; le cadrage choisi impose donc aussi " +
            "l'état de la boîte de section.")]
        public static Task<CameraZoomResult> animator_build_camera_zoom(
            [Description("Point de vue enregistré vers lequel GLISSER (la vue serrée)")] string toViewpoint,
            [Description("Nom de la scène à créer (remplace une homonyme)")] string sceneName,
            [Description("Nom du jeu d'animation (vide = 'Camera')")] string animName = "",
            [Description("Point de vue de DÉPART et de RETOUR (vide = la vue courante au moment de l'appel)")] string fromViewpoint = "",
            [Description("Durée totale en secondes (def 60)")] double durationSeconds = 60,
            [Description("Part du temps du glissement aller, 0.05 à 0.45 (def 0.2)")] double moveInFraction = 0.2,
            [Description("Part du temps maintenue sur la vue serrée, 0.1 à 0.8 (def 0.6)")] double holdFraction = 0.6,
            [Description("true = revient à la vue de départ à la fin ; false = ALLER SIMPLE, la caméra RESTE sur la vue d'arrivée (pour enchaîner une autre scène). Def true")] bool returnToStart = true) =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                if (moveInFraction < 0.05) moveInFraction = 0.05;
                if (moveInFraction > 0.45) moveInFraction = 0.45;
                if (holdFraction < 0.1) holdFraction = 0.1;
                if (holdFraction > 0.8) holdFraction = 0.8;
                if (moveInFraction + holdFraction > 0.95) holdFraction = 0.95 - moveInFraction;

                var state = LcOpState.GetActiveInstance();
                if (state == null) return new CameraZoomResult { Error = "LcOpState.GetActiveInstance() a renvoyé null." };
                var root = LcAnSavedAnimationsElement.Get(state);
                if (root == null)
                    return new CameraZoomResult { Error = "Racine des animations NULLE : créer une scène une fois dans l'Animator puis relancer." };
                var rootFolder = root.Get();
                if (rootFolder == null) return new CameraZoomResult { Error = "root.Get() a renvoyé null." };

                Viewpoint Resolve(string name)
                {
                    SavedViewpoint hit = null;
                    void Walk(SavedItem item)
                    {
                        if (hit != null) return;
                        if (item is SavedViewpoint s && s.DisplayName == name) { hit = s; return; }
                        if (item is GroupItem g) foreach (var c in g.Children) Walk(c);
                    }
                    foreach (var child in doc.SavedViewpoints.RootItem.Children) Walk(child);
                    return hit?.Viewpoint;
                }

                var toVp = Resolve(toViewpoint);
                if (toVp == null) return new CameraZoomResult { Error = "Point de vue introuvable: '" + toViewpoint + "'" };

                Viewpoint fromVp;
                string fromLabel;
                if (string.IsNullOrWhiteSpace(fromViewpoint))
                {
                    fromVp = doc.CurrentViewpoint.ToViewpoint();
                    fromLabel = "(vue courante au moment de la construction)";
                }
                else
                {
                    fromVp = Resolve(fromViewpoint);
                    if (fromVp == null) return new CameraZoomResult { Error = "Point de vue de départ introuvable: '" + fromViewpoint + "'" };
                    fromLabel = fromViewpoint;
                }

                // DEUX PIÈGES, tous deux constatés en exécution :
                // 1) `Viewpoint.InternalViewpoint.GetCamera()` renvoie une caméra PARTAGÉE, pas un
                //    instantané : deux lectures donnent le MÊME objet, donc des images-clés identiques
                //    et aucun mouvement. Il faut recopier les valeurs dans un LcNvCamera neuf.
                // 2) En projection ORTHOGRAPHIQUE, déplacer la caméra ne change RIEN au grossissement :
                //    seule l'ÉTENDUE de vue (HeightField) le gouverne. D'où sa copie explicite.
                LcNvCamera CamOf(Viewpoint v)
                {
                    var c = new LcNvCamera();
                    c.SetProjection(v.Projection);
                    c.SetPosition(v.Position);
                    c.SetRotation(v.Rotation);
                    c.SetHeightField(v.HeightField);
                    return c;
                }

                var camWide = CamOf(fromVp);
                var camClose = CamOf(toVp);
                string homeApplied = fromLabel;

                for (int i = rootFolder.Children.Count() - 1; i >= 0; i--)
                    if (rootFolder.Children.ElementAt(i).DisplayName == sceneName)
                        try { LcAnSavedAnimationsElement.DeleteItem(state, rootFolder, i); } catch { }

                var sceneInDoc = LcAnSavedAnimationsElement.InsertItemCopy(state, rootFolder, rootFolder.Children.Count(),
                    new LcAnScene { DisplayName = sceneName }) as GroupItem;
                if (sceneInDoc == null) return new CameraZoomResult { Error = "Création de la scène refusée." };

                var anim = new LcAnCameraAnim { DisplayName = string.IsNullOrWhiteSpace(animName) ? "Camera" : animName };
                var animInDoc = LcAnSavedAnimationsElement.InsertItemCopy(state, sceneInDoc, 0, anim) as GroupItem;
                if (animInDoc == null) return new CameraZoomResult { Error = "Création de l'animation de caméra refusée." };

                double tIn = durationSeconds * moveInFraction;
                double tHold = durationSeconds * (moveInFraction + holdFraction);
                var plan = returnToStart
                    ? new[]
                    {
                        new { T = 0.0,             C = camWide,  Label = "depart (vue large)" },
                        new { T = tIn,             C = camClose, Label = "arrivee sur la vue serree" },
                        new { T = tHold,           C = camClose, Label = "fin du maintien" },
                        new { T = durationSeconds, C = camWide,  Label = "retour a la vue large" }
                    }
                    // Aller simple : on tient la vue d'arrivée jusqu'au bout, pour que la scène suivante
                    // démarre exactement là où celle-ci s'arrête. C'est ce qui permet d'enchaîner.
                    : new[]
                    {
                        new { T = 0.0,             C = camWide,  Label = "depart" },
                        new { T = tIn,             C = camClose, Label = "arrivee (maintenue jusqu'a la fin)" },
                        new { T = durationSeconds, C = camClose, Label = "fin, camera restee sur la vue d'arrivee" }
                    };

                var made = new List<CameraKeyFrameInfo>();
                for (int i = 0; i < plan.Length; i++)
                {
                    var kf = new LcAnCameraKeyFrame();
                    // Nommer l'image-clé : un arbre d'Animator sans libellés est inexploitable à la main.
                    kf.DisplayName = string.Format(CultureInfo.InvariantCulture,
                        "{0:00} - {1:F1}s - {2}", i + 1, plan[i].T, plan[i].Label);
                    kf.SetCamera(plan[i].C);
                    kf.SetTime(Math.Round(plan[i].T, 3));
                    kf.SetInterpolate(true);
                    LcAnSavedAnimationsElement.InsertItemCopy(state, animInDoc, i, kf);

                    var p = plan[i].C.GetPosition();
                    made.Add(new CameraKeyFrameInfo
                    {
                        Time = Math.Round(plan[i].T, 3),
                        Position = string.Format(CultureInfo.InvariantCulture, "{0:F1}, {1:F1}, {2:F1}", p.X, p.Y, p.Z),
                        HeightField = Math.Round(plan[i].C.GetHeightField(), 2),
                        Label = plan[i].Label
                    });
                }

                string partId = null, itemId = null;
                try
                {
                    var r = new SavedItemReference();
                    root.MakePath(sceneInDoc, r);
                    if (!r.IsNull) { partId = r.DocumentPartId; itemId = r.SavedItemId; }
                }
                catch { }

                return new CameraZoomResult
                {
                    SceneName = sceneName,
                    AnimationName = anim.DisplayName,
                    TargetSet = toViewpoint,
                    DurationSeconds = durationSeconds,
                    Projection = toVp.Projection.ToString(),
                    HomeViewpoint = homeApplied,
                    KeyFrames = made.ToArray(),
                    DocumentPartId = partId,
                    SavedItemId = itemId
                };
            });

        [McpServerTool(ReadOnly = true), Description(
            "Inspecte en profondeur un jeu d'animation de l'Animator et compte ses images-clés par DEUX " +
            "voies indépendantes : l'énumération des enfants du nœud, et l'accesseur dédié " +
            "GetOrderedKeyFrames. Sert à lever un doute quand un jeu paraît vide : si les deux comptes " +
            "diffèrent, c'est l'énumération par les enfants qui est aveugle, pas le jeu qui est vide. " +
            "Donne aussi la durée calculée et, pour une animation de coupe, le détail des plans de chaque " +
            "image-clé.")]
        public static Task<InspectAnimResult> animator_inspect_animation(
            [Description("Nom de la scène")] string sceneName,
            [Description("Nom du jeu d'animation dans cette scène")] string animName) =>
            UiThread.Run(() =>
            {
                var state = LcOpState.GetActiveInstance();
                if (state == null) return new InspectAnimResult { Error = "LcOpState.GetActiveInstance() a renvoyé null." };
                var root = LcAnSavedAnimationsElement.Get(state);
                if (root == null) return new InspectAnimResult { Error = "Racine des animations nulle." };
                var rootFolder = root.Get();

                GroupItem scene = null;
                for (int i = 0; i < rootFolder.Children.Count(); i++)
                    if (rootFolder.Children.ElementAt(i).DisplayName == sceneName &&
                        rootFolder.Children.ElementAt(i) is GroupItem g) { scene = g; break; }
                if (scene == null) return new InspectAnimResult { Error = "Scène introuvable: '" + sceneName + "'" };

                GroupItem anim = null;
                for (int i = 0; i < scene.Children.Count(); i++)
                    if (scene.Children.ElementAt(i).DisplayName == animName &&
                        scene.Children.ElementAt(i) is GroupItem g2) { anim = g2; break; }
                if (anim == null) return new InspectAnimResult { Error = "Jeu introuvable: '" + animName + "'" };

                var res = new InspectAnimResult { SceneName = sceneName, AnimName = animName, TypeName = anim.GetType().Name };
                res.ChildrenCount = anim.Children.Count();

                // Voie 2 : l'accesseur dédié, qui peut voir ce que l'énumération des enfants ignore.
                var rows = new List<KeyFrameRow>();
                try
                {
                    var la = anim as LcAnAnimation;
                    if (la == null) { res.OrderedCount = -1; res.Note = "Le jeu n'est pas un LcAnAnimation."; }
                    else
                    {
                        var vec = new LcAnKeyFrameVec();
                        la.GetOrderedKeyFrames(vec);
                        res.OrderedCount = vec.Size();
                        for (int i = 0; i < vec.Size(); i++)
                        {
                            var kf = vec.ItemAt(i);
                            var row = new KeyFrameRow { Index = i + 1, TypeName = kf.GetType().Name, Time = Math.Round(kf.GetTime(), 3) };
                            if (kf is LcAnClipKeyFrame ck)
                            {
                                try
                                {
                                    var pl = ck.GetPlanes();
                                    row.Planes = pl == null ? 0 : pl.Size();
                                    if (pl != null && pl.Size() > 0)
                                        row.FirstPlaneDistances = string.Join(" | ",
                                            Enumerable.Range(0, pl.Size()).Select(k =>
                                                pl.Get(k).Distance.ToString("F2", CultureInfo.InvariantCulture)));
                                }
                                catch (Exception ex) { row.Note = "GetPlanes: " + ex.Message; }
                            }
                            rows.Add(row);
                        }
                        try { res.Duration = Math.Round(la.CalcDuration(false), 3); } catch { }
                    }
                }
                catch (Exception ex) { res.Error = "GetOrderedKeyFrames: " + ex.Message; }

                res.KeyFrames = rows.ToArray();
                res.Verdict = res.OrderedCount > 0
                    ? (res.ChildrenCount == res.OrderedCount ? "coherent, images-cles presentes"
                                                             : "IMAGES-CLES PRESENTES mais invisibles a l'enumeration des enfants")
                    : "reellement aucune image-cle";
                return res;
            });

        [McpServerTool, Description(
            "Crée un JEU DE PLANS DE COUPE vide (section plane set) dans une scène de l'Animator. " +
            "C'est le conteneur que la procédure Autodesk demande de sélectionner AVANT de capturer des " +
            "images-clés de section : Animation > Animator, choisir le jeu dans l'arbre, Viewpoint > Enable " +
            "Sectioning, positionner, puis Capture Keyframe. " +
            "Le conteneur est créable par code, contrairement à ses images-clés, d'où ce découpage : " +
            "l'outil pose le jeu, l'utilisateur capture UNE image-clé, puis " +
            "animator_build_section_sweep_from_keyframe génère toutes les autres.")]
        public static Task<CreateSectionSetResult> animator_create_section_set(
            [Description("Nom de la scène qui accueille le jeu (créée si absente)")] string sceneName,
            [Description("Nom du jeu de plans de coupe (def 'Coupe')")] string animName = "Coupe") =>
            UiThread.Run(() =>
            {
                var state = LcOpState.GetActiveInstance();
                if (state == null) return new CreateSectionSetResult { Error = "LcOpState.GetActiveInstance() a renvoyé null." };
                var root = LcAnSavedAnimationsElement.Get(state);
                if (root == null) return new CreateSectionSetResult { Error = "Racine des animations nulle : créer une scène une fois dans l'Animator." };
                var rootFolder = root.Get();
                if (rootFolder == null) return new CreateSectionSetResult { Error = "root.Get() a renvoyé null." };

                GroupItem scene = null;
                for (int i = 0; i < rootFolder.Children.Count(); i++)
                    if (rootFolder.Children.ElementAt(i) is LcAnScene s && s.DisplayName == sceneName) { scene = s; break; }
                bool created = false;
                if (scene == null)
                {
                    scene = LcAnSavedAnimationsElement.InsertItemCopy(state, rootFolder, rootFolder.Children.Count(),
                        new LcAnScene { DisplayName = sceneName }) as GroupItem;
                    created = true;
                }
                if (scene == null) return new CreateSectionSetResult { Error = "Scène introuvable et non créée." };

                for (int i = scene.Children.Count() - 1; i >= 0; i--)
                    if (scene.Children.ElementAt(i).DisplayName == animName)
                        try { LcAnSavedAnimationsElement.DeleteItem(state, scene, i); } catch { }

                var clip = LcAnSavedAnimationsElement.InsertItemCopy(state, scene, scene.Children.Count(),
                    new LcAnClipAnim { DisplayName = animName }) as GroupItem;
                if (clip == null) return new CreateSectionSetResult { Error = "Création du jeu de plans de coupe refusée." };

                return new CreateSectionSetResult
                {
                    SceneName = sceneName,
                    SceneCreated = created,
                    AnimName = animName,
                    TypeCreated = clip.GetType().Name,
                    NextStep = "Dans l'Animator, sélectionner '" + sceneName + " / " + animName +
                               "', activer Enable Sectioning, positionner la coupe, puis Capture Keyframe UNE fois."
                };
            });

        [McpServerTool, Description(
            "Génère un BALAYAGE de coupe à partir d'UNE image-clé capturée à la main. " +
            "Prend la première image-clé du jeu de plans de coupe indiqué comme gabarit, la duplique autant " +
            "de fois que demandé, et déplace un plan le long de sa normale à chaque copie. " +
            "Contourne l'impossibilité de créer une image-clé de coupe par code : on en clone une existante. " +
            "Prérequis : avoir capturé au moins une image-clé dans ce jeu (voir animator_create_section_set).")]
        public static Task<SweepFromKeyframeResult> animator_build_section_sweep_from_keyframe(
            [Description("Nom de la scène contenant le jeu de coupe")] string sceneName,
            [Description("Nom du jeu de plans de coupe")] string animName,
            [Description("Index du plan à déplacer, 1 = premier")] int planeIndex,
            [Description("Décalage de départ le long de la normale")] double fromOffset,
            [Description("Décalage d'arrivée, 0 = position capturée (def 0)")] double toOffset = 0,
            [Description("Nombre d'images-clés (def 12)")] int steps = 12,
            [Description("Durée totale de l'animation en secondes (def 30)")] double durationSeconds = 30) =>
            UiThread.Run(() =>
            {
                var state = LcOpState.GetActiveInstance();
                if (state == null) return new SweepFromKeyframeResult { Error = "LcOpState.GetActiveInstance() a renvoyé null." };
                var root = LcAnSavedAnimationsElement.Get(state);
                if (root == null) return new SweepFromKeyframeResult { Error = "Racine des animations nulle." };
                var rootFolder = root.Get();
                if (steps < 2) steps = 2;

                GroupItem scene = null;
                for (int i = 0; i < rootFolder.Children.Count(); i++)
                    if (rootFolder.Children.ElementAt(i) is LcAnScene s && s.DisplayName == sceneName) { scene = s; break; }
                if (scene == null) return new SweepFromKeyframeResult { Error = "Scène introuvable: '" + sceneName + "'" };

                GroupItem clip = null;
                for (int i = 0; i < scene.Children.Count(); i++)
                    if (scene.Children.ElementAt(i) is GroupItem g && g.DisplayName == animName) { clip = g; break; }
                if (clip == null) return new SweepFromKeyframeResult { Error = "Jeu de coupe introuvable: '" + animName + "'" };

                var template = clip.Children.OfType<LcAnClipKeyFrame>().FirstOrDefault();
                if (template == null)
                    return new SweepFromKeyframeResult
                    {
                        Error = "Aucune image-clé dans '" + animName + "'. Il faut en capturer UNE à la main : " +
                                "sélectionner le jeu dans l'Animator, Enable Sectioning, positionner, Capture Keyframe."
                    };

                var refPlanes = template.GetPlanes();
                if (refPlanes == null || refPlanes.Size() == 0)
                    return new SweepFromKeyframeResult { Error = "L'image-clé gabarit ne porte aucun plan." };
                if (planeIndex < 1 || planeIndex > refPlanes.Size())
                    return new SweepFromKeyframeResult { Error = "planeIndex hors bornes : 1 à " + refPlanes.Size() };

                var n0 = refPlanes.Get(planeIndex - 1).Normal;
                double d0 = refPlanes.Get(planeIndex - 1).Distance;

                // On repart d'une base propre : on retire tout sauf le gabarit, qu'on relit ensuite.
                for (int i = clip.Children.Count() - 1; i >= 1; i--)
                    try { LcAnSavedAnimationsElement.DeleteItem(state, clip, i); } catch { }

                var made = new List<SweepStep2>();
                for (int k = 0; k < steps; k++)
                {
                    double t = (double)k / (steps - 1);
                    double off = fromOffset + (toOffset - fromOffset) * t;

                    // CLONAGE : le ctor de LcAnClipKeyFrame est ferme, on duplique donc le gabarit.
                    var copy = template.CreateCopy() as LcAnClipKeyFrame;
                    if (copy == null) { made.Add(new SweepStep2 { Index = k + 1, Error = "CreateCopy a echoue" }); continue; }

                    var cp = new ClipPlaneSet();
                    cp.CopyFrom(refPlanes);
                    cp.Translate(planeIndex - 1, new Vector3D(n0.X * off, n0.Y * off, n0.Z * off));
                    copy.SetPlanes(cp);
                    copy.SetTime(Math.Round(durationSeconds * t, 3));
                    copy.SetInterpolate(true);

                    LcAnSavedAnimationsElement.InsertItemCopy(state, clip, clip.Children.Count(), copy);
                    made.Add(new SweepStep2
                    {
                        Index = k + 1,
                        Time = Math.Round(durationSeconds * t, 3),
                        Offset = Math.Round(off, 3),
                        Distance = Math.Round(cp.Get(planeIndex - 1).Distance, 3)
                    });
                }

                // Le gabarit d'origine reste en tete : on le retire pour ne garder que la serie generee.
                try { LcAnSavedAnimationsElement.DeleteItem(state, clip, 0); } catch { }

                string partId = null, itemId = null;
                try
                {
                    var r = new SavedItemReference();
                    root.MakePath(scene, r);
                    if (!r.IsNull) { partId = r.DocumentPartId; itemId = r.SavedItemId; }
                }
                catch { }

                return new SweepFromKeyframeResult
                {
                    SceneName = sceneName,
                    AnimName = animName,
                    PlaneIndex = planeIndex,
                    PlaneNormal = string.Format(CultureInfo.InvariantCulture, "{0:F3}, {1:F3}, {2:F3}", n0.X, n0.Y, n0.Z),
                    TemplateDistance = Math.Round(d0, 3),
                    KeyFramesCreated = made.Count,
                    DurationSeconds = durationSeconds,
                    Steps = made.ToArray(),
                    DocumentPartId = partId,
                    SavedItemId = itemId == null ? null : itemId + "\n" + animName
                };
            });

        // RETIRÉ DE LA SURFACE MCP le 2026-08-06 : l'attribut [McpServerTool] est ôté pour que cet
        // outil ne puisse plus être appelé. Il n'a JAMAIS été exécuté et ne doit pas l'être : la
        // lecture du code compilé prouve que cette voie ne peut pas fonctionner. Détail sous la
        // méthode et dans Skills/15_coupe_animee_verdict.md.
        [Description(
            "RETIRÉ, NE PAS EXPOSER. Créer une image-clé de coupe par code est prouvé impossible. " +
            "Ancienne description : SONDE RISQUÉE, étape 1 sur 2. Tente de construire un LcAnClipKeyFrame (image-clé de plan de " +
            "coupe) malgré l'absence de constructeur public, en passant par la plomberie SWIG " +
            "InternalFactory puis le constructeur non public via réflexion. " +
            "N'INSÈRE RIEN dans le document : on vérifie seulement si l'objet peut être créé et manipulé. " +
            "⚠️ Alloue un objet natif par une voie non documentée : PEUT FAIRE PLANTER NAVISWORKS. " +
            "ENREGISTRER LE DOCUMENT AVANT. Ne s'exécute que si confirmRisk=true.")]
        public static Task<ClipProbeResult> animator_probe_clip_keyframe(
            [Description("Doit valoir true pour exécuter. Confirme que le document est enregistré.")] bool confirmRisk = false) =>
            UiThread.Run(() =>
            {
                var res = new ClipProbeResult();
                if (!confirmRisk)
                    return new ClipProbeResult { Error = "Refusé : confirmRisk=false. Enregistrer le document puis rappeler avec confirmRisk=true." };

                var t = typeof(LcAnClipKeyFrame);
                var steps = new List<string>();

                try
                {
                    var factory = t.GetMethod("InternalFactory", BindingFlags.Public | BindingFlags.Static);
                    if (factory == null) { res.Error = "InternalFactory introuvable."; return res; }
                    var initType = factory.GetParameters()[0].ParameterType.GetElementType();
                    steps.Add("InternalFactory trouvé, paramètre de type " + initType.Name);

                    var args = new object[] { Activator.CreateInstance(initType) };
                    var handle = factory.Invoke(null, args);
                    steps.Add("InternalFactory invoqué, retour = " + (handle == null ? "null" : handle.GetType().Name));

                    var ctor = t.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).FirstOrDefault();
                    if (ctor == null) { res.Error = "Constructeur non public introuvable."; res.Steps = steps.ToArray(); return res; }
                    steps.Add("Constructeur non public trouvé");

                    var kf = ctor.Invoke(new object[] { args[0] }) as LcAnClipKeyFrame;
                    if (kf == null) { res.Error = "Construction retournée nulle."; res.Steps = steps.ToArray(); return res; }
                    steps.Add("OBJET CONSTRUIT : " + kf.GetType().Name);

                    // On vérifie qu'il est réellement exploitable : lecture, écriture, relecture.
                    try
                    {
                        var got = kf.GetPlanes();
                        steps.Add("GetPlanes() -> " + (got == null ? "null" : got.Size() + " plans"));
                    }
                    catch (Exception ex) { steps.Add("GetPlanes() a levé : " + ex.Message); }

                    try
                    {
                        var set = new ClipPlaneSet();
                        kf.SetPlanes(set);
                        steps.Add("SetPlanes() accepté");
                        var back = kf.GetPlanes();
                        steps.Add("relecture -> " + (back == null ? "null" : back.Size() + " plans"));
                        res.Usable = true;
                    }
                    catch (Exception ex) { steps.Add("SetPlanes() a levé : " + ex.Message); }

                    try { kf.SetTime(1.5); steps.Add("SetTime(1.5) accepté, relu = " + kf.GetTime()); }
                    catch (Exception ex) { steps.Add("SetTime a levé : " + ex.Message); }

                    res.Constructed = true;
                }
                catch (Exception ex)
                {
                    var inner = ex.InnerException ?? ex;
                    res.Error = inner.GetType().Name + " : " + inner.Message;
                }
                res.Steps = steps.ToArray();
                return res;
            });

        /// <summary>
        /// Toutes les bibliothèques Navisworks chargées, pas seulement celle de l'Animator.
        ///
        /// PIÈGE (2026-08-06) : navisworks.animator.clr.dll déclare des structures VIDES, sans espace
        /// de noms, portant le nom des types du noyau (LcOpSectionKeyFrame, LcOaClipPlaneSet…). Ce ne
        /// sont que des marqueurs servant à nommer des pointeurs natifs. Les vraies classes sont dans
        /// d'AUTRES assemblies. Ne sonder que l'assembly de l'Animator fait donc conclure à tort qu'un
        /// type est une coquille vide.
        /// </summary>
        private static IEnumerable<Type> AllNavisTypes()
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var n = a.GetName().Name ?? "";
                if (n.IndexOf("navisworks", StringComparison.OrdinalIgnoreCase) < 0) continue;
                Type[] types;
                try { types = a.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
                catch { continue; }
                foreach (var t in types) yield return t;
            }
        }

        [McpServerTool(ReadOnly = true), Description(
            "Énumère les CONSTRUCTEURS et MÉTHODES d'un ou plusieurs types, en balayant TOUTES les " +
            "bibliothèques Navisworks chargées (API managée, interop, Animator, TimeLiner), avec leur " +
            "accessibilité et leur assembly d'origine. " +
            "Sert à découvrir une voie de création quand le constructeur direct n'est pas public. " +
            "Ne lit que les MÉTADONNÉES : n'instancie rien, n'appelle aucun code natif, aucun risque. " +
            "Plusieurs types séparés par ';'. Un nom contenant '*' déclenche une RECHERCHE de noms de types. " +
            "Le filtre ne garde que les membres dont le nom le contient.")]
        public static Task<ReflectTypesResult> animator_reflect_type(
            [Description("Noms de types (court ou complet), séparés par ';'. '*Truc' cherche les types contenant 'Truc'.")] string typeNames,
            [Description("Ne garder que les membres contenant ce texte (vide = tous)")] string filter = "",
            [Description("Inclure les membres hérités des classes de base (def false)")] bool includeInherited = false) =>
            UiThread.Run(() =>
            {
                var wanted = (typeNames ?? "").Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                if (wanted.Count == 0) return new ReflectTypesResult { Error = "typeNames est vide." };

                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                if (!includeInherited) flags |= BindingFlags.DeclaredOnly;

                string Vis(Type x) =>
                    x.IsPublic ? "public" : x.IsNestedPublic ? "nested-public" : "internal";
                string Asm(Type x) => x.Assembly.GetName().Name;
                string Acc(MethodBase m) =>
                    m.IsPublic ? "public" : m.IsFamily ? "protected" : m.IsAssembly ? "internal" : "private";
                // Le nom COMPLET du type de paramètre : c'est ce qui distingue la vraie classe du
                // marqueur homonyme sans espace de noms.
                string Sig(MethodBase m) => string.Join(", ",
                    m.GetParameters().Select(p =>
                    {
                        var pt = p.ParameterType;
                        var nm = (pt.FullName ?? pt.Name).TrimEnd('&');
                        return (pt.IsByRef ? "ref " : "") + nm + " " + p.Name;
                    }));

                var all = AllNavisTypes().ToList();
                var rows = new List<ReflectedType>();
                foreach (var name in wanted)
                {
                    if (name.Contains("*"))
                    {
                        var needle = name.Replace("*", "");
                        var hits = all
                            .Where(x => x.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                            .Select(x => Vis(x) + "  " + (x.FullName ?? x.Name) + "   [" + Asm(x) + "]")
                            .Distinct().OrderBy(x => x).ToArray();
                        rows.Add(new ReflectedType { TypeName = name, BaseType = "(recherche de noms)", MethodCount = hits.Length, Methods = hits });
                        continue;
                    }

                    // Plusieurs types portent le MÊME nom court dans des assemblies et des espaces de
                    // noms différents : chercher d'abord sur le nom complet, puis privilégier le type
                    // VISIBLE ET DOTÉ DE MEMBRES, sinon on lit un marqueur vide.
                    var cands = all.Where(x => x.FullName == name).ToList();
                    if (cands.Count == 0) cands = all.Where(x => x.Name == name).ToList();
                    var t = cands.OrderByDescending(x => (x.IsPublic || x.IsNestedPublic) ? 1 : 0)
                                 .ThenByDescending(x => x.GetMembers(flags).Length)
                                 .FirstOrDefault();
                    if (t == null) { rows.Add(new ReflectedType { TypeName = name, Error = "type introuvable dans les assemblies Navisworks chargées" }); continue; }

                    var ctors = t.GetConstructors(flags)
                        .Select(c => Acc(c) + " " + t.Name + "(" + Sig(c) + ")").ToArray();

                    var methods = t.GetMethods(flags)
                        .Where(m => !m.IsSpecialName)
                        .Where(m => string.IsNullOrWhiteSpace(filter) ||
                                    m.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                        .OrderBy(m => m.Name)
                        .Select(m => Acc(m) + (m.IsStatic ? " static " : " ") +
                                     m.ReturnType.Name + " " + m.Name + "(" + Sig(m) + ")")
                        .Distinct().ToArray();

                    rows.Add(new ReflectedType
                    {
                        TypeName = Vis(t) + "  " + (t.FullName ?? t.Name) + "   [" + Asm(t) + "]"
                                   + (cands.Count > 1 ? "  (" + cands.Count + " homonymes)" : ""),
                        BaseType = t.BaseType == null ? null : (t.BaseType.FullName ?? t.BaseType.Name),
                        Constructors = ctors,
                        MethodCount = methods.Length,
                        Methods = methods
                    });
                }
                var scanned = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetName().Name ?? "")
                    .Where(n => n.IndexOf("navisworks", StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(n => n).ToArray();
                return new ReflectTypesResult
                {
                    Assembly = scanned.Length + " assemblies : " + string.Join(", ", scanned),
                    Filter = filter,
                    Types = rows.ToArray()
                };
            });

        // ─────────────────────────────────────────────────────────────────────────────────────────
        // POURQUOI LA SONDE CI-DESSUS EST DÉFINITIVEMENT ABANDONNÉE (2026-08-06)
        //
        // Constructeur PUBLIC d'une image-clé sœur, LcAnCameraKeyFrame, lu en IL :
        //     LcAnCameraKeyFrame.new(0x100)          alloue 256 octets natifs
        //     LcAnCameraKeyFrame.{ctor}(ptr)         exécute le constructeur C++
        //     NativeHandle.InternalInit(ptr, ...)    rattache l'enveloppe gérée
        //
        // LcAnClipKeyFrame.InternalFactory, lu en IL :
        //     newobj LcAnClipKeyFrame..ctor(NativeHandleInit&)     ... et rien d'autre.
        //
        // Elle ne produit donc qu'une enveloppe gérée sur un handle natif NUL : le premier appel
        // déréférence du vide. Et les fonctions globales `LcAnClipKeyFrame.new` / `.{ctor}` /
        // `.delete` N'EXISTENT PAS dans navisworks.animator.clr, alors qu'elles existent pour la
        // caméra et la sélection. Autodesk a exporté de quoi LIRE et MODIFIER une image-clé de coupe
        // (GetPlanes / SetPlanes / EditPlanes) mais pas de quoi en CRÉER une.
        //
        // → Seule source possible : la capture manuelle dans l'Animator. Ensuite `CreateCopy()`,
        //   qui est public, permet de la cloner : c'est ce que fait
        //   animator_build_section_sweep_from_keyframe, et cela FONCTIONNE (16 clés générées).
        //   Mais TimeLiner n'applique pas les animations de coupe : voir Skills/15.
        // ─────────────────────────────────────────────────────────────────────────────────────────

        [McpServerTool, Description(
            "Supprime une scène Animator par son nom. Sert au nettoyage : les jeux de sélection se vident " +
            "avec clear_sets_folder, mais les scènes d'animation n'avaient aucun outil de suppression.")]
        public static Task<DeleteSceneResult> animator_delete_scene(
            [Description("Nom exact de la scène à supprimer")] string sceneName) =>
            UiThread.Run(() =>
            {
                var state = LcOpState.GetActiveInstance();
                if (state == null) return new DeleteSceneResult { Error = "LcOpState.GetActiveInstance() a renvoyé null." };
                var root = LcAnSavedAnimationsElement.Get(state);
                if (root == null) return new DeleteSceneResult { Error = "Racine des animations nulle (aucune scène n'a jamais existé)." };
                var rootFolder = root.Get();
                if (rootFolder == null) return new DeleteSceneResult { Error = "root.Get() a renvoyé null." };

                int removed = 0;
                for (int i = rootFolder.Children.Count() - 1; i >= 0; i--)
                {
                    if (rootFolder.Children.ElementAt(i).DisplayName != sceneName) continue;
                    try { LcAnSavedAnimationsElement.DeleteItem(state, rootFolder, i); removed++; } catch { }
                }
                return new DeleteSceneResult { SceneName = sceneName, Removed = removed, Remaining = rootFolder.Children.Count() };
            });

        [McpServerTool, Description(
            "Crée DE BOUT EN BOUT, par code, une animation Animator qui fait DESCENDRE un jeu de sélection " +
            "étage par étage : une scène, un jeu d'animation ciblant la sélection, et une image-clé de " +
            "translation par niveau, calées sur les hauteurs d'étage RÉELLES lues dans le modèle. " +
            "Sert à faire descendre un équipement de chantier (plateforme, abri, ascenseur) au rythme d'une démolition 4D par niveau. " +
            "L'animation obtenue s'attache ensuite aux tâches avec timeliner_set_task_animation. " +
            "PRÉREQUIS : au moins une scène doit avoir déjà existé dans l'Animator de ce document, sinon la " +
            "racine des animations est nulle (élément créé paresseusement) : vérifier avec animator_probe.")]
        public static Task<BuildDescentResult> animator_build_level_descent(
            [Description("Jeu de sélection à animer (ex 'EXCLU - Abri', produit par build_level_demolition_sets)")] string setName,
            [Description("Nom de la scène à créer (def 'Descente')")] string sceneName = "Descente",
            [Description("Nom du jeu d'animation (vide = nom du jeu de sélection)")] string animName = "",
            [Description("Niveau de départ, en haut (vide = le plus haut du modèle)")] string fromLevel = "",
            [Description("Niveau d'arrivée, en bas (vide = le plus bas du modèle)")] string toLevel = "",
            [Description("Durée totale de l'animation en secondes (def 60)")] double durationSeconds = 60,
            [Description("true = descente en escalier (palier puis chute), false = rampe continue (def true)")] bool stepwise = true,
            [Description("Part du palier consacrée à la chute, 0.05 à 0.9 (def 0.25)")] double dropFraction = 0.25,
            [Description("Valeur de 'Catégorie' des cotes de niveau (def 'Niveaux')")] string levelCategoryValue = "Niveaux") =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;

                var state = LcOpState.GetActiveInstance();
                if (state == null) return new BuildDescentResult { Error = "LcOpState.GetActiveInstance() a renvoyé null." };

                var root = LcAnSavedAnimationsElement.Get(state);
                if (root == null)
                    return new BuildDescentResult { Error = "Racine des animations NULLE. L'élément est créé paresseusement : créer une scène une fois dans l'Animator (volet Animator, clic droit, Add Scene), puis relancer." };

                var rootFolder = root.Get();
                if (rootFolder == null) return new BuildDescentResult { Error = "root.Get() a renvoyé null." };

                // --- La cible.
                var set = FindSet(doc, setName);
                if (set == null) return new BuildDescentResult { Error = "Jeu de sélection introuvable: '" + setName + "'" };
                var items = new ModelItemCollection();
                items.AddRange(set.GetSelectedItems());
                if (items.Count == 0) return new BuildDescentResult { Error = "Le jeu '" + setName + "' est vide." };

                // --- Les paliers, d'après les hauteurs d'étage réelles.
                var planes = LevelSetTools.ReadLevelPlanes(doc, levelCategoryValue);
                if (planes.Count < 2) return new BuildDescentResult { Error = "Moins de 2 cotes de niveau trouvées : impossible de bâtir une descente." };

                planes.Reverse(); // du haut vers le bas, sens de la démolition
                int start = string.IsNullOrWhiteSpace(fromLevel) ? 0 : planes.FindIndex(p => p.Level == fromLevel);
                int end = string.IsNullOrWhiteSpace(toLevel) ? planes.Count - 1 : planes.FindIndex(p => p.Level == toLevel);
                if (start < 0) return new BuildDescentResult { Error = "fromLevel introuvable: '" + fromLevel + "'", LevelsAvailable = planes.Select(p => p.Level).ToArray() };
                if (end < 0) return new BuildDescentResult { Error = "toLevel introuvable: '" + toLevel + "'", LevelsAvailable = planes.Select(p => p.Level).ToArray() };
                if (end <= start) return new BuildDescentResult { Error = "toLevel doit être SOUS fromLevel dans l'ordre descendant." };

                var path = planes.GetRange(start, end - start + 1);
                double zRef = path[0].Z;
                if (dropFraction < 0.05) dropFraction = 0.05;
                if (dropFraction > 0.9) dropFraction = 0.9;

                // --- Purge d'une scène homonyme, pour être relançable.
                // NB: dans cette couche, les MUTATIONS sont des helpers STATIQUES qui prennent l'état
                // (InsertItemCopy, DeleteItem) ; seules les lectures (Get, GetNum, MakePath) sont d'instance.
                for (int i = rootFolder.Children.Count() - 1; i >= 0; i--)
                {
                    var child = rootFolder.Children.ElementAt(i);
                    if (child.DisplayName == sceneName)
                        try { LcAnSavedAnimationsElement.DeleteItem(state, rootFolder, i); } catch { }
                }

                // --- La scène, puis le jeu d'animation. InsertItemCopy insère une COPIE et la retourne :
                //     c'est cette copie qu'il faut utiliser comme parent des insertions suivantes.
                var sceneInDoc = LcAnSavedAnimationsElement.InsertItemCopy(state, rootFolder, rootFolder.Children.Count(),
                    new LcAnScene { DisplayName = sceneName }) as GroupItem;
                if (sceneInDoc == null) return new BuildDescentResult { Error = "Création de la scène refusée (InsertItemCopy a renvoyé null ou un non-GroupItem)." };

                var anim = new LcAnSelectionAnim { DisplayName = string.IsNullOrWhiteSpace(animName) ? setName : animName };
                anim.SetSelection(new Selection(items));
                var animInDoc = LcAnSavedAnimationsElement.InsertItemCopy(state, sceneInDoc, 0, anim) as GroupItem;
                if (animInDoc == null) return new BuildDescentResult { Error = "Création du jeu d'animation refusée." };
                try { (animInDoc as LcAnSelectionAnim)?.PropogateImplicitCenter(state); } catch { }

                // --- Les images-clés : une par palier, décalage Z cumulé = hauteurs d'étage réelles.
                var made = new List<DescentKeyFrame>();
                int idx = 0;
                double step = durationSeconds / Math.Max(1, path.Count - 1);

                void Emit(double t, double z, string level, string kind)
                {
                    var kf = new LcAnSelectionKeyFrame();
                    kf.SetTime(Math.Round(t, 3));
                    kf.SetTranslation(new Vector3D(0, 0, Math.Round(z, 4)));
                    kf.SetInterpolate(true);
                    LcAnSavedAnimationsElement.InsertItemCopy(state, animInDoc, idx++, kf);
                    made.Add(new DescentKeyFrame { Time = Math.Round(t, 3), OffsetZ = Math.Round(z, 4), Level = level, Kind = kind });
                }

                for (int i = 0; i < path.Count; i++)
                {
                    double z = path[i].Z - zRef;      // négatif : on descend
                    double tArrive = i * step;
                    Emit(tArrive, z, path[i].Level, "arrivee");
                    if (stepwise && i < path.Count - 1)
                        Emit(tArrive + step * (1 - dropFraction), z, path[i].Level, "fin de palier");
                }

                // --- La référence, pour l'attacher aux tâches TimeLiner.
                string reference = null, partId = null, itemId = null;
                try
                {
                    var r = new SavedItemReference();
                    root.MakePath(sceneInDoc, r);
                    if (!r.IsNull) { partId = r.DocumentPartId; itemId = r.SavedItemId; reference = partId + " | " + itemId; }
                }
                catch (Exception ex) { reference = "MakePath a échoué: " + ex.Message; }

                return new BuildDescentResult
                {
                    SceneName = sceneName,
                    AnimationName = anim.DisplayName,
                    TargetSet = setName,
                    TargetElements = items.Count,
                    FromLevel = path[0].Level,
                    ToLevel = path[path.Count - 1].Level,
                    TotalDropFeet = Math.Round(path[path.Count - 1].Z - zRef, 3),
                    DurationSeconds = durationSeconds,
                    Stepwise = stepwise,
                    KeyFramesCreated = made.Count,
                    KeyFrames = made.ToArray(),
                    SceneReference = reference,
                    DocumentPartId = partId,
                    SavedItemId = itemId
                };
            });

        [McpServerTool(ReadOnly = true), Description(
            "Lit le lien animation d'une tâche TimeLiner : la référence (DocumentPartId | SavedItemId) et le " +
            "comportement (Scale / Start / End). Sert à récupérer la référence d'une animation attachée à la " +
            "main dans l'UI, pour ensuite la recopier sur d'autres tâches par code.")]
        public static Task<TaskAnimationResult> timeliner_get_task_animation(
            [Description("Nom exact de la tâche")] string taskName) =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var tl = TimelinerDocumentExtensions.GetTimeliner(doc);

                TimelinerTask found = null;
                void Walk(SavedItemCollection items)
                {
                    foreach (var it in items)
                    {
                        if (!(it is TimelinerTask t)) continue;
                        if (found == null && t.DisplayName == taskName) found = t;
                        if (t.IsGroup) Walk(t.Children);
                    }
                }
                Walk(tl.Tasks);

                if (found == null) return new TaskAnimationResult { Error = "Tâche introuvable: '" + taskName + "'" };

                var path = found.AnimationPath;
                return new TaskAnimationResult
                {
                    TaskName = taskName,
                    HasAnimation = path != null && !path.IsNull,
                    DocumentPartId = path?.DocumentPartId,
                    SavedItemId = path?.SavedItemId,
                    DisplayString = path?.DisplayString,
                    Behavior = found.AnimationBehavior.ToString()
                };
            });

        [McpServerTool, Description(
            "Applique un lien animation à UNE OU PLUSIEURS tâches TimeLiner (noms séparés par ';'), à partir " +
            "d'une référence obtenue avec timeliner_get_task_animation. Permet d'attacher une animation créée " +
            "à la main dans l'Animator à toute une séquence, sans refaire le geste dans l'UI. " +
            "behavior: 'Scale' (l'animation s'étale sur la durée de la tâche, le plus courant), 'Start' ou 'End'.")]
        public static Task<SetTaskAnimationResult> timeliner_set_task_animation(
            [Description("Nom(s) de tâche, séparés par ';'")] string taskNames,
            [Description("DocumentPartId de la référence d'animation")] string documentPartId,
            [Description("SavedItemId de la référence d'animation")] string savedItemId,
            [Description("Comportement: Scale (def), Start ou End")] string behavior = "Scale") =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var tl = TimelinerDocumentExtensions.GetTimeliner(doc);

                if (!Enum.TryParse<AnimationBehavior>(behavior, true, out var beh))
                    return new SetTaskAnimationResult { Error = "Comportement invalide: '" + behavior + "' (Scale, Start ou End)" };

                var wanted = taskNames.Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                var done = new List<string>();
                var failed = new List<string>();

                foreach (var wantedName in wanted)
                {
                    TimelinerTask task = null;
                    GroupItem parent = null;
                    int index = -1;

                    void Walk(SavedItemCollection items, GroupItem owner)
                    {
                        int i = 0;
                        foreach (var it in items)
                        {
                            if (it is TimelinerTask t)
                            {
                                if (task == null && t.DisplayName == wantedName) { task = t; parent = owner; index = i; }
                                if (t.IsGroup) Walk(t.Children, t);
                            }
                            i++;
                        }
                    }
                    Walk(tl.Tasks, tl.TasksRoot);

                    if (task == null) { failed.Add(wantedName + " (introuvable)"); continue; }

                    try
                    {
                        // Les tâches du document sont en lecture seule : passer par une copie + TaskEdit,
                        // même schéma que timeliner_set_task_dates.
                        var copy = task.CreateCopy();
                        copy.AnimationPath = new SavedItemReference(documentPartId, savedItemId);
                        copy.AnimationBehavior = beh;
                        tl.TaskEdit(parent ?? tl.TasksRoot, index, copy);
                        done.Add(wantedName);
                    }
                    catch (Exception ex) { failed.Add(wantedName + " (" + ex.Message + ")"); }
                }

                return new SetTaskAnimationResult
                {
                    Applied = done.Count,
                    Tasks = done.ToArray(),
                    Failed = failed.ToArray(),
                    Behavior = beh.ToString()
                };
            });
    }

    public class AnimatorProbeResult
    {
        public ProbeStep[] Steps { get; set; }
        public AnimationInfo[] Animations { get; set; }
        public int AnimationCount { get; set; }
        public bool CreationAttempted { get; set; }
    }

    public class ProbeStep
    {
        public string Step { get; set; }
        public bool Ok { get; set; }
        public string Detail { get; set; }
    }

    public class AnimationInfo
    {
        public string Path { get; set; }
        public string TypeName { get; set; }
        public bool IsGroup { get; set; }
        public string Reference { get; set; }
    }

    public class TaskAnimationResult
    {
        public string TaskName { get; set; }
        public bool HasAnimation { get; set; }
        public string DocumentPartId { get; set; }
        public string SavedItemId { get; set; }
        public string DisplayString { get; set; }
        public string Behavior { get; set; }
        public string Error { get; set; }
    }

    public class BuildDescentResult
    {
        public string SceneName { get; set; }
        public string AnimationName { get; set; }
        public string TargetSet { get; set; }
        public int TargetElements { get; set; }
        public string FromLevel { get; set; }
        public string ToLevel { get; set; }
        public double TotalDropFeet { get; set; }
        public double DurationSeconds { get; set; }
        public bool Stepwise { get; set; }
        public int KeyFramesCreated { get; set; }
        public DescentKeyFrame[] KeyFrames { get; set; }
        public string SceneReference { get; set; }
        public string DocumentPartId { get; set; }
        public string SavedItemId { get; set; }
        public string[] LevelsAvailable { get; set; }
        public string Error { get; set; }
    }

    public class CameraZoomResult
    {
        public string SceneName { get; set; }
        public string AnimationName { get; set; }
        public string TargetSet { get; set; }
        public int TargetElements { get; set; }
        public string TargetPoint { get; set; }
        public double ZoomPadding { get; set; }
        public double DurationSeconds { get; set; }
        public string Projection { get; set; }
        public string HomeViewpoint { get; set; }
        public CameraKeyFrameInfo[] KeyFrames { get; set; }
        public string DocumentPartId { get; set; }
        public string SavedItemId { get; set; }
        public string Error { get; set; }
    }

    public class CameraKeyFrameInfo
    {
        public double Time { get; set; }
        public string Position { get; set; }
        public double HeightField { get; set; }
        public string Label { get; set; }
    }

    public class ReflectTypesResult
    {
        public string Assembly { get; set; }
        public string Filter { get; set; }
        public ReflectedType[] Types { get; set; }
        public string Error { get; set; }
    }

    public class ReflectedType
    {
        public string TypeName { get; set; }
        public string BaseType { get; set; }
        public string[] Constructors { get; set; }
        public int MethodCount { get; set; }
        public string[] Methods { get; set; }
        public string Error { get; set; }
    }

    public class InspectAnimResult
    {
        public string SceneName { get; set; }
        public string AnimName { get; set; }
        public string TypeName { get; set; }
        public int ChildrenCount { get; set; }
        public int OrderedCount { get; set; }
        public double Duration { get; set; }
        public KeyFrameRow[] KeyFrames { get; set; }
        public string Verdict { get; set; }
        public string Note { get; set; }
        public string Error { get; set; }
    }

    public class KeyFrameRow
    {
        public int Index { get; set; }
        public string TypeName { get; set; }
        public double Time { get; set; }
        public int Planes { get; set; }
        public string FirstPlaneDistances { get; set; }
        public string Note { get; set; }
    }

    public class CreateSectionSetResult
    {
        public string SceneName { get; set; }
        public bool SceneCreated { get; set; }
        public string AnimName { get; set; }
        public string TypeCreated { get; set; }
        public string NextStep { get; set; }
        public string Error { get; set; }
    }

    public class SweepFromKeyframeResult
    {
        public string SceneName { get; set; }
        public string AnimName { get; set; }
        public int PlaneIndex { get; set; }
        public string PlaneNormal { get; set; }
        public double TemplateDistance { get; set; }
        public int KeyFramesCreated { get; set; }
        public double DurationSeconds { get; set; }
        public SweepStep2[] Steps { get; set; }
        public string DocumentPartId { get; set; }
        public string SavedItemId { get; set; }
        public string Error { get; set; }
    }

    public class SweepStep2
    {
        public int Index { get; set; }
        public double Time { get; set; }
        public double Offset { get; set; }
        public double Distance { get; set; }
        public string Error { get; set; }
    }

    public class ClipProbeResult
    {
        public bool Constructed { get; set; }
        public bool Usable { get; set; }
        public string[] Steps { get; set; }
        public string Error { get; set; }
    }

    public class DeleteSceneResult
    {
        public string SceneName { get; set; }
        public int Removed { get; set; }
        public int Remaining { get; set; }
        public string Error { get; set; }
    }

    public class DescentKeyFrame
    {
        public double Time { get; set; }
        public double OffsetZ { get; set; }
        public string Level { get; set; }
        public string Kind { get; set; }
    }

    public class SetTaskAnimationResult
    {
        public int Applied { get; set; }
        public string[] Tasks { get; set; }
        public string[] Failed { get; set; }
        public string Behavior { get; set; }
        public string Error { get; set; }
    }
}
