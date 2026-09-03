namespace PackProphet.Sync;

using System.Text.Json;
using System.Text.Json.Serialization;

using PackProphet.State;

/// <summary>
/// What a merge did, so the UI can say it rather than silently reshaping the user's collection.
/// </summary>
/// <param name="Conflicts">
/// Edits to the same thing on both devices since they last agreed. Not errors -- every one of
/// them was resolved -- but the only cases where a resolution rule had to choose, so they are the
/// only ones worth reporting.
/// </param>
public sealed record MergeReport(int Conflicts, List<string> Notes)
{
    public static MergeReport None => new(0, []);
    public bool Clean => Conflicts == 0;
}

public sealed record MergeResult(AppState State, MergeReport Report);

/// <summary>
/// Collects conflicts as they are resolved. Adding one REQUIRES saying what it was, which is the
/// whole reason this exists rather than a counter.
///
/// It replaced a `ref int` beside a separate list of notes, and those two could disagree: nine of
/// the resolution sites bumped the count and none of them wrote a note, so a merge that resolved a
/// changed rarity plan or a changed profile name reported "1 conflict" with nothing to show for it.
/// The settings page then rendered "Both devices had changed the same things:" above an empty list.
/// Making the description part of recording a conflict means that state cannot be constructed.
/// </summary>
internal sealed class Conflicts
{
    private readonly List<string> _notes = [];

    public int Count { get; private set; }

    public void Add(string note)
    {
        Count++;
        _notes.Add(note);
    }

    /// <summary>Several conflicts of one kind, described once. Nothing recorded for none of them.</summary>
    public void Add(int howMany, string note)
    {
        if (howMany <= 0) return;

        Count += howMany;
        _notes.Add(note);
    }

    public MergeReport Report() => new(Count, _notes);
}

/// <summary>
/// Three-way merge of two copies of the state against the last one both devices agreed on.
///
/// Three-way rather than last-writer-wins, because last-writer-wins is not sync: log a pack on
/// your phone, tick two cards on your laptop, and whichever device happened to push second erases
/// the other's afternoon. With a common ancestor, "changed here" and "changed there" can be told
/// apart from "changed nowhere", which resolves almost everything without asking.
///
/// The ancestor is whatever this device last pushed or pulled, kept beside the state
/// (see SyncService). A device with no ancestor -- freshly paired -- cannot three-way merge, and
/// <see cref="FirstPair"/> handles that case separately and more conservatively.
///
/// Rules, in one place because they are the whole trust story:
///
///   Collection counts   per card. One side changed it, that side wins. Both changed it, the
///                       HIGHER count wins -- a tracker that forgets a card you own is worse than
///                       one that shows a card you sold, and the second is visible where the first
///                       is not.
///   Pack and Wonder log per row id. A row missing from one side is a row that side has not seen
///                       yet, never a deletion -- absence cannot delete, because a device that
///                       has not synced in a week is absent from most of the week. A deletion is
///                       said out loud instead, as an id in RemovedLog, and those union: once a
///                       row is deleted anywhere it stays deleted everywhere.
///   Decks, chase lists  per id. Added on either side, kept. Deleted on one side and untouched on
///                       the other, deleted. Edited on both, THIS device wins and it is reported.
///   Everything else     per field, this device wins a genuine conflict, except resource pools and
///                       lifetime totals, which carry their own timestamp and take the newer.
///   Prefs, active       never synced. Grid columns and theme belong to the device you are
///                       holding, not to the collection.
/// </summary>
public static class StateMerge
{
    /// <summary>
    /// Does this state hold nothing a user put there? No cards, no logged packs or Wonder Picks,
    /// no decks and no chase lists, in any collection.
    ///
    /// Not a merge rule -- a safety rail. Every lossy branch below reads "the ancestor had it and
    /// this side does not" as a deletion, which is right for a state the user emptied and
    /// catastrophic for one that was never loaded. The two are identical by value, so the caller
    /// has to know it is holding real state before merging; this is how it says the difference out
    /// loud when the answer is suspicious.
    ///
    /// Preferences and the profile list are deliberately not consulted: a device with three named
    /// collections and nothing in any of them still has nothing to lose.
    ///
    /// A recorded deletion does count, and has to. Resetting a collection now empties the logs for
    /// real rather than having them handed back, so a device that has just been reset looks
    /// exactly like one whose state never loaded -- except for the tombstones, which are the
    /// user's own deliberate act and the one thing an unloaded state cannot have.
    /// </summary>
    public static bool NothingRecorded(AppState state) =>
        state.Profiles.All(p =>
            p.Collection.Count == 0
            && p.PackLog.Count == 0
            && p.WonderLog.Count == 0
            && p.Decks.Count == 0
            && p.ChaseLists.Count == 0
            && p.RemovedLog.Count == 0);

