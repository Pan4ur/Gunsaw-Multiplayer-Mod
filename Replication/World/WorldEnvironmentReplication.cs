using UnityEngine;

public class WorldEnvironmentReplication
{
    private readonly HashSet<string> seenSnapshotFires = [];
    private readonly HashSet<string> seenSnapshotAudio = [];
    
    internal void RefreshButtons()
    {
        foreach (var button in WorldReplication.FindObjectsOfType<ButtonScript>())
        {
            if (button == null || WorldReplication.Instance.buttonIds.ContainsKey(button)) continue;
            var id = WorldReplication.Instance.ButtonId(button);
            WorldReplication.Instance.buttonIds[button] = id;
            WorldReplication.Instance.buttons[id] = button;
            if (!WorldReplication.Instance.buttonActivations.ContainsKey(id)) WorldReplication.Instance.buttonActivations[id] = 0;
        }
    }

    internal void RefreshProximityDoors()
    {
        foreach (var opener in WorldReplication.FindObjectsOfType<QDoorOpen>())
        {
            if (opener == null || WorldReplication.Instance.proximityDoorIds.ContainsKey(opener)) continue;
            var id = WorldReplication.Instance.ProximityDoorId(opener);
            WorldReplication.Instance.proximityDoorIds[opener] = id;
            WorldReplication.Instance.proximityDoors[id] = opener;
        }
    }

    internal void RefreshActivationZones()
    {
        foreach (var zone in WorldReplication.FindObjectsOfType<ActivateZoneScript>())
        {
            if (zone == null || WorldReplication.Instance.activationZoneIds.ContainsKey(zone)) continue;
            var id = WorldReplication.Instance.ActivationZoneId(zone);
            WorldReplication.Instance.activationZoneIds[zone] = id;
            WorldReplication.Instance.activationZones[id] = zone;
        }
    }
    
    internal void RefreshGlasses()
    {
        foreach (var glass in WorldReplication.FindObjectsOfType<GlassScript>())
        {
            if (glass == null || WorldReplication.Instance.glassIds.ContainsKey(glass)) continue;
            var id = WorldReplication.Instance.GlassId(glass);
            WorldReplication.Instance.glassIds[glass] = id;
            WorldReplication.Instance.glasses[id] = glass;
        }
        RefreshLamps();
    }
    
    private void RefreshLamps()
    {
        foreach (var collider in WorldReplication.FindObjectsOfType<Collider2D>())
        {
            if (collider == null || WorldReplication.Instance.lampIds.ContainsKey(collider)) continue;
            var light = collider.GetComponentInParent<UnityEngine.Experimental.Rendering.Universal.Light2D>();
            if (light == null) continue;
            if (!collider.CompareTag("Lamp") &&
                !collider.gameObject.name.StartsWith("Lamp (") &&
                !light.CompareTag("Lamp") &&
                !light.gameObject.name.StartsWith("Lamp (")) continue;
            var id = WorldReplication.Instance.ComponentId(collider);
            WorldReplication.Instance.lampIds[collider] = id;
            WorldReplication.Instance.lamps[id] = new WorldReplication.LampState { Object = light.gameObject, Light = light, Collider = collider };
        }
    }
    
    internal void RefreshDrones()
    {
        foreach (var drone in WorldReplication.FindObjectsOfType<DroneScript>())
        {
            if (drone == null || WorldReplication.Instance.droneIds.ContainsKey(drone)) continue;
            var body = drone.GetComponent<Rigidbody2D>();
            if (body == null) continue;
            var id = WorldReplication.Instance.Id(body);
            WorldReplication.Instance.droneIds[drone] = id;
            WorldReplication.Instance.drones[id] = drone;
            WorldReplication.Instance.droneBodies.Add(body);
        }
    }
    
    internal void DiscoverWorldFires()
    {
        if (MultiplayerSession.IsHost) WorldReplication.Instance.fires.Clear();
        foreach (var fire in WorldReplication.FindObjectsOfType<FireScript>())
            RegisterWorldFireInternal(fire);
    }
    
