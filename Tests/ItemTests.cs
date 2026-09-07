using Hordinary.Agents;
using Hordinary.Config;
using Hordinary.Game;
using Hordinary.World;

namespace Hordinary.Tests;

/// <summary>
/// Objets, armes, inventaire et objets lourds (livrable 11).
///
/// Ce qui est verifie ici n'est jamais « l'objet X fait Y » — le code ne nomme aucun objet — mais que
/// la <b>donnee</b> et le <b>code</b> se repondent : chaque identifiant livre a une tuile, chaque
/// tuile d'objet designe un identifiant livre, un objet equipe pondere l'action qu'il dit ponderer et
/// pas une autre, et les limites du sac sont bien celles de la configuration.
/// </summary>
public class ItemTests
{
    private static ItemCatalogue Catalogue => DemoMap.Items;

    private static TileLegend Legend { get; } = TileLegend.FromJson(DemoMap.ReadConfig("tile_legend.json"));

    private static Survivor Carrier(string id = "bob") => MissionRun.Lone(id, new GridPosition(0, 0));

    // --- Coherence entre les catalogues et la legende de tuiles -------------------------------------

    /// <summary>
    /// « Chaque <c>id</c> des catalogues d'objets et d'armes possede une tuile declaree ». Un objet
    /// sans tuile ne peut pas etre pose sur la carte : il existerait en configuration et nulle part
    /// ailleurs, ce qui est la pire des deux moities.
    /// </summary>
    [Fact]
    public void Chaque_objet_livre_a_une_tuile_declaree()
    {
        var tiled = Legend.DeclaredItems.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in Catalogue.Items)
        {
            Assert.True(
                tiled.Contains(item.Id),
                $"« {item.Id} » n'a aucune tuile dans config/tile_legend.json : on ne pourrait pas le poser.");
        }
    }

    /// <summary>
    /// Et reciproquement : aucune tuile d'objet ne pointe vers un identifiant inexistant. Une telle
    /// tuile poserait au sol quelque chose que personne ne peut ramasser.
    /// </summary>
    [Fact]
    public void Aucune_tuile_d_objet_ne_designe_un_identifiant_inconnu()
    {
        foreach (string id in Legend.DeclaredItems)
        {
            Assert.True(
                Catalogue.Contains(id),
                $"Une tuile designe « {id} », qui n'existe dans aucun catalogue d'objets.");
        }
    }

    /// <summary>
    /// <c>CellType.Obstacle</c> a au moins une tuile. Sans elle, le type existerait dans le code sans
    /// pouvoir etre peint — et les objets lourds n'auraient aucune apparence.
    /// </summary>
    [Fact]
    public void Le_decor_bloquant_est_peignable()
    {
        var obstacles = Legend.Tiles.Values
            .Where(tile => TileLegend.ToCellType(tile.Kind) == CellType.Obstacle)
            .ToList();

        Assert.NotEmpty(obstacles);
    }

    // --- Vignettes d'inventaire --------------------------------------------------------------------

    /// <summary>
    /// Chaque objet livre a une vignette d'inventaire, et cette vignette <b>est</b> sa tuile. La
    /// direction artistique prevoyait quinze dessins de plus, en 48 x 48 et de trois quarts ; ils
    /// n'existent pas et n'existeront pas, et aucun inventaire ne doit dependre de leur arrivee.
    /// </summary>
    [Fact]
    public void Chaque_objet_livre_a_une_vignette()
    {
        foreach (var item in Catalogue.Items)
        {
            Assert.True(
                Legend.TileOf(item.Id) is not null,
                $"« {item.Id} » n'a aucune tuile a montrer dans un emplacement d'inventaire.");
        }
    }

    /// <summary>Un identifiant inconnu n'a pas de vignette — il ne fait pas echouer l'appelant.</summary>
    [Fact]
    public void Un_objet_inconnu_n_a_pas_de_vignette()
    {
        Assert.Null(Legend.TileOf("lance_roquettes"));
        Assert.Null(Legend.TileOf(null));
        Assert.Null(Legend.TileOf("   "));
    }

    /// <summary>
    /// Quand un objet apparait sur plusieurs tuiles, la vignette prend celle qui ne pose que lui.
    /// <c>medikit</c> en a deux : la tuile heritee du MVP, qui peint aussi un sol, et la sienne. Une
    /// vignette d'inventaire montre l'objet, pas le carrelage sur lequel il trainait.
    /// </summary>
    [Fact]
    public void La_vignette_prefere_la_tuile_qui_ne_pose_que_l_objet()
    {
        var candidates = Legend.Tiles
            .Where(pair => string.Equals(pair.Value.Item, "medikit", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(candidates.Count > 1, "Le cas teste a disparu : medikit n'a plus qu'une seule tuile.");
        Assert.Contains(candidates, pair => pair.Value.Kind != TileKind.Ignored);

        var address = Legend.TileOf("medikit");
        Assert.NotNull(address);

        Assert.True(
            Legend.TryGetTile(address!.Value.Source, address.Value.X, address.Value.Y, out var tile)
                && tile.Kind == TileKind.Ignored,
            "La vignette de medikit montre le sol sur lequel il traine plutot que l'objet seul.");
    }

    /// <summary>Une cle de legende se relit exactement comme elle s'ecrit, dans les deux sens.</summary>
    [Fact]
    public void Une_cle_de_legende_se_relit_dans_les_deux_sens()
    {
        Assert.True(TileLegend.TryParseKey(TileLegend.Key(2, 13, 7), out var address));
        Assert.Equal(new TileAddress(2, 13, 7), address);

        foreach (string key in Legend.Tiles.Keys)
        {
            Assert.True(TileLegend.TryParseKey(key, out var parsed), $"La cle « {key} » ne se relit pas.");
            Assert.Equal(key, parsed.ToString());
        }
    }

    /// <summary>Une cle malformee est refusee plutot que devinee : deviner donnerait la mauvaise tuile.</summary>
    [Theory]
    [InlineData("0:3")]
    [InlineData("3,4")]
    [InlineData(":3,4")]
    [InlineData("0:,4")]
    [InlineData("0:3,")]
    [InlineData("a:3,4")]
    public void Une_cle_malformee_n_est_pas_devinee(string key)
        => Assert.False(TileLegend.TryParseKey(key, out _));

    /// <summary>
    /// Chacune des quatre reserves du camp a un objet qui la remplit, et donc une vignette a montrer
    /// en face de son compteur. Aucun code ne nomme cet objet : c'est le catalogue qui dit lequel
    /// nourrit, et l'ecran de preparation ne fait que le suivre.
    /// </summary>
    [Fact]
    public void Chaque_reserve_du_camp_a_une_vignette()
    {
        foreach (string resource in CampStock.Resources)
        {
            var stocker = CampStock.StockerOf(resource, Catalogue);

            Assert.True(stocker is not null, $"Aucun objet livre ne remplit la reserve « {resource} ».");

            Assert.True(
                Legend.TileOf(stocker!.Id) is not null,
                $"L'objet « {stocker.Id} », qui remplit « {resource} », n'a aucune vignette.");
        }
    }

    // --- Les catalogues livres ---------------------------------------------------------------------

    [Fact]
    public void Les_dix_objets_et_les_cinq_armes_sont_livres()
    {
        Assert.Equal(5, Catalogue.OfCategory(ItemCategory.Weapon).Count());
        Assert.Equal(15, Catalogue.Items.Count);
    }

    /// <summary>
    /// Le bruit des armes reste sur le bareme de la specification : marche 1, porte 2, course 3,
    /// porte forcee 6, vitre brisee 8, distraction 10. Une arme au-dela de 10 introduirait une echelle
    /// de bruit parallele, et la perception auditive du livrable 3 cesserait d'etre comparable.
    /// </summary>
    [Fact]
    public void Aucune_arme_ne_sort_du_bareme_de_bruit()
    {
        foreach (var weapon in Catalogue.OfCategory(ItemCategory.Weapon))
        {
            Assert.InRange(weapon.NoiseLevel, 0f, 10f);
        }
    }

    // --- L'inventaire ------------------------------------------------------------------------------

    /// <summary>« Un inventaire plein refuse un ramassage supplementaire. »</summary>
    [Fact]
    public void Un_inventaire_plein_refuse_un_ramassage()
    {
        var survivor = Carrier();
        var sac = survivor.Inventory;

        // Des objets non empilables, pour occuper un emplacement chacun. Un empilable remplirait une
        // seule pile et ne dirait rien des emplacements.
        var distinct = Catalogue.Items
            .Where(item => !item.Stackable)
            .Select(item => item.Id)
            .ToList();

        Assert.True(distinct.Count > sac.Settings.Slots, "Pas assez d'objets distincts pour remplir le sac.");

        for (int i = 0; i < sac.Settings.Slots; i++)
        {
            Assert.True(sac.Add(distinct[i]), $"Le sac a refuse « {distinct[i]} » avant d'etre plein.");
        }

        Assert.True(sac.IsFull);
        Assert.False(sac.Add(distinct[sac.Settings.Slots]), "Un sac plein a quand meme accepte un objet.");
    }

    /// <summary>
    /// Le poids, lui, n'interdit rien : il ralentit. Un survivant peut choisir de rentrer charge —
    /// c'est un arbitrage, pas une barriere.
    /// </summary>
    [Fact]
    public void La_surcharge_ralentit_sans_interdire()
    {
        var survivor = Carrier();
        var sac = survivor.Inventory;

        var lourd = Catalogue.Items.OrderByDescending(item => item.Weight).First();

        Assert.Equal(1f, sac.SpeedMultiplier, 3);

        while (!sac.IsOverweight && !sac.IsFull) Assert.True(sac.Add(lourd.Id));

        Assert.True(sac.IsOverweight, $"Meme plein de « {lourd.Id} », le sac ne pese pas assez pour surcharger.");
        Assert.Equal(sac.Settings.OverweightSpeedPenalty, sac.SpeedMultiplier, 3);
    }

    /// <summary>« Une arme a durabilite nulle ne peut plus attaquer. »</summary>
    [Fact]
    public void Une_arme_usee_jusqu_au_bout_ne_frappe_plus()
    {
        var sac = Carrier().Inventory;

        var melee = Catalogue.OfCategory(ItemCategory.Weapon)
            .First(weapon => !weapon.IsFirearm && weapon.Durability > 0);

        Assert.True(sac.Add(melee.Id));
        Assert.True(sac.EquipWeapon(melee.Id));
        Assert.True(sac.CanAttackWith(melee.Id));

        for (int i = 0; i < melee.Durability; i++) sac.RegisterStrike(melee.Id);

        Assert.Equal(0, sac.DurabilityOf(melee.Id));
        Assert.True(sac.IsBroken(melee.Id));
        Assert.False(sac.CanAttackWith(melee.Id));

        // Elle reste dans le sac : c'est une information a ramener au camp, pas un objet qui disparait.
        Assert.True(sac.Carries(melee.Id));
    }

    /// <summary>« Une arme a feu sans munitions retombe sur ses valeurs de corps a corps. »</summary>
    [Fact]
    public void Une_arme_a_feu_vide_redevient_une_massue()
    {
        var sac = Carrier().Inventory;

        var firearm = Catalogue.OfCategory(ItemCategory.Weapon).First(weapon => weapon.IsFirearm);

        Assert.True(sac.Add(firearm.Id));
        Assert.True(sac.EquipWeapon(firearm.Id));

        var chargee = sac.StrikeWith(firearm.Id);
        Assert.Equal(firearm.Damage, chargee.Damage, 3);
        Assert.Equal(firearm.Range, sac.RangeOf(firearm.Id));

        for (int i = 0; i < firearm.Ammo!.Value; i++) sac.RegisterStrike(firearm.Id);

        var vide = sac.StrikeWith(firearm.Id);

        Assert.Equal(0, sac.AmmoOf(firearm.Id));
        Assert.Equal(firearm.UnloadedDamage, vide.Damage, 3);
        Assert.Equal(firearm.UnloadedNoise, vide.Noise, 3);
        Assert.Equal(1, sac.RangeOf(firearm.Id));

        // Elle reste utilisable, simplement mauvaise : c'est ce qui rend ammo_box interessante sans la
        // rendre indispensable.
        Assert.True(sac.CanAttackWith(firearm.Id));
    }

    /// <summary>Recharger la remet en etat de tirer, et jamais au-dela de son chargeur.</summary>
    [Fact]
    public void Recharger_ne_depasse_pas_le_chargeur()
    {
        var sac = Carrier().Inventory;
        var firearm = Catalogue.OfCategory(ItemCategory.Weapon).First(weapon => weapon.IsFirearm);

        Assert.True(sac.Add(firearm.Id));
        sac.Reload(firearm.Id, 100);

        Assert.Equal(firearm.Ammo!.Value, sac.AmmoOf(firearm.Id));
    }

    /// <summary>Une arme retiree du sac cesse d'etre en main : on ne degaine pas ce qu'on n'a plus.</summary>
    [Fact]
    public void Une_arme_rendue_cesse_d_etre_equipee()
    {
        var sac = Carrier().Inventory;
        var weapon = Catalogue.OfCategory(ItemCategory.Weapon).First();

        Assert.True(sac.Add(weapon.Id));
        Assert.True(sac.EquipWeapon(weapon.Id));
        Assert.True(sac.Remove(weapon.Id));

        Assert.Null(sac.EquippedWeapon);
    }

    // --- Ce qu'un objet equipe change pour le planificateur ----------------------------------------

    /// <summary>
    /// « Un objet equipe modifie le cout de l'action attendue, et lui seule. »
    ///
    /// C'est le test qui garantit qu'aucun code ne teste ce qu'un survivant porte : l'outil ne fait
    /// que reecrire le prix d'une action dans le vocabulaire des competences, et GBFS decouvre le
    /// nouveau plan tout seul.
    /// </summary>
    [Fact]
    public void Un_outil_equipe_ne_pondere_que_l_action_qu_il_nomme()
    {
        var outil = Catalogue.Items.First(item => item.ActionModifiers.Count > 0);
        var vises = outil.ActionModifiers;

        var run = new MissionRun(team: false);
        var agent = run.Agent;

        var avant = agent.Domain.Actions.ToDictionary(action => action.Name, action => action.Cost, StringComparer.OrdinalIgnoreCase);

        Assert.True(agent.Survivor.AddToInventory(outil.Id));
        Assert.True(
            agent.Survivor.Inventory.EquipTool(outil.Id) || agent.Survivor.Inventory.EquipWeapon(outil.Id),
            $"« {outil.Id} » n'a pu etre equipe ni comme outil ni comme arme.");

        foreach (var action in agent.Domain.Actions)
        {
            float before = avant[action.Name];

            if (vises.TryGetValue(action.Name, out var modifier))
            {
                Assert.Equal(before * modifier.Cost, action.Cost, 3);
            }
            else
            {
                Assert.Equal(before, action.Cost, 3);
            }
        }

        // Et la ponderation doit bien avoir change quelque chose, sans quoi le test passerait a vide.
        Assert.Contains(vises, pair => Math.Abs(pair.Value.Cost - 1f) > 0.001f);
    }

    // --- Objets lourds ------------------------------------------------------------------------------

    /// <summary>
    /// « Un objet lourd de <c>mass > 3</c> n'est pas deplacable par un survivant seul. » C'est la
    /// seule regle du jeu qui exige d'etre deux au meme endroit, et c'est ce qui donne au regroupement
    /// une raison mecanique plutot qu'une consigne.
    /// </summary>
    [Fact]
    public void Un_meuble_trop_lourd_demande_deux_paires_de_bras()
    {
        var grid = Plain();
        var objets = new HeavyObjects();

        var benne = objets.Add(new HeavyObject("benne", "dumpster", new GridPosition(5, 5), mass: 5f));

        var pusher = new GridPosition(4, 5);

        Assert.NotNull(objets.WhyCannotPush(benne, pusher, grid));
        Assert.Null(objets.Push(benne, pusher, grid));
        Assert.Equal(new GridPosition(5, 5), benne.Position);

        // Un allie adjacent, et elle bouge.
        Assert.Null(objets.WhyCannotPush(benne, pusher, grid, [new GridPosition(5, 4)]));
        Assert.Equal(new GridPosition(6, 5), objets.Push(benne, pusher, grid, [new GridPosition(5, 4)]));
    }

    /// <summary>Un rayonnage sous le seuil part tout seul, dans la direction opposee au pousseur.</summary>
    [Fact]
    public void Un_meuble_leger_se_pousse_seul_et_en_ligne_droite()
    {
        var grid = Plain();
        var objets = new HeavyObjects();

        var etagere = objets.Add(new HeavyObject("etagere", "shelf", new GridPosition(5, 5), mass: 3f));

        Assert.Equal(new GridPosition(5, 6), objets.Push(etagere, new GridPosition(5, 4), grid));

        // En diagonale, jamais : il faudrait decider laquelle des deux composantes l'emporte.
        Assert.NotNull(objets.WhyCannotPush(etagere, new GridPosition(4, 5), grid));
    }

    /// <summary>
    /// Une epave de voiture occupe quatre cases, et les quatre comptent. Elle se contourne sur toute
    /// son emprise, elle se pousse d'un bloc, et il suffit qu'une seule case d'arrivee soit prise
    /// pour qu'elle ne bouge pas — sans quoi une voiture ne serait qu'un grand dessin pose sur une
    /// case.
    /// </summary>
    [Fact]
    public void Une_epave_de_voiture_occupe_ses_quatre_cases()
    {
        var grid = Plain();
        var objets = new HeavyObjects();

        var epave = objets.Add(new HeavyObject("epave", "wreck", new GridPosition(5, 5), mass: 5f)
        {
            Width = 2,
            Height = 2,
        });

        Assert.Equal(4, epave.Cells.Count());

        // On la trouve depuis n'importe laquelle de ses cases, pas seulement depuis son ancre.
        Assert.Same(epave, objets.At(new GridPosition(6, 6)));
        Assert.True(objets.BlocksMovementAt(new GridPosition(6, 5)));
        Assert.Null(objets.At(new GridPosition(7, 5)));

        // Poussee par le flanc, elle avance en bloc : l'ancre suit, et l'empreinte avec.
        var helpers = new[] { new GridPosition(5, 4) };
        Assert.Equal(new GridPosition(5, 6), objets.Push(epave, new GridPosition(6, 4), grid, helpers));
        Assert.Equal(new GridPosition(6, 7), epave.Cells.Last());

        // Un mur devant une seule des deux cases d'arrivee suffit a la bloquer.
        grid.SetCell(new GridPosition(6, 8), CellType.Wall);
        Assert.NotNull(objets.WhyCannotPush(epave, new GridPosition(6, 5), grid, helpers));
    }

    /// <summary>
    /// « Un objet lourd deplace n'est connu que des survivants qui ont vu la scene. »
    ///
    /// La contrainte de perception partielle du livrable 2 vaut aussi pour le decor : un survivant qui
    /// revient sur une case qu'il croit degagee peut se heurter a un meuble qu'un allie a pousse. Le
    /// pousseur, lui, en fait partie de droit — il a les mains dessus.
    /// </summary>
    [Fact]
    public void Un_meuble_pousse_n_est_connu_que_de_ceux_qui_ont_vu()
    {
        var mission = new MissionRun(team: true);

        var alice = mission.AgentOf("alice");
        var bob = mission.AgentOf("bob");

        // Une case libre a cote d'Alice, et une etagere dessus.
        var target = mission.Map.AllPositions().First(cell =>
            cell.ChebyshevDistanceTo(alice.Survivor.Position) == 1
            && mission.Map.IsWalkable(cell)
            && mission.Map.IsWalkable(Beyond(alice.Survivor.Position, cell)));

        var landing = Beyond(alice.Survivor.Position, target);

        mission.World.Heavy.Add(new HeavyObject("etagere", "shelf", target, mass: 2f));
        mission.Map.SetCell(target, CellType.Obstacle);

        // Bob part du camp comme Alice : il a donc deja vu la case ou l'etagere va atterrir, et la
        // croit degagee. C'est cette croyance-la qui doit survivre a la poussee.
        mission.Band.Perceive();

        var believed = bob.Survivor.Knowledge.MentalMap.GetCell(landing);
        Assert.True(believed.IsKnownWalkable(), $"Bob ne connait pas encore {landing} : le test ne prouverait rien.");

        // Puis il s'eloigne, hors de portee de vue.
        bob.Survivor.Position = mission.Map.AllPositions().Last(cell =>
            mission.Map.IsWalkable(cell)
            && cell.ChebyshevDistanceTo(target) > bob.Survivor.VisionRange);

        Assert.True(alice.Runtime.Push("etagere"), "Alice n'a pas pu pousser l'etagere.");

        Assert.Equal(CellType.Obstacle, alice.Survivor.Knowledge.MentalMap.GetCell(landing));

        // Bob, lui, continue de croire la case degagee : il s'y heurtera.
        Assert.Equal(believed, bob.Survivor.Knowledge.MentalMap.GetCell(landing));

        static GridPosition Beyond(GridPosition from, GridPosition through)
            => new(through.X + (through.X - from.X), through.Y + (through.Y - from.Y));
    }

    /// <summary>Le bruit et la duree d'une poussee suivent la masse : pousser une benne s'entend.</summary>
    [Fact]
    public void L_effort_d_une_poussee_suit_la_masse()
    {
        var legere = new HeavyObject("a", "shelf", new GridPosition(0, 0), mass: 1f);
        var lourde = new HeavyObject("b", "dumpster", new GridPosition(0, 0), mass: 5f);

        Assert.True(lourde.NoiseOfPush > legere.NoiseOfPush);
        Assert.True(lourde.DurationOfPush > legere.DurationOfPush);
    }

    /// <summary>Une carte carree entierement praticable : le decor n'est pas le sujet de ces tests.</summary>
    private static GridMap Plain(int size = 12)
    {
        return new GridMap(size, size, fill: CellType.Floor);
    }
}