    /// <summary>
    /// Merge remote into local, given the ancestor both were last known to share.
    /// </summary>
    public static MergeResult Merge(AppState local, AppState remote, AppState ancestor)
    {
        var conflicts = new Conflicts();

        var ids = new List<string>();
        foreach (var p in local.Profiles) ids.Add(p.Id);
        foreach (var p in remote.Profiles) if (!ids.Contains(p.Id)) ids.Add(p.Id);

        var merged = new List<Profile>();
        foreach (var id in ids)
        {
            var l = local.Profiles.FirstOrDefault(p => p.Id == id);
            var r = remote.Profiles.FirstOrDefault(p => p.Id == id);
            var b = ancestor.Profiles.FirstOrDefault(p => p.Id == id);

            // Present in the ancestor and gone from one side: a real deletion, honoured -- unless
            // the other side edited it since, in which case the edit is the more recent intent and
            // deleting would throw it away.
            if (l is null && r is not null)
            {
                if (b is not null && Same(b, r)) continue;          // deleted here, untouched there
                merged.Add(r);
                continue;
            }
            if (r is null && l is not null)
            {
                if (b is not null && Same(b, l)) continue;          // deleted there, untouched here
                merged.Add(l);
                continue;
            }
            if (l is null || r is null) continue;

            merged.Add(MergeProfile(l, r, b, conflicts));
        }

        // Every device would otherwise have to keep at least one profile; if a merge somehow
        // empties the list, the local copy is better than nothing.
        if (merged.Count == 0) merged = local.Profiles;

        var active = merged.Any(p => p.Id == local.ActiveProfileId)
            ? local.ActiveProfileId
            : merged[0].Id;

        // Prefs and the active profile stay local on purpose: see the class comment.
        var state = local with { Profiles = merged, ActiveProfileId = active };

        return new MergeResult(state, conflicts.Report());
    }

    /// <summary>
    /// The first pull after pairing, where there is no ancestor to compare against.
    ///
    /// Treating an empty ancestor as "everything changed on both sides" would be correct for the
    /// per-key rules and wrong for deletions, which is why this is separate: with no ancestor,
    /// nothing can be a deletion, so the result is the union of both devices. That is the
    /// forgiving direction, and the one a user expects when joining two collections they built
    /// independently.
    /// </summary>
    public static MergeResult FirstPair(AppState local, AppState remote) =>
        Merge(local, remote, AppState.Fresh() with { Profiles = [] });

