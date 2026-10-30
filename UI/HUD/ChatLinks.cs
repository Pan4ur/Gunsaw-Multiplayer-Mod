using System.Text.RegularExpressions;

internal static class ChatLinks
{
    private static readonly Regex urlRegex = new(@"<[^>]*>|(?<url>https?://[^\s<>""']+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static string Format(string text)
    {
        return urlRegex.Replace(text, match =>
        {
            if (!match.Groups["url"].Success) return match.Value;
            var url = match.Value.TrimEnd('.', ',', '!', '?', ';', ':');
            while (url.Length > 0 && IsUnmatchedClosingBracket(url))
                url = url.Substring(0, url.Length - 1);
            if (!IsWebUrl(url)) return match.Value;
            return "<link=\"chat-url\"><color=#66BBFF><u>" + url + "</u></color></link>" + match.Value.Substring(url.Length);
        });
    }

    internal static bool IsWebUrl(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) 
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrEmpty(uri.Host) && string.IsNullOrEmpty(uri.UserInfo);
    
    private static bool IsUnmatchedClosingBracket(string text)
    {
        var closing = text[text.Length - 1];
        var opening = closing == ')' ? '(' : closing == ']' ? '[' : closing == '}' ? '{' : '\0';
        if (opening == '\0') return false;
        var balance = 0;
        foreach (var character in text)
        {
            if (character == opening) balance++;
            if (character == closing) balance--;
        }
        return balance < 0;
    }
}