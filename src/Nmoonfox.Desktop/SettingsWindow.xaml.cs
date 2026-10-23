using System;
using System.ComponentModel;
using System.Windows;
using Nmoonfox.Core;

namespace Nmoonfox.Desktop;

public partial class SettingsWindow : Window
{
    private readonly DesktopController owner;
    private bool updating = true;
    private bool closing;
    public SettingsWindow(DesktopController owner)
    {
        this.owner = owner;
        InitializeComponent();
        var area = SystemParameters.WorkArea;
        MinHeight = Math.Min(MinHeight, Math.Max(200, area.Height - 20));
        MinWidth = Math.Min(MinWidth, Math.Max(240, area.Width - 20));
        Height = Math.Min(Height, Math.Max(200, area.Height - 20));
        Width = Math.Min(Width, Math.Max(240, area.Width - 20));
        Hero.Source = owner.Sprites.Frame(PetState.Idle, 0);
        owner.Changed += Refresh;
        Refresh();
    }
    private void Refresh()
    {
        if (!IsInitialized) return;
        updating = true;
        var settings = owner.World.Settings;
        CountSlider.Value = settings.Count; SizeSlider.Value = settings.Size; SpeedSlider.Value = settings.Speed;
        RoamCheck.IsChecked = settings.RoamEverywhere; TopCheck.IsChecked = settings.AlwaysOnTop; ThroughCheck.IsChecked = settings.ClickThrough;
        YieldCheck.IsChecked = settings.YieldToMouse;
        DisplaysCheck.IsChecked = settings.UseAllDisplays;
        DisplaysLabel.Text = $"{owner.World.Displays.Count} 画面を検出 · 合計 {settings.Count} 匹" +
            (settings.UseAllDisplays ? " · ドラッグで別の画面へ移動できます" : " · メイン画面のみ");
        CountLabel.Text = $"{settings.Count} 匹"; SizeLabel.Text = $"{settings.Size / 144:0.0} 倍";
        SpeedLabel.Text = settings.Speed < 45 ? "ゆっくり" : settings.Speed > 90 ? "元気に" : "のんびり";
        PopulationLabel.Text = owner.World.Paused ? "いまは、ひとやすみ中" : $"{settings.Count} 匹がいっしょに過ごしています";
        PauseButton.Content = owner.World.Paused ? "おさんぽ再開" : "ひとやすみ";
        StatusLabel.Text = owner.Status;
        updating = false;
    }
    private void SliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => Apply();
    private void OptionChanged(object sender, RoutedEventArgs e) => Apply();
    private void Apply()
    {
        if (updating || !IsInitialized) return;
        owner.ChangeSettings(owner.World.Settings with
        {
            Count = (int)CountSlider.Value, Size = SizeSlider.Value, Speed = SpeedSlider.Value,
            AlwaysOnTop = TopCheck.IsChecked == true, RoamEverywhere = RoamCheck.IsChecked == true,
            ClickThrough = ThroughCheck.IsChecked == true, YieldToMouse = YieldCheck.IsChecked == true,
            UseAllDisplays = DisplaysCheck.IsChecked == true
        });
    }
    private void PauseClicked(object sender, RoutedEventArgs e) => owner.TogglePause();
    private void SleepClicked(object sender, RoutedEventArgs e) { owner.World.SleepAll(); }
    private void WakeClicked(object sender, RoutedEventArgs e) => owner.World.WakeAll();
    private void GatherClicked(object sender, RoutedEventArgs e) => owner.GatherHere();
    private void SpreadClicked(object sender, RoutedEventArgs e) => owner.Spread();
    private void ExitClicked(object sender, RoutedEventArgs e) => owner.Exit();
    public void AllowClose() { closing = true; owner.Changed -= Refresh; Close(); }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!closing) { e.Cancel = true; owner.Save(); Hide(); }
        base.OnClosing(e);
    }
}
