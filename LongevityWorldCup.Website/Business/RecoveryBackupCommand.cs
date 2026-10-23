using System.Text.Json;

namespace LongevityWorldCup.Website.Business;

// These operator commands return before web-host/config initialization. They cannot send messages.
public static class RecoveryBackupCommand
{
    public static bool TryRun(string[] args)
    {
        if (args.Length == 0 || args[0] is not ("--create-recovery-backup" or "--verify-recovery-backup")) return false;
        try
        {
            SQLitePCL.Batteries.Init();
            if (args is ["--create-recovery-backup"])
            {
                using var db = new DatabaseManager();
                var root = Directory.GetCurrentDirectory();
                var service = new RecoveryBackupService(db, root, Path.Combine(root, "wwwroot"), TimeProvider.System);
                var result = service.CreateVerifiedAsync().GetAwaiter().GetResult();
                Console.WriteLine(JsonSerializer.Serialize(result));
            }
            else if (args is ["--verify-recovery-backup", var archive, var destination])
            {
                var result = RecoveryBundle.ExtractVerifiedAsync(archive, destination).GetAwaiter().GetResult();
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    Status = "Verified", result.Manifest.CreatedAtUtc, result.Manifest.Release,
                    FileCount = result.Manifest.Files.Count, result.TableRows
                }));
            }
            else throw new ArgumentException("Use --create-recovery-backup or --verify-recovery-backup ARCHIVE NEW_PRIVATE_DIRECTORY.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Recovery backup failed: " + ex.Message);
            Environment.ExitCode = 1;
        }
        return true;
    }
}
