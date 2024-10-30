
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        var downloadManager = new DownloadManager();
        await downloadManager.RetryDownload();
    }
}
