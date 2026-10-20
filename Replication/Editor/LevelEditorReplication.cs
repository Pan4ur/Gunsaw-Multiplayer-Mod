using System.Collections.Concurrent;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Idk why this feature was removed from LE+, but I've been asked a couple of times to add it
internal sealed class LevelEditorReplication : MonoBehaviour
{
    private static LevelEditorReplication? instance;
    private const float CursorSyncInterval = 1f / 30f;
    private const int MaxBufferedBytes = 16 * 1024 * 1024;
    private static readonly ConcurrentQueue<(ushort Peer, INetworkPacket Packet, int Size)> incoming = new();
    private static long incomingBytes;
    private static int captureAfterFrame;
    private readonly Dictionary<Guid, LevelPartGame> objects = new();
    private readonly Dictionary<ushort, Cursor> cursors = new();
    private readonly Dictionary<ushort, int> processed = new();
    private readonly Dictionary<ushort, float> lastSnapshot = new();
    private readonly Dictionary<int, (EditorCommitPacket Packet, int Size)> buffered = new();
    private EditorDocument document = new();
    private LevelEditor? editor;
    private EditorEditPacket? pending;
    private EditorEditPacket? cachedDraft;
    private (EditorDocument Base, EditorEditPacket Edit)? deferredDraft;
    private readonly Dictionary<ushort, EditorCloseReadyPacket> closeReady = new();
    private EditorClosePacket? initialClose;
    private Action? hostTransition;
    private int transition;
    private bool closing;
    private bool leaving;
    private bool executingTransition;
    private bool recoveryPreserved;
    private string savedRecoveryJson = "";
    private string savedRecoveryPath = "";
    private string savedClipboardCode = "";
    private float nextCloseSend;
    private Selectable? lockControl;
    private TMP_Text? lockCaption;
    private Image? lockIcon;
    private Sprite? lockSprite;
    private Sprite? unlockSprite;
    private Guid acceptedSession;
    private int epoch;
    private int nextRequest;
    private bool ready;
    private bool applying;
    private bool failed;
    private bool localEditing;
    private float nextTick;
    private float nextDigest;
    private float nextResync;
    private float pendingSince;
    private uint cursorSequence;
    private Vector2 lastCursor;
    private ulong lastCursorCharacter;
    private bool lastCursorLeftMouseDown;
    private readonly Dictionary<ulong, Sprite?> cursorPaws = new();
    private float nextCursor;
    private int checkpointRevision = -1;
    private float nextCheckpoint;
    private float gapSince;

    internal static bool IsLocalEditor => !MultiplayerSession.IsHosting && GunsawMultiplayerPlugin.Instance?.HasLocalLevelEditorTarget == true;

    internal static bool CanEdit
    {
        get
        {
            if (instance == null || instance.applying)
                return true;
            
            if (instance.leaving && ReferenceEquals(instance.editor, LevelEditor.main))
                return false;
            
            if (IsLocalEditor) 
                return true;
            
            if (instance.editor == null || !ReferenceEquals(instance.editor, LevelEditor.main))
                return !MultiplayerSession.IsActive || MultiplayerSession.IsHosting;
            
            if (!MultiplayerSession.IsActive) 
                return true;
            
            return !instance.failed && instance.ready && !instance.closing && (MultiplayerSession.IsHosting || !instance.document.Locked);
        }
    }

    internal static bool CanLeave => IsLocalEditor || instance == null || instance.editor == null || !MultiplayerSession.IsActive ||
        instance.executingTransition || instance.failed && instance.recoveryPreserved || instance.ready && !instance.failed && !instance.closing &&
        (MultiplayerSession.IsHosting || instance.pending == null && !instance.HasLocalChanges());

    internal static bool CanSave(LevelEditor value)
    {
        if (instance != null && instance.leaving && ReferenceEquals(instance.editor, value)) 
            return false;
        
        return !MultiplayerSession.IsActive || MultiplayerSession.IsHosting || IsLocalEditor && ReferenceEquals(value, LevelEditor.main) ||
            instance != null && ReferenceEquals(instance.editor, value) && instance.ready;
    }

    internal static bool RequestLeave(LevelEditor value, bool playtest)
    {
        if (IsLocalEditor || instance == null || !MultiplayerSession.IsActive) 
            return true;
        
        if (instance.editor != value && !instance.Reset(value)) 
            return false;
        
        return instance.TryLeave(playtest);
    }

    internal static bool PrepareSceneChange()
    {
        if (instance == null) 
            return true;
        
        if (instance.editor == null)
        {
            if (!MultiplayerSession.IsActive && !IsLocalEditor) 
                return true;
            
            var current = LevelEditor.main;
            if (current == null || current.gameObject.scene.name != "LevelEditor")
                return true;
            
            if (!instance.Reset(current)) 
                return false;
        }
        
        return instance.PrepareToLeave();
    }

    internal static void DeferCapture() => captureAfterFrame = Time.frameCount + 1;

    internal static void Enqueue(ushort peer, INetworkPacket packet, int size)
    {
        if (incoming.Count >= 2048) 
            return;
        
        if (Interlocked.Add(ref incomingBytes, size) > 2L * MaxBufferedBytes)
        {
            Interlocked.Add(ref incomingBytes, -size);
            return;
        }
        
        incoming.Enqueue((peer, packet, size));
    }

    private void Awake() => instance = this;

