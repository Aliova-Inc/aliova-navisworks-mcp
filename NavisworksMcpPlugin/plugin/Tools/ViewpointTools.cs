using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Autodesk.Navisworks.Api;
using NavisworksMcpPlugin.Mcp;

namespace NavisworksMcpPlugin.Tools
{
    [McpServerToolType]
    public static class ViewpointTools
    {
        [McpServerTool, Description(
            "List all saved viewpoints in the document, including their hierarchical path. " +
            "Use the path with goto_viewpoint to navigate to a specific view.")]
        public static Task<ViewpointListResult> list_viewpoints() =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var vpList = new List<ViewpointDto>();
                CollectViewpoints(doc.SavedViewpoints.Value, "", vpList);
                return new ViewpointListResult { Count = vpList.Count, Viewpoints = vpList.ToArray() };
            });

        [McpServerTool, Description(
            "Navigate to a saved viewpoint by its hierarchical path (e.g. 'Folder/SubFolder/ViewName'). " +
            "Use list_viewpoints to discover available paths.")]
        public static Task<OkResult> goto_viewpoint(
            [Description("Full hierarchical path of the viewpoint")] string path) =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var target = FindViewpoint(doc.SavedViewpoints.Value, path.Trim());

                if (target == null)
                    throw new System.ArgumentException($"Viewpoint not found: {path}");

                doc.CurrentViewpoint.CopyFrom(target.Viewpoint);
                doc.SavedViewpoints.CurrentSavedViewpoint = target;
                return new OkResult { Ok = true };
            });

        [McpServerTool, Description(
            "Save the current camera position as a new named viewpoint at the root level. " +
            "Returns the path of the saved viewpoint.")]
        public static Task<SavedViewpointResult> save_current_viewpoint(
            [Description("Name for the new viewpoint")] string name) =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var vp = doc.CurrentViewpoint.Value.CreateCopy();
                var savedItem = new SavedViewpoint(vp) { DisplayName = name };
                doc.SavedViewpoints.AddCopy(savedItem);
                return new SavedViewpointResult { Path = name };
            });

        private static void CollectViewpoints(
            SavedItemCollection items, string parentPath, List<ViewpointDto> results)
        {
            foreach (SavedItem item in items)
            {
                var currentPath = string.IsNullOrEmpty(parentPath)
                    ? item.DisplayName
                    : $"{parentPath}/{item.DisplayName}";

                if (item is SavedViewpoint)
                {
                    results.Add(new ViewpointDto
                    {
                        Name = item.DisplayName ?? "",
                        Path = currentPath,
                        IsFolder = false
                    });
                }
                else if (item is FolderItem folder)
                {
                    results.Add(new ViewpointDto
                    {
                        Name = item.DisplayName ?? "",
                        Path = currentPath,
                        IsFolder = true
                    });
                    CollectViewpoints(folder.Children, currentPath, results);
                }
            }
        }

        private static SavedViewpoint FindViewpoint(SavedItemCollection items, string path)
        {
            foreach (SavedItem item in items)
            {
                if (item is SavedViewpoint vp &&
                    string.Equals(path, item.DisplayName, System.StringComparison.OrdinalIgnoreCase))
                    return vp;

                if (item is FolderItem folder)
                {
                    var result = FindInFolder(folder, item.DisplayName, path);
                    if (result != null) return result;
                }
            }
            return null;
        }

        private static SavedViewpoint FindInFolder(FolderItem folder, string folderPath, string targetPath)
        {
            foreach (SavedItem item in folder.Children)
            {
                var currentPath = $"{folderPath}/{item.DisplayName}";

                if (item is SavedViewpoint vp &&
                    string.Equals(targetPath, currentPath, System.StringComparison.OrdinalIgnoreCase))
                    return vp;

                if (item is FolderItem sub)
                {
                    var result = FindInFolder(sub, currentPath, targetPath);
                    if (result != null) return result;
                }
            }
            return null;
        }
    }

    public class ViewpointListResult
    {
        public int Count { get; set; }
        public ViewpointDto[] Viewpoints { get; set; }
    }

    public class ViewpointDto
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public bool IsFolder { get; set; }
    }

    public class SavedViewpointResult
    {
        public string Path { get; set; }
    }
}
