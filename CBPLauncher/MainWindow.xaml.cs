﻿/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

// sometimes comments will refer to a "reference [program]" which refers to https://github.com/tom-weiland/csharp-game-launcher

using Microsoft.Win32;          /// this project was made with .NET framework 4.6.1 (at least as of near the start when I'm writing this comment)
using System;                   /// idk *how much* that changes things, but it does influence a few things like what you have to include here compared to using e.g. .NET core 5.0 apparently
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;    // System.IO.Compression.FileSystem added in project References instead (per stackexchange suggestion - I don't actually fully understand it ::fingerguns::)
using System.Net;           
using System.Windows;
using System.Windows.Forms;     // this thing makes message boxes messy, since now there's one from .Windows and one from .Windows.Forms @_@
using System.Windows.Media;     // used for selecting brushes (used for coloring in e.g. textboxes)
using Microsoft.VisualBasic;    // used for the current (temporary?) popup user text input for manual path; I doubt it's efficient but it doesn't seem to be *too* resource intensive pending a replacement

namespace CBPLauncher
{

    enum LauncherStatus
    {
        readyCBPEnabled,
        readyCBPDisabled,
        loadFailed,
        unloadFailed,
        installFailed,
        installingFirstTimeLocal,
        installingUpdateLocal,
        installingFirstTimeOnline,
        installingUpdateOnline,
        connectionProblemLoaded,
        connectionProblemUnloaded,
        installProblem
    }

    public partial class MainWindow : Window
    {
        private string rootPath;
        private string gameZip;
        private string gameExe;
        private string localMods;
        private string RoNPathFinal = Properties.Settings.Default.RoNPathSetting; // is it possible there's a narrow af edge case where the path ends up wrong after a launcher version upgrade?
        private string RoNPathCheck;
        private string workshopPath;
        private string unloadedModsPath;

        /// ===== START OF MOD LIST =====

        //Community Balance Patch
        private string modnameCBP;
        private string workshopIDCBP;
        private string workshopPathCBP;
        private string localPathCBP;
        private string versionFileCBP;
        private string archiveCBP;

        /// ===== END OF MOD LIST =====

        private LauncherStatus _status;
        internal LauncherStatus Status
        {
            get => _status;
            set
            {
                _status = value;
                switch (_status)
                {
                    case LauncherStatus.readyCBPEnabled:
                        StatusReadout.Text = "Ready: CBP enabled";
                        StatusReadout.Foreground = Brushes.LimeGreen;
                        PlayButton.Content = "Launch Game";
                        PlayButton.IsEnabled = true;
                        break;
                    case LauncherStatus.readyCBPDisabled:
                        StatusReadout.Text = "Ready: CBP disabled";
                        StatusReadout.Foreground = Brushes.Orange;
                        PlayButton.Content = "Launch Game";
                        PlayButton.IsEnabled = true;
                        break;
                    case LauncherStatus.loadFailed:
                        StatusReadout.Text = "Error: unable to load CBP";
                        StatusReadout.Foreground = Brushes.Red;
                        PlayButton.Content = "Retry Unload?";
                        PlayButton.IsEnabled = true;
                        break;
                    case LauncherStatus.unloadFailed:
                        StatusReadout.Text = "Error: unable to unload CBP";
                        StatusReadout.Foreground = Brushes.Red;
                        PlayButton.Content = "Retry Unload?";
                        PlayButton.IsEnabled = true;
                        break;
                    case LauncherStatus.installFailed:
                        StatusReadout.Text = "Error: update failed";
                        StatusReadout.Foreground = Brushes.Red;
                        PlayButton.Content = "Retry Update?";
                        PlayButton.IsEnabled = true;
                        break;
                    // I tried renaming the *Local to *Workshop and VS2019 literally did the opposite of that (by renaming what I just changed) instead of doing what it said it would wtf
                    case LauncherStatus.installingFirstTimeLocal:                       /// primary method: use workshop files;
                        StatusReadout.Text = "Installing patch from local files...";    /// means no local-mods CBP detected
                        StatusReadout.Foreground = Brushes.White;
                        PlayButton.IsEnabled = false;
                        break;
                    case LauncherStatus.installingUpdateLocal:                          /// primary method: use workshop files;
                        StatusReadout.Text = "Installing update from local files...";   /// local-mods CBP detected, but out of date compared to workshop version.txt
                        StatusReadout.Foreground = Brushes.White;
                        PlayButton.IsEnabled = false;
                        break;
                    case LauncherStatus.installingFirstTimeOnline:                      /// backup method: use online files;
                        StatusReadout.Text = "Installing patch from online files...";   /// means no local-mods CBP detected but can't find workshop files either
                        StatusReadout.Foreground = Brushes.White;
                        PlayButton.IsEnabled = false;
                        break;
                    case LauncherStatus.installingUpdateOnline:                         /// backup method: use online files; 
                        StatusReadout.Text = "Installing update from online files...";  /// local-mods CBP detected, but can't find workshop files and
                        StatusReadout.Foreground = Brushes.White;                       /// local files out of date compared to online version.txt
                        PlayButton.IsEnabled = false;
                        break;
                    case LauncherStatus.connectionProblemLoaded:
                        StatusReadout.Text = "Error: connectivity issue (CBP loaded)";
                        StatusReadout.Foreground = Brushes.OrangeRed;
                        PlayButton.Content = "Launch game";
                        PlayButton.IsEnabled = true;
                        break;
                    case LauncherStatus.connectionProblemUnloaded:
                        StatusReadout.Text = "Error: connectivity issue (CBP not loaded)";
                        StatusReadout.Foreground = Brushes.OrangeRed;
                        PlayButton.Content = "Launch game";
                        PlayButton.IsEnabled = true;
                        break;
                    case LauncherStatus.installProblem:
                        StatusReadout.Text = "Potential installation error";
                        StatusReadout.Foreground = Brushes.OrangeRed;
                        PlayButton.Content = "Launch game";
                        PlayButton.IsEnabled = true;
                        break;
                    default:
                        break;
                }
            }
        }

