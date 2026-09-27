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
            WorldReplication.Instance.WireId(id);
            WorldReplication.Instance.lamps[id] = new WorldReplication.LampState { Object = light.gameObject, Light = light, Collider = collider, Position = light.transform.position };
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
            SendFireState(id, fire);
        }
        foreach (var fire in ready) WorldReplication.Instance.pendingRuntimeFires.Remove(fire);
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
        if (door != null)
        {
            WorldReplication.Instance.doorAudioSources[source] = door;
            WorldReplication.Instance.doorAudioBodies[source] = door.GetComponent<Rigidbody2D>();
        }
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
            Sound.Play(button.activateSound, button.transform.position, false, false);
        if (button != null && button.activateOnce && activations > 0) OneTimeButtonReactivation.SetInactive(button);
        if (!exists && button != null) SetButtonInactive(button);
    }
    
    internal void ApplyButtonActivation(string id, ushort peerId)
    {
        ButtonScript button;
        var remotePlayer = NetworkAvatarManager.RemoteBodyForPeer(peerId);
        float allowedAt;
        if (!WorldReplication.Instance.buttons.TryGetValue(id, out button) || button == null || remotePlayer == null ||
            !remotePlayer.isAlive || (remotePlayer.transform.position - button.transform.position).sqrMagnitude > 25f ||
            (WorldReplication.Instance.nextButtonActivation.TryGetValue(id, out allowedAt) && Time.unscaledTime < allowedAt)) return;
        WorldReplication.Instance.nextButtonActivation[id] = Time.unscaledTime + (button.activateOnce ? 1f : 0.15f);
        if (button.activateOnce && OneTimeButtonReactivation.IsUsed(button))
        {
            if (OneTimeButtonReactivation.CanReactivate(button)) OneTimeButtonReactivation.Reactivate(button);
        }
        else button.Activated();
        WorldReplication.Instance.nextSnapshot = 0f; // Sending new world state
    }
    
    internal void ApplyDoorActivation(string id, ushort peerId)
    {
        QDoorOpen opener;
        var remotePlayer = NetworkAvatarManager.RemoteBodyForPeer(peerId);
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
            : NetworkAvatarManager.RemoteBodyForPeer(peerId);
        float allowedAt;
        if (!WorldReplication.Instance.activationZones.TryGetValue(id, out zone) || zone == null || remotePlayer == null ||
            !remotePlayer.isAlive || (!manual && WorldReplication.Instance.activatedZoneIds.Contains(id) && !ActivatesTeleport(zone)) ||
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

    private static bool ActivatesTeleport(ActivateZoneScript zone)
    {
        if (zone == null) return false;
        var hasTeleport = false;
        foreach (var target in GameObject.FindGameObjectsWithTag("Activateable"))
        foreach (var receiver in target.GetComponents<MonoBehaviour>())
        {
            if (receiver == null) continue;
            var method = receiver.GetType().GetMethod("Activate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, new[] { typeof(int) }, null);
            if (method == null) continue;
            var field = receiver.GetType().GetField("id", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(int)) return false;
            if ((int) field.GetValue(receiver) != zone.id) continue;
            if (!(receiver is TeleportZone)) return false;
            hasTeleport = true;
        }
        return hasTeleport;
    }
    
    private static void SetButtonInactive(ButtonScript button)
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

    internal void UpdateButtonReactivationPrompt()
    {
        var world = WorldReplication.Instance;
        world.promptButton = null;
        if (world.promptZone != null) return;
        var player = PlayerScript.player;
        var body = player == null ? null : player.bodyScript;
        if (body == null || !body.isAlive) return;
        foreach (var pair in world.buttons)
        {
            var button = pair.Value;
            if (button == null || !button.activateOnce ||
                (!MultiplayerSession.IsHost && (!world.receivedButtonActivations.TryGetValue(pair.Key, out var activations) || activations == 0)) ||
                (MultiplayerSession.IsHost && (!OneTimeButtonReactivation.IsUsed(button) || !OneTimeButtonReactivation.CanReactivate(button))) ||
                ((Vector2)button.transform.position - (Vector2)body.transform.position).sqrMagnitude > 4f) continue;
            world.promptButton = button;
            break;
        }
        if (world.promptButton == null || !Input.GetKeyDown(player.keys["Use"])) return;
       
        if (MultiplayerSession.IsHost) 
            OneTimeButtonReactivation.Reactivate(world.promptButton);
        else 
            world.QueueButtonActivation(world.promptButton);
    }
    
    internal void ApplyGlassDamage(string id, ushort peerId, float damage, Vector3 bulletPosition)
    {
        GlassScript glass;
        var remoteBody = NetworkAvatarManager.RemoteBodyForPeer(peerId);
        if (!WorldReplication.Instance.glasses.TryGetValue(id, out glass) || glass == null || remoteBody == null ||
            !remoteBody.isAlive || ((Vector2)remoteBody.transform.position - (Vector2)glass.transform.position).sqrMagnitude > 10000f)
            return;
        glass.Damage(Mathf.Max(0f, damage), bulletPosition);
        if (IsGlassBroken(glass)) WorldReplication.Instance.destroyedGlass.Add(id);
    }

    private void ApplyGlassState(string id)
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

    private void ApplyFireState(string id, Vector2 position, float rotation, float fuel, bool canIgnite, float damageMult, float fuelConsMult)
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
        var particles = fire.GetComponent<ParticleSystem>();
        if (particles != null && !particles.isPlaying) particles.Play();
    }

    internal void ApplyFire(WorldFirePacket packet)
    {
        if (string.IsNullOrEmpty(packet.Id)) return;
        if (packet.Exists)
        {
            ApplyFireState(packet.Id, new Vector2(packet.PositionX, packet.PositionY), packet.Rotation, packet.Fuel, packet.CanIgnite, packet.DamageMult, packet.FuelConsMult);
           
            if (!string.IsNullOrEmpty(packet.ParentId) &&
                WorldReplication.Instance.bodies.bodies.TryGetValue(packet.ParentId, out var parent) &&
                parent != null &&
                WorldReplication.Instance.fires.TryGetValue(packet.Id, out var syncedFire) && syncedFire != null)
            {
                syncedFire.transform.SetParent(parent.transform, false);
                syncedFire.transform.localPosition = new Vector3(packet.LocalPositionX, packet.LocalPositionY, 0f);
                syncedFire.transform.localRotation = Quaternion.Euler(0f, 0f, packet.LocalRotation);
            }

            return;
        }

        if (!WorldReplication.Instance.fires.TryGetValue(packet.Id, out var fire))
            return;
       
        WorldReplication.Instance.fires.Remove(packet.Id);
       
        if (fire == null) 
            return;
        
        WorldReplication.Instance.fireIds.Remove(fire);
        
        if (WorldReplication.Instance.clientCreatedFires.Remove(fire)) 
            WorldReplication.Destroy(fire.gameObject);
        else 
            fire.gameObject.SetActive(false);
    }

    internal void SendFireStates(ushort peerId = 0)
    {
        foreach (var pair in WorldReplication.Instance.fires)
            SendFireState(pair.Key, pair.Value, peerId);
    }

    private void SendFireState(string id, FireScript f, ushort peerId = 0)
    {
        if (f == null || string.IsNullOrEmpty(id)) return;
        var pos = f.transform.position;
        var parent = f.transform.parent;
        var parentBody = parent == null ? null : parent.GetComponentInParent<Rigidbody2D>();
        var parentId = parentBody == null ? "" : WorldReplication.Instance.Id(parentBody);
        var localPosition = parentBody == null ? Vector3.zero : parentBody.transform.InverseTransformPoint(pos);
        var localRotation = parentBody == null ? 0f : f.transform.eulerAngles.z - parentBody.transform.eulerAngles.z;
        MultiplayerSession.Send(new WorldFirePacket(true, id, pos.x, pos.y, f.transform.eulerAngles.z, f.fuel,
            f.canIgnite, f.damageMult, f.fuelConsMult, parentId, localPosition.x, localPosition.y, localRotation), peerId);
    }

    internal void ApplyClientLampBreak(string id, Vector2 point)
    {
        if (string.IsNullOrEmpty(id) || WorldReplication.Instance.destroyedLamps.Contains(id)) return;
        ApplyRemoteLampBreak(id, point);
        MultiplayerSession.Send(new WorldInteractionPacket(InteractionType.LampBreak, WorldReplication.Instance.WireId(id), positionX: point.x, positionY: point.y));
    }

    internal void ApplyRemoteLampBreak(string id, Vector2 point)
    {
        if (string.IsNullOrEmpty(id) || WorldReplication.Instance.destroyedLamps.Contains(id)) return;
        if (WorldReplication.Instance.lamps.TryGetValue(id, out var lamp)) BreakLamp(id, lamp, point);
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
    
    private void ApplyMechanismAudio(string id, bool playing, bool loop, float volume, float pitch)
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
        var stale = WorldReplication.Instance.staleClientDoorAudio;
        stale.Clear();
        foreach (var pair in WorldReplication.Instance.clientDoorAudioStartedAt)
        {
            var source = pair.Key;
            DoorScript door;
            Rigidbody2D body;
            if (!WorldReplication.Instance.doorAudioSources.TryGetValue(source, out door) ||
                !WorldReplication.Instance.doorAudioBodies.TryGetValue(source, out body) ||
                source == null || door == null || !source.isPlaying)
            {
                stale.Add(source);
                continue;
            }
            if (Time.unscaledTime - pair.Value < 0.2f) continue;
            if (body == null || body.velocity.sqrMagnitude > 0.0001f || door.point1 == null || door.point2 == null) continue;
            var closeEnough = Mathf.Min(Vector2.Distance(door.transform.position, door.point1.position), Vector2.Distance(door.transform.position, door.point2.position)) < door.speed * 0.05f;
            if (!closeEnough) continue;
            source.Stop();
            stale.Add(source);
        }
        foreach (var source in stale) WorldReplication.Instance.clientDoorAudioStartedAt.Remove(source);
    }
    
    internal WorldEnvironmentPacket CaptureEnvironment()
    {
        var world = WorldReplication.Instance;
        var buttons = new List<EnvironmentButtonState>();
        foreach (var pair in world.buttons)
        {
            if (buttons.Count >= ushort.MaxValue) break;
            world.buttonActivations.TryGetValue(pair.Key, out var activations);
            buttons.Add(new EnvironmentButtonState(world.WireId(pair.Key), pair.Value != null, activations));
        }
        CaptureDestroyedGlass();
        var glass = new List<ulong>();
        foreach (var id in world.destroyedGlass)
        {
            if (glass.Count >= ushort.MaxValue) break;
            glass.Add(world.WireId(id));
        }
        var audio = new List<EnvironmentAudioState>();
        foreach (var pair in world.mechanismAudio)
        {
            if (audio.Count >= ushort.MaxValue) break;
            if (pair.Value == null) continue;
            audio.Add(new EnvironmentAudioState(world.WireId(pair.Key), pair.Value.isPlaying, pair.Value.loop,
                pair.Value.volume, pair.Value.pitch));
        }
        CaptureDestroyedDrones();
        var drones = new List<ulong>();
        foreach (var id in world.destroyedDrones)
        {
            if (drones.Count >= ushort.MaxValue) break;
            drones.Add(world.WireId(id));
        }
        var lamps = new List<EnvironmentLampPowerState>();
        foreach (var pair in world.lamps)
        {
            if (lamps.Count >= ushort.MaxValue) break;
            var runtime = ToggleableLampSystem.RuntimeForLamp(pair.Value);
            if (runtime == null) continue;
            lamps.Add(new EnvironmentLampPowerState(world.WireId(pair.Key), runtime.Powered, runtime.Intensity, (Color32) runtime.Color));
        }
        var manager = GameManager.main;
        var mission = MissionManager.main;
        return new WorldEnvironmentPacket(MultiplayerSession.SnapshotEpoch, Physics2D.gravity.x, Physics2D.gravity.y,
            buttons.ToArray(), glass.ToArray(), new EnvironmentFireState[0], audio.ToArray(), drones.ToArray(),
            manager == null ? 0f : manager.rainIntensity, manager == null ? 0f : manager.snowIntensity,
            manager == null ? 0f : manager.fogIntensity, mission == null ? -1 : mission.killAmount,
            mission == null ? -1 : mission.totalEnemyCount, lamps.ToArray());
    }
    
    internal void ApplyEnvironment(WorldEnvironmentPacket packet)
    {
        if (!MultiplayerSession.IsSnapshotEpochCurrent(packet.SceneEpoch)) return;
        Physics2D.gravity = new Vector2(packet.GravityX, packet.GravityY);
        foreach (var button in packet.Buttons)
            ApplyButtonState(WorldReplication.Instance.ResolveWireId(button.Id), button.Active, button.Activations);
        foreach (var id in packet.DestroyedGlassIds)
            ApplyGlassState(WorldReplication.Instance.ResolveWireId(id));
        foreach (var fire in packet.Fires)
            ApplyFireState(WorldReplication.Instance.ResolveWireId(fire.Id),
                new Vector2(fire.PositionX, fire.PositionY), fire.Rotation, fire.Fuel, fire.CanIgnite, fire.DamageMultiplier, fire.FuelConsumptionMultiplier);
        seenSnapshotAudio.Clear();
        foreach (var audio in packet.Audio)
        {
            var id = WorldReplication.Instance.ResolveWireId(audio.Id);
            seenSnapshotAudio.Add(id);
            ApplyMechanismAudio(id, audio.IsPlaying, audio.Loop, audio.Volume, audio.Pitch);
        }
        StopMissingMechanismAudio(seenSnapshotAudio);
        foreach (var id in packet.DestroyedDroneIds)
            ApplyDroneState(WorldReplication.Instance.ResolveWireId(id));
        ApplyWeather(packet.RainIntensity, packet.SnowIntensity, packet.FogIntensity);
        ApplyMissionEnemyCount(packet.EnemyKills, packet.EnemyTotal);
        foreach (var lamp in packet.LampPower)
            ApplyLampState(WorldReplication.Instance.ResolveWireId(lamp.Id), lamp.Powered, lamp.Intensity, lamp.Color);
    }

    private void ApplyLampState(string id, bool powered, float intensity, Color color)
    {
        WorldReplication.LampState lamp;
        if (!WorldReplication.Instance.lamps.TryGetValue(id, out lamp)) return;
        var runtime = ToggleableLampSystem.RuntimeForLamp(lamp);
        if (runtime == null) return;
        runtime.SetColor(color);
        runtime.SetIntensity(intensity);
        runtime.SetPowered(powered);
    }

    private static void ApplyMissionEnemyCount(int killed, int total)
    {
        if (killed < 0 || total < 0) return;
        var mission = MissionManager.main;
        if (mission == null) return;
        mission.killAmount = killed;
        mission.totalEnemyCount = total;
        if (mission.killsText != null)
            mission.killsText.text = "Enemies: " + Mathf.Max(0, total - killed) + "/" + total;
    }

    private static void ApplyWeather(float rain, float snow, float fog)
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
            if (pair.Value != null && !seen.Contains(pair.Key) && pair.Value.isPlaying)
                pair.Value.Stop();
    }
    
    private void CaptureDestroyedGlass()
    {
        foreach (var pair in WorldReplication.Instance.glasses)
            if (IsGlassBroken(pair.Value)) WorldReplication.Instance.destroyedGlass.Add(pair.Key);
    }
    
    private static bool IsGlassBroken(GlassScript glass) => glass == null || glass.health <= 0f;
    
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
    
    private static bool LampIsDestroyed(WorldReplication.LampState l) =>
        l == null || l.Object == null || !l.Object.activeSelf || l.Light == null || !l.Light.enabled || l.Collider == null || !l.Collider.enabled;
}