﻿using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Diagnostics;           // Needed for working with process...
using System.IO;
using HelperClasses;                // Needed for making symbolic links,...
using BackupNS;
using HelperClasses.Logger;            // Static logging class

namespace PicasaStarter
{
    static class Program
    {
        public static Backup _backup = null;
        public static bool BackupComplete = false;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            #region Initialization
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string appSettingsDir = "";  //Directory containing PicasaStarter settings
            string configurationDir = "";
            Configuration config;
            Settings settings;
            bool ConfigFileExists = true;
            bool settingsfound = false;

            configurationDir = SettingsHelper.DetermineConfigDir();
            appSettingsDir = SettingsHelper.DetermineSettingsDir(configurationDir);
            try
            {
                settings = new Settings();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Picasa 3.9 Has not been Initialized for this user.\n" +
                    "Please run Picasa3 from the start menu to set up the registry.\n " +
                      "Error Msg:  " + ex.Message);
                return;

            }
            config = new Configuration();
            bool showGUI = true;


            try
            {
                config = SettingsHelper.DeSerializeConfig(
                    configurationDir + "\\" + SettingsHelper.ConfigFileName);
            }
            catch (Exception)
            {
                //No config file, set config & settings defaults and signal first time run
                ConfigFileExists = false;
                config.picasaStarterSettingsXMLPath = "";
                settings.picasaDBs.Add(SettingsHelper.GetDefaultPicasaDB());
            }
            // load settings...

            if (ConfigFileExists)
            {
                bool cancelSettingsFileSearch = false;
                string currentDir = appSettingsDir;
                currentDir = Path.GetDirectoryName(currentDir);


                while (!settingsfound && cancelSettingsFileSearch == false)
                {
                    if (!File.Exists(appSettingsDir + "\\" + SettingsHelper.SettingsFileName))
                    {
                        // Take care of case where the settings file is not available but it is referenced in the config file (The settings drive/dir is missing).
                        // Initializes the variables to pass to the MessageBox.Show method.
                        string message = "The Picasa Starter settings file was not found in:\n" + appSettingsDir + "\n\n If it is on a NAS or Portable Drive, " +
                            "\nPlease Connect the drive as the correct drive letter.\n" +
                            "When the Drive is connected, Push YES to Try Again.\n\n" +
                            "To define a new Settings File location, Push NO,\n" +
                            "Then Correct the Settings File location in the First Run dialog \n\n" +
                             "To Exit PicasaStarter Without Trying Again, Push CANCEL.";
                        string caption = "Missing Settings File";

                        // Displays the MessageBox.
                        DialogResult result = MessageBox.Show(message, caption, MessageBoxButtons.YesNoCancel);

                        if (result == DialogResult.Yes)
                        {
                            settingsfound = false;
                        }
                        if (result == DialogResult.Cancel)
                        {
                            cancelSettingsFileSearch = true;
                            ConfigFileExists = false;
                            showGUI = false;
                        }
                        else if (result == DialogResult.No)
                        {
                            ConfigFileExists = false;
                            cancelSettingsFileSearch = true;
                            settings.picasaDBs.Add(SettingsHelper.GetDefaultPicasaDB());
                        }
                    }
                    else
                        settingsfound = true;
                }

                // Try to read the settings file...

                if (settingsfound == true)
                {
                    try
                    {
                        settings = SettingsHelper.DeSerializeSettings(
                            appSettingsDir + "\\" + SettingsHelper.SettingsFileName);
                        settingsfound = true;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error reading settings file: " + ex.Message);
                        settings.picasaDBs.Add(SettingsHelper.GetDefaultPicasaDB());
                        settingsfound = false;
                    }
                }
            }
            #endregion

