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
    /// Découpe du modèle en TRANCHES selon un axe quelconque (X, Y ou Z), pour un balayage.
    ///
    /// Sert à reproduire visuellement un plan de coupe qui se déplace, là où Navisworks ne permet pas
    /// d'animer la section elle-même : au lieu de bouger une surface de coupe, on masque le bâtiment
    /// tranche par tranche avec des tâches TimeLiner, ce qui donne le même effet de révélation
    /// progressive avec un mécanisme parfaitement fiable.
    ///
    /// Généralise le regroupement par altitude de LevelSetTools à n'importe quel axe.
    /// </summary>
    [McpServerToolType]
    public static class SliceTools
    {
        private static string Prop(ModelItem item, string category, string property)
        {
            try
            {
                var p = item.PropertyCategories.FindPropertyByDisplayName(category, property);
                if (p?.Value == null) return null;
                var s = p.Value.ToDisplayString();
                return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
            }
            catch { return null; }
        }

        private static double Coord(Point3D p, string axis)
        {
            switch (axis)
            {
                case "X": return p.X;
                case "Z": return p.Z;
                default: return p.Y;
            }
        }

        [McpServerTool, Description(
            "Découpe le modèle en TRANCHES le long d'un axe (X, Y ou Z) et crée un jeu de sélection par " +
            "tranche, dans l'ordre du balayage. Sert à masquer progressivement une partie du bâtiment pour " +
            "révéler l'intérieur, quand une vraie animation de plan de coupe n'est pas possible. " +
            "Seuls les éléments dont le CENTRE tombe dans l'intervalle [from, to] sont retenus, ce qui permet " +
            "de ne traiter qu'une bande précise. " +
            "descending=true numérote les tranches en partant de 'to' vers 'from' : c'est le sens du balayage. " +
            "Les exclusions par famille et par type protègent les équipements qui ne doivent jamais disparaître.")]
        public static Task<SliceResult> build_slice_sets(
            [Description("Axe de découpe : X, Y ou Z")] string axis,
            [Description("Borne basse de la bande, unités du document")] double from,
            [Description("Borne haute de la bande")] double to,
            [Description("Nombre de tranches (def 12)")] int slices = 12,
            [Description("Dossier de Sets (def '4D_Balayage')")] string folder = "4D_Balayage",
            [Description("Préfixe des jeux (def 'TRANCHE ')")] string prefix = "TRANCHE ",
            [Description("Numéroter de 'to' vers 'from', sens du balayage (def true)")] bool descending = true,
            [Description("Familles à exclure, séparées par ';' (def aucune)")] string excludeFamilies = "",
            [Description("Types à exclure, séparés par ';'")] string excludeTypes = "Plateforme metallique",
            [Description("Vider le dossier avant (def true)")] bool clearFolder = true) =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                axis = (axis ?? "Y").Trim().ToUpperInvariant();
                if (axis != "X" && axis != "Y" && axis != "Z")
                    return new SliceResult { Error = "axis doit valoir X, Y ou Z." };
                if (to <= from) return new SliceResult { Error = "'to' doit être supérieur à 'from'." };
                if (slices < 1) slices = 1;

                var fam = (excludeFamilies ?? "").Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                var typ = (excludeTypes ?? "").Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

                double width = (to - from) / slices;
                var buckets = new ModelItemCollection[slices];
                for (int i = 0; i < slices; i++) buckets[i] = new ModelItemCollection();

                var taken = new HashSet<ModelItem>();
                int excluded = 0, outside = 0, kept = 0;

                foreach (var item in doc.Models.RootItemDescendantsAndSelf)
                {
                    if (!item.IsComposite && !item.HasGeometry) continue;

                    bool ancestorTaken = false;
                    for (var p = item.Parent; p != null; p = p.Parent)
                        if (taken.Contains(p)) { ancestorTaken = true; break; }
                    if (ancestorTaken) continue;

                    BoundingBox3D bb = null;
                    try { bb = item.BoundingBox(); } catch { }
                    if (bb == null) continue;

                    taken.Add(item);

                    var family = Prop(item, "Objet", "Famille") ?? "";
                    var type = Prop(item, "Objet", "Type") ?? "";
                    if (fam.Any(x => family.StartsWith(x, StringComparison.OrdinalIgnoreCase)) ||
                        typ.Any(x => type.StartsWith(x, StringComparison.OrdinalIgnoreCase)))
                    { excluded++; continue; }

                    // Le CENTRE décide de la tranche : un élément à cheval ne se dédouble pas.
                    double c = Coord(bb.Center, axis);
                    if (c < from || c >= to) { outside++; continue; }

                    int idx = (int)Math.Floor((c - from) / width);
                    if (idx < 0) idx = 0;
                    if (idx >= slices) idx = slices - 1;
                    buckets[idx].Add(item);
                    kept++;
                }

                if (clearFolder) TimelinerPrepTools.ClearSetsFolder(doc, folder, out _);
                var folderItem = TimelinerPrepTools.FindOrCreateFolder(doc, folder);
                if (folderItem == null) return new SliceResult { Error = "Dossier introuvable/non créé: " + folder };

                var order = Enumerable.Range(0, slices).ToList();
                if (descending) order.Reverse();

                var made = new List<SliceInfo>();
                int rank = 0;
                foreach (var i in order)
                {
                    if (buckets[i].Count == 0) continue;
                    rank++;
                    double lo = from + i * width, hi = lo + width;
                    var label = string.Format(CultureInfo.InvariantCulture, "{0}{1:00} - {2}{3:F1}", prefix, rank, axis, (lo + hi) / 2);
                    doc.SelectionSets.InsertCopy(folderItem, folderItem.Children.Count(),
                        new SelectionSet(buckets[i]) { DisplayName = label });
                    made.Add(new SliceInfo
                    {
                        Rank = rank,
                        Name = label,
                        From = Math.Round(lo, 2),
                        To = Math.Round(hi, 2),
                        Count = buckets[i].Count
                    });
                }

                return new SliceResult
                {
                    Folder = folder,
                    Axis = axis,
                    Band = string.Format(CultureInfo.InvariantCulture, "{0:F1} -> {1:F1}", from, to),
                    SliceWidth = Math.Round(width, 3),
                    SlicesCreated = made.Count,
                    ElementsKept = kept,
                    ElementsExcluded = excluded,
                    ElementsOutsideBand = outside,
                    Slices = made.ToArray()
                };
            });
    }

    public class SliceResult
    {
        public string Folder { get; set; }
        public string Axis { get; set; }
        public string Band { get; set; }
        public double SliceWidth { get; set; }
        public int SlicesCreated { get; set; }
        public int ElementsKept { get; set; }
        public int ElementsExcluded { get; set; }
        public int ElementsOutsideBand { get; set; }
        public SliceInfo[] Slices { get; set; }
        public string Error { get; set; }
    }

    public class SliceInfo
    {
        public int Rank { get; set; }
        public string Name { get; set; }
        public double From { get; set; }
        public double To { get; set; }
        public int Count { get; set; }
    }
}
