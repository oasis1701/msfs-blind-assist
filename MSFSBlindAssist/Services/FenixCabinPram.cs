using Newtonsoft.Json.Linq;

namespace MSFSBlindAssist.Services;

/// <summary>One entry of a FAP list: a boarding-music channel, a prerecorded announcement or a memo entry.</summary>
public sealed record FenixCabinItem(string Id, string Name);

/// <summary>
/// The FAP's audio state (its PRAM, the prerecorded announcement module) plus the announcement variant.
/// <see cref="SelectedChannel"/> is an index into <see cref="Channels"/>; <see cref="Memo"/> keeps the
/// order the Fenix queues the announcements in, and <c>RemoveFromMemo</c> takes an index into it.
/// </summary>
/// <param name="MusicOn">Boarding music is playing.</param>
/// <param name="Channels">The boarding-music channels.</param>
/// <param name="SelectedChannel">Index into <paramref name="Channels"/> of the selected channel.</param>
/// <param name="Volume">The PRAM volume, in the Fenix's own steps.</param>
/// <param name="NowAnnouncing">The name of the announcement playing, or null when none is.</param>
/// <param name="Memo">The queued announcements, in play order.</param>
/// <param name="PlayingAllInMemo">The Fenix is playing the whole memo.</param>
/// <param name="Announcements">The prerecorded announcements the Fenix offers.</param>
/// <param name="PreferredVariant">The announcement variant in effect.</param>
/// <param name="MaxVariant">The highest variant the Fenix has.</param>
public sealed record FenixCabinPramState(
    bool MusicOn,
    IReadOnlyList<FenixCabinItem> Channels,
    int SelectedChannel,
    int Volume,
    string? NowAnnouncing,
    IReadOnlyList<FenixCabinItem> Memo,
    bool PlayingAllInMemo,
    IReadOnlyList<FenixCabinItem> Announcements,
    int PreferredVariant,
    int MaxVariant);

/// <summary>
/// One FAP audio write: the GraphQL document, its variables (an anonymous object, null for a document
/// without any) and where in the answer its <c>success</c> flag sits: <c>fap.pram.&lt;ResultField&gt;</c>,
/// or <c>fap.&lt;ResultField&gt;</c> when <paramref name="UnderPram"/> is false.
/// </summary>
public sealed record FenixCabinMutation(string Query, object? Variables, string ResultField, bool UnderPram);

/// <summary>
/// The FAP's audio side: boarding music, the PRAM volume, prerecorded announcements and their memo, and
/// the announcement variant. Pure: it builds the GraphQL documents and reads the answers; the sending is
/// <see cref="SendAsync"/> over the <see cref="FenixDataService"/>.
/// <para>
/// Evidence (Fenix 3.0.0.7426 FAP bundle; tools/fenix-gateway/fetch_gateway_bundles.py fetches it and
/// lists the document texts in gql-operations.txt): every mutation below is one of the bundle's own
/// documents, operation name and variable names unchanged.
/// Which store action sends which, and with what value, is the bundle's audio store (the music toggle
/// sends the opposite of the current state, the volume buttons send the current volume plus or minus
/// one, the channel combo sends the channel's index) and its announcement store (the memo and play
/// actions; the memo is removed by index, an announcement is added and played by its id). For the
/// announcement variant, increase sends <c>preferredVariant + 1</c> only below <c>maxVariant</c>,
/// decrease <c>preferredVariant - 1</c> only above 0. Those bounds are the caller's, as are the
/// volume's: the bundle sends the volume unclamped, so <see cref="SetVolume"/> does too.
/// </para>
/// <para>
/// The state query is the bundle's two state fragments (<c>GetBoardingMusicState</c>,
/// <c>GetPrerecordedAnnouncementsState</c>) and its <c>GetAnnouncementsVariant</c> in one round trip;
/// the shape of the answer was read live on 2026-10-21.
/// </para>
/// </summary>
public static class FenixCabinPram
{
    /// <summary>The whole audio state in one read: music, volume, announcements, memo and variant.</summary>
    public const string StateQuery =
        "query { fap { pram { state { bgmOn bgmChannels { id name } bgmSelectedChannel volume onAnnounce { name } announcementsMemo { id name } playingAllInMemo prerecordedAnnouncements } } announcementsVariant { preferredVariant maxVariant } } }";

