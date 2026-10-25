using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Navisworks.Api;
using NavisworksMcpPlugin.Mcp;

namespace NavisworksMcpPlugin.Tools
{
    [McpServerToolType]
    public static class PropertyTools
    {
        [McpServerTool, Description(
            "Get all properties of an item identified by its instance GUID. " +
            "Returns a dictionary grouped by property category, with each property's " +
            "display name, value, and data type. Use get_selection or search_by_property " +
            "to obtain GUIDs first.")]
        public static Task<ItemPropertiesResult> get_item_properties(
            [Description("Instance GUID of the item")] string guid) =>
            UiThread.Run(() =>
            {
                var doc = Application.ActiveDocument;
                var item = doc.Models.RootItemDescendantsAndSelf
                    .FirstOrDefault(i => i.InstanceGuid.ToString() == guid.Trim());

                if (item == null)
                    return new ItemPropertiesResult
                    {
                        Found = false,
                        DisplayName = "",
                        Categories = new PropertyCategoryDto[0]
                    };

                var categories = new List<PropertyCategoryDto>();

                foreach (var cat in item.PropertyCategories)
                {
                    var props = new List<PropertyDto>();
                    foreach (var prop in cat.Properties)
                    {
                        string val = "";
                        string dt = "";
                        try
                        {
                            dt = prop.Value?.DataType.ToString() ?? "";
                            val = prop.Value?.ToDisplayString() ?? "";
                        }
                        catch
                        {
                            try { val = prop.Value?.ToString() ?? "(unreadable)"; }
                            catch { val = "(unreadable)"; }
                        }
                        props.Add(new PropertyDto
                        {
                            Name = prop.DisplayName ?? "",
                            Value = val,
                            DataType = dt
                        });
                    }

                    categories.Add(new PropertyCategoryDto
                    {
                        CategoryName = cat.DisplayName ?? "",
                        Properties = props.ToArray()
                    });
                }

                return new ItemPropertiesResult
                {
                    Found = true,
                    DisplayName = item.DisplayName ?? "",
                    Categories = categories.ToArray()
                };
            });
    }

    public class ItemPropertiesResult
    {
        public bool Found { get; set; }
        public string DisplayName { get; set; }
        public PropertyCategoryDto[] Categories { get; set; }
    }

    public class PropertyCategoryDto
    {
        public string CategoryName { get; set; }
        public PropertyDto[] Properties { get; set; }
    }

    public class PropertyDto
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string DataType { get; set; }
    }
}
