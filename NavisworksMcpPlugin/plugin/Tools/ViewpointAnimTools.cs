using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Navisworks.Api;
using NavisworksMcpPlugin.Mcp;

namespace NavisworksMcpPlugin.Tools
{
    /// <summary>
    /// Animations de POINTS DE VUE enregistrés (SavedViewpointAnimation).
    ///
    /// Pourquoi cette voie plutôt qu'une animation de caméra Animator : une image-clé de caméra
    /// (LcAnCameraKeyFrame) ne transporte QUE la caméra. Un POINT DE VUE, lui, transporte aussi ses
    /// PLANS DE COUPE, son style de rendu et son éclairage. Une animation de points de vue permet
    /// donc de faire évoluer une boîte de section en même temps que le cadrage, ce qu'une animation
    /// de caméra ne sait pas faire.
    ///
    /// C'est aussi la seule voie praticable ici : LcAnClipKeyFrame, l'image-clé de coupe de
    /// l'Animator, n'a AUCUN constructeur public et ne peut donc pas être fabriquée par code.
    /// </summary>
    [McpServerToolType]
    public static class ViewpointAnimTools
    {
        private static SavedViewpoint Resolve(Document doc, string name)
        {
            SavedViewpoint hit = null;
            void Walk(SavedItem item)
            {
                if (hit != null) return;
                if (item is SavedViewpoint s && s.DisplayName == name) { hit = s; return; }
                if (item is GroupItem g) foreach (var c in g.Children) Walk(c);
            }
            foreach (var child in doc.SavedViewpoints.RootItem.Children) Walk(child);
            return hit;
        }

        [McpServerTool(ReadOnly = true), Description(
            "Liste les PLANS DE COUPE d'un point de vue enregistré : index, normale, distance, actif. " +
            "À appeler AVANT viewpoint_build_section_sweep pour choisir le bon plan à faire balayer.")]
        public static Task<ClipPlanesResult> viewpoint_list_clip_planes(
            [Description("Nom du point de vue enregistré")] string viewpointName) =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var sv = Resolve(doc, viewpointName);
                if (sv == null) return new ClipPlanesResult { Error = "Point de vue introuvable: '" + viewpointName + "'" };
                var cp = sv.Viewpoint.ClipPlanes;
                if (cp == null) return new ClipPlanesResult { Error = "Ce point de vue n'a pas de plans de coupe." };

