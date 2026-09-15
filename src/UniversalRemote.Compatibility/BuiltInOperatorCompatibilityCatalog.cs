using UniversalRemote.Abstractions;

namespace UniversalRemote.Compatibility;

public sealed class BuiltInOperatorCompatibilityCatalog : IOperatorCompatibilityCatalog
{
    public static BuiltInOperatorCompatibilityCatalog Instance { get; } = new();
    private static readonly DateOnly ReviewedOn = new(2026, 9, 15);

    private static CompatibilityEvidence Evidence(string title, string uri, CompatibilityEvidenceKind kind)
        => new(title, new Uri(uri, UriKind.Absolute), kind, ReviewedOn);

    private static readonly IReadOnlyList<OperatorCompatibilityProfile> Profiles = Array.AsReadOnly(
    [
        new OperatorCompatibilityProfile(
            id: "orange-tv-uhd",
            @operator: "Orange",
            productFamily: "Décodeur TV UHD",
            models: ["WHD94 / Décodeur TV UHD 4K"],
            providerId: "orange-tv-uhd",
            integrationMode: OperatorIntegrationMode.DedicatedProvider,
            proposedSupportLevel: SupportLevel.Experimental,
            transport: "LAN / HTTP local - endpoint constructeur non documenté publiquement",
            requiresPhysicalValidation: true,
            evidence:
            [
                Evidence("Orange - Décodeur TV UHD compatible 4K", "https://reseaux.orange.fr/vos-equipements/decodeur-tv-uhd-orange", CompatibilityEvidenceKind.OfficialProductDocumentation),
                Evidence("Communauté Orange - commandes remoteControl/cmd", "https://communaute.orange.fr/t5/TV-par-ADSL-et-Fibre/API-pour-commander-le-decodeur-TV-depusi-une-tablette/m-p/520729", CompatibilityEvidenceKind.CommunityObservedProtocol),
                Evidence("Open source - Orange Livebox TV UHD 4K controller", "https://github.com/DalFanajin/Orange-Livebox-TV-UHD-4K-python-controller", CompatibilityEvidenceKind.OpenSourceImplementation)
            ],
            decision: "Provider dédié REMOTE-042 implémenté en Experimental avec IP locale littérale stricte, port 8080 fixe, actions normalisées et aucune relance implicite. Promotion interdite avant recette firmware/appareil réelle."),

        new OperatorCompatibilityProfile(
            id: "bouygues-bbox-androidtv",
            @operator: "Bouygues Telecom",
            productFamily: "Bbox Miami / Bbox 4K / Bbox 4K HDR",
            models:
            [
                "HMB4213H",
                "HMB9213NW",
                "HMB9213NW-v2",
                "HMB9213NW-v2.1",
                "UZW4020BYT",
                "UZW4020BYT3",
                "UZW4020BYT4"
            ],
            providerId: "androidtv",
            integrationMode: OperatorIntegrationMode.ReuseExistingProvider,
            proposedSupportLevel: SupportLevel.Experimental,
            transport: "LAN / Android TV Remote Service v2",
            requiresPhysicalValidation: true,
            evidence:
            [
                Evidence("Bouygues Telecom - Bbox 4K et Bbox Miami sous Android TV", "https://www.bouyguestelecom.fr/choisir-bouygues-telecom/fonctionnalites-tv", CompatibilityEvidenceKind.OfficialPlatformDocumentation),
                Evidence("Bouygues Telecom - modèles de décodeurs déclarés", "https://www.bouyguestelecom.fr/tarifs-conditions", CompatibilityEvidenceKind.OfficialProductDocumentation),
                Evidence("Bouygues Telecom - pourquoi choisir une box Android TV", "https://mag.bouyguestelecom.fr/divertissement/tv-streaming/les-bonnes-raisons-de-choisir-une-box-android-tv/", CompatibilityEvidenceKind.OfficialPlatformDocumentation)
            ],
            decision: "REMOTE-043 ne crée aucun provider Bbox. Le runtime qualifie la famille/modèle à partir des indices de découverte et exige le service Android TV Remote v2, puis une recette physique distincte par modèle et firmware avant toute promotion."),

        new OperatorCompatibilityProfile(
            id: "sfr-connect-tv-androidtv",
            @operator: "SFR",
            productFamily: "Connect TV v1 / v2 / v3",
            models:
            [
                "Connect TV v1 / DV8219_SFR",
                "Connect TV v2 / DV8555",
                "Connect TV v3 SDMC / DV8945-KFS / DV8985",
                "Connect TV v3 Sagemcom / DIW377 ALT FR"
            ],
            providerId: "androidtv",
            integrationMode: OperatorIntegrationMode.ReuseExistingProvider,
            proposedSupportLevel: SupportLevel.Experimental,
            transport: "LAN / Android TV Remote Service v2",
            requiresPhysicalValidation: true,
            evidence:
            [
                Evidence("SFR - Connect TV est un produit Android TV", "https://assistance.sfr.fr/television/decodeur-connect-tv/utiliser-connect-tv-sfr.html", CompatibilityEvidenceKind.OfficialPlatformDocumentation),
                Evidence("SFR - caractéristiques Connect TV v1, v2 et v3", "https://assistance.sfr.fr/television/decodeur-connect-tv/caracteristiques-techniques-connect-tv-sfr.html", CompatibilityEvidenceKind.OfficialProductDocumentation),
                Evidence("Android TV Guide - références matérielles Connect TV", "https://www.androidtv-guide.com/oem/sdmc/", CompatibilityEvidenceKind.ThirdPartyHardwareCatalog)
            ],
            decision: "REMOTE-044 ne crée aucun provider SFR pour Connect TV. Le runtime réutilise androidtv, exige Android TV Remote Service v2 et qualifie génération/référence matérielle avant une recette physique distincte par firmware. Les références DV*/DIW377 sont des indices tiers, pas une nomenclature SFR officiellement garantie."),

        new OperatorCompatibilityProfile(
            id: "sfr-box8-tv",
            @operator: "SFR",
            productFamily: "SFR Box 8 TV",
            models: ["SFR Box 8 TV"],
            providerId: null,
            integrationMode: OperatorIntegrationMode.ResearchOnly,
            proposedSupportLevel: null,
            transport: "LAN / protocole SFR non public à qualifier",
            requiresPhysicalValidation: true,
            evidence:
            [
                Evidence("SFR - fonctionnalités Box 8 TV", "https://assistance.sfr.fr/television/decodeur-tv-8-4k/fonctionnalites-sfr-stb-box-8-tv.html", CompatibilityEvidenceKind.OfficialProductDocumentation),
                Evidence("SFR TV - télécommande virtuelle compatible avec les box/décodeurs pris en charge", "https://assistance.sfr.fr/sfrmail-appli/sfr-tv-8/questions-sfr-tv-8.html", CompatibilityEvidenceKind.OfficialRemoteFeatureDocumentation)
            ],
            decision: "Aucun protocole public suffisamment documenté n'est retenu dans ce lot. Ne pas deviner ni cloner un protocole privé ; poursuivre uniquement par documentation publique ou recette contrôlée sur matériel possédé."),
    ]);

    private BuiltInOperatorCompatibilityCatalog() { }

    public IReadOnlyList<OperatorCompatibilityProfile> List() => Profiles;

    public OperatorCompatibilityProfile? Find(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Profiles.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<OperatorCompatibilityProfile> FindByProvider(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        return Profiles.Where(x => string.Equals(x.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
}