    internal void RefreshKnownWorldFires()
    {
        if (MultiplayerSession.IsHost) return;
        foreach (var pair in WorldReplication.Instance.fires)
        {
            var fire = pair.Value;
            if (fire == null) continue;
            if (!WorldReplication.Instance.clientFireSettings.ContainsKey(fire)) WorldReplication.Instance.clientFireSettings[fire] = new WorldReplication.FireLocalSettings
            {
                enabled = fire.enabled,
                active = fire.gameObject.activeSelf
            };

            fire.enabled = WorldReplication.ShouldTickClientFire(fire);
        }
    }
    
    internal void ProcessPendingRuntimeFires()
    {
        if (!MultiplayerSession.IsHost || WorldReplication.Instance.pendingRuntimeFires.Count == 0) return;
        var ready = new List<FireScript>();
        foreach (var pair in WorldReplication.Instance.pendingRuntimeFires)
        {
            var fire = pair.Key;
            if (Time.frameCount <= pair.Value) continue;
            ready.Add(fire);
            if (fire == null || WorldReplication.IsGameplayOwned(fire) || WorldReplication.Instance.fireIds.ContainsKey(fire)) continue;
            var id = "runtime-fire/" + (++WorldReplication.Instance.nextRuntimeFireId).ToString();
            WorldReplication.Instance.fireIds[fire] = id;
            WorldReplication.Instance.fires[id] = fire;
        }
        foreach (var fire in ready) WorldReplication.Instance.pendingRuntimeFires.Remove(fire);
    }
    
    internal void RegisterWorldFireInternal(FireScript fire)
    {
        if (fire == null || WorldReplication.IsGameplayOwned(fire)) return;
        string id;
        if (!WorldReplication.Instance.fireIds.TryGetValue(fire, out id))
        {
            id = WorldReplication.Instance.ComponentId(fire);
            WorldReplication.Instance.fireIds[fire] = id;
        }
        WorldReplication.Instance.fires[id] = fire;
    }
    
    internal void RefreshMechanismAudio()
    {
        if (MultiplayerSession.IsHost) WorldReplication.Instance.mechanismAudio.Clear();
        CollectMechanismAudio(WorldReplication.FindObjectsOfType<DoorScript>());
        CollectMechanismAudio(WorldReplication.FindObjectsOfType<MovingBelt>());
        CollectMechanismAudio(WorldReplication.FindObjectsOfType<RbMoveToObj>());
        CollectMechanismAudio(WorldReplication.FindObjectsOfType<SawScript>());
        CollectMechanismAudio(WorldReplication.FindObjectsOfType<CustJoint>());
    }
    
    private void CollectMechanismAudio<T>(T[] controllers) where T : MonoBehaviour
    {
        foreach (var controller in controllers)
        {
            if (controller == null || WorldReplication.IsGameplayOwned(controller)) continue;
            var door = controller as DoorScript;
            foreach (var source in controller.GetComponentsInChildren<AudioSource>(true))
                RegisterMechanismAudio(source, door);
            var parentSource = controller.GetComponentInParent<AudioSource>();
            RegisterMechanismAudio(parentSource, door);
            var body = controller.GetComponentInParent<Rigidbody2D>();
            if (body == null) continue;
            foreach (var source in body.GetComponentsInChildren<AudioSource>(true))
                RegisterMechanismAudio(source, door);
        }
    }
    
    private void RegisterMechanismAudio(AudioSource source, DoorScript door = null)
    {
        if (source == null || WorldReplication.IsGameplayOwned(source)) return;
        string id;
        if (!WorldReplication.Instance.mechanismAudioIds.TryGetValue(source, out id))
        {
            id = WorldReplication.Instance.ComponentId(source);
            WorldReplication.Instance.mechanismAudioIds[source] = id;
        }
        WorldReplication.Instance.mechanismAudio[id] = source;
        if (door != null) WorldReplication.Instance.doorAudioSources[source] = door;
        if (MultiplayerSession.IsHost || WorldReplication.Instance.clientAudioWasPlaying.ContainsKey(source)) return;
        WorldReplication.Instance.clientAudioWasPlaying[source] = source.isPlaying;
        source.Stop();
    }
    
