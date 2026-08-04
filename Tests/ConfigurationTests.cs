using Hordinary.Agents;
using Hordinary.Config;
using Hordinary.World;

namespace Hordinary.Tests;

/// <summary>
/// La specification impose que les couts A*, les limites GBFS et les coefficients Utility AI
/// soient modifiables sans toucher au code : ces tests verrouillent le format du fichier.
/// </summary>
public class ConfigurationTests
{
    [Fact]
    public void La_configuration_de_la_specification_est_lue_integralement()
    {
        const string json = """
            {
              "navigation": {
                "known_cell_cost": 1,
                "unknown_cell_cost": 4,
                "suspected_danger_cell_cost": 8,
                "dangerous_cell_cost": 15,
                "closed_door_cost": 3,
                "allow_diagonal": false,
                "max_explored_nodes": 4000
              },
              "perception": {
                "vision_range": 8,
                "field_of_view_degrees": 180,
                "update_interval": 0.15
              },
              "noise": {
                "walk": 1,
                "run": 3,
                "open_door": 2,
                "force_door": 6,
                "break_window": 8,
                "distraction": 10,
                "obstacle_attenuation": 2,
                "lifetime": 6,
                "hearing_threshold": 0.5,
                "localisation_error_factor": 0.35,
                "max_localisation_error": 4
              },
              "zombies": {
                "count": 24,
                "seed": 20260728,
                "minimum_spawn_distance": 9,
                "vision_range": 6,
                "field_of_view_degrees": 140,
                "update_interval": 0.3,
                "wander_speed": 1.1,
                "alert_speed": 1.8,
                "chase_speed": 2.4,
                "separation_weight": 1.5,
                "cohesion_weight": 0.3
              },
              "survivor": {
                "max_health": 100,
                "walk_speed": 4,
                "run_speed": 7,
                "panic_rise_per_second": 0.5,
                "panic_decay_per_second": 0.15,
                "panic_per_wound": 2.5,
                "personality": { "courage": 0.5, "loyalty": 0.7, "caution": 0.8 }
              },
              "utility_ai": {
                "evaluation_interval": 0.75,
                "switch_threshold": 0.15,
                "minimum_score": 0.05,
                "danger_radius": 8,
                "distance_scale": 12,
                "noise_danger_weight": 0.5,
                "loud_noise_intensity": 6,
                "flee_travel_penalty": 0.25,
                "player_order_priority": 0.7
              },
              "planner": { "max_nodes": 1000, "max_depth": 20, "max_time_ms": 50 },
              "knowledge": {
                "zombie_confidence_decay": 0.08,
                "communication_range": 5,
                "ally_confidence_decay": 0.05,
                "door_state_confidence_decay": 0.02,
                "noise_confidence_decay": 0.1,
                "minimum_confidence": 0.05,
                "shared_confidence_penalty": 0.2,
                "prediction_horizon": 3,
                "heading_memory": 2,
                "heading_smoothing": 0.5,
                "maximum_tracked_speed": 3
              }
            }
            """;

        var config = GameConfig.FromJson(json);

        Assert.Equal(1f, config.Navigation.KnownCellCost, 3);
        Assert.Equal(4f, config.Navigation.UnknownCellCost, 3);
        Assert.Equal(8f, config.Navigation.SuspectedDangerCellCost, 3);
        Assert.Equal(15f, config.Navigation.DangerousCellCost, 3);
        Assert.Equal(3f, config.Navigation.ClosedDoorCost, 3);
        Assert.False(config.Navigation.AllowDiagonal);
        Assert.Equal(4000, config.Navigation.MaxExploredNodes);

        Assert.Equal(8, config.Perception.VisionRange);
        Assert.Equal(180f, config.Perception.FieldOfViewDegrees, 3);
        Assert.Equal(0.15f, config.Perception.UpdateInterval, 3);

        // Bareme de la section 18.1, a l'unite pres.
        Assert.Equal(1f, config.Noise.Walk, 3);
        Assert.Equal(3f, config.Noise.Run, 3);
        Assert.Equal(2f, config.Noise.OpenDoor, 3);
        Assert.Equal(6f, config.Noise.ForceDoor, 3);
        Assert.Equal(8f, config.Noise.BreakWindow, 3);
        Assert.Equal(10f, config.Noise.Distraction, 3);
        Assert.Equal(2f, config.Noise.ObstacleAttenuation, 3);
        Assert.Equal(6f, config.Noise.Lifetime, 3);
        Assert.Equal(0.5f, config.Noise.HearingThreshold, 3);
        Assert.Equal(0.35f, config.Noise.LocalisationErrorFactor, 3);
        Assert.Equal(4, config.Noise.MaxLocalisationError);

        Assert.Equal(24, config.Zombies.Count);
        Assert.Equal(20260728, config.Zombies.Seed);
        Assert.Equal(9, config.Zombies.MinimumSpawnDistance);
        Assert.Equal(6, config.Zombies.VisionRange);
        Assert.Equal(140f, config.Zombies.FieldOfViewDegrees, 3);
        Assert.Equal(0.3f, config.Zombies.UpdateInterval, 3);
        Assert.Equal(1.1f, config.Zombies.WanderSpeed, 3);
        Assert.Equal(1.8f, config.Zombies.AlertSpeed, 3);
        Assert.Equal(2.4f, config.Zombies.ChaseSpeed, 3);
        Assert.Equal(1.5f, config.Zombies.SeparationWeight, 3);
        Assert.Equal(0.3f, config.Zombies.CohesionWeight, 3);

        Assert.Equal(100f, config.Survivor.MaxHealth, 3);
        Assert.Equal(4f, config.Survivor.WalkSpeed, 3);
        Assert.Equal(7f, config.Survivor.RunSpeed, 3);
        Assert.Equal(0.5f, config.Survivor.PanicRisePerSecond, 3);
        Assert.Equal(0.15f, config.Survivor.PanicDecayPerSecond, 3);
        Assert.Equal(2.5f, config.Survivor.PanicPerWound, 3);
        Assert.Equal(0.7f, config.Survivor.Personality.Loyalty, 3);
        Assert.Equal(0.8f, config.Survivor.Personality.Caution, 3);

        Assert.Equal(0.75f, config.UtilityAi.EvaluationInterval, 3);
        Assert.Equal(0.15f, config.UtilityAi.SwitchThreshold, 3);
        Assert.Equal(0.05f, config.UtilityAi.MinimumScore, 3);
        Assert.Equal(8f, config.UtilityAi.DangerRadius, 3);
        Assert.Equal(12f, config.UtilityAi.DistanceScale, 3);
        Assert.Equal(0.5f, config.UtilityAi.NoiseDangerWeight, 3);
        Assert.Equal(6f, config.UtilityAi.LoudNoiseIntensity, 3);
        Assert.Equal(0.25f, config.UtilityAi.FleeTravelPenalty, 3);
        Assert.Equal(0.7f, config.UtilityAi.PlayerOrderPriority, 3);
        Assert.Equal(1000, config.Planner.MaxNodes);
        Assert.Equal(20, config.Planner.MaxDepth);
        Assert.Equal(50, config.Planner.MaxTimeMs);

        Assert.Equal(0.08f, config.Knowledge.ZombieConfidenceDecay, 3);
        Assert.Equal(5, config.Knowledge.CommunicationRange);
        Assert.Equal(0.05f, config.Knowledge.AllyConfidenceDecay, 3);
        Assert.Equal(0.02f, config.Knowledge.DoorStateConfidenceDecay, 3);
        Assert.Equal(0.1f, config.Knowledge.NoiseConfidenceDecay, 3);
        Assert.Equal(0.05f, config.Knowledge.MinimumConfidence, 3);
        Assert.Equal(0.2f, config.Knowledge.SharedConfidencePenalty, 3);
        Assert.Equal(3f, config.Knowledge.PredictionHorizon, 3);
        Assert.Equal(2f, config.Knowledge.HeadingMemory, 3);
        Assert.Equal(0.5f, config.Knowledge.HeadingSmoothing, 3);
        Assert.Equal(3f, config.Knowledge.MaximumTrackedSpeed, 3);
    }

