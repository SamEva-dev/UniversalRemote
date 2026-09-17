using System.Text.Json.Serialization;
using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Epg;

internal sealed record EpgCacheDto(
    Guid MediaSourceId,
    Guid EpgSourceId,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    DateTimeOffset RefreshedAt,
    EpgCacheChannelDto[] Channels)
{
    public static EpgCacheDto FromModel(EpgGuideSnapshot snapshot)
        => new(snapshot.MediaSourceId, snapshot.EpgSourceId, snapshot.WindowStart, snapshot.WindowEnd, snapshot.RefreshedAt,
            snapshot.Channels.Select(EpgCacheChannelDto.FromModel).ToArray());

    public EpgGuideSnapshot ToModel()
        => new(MediaSourceId, EpgSourceId, WindowStart, WindowEnd, Channels.Select(x => x.ToModel(MediaSourceId)), RefreshedAt);
}

internal sealed record EpgCacheChannelDto(
    string ExternalId,
    string Title,
    string? Category,
    string? ArtworkUri,
    string? GuideId,
    string? MatchedGuideId,
    EpgCacheProgramDto[] Programs)
{
    public static EpgCacheChannelDto FromModel(EpgGuideChannel row)
        => new(row.Channel.ExternalId, row.Channel.Title, row.Channel.Category, row.Channel.ArtworkUri?.AbsoluteUri,
            row.Channel.GuideId, row.MatchedGuideId, row.Programs.Select(EpgCacheProgramDto.FromModel).ToArray());

    public EpgGuideChannel ToModel(Guid sourceId)
    {
        Uri? artwork = Uri.TryCreate(ArtworkUri, UriKind.Absolute, out var parsed) ? parsed : null;
        var media = new MediaItem(sourceId, ExternalId, MediaItemKind.LiveChannel, Title, Category, artworkUri: artwork, guideId: GuideId);
        return new EpgGuideChannel(media, MatchedGuideId, Programs.Select(static x => x.ToModel()));
    }
}

internal sealed record EpgCacheProgramDto(
    string ChannelGuideId,
    string Title,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? Description,
    string? Category,
    string? ArtworkUri)
{
    public static EpgCacheProgramDto FromModel(EpgProgram program)
        => new(program.ChannelGuideId, program.Title, program.StartsAt, program.EndsAt, program.Description, program.Category, program.ArtworkUri?.AbsoluteUri);

    public EpgProgram ToModel()
    {
        Uri? artwork = Uri.TryCreate(ArtworkUri, UriKind.Absolute, out var parsed) ? parsed : null;
        return new EpgProgram(ChannelGuideId, Title, StartsAt, EndsAt, Description, Category, artwork);
    }
}

[JsonSerializable(typeof(EpgCacheDto))]
internal sealed partial class EpgCacheJsonContext : JsonSerializerContext;
