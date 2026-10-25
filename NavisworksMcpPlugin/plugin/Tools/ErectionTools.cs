using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Timeliner;
using NavisworksMcpPlugin.Mcp;

namespace NavisworksMcpPlugin.Tools
{
    /// <summary>
    /// MONTAGE progressif d'un ouvrage répétitif, palier par palier selon l'altitude.
    /// Miroir de la démolition par niveau : ici on fait APPARAÎTRE des tronçons du bas vers le haut.
    /// Cas d'usage : mât d'ascenseur de chantier, échafaudage, sections de grue, panneaux préfabriqués.
    ///
    /// Regroupe les éléments par altitude de base (tolérance paramétrable, car deux tronçons d'un même
    /// palier ne sont jamais exactement au même Z), trie du bas vers le haut, puis crée un jeu de
    /// sélection ET une tâche Construct par palier, datés en séquence.
    /// </summary>
    [McpServerToolType]
    public static class ErectionTools
    {
        [McpServerTool, Description(
            "Monte un ouvrage répétitif PALIER PAR PALIER du bas vers le haut : regroupe les éléments par " +
            "altitude de base, crée un jeu de sélection et une tâche de construction par palier, datés en " +
            "séquence. Pour un mât d'ascenseur, un échafaudage, des sections de grue. " +
            "nameFilter porte sur le nom affiché de l'élément (ex 'Hoist Mast'). " +
            "daysPerStep accepte des fractions (0.5 = un demi-jour par palier). " +
            "Vérifier les paliers retournés AVANT de jouer la simulation : un pas mal choisi produit des " +
            "paliers dédoublés ou fusionnés.")]
        public static Task<ErectionResult> timeliner_build_erection_by_elevation(
            [Description("Nom affiché des éléments à monter (ex 'Hoist Mast')")] string nameFilter,
            [Description("Date de début du 1er palier, ISO (ex 2030-01-07)")] string startDate,
            [Description("Durée d'un palier en jours, fractions acceptées (def 1)")] double daysPerStep = 1,
            [Description("Dossier de Sets (def '4D_Montage')")] string setFolder = "4D_Montage",
            [Description("Préfixe des jeux et des tâches (def 'MONTAGE - ')")] string prefix = "MONTAGE - ",
            [Description("Type de tâche: 'construct' (def) ou nom exact d'un type existant")] string taskType = "construct",
            [Description("Tolérance de regroupement en altitude, unités du document (def 0.5)")] double bandTolerance = 0.5,
            [Description("true = nom exact, false = le nom contient nameFilter (def true)")] bool matchExact = true,
            [Description("Vider le dossier de Sets avant (def true)")] bool clearFolder = true,
            [Description("Supprimer toutes les tâches TimeLiner avant (def false)")] bool clearTasks = false) =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                if (string.IsNullOrWhiteSpace(nameFilter))
                    return new ErectionResult { Error = "nameFilter est obligatoire." };
                if (!DateTime.TryParse(startDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
                    return new ErectionResult { Error = "startDate invalide: '" + startDate + "' (ISO attendu)" };
                if (daysPerStep <= 0) return new ErectionResult { Error = "daysPerStep doit être positif." };
                if (bandTolerance <= 0) bandTolerance = 0.5;

                // --- Collecte : uniquement le noeud le plus haut de chaque élément.
                var found = new List<(ModelItem Item, double Z)>();
                var taken = new HashSet<ModelItem>();
                foreach (var item in doc.Models.RootItemDescendantsAndSelf)
                {
                    var name = item.DisplayName;
                    if (string.IsNullOrEmpty(name)) continue;
                    bool hit = matchExact
                        ? string.Equals(name, nameFilter, StringComparison.OrdinalIgnoreCase)
                        : name.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!hit) continue;
                    if (!item.IsComposite && !item.HasGeometry) continue;

                    bool ancestorTaken = false;
                    for (var p = item.Parent; p != null; p = p.Parent)
                        if (taken.Contains(p)) { ancestorTaken = true; break; }
                    if (ancestorTaken) continue;

                    double? z = null;
                    try { z = item.BoundingBox()?.Min.Z; } catch { }
                    if (z == null) continue;

                    taken.Add(item);
                    found.Add((item, z.Value));
                }

                if (found.Count == 0)
                    return new ErectionResult { Error = "Aucun élément trouvé pour '" + nameFilter + "' (essayer matchExact=false)." };

                // --- Regroupement par palier : deux tronçons d'un même palier ne sont jamais au même Z exact.
                var bands = new List<(double Z, ModelItemCollection Items)>();
                foreach (var f in found.OrderBy(f => f.Z))
                {
                    if (bands.Count > 0 && Math.Abs(f.Z - bands[bands.Count - 1].Z) <= bandTolerance)
                        bands[bands.Count - 1].Items.Add(f.Item);
                    else
                    {
                        var c = new ModelItemCollection();
                        c.Add(f.Item);
                        bands.Add((f.Z, c));
                    }
                }

                // --- Le type de tâche : jamais codé en dur, les noms sont localisés.
                var tl = TimelinerDocumentExtensions.GetTimeliner(doc);
                var typeNames = new List<string>();
                foreach (var it in tl.SimulationTaskTypes)
                    if (it is SimulationTaskType st) typeNames.Add(st.DisplayName);

                string resolvedType;
                var folded = (taskType ?? "").Trim().ToLowerInvariant();
                if (folded.Length == 0 || folded == "construct")
                    resolvedType = typeNames.FirstOrDefault(n => n.ToLowerInvariant().StartsWith("constru"));
                else
                    resolvedType = typeNames.FirstOrDefault(n => string.Equals(n, taskType, StringComparison.Ordinal));

                if (resolvedType == null)
                    return new ErectionResult
                    {
                        Error = "Type de tâche introuvable pour '" + taskType + "'.",
                        AvailableTaskTypes = typeNames.ToArray()
                    };

                if (clearTasks) tl.TasksClear();
                if (clearFolder) TimelinerPrepTools.ClearSetsFolder(doc, setFolder, out _);
                var folderItem = TimelinerPrepTools.FindOrCreateFolder(doc, setFolder);
                if (folderItem == null) return new ErectionResult { Error = "Dossier introuvable/non créé: " + setFolder };

                // --- Un jeu + une tâche par palier. Numérotation pour que le tri alphabétique = l'ordre.
                var steps = new List<ErectionStep>();
                for (int i = 0; i < bands.Count; i++)
                {
                    var b = bands[i];
                    var label = prefix + (i + 1).ToString("00", CultureInfo.InvariantCulture) +
                                " - Z" + b.Z.ToString("F1", CultureInfo.InvariantCulture);

                    doc.SelectionSets.InsertCopy(folderItem, folderItem.Children.Count(),
                        new SelectionSet(b.Items) { DisplayName = label });
                    var setInDoc = TimelinerPrepTools.FindSetInFolder(folderItem, label);

                    var from = start.AddDays(i * daysPerStep);
                    var to = start.AddDays((i + 1) * daysPerStep);

                    var task = new TimelinerTask
                    {
                        DisplayName = label,
                        SimulationTaskTypeName = resolvedType,
                        PlannedStartDate = from,
                        PlannedEndDate = to
                    };

                    string warn = null;
                    if (setInDoc == null) warn = "jeu non résolu, tâche sans attachement";
                    else
                    {
                        try
                        {
                            var sources = new SelectionSourceCollection();
                            sources.Add(doc.SelectionSets.CreateSelectionSource(setInDoc));
                            task.Selection.CopyFrom(sources);
                        }
                        catch (Exception ex) { warn = "attachement: " + ex.Message; }
                    }

                    tl.TaskAddCopy(task);

                    steps.Add(new ErectionStep
                    {
                        Step = i + 1,
                        Name = label,
                        Z = Math.Round(b.Z, 3),
                        Elements = b.Items.Count,
                        PlannedStart = from.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                        PlannedEnd = to.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                        Warning = warn
                    });
                }

                return new ErectionResult
                {
                    Folder = setFolder,
                    NameFilter = nameFilter,
                    TaskType = resolvedType,
                    ElementsFound = found.Count,
                    Bands = steps.Count,
                    BandTolerance = bandTolerance,
                    LowestZ = Math.Round(bands.First().Z, 3),
                    HighestZ = Math.Round(bands.Last().Z, 3),
                    TimelineStart = start.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    TimelineEnd = start.AddDays(bands.Count * daysPerStep).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    Steps = steps.ToArray()
                };
            });
    }

    public class ErectionResult
    {
        public string Folder { get; set; }
        public string NameFilter { get; set; }
        public string TaskType { get; set; }
        public int ElementsFound { get; set; }
        public int Bands { get; set; }
        public double BandTolerance { get; set; }
        public double LowestZ { get; set; }
        public double HighestZ { get; set; }
        public string TimelineStart { get; set; }
        public string TimelineEnd { get; set; }
        public ErectionStep[] Steps { get; set; }
        public string[] AvailableTaskTypes { get; set; }
        public string Error { get; set; }
    }

    public class ErectionStep
    {
        public int Step { get; set; }
        public string Name { get; set; }
        public double Z { get; set; }
        public int Elements { get; set; }
        public string PlannedStart { get; set; }
        public string PlannedEnd { get; set; }
        public string Warning { get; set; }
    }
}
