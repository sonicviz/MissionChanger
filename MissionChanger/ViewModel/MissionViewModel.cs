﻿using MissionChanger.Classes;
using MissionChanger.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;

namespace MissionChanger.ViewModel
{
    public class MissionViewModel : BaseViewModel
    {
        public ObservableCollection<Mission> Missions { get => missions; set => SetField(ref missions, value); }
        private ObservableCollection<Mission> missions = new ObservableCollection<Mission>();

        public Mission SelectedMission { get => selectedMission; set => SetField2(ref selectedMission, value, nameof(IsMissionSelected)); }
        private Mission selectedMission;

        public bool IsMissionSelected => SelectedMission != null;

        public RelayCommand<Mission> SelectedItemChanged { get; private set; } = null;
        public RelayCommand CommandRestoreOriginal { get; private set; } = null;

        public RelayCommand CommandAdd1000 { get; private set; } = null;


        public MissionViewModel()
        {
            SelectedItemChanged = new RelayCommand<Mission>( c => SelectedMission = c );

            CommandRestoreOriginal = new RelayCommand(OnRestoreOriginal);

            CommandAdd1000 = new RelayCommand(OnAdd1000);
        }

        private void OnAdd1000(object obj)
        {
            try
            {
                if (SelectedMission != null)
                {
                    if (double.IsNaN(SelectedMission.Altitude))
                        SelectedMission.Altitude = 0;

                    SelectedMission.Altitude += 1000;
                }
            }
            catch (Exception)
            {
            }
        }


        private void OnRestoreOriginal(object obj)
        {
            try
            {
                if (SelectedMission != null)
                {
                    SelectedMission.PropertyChanged -= Mission_PropertyChanged;
                    RestoreOriginal(SelectedMission);
                    SelectedMission.PropertyChanged += Mission_PropertyChanged;
                }
            }
            catch (Exception)
            {
            }
        }


        private void RestoreOriginal(Mission mission)
        {
            string backupname = string.Empty;

            if (mission.ManifestFile != null)
            {
                backupname = GetBackupFilename(mission.ManifestFile);

                if (LongFile.Exists(backupname))
                {
                    LongFile.Copy(backupname, mission.ManifestFile, true);
                    LongFile.Delete(backupname);
                }
            }

            backupname = GetBackupFilename(mission.Filename);

            if (LongFile.Exists(backupname))
            {
                string fltFile = mission.Filename;
                LongFile.Copy(backupname, fltFile, true);
                LongFile.Delete(backupname);

                INI INI = new INI(fltFile);

                ReadFLT(INI, mission);

                mission.OriginalAircraft = mission.Aircraft;
                mission.HasBackup = false;
                mission.IsChanged = false;
                SelectedMission = null;
                SelectedMission = mission;
            }
        }

