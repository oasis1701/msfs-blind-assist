using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace MSFSBlindAssist.DisplayLayouts;

/// <summary>
/// The layout file's text: one JSON object holding <c>formatVersion</c>, <c>module</c>, <c>generatorVersion</c>,
/// <c>codeHash</c>, <c>dataRanges</c>, <c>dataHash</c>, <c>searches</c> and <c>readers</c>, in that order, with one
/// <c>{ "layout": … }</c> or <c>{ "failure": "…" }</c> per module reader in the module's order. The same layouts give
/// byte-identical text on any PC (WASM-21): fixed property order, culture-invariant numbers, two-space indents, \n line
/// ends and a final one, no timestamps, the source file saved empty, a dictionary's entries by key. Save the text as
/// UTF-8 without a byte order mark (<c>File.WriteAllText</c>'s default).
/// </summary>
public static class LayoutFileFormat
{
    public const int Version = 1;

    /// <summary>Leaves text such as "°" readable instead of escaping it.</summary>
    private static readonly JavaScriptEncoder Escaping = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    /// <summary>How deep the text may nest, the same for writing and reading, so a file that writes also loads (the
    /// serializer's and the document reader's default is 64, the writer's 1000; a PFD expression tree nests one level
    /// per node).</summary>
    private const int MaxDepth = 256;

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = Escaping,
        MaxDepth = MaxDepth,
    };

    /// <summary>How layouts are saved and loaded. Computed properties are not saved, and every other member of every
    /// layout record is, nulls included. Loading refuses a layout that lacks any member (a struct's such as a
    /// <c>GlassField</c>'s offset, an optional part's null included) or holds null where its record does not allow one,
    /// so a damaged file never loads as offsets of 0.</summary>
    private static readonly JsonSerializerOptions LayoutOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IgnoreReadOnlyProperties = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Encoder = Escaping,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        MaxDepth = MaxDepth,
        Converters = { new JsonStringEnumConverter(), new KeyOrderedDictionaryConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { EveryMemberRequired } },
    };

    /// <summary>The file's text. Each layout is saved with an empty source file.</summary>
    /// <exception cref="ArgumentException">A hash is not 64 lowercase hex characters, or the readers are not exactly the
    /// module's, each once, each with a layout of its type or a failure, not both.</exception>
    /// <exception cref="JsonException">A layout nests deeper than the file allows, or holds null where its record does
    /// not allow one.</exception>
    public static string Write(ModuleDescription module, LayoutFile file)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(file);
        if (!IsHash(file.CodeHash)) throw new ArgumentException($"codeHash {NotAHash}", nameof(file));
        if (!IsHash(file.DataHash)) throw new ArgumentException($"dataHash {NotAHash}", nameof(file));
        var results = ResultsInModuleOrder(module, file);

        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer, WriterOptions))
        {
            w.WriteStartObject();
            w.WriteNumber("formatVersion", Version);
            w.WriteString("module", module.Id);
            w.WriteNumber("generatorVersion", file.GeneratorVersion);
            w.WriteString("codeHash", file.CodeHash);
            w.WriteStartArray("dataRanges");
            foreach (var range in file.DataRanges)
            {
                w.WriteStartObject();
                w.WriteNumber("address", range.Address);
                w.WriteNumber("length", range.Length);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteString("dataHash", file.DataHash);
            w.WriteStartArray("searches");
            foreach (var search in file.Searches)
            {
                w.WriteStartObject();
                w.WriteBase64String("pattern", search.Pattern);
                w.WriteStartArray("hits");
                foreach (var hit in search.Hits) w.WriteNumberValue(hit);
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartObject("readers");
            foreach (var (reader, result) in results)
            {
                w.WriteStartObject(reader.Id);
                if (result.Layout is { } layout)
                {
                    w.WritePropertyName("layout");
                    JsonSerializer.Serialize(w, reader.WithSourceFile(layout, ""), reader.LayoutType, LayoutOptions);
                }
                else
                {
                    w.WriteString("failure", result.Failure);
                }
                w.WriteEndObject();
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";
    }

    /// <summary>The file in <paramref name="text"/>, its layouts with an empty source file, or why it cannot be used:
    /// not JSON, a format version other than <see cref="Version"/>, another module, a reader missing, extra or holding
    /// both or neither of a layout and a failure, or a layout that does not load.</summary>
    public static bool TryRead(ModuleDescription module, string text,
        [NotNullWhen(true)] out LayoutFile? file, [NotNullWhen(false)] out string? failure)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(text);
        file = null;
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { AllowDuplicateProperties = false, MaxDepth = MaxDepth });
            file = Read(module, document.RootElement);
            failure = null;
            return true;
        }
        catch (JsonException e)
        {
            failure = "not a layout file: " + e.Message;
            return false;
        }
        catch (LayoutFileException e)
        {
            failure = e.Message;
            return false;
        }
    }

    private static IReadOnlyList<(LayoutReader Reader, ReaderResult Result)> ResultsInModuleOrder(ModuleDescription module, LayoutFile file)
    {
        var byId = new Dictionary<string, ReaderResult>(StringComparer.Ordinal);
        foreach (var result in file.Readers)
        {
            if (!module.Readers.Any(r => r.Id == result.Reader))
                throw new ArgumentException($"reader {result.Reader} is not one of module {module.Id}'s", nameof(file));
            if (!byId.TryAdd(result.Reader, result))
                throw new ArgumentException($"reader {result.Reader} is given twice", nameof(file));
        }
        var ordered = new List<(LayoutReader, ReaderResult)>();
        foreach (var reader in module.Readers)
        {
            if (!byId.TryGetValue(reader.Id, out var result))
                throw new ArgumentException($"reader {reader.Id} is missing", nameof(file));
            if ((result.Layout is null) == (result.Failure is null))
                throw new ArgumentException($"reader {reader.Id} needs a layout or a failure, not both or neither", nameof(file));
            if (result.Layout is not null && !reader.LayoutType.IsInstanceOfType(result.Layout))
                throw new ArgumentException($"reader {reader.Id}: the layout is a {result.Layout.GetType().Name}, not a {reader.LayoutType.Name}", nameof(file));
            ordered.Add((reader, result));
        }
        return ordered;
    }

    private static LayoutFile Read(ModuleDescription module, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new LayoutFileException("not a layout file: the text is not a JSON object");
        if (!root.TryGetProperty("formatVersion", out var version)) throw new LayoutFileException("the file has no format version");
        if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int number) || number != Version)
            throw new LayoutFileException($"format version {version.GetRawText()} is not known");
        string moduleId = GetText(root, "module");
        if (moduleId != module.Id) throw new LayoutFileException($"the file is for module {moduleId}, not {module.Id}");

        int generatorVersion = WholeNumber(Get(root, "generatorVersion"), "generatorVersion");
        string codeHash = GetHash(root, "codeHash");
        var ranges = GetList(root, "dataRanges")
            .Select(r => new DataRange(Address(Get(r, "address"), "a data range's address"), WholeNumber(Get(r, "length"), "a data range's length")))
            .ToList();
        string dataHash = GetHash(root, "dataHash");
        var searches = GetList(root, "searches").Select(ReadSearch).ToList();

        var readers = Get(root, "readers");
        if (readers.ValueKind != JsonValueKind.Object) throw new LayoutFileException("readers is not an object");
        foreach (var entry in readers.EnumerateObject())
        {
            if (!module.Readers.Any(r => r.Id == entry.Name))
                throw new LayoutFileException($"reader {entry.Name} is not one of module {module.Id}'s");
        }
        var results = new List<ReaderResult>();
        foreach (var reader in module.Readers)
        {
            if (!readers.TryGetProperty(reader.Id, out var entry)) throw new LayoutFileException($"reader {reader.Id} is missing");
            results.Add(ReadResult(reader, entry));
        }
        return new LayoutFile(generatorVersion, codeHash, ranges, dataHash, searches, results);
    }

    private static DataSearch ReadSearch(JsonElement search)
    {
        var pattern = Get(search, "pattern");
        if (pattern.ValueKind != JsonValueKind.String || !pattern.TryGetBytesFromBase64(out var bytes))
            throw new LayoutFileException("a search's pattern is not base64");
        return new DataSearch(bytes, GetList(search, "hits").Select(h => Address(h, "a search's hit")).ToList());
    }

    private static ReaderResult ReadResult(LayoutReader reader, JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object) throw new LayoutFileException($"reader {reader.Id} is not an object");
        bool hasLayout = entry.TryGetProperty("layout", out var layoutElement);
        bool hasFailure = entry.TryGetProperty("failure", out var failureElement);
        if (hasLayout == hasFailure)
            throw new LayoutFileException($"reader {reader.Id} holds {(hasLayout ? "both a layout and a failure" : "neither a layout nor a failure")}");
        if (entry.EnumerateObject().Count() != 1)
            throw new LayoutFileException($"reader {reader.Id} holds more than a {(hasLayout ? "layout" : "failure")}");

        if (hasFailure)
        {
            if (failureElement.ValueKind != JsonValueKind.String) throw new LayoutFileException($"reader {reader.Id}: the failure is not text");
            return new ReaderResult(reader.Id, null, failureElement.GetString()!);
        }
        object? layout;
        try
        {
            layout = layoutElement.Deserialize(reader.LayoutType, LayoutOptions);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException or ArgumentException or FormatException)
        {
            throw new LayoutFileException($"reader {reader.Id}: the layout does not load: {e.Message}");
        }
        if (layout is null) throw new LayoutFileException($"reader {reader.Id}: the layout is null");
        return new ReaderResult(reader.Id, reader.WithSourceFile(layout, ""), null);
    }

    private static JsonElement Get(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var value) ? value : throw new LayoutFileException($"{name} is missing");

    private static string GetText(JsonElement obj, string name)
    {
        var value = Get(obj, name);
        return value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new LayoutFileException($"{name} is not text");
    }

    private const string NotAHash = "is not 64 lowercase hex characters";

    /// <summary>Whether <paramref name="s"/> is a SHA-256 as the file holds it: 64 lowercase hex characters.</summary>
    private static bool IsHash(string? s) => s is { Length: 64 } && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string GetHash(JsonElement obj, string name)
    {
        string value = GetText(obj, name);
        return IsHash(value) ? value : throw new LayoutFileException($"{name} {NotAHash}");
    }

    /// <summary>Makes every settable member of every layout record required, those of the structs (loaded through their
    /// default constructor) and the optional constructor arguments included: Write saves them all, so only a damaged or
    /// hand-edited file lacks one.</summary>
    private static void EveryMemberRequired(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object || info.Type.Assembly != typeof(LayoutFileFormat).Assembly) return;
        foreach (var property in info.Properties)
        {
            if (property.Set is not null || property.AssociatedParameter is not null) property.IsRequired = true;
        }
    }

    private static IEnumerable<JsonElement> GetList(JsonElement obj, string name)
    {
        var value = Get(obj, name);
        return value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : throw new LayoutFileException($"{name} is not a list");
    }

    private static int WholeNumber(JsonElement value, string what) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int n) ? n : throw new LayoutFileException($"{what} is not a whole number");

    private static uint Address(JsonElement value, string what) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out uint n) ? n : throw new LayoutFileException($"{what} is not an address");

    private sealed class LayoutFileException(string message) : Exception(message);

    /// <summary>Saves a read-only dictionary's entries in key order (text keys ordinally, never by the PC's culture),
    /// so two equal layouts give the same text whatever order their entries were added in; loads them as a
    /// <see cref="Dictionary{TKey, TValue}"/>.</summary>
    private sealed class KeyOrderedDictionaryConverter : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) =>
            typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(typeof(KeyOrdered<,>).MakeGenericType(typeToConvert.GetGenericArguments()))!;

        private sealed class KeyOrdered<TKey, TValue> : JsonConverter<IReadOnlyDictionary<TKey, TValue>> where TKey : notnull
        {
            private static readonly IComparer<TKey> KeyOrder =
                typeof(TKey) == typeof(string) ? (IComparer<TKey>)(object)StringComparer.Ordinal : Comparer<TKey>.Default;

            public override IReadOnlyDictionary<TKey, TValue>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                JsonSerializer.Deserialize<Dictionary<TKey, TValue>>(ref reader, options);

            public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<TKey, TValue> value, JsonSerializerOptions options) =>
                JsonSerializer.Serialize(writer, new SortedDictionary<TKey, TValue>(value.ToDictionary(kv => kv.Key, kv => kv.Value), KeyOrder), options);
        }
    }
}