    internal void ApplyButtonState(string id, bool exists, uint activations)
    {
        ButtonScript button;
        WorldReplication.Instance.buttons.TryGetValue(id, out button);
        uint previous;
        var hadPrevious = WorldReplication.Instance.receivedButtonActivations.TryGetValue(id, out previous);
        WorldReplication.Instance.receivedButtonActivations[id] = activations;
        if (hadPrevious && activations > previous && button != null && button.activateSound != null)
            Sound.Play(button.activateSound, button.transform.position, false, false, null, 1f, 1f);
        if (!exists && button != null) SetButtonInactive(button);
    }
    
    internal void ApplyButtonActivation(string id, ushort peerId)
    {
        ButtonScript button;
        var remotePlayer = NetworkAvatarRegistry.RemoteBodyForPeer(peerId);
        float allowedAt;
        if (!WorldReplication.Instance.buttons.TryGetValue(id, out button) || button == null || remotePlayer == null ||
            !remotePlayer.isAlive || (remotePlayer.transform.position - button.transform.position).sqrMagnitude > 25f ||
            (WorldReplication.Instance.nextButtonActivation.TryGetValue(id, out allowedAt) && Time.unscaledTime < allowedAt)) return;
        WorldReplication.Instance.nextButtonActivation[id] = Time.unscaledTime + 0.15f;
        button.Activated();
        WorldReplication.Instance.nextSnapshot = 0f; // Sending new world state
    }
    
    internal void ApplyDoorActivation(string id, ushort peerId)
    {
        QDoorOpen opener;
        var remotePlayer = NetworkAvatarRegistry.RemoteBodyForPeer(peerId);
        float allowedAt;
        if (!WorldReplication.Instance.proximityDoors.TryGetValue(id, out opener) || opener == null || remotePlayer == null ||
            !remotePlayer.isAlive ||
            ((Vector2)remotePlayer.transform.position - (Vector2)opener.transform.position).sqrMagnitude >= 784f ||
            (WorldReplication.Instance.nextDoorActivation.TryGetValue(id, out allowedAt) && Time.unscaledTime < allowedAt)) return;
        var door = opener.GetComponent<DoorScript>();
        if (door == null) return;
        WorldReplication.Instance.nextDoorActivation[id] = Time.unscaledTime + 0.2f;
        WorldReplication.Destroy(opener);
        door.Activate(69);
    }
    
    internal void ApplyZoneActivation(string id, ushort peerId, bool manual)
    {
        ActivateZoneScript zone;
        var localPlayer = PlayerScript.player;
        var remotePlayer = peerId == MultiplayerSession.LocalPeerId
            ? (localPlayer == null ? null : localPlayer.bodyScript)
            : NetworkAvatarRegistry.RemoteBodyForPeer(peerId);
        float allowedAt;
        if (!WorldReplication.Instance.activationZones.TryGetValue(id, out zone) || zone == null || remotePlayer == null ||
            !remotePlayer.isAlive || (!manual && WorldReplication.Instance.activatedZoneIds.Contains(id)) ||
            (WorldReplication.Instance.nextZoneActivation.TryGetValue(id, out allowedAt) && Time.unscaledTime < allowedAt)) return;
        var zoneCollider = zone.GetComponent<Collider2D>();
        if (zoneCollider == null || zoneCollider.bounds.SqrDistance(remotePlayer.transform.position) > 4f) return;
        var hostPlayer = PlayerScript.player;
        var hostBody = hostPlayer == null ? null : hostPlayer.bodyScript;
        if (!string.IsNullOrEmpty(zone.team) && (hostBody == null || zone.team != hostBody.team)) return;
        WorldReplication.Instance.nextZoneActivation[id] = Time.unscaledTime + 0.2f;
        WorldReplication.Instance.activatedZoneIds.Add(id);
        foreach (var target in GameObject.FindGameObjectsWithTag("Activateable"))
            target.SendMessage("Activate", zone.id, SendMessageOptions.DontRequireReceiver);
    }
    
