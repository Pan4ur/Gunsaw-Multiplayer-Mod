internal enum PropHuntPhase : byte
{
    Waiting,
    Hiding,
    Hunting,
    PropsWin,
    HunterWins
}

internal sealed class PropHuntRound
{
    internal readonly Dictionary<ushort, string> Props = new();
    internal readonly HashSet<ushort> Alive = new();
    private readonly Dictionary<ushort, float> changes = new();
    private readonly Dictionary<ushort, float> stuns = new();
    internal ushort Hunter { get; private set; }
    internal PropHuntPhase Phase { get; private set; }
    internal float Deadline { get; private set; }
    private float duration;
    private float pushAt, kickAt;
    internal bool RestartDue(float now) => Phase >= PropHuntPhase.PropsWin && now >= Deadline;

    internal void Start(ushort[] players, ushort hunter, float now, float seconds)
    {
        if (players.Length < 2 || !players.Contains(hunter)) throw new ArgumentException("A hunter and at least one prop are required.");
        Reset();
        foreach (var player in players) Alive.Add(player);
        Hunter = hunter;
        duration = seconds;
        Deadline = now + 30f;
        Phase = PropHuntPhase.Hiding;
    }

    internal void Reset()
    {
        Props.Clear();
        Alive.Clear();
        changes.Clear();
        stuns.Clear();
        pushAt = kickAt = 0f;
        Hunter = 0;
        Deadline = 0f;
        Phase = PropHuntPhase.Waiting;
    }

    internal void Advance(float now)
    {
        if (Phase != PropHuntPhase.Hiding && Phase != PropHuntPhase.Hunting) return;
        if (!Alive.Contains(Hunter)) Phase = PropHuntPhase.PropsWin;
        else if (Alive.All(id => id == Hunter)) Phase = PropHuntPhase.HunterWins;
        else if (now >= Deadline)
        {
            if (Phase == PropHuntPhase.Hiding)
            {
                Phase = PropHuntPhase.Hunting;
                Deadline += duration;
                if (now >= Deadline) Phase = PropHuntPhase.PropsWin;
            }
            else Phase = PropHuntPhase.PropsWin;
        }
        if (Phase >= PropHuntPhase.PropsWin) Deadline = now + 5f;
    }

    internal bool Change(ushort player, string[] choices, float now)
    {
        if ((Phase != PropHuntPhase.Hiding && Phase != PropHuntPhase.Hunting) || player == Hunter || !Alive.Contains(player) ||
            (changes.TryGetValue(player, out var ready) && now < ready)) return false;
        Props.TryGetValue(player, out var previous);
        var available = choices.Where(name => name != previous).ToArray();
        if (available.Length == 0) return false;
        Props[player] = available[UnityEngine.Random.Range(0, available.Length)];
        changes[player] = now + 30f;
        return true;
    }

    internal bool Stun(ushort player, float distance, float now)
    {
        if ((Phase != PropHuntPhase.Hiding && Phase != PropHuntPhase.Hunting) || player == Hunter || !Alive.Contains(player) || !Alive.Contains(Hunter) ||
            float.IsNaN(distance) || distance > PlayerCarrySystem.Range || distance < 0f ||
            (stuns.TryGetValue(player, out var ready) && now < ready)) return false;
        stuns[player] = now + 30f;
        return true;
    }

    internal bool Push(ushort sender, ushort target, float direction, float distance, float now)
    {
        if (Phase != PropHuntPhase.Hunting || sender != Hunter || !Alive.Contains(sender) || !Alive.Contains(target) ||
            !Props.ContainsKey(target) || (direction != -1f && direction != 1f) ||
            float.IsNaN(distance) || distance < 0f || distance > 1f || now < pushAt) return false;
        pushAt = now + 0.1f;
        return true;
    }

    internal bool KickMiss(ushort sender, float now)
    {
        if (Phase != PropHuntPhase.Hunting || sender != Hunter || !Alive.Contains(sender) || now < kickAt) return false;
        kickAt = now + 0.4f;
        return true;
    }
}
