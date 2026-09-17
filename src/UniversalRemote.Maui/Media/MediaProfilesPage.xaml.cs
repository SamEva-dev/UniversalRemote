using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Maui.Media;

public sealed partial class MediaProfilesPage : ContentPage
{
    private readonly IMediaProfileService profiles;
    private readonly IPlaybackService playback;
    private MediaProfile? editing;
    private MediaProfile? active;
    private bool loading;

    public MediaProfilesPage(IMediaProfileService profiles, IPlaybackService playback)
    {
        InitializeComponent();
        this.profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        this.playback = playback ?? throw new ArgumentNullException(nameof(playback));
        kindPicker.ItemsSource = Enum.GetNames<MediaProfileKind>().Select(KindText).ToArray();
        profilesList.SelectionChanged += OnProfileSelected;
        newAdultButton.Clicked += (_, _) => BeginNew(MediaProfileKind.Adult);
        newChildButton.Clicked += (_, _) => BeginNew(MediaProfileKind.Child);
        newGuestButton.Clicked += (_, _) => BeginNew(MediaProfileKind.Guest);
        activateButton.Clicked += async (_, _) => await ActivateEditingAsync();
        saveButton.Clicked += async (_, _) => await SaveAsync();
        deleteButton.Clicked += async (_, _) => await DeleteAsync();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (loading) return;
        loading = true;
        try
        {
            active = await profiles.GetActiveAsync();
            var items = await profiles.ListAsync();
            profilesList.ItemsSource = items.Select(x => new ProfileRow(x, x.Id == active.Id)).ToArray();
            activeProfileLabel.Text = $"{active.DisplayName} • {KindText(active.Kind)}";
            var canManage = active.Kind == MediaProfileKind.Adult;
            newAdultButton.IsEnabled = canManage;
            newChildButton.IsEnabled = canManage;
            newGuestButton.IsEnabled = canManage;
            editorPanel.IsVisible = canManage && editing is not null;
            statusLabel.Text = canManage
                ? $"Langue {active.Preferences.PreferredLanguage.ToUpperInvariant()} • {(active.IsPinProtected ? "PIN actif" : "protégez ce profil par PIN avant de créer un profil enfant/invité")}" 
                : "Mode restreint : sélectionnez un profil adulte pour accéder aux réglages.";
        }
        catch (Exception)
        {
            statusLabel.Text = "Impossible de charger les profils Media.";
        }
        finally { loading = false; }
    }