                var rows = new List<ClipPlaneInfo>();
                for (int i = 0; i < cp.Size(); i++)
                {
                    var p = cp.Get(i);
                    rows.Add(new ClipPlaneInfo
                    {
                        Index = i + 1,
                        Normal = string.Format(CultureInfo.InvariantCulture, "{0:F3}, {1:F3}, {2:F3}", p.Normal.X, p.Normal.Y, p.Normal.Z),
                        Distance = Math.Round(p.Distance, 3),
                        Enabled = p.Enabled
                    });
                }
                return new ClipPlanesResult
                {
                    Viewpoint = viewpointName,
                    SetEnabled = cp.Enabled,
                    Mode = cp.Mode.ToString(),
                    Count = rows.Count,
                    Planes = rows.ToArray()
                };
            });

        // ABANDONNÉ le 2026-08-06, conservé à titre documentaire : cet outil produit bien les points
        // de vue et l'animation, mais TIMELINER N'APPLIQUE PAS les plans de coupe portés par une
        // animation de points de vue. Vérifié deux fois par comparaison d'images rendues : les images
        // sont rigoureusement identiques. Ajouter des SavedViewpointAnimationCut aggrave le résultat.
        // Ne pas rouvrir cette piste : Skills/15_coupe_animee_verdict.md.
        [McpServerTool, Description(
            "ABANDONNÉ : ne produit AUCUN effet visible en simulation. TimeLiner ignore les plans de coupe " +
            "portés par une animation de points de vue (prouvé deux fois par comparaison d'images rendues). " +
            "Conservé pour mémoire, voir Skills/15_coupe_animee_verdict.md. Ancienne description : " +
            "Fait BALAYER un plan de coupe : génère une suite de points de vue identiques au point de vue de " +
            "base, dans lesquels UN plan de coupe est déplacé progressivement le long de sa normale, et les " +
            "assemble en animation. C'est l'équivalent, pour une surface de coupe, de ce que fait " +
            "animator_build_translation pour un objet. " +
            "Utiliser quand une animation à deux vues ne suffit pas : ici chaque position intermédiaire est " +
            "calculée et matérialisée, donc le balayage est aussi fin que le nombre d'étapes. " +
            "Les décalages sont RELATIFS à la position enregistrée du plan : 0 = là où il est dans le point " +
            "de vue de base. Repérer l'index avec viewpoint_list_clip_planes.")]
        public static Task<SweepResult> viewpoint_build_section_sweep(
            [Description("Nom de l'animation à créer (remplace une homonyme)")] string name,
            [Description("Point de vue de base : cadrage et coupe de référence")] string baseViewpoint,
            [Description("Index du plan à déplacer, 1 = premier (voir viewpoint_list_clip_planes)")] int planeIndex,
            [Description("Décalage de DÉPART le long de la normale, unités du document")] double fromOffset,
            [Description("Décalage d'ARRIVÉE, 0 = position enregistrée du plan (def 0)")] double toOffset = 0,
            [Description("Nombre de points de vue générés, minimum 2 (def 12 : balayage fluide)")] int steps = 12,
            [Description("Lissage entre les vues (def true)")] bool smoothing = true,
            [Description("Durée en secondes entre deux vues (def 1). SANS ce délai l'animation a une durée " +
                         "NULLE et se joue en un instant : c'est le piège qui rend un balayage invisible.")] double secondsPerStep = 1.0) =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                if (steps < 2) steps = 2;
                var sv = Resolve(doc, baseViewpoint);
                if (sv == null) return new SweepResult { Error = "Point de vue introuvable: '" + baseViewpoint + "'" };
                var refCp = sv.Viewpoint.ClipPlanes;
                if (refCp == null || refCp.Size() == 0) return new SweepResult { Error = "Le point de vue de base n'a pas de plans de coupe." };
                if (planeIndex < 1 || planeIndex > refCp.Size())
                    return new SweepResult { Error = "planeIndex hors bornes : 1 à " + refCp.Size() };

                var n0 = refCp.Get(planeIndex - 1).Normal;
                double d0 = refCp.Get(planeIndex - 1).Distance;

                var root = doc.SavedViewpoints.RootItem;
                for (int i = root.Children.Count() - 1; i >= 0; i--)
                    if (root.Children.ElementAt(i) is SavedViewpointAnimation && root.Children.ElementAt(i).DisplayName == name)
                        try { doc.SavedViewpoints.RemoveAt(root, i); } catch { }

                var anim = new SavedViewpointAnimation { DisplayName = name, Loops = false };
                try
                {
                    var vals = Enum.GetValues(typeof(SavedViewpointAnimationSmoothing)).Cast<object>().ToList();
                    var none = vals.FirstOrDefault(v => v.ToString().Equals("None", StringComparison.OrdinalIgnoreCase));
                    var other = vals.FirstOrDefault(v => !v.ToString().Equals("None", StringComparison.OrdinalIgnoreCase));
                    var chosen = smoothing ? (other ?? none) : (none ?? other);
                    if (chosen != null) anim.Smoothing = (SavedViewpointAnimationSmoothing)chosen;
                }
                catch { }
                doc.SavedViewpoints.AddCopy(anim);

                SavedViewpointAnimation inDoc = null;
                for (int i = root.Children.Count() - 1; i >= 0; i--)
                    if (root.Children.ElementAt(i) is SavedViewpointAnimation a && a.DisplayName == name) { inDoc = a; break; }
                if (inDoc == null) return new SweepResult { Error = "Animation créée mais introuvable après insertion." };

                var made = new List<SweepStep>();
                for (int k = 0; k < steps; k++)
                {
                    double t = (double)k / (steps - 1);
                    double off = fromOffset + (toOffset - fromOffset) * t;

                    var vp = sv.Viewpoint.CreateCopy();
                    var cp = new ClipPlaneSet();
                    cp.CopyFrom(vp.ClipPlanes);
                    // Déplacement le long de la NORMALE du plan : c'est ce qui fait glisser la surface
                    // de coupe à travers le modèle sans changer son orientation.
                    cp.Translate(planeIndex - 1, new Vector3D(n0.X * off, n0.Y * off, n0.Z * off));
                    vp.ClipPlanes = cp;

                    var label = string.Format(CultureInfo.InvariantCulture, "{0:00} - offset {1:F1}", k + 1, off);
                    doc.SavedViewpoints.AddCopy(inDoc, new SavedViewpoint(vp) { DisplayName = label });

                    // PIÈGE CENTRAL : une animation de points de vue SANS SavedViewpointAnimationCut a une
                    // durée NULLE. Elle se joue en un instant et n'affiche qu'une image figée, ce qui donne
                    // l'illusion que rien ne bouge. Le Cut porte le Delay, donc le temps entre deux vues.
                    if (k < steps - 1 && secondsPerStep > 0)
                        doc.SavedViewpoints.AddCopy(inDoc, new SavedViewpointAnimationCut
                        {
                            Delay = secondsPerStep,
                            DisplayName = string.Format(CultureInfo.InvariantCulture, "delai {0:F2}s", secondsPerStep)
                        });

                    double dNew = d0;
                    try { dNew = cp.Get(planeIndex - 1).Distance; } catch { }
                    made.Add(new SweepStep { Index = k + 1, Offset = Math.Round(off, 3), Distance = Math.Round(dNew, 3) });
                }

                string partId = null, itemId = null;
                try
                {
                    var r = doc.SavedViewpoints.CreateReference(inDoc);
                    if (r != null && !r.IsNull) { partId = r.DocumentPartId; itemId = r.SavedItemId; }
                }
                catch (Exception ex) { return new SweepResult { Error = "Référence impossible: " + ex.Message }; }

                return new SweepResult
                {
                    Name = name,
                    BaseViewpoint = baseViewpoint,
                    PlaneIndex = planeIndex,
                    PlaneNormal = string.Format(CultureInfo.InvariantCulture, "{0:F3}, {1:F3}, {2:F3}", n0.X, n0.Y, n0.Z),
                    OriginalDistance = Math.Round(d0, 3),
                    StepsCreated = made.Count,
                    SecondsPerStep = secondsPerStep,
                    TotalSeconds = Math.Round(secondsPerStep * (made.Count - 1), 2),
                    Steps = made.ToArray(),
                    DocumentPartId = partId,
                    SavedItemId = itemId
                };
            });

        [McpServerTool, Description(
            "Crée une ANIMATION DE POINTS DE VUE à partir de points de vue enregistrés, dans l'ordre donné. " +
            "À préférer à une animation de caméra dès qu'une BOÎTE DE SECTION doit évoluer : un point de vue " +
            "transporte ses plans de coupe, une image-clé de caméra non. C'est donc la seule façon de faire " +
            "balayer une coupe pendant la simulation. " +
            "L'animation obtenue s'attache à une tâche TimeLiner avec timeliner_set_task_animation, en " +
            "utilisant le documentPartId et le savedItemId retournés ici. " +
            "Relançable : une animation homonyme est remplacée.")]
        public static Task<ViewpointAnimResult> viewpoint_build_animation(
            [Description("Nom de l'animation à créer")] string name,
            [Description("Noms des points de vue enregistrés, dans l'ordre, séparés par ';'")] string viewpoints,
            [Description("Lissage entre les vues (def true : mouvement continu)")] bool smoothing = true,
            [Description("Lecture en boucle (def false)")] bool loops = false) =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var names = (viewpoints ?? "").Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                if (names.Count < 2)
                    return new ViewpointAnimResult { Error = "Il faut au moins 2 points de vue." };

                var resolved = new List<SavedViewpoint>();
                foreach (var n in names)
                {
                    var sv = Resolve(doc, n);
                    if (sv == null) return new ViewpointAnimResult { Error = "Point de vue introuvable: '" + n + "'" };
                    resolved.Add(sv);
                }

                // Purge d'une animation homonyme, pour être relançable.
                var root = doc.SavedViewpoints.RootItem;
                for (int i = root.Children.Count() - 1; i >= 0; i--)
                {
                    var child = root.Children.ElementAt(i);
                    if (child is SavedViewpointAnimation && child.DisplayName == name)
                        try { doc.SavedViewpoints.RemoveAt(root, i); } catch { }
                }

                var anim = new SavedViewpointAnimation { DisplayName = name, Loops = loops };
                try
                {
                    // L'enum de lissage varie selon les versions : on prend la valeur qui n'est pas 'None'
                    // quand on veut lisser, sinon 'None'. Résolu dynamiquement pour ne rien coder en dur.
                    var vals = Enum.GetValues(typeof(SavedViewpointAnimationSmoothing)).Cast<object>().ToList();
                    var none = vals.FirstOrDefault(v => v.ToString().Equals("None", StringComparison.OrdinalIgnoreCase));
                    var other = vals.FirstOrDefault(v => !v.ToString().Equals("None", StringComparison.OrdinalIgnoreCase));
                    var chosen = smoothing ? (other ?? none) : (none ?? other);
                    if (chosen != null) anim.Smoothing = (SavedViewpointAnimationSmoothing)chosen;
                }
                catch { }

                doc.SavedViewpoints.AddCopy(anim);

                // AddCopy insère une COPIE : relire l'objet du document pour y ajouter les vues.
                SavedViewpointAnimation inDoc = null;
                for (int i = root.Children.Count() - 1; i >= 0; i--)
                    if (root.Children.ElementAt(i) is SavedViewpointAnimation a && a.DisplayName == name) { inDoc = a; break; }
                if (inDoc == null) return new ViewpointAnimResult { Error = "Animation créée mais introuvable après insertion." };

                var added = new List<ViewpointStep>();
                for (int i = 0; i < resolved.Count; i++)
                {
                    var vp = resolved[i].Viewpoint.CreateCopy();
                    var label = string.Format(CultureInfo.InvariantCulture, "{0:00} - {1}", i + 1, names[i]);
                    doc.SavedViewpoints.AddCopy(inDoc, new SavedViewpoint(vp) { DisplayName = label });

                    int planes = 0; bool clip = false;
                    try { var cp = vp.ClipPlanes; if (cp != null) { clip = cp.Enabled; planes = cp.Size(); } } catch { }
                    added.Add(new ViewpointStep
                    {
                        Index = i + 1,
                        Source = names[i],
                        HeightField = Math.Round(vp.HeightField, 2),
                        ClipEnabled = clip,
                        ClipPlanes = planes
                    });
                }

                string partId = null, itemId = null;
                try
                {
                    var r = doc.SavedViewpoints.CreateReference(inDoc);
                    if (r != null && !r.IsNull) { partId = r.DocumentPartId; itemId = r.SavedItemId; }
                }
                catch (Exception ex) { return new ViewpointAnimResult { Error = "Référence impossible: " + ex.Message }; }

                return new ViewpointAnimResult
                {
                    Name = name,
                    Smoothing = anim.Smoothing.ToString(),
                    Loops = loops,
                    Steps = added.ToArray(),
                    DocumentPartId = partId,
                    SavedItemId = itemId
                };
            });
    }

    public class ViewpointAnimResult
    {
        public string Name { get; set; }
        public string Smoothing { get; set; }
        public bool Loops { get; set; }
        public ViewpointStep[] Steps { get; set; }
        public string DocumentPartId { get; set; }
        public string SavedItemId { get; set; }
        public string Error { get; set; }
    }

    public class ClipPlanesResult
    {
        public string Viewpoint { get; set; }
        public bool SetEnabled { get; set; }
        public string Mode { get; set; }
        public int Count { get; set; }
        public ClipPlaneInfo[] Planes { get; set; }
        public string Error { get; set; }
    }

    public class ClipPlaneInfo
    {
        public int Index { get; set; }
        public string Normal { get; set; }
        public double Distance { get; set; }
        public bool Enabled { get; set; }
    }

    public class SweepResult
    {
        public string Name { get; set; }
        public string BaseViewpoint { get; set; }
        public int PlaneIndex { get; set; }
        public string PlaneNormal { get; set; }
        public double OriginalDistance { get; set; }
        public int StepsCreated { get; set; }
        public double SecondsPerStep { get; set; }
        public double TotalSeconds { get; set; }
        public SweepStep[] Steps { get; set; }
        public string DocumentPartId { get; set; }
        public string SavedItemId { get; set; }
        public string Error { get; set; }
    }

    public class SweepStep
    {
        public int Index { get; set; }
        public double Offset { get; set; }
        public double Distance { get; set; }
    }

    public class ViewpointStep
    {
        public int Index { get; set; }
        public string Source { get; set; }
        public double HeightField { get; set; }
        public bool ClipEnabled { get; set; }
        public int ClipPlanes { get; set; }
    }
}