    private void Update()
    {
        var current = LevelEditor.main;
        if (IsLocalEditor || (!MultiplayerSession.IsHosting && !MultiplayerSession.IsConnected) || current == null || current.gameObject.scene.name != "LevelEditor")
        {
            var localEditor = IsLocalEditor && current != null && current.gameObject.scene.name == "LevelEditor" ? current : null;
            if (!ReferenceEquals(editor, localEditor) && !Reset(localEditor)) 
                return;
           
            while (incoming.TryDequeue(out var item))
            {
                Interlocked.Add(ref incomingBytes, -item.Size);
                if (MultiplayerSession.IsConnected && !MultiplayerSession.IsHosting && item.Peer == MultiplayerSession.HostPeerId && item.Packet is EditorClosePacket close && close.Closing && close.Epoch == MultiplayerSession.CurrentSceneEpoch)
                {
                    try
                    {
                        MultiplayerSession.Send(new EditorCloseReadyPacket(close.Epoch, close.Session, close.Transition, 0, document.Checksum(), true), item.Peer);
                    }
                    catch (IOException) { }
                }
            }
            return;
        }
        
        if ((editor != current || localEditing) && !Reset(current)) 
            return;
        
        if (epoch != MultiplayerSession.CurrentSceneEpoch)
        {
            PrepareToLeave();
            return;
        }
        
        if (Time.frameCount < captureAfterFrame)
            return;
        
        try
        {
            InstallLock();
            if (leaving)
                return;
            
            for (var i = 0; i < 256 && incoming.TryDequeue(out var item); i++)
            {
                Interlocked.Add(ref incomingBytes, -item.Size);
                Receive(item.Peer, item.Packet, item.Size);
            }
            
            if (failed)
            {
                TickClosing();
                UpdateLockPresentation();
                return;
            }
            
            if (!ready)
            {
                RequestSnapshot();
                return;
            }
            
            SendCursor();
            
            if (buffered.Count != 0 && Time.unscaledTime - gapSince > 0.75f) 
                RequestSnapshot();
            
            if (!CanEdit) 
                NavigateReadOnly();
            
            if (Time.unscaledTime >= nextTick)
            {
                nextTick = Time.unscaledTime + 0.1f;
                
                if (MultiplayerSession.IsHosting) 
                    CommitLocal();
                else if (pending == null && !document.Locked) 
                    Propose();
                else if (pending != null && Time.unscaledTime - pendingSince > 2f)
                {
                    MultiplayerSession.Send(pending.Value, MultiplayerSession.HostPeerId);
                    pendingSince = Time.unscaledTime;
                }
            }
            
            if (MultiplayerSession.IsHosting && Time.unscaledTime >= nextDigest)
            {
                nextDigest = Time.unscaledTime + 2f;
                MultiplayerSession.Send(new EditorChecksumPacket(epoch, document.Session, document.Revision, document.Checksum()));
            }
            
            foreach (var peer in cursors.Where(pair => Time.unscaledTime - pair.Value.Updated > 3f).Select(pair => pair.Key).ToArray())
            {
                if (cursors[peer].Name != null) 
                    Destroy(cursors[peer].Name!.gameObject);
                
                cursors.Remove(peer);
            }
            
            if (MultiplayerSession.IsHosting)
            {
                var peers = new HashSet<ushort>(MultiplayerSession.PeerIds());
                foreach (var peer in processed.Keys.Where(peer => !peers.Contains(peer)).ToArray())
                    processed.Remove(peer);
            }
            
            if (Time.unscaledTime >= nextCheckpoint && checkpointRevision != document.Revision)
            {
                nextCheckpoint = Time.unscaledTime + 30f;
                if (SaveCheckpoint()) 
                    checkpointRevision = document.Revision;
            }
            
            TickClosing();
            UpdateLockPresentation();
        }
        catch (Exception exception)
        {
            failed = true;
            editor!.SetInfoText("Editor sync failed! Save a recovery.");
            GunsawMultiplayerPlugin.LogInfo("Editor replication failed: " + exception);
            TryPreserveRecovery();
        }
    }

    private bool Reset(LevelEditor? value)
    {
        if (!leaving && NeedsRecovery() && !TryPreserveRecovery()) 
            return false;
        
        if (lockControl != null) 
            Destroy(lockControl.gameObject);
        
        lockCaption = null;
        lockIcon = null;
        editor = value;
        localEditing = editor != null && IsLocalEditor;
      
        if (editor != null && editor.level == null) 
            editor.level = new Level();
        
        objects.Clear();
        ClearCursorNames();
        cursors.Clear();
        cursorPaws.Clear();
        processed.Clear();
        lastSnapshot.Clear();
        buffered.Clear();
        document = new EditorDocument();
        pending = null;
        cachedDraft = null;
        deferredDraft = null;
        closeReady.Clear();
        initialClose = null;
        hostTransition = null;
        transition = 0;
        closing = leaving = executingTransition = false;
        nextCloseSend = 0f;
        ready = false;
        failed = false;
        acceptedSession = Guid.Empty;
        nextRequest = 0;
        nextResync = nextTick = nextDigest = 0f;
        checkpointRevision = -1;
        nextCheckpoint = 0f;
        gapSince = 0f;
        
        if (editor != null && MultiplayerSession.IsHosting)
            MultiplayerSession.NoteHostSceneHandle(editor.gameObject.scene.handle);
       
        epoch = MultiplayerSession.CurrentSceneEpoch;
        
        if (editor != null && MultiplayerSession.IsHosting)
            ready = true;
        
        return true;
    }

    private Dictionary<Guid, LevelPart> Capture()
    {
        var result = new Dictionary<Guid, LevelPart>();
        if (editor == null)
            return result;
        
        CustomPropEditorController.Ensure(editor).SynchronizeForReplication();
        var known = objects.Where(pair => pair.Value != null).ToDictionary(pair => pair.Value.GetInstanceID(), pair => pair.Key);
        foreach (Transform child in editor.transform)
        {
            var component = child.GetComponent<LevelPartGame>();
            if (component == null || !child.gameObject.activeSelf) continue;
            if (!known.TryGetValue(component.GetInstanceID(), out var id)) id = Guid.NewGuid();
            objects[id] = component;
            var part = CopyPart(component.part);
            part.pos = child.position;
            part.rot = Mathf.Round(child.eulerAngles.z * 1000f) / 1000f % 360f;
           
            var renderer = child.GetComponent<SpriteRenderer>();
            if (renderer != null && renderer.drawMode == SpriteDrawMode.Tiled) 
                part.size = renderer.size;
            
            result.Add(id, part);
        }
        return result;
    }

