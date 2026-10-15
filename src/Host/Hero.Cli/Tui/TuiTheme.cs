using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Hero.Cli.Tui;

/// <summary>Named Terminal.Gui schemes for the interactive shell.</summary>
internal static class TuiTheme
{
    public static void Register()
    {
        // Calm dark chrome — cyan accent, no purple / neon glow.
        var bg = Color.Black;
        var panel = new Color(0x12, 0x14, 0x18);
        var text = new Color(0xD0, 0xD4, 0xDC);
        var muted = new Color(0x8B, 0x93, 0xA0);
        var accent = new Color(0x5B, 0xC0, 0xBE);
        var statusBg = new Color(0x1A, 0x22, 0x2C);
        var inputBg = new Color(0x0E, 0x12, 0x18);

        SchemeManager.AddScheme(
            "HeroChrome",
            new Scheme
            {
                Normal = new TgAttribute(accent, bg),
                Focus = new TgAttribute(accent, bg),
                HotNormal = new TgAttribute(accent, bg),
                HotFocus = new TgAttribute(Color.White, bg),
            });

        SchemeManager.AddScheme(
            "HeroTranscript",
            new Scheme
            {
                Normal = new TgAttribute(text, bg),
                Focus = new TgAttribute(text, bg),
                Editable = new TgAttribute(text, bg),
                ReadOnly = new TgAttribute(text, bg),
                Active = new TgAttribute(Color.Black, accent),
            });

        SchemeManager.AddScheme(
            "HeroHint",
            new Scheme
            {
                Normal = new TgAttribute(muted, bg),
                Focus = new TgAttribute(muted, bg),
            });

        SchemeManager.AddScheme(
            "HeroCompose",
            new Scheme
            {
                Normal = new TgAttribute(accent, panel),
                Focus = new TgAttribute(accent, panel),
                HotNormal = new TgAttribute(accent, panel),
                HotFocus = new TgAttribute(Color.White, panel),
            });

        SchemeManager.AddScheme(
            "HeroPrompt",
            new Scheme
            {
                Normal = new TgAttribute(accent, inputBg),
                Focus = new TgAttribute(accent, inputBg),
            });

        SchemeManager.AddScheme(
            "HeroInput",
            new Scheme
            {
                Normal = new TgAttribute(text, inputBg),
                Focus = new TgAttribute(Color.White, inputBg),
                Editable = new TgAttribute(Color.White, inputBg),
            });

        SchemeManager.AddScheme(
            "HeroStatus",
            new Scheme
            {
                Normal = new TgAttribute(muted, statusBg),
                Focus = new TgAttribute(text, statusBg),
                HotNormal = new TgAttribute(accent, statusBg),
            });
    }
}