    private static Profile MergeProfile(
        Profile local, Profile remote, Profile? ancestor, Conflicts conflicts)
    {
        var b = ancestor;

        // Every note names the collection it happened in. With one profile that is redundant, and
        // with an alt account it is the only thing that makes the sentence actionable.
        var where = local.Name;

        var collisions = 0;
        var collection = MergeCounts(
            local.Collection, remote.Collection, b?.Collection, ref collisions);
        conflicts.Add(collisions,
            $"{collisions} card {(collisions == 1 ? "count" : "counts")} in {where} changed on " +
            "both devices; kept the higher one.");

        var deckClashes = 0;
        var decks = MergeById(
            local.Decks, remote.Decks, b?.Decks, d => d.Id, ref deckClashes);
        conflicts.Add(deckClashes,
            $"{deckClashes} {(deckClashes == 1 ? "deck was" : "decks were")} edited on both " +
            $"devices in {where}; kept this device's version.");

        var listClashes = 0;
        var lists = MergeById(
            local.ChaseLists, remote.ChaseLists, b?.ChaseLists, w => w.Id, ref listClashes);
        conflicts.Add(listClashes,
            $"{listClashes} chase {(listClashes == 1 ? "list was" : "lists were")} edited on both " +
            $"devices in {where}; kept this device's version.");

        // Deletions union, and are applied to both logs: a row deleted on either device is gone
        // from the result even when the other device still holds it, which is the whole point of
        // recording the deletion rather than inferring it from absence.
        var removed = new HashSet<string>(local.RemovedLog, StringComparer.Ordinal);
        removed.UnionWith(remote.RemovedLog);

        return local with
        {
            // A device that never renamed the collection should not outvote one that did.
            Name = PickRef(local.Name, remote.Name, b?.Name, string.Equals,
                           conflicts, "The collection name", where,
                           unset: n => n == Profile.DefaultName),
            Collection = collection,
            Decks = decks,
            ChaseLists = lists,
            PackLog = MergeLog(local.PackLog, remote.PackLog, b?.PackLog, LogId.Of, e => e.At,
                               removed, conflicts, where, "logged pack"),
            WonderLog = MergeLog(local.WonderLog, remote.WonderLog, b?.WonderLog, LogId.Of,
                                 e => e.At, removed, conflicts, where, "Wonder Pick offer"),
            RemovedLog = [.. removed],
            Targets = MergeTargets(local.Targets, remote.Targets, b?.Targets, conflicts, where),
            Resources = MergeResources(local.Resources, remote.Resources, b?.Resources,
                                       conflicts, where),
            // Twenty ordered slots retyped by hand into the game. Merging them entry by entry
            // would produce a board that matches neither device and would have to be retyped
            // again, so it moves as one value.
            TradeBoard = PickRef(local.TradeBoard, remote.TradeBoard, b?.TradeBoard, SameList,
                                 conflicts, "The trade board", where,
                                 unset: board => board.Count == 0),
            Lifetime = Newer(local.Lifetime, remote.Lifetime),
            InGameName = PickText(local.InGameName, remote.InGameName, b?.InGameName,
                                  conflicts, "The in-game name", where),
            WantListId = local.WantListId ?? remote.WantListId,
        };
    }

    // ---- Collection counts ---------------------------------------------------------------

    private static Dictionary<string, int> MergeCounts(
        Dictionary<string, int> local,
        Dictionary<string, int> remote,
        Dictionary<string, int>? ancestor,
        ref int conflicts)
    {
        var result = new Dictionary<string, int>(local);

        foreach (var (key, theirs) in remote)
        {
            var haveMine = local.TryGetValue(key, out var mine);
            ancestor ??= [];
            var haveBase = ancestor.TryGetValue(key, out var was);

            var mineChanged = !haveBase ? haveMine : !haveMine || mine != was;
            var theirsChanged = !haveBase || theirs != was;

            if (!theirsChanged) continue;              // nothing new from them
            if (!mineChanged) { result[key] = theirs; continue; }
            if (haveMine && mine == theirs) continue;  // same edit, both sides

            // Both moved, differently.
            result[key] = Math.Max(mine, theirs);
            conflicts++;
        }

        // A card the remote dropped that the ancestor had, and this device did not touch, is a
        // deletion there and has to be honoured here.
        if (ancestor is not null)
        {
            foreach (var (key, was) in ancestor)
            {
                if (remote.ContainsKey(key)) continue;
                if (!local.TryGetValue(key, out var mine) || mine != was) continue;
                result.Remove(key);
            }
        }

        // Zero and absent mean the same thing to every reader; keeping both makes two states that
        // are equal look different to the next merge.
        foreach (var key in result.Where(kv => kv.Value <= 0).Select(kv => kv.Key).ToList())
            result.Remove(key);

        return result;
    }