    private async void OnProfileSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not ProfileRow row) return;
        profilesList.SelectedItem = null;

        if (active?.Kind == MediaProfileKind.Adult)
        {
            LoadEditor(row.Profile);
            return;
        }

        await SwitchToAsync(row.Profile);
    }

    private void BeginNew(MediaProfileKind kind)
    {
        if (active?.Kind != MediaProfileKind.Adult) return;
        var restrictions = kind == MediaProfileKind.Child
            ? new MediaProfileRestrictions(maximumAgeRating: 12, blockUnratedContent: true)
            : MediaProfileRestrictions.Unrestricted;
        var profile = new MediaProfile(
            Guid.NewGuid(),
            kind switch { MediaProfileKind.Child => "Enfant", MediaProfileKind.Guest => "Invité", _ => "Adulte" },
            kind,
            restrictions,
            new MediaProfilePreferences("fr", autoplayNextEpisode: kind != MediaProfileKind.Guest),
            isPinProtected: kind == MediaProfileKind.Adult);
        LoadEditor(profile);
    }

    private void LoadEditor(MediaProfile profile)
    {
        if (active?.Kind != MediaProfileKind.Adult) return;
        editing = profile;
        editorPanel.IsVisible = true;
        nameEntry.Text = profile.DisplayName;
        kindPicker.SelectedIndex = (int)profile.Kind;
        pinSwitch.IsToggled = profile.IsPinProtected;
        pinEntry.Text = string.Empty;
        liveSwitch.IsToggled = profile.Restrictions.AllowedKinds.Contains(MediaItemKind.LiveChannel);
        movieSwitch.IsToggled = profile.Restrictions.AllowedKinds.Contains(MediaItemKind.Movie);
        seriesSwitch.IsToggled = profile.Restrictions.AllowedKinds.Contains(MediaItemKind.Series) || profile.Restrictions.AllowedKinds.Contains(MediaItemKind.Episode);
        maxAgeEntry.Text = profile.Restrictions.MaximumAgeRating?.ToString() ?? string.Empty;
        unratedSwitch.IsToggled = profile.Restrictions.BlockUnratedContent;
        blockedCategoriesEditor.Text = string.Join(", ", profile.Restrictions.BlockedCategories);
        languageEntry.Text = profile.Preferences.PreferredLanguage;
        autoplaySwitch.IsToggled = profile.Preferences.AutoplayNextEpisode;
        subtitlesSwitch.IsToggled = profile.Preferences.ShowSubtitlesByDefault;
        activateButton.IsEnabled = active?.Id != profile.Id;
        deleteButton.IsEnabled = active?.Id != profile.Id;
    }

    private async Task ActivateEditingAsync()
    {
        if (editing is null || active?.Kind != MediaProfileKind.Adult) return;
        await SwitchToAsync(editing);
    }

    private async Task SwitchToAsync(MediaProfile profile)
    {
        if (active?.Id == profile.Id) return;
        string? pin = null;
        if (profile.IsPinProtected)
            pin = await DisplayPromptAsync("Profil protégé", $"PIN pour « {profile.DisplayName} »", "Valider", "Annuler", "4 à 8 chiffres", 8, Keyboard.Numeric);

        if (profile.IsPinProtected && string.IsNullOrWhiteSpace(pin)) return;
        var result = await profiles.SwitchAsync(profile.Id, pin);
        if (!result.Succeeded)
        {
            await DisplayAlertAsync("Profils Media", result.Message ?? "Impossible de changer de profil.", "OK");
            return;
        }

        if (playback.CurrentSession is not null)
        {
            try { await playback.StopAsync(); }
            catch { /* A profile switch must not be undone because the player failed to stop cleanly. */ }
        }
        editing = null;
        editorPanel.IsVisible = false;
        await RefreshAsync();
    }

    private async Task SaveAsync()
    {
        if (editing is null || active?.Kind != MediaProfileKind.Adult) return;
        try
        {
            var name = nameEntry.Text?.Trim() ?? string.Empty;
            if (name.Length == 0) throw new ArgumentException("Donnez un nom au profil.");
            var kind = (MediaProfileKind)Math.Clamp(kindPicker.SelectedIndex, 0, 2);
            int? maximumAge = null;
            if (!string.IsNullOrWhiteSpace(maxAgeEntry.Text))
            {
                if (!int.TryParse(maxAgeEntry.Text, out var parsedAge) || parsedAge is < 0 or > 21)
                    throw new ArgumentException("L’âge maximal doit être compris entre 0 et 21.");
                maximumAge = parsedAge;
            }

            var allowedKinds = new List<MediaItemKind>();
            if (liveSwitch.IsToggled) allowedKinds.Add(MediaItemKind.LiveChannel);
            if (movieSwitch.IsToggled) allowedKinds.Add(MediaItemKind.Movie);
            if (seriesSwitch.IsToggled) { allowedKinds.Add(MediaItemKind.Series); allowedKinds.Add(MediaItemKind.Episode); }
            var blockedCategories = (blockedCategoriesEditor.Text ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var updated = new MediaProfile(
                editing.Id,
                name,
                kind,
                new MediaProfileRestrictions(
                    allowedKinds,
                    blockedCategories,
                    editing.Restrictions.BlockedItems,
                    maximumAge,
                    unratedSwitch.IsToggled),
                new MediaProfilePreferences(
                    string.IsNullOrWhiteSpace(languageEntry.Text) ? "fr" : languageEntry.Text,
                    autoplaySwitch.IsToggled,
                    subtitlesSwitch.IsToggled,
                    editing.Preferences.PreferredPlaybackTargetId),
                pinSwitch.IsToggled);

            await profiles.SaveAsync(updated, string.IsNullOrWhiteSpace(pinEntry.Text) ? null : pinEntry.Text.Trim());
            editing = updated;
            await RefreshAsync();
            LoadEditor(updated);
            await DisplayAlertAsync("Profils Media", "Profil enregistré.", "OK");
        }
        catch (ArgumentException ex)
        {
            await DisplayAlertAsync("Profils Media", ex.Message, "OK");
        }
        catch (InvalidOperationException ex)
        {
            await DisplayAlertAsync("Profils Media", ex.Message, "OK");
        }
        catch (Exception)
        {
            await DisplayAlertAsync("Profils Media", "Impossible d’enregistrer le profil.", "OK");
        }
    }

    private async Task DeleteAsync()
    {
        if (editing is null || active?.Kind != MediaProfileKind.Adult || active.Id == editing.Id) return;
        if (!await DisplayAlertAsync("Supprimer le profil", $"Supprimer « {editing.DisplayName} » ?", "Supprimer", "Annuler")) return;
        if (!await profiles.DeleteAsync(editing.Id))
        {
            await DisplayAlertAsync("Profils Media", "Ce profil ne peut pas être supprimé : conservez un profil adulte protégé par PIN.", "OK");
            return;
        }
        editing = null;
        editorPanel.IsVisible = false;
        await RefreshAsync();
    }

    private static string KindText(MediaProfileKind kind) => kind switch
    {
        MediaProfileKind.Child => "Enfant",
        MediaProfileKind.Guest => "Invité",
        _ => "Adulte"
    };

    private static string KindText(string enumName)
        => Enum.TryParse<MediaProfileKind>(enumName, out var kind) ? KindText(kind) : enumName;

    private sealed class ProfileRow(MediaProfile profile, bool active)
    {
        public MediaProfile Profile { get; } = profile;
        public string Name => Profile.DisplayName;
        public string ActiveText => active ? "ACTIF" : string.Empty;
        public string Subtitle => $"{KindText(Profile.Kind)} • {(Profile.IsPinProtected ? "PIN" : "sans PIN")}";
    }
}
