using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

internal static partial class MultiplayerSession
{
    private static UdpClient socket;
    private static volatile bool relayConnected;
    private static IPEndPoint relayEndpoint;
    private static CancellationTokenSource socketCancellation;
    private static readonly object sendLock = new();
    private static readonly PacketSendQueue sendQueue = new();
    private static readonly byte[] sendBuffer = new byte[UdpDatagram.MaxDatagramLength];
    private static readonly AutoResetEvent sendSignal = new(false);
    private static Thread sendThread;
    private static readonly byte[] udpMagic = new byte[] { 0x47, 0x55, 0x44, 0x50 };
    private const byte UdpAuth = 1;
    private const byte UdpAuthOk = 2;
    private const byte UdpData = 3;
    private const byte UdpForwarded = 4;
    private const byte UdpAuthFailed = 5;
    private const byte UdpP2PEnable = 6;
    private const byte UdpCandidate = 7;
    private const byte UdpDirectData = 8;
    private const byte UdpKeepAlive = 9;
    private const int P2PKeySize = 16;
    private const long P2PConnectTimeoutTicks = TimeSpan.TicksPerSecond * 5;
    private const long P2PKeepAliveTicks = TimeSpan.TicksPerSecond * 10;
    private const long P2PProbeRetryTicks = TimeSpan.TicksPerMillisecond * 500;
    private static int transportMessageSequence;
    private static readonly FragmentReassembler fragmentReassembler = new();
    private static readonly ReliableChannel reliableChannel = new();

    private static void SendDisconnectImmediately()
    {
        UdpClient current;
        CancellationTokenSource cancellation;
        ushort targetId;
        lock (statusLock)
        {
            current = socket;
            cancellation = socketCancellation;
            targetId = isHost ? (ushort)0 : hostPeerId;
        }
        if (current == null || cancellation == null || !relayConnected) return;

        var disconnect = DisconnectPacket.ClientClosed();
        var packet = new OutboundPacket(disconnect.Type, disconnect.Settings,
            targetId, PacketCodec.Encode(disconnect));
        try { SendPacketBlocking(current, cancellation, packet); }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
        catch (IOException) { }
    }

