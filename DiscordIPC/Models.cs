namespace DiscordIPC
{
    public sealed class PresencePair
    {
        public string First { get; set; }
        public string Second { get; set; }

        public PresencePair(string first, string second)
        {
            First = first;
            Second = second;
        }
    }

    public sealed class PresenceParty
    {
        public string Id { get; set; }
        public int Size { get; set; }
        public int MaxSize { get; set; }

        public PresenceParty(string id, int size, int maxSize)
        {
            Id = id;
            Size = size;
            MaxSize = maxSize;
        }
    }

    public sealed class ActivityButton
    {
        public string Label { get; set; }
        public string Url { get; set; }

        public ActivityButton(string label, string url)
        {
            Label = label;
            Url = url;
        }
    }

    public sealed class ActivityAsset
    {
        public string LargeImage { get; set; }
        public string LargeText { get; set; }
        public string SmallImage { get; set; }
        public string SmallText { get; set; }
    }

    public sealed class ActivityTimestamp
    {
        public long Start { get; set; }
        public long? End { get; set; }
    }

    public sealed class Activity
    {
        public string Details { get; set; }
        public string State { get; set; }
        public ActivityTimestamp Timestamps { get; set; }
        public ActivityAsset Assets { get; set; }
        public List<ActivityButton> Buttons { get; set; }
        public PresenceParty Party { get; set; }
        public string JoinSecret { get; set; }
        public bool Instance { get; set; }
    }

    public sealed class IPCUser
    {
        public string Id { get; internal set; }
        public string Username { get; internal set; }
        public string Avatar { get; internal set; }

        public IPCUser()
        {
            Id = "none";
            Username = "none";
            Avatar = "none";
        }

        internal IPCUser(string id, string username, string avatar)
        {
            Id = string.IsNullOrEmpty(id) ? "none" : id;
            Username = string.IsNullOrEmpty(username) ? "none" : username;
            Avatar = string.IsNullOrEmpty(avatar) ? "none" : avatar;
        }

        public string AvatarLink(int size = 128, bool forcePng = false)
        {
            string extension = Avatar.StartsWith("a_", StringComparison.Ordinal) && !forcePng ? "gif" : "png";
            return "https://cdn.discordapp.com/avatars/" + Id + "/" + Avatar + "." + extension + "?size=" + size;
        }
    }
}
