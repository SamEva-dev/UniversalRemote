using System.Globalization;
namespace UniversalRemote.Presentation;

public static class RemoteLabels
{
    public static bool IsFrench => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr";
    public static string Text(string french, string english) => IsFrench ? french : english;
    public static string Action(RemoteUiControl control) => control.Action.Id switch
    {
        "power.toggle" => Text("Marche / arrêt", "Power"),
        "volume.up" => "Volume +", "volume.down" => "Volume −",
        "audio.mute.toggle" => Text("Muet", "Mute"),
        "navigation.up" => Text("Haut", "Up"), "navigation.down" => Text("Bas", "Down"),
        "navigation.left" => Text("Gauche", "Left"), "navigation.right" => Text("Droite", "Right"),
        "navigation.ok" => "OK", "navigation.back" => Text("Retour", "Back"), "navigation.home" => Text("Accueil", "Home"),
        _ => control.Action.Id
    };
    public static string Section(string id) => id switch
    {
        "power" => Text("Alimentation", "Power"), "audio" => "Audio", "navigation" => "Navigation",
        "extras" => Text("Autres commandes", "More controls"), _ => id
    };
}