    private EditorEditPacket CaptureChanges(int request = 0)
    {
        var local = Capture();
        var changes = new List<EditorObjectChange>();
        var pendingIds = pending.HasValue ? new HashSet<Guid>(pending.Value.Changes.Select(change => change.Id)) : new HashSet<Guid>();
        foreach (var pair in local)
        {
            var exists = document.Objects.TryGetValue(pair.Key, out var old);
            var scale = objects[pair.Key].transform.localScale;
            if (pendingIds.Contains(pair.Key) || !EditorDocument.Same(exists ? old.Part : null, pair.Value) || !old.Scale.Equals(scale))
                changes.Add(new EditorObjectChange(pair.Key, exists ? old.Version : 0, pair.Value, scale));
        }
       
        foreach (var old in document.Objects.Values)
            if (!local.ContainsKey(old.Id))
                changes.Add(new EditorObjectChange(old.Id, old.Version, null));
     
        foreach (var id in pendingIds)
            if (!local.ContainsKey(id) && !document.Objects.ContainsKey(id))
                changes.Add(new EditorObjectChange(id, 0, null));
       
        var envChanged = pending.HasValue && pending.Value.Environment != null || !EditorDocument.SameEnvironment(editor!.level, document.Environment);
        var env = envChanged ? CopyEnvironment(editor!.level) : null;
        var edit = new EditorEditPacket(epoch, document.Session, document.Revision, request, env, [.. changes]);
        cachedDraft = edit;
        return edit;
    }

    private bool HasLocalChanges()
    {
        var changes = CaptureChanges();
        return HasChanges(changes) || deferredDraft.HasValue && HasChanges(deferredDraft.Value.Edit);
    }

    private static bool HasChanges(EditorEditPacket edit) => edit.Changes.Length != 0 || edit.Environment != null;

    private static Level CopyEnvironment(Level level) => new()
    {
        lightIntensity = level.lightIntensity, lightColor = level.lightColor, hasBackground = level.hasBackground
    };

    private static LevelPart CopyPart(LevelPart part) => new(part.pos, part.rot, part.path)
    {
        id = part.id, activId = part.activId, team = part.team ?? "", size = part.size, force = part.force
    };

    private void Propose()
    {
        var edit = CaptureChanges(nextRequest + 1);
        if (edit.Changes.Length == 0 && edit.Environment == null)
            return;
        
        nextRequest = edit.Request;
        pending = edit;
        pendingSince = Time.unscaledTime;
        MultiplayerSession.Send(edit, MultiplayerSession.HostPeerId);
    }

    private void CommitLocal()
    {
        if (failed || editor == null || Time.frameCount < captureAfterFrame) 
            return;
        
        var edit = CaptureChanges();
        if (edit.Changes.Length == 0 && edit.Environment == null)
            return;
        
        Commit(edit, MultiplayerSession.LocalPeerId);
    }

    private void Commit(EditorEditPacket edit, ushort author)
    {
        var revision = checked(document.Revision + 1);
        var changes = edit.Changes.Select(change => new EditorObjectChange(change.Id, revision, change.Part, change.Scale)).ToArray();
        var commit = new EditorCommitPacket(epoch, document.Session, revision, edit.Request, author, document.Locked, "", edit.Environment, changes);
        document.Apply(commit);
        MultiplayerSession.Send(new EditorCommitPacket(epoch, document.Session, revision, edit.Request, author, document.Locked, document.Checksum(), edit.Environment, changes));
    }

    private void Receive(ushort peer, INetworkPacket packet, int size)
    {
        if (failed && packet is not EditorClosePacket && packet is not EditorCloseReadyPacket)
            return;
        
        switch (packet)
        {
            case EditorCursorPacket cursor:
                ReceiveCursor(peer, cursor);
                break;
            case EditorSnapshotRequestPacket request when MultiplayerSession.IsHosting && request.Epoch == epoch:
                CommitLocal();
                SendSnapshot(peer, 0);
                break;
            case EditorEditPacket edit when MultiplayerSession.IsHosting && edit.Epoch == epoch:
                ReceiveEdit(peer, edit);
                break;
            case EditorSnapshotPacket snapshot when !MultiplayerSession.IsHosting && snapshot.Epoch == epoch:
                ReceiveSnapshot(snapshot);
                break;
            case EditorCommitPacket commit when !MultiplayerSession.IsHosting && commit.Epoch == epoch:
                ReceiveCommit(commit, size);
                break;
            case EditorChecksumPacket checksum when !MultiplayerSession.IsHosting && checksum.Epoch == epoch:
                ReceiveChecksum(checksum);
                break;
            case EditorClosePacket close when !MultiplayerSession.IsHosting && close.Epoch == epoch:
                ReceiveClose(close);
                break;
            case EditorCloseReadyPacket acknowledged when MultiplayerSession.IsHosting && acknowledged.Epoch == epoch:
                if (closing && acknowledged.Session == document.Session && acknowledged.Transition == transition)
                    closeReady[peer] = acknowledged;
                break;
        }
    }

    private void ReceiveCursor(ushort peer, EditorCursorPacket cursor)
    {
        if (!ready || float.IsNaN(cursor.Position.x) || float.IsInfinity(cursor.Position.x) ||
            float.IsNaN(cursor.Position.y) || float.IsInfinity(cursor.Position.y) || NetworkAvatarManager.knownCharacterPrefabs.All(path => NetworkWireId.FromString(path) != cursor.CharacterPrefabId))
            return;
        
        var id = MultiplayerSession.IsHosting ? peer : cursor.Peer;
        if (id == MultiplayerSession.LocalPeerId)
            return;
        
        if (cursors.TryGetValue(id, out var old) && unchecked((int)(cursor.Sequence - old.Sequence)) <= 0) 
            return;
        
        var now = Time.unscaledTime;
        cursors[id] = new Cursor(old?.PositionAt(now) ?? cursor.Position, cursor.Position, cursor.Sequence, now,
            old == null ? CursorSyncInterval : Mathf.Clamp(now - old.Updated, CursorSyncInterval, CursorSyncInterval * 2f),
            cursor.CharacterPrefabId, cursor.LeftMouseDown) { Name = old?.Name };
        if (MultiplayerSession.IsHosting)
            MultiplayerSession.Send(new EditorCursorPacket(peer, cursor.Sequence, cursor.Position, cursor.CharacterPrefabId, cursor.LeftMouseDown));
    }

    private void ReceiveEdit(ushort peer, EditorEditPacket edit)
    {
        if (edit.Session != document.Session || !ready || failed)
            return;
        
        CommitLocal();
        
        if (processed.TryGetValue(peer, out var previous) && edit.Request <= previous)
        {
            SendSnapshot(peer, edit.Request);
            return;
        }
        
        processed[peer] = edit.Request;
        
        if (!document.CanApply(edit) || !ValidateParts(edit.Changes))
        {
            SendSnapshot(peer, edit.Request);
            return;
        }
        
        ApplyWorld(edit.Changes, edit.Environment, false);
        Commit(edit, peer);
    }

