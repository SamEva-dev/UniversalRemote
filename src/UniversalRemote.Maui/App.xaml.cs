using Microsoft.Extensions.DependencyInjection;

namespace UniversalRemote.Maui
{
    public partial class App : Microsoft.Maui.Controls.Application
    {
        private readonly IServiceProvider services;

        public App(IServiceProvider services)
        {
            this.services = services;
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(services.GetRequiredService<AppShell>());
        }
    }
}
