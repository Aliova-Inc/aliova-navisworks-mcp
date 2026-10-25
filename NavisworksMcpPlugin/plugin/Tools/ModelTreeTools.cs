using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Navisworks.Api;
using NavisworksMcpPlugin.Mcp;

namespace NavisworksMcpPlugin.Tools
{
    [McpServerToolType]
    public static class ModelTreeTools
    {
        [McpServerTool, Description(
            "List all federated models/partitions in the document with their display name, source file, " +
            "element count, and bounding box. Essential to know the exact model names before isolate_by_multi_search (branches) or hide_by_model.")]
        public static Task<ModelListResult> list_models() =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var models = new List<ModelDto>();

                foreach (var model in doc.Models)
                {
                    var root = model.RootItem;
                    // Collect partitions (direct children or the root itself)
                    var partitions = root.Children.ToList();
                    if (partitions.Count == 0)
                    {
                        // Single-model file — treat root as the partition
                        models.Add(ModelDtoFrom(root, model.FileName));
                    }
                    else
                    {
                        foreach (var part in partitions)
                        {
                            models.Add(ModelDtoFrom(part, model.FileName));
                        }
                    }
                }

                return new ModelListResult
                {
                    TotalModels = models.Count,
                    Models = models.ToArray()
                };
            });

        [McpServerTool, Description(
            "Get the hierarchical tree structure of a model/partition up to a specified depth. " +
            "Pass a keyword to match the partition name, and a depth (default 3). " +
            "Returns the tree with element counts at each level. Useful to understand model organization (categories, levels, types).")]
        public static Task<TreeResult> get_model_tree(
            [Description("Keyword to match partition display name (case-insensitive)")] string keyword,
            [Description("Maximum depth to traverse (default 3, max 6)")] int depth = 3) =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var kw = keyword.Trim().ToLowerInvariant();
                if (depth < 1) depth = 1;
                if (depth > 6) depth = 6;

                ModelItem target = null;
                foreach (var model in doc.Models)
                {
                    foreach (var item in model.RootItem.DescendantsAndSelf)
                    {
                        var dn = item.DisplayName?.ToLowerInvariant() ?? "";
                        if (dn.Contains(kw))
                        {
                            target = item;
                            break;
                        }
                    }
                    if (target != null) break;
                }

                if (target == null)
                    throw new System.ArgumentException($"No model/partition found matching '{keyword}'");

                var tree = BuildNode(target, 0, depth);
                return new TreeResult { Root = tree };
            });

        private static TreeNodeDto BuildNode(ModelItem item, int currentDepth, int maxDepth)
        {
            var node = new TreeNodeDto
            {
                Name = item.DisplayName ?? "",
                ClassName = item.ClassDisplayName ?? item.ClassName ?? "",
                ChildCount = item.Children.Count(),
                DescendantCount = item.Descendants.Count(),
                Guid = item.InstanceGuid.ToString()
            };

            if (currentDepth < maxDepth && item.Children.Any())
            {
                var children = new List<TreeNodeDto>();
                foreach (var child in item.Children)
                {
                    children.Add(BuildNode(child, currentDepth + 1, maxDepth));
                }
                node.Children = children.ToArray();
            }

            return node;
        }

        private static ModelDto ModelDtoFrom(ModelItem item, string sourceFile)
        {
            var bb = item.BoundingBox();
            return new ModelDto
            {
                Name = item.DisplayName ?? "",
                ClassName = item.ClassDisplayName ?? item.ClassName ?? "",
                SourceFile = sourceFile ?? "",
                ElementCount = item.Descendants.Count(),
                Guid = item.InstanceGuid.ToString(),
                BoundingBox = bb != null
                    ? $"({bb.Min.X:F2},{bb.Min.Y:F2},{bb.Min.Z:F2})-({bb.Max.X:F2},{bb.Max.Y:F2},{bb.Max.Z:F2})"
                    : ""
            };
        }
    }

    public class ModelListResult
    {
        public int TotalModels { get; set; }
        public ModelDto[] Models { get; set; }
    }

    public class ModelDto
    {
        public string Name { get; set; }
        public string ClassName { get; set; }
        public string SourceFile { get; set; }
        public int ElementCount { get; set; }
        public string Guid { get; set; }
        public string BoundingBox { get; set; }
    }

    public class TreeResult
    {
        public TreeNodeDto Root { get; set; }
    }

    public class TreeNodeDto
    {
        public string Name { get; set; }
        public string ClassName { get; set; }
        public int ChildCount { get; set; }
        public int DescendantCount { get; set; }
        public string Guid { get; set; }
        public TreeNodeDto[] Children { get; set; }
    }
}
