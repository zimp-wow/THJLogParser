using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using Syncfusion.Windows.Shared;
using Windows.ApplicationModel.Store;

namespace EQLogParser
{
  internal enum SpellClass
  {
    WAR = 1, CLR = 2, PAL = 4, RNG = 8, SHD = 16, DRU = 32, MNK = 64, BRD = 128, ROG = 256,
    SHM = 512, NEC = 1024, WIZ = 2048, MAG = 4096, ENC = 8192, BST = 16384, BER = 32768
  }

  internal enum SpellTarget
  {
    LOS = 1, CASTERAE = 2, CASTERGROUP = 3, CASTERPB = 4, SINGLETARGET = 5, SELF = 6, TARGETAE = 8, PET = 14,
    CASTERPBPLAYERS = 36, NEARBYPLAYERSAE = 40, TARGETGROUP = 41, DIRECTIONAE = 42, TARGETRINGAE = 45
  }

  internal enum SpellResist
  {
    UNDEFINED = -1, UNRESISTABLE = 0, MAGIC, FIRE, COLD, POISON, DISEASE, LOWEST, AVERAGE, PHYSICAL, CORRUPTION
  }

  internal static class Labels
  {
    public const string ABSORB = "Absorb";
    public const string DD = "Direct Damage";
    public const string DOT = "DoT Tick";
    public const string DS = "Damage Shield";
    public const string RS = "Reverse DS";
    public const string BANE = "Bane Damage";
    public const string PROC = "Proc";
    public const string HOT = "HoT Tick";
    public const string HEAL = "Direct Heal";
    public const string MELEE = "Melee";
    public const string SELFHEAL = "Melee Heal";
    public const string NODATA = "No Data Available";
    public const string PETPLAYEROPTION = "Players +Pets";
    public const string PLAYEROPTION = "Players";
    public const string PETOPTION = "Pets";
    public const string RAIDOPTION = "Raid";
    public const string RAIDTOTALS = "Totals";
    public const string RIPOSTE = "Riposte";
    public const string EVERYTHINGOPTION = "Uncategorized";
    public const string UNASSIGNED = "Unknown Pet Owner";
    public const string UNK = "Unknown";
    public const string UNKSPELL = "Unknown Spell";
    public const string RECEIVEDHEALPARSE = "Received Healing";
    public const string HEALPARSE = "Healing";
    public const string TANKPARSE = "Tanking";
    public const string TOPHEALSPARSE = "Top Heals";
    public const string DAMAGEPARSE = "Damage";
    public const string MISS = "Miss";
    public const string DODGE = "Dodge";
    public const string PARRY = "Parry";
    public const string BLOCK = "Block";
    public const string INVULNERABLE = "Invulnerable";
    // Xan - adding a rune type
    public const string RUNE = "Rune";
  }

  class DataManager
  {
    private static readonly log4net.ILog LOG = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

    internal static Lazy<DataManager> _instance = new Lazy<DataManager>(() => new DataManager());
    internal static DataManager Instance => _instance.Value;
    internal event EventHandler<string> EventsRemovedFight;
    internal event EventHandler<Fight> EventsNewFight;
    internal event EventHandler<Fight> EventsNewNonTankingFight;
    internal event EventHandler<Fight> EventsNewOverlayFight;
    internal event EventHandler<RandomRecord> EventsNewRandomRecord;
    internal event EventHandler<Fight> EventsUpdateFight;
    internal event Action<bool> EventsClearedActiveData;

    internal const int MAXTIMEOUT = 60;
    internal const int FIGHTTIMEOUT = 30;
    internal const double BUFFS_OFFSET = 90;
    internal uint MyNukeCritRateMod { get; private set; } = 0;
    internal uint MyDoTCritRateMod { get; private set; } = 0;

    private static readonly SpellAbbrvComparer AbbrvComparer = new SpellAbbrvComparer();
    private static readonly TimedActionComparer TAComparer = new TimedActionComparer();
    private static readonly object LockObject = new object();

    private readonly List<ActionBlock> AllMiscBlocks = new List<ActionBlock>();
    private readonly List<ActionBlock> AllDeathBlocks = new List<ActionBlock>();
    private readonly List<ActionBlock> AllHealBlocks = new List<ActionBlock>();
    private readonly List<ActionBlock> AllSpellCastBlocks = new List<ActionBlock>();
    private readonly List<ActionBlock> AllReceivedSpellBlocks = new List<ActionBlock>();
    private readonly List<ActionBlock> AllResistBlocks = new List<ActionBlock>();
    private readonly List<ActionBlock> AllRandomBlocks = new List<ActionBlock>();
    private readonly List<ActionBlock> AllLootBlocks = new List<ActionBlock>();
    private readonly List<TimedAction> AllSpecialActions = new List<TimedAction>();
    private readonly List<LootRecord> AssignedLoot = new List<LootRecord>();

    private readonly List<string> AdpsKeys = new List<string> { "#DoTCritRate", "#NukeCritRate" };
    private readonly Dictionary<string, Dictionary<string, uint>> AdpsActive = new Dictionary<string, Dictionary<string, uint>>();
    private readonly Dictionary<string, Dictionary<string, uint>> AdpsValues = new Dictionary<string, Dictionary<string, uint>>();
    private readonly Dictionary<string, HashSet<SpellData>> AdpsLandsOn = new Dictionary<string, HashSet<SpellData>>();
    private readonly Dictionary<string, HashSet<SpellData>> AdpsWearOff = new Dictionary<string, HashSet<SpellData>>();
    private readonly Dictionary<string, List<SpellData>> SpellsNameDB = new Dictionary<string, List<SpellData>>();
    private readonly Dictionary<string, bool> OldSpellNamesDB = new Dictionary<string, bool>();
    private readonly SpellTreeNode LandsOnOtherTree = new SpellTreeNode();
    private readonly SpellTreeNode LandsOnYouTree = new SpellTreeNode();
    private readonly SpellTreeNode WearOffTree = new SpellTreeNode();

    private readonly ConcurrentDictionary<string, byte> AllNpcs = new ConcurrentDictionary<string, byte>();
    private readonly ConcurrentDictionary<string, Dictionary<SpellResist, ResistCount>> NpcResistStats = new ConcurrentDictionary<string, Dictionary<SpellResist, ResistCount>>();
    private readonly ConcurrentDictionary<string, TotalCount> NpcTotalSpellCounts = new ConcurrentDictionary<string, TotalCount>();
    private readonly ConcurrentDictionary<string, SpellData> SpellsAbbrvDB = new ConcurrentDictionary<string, SpellData>();
    private readonly ConcurrentDictionary<string, SpellClass> SpellsToClass = new ConcurrentDictionary<string, SpellClass>();

    private readonly ConcurrentDictionary<string, Fight> ActiveFights = new ConcurrentDictionary<string, Fight>();
    private readonly ConcurrentDictionary<string, byte> LifetimeFights = new ConcurrentDictionary<string, byte>();
    private readonly ConcurrentDictionary<long, Fight> OverlayFights = new ConcurrentDictionary<long, Fight>();

    private readonly ConcurrentDictionary<string, string> SpellAbbrvCache = new ConcurrentDictionary<string, string>();
    private readonly ConcurrentDictionary<string, string> RanksCache = new ConcurrentDictionary<string, string>();
    private readonly ConcurrentDictionary<string, string> TitleToClass = new ConcurrentDictionary<string, string>();

    private int LastSpellIndex = -1;

    internal bool HasOverlayFights() => !OverlayFights.IsEmpty;
    private DataManager()
    {
      DictionaryUniqueListHelper<string, SpellData> helper = new DictionaryUniqueListHelper<string, SpellData>();
      var spellList = new List<SpellData>();

      // build ranks cache
      Enumerable.Range(1, 9).ToList().ForEach(r => RanksCache[r.ToString(CultureInfo.CurrentCulture)] = "");
      Enumerable.Range(1, 200).ToList().ForEach(r => RanksCache[TextFormatUtils.IntToRoman(r)] = "");
      RanksCache["Third"] = "Root";
      RanksCache["Fifth"] = "Root";
      RanksCache["Octave"] = "Root";
      
        // Player title mapping for /who queries
      ConfigUtil.ReadList(@"data\titles.txt").ForEach(line =>
      {
        string[] split = line.Split('=');
        if (split.Length == 2)
        {
          TitleToClass[split[0]] = split[0];
          foreach (var title in split[1].Split(','))
          {
            TitleToClass[title + " (" + split[0] + ")"] = split[0];
          }
        }
      });


      // Old Spell cache (EQEMU)
      ConfigUtil.ReadEmbeddedList(EQLogParser.Resource.oldspells).ForEach(line => OldSpellNamesDB[line] = true);

      List<string>  sd  = RetrieveSpellsLibrary();
      sd.ForEach(line =>
      {
        try
        {
          var spellData = ParseCustomSpellData(line);
          if (spellData != null)
          {
            spellList.Add(spellData);
            helper.AddToList(SpellsNameDB, spellData.Name, spellData);

            if (!SpellsAbbrvDB.ContainsKey(spellData.NameAbbrv))
            {
              SpellsAbbrvDB[spellData.NameAbbrv] = spellData;
            }
            else if (string.Compare(SpellsAbbrvDB[spellData.NameAbbrv].Name, spellData.Name, true, CultureInfo.CurrentCulture) < 0)
            {
              // try to keep the newest version
              SpellsAbbrvDB[spellData.NameAbbrv] = spellData;
            }

            // restricted received spells to only ADPS related
            if (!string.IsNullOrEmpty(spellData.LandsOnOther) && (spellData.Adps > 0 || spellData.IsBeneficial))
            {
              BuildSpellPath(spellData.LandsOnOther.Trim().Split(' ').ToList(), LandsOnOtherTree, spellData);
            }

            if (!string.IsNullOrEmpty(spellData.LandsOnYou) && (spellData.Adps > 0 || spellData.IsBeneficial))
            {
              BuildSpellPath(spellData.LandsOnYou.Trim().Split(' ').ToList(), LandsOnYouTree, spellData);
            }

            if (!string.IsNullOrEmpty(spellData.WearOff) && (spellData.Adps > 0 || spellData.IsBeneficial))
            {
              BuildSpellPath(spellData.WearOff.Trim().Split(' ').ToList(), WearOffTree, spellData);
            }
          }
        }
        catch (OverflowException ex)
        {
          LOG.Error("Error reading spell data", ex);
        }
      });

      var keepOut = new Dictionary<string, byte>();
      var classEnums = Enum.GetValues(typeof(SpellClass)).Cast<SpellClass>().ToList();

      spellList.ForEach(spell =>
      {
        // exact match meaning class-only spell that are of certain target types
        var tgt = (SpellTarget)spell.Target;
        if (spell.Level <= 254 && spell.Proc == 0 && (tgt == SpellTarget.SELF || tgt == SpellTarget.SINGLETARGET || tgt == SpellTarget.LOS || spell.Rank > 1) &&
          classEnums.Contains((SpellClass)spell.ClassMask))
        {
          // Obviously illusions are bad to look for
          // Call of Fire is Ranger only and self target but VT clickie lets warriors use it
          if (spell.Name.IndexOf("Illusion", StringComparison.OrdinalIgnoreCase) == -1 &&
          !spell.Name.EndsWith(" gate", StringComparison.OrdinalIgnoreCase) &&
          spell.Name.IndexOf(" Synergy", StringComparison.OrdinalIgnoreCase) == -1 &&
          spell.Name.IndexOf("Call of Fire", StringComparison.OrdinalIgnoreCase) == -1)
          {
            // these need to be unique and keep track if a conflict is found
            if (SpellsToClass.ContainsKey(spell.Name))
            {
              SpellsToClass.TryRemove(spell.Name, out SpellClass _);
              keepOut[spell.Name] = 1;
            }
            else if (!keepOut.ContainsKey(spell.Name))
            {
              SpellsToClass[spell.Name] = (SpellClass)spell.ClassMask;
            }
          }
        }
      });

      // load NPCs
      ConfigUtil.ReadList(@"data\npcs.txt").ForEach(line => AllNpcs[line.Trim()] = 1);

      // Load Adps
      AdpsKeys.ForEach(adpsKey => AdpsActive[adpsKey] = new Dictionary<string, uint>());
      AdpsKeys.ForEach(adpsKey => AdpsValues[adpsKey] = new Dictionary<string, uint>());

      string key = null;
      foreach (var line in ConfigUtil.ReadList(@"data\adpsMeter.txt"))
      {
        if (!string.IsNullOrEmpty(line) && line.Trim() is string trimmed && trimmed.Length > 0)
        {
          if (trimmed[0] != '#' && !string.IsNullOrEmpty(key))
          {
            if (trimmed.Split('|') is string[] multiple && multiple.Length > 0)
            {
              foreach (var spellLine in multiple)
              {
                if (spellLine.Split('=') is string[] list && list.Length == 2 && uint.TryParse(list[1], out uint rate))
                {
                  if (GetAdpsByName(list[0]) is SpellData spellData)
                  {
                    AdpsValues[key][spellData.NameAbbrv] = rate;

                    if (!AdpsWearOff.TryGetValue(spellData.WearOff, out HashSet<SpellData> wearOffList))
                    {
                      AdpsWearOff[spellData.WearOff] = new HashSet<SpellData>();
                    }

                    AdpsWearOff[spellData.WearOff].Add(spellData);

                    if (!AdpsLandsOn.TryGetValue(spellData.LandsOnYou, out HashSet<SpellData> landsOnList))
                    {
                      AdpsLandsOn[spellData.LandsOnYou] = new HashSet<SpellData>();
                    }

                    AdpsLandsOn[spellData.LandsOnYou].Add(spellData);
                  }
                }
              }
            }
          }
          else if (AdpsKeys.Contains(trimmed))
          {
            key = trimmed;
          }
        }
      }

      PlayerManager.Instance.EventsNewTakenPetOrPlayerAction += (sender, name) => RemoveFight(name);
      PlayerManager.Instance.EventsNewVerifiedPlayer += (sender, name) => RemoveFight(name);
      PlayerManager.Instance.EventsNewVerifiedPet += (sender, name) => RemoveFight(name);
    }


