using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Entities.Relics;

namespace QuriousCraftingRelics.Chaos;

/// <summary>
/// Versioned, self-contained persistence of one run's frozen generation state
/// (R04-01): the durable run identity, every frozen input of
/// <see cref="QuriousGenerationSnapshot"/> and all
/// <see cref="ChaosRelicGenerator.TotalSlots"/> generated definitions.
///
/// WHY: the frozen snapshot used to live in memory only, so a process restart,
/// a save switch or a ledger eviction re-froze the run from the LIVE config and
/// the held relics changed meaning. The payload stored here makes the
/// definitions DATA: restoring them never re-runs the generator and never reads
/// the user's config, so a .NET RNG change, a catalog change or a preference
/// edit cannot reroll an already-generated run.
///
/// FORMAT: "qcrgen" ':' schema ':' semantics ':' base64(gzip(UTF8(JSON))).
/// - schema is the payload LAYOUT version and must match exactly.
/// - semantics is the GENERATION-semantics version. A payload from an OLDER
///   semantics version is accepted verbatim (it carries its own frozen specs,
///   pools and definitions, so nothing has to be re-derived); a payload from a
///   NEWER one is rejected - this build cannot know what it means.
/// - The JSON carries the identity, the seed, the canonical fingerprint, the
///   canonical cache key, the frozen budgets/chances/gates, the full four-way
///   template metadata (positive/negative x core/extra, with the frozen
///   effective band, the catalog spec economics and the effective config
///   economics per template), the ORDERED active pools and every definition
///   with template/amount/name/rarity/text.
///
/// VALIDATION (all of it before any state is published): header and version,
/// encoded/compressed/decompressed length caps, bounded decompression, strict
/// JSON (missing, unknown, null or repeated members are rejected), identity and
/// seed binding, cache key vs seed + fingerprint, pools vs gates/templates,
/// template existence and polarity against the current catalog, per-entry band
/// and structural limits (TotalSlots, MaxPositives, at most one negative, no
/// repeated template) and, last, the canonical fingerprint RECOMPUTED from the
/// restored fields.
///
/// LIMITS: <see cref="MaxEncodedLength"/> chars of payload, at most
/// <see cref="MaxCompressedBytes"/> compressed bytes, at most
/// <see cref="MaxDecompressedBytes"/> bytes of JSON. The Steam transport caps a
/// whole reliable message at 524288 bytes, so the payload must stay far below
/// that. The same caps are enforced while ENCODING, so a state that could not be
/// decoded again is never written.
///
/// NOT TOUCHED BY DESIGN: no live config, no registry, no RNG, no global
/// snapshot. <see cref="Decode(string, string, string)"/> is a pure function of
/// the payload plus the expected identity and seed.
/// </summary>
public static class QuriousGenerationPersistence
{
    /// <summary>Payload layout version; a payload with any other value is refused.</summary>
    public const int SchemaVersion = 1;

    /// <summary>
    /// Generation-semantics version of this build. Bump when the generation
    /// algorithm changes in a way that would alter what a set of frozen inputs
    /// means; older payloads still restore verbatim (they carry their own
    /// definitions), newer ones are refused.
    /// </summary>
    public const int GeneratorSemanticsVersion = 1;

    /// <summary>
    /// Hard cap on the ENCODED payload, in characters. ASCII base64, so one
    /// character is one byte on the wire; kept far below the 524288 byte Steam
    /// message cap because the payload shares that budget with the whole run.
    /// </summary>
    public const int MaxEncodedLength = 65536;

    /// <summary>
    /// Hard cap on the compressed body, in bytes: the base64 capacity of
    /// <see cref="MaxEncodedLength"/> minus the fixed header, so any body that
    /// passes this cap re-encodes within the character cap.
    /// </summary>
    public const int MaxCompressedBytes = (MaxEncodedLength - 16) / 4 * 3;

    /// <summary>
    /// Hard cap on the decompressed UTF-8 JSON, in bytes; decompression stops
    /// past it, so a crafted body cannot expand without bound.
    /// </summary>
    public const int MaxDecompressedBytes = 1048576;

