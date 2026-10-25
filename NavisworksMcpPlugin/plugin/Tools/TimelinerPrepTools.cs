using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Navisworks.Api;
using NavisworksMcpPlugin.Mcp;

namespace NavisworksMcpPlugin.Tools
{
    /// <summary>
    /// Préparation du 4D TimeLiner : lecture des phases Revit (propriétés NamedConstant, non
    /// cherchables par search_by_property) et création des jeux de sélection par phase que
    /// TimeLiner consommera (un set = les éléments créés / démolis à une phase donnée).
    /// La phase est résolue EN CODE (item.PropertyCategories) puis les items sont regroupés en
    /// sets explicites — cela évite la limitation du VariantData.FromDisplayString sur NamedConstant.
    /// </summary>
    [McpServerToolType]
    public static class TimelinerPrepTools
    {
        // Résout la valeur affichée d'une propriété (par noms de catégorie/propriété affichés).
        private static string PhaseValue(ModelItem item, string category, string property)
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

        // Vrai si le parent porte la même valeur de phase (=> item n'est pas le noeud le plus haut).
        private static bool ParentHasSamePhase(ModelItem item, string category, string property, string value)
        {
            var parent = item.Parent;
            if (parent == null) return false;
            return PhaseValue(parent, category, property) == value;
        }

        internal static FolderItem FindOrCreateFolder(Document doc, string folder)
        {
            var root = doc.SelectionSets.RootItem;
            for (int i = 0; i < root.Children.Count(); i++)
            {
                if (root.Children.ElementAt(i) is FolderItem f && f.DisplayName == folder)
                    return f;
            }
            var newFolder = new FolderItem { DisplayName = folder };
            doc.SelectionSets.InsertCopy(root, root.Children.Count(), newFolder);
            // Re-scan par nom (ne pas se fier à .Last() : la collection peut ne pas refléter
            // l'insertion de façon ordonnée, surtout quand il n'y avait aucun set au départ).
            for (int i = 0; i < root.Children.Count(); i++)
                if (root.Children.ElementAt(i) is FolderItem nf && nf.DisplayName == folder)
                    return nf;
            return null;
        }