        internal void ResetOverlayFights(bool active = false)
        {
            var groupId = (active && !ActiveFights.IsEmpty) ? ActiveFights.Values.First().GroupId : -1;
            // active is used after the log as been loaded. the overlay opening is displayed so that
            // FightTable has time to populate the GroupIds. if for some reason not enough time has
            // elapsed then the IDs will still be 0 so ignore
            if (groupId == 0)
            {
                groupId = -1;
            }

            var removeList = new List<long>();
            foreach (var fight in OverlayFights.Values)
            {
                if (fight != null && (groupId == -1 || fight.GroupId != groupId))
                {
                    fight.PlayerTotals.Clear();
                    removeList.Add(fight.Id);
                }
            }

            removeList.ForEach(RemoveOverlayFight);
        }
        internal void AddDeathRecord(DeathRecord record, double beginTime) => Helpers.AddAction(AllDeathBlocks, record, beginTime);
    internal void AddMiscRecord(IAction action, double beginTime) => Helpers.AddAction(AllMiscBlocks, action, beginTime);
    internal void AddReceivedSpell(ReceivedSpell received, double beginTime) => Helpers.AddAction(AllReceivedSpellBlocks, received, beginTime);
    internal List<Fight> GetOverlayFights() => OverlayFights.Values.ToList();
    internal void RemoveOverlayFight(long id) => OverlayFights.Remove(id, out _);
    internal List<ActionBlock> GetAllLoot() => AllLootBlocks.ToList();
    internal List<ActionBlock> GetAllRandoms() => AllRandomBlocks.ToList();
    internal string GetClassFromTitle(string title) => TitleToClass.ContainsKey(title) ? TitleToClass[title] : null;
    internal List<ActionBlock> GetCastsDuring(double beginTime, double endTime) => SearchActions(AllSpellCastBlocks, beginTime, endTime);
    internal List<ActionBlock> GetDeathsDuring(double beginTime, double endTime) => SearchActions(AllDeathBlocks, beginTime, endTime);
    internal List<ActionBlock> GetHealsDuring(double beginTime, double endTime) => SearchActions(AllHealBlocks, beginTime, endTime);
    internal List<ActionBlock> GetMiscDuring(double beginTime, double endTime) => SearchActions(AllMiscBlocks, beginTime, endTime);
    internal ConcurrentDictionary<string, Dictionary<SpellResist, ResistCount>> GetNpcResistStats() => NpcResistStats;
    internal ConcurrentDictionary<string, TotalCount> GetNpcTotalSpellCounts() => NpcTotalSpellCounts;
    internal List<ActionBlock> GetResistsDuring(double beginTime, double endTime) => SearchActions(AllResistBlocks, beginTime, endTime);
    internal List<ActionBlock> GetReceivedSpellsDuring(double beginTime, double endTime) => SearchActions(AllReceivedSpellBlocks, beginTime, endTime);
    internal SpellData GetSpellByAbbrv(string abbrv) => (!string.IsNullOrEmpty(abbrv) && abbrv != Labels.UNKSPELL && SpellsAbbrvDB.ContainsKey(abbrv)) ? SpellsAbbrvDB[abbrv] : null;
    internal bool IsKnownNpc(string npc) => !string.IsNullOrEmpty(npc) && AllNpcs.ContainsKey(npc.ToLower(CultureInfo.CurrentCulture));
    internal bool IsOldSpell(string name) => OldSpellNamesDB.ContainsKey(name);
    internal bool IsPlayerSpell(string name) => GetSpellByName(name)?.ClassMask > 0;
    internal bool IsLifetimeNpc(string name) => LifetimeFights.ContainsKey(name);

    internal string AbbreviateSpellName(string spell)
    {
      if (!SpellAbbrvCache.TryGetValue(spell, out string result))
      {
        result = spell;
        int index;
        if ((index = spell.IndexOf(" Rk. ", StringComparison.Ordinal)) > -1)
        {
          result = spell.Substring(0, index);
        }
        else if ((index = spell.LastIndexOf(" ", StringComparison.Ordinal)) > -1)
        {
          string lastWord = spell.Substring(index + 1);

          if (RanksCache.TryGetValue(lastWord, out string root))
          {
            result = spell.Substring(0, index);
            if (!string.IsNullOrEmpty(root))
            {
              result += " " + root;
            }
          }
        }

        SpellAbbrvCache[spell] = result;
      }

      return string.Intern(result);
    }

    internal void AddLootRecord(LootRecord record, double beginTime)
    {
      Helpers.AddAction(AllLootBlocks, record, beginTime);

      if (!record.IsCurrency && record.Quantity > 0 && AssignedLoot.Count > 0)
      {
        var found = AssignedLoot.FindLastIndex(item => item.Player == record.Player && item.Item == record.Item);
        if (found > -1)
        {
          AssignedLoot.RemoveAt(found);

          foreach (var block in AllLootBlocks.OrderByDescending(block => block.BeginTime))
          {
            found = block.Actions.FindLastIndex(item => item is LootRecord loot && loot.Player == record.Player && loot.Item == record.Item && loot.Quantity == 0);
            if (found > -1)
            {
              lock (block.Actions)
              {
                block.Actions.RemoveAt(found);
              }
            }
          }
        }
      }
      else if (!record.IsCurrency && record.Quantity == 0)
      {
        AssignedLoot.Add(record);
      }
    }

    internal void AddRandomRecord(RandomRecord record, double beginTime)
    {
      Helpers.AddAction(AllRandomBlocks, record, beginTime);
      EventsNewRandomRecord?.Invoke(this, record);
    }

    internal void AddResistRecord(ResistRecord record, double beginTime)
    {
      Helpers.AddAction(AllResistBlocks, record, beginTime);

      if (SpellsNameDB.TryGetValue(record.Spell, out List<SpellData> spellList))
      {
        if (spellList.Find(item => !item.IsBeneficial) is SpellData spellData)
        {
          UpdateNpcSpellResistStats(record.Defender, spellData.Resist, true);
        }
      }
    }

    internal void CheckExpireFights(double currentTime)
    {
      foreach (ref Fight fight in ActiveFights.Values.ToArray().AsSpan())
      {
        double diff = currentTime - fight.LastTime;
        if (diff > MAXTIMEOUT || diff > FIGHTTIMEOUT && fight.DamageBlocks.Count > 0)
        {
          RemoveActiveFight(fight.CorrectMapKey);
        }
      }
    }

    internal SpellData GetAdpsByName(string name)
    {
      SpellData spellData = null;

      if (!SpellsAbbrvDB.TryGetValue(name, out spellData))
      {
        if (SpellsNameDB.TryGetValue(name, out List<SpellData> spellList))
        {
          spellData = spellList.Find(item => item.Adps > 0);
        }
      }

      return spellData;
    }

    internal Fight GetFight(string name)
    {
      Fight result = null;
      if (!string.IsNullOrEmpty(name))
      {
        ActiveFights.TryGetValue(name, out result);
      }
      return result;
    }

    internal SpellData GetDamagingSpellByName(string name, bool bPC = false)
    {
      SpellData spellData = null;

      if (!string.IsNullOrEmpty(name) && name != Labels.UNKSPELL && SpellsNameDB.TryGetValue(name, out List<SpellData> spellList))
      {
        // Xan - SOE/DBG brilliantly have multiple spells with the same name, so added a param to check for PC spells if we think we're parsing a player
        if( bPC )
            spellData = spellList.Find(item => item.Damaging > 0 && item.ClassMask > 0 );
        else
            spellData = spellList.Find(item => item.Damaging > 0);

        // Xan - losing spells here due to the Damaging field (ex. Splurt not found)
        // going to return first entry in that case if only 1 entry
        if( spellData == null && spellList.Count == 1 )
            spellData   = spellList[0];
      }

      return spellData;
    }

    internal SpellData GetHealingSpellByName(string name)
    {
      SpellData spellData = null;

      if (!string.IsNullOrEmpty(name) && name != Labels.UNKSPELL && SpellsNameDB.TryGetValue(name, out List<SpellData> spellList))
      {
        spellData = spellList.Find(item => item.Damaging < 0);
      }

      return spellData;
    }

    internal SpellData GetSpellByName(string name)
    {
      SpellData spellData = null;

      if (!string.IsNullOrEmpty(name) && name != Labels.UNKSPELL && SpellsNameDB.TryGetValue(name, out List<SpellData> spellList))
      {
        if (spellList.Count <= 10)
        {
          foreach (ref SpellData spell in spellList.ToArray().AsSpan())
          {
            if (spellData == null || (spellData.Level < spell.Level && spell.Level <= 250) || (spellData.Level > 250 && spell.Level <= 250))
            {
              spellData = spell;
            }
          }
        }
        else
        {
          spellData = spellList.Last();
        }
      }

      return spellData;
    }

    internal void AddSpecial(TimedAction action)
    {
      lock (AllSpecialActions)
      {
        AllSpecialActions.Add(action);
      }
    }

    internal void AddHealRecord(HealRecord record, double beginTime)
    {
      record.Healer = PlayerManager.Instance.ReplacePlayer(record.Healer, record.Healed);
      record.Healed = PlayerManager.Instance.ReplacePlayer(record.Healed, record.Healer);
      Helpers.AddAction(AllHealBlocks, record, beginTime);
    }

    //THJ: Removed spell name requirement. Assuming this correctly flags player's last cast
    internal void HandleSpellInterrupt(string player, double beginTime)
    {
      for (int i = AllSpellCastBlocks.Count - 1; i >= 0 && beginTime - AllSpellCastBlocks[i].BeginTime <= 5; i--)
      {
        int index = AllSpellCastBlocks[i].Actions.FindLastIndex(action => action is SpellCast sc && sc.Caster == player);
        if (index > -1 && AllSpellCastBlocks[i].Actions[index] is SpellCast cast)
        {
          cast.Interrupted = true;
          break;
        }
      }
    }

    internal void AddSpellCast(SpellCast cast, double beginTime)
    {
      if (SpellsNameDB.ContainsKey(cast.Spell))
      {
        Helpers.AddAction(AllSpellCastBlocks, cast, beginTime);
        LastSpellIndex = AllSpellCastBlocks.Count - 1;

        if (SpellsToClass.TryGetValue(cast.Spell, out SpellClass theClass))
        {
          PlayerManager.Instance.UpdatePlayerClassFromSpell(cast, theClass);
        }
      }
    }

    internal List<TimedAction> GetSpecials()
    {
      lock (AllSpecialActions)
      {
        return AllSpecialActions.OrderBy(special => special.BeginTime).ToList();
      }
    }

    internal SpellTreeResult GetLandsOnOther(List<string> data, out string player)
    {
      player = null;
      var found = SearchSpellPath(LandsOnOtherTree, data);

      if (found.SpellData.Count > 0 && found.DataIndex > -1)
      {
        player = string.Join(" ", data.ToArray(), 0, found.DataIndex + 1);
        if (player.EndsWith("'s"))
        {
          // if string is only 2 then it must be invalid
          player = (player.Length > 2) ? player.Substring(0, player.Length - 2) : null;
        }

        found.SpellData = FindByLandsOn(player, found.SpellData);
      }

      return found;
    }

    internal SpellTreeResult GetLandsOnYou(List<string> data)
    {
      var found = SearchSpellPath(LandsOnYouTree, data);

      if (found.DataIndex == 0 && found.SpellData.Count > 0)
      {
        found.SpellData = FindByLandsOn(ConfigUtil.PlayerName, found.SpellData);

        // check Adps
        if (AdpsLandsOn.TryGetValue(found.SpellData[0].LandsOnYou, out HashSet<SpellData> spellDataSet) && spellDataSet.Count > 0)
        {
          var spellData = spellDataSet.Count == 1 ? spellDataSet.First() : FindPreviousCast(ConfigUtil.PlayerName, spellDataSet.ToList(), true);

          // this only handles latest versions of spells so an older one may have given us the landsOn string and then it wasn't found
          // for some spells this makes sense because of the level requirements and it wouldn't do anything but thats not true for all of them
          // need to handle older spells and multiple rate values
          if (spellData != null)
          {
            AdpsKeys.ForEach(key =>
            {
              if (AdpsValues[key].TryGetValue(spellData.NameAbbrv, out uint value))
              {
                AdpsActive[key][spellData.LandsOnYou] = value;
                RecalculateAdps();
              }
            });
          }
        }
      }

      return found;
    }

    internal SpellTreeResult GetWearOff(List<string> data)
    {
      var found = SearchSpellPath(WearOffTree, data);

      if (found.DataIndex == 0 && found.SpellData.Count > 0)
      {
        found.SpellData = FindByLandsOn(data[0], found.SpellData);

        // check Adps
        if (AdpsWearOff.TryGetValue(found.SpellData[0].WearOff, out HashSet<SpellData> spellDataSet) && spellDataSet.Count > 0)
        {
          var spellData = spellDataSet.First();

          AdpsKeys.ForEach(key =>
          {
            if (AdpsValues[key].TryGetValue(spellData.NameAbbrv, out uint value))
            {
              AdpsActive[key].Remove(spellData.LandsOnYou);
              RecalculateAdps();
            }
          });
        }
      }

      return found;
    }

    internal void UpdateNpcSpellReflectStats(string npc)
    {
      lock (NpcTotalSpellCounts)
      {
        if (!NpcTotalSpellCounts.TryGetValue(npc, out TotalCount value))
        {
          value = new TotalCount { Reflected = 1 };
          NpcTotalSpellCounts[npc] = value;
        }
        else
        {
          value.Reflected++;
        }
      }
    }

    internal void UpdateNpcSpellResistStats(string npc, SpellResist resist, bool resisted = false)
    {
      // NPC is always upper case after it is parsed
      lock (NpcResistStats)
      {
        if (!NpcResistStats.TryGetValue(npc, out Dictionary<SpellResist, ResistCount> stats))
        {
          stats = new Dictionary<SpellResist, ResistCount>();
          NpcResistStats[npc] = stats;
        }

        if (!stats.TryGetValue(resist, out ResistCount count))
        {
          stats[resist] = resisted ? new ResistCount { Resisted = 1 } : new ResistCount { Landed = 1 };
        }
        else
        {
          if (resisted)
          {
            count.Resisted++;
          }
          else
          {
            count.Landed++;
          }
        }
      }

      lock (NpcTotalSpellCounts)
      {
        if (!NpcTotalSpellCounts.TryGetValue(npc, out TotalCount value))
        {
          value = new TotalCount { Landed = 1 };
          NpcTotalSpellCounts[npc] = value;
        }
        else
        {
          value.Landed++;
        }
      }
    }

    internal void ZoneChanged()
    {
      bool updated = false;
      foreach (var active in AdpsActive)
      {
        active.Value.Keys.ToList().ForEach(landsOn =>
        {
          if (AdpsLandsOn[landsOn].Any(spellData => spellData.SongWindow))
          {
            AdpsActive[active.Key].Remove(landsOn);
            updated = true;
          }
        });
      }

      if (updated)
      {
        RecalculateAdps();
      }
    }

