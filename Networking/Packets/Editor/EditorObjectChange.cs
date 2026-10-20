using UnityEngine;

internal readonly struct EditorObjectChange
{
    internal readonly Guid Id;
    internal readonly int Version;
    internal readonly LevelPart? Part;
    internal readonly Vector3 Scale;

    internal EditorObjectChange(Guid id, int version, LevelPart? part, Vector3? scale = null)
    {
        Id = id;
        Version = version;
        Part = part;
        Scale = scale ?? Vector3.one;
    }
}