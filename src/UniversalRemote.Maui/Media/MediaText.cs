using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Maui.Media;

internal static class MediaText
{
    public static string Kind(MediaItemKind kind) => kind switch
    {
        MediaItemKind.LiveChannel => "TV en direct",
        MediaItemKind.Movie => "Film",
        MediaItemKind.Series => "Série",
        MediaItemKind.Episode => "Épisode",
        _ => "Média"
    };

    public static string Duration(TimeSpan? duration)
    {
        if (duration is null || duration <= TimeSpan.Zero) return "Durée inconnue";
        return duration.Value.TotalHours >= 1
            ? $"{(int)duration.Value.TotalHours} h {duration.Value.Minutes:00}"
            : $"{duration.Value.Minutes} min";
    }

    public static string Progress(TimeSpan position, TimeSpan? duration)
        => duration is { } d && d > TimeSpan.Zero
            ? $"{Format(position)} / {Format(d)}"
            : $"Reprise à {Format(position)}";

    private static string Format(TimeSpan value)
        => value.TotalHours >= 1 ? value.ToString(@"hh\:mm\:ss") : value.ToString(@"mm\:ss");
}