    private bool ValidateParts(EditorObjectChange[] changes)
    {
        foreach (var change in changes)
        {
            if (change.Part == null) 
                continue;
            
            if (change.Part.path == "MP/CustomProp") // hardcode alert
            {
                if (!CustomPropEditorController.CanReplicate(change.Part))
                    return false;
            }
            
            else if (Resources.Load<GameObject>("Editor/" + change.Part.path) == null) 
                return false;
        }
        return true;
    }

    private void ReceiveSnapshot(EditorSnapshotPacket snapshot)
    {
        if (ready && snapshot.Session == document.Session && snapshot.Revision < document.Revision)
            return;
        
        if (acceptedSession != Guid.Empty && snapshot.Session != acceptedSession) 
            return;
        
        if (ready && snapshot.Session == document.Session && snapshot.Revision == document.Revision &&
            snapshot.Checksum == document.Checksum() && (!pending.HasValue || snapshot.Request != pending.Value.Request)) return;
       
        var candidate = new EditorDocument();
        candidate.Restore(snapshot);
        
        if (candidate.Checksum() != snapshot.Checksum || !ValidateParts(snapshot.Changes))
            throw new InvalidDataException("Editor snapshot cannot be reproduced with installed props.");
      
        var rebase = RebaseLocal(candidate);
        if (rebase.Conflicts || !ready && editor!.transform.childCount > 0) SaveRecovery();
        deferredDraft = (candidate.BasisFor(rebase.Edit), rebase.Edit);
        ApplyWorld(snapshot.Changes, snapshot.Environment, true);
        document = candidate;
        RestoreDraft(rebase.Edit);
        acceptedSession = snapshot.Session;
        ready = true;
      
        if (pending.HasValue && (snapshot.Request == pending.Value.Request || candidate.Contains(pending.Value))) 
            pending = null;
        
        foreach (var revision in buffered.Where(pair => pair.Key <= document.Revision || pair.Value.Packet.Session != document.Session).Select(pair => pair.Key).ToArray())
            buffered.Remove(revision);
        
        ApplyBuffered();
        nextResync = Time.unscaledTime + 1f;
     
        if (!rebase.Conflicts) 
            editor!.SetInfoText(document.Locked ? "Editor synced. Host has locked editing." : "Editor synced.");
       
        if (initialClose.HasValue)
        {
            var close = initialClose.Value;
            initialClose = null;
            ReceiveClose(close);
        }
    }

    private void ReceiveChecksum(EditorChecksumPacket checksum)
    {
        if (!ready || checksum.Session != document.Session)
            return;
        
        if (checksum.Revision > document.Revision && buffered.Count == 0 || checksum.Revision == document.Revision && checksum.Checksum != document.Checksum())
            RequestSnapshot();
    }

    private void ReceiveCommit(EditorCommitPacket commit, int size)
    {
        if (!ready)
        {
            BufferCommit(commit, size);
            return;
        }
        
        if (commit.Session != document.Session || commit.Revision <= document.Revision) 
            return;
        
        if (commit.Revision != document.Revision + 1)
        {
            BufferCommit(commit, size);
            return;
        }
        ApplyCommit(commit);
        ApplyBuffered();
    }

    private void BufferCommit(EditorCommitPacket commit, int size)
    {
        if (buffered.Count == 0) 
            gapSince = Time.unscaledTime;
        
        if (buffered.Count < 512 && buffered.Values.Sum(value => (long)value.Size) + size <= MaxBufferedBytes)
            buffered[commit.Revision] = (commit, size);
    }

    private void ApplyBuffered()
    {
        while (buffered.TryGetValue(document.Revision + 1, out var next))
        {
            buffered.Remove(next.Packet.Revision);
            ApplyCommit(next.Packet);
        }
    }

    private (EditorEditPacket Edit, bool Conflicts) RebaseLocal(EditorDocument target)
    {
        if (deferredDraft.HasValue)
            return deferredDraft.Value.Base.Rebase(deferredDraft.Value.Edit, target, pending);
        
        var local = CaptureChanges();
        
        if (!ready) 
            return (new EditorEditPacket(epoch, target.Session, target.Revision, 0, null, []), false);
       
        return document.Rebase(local, target, pending);
    }

    private void RestoreDraft(EditorEditPacket draft)
    {
        if (HasChanges(draft) && document.Locked)
            deferredDraft = (document.BasisFor(draft), draft);
        else
        {
            if (HasChanges(draft)) 
                ApplyWorld(draft.Changes, draft.Environment, false);
            
            deferredDraft = null;
        }
        CaptureChanges();
    }

    private void ApplyCommit(EditorCommitPacket commit)
    {
        var own = pending.HasValue && commit.Author == MultiplayerSession.LocalPeerId && commit.Request == pending.Value.Request;
        var local = deferredDraft.HasValue ? deferredDraft.Value.Edit : CaptureChanges();
        var basis = deferredDraft.HasValue ? deferredDraft.Value.Base : document.BasisFor(local);
        var target = document.BasisFor(local);
        target.Apply(commit);
    
        var rebase = basis.Rebase(local, target, own ? pending : null);
        if (rebase.Conflicts || commit.Locked && !document.Locked && HasLocalChanges())
            SaveRecovery();
        
        deferredDraft = (target.BasisFor(rebase.Edit), rebase.Edit);
        ApplyWorld(commit.Changes, commit.Environment, false);
        document.Apply(commit);
        RestoreDraft(rebase.Edit);
       
        if (own)
            pending = null;
        
        if (document.Checksum() != commit.Checksum)
            RequestSnapshot();
    }

