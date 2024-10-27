using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        await RetryDownload();
    }

    static async Task RetryDownload()
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

    static async Task DownloadFileInParts(string url, string destinationPath, int partCount)
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

                downloadTasks[i] = DownloadPart(httpClient, url, destinationPath, i + 1, start, end, partSize);
            }

            await Task.WhenAll(downloadTasks);
            Console.WriteLine("All parts downloaded successfully.");

            await MergeParts(destinationPath, partCount);
        }
    }

    static async Task DownloadPart(HttpClient httpClient, string url, string destinationPath, int partNumber, long start, long end, long partSize)
    {
        string partFilePath = destinationPath + $".part{partNumber}";
        string progressFilePath = destinationPath + $".progress{partNumber}";

        long totalReadBytes = 0;

        if (File.Exists(partFilePath))
        {
            long currentPartSize = new FileInfo(partFilePath).Length;
            if (currentPartSize >= partSize)
            {
                Console.WriteLine($"Part {partNumber} already downloaded fully.");
                return;
            }
            totalReadBytes = currentPartSize;
            start += totalReadBytes;
        }

        httpClient.DefaultRequestHeaders.Range = new System.Net.Http.Headers.RangeHeaderValue(start, end);

        try
        {
            var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            using (var contentStream = await response.Content.ReadAsStreamAsync())
            using (var fileStream = new FileStream(partFilePath, FileMode.Append, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[8192];
                int readBytes;
                while ((readBytes = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0 && totalReadBytes < partSize)
                {
                    await fileStream.WriteAsync(buffer, 0, readBytes);
                    totalReadBytes += readBytes;

                    File.WriteAllText(progressFilePath, totalReadBytes.ToString());

                    double progressPercentage = (double)totalReadBytes / partSize * 100;
                    Console.WriteLine($"Part {partNumber} downloading: {progressPercentage:F2}% ({totalReadBytes} / {partSize} bytes)");

                    if (totalReadBytes >= partSize)
                    {
                        break;
                    }
                }
            }

            File.Delete(progressFilePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred in part {partNumber}: {ex.Message}");
            throw;
        }
    }

    static async Task MergeParts(string destinationPath, int partCount)
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
