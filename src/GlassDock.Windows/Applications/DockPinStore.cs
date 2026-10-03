using System.Text.Json;
using GlassDock.Core.Applications;

namespace GlassDock.Windows.Applications;

/// <summary>GlassDock-only preferences. Imported Windows taskbar pins are never modified.</summary>
internal sealed class DockPinStore
{
    private readonly object gate = new();
    private readonly string path = Settings.DokyUserData.PinsPath;
    private readonly ShellApplicationIdentityResolver identityResolver = new();

    private sealed record SavedPin(ApplicationIdentity Identity, string Name, string Target);

    private sealed record Preferences
    {
        public List<SavedPin> Pins { get; init; } = [];
        public HashSet<string> Excluded { get; init; } = [];
        public List<string>? Order { get; init; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<DockStack>? Stacks { get; init; }
    }

    private Preferences preferences = new();

    public DockPinStore(string? preferencesPath = null)
    {
        if (preferencesPath is not null) path = preferencesPath;
        try
        {
            if (!File.Exists(path))
                return;

            if (JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path)) is not { } saved)
                return;

            preferences = new Preferences
            {
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
            Stacks = source.Stacks?.Select(stack => stack with { ApplicationIds = stack.ApplicationIds.Select(Map).Distinct(StringComparer.Ordinal).ToArray() }).ToArray(),
            Pins = pins,
            Excluded = source.Excluded
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
                Stacks = preferences.Stacks,
                Pins = pins,
                Excluded = excluded,
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
                Stacks = preferences.Stacks,
                Pins = pins,
                Excluded = excluded,
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
                Stacks = preferences.Stacks,
                Pins = preferences.Pins.ToList(),
                Excluded = new HashSet<string>(
                    preferences.Excluded,
                    StringComparer.Ordinal),
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