    private static UdpClient ConnectRelay(string address, string lobbyId, string relayKey)
    {
        if (lobbyId == null || lobbyId.Length != 32 || relayKey == null || relayKey.Length != 32)
            throw new InvalidOperationException("Invalid relay credentials.");

        var candidate = (address ?? "").Trim();
        if (!candidate.Contains("://")) candidate = "udp://" + candidate;
        Uri uri;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out uri) || string.IsNullOrEmpty(uri.Host))
            throw new InvalidOperationException("Invalid UDP relay address.");
        if (uri.Scheme != "udp")
            throw new InvalidOperationException("UDP relay address must start with udp://.");
        var port = uri.IsDefaultPort ? 27015 : uri.Port;
        if (port < 1 || port > 65535) throw new InvalidOperationException("Invalid UDP relay port.");

        var addresses = Dns.GetHostAddresses(uri.Host);
        Array.Sort(addresses, (left, right) =>
        {
            var leftRank = left.AddressFamily == AddressFamily.InterNetwork ? 0 : 1;
            var rightRank = right.AddressFamily == AddressFamily.InterNetwork ? 0 : 1;
            return leftRank.CompareTo(rightRank);
        });

        UdpClient client = null;
        IPEndPoint endpoint = null;
        Exception lastConnectError = null;
        foreach (var relayAddress in addresses)
        {
            if (relayAddress.AddressFamily != AddressFamily.InterNetwork &&
                relayAddress.AddressFamily != AddressFamily.InterNetworkV6) continue;
            try
            {
                var candidateClient = new UdpClient(relayAddress.AddressFamily);
                endpoint = new IPEndPoint(relayAddress, port);
                client = candidateClient;
                break;
            }
            catch (Exception exception)
            {
                lastConnectError = exception;
            }
        }
        if (client == null)
            throw new IOException("Could not create a UDP socket for " + uri.Host + ".", lastConnectError);
        try
        {
            client.Client.ReceiveTimeout = 500;
            socketCancellation = new CancellationTokenSource();
            socket = client;
            relayEndpoint = endpoint;
            relayConnected = false;

            var auth = new byte[5 + 64];
            Buffer.BlockCopy(udpMagic, 0, auth, 0, udpMagic.Length);
            auth[4] = UdpAuth;
            Buffer.BlockCopy(Encoding.ASCII.GetBytes(lobbyId), 0, auth, 5, 32);
            Buffer.BlockCopy(Encoding.ASCII.GetBytes(relayKey), 0, auth, 37, 32);

            var authenticated = false;
            for (var attempt = 0; attempt < 10 && !authenticated; attempt++)
            {
                client.Send(auth, auth.Length, endpoint);

                if (!client.Client.Poll(500000, SelectMode.SelectRead)) continue;
                try
                {
                    IPEndPoint remote = null;
                    var response = client.Receive(ref remote);
                    if (response != null && response.Length >= 7 && HasUdpMagic(response) && EndpointsEqual(remote, endpoint))
                    {
                        if (response[4] == UdpAuthFailed)
                        {
                            var reason = response.Length > 7
                                ? Encoding.UTF8.GetString(response, 7, response.Length - 7).Trim()
                                : "";
                            throw new InvalidOperationException(string.IsNullOrEmpty(reason)
                                ? "UDP relay rejected the lobby key." : reason);
                        }
                        authenticated = response[4] == UdpAuthOk;
                        if (authenticated)
                        {
                            if (response.Length >= 7 + P2PKeySize)
                            {
                                p2pKey = new byte[P2PKeySize];
                                Buffer.BlockCopy(response, 7, p2pKey, 0, P2PKeySize);
                            }
                            var authenticatedPeer = BitConverter.ToUInt16(response, 5);
                            if (localPeerId != 0 && authenticatedPeer != localPeerId)
                                throw new InvalidOperationException("UDP relay returned a different peer ID.");
                        }
                    }
                }
                catch (SocketException exception)
                {
                    if (exception.SocketErrorCode != SocketError.TimedOut) throw;
                }
            }
            if (!authenticated)
            {
                throw new IOException("UDP relay did not answer authentication.");
            }

            client.Client.ReceiveTimeout = 0;
            relayConnected = true;
            StartSendWorker(client, socketCancellation);
            return client;
        }
        catch
        {
            CloseSocket();
            client.Dispose();
            throw;
        }
    }

    private static ArraySegment<byte> ReadPacket(UdpClient current, out ushort senderId)
    {
        senderId = 0;
        if (current == null || current != socket || !relayConnected) return default;
        while (relayConnected && current == socket)
        {
            IPEndPoint remote = null;
            var datagram = current.Receive(ref remote);
            if (current != socket) return default;
            if (datagram == null || !HasUdpMagic(datagram)) continue;
            var metadata = 0;
            if (datagram.Length >= 5 && datagram[4] == UdpCandidate && EndpointsEqual(remote, relayEndpoint))
            {
                RegisterCandidate(datagram);
                continue;
            }
            if (datagram.Length >= 19 && datagram[4] == UdpForwarded && EndpointsEqual(remote, relayEndpoint))
            {
                senderId = BitConverter.ToUInt16(datagram, 5);
                metadata = UdpDatagram.RelayMetadataOffset;
            }
            else if (TryAcceptDirectPacket(datagram, remote, out senderId)) metadata = UdpDatagram.DirectMetadataOffset;
            else continue;
            Interlocked.Add(ref receivedBytes, datagram.Length);
            Interlocked.Increment(ref receivedPackets);
            if (fragmentReassembler.TryAccept(datagram, metadata, senderId, Stopwatch.GetTimestamp(), out var packet))
            {
                return packet;
            }

        }
        return default;
    }

    private static void EnableP2P()
    {
        if (p2pKey == null || p2pKey.Length != P2PKeySize)
        {
            GunsawMultiplayerPlugin.LogInfo("P2P unavailable: UDP relay did not provide a valid P2P key.");
            return;
        }
        GunsawMultiplayerPlugin.LogInfo("P2P enabled; waiting for relay candidates.");
        SendControlToRelay(UdpP2PEnable);
    }

    private static void SendControlToRelay(byte type)
    {
        var current = socket;
        var endpoint = relayEndpoint;
        if (current == null || endpoint == null || !relayConnected) return;
        var control = new byte[] { udpMagic[0], udpMagic[1], udpMagic[2], udpMagic[3], type };
        try { current.Send(control, control.Length, endpoint); }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
    }

    private static void RegisterCandidate(byte[] packet)
    {
        if (packet.Length < 10) return;
        var peerId = BitConverter.ToUInt16(packet, 5);
        var littleEndianPort = BitConverter.ToUInt16(packet, 7);
        var networkEndianPort = (ushort)((packet[7] << 8) | packet[8]);
        var length = packet[9];
        if (peerId == 0 || peerId == localPeerId || littleEndianPort == 0 ||
            (length != 4 && length != 16) ||
            packet.Length != 10 + length) return;
        var address = new byte[length];
        Buffer.BlockCopy(packet, 10, address, 0, length);
        var endpoint = new IPEndPoint(new IPAddress(address), littleEndianPort);
        var alternateEndpoint = networkEndianPort == 0 || networkEndianPort == littleEndianPort
            ? null
            : new IPEndPoint(new IPAddress(address), networkEndianPort);
        var shouldProbe = false;
        lock (statusLock)
        {
            P2PPeer peer;
            var knownEndpoint = p2pPeers.TryGetValue(peerId, out peer) &&
                (EndpointsEqual(peer.Endpoint, endpoint) || EndpointsEqual(peer.Endpoint, alternateEndpoint) ||
                 EndpointsEqual(peer.AlternateEndpoint, endpoint) ||
                 EndpointsEqual(peer.AlternateEndpoint, alternateEndpoint));
            if (!knownEndpoint)
            {
                peer = new P2PPeer { Endpoint = endpoint, AlternateEndpoint = alternateEndpoint };
                p2pPeers[peerId] = peer;
                shouldProbe = true;
            }
            else
            {
                peer.AlternateEndpoint = alternateEndpoint;
                if (!peer.Connected) shouldProbe = true;
            }
            if (shouldProbe) peer.NextProbeTicks = DateTime.UtcNow.Ticks + P2PProbeRetryTicks;
        }
        GunsawMultiplayerPlugin.LogInfo("P2P candidate for peer " + peerId + ": " + endpoint +
         (alternateEndpoint == null ? "" : " (alternate byte order: " + alternateEndpoint + ")") + ".");
        if (shouldProbe) SendDirectProbe(peerId);
    }

    private static bool TryAcceptDirectPacket(byte[] datagram, IPEndPoint remote, out ushort senderId)
    {
        senderId = 0;
        if (datagram.Length < 35 || datagram[4] != UdpDirectData || p2pKey == null) return false;
        senderId = BitConverter.ToUInt16(datagram, 5);
        if (senderId == 0 || senderId == localPeerId)
        {
            GunsawMultiplayerPlugin.LogInfo("P2P direct packet rejected from " + remote + ": invalid peer ID " + senderId + ".");
            return false;
        }
        for (var index = 0; index < P2PKeySize; index++)
            if (datagram[7 + index] != p2pKey[index])
            {
                GunsawMultiplayerPlugin.LogInfo("P2P direct packet rejected from " + remote + ": lobby key mismatch.");
                return false;
            }
        var wasConnected = false;
        lock (statusLock)
        {
            P2PPeer peer;
            if (!p2pPeers.TryGetValue(senderId, out peer))
            {
                GunsawMultiplayerPlugin.LogInfo("P2P direct packet rejected from " + remote + ": peer " + senderId + " has no candidate.");
                return false;
            }

            if (!EndpointsEqual(peer.Endpoint, remote)) peer.Endpoint = remote;
            wasConnected = peer.Connected;
            peer.Connected = true;
            peer.NextProbeTicks = 0;
            p2pPeers[senderId] = peer;
        }
        if (!wasConnected) GunsawMultiplayerPlugin.LogInfo("P2P direct path authenticated with peer " + senderId + " from " + remote + ".");
        if (BitConverter.ToInt32(datagram, 23) == 0 && BitConverter.ToUInt16(datagram, 27) == 0 &&
            BitConverter.ToUInt16(datagram, 29) == 1 && BitConverter.ToInt32(datagram, 31) == 0)
        {
            if (!wasConnected) SendDirectProbe(senderId);
            return false;
        }
        return true;
    }

    private static bool IsP2PConnected(ushort peerId)
    {
        lock (statusLock)
        {
            P2PPeer peer;
            return p2pPeers.TryGetValue(peerId, out peer) && peer.Connected;
        }
    }

    private static bool TryGetP2PEndpoint(ushort peerId, out IPEndPoint endpoint)
    {
        endpoint = null;
        if (connectionMode == ConnectionMode.Relay) return false;
        lock (statusLock)
        {
            P2PPeer peer;
            if (!p2pPeers.TryGetValue(peerId, out peer) || !peer.Connected) return false;
            endpoint = peer.Endpoint;
            return endpoint != null;
        }
    }

    private static void SendDirectProbe(ushort peerId)
    {
        IPEndPoint endpoint;
        IPEndPoint alternateEndpoint;
        if (!TryGetP2PCandidates(peerId, out endpoint, out alternateEndpoint)) return;
        var probe = new byte[35];
        Buffer.BlockCopy(udpMagic, 0, probe, 0, udpMagic.Length);
        probe[4] = UdpDirectData;
        Buffer.BlockCopy(BitConverter.GetBytes(localPeerId), 0, probe, 5, sizeof(ushort));
        Buffer.BlockCopy(p2pKey, 0, probe, 7, P2PKeySize);
        Buffer.BlockCopy(BitConverter.GetBytes((ushort)1), 0, probe, 29, sizeof(ushort));
        try { socket.Send(probe, probe.Length, endpoint); } catch (SocketException) { } catch (ObjectDisposedException) { }
        if (alternateEndpoint != null && !EndpointsEqual(endpoint, alternateEndpoint))
            try { socket.Send(probe, probe.Length, alternateEndpoint); }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
    }

    private static bool TryGetP2PCandidates(ushort peerId, out IPEndPoint endpoint,
        out IPEndPoint alternateEndpoint)
    {
        endpoint = null;
        alternateEndpoint = null;
        lock (statusLock)
        {
            P2PPeer peer;
            if (!p2pPeers.TryGetValue(peerId, out peer)) return false;
            endpoint = peer.Endpoint;
            alternateEndpoint = peer.AlternateEndpoint;
            return endpoint != null && p2pKey != null;
        }
    }

    private static bool EndpointsEqual(IPEndPoint left, IPEndPoint right)
    {
        return left != null && right != null && left.Port == right.Port && left.Address.Equals(right.Address);
    }

    private static bool ProcessReliablePacket(ref ArraySegment<byte> packet, ushort senderId)
    {
        var deliver = reliableChannel.TryUnwrap(packet, senderId, Stopwatch.GetTimestamp(), out packet, out var acknowledgement);
        if (acknowledgement != null)
            SendSerialized(PacketType.ReliableAck, default(ReliableAckPacket).Settings,
                acknowledgement, senderId, true);
        return deliver;
    }

    private static void StartSendWorker(UdpClient client, CancellationTokenSource cancellation)
    {
        sendQueue.Clear();
        var worker = new Thread(() => SendLoop(client, cancellation));
        worker.IsBackground = true;
        worker.Name = "Gunsaw UDP sender";
        worker.Priority = ThreadPriority.AboveNormal;
        sendThread = worker;
        worker.Start();
    }

    private static void SendLoop(UdpClient client, CancellationTokenSource cancellation)
    {
        var due = new List<OutboundPacket>();
        try
        {
            while (!cancellation.IsCancellationRequested && relayConnected && client == socket)
            {
                PumpSessionTransfers();
                var hasPacket = sendQueue.TryDequeue(out var packet);
                try
                {
                    if (hasPacket) SendPacketBlocking(client, cancellation, packet);
                    SendPendingReliablePackets(client, cancellation, due);
                }
                catch (OperationCanceledException) { return; }
                catch (ObjectDisposedException) { return; }
                catch (Exception exception)
                {
                    if (client != socket) return;
                    GunsawMultiplayerPlugin.LogInfo("UDP sender skipped a packet: " + exception.GetType().Name + ": " + exception.Message);
                    if (relayConnected) SetStatus("UDP send error; see multiplayer log.");
                }
                if (!hasPacket) sendSignal.WaitOne(25);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException exception)
        {
            if (relayConnected) SetStatus("UDP send failed: " + exception.Message);
        }
        catch (IOException exception)
        {
            if (relayConnected) SetStatus("UDP send failed: " + exception.Message);
        }
    }

    private static void SendPendingReliablePackets(UdpClient client, CancellationTokenSource cancellation, List<OutboundPacket> due)
    {
        reliableChannel.TakeInitialSends(Stopwatch.GetTimestamp(), due);
        foreach (var packet in due) SendPacketBlocking(client, cancellation, packet);
        reliableChannel.TakeDue(Stopwatch.GetTimestamp(), due);
        foreach (var packet in due) SendPacketBlocking(client, cancellation, packet);
    }

    private static void SendPacketBlocking(UdpClient client, CancellationTokenSource cancellation,
        OutboundPacket packet)
    {
        lock (sendLock)
        {
            if (client == null || client != socket || cancellation == null || cancellation.IsCancellationRequested || !relayConnected)
                throw new IOException("Relay connection is closed.");
            if (packet.Data == null) return;

            if (packet.Reliable && !reliableChannel.TryBeginSend(packet.SequenceId)) return;
            try
            {
                var targetId = packet.TargetId;
                var direct = TryGetP2PEndpoint(targetId, out var endpoint);
                if (!direct)
                {
                    endpoint = relayEndpoint;
                    if (connectionMode == ConnectionMode.P2P) relayFallback = true;
                }
                var fragmentCount = UdpDatagram.FragmentCount(packet.WireLength);
                var messageId = Interlocked.Increment(ref transportMessageSequence);
                for (var index = 0; index < fragmentCount; index++)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    var length = UdpDatagram.WriteFragment(sendBuffer, packet, messageId, index, localPeerId, direct ? p2pKey : null);
                    var sentTimestamp = Stopwatch.GetTimestamp();
                    client.Send(sendBuffer, length, endpoint);
                    if (packet.Reliable && index == fragmentCount - 1)
                        reliableChannel.MarkSent(packet.SequenceId, sentTimestamp);
                    Interlocked.Add(ref sentBytes, length);
                 //   AddOutgoingTrafficBytes(packet.Policy.Traffic, length);
                    Interlocked.Increment(ref sentPackets);
                    if (!packet.Priority && fragmentCount > 1 && sendQueue.TryDequeuePriority(out var urgent))
                        SendPacketBlocking(client, cancellation, urgent);
                    if (fragmentCount > 16 && (index & 3) == 3) Thread.Sleep(1);
                }
            }
            catch
            {
                if (packet.Reliable) reliableChannel.SendFailed(packet.SequenceId, Stopwatch.GetTimestamp());
                throw;
            }
        }
    }

  //  private static void AddOutgoingTrafficBytes(TrafficKind kind, int bytes)
  //  {
  //      if (kind == TrafficKind.Npc) Interlocked.Add(ref sentNpcBytes, bytes);
  //      else if (kind == TrafficKind.World) Interlocked.Add(ref sentWorldBytes, bytes);
  //      else if (kind == TrafficKind.Avatar) Interlocked.Add(ref sentAvatarBytes, bytes);
  //      else Interlocked.Add(ref sentOtherBytes, bytes);
  //  }

    private static bool HasUdpMagic(byte[] packet)
    {
        return packet != null && packet.Length >= udpMagic.Length &&
            packet[0] == udpMagic[0] && packet[1] == udpMagic[1] &&
            packet[2] == udpMagic[2] && packet[3] == udpMagic[3];
    }

    private static void CloseSocket()
    {
        UdpClient current;
        CancellationTokenSource cancellation;
        Thread worker;
        lock (statusLock)
        {
            current = socket;
            cancellation = socketCancellation;
            worker = sendThread;
            relayConnected = false;
            socket = null;
            socketCancellation = null;
            sendThread = null;
            relayEndpoint = null;
            p2pKey = null;
            p2pPeers.Clear();
        }
        cancellation?.Cancel();
        sendSignal.Set();
        current?.Close();
        
        if (worker != null && worker != Thread.CurrentThread) 
            worker.Join(1000);
        
        sendQueue.Clear();
        ClearSessionTransfers();
        reliableChannel.Reset();
        fragmentReassembler.Reset();
        current?.Dispose();
        cancellation?.Dispose();
    }
}