    internal static void SetButtonInactive(ButtonScript button)
    {
        if (button.transform.childCount > 0)
        {
            var child = button.transform.GetChild(0);
            var renderer = child.GetComponent<SpriteRenderer>();
            var inactive = Resources.Load<Sprite>("Spawnables/buttonInactive");
            if (renderer != null && inactive != null) renderer.sprite = inactive;
            foreach (var light in child.GetComponents<UnityEngine.Experimental.Rendering.Universal.Light2D>())
                light.color = Color.red;
        }
        WorldReplication.Destroy(button);
    }
    
    // hacky fix but i hope it doesnt fuckup the level logic
    internal void UpdateZonePrompt()
    {
        WorldReplication.Instance.promptZone = null;
        var player = PlayerScript.player;
        var body = player == null ? null : player.bodyScript;
        if (body == null || !body.isAlive) return;
        foreach (var pair in WorldReplication.Instance.activationZones)
        {
            var zone = pair.Value;
            var collider = zone == null ? null : zone.GetComponent<Collider2D>();
            if (collider == null || !WorldReplication.Instance.localZonePrompts.Contains(pair.Key) || collider.bounds.SqrDistance(body.transform.position) > 4f) continue;
            WorldReplication.Instance.promptZone = zone;
            break;
        }
        if (WorldReplication.Instance.promptZone == null || !Input.GetKeyDown(player.keys["Use"])) return;
        if (MultiplayerSession.IsHost) WorldReplication.Instance.ActivateLocalZone(WorldReplication.Instance.promptZone, true);
        else WorldReplication.Instance.QueueZoneActivation(WorldReplication.Instance.promptZone, true);
    }
    
    internal void ApplyGlassDamage(string id, ushort peerId, float damage, Vector3 bulletPosition)
    {
        GlassScript glass;
        var remoteBody = NetworkAvatarRegistry.RemoteBodyForPeer(peerId);
        if (!WorldReplication.Instance.glasses.TryGetValue(id, out glass) || glass == null || remoteBody == null ||
            !remoteBody.isAlive || ((Vector2)remoteBody.transform.position - (Vector2)glass.transform.position).sqrMagnitude > 10000f)
            return;
        glass.Damage(Mathf.Max(0f, damage), bulletPosition);
        if (IsGlassBroken(glass)) WorldReplication.Instance.destroyedGlass.Add(id);
    }

    internal void ApplyGlassState(string id)
    {
        GlassScript glass;
        if (!WorldReplication.Instance.glasses.TryGetValue(id, out glass) || glass == null)
        {
            RefreshGlasses();
            if (!WorldReplication.Instance.glasses.TryGetValue(id, out glass) || glass == null) return;
        }
        if (IsGlassBroken(glass)) return;
        MultiplayerGlassDamagePatch.ApplyingNetworkState = true;
        try { glass.Damage(float.MaxValue, glass.transform.position); }
        finally { MultiplayerGlassDamagePatch.ApplyingNetworkState = false; }
    }

    internal void ApplyFireState(string id, Vector2 position, float rotation, float fuel,
        bool canIgnite, float damageMult, float fuelConsMult)
    {
        FireScript fire;
        if (!WorldReplication.Instance.fires.TryGetValue(id, out fire) || fire == null)
        {
            foreach (var candidate in WorldReplication.FindObjectsOfType<FireScript>())
            {
                if (candidate == null || candidate.GetComponentInParent<BodyScript>() != null ||
                    WorldReplication.Instance.fireIds.ContainsKey(candidate) ||
                    ((Vector2)candidate.transform.position - position).sqrMagnitude > 0.25f) continue;
                fire = candidate;
                break;
            }
            if (fire == null)
            {
                var prefab = Resources.Load<GameObject>("Spawnables/FireParticle");
                var created = prefab == null ? null : WorldReplication.Instantiate(prefab, position,
                    Quaternion.Euler(0f, 0f, rotation));
                fire = created == null ? null : created.GetComponent<FireScript>();
                if (fire == null)
                {
                    if (created != null) WorldReplication.Destroy(created);
                    return;
                }
                WorldReplication.Instance.clientCreatedFires.Add(fire);
            }
            else if (!WorldReplication.Instance.clientFireSettings.ContainsKey(fire))
            {
                WorldReplication.Instance.clientFireSettings[fire] = new WorldReplication.FireLocalSettings
                {
                    enabled = fire.enabled,
                    active = fire.gameObject.activeSelf
                };
            }
            WorldReplication.Instance.fireIds[fire] = id;
            WorldReplication.Instance.fires[id] = fire;
        }
        fire.gameObject.SetActive(true);
        fire.transform.position = position;
        fire.transform.rotation = Quaternion.Euler(0f, 0f, rotation);
        fire.fuel = fuel;
        fire.canIgnite = canIgnite;
        fire.damageMult = damageMult;
        fire.fuelConsMult = fuelConsMult;
        fire.enabled = WorldReplication.ShouldTickClientFire(fire);
        var particles = fire.GetComponent<ParticleSystem>();
        if (particles != null && !particles.isPlaying) particles.Play();
    }
    
