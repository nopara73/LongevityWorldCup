namespace LongevityWorldCup.Website.Business
{
    public static class NewsletterService
    {
        private static readonly object FileLock = new();

        public static async Task<string?> SubscribeAsync(string email, ILogger logger, IWebHostEnvironment environment)
        {
            string contentRootPath = environment.ContentRootPath;
            string dataDir = Path.Combine(contentRootPath, "AppData");
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }

            string filePath = Path.Combine(dataDir, "subscriptions.txt");

            int maxRetries = 3;
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    // Use the FileLock to synchronize file access
                    lock (FileLock)
                    {
                        // Open or create the file with exclusive access
                        using var stream = new FileStream(
                            filePath,
                            FileMode.OpenOrCreate,
                            FileAccess.ReadWrite,
                            FileShare.None
                        );

                        // Read existing emails
                        stream.Seek(0, SeekOrigin.Begin);
                        var existingEmails = new List<string>();
                        using (var reader = new StreamReader(stream, leaveOpen: true))
                        {
                            string? line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                existingEmails.Add(line.Trim());
                            }
                        }

                        if (existingEmails.Contains(email, StringComparer.OrdinalIgnoreCase))
                        {
                            return "This email is already subscribed.";
                        }

                        // Append the new email
                        stream.Seek(0, SeekOrigin.End);
                        using var writer = new StreamWriter(stream, leaveOpen: true);
                        writer.WriteLine(email);
                    }

                    break; // Success
                }
                catch (IOException ex)
                {
                    if (i == maxRetries - 1)
                    {
                        logger.LogError(ex, "Error accessing subscription file.");
                        return "An error occurred while saving your subscription. Please try again later.";
                    }

                    // Wait before retrying
                    await Task.Delay(100);
                }
            }

            return null; // Success
        }

        public static async Task<string?> UnsubscribeAsync(string email, ILogger logger, IWebHostEnvironment environment)
        {
            string contentRootPath = environment.ContentRootPath;
            string dataDir = Path.Combine(contentRootPath, "AppData");
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }

            string filePath = Path.Combine(dataDir, "subscriptions.txt");

            int maxRetries = 3;
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    lock (FileLock)
                    {
                        if (!File.Exists(filePath))
                        {
                            return null; // Idempotent success
                        }

                        var remainingEmails = new List<string>();
                        foreach (var line in File.ReadAllLines(filePath))
                        {
                            var trimmed = line.Trim();
                            if (string.IsNullOrWhiteSpace(trimmed))
                            {
                                continue;
                            }

                            if (!string.Equals(trimmed, email, StringComparison.OrdinalIgnoreCase))
                            {
                                remainingEmails.Add(trimmed);
                            }
                        }

                        File.WriteAllLines(filePath, remainingEmails);
                    }

                    break; // Success
                }
                catch (IOException ex)
                {
                    if (i == maxRetries - 1)
                    {
                        logger.LogError(ex, "Error accessing subscription file.");
                        return "An error occurred while removing your subscription. Please try again later.";
                    }

                    await Task.Delay(100);
                }
            }

            return null; // Success
        }
    }
}