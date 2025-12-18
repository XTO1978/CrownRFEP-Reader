namespace CrownRFEP_Reader.Views.Controls;

public partial class TopTabsBar : ContentView
{
    public TopTabsBar()
    {
        InitializeComponent();
    }

    private static Task GoToRootAsync(string rootRoute)
    {
        // Navegación absoluta al root de cada sección.
        return Shell.Current.GoToAsync($"//{rootRoute}");
    }

    private async void OnDashboardClicked(object sender, EventArgs e) => await GoToRootAsync("dashboard");
    private async void OnSessionsClicked(object sender, EventArgs e) => await GoToRootAsync("sessions");
    private async void OnAthletesClicked(object sender, EventArgs e) => await GoToRootAsync("athletes");
    private async void OnStatsClicked(object sender, EventArgs e) => await GoToRootAsync("stats");
    private async void OnImportClicked(object sender, EventArgs e) => await GoToRootAsync("import");
}