    [Fact]
    public void Les_reglages_du_planificateur_sont_lus_integralement()
    {
        // Livrable 5 : les limites GBFS, les poids de l'heuristique enrichie et la construction de
        // l'etat symbolique doivent tous etre modifiables sans recompiler.
        const string json = """
            {
              "planner": {
                "max_nodes": 750,
                "max_depth": 12,
                "max_time_ms": 30,
                "unsatisfied_weight": 2,
                "distance_weight": 0.9,
                "danger_weight": 3,
                "noise_weight": 0.2,
                "distance_scale": 20,
                "noise_scale": 8,
                "danger_threshold": 0.4,
                "door_link_radius": 2,
                "arrival_radius": 0,
                "exploration_fallback_goal": "Explorer"
              },
              "survivor": { "search_radius": 5 }
            }
            """;

        var planner = GameConfig.FromJson(json).Planner;

        Assert.Equal(750, planner.MaxNodes);
        Assert.Equal(12, planner.MaxDepth);
        Assert.Equal(30, planner.MaxTimeMs);
        Assert.Equal(2f, planner.UnsatisfiedWeight, 3);
        Assert.Equal(0.9f, planner.DistanceWeight, 3);
        Assert.Equal(3f, planner.DangerWeight, 3);
        Assert.Equal(0.2f, planner.NoiseWeight, 3);
        Assert.Equal(20f, planner.DistanceScale, 3);
        Assert.Equal(8f, planner.NoiseScale, 3);
        Assert.Equal(0.4f, planner.DangerThreshold, 3);
        Assert.Equal(2, planner.DoorLinkRadius);
        Assert.Equal(0, planner.ArrivalRadius);
        Assert.Equal("Explorer", planner.ExplorationFallbackGoal);

        Assert.Equal(5f, GameConfig.FromJson(json).Survivor.SearchRadius, 3);
    }

