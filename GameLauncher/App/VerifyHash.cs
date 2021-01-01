﻿using GameLauncherReborn;
using DiscordRPC;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using GameLauncher.App.Classes;
using GameLauncher.App.Classes.Logger;
using GameLauncher.HashPassword;
using GameLauncher.Resources;

namespace GameLauncher.App
{
    public partial class VerifyHash : Form
    {
        private readonly IniFile _settingFile = new IniFile("Settings.ini");
        private readonly RichPresence _presence = new RichPresence();

        //VerifyHash
        string[][] scannedHashes;
        public int filesToScan;
        public int badFiles;
        public int totalFilesScanned;
        public int redownloadedCount;
        public List<string> InvalidFileList = new List<string>();
        public List<string> ValidFileList = new List<string>();
        //Log launcherLog = new Log("launcher.log");

        public VerifyHash()
        {
            InitializeComponent();
            ApplyEmbeddedFonts();
        }

        private void VerifyHash_Load(object sender, EventArgs e)
        {
            VersionLabel.Text = "Version: v" + Application.ProductVersion;
            Log.Core("VerifyHash Opened");
            /* Clean up previous logs and start logging */
            string[] filestocheck = new string[] { "validfiles.dat", "invalidfiles.dat", "Verify.log" };
            foreach (String file in filestocheck)
            {
                if (File.Exists(file)) File.Delete(file);
            }
            LogVerify.StartVerifyLogging();
        }

        public void GameScanner(bool startScan)
        {
            Thread StartScan;
            StartScan = new Thread(new ThreadStart(StartGameScanner))
            {
                Name = "FileScanner"
            };

            if (startScan == true)
            {
                //StatusText.Text = "Validating files on background.".ToUpper();
                //Threaded CheckFiles
                StartScan.Start();
                Log.Debug("Started Scanner");
            }
            else if (startScan == false)
            {
                StartScan.Abort();
                //StatusText.Text = "Unkown Status.".ToUpper();
                /*
                // This terminating this way is truncating logging
                Process[] allOfThem = Process.GetProcessesByName("VerifyHash");
                foreach (var oneProcess in allOfThem)
                {
                    Process.GetProcessById(oneProcess.Id).Kill();
                }
                Process.GetProcessById(Process.GetCurrentProcess().Id).Kill();
                */
                //Log.Debug("Stopped Scanner");
            }
        }

        private void StartGameScanner()
        {
            _presence.Details = "In-Launcher: " + Application.ProductVersion;
            _presence.State = "Validating Game Files!";
            _presence.Assets = new Assets
            {
                LargeImageText = "SBRW",
                LargeImageKey = "nfsw"
            };
            if (MainScreen.discordRpcClient != null) MainScreen.discordRpcClient.SetPresence(_presence);

            try
            {
                /* Fetch and Read Remote checksums.dat */
                if (File.Exists("checksums.dat")) File.Delete("checksums.dat");
                String[] getFilesToCheck = new WebClient().DownloadString("http://localhost/checksums.dat").Split('\n');
                File.WriteAllLines("checksums.dat", getFilesToCheck);
                /* Read Local checksums.dat */
                //String[] getFilesToCheck = File.ReadAllLines("checksums.dat");
                scannedHashes = new string[getFilesToCheck.Length][];
                for (var i = 0; i < getFilesToCheck.Length; i++)
                {
                    scannedHashes[i] = getFilesToCheck[i].Split(' ');
                }
                filesToScan = scannedHashes.Length;
                totalFilesScanned = 0;

                foreach (string[] file in scannedHashes)
                {
                    String FileHash = file[0].Trim();
                    String FileName = file[1].Trim();
                    String RealPathToFile = _settingFile.Read("InstallationDirectory") + FileName;

                    if (!File.Exists(RealPathToFile))
                    {
                        InvalidFileList.Add(FileName);
                        LogVerify.Missing("File: " + FileName);
                    }
                    else
                    {
                        if (FileHash != SHA.HashFile(RealPathToFile).Trim())
                        {
                            InvalidFileList.Add(FileName);
                            LogVerify.Invalid("File: " + FileName);
                        }
                        else
                        {
                            //ValidFileList.Add(RealPathToFile);
                            //File.WriteAllLines("validfiles.dat", ValidFileList);
                            LogVerify.Valid("File: " + FileName);
                        }
                    }
                    totalFilesScanned++;
                    ScanProgressText.Text = "Scanning Files: " + (totalFilesScanned * 100 / getFilesToCheck.Length) + "%";
                    ScanProgressBar.Value = totalFilesScanned * 100 / getFilesToCheck.Length;
                }

                Log.Info("Scan Completed"); // This isn't logging

                if (InvalidFileList != null)
                {
                    ScanProgressText.Text = "Found Invalid Files";
                    File.WriteAllLines("invalidfiles.dat", InvalidFileList);
                    Log.Info("Found Invalid Files and Will Start File Downloader");
                    CorruptedFilesFound();
                }
                else
                {
                    GameScanner(false);
                    StartScanner.Visible = true;
                    StopScanner.Visible = false;
                    ScanProgressText.Text = "Scan Complete. No Missing Files Where Found";
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message);
            }
            
        }

