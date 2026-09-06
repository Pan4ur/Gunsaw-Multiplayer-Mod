using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/*
     lol
     Gunsaw using Unity 2021.1.23f1. According to Unity’s official release notes, the later version 2021.3.12f1
     replaced the LoadImage function’s libjpeg-turbo version 1.3.1 with 2.1.2 specifically due to security issues.
     The old decoder contained processing for specially crafted JPEGs that involved reading outside the buffer and other
     memory-safety errors
 */
internal sealed class GraffitiSystem : MonoBehaviour
{
    private const int MaxImageDimension = 1280;
    private const int NetworkImageHeaderBytes = 8;
    internal const int MaxImageBytes = NetworkImageHeaderBytes + MaxImageDimension * MaxImageDimension * 2;
    internal const int MinImageBytes = NetworkImageHeaderBytes + 2;
    internal const int MaxPacketPayloadBytes = GraffitiPacket.MetadataBytes + MaxImageBytes;
    private const int MaxSourceImageBytes = 1_048_576;
    private const int MaxLibraryImages = 128;
    private const int MaxPlacedGraffiti = 32;
    private const int MaxIncomingPackets = 32;
    private const int MaxProcessedPerFrame = 2;
    private const int MaxAcceptedIds = 128;
    private const float MaxCoordinate = 100_000f;
    private static readonly byte[] networkImageMagic = { 0x47, 0x53, 0x47, 0x32 };
    private static GraffitiSystem instance;
    private static volatile bool showGraffiti;
    private readonly Dictionary<string, LibraryImage> library = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ulong, Graffiti> placed = new();
    private readonly Queue<ulong> placedOrder = new();
    private readonly Queue<IncomingGraffiti> incoming = new();
    private readonly Dictionary<ushort, long> nextPeerGraffitiAt = new();
    private readonly HashSet<ulong> acceptedIds = new();
    private readonly Queue<ulong> acceptedIdOrder = new();
    private readonly HashSet<ushort> queuedSenders = new();
    private long nextGlobalGraffitiAt;
    private float nextHostBroadcastAt;
    private string folder;
    private string selectedPath;
    private GameObject preview;
    private SpriteRenderer previewRenderer;
    private float pressStarted;
    private bool holdingT, pickerOpen, placing;
    private float previewScale = 1f, previewRotation;
    private FileSystemWatcher libraryWatcher;
    private int libraryDirty;
    private float nextLibraryScanAt;
    private float noImagesNoticeUntil;
    private Canvas interfaceCanvas;
    private GameObject pickerRoot, pickerContent, noticeRoot;
    private ScrollRect pickerScroll;
    private bool pickerDirty;
    private bool interfaceFontApplied;
    private bool cursorVisibilityCaptured, previousCursorVisibility;
    private CursorLockMode previousCursorLockMode;
    private readonly Dictionary<string, Image> pickerItems = new(StringComparer.OrdinalIgnoreCase);
    private int sceneHandle = int.MinValue;
    private int nextSortingOrder = 100;

    internal static void Initialize(bool headless)
    {
        if (instance != null) return;
        var root = new GameObject("MP Graffiti System");
        DontDestroyOnLoad(root);
        instance = root.AddComponent<GraffitiSystem>();
        showGraffiti = !headless && PlayerPrefs.GetInt("showgraffiti", 1) != 0;
        if (headless) return;
        instance.folder = Path.Combine(BepInEx.Paths.PluginPath, "Graffiti");
        Directory.CreateDirectory(instance.folder);
        instance.ScanLibrary();
        instance.libraryWatcher = new FileSystemWatcher(instance.folder, "*.*");
        instance.libraryWatcher.Changed += instance.OnLibraryChanged;
        instance.libraryWatcher.Created += instance.OnLibraryChanged;
        instance.libraryWatcher.Deleted += instance.OnLibraryChanged;
        instance.libraryWatcher.Renamed += instance.OnLibraryRenamed;
        instance.libraryWatcher.EnableRaisingEvents = true;
        instance.CreateInterface();
    }

