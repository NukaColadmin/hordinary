using Hordinary.Agents;
using Hordinary.Game;
using Hordinary.World;

namespace Hordinary.Tests;

/// <summary>
/// Le paquetage : ce que le joueur confie a chacun avant le depart, pris dans l'entrepot du camp.
///
/// Ce qui est verifie ici n'est jamais « la trousse fait X » — le coeur ne nomme aucun objet — mais
/// que le pret tienne ses deux bouts : rien ne se cree entre l'etagere et l'epaule, rien ne se perd
/// entre le van et l'entrepot. Les fichiers lus sont ceux qui sont livres : un objet retire du
/// catalogue ou du materiel de depart doit faire echouer ce qui s'en sert.
/// </summary>
public class CampLoadoutTests
{
    private static ItemCatalogue Catalogue => DemoMap.Items;

    private static CampSettings Settings { get; } = CampSettings.FromJson(DemoMap.ReadConfig("camp.json"));

    /// <summary>Un camp dont l'entrepot contient exactement ce que le test demande.</summary>
    private static CampStock Camp(params (string Item, int Quantity)[] storage)
        => new(Settings with
        {
            Storage = [.. storage.Select(line => new ItemRequirement(line.Item, line.Quantity))],
        });

    private static string FirstWeapon
        => Catalogue.Items.First(item => item.IsWeapon).Id;

    private static string FirstConsumable
        => Catalogue.Items.First(item => item.Category == ItemCategory.Consumable && !item.IsWeapon).Id;

    // --- Le materiel livre --------------------------------------------------------------------------

    /// <summary>
    /// Le materiel de depart de <c>config/camp.json</c> ne nomme que des objets livres. Un
    /// identifiant fautif ne se verrait autrement qu'a l'ecran de camp, sous la forme d'une etagere
    /// plus courte que le fichier ne l'annonce.
    /// </summary>
    [Fact]
    public void Le_materiel_de_depart_du_camp_ne_nomme_que_des_objets_livres()
    {
        foreach (var line in Settings.Storage)
        {
            Assert.True(
                Catalogue.Find(line.Item) is not null,
                $"« {line.Item} » est dans le materiel de depart de config/camp.json "
              + "et dans aucun des deux catalogues d'objets.");
        }
    }

    /// <summary>
    /// L'entrepot ouvre avec ce que le fichier annonce : sans cela, il n'existerait qu'apres une
    /// premiere victoire et le paquetage n'aurait rien a repartir le premier jour.
    /// </summary>
    [Fact]
    public void L_entrepot_ouvre_avec_le_materiel_declare()
    {
        var camp = new CampStock(Settings);

        int declared = Settings.Storage.Sum(line => line.Quantity);

        Assert.Equal(Math.Min(declared, Settings.StorageSlots), camp.Stored.Count);
    }

    // --- Composer un paquetage ----------------------------------------------------------------------

    /// <summary>
    /// Composer un paquetage ne coute rien : c'est le depart qui debite, comme le cout d'entree d'une
    /// offre. Sans cela, changer d'avis viderait l'entrepot.
    /// </summary>
    [Fact]
    public void Composer_un_paquetage_ne_debite_pas_l_entrepot()
    {
        string item = FirstConsumable;
        var camp = Camp((item, 2));
        var loadout = new CampLoadout(camp, Catalogue);

        Assert.True(loadout.Give("alice", item));

        Assert.Equal(2, camp.CountOf(item));
        Assert.Equal(1, loadout.Available(item));

        var alice = MissionRun.Lone("alice", new GridPosition(0, 0));
        loadout.HandOver(alice);

        Assert.Equal(1, camp.CountOf(item));
        Assert.True(alice.Carries(item));
    }

    /// <summary>
    /// L'entrepot ne promet pas deux fois le meme exemplaire. Un camp qui n'a qu'une trousse ne peut
    /// pas l'avoir confiee a deux survivants — le second bouton est grise avant d'etre clique.
    /// </summary>
    [Fact]
    public void L_entrepot_ne_promet_pas_deux_fois_le_meme_exemplaire()
    {
        string item = FirstConsumable;
        var camp = Camp((item, 1));
        var loadout = new CampLoadout(camp, Catalogue);

        Assert.True(loadout.Give("alice", item));

        Assert.False(loadout.CanGive("bob", item));
        Assert.False(loadout.Give("bob", item));
        Assert.Empty(loadout.Of("bob"));
    }

    /// <summary>
    /// Le paquetage se compose sous les regles du sac, et non sous des regles a lui : un sac d'un
    /// seul emplacement ne prend pas deux objets differents.
    /// </summary>
    [Fact]
    public void Un_paquetage_ne_depasse_pas_les_emplacements_du_sac()
    {
        string first = FirstConsumable;
        string second = FirstWeapon;

        var camp = Camp((first, 1), (second, 1));
        var loadout = new CampLoadout(camp, Catalogue, new InventorySettings { Slots = 1 });

        Assert.True(loadout.Give("alice", first));

        Assert.False(loadout.CanGive("alice", second));
        Assert.False(loadout.Give("alice", second));

        // Et l'exemplaire refuse reste disponible pour quelqu'un d'autre.
        Assert.True(loadout.CanGive("bob", second));
    }

    /// <summary>
    /// Retirer un survivant de l'equipe rend ce qu'on lui avait confie. Sans cela, il garderait
    /// l'unique trousse du camp en otage : promise a quelqu'un qui reste, introuvable pour ceux qui
    /// partent.
    /// </summary>
    [Fact]
    public void Desenroler_un_survivant_rend_ce_qu_on_lui_avait_confie()
    {
        string item = FirstConsumable;
        var camp = Camp((item, 1));
        var loadout = new CampLoadout(camp, Catalogue);

        loadout.Give("alice", item);
        Assert.Equal(0, loadout.Available(item));

        loadout.Clear("alice");

        Assert.Equal(1, loadout.Available(item));
        Assert.True(loadout.CanGive("bob", item));
    }

