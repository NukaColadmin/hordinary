using Hordinary.Agents;
using Hordinary.Config;
using Hordinary.Game;
using Hordinary.World;

namespace Hordinary.Tests;

/// <summary>
/// Le catalogue de missions (<c>config/missions.json</c>) et le camp.
///
/// Une mission est une donnee, comme un survivant, un objectif Utility AI ou une action du domaine :
/// aucun endroit de <c>Hordinary.Core</c> ne nomme « Ravitaillement pharmacie ». Ce qui est verifie
/// ici, c'est que le fichier livre se charge, qu'il decrit des missions jouables sur la carte livree,
/// et qu'un fichier fautif se signale au chargement plutot que de produire une partie absurde.
/// </summary>
public class MissionCatalogueTests
{
    private static MissionCatalogue Delivered => MissionCatalogue.FromJson(DemoMap.ReadConfig("missions.json"));

    // --- Le fichier livre --------------------------------------------------------------------------

    [Fact]
    public void Le_catalogue_livre_se_charge_et_propose_plusieurs_missions()
    {
        var catalogue = Delivered;

        Assert.True(catalogue.Missions.Count >= 2, "Un choix de mission suppose au moins deux missions.");
        Assert.NotNull(catalogue.Default);
        Assert.All(catalogue.Missions, mission => Assert.False(string.IsNullOrWhiteSpace(mission.Name)));
    }

    /// <summary>
    /// Chaque mission doit etre jouable sur la carte livree : son objectif et son camp existent, et
    /// tous deux offrent une case praticable. Une mission qui vise une zone absente enverrait le
    /// groupe nulle part, et rien dans le jeu ne le dirait.
    /// </summary>
    [Fact]
    public void Chaque_mission_livree_est_jouable_sur_la_carte_livree()
    {
        var map = DemoMap.Load();

        foreach (var mission in Delivered.Missions)
        {
            var objective = mission.ObjectiveIn(map);
            var camp = mission.CampIn(map);

            Assert.True(objective is not null, $"« {mission.Name} » vise une zone absente de la carte.");
            Assert.True(camp is not null, $"« {mission.Name} » n'a pas de camp sur la carte.");

            Assert.True(
                map.FindWalkableIn(objective!).HasValue,
                $"« {mission.Name} » : {objective!.Name} n'offre aucune case praticable.");

            Assert.True(
                map.FindWalkableIn(camp!).HasValue,
                $"« {mission.Name} » : {camp!.Name} n'offre aucune case praticable.");
        }
    }

    /// <summary>
    /// Les agents commencent et finissent la mission au meme endroit. Le camp de chaque mission est
    /// donc aussi la zone de depart, et le briefing la nomme sous les deux vocabulaires que le jeu
    /// emploie — <c>start</c> pour le point de depart, <c>extraction</c> pour la sortie.
    /// </summary>
    [Fact]
    public void Toute_mission_part_du_camp_et_y_revient()
    {
        var map = DemoMap.Load();

        foreach (var mission in Delivered.Missions)
        {
            Assert.Equal(ZoneKind.Start, mission.Camp);
            Assert.NotEqual(mission.Camp, mission.Objective);

            var briefed = mission.BriefedZones().ToList();
            Assert.Contains(ZoneKind.Start, briefed);
            Assert.Contains(ZoneKind.Extraction, briefed);
            Assert.Contains(mission.Objective, briefed);

            // Le depart et la sortie designent bien le meme endroit sur la carte livree.
            Assert.Equal(map.GetZone(ZoneKind.Start)!.Min, map.GetZone(ZoneKind.Extraction)!.Min);
        }
    }

    /// <summary>
    /// La difficulte n'est pas une etiquette : elle se lit dans la taille de la horde. Les deux
    /// doivent rester d'accord, sans quoi le joueur choisit sur une promesse que le jeu ne tient pas.
    /// </summary>
    [Fact]
    public void La_difficulte_annoncee_suit_la_taille_de_la_horde()
    {
        var missions = Delivered.Missions;

        Assert.All(missions, mission => Assert.NotNull(mission.ZombieCount));

        var ordered = missions.OrderBy(mission => mission.ZombieCount).ToList();
        Assert.Equal(missions, ordered);
    }

