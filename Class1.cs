
public class DownloadManager
{
    private string Url { get; set; }
    private int PartCount { get; set; }
    private string DestinationPath { get; set; }
    private HttpClient HttpClient { get; set; }
    private long TotalFileSize { get; set; }

    public DownloadManager(string url, int partCount)
    {
        Url = url;
        PartCount = partCount;
        HttpClient = new HttpClient();
        DestinationPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads", Path.GetFileName(url));
    }

    public async Task StartDownload()
    {
        Console.WriteLine($"Starting download: {Url}");

        if (File.Exists("download_info.txt"))
        {
            var lines = File.ReadAllLines("download_info.txt");
            Url = lines[0];
            PartCount = int.Parse(lines[1]);
            Console.WriteLine($"Resuming download with {PartCount} parts.");
        }
        else
        {
            File.WriteAllLines("download_info.txt", new[] { Url, PartCount.ToString() });
        }

        var response = await HttpClient.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        TotalFileSize = response.Content.Headers.ContentLength ?? -1;

        if (TotalFileSize <= 0)
        {
            Console.WriteLine("Unable to retrieve file size.");
            return;
        }

        long partSize = TotalFileSize / PartCount;
        Task[] downloadTasks = new Task[PartCount];

        for (int i = 0; i < PartCount; i++)
        {
            long start = i * partSize;
            long end = (i == PartCount - 1) ? TotalFileSize - 1 : (start + partSize - 1);

            var downloadPart = new DownloadPart(i + 1, Url, DestinationPath, HttpClient, start, end, partSize);
            downloadTasks[i] = downloadPart.DownloadAsync();
        }

        await Task.WhenAll(downloadTasks);
        Console.WriteLine("All parts downloaded successfully.");

        await MergeParts();
    }

    private async Task MergeParts()
    {
        using (var outputStream = new FileStream(DestinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            for (int i = 1; i <= PartCount; i++)
            {
                string partFilePath = DestinationPath + $".part{i}";

                if (File.Exists(partFilePath))
                {
                    using (var inputStream = new FileStream(partFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        await inputStream.CopyToAsync(outputStream);
                    }
                    File.Delete(partFilePath);
                    Console.WriteLine($"Part {i} merged.");
                }
                else
                {
                    Console.WriteLine($"Part {i} is missing, merge skipped.");
                }
            }
        }
        Console.WriteLine($"All parts merged: {DestinationPath}");
        File.Delete("download_info.txt");
    }
}

public class DownloadPart
{
    private int PartNumber { get; set; }
    private string Url { get; set; }
    private string DestinationPath { get; set; }
    private HttpClient HttpClient { get; set; }
    private long Start { get; set; }
    private long End { get; set; }
    private long PartSize { get; set; }

    public DownloadPart(int partNumber, string url, string destinationPath, HttpClient httpClient, long start, long end, long partSize)
    {
        PartNumber = partNumber;
        Url = url;
        DestinationPath = destinationPath;
        HttpClient = httpClient;
        Start = start;
        End = end;
        PartSize = partSize;
    }

    public async Task DownloadAsync()
    {
        string partFilePath = DestinationPath + $".part{PartNumber}";
        string progressFilePath = DestinationPath + $".progress{PartNumber}";

        long totalReadBytes = 0;

        // Check if part file already has the expected size
        if (File.Exists(partFilePath))
        {
            long currentPartSize = new FileInfo(partFilePath).Length;
            if (currentPartSize >= PartSize)
            {
                Console.WriteLine($"Part {PartNumber} already downloaded fully.");
                return;
            }
            totalReadBytes = currentPartSize;
            Start += totalReadBytes;
        }

        HttpClient.DefaultRequestHeaders.Range = new System.Net.Http.Headers.RangeHeaderValue(Start, End);

        var response = await HttpClient.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        using (var contentStream = await response.Content.ReadAsStreamAsync())
        using (var fileStream = new FileStream(partFilePath, FileMode.Append, FileAccess.Write, FileShare.None))
        {
            byte[] buffer = new byte[8192];
            int readBytes;
            while ((readBytes = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0 && totalReadBytes < PartSize)
            {
                await fileStream.WriteAsync(buffer, 0, readBytes);
                totalReadBytes += readBytes;

                File.WriteAllText(progressFilePath, totalReadBytes.ToString());

                double progressPercentage = (double)totalReadBytes / PartSize * 100;
                Console.WriteLine($"Part {PartNumber} downloading: {progressPercentage:F2}% ({totalReadBytes} / {PartSize} bytes)");

                // Ensure we don't write more than the designated part size
                if (totalReadBytes >= PartSize)
                {
                    break;
                }
            }
        }

        File.Delete(progressFilePath);
    }
}
