using UniversalRemote.Abstractions;
using UniversalRemote.Application;
using UniversalRemote.Presentation;

namespace UniversalRemote.Maui.Activities;

public sealed class ActivitiesPage : ContentPage
{
    private readonly ActivitiesViewModel viewModel;
    private readonly VerticalStackLayout activitiesHost = new() { Spacing = 12 };
    private readonly VerticalStackLayout stepsHost = new() { Spacing = 10 };
    private readonly VerticalStackLayout reportHost = new() { Spacing = 10 };
    private readonly Label status = new() { FontSize = 12, HorizontalTextAlignment = TextAlignment.Center };
    private readonly Button newButton = new() { Text = RemoteLabels.Text("Nouvelle activité", "New activity"), MinimumHeightRequest = 48 };
    private readonly Button refreshButton = new() { Text = RemoteLabels.Text("Actualiser", "Refresh"), MinimumHeightRequest = 48 };
    private readonly Button cancelRunButton = new() { Text = RemoteLabels.Text("Annuler l’exécution", "Cancel run"), MinimumHeightRequest = 48, IsVisible = false };
    private readonly ActivityIndicator runIndicator = new() { IsVisible = false };
    private readonly Entry nameEntry = new() { Placeholder = "Regarder la TV", MaxLength = 80, MinimumHeightRequest = 48 };
    private readonly Picker roomPicker = new() { Title = RemoteLabels.Text("Pièce (optionnelle)", "Room (optional)"), MinimumHeightRequest = 48 };
    private readonly Picker policyPicker = new() { Title = RemoteLabels.Text("Politique d’échec", "Failure policy"), MinimumHeightRequest = 48 };
    private readonly Border editorBorder;
    private readonly Button addCommandButton = new() { Text = RemoteLabels.Text("Ajouter une commande", "Add command"), MinimumHeightRequest = 48 };
    private readonly Button addDelayButton = new() { Text = RemoteLabels.Text("Ajouter un délai", "Add delay"), MinimumHeightRequest = 48 };
    private readonly Button saveButton = new() { Text = RemoteLabels.Text("Enregistrer", "Save"), MinimumHeightRequest = 48 };
    private readonly Button closeEditorButton = new() { Text = RemoteLabels.Text("Fermer", "Close"), MinimumHeightRequest = 48 };