    // --- Validation ---------------------------------------------------------------------------------

    [Fact]
    public void Une_mission_sans_identifiant_est_refusee()
    {
        Assert.Throws<InvalidOperationException>(
            () => MissionCatalogue.FromJson("""{ "missions": [ { "name": "Sans identifiant" } ] }"""));
    }

    [Fact]
    public void Un_identifiant_en_double_est_refuse()
    {
        Assert.Throws<InvalidOperationException>(() => MissionCatalogue.FromJson("""
            { "missions": [
                { "id": "a", "name": "Une" },
                { "id": "a", "name": "Deux" }
            ] }
            """));
    }

    /// <summary>Chercher l'objectif dans le camp lui-meme ne donne rien a jouer.</summary>
    [Fact]
    public void Une_mission_dont_l_objectif_est_le_camp_est_refusee()
    {
        Assert.Throws<InvalidOperationException>(() => MissionCatalogue.FromJson("""
            { "missions": [
                { "id": "a", "name": "Sur place", "objective": "Start", "camp": "Start" }
            ] }
            """));
    }

    [Fact]
    public void Une_mission_sans_caisse_est_refusee()
    {
        Assert.Throws<InvalidOperationException>(() => MissionCatalogue.FromJson("""
            { "missions": [ { "id": "a", "name": "Vide", "supplies": 0 } ] }
            """));
    }

    // --- Resolution des lieux -------------------------------------------------------------------------

    /// <summary>
    /// La carte compte deux batiments : le type ne suffit plus a designer celui du sud. Le nom
    /// tranche, et retombe sur le type quand il ne correspond a rien — une faute de frappe dans le
    /// fichier ne doit pas rendre une mission injouable.
    /// </summary>
    [Fact]
    public void Le_nom_du_lieu_l_emporte_sur_son_type()
    {
        var map = DemoMap.Load();

        var byName = new MissionDefinition
        {
            Id = "x", Name = "X", Objective = ZoneKind.Building, ObjectiveZone = "Hangar sud",
        };

        var byKind = new MissionDefinition { Id = "y", Name = "Y", Objective = ZoneKind.Building };
        var mistyped = byName with { ObjectiveZone = "Hangar qui n'existe pas" };

        Assert.Equal("Hangar sud", byName.ObjectiveIn(map)!.Name);
        Assert.Equal(map.GetZone(ZoneKind.Building)!.Name, byKind.ObjectiveIn(map)!.Name);
        Assert.Equal(map.GetZone(ZoneKind.Building)!.Name, mistyped.ObjectiveIn(map)!.Name);
    }

    // --- Effet sur la partie -----------------------------------------------------------------------

    /// <summary>
    /// La mission choisie decide de ce que le suivi de partie regarde : ou il faut rapporter la
    /// caisse, et ce qu'est « la caisse ». Une mission differente change donc la condition de
    /// victoire sans qu'une ligne de code ne bouge.
    /// </summary>
    [Fact]
    public void La_mission_choisie_decide_de_la_condition_de_victoire()
    {
        var mission = new MissionRun(
            team: false,
            definition: new MissionDefinition
            {
                Id = "entrepot",
                Name = "Entrepot",
                Objective = ZoneKind.Building,
                ObjectiveZone = "Entrepot ouest",
                Camp = ZoneKind.Start,
            });

        Assert.Equal("entrepot", mission.Tracker.Mission.Id);

        // Rapporter la caisse a l'entrepot ne remplit rien : c'est au camp qu'on l'attend.
        mission.Alice.AddToInventory(ItemKind.Medicine);
        mission.Alice.Position = mission.Map.FindWalkableIn(
            mission.Map.Zones.First(zone => zone.Name == "Entrepot ouest"))!.Value;

        Assert.False(mission.Tracker.Evaluate(mission.World.Time).IsOver);
        Assert.False(mission.Tracker.Done);

        mission.Alice.Position = mission.Spawn(ZoneKind.Start);
        mission.Tracker.Evaluate(mission.World.Time);

        Assert.True(mission.Tracker.Done);
    }
}