    internal void ApplyLampState(string id)
    {
        WorldReplication.LampState lamp;
        if (!WorldReplication.Instance.lamps.TryGetValue(id, out lamp))
        {
            RefreshLamps();
            if (!WorldReplication.Instance.lamps.TryGetValue(id, out lamp)) return;
        }
        BreakLamp(id, lamp, lamp.Object == null ? Vector2.zero : lamp.Object.transform.position);
    }
    
    private void BreakLamp(string id, WorldReplication.LampState lamp, Vector2 hitPoint)
    {
        if (lamp == null) return;
        var lampObject = lamp.Object;
        if (lampObject != null)
        {
            var position = (Vector2)lampObject.transform.position;
            WorldReplication.Destroy(lampObject);
            Sound.Play(Resources.Load<AudioClip>("Sounds/LightBreak"), hitPoint);
            WorldReplication.Instantiate(Resources.Load("Spawnables/LampShards"), hitPoint, Quaternion.identity);
            WorldReplication.Destroy(WorldReplication.Instantiate(Resources.Load("Spawnables/Shock"), position, Quaternion.identity), 15f);
        }
        WorldReplication.Instance.destroyedLamps.Add(id);
    }
    
    internal void ApplyDroneDamage(string id, float amount)
    {
        DroneScript drone;
        if (!WorldReplication.Instance.drones.TryGetValue(id, out drone) || drone == null) return;
        drone.Damage(amount);
    }

    private void ApplyDroneState(string id)
    {
        DroneScript drone;
        if (!WorldReplication.Instance.drones.TryGetValue(id, out drone) || drone == null) return;
        var renderer = drone.GetComponent<SpriteRenderer>();
        if (renderer != null && drone.deadSprite != null) renderer.sprite = drone.deadSprite;
        if (drone.deactiveOnDeath != null)
            foreach (var child in drone.deactiveOnDeath)
                if (child != null) child.SetActive(false);
        var source = drone.GetComponent<AudioSource>();
        if (source != null) source.Stop();
        if (drone.breakSound != null) Sound.Play(drone.breakSound, drone.transform.position);
        var shock = Resources.Load<GameObject>("Spawnables/Shock");
        if (shock != null) WorldReplication.Destroy(WorldReplication.Instantiate(shock, drone.transform), 20f);
        WorldReplication.Instance.drones.Remove(id);
        WorldReplication.Instance.droneIds.Remove(drone);
        WorldReplication.Destroy(drone);
    }
    
    internal void RemoveMissingFires(HashSet<string> seen)
    {
        var missing = new List<string>();
        foreach (var pair in WorldReplication.Instance.fires)
            if (!seen.Contains(pair.Key)) missing.Add(pair.Key);
        foreach (var id in missing)
        {
            var fire = WorldReplication.Instance.fires[id];
            WorldReplication.Instance.fires.Remove(id);
            if (fire == null) continue;
            WorldReplication.Instance.fireIds.Remove(fire);
            if (WorldReplication.Instance.clientCreatedFires.Remove(fire)) WorldReplication.Destroy(fire.gameObject);
            else fire.gameObject.SetActive(false);
        }
    }
    