    // ---- Logs -------------------------------------------------------------

    /// <summary>
    /// A log, merged row by row on identity, with deleted rows dropped and the result put back in
    /// order.
    ///
    /// This was a plain union on content identity, which was wrong twice over. A row the user
    /// deleted came back from whichever device still had it, so clearing the history undid
    /// itself. And an EDITED row -- redating a pack -- changed its own content identity, so the
    /// other device's copy no longer matched it and the union kept both: one pack, logged twice,
    /// counted twice in every figure on the history page.
    ///
    /// Row ids fix the second, and make the first expressible: a deletion is an id in RemovedLog
    /// rather than an absence, and an absence goes back to meaning "not seen here yet".
    ///
    /// Sorted on the way out, by time and then by id. Not cosmetic: two devices that merged the
    /// same rows in a different order would hold lists that compare unequal, and the merge would
    /// push and re-push a result that never settles.
    /// </summary>
    private static List<T> MergeLog<T>(
        List<T> local, List<T> remote, List<T>? ancestor,
        Func<T, string> id, Func<T, DateTimeOffset> at, HashSet<string> removed,
        Conflicts conflicts, string where, string what)
        where T : class
    {
        // Indexed rather than scanned. MergeById does the same work with FirstOrDefault, which is
        // fine for a dozen decks and quadratic for a log: a pair of devices with three thousand
        // packs each would compare nine million times per sync, on a phone.
        var theirs = ById(remote, id);
        var before = ById(ancestor, id);
        var mineByKey = ById(local, id);

        var clashes = 0;
        var merged = new List<T>(local.Count + remote.Count);

        foreach (var mine in local)
        {
            var key = id(mine);
            var them = theirs.GetValueOrDefault(key);
            var was = before.GetValueOrDefault(key);

            if (them is null)
            {
                // Gone there. Honoured unless this device edited it since -- an edit is the more
                // recent intent, and deleting would throw it away.
                if (was is not null && Same(mine, was)) continue;
                merged.Add(mine);
                continue;
            }

            if (Same(mine, them)) { merged.Add(mine); continue; }

            var mineChanged = was is null || !Same(mine, was);
            var theirsChanged = was is null || !Same(them, was);

            if (!mineChanged) { merged.Add(them); continue; }
            if (!theirsChanged) { merged.Add(mine); continue; }

            merged.Add(mine);
            clashes++;
        }

        foreach (var them in remote)
        {
            var key = id(them);
            if (mineByKey.ContainsKey(key)) continue;

            var was = before.GetValueOrDefault(key);
            if (was is not null && Same(them, was)) continue;    // deleted here, untouched there
            merged.Add(them);
        }

        conflicts.Add(clashes,
            $"{clashes} {(clashes == 1 ? what : what + "s")} in {where} " +
            $"{(clashes == 1 ? "was" : "were")} edited on both devices; kept this device's.");

        return merged.Where(row => !removed.Contains(id(row)))
                     .OrderBy(at).ThenBy(id, StringComparer.Ordinal)
                     .ToList();
    }

    /// <summary>
    /// Rows by id, first one wins. A duplicate id cannot come out of this app -- ids are assigned
    /// on read and on log -- but it can come out of a hand-edited backup, and a merge is not the
    /// place to throw over one.
    /// </summary>
    private static Dictionary<string, T> ById<T>(List<T>? rows, Func<T, string> id)
        where T : class
    {
        var index = new Dictionary<string, T>(rows?.Count ?? 0, StringComparer.Ordinal);
        foreach (var row in rows ?? []) index.TryAdd(id(row), row);
        return index;
    }

    // ---- Keyed collections ---------------------------------------------------------------