    // ---- State ----------------------------------------------------------------------------------

    /// <summary>
    /// Reads the answer to <see cref="StateQuery"/>; null when it has no <c>fap.pram.state</c> object.
    /// Inside a state, a field of an unexpected type reads as empty, false or 0 and the rest is kept.
    /// Every list entry keeps its position (a channel or memo entry the Fenix sends without an id or a
    /// name is an item with empty text), because <see cref="FenixCabinPramState.SelectedChannel"/> and
    /// the memo's remove index count positions.
    /// </summary>
    public static FenixCabinPramState? Parse(JObject? data)
    {
        var fap = data?["fap"] as JObject;
        if ((fap?["pram"] as JObject)?["state"] is not JObject state) return null;
        var variant = fap?["announcementsVariant"] as JObject;

        return new FenixCabinPramState(
            MusicOn: Bool(state["bgmOn"]),
            Channels: Items(state["bgmChannels"]),
            SelectedChannel: Int(state["bgmSelectedChannel"]),
            Volume: Int(state["volume"]),
            NowAnnouncing: Text((state["onAnnounce"] as JObject)?["name"]),
            Memo: Items(state["announcementsMemo"]),
            PlayingAllInMemo: Bool(state["playingAllInMemo"]),
            Announcements: Items(state["prerecordedAnnouncements"]),
            PreferredVariant: Int(variant?["preferredVariant"]),
            MaxVariant: Int(variant?["maxVariant"]));
    }

    private static IReadOnlyList<FenixCabinItem> Items(JToken? list)
    {
        if (list is not JArray array) return [];
        var items = new List<FenixCabinItem>(array.Count);
        foreach (var entry in array)
        {
            var item = entry as JObject;
            items.Add(new FenixCabinItem(Text(item?["id"]) ?? "", Text(item?["name"]) ?? ""));
        }
        return items;
    }

    private static string? Text(JToken? token)
        => token is JValue { Type: JTokenType.String, Value: string text } ? text : null;

    private static bool Bool(JToken? token)
        => token is JValue { Type: JTokenType.Boolean, Value: true };

    private static int Int(JToken? token)
        => token is JValue { Type: JTokenType.Integer, Value: long n and >= int.MinValue and <= int.MaxValue } ? (int)n : 0;

    // ---- Mutations ------------------------------------------------------------------------------

    private const string SetMusicOnDocument =
        "mutation SetBoardingMusicOn($on: Boolean!) { fap { pram { setBgmOn(on: $on) { success } } } }";
    private const string SetVolumeDocument =
        "mutation SetVolume($volume: Int!) { fap { pram { setVolume(volume: $volume) { success } } } }";
    private const string SetChannelDocument =
        "mutation SetBgmChannel($channelIdx: Int!) { fap { pram { setBgmChannel(channelIdx: $channelIdx) { success } } } }";
    private const string AddToMemoDocument =
        "mutation AddToAnnouncementsMemo($announcementId: UUID!) { fap { pram { addToMemo(announcementId: $announcementId) { success } } } }";
    private const string RemoveFromMemoDocument =
        "mutation RemoveFromAnnouncementsMemo($index: Int!) { fap { pram { removeFromMemo(index: $index) { success } } } }";
    private const string ClearMemoDocument =
        "mutation ClearAnnouncementsMemo { fap { pram { clearMemo { success } } } }";
    private const string DirectPlayDocument =
        "mutation DirectPlayAnnouncement($announcementId: UUID!) { fap { pram { directPlay(announcementId: $announcementId) { success } } } }";
    private const string PlayNextDocument =
        "mutation PlayNextAnnouncement { fap { pram { playNext { success } } } }";
    private const string PlayAllDocument =
        "mutation PlayAllAnnouncements { fap { pram { playAll { success } } } }";
    private const string StopDocument =
        "mutation StopAnnouncements { fap { pram { stop { success } } } }";
    private const string SetAnnouncementsVariantDocument =
        "mutation SetAnnouncementsVariant($variant: Int!) { fap { setAnnouncementsVariant(variant: $variant) { success } } }";

