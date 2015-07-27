using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Holf.AllForOne;
using Microsoft.VisualBasic.Devices;
using Microsoft.VisualBasic.Logging;

namespace NuGetDirSync
{
    public class NuGetDirSync
    {
        private readonly string _apiKey = ConfigurationManager.AppSettings["NuGetApiKey"];
        private readonly double _furtherChangesWaitSeconds = Convert.ToDouble(ConfigurationManager.AppSettings["FurtherChangesWaitSeconds"]);
        private readonly string _nugetServer = ConfigurationManager.AppSettings["NuGetServer"];
        private readonly string _packageName = ConfigurationManager.AppSettings["PackageName"];
        private readonly string _tempPath = ConfigurationManager.AppSettings["TempPath"];
        private readonly string _versionFile = ConfigurationManager.AppSettings["VersionFile"];
        private readonly string _watchFolder = ConfigurationManager.AppSettings["WatchFolder"];
        private IDisposable _currentSubscription;

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

            var wait = TimeSpan.FromSeconds(_furtherChangesWaitSeconds);
            _currentSubscription = CreateObservableFileSystemStream(_watchFolder)
                .Do(x => Trace.TraceInformation("Changes Detected - Waiting for more"))
                .Throttle(wait)
                .ObserveLatestOn(CurrentThreadScheduler.Instance)
                .Do(_ => Trace.TraceInformation("Begining packaging process"))
                .Select(_ => GetAndIncrementVersion())
                .Subscribe(BuildAndDeployPackage);
        }

        public void Stop()
        {
            var currentSubscription = _currentSubscription;
            if (currentSubscription != null)
                currentSubscription.Dispose();
            _currentSubscription = null;
        }

        private Version GetAndIncrementVersion()
        {
            var currentVersion = 1;
            if (File.Exists(_versionFile))
                currentVersion = Convert.ToInt32(File.ReadAllText(_versionFile));

            File.WriteAllText(_versionFile, (currentVersion + 1).ToString());

            return new Version(0, 0, currentVersion);
        }

        private void BuildAndDeployPackage(Version packageVersion)
        {
            var workingDirPath = Path.Combine(_tempPath, string.Format("{0}", packageVersion));
            CloneContent(workingDirPath);

            var packagePath = BuildPackage(packageVersion, workingDirPath, _packageName);
            PushPackage(packagePath, _apiKey, _nugetServer);

            CleanUp(workingDirPath);
        }

        private static void CleanUp(string workingDirPath)
        {
            Trace.TraceInformation("Cleaning up - deleting {0}", workingDirPath);
            Directory.Delete(workingDirPath, true);
        }

        private void CloneContent(string workingDirPath)
        {
            Trace.TraceInformation("Cloning directory to {0}", workingDirPath);
            Directory.CreateDirectory(workingDirPath);
            new Computer().FileSystem.CopyDirectory(_watchFolder, Path.Combine(workingDirPath, "content"));
        }

        private void PushPackage(string packagePath, string apiKey, string nugetServer)
        {
            Trace.TraceInformation("Publising package - {0}", packagePath);
            if (string.IsNullOrWhiteSpace(packagePath))
                return;

            var nugetPushProcessInfo = new ProcessStartInfo("NuGet.exe", string.Format("push {0} -ApiKey {1} -Source {2}", packagePath, apiKey, nugetServer))
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            if (!RunSystemProcess(nugetPushProcessInfo))
            {
                Trace.TraceError("Publish failed - {0}", packagePath);
            }
        }

        private string BuildPackage(Version packageVersion, string workingDirPath, string packageName)
        {
            Trace.TraceInformation("Building Package - {0}", packageVersion);

            var nuSpecFileName = Path.Combine(workingDirPath, string.Format("{0}.nuspec", packageName));
            var nuSpecContent = new PackageSpecTemplate(packageVersion, packageName).TransformText();
            File.WriteAllText(nuSpecFileName, nuSpecContent);

            var outputDirectory = workingDirPath;
            var nugetPackProcessInfo = new ProcessStartInfo("NuGet.exe", string.Format("pack {0} -OutputDirectory {1}", nuSpecFileName, outputDirectory))
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            if (RunSystemProcess(nugetPackProcessInfo))
            {
                return string.Format(Path.Combine(outputDirectory, string.Format("{0}.{1}.nupkg", packageName, packageVersion)));
            }

            Trace.TraceError("Build package failed - {0}", packageVersion);
            return null;
        }

        private IObservable<FileSystemEventArgs> CreateObservableFileSystemStream(string watchFolder)
        {
            return Observable.Create<FileSystemEventArgs>(observer =>
            {
                var fileSystemWatcher = new FileSystemWatcher(watchFolder)
                {
                    EnableRaisingEvents = true,
                    Filter = "*.*",
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
                };

                Debug.WriteLine("FileSystemWatcher Created");

                fileSystemWatcher.Created += (sender, args) => observer.OnNext(args);
                fileSystemWatcher.Changed += (sender, args) => observer.OnNext(args);
                fileSystemWatcher.Deleted += (sender, args) => observer.OnNext(args);
                fileSystemWatcher.Renamed += (sender, args) => observer.OnNext(args);              

                return fileSystemWatcher;
            });
        }

        private static bool RunSystemProcess(ProcessStartInfo startInfo)
        {
            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    Trace.TraceError("Failed to start process {0} {1}", startInfo.FileName, startInfo.Arguments);
                    return false;
                }
                process.TieLifecycleToParentProcess();
                process.WaitForExit();

                var stdOut = process.StandardOutput.ReadToEnd();
                if (!string.IsNullOrWhiteSpace(stdOut))
                    Trace.TraceInformation(stdOut);

                var stdErr = process.StandardError.ReadToEnd();
                if (!string.IsNullOrWhiteSpace(stdErr))
                    Trace.TraceError(stdErr);

                if (process.ExitCode == 0)
                {
                    return true;
                }
                Trace.TraceError("Process failed: {0} {1}", startInfo.FileName, startInfo.Arguments);
                return false;
            }
        }
    }
}