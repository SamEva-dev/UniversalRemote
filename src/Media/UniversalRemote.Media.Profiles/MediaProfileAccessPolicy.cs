using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Profiles;

public sealed class MediaProfileAccessPolicy(IMediaProfileService profiles) : IMediaAccessPolicy
{
    public Task<MediaAccessDecision> EvaluateAsync(MediaItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        return EvaluateAsync(MediaReference.From(item), item.Category, item.MinimumAge, cancellationToken);
    }

    public async Task<MediaAccessDecision> EvaluateAsync(
        MediaReference reference,
        string? category = null,
        int? minimumAge = null,
        CancellationToken cancellationToken = default)
    {
        var profile = await profiles.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        var restrictions = profile.Restrictions;

        if (!restrictions.AllowedKinds.Contains(reference.Kind))
            return MediaAccessDecision.Denied("profile.kind_blocked", "Ce type de contenu n’est pas autorisé pour le profil actif.");

        if (restrictions.BlockedItems.Contains(reference))
            return MediaAccessDecision.Denied("profile.item_blocked", "Ce contenu est bloqué pour le profil actif.");

        if (!string.IsNullOrWhiteSpace(category) && restrictions.BlockedCategories.Contains(category.Trim()))
            return MediaAccessDecision.Denied("profile.category_blocked", "Cette catégorie est bloquée pour le profil actif.");

        if (restrictions.MaximumAgeRating is { } maximum)
        {
            if (minimumAge is { } requiredAge && requiredAge > maximum)
                return MediaAccessDecision.Denied("profile.age_blocked", "Ce contenu dépasse l’âge autorisé pour le profil actif.");
            if (minimumAge is null && restrictions.BlockUnratedContent)
                return MediaAccessDecision.Denied("profile.unrated_blocked", "Ce contenu n’a pas de classification d’âge exploitable et est bloqué par ce profil.");
        }

        return MediaAccessDecision.Allowed;
    }
}
