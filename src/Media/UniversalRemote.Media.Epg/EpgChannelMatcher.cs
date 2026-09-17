using System.Globalization;
using System.Text;
using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Epg;

public sealed class EpgChannelMatcher
{
    public string? Match(MediaItem channel, IReadOnlyList<EpgChannel> guideChannels)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(guideChannels);
        if (channel.Kind != MediaItemKind.LiveChannel) return null;

        if (!string.IsNullOrWhiteSpace(channel.GuideId))
        {
            var exact = guideChannels.FirstOrDefault(x => string.Equals(x.GuideId, channel.GuideId, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact.GuideId;
        }

        var channelName = Normalize(channel.Title);
        if (channelName.Length == 0) return null;
        foreach (var guideChannel in guideChannels)
        {
            if (Normalize(guideChannel.GuideId) == channelName) return guideChannel.GuideId;
            if (guideChannel.DisplayNames.Any(x => Normalize(x) == channelName)) return guideChannel.GuideId;
        }

        return null;
    }

    internal static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) builder.Append(char.ToUpperInvariant(ch));
        }
        return builder.ToString();
    }
}