    internal  List<string> RetrieveSpellsLibrary()
    {
        var myDirectory = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        var spellsFilePath = System.IO.Path.Combine(myDirectory, "spells_us.txt");
        if (System.IO.File.Exists(spellsFilePath))
        {
            try
            {
                List<string> sd = ConfigUtil.ReadList(spellsFilePath);
                sd.ForEach(line => {
                    var spellData = ParseCustomSpellData(line);
                });
                return sd;
            }
            catch (Exception ex)
            {
                LOG.Error($"Error reading spell data from [{spellsFilePath}]", ex);
            }
        }
        myDirectory = System.IO.Directory.GetParent(myDirectory).FullName;
        spellsFilePath = System.IO.Path.Combine(myDirectory, "spells_us.txt");
        if (System.IO.File.Exists(spellsFilePath))
        {
            try
            {
                List<string> sd = ConfigUtil.ReadList(spellsFilePath);
                sd.ForEach(line => {
                    var spellData = ParseCustomSpellData(line);
                });
                return sd;
            }
            catch (Exception ex)
            {
                LOG.Error($"Error reading spell data from [{spellsFilePath}]", ex);
            }
        }
        return ConfigUtil.ReadEmbeddedList(EQLogParser.Resource.spells);
    }

    internal SpellData ParseCustomSpellData(string line)
    {
      SpellData spellData = null;

      if (!string.IsNullOrEmpty(line))
      {
        string[] data = line.Split('^');

        // Xan - parse real spell data
        if( data.Length == 237 )
        {
            THJSpell   thj = THJSpellParser.ParseSpellTHJ( data );

            int duration     = thj.DurationTicks *6;

            if (duration > ushort.MaxValue)
            {
                duration = ushort.MaxValue;
            }
            else if (duration < 0)
            {
                duration = 0;
            }

            spellData   = new SpellData
            {
                ID              = thj.ID.ToString( ),
                Name            = thj.Name,
                NameAbbrv       = thj.Name,
                Level           = (byte) THJSpellParser.GetLowestLevel( thj ),
                Duration        = (ushort) duration,
                IsBeneficial    = thj.Beneficial,
                Target          = (byte) thj.Target,
                MaxHits         = (ushort) thj.MaxHits,
                ClassMask       = (ushort) thj.ClassesMask,
                Damaging        = 0,
                Resist          = (EQLogParser.SpellResist) thj.ResistType,
                SongWindow      = thj.SongWindow,
                Adps            = 0,
                Mgb             = thj.MGBable,
                Rank            = (byte) thj.Rank,
                LandsOnYou      = thj.LandOnSelf,
                LandsOnOther    = thj.LandOnOther,
                WearOff         = thj.WearsOff,
                Proc            = 0
            };
            foreach( var v in thj.Slots )
            {
                switch( (THJSpellEffect) v.SPA )
				{
                    // label if damaging
                    case THJSpellEffect.Current_HP:
                    case THJSpellEffect.Current_HP_Non_Repeating:
                    case THJSpellEffect.Current_HP_Repeating:
                        spellData.Damaging  = (short) (v.Base1 > 0 ? -1 : 1);
                        break;
                    case THJSpellEffect.Donals_Heal:
                    case THJSpellEffect.Current_HP_Percent:
                    case THJSpellEffect.Current_HP_Percent_Tick:
                        spellData.Damaging  = -1;
                        break;
                    case THJSpellEffect.Skill_Attack:
                    case THJSpellEffect.Rampage:
                    case THJSpellEffect.AE_Attack:
                    case THJSpellEffect.Mana_Burn:
                        spellData.Damaging  = 1;
                        break;

                    // label is adps
                    case THJSpellEffect.ATK:
                    case THJSpellEffect.Melee_Haste:
                    case THJSpellEffect.Melee_Haste_v2:
                    case THJSpellEffect.Melee_Haste_v119:
                    case THJSpellEffect.Spell_Damage:
                    case THJSpellEffect.Critical_Nuke_Damage:
                    case THJSpellEffect.Crippling_Blow_Chance:
                    case THJSpellEffect.Weapon_Delay:
                    case THJSpellEffect.Hit_Chance:
                    case THJSpellEffect.Hit_Damage:
                    case THJSpellEffect.Min_Hit_Damage:
                    case THJSpellEffect.Critical_Hit_Chance:
                    case THJSpellEffect.Frenzied_Devastation:
                    case THJSpellEffect.Slay_Undead:
                    case THJSpellEffect.Weapon_Damage_Bonus:
                    case THJSpellEffect.Double_Attack_Skill:
                    case THJSpellEffect.Frontal_Backstab_Chance:
                    case THJSpellEffect.Frontal_Backstab_Min_Damage:
                    case THJSpellEffect.Triple_Backstab_Chance:
                    case THJSpellEffect.Critical_DoT_Chance:
                    case THJSpellEffect.Critical_Heal_Chance:
                    case THJSpellEffect.Flurry:
                    case THJSpellEffect.Double_Special_Attack_Chance:
                    case THJSpellEffect.Spell_Damage_Bonus:
                    case THJSpellEffect.Spell_Duration_Focus_v287:
                    case THJSpellEffect.Critical_Nuke_Chance:
                    case THJSpellEffect.Spell_Damage_Taken_v296:
                    case THJSpellEffect.Spell_Damage_Taken_v297:
                    case THJSpellEffect.Spell_Damage_v302:
                    case THJSpellEffect.Spell_Damage_v303:
                    case THJSpellEffect.Hit_Damage_v2:
                    case THJSpellEffect.Spell_Damage_v461:
                    case THJSpellEffect.Spell_Damage_v462:
                    case THJSpellEffect.Pet_Critical_Hit_Damage:
                    case THJSpellEffect.Spell_Damage_Taken_v483:
                    case THJSpellEffect.Spell_Damage_Taken_v484:
                    case THJSpellEffect.Critical_Hit_Damage_NonStacking:
                    case THJSpellEffect.Spell_Damage_v507:
                    case THJSpellEffect.Spell_Damage_v508:
                    case THJSpellEffect.Critical_Hit_Damage:
                    case THJSpellEffect.Triple_Attack:
                    case THJSpellEffect.Melee_Delay:
                    case THJSpellEffect.Critical_DoT_Damage:
                    case THJSpellEffect.Twincast_Chance:
                    case THJSpellEffect.Weapon_Damage_Bonus_v418:
                    case THJSpellEffect.Ranged_Damage:
                        spellData.Adps  = 1;
                        break;

                    // we would check base1 for these to add to a list of procs but have to run that back through after all spells read
                    case THJSpellEffect.Add_Melee_Proc_v85:
                    case THJSpellEffect.Add_Range_Proc:
                    case THJSpellEffect.Add_AA_Proc:
                    case THJSpellEffect.Defensive_Proc:
                    case THJSpellEffect.Add_Melee_Proc_v419:
                    case THJSpellEffect.Add_Skill_Proc:
                    case THJSpellEffect.Spell_Proc:
                        break;
                }
            }
        }
        else if (data.Length >= 11)
        {
          int duration = int.Parse(data[3], CultureInfo.CurrentCulture) * 6; // as seconds
          int beneficial = int.Parse(data[4], CultureInfo.CurrentCulture);
          byte target = byte.Parse(data[6], CultureInfo.CurrentCulture);
          ushort classMask = ushort.Parse(data[7], CultureInfo.CurrentCulture);

          // deal with too big or too small values
          // all adps we care about is in the range of a few minutes
          if (duration > ushort.MaxValue)
          {
            duration = ushort.MaxValue;
          }
          else if (duration < 0)
          {
            duration = 0;
          }

          spellData = new SpellData
          {
            ID = string.Intern(data[0]),
            Name = string.Intern(data[1]),
            NameAbbrv = string.Intern(AbbreviateSpellName(data[1])),
            Level = byte.Parse(data[2], CultureInfo.CurrentCulture),
            Duration = (ushort)duration,
            IsBeneficial = beneficial != 0,
            Target = target,
            MaxHits = ushort.Parse(data[5], CultureInfo.CurrentCulture),
            ClassMask = classMask,
            Damaging = short.Parse(data[8], CultureInfo.CurrentCulture),
            //CombatSkill = uint.Parse(data[9], CultureInfo.CurrentCulture),
            Resist = (EQLogParser.SpellResist)int.Parse(data[10], CultureInfo.CurrentCulture),
            SongWindow = data[11] == "1",
            Adps = byte.Parse(data[12], CultureInfo.CurrentCulture),
            Mgb = data[13] == "1",
            Rank = byte.Parse(data[14], CultureInfo.CurrentCulture),
            LandsOnYou = string.Intern(data[15]),
            LandsOnOther = string.Intern(data[16]),
            WearOff = string.Intern(data[17]),
            Proc = byte.Parse(data[18], CultureInfo.CurrentCulture)
          };
        }
      }

      return spellData;
    }

    private void RecalculateAdps()
    {
      lock (LockObject)
      {
        MyDoTCritRateMod = (uint)AdpsActive[AdpsKeys[0]].Sum(kv => kv.Value);
        MyNukeCritRateMod = (uint)AdpsActive[AdpsKeys[1]].Sum(kv => kv.Value);
      }
    }

    private SpellData FindPreviousCast(string player, List<SpellData> output, bool isAdps = false)
    {
      if (LastSpellIndex > -1)
      {
        var endTime = AllSpellCastBlocks[LastSpellIndex].BeginTime - 5;
        for (int i = LastSpellIndex; i >= 0 && AllSpellCastBlocks[i].BeginTime >= endTime; i--)
        {
          for (int j = AllSpellCastBlocks[i].Actions.Count - 1; j >= 0; j--)
          {
            if (AllSpellCastBlocks[i].Actions[j] is SpellCast cast && !cast.Interrupted &&
              output.Find(spellData => (spellData.Target != (int)SpellTarget.SELF || cast.Caster == player) &&
              spellData.Name == cast.Spell && (!isAdps || spellData.Adps > 0)) is SpellData found)
            {
              return found;
            }
          }
        }
      }

      return null;
    }

    private List<SpellData> FindByLandsOn(string player, List<SpellData> output)
    {
      List<SpellData> result = null;

      if (output.Count == 1)
      {
        result = output;
      }
      else if (output.Count > 1)
      {
        var foundSpellData = FindPreviousCast(player, output);
        if (foundSpellData == null)
        {
          // one more thing, if all the abbrviations look the same then we know the spell
          // even if the version is wrong. grab the newest
          result = (output.Distinct(AbbrvComparer).Count() == 1) ? new List<SpellData> { output.First() } : output;
        }
        else
        {
          result = new List<SpellData> { foundSpellData };
        }
      }

      return result;
    }

    internal bool RemoveActiveFight(string name)
    {
      bool removed = ActiveFights.TryRemove(name, out Fight fight);
      if (removed)
      {
        fight.Dead = true;
      }
      return removed;
    }

    internal void UpdateIfNewFightMap(string name, Fight fight, bool isNonTankingFight)
    {
      LifetimeFights[name] = 1;

      if (ActiveFights.TryAdd(name, fight))
      {
        ActiveFights[name] = fight;
        EventsNewFight?.Invoke(this, fight);
      }
      else
      {
        EventsUpdateFight?.Invoke(this, fight);
      }

      // basically an Add use case for only showing Fights with player damage
      if (isNonTankingFight)
      {
        EventsNewNonTankingFight?.Invoke(this, fight);
      }

      if (fight.DamageHits > 0)
      {
        lock (OverlayFights)
        {
          OverlayFights[fight.Id] = fight;
          EventsNewOverlayFight?.Invoke(this, fight);
        }
      }
    }

    internal void ResetOverlayFights()
    {
      lock (OverlayFights)
      {
        foreach (ref var fight in OverlayFights.Values.ToArray().AsSpan())
        {
          fight.PlayerTotals.Clear();
        }

        OverlayFights.Clear();
      }
    }

    internal void Clear()
    {
      lock (LockObject)
      {
        LastSpellIndex = -1;
        ActiveFights.Clear();
        LifetimeFights.Clear();
        OverlayFights.Clear();
        AllDeathBlocks.Clear();
        AllMiscBlocks.Clear();
        AllSpellCastBlocks.Clear();
        AllReceivedSpellBlocks.Clear();
        AllResistBlocks.Clear();
        AllHealBlocks.Clear();
        AllLootBlocks.Clear();
        AllRandomBlocks.Clear();
        AllSpecialActions.Clear();
        SpellAbbrvCache.Clear();
        NpcTotalSpellCounts.Clear();
        NpcResistStats.Clear();
        ClearActiveAdps();
        EventsClearedActiveData?.Invoke(true);
      }
    }

    internal void ClearActiveAdps()
    {
      lock (LockObject)
      {
        AdpsKeys.ForEach(key => AdpsActive[key].Clear());
        MyDoTCritRateMod = 0;
        MyNukeCritRateMod = 0;
      }
    }

    internal static bool ResolveSpellAmbiguity(ReceivedSpell spell, out SpellData replaced)
    {
      replaced = null;

      if (spell.Ambiguity.Count < 30)
      {
        int spellClass = (int)PlayerManager.Instance.GetPlayerClassEnum(spell.Receiver);
        var subset = spell.Ambiguity.FindAll(test => test.Target == (int)SpellTarget.SELF && spellClass != 0 && (test.ClassMask & spellClass) == spellClass);
        var distinct = subset.Distinct(AbbrvComparer).ToList();
        replaced = distinct.Count == 1 ? distinct.First() : spell.Ambiguity.First();
      }

      return replaced != null;
    }

    private void RemoveFight(string name)
    {
      if (!string.IsNullOrEmpty(name))
      {
        bool removed = ActiveFights.TryRemove(name, out Fight npc);
        removed = LifetimeFights.TryRemove(name, out byte bnpc) || removed;

        if (removed)
        {
          EventsRemovedFight?.Invoke(this, name);
        }

        lock (OverlayFights)
        {
          OverlayFights.Values.ToList().ForEach(fight =>
          {
            if (fight.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
              OverlayFights.TryRemove(fight.Id, out Fight _);
            }
          });
        }
      }
    }

    private static List<ActionBlock> SearchActions(List<ActionBlock> allActions, double beginTime, double endTime)
    {
      ActionBlock startBlock = new ActionBlock { BeginTime = beginTime };
      ActionBlock endBlock = new ActionBlock { BeginTime = endTime + 1 };

      int startIndex = allActions.BinarySearch(startBlock, TAComparer);
      if (startIndex < 0)
      {
        startIndex = Math.Abs(startIndex) - 1;
      }

      int endIndex = allActions.BinarySearch(endBlock, TAComparer);
      if (endIndex < 0)
      {
        endIndex = Math.Abs(endIndex) - 1;
      }

      int last = endIndex - startIndex;
      return last > 0 ? allActions.GetRange(startIndex, last) : new List<ActionBlock>();
    }