        internal void LoadMissions(string communityFolder)
        {
            try
            {
                IEnumerable<string> fltFiles = System.IO.Directory.EnumerateFiles(LongFile.GetWin32LongPath(communityFolder), "*.flt", System.IO.SearchOption.AllDirectories);

                foreach (string fltFile in fltFiles)
                {
                    if (!(fltFile.ToLower().Contains("official") && fltFile.ToLower().Contains("template")))
                    {
                        INI INI = new INI(fltFile);

                        Mission mission = new Mission();
                        mission.MissionType = INI.Read("MissionType", "Main");

                        if (mission.MissionType.Equals("BushTrip", StringComparison.OrdinalIgnoreCase))
                        {
                            mission.Name = Path.GetFileNameWithoutExtension(fltFile);

                            mission.ManifestFile = GetManifestFile(fltFile);
                            mission.Manifest = GetManifest(mission.ManifestFile);
                            mission.IsProtected = mission.Manifest.total_package_size?.Length > 0;

                            mission.HasWeatherFile = LongFile.Exists(Path.Combine(Path.GetDirectoryName(fltFile), "Weather.WPR"));

                            mission.Title = mission.Manifest != null ? $"{mission.Manifest.title} ({mission.Name})" : mission.Name;
                            mission.Filename = LongFile.RemoveWin32LongPath(fltFile);

                            ReadFLT(INI, mission);

                            string backupname = GetBackupFilename(fltFile);

                            if (LongFile.Exists(backupname))
                            {
                                mission.OriginalAircraft = new INI(backupname).Read("Sim", "Sim.0");
                                mission.HasBackup = true;
                            }
                            else
                            {
                                mission.OriginalAircraft = mission.Aircraft;
                                mission.HasBackup = false;
                            }

                            AddSavedMissions(mission);

                            if (!string.IsNullOrWhiteSpace(mission.MissionType) && !string.IsNullOrWhiteSpace(mission.Aircraft))
                            {
                                mission.PropertyChanged += Mission_PropertyChanged;
                                Missions.Add(mission);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("something went wrong loading the missions" +
                    Environment.NewLine +
                    ex.Message);
            }

        }

        private void Mission_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (! e.PropertyName.Equals(nameof(Mission.IsChanged)))
                (sender as Mission).IsChanged = true;

            if (e.PropertyName.Equals(nameof(Mission.Altitude)))
                (sender as Mission).IsOnGround = false;

        }


        private string GetManifestFile(string fltFile)
        {
            DirectoryInfo di = Directory.GetParent(Path.GetDirectoryName(fltFile));

            while (di != null)
            {
                string manifest_filename = Path.Combine(di.FullName, "manifest.json");
                if (LongFile.Exists(manifest_filename))
                {
                    return manifest_filename;
                }

                di = di.Parent;
            }

            return null;
        }

        private Manifest GetManifest(string manifestFile)
        {
            if (LongFile.Exists(manifestFile))
            {
                string jsonData = LongFile.ReadAllText(manifestFile);
                JavaScriptSerializer js = new JavaScriptSerializer();
                js.MaxJsonLength = jsonData.Length + 10;

                Manifest mf = js.Deserialize<Manifest>(jsonData);
                return mf;
            }

            return null;
        }

        private long GetPackageSize(string manifestFile)
        {
            DirectoryInfo di = new DirectoryInfo(Path.GetDirectoryName(manifestFile));
            return GetDirectorySize(di);
        }

        private long GetDirectorySize(DirectoryInfo di)
        {
            long size = 0;

            foreach (FileInfo fi in di.GetFiles())
            {
                size += fi.Length;
            }

            foreach (DirectoryInfo di2 in di.GetDirectories())
            {
                size += GetDirectorySize(di2);
            }

            return size;
        }

        private static string SavedMissonsPath()
        {
            string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string RoamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);


            List<string> sl = new List<string>()
            {
                LocalAppData + @"\Packages\Microsoft.FlightSimulator_8wekyb3d8bbwe\LocalState\MISSIONS\ACTIVITIES\",

                RoamingAppData + @"\Microsoft Flight Simulator\MISSIONS\ACTIVITIES\"
            };

            foreach (string d in sl)
                if (Directory.Exists(d))
                    return d;

            return string.Empty;
        }

        private void AddSavedMissions(Mission theMission)
        {
            try
            {
                string savedMissionPath = SavedMissonsPath();

                if (!string.IsNullOrWhiteSpace(savedMissionPath))
                {
                    savedMissionPath = LongFile.GetWin32LongPath(Path.Combine(savedMissionPath, theMission.Name + "_SAVE"));

                    if (Directory.Exists(savedMissionPath))
                    {
                        IEnumerable<string> fltFiles = System.IO.Directory.EnumerateFiles(savedMissionPath, "*.flt", System.IO.SearchOption.AllDirectories);

                        foreach (string fltFile in fltFiles)
                        {
                            INI INI = new INI(fltFile);

                            string name = fltFile.Replace(savedMissionPath, string.Empty);

                            if (name.IndexOf("BCKP", StringComparison.OrdinalIgnoreCase) == -1)
                            {
                                string file = Path.GetFileName(name);
                                name = name.Replace(file, string.Empty);
                                name = name.Replace("\\", string.Empty);

                                Mission savedMission = new Mission();
                                savedMission.IsSavedMission = true;
                                savedMission.IsProtected = theMission.IsProtected;
                                savedMission.HasWeatherFile = theMission.HasWeatherFile;

                                savedMission.MissionType = INI.Read("MissionType", "Main");
                                savedMission.Name = name;    // TODO
                                savedMission.Title = name;
                                savedMission.Filename = LongFile.RemoveWin32LongPath(fltFile);

                                savedMission.LostGPS = !INI.KeyExists("WpInfo0", "GPS_Engine");

                                ReadFLT(INI, savedMission);

                                string backupname = GetBackupFilename(fltFile);

                                if (LongFile.Exists(backupname))
                                {
                                    savedMission.OriginalAircraft = new INI(backupname).Read("Sim", "Sim.0");
                                    savedMission.HasBackup = true;
                                }
                                else
                                {
                                    savedMission.OriginalAircraft = savedMission.Aircraft;
                                    savedMission.HasBackup = false;
                                }

                                if (!string.IsNullOrWhiteSpace(savedMission.MissionType) && !string.IsNullOrWhiteSpace(savedMission.Aircraft))
                                {
                                    savedMission.PropertyChanged += Mission_PropertyChanged;
                                    theMission.SavedMissions.Add(savedMission);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private void ReadFLT(INI INI, Mission mission)
        {
            mission.Aircraft = INI.Read("Sim", "Sim.0");

            ReadWeather(INI, mission);
            mission.DateTime = ReadDateTime(INI);
            mission.MultiPlayer = INI.ReadDefault(0, "Status", "MultiPlayer") > 0;

            mission.IsOnGround = INI.ReadDefault(false, "SimOnGround", "SimVars.0");
            mission.Altitude = INI.ReadDefault(double.NaN, "Altitude", "SimVars.0");
            mission.Pitch = INI.ReadDefault(double.NaN, "Pitch", "SimVars.0");
            mission.Bank = INI.ReadDefault(double.NaN, "Bank", "SimVars.0");
        }

        private void ReadWeather(INI INI, Mission mission)
        {
            mission.UseWeatherFile = INI.ReadDefault(false, "UseWeatherFile", "Weather");
            mission.UseLiveWeather = INI.ReadDefault(false, "UseLiveWeather", "Weather");
            mission.WeatherPresetFile = INI.ReadDefault("", "WeatherPresetFile", "Weather");
            mission.WeatherCanBeLive = INI.ReadDefault(false, "WeatherCanBeLive", "Weather");
        }

        private DateTime ReadDateTime(INI INI)
        {
            int year = INI.ReadDefault(2021, "Year", "DateTimeSeason");
            int days = INI.ReadDefault(1, "Day", "DateTimeSeason");
            int hours = INI.ReadDefault(8, "Hours", "DateTimeSeason");
            int minutes = INI.ReadDefault(0, "Minutes", "DateTimeSeason");
            int seconds = INI.ReadDefault(0, "Seconds", "DateTimeSeason");

            DateTime dt = new DateTime(year, 1, 1, hours, minutes, seconds);
            dt = dt.AddDays(days -1);

            return dt;
        }

        internal void SaveMissions()
        {
            foreach (Mission mission in Missions)
            {
                if (mission.IsChanged && mission.IsProtected)
                {
                    if (MessageBoxResult.No ==
                        MessageBox.Show("You changed at least one protected mission. " +
                        "This can prevent the simulator from starting or the mission becomes impossible to start." + Environment.NewLine +
                        "Do you want to proceed?", "MissionChanger", MessageBoxButton.YesNo))
                    {
                        return;
                    }

                    break;
                }
            }

            foreach (Mission mission in Missions)
            {
                if (mission.IsChanged)
                    SaveMission(mission);

                foreach (Mission savedMission in mission.SavedMissions)
                {
                    if (savedMission.IsChanged)
                        SaveMission(savedMission);
                }
            }
        }

        private void SaveMission(Mission mission)
        {
            if (mission.IsChanged)
            {
                string backupname = GetBackupFilename(mission.Filename);

                if (!LongFile.Exists(backupname))
                    LongFile.Copy(mission.Filename, backupname);

                mission.HasBackup = true;

                DateTime creationDateTime = LongFile.GetCreationTime(mission.Filename);
                DateTime lastWriteDateTime = LongFile.GetLastWriteTime(mission.Filename);

                INI INI = new INI(LongFile.GetWin32LongPath(mission.Filename));
                INI.Write("Sim", mission.Aircraft, "Sim.0");

                INI.Write("Year", mission.DateTime.Year, "DateTimeSeason");
                INI.Write("Day", mission.DateTime.DayOfYear, "DateTimeSeason");
                INI.Write("Hours", mission.DateTime.Hour, "DateTimeSeason");
                INI.Write("Minutes", mission.DateTime.Minute, "DateTimeSeason");
                INI.Write("Seconds", mission.DateTime.Second, "DateTimeSeason");

                if (mission.UseWeatherFile)
                {
                    INI.Write("UseWeatherFile", true, "Weather");
                    INI.DeleteKey("UseLiveWeather", "Weather");
                    INI.DeleteKey("WeatherPresetFile", "Weather");
                    INI.DeleteKey("WeatherCanBeLive", "Weather");
                }
                else
                if (mission.UseLiveWeather)
                {
                    INI.Write("UseWeatherFile", false, "Weather");
                    INI.Write("UseLiveWeather", true, "Weather");
                    INI.Write("WeatherPresetFile", "", "Weather");
                    INI.Write("WeatherCanBeLive", true, "Weather");
                }
                else
                {
                    INI.Write("UseWeatherFile", false, "Weather");
                    INI.Write("UseLiveWeather", false, "Weather");
                    INI.Write("WeatherPresetFile", mission.WeatherPresetFile, "Weather");
                    INI.Write("WeatherCanBeLive", false, "Weather");
                }

                INI.Write("Status", mission.MultiPlayer ? "1" : "0", "Multiplayer");

                if (mission.Manifest?.total_package_size?.Length > 0)
                {
                    string backupName = GetBackupFilename(mission.ManifestFile);

                    if (!LongFile.Exists(backupName))
                        LongFile.Copy(mission.ManifestFile, backupName);

                    long packageSize = GetPackageSize(mission.ManifestFile);

                    if (packageSize > 0)
                    {
                        DateTime creationDateTimeManifest = LongFile.GetCreationTime(mission.ManifestFile);
                        DateTime lastWriteDateTimeManifest = LongFile.GetLastWriteTime(mission.ManifestFile);

                        int l = mission.Manifest.total_package_size.Length;

                        string s = "00000000000000000000" + packageSize.ToString();

                        s = s.Substring(s.Length - l);

                        string mfText = LongFile.ReadAllText(mission.ManifestFile);

                        int p1 = mfText.IndexOf(nameof(Manifest.total_package_size));

                        int p2 = mfText.IndexOf("\"", p1+ nameof(Manifest.total_package_size).Length+1);

                        mfText = mfText.Remove(p2 + 1, l);
                        mfText = mfText.Insert(p2 + 1, s);

                        LongFile.WriteAllText(mission.ManifestFile, mfText, new UTF8Encoding(false));

                        LongFile.SetCreationTime(mission.ManifestFile, creationDateTimeManifest);
                        LongFile.SetLastWriteTime(mission.ManifestFile, lastWriteDateTimeManifest);
                    }
                }

                if (mission.IsSavedMission)
                {
                    INI.Write("SimOnGround", mission.IsOnGround, "SimVars.0");

                    if (!double.IsNaN(mission.Altitude))
                        INI.Write("Altitude", string.Format(CultureInfo.InvariantCulture, "{0:+000000.00;-000000.00}", mission.Altitude), "SimVars.0");

                    if (!double.IsNaN(mission.Pitch))
                        INI.Write("Pitch", string.Format(CultureInfo.InvariantCulture, "{0}", mission.Pitch), "SimVars.0");

                    if (!double.IsNaN(mission.Bank))
                        INI.Write("Bank", string.Format(CultureInfo.InvariantCulture, "{0}", mission.Bank), "SimVars.0");
                }

                LongFile.SetCreationTime(mission.Filename, creationDateTime);
                LongFile.SetLastWriteTime(mission.Filename, lastWriteDateTime);

                ReadFLT(INI, mission);

                mission.IsChanged = false;
            }
        }

        string GetBackupFilename(string fullname, bool remove_ext = true)
        {
            string path = Path.GetDirectoryName(fullname);
            string filename = remove_ext ? Path.GetFileNameWithoutExtension(fullname) : Path.GetFileName(fullname);

            return Path.Combine(path, filename + ".bak");
        }
    }
}
