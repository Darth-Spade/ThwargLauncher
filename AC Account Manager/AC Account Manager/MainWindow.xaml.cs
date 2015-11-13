﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
//using System.Windows.Shapes;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using WindowPlacementUtil;

namespace AC_Account_Manager
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        //private Dictionary<string, List<AccountCharacter>> _allAccountCharacters;
        
        private List<string> _Images = new List<string>();
        private System.Random _rand = new Random();
        private BackgroundWorker _worker = new BackgroundWorker();
        private string _launcherLocation;

        public static string UsersFilePath = Path.Combine(Configuration.AppFolder, "UserNames.txt");
        private MainWindowViewModel _viewModel = new MainWindowViewModel();


        public MainWindow()
        {
            InitializeComponent();
            DataContext = _viewModel;
            _viewModel.PropertyChanged += _viewModel_PropertyChanged;

            MigrateSettingsIfNeeded();
            EnsureDataFoldersExist();

            LoadUserAccounts(initialLoad: true);
            LoadImages();
            ChangeBackgroundImageRandomly();

            WireUpBackgroundWorker();

            if (Properties.Settings.Default.ACLocation != "")
            {
                txtLauncherLocation.Text = Properties.Settings.Default.ACLocation;
            }
        }
        private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
        {
            this.Show();
            if (Properties.Settings.Default.ShowHelpAtStart)
            {
                DisplayHelpWindow();
            }
        }

        void _viewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // TODO - this is a workaround
            if (e.PropertyName == "KnownUserAccounts")
            {
                this.lstUsername.ItemsSource = null;
                this.lstUsername.ItemsSource = _viewModel.KnownUserAccounts;
            }
        }
        private void MigrateSettingsIfNeeded()
        {
            if (Properties.Settings.Default.NeedsUpgrade)
            {
                Properties.Settings.Default.Upgrade();
                Properties.Settings.Default.NeedsUpgrade = false;
                Properties.Settings.Default.Save();
            }
        }
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            LoadWindowSettings();
        }
        private void LoadWindowSettings()
        {
            this.SetPlacement(Properties.Settings.Default.MainWindowPlacement);
        }
        private void SaveWindowSettings()
        {
            Properties.Settings.Default.MainWindowPlacement = this.GetPlacement();
            Properties.Settings.Default.Save();
        }
        private void LoadImages()
        {
            _Images.Clear();
            _Images.Add("acwallpaperwideaerbax.jpg");
            _Images.Add("acwallpaperwideaerfalle.jpg");
            _Images.Add("acwallpaperwideGroup.jpg");
        }

        private string PickRandomImage()
        {
            return _Images[_rand.Next(_Images.Count)];
        }

        private void ChangeBackgroundImageRandomly()
        {
            LoadImages();
            string imageName = PickRandomImage();
            //BigGrid.Background = MyTextBlock.Background;
            ImageBrush brush = new ImageBrush(
                new BitmapImage(
                    new Uri("Images\\" + imageName, UriKind.Relative)
                    ));
            ContentGrid.Background = brush;
            
        }

        private void EnsureDataFoldersExist()
        {
            // Ensure program data folder exists
            string specificFolder = Configuration.AppFolder;
            if (!Directory.Exists(specificFolder))
            {
                Directory.CreateDirectory(specificFolder);
            }

            // Ensure characters file exists
            if (!File.Exists(UsersFilePath))
            {
                var fileStream = File.Create(UsersFilePath);
                fileStream.Close();
            }
            // Ensure profiles folder exists
            var mgr = new ProfileManager();
            mgr.EnsureProfileFolderExists();
        }

        private void LoadUserAccounts(bool initialLoad = false)
        {
            if (!initialLoad) // we do not save the first time, because have never yet loaded
            {
                SaveCurrentProfile();
            }
            ReloadKnownAccountsAndCharacters();
            try
            {
                _viewModel.LoadMostRecentProfile();
            }
            catch
            {
                ShowErrorMessage("Error loading profile");
            }
        }
        private void SaveCurrentProfile()
        {
            _viewModel.SaveCurrentProfile();
        }
        private void ReloadKnownAccountsAndCharacters()
        {
            var characterMgr = MagFilter.CharacterManager.ReadCharacters();
            _viewModel.Reset();
            using (var reader = new StreamReader(UsersFilePath))
            {
                while (!reader.EndOfStream)
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) { continue; }
                        string[] arr = line.Split(',');
                        string accountName = arr[0];
                        string password = arr[1];

                        var user = new UserAccount(accountName, characterMgr)
                        {
                            Password = password
                        };
                        _viewModel.KnownUserAccounts.Add(user);
                    }
                }
            }
            this.lstUsername.ItemsSource = null;
            this.lstUsername.ItemsSource = _viewModel.KnownUserAccounts;
        }
        private void btnLaunch_Click(object sender, RoutedEventArgs e)
        {
            _launcherLocation = txtLauncherLocation.Text;
            if (string.IsNullOrEmpty(_launcherLocation))
            {
                ShowErrorMessage("Game launcher location required");
                return;
            }
            if (!File.Exists(_launcherLocation))
            {
                ShowErrorMessage(string.Format("Game launcher missing: '{0}'", _launcherLocation));
                return;
            }
            if (!CheckAccountsAndPasswords())
            {
                return;
            }
            LaunchAllClientsOnAllServersOnThread();
        }
        private bool CheckAccountsAndPasswords()
        {
            int count = 0;
            foreach (var account in _viewModel.KnownUserAccounts)
            {
                if (account.AccountLaunchable)
                {
                    foreach (var server in account.Servers)
                    {
                        if (server.ServerSelected)
                        {
                            ++count;
                            if (string.IsNullOrWhiteSpace(account.Name))
                            {
                                ShowErrorMessage("Blank account not allowed");
                                return false;
                            }
                            if (string.IsNullOrWhiteSpace(account.Password))
                            {
                                ShowErrorMessage("Black password not allowed");
                                return false;
                            }
                        }
                    }
                }
            }
            if (count == 0)
            {
                ShowErrorMessage("No accounts and servers selected");
                return false;
            }
            return true;
        }

        private void WireUpBackgroundWorker()
        {
            _worker.WorkerReportsProgress = true;
            _worker.WorkerSupportsCancellation = true;
            _worker.DoWork += _worker_DoWork;
            _worker.ProgressChanged += _worker_ProgressChanged;
            _worker.RunWorkerCompleted += _worker_RunWorkerCompleted;
        }

        private void _worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            try
            {
                if (e.Cancelled)
                {
                    lblWorkerProgress.Content = "User Cancelled";
                }
                else if (e.Error != null)
                {
                    lblWorkerProgress.Content = string.Format("Error: {0}", e.Error.Message);
                }
                else
                {
                    lblWorkerProgress.Content = "Launcher Complete";
                }
            }
            finally
            {
                EnableInterface(true);
            }
        }
        private class WorkerArgs
        {
            public LaunchSorter.LaunchList LaunchList;
        }
        private void LaunchAllClientsOnAllServersOnThread()
        {
            if (_worker.IsBusy)
            {
                lblWorkerProgress.Content = "Launcher In Use";
            }
            else
            {
                EnableInterface(false);
                var launchMgr = new LaunchSorter();
                LaunchSorter.LaunchList launchList = launchMgr.GetLaunchList(_viewModel.KnownUserAccounts);
                _viewModel.RecordProfileLaunch();
                WorkerArgs args = new WorkerArgs()
                    {
                        LaunchList = launchList
                    };
                _worker.RunWorkerAsync(args);
            }
        }
        private void EnableInterface(bool enable)
        {
            btnLaunch.IsEnabled = enable;
        }

        private class ProgressInfo { public int Index; public int Total; public string Message; }
        void _worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            ProgressInfo info = (e.UserState as ProgressInfo);
            lblWorkerProgress.Content = string.Format(
                "{0}% - {1}/{2} - {3}",
                e.ProgressPercentage,
                info.Index, info.Total,
                info.Message
                );
        }

        void workerReportProgress(string verb, LaunchSorter.LaunchItem launchItem, int index, int total)
        {
            int pct = (int)(100.0 * index / total);
            string context = string.Format(
                "{0} {1}:{2}",
                verb, launchItem.AccountName, launchItem.ServerName);
            var progressInfo = new ProgressInfo()
                {
                    Index = index,
                    Total = total,
                    Message = context
                };
            _worker.ReportProgress(pct, progressInfo);
        }
        void _worker_DoWork(object sender, DoWorkEventArgs e)
        {
            WorkerArgs args = (e.Argument as WorkerArgs);
            if (args == null) { return; }
            int serverIndex = 0;
            int serverTotal = args.LaunchList.GetLaunchItemCount();
            Dictionary<string, DateTime> accountLaunchTimes = new Dictionary<string, DateTime>();

            foreach (var launchItem in args.LaunchList.GetLaunchList())
            {
                if (_worker.CancellationPending)
                {
                    e.Cancel = true;
                    break;
                }
                DateTime lastLaunch = (accountLaunchTimes.ContainsKey(launchItem.AccountName)
                                           ? accountLaunchTimes[launchItem.AccountName]
                                           : DateTime.MinValue);
                TimeSpan delay = new TimeSpan(0, 5, 0) - (DateTime.Now - lastLaunch);
                while (delay.TotalMilliseconds > 0)
                {
                    string context = string.Format("Waiting {0} sec", (int)delay.TotalSeconds+1);
                    workerReportProgress(context, launchItem, serverIndex, serverTotal);

                    System.Threading.Thread.Sleep(1000);
                    delay = new TimeSpan(0, 5, 0) - (DateTime.Now - lastLaunch);
                }

                ;
                workerReportProgress("Launching", launchItem, serverIndex, serverTotal);
                accountLaunchTimes[launchItem.AccountName] = DateTime.Now;

                var launcher = new GameLauncher();
                try
                {
                    var finder = new WindowFinder();
                    finder.RecordExistingWindows();
                    bool okgo = launcher.LaunchGameClient(
                        _launcherLocation,
                        launchItem.ServerName,
                        accountName: launchItem.AccountName,
                        password: launchItem.Password,
                        desiredCharacter: launchItem.CharacterSelected
                        );
                    if (!okgo)
                    {
                        break;
                    }
                    string gameCaptionPattern = ConfigSettings.GetConfigString("GameCaptionPattern", null);
                    if (gameCaptionPattern != null)
                    {
                        System.Text.RegularExpressions.Regex regex = new System.Text.RegularExpressions.Regex(gameCaptionPattern);
                        IntPtr hwnd = finder.FindNewWindow(regex);
                        if (hwnd != IntPtr.Zero)
                        {
                            string newGameTitle = GetNewGameTitle(launchItem);
                            if (!string.IsNullOrEmpty(newGameTitle))
                            {
                                finder.SetWindowTitle(hwnd, newGameTitle);
                            }
                        }
                    }
                }
                catch (Exception exc)
                {
                    ShowErrorMessage("Exception launching game launcher: " + exc.Message);
                    break;
                }

                ++serverIndex;
                workerReportProgress("Launched", launchItem, serverIndex, serverTotal);
            }
        }
        private string GetNewGameTitle(LaunchSorter.LaunchItem launchItem)
        {
            string pattern = ConfigSettings.GetConfigString("NewGameTitle", "");
            pattern = pattern.Replace("%ACCOUNT%", launchItem.AccountName);
            pattern = pattern.Replace("%SERVER%", launchItem.ServerName);
            pattern = pattern.Replace("%CHARACTER%", launchItem.CharacterSelected);
            return pattern;
        }

        private void ShowErrorMessage(string msg)
        {
            ShowMessage(msg, "Caption", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private void ShowMessage(string msg)
        {
            ShowMessage(msg, "Caption", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private void ShowMessage(string msg, string caption, MessageBoxButton button, MessageBoxImage image)
        {
            Application.Current.Dispatcher.Invoke(() =>
                MessageBox.Show(msg, caption, button, image));
        }

        private void OpenLauncherLocation()
        {
            // Create OpenFileDialog
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.InitialDirectory = "C:\\Turbine\\Asheron's Call";

            // Set filter for file extension and default file extension
            dlg.DefaultExt = ".exe";
            dlg.Filter = "Executables (exe)|*.exe|All files (*.*)|*.*";

            // Display OpenFileDialog by calling ShowDialog method
            Nullable<bool> result = dlg.ShowDialog();

            // Get the selected file name and display in a TextBox
            if (result == true)
            {
                // Open document
                string filename = dlg.FileName;
                txtLauncherLocation.Text = filename;
            }
        }

        private void txtLauncherLocation_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            OpenLauncherLocation();
        }

        private void btnLauncherLocation_Click(object sender, RoutedEventArgs e)
        {
            OpenLauncherLocation();
        }

        private void AC_Account_Manager_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveWindowSettings();
            SaveCurrentProfile();

            Properties.Settings.Default.SelectedUser = lstUsername.SelectedIndex;
            Properties.Settings.Default.ACLocation = txtLauncherLocation.Text;
            
            Properties.Settings.Default.Save();
        }

        private void btnAddUsers_Click(object sender, RoutedEventArgs e)
        {
            MainWindowDisable();
            var dlg = new AddUsers();
            dlg.ShowDialog();
            LoadUserAccounts();
            MainWindowEnable();
        }
        private void btnChooseProfile_Click(object sender, RoutedEventArgs e)
        {
            MainWindowDisable();
            var dlg = new ChooseProfile();
            dlg.ShowDialog();
            MainWindowEnable();
        }
        private void MainWindowDisable()
        {
            rctBlack.Fill = new SolidColorBrush(Colors.Gainsboro);
        }
        private void MainWindowEnable()
        {
            rctBlack.Fill = new SolidColorBrush(Colors.Black);
        }
        private void btnOpenUsers_Click(object sender, RoutedEventArgs e)
        {
            MainWindowDisable();

            var startInfo = new ProcessStartInfo("notepad", UsersFilePath);
            var notepadProcess = new Process() {StartInfo = startInfo};
            if (notepadProcess.Start())
            {
                notepadProcess.WaitForExit();
            }
            LoadUserAccounts();
            MainWindowEnable();
        }

        private void btnHelp_Click(object sender, RoutedEventArgs e)
        {
            DisplayHelpWindow();
        }
        private void DisplayHelpWindow()
        {
            MainWindowDisable();
            var dlg = new Help();
            dlg.ShowDialog();
            MainWindowEnable();
        }

    }
}
