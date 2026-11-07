using Windows.Devices.Radios;

namespace GlassDock.Windows.Desktop;

internal static class RadioStateChange
{
    internal static async Task SetAsync(Radio[] radios, RadioState desired, CancellationToken cancel)
    {
        if (radios.Length == 0) throw new InvalidOperationException("No controllable radio adapter");
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(Radio sender, object args)
        {
            try { if (radios.All(radio => radio.State == desired)) settled.TrySetResult(); }
            catch (Exception error) when (error is not OutOfMemoryException) { settled.TrySetException(error); }
        }
        var observed = new List<Radio>();
        try
        {
            foreach (var radio in radios) { radio.StateChanged += Changed; observed.Add(radio); }
            foreach (var radio in radios)
            {
                cancel.ThrowIfCancellationRequested();
                if (await radio.SetStateAsync(desired).AsTask(cancel).ConfigureAwait(false) != RadioAccessStatus.Allowed)
                    throw new InvalidOperationException("Windows or the adapter blocked this radio change.");
            }
            Changed(radios[0], EventArgs.Empty);
            await settled.Task.WaitAsync(cancel).ConfigureAwait(false);
        }
        finally
        {
            foreach (var radio in observed)
            {
                try { radio.StateChanged -= Changed; }
                catch (Exception error) when (error is not OutOfMemoryException)
                { System.Diagnostics.Debug.WriteLine($"Radio observer cleanup: {error.Message}"); }
            }
        }
    }
}
