using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Navisworks.Api;
using NavisworksMcpPlugin.Mcp;

namespace NavisworksMcpPlugin.Tools
{
    [McpServerToolType]
    public static class SearchTools
    {
        [McpServerTool, Description(
            "Search for items by a property value. Specify the property category tab name " +
            "(e.g. 'Element', 'LcOaNode'), the property display name (e.g. 'Name', 'Type'), " +
            "the operator ('Contains' or 'Equals'), and the value to match. " +
            "Returns up to maxResults matching items (default 100).")]
        public static Task<SearchResult> search_by_property(
            [Description("Property category/tab name (e.g. 'Element', 'LcOaNode', 'Item')")] string category,
            [Description("Property display name within that category (e.g. 'Name', 'Type')")] string property,
            [Description("Match operator: 'Contains' or 'Equals'")] string op,
            [Description("Value to search for")] string value,
            [Description("Maximum results to return (default 100)")] int maxResults = 100) =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var search = new Search();
                search.Selection.SelectAll();
                search.PruneBelowMatch = false;

                var condition = SearchCondition.HasPropertyByDisplayName(category, property);
                if (op?.ToLower() == "equals")
                    condition = condition.EqualValue(VariantData.FromDisplayString(value));
                else
                    condition = condition.DisplayStringContains(value);

                search.SearchConditions.Add(condition);

                var results = search.FindAll(doc, false);
                var items = results.Take(maxResults).Select(ItemSummary.From).ToArray();

                return new SearchResult
                {
                    TotalFound = results.Count(),
                    ReturnedCount = items.Length,
                    Items = items
                };
            });

        [McpServerTool, Description(
            "Search for items by their display name. Finds items whose name contains the given text. " +
            "Useful for quick lookups by element name.")]
        public static Task<SearchResult> search_by_name(
            [Description("Text to search for in item display names")] string name,
            [Description("Maximum results to return (default 100)")] int maxResults = 100) =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var lowerName = name.ToLower();
                var matches = new List<ModelItem>();

                foreach (var item in doc.Models.RootItemDescendantsAndSelf)
                {
                    if (matches.Count >= maxResults) break;
                    if (item.DisplayName?.ToLower().Contains(lowerName) == true)
                        matches.Add(item);
                }

                return new SearchResult
                {
                    TotalFound = matches.Count,
                    ReturnedCount = matches.Count,
                    Items = matches.Select(ItemSummary.From).ToArray()
                };
            });

        [McpServerTool, Description(
            "Save a search as a Search Set in the Sets panel (reusable for clash detection, reports, etc.). " +
            "Creates the folder if it doesn't exist. One call = one search set. " +
            "Example: category='Identity Data', property='Workset', op='Contains', value='PL_Alimentation', name='PL_Alimentation', folder='Plomberie'")]
        public static Task<SaveSearchSetResult> save_search_set(
            [Description("Property category/tab name (e.g. 'Identity Data')")] string category,
            [Description("Property display name (e.g. 'Workset')")] string property,
            [Description("Match operator: 'Contains' or 'Equals'")] string op,
            [Description("Value to search for")] string value,
            [Description("Display name for the saved search set")] string name,
            [Description("Folder name in the Sets panel (created if missing)")] string folder) =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;

                // Build search
                var search = new Search();
                search.Selection.SelectAll();
                search.PruneBelowMatch = false;
                var condition = SearchCondition.HasPropertyByDisplayName(category, property);
                if (op?.ToLower() == "equals")
                    condition = condition.EqualValue(VariantData.FromDisplayString(value));
                else
                    condition = condition.DisplayStringContains(value);
                search.SearchConditions.Add(condition);

                // Find or create folder in Sets panel
                var root = doc.SelectionSets.RootItem;
                FolderItem targetFolder = null;
                for (int i = 0; i < root.Children.Count(); i++)
                {
                    var child = root.Children.ElementAt(i);
                    if (child is FolderItem f && f.DisplayName == folder)
                    {
                        targetFolder = f;
                        break;
                    }
                }
                if (targetFolder == null)
                {
                    var newFolder = new FolderItem { DisplayName = folder };
                    doc.SelectionSets.InsertCopy(root, root.Children.Count(), newFolder);
                    targetFolder = (FolderItem)root.Children.Last();
                }

                // Save as parametric search set (re-runs automatically, usable in clash tests)
                var selSet = new SelectionSet(search) { DisplayName = name };
                doc.SelectionSets.InsertCopy(targetFolder, targetFolder.Children.Count(), selSet);

                // Count current matches for feedback
                var results = search.FindAll(doc, false);
                return new SaveSearchSetResult
                {
                    Name = name,
                    Folder = folder,
                    MatchCount = results.Count()
                };
            });
    }

    public class SaveSearchSetResult
    {
        public string Name { get; set; }
        public string Folder { get; set; }
        public int MatchCount { get; set; }
    }

    public class SearchResult
    {
        public int TotalFound { get; set; }
        public int ReturnedCount { get; set; }
        public ItemSummary[] Items { get; set; }
    }
}
