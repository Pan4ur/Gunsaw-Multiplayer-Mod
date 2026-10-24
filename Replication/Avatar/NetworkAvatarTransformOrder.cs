using UnityEngine;

internal sealed class NetworkAvatarTransformOrder
{
    private readonly List<Transform> ordered = [];
    private readonly Dictionary<Transform, int> depths = new();
    private readonly Dictionary<Transform, Transform> parents = new();

    internal List<Transform> Update<T>(Dictionary<Transform, T> targets)
    {
        var changed = targets.Count != ordered.Count;
        if (!changed)
            if (targets.Keys.Any(target => !depths.ContainsKey(target)))
                changed = true;
        
        if (!changed)
            if (parents.Any(pair => pair.Key == null || pair.Key.parent != pair.Value))
                changed = true;
        
        if (!changed) 
            return ordered;

        Clear();
        foreach (var target in targets.Keys)
        {
            var depth = 0;
            var current = target;
            while (current != null)
            {
                if (!parents.TryGetValue(current, out var parent))
                {
                    parent = current.parent;
                    parents.Add(current, parent);
                }
                depth++;
                current = parent;
            }
            depths.Add(target, depth);
            ordered.Add(target);
        }
        ordered.Sort((left, right) => depths[left].CompareTo(depths[right]));
        return ordered;
    }

    internal void Clear()
    {
        ordered.Clear();
        depths.Clear();
        parents.Clear();
    }
}