    private void ApplyWorld(EditorObjectChange[] changes, Level? environment, bool snapshot)
    {
        applying = true;
        try
        {
            var selectionChanged = false;
            if (snapshot)
            {
                foreach (var child in editor!.transform.Cast<Transform>().ToArray()) 
                    Remove(child.GetComponent<LevelPartGame>());
                
                objects.Clear();
            }
            foreach (var change in changes)
            {
                objects.TryGetValue(change.Id, out var component);
              
                if (component != null && component.gameObject == editor!.currentlySelected)
                    selectionChanged = true;
               
                if (change.Part == null)
                {
                    Remove(component);
                    objects.Remove(change.Id);
                    continue;
                }
                
                if (component != null && component.part.path != change.Part.path)
                {
                    Remove(component);
                    component = null;
                }
              
                if (change.Part.path == "MP/CustomProp") // hardcode alert
                    component = CustomPropEditorController.Ensure(editor!).ApplyReplicatedPart(component, CopyPart(change.Part));
                else
                {
                    if (component == null)
                    {
                        var prefab = Resources.Load<GameObject>("Editor/" + change.Part.path);
                        if (prefab == null) 
                            throw new InvalidDataException("Missing editor prefab: " + change.Part.path);
                     
                        component = Instantiate(prefab, editor!.transform).GetComponent<LevelPartGame>();
                    }
              
                    component.part = CopyPart(change.Part);
                    component.transform.position = change.Part.pos;
                    component.transform.rotation = Quaternion.Euler(0, 0, change.Part.rot);
                    
                    var renderer = component.GetComponent<SpriteRenderer>();
                    if (renderer != null && renderer.drawMode == SpriteDrawMode.Tiled) 
                        renderer.size = change.Part.size;
              
                    if (component.fullName == "Colored Lamp")
                        LevelLoader.UpdateLampColor(component.gameObject, change.Part.force.x, LevelLoader.HexToColor(change.Part.team));
                   
                    if (component.CompareTag("Backwall") && renderer != null)
                    {
                        var color = renderer.color;
                        color.a = editor!.wallMode ? 1f : 0.3f;
                        renderer.color = color;
                    }
                }
                component.transform.localScale = change.Scale;
                objects[change.Id] = component;
            }
        
            if (environment != null)
            {
                editor!.level.lightColor = environment.lightColor;
                editor.level.lightIntensity = environment.lightIntensity;
                editor.level.hasBackground = environment.hasBackground;
                editor.UpdateBackground();
                editor.UpdateSliders();
            }
       
            editor!.UpdateLighting();
           
            if (editor.currentlySelected == null || !editor.currentlySelected.activeSelf)
            {
                editor.currentlySelected = null;
                editor.objMenu.SetActive(false);
            }
            else if (selectionChanged) 
                RefreshInspector();
            LevelEditorPlusCompatibility.RefreshSelection(editor);
        }
        finally { applying = false; }
    }

    private void RefreshInspector()
    {
        var component = editor!.currentlySelected.GetComponent<LevelPartGame>();
        RefreshField(editor.posXField, component.transform.position.x.ToString());
        RefreshField(editor.posYField, component.transform.position.y.ToString());
        RefreshField(editor.rotField, component.transform.eulerAngles.z.ToString());
       
        if (component.GetComponent<CustomPropMarker>() != null)
        {
            CustomPropEditorController.Ensure(editor).RefreshReplicatedInspector(component);
            return;
        }
       
        var part = component.part;
        RefreshField(editor.sizeXField, component.showSize ? part.size.x.ToString() : "--");
        RefreshField(editor.sizeYField, component.showSize ? part.size.y.ToString() : "--");
        RefreshField(editor.forceXField, component.showForce ? part.force.x.ToString() : "--");
        RefreshField(editor.forceYField, component.showForce ? part.force.y.ToString() : "--");
        RefreshField(editor.idField, component.showId ? part.id.ToString() : "--");
        RefreshField(editor.tarIdField, component.showActiveId ? part.activId.ToString() : "--");
        RefreshField(editor.teamField, component.showTeam ? part.team : "--");
        CustomPropEditorController.Ensure(editor).RefreshReplicatedInspector(component);
    }

    private static void RefreshField(TMP_InputField f, string value)
    {
        if (!f.isFocused) f.SetTextWithoutNotify(value);
    }

    private void Remove(LevelPartGame? component)
    {
        if (component == null) 
            return;
        
        CustomPropEditorController.Ensure(editor!).ForgetReplicatedPart(component);
       
        LevelEditorPlusCompatibility.ForgetSelection(editor!, component.gameObject);
       
        component.gameObject.SetActive(false);
        component.transform.SetParent(null);
        Destroy(component.gameObject);
    }

    private void RequestSnapshot()
    {
        if (MultiplayerSession.IsHosting || Time.unscaledTime < nextResync)
            return;
        
        nextResync = Time.unscaledTime + 2f;
        MultiplayerSession.Send(new EditorSnapshotRequestPacket(epoch), MultiplayerSession.HostPeerId);
    }

    private void SendSnapshot(ushort peer, int request)
    {
        if (MultiplayerSession.HasPendingReliablePacket(peer, PacketType.EditorSnapshot)) 
            return;
        
        if (request == 0 && lastSnapshot.TryGetValue(peer, out var time) && Time.unscaledTime - time < 1f)
            return;
        
        if (MultiplayerSession.Send(document.Snapshot(epoch, request), peer))
            lastSnapshot[peer] = Time.unscaledTime;
    }

    private void FlushInspector()
    {
        if (editor == null) 
            return;
        
        var previous = applying;
        applying = true;
      
        try
        {
            foreach (var field in new[] { editor.posXField, editor.posYField, editor.rotField, editor.sizeXField, editor.sizeYField, editor.forceXField, editor.forceYField, editor.idField, editor.tarIdField, editor.teamField })
                if (field != null && field.isFocused)
                    field.DeactivateInputField();
            editor.hasGrabPos = false;
        }
        finally
        {
            applying = previous; 
        }
    }

    private bool TryLeave(bool playtest)
    {
        if (executingTransition) return true;
        FlushInspector();
        if (failed)
        {
            if (playtest)
            {
                editor!.SetInfoText("Sync stopped. Save the level and return to the menu.");
                return false;
            }
            
            if (MultiplayerSession.IsHosting)
                CancelClosing();
            
            return TryPreserveRecovery();
        }
        
        if (!ready) 
            return !playtest;
        
        if (MultiplayerSession.IsHosting)
        {
            if (!closing)
            {
                closing = true;
                transition++;
                closeReady.Clear();
                nextCloseSend = 0f;
                hostTransition = playtest ? () => editor!.PlayTest() : () => editor!.BackToMenu();
                CommitLocal();
            }
            editor!.SetInfoText("Waiting for editors to finish. Esc cancels.");
            return false;
        }
        
        if (closing)
            return false;
        
        if (pending.HasValue || HasLocalChanges())
        {
            if (!document.Locked && !pending.HasValue) 
                Propose();
            else if (document.Locked && !pending.HasValue) 
                return TryPreserveRecovery();
            
            editor!.SetInfoText("Awaiting for the host to confirm edits...");
            return false;
        }
        return true;
    }