        public MainWindow()
        {
            try
            {
                InitializeComponent();

                if (Properties.Settings.Default.UpgradeRequired == true)
                {
                    UpgradeSettings();
                    SaveSettings();
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error during initialization: {ex}");
                Environment.Exit(0); // for now, if a core part of the program fails then it needs to close to prevent broken but user-accessible functionality
            }

            RegistryKey regPath; //this part (and related below) is to find the install location for RoN:EE (Steam)
 //!!!!!!           // apparently this is not a good method for the registry part? use using instead? (but I don't know how to make that work with the bit-check :( ) https://stackoverflow.com/questions/1675864/read-a-registry-key
            if (Environment.Is64BitOperatingSystem) //I don't *fully* understand what's going on here (ported from stackexchange), but this block seems to be needed to prevent null value return due to 32/64 bit differences???
            {
                regPath = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            }
            else
            {
                regPath = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            }

            regPathDebug.Text = "Debug: registry read as " + regPath;

            // create / find paths for RoN, Steam Workshop, and relevant mods
            try
            {
                // core paths
                rootPath = Directory.GetCurrentDirectory();
                gameZip = Path.Combine(rootPath, "Community Balance Patch.zip"); //static file name even with updates, otherwise you have to change this value!

                if (Properties.Settings.Default.RoNPathSetting == "no path")
                {
                    // debug: System.Windows.MessageBox.Show($"path" + RoNPathFinal);

                    using (RegistryKey ronReg = regPath.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 287450"))
                    {
                        if (ronReg != null) // some RoN:EE installs (for some UNGODLY REASON WHICH I DON'T UNDERSTAND) don't have their location in the registry, so we have to work around that
                        {
                            // success: automated primary
                            RoNPathCheck = regPath.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 287450").GetValue("InstallLocation").ToString();
                            RoNPathFound();
                        }

                        else
                        {
                            // try a default 64-bit install path, since that should probably work for most of the users with cursed registries
                            RoNPathCheck = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + @"\Steam\steamapps\common\Rise of Nations";

                            if (File.Exists(Path.Combine(RoNPathCheck, "riseofnations.exe")))
                            {
                                // success: automated secondary 1
                                RoNPathFound();
                            }
                            else
                            {
                                // old way of doing it, but used as as backup because I don't know if the environment call method ever fails or not
                                RoNPathCheck = @"C:\Program Files (x86)\Steam\steamapps\common\Rise of Nations";

                                if (File.Exists(Path.Combine(RoNPathCheck, "riseofnations.exe")))
                                {
                                    // success: automated secondary 2
                                    RoNPathFound();
                                }

                                // automated methods unable to locate RoN install path - ask user for path
                                else
                                {
                                    //people hate gotos (less so in C# but still) but this seems like a very reasonable substitute for a while-not-true loop that I haven't figured out how to implement here
                                    AskManualPath:

                                    RoNPathCheck = Interaction.InputBox($"Please provide the file path to the folder where Rise of Nations: Extended Edition is installed."
                                                                       + "\n\n" + @"e.g. D:\Steamgames\common\Rise of Nations", "Unable to detect RoN install");

                                    // check that the user has input a seemingly valid location
                                    if (File.Exists(Path.Combine(RoNPathCheck, "riseofnations.exe")))
                                    {
                                        // success: manual path
                                        RoNPathFound();
                                    }
                                    else
                                    {
                                        // tell user invalid path, ask if they want to try again
                                        DialogResult dialogResult = System.Windows.Forms.MessageBox.Show($"Rise of Nations install not detected in that location. "
                                                                                                        + "The path needs to be the folder that riseofnations.exe is located in, not including the executable itself."
                                                                                                        + "\n\n Would you like to try entering a path again?", "Invalid Path", MessageBoxButtons.YesNo);
                                        if (dialogResult == System.Windows.Forms.DialogResult.Yes)
                                        {
                                            goto AskManualPath;
                                        }
                                        else if (dialogResult == System.Windows.Forms.DialogResult.No)
                                        {
                                            System.Windows.MessageBox.Show($"Launcher will now close.");
                                            Environment.Exit(0);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                else
                //frequent usage probably doesn't need this popup
                //{
                //    RoNPathFinal = Properties.Settings.Default.RoNPathSetting;
                //}

                gameExe = Path.Combine(RoNPathFinal, "riseofnations.exe"); //in EE v1.20 this is the main game exe, with patriots.exe as the launcher (in T&P main game was rise.exe)
                localMods = Path.Combine(RoNPathFinal, "mods");
                workshopPath = Path.GetFullPath(Path.Combine(RoNPathFinal, @"..\..", @"workshop\content\287450")); //maybe not the best method, but serviceable? Path.GetFullPath used to make final path more human-readable

                /// ===== START OF MOD LIST =====

                // Community Balance Patch
                modnameCBP = "Community Balance Patch"; // this has to be static, which loses the benefit of having the version display in in-game mod manager, but acceptable because it will display in CBP Launcher instead
                workshopIDCBP = "2287791153"; // by separating the mod ID, more mods can be supported in the future and it can become a local/direct mods mod manager (direct needs more work still though)

                workshopPathCBP = Path.Combine(Path.GetFullPath(workshopPath), workshopIDCBP); /// getfullpath ensures the slash is included between the two
                localPathCBP = Path.Combine(Path.GetFullPath(localMods), modnameCBP);          /// I tried @"\" and "\\" and both made the first part (localMods) get ignored in the combined path
                versionFileCBP = Path.Combine(localPathCBP, "Version.txt"); // moved here in order to move with the data files (useful), and better structure to support other mods in future

                /// TODO
                /// use File.Exists and/or Directory.Exists to confirm that CBP files have actually downloaded from Workshop
                /// (at the moment it just assumes they exist and eventually errors later on if they don't)

                // Example New Mod
                // modname<MOD> = A
                // workshopID<MOD> = B

                // workshopPath<MOD> = X
                // localPath<MOD> = Y // remember to declare (?) these new strings at the top ("private string = ") as well
                // versionFile<MOD> = Path.Combine(localPath<MOD>, "Version.txt"

                /// Ideally in future these values can be dynamically loaded/added/edited etc from
                /// within the program's UI (using e.g. external files), meaning that these values no longer need to be hardcoded like this.
                /// I guess it might involve a Steam Workshop api call/request(?) for a string the user inputs (e.g. "Community Balance Patch")
                /// and then it comes back with the ID and description so that the user can decide if that's the one they want.
                /// If it is, then its details are populated into new strings within CBP Launcher.
                /// No idea how to actually do that yet though :HnZdead:
                /// And even if I did, I suspect it might be more work than it's worth - very few people seem to care enough to put in the effort on this
                /// so it's probably not viable for *me* to put in the effort to make the program go from an 8/10 to a 10/10 - 
                /// it sure would be nice to avoid half-duplicating these here though.
                /// 
                /// Also this list should probably be moved to a different file if implemented so it doesn't clog this thing up once it supports more mods

                /// ===== END OF MOD LIST =====
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error creating paths: {ex}");
                Environment.Exit(0); // for now, if a core part of the program fails then it needs to close to prevent broken but user-accessible functionality
            }

            // show detected paths in the UI
            try
            {
                EEPath.Text = RoNPathFinal;
                workshopPathDebug.Text = workshopPath;
                workshopPathCBPDebug.Text = workshopPathCBP;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error displaying paths in UI {ex}");
                Environment.Exit(0); // for now, if a core part of the program fails then it needs to close to prevent broken but user-accessible functionality
            }

            // create directories
            try
            {
                Directory.CreateDirectory(Path.Combine(localMods, "Unloaded Mods")); // will be used to unload CBP
                unloadedModsPath = Path.Combine(localMods, "Unloaded Mods");

                Directory.CreateDirectory(Path.Combine(unloadedModsPath, "CBP Archive")); // will be used for archiving old CBP versions
                archiveCBP = Path.Combine(unloadedModsPath, "CBP Archive");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error creating directories {ex}");
                Environment.Exit(0); // for now, if a core part of the program fails then it needs to close to prevent broken but user-accessible functionality
            }

            CBPDefaultChecker();

        }

        private void CheckForUpdates()
        {
            try // without the try you can accidentally create online-only DRM whoops
            {
                VersionTextLatest.Text = "Checking latest version...";
                VersionTextInstalled.Text = "Checking installed version...";

                WebClient webClient = new WebClient();                                                               /// Moved this section from reference to here in order to display
                Version onlineVersion = new Version(webClient.DownloadString("http://mhloppy.com/CBP/version.txt")); /// latest available version as well as installed version

                VersionTextLatest.Text = "Latest CBP version: "
                     + VersionArray.versionStart[onlineVersion.major]
                     + VersionArray.versionMiddle[onlineVersion.minor]  ///space between major and minor moved to the string arrays in order to support the eventual 1.x release(s)
                     + VersionArray.versionEnd[onlineVersion.subMinor]  ///it's nice to have a little bit of forward thinking in the mess of code sometimes ::fingerguns::
                     + VersionArray.versionHotfix[onlineVersion.hotfix];

                if (File.Exists(versionFileCBP)) //If there's already a version.txt in the local-mods CBP folder, then...
                {
                    Version localVersion = new Version(File.ReadAllText(versionFileCBP)); // this doesn't use UpdateLocalVersionNumber() because of the compare done below it - will break if replaced without modification

                    VersionTextInstalled.Text = "Installed CBP version: "
                                            + VersionArray.versionStart[localVersion.major]
                                            + VersionArray.versionMiddle[localVersion.minor]
                                            + VersionArray.versionEnd[localVersion.subMinor]
                                            + VersionArray.versionHotfix[localVersion.hotfix];
                    try
                    {
                        if (onlineVersion.IsDifferentThan(localVersion))
                        {
                            InstallGameFiles(true, onlineVersion);
                        }
                        else
                        {
                            Status = LauncherStatus.readyCBPEnabled; //if the local version.txt matches the version found in the online file, then no patch required
                            Properties.Settings.Default.CBPLoaded = true;
                            Properties.Settings.Default.CBPUnloaded = false;
                            SaveSettings();
                        }
                    }
                    catch (Exception ex)
                    {
                        Status = LauncherStatus.installFailed;
                        System.Windows.MessageBox.Show($"Error installing patch files: {ex}");
                    }
                }

                // compatibility with a6c (maybe making it compatible was a mistake)
                else if (Directory.Exists(Path.Combine(localMods, "Community Balance Patch (Alpha 6c)")))
                {
                    InstallGameFiles(true, Version.zero);
                }

                else
                {
                    InstallGameFiles(false, Version.zero);
                }
            }
            catch (Exception ex)
            {
                if (Properties.Settings.Default.CBPLoaded == true)
                {
                    Status = LauncherStatus.connectionProblemLoaded;
                }
                else
                {
                    Status = LauncherStatus.connectionProblemUnloaded;
                }

                UpdateLocalVersionNumber();
                VersionTextLatest.Text = "Unable to check latest version";
                System.Windows.MessageBox.Show($"Error checking for updates. Maybe no connection could be established? {ex}");
            }
        }

        private void InstallGameFiles(bool _isUpdate, Version _onlineVersion)
        {
            if (Properties.Settings.Default.CBPUnloaded == false)
            {
                if (Properties.Settings.Default.NoWorkshopFiles == false)
                {
                    // try using workshop files
                    try
                    {
                        // extra steps depending on whether this an update to existing install or first time install
                        if (_isUpdate)
                        {
                            Status = LauncherStatus.installingUpdateLocal;

                            // if archive setting is enabled, archive the old version; it looks for an unversioned CBP folder and has a separate check for a6c specifically
                            if (Properties.Settings.Default.CBPArchive == true)
                            {
                                // standard (non-a6c) archiving
                                if (Directory.Exists(Path.Combine(localPathCBP)))
                                {
                                    ArchiveNormal();
                                }
                                
                                // compatibility with archiving a6c
                                if(Directory.Exists(Path.Combine(localMods, "Community Balance Patch (Alpha 6c)")))
                                {
                                    ArchiveA6c();
                                }

                                else
                                {
                                    System.Windows.MessageBox.Show($"Archive setting is on, but there doesn't seem to be any compatible CBP install to archive.");
                                }
                            }
                        }
                        else
                        {
                            Status = LauncherStatus.installingFirstTimeLocal;
                        }

                        // perhaps this is a chance to use async, but the benefits are minor given the limited IO, and my half-hour attempt wasn't adequate to get it working
                        DirectoryCopy(Path.Combine(workshopPathCBP, "Community Balance Patch"), Path.Combine(localPathCBP), true);

                        try
                        {
                            UpdateLocalVersionNumber();

                            Properties.Settings.Default.CBPLoaded = true;
                            Properties.Settings.Default.CBPUnloaded = false;
                            SaveSettings();

                            Status = LauncherStatus.readyCBPEnabled;
                        }
                        catch (Exception ex)
                        {
                            Status = LauncherStatus.loadFailed;
                            System.Windows.MessageBox.Show($"Error loading CBP: {ex}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Status = LauncherStatus.installFailed;
                        System.Windows.MessageBox.Show($"Error installing CBP from Workshop files: {ex}");
                    }
                }

                else if (Properties.Settings.Default.NoWorkshopFiles == true) // as of v0.3 release this option isn't even exposed to the user yet, but it'll be useful later
                {
                    // try using online files
                    try
                    {
                        WebClient webClient = new WebClient();
                        if (_isUpdate)
                        {
                            Status = LauncherStatus.installingUpdateOnline;
                        }
                        else
                        {
                            Status = LauncherStatus.installingFirstTimeOnline;
                            _onlineVersion = new Version(webClient.DownloadString("http://mhloppy.com/CBP/version.txt")); /// maybe this should be ported to e.g. google drive as well? then again it's a 1KB file so I
                                                                                                                          /// guess the main concern would be server downtime (either temporary or long term server-taken-offline-forever)
                        }

                        webClient.DownloadFileCompleted += new AsyncCompletedEventHandler(DownloadGameCompletedCallback);
                        webClient.DownloadFileAsync(new Uri("https://drive.google.com/uc?export=download&id=1hQYZtdsTDihFi33Cc_BisRUHdXvSy5o4"), gameZip, _onlineVersion); //a6c old one https://drive.google.com/uc?export=download&id=1usd0ihBy5HWxsD6UiabV3ohzGxB7SxDD
                    }
                    catch (Exception ex)
                    {
                        Status = LauncherStatus.installFailed;
                        System.Windows.MessageBox.Show($"Error retrieving patch files: {ex}");
                    }
                }

            }

            if (Properties.Settings.Default.CBPUnloaded == true)
            {
                try
                {
                    Directory.Move(Path.Combine(unloadedModsPath, "Community Balance Patch"), Path.Combine(localPathCBP)); //this will still currently fail if the folder already exists though

                    UpdateLocalVersionNumber();

                    Properties.Settings.Default.CBPLoaded = true;
                    Properties.Settings.Default.CBPUnloaded = false;
                    SaveSettings();

                    Status = LauncherStatus.readyCBPEnabled;
                }
                catch (Exception ex)
                {
                    Status = LauncherStatus.loadFailed;
                    System.Windows.MessageBox.Show($"Error loading CBP: {ex}");
                }
            }
        }

        private void DownloadGameCompletedCallback(object sender, AsyncCompletedEventArgs e)
        {
            try
            {
                var onlineVersion = ((Version)e.UserState); //I literally don't know when to use var vs other stuff, but it works here so I guess it's fine???

                /// To make the online version be converted (not just the local version), need the near-duplicate code below (on this indent level).
                /// Vs reference, it separates the conversion to string until after displaying the version number,
                /// that way it displays e.g. "Alpha 6c" but actually writes e.g. "6.0.3" to version.txt so that future compares to that file will work
                string onlineVersionString = ((Version)e.UserState).ToString();

                try
                {
                    ZipFile.ExtractToDirectory(gameZip, localMods);
                    File.Delete(gameZip); //extra file to local mods folder, then delete it after the extraction is done
                }
                catch (Exception ex)
                {
                    Status = LauncherStatus.installFailed;
                    File.Delete(gameZip); //without this, the .zip will remain if it successfully downloads but then errors while unzipping

                    // show a message asking user if they want to ignore the error (and unlock the launch button)
                    string message = $"If you've already installed CBP this error might be okay to ignore. It may occur if you have the CBP files but no version.txt file to read from, causing the launcher to incorrectly think CBP is not installed. "
                                     + "It's also *probably* okay to ignore this if you want to just play non-CBP for now."
                                     + Environment.NewLine + Environment.NewLine + "Full error: " + Environment.NewLine + $"{ex}"
                                     + Environment.NewLine + Environment.NewLine + "Ignore error and continue?";
                    string caption = "Error installing new patch files";
                    MessageBoxButtons buttons = MessageBoxButtons.YesNo;
                    DialogResult result;

                    result = System.Windows.Forms.MessageBox.Show(message, caption, buttons);

                    // if they say yes, then also ask if they want to write a new version.txt file where the mod is supposed to be installed:
                    if (result == System.Windows.Forms.DialogResult.Yes)
                    {
                        Status = LauncherStatus.installProblem;

                        string message2 = $"If you're very confident that CBP is actually installed and the problem is just the version.txt file, you can write a new file to resolve this issue."
                                          + Environment.NewLine + Environment.NewLine + "Would you like to write a new version.txt file?"
                                          + Environment.NewLine + "(AVOID DOING THIS IF YOU'RE NOT SURE!!)";
                        string caption2 = "Write new version.txt file?";
                        MessageBoxButtons buttons2 = MessageBoxButtons.YesNo;
                        DialogResult result2;

                        result2 = System.Windows.Forms.MessageBox.Show(message2, caption2, buttons2);
                        if (result2 == System.Windows.Forms.DialogResult.Yes)
                        {
                            // currently do nothing; explicitly preferred over using if-not-yes-then-return in case I change this later
                        }
                        else
                        {
                            return;
                        }
                    }
                    else
                    {
                        Environment.Exit(0); /// if they say no, then application is kill;
                    }                        /// Env.Exit used instead of App.Exit because it prevents more code from running
                }                            /// App.Exit was writing the new version file even if you said no on the prompt - maybe could be resolved, but this is okay I think

                File.WriteAllText(versionFileCBP, onlineVersionString); // I thought this is where return would go, but it doesn't, so I evidently don't know what I'm doing

                UpdateLocalVersionNumber();

                Status = LauncherStatus.readyCBPEnabled;
                Properties.Settings.Default.CBPLoaded = true;
                SaveSettings();
            }
            catch (Exception ex)
            {
                Status = LauncherStatus.installFailed;
                File.Delete(gameZip); //without this, the .zip will remain if it successfully downloads but then errors while unzipping
                System.Windows.MessageBox.Show($"Error installing new patch files: {ex}"); 
            }
        }

        private void UnloadCBP()
        {
            if (Properties.Settings.Default.CBPUnloaded == false)
            {
                try
                {
                    System.IO.Directory.Move(localPathCBP, Path.Combine(unloadedModsPath, "Community Balance Patch"));
                    Properties.Settings.Default.CBPUnloaded = true;
                    Properties.Settings.Default.CBPLoaded = false;
                    SaveSettings();

                    VersionTextInstalled.Text = "Installed CBP version: not loaded";

                    Status = LauncherStatus.readyCBPDisabled;
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error unloading mod: {ex}");
                    Status = LauncherStatus.unloadFailed;
                }

                if (Properties.Settings.Default.UnloadWorkshopToo == true)
                {
                    try
                    {
                        //unload workshop
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"Error unloading Workshop mod: {ex}");
                        Status = LauncherStatus.unloadFailed;
                    }
                }

            }
            else
            {
                System.Windows.MessageBox.Show($"CBP is already unloaded.");
            }
        }

        private void UpdateLocalVersionNumber()
        {
            if (File.Exists(versionFileCBP))
            {
                Version localVersion = new Version(File.ReadAllText(versionFileCBP)); // moved to separate thing to reduce code duplication

                VersionTextInstalled.Text = "Installed CBP version: "
                                        + VersionArray.versionStart[localVersion.major]
                                        + VersionArray.versionMiddle[localVersion.minor]  ///space between major and minor moved to the string arrays in order to support the eventual 1.x release(s)
                                    + VersionArray.versionEnd[localVersion.subMinor]  ///it's nice to have a little bit of forward thinking in the mess of code sometimes ::fingerguns::
                                    + VersionArray.versionHotfix[localVersion.hotfix];
            }
            else
            {
                VersionTextInstalled.Text = "CBP not installed";
            }
        }

        private void Window_ContentRendered(object sender, EventArgs e)
        {
            // allow user to switch between CBP and unmodded, and if unmodded then CBP updating logic unneeded
            if (Properties.Settings.Default.DefaultCBP == true)
            {
                CheckForUpdates();
            };
            if (Properties.Settings.Default.DefaultCBP == false)
            {
                if (Properties.Settings.Default.CBPUnloaded == false && Properties.Settings.Default.CBPLoaded == true)
                {
                    UnloadCBP();
                }
                else
                {
                    Status = LauncherStatus.readyCBPDisabled;
                }
            }
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(gameExe) && Status == LauncherStatus.readyCBPEnabled || Status == LauncherStatus.readyCBPDisabled) // make sure all "launch" button options are included here
            {
                ProcessStartInfo startInfo = new ProcessStartInfo(gameExe) // if you do this wrong (I don't fully remember what "wrong" was) the game can launch weirdly e.g. errors, bad mod loads etc.
                {
                    WorkingDirectory = RoNPathFinal //this change compared to reference app was suggested by VS itself - I'm assuming it's functionally equivalent at worst
                };
                Process.Start(startInfo);
                //DEBUG: Process.Start(gameExe);

                Close();
            }
            else if (Status == LauncherStatus.installFailed)
            {
                CheckForUpdates(); // because CheckForUpdates currently includes the logic for *all* installing/loading, it's used for both installFailed and loadFailed right now
            }
            else if (Status == LauncherStatus.loadFailed)
            {
                CheckForUpdates();
            }
            else if (Status == LauncherStatus.unloadFailed)
            {
                UnloadCBP();
            }
        }

        private void ManualLoadCBP_Click(object sender, RoutedEventArgs e)
        {
            CheckForUpdates();
        }

        private void ManualUnloadCBP_Click(object sender, RoutedEventArgs e)
        {
            UnloadCBP();
        }
        private void ResetSettings_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.Reset();

            System.Windows.MessageBox.Show($"Settings reset. Default settings will be loaded the next time the program is loaded.");
        }

        private void CBPDefaultCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.DefaultCBP = true;

            SaveSettings();

            //debug: System.Windows.MessageBox.Show($"New value: {Properties.Settings.Default.DefaultCBP}"); //p.s. this will *also* be activated if the program loads with check enabled
        }

        private void CBPDefaultCheckbox_UnChecked(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.DefaultCBP = false;

            SaveSettings();

            //debug: System.Windows.MessageBox.Show($"new value: {Properties.Settings.Default.DefaultCBP}");
        }
        private void SaveSettings()
        {
            Properties.Settings.Default.Save();
        }

        private void UpgradeSettings()
        {
            Properties.Settings.Default.Upgrade();
            Properties.Settings.Default.UpgradeRequired = false;
        }

        private void CBPDefaultChecker()
        {
            if (Properties.Settings.Default.DefaultCBP == true)
            { 
                CBPDefaultCheckbox.IsChecked = true; 
            }
            else if (Properties.Settings.Default.DefaultCBP == false)
            {
                CBPDefaultCheckbox.IsChecked = false;
            }
        }

        private void RoNPathFound()
        {
            if (RoNPathFinal == $"no path")
            {
                System.Windows.MessageBox.Show($"Rise of Nations detected in " + RoNPathCheck);
            }
            RoNPathFinal = RoNPathCheck;

            Properties.Settings.Default.RoNPathSetting = RoNPathFinal;
            SaveSettings();
        }

        private void ArchiveNormal()
        {
            try
            {
                //rename it after moving it, then check version and use that to rename the folder in the archived location
                Directory.Move(Path.Combine(localPathCBP), Path.Combine(archiveCBP, "Community Balance Patch"));

                Version archiveVersion = new Version(File.ReadAllText(Path.Combine(archiveCBP, "Community Balance Patch", "version.txt")));

                string archiveVersionNew = VersionArray.versionStart[archiveVersion.major]
                                         + VersionArray.versionMiddle[archiveVersion.minor]
                                         + VersionArray.versionEnd[archiveVersion.subMinor]
                                         + VersionArray.versionHotfix[archiveVersion.hotfix];

                Directory.Move(Path.Combine(archiveCBP, "Community Balance Patch"), Path.Combine(archiveCBP, "Community Balance Patch " + "(" + archiveVersionNew + ")"));
                System.Windows.MessageBox.Show(archiveVersionNew + " has been archived.");
            }
            catch (Exception ex)
            {
                Status = LauncherStatus.loadFailed;
                System.Windows.MessageBox.Show($"Error archiving previous CBP version: {ex}");
            }
        }

        // can't use same version check because it uses a 3-digit identifier, not 4-digit, but since we know its name it's not too bad
        private void ArchiveA6c()
        {
            try
            {
                //rename it after moving it
                Directory.Move(Path.Combine(localMods, "Community Balance Patch (Alpha 6c)"), Path.Combine(archiveCBP, "Community Balance Patch (Alpha 6c)"));
                System.Windows.MessageBox.Show("Alpha 6c has been archived.");
            }
            catch (Exception ex)
            {
                Status = LauncherStatus.loadFailed;
                System.Windows.MessageBox.Show($"Error archiving previous CBP version (compatbility for a6c): {ex}");
            }
        }

        // MS reference method of dir copying
        private static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs)
        {
            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new DirectoryInfo(sourceDirName);

            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException(
                    "Source folder does not exist or could not be found: "
                    + sourceDirName);
            }

            DirectoryInfo[] dirs = dir.GetDirectories();

            // If the destination directory doesn't exist, create it.       
            Directory.CreateDirectory(destDirName);

            // Get the files in the directory and copy them to the new location.
            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string tempPath = Path.Combine(destDirName, file.Name);
                file.CopyTo(tempPath, false);
            }

            // If copying subdirectories, copy them and their contents to new location.
            if (copySubDirs)
            {
                foreach (DirectoryInfo subdir in dirs)
                {
                    string tempPath = Path.Combine(destDirName, subdir.Name);
                    DirectoryCopy(subdir.FullName, tempPath, copySubDirs);
                }
            }
        }
    }

    struct Version 
    {
        internal static Version zero = new Version(0, 0, 0, 0);

        // introduced a fourth tier of version numbering as well, since the naming convention doesn't work very well for subminor being used for the purpose of a hotfix
        public short major;    ///in reference these are private, but I want to refer to them in the version displayed to the user (which I'm converting to X.Y.Z numerical to e.g. "Alpha 6c")
        public short minor;    ///I feel obliged to point out that I have little/no frame of reference to know if this is "bad" to do so maybe this is a code sin and I'm just too naive to know
        public short subMinor;
        public short hotfix;

        internal Version(short _major, short _minor, short _subMinor, short _hotFix)
        {
            major = _major;
            minor = _minor;
            subMinor = _subMinor;
            hotfix = _hotFix;
        }

        internal Version(string _version)
        {
            string[] _versionStrings = _version.Split('.'); //version.txt uses an X.Y.Z version pattern e.g. 6.0.3, so break that up  on the "." to parse each value
            if (_versionStrings.Length !=4)
            {
                major = 0;
                minor = 0;
                subMinor = 0;
                hotfix = 0;
                return; //if the version detected doesn't seem to follow the format expected, set detected version to 0.0.0
            }

            major = short.Parse(_versionStrings[0]);
            minor = short.Parse(_versionStrings[1]);
            subMinor = short.Parse(_versionStrings[2]);
            hotfix = short.Parse(_versionStrings[3]);
        }

        internal bool IsDifferentThan(Version _otherVersion) //check if version (from local version.txt file) matches online with online version.txt
        {
            if (major != _otherVersion.major)
            {
                return true;
            }
            else
            {
                if (minor != _otherVersion.minor)
                {
                    return true;
                }
                else
                {
                    if (subMinor != _otherVersion.subMinor) //presumably there's a more efficient / elegant way to lay this out e.g. run one check thrice, cycling major->minor->subminor->hotfix
                    {
                        return true;
                    }
                    else
                    {
                        if (hotfix != _otherVersion.hotfix)
                        {
                            return true;
                        }
                    }
                }
            }
            return false; //detecting if they're different, so false = not different
        }

        public override string ToString()
        { 
            return $"{major}.{minor}.{subMinor}.{hotfix}"; //because this is used for comparison, you can't put the conversion into e.g. "Alpha 6c" here or it will fail the version check above because of the format change
        }
    }

    public class VersionArray //this seems like an inelegant way to implement the string array? but I wasn't sure where else to put it (and have it work)
    {
        //cheeky bit of extra changes to convert the numerical/int based X.Y.Z into the versioning I already used before this launcher
        public static string[] versionStart = new string[11] { "not installed", "Pre-Alpha ", "Alpha ", "Beta ", "Release Candidate ", "1.", "2.", "3.", "4.", "5.", "6." }; // I am a fucking god figuring out how to properly use these arrays based on 10 fragments of 5% knowledge each
        public static string[] versionMiddle = new string[16] { "", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14", "15" }; // I don't even know what "static" means in this context, I just know what I need to use it
        public static string[] versionEnd = new string[17] { "", "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m", "n", "o", "p" }; //e.g. can optionally just skip the subminor by intentionally using [0]
        public static string[] versionHotfix = new string[10] { "", " (hotfix 1)", " (hotfix 2)", " (hotfix 3)", " (hotfix 4)", " (hotfix 5)", " (hotfix 6)", " (hotfix 7)", " (hotfix 8)", " (hotfix 9)"}; //e.g. can optionally just skip the hotfix by intentionally using [0]
    }
}
