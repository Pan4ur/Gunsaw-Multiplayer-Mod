internal readonly struct DeliverySettings
{
    internal static readonly DeliverySettings Default = new (false, false, true);
    internal static readonly DeliverySettings Lossless = new (true, false, true);

    internal readonly bool Reliable;
    internal readonly bool Replaceable;
    internal readonly bool Priority;
    internal readonly bool Compress;
    
    internal DeliverySettings(bool reliable, bool replaceable, bool priority, bool compress = false)
    {
        Reliable = reliable;
        Replaceable = replaceable;
        Priority = priority;
        Compress = compress;
    }
}