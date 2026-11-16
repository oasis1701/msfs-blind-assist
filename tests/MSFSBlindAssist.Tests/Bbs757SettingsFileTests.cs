using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Changing one value in BlueBird's settings files (<see cref="Bbs757SettingsFile"/>): only that value's bytes change,
/// every other value is checked unchanged, the file is replaced through a temp file only while the guard allows and
/// never created, a safety copy is taken once, and a registration setting goes to every registration's file. Also the
/// work folder's listings (<see cref="Bbs757WorkFolder.RegistrationFiles"/>, <see cref="Bbs757WorkFolder.ExistingFolders"/>,
/// <see cref="Bbs757WorkFolder.SimulatorOf"/>). Temp folders only: never BlueBird's own files.
/// </summary>
public class Bbs757SettingsFileTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    // efb_settings.json with made-up IDs and codes, and G-BBS75-opt.json as BlueBird wrote it (one line, its key order).
    private static readonly string SettingsFixture = Fixture("bbs757-efb-settings.json");
    private static readonly string LiveryFixture = Fixture("bbs757-livery-opt.json");

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "msfsba-bbs757-settings-" + Guid.NewGuid().ToString("N"));
        public TempDir() => Directory.CreateDirectory(Path);
        public string Sub(string name) => System.IO.Path.Combine(Path, name);
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// The settings fixture as <c>efb_settings.json</c> in <paramref name="tmp"/>: byte for byte without overrides; with
    /// them, each override's top-level key set to its JSON (added last when the fixture has no such key), written compact.
    /// </summary>
    private static string WriteFixtureSettings(TempDir tmp, params (string Key, string Json)[] overrides)
    {
        string file = tmp.Sub("efb_settings.json");
        if (overrides.Length == 0)
        {
            File.Copy(SettingsFixture, file);
            return file;
        }

        var root = JsonNode.Parse(File.ReadAllText(SettingsFixture))!.AsObject();
        foreach (var (key, json) in overrides) root[key] = JsonNode.Parse(json);
        File.WriteAllText(file, root.ToJsonString());
        return file;
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static Bbs757Value ValueOf(object value) => value switch
    {
        long whole => Bbs757Value.Of(whole),
        bool on => Bbs757Value.Of(on),
        _ => throw new ArgumentException($"No test value of type {value.GetType()}.", nameof(value)),
    };

    private static int Occurrences(string text, string part)
    {
        int count = 0;
        for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + 1, StringComparison.Ordinal))
            count++;
        return count;
    }

    /// <summary>
    /// <paramref name="json"/> with the value at <paramref name="path"/> written as <paramref name="newToken"/> and every
    /// other character kept: what an edit of that one value must give. The value's <c>"key":old</c> must occur once, so
    /// nothing else can be the part replaced.
    /// </summary>
    private static string WithValue(string json, string path, string newToken)
    {
        string[] keys = path.Split('.');
        using var doc = JsonDocument.Parse(json);
        JsonElement old = doc.RootElement;
        foreach (string key in keys) old = old.GetProperty(key);

        string before = $"\"{keys[^1]}\":{old.GetRawText()}";
        Assert.Equal(1, Occurrences(json, before));
        return json.Replace(before, $"\"{keys[^1]}\":{newToken}");
    }

    private static Bbs757SaveResult Refused(string reason) => new(Bbs757SaveStatus.Refused, reason);

    // Where a file's safety copies go inside the backup folder: the first 8 hex digits of SHA-256 over its folder's full
    // path in upper case, worked out here on its own so that a change of scheme (string.GetHashCode differs per process)
    // fails a test.
    private static string BackupKeyOf(string folder) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(folder).ToUpperInvariant())))[..8];

    // ---- changing the bytes -----------------------------------------------------------------------

    [Theory]   // each path of a different type and depth
    [InlineData("loading_speed", 3L)]
    [InlineData("tiller_realistic", true)]
    [InlineData("failures.random_enabled", true)]
    public void Edit_changes_only_that_value_s_bytes(string path, object value)
    {
        byte[] original = File.ReadAllBytes(SettingsFixture);
        Bbs757Value wanted = ValueOf(value);

        var edit = Bbs757SettingsFile.Edit(original, path, wanted);

        Assert.Null(edit.Refusal);
        // Every byte before the value's token and after it is the fixture's own, its final line break included.
        Assert.Equal(Utf8(WithValue(Encoding.UTF8.GetString(original), path, wanted.ToJson())), edit.Bytes);
        using var doc = JsonDocument.Parse(edit.Bytes!);
        Assert.Equal(wanted, Bbs757SettingsFile.ValueAt(doc.RootElement, path, wanted.Type));
    }

    [Fact]
    public void Edit_changes_a_nested_registration_value_and_not_a_same_named_key_elsewhere()
    {
        byte[] livery = File.ReadAllBytes(LiveryFixture);

        var edit = Bbs757SettingsFile.Edit(livery, "options.egpws_callouts.80", Bbs757Value.Of(false));

        Assert.Null(edit.Refusal);
        Assert.Equal(Utf8(WithValue(Encoding.UTF8.GetString(livery), "options.egpws_callouts.80", "false")), edit.Bytes);
        using (var doc = JsonDocument.Parse(edit.Bytes!))
        {
            Assert.Equal(Bbs757Value.Of(false), Bbs757SettingsFile.ValueAt(doc.RootElement, "options.egpws_callouts.80", Bbs757ValueType.Bool));
            Assert.Equal(Bbs757Value.Of(true), Bbs757SettingsFile.ValueAt(doc.RootElement, "options.pilot_callouts.80_knots", Bbs757ValueType.Bool));
        }

        // The same key earlier in the file, at the top, in a same-named object elsewhere and beside a longer key: only the
        // one at the path changes.
        byte[] decoys = Utf8("{\"80\":true,\"egpws_callouts\":{\"80\":true},\"options\":{\"pilot_callouts\":{\"80_knots\":true,\"80\":true},\"egpws_callouts\":{\"80\":true}}}");
        Assert.Equal(
            Utf8("{\"80\":true,\"egpws_callouts\":{\"80\":true},\"options\":{\"pilot_callouts\":{\"80_knots\":true,\"80\":true},\"egpws_callouts\":{\"80\":false}}}"),
            Bbs757SettingsFile.Edit(decoys, "options.egpws_callouts.80", Bbs757Value.Of(false)).Bytes);
    }

    [Fact]
    public void Edit_refuses_a_missing_key_a_wrong_type_an_object_and_invalid_json()
    {
        byte[] settings = File.ReadAllBytes(SettingsFixture);

        static void Refuses(byte[] file, string path, Bbs757Value value, string reason)
        {
            var edit = Bbs757SettingsFile.Edit(file, path, value);
            Assert.Equal(reason, edit.Refusal);
            Assert.Null(edit.Bytes);
        }

        // No such key: at the top, inside an object, under a key that holds a value, or only at another depth.
        Refuses(settings, "no_such_key", Bbs757Value.Of(1L), Bbs757SettingsFile.NotInFileReason);
        Refuses(settings, "failures.no_such_key", Bbs757Value.Of(true), Bbs757SettingsFile.NotInFileReason);
        Refuses(settings, "loading_speed.speed", Bbs757Value.Of(1L), Bbs757SettingsFile.NotInFileReason);
        Refuses(settings, "random_enabled", Bbs757Value.Of(true), Bbs757SettingsFile.NotInFileReason);
        // The key holds another type than the value's.
        Refuses(settings, "loading_speed", Bbs757Value.Of(true), Bbs757SettingsFile.NotInFileReason);
        Refuses(settings, "tiller_realistic", Bbs757Value.Of(1L), Bbs757SettingsFile.NotInFileReason);
        Refuses(settings, "hoppie_logon_code", Bbs757Value.Of(1L), Bbs757SettingsFile.NotInFileReason);
        Refuses(settings, "display_brightness", Bbs757Value.Of(1L), Bbs757SettingsFile.NotInFileReason);   // 0.8: no whole number
        Refuses(Utf8("{\"loading_speed\":null}"), "loading_speed", Bbs757Value.Of(3L), Bbs757SettingsFile.NotInFileReason);
        // An object or a list where a value was expected.
        Refuses(settings, "failures", Bbs757Value.Of(true), Bbs757SettingsFile.NotInFileReason);
        Refuses(settings, "chart_view_states", Bbs757Value.Of("x"), Bbs757SettingsFile.NotInFileReason);
        Refuses(Utf8("{\"loading_speed\":[2]}"), "loading_speed", Bbs757Value.Of(3L), Bbs757SettingsFile.NotInFileReason);
        // Not JSON: text, cut short after the value, nothing at all.
        Refuses(Utf8("not json"), "loading_speed", Bbs757Value.Of(3L), Bbs757SettingsFile.UnreadableReason);
        Refuses(Utf8("{\"loading_speed\":2"), "loading_speed", Bbs757Value.Of(3L), Bbs757SettingsFile.UnreadableReason);
        Refuses(Array.Empty<byte>(), "loading_speed", Bbs757Value.Of(3L), Bbs757SettingsFile.UnreadableReason);
    }

    [Fact]
    public void Edit_refuses_a_key_written_twice_on_its_path()
    {
        // Which of the two BlueBird reads is unknown, so neither is changed.
        Assert.Equal(Bbs757SettingsFile.UnreadableReason,
            Bbs757SettingsFile.Edit(Utf8("{\"loading_speed\":2,\"loading_speed\":1}"), "loading_speed", Bbs757Value.Of(3L)).Refusal);
        Assert.Equal(Bbs757SettingsFile.UnreadableReason,
            Bbs757SettingsFile.Edit(Utf8("{\"failures\":{\"random_enabled\":false},\"failures\":{}}"), "failures.random_enabled",
                Bbs757Value.Of(true)).Refusal);

        // A key written twice off the path is not read, so it is no reason to refuse: it is kept as it is.
        Assert.Equal(Utf8("{\"x\":1,\"x\":2,\"loading_speed\":3}"),
            Bbs757SettingsFile.Edit(Utf8("{\"x\":1,\"x\":2,\"loading_speed\":2}"), "loading_speed", Bbs757Value.Of(3L)).Bytes);
    }

    [Fact]
    public void Edit_round_trips_codes_with_quotes_backslashes_and_non_ascii()
    {
        const string code = "AB\"C\\D é";
        byte[] original = File.ReadAllBytes(SettingsFixture);

        var edit = Bbs757SettingsFile.Edit(original, "hoppie_logon_code", Bbs757Value.Of(code));

        Assert.Null(edit.Refusal);
        // Escaped as little as JSON allows: the accent stays readable in the file.
        Assert.Contains("\"hoppie_logon_code\":\"AB\\\"C\\\\D é\",", Encoding.UTF8.GetString(edit.Bytes!));
        using (var doc = JsonDocument.Parse(edit.Bytes!))
            Assert.Equal(Bbs757Value.Of(code), Bbs757SettingsFile.ValueAt(doc.RootElement, "hoppie_logon_code", Bbs757ValueType.Text));

        // And back: the escaped token is replaced whole, however much longer its escapes make it.
        Assert.Equal(original, Bbs757SettingsFile.Edit(edit.Bytes!, "hoppie_logon_code", Bbs757Value.Of("NOT-A-REAL-CODE")).Bytes);
    }

    [Fact]
    public void A_byte_order_mark_before_the_json_is_kept()
    {
        byte[] original = [0xEF, 0xBB, 0xBF, .. Utf8("{\"loading_speed\":2}")];
        byte[] expected = [0xEF, 0xBB, 0xBF, .. Utf8("{\"loading_speed\":3}")];

        Assert.Equal(expected, Bbs757SettingsFile.Edit(original, "loading_speed", Bbs757Value.Of(3L)).Bytes);
    }

    [Fact]
    public void Text_with_an_unpaired_surrogate_escape_is_refused_and_never_throws()
    {
        using var tmp = new TempDir();
        // JSON allows any \u escape, so the file parses, but no string can hold a lone \uD800.
        string file = tmp.Sub("efb_settings.json");
        File.WriteAllText(file, File.ReadAllText(SettingsFixture)
            .Replace("\"hoppie_logon_code\":\"NOT-A-REAL-CODE\"", "\"hoppie_logon_code\":\"\\uD800x\""));
        byte[] before = File.ReadAllBytes(file);
        var backup = new Bbs757SettingsBackup(tmp.Sub("bk"));

        using (var doc = Bbs757SettingsFile.Read(file))
        {
            Assert.NotNull(doc);
            JsonElement code = doc!.RootElement.GetProperty("hoppie_logon_code");
            Assert.Throws<InvalidOperationException>(() => code.GetString());   // what reading it as text must survive
            Assert.Null(Bbs757Value.From(code, Bbs757ValueType.Text));
            Assert.Null(Bbs757SettingsFile.ValueAt(doc.RootElement, "hoppie_logon_code", Bbs757ValueType.Text));
        }
        Assert.Equal(Bbs757SettingsFile.UnreadableReason,
            Bbs757SettingsFile.Edit(before, "hoppie_logon_code", Bbs757Value.Of("NEW-CODE")).Refusal);
        Assert.Equal(Refused(Bbs757SettingsFile.UnreadableReason),
            Bbs757SettingsFile.Save(file, "hoppie_logon_code", Bbs757Value.Of("NEW-CODE"), () => true, backup, false));
        Assert.Equal(before, File.ReadAllBytes(file));

        // Elsewhere it is never read: another value saves, and the text stays as it was.
        Assert.Equal(Bbs757SaveStatus.Saved,
            Bbs757SettingsFile.Save(file, "loading_speed", Bbs757Value.Of(3L), () => true, backup, true).Status);
        Assert.Contains("\"hoppie_logon_code\":\"\\uD800x\"", File.ReadAllText(file));

        // In a key's name: refused, not thrown, whether the name comes before or after the value.
        foreach (string json in new[] { "{\"\\uD800x\":1,\"loading_speed\":2}", "{\"loading_speed\":2,\"\\uD800x\":1}" })
        {
            Assert.Equal(Bbs757SettingsFile.UnreadableReason,
                Bbs757SettingsFile.Edit(Utf8(json), "loading_speed", Bbs757Value.Of(3L)).Refusal);
            using var doc = JsonDocument.Parse(json);
            _ = Bbs757SettingsFile.ValueAt(doc.RootElement, "loading_speed", Bbs757ValueType.Integer);
        }
    }

    [Fact]
    public void The_check_after_the_splice_catches_any_other_change()
    {
        byte[] original = Utf8("{\"a\":1,\"b\":{\"c\":true,\"d\":\"x\"},\"e\":[1,{\"f\":2}]}");
        string? Check(string edited) => Bbs757SettingsFile.Verify(original, Utf8(edited), "b.c", Bbs757Value.Of(false));

        Assert.Null(Check("{\"a\":1,\"b\":{\"c\":false,\"d\":\"x\"},\"e\":[1,{\"f\":2}]}"));                      // only the value
        Assert.Equal(Bbs757SettingsFile.VerifyReason, Check("{\"a\":1,\"b\":{\"c\":true,\"d\":\"x\"},\"e\":[1,{\"f\":2}]}"));    // not the value
        Assert.Equal(Bbs757SettingsFile.VerifyReason, Check("{\"a\":1,\"b\":{\"c\":\"false\",\"d\":\"x\"},\"e\":[1,{\"f\":2}]}")); // another type
        Assert.Equal(Bbs757SettingsFile.VerifyReason, Check("{\"a\":2,\"b\":{\"c\":false,\"d\":\"x\"},\"e\":[1,{\"f\":2}]}"));   // another value
        Assert.Equal(Bbs757SettingsFile.VerifyReason, Check("{\"a\":1,\"b\":{\"c\":false,\"d\":\"y\"},\"e\":[1,{\"f\":2}]}"));   // its neighbor
        Assert.Equal(Bbs757SettingsFile.VerifyReason, Check("{\"a\":1,\"b\":{\"c\":false,\"d\":\"x\"},\"e\":[1,{\"f\":3}]}"));   // in a list
        Assert.Equal(Bbs757SettingsFile.VerifyReason, Check("{\"a\":1.0,\"b\":{\"c\":false,\"d\":\"x\"},\"e\":[1,{\"f\":2}]}")); // written otherwise
        Assert.Equal(Bbs757SettingsFile.VerifyReason, Check("{\"a\":1,\"b\":{\"c\":false,\"d\":\"x\"},\"e\":[1,{\"f\":2}],\"g\":0}")); // a key more
        Assert.Equal(Bbs757SettingsFile.VerifyReason, Check("{\"b\":{\"c\":false,\"d\":\"x\"},\"e\":[1,{\"f\":2}]}"));             // a key less
        Assert.Equal(Bbs757SettingsFile.VerifyReason, Check("{\"b\":{\"c\":false,\"d\":\"x\"},\"a\":1,\"e\":[1,{\"f\":2}]}"));      // reordered
        Assert.Equal(Bbs757SettingsFile.VerifyReason, Check("{\"a\":1,\"b\":{\"c\":false,\"d\":\"x\"},\"e\":[1,{\"f\":2}]"));      // not JSON
    }

    // ---- reading ----------------------------------------------------------------------------------

    [Fact]
    public void ValueAt_follows_the_dotted_path_and_takes_only_the_type_asked()
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(LiveryFixture));
        JsonElement root = doc.RootElement;

        Assert.Equal(Bbs757Value.Of("lbs"), Bbs757SettingsFile.ValueAt(root, "options.general.weight_unit", Bbs757ValueType.Text));
        Assert.Equal(Bbs757Value.Of(true), Bbs757SettingsFile.ValueAt(root, "options.egpws_callouts.80", Bbs757ValueType.Bool));
        Assert.Equal(Bbs757Value.Of(1L), Bbs757SettingsFile.ValueAt(root, "options.model_visibility.cabin_layout", Bbs757ValueType.Integer));
        // A stored whole number is a valid float.
        Assert.Equal(Bbs757Value.OfFloat(1.0), Bbs757SettingsFile.ValueAt(root, "options.model_visibility.cabin_layout", Bbs757ValueType.Float));

        Assert.Null(Bbs757SettingsFile.ValueAt(root, "options.egpws_callouts.80", Bbs757ValueType.Integer));      // another type
        Assert.Null(Bbs757SettingsFile.ValueAt(root, "options.egpws_callouts.81", Bbs757ValueType.Bool));         // no such key
        Assert.Null(Bbs757SettingsFile.ValueAt(root, "80", Bbs757ValueType.Bool));                                 // only at its path
        Assert.Null(Bbs757SettingsFile.ValueAt(root, "options.general", Bbs757ValueType.Text));                   // an object
        Assert.Null(Bbs757SettingsFile.ValueAt(root, "options.general.weight_unit.x", Bbs757ValueType.Text));     // through a value
        Assert.Null(Bbs757SettingsFile.ValueAt(default, "options", Bbs757ValueType.Text));                        // nothing read
    }

    [Fact]
    public void ValueAt_past_a_key_name_with_an_unpaired_surrogate_escape_reads_null_and_never_throws()
    {
        // A name at least as long as the key looked for is unescaped to be compared, and a lone \uD800 cannot be: the
        // lookup throws, though the file parses. (A shorter name is passed over unread, so it never gets this far.)
        using var doc = JsonDocument.Parse("{\"loading_speed\":2,\"\\uD800abcdefghijk\":1}");
        Assert.Throws<InvalidOperationException>(() => doc.RootElement.TryGetProperty("loading_speed", out _));

        Assert.Null(Bbs757SettingsFile.ValueAt(doc.RootElement, "loading_speed", Bbs757ValueType.Integer));
        Assert.Null(Bbs757SettingsFile.ValueAt(doc.RootElement, "loading_speed.x", Bbs757ValueType.Integer));
    }

    [Fact]
    public void ValueAt_on_a_disposed_document_throws_and_is_never_read_as_a_missing_key()
    {
        // A caller's mistake (the rows and the save's checks need their documents undisposed) must not pass as "unreadable".
        var doc = JsonDocument.Parse("{\"loading_speed\":2,\"failures\":{\"random_enabled\":true}}");
        JsonElement root = doc.RootElement;
        doc.Dispose();

        Assert.Throws<ObjectDisposedException>(() => Bbs757SettingsFile.ValueAt(root, "loading_speed", Bbs757ValueType.Integer));
        Assert.Throws<ObjectDisposedException>(() =>
            Bbs757SettingsFile.ValueAt(root, "failures.random_enabled", Bbs757ValueType.Bool));
    }

    [Fact]
    public void Read_parses_the_file_and_is_null_when_it_is_missing_too_large_or_not_json()
    {
        using var tmp = new TempDir();
        string file = WriteFixtureSettings(tmp);

        using (var doc = Bbs757SettingsFile.Read(file))
        {
            Assert.NotNull(doc);
            Assert.Equal(Bbs757Value.Of(2L), Bbs757SettingsFile.ValueAt(doc!.RootElement, "loading_speed", Bbs757ValueType.Integer));
        }

        // Read while another program has the file open for writing, as BlueBird may.
        using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        using (var doc = Bbs757SettingsFile.Read(file))
            Assert.NotNull(doc);

        Assert.Null(Bbs757SettingsFile.Read(tmp.Sub("missing.json")));
        File.WriteAllText(tmp.Sub("not.json"), "not json");
        Assert.Null(Bbs757SettingsFile.Read(tmp.Sub("not.json")));
        File.WriteAllText(tmp.Sub("large.json"), "{\"a\":\"" + new string('x', 256 * 1024) + "\"}");
        Assert.Null(Bbs757SettingsFile.Read(tmp.Sub("large.json")));
    }

    // ---- saving -----------------------------------------------------------------------------------

    [Fact]
    public void An_unknown_value_elsewhere_survives_a_save()
    {
        using var tmp = new TempDir();
        string file = WriteFixtureSettings(tmp, ("irs_align_time", "77"));      // fixture with irs_align_time set to 77
        var r = Bbs757SettingsFile.Save(file, "loading_speed", Bbs757Value.Of(3L), () => true, new Bbs757SettingsBackup(tmp.Sub("bk")), true);
        Assert.Equal(Bbs757SaveStatus.Saved, r.Status);
        Assert.Contains("\"irs_align_time\":77", File.ReadAllText(file));
    }

    [Fact]
    public void Unknown_keys_are_kept()
    {
        using var tmp = new TempDir();
        string file = WriteFixtureSettings(tmp, ("future_key", "{\"a\":1}"));
        string before = File.ReadAllText(file);
        Assert.EndsWith(",\"future_key\":{\"a\":1}}", before);   // a key this BlueBird version does not write

        var r = Bbs757SettingsFile.Save(file, "loading_speed", Bbs757Value.Of(3L), () => true, new Bbs757SettingsBackup(tmp.Sub("bk")), true);

        Assert.Equal(Bbs757SaveStatus.Saved, r.Status);
        Assert.Equal(WithValue(before, "loading_speed", "3"), File.ReadAllText(file));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_float_is_saved_with_a_decimal_point(string culture)
    {
        using var tmp = new TempDir();
        string file = WriteFixtureSettings(tmp);

        var r = TestCulture.Run(culture, () => Bbs757SettingsFile.Save(file, "failures.random_multiplier", Bbs757Value.OfFloat(2.0),
            () => true, new Bbs757SettingsBackup(tmp.Sub("bk")), true));

        Assert.Equal(Bbs757SaveStatus.Saved, r.Status);
        Assert.Contains("\"random_multiplier\":2.0", File.ReadAllText(file));
    }

    [Fact]
    public void A_save_of_the_value_already_stored_writes_nothing()
    {
        using var tmp = new TempDir();
        string file = WriteFixtureSettings(tmp);
        var written = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, written);
        byte[] before = File.ReadAllBytes(file);

        var r = Bbs757SettingsFile.Save(file, "loading_speed", Bbs757Value.Of(2L), () => true, new Bbs757SettingsBackup(tmp.Sub("bk")), true);

        Assert.Equal(new Bbs757SaveResult(Bbs757SaveStatus.Unchanged), r);
        Assert.Equal(written, File.GetLastWriteTimeUtc(file));
        Assert.Equal(before, File.ReadAllBytes(file));
        Assert.False(File.Exists(file + ".msfsba-tmp"));
        Assert.False(Directory.Exists(tmp.Sub("bk")));   // nothing written, so nothing to keep a copy of
    }

    [Fact]
    public void A_save_refuses_while_the_simulator_runs()
    {
        using var tmp = new TempDir();
        string file = WriteFixtureSettings(tmp);
        byte[] before = File.ReadAllBytes(file);

        var r = Bbs757SettingsFile.Save(file, "loading_speed", Bbs757Value.Of(3L), () => false, new Bbs757SettingsBackup(tmp.Sub("bk")), true);

        Assert.Equal(Refused(Bbs757SettingsFile.SimRunningReason), r);
        Assert.Equal(before, File.ReadAllBytes(file));
        Assert.False(File.Exists(file + ".msfsba-tmp"));
        Assert.False(Directory.Exists(tmp.Sub("bk")));
    }

    [Fact]
    public void A_save_refuses_when_the_simulator_starts_before_the_write()
    {
        using var tmp = new TempDir();
        string file = WriteFixtureSettings(tmp);
        byte[] before = File.ReadAllBytes(file);
        int calls = 0;
        var r = Bbs757SettingsFile.Save(file, "loading_speed", Bbs757Value.Of(3L), () => ++calls == 1, new Bbs757SettingsBackup(tmp.Sub("bk")), true);
        Assert.Equal(new Bbs757SaveResult(Bbs757SaveStatus.Refused, Bbs757SettingsFile.SimRunningReason), r);
        Assert.Equal(before, File.ReadAllBytes(file));
        Assert.False(File.Exists(file + ".msfsba-tmp"));
    }

    [Fact]
    public void A_save_refuses_a_file_in_use()
    {
        using var tmp = new TempDir();
        string file = WriteFixtureSettings(tmp);
        using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var r = Bbs757SettingsFile.Save(file, "loading_speed", Bbs757Value.Of(3L), () => true, new Bbs757SettingsBackup(tmp.Sub("bk")), true);
            Assert.Equal(new Bbs757SaveResult(Bbs757SaveStatus.Refused, Bbs757SettingsFile.InUseReason), r);
        }
    }

    [Fact]
    public void A_save_whose_move_fails_leaves_the_file_and_no_temp_file()
    {
        using var tmp = new TempDir();
        string file = WriteFixtureSettings(tmp);
        byte[] before = File.ReadAllBytes(file);
        int calls = 0;
        FileStream? reader = null;
        try
        {
            // Opened at the second guard check, after the read: the backup and the temp file still succeed, but a file
            // open without FileShare.Delete cannot be replaced.
            var r = Bbs757SettingsFile.Save(file, "loading_speed", Bbs757Value.Of(3L), () =>
            {
                if (++calls == 2) reader = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                return true;
            }, new Bbs757SettingsBackup(tmp.Sub("bk")), true);

            Assert.Equal(Refused(Bbs757SettingsFile.InUseReason), r);
            Assert.Equal(2, calls);
            Assert.Single(Directory.GetFiles(tmp.Sub("bk"), "*", SearchOption.AllDirectories));   // the backup was made
        }
        finally
        {
            reader?.Dispose();
        }

        Assert.Equal(before, File.ReadAllBytes(file));
        Assert.False(File.Exists(file + ".msfsba-tmp"));
    }

    [Fact]
    public void A_save_logs_its_key_its_value_only_when_it_may_and_why_it_was_refused()
    {
        using var tmp = new TempDir();
        string file = WriteFixtureSettings(tmp);
        var backup = new Bbs757SettingsBackup(tmp.Sub("bk"));
        var lines = new List<string>();
        void Save(string path, Bbs757Value value, bool simulatorClosed, bool mayLog) =>
            Bbs757SettingsFile.Save(file, path, value, () => simulatorClosed, backup, mayLog, lines.Add);

        Save("loading_speed", Bbs757Value.Of(3L), simulatorClosed: true, mayLog: true);
        Save("loading_speed", Bbs757Value.Of(3L), simulatorClosed: true, mayLog: true);         // unchanged: no line
        Save("irs_align_time", Bbs757Value.Of(2L), simulatorClosed: false, mayLog: true);
        Save("no_such_key", Bbs757Value.Of(1L), simulatorClosed: true, mayLog: true);
        // The pilot's identifiers: their key, never their value, saved or refused.
        Save("simbrief_id", Bbs757Value.Of(987654L), simulatorClosed: true, mayLog: false);
        Save("simbrief_id", Bbs757Value.Of(555L), simulatorClosed: false, mayLog: false);
        Save("hoppie_logon_code", Bbs757Value.Of("SECRET-LOGON"), simulatorClosed: true, mayLog: false);

        // Each line names its file, by name only: a registration save makes one line for every registration's file.
        Assert.Equal(new[]
        {
            "settings: efb_settings.json: loading_speed = 3",
            "settings: efb_settings.json: irs_align_time = 2 not saved: the simulator is running",
            "settings: efb_settings.json: no_such_key = 1 not saved: this BlueBird version doesn't have it",
            "settings: efb_settings.json: simbrief_id changed",
            "settings: efb_settings.json: simbrief_id not saved: the simulator is running",
            "settings: efb_settings.json: hoppie_logon_code changed",
        }, lines);
        Assert.DoesNotContain(lines, line => line.Contains("987654") || line.Contains("555") || line.Contains("SECRET"));
        Assert.DoesNotContain(lines, line => line.Contains(tmp.Path, StringComparison.OrdinalIgnoreCase));   // never the folder
        Assert.Contains("\"simbrief_id\":987654", File.ReadAllText(file));   // saved all the same

        // A registration's file, saved and refused.
        string liveries = tmp.Sub("liveries");
        Directory.CreateDirectory(liveries);
        string livery = Path.Combine(liveries, "G-BBS75-opt.json");
        File.Copy(LiveryFixture, livery);
        lines.Clear();

        Bbs757SettingsFile.Save(livery, "options.ehsi.up_reference", Bbs757Value.Of("trk"), () => true, backup, true, lines.Add);
        Bbs757SettingsFile.Save(livery, "options.ehsi.up_reference", Bbs757Value.Of("hdg"), () => false, backup, true, lines.Add);

        Assert.Equal(new[]
        {
            "settings: G-BBS75-opt.json: options.ehsi.up_reference = \"trk\"",
            "settings: G-BBS75-opt.json: options.ehsi.up_reference = \"hdg\" not saved: the simulator is running",
        }, lines);
    }

    [Fact]
    public void A_missing_file_is_refused_and_never_created()
    {
        using var tmp = new TempDir();
        string file = tmp.Sub("efb_settings.json");
        string inMissingFolder = Path.Combine(tmp.Sub("work"), "efb_settings.json");
        var backup = new Bbs757SettingsBackup(tmp.Sub("bk"));

        Assert.Equal(Refused(Bbs757SettingsFile.MissingReason),
            Bbs757SettingsFile.Save(file, "loading_speed", Bbs757Value.Of(3L), () => true, backup, true));
        Assert.Equal(Refused(Bbs757SettingsFile.MissingReason),
            Bbs757SettingsFile.Save(inMissingFolder, "loading_speed", Bbs757Value.Of(3L), () => true, backup, true));

        Assert.False(File.Exists(file));
        Assert.False(File.Exists(file + ".msfsba-tmp"));
        Assert.False(Directory.Exists(tmp.Sub("work")));
        Assert.False(Directory.Exists(tmp.Sub("bk")));
    }

    [Fact]
    public void The_first_write_copies_the_file_to_the_backup_folder_once()
    {
        using var tmp = new TempDir();
        string file = WriteFixtureSettings(tmp);
        byte[] original = File.ReadAllBytes(file);
        var backup = new Bbs757SettingsBackup(tmp.Sub("bk"));

        Assert.Equal(Bbs757SaveStatus.Saved,
            Bbs757SettingsFile.Save(file, "loading_speed", Bbs757Value.Of(3L), () => true, backup, true).Status);
        Assert.Equal(Bbs757SaveStatus.Saved,
            Bbs757SettingsFile.Save(file, "irs_align_time", Bbs757Value.Of(2L), () => true, backup, true).Status);

        // One copy, under the file's own name in its folder's key, of the file as it was before the first save.
        string copy = Assert.Single(Directory.GetFiles(tmp.Sub("bk"), "*", SearchOption.AllDirectories));
        Assert.Equal(Path.Combine(tmp.Sub("bk"), BackupKeyOf(tmp.Path), "efb_settings.json"), copy);
        Assert.Equal(original, File.ReadAllBytes(copy));
        Assert.NotEqual(original, File.ReadAllBytes(file));
    }

    [Fact]
    public void Files_of_one_name_in_two_folders_keep_a_backup_each()
    {
        using var tmp = new TempDir();
        string msfs2020 = tmp.Sub("msfs2020-work"), msfs2024 = tmp.Sub("msfs2024-work");
        Directory.CreateDirectory(msfs2020);
        Directory.CreateDirectory(msfs2024);
        string first = Path.Combine(msfs2020, "efb_settings.json"), second = Path.Combine(msfs2024, "efb_settings.json");
        File.Copy(SettingsFixture, first);
        File.WriteAllText(second, WithValue(File.ReadAllText(SettingsFixture), "loading_speed", "1"));
        byte[] firstOriginal = File.ReadAllBytes(first), secondOriginal = File.ReadAllBytes(second);
        Assert.NotEqual(firstOriginal, secondOriginal);
        var backup = new Bbs757SettingsBackup(tmp.Sub("bk"));

        Assert.Equal(Bbs757SaveStatus.Saved,
            Bbs757SettingsFile.Save(first, "irs_align_time", Bbs757Value.Of(2L), () => true, backup, true).Status);
        Assert.Equal(Bbs757SaveStatus.Saved,
            Bbs757SettingsFile.Save(second, "irs_align_time", Bbs757Value.Of(2L), () => true, backup, true).Status);

        // Two copies, each in its own folder's key and holding its own file's bytes from before its save.
        Assert.Equal(2, Directory.GetFiles(tmp.Sub("bk"), "*", SearchOption.AllDirectories).Length);
        Assert.Equal(firstOriginal, File.ReadAllBytes(Path.Combine(tmp.Sub("bk"), BackupKeyOf(msfs2020), "efb_settings.json")));
        Assert.Equal(secondOriginal, File.ReadAllBytes(Path.Combine(tmp.Sub("bk"), BackupKeyOf(msfs2024), "efb_settings.json")));
    }

    [Fact]
    public void Set_to_dark_blue_changes_only_the_themes_bytes_and_the_row_then_reads_dark_blue()
    {
        // The settings window's one write of ui_theme (B757-21), through its save (Bbs757SettingsSave.Run over Save): over a file
        // holding Light blue only the theme's value changes, so the file is the fixture's Dark blue again byte for byte, and the row
        // read from it says Dark blue, with no way back left to offer.
        using var tmp = new TempDir();
        string file = tmp.Sub("efb_settings.json");
        byte[] darkBlue = File.ReadAllBytes(SettingsFixture);
        File.WriteAllBytes(file, Utf8(WithValue(Encoding.UTF8.GetString(darkBlue), Bbs757EfbSettings.ThemePath, "1")));
        Bbs757Setting theme = Bbs757EfbSettings.ByPath(Bbs757EfbSettings.ThemePath)!;

        Bbs757SettingRow ThemeRow()
        {
            using var read = Bbs757SettingsFile.Read(file)!;
            return Bbs757SettingsRows.For(Bbs757SettingSection.General, read.RootElement, Array.Empty<JsonElement>())
                .Single(row => row.Setting.Path == Bbs757EfbSettings.ThemePath);
        }

        Bbs757SettingRow before = ThemeRow();
        Assert.Equal("Light blue", before.Text);
        Assert.True(Bbs757SettingsRows.OffersDarkBlue(before));

        var registrationWrites = new List<string>();
        Bbs757SettingSaveOutcome outcome;
        using (var doc = Bbs757SettingsFile.Read(file)!)
        {
            outcome = Bbs757SettingsSave.Run(theme, Bbs757EfbSettings.DarkBlueTheme,
                new Bbs757SettingsSnapshot(doc.RootElement, true, Array.Empty<JsonElement>(), 0), new[] { before },
                (path, value) => Bbs757SettingsFile.Save(file, path, value, () => true, new Bbs757SettingsBackup(tmp.Sub("bk")), true, _ => { }),
                (path, value) =>
                {
                    registrationWrites.Add(path);
                    return Array.Empty<(string, Bbs757SaveResult)>();
                },
                _ => { });
        }

        Assert.Null(outcome.Spoken);
        Assert.Empty(registrationWrites);
        Assert.Equal(darkBlue, File.ReadAllBytes(file));
        Bbs757SettingRow after = ThemeRow();
        Assert.Equal((Bbs757RowState.Value, 0, "Dark blue"), (after.State, after.ChoiceIndex, after.Text));
        Assert.Equal("Dark blue. Non-default color schemes are not supported.", Bbs757SettingsRows.ThemeNoteText(after));
        Assert.False(Bbs757SettingsRows.OffersDarkBlue(after));
    }

    // ---- every registration -------------------------------------------------------------------------

    [Fact]
    public void Registration_saves_report_each_registration()
    {
        using var tmp = new TempDir();
        string work = tmp.Sub("work");
        string liveries = Path.Combine(work, "liveries");
        Directory.CreateDirectory(liveries);
        File.Copy(LiveryFixture, Path.Combine(liveries, "N757BB-opt.json"));
        File.WriteAllText(Path.Combine(liveries, "G-BBS75-opt.json"), "not json");
        File.Copy(LiveryFixture, Path.Combine(liveries, "D-ABCD-opt.json"));
        File.WriteAllText(Path.Combine(liveries, "G-BBS75.json"), "{}");   // not a registration's options file
        File.WriteAllText(Path.Combine(work, "X-OUT-opt.json"), "{}");     // not in liveries

        var results = Bbs757SettingsFile.SaveToRegistrations(work, "options.egpws_callouts.80", Bbs757Value.Of(false),
            () => true, new Bbs757SettingsBackup(tmp.Sub("bk")), true);

        // By registration, the one that could not be read named with the others.
        Assert.Equal(new (string, Bbs757SaveResult)[]
        {
            ("D-ABCD", new Bbs757SaveResult(Bbs757SaveStatus.Saved)),
            ("G-BBS75", Refused(Bbs757SettingsFile.UnreadableReason)),
            ("N757BB", new Bbs757SaveResult(Bbs757SaveStatus.Saved)),
        }, results);
        string saved = WithValue(File.ReadAllText(LiveryFixture), "options.egpws_callouts.80", "false");
        Assert.Equal(saved, File.ReadAllText(Path.Combine(liveries, "D-ABCD-opt.json")));
        Assert.Equal(saved, File.ReadAllText(Path.Combine(liveries, "N757BB-opt.json")));
        Assert.Equal("not json", File.ReadAllText(Path.Combine(liveries, "G-BBS75-opt.json")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(liveries, "G-BBS75.json")));
    }

    [Fact]
    public void No_registration_files_give_no_results()
    {
        using var tmp = new TempDir();
        string work = tmp.Sub("work");
        Directory.CreateDirectory(work);
        var backup = new Bbs757SettingsBackup(tmp.Sub("bk"));

        // No liveries folder yet, then an empty one.
        Assert.Empty(Bbs757SettingsFile.SaveToRegistrations(work, "options.egpws_callouts.80", Bbs757Value.Of(false), () => true, backup, true));
        Directory.CreateDirectory(Path.Combine(work, "liveries"));
        Assert.Empty(Bbs757SettingsFile.SaveToRegistrations(work, "options.egpws_callouts.80", Bbs757Value.Of(false), () => true, backup, true));
        Assert.Empty(Bbs757WorkFolder.RegistrationFiles(tmp.Sub("no-such-folder")));
    }

    // ---- the work folders ---------------------------------------------------------------------------

    [Fact]
    public void ExistingFolders_are_the_roots_holding_blueBirds_settings_in_their_order()
    {
        using var tmp = new TempDir();
        string first = tmp.Sub("first"), bare = tmp.Sub("bare"), second = tmp.Sub("second"), missing = tmp.Sub("missing");
        foreach (string folder in new[] { first, second })
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "efb_settings.json"), "{}");
        }
        Directory.CreateDirectory(bare);   // a folder without BlueBird's settings file

        Assert.Equal(new[] { second, first }, Bbs757WorkFolder.ExistingFolders(new[] { missing, second, bare, first, "" }));
        Assert.Empty(Bbs757WorkFolder.ExistingFolders(new[] { missing, bare }));
        Assert.Empty(Bbs757WorkFolder.ExistingFolders(Array.Empty<string>()));
    }

    // ---- reading one setting for a tablet press (plan 18, Task 8) ------------------------------------------------------

    private static Bbs757Setting SettingAt(string path) => Bbs757EfbSettings.ByPath(path) ?? throw new ArgumentException(path);

    [Fact]
    public void ReadValue_reads_a_global_setting_from_efb_settings()
    {
        using var tmp = new TempDir();
        string work = tmp.Sub("work");
        Directory.CreateDirectory(work);
        File.WriteAllText(Path.Combine(work, "efb_settings.json"), "{\"baro_sync\":true,\"loading_speed\":2,\"automatic_loading\":1}");

        Assert.Equal(Bbs757Value.Of(true), Bbs757SettingsFile.ReadValue(work, SettingAt("baro_sync")));
        Assert.Equal(Bbs757Value.Of(2L), Bbs757SettingsFile.ReadValue(work, SettingAt("loading_speed")));
        // A value of another type than the setting's, and a key the file lacks, read as nothing.
        Assert.Null(Bbs757SettingsFile.ReadValue(work, SettingAt("automatic_loading")));
        Assert.Null(Bbs757SettingsFile.ReadValue(work, SettingAt("cpdlc_network")));
    }

    [Fact]
    public void ReadValue_reads_a_registration_setting_from_the_file_written_last()
    {
        // BlueBird writes the flown registration's file as the tablet changes it: the newest is the one in use.
        using var tmp = new TempDir();
        string work = tmp.Sub("work");
        string liveries = Path.Combine(work, "liveries");
        Directory.CreateDirectory(liveries);
        string older = Path.Combine(liveries, "D-ABCD-opt.json"), newer = Path.Combine(liveries, "G-BBS75-opt.json");
        File.WriteAllText(older, "{\"options\":{\"egpws_callouts\":{\"2500\":true}}}");
        File.WriteAllText(newer, "{\"options\":{\"egpws_callouts\":{\"2500\":false}}}");
        var now = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(older, now.AddMinutes(-5));
        File.SetLastWriteTimeUtc(newer, now);
        var callout = SettingAt("options.egpws_callouts.2500");

        Assert.Equal(newer, Bbs757WorkFolder.LatestRegistrationFile(work));
        Assert.Equal(Bbs757Value.Of(false), Bbs757SettingsFile.ReadValue(work, callout));

        File.SetLastWriteTimeUtc(older, now.AddMinutes(5));
        Assert.Equal(older, Bbs757WorkFolder.LatestRegistrationFile(work));
        Assert.Equal(Bbs757Value.Of(true), Bbs757SettingsFile.ReadValue(work, callout));
    }

    [Fact]
    public void ReadValue_reads_nothing_without_the_file()
    {
        using var tmp = new TempDir();
        string work = tmp.Sub("work");
        Directory.CreateDirectory(work);

        Assert.Null(Bbs757SettingsFile.ReadValue(work, SettingAt("baro_sync")));
        Assert.Null(Bbs757SettingsFile.ReadValue(work, SettingAt("options.egpws_callouts.2500")));
        Assert.Null(Bbs757WorkFolder.LatestRegistrationFile(work));
        Assert.Null(Bbs757SettingsFile.ReadValue(tmp.Sub("no-such-folder"), SettingAt("baro_sync")));
    }

    [Fact]
    public void SimulatorOf_names_the_store_and_steam_roots()
    {
        var roots = Bbs757WorkFolder.DefaultRoots();

        Assert.Equal("MSFS 2024", Bbs757WorkFolder.SimulatorOf(roots[0]));   // Microsoft Store
        Assert.Equal("MSFS 2024", Bbs757WorkFolder.SimulatorOf(roots[1]));
        Assert.Equal("MSFS 2024", Bbs757WorkFolder.SimulatorOf(roots[2]));   // Steam
        Assert.Equal("MSFS 2024", Bbs757WorkFolder.SimulatorOf(roots[3]));
        Assert.Equal("MSFS 2020", Bbs757WorkFolder.SimulatorOf(roots[4]));   // Microsoft Store
        Assert.Equal("MSFS 2020", Bbs757WorkFolder.SimulatorOf(roots[5]));   // Steam
        Assert.Equal("MSFS 2024", Bbs757WorkFolder.SimulatorOf(roots[0].ToUpperInvariant()));
    }
}