    private static List<T> MergeById<T>(
        List<T> local, List<T> remote, List<T>? ancestor, Func<T, string> id, ref int conflicts)
        where T : class
    {
        ancestor ??= [];
        var result = new List<T>();
        var handled = new HashSet<string>();

        foreach (var mine in local)
        {
            var key = id(mine);
            handled.Add(key);

            var theirs = remote.FirstOrDefault(x => id(x) == key);
            var was = ancestor.FirstOrDefault(x => id(x) == key);

            if (theirs is null)
            {
                // Deleted there. Honoured unless this device edited it since.
                if (was is not null && Same(mine, was)) continue;
                result.Add(mine);
                continue;
            }

            if (Same(mine, theirs)) { result.Add(mine); continue; }

            var mineChanged = was is null || !Same(mine, was);
            var theirsChanged = was is null || !Same(theirs, was);

            if (!mineChanged) { result.Add(theirs); continue; }
            if (!theirsChanged) { result.Add(mine); continue; }

            result.Add(mine);
            conflicts++;
        }

        foreach (var theirs in remote)
        {
            var key = id(theirs);
            if (handled.Contains(key)) continue;

            // Absent here. New there, or deleted here -- and a deletion here only stands if they
            // left it alone.
            var was = ancestor.FirstOrDefault(x => id(x) == key);
            if (was is not null && Same(theirs, was)) continue;
            result.Add(theirs);
        }

        return result;
    }

    // ---- Settings ------------------------------------------------------------------------

    private static TargetSettings MergeTargets(
        TargetSettings local, TargetSettings remote, TargetSettings? ancestor,
        Conflicts conflicts, string where)
    {
        var plan = PickMap(local.DefaultPlan, remote.DefaultPlan, ancestor?.DefaultPlan,
                           conflicts, "The default rarity plan", where,
                           unset: TargetSettings.Default.DefaultPlan);

        var bySet = new Dictionary<string, Dictionary<int, int>>(local.PlanBySet);
        foreach (var (set, theirs) in remote.PlanBySet)
        {
            if (!local.PlanBySet.TryGetValue(set, out var mine)) { bySet[set] = theirs; continue; }
            if (SameMap(mine, theirs)) continue;

            var was = ancestor is not null && ancestor.PlanBySet.TryGetValue(set, out var w) ? w : null;
            // No ancestor: the two have never agreed on this set, so there is no way to tell
            // who changed it, and the device the user is holding wins.
            if (was is null || !SameMap(theirs, was) && !SameMap(mine, was))
            {
                conflicts.Add($"The plan for {set} in {where} differs between devices; kept this " +
                              "device's.");
                continue;
            }

            if (SameMap(mine, was)) bySet[set] = theirs;     // only they changed it
        }

        // Overrides the remote removed, that this device left alone.
        if (ancestor is not null)
        {
            foreach (var (set, was) in ancestor.PlanBySet)
            {
                if (remote.PlanBySet.ContainsKey(set)) continue;
                if (!local.PlanBySet.TryGetValue(set, out var mine) || !SameMap(mine, was)) continue;
                bySet.Remove(set);
            }
        }

        return local with { DefaultPlan = plan, PlanBySet = bySet };
    }

    /// <summary>
    /// Pools carry the moment they were true, which is exactly the tiebreak a conflict needs:
    /// a stamina balance read five minutes ago beats one read yesterday, whichever device asked.
    /// Pools with no timestamp fall back to the ordinary rule.
    /// </summary>
    private static Resources MergeResources(
        Resources local, Resources remote, Resources? ancestor,
        Conflicts conflicts, string where) =>
        local with
        {
            Wonder = NewerPool(local.Wonder, remote.Wonder),
            Trade = NewerPool(local.Trade, remote.Trade),
            PackHourglasses = PickValue(local.PackHourglasses, remote.PackHourglasses,
                                        ancestor?.PackHourglasses,
                                        conflicts, "The pack hourglass count", where, unset: 0),
            Shinedust = PickValue(local.Shinedust, remote.Shinedust, ancestor?.Shinedust,
                                  conflicts, "The shinedust total", where, unset: 0),
            Premium = local.Premium || remote.Premium,
            NextFreePackAt = Later(local.NextFreePackAt, remote.NextFreePackAt),
            PackPointsBySet = PickMapByKey(local.PackPointsBySet, remote.PackPointsBySet,
                                           ancestor?.PackPointsBySet, conflicts, where),
        };

