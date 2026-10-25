using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Navisworks.Api;
using NavisworksMcpPlugin.Mcp;

namespace NavisworksMcpPlugin.Tools
{
    [McpServerToolType]
    public static class DocumentTools
    {
        [McpServerTool, Description(
            "Get information about the currently open Navisworks document: file path, title, " +
            "units, number of federated models, and total root item count.")]
        public static Task<DocumentInfo> get_document_info() =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var models = doc.Models.ToList();

                return new DocumentInfo
                {
                    Title = doc.Title ?? "",
                    FileName = doc.FileName ?? "",
                    CurrentFileName = doc.CurrentFileName ?? "",
                    Units = doc.Units.ToString(),
                    ModelCount = models.Count,
                    ModelSources = models.Select(m => m.FileName ?? "").ToArray(),
                    RootItemCount = doc.Models.RootItemDescendantsAndSelf.Count()
                };
            });
    }

    public class DocumentInfo
    {
        public string Title { get; set; }
        public string FileName { get; set; }
        public string CurrentFileName { get; set; }
        public string Units { get; set; }
        public int ModelCount { get; set; }
        public string[] ModelSources { get; set; }
        public int RootItemCount { get; set; }
    }
}
