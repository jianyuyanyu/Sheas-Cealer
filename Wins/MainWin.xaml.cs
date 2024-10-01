﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using IWshRuntimeLibrary;
using Microsoft.Win32;
using OnaCore;
using Sheas_Cealer.Consts;
using Sheas_Cealer.Preses;
using Sheas_Cealer.Utils;
using File = System.IO.File;

namespace Sheas_Cealer.Wins;

public partial class MainWin : Window
{
    private static MainPres? MainPres;
    private static readonly HttpClient MainClient = new();
    private static DispatcherTimer? HoldButtonTimer;
    private static readonly FileSystemWatcher HostWatcher = new(AppDomain.CurrentDomain.SetupInformation.ApplicationBase!, "Cealing-Host-*.json") { EnableRaisingEvents = true, NotifyFilter = NotifyFilters.LastWrite };
    private static readonly Dictionary<string, (string hostRulesFragments, string hostResolverRulesFragments)> CealArgsFragments = [];
    private static string CealArgs = string.Empty;

    internal MainWin(string[] args)
    {
        InitializeComponent();

        DataContext = MainPres = new(args);

        HostWatcher.Changed += HostWatcher_Changed;

        foreach (string hostPath in Directory.GetFiles(HostWatcher.Path, HostWatcher.Filter))
            HostWatcher_Changed(null!, new(new(), Path.GetDirectoryName(hostPath)!, Path.GetFileName(hostPath)));
    }

    protected override void OnSourceInitialized(EventArgs e) => IconRemover.RemoveIcon(this);
    private void MainWin_Loaded(object sender, RoutedEventArgs e) => SettingsBox.Focus();
    private void MainWin_Closing(object sender, CancelEventArgs e) => Application.Current.Shutdown();

