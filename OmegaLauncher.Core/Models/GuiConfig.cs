using OmegaLauncher.Core.News;

namespace OmegaLauncher.Core.Models
{
    public class GuiConfig
    {
        public string ClientPath { get; set; } = string.Empty;
        public bool AutoCheckUpdates { get; set; } = true;
        public bool DeckResolution { get; set; } = true;           // Linux: start the game at 1280x800 on a Steam Deck
        public string RunnerPath { get; set; } = string.Empty;     // Linux: proton script, umu-run or wine; empty = detect
        public string PrefixPath { get; set; } = string.Empty;     // Linux: Wine prefix folder; empty = launcher data folder
        public string ThemeOverride { get; set; } = string.Empty;
        public NewsFeedSourceCategories NewsCategoryFilter { get; set; } = NewsFeedSourceCategories.All;
        public string DefaultNewsFeedUrl { get; set; } = "https://crypto137.github.io/MHServerEmu/feed.xml";

        public GuiConfig Clone()
        {
            return (GuiConfig)MemberwiseClone();
        }
    }
}
