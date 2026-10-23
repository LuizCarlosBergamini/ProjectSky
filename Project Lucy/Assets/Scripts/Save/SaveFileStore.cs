using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// The save files on disk, one per slot (slotN.sav) plus the previous version of each (slotN.sav.bak). Writes are
/// atomic: the new text goes to a temporary file that then replaces the old one, so a crash or power loss mid-write
/// never leaves a half-written save. Knows nothing about what the text means.
/// </summary>
public class SaveFileStore
{
    private const string Extension = ".sav";
    private const string BackupSuffix = ".bak";
    private const string TempSuffix = ".tmp";

    public string Directory { get; }

    public SaveFileStore(string directory)
    {
        Directory = directory;
    }

    public string GetPath(int slot) => Path.Combine(Directory, $"slot{slot}{Extension}");

    public bool Exists(int slot)
    {
        string path = GetPath(slot);
        return File.Exists(path) || File.Exists(path + BackupSuffix);
    }

    public void Write(int slot, string text)
    {
        System.IO.Directory.CreateDirectory(Directory);

        string path = GetPath(slot);
        string temp = path + TempSuffix;
        string backup = path + BackupSuffix;

        File.WriteAllText(temp, text);

        if (!File.Exists(path))
        {
            File.Move(temp, path);
            return;
        }

        try
        {
            File.Replace(temp, path, backup);
        }
        catch (PlatformNotSupportedException)
        {
            // Not atomic, but keeps a backup on platforms without File.Replace.
            File.Copy(path, backup, true);
            File.Delete(path);
            File.Move(temp, path);
        }
    }

    /// <summary>The slot's file text, then its backup's, in that order; files that are missing or unreadable are skipped.</summary>
    public IEnumerable<string> ReadCandidates(int slot)
    {
        string path = GetPath(slot);
        foreach (string candidate in new[] { path, path + BackupSuffix })
        {
            string text = TryRead(candidate);
            if (text != null) yield return text;
        }
    }

    public void Delete(int slot)
    {
        string path = GetPath(slot);
        foreach (string file in new[] { path, path + BackupSuffix, path + TempSuffix })
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    private static string TryRead(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            Debug.LogWarning($"Nao foi possivel ler o save '{path}': {e.Message}");
            return null;
        }
    }
}