    public static SpellTreeResult SearchSpellPath(SpellTreeNode node, List<string> data, int lastIndex = -1)
    {
      if (lastIndex == -1)
      {
        lastIndex = data.Count - 1;
      }

      if (node.Words.TryGetValue(data[lastIndex], out SpellTreeNode child))
      {
        if (lastIndex > 0)
        {
          return SearchSpellPath(child, data, lastIndex - 1);
        }
        else
        {
          return new SpellTreeResult { SpellData = child.SpellData, DataIndex = lastIndex };
        }
      }

      return new SpellTreeResult { SpellData = node.SpellData, DataIndex = lastIndex };
    }

    private static void BuildSpellPath(List<string> data, SpellTreeNode node, SpellData spellData, int lastIndex = -1)
    {
      if (lastIndex == -1)
      {
        lastIndex = data.Count - 1;
      }

      if (data[lastIndex] == "'s")
      {
        node.SpellData.Add(spellData);
        node.SpellData.Sort((a, b) => DurationCompare(a, b));
      }
      else
      {
        if (!node.Words.TryGetValue(data[lastIndex], out SpellTreeNode child))
        {
          child = new SpellTreeNode();
          node.Words[data[lastIndex]] = child;
        }

        if (lastIndex == 0)
        {
          child.SpellData.Add(spellData);
          child.SpellData.Sort((a, b) => DurationCompare(a, b));
        }
        else
        {
          BuildSpellPath(data, child, spellData, lastIndex - 1);
        }
      }
    }

    static int DurationCompare(SpellData a, SpellData b)
    {
      if (b.Duration.CompareTo(a.Duration) is int result && result == 0)
      {
        if (int.TryParse(a.ID, out int aInt) && int.TryParse(b.ID, out int bInt) && aInt != bInt)
        {
          result = aInt > bInt ? -1 : 1;
        }
      }

      return result;
    }

    private class SpellAbbrvComparer : IEqualityComparer<SpellData>
    {
      public bool Equals(SpellData x, SpellData y) => x?.NameAbbrv == y?.NameAbbrv;
      public int GetHashCode(SpellData obj) => obj == null ? 0 : obj.NameAbbrv.GetHashCode();
    }

    private class TimedActionComparer : IComparer<TimedAction>
    {
      public int Compare(TimedAction x, TimedAction y) => (x != null && y != null) ? x.BeginTime.CompareTo(y.BeginTime) : 0;
    }

    public class ResistCount
    {
      internal uint Landed { get; set; }
      internal uint Resisted { get; set; }
    }

    public class TotalCount
    {
      internal uint Landed { get; set; }
      internal uint Reflected { get; set; }
    }

    public class SpellTreeNode
    {
      internal List<SpellData> SpellData { get; set; } = new List<SpellData>();
      internal Dictionary<string, SpellTreeNode> Words { get; set; } = new Dictionary<string, SpellTreeNode>();
    }

    public class SpellTreeResult
    {
      internal List<SpellData> SpellData { get; set; }
      internal int DataIndex { get; set; }
    }

    // Xan - adding in actual spell parser to pull in current THJ spells vs the abbreviated list packaged with the parser
    public enum THJSpellClasses
    {
        WAR = 1, CLR, PAL, RNG, SHD, DRU, MNK, BRD, ROG, SHM, NEC, WIZ, MAG, ENC, BST, BER
    }

    public enum THJSpellClassesLong
    {
        Warrior = 1, Cleric, Paladin, Ranger, ShadowKnight, Druid, Monk, Bard, Rogue, Shaman,
        Necro, Wizard, Mage, Enchanter, Beastlord, Berserker
    }

    [Flags]
    public enum THJSpellClassesMask
    {
        WAR = 1, CLR = 2, PAL = 4, RNG = 8, SHD = 16, DRU = 32, MNK = 64, BRD = 128, ROG = 256,
        SHM = 512, NEC = 1024, WIZ = 2048, MAG = 4096, ENC = 8192, BST = 16384, BER = 32768,
        ALL = 65535
    }

    [Flags]
    public enum THJSpellClassesMaskLong
    {
        Warrior = 1, Cleric = 2, Paladin = 4, Ranger = 8, ShadowKnight = 16, Druid = 32, Monk = 64, Bard = 128, Rogue = 256,
        Shaman = 512, Necro = 1024, Wizard = 2048, Mage = 4096, Enchanter = 8192, Beastlord = 16384, Berserker = 32768,
        ALL = 65535
    }

    public enum THJSpellEffect
    {
        Current_HP = 0,
        AC = 1,
        ATK = 2,
        Movement_Speed = 3,
        STR = 4,
        DEX = 5,
        AGI = 6,
        STA = 7,
        WIS = 8,
        INT = 9,
        CHA = 10,
        Melee_Haste = 11,
        Unstable_Invis = 12,
        See_Invis = 13,
        Enduring_Breath = 14,
        Current_Mana_Repeating = 15,
        Pacify = 18,
        Blind = 20,
        Stun = 21,
        Charm = 22,
        Fear = 23,
        Bind = 25,
        Gate = 26,
        Dispel = 27,
        Unstable_Undead_Invis = 28,
        Mesmerize = 31,
        Summon_Item = 32,
        Summon_Pet = 33,
        Disease_Counter = 35,
        Poison_Counter = 36,
        Twincast_Blocker = 39,
        Invulnerability = 40,
        Shadowstep = 42,
        Delayed_Heal_Marker = 44,
        Fire_Resist = 46,
        Cold_Resist = 47,
        Poison_Resist = 48,
        Disease_Resist = 49,
        Magic_Resist = 50,
        Rune = 55,
        Levitate = 57,
        Illusion = 58,
        Damage_Shield = 59,
        Identify = 61,
        Memory_Blur = 63,
        Stun_Spin = 64,
        Reclaim_Pet = 68,
        HP_Buff = 69,
        Summon_Skeleton_Pet = 71,
        Feign_Death = 74,
        Current_HP_Non_Repeating = 79,
        Resurrect = 81,
        Summon_Player = 82,
        Teleport = 83,
        Add_Melee_Proc_v85 = 85,
        Assist_Radius = 86,
        Evacuate = 88,
        Summon_Corpse = 91,
        Hate = 92,
        Silence = 96,
        Mana_Buff = 97,
        Melee_Haste_v2 = 98,        
        Root = 99,
        Current_HP_Repeating = 100,
        Donals_Heal = 101,
        Translocate = 104,
        All_Resists = 111,
        Summon_Mount = 113,
        Hate_Mod = 114,
        Curse_Counter = 116,
        Singing_Amplification = 118,
        Melee_Haste_v119 = 119,
        Spell_Damage = 124, // the 10 other variants don't use "focus" wording
        Spell_Healing_Focus = 125,
        Spell_Resist_Mod = 126,
        Spell_Haste_Focus = 127,
        Spell_Duration_Focus = 128,
        Spell_Range_Focus = 129,
        Spell_Hate_Focus = 130,
        Reagent_Use_Chance = 131,
        Spell_Mana_Cost_Focus = 132,
        Stun_Time_Mod = 133,
        Current_HP_Percent = 147,
        Stacking_Blocker = 148,
        Divine_Intervention_With_Heal = 150,
        Suspend_Pet = 151,
        Summon_Swarm_Pet = 152,
        Dispel_Detrimental = 154,
        Reflect_Spell = 158,
        Spell_Rune = 161,
        Melee_Rune = 162,
        Absorb_Hits = 163,
        Pet_Power_Mod = 167,
        Melee_Mitigation = 168,
        Critical_Hit_Chance = 169,
        Critical_Nuke_Damage = 170,
        Crippling_Blow_Chance = 171,
        Avoid_Melee_Chance = 172,
        Riposte_Chance = 173,
        Dodge_Chance = 174,
        Parry_Chance = 175,
        Melee_Resource_Tap = 178,
        Spell_Resist_Chance = 180, // sanctification
        Weapon_Delay = 182,
        Hit_Chance = 184,
        Hit_Damage = 185,
        Min_Hit_Damage = 186,
        Block_Chance = 188,
        Endurance_Repeating = 189,
        Inhibit_Combat = 191,
        Hate_Repeating = 192,
        Skill_Attack = 193,
        Cancel_All_Aggro = 194,
        Stun_Resist_Chance = 195,
        Taunt = 199,
        Worn_Proc_Rate = 200,
        Add_Range_Proc = 201,
        Rampage = 205,
        AE_Taunt = 206,
        AE_Attack = 211,
        Frenzied_Devastation = 212,
        Slay_Undead = 219,
        Weapon_Damage_Bonus = 220,
        Back_Block_Chance = 222,
        Double_Riposte_Skill = 223,
        Additional_Riposte_Skill = 224,
        Double_Attack_Skill = 225,
        Persistent_Casting_AA = 229, // cast through stun
        Divine_Intervention = 232,
        Food_Consumption = 233,
        Reclaim_Pet_v2 = 241,
        Lung_Capacity = 246,
        Frontal_Backstab_Chance = 252,
        Frontal_Backstab_Min_Damage = 253,
        Shroud_Of_Stealth = 256,
        Triple_Backstab_Chance = 258,
        Combat_Stability = 259, // ac soft cap. AA and a few shaman spells
        Instrument_Mod = 260,
        Worn_Attrib_Cap = 262,
        No_Fizzle = 265,
        Song_Range = 270,
        Innate_Movement_Speed = 271, // AA
        Critical_DoT_Chance = 273,
        Critical_Heal_Chance = 274,
        Flurry = 279,
        Double_Special_Attack_Chance = 283, // monk specials
        Spell_Damage_Bonus = 286,
        Spell_Duration_Focus_v287 = 287,
        Add_AA_Proc = 288,
        Cast_On_Duration_Fade = 289,
        Movement_Speed_Cap = 290,
        Purify = 291,
        Frontal_Stun_Resist_Chance = 293, // AA
        Critical_Nuke_Chance = 294,
        Spell_Damage_Taken_v296 = 296,
        Spell_Damage_Taken_v297 = 297,
        Ranged_Damage = 301,
        Spell_Damage_v302 = 302,
        Spell_Damage_v303 = 303,
        Avoid_Riposte_Chance = 304,
        Damage_Shield_Taken = 305,
        Suspend_Minion = 308,
        Teleport_To_Bind = 309,
        Reduce_Timer = 310,
        Invis = 314,
        Undead_Invis = 315,
        Critical_HoT_Chance = 319,
        Shield_Block = 320,
        Targets_Target_Hate = 321,
        Gate_to_Home_City = 322,
        Defensive_Proc = 323,
        Blood_Magic = 324,
        Critical_Hit_Damage = 330,
        Salvage_Components_Chance = 331,
        Summon_To_Corpse = 332,
        Block_Matching_Spell = 335,
        XP_Gain_Mod = 337,
        Spell_Proc = 339,
        Trigger_Spell_Single = 340,
        Interrupt_Casting = 343,
        Shield_Equip_Hate_Mod = 349, // AA
        Mana_Burn = 350,
        Summon_Aura = 351,
        Aura_Slots = 353,
        Silence_With_Limits = 357,
        Current_Mana = 358,
        Cast_On_Death = 361,
        Triple_Attack = 364,
        Cast_On_Killshot = 365,
        Group_Shielding = 366,
        Corruption_Counter = 369,
        Corruption_Resist = 370,
        Melee_Delay = 371,
        Foraging_Skill_Cap = 372,
        Trigger_Spell_On_Fade = 373,
        Trigger_Spell = 374,
        Critical_DoT_Damage = 375,
        Fling = 376,
        Cast_If_Not_Cured = 377,
        Resist_Other_Effect = 378,
        Directional_Shadowstep = 379,
        PushBackUp = 380,
        Fling_To_Self = 381,
        Inhibit_Effect = 382,
        Cast_On_Spell = 383,
        Fling_To_Target = 384,
        Limit_Group = 385,
        Cast_On_Curer = 386,
        Cast_On_Cured = 387,
        Summon_All_Corpses = 388,
        Reset_Recast_Timer = 389,
        Lockout_Recast_Timer = 390,
        Limit_Max_Mana = 391,
        Healing_Bonus = 392,
        Healing_Taken_Pct = 393,
        Healing_Taken = 394,
        Critical_Incoming_Heal_Chance = 395,
        Base_Healing = 396,
        Pet_Melee_Mitigation = 397,
        Pet_Duration = 398,
        Twincast_Chance = 399,
        Heal_From_Mana = 400,
        Ignite_Mana = 401,
        Ignite_Endurance = 402,
        Limit_Spell_Class = 403,
        Limit_Spell_Subclass = 404,
        Staff_Block_Chance = 405,
        Cast_On_Max_Hits_Used = 406,
        Cast_On_Spell_Land_v407 = 407,
        Cap_HP = 408,
        Cap_Mana = 409,
        Cap_End = 410,
        Limit_Player_Class = 411,
        Limit_Race = 412,
        Song_Effectiveness = 413,
        Limit_Casting_Skill = 414,
        Limit_Item_Class = 415,
        AC_v416 = 416,
        Current_Mana_Repeating_v417 = 417,
        Weapon_Damage_Bonus_v418 = 418,
        Add_Melee_Proc_v419 = 419,
        Max_Hits = 420,
        Max_Hits_Counter = 421,
        Limit_Max_Hits_Min = 422,
        Limit_Max_Hits_Type = 423,
        Gravitate = 424,
        Fly = 425,
        Cast_On_Skill_Use = 427,
        Add_Skill_Proc = 429,
        Teleport_to_Caster_Anchor = 437,
        Teleport_to_Player_Anchor = 438,
        Lock_Aggro = 444,
        Buff_Blocker_A = 446,
        Buff_Blocker_B = 447,
        Buff_Blocker_C = 448,
        Buff_Blocker_D = 449,
        Absorb_DoT_Damage = 450,
        Absorb_Melee_Damage = 451,
        Absorb_Spell_Damage = 452,
        Spell_Resource_Tap = 457,
        Faction_Hit = 458,
        Hit_Damage_v2 = 459,
        Spell_Damage_v461 = 461,
        Spell_Damage_v462 = 462,
        Trigger_Spell_Group_Single = 469,
        Trigger_Spell_Group = 470,
        Repeat_Melee_Round_Chance = 471,
        Pet_Critical_Hit_Damage = 474,
        Cast_On_Spell_Land_v481 = 481,
        Spell_Damage_Taken_v483 = 483,
        Spell_Damage_Taken_v484 = 484,
        Critical_Hit_Damage_NonStacking = 496,
        Spell_Haste_Focus_v500 = 500,
        Spell_Haste_Focus_Abs = 501,
        Spell_Damage_v507 = 507,
        Spell_Damage_v508 = 508,
        Incoming_Resist_Mod = 510,
        Current_Mana_Percent = 522,
        Current_End_Percent = 524,
        Current_HP_Percent_Tick = 525,
        Current_Mana_Percent_Tick = 526,
        Current_End_Percent_Tick = 527,
    }

