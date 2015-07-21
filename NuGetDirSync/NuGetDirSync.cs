using Holf.AllForOne;
using Microsoft.VisualBasic.Devices;
using Microsoft.VisualBasic.Logging;
using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Timers;

namespace NuGetDirSync
{
    public class NuGetDirSync
    {
        private readonly object _lock = new object();
        private Timer _notificationTimer;

        private readonly string _tempPath = ConfigurationManager.AppSettings["TempPath"];
        private readonly string _versionFile = ConfigurationManager.AppSettings["VersionFile"];
        private readonly string _packageName = ConfigurationManager.AppSettings["PackageName"];
        private readonly string _packageNuSpec = ConfigurationManager.AppSettings["PackageNuSpec"];
        private readonly string _watchFolder = ConfigurationManager.AppSettings["WatchFolder"];
        private readonly string _nugetServer = ConfigurationManager.AppSettings["NuGetServer"];
        private readonly string _apiKey = ConfigurationManager.AppSettings["NuGetApiKey"];
        private readonly double _furtherChangesWaitSeconds = Convert.ToDouble(ConfigurationManager.AppSettings["FurtherChangesWaitSeconds"]);

        public NuGetDirSync()
        {
            Trace.Listeners.Add(new FileLogTraceListener
            {
                Location = LogFileLocation.ExecutableDirectory,
                LogFileCreationSchedule = LogFileCreationScheduleOption.Weekly,
                AutoFlush = true,
                TraceOutputOptions = TraceOptions.DateTime,
                IncludeHostName = false
            });
            Trace.Listeners.Add(new ConsoleTraceListener());
        }

        public void Start()
        {
            if (string.IsNullOrEmpty(_watchFolder) || string.IsNullOrEmpty(_nugetServer) || string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_packageName))
            {
                throw new Exception("One or more configuration values is missing in App.config.");
            }

            _notificationTimer = new Timer();
            _notificationTimer.Elapsed += notificationTimer_Elapsed;
            _notificationTimer.Interval = _furtherChangesWaitSeconds;
            var fileSystemWatcher = new FileSystemWatcher(_watchFolder)
            {
                EnableRaisingEvents = true,
                Filter = "*.*",
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
            };
            fileSystemWatcher.Created += FolderUpdated;
            fileSystemWatcher.Changed += FolderUpdated;
            fileSystemWatcher.Deleted += FolderUpdated;
            fileSystemWatcher.Renamed += FolderUpdated;
        }

        public void Stop()
        {
        }

        private void notificationTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            lock (_lock)
            {
                Trace.TraceInformation("Changes completed, creating package.");
                var nextVersion = 1;
                if (File.Exists(_versionFile))
                    nextVersion = Convert.ToInt32(File.ReadAllText(_versionFile));
                if (Directory.Exists(_tempPath))
                    Directory.Delete(_tempPath, true);
                new Computer().FileSystem.CopyDirectory(_watchFolder, _tempPath);
                File.WriteAllText(_packageNuSpec, string.Format(File.ReadAllText(_packageNuSpec), _packageName));
                var nugetPackProcess = new ProcessStartInfo("NuGet.exe", string.Format("pack {0} -Version 0.0.{1}", _packageNuSpec, nextVersion))
                {
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };
                var nugetPushProcess = new ProcessStartInfo("NuGet.exe", string.Format("push {0}.0.0.{1}.nupkg -ApiKey {2} -Source {3}", _packageName, nextVersion, _apiKey, _nugetServer))
                {
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };
                using (var resource1 = Process.Start(nugetPackProcess))
                {
                    resource1.TieLifecycleToParentProcess();
                    resource1.WaitForExit();
                    Trace.TraceInformation(resource1.StandardOutput.ReadToEnd());
                    Trace.TraceError(resource1.StandardError.ReadToEnd());
                    if (resource1.ExitCode == 0)
                    {
                        using (var resource0 = Process.Start(nugetPushProcess))
                        {
                            resource0.TieLifecycleToParentProcess();
                            resource0.WaitForExit();
                            Trace.TraceInformation(resource0.StandardOutput.ReadToEnd());
                            Trace.TraceError(resource0.StandardError.ReadToEnd());
                            if (resource0.ExitCode == 0)
                            {
                                Trace.TraceInformation("Successfully generated and pushed package {0}.0.0.{1}.nupkg", _packageName, nextVersion);
                                File.WriteAllText(_versionFile, (nextVersion + 1).ToString());
                            }
                        }
                    }
                }
                _notificationTimer.Stop();
            }
        }

        private void FolderUpdated(object sender, FileSystemEventArgs e)
        {
            Trace.TraceInformation("Change detected in folder, waiting for further changes...");
            _notificationTimer.Stop();
            _notificationTimer.Start();
        }
    }
}