        [McpServerTool(ReadOnly = true), Description(
            "Compte les éléments par phase (lecture seule) — pour vérifier avant de créer les sets. " +
            "Résout la propriété de phase Revit (NamedConstant) que search_by_property ne sait pas matcher. " +
            "Défauts pour un modèle FR : category='Phase de création', property='Nom'. " +
            "Pour la démolition : category='Phase de démolition'.")]
        public static Task<PhaseCountResult> count_by_phase(
            [Description("Catégorie de propriété affichée (def 'Phase de création')")] string category = "Phase de création",
            [Description("Propriété affichée dans cette catégorie (def 'Nom')")] string property = "Nom") =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var counts = new Dictionary<string, int>();
                foreach (var item in doc.Models.RootItemDescendantsAndSelf)
                {
                    var v = PhaseValue(item, category, property);
                    if (v == null) continue;
                    if (ParentHasSamePhase(item, category, property, v)) continue; // noeud le plus haut only
                    counts[v] = counts.TryGetValue(v, out var c) ? c + 1 : 1;
                }
                return new PhaseCountResult
                {
                    Category = category,
                    Property = property,
                    Phases = counts.OrderBy(k => k.Key)
                        .Select(k => new PhaseCount { Phase = k.Key, Count = k.Value }).ToArray()
                };
            });

        [McpServerTool, Description(
            "Crée en un appel tous les jeux de sélection par phase pour le 4D TimeLiner : " +
            "un set 'CREE - <phase>' par phase de création (éléments qui APPARAISSENT) et " +
            "un set 'DEMO - <phase>' par phase de démolition (éléments qui DISPARAISSENT), " +
            "rangés dans un dossier des Sets. Regroupe les éléments par phase en lisant la propriété " +
            "NamedConstant en code (contourne la limite de search_by_property). " +
            "Relançable : videz d'abord le dossier avec clear_sets_folder pour régénérer proprement.")]
        public static Task<BuildSetsResult> build_phase_search_sets(
            [Description("Dossier cible dans le panneau Sets (def '4D')")] string folder = "4D",
            [Description("Catégorie de la phase de création (def 'Phase de création')")] string createdCategory = "Phase de création",
            [Description("Propriété de la phase de création (def 'Nom')")] string createdProperty = "Nom",
            [Description("Catégorie de la phase de démolition (def 'Phase de démolition')")] string demolishedCategory = "Phase de démolition",
            [Description("Propriété de la phase de démolition (def 'Nom')")] string demolishedProperty = "Nom",
            [Description("Préfixe des sets de création (def 'CREE - ')")] string creePrefix = "CREE - ",
            [Description("Préfixe des sets de démolition (def 'DEMO - ')")] string demoPrefix = "DEMO - ") =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var refs = BuildPhaseSets(doc, folder, createdCategory, createdProperty,
                    demolishedCategory, demolishedProperty, creePrefix, demoPrefix, out var error);
                if (error != null)
                    return new BuildSetsResult { Folder = folder, SetsCreated = 0, Sets = new PhaseSetInfo[0], Error = error };

                return new BuildSetsResult
                {
                    Folder = folder,
                    SetsCreated = refs.Count,
                    Sets = refs.Select(r => new PhaseSetInfo { Name = r.Name, Kind = r.Kind, Count = r.Count }).ToArray()
                };
            });

        /// <summary>
        /// Regroupe les éléments par phase (création ET démolition) en UN SEUL parcours du modèle,
        /// puis crée les sets correspondants dans <paramref name="folder"/>.
        /// Retourne les sets AVEC leur SavedItem résolu dans le document (InsertCopy insère une COPIE :
        /// l'objet construit ici n'est pas celui du document, il faut le relire par nom pour pouvoir
        /// en faire un SelectionSource attachable à une tâche TimeLiner).
        /// Appelé par build_phase_search_sets et par timeliner_build_from_phases.
        /// </summary>
        internal static List<PhaseSetRef> BuildPhaseSets(
            Document doc, string folder,
            string createdCategory, string createdProperty,
            string demolishedCategory, string demolishedProperty,
            string creePrefix, string demoPrefix, out string error)
        {
            error = null;
            var created = new Dictionary<string, ModelItemCollection>();
            var demolished = new Dictionary<string, ModelItemCollection>();

            foreach (var item in doc.Models.RootItemDescendantsAndSelf)
            {
                var cv = PhaseValue(item, createdCategory, createdProperty);
                if (cv != null && !ParentHasSamePhase(item, createdCategory, createdProperty, cv))
                {
                    if (!created.TryGetValue(cv, out var coll)) { coll = new ModelItemCollection(); created[cv] = coll; }
                    coll.Add(item);
                }
                var dv = PhaseValue(item, demolishedCategory, demolishedProperty);
                if (dv != null && !ParentHasSamePhase(item, demolishedCategory, demolishedProperty, dv))
                {
                    if (!demolished.TryGetValue(dv, out var coll)) { coll = new ModelItemCollection(); demolished[dv] = coll; }
                    coll.Add(item);
                }
            }

            var folderItem = FindOrCreateFolder(doc, folder);
            if (folderItem == null)
            {
                error = "Dossier introuvable/non créé: " + folder;
                return new List<PhaseSetRef>();
            }

            var refs = new List<PhaseSetRef>();
            void Emit(string name, string kind, string phase, ModelItemCollection items)
            {
                var set = new SelectionSet(items) { DisplayName = name };
                doc.SelectionSets.InsertCopy(folderItem, folderItem.Children.Count(), set);
                refs.Add(new PhaseSetRef
                {
                    Name = name,
                    Kind = kind,
                    Phase = phase,
                    Count = items.Count,
                    Item = FindSetInFolder(folderItem, name)
                });
            }

            foreach (var kv in created.OrderBy(k => k.Key))
                Emit(creePrefix + kv.Key, "Construct", kv.Key, kv.Value);
            foreach (var kv in demolished.OrderBy(k => k.Key))
                Emit(demoPrefix + kv.Key, "Demolish", kv.Key, kv.Value);

            return refs;
        }

        /// <summary>Relit un set par son nom affiché dans un dossier de Sets (après InsertCopy).</summary>
        internal static SavedItem FindSetInFolder(FolderItem folderItem, string name)
        {
            for (int i = 0; i < folderItem.Children.Count(); i++)
            {
                var child = folderItem.Children.ElementAt(i);
                if (child is SelectionSet s && s.DisplayName == name) return s;
            }
            return null;
        }

        /// <summary>
        /// Recense les sets déjà présents dans un dossier, indexés par nom affiché.
        /// Sert au chemin « ne pas régénérer les sets » de timeliner_build_from_phases.
        /// </summary>
        internal static Dictionary<string, SavedItem> ResolveSetsInFolder(Document doc, string folder)
        {
            var map = new Dictionary<string, SavedItem>();
            var root = doc.SelectionSets.RootItem;
            for (int i = 0; i < root.Children.Count(); i++)
            {
                if (!(root.Children.ElementAt(i) is FolderItem f) || f.DisplayName != folder) continue;
                for (int j = 0; j < f.Children.Count(); j++)
                    if (f.Children.ElementAt(j) is SelectionSet s && !map.ContainsKey(s.DisplayName))
                        map[s.DisplayName] = s;
            }
            return map;
        }

        [McpServerTool(ReadOnly = true), Description(
            "Liste les jeux de sélection existants (nom, dossier, type). Utile pour vérifier avant TimeLiner.")]
        public static Task<ListSetsResult> list_selection_sets() =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var found = new List<SetEntry>();
                void Walk(SavedItem item, string path)
                {
                    if (item is FolderItem f)
                    {
                        foreach (var child in f.Children)
                            Walk(child, string.IsNullOrEmpty(path) ? f.DisplayName : path);
                    }
                    else if (item is SelectionSet s)
                    {
                        int count = -1;
                        try { count = s.GetSelectedItems().Count(); } catch { }
                        found.Add(new SetEntry { Name = s.DisplayName, Folder = path, Count = count });
                    }
                }
                foreach (var child in doc.SelectionSets.RootItem.Children)
                    Walk(child, "");
                return new ListSetsResult { Count = found.Count, Sets = found.ToArray() };
            });

        [McpServerTool, Description(
            "Vide un dossier de Sets (supprime tous les sets qu'il contient) pour régénérer proprement. " +
            "À appeler avant build_phase_search_sets pour éviter les doublons.")]
        public static Task<ClearResult> clear_sets_folder(
            [Description("Nom du dossier à vider (def '4D')")] string folder = "4D") =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                int removed = ClearSetsFolder(doc, folder, out var existed);
                return new ClearResult { Folder = folder, Removed = removed, Existed = existed };
            });

        /// <summary>
        /// Vide un dossier de Sets. Retourne le nombre de sets retirés ; <paramref name="existed"/>
        /// dit si le dossier était présent. Appelé par clear_sets_folder et par le chemin
        /// « régénérer les sets » de timeliner_build_from_phases.
        /// </summary>
        internal static int ClearSetsFolder(Document doc, string folder, out bool existed)
        {
            var root = doc.SelectionSets.RootItem;
            FolderItem target = null;
            for (int i = 0; i < root.Children.Count(); i++)
                if (root.Children.ElementAt(i) is FolderItem f && f.DisplayName == folder) { target = f; break; }
            if (target == null) { existed = false; return 0; }

            existed = true;
            int removed = 0;
            while (target.Children.Count() > 0)
            {
                doc.SelectionSets.Remove(target, target.Children.ElementAt(0));
                removed++;
            }
            return removed;
        }
    }

    public class PhaseCountResult
    {
        public string Category { get; set; }
        public string Property { get; set; }
        public PhaseCount[] Phases { get; set; }
    }
    public class PhaseCount { public string Phase { get; set; } public int Count { get; set; } }

    public class BuildSetsResult
    {
        public string Folder { get; set; }
        public int SetsCreated { get; set; }
        public PhaseSetInfo[] Sets { get; set; }
        public string Error { get; set; }
    }
    public class PhaseSetInfo { public string Name { get; set; } public string Kind { get; set; } public int Count { get; set; } }

    /// <summary>Set de phase créé, avec le SavedItem résolu dans le document (usage interne).</summary>
    internal class PhaseSetRef
    {
        public string Name;
        public string Kind;
        public string Phase;
        public int Count;
        public SavedItem Item;
    }

    public class ListSetsResult { public int Count { get; set; } public SetEntry[] Sets { get; set; } }
    public class SetEntry { public string Name { get; set; } public string Folder { get; set; } public int Count { get; set; } }

    public class ClearResult { public string Folder { get; set; } public int Removed { get; set; } public bool Existed { get; set; } }
}
