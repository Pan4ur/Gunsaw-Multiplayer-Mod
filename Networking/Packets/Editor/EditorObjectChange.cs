internal readonly struct EditorObjectChange
{
    internal readonly Guid Id;
    internal readonly int Version;
    internal readonly LevelPart? Part;

    internal EditorObjectChange(Guid id, int version, LevelPart? part)
    {
        Id = id;
        Version = version;
        Part = part;
    }
}