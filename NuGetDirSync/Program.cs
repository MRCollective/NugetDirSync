using Topshelf;

namespace NuGetDirSync
{
  public class Program
  {
    public static void Main()
    {
        HostFactory.Run(x =>
        {
            x.Service<NuGetDirSync>(s =>
            {
                s.ConstructUsing(name => new NuGetDirSync());
                s.WhenStarted(n => n.Start());
                s.WhenStopped(n => n.Stop());
            });
            x.RunAsLocalSystem();
            x.StartAutomaticallyDelayed();
            x.SetDescription("Watches a given folder in a file system and publishes the contents to NuGet packages when those contents change.");
            x.SetDisplayName("NuGetDirSync");
            x.SetServiceName("NuGetDirSync");
        });
    }
  }
}
