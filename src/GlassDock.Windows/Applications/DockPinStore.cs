using System.Text.Json;
using GlassDock.Core.Applications;

namespace GlassDock.Windows.Applications;

/// <summary>GlassDock-only preferences. Imported Windows taskbar pins are never modified.</summary>
internal sealed class DockPinStore
{
    private readonly object gate = new();
    private readonly string path = Settings.DokyUserData.PinsPath;
    private readonly ShellApplicationIdentityResolver identityResolver = new();
    private readonly InstalledPinReconciler installations;

    private sealed record SavedPin(ApplicationIdentity Identity, string Name, string Target);

    private sealed record Preferences
    {
        public Dictionary<string, PinInstallationEvidence> Installations { get; init; } = new(StringComparer.Ordinal);
        public List<SavedPin> Pins { get; init; } = [];
        public HashSet<string> Excluded { get; init; } = [];
        // User override for unavailable local shortcuts. Other missing shortcuts
        // stay saved but do not occupy dock space until the target returns.
        public HashSet<string> ShownMissingPins { get; init; } = [];
        public List<string>? Order { get; init; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<DockStack>? Stacks { get; init; }
    }

    private Preferences preferences = new();
    private IReadOnlyList<RecoverableDockPin> recoverablePins = [];

    public DockPinStore(string? preferencesPath = null, InstalledPinReconciler? installations = null)
    {
        this.installations = installations ?? new();
        if (preferencesPath is not null) path = preferencesPath;
        try
        {
            if (!File.Exists(path))
                return;

            if (JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path)) is not { } saved)
                return;

