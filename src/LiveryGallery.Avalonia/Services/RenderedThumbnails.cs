using Avalonia.Threading;
using ForzaToolkit.Liveries.Scanning;
using LiveryGallery.ViewModels;
using LiveryGallery.Viewer3D;

namespace LiveryGallery.Services;

/// <summary>
/// The previews the gallery draws from the 3D model for cards whose save has none (<see cref="ILiveryThumbnails"/>).
/// A drawn preview is written into the shared preview folder under <see cref="LiveryDrawnPreviews.FileName"/>, and the
/// next scan makes it the livery's preview: from then on it goes into the archive and the backups with the livery, and
/// it is deleted with the livery or once the game writes a preview of its own. Nothing is written to the save.
/// One preview is drawn at a time, in the order the cards asked; a preview whose cards all left the screen before it
/// started is not drawn. A livery that could not be drawn is not tried again until the game folder changes.
/// Used from the UI thread.
/// </summary>
internal sealed class RenderedThumbnails
{
    private sealed class Job(string fileName, LiveryThumbnailRequest request)
    {
        public string FileName { get; } = fileName;
        public LiveryThumbnailRequest Request { get; } = request;
        public TaskCompletionSource<string?> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Waiters;
    }

    private readonly ILiveryThumbnails _thumbnails;
    private readonly Func<string, string?> _liveryFileOf;
    private readonly string _previewDirectory;
    private readonly ClosableAsyncLock _saveFolderLock;
    private readonly Dictionary<string, Job> _jobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<Job> _queue = new();
    private readonly HashSet<string> _failed = new(StringComparer.OrdinalIgnoreCase);
    private int _generation;
    private bool _running;
    private bool _shuttingDown;

    /// <param name="thumbnails">Draws the previews</param>
    /// <param name="liveryFileOf">A livery folder's name → its livery file in the current save; null when there is no save</param>
    /// <param name="previewDirectory">The shared preview folder the scanner reads (<c>LiveryCacheStore.ThumbsDir</c>)</param>
    /// <param name="saveFolderLock">Held while a drawn preview is written, so a scan does not take it for an orphan</param>
    public RenderedThumbnails(ILiveryThumbnails thumbnails, Func<string, string?> liveryFileOf, string previewDirectory, ClosableAsyncLock saveFolderLock)
    {
        _thumbnails = thumbnails;
        _liveryFileOf = liveryFileOf;
        _previewDirectory = previewDirectory;
        _saveFolderLock = saveFolderLock;
    }

    /// <summary>A preview was drawn and written: a scan makes it the livery's preview</summary>
    public event Action? Drawn;

    /// <summary>The card needs a preview of the gallery's own and one can be drawn now</summary>
    public bool CanRender(LiveryEntry entry) =>
        !_shuttingDown && entry.CanRenderThumbnail && _thumbnails.IsAvailable
        && FileNameOf(entry) is { } name && !_failed.Contains(name);

    /// <summary>
    /// The drawn preview of <paramref name="entry"/>, drawn when there is none yet; null when it cannot be drawn or
    /// <paramref name="ct"/> (the card left the screen) was cancelled
    /// </summary>
    public async Task<string?> GetPathAsync(LiveryEntry entry, CancellationToken ct)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!CanRender(entry) || FileNameOf(entry) is not { } name) return null;

        string path = Path.Combine(_previewDirectory, name);
        if (File.Exists(path)) return path; // drawn before; the next scan attaches it

        bool queued = false;
        if (!_jobs.TryGetValue(name, out var job))
        {
            if (_liveryFileOf(entry.FolderName) is not { } liveryFile) return null;
            job = new Job(name, new LiveryThumbnailRequest(entry.CarId, liveryFile));
            _jobs[name] = job;
            _queue.Enqueue(job);
            queued = true;
        }

        // Counted before the queue runs: a job nobody waits for is skipped
        job.Waiters++;
        if (queued) _ = RunAsync();
        try
        {
            return await job.Done.Task.WaitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            job.Waiters--;
        }
    }

    /// <summary>The game folder changed: liveries that failed may draw now</summary>
    public void Reset()
    {
        Dispatcher.UIThread.VerifyAccess();
        _generation++;
        _failed.Clear();
    }

    /// <summary>The app is closing: nothing more is drawn</summary>
    public void BeginShutdown()
    {
        _shuttingDown = true;
        while (_queue.TryDequeue(out var job)) Finish(job, null);
    }

    private async Task RunAsync()
    {
        if (_running) return;
        _running = true;
        int written = 0;
        try
        {
            while (!_shuttingDown && _queue.TryDequeue(out var job))
            {
                if (job.Waiters == 0)
                {
                    Finish(job, null);
                    continue;
                }

                int generation = _generation;
                string? path = null;
                try
                {
                    if (await _thumbnails.DrawAsync(job.Request, CancellationToken.None) is { } png)
                        path = await WriteAsync(job.FileName, png);
                }
                catch (Exception ex)
                {
                    AppLog.LogErrorThrottled("RenderedThumbnails", $"Failed to draw a preview of '{job.Request.LiveryFilePath}' from 3D", ex);
                }

                if (path is not null) written++;
                // Not tried again while the game folder stays the same; a failure because it went away is not remembered
                else if (generation == _generation && !_shuttingDown && _thumbnails.IsAvailable) _failed.Add(job.FileName);
                Finish(job, path);
            }
        }
        finally
        {
            _running = false;
            if (written > 0 && !_shuttingDown) Drawn?.Invoke();
        }
    }

    private async Task<string?> WriteAsync(string fileName, byte[] png)
    {
        string path = Path.Combine(_previewDirectory, fileName);
        try
        {
            // Under the save folder lock: a scan running now would delete a preview no livery refers to yet
            using (await _saveFolderLock.AcquireAsync())
            {
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(_previewDirectory);
                    AtomicFile.WriteAllBytes(path, png);
                });
            }
            return path;
        }
        catch (OperationCanceledException) when (_saveFolderLock.IsClosing)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.LogErrorThrottled(path, $"Failed to write the drawn preview '{path}'", ex);
            return null;
        }
    }

    private void Finish(Job job, string? path)
    {
        if (_jobs.TryGetValue(job.FileName, out var current) && ReferenceEquals(current, job)) _jobs.Remove(job.FileName);
        job.Done.TrySetResult(path);
    }

    private static string? FileNameOf(LiveryEntry entry) => LiveryDrawnPreviews.FileName(entry.CarId, entry.CLiveryHash);
}
