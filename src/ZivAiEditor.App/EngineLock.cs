using System;
using System.IO;

namespace ZivAiEditor.App;

/// <summary>
/// Cross-process engine mutex for the bridge (bridge §5): guarantees at most one AI task — editor
/// manual task or quick task — is running at a time, shared with ZIV through the same file. The
/// lock semantics are <b>"can the file be opened exclusively"</b>, independent of the file's
/// existence, so a crashed process self-heals (the OS closes the handle). Lives in the App layer
/// only (Z4) and takes an injectable path so it is unit-testable.
/// </summary>
internal sealed class EngineLock : IDisposable
{
    private FileStream? _stream;

    public EngineLock(string path) => Path = path;

    /// <summary>The lock file path (<c>&lt;program dir&gt;/engine.lock</c> in production).</summary>
    public string Path { get; }

    /// <summary>True while this instance holds the exclusive handle.</summary>
    public bool IsHeld => _stream is not null;

    /// <summary>
    /// Tries to take the exclusive lock. Returns <c>true</c> when acquired; <c>false</c> when
    /// another process holds it. Re-acquiring while already held is a no-op success.
    /// </summary>
    public bool TryAcquire()
    {
        if (_stream is not null)
        {
            return true;
        }

        try
        {
            _stream = new FileStream(Path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Probes whether the engine is busy <b>without creating the file and without taking the
    /// lock</b> (bridge §5). <see cref="FileNotFoundException"/> = idle; <see cref="IOException"/>
    /// = busy. Never throws.
    /// </summary>
    public static bool IsBusy(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>Closes the handle so the OS releases the lock. Idempotent; does not delete the file.</summary>
    public void Release()
    {
        _stream?.Dispose();
        _stream = null;
    }

    /// <summary>
    /// Releases the handle and best-effort deletes the file (bridge release order). The delete is
    /// skipped when this instance never held the lock, so it cannot remove a peer's lock file.
    /// Never throws.
    /// </summary>
    public void Dispose()
    {
        var held = _stream is not null;
        Release();
        if (!held)
        {
            return;
        }

        try
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
        catch (Exception)
        {
            // A leftover file is harmless: the lock is "can it be opened exclusively", not existence.
        }
    }
}
