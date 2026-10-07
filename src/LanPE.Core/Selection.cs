namespace LanPE.Core;

/// <summary>用户在 UI 上的选择：哪些组件、哪些软件、哪些选项。</summary>
public sealed class Selection
{
    public sealed class ComponentSelection
    {
        /// <summary>组件 id。</summary>
        public string ComponentId { get; set; } = "";
        public bool Selected { get; set; }

        /// <summary>已勾选的软件 id 集合。</summary>
        public HashSet<string> SoftwareIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>选项值：id -> 字符串值。</summary>
        public Dictionary<string, string> Options { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public bool IsSoftwareSelected(string? id) => id != null && SoftwareIds.Contains(id);

        public string GetOption(string id, string? fallback = null) =>
            Options.TryGetValue(id, out var v) ? v : fallback ?? "";
    }

    public List<ComponentSelection> Components { get; } = new();

    public ComponentSelection For(string? id)
    {
        if (id != null)
            foreach (var c in Components)
                if (string.Equals(c.ComponentId, id, StringComparison.OrdinalIgnoreCase))
                    return c;

        var created = new ComponentSelection { ComponentId = id ?? "" };
        Components.Add(created);
        return created;
    }

    public IEnumerable<ComponentSelection> SelectedComponents()
    {
        foreach (var c in Components)
            if (c.Selected) yield return c;
    }
}