    private bool PrepareToLeave()
    {
        if (leaving) 
            return true;
        
        if (Time.frameCount < captureAfterFrame) 
            return false;
        
        try
        {
            FlushInspector();
            CaptureChanges();
        }
        catch (Exception e)
        {
            failed = true;
            GunsawMultiplayerPlugin.LogInfo("Could not capture editor before leaving: " + e);
        }
        
        if (NeedsRecovery() && !TryPreserveRecovery()) 
            return false;
        
        leaving = true;
        return true;
    }

    private void ReceiveClose(EditorClosePacket packet)
    {
        if (!ready)
        {
            if (!initialClose.HasValue || packet.Transition > initialClose.Value.Transition || packet.Transition == initialClose.Value.Transition && !packet.Closing) 
                initialClose = packet;
            
            if (failed)
            {
                var close = initialClose.Value;
                if (close.Closing && TryPreserveRecovery())
                    MultiplayerSession.Send(new EditorCloseReadyPacket(close.Epoch, close.Session, close.Transition, 0, document.Checksum(), true), MultiplayerSession.HostPeerId);
            }
            else 
                RequestSnapshot();
            
            return;
        }
        
        if (packet.Session != document.Session || packet.Transition < transition || packet.Transition == transition && !closing && packet.Closing)
            return;
        
        FlushInspector();
        transition = packet.Transition;
        closing = packet.Closing;
        nextCloseSend = 0f;
        
        editor!.SetInfoText(closing ? "Host is closing the editor. Finishing local edits..." : "Editor close cancelled.");
    }

    private void CancelClosing()
    {
        if (!closing)
            return;
        
        MultiplayerSession.Send(new EditorClosePacket(epoch, document.Session, transition, false));
        closing = false;
        hostTransition = null;
        closeReady.Clear();
        editor!.SetInfoText("Editor close cancelled.");
    }

    private void TickClosing()
    {
        if (!closing || Time.frameCount < captureAfterFrame)
            return;
        
        if (MultiplayerSession.IsHosting)
        {
            if (failed || Input.GetKeyDown(KeyCode.Escape))
            {
                CancelClosing();
                return;
            }
            
            CommitLocal();
            
            var peers = MultiplayerSession.PeerIds();
            if (document.CanClose(transition, peers, closeReady))
            {
                var action = hostTransition;
                hostTransition = null;
                executingTransition = true;
                try
                {
                    action?.Invoke();
                }
                finally
                {
                    executingTransition = false;
                }
                return;
            }
            
            if (Time.unscaledTime < nextCloseSend) 
                return;
            
            nextCloseSend = Time.unscaledTime + 1f;
            MultiplayerSession.Send(new EditorClosePacket(epoch, document.Session, transition, true));
            return;
        }
        
        if (Time.unscaledTime < nextCloseSend)
            return;
        
        nextCloseSend = Time.unscaledTime + 1f;
        var recovered = failed || document.Locked && HasLocalChanges();
        
        if (recovered)
        {
            if (!TryPreserveRecovery()) 
                return;
        }
        else if (pending.HasValue || HasLocalChanges() || buffered.Count != 0)
            return;
        
        MultiplayerSession.Send(new EditorCloseReadyPacket(epoch, document.Session, transition, document.Revision, document.Checksum(), recovered), MultiplayerSession.HostPeerId);
    }

    private bool NeedsRecovery()
    {
        if (failed || pending.HasValue || deferredDraft.HasValue) 
            return true;
        
        return editor != null ? HasLocalChanges() : cachedDraft.HasValue && HasChanges(cachedDraft.Value);
    }

    private Level RecoveryLevel()
    {
        Dictionary<Guid, LevelPart> local;
        Level environment;
        bool captured;
        
        try
        {
            captured = editor != null && !deferredDraft.HasValue;
            local = captured ? Capture() : document.Objects.Values.ToDictionary(change => change.Id, change => CopyPart(change.Part!));
            environment = CopyEnvironment(captured ? editor!.level : document.Environment);
        }
        catch
        {
            captured = false;
            local = document.Objects.Values.ToDictionary(change => change.Id, change => CopyPart(change.Part!));
            environment = CopyEnvironment(document.Environment);
        }

        var draft = deferredDraft.HasValue ? deferredDraft.Value.Edit : !captured ? cachedDraft ?? pending : null;
        if (draft.HasValue)
        {
            foreach (var change in draft.Value.Changes)
            {
                if (change.Part == null) 
                    local.Remove(change.Id);
                else 
                    local[change.Id] = CopyPart(change.Part);
            }
            
            if (draft.Value.Environment != null)
                environment = CopyEnvironment(draft.Value.Environment);
        }
        
        environment.parts = [.. local.OrderBy(pair => pair.Key).Select(pair => pair.Value)];
        return environment;
    }

    private void SaveRecovery()
    {
        var json = JsonUtility.ToJson(RecoveryLevel());
        if (recoveryPreserved && savedRecoveryJson == json &&
            (File.Exists(savedRecoveryPath) || !string.IsNullOrEmpty(savedClipboardCode) && GUIUtility.systemCopyBuffer == savedClipboardCode)) return;
        var directory = Path.Combine(Application.persistentDataPath, "MultiplayerEditorRecovery");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        savedRecoveryJson = json;
        savedRecoveryPath = path;
        savedClipboardCode = "";
        recoveryPreserved = true;
        if (editor != null) editor.SetInfoText("Unconfirmed edits backed up: " + path);
        GunsawMultiplayerPlugin.LogInfo("Editor recovery saved: " + path);
    }