    private void Update()
    {
        var activeScene = SceneManager.GetActiveScene();
        if (sceneHandle != activeScene.handle)
        {
            sceneHandle = activeScene.handle;
            ClearPlaced();
        }

        if (Volatile.Read(ref libraryDirty) != 0 && Time.unscaledTime >= nextLibraryScanAt)
        {
            Interlocked.Exchange(ref libraryDirty, 0);
            nextLibraryScanAt = Time.unscaledTime + 1f;
            ScanLibrary();
        }
        
        ProcessIncoming();
        
        if (noticeRoot != null) 
            noticeRoot.SetActive(Time.unscaledTime < noImagesNoticeUntil);
        
        if (activeScene.name == "LevelSelect" || activeScene.name == "LevelLoader" || Camera.main == null)
        {
            Cancel();
            return;
        }

        if (MultiplayerHud.IsTyping)
        {
            Cancel();
            return;
        }

        HandleInput();
        UpdatePreview();
    }

    internal static bool TryBeginReceive(ushort senderId)
    {
        if (instance == null || senderId == 0) return false;
        var hosting = MultiplayerSession.IsHosting;
        if (hosting)
        {
            if (!MultiplayerSession.HasPeer(senderId)) return false;
        }
        else if (senderId != MultiplayerSession.HostPeerId || !ShowGraffiti)
            return false;

        lock (instance.incoming)
        {
            if (instance.incoming.Count >= MaxIncomingPackets || hosting && instance.queuedSenders.Contains(senderId) ||
                !instance.AllowIncomingLocked(hosting, senderId))
                return false;
            return true;
        }
    }

    internal static void Receive(ushort senderId, GraffitiPacket packet)
    {
        if (instance == null || senderId == 0 || packet.SceneEpoch != MultiplayerSession.CurrentSceneEpoch ||
            !IsPacketValid(packet))
            return;

        var hosting = MultiplayerSession.IsHosting;
        if (hosting)
        {
            if (!MultiplayerSession.HasPeer(senderId)) return;
        }
        else
        {
            if (senderId != MultiplayerSession.HostPeerId || !ShowGraffiti) return;
        }

        lock (instance.incoming)
        {
            var ownerId = hosting ? senderId : packet.OwnerId;
            var key = ((ulong)ownerId << 32) | packet.Id;
            if (instance.incoming.Count >= MaxIncomingPackets || instance.acceptedIds.Contains(key)) return;
            instance.acceptedIds.Add(key);
            instance.acceptedIdOrder.Enqueue(key);
            while (instance.acceptedIdOrder.Count > MaxAcceptedIds)
                instance.acceptedIds.Remove(instance.acceptedIdOrder.Dequeue());
            if (hosting) instance.queuedSenders.Add(senderId);
            instance.incoming.Enqueue(new IncomingGraffiti(senderId, packet));
        }
    }

