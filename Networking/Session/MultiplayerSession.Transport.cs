using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

internal static partial class MultiplayerSession
{
    private static void Send(byte[] header, byte[] payload, ushort targetId = 0)
    {
        var current = socket;
        if (current == null || !relayConnected) return;
        try
        {
            PacketHeader packetHeader;
            if (!PacketHeader.TryRead(header, out packetHeader)) return;
            SendPacket(PacketCodec.Encode(packetHeader.Type, payload), targetId);
        }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
        catch (IOException) { }
    }

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

        var payload = PacketCodec.Encode(DisconnectPacket.ClientClosed());
        var routed = new byte[sizeof(ushort) + payload.Length];
        Buffer.BlockCopy(BitConverter.GetBytes(targetId), 0, routed, 0, sizeof(ushort));
        Buffer.BlockCopy(payload, 0, routed, sizeof(ushort), payload.Length);
        try { SendPacketBlocking(current, cancellation, routed); }
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
                if (response != null && response.Length >= 7 && HasUdpMagic(response))
                {
                    if (response[4] == UdpAuthFailed)
                        throw new InvalidOperationException("UDP relay rejected the lobby key.");
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
            client.Close();
            socket = null;
            throw new IOException("UDP relay did not answer authentication.");
        }

        client.Client.ReceiveTimeout = 0;
        relayConnected = true;
        StartSendWorker(client, socketCancellation);
        return client;
    }

    private static byte[] ReadPacket(byte[] buffer, out ushort senderId)
    {
        senderId = 0;
        var current = socket;
        if (current == null || !relayConnected) return null;
        while (relayConnected)
        {
            IPEndPoint remote = null;
            var datagram = current.Receive(ref remote);
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
                metadata = 7;
            }
            else if (TryAcceptDirectPacket(datagram, remote, out senderId)) metadata = 23;
            else continue;
            if (datagram.Length < metadata + 12) continue;
            var messageId = BitConverter.ToInt32(datagram, metadata);
            var fragmentIndex = BitConverter.ToUInt16(datagram, metadata + 4);
            var fragmentCount = BitConverter.ToUInt16(datagram, metadata + 6);
            var totalLength = BitConverter.ToInt32(datagram, metadata + 8);
            var payloadOffset = metadata + 12;
            var fragmentLength = datagram.Length - payloadOffset;
            if (senderId == 0 || fragmentCount == 0 || fragmentIndex >= fragmentCount ||
                totalLength < 0 || totalLength > 4 * 1024 * 1024 || fragmentLength < 0) continue;
            Interlocked.Add(ref receivedBytes, datagram.Length);
            Interlocked.Increment(ref receivedPackets);
            if (fragmentCount == 1)
            {
                if (fragmentLength != totalLength) continue;
                var single = new byte[fragmentLength];
                if (fragmentLength > 0) Buffer.BlockCopy(datagram, payloadOffset, single, 0, fragmentLength);
                return single;
            }
            var key = ((long)senderId << 32) | (uint)messageId;
            FragmentTransfer transfer;
            if (!fragmentTransfers.TryGetValue(key, out transfer) || transfer.TotalLength != totalLength ||
                transfer.Fragments.Length != fragmentCount)
            {
                transfer = new FragmentTransfer(totalLength, fragmentCount);
                fragmentTransfers[key] = transfer;
            }
            if (transfer.Fragments[fragmentIndex] == null)
            {
                var fragment = new byte[fragmentLength];
                if (fragmentLength > 0) Buffer.BlockCopy(datagram, payloadOffset, fragment, 0, fragmentLength);
                transfer.Fragments[fragmentIndex] = fragment;
                transfer.Received++;
            }
            CleanupFragmentTransfers();
            if (transfer.Received != transfer.Fragments.Length) continue;
            var packet = new byte[transfer.TotalLength];
            var destination = 0;
            foreach (var fragment in transfer.Fragments)
            {
                if (fragment == null || destination + fragment.Length > packet.Length) { packet = null; break; }
                Buffer.BlockCopy(fragment, 0, packet, destination, fragment.Length);
                destination += fragment.Length;
            }
            fragmentTransfers.Remove(key);
            if (packet != null && destination == packet.Length) return packet;
        }
        return null;
    }

    private static void EnableP2P()
    {
        if (p2pKey == null || p2pKey.Length != P2PKeySize)
        {
            LogP2PWarning("P2P unavailable: UDP relay did not provide a valid P2P key.");
            return;
        }
        LogP2PInfo("P2P enabled; waiting for relay candidates.");
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
        LogP2PInfo("P2P candidate for peer " + peerId + ": " + endpoint +
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
            LogP2PWarning("P2P direct packet rejected from " + remote + ": invalid peer ID " + senderId + ".");
            return false;
        }
        for (var index = 0; index < P2PKeySize; index++)
            if (datagram[7 + index] != p2pKey[index])
            {
                LogP2PWarning("P2P direct packet rejected from " + remote + ": lobby key mismatch.");
                return false;
            }
        var wasConnected = false;
        lock (statusLock)
        {
            P2PPeer peer;
            if (!p2pPeers.TryGetValue(senderId, out peer))
            {
                LogP2PWarning("P2P direct packet rejected from " + remote + ": peer " + senderId + " has no candidate.");
                return false;
            }

            if (!EndpointsEqual(peer.Endpoint, remote)) peer.Endpoint = remote;
            wasConnected = peer.Connected;
            peer.Connected = true;
            peer.NextProbeTicks = 0;
            p2pPeers[senderId] = peer;
        }
        if (!wasConnected) LogP2PInfo("P2P direct path authenticated with peer " + senderId + " from " + remote + ".");
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

    private static void LogP2PInfo(string message)
    {
        sessionLogger?.LogInfo(message);
        MultiplayerDiagnosticLog.Write(isHost, "INFO", message);
    }

    private static void LogP2PWarning(string message)
    {
        sessionLogger?.LogWarning(message);
        MultiplayerDiagnosticLog.Write(isHost, "WARN", message);
    }

    private static void SendPacket(byte[] packet, ushort targetId = 0, bool? priority = null,
        bool allowReliable = true, bool sendImmediately = false)
    {
        if (packet == null || packet.Length == 0) return;
        if (socket == null || !relayConnected) throw new IOException("Relay connection is closed.");

        if (targetId == 0 && connectionMode != ConnectionMode.Relay)
        {
            var targets = PeerIds();
            if (targets.Length > 0)
            {
                foreach (var peerId in targets) SendPacket(packet, peerId, priority, allowReliable, sendImmediately);
                return;
            }
        }

        if (allowReliable && ShouldSendReliable(packet))
        {
            if (targetId == 0 && isHost)
            {
                var targetPeers = PeerIds();
                if (targetPeers.Length > 0)
                {
                    foreach (var peerId in targetPeers) SendPacket(packet, peerId, priority, true);
                    return;
                }
            }
            var reliableId = reliableChannel.NextSequenceId();
            var wrapped = PacketCodec.Encode(new ReliablePacket(reliableId, packet));
            var routedReliable = RoutePacket(wrapped, targetId);
            reliableChannel.Track(reliableId, targetId, routedReliable, DateTime.UtcNow.Ticks);
            if (sendImmediately) SendPacketImmediately(wrapped, targetId);
            else EnqueueRoutedPacket(routedReliable, true);
            return;
        }

        EnqueueRoutedPacket(RoutePacket(packet, targetId), priority);
    }

    private static byte[] RoutePacket(byte[] packet, ushort targetId)
    {
        var routed = new byte[sizeof(ushort) + packet.Length];
        Buffer.BlockCopy(BitConverter.GetBytes(targetId), 0, routed, 0, sizeof(ushort));
        Buffer.BlockCopy(packet, 0, routed, sizeof(ushort), packet.Length);
        return routed;
    }

    private static void SendPacketImmediately(byte[] packet, ushort targetId)
    {
        var current = socket;
        var cancellation = socketCancellation;
        if (current == null || cancellation == null || !relayConnected)
            throw new IOException("Relay connection is closed.");
        SendPacketBlocking(current, cancellation, RoutePacket(packet, targetId));
    }


    private static void EnqueueRoutedPacket(byte[] routed, bool? priority = null)
    {
        var queue = (priority ?? IsLatencySensitivePacket(routed)) ? prioritySendQueue : sendQueue;
        lock (sendQueueLock)
        {
            if (IsReplaceableStatePacket(routed))
            {
                var pending = queue.Count;
                for (var index = 0; index < pending; index++)
                {
                    var queued = queue.Dequeue();
                    if (!SameReplaceableState(queued, routed)) queue.Enqueue(queued);
                }
            }
            if (sendQueue.Count + prioritySendQueue.Count >= MaxQueuedPackets)
            {
                SetStatus("Network send queue is overloaded; dropping a packet.");
                return;
            }
            queue.Enqueue(routed);
        }
        sendSignal.Set();
    }

    private static bool ShouldSendReliable(byte[] packet)
    {
        return Matches(packet, hello) || HasHeader(packet, hello) ||
            Matches(packet, accepted) || HasHeader(packet, accepted) ||
            HasHeader(packet, sceneHeader) || HasHeader(packet, settingsHeader) ||
            HasHeader(packet, customLevelHeader) ||
            HasHeader(packet, worldDamageHeader) || HasHeader(packet, npcDamageHeader) ||
            HasHeader(packet, worldEnvironmentHeader) ||
            HasHeader(packet, worldInteractionHeader) ||
            HasHeader(packet, playerDamageHeader) || HasHeader(packet, pvpDamageHeader) ||
            HasHeader(packet, playerTeleportHeader);
    }

    private static bool ProcessReliablePacket(ref byte[] packet, ushort senderId)
    {
        byte[] acknowledgement;
        if (!reliableChannel.TryUnwrap(packet, senderId, out packet, out acknowledgement)) return false;
        if (acknowledgement != null) SendPacket(acknowledgement, senderId, true, false);
        return true;
    }

    private static bool IsReplaceableStatePacket(byte[] packet)
    {
        return HasRoutedHeader(packet, identityHeader) ||
            HasRoutedHeader(packet, snapshotHeader) ||
            HasRoutedHeader(packet, worldHeader) ||
            HasRoutedHeader(packet, worldInputHeader) ||
            HasRoutedHeader(packet, npcHeader);
    }

    private static bool IsLatencySensitivePacket(byte[] packet)
    {
        return !HasRoutedHeader(packet, worldHeader) && !HasRoutedHeader(packet, npcHeader) &&
            !HasRoutedHeader(packet, worldEnvironmentHeader) &&
            !HasRoutedHeader(packet, customLevelHeader) && !HasRoutedHeader(packet, sceneHeader);
    }

    private static bool SameReplaceableState(byte[] left, byte[] right)
    {
        if (!IsReplaceableStatePacket(left) || !IsReplaceableStatePacket(right) ||
            left.Length < sizeof(ushort) + hello.Length || right.Length < sizeof(ushort) + hello.Length)
            return false;
        if (left[0] != right[0] || left[1] != right[1] ||
            left[sizeof(ushort) + hello.Length - 1] != right[sizeof(ushort) + hello.Length - 1])
            return false;

        if (HasRoutedHeader(right, npcHeader))
        {
            var rightOffset = sizeof(ushort) + npcHeader.Length;
            if (right.Length < rightOffset + 8 ||
                BitConverter.ToUInt16(right, rightOffset + 4) != 0) return false;
            var leftOffset = sizeof(ushort) + npcHeader.Length;
            if (left.Length < leftOffset + 8) return false;
            return BitConverter.ToInt32(left, leftOffset) !=
                BitConverter.ToInt32(right, rightOffset);
        }
        return true;
    }

    private static bool HasRoutedHeader(byte[] packet, byte[] header)
    {
        if (packet == null || header == null || packet.Length < sizeof(ushort) + header.Length) return false;
        for (var index = 0; index < header.Length; index++)
            if (packet[sizeof(ushort) + index] != header[index]) return false;
        return true;
    }

    private static void StartSendWorker(UdpClient client, CancellationTokenSource cancellation)
    {
        lock (sendQueueLock)
        {
            sendQueue.Clear();
            prioritySendQueue.Clear();
        }
        var worker = new Thread(() => SendLoop(client, cancellation));
        worker.IsBackground = true;
        worker.Name = "Gunsaw UDP sender";
        worker.Priority = ThreadPriority.AboveNormal;
        sendThread = worker;
        worker.Start();
    }

    private static void SendLoop(UdpClient client, CancellationTokenSource cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested && relayConnected)
            {
                byte[] packet = null;
                lock (sendQueueLock)
                {
                    if (prioritySendQueue.Count > 0) packet = prioritySendQueue.Dequeue();
                    else if (sendQueue.Count > 0) packet = sendQueue.Dequeue();
                }
                if (packet != null) SendPacketBlocking(client, cancellation, packet);
                ResendReliablePackets(client, cancellation);
                if (packet == null) sendSignal.WaitOne(25);
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

    private static void ResendReliablePackets(UdpClient client, CancellationTokenSource cancellation)
    {
        var due = reliableChannel.TakeDue(DateTime.UtcNow.Ticks);
        foreach (var packet in due) SendPacketBlocking(client, cancellation, packet);
    }

    private static void SendPacketBlocking(UdpClient client, CancellationTokenSource cancellation,
        byte[] routedPacket)
    {
        lock (sendLock)
        {
            if (client == null || cancellation == null || cancellation.IsCancellationRequested || !relayConnected)
                throw new IOException("Relay connection is closed.");
            if (routedPacket == null || routedPacket.Length < sizeof(ushort)) return;

            var targetId = BitConverter.ToUInt16(routedPacket, 0);
            IPEndPoint directEndpoint;
            if (TryGetP2PEndpoint(targetId, out directEndpoint))
            {
                SendDirectPacketBlocking(client, routedPacket, directEndpoint);
                return;
            }

            if (connectionMode == ConnectionMode.P2P) relayFallback = true;
            var totalLength = routedPacket.Length - sizeof(ushort);
            var messageId = Interlocked.Increment(ref transportMessageSequence);
            var fragmentCount = Math.Max(1, (totalLength + UdpFragmentPayload - 1) / UdpFragmentPayload);
            var trafficKind = ClassifyOutgoingTraffic(routedPacket);
            if (fragmentCount > ushort.MaxValue) throw new InvalidDataException("UDP packet is too large.");

            for (var index = 0; index < fragmentCount; index++)
            {
                var sourceOffset = sizeof(ushort) + index * UdpFragmentPayload;
                var length = Math.Min(UdpFragmentPayload, totalLength - index * UdpFragmentPayload);
                var datagram = new byte[19 + length];
                Buffer.BlockCopy(udpMagic, 0, datagram, 0, udpMagic.Length);
                datagram[4] = UdpData;
                Buffer.BlockCopy(BitConverter.GetBytes(targetId), 0, datagram, 5, sizeof(ushort));
                Buffer.BlockCopy(BitConverter.GetBytes(messageId), 0, datagram, 7, sizeof(int));
                Buffer.BlockCopy(BitConverter.GetBytes((ushort)index), 0, datagram, 11, sizeof(ushort));
                Buffer.BlockCopy(BitConverter.GetBytes((ushort)fragmentCount), 0, datagram, 13, sizeof(ushort));
                Buffer.BlockCopy(BitConverter.GetBytes(totalLength), 0, datagram, 15, sizeof(int));
                if (length > 0) Buffer.BlockCopy(routedPacket, sourceOffset, datagram, 19, length);
                client.Send(datagram, datagram.Length, relayEndpoint);
                Interlocked.Add(ref sentBytes, datagram.Length);
                AddOutgoingTrafficBytes(trafficKind, datagram.Length);
                Interlocked.Increment(ref sentPackets);
            }
        }
    }

    private static void SendDirectPacketBlocking(UdpClient client, byte[] routedPacket, IPEndPoint endpoint)
    {
        var totalLength = routedPacket.Length - sizeof(ushort);
        var messageId = Interlocked.Increment(ref transportMessageSequence);
        var fragmentCount = Math.Max(1, (totalLength + UdpFragmentPayload - 1) / UdpFragmentPayload);
        var trafficKind = ClassifyOutgoingTraffic(routedPacket);
        for (var index = 0; index < fragmentCount; index++)
        {
            var sourceOffset = sizeof(ushort) + index * UdpFragmentPayload;
            var length = Math.Min(UdpFragmentPayload, totalLength - index * UdpFragmentPayload);
            var datagram = new byte[35 + length];
            Buffer.BlockCopy(udpMagic, 0, datagram, 0, udpMagic.Length);
            datagram[4] = UdpDirectData;
            Buffer.BlockCopy(BitConverter.GetBytes(localPeerId), 0, datagram, 5, sizeof(ushort));
            Buffer.BlockCopy(p2pKey, 0, datagram, 7, P2PKeySize);
            Buffer.BlockCopy(BitConverter.GetBytes(messageId), 0, datagram, 23, sizeof(int));
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)index), 0, datagram, 27, sizeof(ushort));
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)fragmentCount), 0, datagram, 29, sizeof(ushort));
            Buffer.BlockCopy(BitConverter.GetBytes(totalLength), 0, datagram, 31, sizeof(int));
            if (length > 0) Buffer.BlockCopy(routedPacket, sourceOffset, datagram, 35, length);
            client.Send(datagram, datagram.Length, endpoint);
            Interlocked.Add(ref sentBytes, datagram.Length);
            AddOutgoingTrafficBytes(trafficKind, datagram.Length);
            Interlocked.Increment(ref sentPackets);
        }
    }

    private static byte ClassifyOutgoingTraffic(byte[] routedPacket)
    {
        const int packetOffset = sizeof(ushort);
        if (HasHeaderAt(routedPacket, packetOffset, npcHeader)) return 1;
        if (HasHeaderAt(routedPacket, packetOffset, worldHeader)) return 2;
        if (HasHeaderAt(routedPacket, packetOffset, snapshotHeader)) return 3;
        return 0;
    }

    private static bool HasHeaderAt(byte[] packet, int offset, byte[] header)
    {
        if (packet == null || header == null || offset < 0 || packet.Length < offset + header.Length) return false;
        for (var index = 0; index < header.Length; index++)
            if (packet[offset + index] != header[index]) return false;
        return true;
    }

    private static void AddOutgoingTrafficBytes(byte kind, int bytes)
    {
        if (kind == 1) Interlocked.Add(ref sentNpcBytes, bytes);
        else if (kind == 2) Interlocked.Add(ref sentWorldBytes, bytes);
        else if (kind == 3) Interlocked.Add(ref sentAvatarBytes, bytes);
        else Interlocked.Add(ref sentOtherBytes, bytes);
    }

    private static bool HasUdpMagic(byte[] packet)
    {
        return packet != null && packet.Length >= udpMagic.Length &&
            packet[0] == udpMagic[0] && packet[1] == udpMagic[1] &&
            packet[2] == udpMagic[2] && packet[3] == udpMagic[3];
    }

    private static void CleanupFragmentTransfers()
    {
        if (fragmentTransfers.Count < 128) return;
        var cutoff = DateTime.UtcNow.Ticks - TimeSpan.TicksPerSecond * 5;
        var stale = new List<long>();
        foreach (var pair in fragmentTransfers)
            if (pair.Value.CreatedTicks < cutoff) stale.Add(pair.Key);
        foreach (var key in stale) fragmentTransfers.Remove(key);
    }

    private static void CloseSocket(bool graceful = false)
    {
        var current = socket;
        var cancellation = socketCancellation;
        relayConnected = false;
        socket = null;
        socketCancellation = null;
        relayEndpoint = null;
        p2pKey = null;
        p2pPeers.Clear();
        try { if (cancellation != null) cancellation.Cancel(); } catch { }
        sendSignal.Set();
        lock (sendQueueLock)
        {
            sendQueue.Clear();
            prioritySendQueue.Clear();
        }
        reliableChannel.Reset();
        fragmentTransfers.Clear();
        try { if (current != null) current.Close(); } catch { }
        if (current != null) current.Dispose();
        if (cancellation != null) cancellation.Dispose();
        sendThread = null;
    }

    private static byte[] PacketWithPayload(byte[] header, byte[] payload)
    {
        PacketHeader packetHeader;
        return PacketHeader.TryRead(header, out packetHeader)
            ? PacketCodec.Encode(packetHeader.Type, payload)
            : new byte[0];
    }

}