    public enum THJSpellSkill
    {
        Hit = -1, // weapons/archery/backstab/frenzy/kick/etc..
        _1H_Blunt = 0,
        _1H_Slash = 1,
        _2H_Blunt = 2,
        _2H_Slash = 3,
        Abjuration = 4,
        Alteration = 5,
        Apply_Poison = 6,
        Archery = 7,
        Backstab = 8,
        Bind_Wound = 9,
        Bash = 10,
        Block = 11,
        Brass = 12,
        Channeling = 13,
        Conjuration = 14,
        Defense = 15,
        Disarm = 16,
        Disarm_Traps = 17,
        Divination = 18,
        Dodge = 19,
        Double_Attack = 20,
        Dragon_Punch = 21,
        Dual_Wield = 22,
        Eagle_Strike = 23,
        Evocation = 24,
        Feign_Death = 25,
        Flying_Kick = 26,
        Forage = 27,
        Hand_to_Hand = 28,
        Hide = 29,
        Kick = 30,
        Meditate = 31,
        Mend = 32,
        Offense = 33,
        Parry = 34,
        Pick_Lock = 35,
        _1H_Pierce = 36,
        Riposte = 37,
        Round_Kick = 38,
        Safe_Fall = 39,
        Sense_Heading = 40,
        Singing = 41,
        Sneak = 42,
        Specialize_Abjure = 43,
        Specialize_Alteration = 44,
        Specialize_Conjuration = 45,
        Specialize_Divination = 46,
        Specialize_Evocation = 47,
        Pick_Pockets = 48,
        Stringed = 49,
        Swimming = 50,
        Throwing = 51,
        Tiger_Claw = 52,
        Tracking = 53,
        Wind = 54,
        Fishing = 55,
        Poison_Making = 56,
        Tinkering = 57,
        Research = 58,
        Alchemy = 59,
        Baking = 60,
        Tailoring = 61,
        Sense_Traps = 62,
        Blacksmithing = 63,
        Fletching = 64,
        Brewing = 65,
        Alcohol_Tolerance = 66,
        Begging = 67,
        Jewelry_Making = 68,
        Pottery = 69,
        Percussion = 70,
        Intimidation = 71,
        Berserking = 72,
        Taunt = 73,
        Frenzy = 74,
        Remove_Trap = 75,
        Triple_Attack = 76,
        _2H_Pierce = 77,
        Melee = 98, // generic melee hit that doesn't get scaled up like weapon skills 
        Harm_Touch = 105,
        Lay_Hands = 107,
        Slam = 111,
        Inspect_Chest = 114,
        Open_Chest = 115,
        Reveal_Trap_Chest = 116,
    }

    // These are actually itemtypes
    public enum THJSpellInstrument
    {
        Woodwind = 23,
        Strings = 24,
        Brass = 25,
        Percussion = 26,
        Singing = 50,
        All = 51
    }

    public enum THJSpellWornAttribCap
    {
        Base_Stats = -1,
        STR = 0,
        STA = 1,
        AGI = 2,
        DEX = 3,
        WIS = 4,
        INT = 5,
        CHA = 6,
        Magic_Resist = 7,
        Cold_Resist = 8,
        Fire_Resist = 9,
        Poison_Resist = 10,
        Disease_Resist = 11,
        Corruption_Resist = 13
    }

    public enum THJSpellResist
    {
        None = -1, // beneficial spells        
        Unresistable = 0, // Unresistable except for SPA 180
        Magic = 1,
        Fire = 2,
        Cold = 3,
        Poison = 4,
        Disease = 5,
        Lowest = 6, // Chromatic/lowest
        Average = 7, // Prismatic/average
        Physical = 8,
        Corruption = 9
    }

    public enum THJSpellBodyType
    {
        Humanoid = 1,
        Werewolf = 2,
        Undead = 3,
        Giant = 4,
        Golem = 5,
        Extraplanar = 6,
        UndeadPet = 8,
        Raid_Giant = 9,
        Vampire = 12,
        Atenha_Ra = 13,
        Greater_Akheva = 14,
        Khati_Sha = 15,
        Seru = 16,
        Draz_Nurakk = 18,
        Zek = 19,
        Luggald = 20,
        Animal = 21,
        Insect = 22,
        Elemental = 24,
        Plant = 25,
        Dragon = 26,
        Summoned = 28,
        Familiar = 31,
        Muramite = 34
    }

    // https://github.com/EQEmu/Server/blob/06dfba3c81fd8617f6e5838ec447d01c6def1819/common/races.h
    public enum THJSpellRaceType
    {
        Human = 1,
        Barbarian = 2,
        Erudite = 3,
        Wood_Elf = 4,
        High_Elf = 5,
        Dark_Elf = 6,
        Half_Elf = 7,
        Dwarf = 8,
        Troll = 9,
        Ogre = 10,
        Halfling = 11,
        Gnome = 12,
        Gnoll = 39,
        Lizard_Man = 51,
        Grimling = 202,
        Shissar = 217,
        Akheva = 230,
        Seru = 236,
        Kyv = 409,
        Drake = 432,
        Sporali = 456,
        Vampire = 466, // mayong related
        Minotaur = 574,
        Wereorc = 579,
        Wyvern = 581,
    }

    public enum THJSpellTarget
    {
        Line_of_Sight = 1,
        Caster_AE = 2,
        Caster_Group = 3,
        Caster_PB = 4,
        Single = 5,
        Self = 6,
        Target_AE = 8,
        Animal = 9,
        Undead = 10,
        Summoned = 11,
        Lifetap = 13,
        Pet = 14,
        Corpse = 15,
        Plant = 16,
        Old_Giants = 17,
        Old_Dragons = 18,
        Target_AE_Lifetap = 20,
        Target_AE_Undead = 21,
        Target_AE_Summoned = 25,
        Hatelist = 32,
        Hatelist2 = 33,
        Chest = 34,
        Special_Muramites = 35, // bane for Ingenuity group trial in MPG
        Caster_PB_Players = 36,
        Caster_PB_NPC = 37,
        Pet2 = 38,
        No_Pets = 39, // single/group/ae ?
        Caster_AE_Players = 40, // bard AE hits all players
        Target_Group = 41,
        Directional_AE = 42,
        Single_In_Group = 43,
        Frontal_AE = 44,
        Target_Ring_AE = 45,
        Targets_Target = 46,
        Pet_Owner = 47,
        Target_AE_No_Players_Pets = 50, // blanket of forgetfullness. beneficial, AE mem blur, with max targets
        Single_Ally_or_Self = 51, // target player or self if target is a mob
        Single_Ally_or_TT = 52, // splashes your allied target or enemy's target in a deluge of healing waters
    }

    // these are found as type 39 in the dbstr file
    public enum THJSpellTargetRestrict
    {
        Caster = 3, // (any NPC with mana) guess
        Not_On_Horse = 5, // guess
        Animal_or_Humanoid = 100,
        Dragon = 101,
        Animal_or_Insect = 102,
        Animal = 104,
        Plant = 105,
        Giant = 106,
        Not_Animal_or_Humanoid = 108,
        Bixie = 109,
        Harpy = 110,
        Gnoll = 111,
        Sporali = 112,
        Kobald = 113,
        Shade = 114,
        Drakkin = 115,
        Animal_or_Plant = 117,
        Summoned = 118,
        Fire_Pet = 119,
        Undead = 120,
        Living = 121,
        Fairy = 122,
        Humanoid = 123,
        Undead_HP_Less_Than_10_Percent = 124,
        Clockwork_HP_Less_Than_45_Percent = 125,
        Wisp_HP_Less_Than_10_Percent = 126,
        Melee_Class_Except_Bard = 127,
        Pure_Melee_Class = 128,
        Pure_Caster_Class = 129,
        Hybrid_Class = 130,
        Warrior = 131,
        Cleric = 132,
        Paladin = 133,
        Ranger = 134,
        ShadowKnight = 135,
        Druid = 136,
        Monk = 137,
        Bard = 138,
        Rogue = 139,
        Shaman = 140,
        Necro = 141,
        Wizard = 142,
        Mage = 143,
        Enchanter = 144,
        Beastlord = 145,
        Berserker = 146,
        Not_Warrior_Paladin_or_ShadowKnight = 148,
        Not_Raid_Boss = 190,
        Raid_Boss = 191,
        HP_Less_Than_5_Percent = 199, // duple of 501
        HP_Above_75_Percent = 201,
        HP_Less_Than_20_Percent = 203, // dupe of 504
        HP_Less_Than_50_Percent = 204, // duple of 510
        HP_Less_Than_75_Percent = 205,
        Not_In_Combat = 216,
        At_Least_1_Pet_On_Hatelist = 221,
        At_Least_2_Pets_On_Hatelist = 222,
        At_Least_3_Pets_On_Hatelist = 223,
        At_Least_4_Pets_On_Hatelist = 224,
        At_Least_5_Pets_On_Hatelist = 225,
        At_Least_6_Pets_On_Hatelist = 226,
        At_Least_7_Pets_On_Hatelist = 227,
        At_Least_8_Pets_On_Hatelist = 228,
        At_Least_9_Pets_On_Hatelist = 229,
        At_Least_10_Pets_On_Hatelist = 230,
        At_Least_11_Pets_On_Hatelist = 231,
        At_Least_12_Pets_On_Hatelist = 232,
        At_Least_13_Pets_On_Hatelist = 233,
        At_Least_14_Pets_On_Hatelist = 234,
        At_Least_15_Pets_On_Hatelist = 235,
        At_Least_16_Pets_On_Hatelist = 236,
        At_Least_17_Pets_On_Hatelist = 237,
        At_Least_18_Pets_On_Hatelist = 238,
        At_Least_19_Pets_On_Hatelist = 239,
        At_Least_20_Pets_On_Hatelist = 240,
        HP_Less_Than_35_Percent = 250, // duple of 507
        Between_1_To_2_Pets_On_Hatelist = 260,
        Between_3_To_5_Pets_On_Hatelist = 261,
        Between_6_To_9_Pets_On_Hatelist = 262,
        Between_10_To_14_Pets_On_Hatelist = 263,
        More_Than_14_Pets_On_Hatelist = 264,
        Between_3_To_6_Pets_On_Hatelist = 265,
        More_Than_6_Pets_On_Hatelist = 266,
        Chain_Plate_Classes = 304,
        HP_Between_5_and_9_Percent = 350,
        HP_Between_10_and_14_Percent = 351,
        HP_Between_15_and_19_Percent = 352,
        HP_Between_20_and_24_Percent = 353,
        HP_Between_25_and_29_Percent = 354,
        HP_Between_30_and_34_Percent = 355,
        HP_Between_30_and_39_Percent = 356,
        HP_Between_40_and_44_Percent = 357,
        HP_Between_40_and_49_Percent = 358,
        HP_Between_50_and_54_Percent = 359,
        HP_Between_50_and_59_Percent = 360,
        HP_Between_5_and_15_Percent = 398,
        HP_Between_15_and_25_Percent = 399,
        HP_Between_1_and_25_Percent = 400,
        HP_Between_25_and_35_Percent = 401,
        HP_Between_35_and_45_Percent = 402,
        HP_Between_45_and_55_Percent = 403,
        HP_Between_55_and_65_Percent = 404,
        HP_Between_65_and_75_Percent = 405,
        HP_Between_75_and_85_Percent = 406,
        HP_Between_85_and_95_Percent = 407,
        HP_Above_45_Percent = 408,
        HP_Above_55_Percent = 409,
        HP_Above_99_Percent = 412,
        Mana_Above_10_Percent = 429,
        //Has_Mana = 412, // guess based on Suppressive Strike
        HP_Below_5_Percent = 501,
        HP_Below_10_Percent = 502,
        HP_Below_15_Percent = 503,
        HP_Below_20_Percent = 504,
        HP_Below_25_Percent = 505,
        HP_Below_30_Percent = 506,
        HP_Below_35_Percent = 507,
        HP_Below_40_Percent = 508,
        HP_Below_45_Percent = 509,
        HP_Below_50_Percent = 510,
        HP_Below_55_Percent = 511,
        HP_Below_60_Percent = 512,
        HP_Below_65_Percent = 513,
        HP_Below_70_Percent = 514,
        HP_Below_75_Percent = 515,
        HP_Below_80_Percent = 516,
        HP_Below_85_Percent = 517,
        HP_Below_90_Percent = 518,
        HP_Below_95_Percent = 519,
        Mana_Below_X_Percent = 521, // 5?
        End_Below_40_Percent = 522,
        Mana_Below_40_Percent = 523,
        HP_Above_20_Percent = 524,
        Humanoid2 = 601,
        Werewolf2 = 602,
        Undead2 = 603, // vampiric too? Celestial Contravention Strike
        Giants2 = 604,
        Constructs2 = 605,
        Extraplanar = 606,
        Summoned2 = 607,
        UndeadPet = 608,
        KaelGiant = 609,
        Coldain = 610,
        Summoned3 = 624,
        Warders = 628,
        VeeshanDragon = 630,
        Not_Undead_Or_Summoned = 635,
        Not_Plant = 636,
        Not_Player = 700,
        Not_Pet = 701,
        Level_Above_42 = 800,
        Treant = 815,
        Bixie2 = 816,
        Scarecrow = 817,
        Vampire_or_Undead = 818,
        Not_Vampire_or_Undead = 819,
        Knight_Hybrid_Melee_Classes = 820,
        Warrior_Caster_Priest_Classes = 821,
        End_Below_21_Percent = 825,
        End_Below_25_Percent = 826,
        End_Below_29_Percent = 827,
        Regular_Server = 836,
        Progression_Server = 837,
        GoD_Unlocked = 839,
        Humanoid_Level_84_Max = 842,
        Humanoid_Level_86_Max = 843,
        Humanoid_Level_88_Max = 844,
        Has_Crystallized_Flame_Buff = 845,
        Has_Incendiary_Ooze_Buff = 847,
        Level_90_Max = 860,
        Level_92_Max = 861,
        Level_94_Max = 862,
        Level_95_Max = 863,
        Level_97_Max = 864,
        Level_99_Max = 865,
        Level_100_Max = 869,
        Level_102_Max = 870,
        Level_104_Max = 871,
        Level_105_Max = 872,
        Level_107_Max = 873,
        Level_109_Max = 874,
        Level_110_Max = 875,
        Level_112_Max = 876,
        Level_114_Max = 877,
        Has_TBL_Esianti_Access = 997,
        Has_Item_Clockwork_Scraps = 999,
        Between_Level_1_and_75 = 1000,
        Between_Level_76_and_85 = 1001,
        Between_Level_86_and_95 = 1002,
        Between_Level_96_and_105 = 1003,
        HP_Less_Than_80_Percent = 1004,
        Level_Above_34 = 1474,
        In_Two_Handed_Stance = 2000,
        In_Dual_Wield_Handed_Stance = 2001,
        In_Shield_Stance = 2002,
        Not_In_Two_Handed_Stance = 2010,
        Not_In_Dual_Wield_Handed_Stance = 2011,
        Not_In_Shield_Stance = 2012,
        Level_46_Max = 2761,
        No_Mana_Burn_Buff = 8450,
        Male_Toon_Plate_User = 11044,
        Male_Toon_Druid_Enchanter_Magician_Necromancer_Shaman_or_Wizard = 11090,
        Male_Toon_Beastlord_Berserker_Monk_Ranger_or_Rogue = 11209,
        Female_Toon_Plate_User = 11210,
        Female_Toon_Druid_Enchanter_Magician_Necromancer_Shaman_or_Wizard = 11211,
        Female_Toon_Beastlord_Berserker_Monk_Ranger_or_Rogue = 11248,
        No_Illusions_of_Grandeur_Buff = 12519,
        HP_Above_50_Percent = 16010,
        HP_Under_50_Percent = 16031,
        No_Shroud_of_Prayer_Buff = 32339,
        Mana_Below_20_Percent = 38311,
        Mana_Above_50_Percent = 38312,
        Missing_Achievement_Legendary_Answerer = 39281,
        Completed_Achievement_Legendary_Answerer = 42280,
        Summoned_or_Undead = 49326,
        Caster_Priest_Classes = 49529,
        // melee classes must be below 30% endurance and classes that use mana must be below 30% mana
        Melee_Class_End_Below_30_Percent_or_Mana_Class_Mana_Below_30_Percent = 49573,
        Bard_Class = 49574,
        Not_Bard_Class = 49575,
        No_Furious_Rampage_Buff = 49612,
        Mana_Below_30_Percent = 49809,
        No_Harmonious_Precision_Buff = 50003,
        No_Harmonious_Expanse_Buff = 50009
    }

