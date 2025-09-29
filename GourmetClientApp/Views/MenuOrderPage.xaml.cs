using Microsoft.Maui.Controls;
using System.Collections.ObjectModel;
using GourmetClientApp.Model;

namespace GourmetClientApp.Views;

public partial class MenuOrderPage : ContentPage
{
    public ObservableCollection<GourmetMenu> MenuItems { get; set; } = new();

    public MenuOrderPage()
    {
        InitializeComponent();
        BindingContext = this;
        LoadSampleData();
    }

    private void LoadSampleData()
    {
        // Load some sample menu items to demonstrate the structure
        MenuItems.Add(new GourmetMenu(
            DateTime.Today,
            GourmetMenuCategory.Menu1,
            "M001",
            "Schnitzel Wiener Art",
            "Knuspriges Schnitzel mit Pommes und Salat",
            new char[] { 'A', 'C', 'G' },
            true));

        MenuItems.Add(new GourmetMenu(
            DateTime.Today,
            GourmetMenuCategory.Menu2,
            "V001", 
            "Vegetarische Pasta",
            "Pasta mit Gemüse und Kräutersauce",
            new char[] { 'A', 'C' },
            true));

        MenuItems.Add(new GourmetMenu(
            DateTime.Today,
            GourmetMenuCategory.SoupAndSalad,
            "S001",
            "Tomatensuppe",
            "Cremige Tomatensuppe mit Basilikum",
            new char[] { 'G' },
            true));

        MenuStatusLabel.Text = $"{MenuItems.Count} menu items available";
        MenuCollectionView.IsVisible = true;
    }

    private void OnRefreshMenuClicked(object sender, EventArgs e)
    {
        // In the full implementation, this would call the network service
        MenuStatusLabel.Text = "Refreshing menu...";
        
        // Simulate refresh delay
        Task.Delay(1000).ContinueWith(_ =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                MenuStatusLabel.Text = $"{MenuItems.Count} menu items refreshed";
            });
        });
    }
}