        private void CorruptedFilesFound()
        {
            redownloadedCount = 0;

            if (File.Exists("invalidfiles.dat") && File.ReadAllLines("invalidfiles.dat") != null)
            {
                InvalidProgressText.Text = "RE-DOWNLOADING INVALID FILES";
                string[] files = File.ReadAllLines("invalidfiles.dat");

                foreach (string text in files)
                {
                    try 
                    {
                        string text2 = _settingFile.Read("InstallationDirectory") + text;
                        string address = "http://mtntr.pl/unpacked" + text.Replace("\\", "/");
                        if (File.Exists(text2))
                        {
                            LogVerify.Deleted("File: " + text2);
                            File.Delete(text2);
                        }
                        new WebClient().DownloadFile(address, text2);
                        LogVerify.Downloaded("File: " + text2);
                        redownloadedCount++;
                        Application.DoEvents();
                    }
                    catch { }
                    InvalidProgressText.Text = "Redownloading Files: " + (redownloadedCount * 100 / files.Length) + "%";
                    InvalidProgressBar.Value = redownloadedCount / files.Length;
                }
                InvalidProgressText.Text = redownloadedCount + " Invalid Files Were Redownloaded";
                GameScanner(false);
                StartScanner.Visible = true;
                StopScanner.Visible = false;
            }
            else
            {
                InvalidProgressText.Text = "All Files Validated";
                GameScanner(false);
                StartScanner.Visible = true;
                StopScanner.Visible = false;
            }
        }

        private void StartScanner_Click(object sender, EventArgs e)
        {
            GameScanner(true);
            StartScanner.Visible = false;
            StopScanner.Visible = true;
        }

        private void StopScanner_Click(object sender, EventArgs e)
        {
            GameScanner(false);
            StartScanner.Visible = true;
            StopScanner.Visible = false;
        }

        private void ApplyEmbeddedFonts() 
        {
            FontFamily DejaVuSans = FontWrapper.Instance.GetFontFamily("DejaVuSans.ttf");
            FontFamily DejaVuSansBold = FontWrapper.Instance.GetFontFamily("DejaVuSans-Bold.ttf");
            ScanProgressText.Font = new Font(DejaVuSansBold, 9f, FontStyle.Bold);
            InvalidProgressText.Font = new Font(DejaVuSansBold, 9f, FontStyle.Bold);
            StartScanner.Font = new Font(DejaVuSansBold, 9f, FontStyle.Bold);
            StopScanner.Font = new Font(DejaVuSansBold, 9f, FontStyle.Bold);
            VersionLabel.Font = new Font(DejaVuSans, 9f, FontStyle.Regular);
        }

    }
}
