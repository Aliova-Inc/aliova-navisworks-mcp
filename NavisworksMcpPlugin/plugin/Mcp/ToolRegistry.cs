using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace NavisworksMcpPlugin.Mcp
{
    /// <summary>
    /// Discovers [McpServerToolType] classes and [McpServerTool] methods via reflection,
    /// builds JSON Schema for parameters, and invokes tools by name.
    /// </summary>
    public class ToolRegistry
    {
        private readonly Dictionary<string, ToolEntry> _tools = new Dictionary<string, ToolEntry>(StringComparer.OrdinalIgnoreCase);

        public void DiscoverTools(Assembly assembly)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.GetCustomAttribute<McpServerToolTypeAttribute>() == null) continue;

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (method.GetCustomAttribute<McpServerToolAttribute>() == null) continue;

                    var desc = method.GetCustomAttribute<DescriptionAttribute>();
                    var entry = new ToolEntry
                    {
                        Name = method.Name,
                        Description = desc?.Description ?? "",
                        Method = method,
                        Parameters = method.GetParameters()
                    };
                    _tools[entry.Name] = entry;
                }
            }
        }

        public List<object> ListTools()
        {
            var result = new List<object>();
            foreach (var tool in _tools.Values.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(new
                {
                    name = tool.Name,
                    description = tool.Description,
                    inputSchema = BuildInputSchema(tool)
                });
            }
            return result;
        }

        public async Task<object> InvokeTool(string name, JsonElement? arguments)
        {
            if (!_tools.TryGetValue(name, out var tool))
                throw new ArgumentException($"Unknown tool: {name}");

            var args = new object[tool.Parameters.Length];
            for (int i = 0; i < tool.Parameters.Length; i++)
            {
                var param = tool.Parameters[i];
                if (arguments.HasValue && arguments.Value.TryGetProperty(param.Name, out var val))
                {
                    args[i] = ConvertArgument(val, param.ParameterType);
                }
                else if (param.HasDefaultValue)
                {
                    args[i] = param.DefaultValue;
                }
                else
                {
                    throw new ArgumentException($"Missing required parameter: {param.Name}");
                }
            }

            var result = tool.Method.Invoke(null, args);

            // Unwrap Task<T>
            if (result is Task task)
            {
                await task.ConfigureAwait(false);
                var taskType = task.GetType();
                if (taskType.IsGenericType)
                {
                    return taskType.GetProperty("Result").GetValue(task);
                }
                return null;
            }

            return result;
        }

        private static object ConvertArgument(JsonElement element, Type targetType)
        {
            if (targetType == typeof(string))
                return element.GetString();
            if (targetType == typeof(int))
                return element.GetInt32();
            if (targetType == typeof(double))
                return element.GetDouble();
            if (targetType == typeof(bool))
                return element.GetBoolean();
            if (targetType == typeof(long))
                return element.GetInt64();

            return JsonSerializer.Deserialize(element.GetRawText(), targetType);
        }

        private static object BuildInputSchema(ToolEntry tool)
        {
            if (tool.Parameters.Length == 0)
            {
                return new
                {
                    type = "object",
                    properties = new Dictionary<string, object>(),
                    required = Array.Empty<string>()
                };
            }

            var properties = new Dictionary<string, object>();
            var required = new List<string>();

            foreach (var param in tool.Parameters)
            {
                var desc = param.GetCustomAttribute<DescriptionAttribute>();
                var prop = BuildPropertySchema(param.ParameterType);
                if (desc != null)
                    prop["description"] = desc.Description;

                if (param.HasDefaultValue && param.DefaultValue != null)
                    prop["default"] = param.DefaultValue;

                properties[param.Name] = prop;

                if (!param.HasDefaultValue)
                    required.Add(param.Name);
            }

            return new
            {
                type = "object",
                properties,
                required = required.ToArray()
            };
        }

        private static Dictionary<string, object> BuildPropertySchema(Type t)
        {
            var type = Nullable.GetUnderlyingType(t) ?? t;

            if (type.IsArray)
            {
                return new Dictionary<string, object>
                {
                    ["type"] = "array",
                    ["items"] = new Dictionary<string, object>
                    {
                        ["type"] = JsonTypeFor(type.GetElementType())
                    }
                };
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                return new Dictionary<string, object>
                {
                    ["type"] = "array",
                    ["items"] = new Dictionary<string, object>
                    {
                        ["type"] = JsonTypeFor(type.GetGenericArguments()[0])
                    }
                };
            }

            return new Dictionary<string, object>
            {
                ["type"] = JsonTypeFor(type)
            };
        }

        private static string JsonTypeFor(Type t)
        {
            t = Nullable.GetUnderlyingType(t) ?? t;
            if (t == typeof(string)) return "string";
            if (t == typeof(int) || t == typeof(long)) return "integer";
            if (t == typeof(double) || t == typeof(float)) return "number";
            if (t == typeof(bool)) return "boolean";
            return "string";
        }

        private class ToolEntry
        {
            public string Name;
            public string Description;
            public MethodInfo Method;
            public ParameterInfo[] Parameters;
        }
    }
}
