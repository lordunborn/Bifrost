using OmegaLauncher.Core.Platform;

namespace OmegaLauncher.Updates.Tests
{
    public class SteamDeckTests
    {
        private static string NoEnv(string name) => null;
        private static string NoDmi(string name) => null;

        [Fact]
        public void Detect_NormalPc_IsNotADeck()
        {
            Assert.False(SteamDeck.Detect(NoEnv, NoDmi));
            Assert.False(SteamDeck.Detect(NoEnv, name => name == "sys_vendor" ? "ASUSTeK" : "ROG"));
        }

        [Fact]
        public void Detect_GamingModeVariable_IsADeck()
        {
            Assert.True(SteamDeck.Detect(name => name == "SteamDeck" ? "1" : null, NoDmi));
        }

        [Theory]
        [InlineData("Jupiter")]
        [InlineData("Galileo")]
        public void Detect_ValveBoard_IsADeckInDesktopModeToo(string product)
        {
            Assert.True(SteamDeck.Detect(NoEnv, name => name == "sys_vendor" ? "Valve\n" : name == "product_name" ? product + "\n" : null));
        }

        [Fact]
        public void Detect_ValveVendorWithOtherBoard_IsNotADeck()
        {
            Assert.False(SteamDeck.Detect(NoEnv, name => name == "sys_vendor" ? "Valve" : "Steam Machine"));
        }

        [Fact]
        public void Detect_ForceVariable_WorksAnywhere()
        {
            Assert.True(SteamDeck.Detect(name => name == SteamDeck.ForceEnvironmentVariable ? "1" : null, NoDmi));
        }

        [Fact]
        public void AddResolutionArguments_UsesTheDeckScreen()
        {
            List<string> args = new() { "-robocopy" };

            SteamDeck.AddResolutionArguments(args);

            Assert.Equal(new[] { "-robocopy", "-ResX=1280", "-ResY=800" }, args);
        }
    }
}