    [Fact]
    public void Les_reglages_d_exploration_et_de_partage_sont_lus_integralement()
    {
        // Livrable 7 : les quatre termes du score de frontiere, la portee de communication et la
        // cadence de partage doivent tous etre modifiables sans recompiler. Mettre
        // duplication_weight a zero doit suffire a retrouver le comportement d'avant ce livrable.
        const string json = """
            {
              "exploration": {
                "information_radius": 6,
                "information_weight": 1.2,
                "distance_weight": 0.4,
                "danger_weight": 1.1,
                "duplication_weight": 0,
                "duplication_radius": 12,
                "max_candidates": 40
              },
              "knowledge": {
                "communication_range": 7,
                "sharing_interval": 0.5,
                "radio_enabled": true,
                "intent_memory": 30
              },
              "planner": { "ally_wounded_threshold": 0.45, "ally_panic_threshold": 0.65 },
              "survivor": { "heal_amount": 60, "calm_amount": 0.25 }
            }
            """;

        var config = GameConfig.FromJson(json);

        Assert.Equal(6, config.Exploration.InformationRadius);
        Assert.Equal(1.2f, config.Exploration.InformationWeight, 3);
        Assert.Equal(0.4f, config.Exploration.DistanceWeight, 3);
        Assert.Equal(1.1f, config.Exploration.DangerWeight, 3);
        Assert.Equal(0f, config.Exploration.DuplicationWeight, 3);
        Assert.Equal(12f, config.Exploration.DuplicationRadius, 3);
        Assert.Equal(40, config.Exploration.MaxCandidates);

        Assert.Equal(7, config.Knowledge.CommunicationRange);
        Assert.Equal(0.5f, config.Knowledge.SharingInterval, 3);
        Assert.True(config.Knowledge.RadioEnabled);
        Assert.Equal(30f, config.Knowledge.IntentMemory, 3);

        Assert.Equal(0.45f, config.Planner.AllyWoundedThreshold, 3);
        Assert.Equal(0.65f, config.Planner.AllyPanicThreshold, 3);

        Assert.Equal(60f, config.Survivor.HealAmount, 3);
        Assert.Equal(0.25f, config.Survivor.CalmAmount, 3);
    }

    [Fact]
    public void Un_survivant_se_declare_entierement_en_configuration()
    {
        // Livrable 7 : ajouter un quatrieme survivant, ou lui donner une competence, ne doit jamais
        // demander de recompiler. Rien dans le code ne nomme Alice, Bob ni Claire.
        const string json = """
            {
              "survivors": [
                {
                  "id": "dan",
                  "name": "Dan",
                  "role": "Guetteur",
                  "personality": { "courage": 0.3, "loyalty": 0.6, "caution": 0.9 },
                  "max_health": 90,
                  "walk_speed": 3.5,
                  "vision_range": 14,
                  "noise_multiplier": 0.4,
                  "skills": {
                    "Observe": { "cost": 0.2, "duration": 0.3 },
                    "ForceDoor": { "enabled": false }
                  },
                  "aptitudes": { "exploration": 0.8 }
                }
              ]
            }
            """;

        var profile = SurvivorRoster.FromJson(json).Find("dan");

        Assert.NotNull(profile);
        Assert.Equal("Guetteur", profile.Role);
        Assert.Equal(0.9f, profile.Personality.Caution, 3);
        Assert.Equal(0.4f, profile.NoiseMultiplier, 3);
        Assert.Equal(0.8f, profile.AptitudeFor("exploration"), 3);
        Assert.False(profile.SkillFor("ForceDoor").Enabled);

        // Les caracteristiques absentes retombent sur la ligne de base, elles ne valent pas zero.
        var baseline = new SurvivorSettings { MaxHealth = 100f, WalkSpeed = 4f, RunSpeed = 7f };
        var effective = profile.ApplyTo(baseline);

        Assert.Equal(90f, effective.MaxHealth, 3);
        Assert.Equal(3.5f, effective.WalkSpeed, 3);
        Assert.Equal(7f, effective.RunSpeed, 3);
        Assert.Equal(14, profile.ApplyTo(new PerceptionSettings { VisionRange = 8 }).VisionRange);
    }

