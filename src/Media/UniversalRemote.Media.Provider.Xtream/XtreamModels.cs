namespace UniversalRemote.Media.Provider.Xtream;

internal sealed record XtreamCategory(string Id, string Name);

internal sealed record XtreamStream(
    string Id,
    string Name,
    string? CategoryId,
    Uri? ArtworkUri,
    string? ContainerExtension);

internal sealed record XtreamSeries(
    string Id,
    string Name,
    string? CategoryId,
    Uri? ArtworkUri);

internal sealed record XtreamEpisode(
    string Id,
    string Title,
    int SeasonNumber,
    int EpisodeNumber,
    TimeSpan? Duration,
    Uri? ArtworkUri,
    string? ContainerExtension);

internal sealed record XtreamSeriesInfo(
    string Title,
    IReadOnlyDictionary<int, Uri?> SeasonArtwork,
    IReadOnlyList<XtreamEpisode> Episodes);
