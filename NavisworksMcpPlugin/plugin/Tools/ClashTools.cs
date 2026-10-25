using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using NavisworksMcpPlugin.Mcp;

namespace NavisworksMcpPlugin.Tools
{
    [McpServerToolType]
    public static class ClashTools
    {
        [McpServerTool, Description(
            "List all clash tests in the document with their name, status, and result counts " +
            "(new, active, resolved, approved). Use this to get an overview of clash detection results.")]
        public static Task<ClashTestListResult> list_clash_tests() =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                // TODO: vérifier signature dans la doc CHM — GetClash() vs DocumentClash.Current
                var clashDoc = doc.GetClash();
                var tests = clashDoc.TestsData.Tests;
                var results = new List<ClashTestDto>();

                foreach (ClashTest test in tests)
                {
                    int newCount = 0, activeCount = 0, resolvedCount = 0, approvedCount = 0, total = 0;
                    CountClashResults(test.Children, ref newCount, ref activeCount, ref resolvedCount, ref approvedCount, ref total);

                    results.Add(new ClashTestDto
                    {
                        Name = test.DisplayName ?? "",
                        Status = test.Status.ToString(),
                        NewCount = newCount,
                        ActiveCount = activeCount,
                        ResolvedCount = resolvedCount,
                        ApprovedCount = approvedCount,
                        TotalResults = total
                    });
                }

                return new ClashTestListResult { Count = results.Count, Tests = results.ToArray() };
            });

        [McpServerTool, Description(
            "Get detailed clash results for a specific clash test by name. Returns each clash " +
            "with item A name/GUID, item B name/GUID, distance, and status. " +
            "Use list_clash_tests first to find available test names.")]
        public static Task<ClashResultListResult> get_clash_results(
            [Description("Name of the clash test")] string testName,
            [Description("Maximum results to return (default 200)")] int maxResults = 200) =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var clashDoc = doc.GetClash();
                ClashTest target = null;

                foreach (ClashTest test in clashDoc.TestsData.Tests)
                {
                    if (test.DisplayName == testName.Trim())
                    {
                        target = test;
                        break;
                    }
                }

                if (target == null)
                    throw new System.ArgumentException($"Clash test not found: {testName}");

                var results = new List<ClashResultDto>();
                CollectClashResults(target.Children, results, maxResults);

                int totalCount = 0, n = 0, a = 0, r = 0, ap = 0;
                CountClashResults(target.Children, ref n, ref a, ref r, ref ap, ref totalCount);

                return new ClashResultListResult
                {
                    TestName = target.DisplayName ?? "",
                    TotalResults = totalCount,
                    ReturnedCount = results.Count,
                    Results = results.ToArray()
                };
            });

        private static void CountClashResults(
            SavedItemCollection children,
            ref int newCount, ref int activeCount, ref int resolvedCount, ref int approvedCount, ref int total)
        {
            foreach (var child in children)
            {
                if (child is ClashResult result)
                {
                    total++;
                    switch (result.Status)
                    {
                        case ClashResultStatus.New: newCount++; break;
                        case ClashResultStatus.Active: activeCount++; break;
                        case ClashResultStatus.Resolved: resolvedCount++; break;
                        case ClashResultStatus.Approved: approvedCount++; break;
                    }
                }
                else if (child is ClashResultGroup group)
                {
                    CountClashResults(group.Children, ref newCount, ref activeCount, ref resolvedCount, ref approvedCount, ref total);
                }
            }
        }

        private static void CollectClashResults(SavedItemCollection children, List<ClashResultDto> results, int maxResults)
        {
            foreach (var child in children)
            {
                if (results.Count >= maxResults) return;

                if (child is ClashResult result)
                {
                    results.Add(new ClashResultDto
                    {
                        Name = result.DisplayName ?? "",
                        Status = result.Status.ToString(),
                        Distance = result.Distance,
                        ItemAName = result.CompositeItemSelection1?.FirstOrDefault()?.DisplayName ?? "",
                        ItemAGuid = result.CompositeItemSelection1?.FirstOrDefault()?.InstanceGuid.ToString() ?? "",
                        ItemBName = result.CompositeItemSelection2?.FirstOrDefault()?.DisplayName ?? "",
                        ItemBGuid = result.CompositeItemSelection2?.FirstOrDefault()?.InstanceGuid.ToString() ?? ""
                    });
                }
                else if (child is ClashResultGroup group)
                {
                    CollectClashResults(group.Children, results, maxResults);
                }
            }
        }
    }

    public class ClashTestListResult
    {
        public int Count { get; set; }
        public ClashTestDto[] Tests { get; set; }
    }

    public class ClashTestDto
    {
        public string Name { get; set; }
        public string Status { get; set; }
        public int NewCount { get; set; }
        public int ActiveCount { get; set; }
        public int ResolvedCount { get; set; }
        public int ApprovedCount { get; set; }
        public int TotalResults { get; set; }
    }

    public class ClashResultListResult
    {
        public string TestName { get; set; }
        public int TotalResults { get; set; }
        public int ReturnedCount { get; set; }
        public ClashResultDto[] Results { get; set; }
    }

    public class ClashResultDto
    {
        public string Name { get; set; }
        public string Status { get; set; }
        public double Distance { get; set; }
        public string ItemAName { get; set; }
        public string ItemAGuid { get; set; }
        public string ItemBName { get; set; }
        public string ItemBGuid { get; set; }
    }
}