    public ActivitiesPage(ActivitiesViewModel viewModel)
    {
        this.viewModel = viewModel;
        Title = RemoteLabels.Text("Activités", "Activities");
        BindingContext = viewModel;

        status.SetBinding(Label.TextProperty, nameof(ActivitiesViewModel.Status));
        cancelRunButton.SetBinding(IsVisibleProperty, nameof(ActivitiesViewModel.IsRunning));
        runIndicator.SetBinding(ActivityIndicator.IsRunningProperty, nameof(ActivitiesViewModel.IsRunning));
        runIndicator.SetBinding(IsVisibleProperty, nameof(ActivitiesViewModel.IsRunning));

        policyPicker.ItemsSource = new[]
        {
            new FailurePolicyChoice(
                ActivityFailurePolicy.StopOnFirstNonAccepted,
                RemoteLabels.Text("Sécurisé — arrêter au premier échec ou résultat incertain", "Safe — stop on first failed or uncertain result")),
            new FailurePolicyChoice(
                ActivityFailurePolicy.ContinueAfterNonAccepted,
                RemoteLabels.Text("Continuer — ne jamais rejouer la commande en incident", "Continue — never replay the command in issue"))
        };
        policyPicker.ItemDisplayBinding = new Binding(nameof(FailurePolicyChoice.Label));
        policyPicker.SelectedIndex = 0;

        roomPicker.ItemDisplayBinding = new Binding(nameof(RoomChoice.Label));

        newButton.Clicked += (_, _) => BeginNew();
        refreshButton.Clicked += async (_, _) => await RefreshAsync().ConfigureAwait(true);
        cancelRunButton.Clicked += (_, _) => viewModel.CancelRun();
        addCommandButton.Clicked += async (_, _) => await AddCommandAsync().ConfigureAwait(true);
        addDelayButton.Clicked += async (_, _) => await AddDelayAsync().ConfigureAwait(true);
        saveButton.Clicked += async (_, _) => await SaveEditorAsync().ConfigureAwait(true);
        closeEditorButton.Clicked += (_, _) => CloseEditor();

        editorBorder = new Border
        {
            Padding = new Thickness(14),
            StrokeThickness = 1,
            IsVisible = false,
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    new Label { Text = RemoteLabels.Text("Éditeur d’activité", "Activity editor"), FontSize = 20, FontAttributes = FontAttributes.Bold },
                    new Label
                    {
                        Text = RemoteLabels.Text(
                            "Les étapes utilisent uniquement des Device.Id persistants et des RemoteAction normalisées. Aucune logique constructeur n’est enregistrée dans la macro.",
                            "Steps use only persistent Device.Id values and normalized RemoteAction IDs. No vendor-specific logic is stored in the macro."),
                        FontSize = 12
                    },
                    nameEntry,
                    roomPicker,
                    new HorizontalStackLayout { Spacing = 8, Children = { addCommandButton, addDelayButton } },
                    stepsHost,
                    new HorizontalStackLayout { Spacing = 8, Children = { saveButton, closeEditorButton } }
                }
            }
        };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new Label { Text = RemoteLabels.Text("Activités", "Activities"), FontSize = 24, FontAttributes = FontAttributes.Bold },
                    new Label
                    {
                        Text = RemoteLabels.Text(
                            "Créez des scénarios multi-appareils, ordonnez les commandes et délais, puis lancez-les avec une politique explicite en cas d’incident.",
                            "Create multi-device scenarios, order commands and delays, then run them with an explicit issue policy."),
                        FontSize = 13
                    },
                    new HorizontalStackLayout { Spacing = 10, Children = { newButton, refreshButton } },
                    policyPicker,
                    new HorizontalStackLayout { Spacing = 10, Children = { runIndicator, cancelRunButton } },
                    status,
                    editorBorder,
                    activitiesHost,
                    reportHost
                }
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAsync().ConfigureAwait(true);
    }

    private async Task RefreshAsync()
    {
        SetInteractive(false);
        try
        {
            await viewModel.RefreshAsync().ConfigureAwait(true);
            RenderActivities();
            if (viewModel.IsEditing)
            {
                PopulateEditor();
                RenderDraftSteps();
            }
        }
        finally { SetInteractive(true); }
    }

    private void BeginNew()
    {
        try
        {
            viewModel.BeginNew();
            PopulateEditor();
            RenderDraftSteps();
            editorBorder.IsVisible = true;
        }
        catch (InvalidOperationException) { }
    }

    private async Task BeginEditAsync(ActivitySummary activity, Button button)
    {
        button.IsEnabled = false;
        try
        {
            await viewModel.BeginEditAsync(activity.Id).ConfigureAwait(true);
            PopulateEditor();
            RenderDraftSteps();
            editorBorder.IsVisible = true;
        }
        catch (Exception)
        {
            await DisplayAlertAsync(
                RemoteLabels.Text("Activité", "Activity"),
                RemoteLabels.Text("Impossible de charger cette activité.", "Unable to load this activity."),
                "OK").ConfigureAwait(true);
        }
        finally { button.IsEnabled = true; }
    }

    private void CloseEditor()
    {
        viewModel.CancelEditing();
        editorBorder.IsVisible = false;
        stepsHost.Children.Clear();
    }

    private void PopulateEditor()
    {
        nameEntry.Text = viewModel.EditorName;
        var choices = new List<RoomChoice>
        {
            new(null, RemoteLabels.Text("Sans pièce", "No room"))
        };
        choices.AddRange(viewModel.Rooms.Select(room => new RoomChoice(room.Id, room.Name)));
        roomPicker.ItemsSource = choices;
        var index = choices.FindIndex(choice => choice.Id == viewModel.EditorRoomId);
        roomPicker.SelectedIndex = index >= 0 ? index : 0;
    }

    private void RenderActivities()
    {
        activitiesHost.Children.Clear();
        if (viewModel.Activities.Count == 0)
        {
            activitiesHost.Children.Add(new Label
            {
                Text = RemoteLabels.Text("Aucune activité enregistrée.", "No saved activities."),
                HorizontalTextAlignment = TextAlignment.Center
            });
            return;
        }

        foreach (var activity in viewModel.Activities)
            activitiesHost.Children.Add(ActivityCard(activity));
    }

    private View ActivityCard(ActivitySummary activity)
    {
        var edit = new Button { Text = RemoteLabels.Text("Modifier", "Edit"), MinimumHeightRequest = 44, AutomationId = $"activity-edit-{activity.Id:N}" };
        var run = new Button { Text = RemoteLabels.Text("Lancer", "Run"), MinimumHeightRequest = 44, AutomationId = $"activity-run-{activity.Id:N}" };
        var delete = new Button { Text = RemoteLabels.Text("Supprimer", "Delete"), MinimumHeightRequest = 44, AutomationId = $"activity-delete-{activity.Id:N}" };

        edit.Clicked += async (_, _) => await BeginEditAsync(activity, edit).ConfigureAwait(true);
        run.Clicked += async (_, _) => await RunAsync(activity, run).ConfigureAwait(true);
        delete.Clicked += async (_, _) => await DeleteAsync(activity, delete).ConfigureAwait(true);

        return new Border
        {
            Padding = new Thickness(14),
            StrokeThickness = 1,
            Content = new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    new Label { Text = activity.Name, FontSize = 18, FontAttributes = FontAttributes.Bold },
                    new Label
                    {
                        Text = RemoteLabels.Text(
                            $"{viewModel.RoomName(activity.RoomId)} • {activity.Steps.Count} étape(s)",
                            $"{viewModel.RoomName(activity.RoomId)} • {activity.Steps.Count} step(s)"),
                        FontSize = 12
                    },
                    new HorizontalStackLayout { Spacing = 8, Children = { edit, run, delete } }
                }
            }
        };
    }

    private async Task AddCommandAsync()
    {
        if (viewModel.Devices.Count == 0)
        {
            await DisplayAlertAsync(
                RemoteLabels.Text("Commande", "Command"),
                RemoteLabels.Text("Associez d’abord un appareil.", "Pair a device first."),
                "OK").ConfigureAwait(true);
            return;
        }

        var deviceLabels = viewModel.Devices.Select((device, index) => $"{index + 1}. {device.DisplayName}").ToArray();
        var selectedDeviceLabel = await DisplayActionSheetAsync(
            RemoteLabels.Text("Choisir un appareil", "Choose a device"),
            RemoteLabels.Text("Annuler", "Cancel"),
            null,
            deviceLabels).ConfigureAwait(true);
        var deviceIndex = Array.IndexOf(deviceLabels, selectedDeviceLabel);
        if (deviceIndex < 0) return;
        var device = viewModel.Devices[deviceIndex];

        IReadOnlyList<ActivityActionOption> actions;
        try { actions = await viewModel.GetActionsAsync(device.Id).ConfigureAwait(true); }
        catch (Exception) { actions = Array.Empty<ActivityActionOption>(); }
        if (actions.Count == 0)
        {
            await DisplayAlertAsync(
                RemoteLabels.Text("Commande", "Command"),
                RemoteLabels.Text("Cet appareil n’expose aucune commande utilisable.", "This device exposes no usable commands."),
                "OK").ConfigureAwait(true);
            return;
        }

        var actionLabels = actions.Select((action, index) => $"{index + 1}. {action.Label} · {action.Id}").ToArray();
        var selectedActionLabel = await DisplayActionSheetAsync(
            RemoteLabels.Text("Choisir une commande", "Choose a command"),
            RemoteLabels.Text("Annuler", "Cancel"),
            null,
            actionLabels).ConfigureAwait(true);
        var actionIndex = Array.IndexOf(actionLabels, selectedActionLabel);
        if (actionIndex < 0) return;

        viewModel.AddCommandStep(device.Id, actions[actionIndex].Id);
        RenderDraftSteps();
    }

    private async Task AddDelayAsync()
    {
        var value = await DisplayPromptAsync(
            RemoteLabels.Text("Ajouter un délai", "Add delay"),
            RemoteLabels.Text("Durée en millisecondes (50 à 60000)", "Duration in milliseconds (50 to 60000)"),
            RemoteLabels.Text("Ajouter", "Add"),
            RemoteLabels.Text("Annuler", "Cancel"),
            initialValue: "750",
            maxLength: 5,
            keyboard: Keyboard.Numeric).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(value)) return;
        if (!int.TryParse(value, out var milliseconds)
            || milliseconds < DelayActivityStep.MinimumDuration.TotalMilliseconds
            || milliseconds > DelayActivityStep.MaximumDuration.TotalMilliseconds)
        {
            await DisplayAlertAsync(
                RemoteLabels.Text("Délai invalide", "Invalid delay"),
                RemoteLabels.Text("Saisissez une durée comprise entre 50 et 60000 ms.", "Enter a duration between 50 and 60000 ms."),
                "OK").ConfigureAwait(true);
            return;
        }

        viewModel.AddDelayStep(milliseconds);
        RenderDraftSteps();
    }

    private void RenderDraftSteps()
    {
        stepsHost.Children.Clear();
        if (viewModel.DraftSteps.Count == 0)
        {
            stepsHost.Children.Add(new Label
            {
                Text = RemoteLabels.Text("Aucune étape. Ajoutez une commande ou un délai.", "No steps. Add a command or delay."),
                FontSize = 12
            });
            return;
        }

        for (var index = 0; index < viewModel.DraftSteps.Count; index++)
        {
            var stepIndex = index;
            var step = viewModel.DraftSteps[index];
            var up = new Button { Text = "↑", MinimumWidthRequest = 44, MinimumHeightRequest = 44, IsEnabled = index > 0, AutomationId = $"activity-step-up-{index}" };
            var down = new Button { Text = "↓", MinimumWidthRequest = 44, MinimumHeightRequest = 44, IsEnabled = index < viewModel.DraftSteps.Count - 1, AutomationId = $"activity-step-down-{index}" };
            var remove = new Button { Text = "×", MinimumWidthRequest = 44, MinimumHeightRequest = 44, AutomationId = $"activity-step-remove-{index}" };
            up.Clicked += (_, _) => { viewModel.MoveStep(stepIndex, -1); RenderDraftSteps(); };
            down.Clicked += (_, _) => { viewModel.MoveStep(stepIndex, 1); RenderDraftSteps(); };
            remove.Clicked += (_, _) => { viewModel.RemoveStep(stepIndex); RenderDraftSteps(); };

            var description = step.Kind == ActivityStepInputKind.Delay
                ? RemoteLabels.Text($"Délai {step.DelayMilliseconds} ms", $"Delay {step.DelayMilliseconds} ms")
                : $"{viewModel.DeviceName(step.DeviceId)} — {ActivitiesViewModel.ActionLabel(step.ActionId)}";

            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                },
                ColumnSpacing = 10
            };
            var number = new Label { Text = $"{index + 1}.", FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center };
            var label = new Label { Text = description, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.WordWrap };
            var buttons = new HorizontalStackLayout { Spacing = 4, Children = { up, down, remove } };
            row.Children.Add(number);
            row.Children.Add(label);
            row.Children.Add(buttons);
            Grid.SetColumn(label, 1);
            Grid.SetColumn(buttons, 2);
            stepsHost.Children.Add(new Border { Padding = new Thickness(10, 6), StrokeThickness = 1, Content = row });
        }
    }

    private async Task SaveEditorAsync()
    {
        var name = nameEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            await DisplayAlertAsync(
                RemoteLabels.Text("Nom requis", "Name required"),
                RemoteLabels.Text("Donnez un nom à l’activité.", "Give the activity a name."),
                "OK").ConfigureAwait(true);
            return;
        }

        var roomId = (roomPicker.SelectedItem as RoomChoice)?.Id;
        SetInteractive(false);
        try
        {
            await viewModel.SaveEditingAsync(name, roomId).ConfigureAwait(true);
            editorBorder.IsVisible = false;
            RenderActivities();
        }
        catch (Exception)
        {
            await DisplayAlertAsync(
                RemoteLabels.Text("Enregistrement impossible", "Unable to save"),
                RemoteLabels.Text(
                    "Vérifiez que les appareils existent encore et que chaque commande est toujours supportée.",
                    "Check that the devices still exist and each command is still supported."),
                "OK").ConfigureAwait(true);
        }
        finally { SetInteractive(true); }
    }

    private async Task DeleteAsync(ActivitySummary activity, Button button)
    {
        var confirmed = await DisplayAlertAsync(
            RemoteLabels.Text("Supprimer l’activité", "Delete activity"),
            RemoteLabels.Text($"Supprimer « {activity.Name} » ?", $"Delete '{activity.Name}'?"),
            RemoteLabels.Text("Supprimer", "Delete"),
            RemoteLabels.Text("Annuler", "Cancel")).ConfigureAwait(true);
        if (!confirmed) return;

        button.IsEnabled = false;
        try
        {
            await viewModel.DeleteAsync(activity.Id).ConfigureAwait(true);
            RenderActivities();
            editorBorder.IsVisible = viewModel.IsEditing;
        }
        catch (Exception)
        {
            await DisplayAlertAsync(
                RemoteLabels.Text("Activité", "Activity"),
                RemoteLabels.Text("Impossible de supprimer cette activité.", "Unable to delete this activity."),
                "OK").ConfigureAwait(true);
        }
        finally { button.IsEnabled = true; }
    }

    private async Task RunAsync(ActivitySummary activity, Button button)
    {
        var policy = (policyPicker.SelectedItem as FailurePolicyChoice)?.Policy
            ?? ActivityFailurePolicy.StopOnFirstNonAccepted;
        reportHost.Children.Clear();
        SetInteractive(false);
        cancelRunButton.IsEnabled = true;
        try
        {
            var report = await viewModel.RunAsync(activity.Id, policy).ConfigureAwait(true);
            if (report is not null) RenderReport(report);
        }
        catch (Exception)
        {
            await DisplayAlertAsync(
                RemoteLabels.Text("Exécution impossible", "Unable to run"),
                RemoteLabels.Text("La macro a été arrêtée. Aucune commande incertaine n’est rejouée automatiquement.", "The macro was stopped. No uncertain command is automatically replayed."),
                "OK").ConfigureAwait(true);
        }
        finally
        {
            cancelRunButton.IsEnabled = true;
            SetInteractive(true);
            button.IsEnabled = true;
        }
    }

    private void RenderReport(ActivityRunReport report)
    {
        reportHost.Children.Clear();
        reportHost.Children.Add(new Label
        {
            Text = RemoteLabels.Text("Rapport d’exécution", "Run report"),
            FontSize = 20,
            FontAttributes = FontAttributes.Bold
        });
        reportHost.Children.Add(new Label
        {
            Text = $"{report.ActivityName} — {ActivitiesViewModel.StatusText(report.Status)}",
            FontSize = 13
        });

        foreach (var step in report.Steps)
        {
            var title = step.Delay is not null
                ? RemoteLabels.Text($"Étape {step.Position + 1} — délai {step.Delay.Value.TotalMilliseconds:0} ms", $"Step {step.Position + 1} — delay {step.Delay.Value.TotalMilliseconds:0} ms")
                : RemoteLabels.Text(
                    $"Étape {step.Position + 1} — {viewModel.DeviceName(step.DeviceId)} — {ActivitiesViewModel.ActionLabel(step.Action?.Id)}",
                    $"Step {step.Position + 1} — {viewModel.DeviceName(step.DeviceId)} — {ActivitiesViewModel.ActionLabel(step.Action?.Id)}");

            var detail = ActivitiesViewModel.StepStatusText(step.Status);
            if (step.Error is not null) detail += $" • {step.Error}";
            if (step.Delivery is not null) detail += $" • {step.Delivery}";
            if ((step.Status is ActivityStepRunStatus.Unknown or ActivityStepRunStatus.Cancelled) && step.Delivery == DeliveryState.Unknown)
                detail += RemoteLabels.Text(" • commande non rejouée", " • command not replayed");

            reportHost.Children.Add(new Border
            {
                Padding = new Thickness(12, 8),
                StrokeThickness = 1,
                Content = new VerticalStackLayout
                {
                    Spacing = 4,
                    Children =
                    {
                        new Label { Text = title, FontAttributes = FontAttributes.Bold },
                        new Label { Text = detail, FontSize = 12 }
                    }
                }
            });
        }
    }

    private void SetInteractive(bool enabled)
    {
        var effective = enabled && !viewModel.IsRunning;
        newButton.IsEnabled = effective;
        refreshButton.IsEnabled = effective;
        policyPicker.IsEnabled = effective;
        activitiesHost.IsEnabled = effective;
        editorBorder.IsEnabled = effective;
        cancelRunButton.IsEnabled = viewModel.IsRunning;
    }

    private sealed record RoomChoice(Guid? Id, string Label);
    private sealed record FailurePolicyChoice(ActivityFailurePolicy Policy, string Label);
}