            preferences = new Preferences
            {
                Installations = (saved.Installations ?? []).Where(pair => pair.Value is not null)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                Stacks = ValidateStacks(saved.Stacks),
                Pins = (saved.Pins ?? [])
                    .Where(pin =>
                        pin is { Identity: not null } &&
                        !string.IsNullOrWhiteSpace(pin.Target) &&
                        !string.IsNullOrWhiteSpace(pin.Name))
                    .DistinctBy(pin => pin.Identity.Key, StringComparer.Ordinal)
                    .ToList(),

                Excluded = saved.Excluded is null
                    ? new HashSet<string>(StringComparer.Ordinal)
                    : new HashSet<string>(saved.Excluded, StringComparer.Ordinal),
                ShownMissingPins = saved.ShownMissingPins is null
                    ? new HashSet<string>(StringComparer.Ordinal)
                    : new HashSet<string>(saved.ShownMissingPins, StringComparer.Ordinal),

                Order = saved.Order?
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToList()
            };
        }
        catch (Exception error) when (
            error is IOException or
            UnauthorizedAccessException or
            JsonException)
        {
        }
    }

    public IReadOnlyList<PinnedApplication> Apply(
        IReadOnlyList<PinnedApplication> imported,
        WindowsApplicationIconService icons)
    {
        lock (gate)
        {
            var normalized = NormalizeShortcutPins(preferences);
            if (!ReferenceEquals(normalized, preferences))
            {
                // Canonicalize only Doky's own saved shortcut identity. The exact
                // launch target remains unchanged, so custom arguments/profile
                // behavior is preserved. Persist best-effort so pin/unpin/order
                // operations use the canonical identity on later launches too.
                if (!Save(normalized))
                    preferences = normalized;
            }

            var result = imported
                .Where(pin => !preferences.Excluded.Contains(pin.Identity.Key))
                .ToList();

            foreach (var pin in preferences.Pins)
            {
                if (!result.Any(item => item.Identity.Key == pin.Identity.Key))
                {
                    result.Add(new(
                        pin.Identity,
                        pin.Name,
                        pin.Target,
                        icons.FromShell(pin.Identity.Key, pin.Target)));
                }
            }

            for (var index = 0; index < result.Count; index++)
            {
                var pin = result[index];
                var availability = PinTargetProbe.Check(pin.LaunchTarget, pin.Identity);
                if (availability == PinTargetAvailability.Unavailable) icons.Invalidate(pin.Identity.Key);
                result[index] = pin with { Availability = availability,
                    Icon = availability == PinTargetAvailability.Unavailable ? ApplicationIconFallback.Visible(null) : pin.Icon };
            }

            var reconciliation = installations.Inspect(result, preferences.Installations);
            var removed = reconciliation.Removed;
            if (removed.Count > 0 || !preferences.Installations.OrderBy(p => p.Key).SequenceEqual(reconciliation.Evidence.OrderBy(p => p.Key)))
            {
                var next = WithoutPins(preferences, removed, reconciliation.Evidence);
                // Publish removal only after the atomic preferences write succeeds.
                // Imported Windows shortcuts are excluded, never edited or deleted.
                if (Save(next))
                {
                    result.RemoveAll(pin => removed.Contains(pin.Identity.Key));
                    foreach (var id in removed) icons.Invalidate(id);
                }
            }

            // Only Doky's own local .lnk pins are eligible for visual hiding.
            // Preserve the saved identity, ordering and stacks; do not infer
            // permanent uninstall from an unavailable shortcut. Imported pins,
            // package identities, directories, .url files and direct executable
            // pins continue to use their original behavior.
            var customIds = preferences.Pins.Select(pin => pin.Identity.Key)
                .ToHashSet(StringComparer.Ordinal);
            recoverablePins = result
                .Where(pin => customIds.Contains(pin.Identity.Key) &&
                    pin.LaunchTarget.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) &&
                    pin.Availability == PinTargetAvailability.Unavailable)
                .Select(pin => new RecoverableDockPin(pin.Identity.Key, pin.Name, pin.LaunchTarget,
                    !preferences.ShownMissingPins.Contains(pin.Identity.Key)))
                .ToArray();
            var hiddenIds = recoverablePins.Where(pin => pin.Hidden)
                .Select(pin => pin.Id).ToHashSet(StringComparer.Ordinal);
            result.RemoveAll(pin => hiddenIds.Contains(pin.Identity.Key));

            if (preferences.Order is not { Count: > 0 } order)
                return result;

            var orderIndex = order
                .Select((id, index) => (id, index))
                .ToDictionary(pair => pair.id, pair => pair.index, StringComparer.Ordinal);

            return result
                .Select((pin, originalIndex) => new
                {
                    Pin = pin,
                    OriginalIndex = originalIndex,
                    SavedIndex = orderIndex.GetValueOrDefault(
                        pin.Identity.Key,
                        int.MaxValue)
                })
                .OrderBy(item => item.SavedIndex)
                .ThenBy(item => item.OriginalIndex)
                .Select(item => item.Pin)
                .ToArray();
        }
    }

    /// <summary>Entries hidden from the dock are always available here for recovery.</summary>
    internal IReadOnlyList<RecoverableDockPin> GetRecoverablePins()
    {
        lock (gate) return recoverablePins.ToArray();
    }

    internal bool SetMissingPinVisible(string id, bool visible)
    {
        lock (gate)
        {
            if (!recoverablePins.Any(pin => pin.Id == id) ||
                !preferences.Pins.Any(pin => pin.Identity.Key == id)) return false;
            var shown = new HashSet<string>(preferences.ShownMissingPins, StringComparer.Ordinal);
            if (visible) shown.Add(id); else shown.Remove(id);
            if (!Save(preferences with { ShownMissingPins = shown })) return false;
            recoverablePins = recoverablePins.Select(pin => pin.Id == id ? pin with { Hidden = !visible } : pin).ToArray();
            return true;
        }
    }

    internal bool RemoveRecoverablePin(string id)
    {
        lock (gate)
        {
            if (!recoverablePins.Any(pin => pin.Id == id) ||
                !preferences.Pins.Any(pin => pin.Identity.Key == id)) return false;
            var removed = new HashSet<string>(StringComparer.Ordinal) { id };
            if (!Save(WithoutPins(preferences, removed, preferences.Installations
                    .Where(pair => !removed.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)))) return false;
            recoverablePins = recoverablePins.Where(pin => pin.Id != id).ToArray();
            return true;
        }
    }

    private static Preferences WithoutPins(Preferences source, IReadOnlySet<string> removed,
        Dictionary<string, PinInstallationEvidence> evidence)
    {
        if (removed.Count == 0) return source with { Installations = evidence };
        var order = source.Order?.Where(id => !removed.Contains(id)).ToList();
        var stacks = new List<DockStack>();
        foreach (var stack in source.Stacks ?? [])
        {
            var members = stack.ApplicationIds.Where(id => !removed.Contains(id)).ToArray();
            if (members.Length >= 2) { stacks.Add(stack with { ApplicationIds = members }); continue; }
            if (order is not null && order.IndexOf(stack.Id) is var slot && slot >= 0)
            {
                order.RemoveAt(slot);
                if (members.Length == 1)
                {
                    order.Remove(members[0]);
                    order.Insert(Math.Min(slot, order.Count), members[0]);
                }
            }
        }
        return source with
        {
            Installations = evidence,
            Pins = source.Pins.Where(pin => !removed.Contains(pin.Identity.Key)).ToList(),
            Excluded = source.Excluded.Concat(removed).ToHashSet(StringComparer.Ordinal),
            ShownMissingPins = source.ShownMissingPins.Where(id => !removed.Contains(id)).ToHashSet(StringComparer.Ordinal),
            Order = order,
            Stacks = stacks.Count == 0 ? null : stacks
        };
    }

    private Preferences NormalizeShortcutPins(Preferences source)
    {
        Dictionary<string, string>? remap = null;
        var pins = new List<SavedPin>(source.Pins.Count);

        foreach (var pin in source.Pins)
        {
            var next = pin;
            if (string.IsNullOrWhiteSpace(pin.Identity.AppUserModelId) &&
                pin.Target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) &&
                identityResolver.ResolveShortcut(pin.Target) is { } canonical &&
                !string.Equals(canonical.Key, pin.Identity.Key, StringComparison.Ordinal))
            {
                next = pin with { Identity = canonical };
                (remap ??= new(StringComparer.Ordinal))[pin.Identity.Key] = canonical.Key;
            }

            // If two saved shortcuts resolve to the same application identity,
            // keep the first launch target/order entry instead of creating a
            // duplicate dock icon.
            if (!pins.Any(existing =>
                    string.Equals(existing.Identity.Key, next.Identity.Key, StringComparison.Ordinal)))
                pins.Add(next);
        }

        if (remap is null)
            return source;

        string Map(string id) => remap.TryGetValue(id, out var mapped) ? mapped : id;

        return new Preferences
        {
            Installations = source.Installations.GroupBy(pair => Map(pair.Key), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal),
            Stacks = source.Stacks?.Select(stack => stack with { ApplicationIds = stack.ApplicationIds.Select(Map).Distinct(StringComparer.Ordinal).ToArray() }).ToArray(),
            Pins = pins,
            Excluded = source.Excluded
                .Select(Map)
                .ToHashSet(StringComparer.Ordinal),
            ShownMissingPins = source.ShownMissingPins
                .Select(Map)
                .ToHashSet(StringComparer.Ordinal),
            Order = source.Order?
                .Select(Map)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList()
        };
    }

    public bool Set(DockApplication application, bool pinned)
    {
        lock (gate)
        {
            var target = WindowsApplicationLauncher.Target(application);
            if (pinned && target is null)
                return false;

            var pins = preferences.Pins
                .Where(pin => pin.Identity.Key != application.Id)
                .ToList();

            var excluded = new HashSet<string>(
                preferences.Excluded,
                StringComparer.Ordinal);

            List<string>? order = preferences.Order is null
                ? null
                : preferences.Order
                    .Where(id => id != application.Id)
                    .ToList();

            if (pinned)
            {
                excluded.Remove(application.Id);
                pins.Add(new(
                    application.Identity,
                    application.Name,
                    target!));

                // Once a custom order exists, newly pinned apps belong at its end.
                order?.Add(application.Id);
            }
            else
            {
                excluded.Add(application.Id);
            }

            return Save(new Preferences
            {
                Installations = preferences.Installations.Where(pair => pair.Key != application.Id)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                Stacks = preferences.Stacks,
                Pins = pins,
                Excluded = excluded,
                ShownMissingPins = preferences.ShownMissingPins.Where(id => id != application.Id)
                    .ToHashSet(StringComparer.Ordinal),
                Order = order
            });
        }
    }

    public bool AddExternalTarget(string path)
    {
        lock (gate)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            path = Path.GetFullPath(path);

            if (!File.Exists(path) && !Directory.Exists(path))
                return false;

            // Keep the shell target exact (including .lnk files and folders).
            // The identity is GlassDock-local; LaunchTarget remains the authoritative
            // ShellExecute target.
            var identity = new ApplicationIdentity(null, path);
            var id = identity.Key;

            var name = Directory.Exists(path)
                ? (Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } folderName
                    ? folderName
                    : path)
                : Path.GetFileNameWithoutExtension(path);

            if (string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var description = System.Diagnostics.FileVersionInfo
                        .GetVersionInfo(path)
                        .FileDescription;

                    if (!string.IsNullOrWhiteSpace(description))
                        name = description;
                }
                catch
                {
                    // Filename remains a safe display name.
                }
            }

            if (string.IsNullOrWhiteSpace(name))
                name = path;

            var pins = preferences.Pins
                .Where(pin => pin.Identity.Key != id)
                .ToList();

            pins.Add(new(identity, name, path));

            var excluded = new HashSet<string>(
                preferences.Excluded,
                StringComparer.Ordinal);

            excluded.Remove(id);

            var order = preferences.Order?.Where(existing => existing != id).ToList();

            // If a custom order already exists, append the new item. If there is
            // no custom order yet, Apply() naturally appends GlassDock-only pins.
            order?.Add(id);

            return Save(new Preferences
            {
                Installations = preferences.Installations.Where(pair => pair.Key != id)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                Stacks = preferences.Stacks,
                Pins = pins,
                Excluded = excluded,
                ShownMissingPins = preferences.ShownMissingPins.Where(existing => existing != id)
                    .ToHashSet(StringComparer.Ordinal),
                Order = order
            });
        }
    }

    public IReadOnlyList<DockApplication> ApplyOrder(IReadOnlyList<DockApplication> applications)
    {
        lock (gate)
        {
            if (preferences.Order is not { Count: > 0 } order) return applications;
            var indices = order.Select((id, index) => (id, index))
                .ToDictionary(pair => pair.id, pair => pair.index, StringComparer.Ordinal);
            return applications.OrderBy(app => indices.GetValueOrDefault(app.Id, int.MaxValue)).ToArray();
        }
    }

    public bool Reorder(
        IReadOnlyList<string> currentIds,
        IReadOnlyList<string> orderedIds)
    {
        lock (gate)
        {
            var requested = orderedIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (requested.Length != currentIds.Count)
                return false;

            var currentSet = currentIds.ToHashSet(StringComparer.Ordinal);
            if (requested.Any(id => !currentSet.Contains(id)))
                return false;

            return Save(new Preferences
            {
                Installations = preferences.Installations,
                Stacks = preferences.Stacks,
                Pins = preferences.Pins.ToList(),
                Excluded = new HashSet<string>(
                    preferences.Excluded,
                    StringComparer.Ordinal),
                ShownMissingPins = new HashSet<string>(preferences.ShownMissingPins, StringComparer.Ordinal),
                // Keep identities of temporarily absent apps without pinning them.
                Order = requested.Concat(preferences.Order ?? [])
                    .Distinct(StringComparer.Ordinal).ToList()
            });
        }
    }

    private static IReadOnlyList<DockStack>? ValidateStacks(IReadOnlyList<DockStack>? stacks)
    {
        if (stacks is null) return null;
        var members = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var valid = new List<DockStack>();
        foreach (var stack in stacks)
        {
            if (stack is null || string.IsNullOrWhiteSpace(stack.Id) || !stack.Id.StartsWith("stack:", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(stack.Name) || stack.ApplicationIds is null || !ids.Add(stack.Id)) continue;
            var apps = stack.ApplicationIds.Where(id => !string.IsNullOrWhiteSpace(id) && !id.StartsWith("stack:", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal).Take(DockStack.MaximumApps).Where(members.Add).ToArray();
            if (apps.Length > 0) valid.Add(stack with { ApplicationIds = apps });
        }
        return valid.Count == 0 ? null : valid;
    }
    public IReadOnlyList<DockApplication> ApplyStacks(IReadOnlyList<DockApplication> applications)
    {
        lock (gate)
        {
            if (preferences.Stacks is not { Count: > 0 }) return ApplyOrder(applications);
            var byId = applications.ToDictionary(app => app.Id, StringComparer.Ordinal);
            var used = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<DockApplication>();
            foreach (var stack in preferences.Stacks)
            {
                var members = stack.ApplicationIds.Where(id => byId.ContainsKey(id) && used.Add(id))
                    .Select(id => byId[id]).ToArray();
                if (members.Length == 0) continue;
                if (members.Length == 1) { result.Add(members[0]); continue; }
                result.Add(new(stack.Id, new(null, null, ShellPath: stack.Id), stack.Name, null, true,
                    members.SelectMany(app => app.Windows).ToArray(), null) { Stack = stack, StackApps = members });
            }
            result.AddRange(applications.Where(app => !used.Contains(app.Id)));
            return ApplyOrder(result);
        }
    }

    public bool MergeStack(string sourceId, string targetId, IReadOnlyList<DockApplication> applications)
    {
        lock (gate)
        {
            var layout = ApplyStacks(applications);
            var source = layout.FirstOrDefault(app => app.Id == sourceId);
            var target = layout.FirstOrDefault(app => app.Id == targetId);
            if (source is null || target is null || source == target || !source.IsPinned || !target.IsPinned || source.Stack is not null)
                return false;
            var members = target.Stack?.ApplicationIds.ToList() ?? [target.Id];
            if (members.Contains(source.Id) || members.Count >= DockStack.MaximumApps) return false;
            members.Add(source.Id);
            var stack = new DockStack(target.Stack?.Id ?? "stack:" + Guid.NewGuid().ToString("N"), target.Stack?.Name ?? "Stack", members);
            var stacks = (preferences.Stacks ?? []).Where(item => item.Id != stack.Id).Append(stack).ToArray();
            // Snapshot exact imported/custom launch targets before grouping; never touch Windows pins.
            var pins = preferences.Pins.ToList();
            foreach (var app in applications.Where(app => members.Contains(app.Id)))
            {
                if (pins.Any(pin => pin.Identity.Key == app.Id)) continue;
                var launch = WindowsApplicationLauncher.Target(app);
                if (launch is null) return false;
                pins.Add(new(app.Identity, app.Name, launch));
            }
            var order = layout.Where(app => app.Id != source.Id).Select(app => app.Id == target.Id ? stack.Id : app.Id)
                .Concat((preferences.Order ?? []).Where(id => id != source.Id && id != target.Id && !members.Contains(id)))
                .Distinct(StringComparer.Ordinal).ToList();
            return Save(preferences with { Pins = pins, Stacks = stacks, Order = order });
        }
    }

    public bool RenameStack(string id, string name)
    {
        lock (gate)
        {
            name = name.Trim();
            if (name.Length is < 1 or > 40 || preferences.Stacks?.Any(stack => stack.Id == id) != true) return false;
            return Save(preferences with { Stacks = preferences.Stacks.Select(stack => stack.Id == id ? stack with { Name = name } : stack).ToArray() });
        }
    }

    public bool ReorderStack(string id, IReadOnlyList<string> order)
    {
        lock (gate)
        {
            var stack = preferences.Stacks?.FirstOrDefault(item => item.Id == id);
            if (stack is null || order.Count != stack.ApplicationIds.Count || order.Distinct().Count() != order.Count ||
                order.Any(item => !stack.ApplicationIds.Contains(item))) return false;
            return Save(preferences with { Stacks = preferences.Stacks!.Select(item => item.Id == id ? item with { ApplicationIds = order.ToArray() } : item).ToArray() });
        }
    }

    public bool ExtractStack(string id, string? appId = null, string? beforeId = null)
    {
        lock (gate)
        {
            var stack = preferences.Stacks?.FirstOrDefault(item => item.Id == id);
            if (stack is null || (appId is not null && !stack.ApplicationIds.Contains(appId))) return false;
            var remaining = appId is null ? [] : stack.ApplicationIds.Where(item => item != appId).ToArray();
            var removed = appId is null ? stack.ApplicationIds.ToArray() : [appId];
            var stacks = preferences.Stacks!.Where(item => item.Id != id).ToList();
            if (remaining.Length > 1) stacks.Add(stack with { ApplicationIds = remaining });
            var order = (preferences.Order ?? []).Where(item => !removed.Contains(item) && !remaining.Contains(item)).ToList();
            var slot = order.IndexOf(id);
            if (slot < 0) slot = order.Count;
            if (remaining.Length <= 1)
            {
                order.Remove(id);
                order.InsertRange(Math.Min(slot, order.Count), remaining);
            }
            var insertion = beforeId is null ? -1 : order.IndexOf(beforeId);
            if (insertion < 0) insertion = Math.Min(slot + (remaining.Length > 0 ? 1 : 0), order.Count);
            order.InsertRange(insertion, removed);
            return Save(preferences with { Stacks = stacks.Count == 0 ? null : stacks, Order = order });
        }
    }
    private bool Save(Preferences next)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var temporary = path + ".tmp";
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(next));

            File.Move(
                temporary,
                path,
                true);

            preferences = next;
            return true;
        }
        catch (Exception error) when (
            error is IOException or
            UnauthorizedAccessException)
        {
            return false;
        }
    }
}
