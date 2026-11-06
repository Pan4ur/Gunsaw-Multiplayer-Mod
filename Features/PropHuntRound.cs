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
    private readonly Dictionary<ushort, float> locators = new();
    internal readonly HashSet<ushort> Hunters = new();
    internal ushort Hunter => Hunters.FirstOrDefault();
    internal PropHuntPhase Phase { get; private set; }
    internal float Deadline { get; private set; }
    private float duration;
    private readonly Dictionary<ushort, float> pushes = new(), kicks = new();
    internal bool RestartDue(float now) => Phase >= PropHuntPhase.PropsWin && now >= Deadline;

    internal void Start(ushort[] players, ushort hunter, float now, float seconds)
        => Start(players, new[] { hunter }, now, seconds);

    internal void Start(ushort[] players, ushort[] hunters, float now, float seconds)
    {
        if (players.Length < 2 || hunters.Length == 0 || hunters.Distinct().Count() != hunters.Length || hunters.Length >= players.Distinct().Count() || hunters.Any(id => !players.Contains(id))) throw new ArgumentException("A hunter and at least one prop are required.");
        Reset();
        foreach (var player in players) Alive.Add(player);
        Hunters.UnionWith(hunters);
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
        locators.Clear();
        pushes.Clear();
        kicks.Clear();
        Hunters.Clear();
        Deadline = 0f;
        Phase = PropHuntPhase.Waiting;
    }

    internal void Advance(float now)
    {
        if (Phase != PropHuntPhase.Hiding && Phase != PropHuntPhase.Hunting) return;
        if (!Hunters.Any(Alive.Contains)) Phase = PropHuntPhase.PropsWin;
        else if (Alive.All(Hunters.Contains)) Phase = PropHuntPhase.HunterWins;
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

    internal bool Change(ushort player, string[] choices, float now, float cooldown = 30f)
    {
        if ((Phase != PropHuntPhase.Hiding && Phase != PropHuntPhase.Hunting) || Hunters.Contains(player) || !Alive.Contains(player) ||
            (changes.TryGetValue(player, out var ready) && now < ready)) return false;
        Props.TryGetValue(player, out var previous);
        var available = choices.Where(name => name != previous).ToArray();
        if (available.Length == 0) return false;
        Props[player] = available[UnityEngine.Random.Range(0, available.Length)];
        changes[player] = now + cooldown;
        return true;
    }
    
    internal bool Stun(ushort player, ushort target, float distance, float now, float cooldown = 30f)
    {
        if ((Phase != PropHuntPhase.Hiding && Phase != PropHuntPhase.Hunting) || Hunters.Contains(player) || !Alive.Contains(player) || !Hunters.Contains(target) || !Alive.Contains(target) ||
            float.IsNaN(distance) || distance > PlayerCarrySystem.Range || distance < 0f ||
            (stuns.TryGetValue(player, out var ready) && now < ready)) return false;
        stuns[player] = now + cooldown;
        return true;
    }

    internal bool Push(ushort sender, ushort target, float direction, float distance, float now)
    {
        if (Phase != PropHuntPhase.Hunting || !Hunters.Contains(sender) || !Alive.Contains(sender) || !Alive.Contains(target) ||
            !Props.ContainsKey(target) || (direction != -1f && direction != 1f) ||
            float.IsNaN(distance) || distance < 0f || distance > 1f || (pushes.TryGetValue(sender, out var pushAt) && now < pushAt)) return false;
        pushes[sender] = now + 0.1f;
        return true;
    }

    internal bool Locate(ushort sender, float now, bool enabled, float cooldown)
    {
        if (!enabled || Phase != PropHuntPhase.Hunting || !Hunters.Contains(sender) || !Alive.Contains(sender) || (locators.TryGetValue(sender, out var ready) && now < ready))
            return false;
            
        locators[sender] = now + cooldown;
        return true;
    }

    internal bool KickMiss(ushort sender, float now)
    {
        if (Phase != PropHuntPhase.Hunting || !Hunters.Contains(sender) || !Alive.Contains(sender) || (kicks.TryGetValue(sender, out var kickAt) && now < kickAt)) return false;
        kicks[sender] = now + 0.4f;
        return true;
    }
}
