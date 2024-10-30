using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

public class DownloadManager
{
    public async Task RetryDownload()
    {
        string url;
        int partCount;

        if (File.Exists("download_info.txt"))
        {
            var lines = File.ReadAllLines("download_info.txt");
            url = lines[0];
            partCount = int.Parse(lines[1]);
            Console.WriteLine($"Resuming download: {url} ({partCount} parts)");
        }
        else
        {
            Console.WriteLine("Enter the URL of the file to download:");
            url = Console.ReadLine();

            Console.WriteLine("How many parts do you want to download it in?");
            while (!int.TryParse(Console.ReadLine(), out partCount) || partCount <= 0)
            {
                Console.WriteLine("Please enter a valid positive number.");
            }

            File.WriteAllLines("download_info.txt", new[] { url, partCount.ToString() });
        }

        string downloadsFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads";
        string fileName = Path.GetFileName(url);
        string destinationPath = Path.Combine(downloadsFolder, fileName);

        try
        {
            await DownloadFileInParts(url, destinationPath, partCount);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
            File.Delete("download_info.txt"); // Hata olursa dosya silinir
            Console.WriteLine("An error occurred. Restarting the download process.");
            await RetryDownload(); // Hata sonrası baştan başlatma
        }
    }

    private async Task DownloadFileInParts(string url, string destinationPath, int partCount)
    {
        using (HttpClient httpClient = new HttpClient())
        {
            var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            long totalBytes = response.Content.Headers.ContentLength ?? -1;

            if (totalBytes <= 0)
            {
                Console.WriteLine("Unable to retrieve file size.");
                return;
            }

            long partSize = totalBytes / partCount;
            Task[] downloadTasks = new Task[partCount];

            for (int i = 0; i < partCount; i++)
            {
                long start = i * partSize;
                long end = (i == partCount - 1) ? totalBytes - 1 : (start + partSize - 1);

                var downloadPart = new DownloadPart(httpClient, url, destinationPath, i + 1, start, end, partSize);
                downloadTasks[i] = downloadPart.DownloadAsync();
            }

            await Task.WhenAll(downloadTasks);
            Console.WriteLine("All parts downloaded successfully.");

            await MergeParts(destinationPath, partCount);
        }
    }

    private async Task MergeParts(string destinationPath, int partCount)
    {
        using (var outputStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            for (int i = 1; i <= partCount; i++)
            {
                string partFilePath = destinationPath + $".part{i}";

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
        Console.WriteLine($"All parts merged: {destinationPath}");
        File.Delete("download_info.txt");
    }
}

public class DownloadPart
{
    private HttpClient HttpClient { get; }
    private string Url { get; }
    private string DestinationPath { get; }
    private int PartNumber { get; }
    private long Start { get; set; } // 'set' erişimci eklendi
    private long End { get; }
    private long PartSize { get; }

    public DownloadPart(HttpClient httpClient, string url, string destinationPath, int partNumber, long start, long end, long partSize)
    {
        HttpClient = httpClient;
        Url = url;
        DestinationPath = destinationPath;
        PartNumber = partNumber;
        Start = start; // Başlangıç değeri atanıyor
        End = end;
        PartSize = partSize;
    }

    public async Task DownloadAsync()
    {
        string partFilePath = DestinationPath + $".part{PartNumber}";
        string progressFilePath = DestinationPath + $".progress{PartNumber}";

        long totalReadBytes = 0;

        if (File.Exists(partFilePath))
        {
            long currentPartSize = new FileInfo(partFilePath).Length;
            if (currentPartSize >= PartSize)
            {
                Console.WriteLine($"Part {PartNumber} already downloaded fully.");
                return;
            }
            totalReadBytes = currentPartSize;
            Start += totalReadBytes; // Artık bu satırda hata olmayacak
        }

        HttpClient.DefaultRequestHeaders.Range = new System.Net.Http.Headers.RangeHeaderValue(Start, End);

        try
        {
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

                    if (totalReadBytes >= PartSize)
                    {
                        break;
                    }
                }
            }

            File.Delete(progressFilePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred in part {PartNumber}: {ex.Message}");
            throw;
        }
    }
}