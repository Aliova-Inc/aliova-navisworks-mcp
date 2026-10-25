using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Navisworks.Api;
using NavisworksMcpPlugin.Mcp;

namespace NavisworksMcpPlugin.Tools
{
    [McpServerToolType]
    public static class IsolationTools
    {
        [McpServerTool, Description(
            "Isolate specific items by their instance GUIDs — hides everything except the matching items and all their descendants. " +
            "Pass a comma-separated list of GUIDs. Use search or selection tools to obtain GUIDs first.")]
        public static Task<IsolateResult> isolate_items(
            [Description("Comma-separated instance GUIDs of items to keep visible")] string guids) =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var guidSet = new HashSet<string>(
                    guids.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(g => g.Trim()),
                    StringComparer.OrdinalIgnoreCase);

                var matches = doc.Models.RootItemDescendantsAndSelf
                    .Where(item => guidSet.Contains(item.InstanceGuid.ToString()))
                    .ToList();

                if (matches.Count == 0)
                    throw new ArgumentException("No items found for the provided GUIDs.");

                var toShow = new HashSet<ModelItem>();
                foreach (var m in matches)
                    foreach (var d in m.DescendantsAndSelf)
                        toShow.Add(d);
                foreach (var m in matches)
                {
                    var parent = m.Parent;
                    while (parent != null) { toShow.Add(parent); parent = parent.Parent; }
                }

                doc.Models.ResetAllHidden();
                var toHide = doc.Models.RootItemDescendantsAndSelf
                    .Where(i => !toShow.Contains(i)).ToList();
                doc.Models.SetHidden(toHide, true);