    private const string Magic = "qcrgen";
    private const char Separator = ':';
    private static readonly char[] Whitespace = { ' ', '\t', '\n', '\r', '\f', '\v' };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false,
    };

    /// <summary>
    /// Encodes one run's frozen generation state. Pure: reads the snapshot and
    /// the definitions, writes nothing anywhere else. Throws
    /// <see cref="InvalidOperationException"/> for a state this format cannot
    /// represent (that state must never reach a save file).
    /// </summary>
    public static string Encode(
        string identity, QuriousGenerationSnapshot snapshot, IReadOnlyList<ChaosRelicDefinition> definitions)
    {
        // The identity is taken VERBATIM: an unrecognized (foreign) token must
        // stay bindable, so no shape check is applied. Only the empty string is
        // refused - it would make the payload unbindable.
        if (string.IsNullOrEmpty(identity))
        {
            throw new ArgumentException("identity must not be empty", nameof(identity));
        }
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(definitions);
        if (string.IsNullOrEmpty(snapshot.RunSeed))
        {
            throw new InvalidOperationException(
                "the frozen snapshot carries no run seed; a payload must be bound to a seed.");
        }

        var payload = new PayloadDto
        {
            Identity = identity,
            Seed = snapshot.RunSeed!,
            Schema = SchemaVersion,
            GeneratorSemantics = GeneratorSemanticsVersion,
            Fingerprint = snapshot.CanonicalFingerprint,
            CacheKey = snapshot.CanonicalCacheKey,
            Multiplier = snapshot.Multiplier,
            BudgetCommon = snapshot.BudgetCommon,
            BudgetUncommon = snapshot.BudgetUncommon,
            BudgetRare = snapshot.BudgetRare,
            NegativeChanceCommon = snapshot.NegativeChanceCommon,
            NegativeChanceUncommon = snapshot.NegativeChanceUncommon,
            NegativeChanceRare = snapshot.NegativeChanceRare,
            EnableExtraPool = snapshot.EnableExtraPool,
            WatcherModLoaded = snapshot.WatcherModLoaded,
            ActivePositives = snapshot.ActivePositiveTemplates.ToArray(),
            ActiveNegatives = snapshot.ActiveNegativeTemplates.ToArray(),
            Templates = BuildTemplateDtos(snapshot),
            Definitions = definitions.Select(ToDto).ToArray(),
        };

        // Validate BEFORE anything is written: a state that this format cannot
        // represent consistently (or that Decode would refuse) must never reach
        // a save file - writing one would mean the run's own definitions could
        // not be read back. The validator raises InvalidDataException; Encode's
        // contract is InvalidOperationException for an unrepresentable state,
        // so the exception is re-typed here (the message is kept).
        try
        {
            Validate(payload, identity, snapshot.RunSeed!, checkBinding: true);
        }
        catch (InvalidDataException e)
        {
            throw new InvalidOperationException(
                "the frozen state cannot be persisted consistently: " + e.Message, e);
        }

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        if (json.Length > MaxDecompressedBytes)
        {
            throw new InvalidOperationException(
                $"generation payload JSON is {json.Length} bytes, above the {MaxDecompressedBytes} byte cap.");
        }

        byte[] compressed = Compress(json);
        if (compressed.Length > MaxCompressedBytes)
        {
            throw new InvalidOperationException(
                $"generation payload compresses to {compressed.Length} bytes, above the " +
                $"{MaxCompressedBytes} byte cap.");
        }

        string encoded = Convert.ToBase64String(compressed);
        string result = Magic + Separator
            + SchemaVersion.ToString(Invariant) + Separator
            + GeneratorSemanticsVersion.ToString(Invariant) + Separator
            + encoded;
        if (result.Length > MaxEncodedLength)
        {
            throw new InvalidOperationException(
                $"generation payload encodes to {result.Length} chars, above the {MaxEncodedLength} char cap.");
        }

        return result;
    }

    /// <summary>
    /// Decodes and fully validates a payload against the run it is expected to
    /// belong to. Every failure throws <see cref="InvalidDataException"/>; the
    /// caller must treat that as "keep the saved state, do not publish" - this
    /// method never mutates or clears anything.
    /// </summary>
    public static QuriousSavedGeneration Decode(string payload, string expectedIdentity, string expectedSeed)
    {
        if (expectedIdentity is null)
        {
            throw new InvalidDataException("expected identity is null; a payload is never bound to a null run");
        }
        if (expectedSeed is null)
        {
            throw new InvalidDataException("expected seed is null; a payload is never bound to a null seed");
        }
        if (payload is null)
        {
            throw new InvalidDataException("generation payload is null");
        }
        if (payload.Length == 0)
        {
            throw new InvalidDataException("generation payload is empty");
        }
        if (payload.Length > MaxEncodedLength)
        {
            throw new InvalidDataException(
                $"generation payload is {payload.Length} chars, above the {MaxEncodedLength} char cap");
        }
        if (payload.IndexOfAny(Whitespace) >= 0)
        {
            throw new InvalidDataException(
                "generation payload contains whitespace; the encoded form never does");
        }

        string[] header = payload.Split(Separator, 4);
        if (header.Length != 4 || header[0] != Magic)
        {
            throw new InvalidDataException("generation payload header is not a qcrgen payload");
        }
        if (!int.TryParse(header[1], NumberStyles.None, Invariant, out int schema))
        {
            throw new InvalidDataException($"generation payload schema '{header[1]}' is not an integer");
        }
        if (schema != SchemaVersion)
        {
            throw new InvalidDataException(
                $"generation payload schema {schema} is not supported by this build (expected {SchemaVersion})");
        }
        if (!int.TryParse(header[2], NumberStyles.None, Invariant, out int semantics))
        {
            throw new InvalidDataException(
                $"generation payload semantics '{header[2]}' is not an integer");
        }
        if (semantics < 1)
        {
            throw new InvalidDataException($"generation payload semantics {semantics} is not a valid version");
        }
        if (semantics > GeneratorSemanticsVersion)
        {
            throw new InvalidDataException(
                $"generation payload was written with generation semantics {semantics}; this build " +
                $"understands up to {GeneratorSemanticsVersion} and must not guess");
        }

        byte[] compressed = DecodeBase64(header[3]);
        byte[] json = DecompressBounded(compressed);

        PayloadDto? dto;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32,
            });
            RejectDuplicateMembers(document.RootElement, "root", 0);
            dto = document.RootElement.Deserialize<PayloadDto>(JsonOptions);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"generation payload JSON is invalid: {e.Message}");
        }
        catch (NotSupportedException e)
        {
            throw new InvalidDataException($"generation payload JSON is not supported: {e.Message}");
        }

        if (dto is null)
        {
            throw new InvalidDataException("generation payload JSON is null");
        }

        return Restore(dto, expectedIdentity, expectedSeed);
    }

    /// <summary>
    /// Execution-capability gate, kept separate from <see cref="Decode"/> so the
    /// codec itself stays a pure function of the payload (Decode is also used by
    /// probes and offline tooling, where no live capability state exists).
    /// Entries whose effect cannot run in THIS process (currently: the Watcher
    /// stance templates, which need the Watcher mod's combat helper) make the
    /// whole restore invalid - the caller must not publish a run whose entries
    /// would silently do nothing. Every offending entry is named in the
    /// exception, and no entry is ever dropped.
    /// </summary>
    public static void ValidateExecutionCapability(
        IReadOnlyList<ChaosRelicDefinition> definitions, bool watcherModLoaded)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        if (watcherModLoaded)
        {
            return;
        }

        List<string>? unavailable = null;
        foreach (ChaosRelicDefinition definition in definitions)
        {
            foreach (ChaosRelicOperation operation in definition.Operations)
            {
                if (!ChaosRelicExtraCatalog.WatcherTemplates.Contains(operation.Template, StringComparer.Ordinal))
                {
                    continue;
                }
                (unavailable ??= new List<string>()).Add($"slot {definition.Slot} entry {operation.Template}");
            }
        }

        if (unavailable is not null)
        {
            throw new InvalidDataException(
                "the saved run carries entries that cannot execute without the Watcher mod: " +
                string.Join(", ", unavailable) +
                "; the entries are kept, and the run is refused rather than silently degraded.");
        }
    }

    /// <summary>
    /// Fully validated restore. The returned value is a fresh object graph -
    /// arrays, lists and specs are copies, so mutating the returned definitions
    /// cannot alter the saved state and vice versa.
    /// </summary>
    private static QuriousSavedGeneration Restore(PayloadDto dto, string expectedIdentity, string expectedSeed)
    {
        Validate(dto, expectedIdentity, expectedSeed, checkBinding: true);

        var templateById = new Dictionary<string, TemplateDto>(dto.Templates!.Length, StringComparer.Ordinal);
        foreach (TemplateDto template in dto.Templates!)
        {
            templateById[template.Template!] = template;
        }

        var costs = new Dictionary<string, int>(dto.Templates!.Length, StringComparer.Ordinal);
        var refunds = new Dictionary<string, int>(dto.Templates!.Length, StringComparer.Ordinal);
        var bounds = new Dictionary<string, (int Min, int Max)>(dto.Templates!.Length, StringComparer.Ordinal);
        var effectiveSpecs = new Dictionary<string, ChaosRelicCatalog.TemplateSpec>(
            dto.Templates!.Length, StringComparer.Ordinal);
        foreach (TemplateDto template in dto.Templates!)
        {
            string id = template.Template!;
            costs[id] = template.CostPerPoint!.Value;
            refunds[id] = template.RefundPerPoint!.Value;
            bounds[id] = (template.Min!.Value, template.Max!.Value);
            effectiveSpecs[id] = new ChaosRelicCatalog.TemplateSpec(
                id,
                template.IsNegative!.Value,
                template.Min.Value,
                template.Max.Value,
                template.CatalogCostPerPoint!.Value,
                template.CatalogRefundPerPoint!.Value,
                template.TextPattern!,
                template.Decaying!.Value);
        }

        var definitions = new ChaosRelicDefinition[dto.Definitions!.Length];
        for (int i = 0; i < dto.Definitions!.Length; i++)
        {
            definitions[i] = ToDefinition(dto.Definitions[i]!);
        }

        var snapshot = QuriousGenerationSnapshot.Restore(new QuriousGenerationSnapshot.RestoreInput
        {
            RunSeed = dto.Seed!,
            Multiplier = dto.Multiplier!.Value,
            BudgetCommon = dto.BudgetCommon!.Value,
            BudgetUncommon = dto.BudgetUncommon!.Value,
            BudgetRare = dto.BudgetRare!.Value,
            NegativeChanceCommon = dto.NegativeChanceCommon!.Value,
            NegativeChanceUncommon = dto.NegativeChanceUncommon!.Value,
            NegativeChanceRare = dto.NegativeChanceRare!.Value,
            EnableExtraPool = dto.EnableExtraPool!.Value,
            WatcherModLoaded = dto.WatcherModLoaded!.Value,
            Costs = costs,
            Refunds = refunds,
            Bounds = bounds,
            EffectiveSpecs = effectiveSpecs,
            ActivePositiveTemplates = dto.ActivePositives!,
            ActiveNegativeTemplates = dto.ActiveNegatives!,
            CanonicalFingerprint = dto.Fingerprint!,
            CanonicalCacheKey = dto.CacheKey!,
        });

        return new QuriousSavedGeneration(expectedIdentity, snapshot, definitions);
    }

    private static ChaosRelicDefinition ToDefinition(DefinitionDto dto)
    {
        var operations = new List<ChaosRelicOperation>(dto.Operations!.Length);
        foreach (OperationDto operation in dto.Operations!)
        {
            operations.Add(new ChaosRelicOperation(operation.Template!, operation.Amount!.Value, operation.Text!));
        }
        return new ChaosRelicDefinition(
            dto.Slot!.Value, (RelicRarity)dto.Rarity!.Value, dto.Name!, operations.AsReadOnly());
    }

    private static DefinitionDto ToDto(ChaosRelicDefinition definition) => new()
    {
        Slot = definition.Slot,
        Rarity = (int)definition.Rarity,
        Name = definition.Name,
        Operations = definition.Operations!.Select(op => new OperationDto
        {
            Template = op.Template,
            Amount = op.Amount,
            Text = op.Text,
        }).ToArray(),
    };

    private static TemplateDto[] BuildTemplateDtos(QuriousGenerationSnapshot snapshot)
    {
        var templates = new List<string>(ChaosRelicCatalog.PositiveTemplates);
        templates.AddRange(ChaosRelicCatalog.NegativeTemplates);
        templates.AddRange(ChaosRelicExtraCatalog.PositiveTemplates);
        templates.AddRange(ChaosRelicExtraCatalog.NegativeTemplates);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var dtos = new List<TemplateDto>(templates.Count);
        foreach (string template in templates)
        {
            if (!seen.Add(template))
            {
                continue;
            }
            var spec = ChaosTemplates.Spec(template);
            var effective = snapshot.EffectiveSpecFor(template)
                ?? throw new InvalidOperationException(
                    $"the frozen snapshot has no effective spec for template {template}; it cannot be persisted.");
            var (min, max) = snapshot.BoundsFor(template)
                ?? throw new InvalidOperationException(
                    $"the frozen snapshot has no bounds for template {template}; it cannot be persisted.");
            if (effective.Min != min || effective.Max != max)
            {
                throw new InvalidOperationException(
                    $"the frozen snapshot's effective band for {template} disagrees with its bounds; " +
                    "it cannot be persisted consistently.");
            }
            dtos.Add(new TemplateDto
            {
                Template = template,
                IsNegative = spec.IsNegative,
                Decaying = spec.Decaying,
                TextPattern = spec.TextPattern,
                Min = min,
                Max = max,
                CatalogCostPerPoint = spec.CostPerPoint,
                CatalogRefundPerPoint = spec.RefundPerPoint,
                CostPerPoint = snapshot.FrozenCosts.CostPerPoint(template),
                RefundPerPoint = snapshot.FrozenCosts.RefundPerPoint(template),
            });
        }
        return dtos.ToArray();
    }

    // ---------- validation ----------

    private static void Validate(PayloadDto dto, string expectedIdentity, string expectedSeed, bool checkBinding)
    {
        Require(dto.Schema, nameof(dto.Schema));
        Require(dto.GeneratorSemantics, nameof(dto.GeneratorSemantics));
        Require(dto.Identity, nameof(dto.Identity));
        Require(dto.Seed, nameof(dto.Seed));
        Require(dto.Fingerprint, nameof(dto.Fingerprint));
        Require(dto.CacheKey, nameof(dto.CacheKey));
        Require(dto.Multiplier, nameof(dto.Multiplier));
        Require(dto.BudgetCommon, nameof(dto.BudgetCommon));
        Require(dto.BudgetUncommon, nameof(dto.BudgetUncommon));
        Require(dto.BudgetRare, nameof(dto.BudgetRare));
        Require(dto.NegativeChanceCommon, nameof(dto.NegativeChanceCommon));
        Require(dto.NegativeChanceUncommon, nameof(dto.NegativeChanceUncommon));
        Require(dto.NegativeChanceRare, nameof(dto.NegativeChanceRare));
        Require(dto.EnableExtraPool, nameof(dto.EnableExtraPool));
        Require(dto.WatcherModLoaded, nameof(dto.WatcherModLoaded));
        Require(dto.ActivePositives, nameof(dto.ActivePositives));
        Require(dto.ActiveNegatives, nameof(dto.ActiveNegatives));
        Require(dto.Templates, nameof(dto.Templates));
        Require(dto.Definitions, nameof(dto.Definitions));

        if (dto.Schema != SchemaVersion)
        {
            throw new InvalidDataException($"payload schema {dto.Schema} does not match {SchemaVersion}");
        }
        if (dto.GeneratorSemantics is < 1 or > GeneratorSemanticsVersion)
        {
            throw new InvalidDataException(
                $"payload generation semantics {dto.GeneratorSemantics} is outside the supported range");
        }
        if (checkBinding)
        {
            if (!string.Equals(dto.Identity, expectedIdentity, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "generation payload belongs to a different run identity than the one being loaded");
            }
            if (!string.Equals(dto.Seed, expectedSeed, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "generation payload was frozen for a different seed than the run being loaded");
            }
        }

        // Cache key shape = seed + NUL + fingerprint, identical to the frozen
        // CanonicalCacheKey (QuriousGenerationSnapshot.Capture builds it the
        // same way), so a payload whose binding fields were edited cannot keep
        // a cache key that no longer describes them.
        string expectedCacheKey = dto.Seed! + "\0" + dto.Fingerprint!;
        if (!string.Equals(dto.CacheKey, expectedCacheKey, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "generation payload cache key does not match its own seed and fingerprint");
        }

        if (dto.Templates!.Length == 0)
        {
            throw new InvalidDataException("generation payload carries no template metadata");
        }
        var templateById = new Dictionary<string, TemplateDto>(dto.Templates!.Length, StringComparer.Ordinal);
        foreach (TemplateDto template in dto.Templates!)
        {
            if (template is null)
            {
                throw new InvalidDataException("generation payload has a null template metadata entry");
            }
            Require(template.Template, nameof(template.Template));
            Require(template.TextPattern, nameof(template.TextPattern));
            Require(template.IsNegative, nameof(template.IsNegative));
            Require(template.Decaying, nameof(template.Decaying));
            Require(template.Min, nameof(template.Min));
            Require(template.Max, nameof(template.Max));
            Require(template.CatalogCostPerPoint, nameof(template.CatalogCostPerPoint));
            Require(template.CatalogRefundPerPoint, nameof(template.CatalogRefundPerPoint));
            Require(template.CostPerPoint, nameof(template.CostPerPoint));
            Require(template.RefundPerPoint, nameof(template.RefundPerPoint));
            if (!templateById.TryAdd(template.Template!, template))
            {
                throw new InvalidDataException(
                    $"generation payload repeats template metadata for {template.Template}");
            }
            if (template.Min!.Value > template.Max!.Value)
            {
                throw new InvalidDataException(
                    $"generation payload has an inverted band for template {template.Template}");
            }
            if (template.CostPerPoint!.Value < 0 || template.RefundPerPoint!.Value < 0)
            {
                throw new InvalidDataException(
                    $"generation payload has a negative price for template {template.Template}");
            }
            if (!ChaosTemplates.Has(template.Template!))
            {
                throw new InvalidDataException(
                    $"generation payload references template {template.Template}, which this build's " +
                    "catalog does not know; its entries could not be executed, so the payload is refused " +
                    "instead of dropping them.");
            }
            var spec = ChaosTemplates.Spec(template.Template!);
            if (spec.IsNegative != template.IsNegative!.Value)
            {
                throw new InvalidDataException(
                    $"generation payload classifies template {template.Template} differently from the catalog");
            }
            // Decaying/text are carried by the payload and verified against the
            // payload's own stored values (see ValidateDefinitions); they are
            // not compared to the catalog, so a catalog text edit cannot make an
            // already-saved run unrestorable. Only what EXECUTION depends on is
            // compared: existence and polarity.
        }

        ValidatePool(dto.ActivePositives!, templateById, expectNegative: false, "active positive pool");
        ValidatePool(dto.ActiveNegatives!, templateById, expectNegative: true, "active negative pool");
        // Gate consistency: a pool entry the frozen gates could not have
        // produced means the payload is internally inconsistent (flags edited,
        // pools from another run, partial write). Checked as a subset rule, not
        // as set equality, so a payload written by a build with a different
        // template inventory still restores verbatim.
        foreach (string id in dto.ActivePositives!)
        {
            CheckPoolGates(dto, id, "active positive pool");
        }
        foreach (string id in dto.ActiveNegatives!)
        {
            CheckPoolGates(dto, id, "active negative pool");
        }
        if (!dto.ActivePositives!.Any(id => ChaosRelicCatalog.HasTemplate(id)))
        {
            throw new InvalidDataException(
                "generation payload's active positive pool carries no core-pool template");
        }
        if (!dto.ActiveNegatives!.Any(id => ChaosRelicCatalog.HasTemplate(id)))
        {
            throw new InvalidDataException(
                "generation payload's active negative pool carries no core-pool template");
        }

        ValidateDefinitions(dto.Definitions!, templateById, dto.ActivePositives!, dto.ActiveNegatives!);
        ValidateFingerprint(dto, templateById);
    }

    /// <summary>
    /// A pool entry must be producible from the frozen gates alone: core
    /// templates are always allowed; extra templates require the extra pool
    /// (and, for the stance positives, the Watcher mod that was recorded as
    /// loaded at capture). Anything else is an inconsistent payload.
    /// </summary>
    private static void CheckPoolGates(PayloadDto dto, string id, string what)
    {
        if (ChaosRelicCatalog.HasTemplate(id))
        {
            return;
        }
        if (!dto.EnableExtraPool!.Value)
        {
            throw new InvalidDataException(
                $"generation payload's {what} contains extra template {id} while the frozen extra pool is off");
        }
        if (ChaosRelicExtraCatalog.WatcherTemplates.Contains(id, StringComparer.Ordinal)
            && !dto.WatcherModLoaded!.Value)
        {
            throw new InvalidDataException(
                $"generation payload's {what} contains Watcher template {id} while the frozen state says " +
                "the Watcher mod was not loaded");
        }
    }

    private static void ValidatePool(
        string[] pool, Dictionary<string, TemplateDto> templateById, bool expectNegative, string what)
    {
        if (pool.Length == 0)
        {
            throw new InvalidDataException($"generation payload's {what} is empty");
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in pool)
        {
            if (id is null)
            {
                throw new InvalidDataException($"generation payload's {what} contains a null template");
            }
            if (!templateById.TryGetValue(id, out TemplateDto? template))
            {
                throw new InvalidDataException(
                    $"generation payload's {what} references {id}, which has no frozen metadata");
            }
            if (template.IsNegative!.Value != expectNegative)
            {
                throw new InvalidDataException(
                    $"generation payload's {what} contains {id} with the wrong polarity");
            }
            if (!seen.Add(id))
            {
                throw new InvalidDataException($"generation payload's {what} repeats template {id}");
            }
        }
    }

    private static void ValidateDefinitions(
        DefinitionDto[] definitions,
        Dictionary<string, TemplateDto> templateById,
        string[] activePositives,
        string[] activeNegatives)
    {
        // An entry can only have come from the payload's own active pools; a
        // definition referencing anything else means pools and definitions were
        // written from different states (or edited), so the payload is refused.
        // The polarity split is the TEMPLATE's own isNegative flag (the
        // generator's own negative test), so a template the generator treats as
        // a positive is always checked against the positive pool.
        var positives = new HashSet<string>(
            activePositives.Where(id => !templateById[id].IsNegative!.Value), StringComparer.Ordinal);
        var negatives = new HashSet<string>(
            activeNegatives.Where(id => templateById[id].IsNegative!.Value), StringComparer.Ordinal);

        if (definitions.Length != ChaosRelicGenerator.TotalSlots)
        {
            throw new InvalidDataException(
                $"generation payload carries {definitions.Length} definitions; this build has " +
                $"{ChaosRelicGenerator.TotalSlots} slots");
        }
        var slots = new bool[ChaosRelicGenerator.TotalSlots];
        foreach (DefinitionDto definition in definitions)
        {
            if (definition is null)
            {
                throw new InvalidDataException("generation payload has a null definition entry");
            }
            Require(definition.Slot, nameof(definition.Slot));
            Require(definition.Rarity, nameof(definition.Rarity));
            Require(definition.Name, nameof(definition.Name));
            Require(definition.Operations, nameof(definition.Operations));
            int slot = definition.Slot!.Value;
            if (slot < 0 || slot >= ChaosRelicGenerator.TotalSlots)
            {
                throw new InvalidDataException($"generation payload has out-of-range slot {slot}");
            }
            if (slots[slot])
            {
                throw new InvalidDataException($"generation payload repeats slot {slot}");
            }
            slots[slot] = true;
            if (definition.Name!.Length == 0)
            {
                throw new InvalidDataException($"generation payload has an empty name for slot {slot}");
            }
            int rarity = definition.Rarity!.Value;
            if (!Enum.IsDefined(typeof(RelicRarity), rarity))
            {
                throw new InvalidDataException($"generation payload has unknown rarity {rarity} for slot {slot}");
            }
            if ((int)ChaosRelicGenerator.RarityForSlot(slot) != rarity)
            {
                throw new InvalidDataException(
                    $"slot {slot} carries rarity {rarity}, but this build derives " +
                    $"{(int)ChaosRelicGenerator.RarityForSlot(slot)} for that slot");
            }
            int negativeCount = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (OperationDto operation in definition.Operations!)
            {
                if (operation is null)
                {
                    throw new InvalidDataException($"slot {slot} has a null entry");
                }
                Require(operation.Template, nameof(operation.Template));
                Require(operation.Amount, nameof(operation.Amount));
                Require(operation.Text, nameof(operation.Text));
                string id = operation.Template!;
                if (!templateById.TryGetValue(id, out TemplateDto? template))
                {
                    throw new InvalidDataException(
                        $"slot {slot} carries unknown template {id}; the payload is refused rather than " +
                        "dropping the entry.");
                }
                if (!seen.Add(id))
                {
                    throw new InvalidDataException($"slot {slot} repeats template {id}");
                }
                bool negative = template.IsNegative!.Value;
                if (!(negative ? negatives : positives).Contains(id))
                {
                    throw new InvalidDataException(
                        $"slot {slot} carries {id}, which is not in the payload's own active pool for " +
                        "that polarity; pools and definitions disagree.");
                }
                if (template.IsNegative!.Value)
                {
                    negativeCount++;
                    if (negativeCount > 1)
                    {
                        throw new InvalidDataException($"slot {slot} carries more than one negative entry");
                    }
                }
                int amount = operation.Amount!.Value;
                if (amount < template.Min!.Value || amount > template.Max!.Value)
                {
                    throw new InvalidDataException(
                        $"slot {slot} has amount {amount} outside the frozen band " +
                        $"{template.Min}-{template.Max} for {id}");
                }
                string expectedText = ChaosRelicGenerator.RenderOperation(
                    new ChaosRelicCatalog.TemplateSpec(
                        id,
                        template.IsNegative.Value,
                        template.Min.Value,
                        template.Max.Value,
                        template.CatalogCostPerPoint!.Value,
                        template.CatalogRefundPerPoint!.Value,
                        template.TextPattern!,
                        template.Decaying!.Value),
                    amount);
                if (!string.Equals(operation.Text, expectedText, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"slot {slot} carries text that does not match {id} at amount {amount}");
                }
            }
            int positiveCount = definition.Operations!.Length - negativeCount;
            if (positiveCount < 1)
            {
                throw new InvalidDataException($"slot {slot} has no positive entry");
            }
            if (positiveCount > ChaosRelicGenerator.MaxPositives)
            {
                throw new InvalidDataException(
                    $"slot {slot} has {positiveCount} positive entries, above MaxPositives " +
                    $"{ChaosRelicGenerator.MaxPositives}");
            }
        }
    }

    /// <summary>
    /// The last gate: rebuild the canonical fingerprint from the RESTORED
    /// fields and require it to equal the one stored in the payload. Any drift
    /// between the stored fields and the stored fingerprint (a hand-edited or
    /// partially written payload) is refused here instead of being published.
    /// </summary>
    private static void ValidateFingerprint(PayloadDto dto, Dictionary<string, TemplateDto> templateById)
    {
        var ordered = new List<string>(dto.ActivePositives!);
        ordered.AddRange(dto.ActiveNegatives!);
        ordered.Sort(StringComparer.Ordinal);

        var sb = new System.Text.StringBuilder(256);
        sb.Append(dto.BudgetCommon!.Value).Append('/')
          .Append(dto.BudgetUncommon!.Value).Append('/')
          .Append(dto.BudgetRare!.Value).Append('/')
          .Append(dto.NegativeChanceCommon!.Value).Append('/')
          .Append(dto.NegativeChanceUncommon!.Value).Append('/')
          .Append(dto.NegativeChanceRare!.Value).Append('/')
          .Append(dto.EnableExtraPool!.Value ? '1' : '0').Append('/')
          .Append(dto.WatcherModLoaded!.Value ? '1' : '0');
        foreach (string id in ordered)
        {
            TemplateDto template = templateById[id];
            sb.Append('|').Append(id)
              .Append(':').Append(template.CostPerPoint!.Value)
              .Append(':').Append(template.RefundPerPoint!.Value)
              .Append(':').Append(template.Min!.Value)
              .Append(':').Append(template.Max!.Value);
        }

        if (!string.Equals(sb.ToString(), dto.Fingerprint, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "generation payload's stored fingerprint does not match its own frozen fields; the " +
                "payload is inconsistent and was not restored.");
        }
    }

    // ---------- helpers ----------

    private static void Require<T>(T? value, string name) where T : class
    {
        if (value is null)
        {
            throw new InvalidDataException(
                $"generation payload is missing {name}; a field-less payload is never completed from " +
                "the live configuration");
        }
    }

    private static void Require(int? value, string name)
    {
        if (!value.HasValue)
        {
            throw new InvalidDataException(
                $"generation payload is missing {name}; a field-less payload is never completed from " +
                "the live configuration");
        }
    }

    private static void Require(bool? value, string name)
    {
        if (!value.HasValue)
        {
            throw new InvalidDataException(
                $"generation payload is missing {name}; a field-less payload is never completed from " +
                "the live configuration");
        }
    }

    /// <summary>
    /// Strict base64: standard alphabet, canonical padding, no whitespace, and
    /// the decoded length re-checked against the compressed cap.
    /// </summary>
    private static byte[] DecodeBase64(string body)
    {
        if (body.Length == 0)
        {
            throw new InvalidDataException("generation payload body is empty");
        }
        if (body.Length % 4 != 0)
        {
            throw new InvalidDataException("generation payload body is not padded base64");
        }
        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];
            bool alphabet = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')
                || (c >= '0' && c <= '9') || c == '+' || c == '/';
            bool padding = c == '=' && i >= body.Length - 2;
            if (!alphabet && !padding)
            {
                throw new InvalidDataException(
                    $"generation payload body has a character outside the base64 alphabet at index {i}");
            }
        }
        byte[] compressed;
        try
        {
            compressed = Convert.FromBase64String(body);
        }
        catch (FormatException e)
        {
            throw new InvalidDataException($"generation payload body is not base64: {e.Message}");
        }
        if (compressed.Length > MaxCompressedBytes)
        {
            throw new InvalidDataException(
                $"generation payload is {compressed.Length} compressed bytes, above the " +
                $"{MaxCompressedBytes} byte cap");
        }
        return compressed;
    }

    private static byte[] Compress(byte[] json)
    {
        using var output = new MemoryStream(json.Length / 3);
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(json, 0, json.Length);
        }
        return output.ToArray();
    }

    /// <summary>
    /// Bounded decompression: reads at most <see cref="MaxDecompressedBytes"/> + 1
    /// bytes, so a crafted body cannot expand without limit, and refuses a
    /// stream that would exceed the cap or that is not a gzip stream at all.
    /// </summary>
    private static byte[] DecompressBounded(byte[] compressed)
    {
        // A gzip member is at least a 10 byte header plus an 8 byte trailer.
        if (compressed.Length < 18)
        {
            throw new InvalidDataException(
                $"generation payload body is {compressed.Length} bytes; a gzip member is at least 18");
        }
        try
        {
            using var input = new MemoryStream(compressed, writable: false);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream(Math.Min(compressed.Length * 4, MaxDecompressedBytes));
            byte[] buffer = new byte[81920];
            int total = 0;
            int read;
            while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total > MaxDecompressedBytes)
                {
                    throw new InvalidDataException(
                        $"generation payload expands past the {MaxDecompressedBytes} byte cap; refused " +
                        "before the whole body was read.");
                }
                output.Write(buffer, 0, read);
            }
            if (total == 0)
            {
                throw new InvalidDataException("generation payload decompressed to nothing");
            }
            // GZipStream validates the member's CRC32 and length, but it stops at
            // the end of the member, so bytes appended AFTER a complete member
            // would otherwise be ignored. The trailer must therefore be the last
            // 8 bytes of the body and its ISIZE field must equal what was
            // decompressed - otherwise the body is truncated or carries trailing
            // data and is refused.
            int trailer = compressed.Length - 4;
            uint isize = (uint)(compressed[trailer]
                | (compressed[trailer + 1] << 8)
                | (compressed[trailer + 2] << 16)
                | (compressed[trailer + 3] << 24));
            if (isize != (uint)total)
            {
                throw new InvalidDataException(
                    $"generation payload gzip trailer reports {isize} bytes but {total} were decompressed; " +
                    "the body is truncated or carries trailing data.");
            }
            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            throw new InvalidDataException($"generation payload is not a readable gzip body: {e.Message}");
        }
    }

    /// <summary>
    /// Strictness gate: System.Text.Json accepts repeated JSON members and keeps
    /// the last one, so a payload that declares a field twice would silently
    /// discard one value. Rejected here, with the offending path.
    /// </summary>
    private static void RejectDuplicateMembers(JsonElement element, string path, int depth)
    {
        if (depth > 32)
        {
            throw new InvalidDataException("generation payload JSON is nested too deeply");
        }
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                    {
                        throw new InvalidDataException(
                            $"generation payload repeats the JSON member '{property.Name}' under {path}");
                    }
                    RejectDuplicateMembers(property.Value, path + "/" + property.Name, depth + 1);
                }
                break;
            case JsonValueKind.Array:
                int index = 0;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    RejectDuplicateMembers(item, path + "/" + index.ToString(Invariant), depth + 1);
                    index++;
                }
                break;
        }
    }

    private static CultureInfo Invariant => CultureInfo.InvariantCulture;

    // ---------- DTOs (payload layout; nulls are refused, never defaulted) ----------
    //
    // Public so the serializer and the isolated probe can see them; the FORMAT
    // contract is the class summary above, not this shape.

    public sealed class PayloadDto
    {
        public PayloadDto()
        {
        }

        public int? Schema { get; set; }
        public int? GeneratorSemantics { get; set; }
        public string? Identity { get; set; }
        public string? Seed { get; set; }
        public string? Fingerprint { get; set; }
        public string? CacheKey { get; set; }
        public int? Multiplier { get; set; }
        public int? BudgetCommon { get; set; }
        public int? BudgetUncommon { get; set; }
        public int? BudgetRare { get; set; }
        public int? NegativeChanceCommon { get; set; }
        public int? NegativeChanceUncommon { get; set; }
        public int? NegativeChanceRare { get; set; }
        public bool? EnableExtraPool { get; set; }
        public bool? WatcherModLoaded { get; set; }
        public string[]? ActivePositives { get; set; }
        public string[]? ActiveNegatives { get; set; }
        public TemplateDto[]? Templates { get; set; }
        public DefinitionDto[]? Definitions { get; set; }
    }

    public sealed class TemplateDto
    {
        public TemplateDto()
        {
        }

        public string? Template { get; set; }
        public bool? IsNegative { get; set; }
        public bool? Decaying { get; set; }
        public string? TextPattern { get; set; }
        public int? Min { get; set; }
        public int? Max { get; set; }
        public int? CatalogCostPerPoint { get; set; }
        public int? CatalogRefundPerPoint { get; set; }
        public int? CostPerPoint { get; set; }
        public int? RefundPerPoint { get; set; }
    }

    public sealed class DefinitionDto
    {
        public DefinitionDto()
        {
        }

        public int? Slot { get; set; }
        public int? Rarity { get; set; }
        public string? Name { get; set; }
        public OperationDto[]? Operations { get; set; }
    }

    public sealed class OperationDto
    {
        public OperationDto()
        {
        }

        public string? Template { get; set; }
        public int? Amount { get; set; }
        public string? Text { get; set; }
    }
}

/// <summary>
/// One restored run: its durable identity, the frozen generation snapshot and
/// all <see cref="ChaosRelicGenerator.TotalSlots"/> definitions, exactly as they
/// were saved. Read-only by construction - the three properties are the only
/// surface, the lists were copied during the restore and the definitions are
/// immutable records - so a consumer cannot edit the saved state through this
/// value.
/// </summary>
public sealed class QuriousSavedGeneration
{
    internal QuriousSavedGeneration(
        string identity, QuriousGenerationSnapshot snapshot, IReadOnlyList<ChaosRelicDefinition> definitions)
    {
        Identity = identity;
        Snapshot = snapshot;
        Definitions = definitions;
    }

    /// <summary>Durable identity of the run this payload belongs to.</summary>
    public string Identity { get; }

    /// <summary>Frozen generation inputs, restored from the payload.</summary>
    public QuriousGenerationSnapshot Snapshot { get; }

    /// <summary>The saved definitions, one per slot.</summary>
    public IReadOnlyList<ChaosRelicDefinition> Definitions { get; }
}