    private void MainWin_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }
    private void MainWin_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
            MainPres!.BrowserPath = ((string[])e.Data.GetData(DataFormats.FileDrop))[0];
    }

    private void SettingsBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        switch (MainPres!.SettingsMode)
        {
            case MainConst.SettingsMode.BrowserPathMode:
                MainPres.BrowserPath = SettingsBox.Text;
                return;
            case MainConst.SettingsMode.UpstreamUrlMode:
                MainPres.UpstreamUrl = SettingsBox.Text;
                return;
            case MainConst.SettingsMode.ExtraArgsMode:
                MainPres.ExtraArgs = SettingsBox.Text;
                return;
        }
    }
    private void SettingsModeButton_Click(object sender, RoutedEventArgs e)
    {
        MainPres!.SettingsMode = MainPres.SettingsMode switch
        {
            MainConst.SettingsMode.BrowserPathMode => MainConst.SettingsMode.UpstreamUrlMode,
            MainConst.SettingsMode.UpstreamUrlMode => MainConst.SettingsMode.ExtraArgsMode,
            MainConst.SettingsMode.ExtraArgsMode => MainConst.SettingsMode.BrowserPathMode,
            _ => throw new UnreachableException()
        };
    }
    private void SettingsFunctionButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog browserPathDialog = new() { Filter = $"{MainConst._BrowserPathDialogFilterFileType} (*.exe)|*.exe" };

        switch (MainPres!.SettingsMode)
        {
            case MainConst.SettingsMode.BrowserPathMode when browserPathDialog.ShowDialog().GetValueOrDefault():
                SettingsBox.Focus();
                MainPres.BrowserPath = browserPathDialog.FileName;
                return;
            case MainConst.SettingsMode.UpstreamUrlMode:
                MainPres.UpstreamUrl = MainConst.DefaultUpstreamUrl;
                return;
            case MainConst.SettingsMode.ExtraArgsMode:
                MainPres.ExtraArgs = string.Empty;
                return;
        }
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (HoldButtonTimer!.IsEnabled)
            StartButtonHoldTimer_Tick(null, null!);
    }
    private void StartButton_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        HoldButtonTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        HoldButtonTimer.Tick += StartButtonHoldTimer_Tick;
        HoldButtonTimer.Start();
    }
    private void StartButtonHoldTimer_Tick(object? sender, EventArgs e)
    {
        HoldButtonTimer!.Stop();

        if (string.IsNullOrWhiteSpace(CealArgs))
            throw new Exception(MainConst._HostErrorHint);
        if (MessageBox.Show(MainConst._KillBrowserProcessPrompt, string.Empty, MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;

        IWshShortcut uncealedBrowserShortcut = (IWshShortcut)new WshShell().CreateShortcut(Path.Combine(AppDomain.CurrentDomain.SetupInformation.ApplicationBase!, "Uncealed-Browser.lnk"));
        uncealedBrowserShortcut.TargetPath = MainPres!.BrowserPath;
        uncealedBrowserShortcut.Description = "Created By Sheas Cealer";
        uncealedBrowserShortcut.Save();

        foreach (Process browserProcess in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(MainPres.BrowserPath)))
        {
            browserProcess.Kill();
            browserProcess.WaitForExit();
        }

        new CommandProc(sender == null).ShellRun(AppDomain.CurrentDomain.SetupInformation.ApplicationBase!, ($"{CealArgs} {MainPres!.ExtraArgs}").Trim());
    }

    private void EditHostButton_Click(object sender, RoutedEventArgs e)
    {
        Button? senderButton = sender as Button;

        string hostPath = Path.Combine(AppDomain.CurrentDomain.SetupInformation.ApplicationBase!, senderButton == EditLocalHostButton ? "Cealing-Host-Local.json" : "Cealing-Host-Upstream.json");

        if (!File.Exists(hostPath))
            File.Create(hostPath).Dispose();

        ProcessStartInfo processStartInfo = new(hostPath) { UseShellExecute = true };
        Process.Start(processStartInfo);
    }
    private async void UpdateUpstreamHostButton_Click(object sender, RoutedEventArgs e)
    {
        string upstreamHostUrl = (MainPres!.UpstreamUrl.StartsWith("http://") || MainPres!.UpstreamUrl.StartsWith("https://") ? string.Empty : "https://") + MainPres!.UpstreamUrl;
        string upstreamHostString = await Http.GetAsync<string>(upstreamHostUrl, MainClient);
        string localHostPath = Path.Combine(AppDomain.CurrentDomain.SetupInformation.ApplicationBase!, "Cealing-Host-Upstream.json");
        string localHostString;

        if (!File.Exists(localHostPath))
            File.Create(localHostPath).Dispose();

        using (StreamReader localHostStreamReader = new(localHostPath))
            localHostString = localHostStreamReader.ReadToEnd();

        if (localHostString.Replace("\r", string.Empty) == upstreamHostString)
            MessageBox.Show(MainConst._UpstreamHostUtdHint);
        else
        {
            MessageBoxResult overrideOptionResult = MessageBox.Show(MainConst._OverrideUpstreamHostPrompt, string.Empty, MessageBoxButton.YesNoCancel);
            if (overrideOptionResult == MessageBoxResult.Yes)
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.SetupInformation.ApplicationBase!, "Cealing-Host-Upstream.json"), upstreamHostString);
                MessageBox.Show(MainConst._UpdateUpstreamHostSuccessHint);
            }
            else if (overrideOptionResult == MessageBoxResult.No)
                Process.Start(new ProcessStartInfo(upstreamHostUrl) { UseShellExecute = true });
        }
    }
    private void ThemesButton_Click(object sender, RoutedEventArgs e) => MainPres!.IsLightTheme = MainPres.IsLightTheme.HasValue ? MainPres.IsLightTheme.Value ? null : true : false;
    private void AboutButton_Click(object sender, RoutedEventArgs e) => new AboutWin().ShowDialog();

    private void HostWatcher_Changed(object sender, FileSystemEventArgs e)
    {
        try
        {
            string hostRulesFragments = string.Empty;
            string hostResolverRulesFragments = string.Empty;
            string hostName = e.Name!.TrimStart("Cealing-Host-".ToCharArray()).TrimEnd(".json".ToCharArray());
            int ruleIndex = 0;

            using FileStream hostStream = new(e.FullPath, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            JsonDocumentOptions hostOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
            JsonElement hostArray = JsonDocument.Parse(hostStream, hostOptions).RootElement;

            foreach (JsonElement hostItem in hostArray.EnumerateArray())
            {
                string hostSni = string.IsNullOrWhiteSpace(hostItem[1].ToString()) ? $"{hostName}{ruleIndex}" : hostItem[1].ToString();
                string hostIp = string.IsNullOrWhiteSpace(hostItem[2].ToString()) ? "127.0.0.1" : hostItem[2].ToString();

                foreach (JsonElement hostDomain in hostItem[0].EnumerateArray())
                    hostRulesFragments += $"MAP {hostDomain} {hostSni},";

                hostResolverRulesFragments += $"MAP {hostSni} {hostIp},";

                ++ruleIndex;
            }

            CealArgsFragments[hostName] = (hostRulesFragments, hostResolverRulesFragments);

            string hostRules = string.Empty;
            string hostResolverRules = string.Empty;

            foreach ((string hostRulesFragments, string hostResolverRulesFragments) CealArgsFragment in CealArgsFragments.Values)
            {
                hostRules += CealArgsFragment.hostRulesFragments;
                hostResolverRules += CealArgsFragment.hostResolverRulesFragments;
            }

            CealArgs = @$"--host-rules=""{hostRules.TrimEnd(',')}"" --host-resolver-rules=""{hostResolverRules.TrimEnd(',')}"" --test-type --ignore-certificate-errors";
        }
        catch { CealArgs = string.Empty; }
    }
    private void MainWin_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyboardDevice.Modifiers == ModifierKeys.Control && e.Key == Key.W)
            Application.Current.Shutdown();
    }
}