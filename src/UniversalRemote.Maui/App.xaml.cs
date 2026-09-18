using Microsoft.Extensions.DependencyInjection;

namespace UniversalRemote.Maui;

public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly IServiceProvider services;

    public App(IServiceProvider services)
    {
        StartupDiagnostics.Install();
        this.services = services ?? throw new ArgumentNullException(nameof(services));
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        try
        {
            return new Window(services.GetRequiredService<AppShell>());
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Record("App.CreateWindow", ex);

            // Keep the process alive and make startup failures visible instead of closing immediately.
            var errorPage = new ContentPage
            {
                Title = "UniversalRemote — démarrage",
                Content = new ScrollView
                {
                    Content = new VerticalStackLayout
                    {
                        Padding = new Thickness(24),
                        Spacing = 14,
                        Children =
                        {
                            new Label
                            {
                                Text = "UniversalRemote n’a pas pu ouvrir l’interface principale.",
                                FontSize = 22,
                                FontAttributes = FontAttributes.Bold
                            },
                            new Label
                            {
                                Text = "L’erreur a été enregistrée dans startup-diagnostics.log. Relancez depuis Visual Studio et transmettez la première exception si cet écran apparaît.",
                                FontSize = 14
                            },
#if DEBUG
                            new Label
                            {
                                Text = ex.ToString(),
                                FontSize = 11
                            }
#endif
                        }
                    }
                }
            };

            return new Window(errorPage);
        }
    }
}