    internal void ApplyMechanismAudio(string id, bool playing, bool loop, float volume, float pitch)
    {
        AudioSource source;
        if (!WorldReplication.Instance.mechanismAudio.TryGetValue(id, out source) || source == null) return;
        source.loop = loop;
        source.volume = Mathf.Clamp01(volume);
        source.pitch = Mathf.Clamp(pitch, -3f, 3f);
        if (playing)
        {
            if (!source.isPlaying && source.clip != null)
            {
                source.Play();
                if (WorldReplication.Instance.doorAudioSources.ContainsKey(source)) WorldReplication.Instance.clientDoorAudioStartedAt[source] = Time.unscaledTime;
            }
        }
        else if (source.isPlaying)
        {
            source.Stop();
            WorldReplication.Instance.clientDoorAudioStartedAt.Remove(source);
        }
    }
    
    internal void StopSettledClientDoorAudio()
    {
        if (MultiplayerSession.IsHost) return;
        foreach (var pair in WorldReplication.Instance.doorAudioSources)
        {
            var source = pair.Key;
            var door = pair.Value;
            if (source == null || door == null || !source.isPlaying) continue;
            float startedAt;
            if (!WorldReplication.Instance.clientDoorAudioStartedAt.TryGetValue(source, out startedAt) ||
                Time.unscaledTime - startedAt < 0.2f) continue;
            var body = door.GetComponent<Rigidbody2D>();
            if (body == null || body.velocity.sqrMagnitude > 0.0001f || door.point1 == null || door.point2 == null) continue;
            var closeEnough = Mathf.Min(Vector2.Distance(door.transform.position, door.point1.position),
                Vector2.Distance(door.transform.position, door.point2.position)) < door.speed * 0.05f;
            if (!closeEnough) continue;
            source.Stop();
            WorldReplication.Instance.clientDoorAudioStartedAt.Remove(source);
        }
    }
    
