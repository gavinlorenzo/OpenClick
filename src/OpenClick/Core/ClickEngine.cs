using OpenClick.Native;

namespace OpenClick.Core;

/// <summary>
/// Background click loop engine. Performs repeated clicks on a dedicated thread.
/// </summary>
public sealed class ClickEngine : IDisposable
{
    private readonly object syncLock = new();
    private Thread? engineThread;
    private volatile bool stopFlag;
    private long clicksPerformed;

    public bool IsRunning
    {
        get
        {
            lock (syncLock)
            {
                return engineThread?.IsAlive ?? false;
            }
        }
    }

    public long ClicksPerformed
    {
        get { return Interlocked.Read(ref clicksPerformed); }
    }

    public event Action? Started;
    public event Action? Stopped;
    public event Action<long>? ClickTick;

    /// <summary>
    /// Start clicking. No-op if already running. Clones settings and resets the click counter.
    /// </summary>
    public void Start(ClickSettings settings, IClickTarget target)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        if (target == null) throw new ArgumentNullException(nameof(target));

        lock (syncLock)
        {
            // No-op if already running
            if (engineThread?.IsAlive ?? false)
            {
                return;
            }

            // Clone settings and reset counter
            ClickSettings clonedSettings = settings.Clone();
            Interlocked.Exchange(ref clicksPerformed, 0);
            stopFlag = false;

            // Launch engine thread
            engineThread = new Thread(() => RunClickLoop(clonedSettings, target))
            {
                IsBackground = true,
                Name = "OpenClick.ClickEngine"
            };
            engineThread.Start();
        }
    }

    /// <summary>
    /// Stop clicking. Blocks until thread exits (with 2 second timeout). Safe to call when not running.
    /// </summary>
    public void Stop()
    {
        Thread? threadToJoin;
        lock (syncLock)
        {
            stopFlag = true;
            threadToJoin = engineThread;
        }

        // Skip join if called from the engine thread itself
        if (threadToJoin != null && threadToJoin != Thread.CurrentThread)
        {
            threadToJoin.Join(2000);
        }
    }

    public void Dispose()
    {
        Stop();
    }

    /// <summary>
    /// The main click loop, running on the engine thread.
    /// </summary>
    private void RunClickLoop(ClickSettings settings, IClickTarget target)
    {
        try
        {
            // Begin high-resolution timing
            NativeMethods.timeBeginPeriod(1);

            try
            {
                // Raise Started event
                Started?.Invoke();

                // Create a Random instance for this run
                Random rng = new();

                while (true)
                {
                    // Perform a click
                    bool targetAlive = target.PerformClick(settings, rng);

                    // Increment counter and raise event
                    long newCount = Interlocked.Increment(ref clicksPerformed);
                    ClickTick?.Invoke(newCount);

                    // Stop if target is dead or we've reached the repeat count
                    if (!targetAlive)
                    {
                        break;
                    }

                    if (settings.RepeatMode == RepeatMode.Count && newCount >= settings.RepeatCount)
                    {
                        break;
                    }

                    // Wait for the effective interval, checking stop flag in 15ms chunks
                    int waitMs = settings.GetEffectiveIntervalMs(rng);
                    const int chunkMs = 15;
                    int elapsed = 0;

                    while (elapsed < waitMs)
                    {
                        if (stopFlag)
                        {
                            break;
                        }

                        int toWait = Math.Min(chunkMs, waitMs - elapsed);
                        Thread.Sleep(toWait);
                        elapsed += toWait;
                    }

                    // Exit if stop was requested
                    if (stopFlag)
                    {
                        break;
                    }
                }
            }
            finally
            {
                // End high-resolution timing
                NativeMethods.timeEndPeriod(1);

                // Raise Stopped event
                Stopped?.Invoke();
            }
        }
        catch
        {
            // Swallow exceptions in the engine thread to avoid crashing
        }
    }
}