    [Fact]
    public void Un_roster_incoherent_est_refuse_au_chargement()
    {
        var duplicate = Assert.Throws<InvalidOperationException>(() => SurvivorRoster.FromJson(
            """{ "survivors": [ { "id": "alice", "name": "Alice" }, { "id": "alice", "name": "Alix" } ] }"""));

        Assert.Contains("deux fois", duplicate.Message, StringComparison.Ordinal);

        var aptitude = Assert.Throws<InvalidOperationException>(() => SurvivorRoster.FromJson(
            """{ "survivors": [ { "id": "bob", "name": "Bob", "aptitudes": { "breaching": 4 } } ] }"""));

        Assert.Contains("entre 0 et 1", aptitude.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Une_section_absente_retombe_sur_les_valeurs_par_defaut()
    {
        var config = GameConfig.FromJson("""{ "navigation": { "unknown_cell_cost": 9 } }""");

        Assert.Equal(9f, config.Navigation.UnknownCellCost, 3);
        Assert.Equal(1f, config.Navigation.KnownCellCost, 3);
        Assert.Equal(20, config.Planner.MaxDepth);
    }

    [Fact]
    public void La_legende_associe_les_tuiles_de_l_atlas_aux_types_de_cellules()
    {
        const string json = """
            {
              "tile_size": 16,
              "tiles": {
                "0:5,0": "Wall",
                "0:6,0": "FloorIndoor",
                "1:0,0": "DoorClosed",
                "2:0,0": "DoorOpen"
              }
            }
            """;

        var legend = TileLegend.FromJson(json);

        Assert.Equal(16, legend.TileSize);
        Assert.True(legend.TryGetKind(0, 5, 0, out var wall));
        Assert.Equal(CellType.Wall, TileLegend.ToCellType(wall));

        Assert.True(legend.TryGetKind(0, 6, 0, out var floor));
        Assert.Equal(CellType.Floor, TileLegend.ToCellType(floor));
        Assert.True(TileLegend.ToIndoorFlag(floor));

        // Les deux portes partagent la coordonnee d'atlas 0,0 : seule la source les distingue.
        Assert.True(legend.TryGetKind(1, 0, 0, out var closed));
        Assert.Equal(CellType.DoorClosed, TileLegend.ToCellType(closed));

        Assert.True(legend.TryGetKind(2, 0, 0, out var open));
        Assert.Equal(CellType.DoorOpen, TileLegend.ToCellType(open));

        Assert.False(legend.TryGetKind(0, 9, 9, out _));
    }

    [Fact]
    public void Les_zones_declarees_marquent_la_pharmacie_et_l_extraction()
    {
        var map = TestMaps.Parse(
            ".....",
            "..#..",
            ".....");

        const string json = """
            {
              "stamp_cell_types": true,
              "zones": [
                { "name": "Pharmacie", "kind": "Pharmacy", "min": [0, 0], "max": [2, 1] },
                { "name": "Extraction", "kind": "Extraction", "min": [4, 2], "max": [4, 2] }
              ]
            }
            """;

        MapLocationsConfig.FromJson(json).ApplyTo(map);

        Assert.Equal(CellType.Pharmacy, map.GetCell(new GridPosition(0, 0)));
        Assert.Equal(CellType.Extraction, map.GetCell(new GridPosition(4, 2)));

        // Le marquage ne doit pas transformer un mur en sol praticable.
        Assert.Equal(CellType.Wall, map.GetCell(new GridPosition(2, 1)));
        Assert.False(map.IsWalkable(new GridPosition(2, 1)));

        var pharmacy = map.GetZone(ZoneKind.Pharmacy);
        Assert.NotNull(pharmacy);
        Assert.Equal("Pharmacie", pharmacy.Name);
        Assert.True(pharmacy.Contains(new GridPosition(1, 1)));
        Assert.False(pharmacy.Contains(new GridPosition(3, 0)));
    }

    [Fact]
    public void La_pharmacie_et_l_extraction_restent_praticables()
    {
        var map = TestMaps.Parse("..");
        map.SetCell(new GridPosition(0, 0), CellType.Pharmacy);
        map.SetCell(new GridPosition(1, 0), CellType.Extraction);

        Assert.True(map.IsWalkable(new GridPosition(0, 0)));
        Assert.True(map.IsWalkable(new GridPosition(1, 0)));
    }
}
