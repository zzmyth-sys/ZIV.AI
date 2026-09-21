using System.Diagnostics;
using System.IO.Pipes;

const string PipeName = "zivai_bench";
long[] sizes = { 576L * 1024, (long)(2.25 * 1024 * 1024), (long)(7.91 * 1024 * 1024) };
int repeats = 20;

using var server = new NamedPipeServerStream(
    PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
    PipeOptions.Asynchronous, 1 << 20, 1 << 20);

Console.WriteLine("WAITING_FOR_CLIENT");
server.WaitForConnection();
Console.WriteLine("CLIENT_CONNECTED");

var rng = new Random(1);
var header = new byte[4];
var readHeader = new byte[4];

foreach (var size in sizes)
{
    var data = new byte[size];
    rng.NextBytes(data);
    var echo = new byte[size];
    double totalMs = 0;
    for (int i = 0; i < repeats; i++)
    {
        var sw = Stopwatch.StartNew();
        BitConverter.TryWriteBytes(header, (int)size);
        server.Write(header, 0, 4);
        server.Write(data, 0, data.Length);
        server.Flush();

        server.ReadExactly(readHeader, 0, 4);
        int len = BitConverter.ToInt32(readHeader, 0);
        server.ReadExactly(echo, 0, len);
        sw.Stop();
        totalMs += sw.Elapsed.TotalMilliseconds;
    }
    double avg = totalMs / repeats;
    double mb = size / 1024.0 / 1024.0;
    Console.WriteLine($"SIZE={size}B ({mb:F2}MB) avg_ms={avg:F3} throughput_MBps={(mb / (avg / 1000.0)):F1}");
}

Console.WriteLine("DONE");