    public enum THJSpellZoneRestrict
    {
        None = 0,
        Outdoors = 1,
        Indoors = 2
    }

    // these are found as type 11 in the dbstr file
    public enum THJSpellIllusion
    {
        Gender_Change = -1,
        Male = (-1 << 16) + 1000,
        Female = (-1 << 16) + 2000,
        Human = 1,
        Barbarian = 2,
        Erudite = 3,
        Wood_Elf = 4,
        High_Elf = 5,
        Dark_Elf = 6,
        Half_Elf = 7,
        Dwarf = 8,
        Troll = 9,
        Ogre = 10,
        Halfling = 11,
        Gnome = 12,
        Old_Aviak = 13,
        Old_Werewolf = 14,
        Old_Brownie = 15,
        Old_Centaur = 16,
        Ice_Giant = 18,
        Trakanon = 19,
        Venril_Sathir = 20,
        Old_Evil_Eye = 21,
        Old_Kerran = 23,
        Froglok = 27,
        Old_Gargoyle = 29,
        Gelatinous_Cube = 31,
        Old_Rat = 36,
        Old_Spider = 38,
        Old_Gnoll = 39,
        Old_Wolf = 42,
        Black_Spirit_Wolf = (42 << 16) + 3001,
        White_Spirit_Wolf = (42 << 16) + 3002,
        Old_Bear = 43,
        Polar_Bear = (43 << 16) + 2,
        Freeport_Militia = 44,
        Imp = 46,
        Old_Kobold = 48,
        Lizard_Man = 51,
        Mimic = 52,
        Old_Orc = 54,
        Old_Drachnid = 57,
        Solusek_Ro = 58,
        Tunare = 62,
        Tiger = 63,
        Mayong = 65,
        Ralos_Zek = 66,
        Elemental = 75,
        Earth_Elemental = 75 << 16,
        Fire_Elemental = (75 << 16) + 3001,
        Water_Elemental = (75 << 16) + 3002,
        Air_Elemental = (75 << 16) + 3003,
        Old_Scarecrow = 82,
        Old_Skeleton = 85,
        Old_Drake = 89,
        Old_Alligator = 91,
        Dwarven_Guard = 94,
        Old_Cazic_Thule = 95,
        Cockatrice = 96,
        Old_Vampire = 98,
        Old_Amygdalan = 99,
        Old_Dervish = 100,
        Tadpole = 102,
        Old_Kedge = 103,
        Mammoth = 107,
        Wasp = 109,
        Mermaid = 110,
        Seahorse = 116,
        Ghost = 118,
        Sabertooth = 119,
        Spirit_Wolf = 120,
        Gorgon = 121,
        Old_Dragon = 122,
        Innoruuk = 123,
        Unicorn = 124,
        Pegasus = 125,
        Djinn = 126,
        Invisible_Man = 127,
        Iksar = 128,
        Vah_Shir = 130,
        Old_Sarnak = 131,
        Old_Drolvarg = 133,
        Mosquito = 134,
        Rhinoceros = 135,
        Xalgoz = 136,
        Kunark_Goblin = 137,
        Yeti = 138,
        Iksar2 = 139,
        Kunark_Giant = 140,
        Nearby_Object = 142,
        Tree = 143,
        Erollisi_Marr = 150,
        Tribunal = 151,
        Bristlebane = 153,
        Sarnak_Skeleton = 155,
        Old_Iksar_Skeleton = 161,
        Old_Raptor = 163,
        Snow_Rabbit = 176,
        Walrus = 177,
        Geonid = 178,
        Tizmak = 181,
        Coldain = 183,
        Coldain_Citizen = (183 << 16) + 2,
        Hag = 185,
        Othmir = 190,
        Ulthork = 191,
        Sea_Turtle = 194,
        Shik_Nar = 199,
        Rockhopper = 200,
        Underbulk = 201,
        Old_Grimling = 202,
        Worm = 203,
        Shadel = 205,
        Owlbear = 206,
        Rhino_Beetle = 207,
        Earth_Elemental2 = 209,
        Air_Elemental2 = 210,
        Water_Elemental2 = 211,
        Fire_Elemental2 = 212,
        Thought_Horror = 214,
        Shissar = 217,
        Fungal_Fiend = 218,
        Stonegrabber = 220,
        Zelniak = 222,
        Lightcrawler = 223,
        Shadow = 224,
        Sunflower = 225,
        Sun_Revenant = 226,
        Shrieker = 227,
        Galorian = 228,
        Netherbian = 229,
        Akheva = 230,
        Wretch = 235,
        Guard = 239,
        Hraquis = 261,
        Nightmare_Gargoyle = 280,
        Tormentor = 285,
        Animated_Armor = 323,
        Arachnid = 326,
        Guktan = 330,
        Troll_Pirate = 331,
        Male_Troll_Pirate = (331 << 16) + 1000,
        Female_Troll_Pirate = (331 << 16) + 2000,
        Gnome_Pirate = 338,
        Male_Gnome_Pirate = (338 << 16) + 1000,
        Female_Gnome_Pirate = (338 << 16) + 2000,
        Dark_Elf_Pirate = 339,
        Male_Dark_Elf_Pirate = (339 << 16) + 1000,
        Female_Dark_Elf_Pirate = (339 << 16) + 2000,
        Ogre_Pirate = 340,
        Male_Ogre_Pirate = (340 << 16) + 1000,
        Female_Ogre_Pirate = (340 << 16) + 2000,
        Human_Pirate = 341,
        Male_Human_Pirate = (341 << 16) + 1000,
        Female_Human_Pirate = (341 << 16) + 2000,
        Erudite_Pirate = 342,
        Male_Erudite_Pirate = (342 << 16) + 1000,
        Female_Erudite_Pirate = (342 << 16) + 2000,
        Troll_Zombie = 344,
        Froglok_Skeleton = 349,
        Undead_Froglok = 350,
        Veksar = 353,
        Scaled_Wolf = 356,
        Vampire = 360,
        Nightrage_Orphan = (360 << 16) + 1,
        Skeleton = 367,
        Drybone_Skeleton = (367 << 16) + 3001,
        Frostbone_Skeleton = (367 << 16) + 3002,
        Firebone_Skeleton = (367 << 16) + 3003,
        Scorched_Skeleton = (367 << 16) + 3004,
        Mummy = 368,
        Froglok_Ghost = 371,
        Shade = 373,
        Golem = 374,
        Ice_Golem = (374 << 16) + 3001,
        Crystal_Golem = (374 << 16) + 3003,
        Jokester = 384,
        Nihil = 385,
        Trusik = 386,
        Hynid = 388,
        Turepta = 389,
        Cragbeast = 390,
        Stonemite = 391,
        Ukun = 392,
        Ikaav = 394,
        Aneuk = 395,
        Kyv = 396,
        Noc = 397,
        Ra_tuk = 398,
        Taneth = 399,
        Huvul = 400,
        Mutna = 401,
        Mastruq = 402,
        Taelosian = 403,
        Mata_Muram = 406,
        Lightning_Warrior = 407,
        Feran = 410,
        Pyrilen = 411,
        Chimera = 412,
        Dragorn = 413,
        Murkglider = 414,
        Rat = 415,
        Bat = 416,
        Gelidran = 417,
        Girplan = 419,
        Crystal_Shard = 425,
        Dervish = 431,
        Drake = 432,
        Goblin = 433,
        Solusek_Goblin = (433 << 16) + 3001,
        Dagnor_Goblin = (433 << 16) + 3002,
        Valley_Goblin = (433 << 16) + 3003,
        Aqua_Goblin = (433 << 16) + 3007,
        Goblin_King = (433 << 16) + 3008,
        Rallosian_Goblin = (433 << 16) + 3011,
        Frost_Goblin = (433 << 16) + 3012,
        Kirin = 434,
        Basilisk = 436,
        Puma = 439,
        Domain_Prowler = (439 << 16) + 3009,
        Spider = 440,
        Snow_Spider = (440 << 16) + 4,
        Spider_Queen = 441,
        Animated_Statue = 442,
        Lava_Spider = 450,
        Lava_Spider_Queen = 451,
        Dragon_Egg = 445,
        Werewolf = 454,
        White_Werewolf = (454 << 16) + 3002,
        Kobold = 455,
        Kobold_King = (455 << 16) + 3002,
        Sporali = 456,
        Violet_Sporali = (456 << 16) + 3002,
        Azure_Sporali = (456 << 16) + 3011,
        Gnomework = 457,
        Orc = 458,
        Bloodmoon_Orc = (458 << 16) + 3004,
        Drachnid = 461,
        Drachnid_Cocoon = 462,
        Fungus_Patch = 463,
        Gargoyle = 464,
        Runed_Gargoyle = (464 << 16) + 3001,
        Undead_Shiliskin = 467,
        Armored_Shiliskin = (467 << 16) + 3005,
        Snake = 468,
        Evil_Eye = 469,
        Minotaur = 470,
        Zombie = 471,
        Clockwork_Boar = 472,
        Fairy = 473,
        Tree_Fairy = (473 << 16) + 3001,
        Witheran = 474,
        Air_Elemental3 = 475,
        Earth_Elemental3 = 476,
        Fire_Elemental3 = 477,
        Water_Elemental3 = 478,
        Alligator = 479,
        Bear = 480,
        Wolf = 482,
        Spectre = 485,
        Banshee = 487,
        Banshee2 = 488,
        Elddar = 489,
        Bone_Golem = 491,
        Scrykin = 495,
        Treant = 496, // or izon
        Regal_Vampire = 497,
        Floating_Skull = 512,
        Totem = 514,
        Bixie_Drone = 520,
        Bixie_Queen = (520 << 16) + 3002,
        Centaur = 521,
        Centaur_Warrior = (521 << 16) + 3003,
        Drakkin = 522,
        Gnoll = 524,
        Undead_Gnoll = (524 << 16) + 3001,
        Mucktail_Gnoll = (524 << 16) + 3002,
        Gnoll_Reaver = (524 << 16) + 3003,
        Blackburrow_Gnoll = (524 << 16) + 3004,
        Satyr = 529,
        Dragon = 530,
        Hideous_Harpy = 527,
        Satyr2 = 529,
        Dragon2 = 530,
        Goo = 549,
        Aviak = 558,
        Beetle = 559,
        Death_Beetle = (559 << 16) + 3001,
        Kedge = 561,
        Kerran = 562,
        Shissar2 = 563,
        Siren = 564,
        Siren_Sorceress = (564 << 16) + 3001,
        Plaguebringer = 566,
        Combine_Human = (566 << 16) + 3,
        Bokon = (566 << 16) + 9,
        Hooded_Plaguebringer = (566 << 16) + 1007,
        Hooded_Plaguebringer2 = (566 << 16) + 2007, // Potentially Female specific vs 1007 Male specific
        Brownie = 568,
        Brownie_Noble = (568 << 16) + 2, // can be either based on toons gender
        Male_Brownie_Noble = (568 << 16) + 1002,
        Female_Brownie_Noble = (568 << 16) + 2002,
        Steam_Suit = 570,
        Ghoul = 571,
        Embattled_Minotaur = 574,
        Scarecrow = 575,
        Shade2 = 576,
        Steamwork = 577,
        Tyranont = 578,
        Worg = 580,
        Wyvern = 581,
        Elven_Ghost = 587,
        Burynai = 602,
        Dracolich = 604,
        Iksar_Ghost = 605,
        Iksar_Skeleton = 606,
        Mephit = 607,
        Muddite = 608,
        Raptor = 609,
        Sarnak = 610,
        Scorpion = 611,
        Plague_Fly = 612,
        Burning_Nekhon = 614,
        Shadow_Nekhon = (614 << 16) + 3001,
        Crystal_Hydra = 615,
        Crystal_Sphere = 616,
        Vitrik = 620,
        Bellikos = 638,
        Cliknar = 643,
        Ant = 644,
        New_Coldain = 645,
        Restless_Coldain = (645 << 16) + 4,
        Crystal_Sessiloid = 647,
        Telmira = 653,
        Flood_Telmira = (653 << 16) + 3002,
        Morell_Thule = 658,
        Marionette = 659,
        Book_Dervish = 660,
        Topiary_Lion = 661,
        Rotdog = 662,
        Amygdalan = 663,
        Sandman = 664,
        Grandfather_Clock = 665,
        Gingerbread_Man = 666,
        Royal_Guardian = 667,
        Rabbit = 668,
        Gouzah_Rabbit = (668 << 16) + 3003,
        Polka_Dot_Rabbit = (668 << 16) + 3005,
        Cazic_Thule = 670,
        Selyrah = 686,
        Goral = 687,
        Braxi = 688,
        Kangon = 689,
        Undead_Thelasa = 695,
        Thel_Ereth_Ril = (695 << 16) + 3021,
        Swinetor = 696,
        Swinetor_Necro = (696 << 16) + 3001,
        Triumvirate = 697,
        Hadal = 698,
        Hadal_Templar = (698 << 16) + 3002,
        Alaran_Ghost = 708,
        Holgresh = 715,
        Ratman = 718,
        Fallen_Knight = 719,
        Akhevan = 722,
        Tirun = 734,
        Bixie = 741,
        Bixie_Soldier = (741 << 16) + 2,
        Butterfly = 742,
        Book_Minion = 760,
        Broom_Minion = 761,
        Clockwork_Gnome = 763,
        Arc_Worker = 766,
        Relifed_Skeleton = 768,
        Cursed_Siren = 769,
        Tyrannosaurus = 771,
        Ankylosaurus = 774,
        Thaell_Ew = 785,
        Drolvarg = 843,
        Snow_Kitten = (845 << 16) + 3012,
        Earth_Elemental4 = 855,
        Air_Elemental4 = 856,
        Water_Elemental4 = 857,
        Fire_Elemental4 = 858,
        Aalishai_Efreeti = (862 << 16) + 2,
        Male_Djinn = (862 << 16) + 1000,
        Duende = (862 << 16) + 1001,
        Male_Efreeti = (862 << 16) + 1002,
        Female_Djinn = (862 << 16) + 2000,
        Ondine = (862 << 16) + 2001,
        Female_Efreeti = (862 << 16) + 2002,
        CoV_Ulthork = 871,
        Proudheart_Hare = (872 << 16) + 3005,
        Trueheart_Hare = (872 << 16) + 3006,
        Boundless_Heart_Hare = (872 << 16) + 3007,
        ToL_Shade = 895,
        Grimling = 905,
        Stitchwork_Lion = 918,
    }

