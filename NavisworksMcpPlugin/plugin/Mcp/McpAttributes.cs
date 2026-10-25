using System;

namespace NavisworksMcpPlugin.Mcp
{
    /// <summary>Marks a static class as containing MCP tool methods.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class McpServerToolTypeAttribute : Attribute { }

    /// <summary>Marks a static method as an MCP-invocable tool.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class McpServerToolAttribute : Attribute
    {
        public bool ReadOnly { get; set; }
    }
}