    private static ResourcePool NewerPool(ResourcePool local, ResourcePool remote)
    {
        if (local == remote) return local;
        if (remote.AsOf is null) return local;
        if (local.AsOf is null) return remote;
        return remote.AsOf > local.AsOf ? remote : local;
    }

    // ---- Field-level helpers -------------------------------------------------------------

    /// <summary>
    /// The one rule, for a field that moves as a single value: whoever changed it wins, and when
    /// both did, this device wins and the caller is told.
    ///
    /// A missing ancestor means the two copies have never agreed on this field, so there is no
    /// way to tell who changed it -- that reads as a conflict resolved in favour of the device the
    /// user is holding.
    /// </summary>
    private static T PickValue<T>(
        T local, T remote, T? ancestor, Conflicts conflicts, string what, string where,
        T? unset = null)
        where T : struct
    {
        if (local.Equals(remote)) return local;

        if (ancestor is { } was)
        {
            if (local.Equals(was)) return remote;
            if (remote.Equals(was)) return local;
        }
        else if (unset is { } blank)
        {
            // No ancestor, so there is no way to tell who changed it -- but there is a way to tell
            // who never set it. A device joining for the first time has nothing in these fields,
            // and letting its emptiness beat the other device's real figure is not a conflict
            // resolution, it is a deletion.
            if (local.Equals(blank)) return remote;
            if (remote.Equals(blank)) return local;
        }

        conflicts.Add(Note(what, where));
        return local;
    }

    private static T PickRef<T>(
        T local, T remote, T? ancestor, Func<T, T, bool> same,
        Conflicts conflicts, string what, string where,
        Func<T, bool>? unset = null)
        where T : class
    {
        if (same(local, remote)) return local;

        if (ancestor is not null)
        {
            if (same(local, ancestor)) return remote;
            if (same(remote, ancestor)) return local;
        }
        else if (unset is not null)
        {
            if (unset(local)) return remote;
            if (unset(remote)) return local;
        }

        conflicts.Add(Note(what, where));
        return local;
    }

    /// <summary>
    /// Optional text -- an in-game name, a profile label. Null ancestor and null value are the
    /// same thing here (never set), and both mean there is nothing to compare against, so the
    /// value the user can currently see wins.
    /// </summary>
    private static string? PickText(
        string? local, string? remote, string? ancestor,
        Conflicts conflicts, string what, string where)
    {
        if (local == remote) return local;
        if (local is null) return remote;
        if (remote is null) return local;

        if (ancestor is not null)
        {
            if (local == ancestor) return remote;
            if (remote == ancestor) return local;
        }

        conflicts.Add(Note(what, where));
        return local;
    }

    /// <summary>
    /// One sentence, for every field that moves as a single value. Naming the field is the point:
    /// "1 conflict" tells the user nothing they can act on, and "the trade board differs" tells
    /// them exactly where to look.
    /// </summary>
    private static string Note(string what, string where) =>
        $"{what} in {where} differs between devices; kept this device's.";

    private static Dictionary<int, int> PickMap(
        Dictionary<int, int> local, Dictionary<int, int> remote,
        Dictionary<int, int>? ancestor, Conflicts conflicts, string what, string where,
        Dictionary<int, int>? unset = null)
    {
        if (SameMap(local, remote)) return local;

        if (ancestor is not null)
        {
            if (SameMap(local, ancestor)) return remote;
            if (SameMap(remote, ancestor)) return local;
        }
        else if (unset is not null)
        {
            if (SameMap(local, unset)) return remote;
            if (SameMap(remote, unset)) return local;
        }

        conflicts.Add(Note(what, where));
        return local;
    }

