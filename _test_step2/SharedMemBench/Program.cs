using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Threading;

const string MapName = "zivai_shm_bench";
long[] sizes = { 576L * 1024, (long)(2.25 * 1024 * 1024), (long)(7.91 * 1024 * 1024) };
int repeats = 20;
int cap = 8 * 1024 * 1024 + 64;

using var mmf = MemoryMappedFile.CreateOrOpen(MapName, cap);
using var acc = mmf.CreateViewAccessor(0, cap);
using var dataReady = new EventWaitHandle(false, EventResetMode.AutoReset, "zivai_shm_data_ready");
using var ack = new EventWaitHandle(false, EventResetMode.AutoReset, "zivai_shm_ack");

Console.WriteLine("SHM_WAITING");
var rng = new Random(1);

foreach (var size in sizes)
{
    var data = new byte[size];
    rng.NextBytes(data);
    double totalMs = 0;
    for (int i = 0; i < repeats; i++)
    {
        var sw = Stopwatch.StartNew();
        acc.Write(0, (int)size);
        acc.WriteArray(4, data, 0, data.Length);
        dataReady.Set();
        ack.WaitOne();
        sw.Stop();
        totalMs += sw.Elapsed.TotalMilliseconds;
    }
    double avg = totalMs / repeats;
    double mb = size / 1024.0 / 1024.0;
    Console.WriteLine($"SIZE={size}B ({mb:F2}MB) avg_ms={avg:F3} throughput_MBps={(mb / (avg / 1000.0)):F1}");
}

// terminate signal
acc.Write(0, -1);
dataReady.Set();
Console.WriteLine("DONE");
