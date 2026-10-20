using Bifrost.Core.ClientManagement;

namespace Bifrost.Core.Platform
{
    /// <summary>The <c>--print-launch</c> dry run: shows what the launcher found and the command it would run, without starting the game.</summary>
    public static class LaunchDiagnostics
    {
        public static int Run(TextWriter output)
        {
            ClientLauncher launcher = new();

            string clientPath = ClientHelper.GetClientPath();
            ClientLauncherInitializationResult result = launcher.Initialize(clientPath);

            if (result != ClientLauncherInitializationResult.Success)
            {
                foreach (string path in ClientHelper.GetFallbackPaths(launcher.GuiConfig.ClientPath))
                {
                    string root = ClientHelper.ResolveRoot(path);
                    if (root != null && launcher.Initialize(root) == ClientLauncherInitializationResult.Success)
                    {
                        result = ClientLauncherInitializationResult.Success;
                        break;
                    }
                }
            }

            output.WriteLine($"System: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
            output.WriteLine($"Game folder: {(result == ClientLauncherInitializationResult.Success ? launcher.ClientRoot : ClientLauncher.GetInitializationResultText(result))}");

            if (result != ClientLauncherInitializationResult.Success)
                return 1;

            if (OperatingSystem.IsLinux())
            {
                List<GameRunner> runners = launcher.FindRunners();
                output.WriteLine(runners.Count == 0 ? "Runners: none found" : "Runners (first one is used):");
                foreach (GameRunner runner in runners)
                    output.WriteLine($"  {runner}");
            }

            LaunchCommand command = launcher.BuildLaunchCommand();
            if (command == null)
            {
                output.WriteLine("Cannot launch: " + launcher.LastLaunchError);
                return 2;
            }

            output.WriteLine("Command:");
            output.WriteLine(command.Describe());
            return 0;
        }
    }
}