    // these are found as type 45 in the dbstr file
    //public enum SpellFaction
    //{
    //    SoF_SHIP_Workshop = 1178,
    //    SoD_Kithicor_Good = 1204, // army of light
    //    SoD_Kithicor_Evil = 1205, // army of obliteration
    //    SoD_Ancient_Iksar = 1229
    //}

    public enum THJSpellMaxHits
    {
        None = 0,
        Incoming_Hit_Attempts = 1, // incoming melee attempts (prior to success checks)
        Outgoing_Hit_Attempts = 2, // outgoing melee attempts of Skill type (prior to success checks)
        Incoming_Spells = 3,
        Outgoing_Spells = 4,
        Outgoing_Hit_Successes = 5,
        Incoming_Hit_Successes = 6,
        Matching_Spells = 7, // mostly outgoing, sometimes incoming (puratus) matching limits
        Incoming_Hits_Or_Spells = 8,
        Reflected_Spells = 9,
        Defensive_Proc_Casts = 10,
        Offensive_Proc_Casts = 11,
        // 2015-7-22 patch:
        // Damage shield damage is now considered magical non-melee damage; this means that melee guard and melee threshold guard 
        // spell effects will no longer negate damage shield damage. Rune, spell guard, spell threshold guard, and spells that 
        // allow you to absorb damage as mana will continue to block damage shield damage.
        Incoming_Hits_Or_Spells_Or_DS = 13,
        // Type 14 (which these buffs have been updated to use) will only consume charges when the attacker's melee attack 
        // matches the skill that is improved by the buff. This should correct the issue where special attacks are consuming 
        // limited use counters on buffs that improve the standard 7 weapon skills.
        Outgoing_Hits_Affected_By_Buff = 14,
        Tradeskill_Combines = 15
    }

    public enum THJSpellTeleport
    {
        Primary_Anchor = 52584,
        Secondary_Anchor = 52585,
        Guild_Anchor = 50874
    }

    // it's hard to stick many spells into a single category, but i think this is only used by SPA 403
    public enum THJSpellCategory
    {
        Cures = 2,
        Offensive_Damage = 3, // nukes, DoT, AA discs, and spells that cast nukes as a side effect
        Heals = 5,
        Lifetap = 6,
        Transport = 8
    }

    // some common spell reagents 
    public enum THJSpellReagent
    {
        CLASS_3_Wood_Silver_Tip_Arrow = 8658,
        Tiny_Jade_Inlaid_Coffin = 9962,
        Essence_Emerald = 9963,
        Fire_Beetle_Eye = 10307,
        Black_Pearl = 10012,
        Malachite = 10015,
        Bloodstone = 10019,
        Jasper = 10020,
        Rose_Quartz = 10021,
        Amber = 10022,
        Jade = 10023,
        Pearl = 10024,
        Topaz = 10025,
        Cats_Eye_Agate = 10026,
        Peridot = 10028,
        Emerald = 10029,
        Opal = 10030,
        Fire_Opal = 10031,
        Star_Ruby = 10032,
        Fire_Emerald = 10033,
        Sapphire = 10034,
        Ruby = 10035,
        Black_Sapphire = 10036,
        Diamond = 10037,
        Fuligan_Soulstone_of_Innoruuk = 10092,
        Cloudy_Stone_of_Veeshan = 10094,
        Encyclopedia_Necrotheurgia = 11571,
        Plains_Pebble = 12832,
        Hand_Drum = 13000,
        Wooden_Flute = 13001,
        Lute = 13011,
        Horn = 13012,
        Mandolin = 13013,
        Bat_Wing = 13068,
        Snake_Scales = 13070,
        Bone_Chips  = 13073,
        Fish_Scales = 13076,
        Tiny_Dagger = 13080,
        Raw_Diamond = 15981,
        Silver_Bar = 16500,
        Electrum_Bar = 16501,
        Gold_Bar = 16502,
        Platinum_Bar = 16503,
        Poison_Vial = 16965,
        Jade_Inlaid_Coffin = 17355,
        Grandmasters_Satchel = 17900,
        Velium_Bar = 22098,
        Blue_Diamond = 22503,
        Black_Ceremonial_Coffin = 28880,
        Axe_of_the_Annihilator = 52673,
        Axe_of_the_Decimator = 52708,
        Axe_of_the_Eradicator = 52816,
        Axe_of_the_Savage = 57263,
        Lesser_Scrying_Stone = 59654,
        Scrying_Stone = 59655,
        Greater_Scrying_Stone = 59656,
        Purified_Crystal = 59740,
        Basic_Axe_Components = 59933,
        Axe_Components = 59934,
        Balanced_Components = 59935,
        Crafted_Axe_Components = 59936,
        Masterwork_Axe_Components = 59937,
        Axe_of_the_Destroyer = 59998,
        Axe_of_the_Sunderer = 64950,
        Tainted_Axe_of_Hatred = 68809,
        Corroded_Axe = 69010,
        Blunt_Axe = 69011,
        Steel_Axe = 69012,
        Bearded_Axe = 69013,
        Mithril_Axe = 69014,
        Balanced_War_Axe = 69015,
        Bonesplicer_Axe = 69016,
        Fleshtear_Axe = 69017,
        Cold_Steel_Cleaving_Axe = 69018,
        Mithril_Bloodaxe = 69019,
        Rage_Axe = 69020,
        Bloodseekers_Axe = 69021,
        Battlerage_Axe = 69022,
        Deathfury_Axe = 69023,
        Axe_of_the_Brute = 76500,
        Dreadstone = 83440,
        Alluring_Flute_of_the_Piper = 87020,
        Axe_of_the_Mangler = 93700,
        Axe_of_the_Demolisher = 99780,
        Axe_of_the_Vindicator = 150401,
    }

    [Flags]
    public enum THJSpellInhibitType
    {
        Buff = 1,
        Worn = 2,
        AA = 4
    }

    public sealed class THJSpellSlot
    {
        public int SPA;
        public int Base1;
        public int Base2;
        public int Max;
        public int Calc;
        public string Desc;

        public override string ToString()
        {
            return String.Format("SPA {0} Base1={1} Base2={2} Max={3} Calc={4}", SPA, Base1, Base2, Max, Calc);
        }
    }
    public sealed class THJSpell
    {
        public const int MAX_LEVEL = 125;

        public int ID;
        public int GroupID;
        public string Name;
        public int Icon;
        public int Mana;
        public int Endurance;
        public int EnduranceUpkeep;
        public int DurationTicks;
        public bool Focusable;
        public List<THJSpellSlot> Slots;
        public byte Level; // max level any class can cast (for categorizing by expansion)
        public byte[] Levels; // casting level for each of the 16 classes
        public byte[] ExtLevels; // similar to levels but assigns levels for side effect spells that don't have levels defined (e.g. a proc effect will get the level of it's proc buff)
        public string ClassesLevels;
        public THJSpellClassesMask ClassesMask;
        public THJSpellSkill Skill;
        public bool Beneficial;
        public bool BeneficialBlockable;
        public THJSpellTarget Target;
        public THJSpellResist ResistType;
        public int ResistMod;
        public bool PartialResist;
        public int MinResist;
        public int MaxResist;
        public string Extra;
        public int HateOverride;
        public int HateMod;
        public int Range;
        public int AERange;
        public int AEDuration; // rain spells
        public float CastingTime;
        public float RestTime; // refresh time
        public float RecastTime;
        public float PushBack;
        public float PushUp;
        public int DescID;
        public string Desc;
        public int MaxHits;
        public THJSpellMaxHits MaxHitsType;
        public int MaxTargets;
        public int RecourseID;
        //public string Recourse;
        public int TimerID;
        public int ViralRange;
        public int MinViralTime;
        public int MaxViralTime;
        public THJSpellTargetRestrict TargetRestrict;
        public THJSpellTargetRestrict CasterRestrict;
        public int[] ConsumeItemID;
        public int[] ConsumeItemCount;
        public int[] FocusID;
        public string LandOnSelf;
        public string LandOnOther;
        public string WearsOff;
        public int ConeStartAngle;
        public int ConeEndAngle;
        public bool MGBable;
        public int Rank;
        public bool CastOutOfCombat;
        public THJSpellZoneRestrict Zone;
        public bool DurationFrozen; // in guildhall/lobby
        public bool Dispelable;
        public bool PersistAfterDeath;
        public bool SongWindow;
        public bool CancelOnSit;
        public bool Sneaking;
        public int[] CategoryDescID; // most AAs don't have these set
        public string Deity;
        public int SongCap;
        public int MinRange;
        public int RangeModCloseDist;
        public int RangeModCloseMult;
        public int RangeModFarDist;
        public int RangeModFarMult;
        public bool Interruptable;
        public bool Feedbackable; // triger spell DS
        public bool Reflectable;
        public int SpellClass;
        public int SpellSubclass;
        public bool CastInFastRegen;
        public bool AllowFastRegen;
        public bool BetaOnly;
        public bool CannotRemove;
        public int CritOverride; // when set the spell has this max % crit chance and mod
        public bool CombatSkill;
        public int ResistPerLevel;
        public int ResistCap;
        public bool NoSanctification;
        public List<string> Stacking;
        public int Version; // Int32.Parse(yyyyMMdd) - non standard date format but convenient for comparisons

        public int[] LinksTo;
        public int RefCount; // number of spells that link to this
        public string[] Categories;
        //public string[] RawData;

        public float Unknown;


        /// Effects can reference other spells or items via square bracket notation. e.g.
        /// [Spell 123]    is a reference to spell 123
        /// [Group 123]    is a reference to spell group 123
        /// [Item 123]     is a reference to item 123
        /// [AA 123]     is a reference to AA group 123
        public static readonly Regex SpellRefExpr = new Regex(@"\[Spell\s(\d+)\]");
        public static readonly Regex GroupRefExpr = new Regex(@"\[Group\s(\d+)\]");
        public static readonly Regex ItemRefExpr = new Regex(@"\[Item\s(\d+)\]");
        public static readonly Regex AARefExpr = new Regex(@"\[AA\s(\d+)\]");
        public static readonly Regex FactionRefExpr = new Regex(@"\[Faction\s(\d+)\]");

        public THJSpell()
        {
            Slots = new List<THJSpellSlot>(6); // first grow will make it a list of 12
            Levels = new byte[16];
            ExtLevels = new byte[16];
            ConsumeItemID = new int[4];
            ConsumeItemCount = new int[4];
            FocusID = new int[4];
            CategoryDescID = new int[3];
            Stacking = new List<string>();
        }

    }
    static float ParseFloat(string s)
    {
        if (String.IsNullOrEmpty(s))
            return 0f;
        return Single.Parse(s, CultureInfo.InvariantCulture);
    }
    static int ParseInt(string s)
    {
        if (s == "" || s == "0" || s[0] == '.')
            return 0;
        // strip decimals. i.e. floor()
        s = Regex.Replace(s, @"\..+", "");
        Int32.TryParse(s, out int result);
        return result;
    }
    static bool ParseBool(string s)
    {
        return !String.IsNullOrEmpty(s) && (s != "0");
    }