            #region Process Command Line Arguments
            if (ConfigFileExists)
            {
               // Save settings
                //---------------------------------------------------------------------------
                try
                {
                    SettingsHelper.SerializeSettings(settings,
                            appSettingsDir + "\\" + SettingsHelper.SettingsFileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error saving settings: " + ex.Message);
                }

                // Process command line arguments...
                //---------------------------------------------------------------------------
                string autoRunDatabaseName = null;
                string backupDatabaseName = null;

                for (int i = 1; i < Environment.GetCommandLineArgs().Length; i++)
                {
                    string arg = Environment.GetCommandLineArgs()[i];

                    // Check if Picasastarter should autorun Picasa with a specified database name...
                    if (arg.Equals("/autorun", StringComparison.CurrentCultureIgnoreCase))
                    {
                        showGUI = false;

                        // The next argument should be the database name...
                        i++;
                        if (i < Environment.GetCommandLineArgs().Length)
                        {
                            autoRunDatabaseName = Environment.GetCommandLineArgs()[i];
                            autoRunDatabaseName = autoRunDatabaseName.Trim(new char[] { '"', ' ' });
                        }
                        else
                        {
                            MessageBox.Show("The /autorun directive should be followed by an existing Picasa database name, or \"Personal\" or \"AskUser\"", "No Database Name");
                        }
                    }
                    else if (arg.Equals("/backup", StringComparison.CurrentCultureIgnoreCase))
                    {
                        showGUI = false;

                        // The next argument should be the database name...
                        i++;
                        if (i < Environment.GetCommandLineArgs().Length)
                        {
                            backupDatabaseName = Environment.GetCommandLineArgs()[i];
                            backupDatabaseName = backupDatabaseName.Trim(new char[] { '"', ' ' });
                        }
                        else
                        {
                            MessageBox.Show("The /backup directive should be followed by an existing Picasa database name, or \"Personal\" or \"AskUser\"", "No Database Name");
                        }
                    }
                    else
                    {
                        MessageBox.Show("Invalid or no command line parameter: " + arg);
                    }
                }

                // If /autorun argument was passed...
                //---------------------------------------------------------------------------
                if (autoRunDatabaseName != null)
                {
                    PicasaDB foundDB = null;
                    if (IOHelper.IsProcessOpen("Picasa3"))
                    {
                        MessageBox.Show("Picasa 3 is presently running on this computer." +
                        "\n\nPlease Exit Picasa before trying to\nrun it from PicasaStarter", "Picasa Already Running");
                        return;
                    }

                    // First check if he wants to be asked which database to run
                    if (autoRunDatabaseName.Equals("AskUser", StringComparison.CurrentCultureIgnoreCase))
                    {
                        // Show Database selection menu 
                        SelectDBForm selectDBForm = new SelectDBForm(settings, false);
                        selectDBForm.ShowDialog();

                        if (selectDBForm.ReturnDBName != null)
                        {
                            autoRunDatabaseName = selectDBForm.ReturnDBName;
                        }
                        else
                            return;

                    }

                    // Next check if he wants to run with the standard personal database...
                    if (autoRunDatabaseName.Equals("personal", StringComparison.CurrentCultureIgnoreCase))
                    {
                        autoRunDatabaseName = settings.picasaDBs[0].Name;
                    }
                    foreach (PicasaDB db in settings.picasaDBs)
                    {
                        if (db.Name.Equals(autoRunDatabaseName, StringComparison.CurrentCultureIgnoreCase))
                            foundDB = db;
                    }

                    if (foundDB != null)
                    {
                        bool xxx = IOHelper.MapVirtualDrive(foundDB, appSettingsDir);
 
                        PicasaRunner runner = new PicasaRunner(settings.PicasaExePath);
                        String dbPath;
                        string destButtonDir;

                        // If the user wants to run his personal default database... 
                        if (foundDB.IsStandardDB == true)
                        {
                            dbPath = null;
                            // Set the directory to put the PicasaButtons in the PicasaDB...
                            destButtonDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) +
                                    "\\Google\\Picasa2\\buttons";
                        }

                        // If the user wants to run a custom database...
                        else
                        {
                            // Set the choosen BaseDir
                            if (!Directory.Exists(foundDB.BaseDir + "\\Google\\Picasa2") &&
                                Directory.Exists(foundDB.BaseDir + "\\Local Settings\\Application Data\\Google\\Picasa2"))
                            {

                                DialogResult result = MessageBox.Show("Do you want to temporarily use the Picasa version 3.8 database?\n" +
                                    "This Picasa 3.8 Database path is:\n " + foundDB.BaseDir + "\\Local Settings\\Application Data" +
                                    "\n\n Please edit the database settings, and convert the database to version 3.9 to stop receiving this warning message",
                                        "Database Not Converted for Picasa Version 3.9+", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1,
                                        (MessageBoxOptions)0x40000);
                                if (result == DialogResult.Yes)
                                {
                                    foundDB.BaseDir = foundDB.BaseDir + "\\Local Settings\\Application Data";
                                }
                            }
                            // Get out without creating a database if the database directory doesn't exist
                            if (!Directory.Exists(foundDB.BaseDir + "\\Google\\Picasa2"))
                            {
                                MessageBox.Show("The database doesn't exist at this location, please choose an existing database or create one.",
                                            "Database doesn't exist or not created", MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1,
                                            (MessageBoxOptions)0x40000);
                                return;
                            }
                            dbPath = foundDB.BaseDir;
                            // Set the directory to put the PicasaButtons in the PicasaDB...
                            destButtonDir = foundDB.BaseDir + "\\Google\\Picasa2\\buttons";
                        }

                        // Copy Buttons and scripts and set the correct Path variable to be able to start scripts...
                        IOHelper.TryDeleteFiles(destButtonDir, "PSButton*");
                        foreach (PicasaButton button in settings.picasaButtons.ButtonList)
                        {
                            try
                            {
                                button.CreateButtonFile(destButtonDir);
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show(ex.Message);
                            }
                        }

                        settings.picasaButtons.Registerbuttons();

                        // Go!
                        try
                        {
                            runner.RunPicasa(dbPath, appSettingsDir);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(ex.Message);
                        }

                        bool xyz;
                        xyz = IOHelper.UnmapVDrive();

                    }
                    else
                    {
                        MessageBox.Show("The database passed with the /autorun parameter was not found: (" + autoRunDatabaseName + ")");
                        autoRunDatabaseName = null;
                    }
                }

                // If /backup argument was passed...
                //---------------------------------------------------------------------------
                if (backupDatabaseName != null)
                {
                    PicasaDB foundDB = null;
                    // First check if he wants to be asked which database to backup
                    if (backupDatabaseName.Equals("AskUser", StringComparison.CurrentCultureIgnoreCase))
                    {
                        // Show Database selection menu 
                        SelectDBForm selectDBForm = new SelectDBForm(settings, true);
                        selectDBForm.ShowDialog();

                        if (selectDBForm.ReturnDBName != null)
                        {
                            backupDatabaseName = selectDBForm.ReturnDBName;
                        }

                    }
                    // Next check if he wants to backup the standard personal database...
                    if (backupDatabaseName.Equals("personal", StringComparison.CurrentCultureIgnoreCase))
                    {
                        // If the user wants to backup his personal default database... (cmd line arg was "personal") 
                        Application.Run(new BackupForm_CL(settings.picasaDBs[0], appSettingsDir));
                        if (BackupComplete)
                        {
                            settings.picasaDBs[0].LastBackupDate = DateTime.Today;
                            try
                            {
                                SettingsHelper.SerializeSettings(settings,
                                        appSettingsDir + "\\" + SettingsHelper.SettingsFileName);
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show("Error saving settings: " + ex.Message);
                            }
                       }
                    }
                    else
                    {
                        // Exit if the Ask menu was cancelled
                        if (backupDatabaseName.Equals("AskUser", StringComparison.CurrentCultureIgnoreCase))
                        {
                            return;
                        }

                        foreach (PicasaDB db in settings.picasaDBs)
                        {
                            //MessageBox.Show("db: " + db.Name + "\nBackup name: " + backupDatabaseName);
                            if (db.Name.Equals(backupDatabaseName, StringComparison.CurrentCultureIgnoreCase))
                                foundDB = db;
                        }
                        //MessageBox.Show("Foundb: " + foundDB.Name + "\nBackup dir " + foundDB.BackupDir );

                        if (foundDB != null)
                        {
                            bool xxx = IOHelper.MapVirtualDrive(foundDB, appSettingsDir);

                            // Set backup date to now in settings and then save new date to settings file before backup
                            DateTime  SaveBUDate = foundDB.LastBackupDate;
                            foundDB.LastBackupDate = DateTime.Today;
                            try
                            {
                                SettingsHelper.SerializeSettings(settings,
                                        appSettingsDir + "\\" + SettingsHelper.SettingsFileName);
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show("Error saving settings: " + ex.Message);
                            }
                            Application.Run(new BackupForm_CL(foundDB, appSettingsDir));

                            //If backup was cancelled, try to restore settings file backup date to original
                            if (!BackupComplete)
                            {
                                // try to save old backup date if backup does not complete
                                foundDB.LastBackupDate = SaveBUDate;
                                try
                                {
                                    SettingsHelper.SerializeSettings(settings,
                                            appSettingsDir + "\\" + SettingsHelper.SettingsFileName);
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show("Error saving settings: " + ex.Message);
                                }
                            }

                            bool xyz;
                            xyz = IOHelper.UnmapVDrive();


                        }
                        else
                        {
                            MessageBox.Show("The database passed with the /backup parameter was not found: (" + backupDatabaseName + ")");
                            backupDatabaseName = null;
                        }
                    }
                }
            }
            #endregion

            #region Start GUI

            if (showGUI == true)
            {
                Application.Run(new MainForm(settings, appSettingsDir, ConfigFileExists));
            }
            #endregion
        }

    }

}