    private bool TryPreserveRecovery()
    {
        try
        {
            SaveRecovery();
            return true;
        }
        catch (Exception exception)
        {
            GunsawMultiplayerPlugin.LogInfo("Could not save editor recovery: " + exception);
            try
            {
                var json = JsonUtility.ToJson(RecoveryLevel());
                var code = Compression.Compress(json);
                GUIUtility.systemCopyBuffer = code;
                if (GUIUtility.systemCopyBuffer != code) throw new IOException("Clipboard verify failed.");
                savedRecoveryJson = json;
                savedRecoveryPath = "";
                savedClipboardCode = code;
                recoveryPreserved = true;
                if (editor != null) 
                    editor.SetInfoText("Recovery copied to clipboard. Paste it into a file after leaving.");
               
                return true;
            }
            catch (Exception clipboardException)
            {
                recoveryPreserved = false;
                GunsawMultiplayerPlugin.LogInfo("Could not export editor recovery: " + clipboardException);
                if (editor != null)
                    editor.SetInfoText("Could not preserve edits. Export the level before leaving.");
                
                return false;
            }
        }
    }

    private void InstallLock()
    {
        if (lockControl != null)
            return;
        
        var controls = Resources.FindObjectsOfTypeAll<Selectable>()
            .Where(control => control.gameObject.scene == editor!.gameObject.scene && (control is Toggle || control is Button))
            .ToArray();
        var source = controls.FirstOrDefault(control => control.GetComponentsInChildren<TMP_Text>(true).Any(IsLightingCaption))
            ?? controls.FirstOrDefault(ControlsLighting);
        if (source == null)
            return;
        
        lockControl = Instantiate(source, source.transform.parent);
        lockControl.name = "Multiplayer Editor Lock";
        lockControl.transform.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);
        var rect = lockControl.GetComponent<RectTransform>();
        rect.anchoredPosition = source.GetComponent<RectTransform>().anchoredPosition + Vector2.down * (rect.rect.height + 5f);
        var labels = lockControl.GetComponentsInChildren<TMP_Text>(true);
        lockCaption = labels.FirstOrDefault(IsLightingCaption) ?? labels.FirstOrDefault();
        if (lockCaption == null)
        {
            var sourceCaption = source.transform.parent.GetComponentsInChildren<TMP_Text>(true)
                .FirstOrDefault(label => label.transform.parent == source.transform.parent && IsLightingCaption(label));
          
            if (sourceCaption != null)
            {
                lockCaption = Instantiate(sourceCaption, lockControl.transform, false);
                lockCaption.name = "Multiplayer Editor Lock Caption";
                var captionRect = lockCaption.rectTransform;
                captionRect.anchorMin = captionRect.anchorMax = Vector2.one * 0.5f;
                captionRect.anchoredPosition = source.transform.InverseTransformPoint(sourceCaption.transform.position);
                captionRect.sizeDelta = sourceCaption.rectTransform.rect.size;
                lockCaption.raycastTarget = false;
            }
        }
      
        lockIcon = lockControl.GetComponentsInChildren<Image>(true)
            .FirstOrDefault(image => image.sprite != null && image.sprite.name == "lightEdit");
      
        if (lockIcon != null)
        {
            var pixelsPerUnit = lockIcon.sprite.pixelsPerUnit;
            lockSprite = EmbeddedSpriteLoader.Load("GunsawMultiplayer.Assets.editor-lock.png", pixelsPerUnit, Vector2.one * 0.5f);
            unlockSprite = EmbeddedSpriteLoader.Load("GunsawMultiplayer.Assets.editor-unlock.png", pixelsPerUnit, Vector2.one * 0.5f);
        }
        
        lockControl.gameObject.SetActive(true);
        lockControl.interactable = MultiplayerSession.IsHosting && ready && !failed && !closing && !leaving;
       