    public static partial class THJSpellParser
    {
        public static int CalcDuration(int calc, int max, int level = THJSpell.MAX_LEVEL )
        {
            int value = 0;

            switch (calc)
            {
                case 0:
                    value = 0;
                    break;
                case 1:
                    value = level / 2;
                    if (value < 1)
                        value = 1;
                    break;
                case 2:
                    value = (level / 2) + 5;
                    if (value < 6)
                        value = 6;
                    break;
                case 3:
                    value = level * 30;
                    break;
                case 4:
                    value = 50;
                    break;
                case 5:
                    value = 2;
                    break;
                case 6:
                    value = level / 2;
                    break;
                case 7:
                    value = level;
                    break;
                case 8:
                    value = level + 10;
                    break;
                case 9:
                    value = level * 2 + 10;
                    break;
                case 10:
                    value = level * 30 + 10;
                    break;
                case 11:
                    value = (level + 3) * 30;
                    break;
                case 12:
                    value = level / 2;
                    if (value < 1)
                        value = 1;
                    break;
                case 13:
                    value = level * 4 + 10;
                    break;
                case 14:
                    value = level * 5 + 10;
                    break;
                case 15:
                    value = (level * 5 + 50) * 2;
                    break;
                case 50:
                    value = 72000;
                    break;
                case 3600:
                    value = 3600;
                    break;
                default:
                    value = max;
                    break;
            }

            if (max > 0 && value > max)
                value = max;

            return value;
        }
        public static THJSpell ParseSpellTHJ(string[] fields, int version=-1)
			{
            var spell = new THJSpell();
            spell.Version = version;

            // 0 SPELLINDEX
            spell.ID = Convert.ToInt32(fields[0]);
            // 1 SPELLNAME
            spell.Name = fields[1].Trim();
            // 2 ACTORTAG
            // 3 NPC_FILENAME
            spell.Extra = fields[3];
            // 4 CASTERMETXT
            // 5 CASTEROTHERTXT
            // 6 CASTEDMETXT
            spell.LandOnSelf = fields[6];
            // 7 CASTEDOTHERTXT
            spell.LandOnOther = fields[7];
            spell.WearsOff = fields[8];
            // 9 RANGE
            spell.Range = ParseInt(fields[9]);
            // 10 IMPACTRADIUS
            spell.AERange = ParseInt(fields[10]);
            // 11 OUTFORCE
            spell.PushBack = ParseFloat(fields[11]);
            // 12 UPFORCE
            spell.PushUp = ParseFloat(fields[12]);
            // 13 CASTINGTIME
            spell.CastingTime = ParseFloat(fields[13]) / 1000f;
            // 14 RECOVERYDELAY
            spell.RestTime = ParseFloat(fields[14]) / 1000f;
            // 15 SPELLDELAY
            spell.RecastTime = ParseFloat(fields[15]) / 1000f;
				// 16 DURATIONBASE
				// 17 DURATIONCAP
            spell.DurationTicks = CalcDuration(ParseInt(fields[16]), ParseInt(fields[17]));
            // 18 IMPACTDURATION
            spell.AEDuration = ParseInt(fields[18]);
            // 19 MANACOST
            spell.Mana = ParseInt(fields[19]);
            // 20 BASEAFFECT1 .. BASEAFFECT12
            // 32 BASE_EFFECT2_1 .. BASE_EFFECT2_12
            // 44 AFFECT1CAP .. AFFECT12CAP
            // 56 IMAGENUMBER
            // 57 MEMIMAGENUMBER
            // 58 EXPENDREAGENT1 .. 61 EXPENDREAGENT4
            // 62 EXPENDQTY1 .. 65 EXPENDQTY4
            // 66 NOEXPENDREAGENT1 .. 69 NOEXPENDREAGENT4
            for (int i = 0; i < 3; i++)
            {
                spell.ConsumeItemID[i] = ParseInt(fields[58 + i]);
                spell.ConsumeItemCount[i] = ParseInt(fields[62 + i]);
                spell.FocusID[i] = ParseInt(fields[66 + i]);
            }
            // 82 LIGHTTYPE
            // 83 BENEFICIAL
            spell.Beneficial = ParseBool(fields[83]);
            // 84 ACTIVATED
            // 85 RESISTTYPE
            spell.ResistType = (THJSpellResist)ParseInt(fields[85]);
            // 86 SPELLAFFECT1 .. SPELLAFFECT12
            // 98 TYPENUMBER
            spell.Target = (THJSpellTarget)ParseInt(fields[98]);
            // 99 BASEDIFFICULTY = fizzle?
            // 100 CASTINGSKILL
            spell.Skill = (THJSpellSkill)ParseInt(fields[100]);
            // 101 ZONETYPE
            spell.Zone = (THJSpellZoneRestrict)ParseInt(fields[101]);
            // 102 ENVIRONMENTTYPE
            // 103 TIMEOFDAY
            // 104 WARRIORMIN .. BERSERKERMIN
            for (int i = 0; i < spell.Levels.Length; i++)
                spell.Levels[i] = (byte)ParseInt(fields[104 + i]);
            // 120 CASTINGANIM
            // 121 TARGETANIM
            // 122 TRAVELTYPE
            // 123 SPELLAFFECTINDEX
            // 124 CANCELONSIT
            spell.CancelOnSit = ParseBool(fields[124]);
            // 125 DIETY_AGNOSTIC .. 141 DIETY_VEESHAN
            string[] gods = {
                "Agnostic", "Bertox", "Brell", "Cazic", "Erollisi", "Bristlebane", "Innoruuk", "Karana", "Mithanial", "Prexus",
                "Quellious", "Rallos", "Rodcet", "Solusek", "Tribunal", "Tunare", "Veeshan" };
            for (int i = 0; i < gods.Length; i++)
                if (ParseBool(fields[125 + i]))
                    spell.Deity += gods[i] + " ";
            // 142 NPC_NO_CAST
            // 143 AI_PT_BONUS
            // 144 NEW_ICON
            spell.Icon = ParseInt(fields[144]);
            // 145 SPELL_EFFECT_INDEX
            // 146 NO_INTERRUPT
            spell.Interruptable = !ParseBool(fields[146]);
            // 147 RESIST_MOD
            spell.ResistMod = ParseInt(fields[147]);
            // 148 NOT_STACKABLE_DOT
            // 149 DELETE_OK
            // 150 REFLECT_SPELLINDEX
            spell.RecourseID = ParseInt(fields[150]);
            // 151 NO_PARTIAL_SAVE = used to prevent a nuke from being partially resisted. it also prevents or allows a player to resist a spell fully if they resist "part" of its components.
            spell.PartialResist = ParseBool(fields[151]);
            // 152 SMALL_TARGETS_ONLY
            // 153 USES_PERSISTENT_PARTICLES
            // 154 BARD_BUFF_BOX
            spell.SongWindow = ParseBool(fields[154]);
            // 155 DESCRIPTION_INDEX
            spell.DescID = ParseInt(fields[155]);
            // 156 PRIMARY_CATEGORY
            spell.CategoryDescID[0] = ParseInt(fields[156]);
            // 157 SECONDARY_CATEGORY_1
            spell.CategoryDescID[1] = ParseInt(fields[157]);
            // 158 SECONDARY_CATEGORY_2
            spell.CategoryDescID[2] = ParseInt(fields[158]);
            // 159 NO_NPC_LOS - NPC Does not Require LoS
            // 160 FEEDBACKABLE - Triggers spell damage shield. This is mostly used on procs and non nukes, so it's not that useful to show
            spell.Feedbackable = ParseBool(fields[160]);
            // 161 REFLECTABLE
            spell.Reflectable = ParseBool(fields[161]);
            // 162 HATE_MOD
            spell.HateMod = ParseInt(fields[162]);
            // 163 RESIST_PER_LEVEL
            spell.ResistPerLevel = ParseInt(fields[163]);
            // 164 RESIST_CAP
            spell.ResistCap = ParseInt(fields[164]);
            // 165 AFFECT_INANIMATE - Can be cast on objects
            // 166 STAMINA_COST
            spell.Endurance = ParseInt(fields[166]);
            // 167 TIMER_INDEX
            spell.TimerID = ParseInt(fields[167]);
            // 168 IS_SKILL
            spell.CombatSkill = ParseBool(fields[168]);
            // 169 ATTACK_OPENING
            // 170 DEFENSE_OPENING
            // 171 SKILL_OPENING
            // 172 NPC_ERROR_OPENING
            // 173 SPELL_HATE_GIVEN
            spell.HateOverride = ParseInt(fields[173]);
            // 174 ENDUR_UPKEEP
            spell.EnduranceUpkeep = ParseInt(fields[174]);
            // 175 LIMITED_USE_TYPE
            spell.MaxHitsType = (THJSpellMaxHits)ParseInt(fields[175]);
            // 176 LIMITED_USE_COUNT
            spell.MaxHits = ParseInt(fields[176]);
            // 177 PVP_RESIST_MOD
            // 178 PVP_RESIST_PER_LEVEL
            // 179 PVP_RESIST_CAP
            // 180 GLOBAL_GROUP
            // 181 PVP_DURATION
            // 182 PVP_DURATION_CAP
            // 183 PCNPC_ONLY_FLAG
            // 184 CAST_NOT_STANDING
            // 185 CAN_MGB
            spell.MGBable = ParseBool(fields[185]);
            // 186 NO_DISPELL
            spell.Dispelable = !ParseBool(fields[186]);
            // 187 NPC_MEM_CATEGORY
            // 188 NPC_USEFULNESS
            // 189 MIN_RESIST
            spell.MinResist = ParseInt(fields[189]);
            // 190 MAX_RESIST
            spell.MaxResist = ParseInt(fields[190]);
            // 191 MIN_SPREAD_TIME
            spell.MinViralTime = ParseInt(fields[191]);
            // 192 MAX_SPREAD_TIME
            spell.MaxViralTime = ParseInt(fields[192]);
            // 193 DURATION_PARTICLE_EFFECT
            // 194 CONE_START_ANGLE
            spell.ConeStartAngle = ParseInt(fields[194]);
            // 195 CONE_END_ANGLE
            spell.ConeEndAngle = ParseInt(fields[195]);
            // 196 SNEAK_ATTACK
            spell.Sneaking = ParseBool(fields[196]);
            // 197 NOT_FOCUSABLE
            spell.Focusable = !ParseBool(fields[197]);
            // 198 NO_DETRIMENTAL_SPELL_AGGRO
            // 199 SHOW_WEAR_OFF_MESSAGE
            // 200 IS_COUNTDOWN_HELD
            spell.DurationFrozen = ParseBool(fields[200]);
            // 201 SPREAD_RADIUS
            spell.ViralRange = ParseInt(fields[201]);
            // 202 BASE_EFFECTS_FOCUS_CAP
            spell.SongCap = ParseInt(fields[202]);
            // 203 STACKS_WITH_SELF
            // 204 NOT_SHOWN_TO_PLAYER
            // 205 NO_BUFF_BLOCK
            spell.BeneficialBlockable = !ParseBool(fields[205]);
            // 206 ANIM_VARIATION
            // 207 SPELL_GROUP
            spell.GroupID = ParseInt(fields[207]);
            // 208 SPELL_GROUP_RANK
            spell.Rank = ParseInt(fields[208]); // rank 1/5/10. a few auras do not have this set properly
            // 209 NO_RESIST - ignore SPA 180 resist?
            spell.NoSanctification = ParseBool(fields[209]);
            // 210 ALLOW_SPELLSCRIBE
            // 211 SPELL_REQ_ASSOCIATION_ID
            spell.TargetRestrict = (THJSpellTargetRestrict)ParseInt(fields[211]);
            // 212 BYPASS_REGEN_CHECK
            spell.AllowFastRegen = ParseBool(fields[212]);
            // 213 CAN_CAST_IN_COMBAT
            spell.CastOutOfCombat = !ParseBool(fields[213]);
            // 214 CAN_CAST_OUT_OF_COMBAT
            // 215 SHOW_DOT_MESSAGE
            // 216 INVALID
            // 217 OVERRIDE_CRIT_CHANCE
            spell.CritOverride = ParseInt(fields[217]);
            // 218 MAX_TARGETS
            spell.MaxTargets = ParseInt(fields[218]);
            // 219 NO_HEAL_DAMAGE_ITEM_MOD
            // 220 CASTER_REQUIREMENT_ID
            spell.CasterRestrict = (THJSpellTargetRestrict)ParseInt(fields[220]);
            // 221 SPELL_CLASS
            // 222 SPELL_SUBCLASS
            // 223 AI_VALID_TARGETS
            // 224 NO_STRIP_ON_DEATH
            spell.PersistAfterDeath = ParseBool(fields[224]);
            // 225 BASE_EFFECTS_FOCUS_SLOPE
            // 226 BASE_EFFECTS_FOCUS_OFFSET
            // 227 DISTANCE_MOD_CLOSE_DIST
            spell.RangeModCloseDist = ParseInt(fields[227]);
            // 228 DISTANCE_MOD_CLOSE_MULT
            spell.RangeModCloseMult = ParseInt(fields[228]);
            // 229 DISTANCE_MOD_FAR_DIST
            spell.RangeModFarDist = ParseInt(fields[229]);
            // 230 DISTANCE_MOD_FAR_MULT
            spell.RangeModFarMult = ParseInt(fields[230]);
            // 231 MIN_RANGE
            spell.MinRange = ParseInt(fields[231]);
            // 232 NO_REMOVE
            spell.CannotRemove = ParseBool(fields[232]);
            // 233 SPELL_RECOURSE_TYPE
            // 234 ONLY_DURING_FAST_REGEN
            spell.CastInFastRegen = ParseBool(fields[234]);
            // 235 IS_BETA_ONLY
            spell.BetaOnly = ParseBool(fields[235]);
            // 236 SPELL_SUBGROUP
            spell.SpellSubclass = ParseInt(fields[236]);

            // each spell has 12 effect slots which have 5 attributes each
            // 20..31 - slot 1..12 base1 effect
            // 32..43 - slot 1..12 base2 effect
            // 44..55 - slot 1..12 max effect
            // 70..81 - slot 1..12 calc forumla data
            // 86..97 - slot 1..12 spa/type
            for (int i = 0; i < 12; i++)
            {
                int spa = ParseInt(fields[86 + i]);
                int calc = ParseInt(fields[70 + i]);
                int max = ParseInt(fields[44 + i]);
                int base1 = ParseInt(fields[20 + i]);
                int base2 = ParseInt(fields[32 + i]);

                // unused slot, no more to follow
                // if (spa == 254)
                //    break;

                // unused slot, but there are more to follow
                //if (desc == null)
                //{
                //    spell.Slots.Add(null);
                //    continue;
                //}

                spell.Slots.Add(new THJSpellSlot
                {
                    SPA = spa,
                    Base1 = base1,
                    Base2 = base2,
                    Max = max,
                    Calc = calc,
                });
            }

            return spell;
        }
        public static int GetLowestLevel( THJSpell s )
        {   int min = -1;
            for( int i=0; i < 16; i++ )
                min = ( (int)s.Levels[i] > 0 && s.Levels[i] != 255) ? Math.Min( (int)s.Levels[i], min ) : min;
            return min == -1 ? 0 : min;
        }
    }
  }

}