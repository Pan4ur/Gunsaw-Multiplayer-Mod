using System;
using System.Collections.Generic;
using System.Net;

internal sealed class PeerRegistry
{
    private readonly Dictionary<ushort, PeerState> peers = new Dictionary<ushort, PeerState>();

    internal int Count => peers.Count;
    internal IEnumerable<PeerState> All => peers.Values;
    internal IEnumerable<KeyValuePair<ushort, PeerState>> Entries => peers;

    internal void Clear() => peers.Clear();
    internal bool Contains(ushort peerId) => peers.ContainsKey(peerId);
    internal bool Remove(ushort peerId) => peers.Remove(peerId);
    internal bool TryGet(ushort peerId, out PeerState peer) => peers.TryGetValue(peerId, out peer);
    internal ushort[] Ids()
    {
        var result = new ushort[peers.Count];
        peers.Keys.CopyTo(result, 0);
        return result;
    }

    internal PeerState Touch(ushort peerId, long nowTicks)
    {
        PeerState peer;
        if (!peers.TryGetValue(peerId, out peer))
        {
            peer = new PeerState();
            peers.Add(peerId, peer);
        }
        peer.LastPacketTicks = nowTicks;
        return peer;
    }
}

internal sealed class PeerState
{
    internal string Name = "Player";
    internal long LastPacketTicks;
    internal int PingMs = -1;
    internal IPEndPoint DirectEndpoint;
    internal long LastProbeTicks;
    internal long LastDirectPacketTicks;
}