        if (lockControl is Toggle toggle)
        {
            toggle.onValueChanged = new Toggle.ToggleEvent();
            toggle.SetIsOnWithoutNotify(document.Locked);
            toggle.onValueChanged.AddListener(SetLocked);
        }
        else if (lockControl is Button button)
        {
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => SetLocked(!document.Locked));
        }
        UpdateLockPresentation();
    }

    private void UpdateLockPresentation()
    {
        if (lockControl == null) 
            return;
        
        if (lockCaption != null)
        {
            var caption = document.Locked ? "Unlock" : "Lock";
            if (lockCaption.text != caption) 
                lockCaption.text = caption;
        }
        
        if (lockIcon != null) 
            lockIcon.sprite = document.Locked ? unlockSprite : lockSprite;
        
        if (lockControl is Toggle toggle) 
            toggle.SetIsOnWithoutNotify(document.Locked);
        else
        {
            var colors = lockControl.colors;
            colors.normalColor = document.Locked ? new Color(0.6f, 1f, 0.6f) : Color.white;
            lockControl.colors = colors;
        }
        
        lockControl.interactable = MultiplayerSession.IsHosting && ready && !failed && !closing && !leaving;
    }

    private static bool IsLightingCaption(TMP_Text label) =>
        string.Equals(label.text.Trim(), "Light", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(label.text.Trim(), "Lighting", StringComparison.OrdinalIgnoreCase);

    private bool ControlsLighting(Selectable control)
    {
        UnityEngine.Events.UnityEventBase click = control is Button button ? button.onClick : ((Toggle)control).onValueChanged;
        for (var index = 0; index < click.GetPersistentEventCount(); index++)
            if (click.GetPersistentTarget(index) == editor && click.GetPersistentMethodName(index) == nameof(LevelEditor.ToggleLighting))
                return true;
        return false;
    }

    private void SetLocked(bool value)
    {
        if (!MultiplayerSession.IsHosting || failed || closing || leaving) 
            return;
        
        CommitLocal();
        document.Locked = value;
        Commit(new EditorEditPacket(epoch, document.Session, document.Revision, 0, null, []), MultiplayerSession.LocalPeerId);
        UpdateLockPresentation();
    }

    private void NavigateReadOnly()
    {
        var camera = Camera.main;
        if (camera == null) 
            return;
        
        camera.orthographicSize = Mathf.Clamp(camera.orthographicSize - Input.mouseScrollDelta.y, 1f, 50f);
       
        if (Input.GetMouseButtonDown(1))
            editor!.camGrabPos = camera.ScreenToWorldPoint(Input.mousePosition);
        
        if (Input.GetMouseButton(1))
            camera.transform.position += (Vector3)(editor!.camGrabPos - (Vector2)camera.ScreenToWorldPoint(Input.mousePosition));
    }

    private bool SaveCheckpoint()
    {
        try
        {
            var level = CopyEnvironment(document.Environment);
            level.parts = [.. document.Objects.Values.OrderBy(value => value.Id).Select(value => value.Part!)];
            var directory = Path.Combine(Application.persistentDataPath, "MultiplayerEditorRecovery");
            
            if (WriteCheckpoint(JsonUtility.ToJson(level), directory, document.Session, out var e)) 
                return true;
            
            GunsawMultiplayerPlugin.LogInfo("Editor checkpoint failed: " + e);
        }
        catch (Exception e)
        {
            GunsawMultiplayerPlugin.LogInfo("Editor checkpoint failed: " + e);
        }
        
        editor!.SetInfoText("Auto backup failed. Editor sync is still active.");
        return false;
    }

    private static bool WriteCheckpoint(string json, string directory, Guid session, out string error)
    {
        error = "";
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, session.ToString("N") + "-confirmed.json");
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, json);
           
            if (File.Exists(path))
                File.Replace(temporary, path, path + ".previous");
            else 
                File.Move(temporary, path);
            
            return true;
        }
        catch (Exception exception)
        {
            error = exception.ToString();
            return false;
        }
    }

    private void SendCursor()
    {
        if (Camera.main == null || Time.unscaledTime < nextCursor) 
            return;
        
        var position = (Vector2)Camera.main.ScreenToWorldPoint(Input.mousePosition);
        var character = NetworkWireId.FromString(PlayerPrefs.GetString("charPrefab"));
        var leftMouseDown = Input.GetMouseButton(0);
        
        if (position == lastCursor && character == lastCursorCharacter && leftMouseDown == lastCursorLeftMouseDown && Time.unscaledTime < nextCursor + 0.9f)
            return;
        
        nextCursor = Time.unscaledTime + CursorSyncInterval;
        lastCursor = position;
        lastCursorCharacter = character;
        lastCursorLeftMouseDown = leftMouseDown;
        MultiplayerSession.Send(new EditorCursorPacket(MultiplayerSession.LocalPeerId, ++cursorSequence, position, character, leftMouseDown), MultiplayerSession.IsHosting ? (ushort)0 : MultiplayerSession.HostPeerId);
    }

    private void OnGUI()
    {
        if (editor == null || !ready || Camera.main == null) 
            return;
        
        var scale = 0.75f * Mathf.Clamp(Screen.height / 1080f, 1f, 2f);
        
        foreach (var pair in cursors)
        {
            if (Time.unscaledTime - pair.Value.Updated > 3f)
                continue;
            
            var point = Camera.main.WorldToScreenPoint(pair.Value.PositionAt(Time.unscaledTime));
            var paw = CursorPaw(pair.Value.CharacterPrefabId);
           
            if (paw != null)
            {
                var texture = paw.texture;
                var rect = paw.textureRect;
                var size = paw.rect.size * (48f * scale / Mathf.Max(paw.rect.width, paw.rect.height));
                var prevColor = GUI.color;
                
                if (pair.Value.LeftMouseDown) 
                    GUI.color = prevColor * Color.Lerp(Color.white, Color.yellow, 0.5f);
           
                GUI.DrawTextureWithTexCoords(new Rect(point.x - size.x * 0.5f, Screen.height - point.y - 10f * scale, size.x, size.y), texture, new Rect(rect.xMax / texture.width, rect.yMax / texture.height, -rect.width / texture.width, -rect.height / texture.height));
                GUI.color = prevColor;
            }
        }
    }

    private Sprite? CursorPaw(ulong character)
    {
        if (cursorPaws.TryGetValue(character, out var sprite)) 
            return sprite;
        
        var path = NetworkAvatarManager.knownCharacterPrefabs.FirstOrDefault(path => NetworkWireId.FromString(path) == character);
        var prefab = path == null ? null : Resources.Load<GameObject>(path);
        var hand = prefab == null ? null : prefab.GetComponentsInChildren<SpriteRenderer>(true)
            .FirstOrDefault(renderer => renderer.name == "HandB" && renderer.sprite != null);
        sprite = hand == null ? null : hand.sprite;
        cursorPaws[character] = sprite;
        return sprite;
    }

    private void LateUpdate()
    {
        var camera = Camera.main;
        var visible = editor != null && ready && camera != null;
        var ui = MultiplayerHud.Instance == null ? null : MultiplayerHud.Instance.GetComponent<MultiplayerHudUi>();
        var scale = 0.75f * Mathf.Clamp(Screen.height / 1080f, 1f, 2f);
        foreach (var pair in cursors)
        {
            var cursor = pair.Value;
            var show = visible && Time.unscaledTime - cursor.Updated <= 3f;
            if (show && cursor.Name == null)
                cursor.Name = ui?.CreateEditorCursorName();
          
            var name = cursor.Name;
            if (name == null) 
                continue;
            
            name.gameObject.SetActive(show);
         
            if (!show) 
                continue;
            
            var point = camera!.WorldToScreenPoint(cursor.PositionAt(Time.unscaledTime));
            name.text = MultiplayerSession.PlayerName(pair.Key);
            name.color = Color.HSVToRGB(pair.Key * 0.618034f % 1f, 0.65f, 1f);
            name.fontSize = 24f * scale / name.canvas.scaleFactor;
            name.rectTransform.position = new Vector3(point.x, point.y - 38f * scale, 0f);
        }
    }

    private void ClearCursorNames()
    {
        foreach (var c in cursors.Values) if (c.Name != null) Destroy(c.Name.gameObject);
    }

    private void OnDestroy()
    {
        ClearCursorNames();
        if (instance == this) instance = null;
    }

    private sealed class Cursor
    {
        internal TMP_Text? Name;
        private readonly Vector2 from;
        private readonly Vector2 target;
        private readonly float duration;
        internal readonly ulong CharacterPrefabId;
        internal readonly bool LeftMouseDown;
        internal readonly uint Sequence;
        internal readonly float Updated;
        
        internal Cursor(Vector2 from, Vector2 target, uint sequence, float updated, float duration, ulong characterPrefabId, bool leftMouseDown)
        {
            this.from = from;
            this.target = target;
            this.duration = duration;
            Sequence = sequence;
            Updated = updated;
            CharacterPrefabId = characterPrefabId;
            LeftMouseDown = leftMouseDown;
        }

        internal Vector2 PositionAt(float time) => Vector2.Lerp(from, target, (time - Updated) / duration);
    }
}