                return new IsolateResult { Visible = toShow.Count };
            });

        [McpServerTool, Description(
            "Isolate elements by searching a property value — same as Navisworks Find Items + Hide Unselected. " +
            "Uses the native Search API. Specify the property category, property name, operator, and value. " +
            "Example: category='Identity Data', property='Workset', op='Contains', value='PL_Alimentation'")]
        public static Task<IsolateResult> isolate_by_search(
            [Description("Property category/tab name (e.g. 'Identity Data', 'Données d\\'identification', 'Mechanical', 'Element')")] string category,
            [Description("Property display name (e.g. 'Workset', 'Sous-projet', 'System Type', 'Type Name')")] string property,
            [Description("Match operator: 'Contains' or 'Equals'")] string op,
            [Description("Comma-separated values to match (OR logic)")] string value) =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var values = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Select(v => v.Trim())
                                  .Where(v => v.Length > 0)
                                  .ToList();

                if (values.Count == 0)
                    throw new ArgumentException("At least one value is required.");

                var searches = new List<Search>();
                foreach (var val in values)
                {
                    var search = new Search();
                    search.Selection.SelectAll();
                    search.PruneBelowMatch = false;
                    var condition = SearchCondition.HasPropertyByDisplayName(category, property);
                    if (op?.ToLower() == "equals")
                        condition = condition.EqualValue(VariantData.FromDisplayString(val));
                    else
                        condition = condition.DisplayStringContains(val);
                    search.SearchConditions.Add(condition);
                    searches.Add(search);
                }

                return IsolateFromSearches(doc, searches);
            });

        [McpServerTool, Description(
            "Isolate elements by combining search filters AND/OR branch names (OR logic between all). " +
            "filters: semicolon-separated 'category|property|op|value'. " +
            "branches: optional comma-separated keywords to match top-level branch display names (case-insensitive). " +
            "Example filters: 'Electrical - Circuiting|Electrical Data|Contains|V' " +
            "Example branches: 'PRJ_ME_EL,EL_Éclairage,EL_Équipements'")]
        public static Task<IsolateResult> isolate_by_multi_search(
            [Description("Semicolon-separated filters, each as 'category|property|op|value'. Can be empty string if only using branches.")] string filters,
            [Description("Optional comma-separated keywords to match branch names in the model tree (case-insensitive, OR logic)")] string branches = "") =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;

                // Parse property filters
                var searches = new List<Search>();
                if (!string.IsNullOrWhiteSpace(filters))
                {
                    var filterList = filters.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(f => f.Split(new[] { '|' }, 4))
                        .Where(f => f.Length == 4)
                        .ToList();

                    foreach (var f in filterList)
                    {
                        string cat = f[0].Trim(), prop = f[1].Trim(), opStr = f[2].Trim(), val = f[3].Trim();
                        var search = new Search();
                        search.Selection.SelectAll();
                        search.PruneBelowMatch = false;
                        var condition = SearchCondition.HasPropertyByDisplayName(cat, prop);
                        if (opStr.ToLower() == "equals")
                            condition = condition.EqualValue(VariantData.FromDisplayString(val));
                        else
                            condition = condition.DisplayStringContains(val);
                        search.SearchConditions.Add(condition);
                        searches.Add(search);
                    }
                }

                // Parse branch keywords
                var branchKeywords = new List<string>();
                if (!string.IsNullOrWhiteSpace(branches))
                {
                    branchKeywords = branches.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(b => b.Trim().ToLowerInvariant())
                        .Where(b => b.Length > 0)
                        .ToList();
                }

                if (searches.Count == 0 && branchKeywords.Count == 0)
                    throw new ArgumentException("At least one filter or branch keyword is required.");

                return IsolateFromSearches(doc, searches, branchKeywords);
            });

        [McpServerTool, Description(
            "Show all items — cancels any active isolation or hiding. " +
            "Restores full model visibility.")]
        public static Task<OkResult> show_all() =>
            UiThread.Run(() =>
            {
                Application.ActiveDocument.Models.ResetAllHidden();
                return new OkResult { Ok = true };
            });

        /// <summary>
        /// Shared isolation logic: from search results, find which top-level tree branches
        /// contain matches, then hide the branches that don't. Fast for any result size
        /// because it only hides at branch level (~50 nodes), never per-element.
        /// </summary>
        /// <summary>
        /// Run searches and isolate by hiding non-matching top-level branches.
        /// Only samples up to 200 results per search to identify branches — never iterates all results.
        /// </summary>
        private static IsolateResult IsolateFromSearches(Document doc, List<Search> searches, List<string> branchKeywords = null)
        {
            var modelRoots = new HashSet<ModelItem>();
            foreach (var model in doc.Models)
                modelRoots.Add(model.RootItem);

            var matchedBranches = new HashSet<ModelItem>();
            int totalFound = 0;

            // 1. From search filters: sample results to identify branches
            foreach (var search in searches)
            {
                var results = search.FindAll(doc, false);
                totalFound += results.Count;

                int sampled = 0;
                foreach (ModelItem item in results)
                {
                    var current = item;
                    while (current.Parent != null)
                    {
                        if (modelRoots.Contains(current.Parent))
                        {
                            matchedBranches.Add(current);
                            break;
                        }
                        current = current.Parent;
                    }
                    sampled++;
                    if (sampled >= 200) break;
                }
            }

            // 2. From branch keywords: match model root names (= source file partitions)
            // In ACC federated models, each source file is its own Model with a descriptive RootItem name
            if (branchKeywords != null && branchKeywords.Count > 0)
            {
                foreach (var model in doc.Models)
                {
                    // Check the model root name (e.g. "PRJ_ME_EL_ENT.rvt - FEDEREE_COMPLET")
                    var rootName = model.RootItem.DisplayName?.ToLowerInvariant() ?? "";
                    bool rootMatches = branchKeywords.Any(kw => rootName.Contains(kw));

                    if (rootMatches)
                    {
                        // Keep all children of this model visible
                        foreach (var child in model.RootItem.Children)
                        {
                            matchedBranches.Add(child);
                            totalFound += child.Descendants.Count() + 1;
                        }
                    }
                    else
                    {
                        // Also check children names (for local NWC/NWD models)
                        foreach (var child in model.RootItem.Children)
                        {
                            var dn = child.DisplayName?.ToLowerInvariant() ?? "";
                            if (branchKeywords.Any(kw => dn.Contains(kw)))
                            {
                                matchedBranches.Add(child);
                                totalFound += child.Descendants.Count() + 1;
                            }
                        }
                    }
                }
            }

            if (matchedBranches.Count == 0)
                throw new ArgumentException("No elements found matching the provided filters or branches.");

            // Hide branches that don't match
            var toHide = new List<ModelItem>();
            foreach (var model in doc.Models)
                foreach (var branch in model.RootItem.Children)
                    if (!matchedBranches.Contains(branch))
                        toHide.Add(branch);

            doc.Models.ResetAllHidden();
            doc.Models.SetHidden(toHide, true);

            return new IsolateResult { Visible = totalFound, BranchesShown = matchedBranches.Count };
        }
    }

    public class IsolateResult
    {
        public int Visible { get; set; }
        public int BranchesShown { get; set; }
    }
}
