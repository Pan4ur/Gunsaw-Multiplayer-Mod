using UnityEngine;

public class DroppedWeaponReplication
{
    internal float nextDroppedWeaponIndicatorUpdate;
    
    internal void RegisterDroppedWeapon(DroppedWeapon dropped)
    {
        var current = WorldReplication.Instance;
        if (current == null || !MultiplayerSession.IsHost || dropped == null) return;
        foreach (var body in dropped.GetComponentsInChildren<Rigidbody2D>(true))
        {
            if (body == null) continue;
            current.bodies.RegisterWorldBody(body);
            current.droppedWeapons[body] = dropped;
        }
    }
    
    internal bool QueueLocalWeaponDrop(BodyScript body)
    {
        var current = WorldReplication.Instance;
        var player = PlayerScript.player;
        if (current == null || !MultiplayerSession.IsConnected || MultiplayerSession.IsHost || body == null || player == null ||
            body != player.bodyScript || !body.isAlive || body.unarmed || body.weapons == null ||
            body.weaponAmmos == null) return false;

        var slot = body.currentWeapon;
        if (slot < 0 || slot >= body.weapons.Count || slot >= body.weaponAmmos.Count ||
            body.weapons[slot] == null) return false;

        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((byte) WorldReplication.WorldInteraction.WeaponDrop);
            writer.Write(0UL);
            writer.Write(slot);
            writer.Write(NetworkWireId.FromString(body.weapons[slot].name));
            writer.Write(body.weaponAmmos[slot]);
            writer.Write(false);
            writer.Write(body.transform.position.x);
            writer.Write(body.transform.position.y);
            MultiplayerSession.SendWorldInteraction(stream.ToArray());
        }
        return true;
    }
    
    internal void QueueWeaponInteraction(DroppedWeapon dropped, BodyScript body, byte operation)
    {
        if (!MultiplayerSession.IsConnected || MultiplayerSession.IsHost || dropped == null || body == null ||
            PlayerScript.player == null || body != PlayerScript.player.bodyScript) return;
        var rigidbody = dropped.GetComponent<Rigidbody2D>();
        if (rigidbody == null) rigidbody = dropped.GetComponentInChildren<Rigidbody2D>(true);
        if (rigidbody == null || !WorldBodyReplication.IsWorldBody(rigidbody)) return;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            var id = WorldReplication.Instance.Id(rigidbody);
            writer.Write(operation);
            writer.Write(WorldReplication.Instance.WireId(id));
            var slot = dropped.stats == null ? -1 : dropped.stats.slot;
            var oldWeapon = slot >= 0 && slot < body.weapons.Count ? body.weapons[slot] : null;
            var oldAmmo = slot >= 0 && slot < body.weaponAmmos.Count ? body.weaponAmmos[slot] : 0;
            if ((WorldReplication.WorldInteraction) operation == WorldReplication.WorldInteraction.WeaponPickup && oldWeapon == null)
                WorldReplication.Instance.pendingDestroyedWeaponPickups[id] = Time.unscaledTime + 1.5f;
            writer.Write(slot);
            writer.Write(NetworkWireId.FromString(oldWeapon == null ? "" : oldWeapon.name));
            writer.Write(oldAmmo);
            writer.Write(dropped.stats != null && body.weapons.Contains(dropped.stats));
            writer.Write(body.transform.position.x);
            writer.Write(body.transform.position.y);
            MultiplayerSession.SendWorldInteraction(stream.ToArray());
        }
    }
    
    internal Rigidbody2D CreateDroppedWeapon(string id, ulong weaponId, int ammo, Vector2 position, float rotation)
    {
        var prefab = Resources.Load<GameObject>("Spawnables/PickupWeapon");
        if (prefab == null) return null;
        var weapon = FindWeaponPreset(weaponId);
        if (weapon == null) return null;
        var dropped = WorldReplication.Instantiate(prefab, position, Quaternion.Euler(0f, 0f, rotation)).GetComponent<DroppedWeapon>();
        if (dropped == null) return null;
        dropped.ChangeWeapon(weapon, ammo);
        var body = dropped.GetComponent<Rigidbody2D>();
        if (body == null) { WorldReplication.Destroy(dropped.gameObject); return null; }
        WorldReplication.Instance.bodies.bodies[id] = body;
        WorldReplication.Instance.bodies.ids[body] = id;
        WorldReplication.Instance.clientCreatedBodies.Add(body);
        WorldReplication.Instance.clientBoundDroppedWeapons.Add(body);
        WorldReplication.Instance.droppedWeapons[body] = dropped;
        WorldReplication.Instance.bodies.interactivePropBodies.Add(body);
        WorldReplication.Instance.bodies.MakeClientControlled(body);
        return body;
    }

    internal Rigidbody2D FindExistingDroppedWeapon(string id, ulong weaponId, Vector2 position)
    {
        Rigidbody2D best = null;
        var bestDistance = 4f;
        foreach (var pair in WorldReplication.Instance.droppedWeapons)
        {
            var body = pair.Key;
            var dropped = pair.Value;
            if (body == null || dropped == null || WorldReplication.Instance.clientCreatedBodies.Contains(body) ||
                WorldReplication.Instance.clientBoundDroppedWeapons.Contains(body) || dropped.stats == null ||
                NetworkWireId.FromString(dropped.stats.name) != weaponId) continue;
            var distance = (body.position - position).sqrMagnitude;
            if (distance > bestDistance) continue;
            bestDistance = distance;
            best = body;
        }
        if (best == null) return null;

        var b = WorldReplication.Instance.bodies;
        
        string previousId;
        if (b.ids.TryGetValue(best, out previousId))
            b.bodies.Remove(previousId);
        b.bodies[id] = best;
        b.ids[best] = id;
        var wire = NetworkWireId.FromString(id);
        b.wireIds[id] = wire;
        b.idsByWire[wire] = id;
        WorldReplication.Instance.clientBoundDroppedWeapons.Add(best);
        b.interactivePropBodies.Add(best);
        return best;
    }
    
    internal void SynchronizeDroppedWeapon(DroppedWeapon dropped, ulong weaponId, int ammo)
    {
        if (dropped == null) return;
        var weapon = dropped.stats;
        var changed = weapon == null || NetworkWireId.FromString(weapon.name) != weaponId;
        if (changed)
        {
            weapon = FindWeaponPreset(weaponId);
            if (weapon == null) return;
            dropped.ChangeWeapon(weapon, ammo);
        }
        if (dropped.ammoAmount != ammo)
        {
            dropped.ammoAmount = ammo;
            changed = true;
        }
        if (!changed) return;
        SynchronizeDroppedWeaponAmmoIndicator(dropped);
        if (ammo <= 0 && weapon.magExtractedSprite != null)
        {
            var renderer = dropped.GetComponent<SpriteRenderer>();
            if (renderer != null) renderer.sprite = weapon.magExtractedSprite;
        }
    }

    internal void UnloadDroppedWeapon(DroppedWeapon dropped)
    {
        if (dropped == null || dropped.stats == null || dropped.ammoAmount <= 0) return;
        dropped.ammoAmount = 0;
        var renderer = dropped.GetComponent<SpriteRenderer>();
        if (renderer != null && dropped.stats.magExtractedSprite != null)
            renderer.sprite = dropped.stats.magExtractedSprite;
        SynchronizeDroppedWeaponAmmoIndicator(dropped);
        var rigidbody = dropped.GetComponent<Rigidbody2D>();
        if (rigidbody != null)
        {
            rigidbody.AddForce(new Vector2(UnityEngine.Random.Range(-1.5f, 1.5f),
                UnityEngine.Random.Range(-1.5f, 1.5f)), ForceMode2D.Impulse);
            rigidbody.AddTorque(UnityEngine.Random.Range(-1.5f, 1.5f), ForceMode2D.Impulse);
        }
    }

    internal void ReplaceDroppedWeaponWithPrevious(DroppedWeapon dropped, BodyScript body, WeaponPreset pickedWeapon)
    {
        if (dropped == null || body == null || pickedWeapon == null) return;
        if (body.currentWeapon == pickedWeapon.slot && body.weapon != null && body.weapon.isReloading)
            body.weapon.CancelReload();
        var previousWeapon = body.weapons[pickedWeapon.slot];
        var previousAmmo = body.weaponAmmos[pickedWeapon.slot];
        dropped.pickupCool = 0.5f;
        dropped.ChangeWeapon(previousWeapon, previousAmmo);
        if (previousWeapon == null) return;
        dropped.ammoAmount = previousAmmo;
        var rigidbody = dropped.GetComponent<Rigidbody2D>();
        if (rigidbody == null) return;
        if (body.currentWeapon == pickedWeapon.slot && body.weapon != null)
        {
            dropped.transform.position = body.weapon.transform.position;
            dropped.transform.rotation = body.weapon.transform.rotation;
            if (body.isRight)
            {
                rigidbody.velocity = body.weapon.transform.right * 6f;
                dropped.transform.localScale = Vector2.one;
            }
            else
            {
                rigidbody.velocity = -body.weapon.transform.right * 6f;
                dropped.transform.localScale = new Vector2(-1f, 1f);
            }
            rigidbody.angularVelocity = UnityEngine.Random.Range(-50f, 50f);
        }
        else if (body.mainTorso != null)
        {
            if (body.isRight)
            {
                dropped.transform.position = body.mainTorso.transform.position - body.mainTorso.transform.right * 0.3f;
                dropped.transform.localScale = Vector2.one;
                dropped.transform.eulerAngles = body.mainTorso.transform.eulerAngles - new Vector3(0f, 0f, 90f);
            }
            else
            {
                dropped.transform.position = body.mainTorso.transform.position + body.mainTorso.transform.right * 0.3f;
                dropped.transform.localScale = new Vector2(-1f, 1f);
                dropped.transform.eulerAngles = body.mainTorso.transform.eulerAngles + new Vector3(0f, 0f, 90f);
            }
        }
    }

    internal static void SynchronizeDroppedWeaponAmmoIndicator(DroppedWeapon dropped)
    {
        if (dropped == null) return;
        var ammoSprite = dropped.ammoSprite;
        if (ammoSprite == null) return;
        var weapon = dropped.stats;
        var player = PlayerScript.player;
        if (weapon != null && player != null && player.ammoImages != null &&
            weapon.ammoType >= 0 && weapon.ammoType < player.ammoImages.Length)
            ammoSprite.sprite = player.ammoImages[weapon.ammoType];
        ammoSprite.transform.position = dropped.transform.position + Vector3.up * 0.6f;
        ammoSprite.transform.rotation = Quaternion.identity;
        ammoSprite.enabled = dropped.ammoAmount > 0 && Mathf.PingPong(Time.time, 0.3f) > 0.15f;
    }

    internal WeaponPreset FindWeaponPreset(ulong weaponId)
    {
        if (weaponId == 0UL) return null;
        foreach (var candidate in Resources.FindObjectsOfTypeAll<WeaponPreset>())
            if (candidate != null && NetworkWireId.FromString(candidate.name) == weaponId) return candidate;
        return null;
    }
    
    internal void AnimateClientDroppedWeaponIndicators()
    {
        if (Time.unscaledTime < nextDroppedWeaponIndicatorUpdate) return;
        nextDroppedWeaponIndicatorUpdate = Time.unscaledTime + 0.1f;
        var player = PlayerScript.player;
        var localBody = player == null ? null : player.bodyScript;
        var localPosition = localBody == null ? Vector2.zero :
            (localBody.rb == null ? (Vector2)localBody.transform.position : localBody.rb.position);
        foreach (var dropped in WorldReplication.Instance.droppedWeapons.Values)
        {
            if (dropped == null) continue;
            if (localBody != null && ((Vector2)dropped.transform.position - localPosition).sqrMagnitude > 256f)
                continue;
            SynchronizeDroppedWeaponAmmoIndicator(dropped);
        }
    }
    
    internal bool IsRuntimeDroppedWeapon(DroppedWeapon dropped)
    {
        if (dropped == null) return false;
        var root = dropped.transform.root;
        return (root != null && root.name.Contains("(Clone)")) ||
               dropped.gameObject.name.Contains("(Clone)");
    }
}