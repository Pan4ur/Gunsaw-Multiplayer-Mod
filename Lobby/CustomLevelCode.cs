using System.IO.Compression;
using System.Text;
using UnityEngine;

internal static class CustomLevelCode
{
    internal static string DecodeAndValidateLevelCode(string code, int maxBytes = int.MaxValue)
    {
        return ValidateLevelJson(Compression.Decompress(code), maxBytes);
    }

    internal static string DecodeAndValidateCatalogLevelCode(string code, int maxBytes = int.MaxValue)
    {
        using (var compressed = new MemoryStream(Convert.FromBase64String((code ?? "").Trim())))
        using (var inflater = new DeflateStream(compressed, CompressionMode.Decompress))
        using (var output = new MemoryStream())
        {
            var buffer = new byte[8192];
            int count;
            while ((count = inflater.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + count > maxBytes)
                    throw new InvalidDataException("The level is larger than " + maxBytes / (1024 * 1024) + " MB.");
                output.Write(buffer, 0, count);
            }
            return ValidateLevelJson(Encoding.UTF8.GetString(output.ToArray()).Trim(), maxBytes);
        }
    }

    internal static string ValidateLevelJson(string json, int maxBytes = int.MaxValue)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("The level JSON is invalid.");
        if (Encoding.UTF8.GetByteCount(json) > maxBytes)
            throw new InvalidDataException("The level is larger than " + maxBytes / (1024 * 1024) + " MB.");
        if (JsonUtility.FromJson<Level>(json) == null)
            throw new InvalidDataException("The level JSON is invalid.");
        return json;
    }
}
