namespace OmegaLauncher.Core.Platform
{
    /// <summary>Recognises a Steam Deck so the game can start at the Deck's own screen size.</summary>
    public static class SteamDeck
    {
        /// <summary>Set to 1 to behave like a Steam Deck on any machine (for testing and dry runs).</summary>
        public const string ForceEnvironmentVariable = "MHO_FORCE_DECK";

        public const int ScreenWidth = 1280;
        public const int ScreenHeight = 800;

        public static bool IsForced => Environment.GetEnvironmentVariable(ForceEnvironmentVariable) == "1";

        public static bool IsSteamDeck() => Detect(Environment.GetEnvironmentVariable, ReadDmi);

        /// <summary>
        /// A Deck is recognised by the SteamDeck=1 variable Steam sets for games (Gaming Mode), or by the Valve board name
        /// ("Jupiter" for the LCD model, "Galileo" for the OLED model), which also works in Desktop Mode.
        /// </summary>
        public static bool Detect(Func<string, string> getEnvironmentVariable, Func<string, string> readDmiValue)
        {
            if (getEnvironmentVariable(ForceEnvironmentVariable) == "1" || getEnvironmentVariable("SteamDeck") == "1")
                return true;

            string vendor = readDmiValue("sys_vendor")?.Trim();
            string product = readDmiValue("product_name")?.Trim();
            return vendor == "Valve" && (product == "Jupiter" || product == "Galileo");
        }

        public static void AddResolutionArguments(List<string> arguments)
        {
            arguments.Add($"-ResX={ScreenWidth}");
            arguments.Add($"-ResY={ScreenHeight}");
        }

        private static string ReadDmi(string name)
        {
            try
            {
                string path = "/sys/devices/virtual/dmi/id/" + name;
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
