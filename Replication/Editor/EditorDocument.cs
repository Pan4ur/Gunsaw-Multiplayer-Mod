using System.Security.Cryptography;

internal sealed class EditorDocument
{
    internal Guid Session = Guid.NewGuid();
    internal int Revision;
    internal bool Locked = true;
    internal Level Environment = new();
    internal readonly Dictionary<Guid, EditorObjectChange> Objects = new();

    internal bool CanApply(EditorEditPacket edit)
    {
        if (edit.Session != Session || Locked || edit.Environment != null && edit.BaseRevision != Revision) 
            return false;
        
        foreach (var change in edit.Changes)
        {
            var exists = Objects.TryGetValue(change.Id, out var current);
            if (exists ? change.Version != current.Version : change.Version != 0 || change.Part == null) 
                return false;
        }
        return true;
    }

    internal void Apply(EditorCommitPacket commit)
    {
        Revision = commit.Revision;
        Locked = commit.Locked;
        
        if (commit.Environment != null) 
            Environment = CopyEnvironment(commit.Environment);
        
        foreach (var change in commit.Changes)
        {
            if (change.Part == null) 
                Objects.Remove(change.Id);
            else 
                Objects[change.Id] = CopyChange(change);
        }
    }

    internal EditorSnapshotPacket Snapshot(int epoch, int request = 0) => new(epoch, Session, Revision, request, Locked, Checksum(), CopyEnvironment(Environment), Objects.Values.OrderBy(value => value.Id).Select(CopyChange).ToArray());

    internal void Restore(EditorSnapshotPacket snapshot)
    {
        Session = snapshot.Session;
        Revision = snapshot.Revision;
        Locked = snapshot.Locked;
        Environment = CopyEnvironment(snapshot.Environment);
        Objects.Clear();
        foreach (var change in snapshot.Changes) 
            Objects.Add(change.Id, CopyChange(change));
    }

    internal string Checksum()
    {
        var writer = new PacketWriter(256);
        writer.WriteBytes(Session.ToByteArray());
        writer.WriteInt32(Revision);
        writer.WriteBoolean(Locked);
        EditorPacketData.WriteEnvironment(ref writer, Environment);
        EditorPacketData.WriteChanges(ref writer, Objects.Values.OrderBy(value => value.Id).ToArray());
        using var hash = SHA256.Create();
        return Convert.ToBase64String(hash.ComputeHash(writer.ToArray()));
    }

    internal EditorDocument BasisFor(EditorEditPacket edit)
    {
        var basis = new EditorDocument
        {
            Session = Session, Revision = Revision, Locked = Locked, Environment = CopyEnvironment(Environment)
        };
       
        foreach (var change in edit.Changes)
            if (Objects.TryGetValue(change.Id, out var current))
                basis.Objects.Add(change.Id, CopyChange(current));
        
        return basis;
    }

    internal (EditorEditPacket Edit, bool Conflicts) Rebase(EditorEditPacket local, EditorDocument target, EditorEditPacket? pending)
    {
        var changes = new List<EditorObjectChange>();
        var conflicts = false;
        var pendingChanges = pending.HasValue ? pending.Value.Changes.ToDictionary(change => change.Id) : null;
       
        foreach (var change in local.Changes)
        {
            Objects.TryGetValue(change.Id, out var original);
            var exists = target.Objects.TryGetValue(change.Id, out var current);
            var baseline = original.Part;
            
            if (pendingChanges != null && pendingChanges.TryGetValue(change.Id, out var sent))
            {
                if (Same(sent.Part, current.Part)) baseline = sent.Part;
                else if (baseline != null && sent.Part != null && current.Part != null)
                    baseline = PendingBaseline(baseline, sent.Part, current.Part);
            }
          
            var merged = MergePart(baseline, change.Part, current.Part, out var conflict);
            conflicts |= conflict;
            if (!Same(merged, current.Part))
                changes.Add(new EditorObjectChange(change.Id, exists ? current.Version : 0, merged));
        }
      
        Level? environment = null;
        
        if (local.Environment != null)
        {
            var sent = pending.HasValue ? pending.Value.Environment : null;
            var merged = CopyEnvironment(target.Environment);
            merged.lightColor = MergeValue(sent == null ? Environment.lightColor :
                AcknowledgedValue(Environment.lightColor, sent.lightColor, target.Environment.lightColor),
                local.Environment.lightColor, target.Environment.lightColor, ref conflicts);
            merged.lightIntensity = MergeValue(sent == null ? Environment.lightIntensity :
                AcknowledgedValue(Environment.lightIntensity, sent.lightIntensity, target.Environment.lightIntensity),
                local.Environment.lightIntensity, target.Environment.lightIntensity, ref conflicts);
            merged.hasBackground = MergeValue(sent == null ? Environment.hasBackground :
                AcknowledgedValue(Environment.hasBackground, sent.hasBackground, target.Environment.hasBackground),
                local.Environment.hasBackground, target.Environment.hasBackground, ref conflicts);
            if (!SameEnvironment(merged, target.Environment)) environment = merged;
        }
        return (new EditorEditPacket(local.Epoch, target.Session, target.Revision, 0, environment, changes.ToArray()), conflicts);
    }

