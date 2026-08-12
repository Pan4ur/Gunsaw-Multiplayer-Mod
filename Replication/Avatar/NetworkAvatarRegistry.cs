public class NetworkAvatarRegistry
{
    internal static readonly Dictionary<ushort, NetworkAvatarReplication> replicas = new();
    
    internal static NetworkAvatarReplication GetOrCreateReplica(ushort peerId)
    {
        var coordinator = NetworkAvatarReplication.Instance;
        if (coordinator == null || peerId == 0 || peerId == MultiplayerSession.LocalPeerId) return null;
        NetworkAvatarReplication replica;
        if (replicas.TryGetValue(peerId, out replica) && replica != null) return replica;
        replica = coordinator.gameObject.AddComponent<NetworkAvatarReplication>();
        replica.remotePeerId = peerId;
        replica.localName = coordinator.localName;
        replicas[peerId] = replica;
        return replica;
    }
    
    internal static NetworkAvatarReplication ReplicaForBody(BodyScript body)
    {
        if (body == null) return null;
        foreach (var replica in replicas.Values)
            if (replica != null && replica.remoteBody == body) return replica;
        return null;
    }
    
    internal static BodyScript RemoteBodyForPeer(ushort peerId)
    {
        NetworkAvatarReplication replica;
        return replicas.TryGetValue(peerId, out replica) && replica != null ? replica.remoteBody : null;
    }
    
    internal static RemotePlayerInfo[] RemotePlayers()
    {
        var result = new List<RemotePlayerInfo>(replicas.Count);
        foreach (var pair in replicas)
            if (pair.Value != null)
                result.Add(new RemotePlayerInfo
                {
                    PeerId = pair.Key,
                    Name = pair.Value.remoteName,
                    Body = pair.Value.remoteBody,
                    AuthoritativePosition = pair.Value.lastAuthoritativePosition,
                    HasAuthoritativePosition = pair.Value.hasAuthoritativePosition,
                    PingMs = MultiplayerSession.PeerPing(pair.Key)
                });
        result.Sort((left, right) => left.PeerId.CompareTo(right.PeerId));
        return result.ToArray();
    }
    
    internal static string RemoteNameForBody(BodyScript body)
    {
        var replica = ReplicaForBody(body);
        return replica == null ? "Player" : replica.remoteName;
    }
    
    internal static bool IsRemoteAvatarBody(BodyScript body)
    {
        return ReplicaForBody(body) != null;
    }
    
    internal static bool IsRemoteReplicaBody(BodyScript body)
    {
        return body != null && (ReplicaForBody(body) != null || body.GetComponentInParent<NetworkReplica>() != null);
    }
    
    internal static void CleanupDisconnectedReplicas()
    {
        var stale = new List<ushort>();
        foreach (var pair in replicas)
            if (pair.Value == null || !MultiplayerSession.HasPeer(pair.Key)) stale.Add(pair.Key);
        foreach (var peerId in stale)
        {
            NetworkAvatarReplication replica;
            if (replicas.TryGetValue(peerId, out replica) && replica != null)
            {
                replica.DestroyRemote();
                UnityEngine.Object.Destroy(replica);
            }
            replicas.Remove(peerId);
        }
    }
    
    internal static void DestroyAllReplicas()
    {
        foreach (var replica in new List<NetworkAvatarReplication>(replicas.Values))
            if (replica != null)
            {
                replica.DestroyRemote();
                UnityEngine.Object.Destroy(replica);
            }
        replicas.Clear();
    }
}