    /// <summary>
    /// Per key rather than whole-map, for figures kept independently per set: two devices
    /// recording points for two different sets have not conflicted, and treating the map as one
    /// value would say they had.
    /// </summary>
    private static Dictionary<string, int> PickMapByKey(
        Dictionary<string, int> local, Dictionary<string, int> remote,
        Dictionary<string, int>? ancestor, Conflicts conflicts, string where)
    {
        var result = new Dictionary<string, int>(local);

        foreach (var (key, theirs) in remote)
        {
            if (!local.TryGetValue(key, out var mine)) { result[key] = theirs; continue; }
            if (mine == theirs) continue;

            var was = ancestor is not null && ancestor.TryGetValue(key, out var w) ? w : (int?)null;

            // A zero on a device that has never recorded points for this set is an absence, not
            // a balance of nothing, so it yields.
            if (was is null && (mine == 0 || theirs == 0))
            {
                if (mine == 0) result[key] = theirs;
                continue;
            }

            if (was is null || mine != was && theirs != was)
            {
                conflicts.Add($"The pack points for {key} in {where} differ between devices; " +
                              "kept this device's.");
                continue;
            }

            if (mine == was) result[key] = theirs;        // only they changed it
        }

        return result;
    }

    private static LifetimeTotals? Newer(LifetimeTotals? local, LifetimeTotals? remote)
    {
        if (local is null) return remote;
        if (remote is null) return local;
        return remote.At > local.At ? remote : local;
    }

    private static DateTimeOffset? Later(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b : b is null ? a : (a > b ? a : b);

    // ---- Equality ------------------------------------------------------------------------

    private static bool SameMap<TKey>(Dictionary<TKey, int> a, Dictionary<TKey, int> b)
        where TKey : notnull =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && v == kv.Value);

    private static bool SameList(List<string> a, List<string> b) => a.SequenceEqual(b);

    /// <summary>
    /// Do these two states hold the same thing? Structural, so two copies of one collection built
    /// up in a different order are equal -- which is what lets a sync skip a pointless upload and
    /// a pointless re-render.
    /// </summary>
    public static bool SameState(AppState a, AppState b) => Same(a, b);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Structural comparison, and the foundation every merge decision rests on: "is this still
    /// what the ancestor had" has to be a real question about the contents.
    ///
    /// Records compare by value, but the lists and dictionaries inside them compare by REFERENCE,
    /// so the compiler-generated equality calls two identical decks read off two devices
    /// different -- and a merge built on that would report a conflict on every unchanged row.
    ///
    /// Comparing serialised text would not do either: two dictionaries holding the same entries
    /// serialise in insertion order, and two devices that added the same cards in a different
    /// order would look like they disagreed. So this walks the JSON instead, treating objects as
    /// unordered and arrays as ordered, which is what those two shapes actually mean here.
    /// </summary>
    private static bool Same<T>(T a, T b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;

        using var left = JsonSerializer.SerializeToDocument(a, Json);
        using var right = JsonSerializer.SerializeToDocument(b, Json);
        return Deep(left.RootElement, right.RootElement);
    }

    private static bool Deep(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;

        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
                var count = 0;
                foreach (var prop in a.EnumerateObject())
                {
                    if (!b.TryGetProperty(prop.Name, out var other)) return false;
                    if (!Deep(prop.Value, other)) return false;
                    count++;
                }
                return count == b.EnumerateObject().Count();

            case JsonValueKind.Array:
                var x = a.EnumerateArray();
                var y = b.EnumerateArray();
                while (true)
                {
                    var hasX = x.MoveNext();
                    var hasY = y.MoveNext();
                    if (hasX != hasY) return false;
                    if (!hasX) return true;
                    if (!Deep(x.Current, y.Current)) return false;
                }

            case JsonValueKind.String:
                return a.GetString() == b.GetString();

            case JsonValueKind.Number:
                return a.GetRawText() == b.GetRawText();

            default:
                return true;      // true, false and null are settled by the kind check
        }
    }
}
