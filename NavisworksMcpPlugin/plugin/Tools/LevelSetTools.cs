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
    /// Classement par ALTITUDE des éléments qui n'ont pas de niveau.
    ///
    /// Problème résolu : un export Revit range les éléments par niveau dans la propriété
    /// « Calque », mais les familles non hébergées (Generic Models : habillages de poteaux,
    /// ancrages, câbles DirectShape) sortent avec « &lt;Pas de niveau&gt; ». Elles échappent donc à
    /// toute sélection par niveau, et une démolition 4D par étage les laisserait debout.
    /// Sur le projet pilote, plusieurs centaines d'éléments étaient dans ce cas.
    ///
    /// PIÈGE VÉRIFIÉ : il faut bucketer sur le Z de la BOÎTE ENGLOBANTE des cotes de niveau, PAS sur
    /// leur propriété « Élévation ». Sur le projet pilote, l'Élévation d'une cote s'écartait du Z de sa
    /// géométrie d'environ un tiers d'étage (décalage du datum de géoréférence). Les boîtes
    /// englobantes des éléments sont dans le même repère que celles des cotes, donc la comparaison
    /// est homogène.
    ///
    /// Second piège : « &lt;Pas de niveau&gt; » n'est PAS trouvable par search_by_property (Equals
    /// renvoie 0, les chevrons ne passent pas par VariantData.FromDisplayString). D'où la comparaison
    /// de chaînes en code, ici.
    /// </summary>
    [McpServerToolType]
    public static class LevelSetTools
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

        /// <summary>
        /// Lit les cotes de niveau du modèle : nom + Z de leur GÉOMÉTRIE, triés par altitude croissante.
        /// Utiliser le Z de la boîte englobante et NON la propriété « Élévation » (écart d'environ un
        /// tiers d'étage sur le projet pilote, décalage du datum de géoréférence). Partagé avec AnimatorTools.
        /// </summary>
        internal static List<LevelPlane> ReadLevelPlanes(Document doc, string levelCategoryValue)
        {
            var planes = new Dictionary<string, double>();
            foreach (var item in doc.Models.RootItemDescendantsAndSelf)
            {
                if (Prop(item, "Objet", "Catégorie") != levelCategoryValue) continue;
                var name = Prop(item, "Objet", "Nom");
                if (string.IsNullOrEmpty(name)) continue;
                var z = BaseZ(item);
                if (z == null) continue;
                if (!planes.ContainsKey(name)) planes[name] = z.Value;
            }
            return planes.OrderBy(kv => kv.Value)
                .Select(kv => new LevelPlane { Level = kv.Key, Z = Math.Round(kv.Value, 3) })
                .ToList();
        }

        private static double? BaseZ(ModelItem item)
        {
            try
            {
                var bb = item.BoundingBox();
                if (bb == null) return null;
                return bb.Min.Z;
            }
            catch { return null; }
        }

        [McpServerTool, Description(
            "Range par ALTITUDE les éléments qui n'ont pas de niveau (Calque = '<Pas de niveau>') et crée " +
            "un jeu de sélection par niveau, nommé 'NIVZ - <niveau>'. Indispensable pour une démolition 4D " +
            "par étage : les familles non hébergées (habillages de poteaux, ancrages, câbles) sortent de Revit " +
            "sans niveau et seraient autrement jamais démolies. " +
            "Lit les cotes de niveau du modèle (nom + Z de leur géométrie), en déduit des tranches " +
            "[Zn, Zn+1), puis affecte chaque élément selon le Z de sa base. " +
            "Retourne les cotes trouvées et les comptes par niveau : TOUJOURS vérifier ce mapping avant " +
            "de bâtir des tâches dessus.")]
        public static Task<LevelZResult> build_level_sets_by_elevation(
            [Description("Dossier de Sets cible (def '4D_Niveaux_Z')")] string folder = "4D_Niveaux_Z",
            [Description("Préfixe des jeux créés (def 'NIVZ - ')")] string setPrefix = "NIVZ - ",
            [Description("Valeur de Calque signifiant 'sans niveau' (def '<Pas de niveau>')")] string noLevelMarker = "<Pas de niveau>",
            [Description("Catégories à ignorer, séparées par ';' (def 'Quadrillages;Niveaux;Lignes' : sans géométrie à démolir)")] string excludeCategories = "Quadrillages;Niveaux;Lignes",
            [Description("Vider le dossier avant (def true)")] bool clearFolder = true,
            [Description("Catégorie de propriété portant le Calque (def 'Elément', NB accent sur le 2e e seulement)")] string layerCategory = "Elément",
            [Description("Propriété portant le niveau (def 'Calque')")] string layerProperty = "Calque",
            [Description("Valeur de 'Catégorie' identifiant les cotes de niveau (def 'Niveaux')")] string levelCategoryValue = "Niveaux") =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var excluded = new HashSet<string>(
                    (excludeCategories ?? "").Split(';').Select(s => s.Trim()).Where(s => s.Length > 0),
                    StringComparer.OrdinalIgnoreCase);

                // --- 1. Les cotes de niveau : nom + Z de leur géométrie (PAS la propriété Élévation).
                var planes = new Dictionary<string, double>();
                foreach (var item in doc.Models.RootItemDescendantsAndSelf)
                {
                    if (Prop(item, "Objet", "Catégorie") != levelCategoryValue) continue;
                    var name = Prop(item, "Objet", "Nom");
                    if (string.IsNullOrEmpty(name)) continue;
                    var z = BaseZ(item);
                    if (z == null) continue;
                    if (!planes.ContainsKey(name)) planes[name] = z.Value;
                }

                if (planes.Count == 0)
                    return new LevelZResult { Error = "Aucune cote de niveau trouvée (catégorie '" + levelCategoryValue + "'). Vérifier le libellé avec get_item_properties." };

                var ordered = planes.OrderBy(kv => kv.Value)
                    .Select(kv => new LevelPlane { Level = kv.Key, Z = Math.Round(kv.Value, 3) }).ToArray();

                // --- 2. Les éléments sans niveau, en ne gardant que le noeud le plus haut de chaque élément.
                var buckets = new Dictionary<string, ModelItemCollection>();
                var taken = new HashSet<ModelItem>();
                int belowLowest = 0, noBox = 0, skippedCategory = 0, considered = 0;

                foreach (var item in doc.Models.RootItemDescendantsAndSelf)
                {
                    if (Prop(item, layerCategory, layerProperty) != noLevelMarker) continue;
                    if (!item.IsComposite && !item.HasGeometry) continue;

                    // Un ancêtre déjà retenu => on est dans sa géométrie interne, on ne le compte pas 2 fois.
                    bool ancestorTaken = false;
                    for (var p = item.Parent; p != null; p = p.Parent)
                        if (taken.Contains(p)) { ancestorTaken = true; break; }
                    if (ancestorTaken) continue;

                    var cat = Prop(item, "Objet", "Catégorie");
                    if (cat != null && excluded.Contains(cat)) { skippedCategory++; continue; }

                    var z = BaseZ(item);
                    if (z == null) { noBox++; continue; }

                    taken.Add(item);
                    considered++;

                    // Tranche : le dernier niveau dont le plan est <= base de l'élément.
                    int idx = -1;
                    for (int i = 0; i < ordered.Length; i++)
                        if (z.Value >= ordered[i].Z - 0.01) idx = i;
                    if (idx < 0) { idx = 0; belowLowest++; }  // sous le niveau le plus bas : rattaché à celui-ci

                    var levelName = ordered[idx].Level;
                    if (!buckets.TryGetValue(levelName, out var coll)) { coll = new ModelItemCollection(); buckets[levelName] = coll; }
                    coll.Add(item);
                }

                // --- 3. Les jeux.
                if (clearFolder) TimelinerPrepTools.ClearSetsFolder(doc, folder, out _);
                var folderItem = TimelinerPrepTools.FindOrCreateFolder(doc, folder);
                if (folderItem == null)
                    return new LevelZResult { Error = "Dossier introuvable/non créé: " + folder, LevelPlanes = ordered };

                var made = new List<LevelZSet>();
                foreach (var plane in ordered)
                {
                    if (!buckets.TryGetValue(plane.Level, out var coll) || coll.Count == 0) continue;
                    var name = setPrefix + plane.Level;
                    var set = new SelectionSet(coll) { DisplayName = name };
                    doc.SelectionSets.InsertCopy(folderItem, folderItem.Children.Count(), set);
                    made.Add(new LevelZSet { Name = name, Level = plane.Level, Z = plane.Z, Count = coll.Count });
                }

                return new LevelZResult
                {
                    Folder = folder,
                    LevelPlanes = ordered,
                    SetsCreated = made.Count,
                    Sets = made.ToArray(),
                    TotalAssigned = considered,
                    BelowLowestLevel = belowLowest,
                    SkippedNoBoundingBox = noBox,
                    SkippedByCategory = skippedCategory
                };
            });
    }

    /// <summary>Volet « démolition par niveau avec exclusions ».</summary>
    [McpServerToolType]
    public static class DemolitionSetTools
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

        [McpServerTool, Description(
            "Construit les jeux de DÉMOLITION par niveau en EXCLUANT des familles, et rassemble les exclus " +
            "dans leurs propres jeux. Indispensable dès qu'un équipement traverse les étages sans devoir être " +
            "démoli : un équipement de chantier (plateforme, abri, ascenseur) doit DESCENDRE, pas disparaître, " +
            "alors qu'un simple jeu par niveau les emporte avec l'étage. " +
            "Un seul jeu par niveau ('DEMO NIV - <niveau>'), qui regroupe les éléments du niveau ET les " +
            "éléments sans niveau rattachés par altitude. Les exclus vont dans 'EXCLU - <motif>', " +
            "prêts à servir de cible à animator_build_level_descent. " +
            "excludeFamilies compare le DÉBUT du nom de famille, sans tenir compte de la casse.")]
        public static Task<DemolitionSetsResult> build_level_demolition_sets(
            [Description("Dossier de Sets cible (def '4D_Demolition')")] string folder = "4D_Demolition",
            [Description("Préfixe des jeux de démolition (def 'DEMO NIV - ')")] string prefix = "DEMO NIV - ",
            [Description("Familles à EXCLURE de la démolition, début de nom, séparées par ';' (def aucune, ex 'Abri;Ascenseur_chantier')")] string excludeFamilies = "",
            [Description("TYPES à exclure, début de nom, séparés par ';'. INDISPENSABLE quand un équipement " +
                         "partage une famille générique : une plateforme de chantier a souvent pour famille 'Sol', " +
                         "et seul son TYPE la distingue d'une dalle de bâtiment.")] string excludeTypes = "",
            [Description("Préfixe des jeux d'exclus (def 'EXCLU - ')")] string excludedSetPrefix = "EXCLU - ",
            [Description("Rattacher aussi les éléments sans niveau, par altitude (def true)")] bool includeLevellessByElevation = true,
            [Description("Catégories sans géométrie à ignorer (def 'Quadrillages;Niveaux;Lignes')")] string excludeCategories = "Quadrillages;Niveaux;Lignes",
            [Description("Vider le dossier avant (def true)")] bool clearFolder = true,
            [Description("Valeur de Calque signifiant 'sans niveau' (def '<Pas de niveau>')")] string noLevelMarker = "<Pas de niveau>",
            [Description("Catégorie portant le Calque (def 'Elément')")] string layerCategory = "Elément",
            [Description("Valeur de 'Catégorie' des cotes de niveau (def 'Niveaux')")] string levelCategoryValue = "Niveaux") =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var excludedCats = new HashSet<string>(
                    (excludeCategories ?? "").Split(';').Select(s => s.Trim()).Where(s => s.Length > 0),
                    StringComparer.OrdinalIgnoreCase);
                var tokens = (excludeFamilies ?? "").Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                var typeTokens = (excludeTypes ?? "").Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

                var planes = LevelSetTools.ReadLevelPlanes(doc, levelCategoryValue);
                if (planes.Count == 0)
                    return new DemolitionSetsResult { Error = "Aucune cote de niveau trouvée (catégorie '" + levelCategoryValue + "')." };

                var byLevel = new Dictionary<string, ModelItemCollection>();
                var byToken = new Dictionary<string, ModelItemCollection>();
                var taken = new HashSet<ModelItem>();
                int demolished = 0, excludedCount = 0, skippedCat = 0, noBox = 0;

                foreach (var item in doc.Models.RootItemDescendantsAndSelf)
                {
                    var level = Prop(item, layerCategory, "Calque");
                    if (level == null) continue;                       // pas un élément porteur de niveau
                    if (!item.IsComposite && !item.HasGeometry) continue;

                    bool ancestorTaken = false;
                    for (var p = item.Parent; p != null; p = p.Parent)
                        if (taken.Contains(p)) { ancestorTaken = true; break; }
                    if (ancestorTaken) continue;

                    var cat = Prop(item, "Objet", "Catégorie");
                    if (cat != null && excludedCats.Contains(cat)) { skippedCat++; continue; }

                    taken.Add(item);

                    // --- Exclusion : ces éléments ne seront JAMAIS démolis.
                    // Par FAMILLE, et aussi par TYPE : un équipement peut porter une famille générique
                    // (une plateforme de chantier a pour famille 'Sol', comme n'importe quelle dalle)
                    // et n'être identifiable que par son type. Exclure la famille serait alors ruineux.
                    var family = Prop(item, "Objet", "Famille") ?? item.DisplayName ?? "";
                    var hit = tokens.FirstOrDefault(t => family.StartsWith(t, StringComparison.OrdinalIgnoreCase));
                    if (hit == null && typeTokens.Count > 0)
                    {
                        var typeName = Prop(item, "Objet", "Type") ?? "";
                        hit = typeTokens.FirstOrDefault(t => typeName.StartsWith(t, StringComparison.OrdinalIgnoreCase));
                    }
                    if (hit != null)
                    {
                        if (!byToken.TryGetValue(hit, out var ex)) { ex = new ModelItemCollection(); byToken[hit] = ex; }
                        ex.Add(item);
                        excludedCount++;
                        continue;
                    }

                    // --- Affectation au niveau.
                    string levelName;
                    if (level != noLevelMarker) levelName = level;
                    else
                    {
                        if (!includeLevellessByElevation) continue;
                        var z = item.BoundingBox()?.Min.Z;
                        if (z == null) { noBox++; continue; }
                        int idx = -1;
                        for (int i = 0; i < planes.Count; i++)
                            if (z.Value >= planes[i].Z - 0.01) idx = i;
                        if (idx < 0) idx = 0;
                        levelName = planes[idx].Level;
                    }

                    if (!byLevel.TryGetValue(levelName, out var coll)) { coll = new ModelItemCollection(); byLevel[levelName] = coll; }
                    coll.Add(item);
                    demolished++;
                }

                if (clearFolder) TimelinerPrepTools.ClearSetsFolder(doc, folder, out _);
                var folderItem = TimelinerPrepTools.FindOrCreateFolder(doc, folder);
                if (folderItem == null)
                    return new DemolitionSetsResult { Error = "Dossier introuvable/non créé: " + folder };

                // Ordre descendant = ordre de démolition.
                var order = planes.Select(p => p.Level).Reverse().ToList();
                foreach (var extra in byLevel.Keys.Where(k => !order.Contains(k))) order.Add(extra);

                var made = new List<DemolitionSet>();
                foreach (var levelName in order)
                {
                    if (!byLevel.TryGetValue(levelName, out var coll) || coll.Count == 0) continue;
                    var name = prefix + levelName;
                    doc.SelectionSets.InsertCopy(folderItem, folderItem.Children.Count(),
                        new SelectionSet(coll) { DisplayName = name });
                    made.Add(new DemolitionSet { Name = name, Level = levelName, Count = coll.Count });
                }

                var exSets = new List<DemolitionSet>();
                foreach (var kv in byToken.OrderBy(k => k.Key))
                {
                    var name = excludedSetPrefix + kv.Key;
                    doc.SelectionSets.InsertCopy(folderItem, folderItem.Children.Count(),
                        new SelectionSet(kv.Value) { DisplayName = name });
                    exSets.Add(new DemolitionSet { Name = name, Level = "(exclu)", Count = kv.Value.Count });
                }

                return new DemolitionSetsResult
                {
                    Folder = folder,
                    DemolitionSets = made.ToArray(),
                    ExcludedSets = exSets.ToArray(),
                    TotalDemolished = demolished,
                    TotalExcluded = excludedCount,
                    SkippedByCategory = skippedCat,
                    SkippedNoBoundingBox = noBox
                };
            });
    }

    public class DemolitionSetsResult
    {
        public string Folder { get; set; }
        public DemolitionSet[] DemolitionSets { get; set; }
        public DemolitionSet[] ExcludedSets { get; set; }
        public int TotalDemolished { get; set; }
        public int TotalExcluded { get; set; }
        public int SkippedByCategory { get; set; }
        public int SkippedNoBoundingBox { get; set; }
        public string Error { get; set; }
    }

    public class DemolitionSet
    {
        public string Name { get; set; }
        public string Level { get; set; }
        public int Count { get; set; }
    }

    public class LevelZResult
    {
        public string Folder { get; set; }
        public LevelPlane[] LevelPlanes { get; set; }
        public int SetsCreated { get; set; }
        public LevelZSet[] Sets { get; set; }
        public int TotalAssigned { get; set; }
        public int BelowLowestLevel { get; set; }
        public int SkippedNoBoundingBox { get; set; }
        public int SkippedByCategory { get; set; }
        public string Error { get; set; }
    }

    public class LevelPlane
    {
        public string Level { get; set; }
        public double Z { get; set; }
    }

    public class LevelZSet
    {
        public string Name { get; set; }
        public string Level { get; set; }
        public double Z { get; set; }
        public int Count { get; set; }
    }
}
