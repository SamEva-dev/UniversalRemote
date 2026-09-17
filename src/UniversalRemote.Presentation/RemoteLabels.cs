using System.Globalization;
namespace UniversalRemote.Remote.Presentation;

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
        "navigation.menu" => "Menu",
        "channel.up" => Text("Chaîne +", "Channel +"), "channel.down" => Text("Chaîne −", "Channel −"),
        "media.playpause" => Text("Lecture / pause", "Play / pause"),
        "media.rewind" => Text("Retour rapide", "Rewind"), "media.fastforward" => Text("Avance rapide", "Fast forward"),
        "media.record" => Text("Enregistrer", "Record"),
        "input.select" => Text("Sources", "Inputs"), "input.tv" => "TV", "input.hdmi1" => "HDMI 1", "input.hdmi2" => "HDMI 2",
        "apps.open" => Text("Applications", "Applications"), "navigation.guide" => "Guide", "navigation.exit" => Text("Quitter", "Exit"),
        "text.delete" => Text("Effacer", "Delete"),
        "key.red" => Text("Rouge", "Red"), "key.green" => Text("Vert", "Green"), "key.yellow" => Text("Jaune", "Yellow"), "key.blue" => Text("Bleu", "Blue"),
        "app.netflix" => "Netflix", "app.youtube" => "YouTube", "app.primevideo" => "Prime Video", "app.disneyplus" => "Disney+",
        var id when id.StartsWith("digit.", StringComparison.Ordinal) && id.Length == 7 => id[6..],
        _ => control.Action.Id
    };
    public static string Section(string id) => id switch
    {
        "power" => Text("Alimentation", "Power"), "audio" => "Audio", "navigation" => "Navigation",
        "channel" => Text("Chaînes", "Channels"), "media" => Text("Média", "Media"),
        "extras" => Text("Autres commandes", "More controls"), _ => id
    };
}