    internal byte[] SerializeEnvironment()
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(MultiplayerSession.SnapshotEpoch);
            BinaryWriterRaw.WriteSingle(writer, Physics2D.gravity.x); BinaryWriterRaw.WriteSingle(writer, Physics2D.gravity.y);
            writer.Write((ushort)WorldReplication.Instance.buttons.Count);
            foreach (var pair in WorldReplication.Instance.buttons)
            {
                writer.Write(WorldReplication.Instance.WireId(pair.Key)); writer.Write(pair.Value != null);
                uint activations; WorldReplication.Instance.buttonActivations.TryGetValue(pair.Key, out activations); writer.Write(activations);
            }
            CaptureDestroyedGlass();
            writer.Write((ushort)Math.Min(ushort.MaxValue, WorldReplication.Instance.destroyedGlass.Count));
            var writtenGlass = 0;
            foreach (var id in WorldReplication.Instance.destroyedGlass)
            {
                if (writtenGlass++ >= ushort.MaxValue) break;
                writer.Write(WorldReplication.Instance.WireId(id));
            }
            CaptureDestroyedLamps();
            writer.Write((ushort)Math.Min(ushort.MaxValue, WorldReplication.Instance.destroyedLamps.Count));
            var writtenLamps = 0;
            foreach (var id in WorldReplication.Instance.destroyedLamps)
            {
                if (writtenLamps++ >= ushort.MaxValue) break;
                writer.Write(WorldReplication.Instance.WireId(id));
            }
            var fireCount = 0;
            foreach (var pair in WorldReplication.Instance.fires) if (pair.Value != null && fireCount < ushort.MaxValue) fireCount++;
            writer.Write((ushort)fireCount);
            var writtenFires = 0;
            foreach (var pair in WorldReplication.Instance.fires)
            {
                var fire = pair.Value;
                if (fire == null || writtenFires >= fireCount) continue;
                writer.Write(WorldReplication.Instance.WireId(pair.Key)); BinaryWriterRaw.WriteSingle(writer, fire.transform.position.x);
                BinaryWriterRaw.WriteSingle(writer, fire.transform.position.y);
                BinaryWriterRaw.WriteSingle(writer, fire.transform.eulerAngles.z);
                BinaryWriterRaw.WriteSingle(writer, fire.fuel); writer.Write(fire.canIgnite);
                BinaryWriterRaw.WriteSingle(writer, fire.damageMult);
                BinaryWriterRaw.WriteSingle(writer, fire.fuelConsMult); writtenFires++;
            }
            var audioCount = 0;
            foreach (var pair in WorldReplication.Instance.mechanismAudio) if (pair.Value != null && audioCount < ushort.MaxValue) audioCount++;
            writer.Write((ushort)audioCount);
            var writtenAudio = 0;
            foreach (var pair in WorldReplication.Instance.mechanismAudio)
            {
                if (pair.Value == null || writtenAudio >= audioCount) continue;
                writer.Write(WorldReplication.Instance.WireId(pair.Key)); writer.Write(pair.Value.isPlaying); writer.Write(pair.Value.loop);
                BinaryWriterRaw.WriteSingle(writer, pair.Value.volume);
                BinaryWriterRaw.WriteSingle(writer, pair.Value.pitch); writtenAudio++;
            }
            CaptureDestroyedDrones();
            writer.Write((ushort)Math.Min(ushort.MaxValue, WorldReplication.Instance.destroyedDrones.Count));
            var writtenDrones = 0;
            foreach (var id in WorldReplication.Instance.destroyedDrones)
            {
                if (writtenDrones++ >= ushort.MaxValue) break;
                writer.Write(WorldReplication.Instance.WireId(id));
            }
            var manager = GameManager.main;
            BinaryWriterRaw.WriteSingle(writer, manager == null ? 0f : manager.rainIntensity);
            BinaryWriterRaw.WriteSingle(writer, manager == null ? 0f : manager.snowIntensity);
            BinaryWriterRaw.WriteSingle(writer, manager == null ? 0f : manager.fogIntensity);
            var mission = MissionManager.main;
            writer.Write(mission == null ? -1 : mission.killAmount);
            writer.Write(mission == null ? -1 : mission.totalEnemyCount);
            return stream.ToArray();
        }
    }
    
    internal void ApplyEnvironment(byte[] data)
    {
        using (var reader = new BinaryReader(new MemoryStream(data)))
        {
            var sceneEpoch = reader.ReadInt32();
            if (!MultiplayerSession.IsSnapshotEpochCurrent(sceneEpoch)) return;
            Physics2D.gravity = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            var buttonCount = reader.ReadUInt16();
            for (var index = 0; index < buttonCount; index++)
                ApplyButtonState(WorldReplication.Instance.ResolveWireId(reader.ReadUInt64()), reader.ReadBoolean(), reader.ReadUInt32());
            var glassCount = reader.ReadUInt16();
            for (var index = 0; index < glassCount; index++)
                ApplyGlassState(WorldReplication.Instance.ResolveWireId(reader.ReadUInt64()));
            var lampCount = reader.ReadUInt16();
            for (var index = 0; index < lampCount; index++)
                ApplyLampState(WorldReplication.Instance.ResolveWireId(reader.ReadUInt64()));
            seenSnapshotFires.Clear();
            var fireCount = reader.ReadUInt16();
            for (var index = 0; index < fireCount; index++)
            {
                var id = WorldReplication.Instance.ResolveWireId(reader.ReadUInt64());
                var position = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                var rotation = reader.ReadSingle(); var fuel = reader.ReadSingle(); var canIgnite = reader.ReadBoolean();
                var damageMult = reader.ReadSingle(); var fuelConsMult = reader.ReadSingle();
                seenSnapshotFires.Add(id); ApplyFireState(id, position, rotation, fuel, canIgnite, damageMult, fuelConsMult);
            }
            RemoveMissingFires(seenSnapshotFires);
            seenSnapshotAudio.Clear();
            var audioCount = reader.ReadUInt16();
            for (var index = 0; index < audioCount; index++)
            {
                var id = WorldReplication.Instance.ResolveWireId(reader.ReadUInt64());
                var playing = reader.ReadBoolean(); var loop = reader.ReadBoolean();
                var volume = reader.ReadSingle(); var pitch = reader.ReadSingle();
                seenSnapshotAudio.Add(id); ApplyMechanismAudio(id, playing, loop, volume, pitch);
            }
            StopMissingMechanismAudio(seenSnapshotAudio);
            var droneCount = reader.ReadUInt16();
            for (var index = 0; index < droneCount; index++)
                ApplyDroneState(WorldReplication.Instance.ResolveWireId(reader.ReadUInt64()));
            var rain = reader.ReadSingle();
            var snow = reader.ReadSingle();
            var fog = reader.ReadSingle();
            ApplyWeather(rain, snow, fog);
            if (reader.BaseStream.Length - reader.BaseStream.Position >= sizeof(int) * 2)
                ApplyMissionEnemyCount(reader.ReadInt32(), reader.ReadInt32());
        }
    }
    
    internal static void ApplyMissionEnemyCount(int killed, int total)
    {
        if (killed < 0 || total < 0) return;
        var mission = MissionManager.main;
        if (mission == null) return;
        mission.killAmount = killed;
        mission.totalEnemyCount = total;
        if (mission.killsText != null)
            mission.killsText.text = "Enemies: " + Mathf.Max(0, total - killed) + "/" + total;
    }

    internal static void ApplyWeather(float rain, float snow, float fog)
    {
        var manager = GameManager.main;
        if (manager == null) return;
        if (!Mathf.Approximately(manager.rainIntensity, rain))
        {
            manager.rainIntensity = rain;
            manager.UpdateRain();
        }
        if (!Mathf.Approximately(manager.snowIntensity, snow))
        {
            manager.snowIntensity = snow;
            manager.UpdateSnow();
        }
        if (!Mathf.Approximately(manager.fogIntensity, fog))
        {
            manager.fogIntensity = fog;
            manager.UpdateFog();
        }
    }
    
    private void StopMissingMechanismAudio(HashSet<string> seen)
    {
        foreach (var pair in WorldReplication.Instance.mechanismAudio)
        {
            if (pair.Value != null && !seen.Contains(pair.Key) && pair.Value.isPlaying)
                pair.Value.Stop();
        }
    }
    
    private void CaptureDestroyedGlass()
    {
        foreach (var pair in WorldReplication.Instance.glasses)
            if (IsGlassBroken(pair.Value)) WorldReplication.Instance.destroyedGlass.Add(pair.Key);
    }
    
    private static bool IsGlassBroken(GlassScript glass)
    {
        if (glass == null) return true;
        return glass.health <= 0f;
    }
    
    private void CaptureDestroyedLamps()
    {
        foreach (var pair in WorldReplication.Instance.lamps)
            if (LampIsDestroyed(pair.Value)) WorldReplication.Instance.destroyedLamps.Add(pair.Key);
    }
    
    private void CaptureDestroyedDrones()
    {
        foreach (var pair in WorldReplication.Instance.drones)
            if (pair.Value == null) WorldReplication.Instance.destroyedDrones.Add(pair.Key);
    }
    
    internal void CaptureDestroyedLampIds(ISet<string> ids)
    {
        if (ids == null) return;
        foreach (var pair in WorldReplication.Instance.lamps)
            if (LampIsDestroyed(pair.Value)) ids.Add(pair.Key);
    }
    
    internal void CollectNewDestroyedLampIds(ISet<string> before, List<string> result)
    {
        if (before == null || result == null) return;
        foreach (var pair in WorldReplication.Instance.lamps)
            if (LampIsDestroyed(pair.Value) && !before.Contains(pair.Key)) result.Add(pair.Key);
    }

    private static bool LampIsDestroyed(WorldReplication.LampState lamp)
    {
        return lamp == null || lamp.Object == null || !lamp.Object.activeSelf ||
               lamp.Light == null || !lamp.Light.enabled || lamp.Collider == null || !lamp.Collider.enabled;
    }

    internal void ApplyRemoteDestroyedLamps(IList<string> ids)
    {
        if (ids == null) return;
        foreach (var id in ids)
            if (!string.IsNullOrEmpty(id)) ApplyLampState(id);
    }
}