    // --- Le depart et le retour ---------------------------------------------------------------------

    /// <summary>
    /// Une arme confiee part <b>en main</b>. Le joueur a decide qui porte le fusil ; la lui laisser
    /// au fond du sac reviendrait a ne pas ecouter la decision.
    /// </summary>
    [Fact]
    public void Une_arme_confiee_part_en_main()
    {
        string weapon = FirstWeapon;
        var camp = Camp((weapon, 1));
        var loadout = new CampLoadout(camp, Catalogue);
        var alice = MissionRun.Lone("alice", new GridPosition(0, 0));

        loadout.Give("alice", weapon);
        loadout.HandOver(alice);

        Assert.Equal(weapon, alice.Inventory.EquippedWeapon);
    }

    /// <summary>
    /// Le materiel prete revient avec celui qui rentre, gagnee ou perdue : preter n'est pas donner,
    /// et une mission ratee coute deja l'essence et le temps.
    /// </summary>
    [Fact]
    public void Le_materiel_prete_revient_avec_celui_qui_rentre()
    {
        string weapon = FirstWeapon;
        var camp = Camp((weapon, 1));
        var loadout = new CampLoadout(camp, Catalogue);
        var alice = MissionRun.Lone("alice", new GridPosition(0, 0));

        loadout.Give("alice", weapon);
        loadout.HandOver(alice);

        Assert.Equal(0, camp.CountOf(weapon));

        var recovered = loadout.Recover(alice);

        Assert.Equal([weapon], recovered);
        Assert.Equal(1, camp.CountOf(weapon));
        Assert.False(alice.Carries(weapon));
    }

    /// <summary>
    /// Ce qui a ete consomme ou perdu en mission ne revient pas : le pret se rend, il ne se
    /// reconstitue pas.
    /// </summary>
    [Fact]
    public void Ce_qui_a_ete_perdu_en_mission_ne_revient_pas()
    {
        string item = FirstConsumable;
        var camp = Camp((item, 1));
        var loadout = new CampLoadout(camp, Catalogue);
        var alice = MissionRun.Lone("alice", new GridPosition(0, 0));

        loadout.Give("alice", item);
        loadout.HandOver(alice);

        alice.RemoveFromInventory(item);

        Assert.Empty(loadout.Recover(alice));
        Assert.Equal(0, camp.CountOf(item));
    }

    /// <summary>
    /// Ce qu'un survivant rapporte et qui n'alimente aucune reserve va a l'entrepot. Avant le
    /// paquetage, cela n'allait nulle part : le survivant est recree a chaque mission, et une arme
    /// disparaissait avec l'objet qui la portait.
    /// </summary>
    [Fact]
    public void Ce_qu_un_survivant_rapporte_et_que_le_camp_ne_boit_pas_va_a_l_entrepot()
    {
        string weapon = FirstWeapon;
        var camp = Camp();
        var alice = MissionRun.Lone("alice", new GridPosition(0, 0));

        alice.AddToInventory(weapon);

        Assert.Empty(camp.Deposit(alice, Catalogue));

        Assert.Equal(1, camp.CountOf(weapon));
        Assert.False(alice.Carries(weapon));
    }

    /// <summary>
    /// Un entrepot plein ne fait pas disparaitre ce qu'il ne peut pas prendre : il le dit, et l'objet
    /// reste dans le sac. La file d'attente, elle, est reservee aux <b>recompenses</b> annoncees.
    /// </summary>
    [Fact]
    public void Un_entrepot_plein_dit_ce_qu_il_ne_peut_pas_prendre()
    {
        string weapon = FirstWeapon;
        var camp = new CampStock(Settings with { StorageSlots = 0, Storage = [] });
        var alice = MissionRun.Lone("alice", new GridPosition(0, 0));

        alice.AddToInventory(weapon);

        Assert.Equal([weapon], camp.Deposit(alice, Catalogue));

        Assert.True(alice.Carries(weapon));
        Assert.Empty(camp.Pending);
    }

    /// <summary>
    /// Une arme a feu confiee part <b>chargee</b> — et c'est a cela que tient la portee d'un
    /// revolver dans une partie.
    ///
    /// Le chargeur est rempli par <see cref="Inventory.Add"/>, au ramassage comme a la remise : une
    /// arme qu'on trouve est une arme qui tire. Ce test n'en verifie pas la mecanique, il verifie que
    /// le chemin du camp au van ne la court-circuite pas. Le jour ou il la court-circuiterait, le
    /// sac rabattrait l'arme sur ses valeurs de massue — dix degats, une case de portee — et rien a
    /// l'ecran ne dirait pourquoi le tireur se fait mordre.
    /// </summary>
    [Fact]
    public void Une_arme_a_feu_confiee_part_chargee_et_prete_a_tirer()
    {
        var firearm = Catalogue.Items.First(item => item.IsFirearm);

        var camp = Camp((firearm.Id, 1));
        var loadout = new CampLoadout(camp, Catalogue);
        var alice = MissionRun.Lone("alice", new GridPosition(0, 0));

        loadout.Give("alice", firearm.Id);
        loadout.HandOver(alice);

        Assert.Equal(firearm.Id, alice.Inventory.EquippedWeapon);
        Assert.Equal(firearm.Ammo!.Value, alice.Inventory.AmmoOf(firearm.Id));
        Assert.Equal(firearm.Range, alice.Inventory.RangeOf(firearm.Id));
        Assert.Equal(firearm.Damage, alice.Inventory.StrikeWith(firearm.Id).Damage, 3);
    }
}
