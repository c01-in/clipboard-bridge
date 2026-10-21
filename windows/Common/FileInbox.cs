using System.Text.Json;

namespace ClipboardBridge;

/// <summary>Runs as the signed-in user, never as the Windows service account.</summary>
public static class FileInbox
{
    sealed record Plan(string Id, SavedFile[] Files, string[] Destinations);
    public static string[] Save(FileInboxBatch batch, string directory, string journalDirectory)
    {
        if (!FileProtocol.IsId(batch.Id) || batch.Files.Length is < 1 or > FileProtocol.MaxFiles || !Path.IsPathFullyQualified(directory))
            throw new InvalidDataException("Invalid file destination.");
        Directory.CreateDirectory(directory); Directory.CreateDirectory(journalDirectory);
        string journal = Path.Combine(journalDirectory, batch.Id + ".json");
        Plan plan;
        if (File.Exists(journal))
        {
            plan = Bridge.Decode<Plan>(File.ReadAllBytes(journal));
            if (plan.Id != batch.Id || !plan.Files.SequenceEqual(batch.Files)) throw new InvalidDataException("File receipt does not match its saved plan.");
        }
        else
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var destinations = batch.Files.Select(f =>
            {
                string name = FileProtocol.SafeName(f.Name), candidate = Path.Combine(directory, name);
                for (int suffix = 1; File.Exists(candidate) || Directory.Exists(candidate) || !used.Add(candidate); suffix++)
                    candidate = Path.Combine(directory, Path.GetFileNameWithoutExtension(name) + " (" + suffix + ")" + Path.GetExtension(name));
                return candidate;
            }).ToArray();
            plan = new Plan(batch.Id, batch.Files, destinations);
            File.WriteAllBytes(journal + ".tmp", Bridge.Encode(plan)); File.Move(journal + ".tmp", journal);
        }
        for (int i = 0; i < plan.Files.Length; i++)
        {
            var source = plan.Files[i]; string destination = plan.Destinations[i];
            // A retry after a tray restart must not create another copy or overwrite a different file.
            if (File.Exists(destination))
            {
                if (new FileInfo(destination).Length != source.Size || FileProtocol.HashFile(destination) != source.Sha256)
                    throw new IOException("A different file occupies the reserved destination: " + destination);
                continue;
            }
            // Keep the temporary component short even for a 240-character source name.
            string partial = Path.Combine(Path.GetDirectoryName(destination)!, ".clipboardbridge-" + batch.Id + "-" + i + ".partial");
            try
            {
                if (File.Exists(partial) && (File.GetAttributes(partial) & FileAttributes.ReparsePoint) != 0) throw new IOException("The temporary file is a link.");
                using (var input = File.OpenRead(source.Path))
                using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024))
                { input.CopyTo(output, 128 * 1024); output.Flush(true); }
                if (new FileInfo(partial).Length != source.Size || FileProtocol.HashFile(partial) != source.Sha256)
                    throw new InvalidDataException("File integrity verification failed: " + source.Name);
                File.Move(partial, destination, false);
            }
            finally { if (File.Exists(partial)) File.Delete(partial); }
        }
        return plan.Destinations;
    }
}