    private void HandleInput()
    {
        if (Input.GetKeyDown(Controls.keys[Controls.GRAFFITI]))
        {
            holdingT = true;
            pressStarted = Time.unscaledTime;
            pickerOpen = false;
        }

        if (holdingT && Input.GetKey(Controls.keys[Controls.GRAFFITI]) && !pickerOpen && Time.unscaledTime - pressStarted >= 0.15f)
        {
            pickerOpen = true;
            placing = false;
            
            if (preview != null)
                preview.SetActive(false);
            
            pickerDirty = true;
            if (!cursorVisibilityCaptured)
            {
                previousCursorVisibility = Cursor.visible;
                previousCursorLockMode = Cursor.lockState;
                cursorVisibilityCaptured = true;
            }

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        if (holdingT && Input.GetKeyUp(Controls.keys[Controls.GRAFFITI]))
        {
            holdingT = false;
            if (pickerOpen)
            {
                pickerOpen = false;
                
                if (pickerRoot != null) 
                    pickerRoot.SetActive(false);
                
                RestoreCursorVisibility();
                
                if (!string.IsNullOrEmpty(selectedPath)) 
                    BeginPlacement(selectedPath);
            }
            else 
                BeginPlacement(selectedPath ?? FirstImagePath());
        }

        if (!placing) 
            return;
        
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cancel();
            return;
        }

        var wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) > 0.001f)
        {
            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) 
                previewRotation += wheel * 12f;
            else 
                previewScale = Mathf.Clamp(previewScale * (wheel > 0f ? 1.1f : 0.9f), 0.1f, 5f);
        }

        if (Input.GetMouseButtonDown(0)) 
            Place();
    }

    private void BeginPlacement(string path)
    {
        if (string.IsNullOrEmpty(path) || !library.TryGetValue(path, out var image))
        {
            noImagesNoticeUntil = Time.unscaledTime + 3f;
            return;
        }

        selectedPath = path;
        placing = true;
        previewScale = 1f;
        previewRotation = 0f;
        
        if (preview == null)
        {
            preview = new GameObject("MP Graffiti Preview");
            previewRenderer = preview.AddComponent<SpriteRenderer>();
            previewRenderer.sortingOrder = 32000;
            previewRenderer.color = new Color(1f, 1f, 1f, 0.65f);
        }

        previewRenderer.sprite = image.Sprite;
        preview.SetActive(true);
    }

    private void UpdatePreview()
    {
        if (!placing || preview == null) 
            return;
        
        var camera = Camera.main;
        if (camera == null) 
            return;
        
        var point = camera.ScreenToWorldPoint(Input.mousePosition);
        
        preview.transform.position = new Vector3(point.x, point.y, 0f);
        preview.transform.rotation = Quaternion.Euler(0f, 0f, previewRotation);
        preview.transform.localScale = Vector3.one * previewScale;
    }

    private void Place()
    {
        if (previewRenderer == null || previewRenderer.sprite == null || !library.TryGetValue(selectedPath, out var image)) 
            return;
        
        var point = preview.transform.position;
        var rotation = Mathf.Repeat(previewRotation + 180f, 360f) - 180f;
        var ownerId = MultiplayerSession.LocalPeerId == 0 ? ushort.MaxValue : MultiplayerSession.LocalPeerId;
        uint id;
        do id = unchecked((uint)Guid.NewGuid().GetHashCode()); while (id == 0);
        var packet = new GraffitiPacket(MultiplayerSession.CurrentSceneEpoch, ownerId, id, point.x, point.y,
            previewScale, rotation, image.Bytes);
        Add(packet);
        
        if (MultiplayerSession.IsHosting)
            MultiplayerSession.Send(packet);
        else if (MultiplayerSession.IsActive && MultiplayerSession.HostPeerId != 0)
            MultiplayerSession.Send(packet, MultiplayerSession.HostPeerId);
        
        Cancel();
    }

    private void ProcessIncoming()
    {
        if (MultiplayerSession.IsHosting && Time.unscaledTime < nextHostBroadcastAt) return;
        for (var processed = 0; processed < MaxProcessedPerFrame; processed++)
        {
            IncomingGraffiti item;
            lock (incoming)
            {
                if (incoming.Count == 0) break;
                item = incoming.Dequeue();
                queuedSenders.Remove(item.SenderId);
            }

            var packet = item.Packet;
            if (packet.SceneEpoch != MultiplayerSession.CurrentSceneEpoch || packet.Image == null || packet.Image.Length == 0) 
                continue;

            if (MultiplayerSession.IsHosting)
            {
                packet = new GraffitiPacket(packet.SceneEpoch, item.SenderId, packet.Id, packet.X, packet.Y,
                    packet.Scale, packet.Rotation, packet.Image);
                if (!GunsawMultiplayerPlugin.IsHeadlessMode && ShowGraffiti && !Add(packet)) continue;
                MultiplayerSession.Send(packet);
                nextHostBroadcastAt = Time.unscaledTime + 1f;
                break;
            }
            else if (ShowGraffiti)
                Add(packet);
        }
    }

    private bool Add(GraffitiPacket packet)
    {
        var key = PacketKey(packet);
        if (placed.ContainsKey(key) || !IsPacketValid(packet)) return false;
        Texture2D texture;
        if (!TryCreateTexture(packet.Image, out texture)) return false;
        while (placed.Count >= MaxPlacedGraffiti && placedOrder.Count > 0)
        {
            var oldestKey = placedOrder.Dequeue();
            if (!placed.TryGetValue(oldestKey, out var oldest)) continue;
            oldest.Destroy();
            placed.Remove(oldestKey);
        }

        var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
        var gameObject = new GameObject("MP Graffiti");
        var renderer = gameObject.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = ++nextSortingOrder;
        gameObject.transform.position = new Vector3(packet.X, packet.Y, 0f);
        gameObject.transform.rotation = Quaternion.Euler(0f, 0f, packet.Rotation);
        gameObject.transform.localScale = Vector3.one * packet.Scale;
        gameObject.SetActive(ShowGraffiti);
        placed.Add(key, new Graffiti(packet, gameObject, texture, sprite));
        placedOrder.Enqueue(key);
        PlaySprayer(gameObject.transform.position);
        return true;
    }

    private static bool IsPacketValid(GraffitiPacket packet) => packet.OwnerId != 0 && packet.Id != 0 &&
                                                               IsBounded(packet.X, MaxCoordinate) &&
                                                               IsBounded(packet.Y, MaxCoordinate) &&
                                                               packet.Scale >= 0.1f && packet.Scale <= 5f &&
                                                               IsBounded(packet.Rotation, 360f) &&
                                                               IsNetworkImageValid(packet.Image);

    private static bool IsBounded(float value, float limit) => !float.IsNaN(value) && !float.IsInfinity(value) &&
                                                                value >= -limit && value <= limit;

    private bool AllowIncomingLocked(bool hosting, ushort peerId)
    {
        if (peerId == 0) return false;
        var now = DateTime.UtcNow.Ticks;
        long peerLimit;
        if (hosting)
        {
            if (nextPeerGraffitiAt.TryGetValue(peerId, out peerLimit) && now < peerLimit) return false;
            nextPeerGraffitiAt[peerId] = now + TimeSpan.TicksPerSecond * 3;
        }
        else
        {
            if (now < nextGlobalGraffitiAt) return false;
            nextGlobalGraffitiAt = now + TimeSpan.TicksPerSecond;
        }
        return true;
    }

    private static ulong PacketKey(GraffitiPacket packet) => ((ulong)packet.OwnerId << 32) | packet.Id;

    private static bool IsNetworkImageValid(byte[] bytes)
    {
        if (bytes == null || bytes.Length < MinImageBytes || bytes.Length > MaxImageBytes) return false;
        for (var index = 0; index < networkImageMagic.Length; index++)
            if (bytes[index] != networkImageMagic[index]) return false;
        var width = bytes[4] | bytes[5] << 8;
        var height = bytes[6] | bytes[7] << 8;
        if (width < 1 || height < 1 || width > MaxImageDimension || height > MaxImageDimension) return false;
        var pixelBytes = checked(width * height * 2);
        return bytes.Length == NetworkImageHeaderBytes + pixelBytes;
    }

    private static bool TryCreateTexture(byte[] bytes, out Texture2D texture)
    {
        texture = null;
        if (!IsNetworkImageValid(bytes)) return false;
        var width = bytes[4] | bytes[5] << 8;
        var height = bytes[6] | bytes[7] << 8;
        var raw = new byte[bytes.Length - NetworkImageHeaderBytes];
        Buffer.BlockCopy(bytes, NetworkImageHeaderBytes, raw, 0, raw.Length);
        try
        {
            texture = new Texture2D(width, height, TextureFormat.RGBA4444, false);
            texture.LoadRawTextureData(raw);
            texture.Apply(false, true);
            return true;
        }
        catch (Exception)
        {
            if (texture != null) Destroy(texture);
            texture = null;
            return false;
        }
    }

    private static byte[] EncodeTexture(Texture2D texture)
    {
        if (texture == null || texture.width < 1 || texture.height < 1 ||
            texture.width > MaxImageDimension || texture.height > MaxImageDimension) return null;
        var pixels = texture.GetPixels32();
        if (pixels == null || pixels.Length != texture.width * texture.height) return null;
        try
        {
            var bytes = new byte[NetworkImageHeaderBytes + pixels.Length * 2];
            Buffer.BlockCopy(networkImageMagic, 0, bytes, 0, networkImageMagic.Length);
            bytes[4] = (byte)texture.width;
            bytes[5] = (byte)(texture.width >> 8);
            bytes[6] = (byte)texture.height;
            bytes[7] = (byte)(texture.height >> 8);
            var offset = NetworkImageHeaderBytes;
            for (var index = 0; index < pixels.Length; index++)
            {
                var pixel = pixels[index];
                var packed = (ushort)((pixel.r >> 4) << 12 |
                                      (pixel.g >> 4) << 8 |
                                      (pixel.b >> 4) << 4 |
                                      pixel.a >> 4);
                bytes[offset++] = (byte)packed;
                bytes[offset++] = (byte)(packed >> 8);
            }
            return bytes;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsSourceImageSizeAllowed(byte[] bytes)
    {
        int width;
        int height;
        if (TryReadPngSize(bytes, out width, out height) || TryReadJpegSize(bytes, out width, out height))
            return width >= 1 && height >= 1 && width <= MaxImageDimension && height <= MaxImageDimension;
        return false;
    }

    private static bool TryReadPngSize(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (bytes == null || bytes.Length < 24 || bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4e ||
            bytes[3] != 0x47 || bytes[4] != 0x0d || bytes[5] != 0x0a || bytes[6] != 0x1a || bytes[7] != 0x0a ||
            bytes[8] != 0 || bytes[9] != 0 || bytes[10] != 0 || bytes[11] != 13 || bytes[12] != 0x49 ||
            bytes[13] != 0x48 || bytes[14] != 0x44 || bytes[15] != 0x52) return false;
        var unsignedWidth = (uint)(bytes[16] << 24 | bytes[17] << 16 | bytes[18] << 8 | bytes[19]);
        var unsignedHeight = (uint)(bytes[20] << 24 | bytes[21] << 16 | bytes[22] << 8 | bytes[23]);
        if (unsignedWidth > int.MaxValue || unsignedHeight > int.MaxValue) return false;
        width = (int)unsignedWidth;
        height = (int)unsignedHeight;
        return true;
    }

    private static bool TryReadJpegSize(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (bytes == null || bytes.Length < 4 || bytes[0] != 0xff || bytes[1] != 0xd8) return false;
        var offset = 2;
        while (offset < bytes.Length)
        {
            if (bytes[offset++] != 0xff) return false;
            while (offset < bytes.Length && bytes[offset] == 0xff) offset++;
            if (offset >= bytes.Length) return false;
            var marker = bytes[offset++];
            if (marker == 0xd9 || marker == 0xda) return false;
            if (marker == 0x01 || marker >= 0xd0 && marker <= 0xd7) continue;
            if (offset + 2 > bytes.Length) return false;
            var segmentLength = bytes[offset] << 8 | bytes[offset + 1];
            if (segmentLength < 2 || offset + segmentLength > bytes.Length) return false;
            if (IsJpegStartOfFrame(marker))
            {
                if (segmentLength < 8) return false;
                height = bytes[offset + 3] << 8 | bytes[offset + 4];
                width = bytes[offset + 5] << 8 | bytes[offset + 6];
                return true;
            }
            offset += segmentLength;
        }
        return false;
    }

    private static bool IsJpegStartOfFrame(byte marker) => marker >= 0xc0 && marker <= 0xc3 ||
                                                            marker >= 0xc5 && marker <= 0xc7 ||
                                                            marker >= 0xc9 && marker <= 0xcb ||
                                                            marker >= 0xcd && marker <= 0xcf;

    private void ScanLibrary()
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cmp = StringComparison.OrdinalIgnoreCase;
        var candidates = 0;
        foreach (var path in Directory.EnumerateFiles(folder))
        {
            var extension = Path.GetExtension(path);
            if (!string.Equals(extension, ".png", cmp) && !string.Equals(extension, ".jpg", cmp) && !string.Equals(extension, ".jpeg", cmp)) 
                continue;

            if (candidates++ >= MaxLibraryImages) break;
            found.Add(path);
            Texture2D texture = null;
            try
            {
                var timestamp = File.GetLastWriteTimeUtc(path);
                if (library.TryGetValue(path, out var old) && old.Timestamp == timestamp)
                    continue;

                var sourceBytes = File.ReadAllBytes(path);
                if (sourceBytes.Length == 0 || sourceBytes.Length > MaxSourceImageBytes || !IsSourceImageSizeAllowed(sourceBytes))
                    continue;

                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, sourceBytes) || texture.width > MaxImageDimension ||
                    texture.height > MaxImageDimension)
                {
                    Destroy(texture);
                    texture = null;
                    continue;
                }

                var networkBytes = EncodeTexture(texture);
                if (networkBytes == null || networkBytes.Length > MaxImageBytes)
                {
                    Destroy(texture);
                    texture = null;
                    continue;
                }
                var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                if (old != null)
                    old.Destroy();

                library[path] = new LibraryImage(networkBytes, texture, sprite, timestamp);
                texture = null;
                if (placing && path == selectedPath && previewRenderer != null)
                    previewRenderer.sprite = sprite;

                pickerDirty = true;
            }
            catch (Exception)
            {
                if (texture != null) Destroy(texture);
            }
        }

        var removed = new List<string>();
        foreach (var pair in library)
            if (!found.Contains(pair.Key))
                removed.Add(pair.Key);
        
        foreach (var path in removed)
        {
            library[path].Destroy();
            library.Remove(path);
            pickerDirty = true;
        }

        if (!string.IsNullOrEmpty(selectedPath) && !library.ContainsKey(selectedPath))
            Cancel();
    }

    private string FirstImagePath()
    {
        foreach (var path in library.Keys)
            return path;
        
        return null;
    }

    private void Cancel()
    {
        holdingT = false;
        pickerOpen = false;
        placing = false;
        
        if (preview != null) 
            preview.SetActive(false);
        
        if (pickerRoot != null)
            pickerRoot.SetActive(false);
        
        RestoreCursorVisibility();
    }

    private void ClearPlaced()
    {
        Cancel();

        lock (incoming)
        {
            incoming.Clear();
            nextPeerGraffitiAt.Clear();
            acceptedIds.Clear();
            acceptedIdOrder.Clear();
            queuedSenders.Clear();
            nextGlobalGraffitiAt = 0;
            nextHostBroadcastAt = 0f;
        }
        foreach (var graffiti in placed.Values)
            graffiti.Destroy();
        
        placed.Clear();
        placedOrder.Clear();
        nextSortingOrder = 100;
    }

    private void LateUpdate()
    {
        if (!pickerOpen || pickerRoot == null)
            return;
        
        ApplyArsenalFont();
        pickerRoot.SetActive(true);
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        
        if (pickerDirty) 
            RebuildPicker();
    }

    private void OnDestroy()
    {
        ClearPlaced();
        
        foreach (var image in library.Values)
            image.Destroy();
        
        library.Clear();
        
        if (preview != null)
            Destroy(preview);
        
        if (interfaceCanvas != null)
            Destroy(interfaceCanvas.gameObject);
        
        if (libraryWatcher != null) 
            libraryWatcher.Dispose();
        
        instance = null;
    }

    private void OnLibraryChanged(object sender, FileSystemEventArgs args) => Interlocked.Exchange(ref libraryDirty, 1);
    private void OnLibraryRenamed(object sender, RenamedEventArgs args) => Interlocked.Exchange(ref libraryDirty, 1);

    private void CreateInterface()
    {
        var root = new GameObject("MP Graffiti Interface", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        DontDestroyOnLoad(root);
        interfaceCanvas = root.GetComponent<Canvas>();
        interfaceCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        interfaceCanvas.sortingOrder = 700;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        pickerRoot = Panel(root.transform, Vector2.zero, new Vector2(900f, 760f));
        Text(pickerRoot.transform, "GRAFFITI", new Vector2(0f, 340f), new Vector2(820f, 36f), 20).alignment = TextAlignmentOptions.Center;
        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(pickerRoot.transform, false);
        Rect((RectTransform)viewport.transform, new Vector2(0f, -25f), new Vector2(850f, 650f));
        viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
        viewport.GetComponent<Mask>().showMaskGraphic = false;
        pickerContent = new GameObject("Images", typeof(RectTransform));
        pickerContent.transform.SetParent(viewport.transform, false);
        var contentRect = (RectTransform) pickerContent.transform;
        contentRect.anchorMin = new Vector2(0.5f, 1f);
        contentRect.anchorMax = new Vector2(0.5f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(840f, 110f);
        pickerScroll = pickerRoot.AddComponent<ScrollRect>();
        pickerScroll.viewport = (RectTransform)viewport.transform;
        pickerScroll.content = contentRect;
        pickerScroll.horizontal = false;
        pickerScroll.vertical = true;
        pickerScroll.movementType = ScrollRect.MovementType.Clamped;
        pickerScroll.scrollSensitivity = 35f;
        noticeRoot = Panel(root.transform, new Vector2(0f, 430f), new Vector2(720f, 48f));
        Text(noticeRoot.transform, "Add a PNG/JPG no larger than 1 MiB and 1280x1280 to BepInEx/plugins/Graffiti", Vector2.zero, new Vector2(780f, 36f), 16).alignment = TextAlignmentOptions.Center;
        pickerRoot.SetActive(false);
        noticeRoot.SetActive(false);
    }

    private void ApplyArsenalFont()
    {
        if (interfaceFontApplied || interfaceCanvas == null || PlayerScript.player == null || PlayerScript.player.ammoText == null) return;
        var source = PlayerScript.player.ammoText;
        foreach (var text in interfaceCanvas.GetComponentsInChildren<TMP_Text>(true))
        {
            text.font = source.font;
            text.fontSharedMaterial = source.fontSharedMaterial;
            text.color = source.color;
        }
        interfaceFontApplied = true;
    }

    private void RebuildPicker()
    {
        pickerDirty = false;
        pickerItems.Clear();
        
        for (var index = pickerContent.transform.childCount - 1; index >= 0; index--)
            Destroy(pickerContent.transform.GetChild(index).gameObject);
        
        var paths = new List<string>(library.Keys);
        var count = paths.Count;
        var columns = 7;
        var rows = Mathf.Max(1, Mathf.CeilToInt(count / (float)columns));
        ((RectTransform)pickerContent.transform).sizeDelta = new Vector2(840f, rows * 118f);
        ((RectTransform)pickerContent.transform).anchoredPosition = Vector2.zero;
        
        for (var index = 0; index < count; index++)
        {
            var path = paths[index];
            var column = index % columns;
            var row = index / columns;
            var item = new GameObject("Graffiti", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline), typeof(EventTrigger));
            item.transform.SetParent(pickerContent.transform, false);
            var itemRect = (RectTransform)item.transform;
            Rect(itemRect, new Vector2((column - (columns - 1) * 0.5f) * 118f, -7f - row * 118f), new Vector2(104f, 104f));
            itemRect.anchorMin = itemRect.anchorMax = new Vector2(0.5f, 1f);
            itemRect.pivot = new Vector2(0.5f, 1f);
            var itemImage = item.GetComponent<Image>();
            itemImage.color = new Color(0.16f, 0.2f, 0.2f, 0.95f);
            var outline = item.GetComponent<Outline>();
            outline.effectColor = new Color(0.83f, 0.66f, 0.12f, 0f);
            outline.effectDistance = new Vector2(3f, -3f);
            pickerItems[path] = itemImage;
            var raw = new GameObject("Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            raw.transform.SetParent(item.transform, false);
            Rect((RectTransform)raw.transform, Vector2.zero, new Vector2(92f, 92f));
            raw.GetComponent<RawImage>().texture = library[path].Texture;
            var trigger = item.GetComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            entry.callback.AddListener(_ => SelectPath(path));
            trigger.triggers.Add(entry);
            var scroll = new EventTrigger.Entry { eventID = EventTriggerType.Scroll };
            scroll.callback.AddListener(data => pickerScroll?.OnScroll((PointerEventData)data));
            trigger.triggers.Add(scroll);
        }

        if (pickerScroll != null) 
            pickerScroll.verticalNormalizedPosition = 1f;
    }

    private void SelectPath(string path)
    {
        selectedPath = path;
        foreach (var pair in pickerItems)
        {
            var outline = pair.Value.GetComponent<Outline>();
            if (outline != null) 
                outline.effectColor = new Color(0.83f, 0.66f, 0.12f, pair.Key == path ? 1f : 0f);
        }
    }

    private void RestoreCursorVisibility()
    {
        if (!cursorVisibilityCaptured) return;
        Cursor.visible = previousCursorVisibility;
        Cursor.lockState = previousCursorLockMode;
        cursorVisibilityCaptured = false;
    }

    internal static bool IsPickerOpen => instance != null && instance.pickerOpen;
    
    internal static bool IsPlacementPreview => instance != null && instance.placing;
    internal static bool ShowGraffiti => showGraffiti;

    internal static void SetShowGraffiti(bool value)
    {
        showGraffiti = value;
        PlayerPrefs.SetInt("showgraffiti", value ? 1 : 0);
        if (instance == null) return;
        foreach (var graffiti in instance.placed.Values) graffiti.SetVisible(value);
    }

    private static void PlaySprayer(Vector3 position)
    {
        if (EmbeddedAudioLoader.SprayerSound != null) Sound.Play(EmbeddedAudioLoader.SprayerSound, position, false, false);
    }

    private static GameObject Panel(Transform parent, Vector2 position, Vector2 size)
    {
        var go = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        Rect((RectTransform) go.transform, position, size);
        go.GetComponent<Image>().color = new Color(0.04f, 0.04f, 0.04f, 0.96f);
        return go;
    }

    private static TMP_Text Text(Transform parent, string value, Vector2 position, Vector2 size, float fontSize)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        Rect((RectTransform)go.transform, position, size);
        var text = go.GetComponent<TextMeshProUGUI>();
        TMP_Text source = PlayerScript.player == null ? null : PlayerScript.player.ammoText;
        
        if (source == null)
            foreach (var candidate in Resources.FindObjectsOfTypeAll<TMP_Text>())
                if (candidate != null && candidate.font != null)
                {
                    source = candidate;
                    break;
                }
        
        if (source != null)
        {
            text.font = source.font;
            text.fontSharedMaterial = source.fontSharedMaterial;
            text.color = source.color;
        }
        else 
            text.font = TMP_Settings.defaultFontAsset;
        
        text.text = value;
        text.fontSize = fontSize;
        
        if (source == null) 
            text.color = Color.white;
        
        text.raycastTarget = false;
        return text;
    }

    private static void Rect(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private sealed class LibraryImage
    {
        internal readonly byte[] Bytes;
        internal readonly Texture2D Texture;
        internal readonly Sprite Sprite;
        internal readonly DateTime Timestamp;

        internal LibraryImage(byte[] bytes, Texture2D texture, Sprite sprite, DateTime timestamp)
        {
            Bytes = bytes;
            Texture = texture;
            Sprite = sprite;
            Timestamp = timestamp;
        }

        internal void Destroy()
        {
            if (Sprite != null) 
                UnityEngine.Object.Destroy(Sprite);
            
            if (Texture != null)
                UnityEngine.Object.Destroy(Texture);
        }
    }

    private readonly struct IncomingGraffiti
    {
        internal readonly ushort SenderId;
        internal readonly GraffitiPacket Packet;

        internal IncomingGraffiti(ushort senderId, GraffitiPacket packet)
        {
            SenderId = senderId;
            Packet = packet;
        }
    }

    private sealed class Graffiti
    {
        internal readonly GraffitiPacket Packet;
        private readonly GameObject gameObject;
        private readonly Texture2D texture;
        private readonly Sprite sprite;

        internal Graffiti(GraffitiPacket packet, GameObject gameObject, Texture2D texture, Sprite sprite)
        {
            Packet = packet;
            this.gameObject = gameObject;
            this.texture = texture;
            this.sprite = sprite;
        }

        internal void SetVisible(bool value)
        {
            if (gameObject != null) gameObject.SetActive(value);
        }

        internal void Destroy()
        {
            if (gameObject != null)
                UnityEngine.Object.Destroy(gameObject);
            
            if (sprite != null) 
                UnityEngine.Object.Destroy(sprite);
            
            if (texture != null)
                UnityEngine.Object.Destroy(texture);
        }
    }
}

/*
    Она знает: мне хуёво, и селфхармит мой ангел
    Мне нужна тысяча таких же, но куда я потратил?
    И выбирай всё, что хочешь: у меня здесь супермаркет
    Это последние розы на твоём шёлковом платье
 */

[HarmonyPatch(typeof(Cursor), "set_visible")]
internal static class GraffitiCursorVisibilityPatch
{
    private static void Prefix(ref bool value)
    {
        if (GraffitiSystem.IsPickerOpen) value = true;
    }
}

[HarmonyPatch(typeof(Cursor), "set_lockState")]
internal static class GraffitiCursorLockPatch
{
    private static void Prefix(ref CursorLockMode value)
    {
        if (GraffitiSystem.IsPickerOpen) value = CursorLockMode.None;
    }
}

[HarmonyPatch(typeof(WeaponScript), "Shoot")]
internal static class GraffitiWeaponShotPatch
{
    private static bool Prefix() => !GraffitiSystem.IsPlacementPreview;
}

[HarmonyPatch(typeof(VelvetScript), "Shoot")]
internal static class GraffitiVelvetShotPatch
{
    private static bool Prefix() => !GraffitiSystem.IsPlacementPreview;
}
