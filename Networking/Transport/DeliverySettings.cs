public readonly struct DeliverySettings
{
    public static readonly DeliverySettings Default = new (false, false, true);
    public static readonly DeliverySettings Lossless = new (true, false, true);

    public readonly bool Reliable;
    public readonly bool Replaceable;
    public readonly bool Priority;
    public readonly bool Compress;
    
    public DeliverySettings(bool reliable, bool replaceable, bool priority, bool compress = false)
    {
        Reliable = reliable;
        Replaceable = replaceable;
        Priority = priority;
        Compress = compress;
    }
}