    /// <summary>Boarding music on or off.</summary>
    public static FenixCabinMutation SetMusicOn(bool on)
        => new(SetMusicOnDocument, new { on }, "setBgmOn", true);

    /// <summary>The PRAM volume, as the Fenix's own step number (the bundle sends it unclamped).</summary>
    public static FenixCabinMutation SetVolume(int volume)
        => new(SetVolumeDocument, new { volume }, "setVolume", true);

    /// <summary>Selects a boarding-music channel by its index in <see cref="FenixCabinPramState.Channels"/>.</summary>
    public static FenixCabinMutation SetChannel(int channelIdx)
        => new(SetChannelDocument, new { channelIdx }, "setBgmChannel", true);

    /// <summary>Queues a prerecorded announcement by its id.</summary>
    public static FenixCabinMutation AddToMemo(string announcementId)
        => new(AddToMemoDocument, new { announcementId }, "addToMemo", true);

    /// <summary>Removes the queued announcement at an index in <see cref="FenixCabinPramState.Memo"/>.</summary>
    public static FenixCabinMutation RemoveFromMemo(int index)
        => new(RemoveFromMemoDocument, new { index }, "removeFromMemo", true);

    /// <summary>Empties the memo.</summary>
    public static FenixCabinMutation ClearMemo()
        => new(ClearMemoDocument, null, "clearMemo", true);

    /// <summary>Plays a prerecorded announcement at once, by its id.</summary>
    public static FenixCabinMutation DirectPlay(string announcementId)
        => new(DirectPlayDocument, new { announcementId }, "directPlay", true);

    /// <summary>Plays the next announcement in the memo.</summary>
    public static FenixCabinMutation PlayNext()
        => new(PlayNextDocument, null, "playNext", true);

    /// <summary>Plays every announcement in the memo.</summary>
    public static FenixCabinMutation PlayAll()
        => new(PlayAllDocument, null, "playAll", true);

    /// <summary>Stops the announcement playing.</summary>
    public static FenixCabinMutation Stop()
        => new(StopDocument, null, "stop", true);

    /// <summary>Sets the announcement variant (not under <c>pram</c>: <c>fap.setAnnouncementsVariant</c>).</summary>
    public static FenixCabinMutation SetAnnouncementsVariant(int variant)
        => new(SetAnnouncementsVariantDocument, new { variant }, "setAnnouncementsVariant", false);

    // ---- Sending --------------------------------------------------------------------------------

    /// <summary>
    /// True only when the answer carries an explicit <c>success: true</c> at the mutation's place
    /// (<c>fap.pram.&lt;ResultField&gt;</c>, or <c>fap.&lt;ResultField&gt;</c> when it is not under
    /// <c>pram</c>). A missing answer, a missing field or any other value is a failure.
    /// </summary>
    public static bool Succeeded(FenixCabinMutation m, JObject? data)
    {
        JToken? holder = data?["fap"];
        if (m.UnderPram) holder = (holder as JObject)?["pram"];
        var result = (holder as JObject)?[m.ResultField] as JObject;
        return Bool(result?["success"]);
    }

    /// <summary>
    /// Sends one mutation and says whether the Fenix accepted it. False when the data service is down,
    /// answers an error or says <c>success: false</c>; nothing is thrown.
    /// </summary>
    public static async Task<bool> SendAsync(this FenixDataService data, FenixCabinMutation m, CancellationToken ct = default)
        => Succeeded(m, await data.QueryAsync(m.Query, m.Variables, ct).ConfigureAwait(false));
}