    internal bool CanClose(int transition, ushort[] peers, IReadOnlyDictionary<ushort, EditorCloseReadyPacket> acknowledgements)
    {
        var checksum = Checksum();
        return peers.All(peer => acknowledgements.TryGetValue(peer, out var ready) && ready.Session == Session &&
            ready.Transition == transition && (ready.Recovered || ready.Revision == Revision && ready.Checksum == checksum));
    }

    internal bool Contains(EditorEditPacket edit) => edit.Changes.All(change =>
        Same(Objects.TryGetValue(change.Id, out var current) ? current.Part : null, change.Part)) &&
        (edit.Environment == null || SameEnvironment(edit.Environment, Environment));

    private static LevelPart PendingBaseline(LevelPart original, LevelPart sent, LevelPart current)
    {
        return new LevelPart(AcknowledgedValue(original.pos, sent.pos, current.pos), AcknowledgedValue(original.rot, sent.rot, current.rot), AcknowledgedValue(original.path, sent.path, current.path))
        {
            id = AcknowledgedValue(original.id, sent.id, current.id),
            activId = AcknowledgedValue(original.activId, sent.activId, current.activId),
            team = AcknowledgedValue(original.team, sent.team, current.team),
            size = AcknowledgedValue(original.size, sent.size, current.size),
            force = AcknowledgedValue(original.force, sent.force, current.force)
        };
    }

    private static T AcknowledgedValue<T>(T original, T sent, T current) => EqualityComparer<T>.Default.Equals(sent, current) ? sent : original;

    private static LevelPart? MergePart(LevelPart? baseline, LevelPart? local, LevelPart? current, out bool conflict)
    {
        conflict = false;
        if (Same(local, current))
            return current;
        
        if (Same(current, baseline))
            return local;
        
        if (Same(local, baseline))
            return current;
        
        if (baseline == null || local == null || current == null)
        {
            conflict = true;
            return current;
        }
        
        return new LevelPart(
            MergeValue(baseline.pos, local.pos, current.pos, ref conflict),
            MergeValue(baseline.rot, local.rot, current.rot, ref conflict),
            MergeValue(baseline.path, local.path, current.path, ref conflict))
        {
            id = MergeValue(baseline.id, local.id, current.id, ref conflict),
            activId = MergeValue(baseline.activId, local.activId, current.activId, ref conflict),
            team = MergeValue(baseline.team ?? "", local.team ?? "", current.team ?? "", ref conflict),
            size = MergeValue(baseline.size, local.size, current.size, ref conflict),
            force = MergeValue(baseline.force, local.force, current.force, ref conflict)
        };
    }

    private static T MergeValue<T>(T baseline, T local, T current, ref bool conflict)
    {
        var comparer = EqualityComparer<T>.Default;
        if (comparer.Equals(local, baseline) || comparer.Equals(local, current)) 
            return current;
        
        if (comparer.Equals(current, baseline)) 
            return local;
        
        conflict = true;
        return current;
    }

    internal static bool Same(LevelPart? left, LevelPart? right) =>
        left == null || right == null ? left == right :
        left.pos.Equals(right.pos) && left.rot.Equals(right.rot) && left.path == right.path && left.id == right.id &&
        left.activId == right.activId && (left.team ?? "") == (right.team ?? "") && left.size.Equals(right.size) && left.force.Equals(right.force);

    internal static bool SameEnvironment(Level left, Level right) =>
        left.hasBackground == right.hasBackground && left.lightColor.Equals(right.lightColor) && left.lightIntensity.Equals(right.lightIntensity);

    private static Level CopyEnvironment(Level environment) => new()
    {
        lightColor = environment.lightColor, lightIntensity = environment.lightIntensity, hasBackground = environment.hasBackground
    };

    private static EditorObjectChange CopyChange(EditorObjectChange change)
    {
        var part = change.Part;
        return new EditorObjectChange(change.Id, change.Version, part == null ? null : new LevelPart(part.pos, part.rot, part.path)
        {
            id = part.id, activId = part.activId, team = part.team, size = part.size, force = part.force
        });
    }
}