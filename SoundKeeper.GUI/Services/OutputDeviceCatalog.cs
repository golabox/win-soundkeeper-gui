using SoundKeeper.GUI.Models;

namespace SoundKeeper.GUI.Services;

public readonly record struct OutputDeviceRow(string Id, string Name, bool IsAvailable, bool IsSelected);

// Merges the outputs Windows reports now with the saved selection. A selected output that is currently absent stays
// listed as unavailable with its last known name: unplugging a device never loses the choice, and the engine targets
// it again as soon as Windows reports it.
public static class OutputDeviceCatalog
{
    public static IReadOnlyList<OutputDeviceRow> BuildRows(IReadOnlyList<AudioOutput> activeOutputs, IReadOnlyList<OutputDeviceSelection> selection)
    {
        var rows = activeOutputs
            .OrderBy(output => output.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(output => new OutputDeviceRow(output.Id, output.Name, true, selection.Any(device => SameId(device.Id, output.Id))))
            .ToList();
        rows.AddRange(selection
            .Where(device => !activeOutputs.Any(output => SameId(output.Id, device.Id)))
            .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(device => new OutputDeviceRow(device.Id, device.Name, false, true)));
        return rows;
    }

    // Keeps the last friendly name Windows gave to each selected output; returns true when one changed.
    public static bool UpdateNames(IReadOnlyList<OutputDeviceSelection> selection, IReadOnlyList<AudioOutput> activeOutputs)
    {
        var changed = false;
        foreach (var device in selection)
        {
            var output = activeOutputs.FirstOrDefault(candidate => SameId(candidate.Id, device.Id));
            if (output.Id is null || output.Name.Length == 0 || output.Name == device.Name) continue;
            device.Name = output.Name;
            changed = true;
        }
        return changed;
    }

    public static bool SameId(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
