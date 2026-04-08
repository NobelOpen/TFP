using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TaskFlow.Mcp
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Set encoding to UTF-8 without BOM to ensure JSON is correctly parsed by external programs
            Console.InputEncoding = new UTF8Encoding(false);
            Console.OutputEncoding = new UTF8Encoding(false);

            try
            {
                using var pipeClient = new NamedPipeClientStream(".", "TaskFlowMcpPipe", PipeDirection.InOut, PipeOptions.Asynchronous);
                
                // Attempt to connect to the TaskFlow main app, wait up to 5 seconds
                try
                {
                    await pipeClient.ConnectAsync(5000);
                }
                catch (TimeoutException)
                {
                    // If connection fails, output JSON-RPC error
                    Console.WriteLine(@"{""jsonrpc"":""2.0"",""error"":{""code"":-32000,""message"":""TaskFlow is not running or MCP server is not ready.""},""id"":null}");
                    return;
                }

                // Prepare readers/writers
                using var pipeReader = new StreamReader(pipeClient, new UTF8Encoding(false), leaveOpen: true);
                using var pipeWriter = new StreamWriter(pipeClient, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

                var cts = new CancellationTokenSource();

                // Start reader task from pipe and forward to stdout
                var pipeToStdout = Task.Run(async () =>
                {
                    try
                    {
                        while (!cts.Token.IsCancellationRequested)
                        {
                            var line = await pipeReader.ReadLineAsync(cts.Token);
                            if (line == null) break; // Pipe closed
                            Console.WriteLine(line);
                        }
                    }
                    catch (Exception)
                    {
                        // Ignore
                    }
                    finally
                    {
                        cts.Cancel();
                    }
                });

                // Start reader task from stdin and forward to pipe
                var stdinToPipe = Task.Run(async () =>
                {
                    try
                    {
                        using var stdinReader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
                        while (!cts.Token.IsCancellationRequested)
                        {
                            var line = await stdinReader.ReadLineAsync(cts.Token);
                            if (line == null) break; // Stdin closed (e.g. client exited)
                            await pipeWriter.WriteLineAsync(line.AsMemory(), cts.Token);
                        }
                    }
                    catch (Exception)
                    {
                        // Ignore
                    }
                    finally
                    {
                        cts.Cancel();
                    }
                });

                await Task.WhenAny(pipeToStdout, stdinToPipe);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Proxy Exception: {ex.Message}");
            }
        }
    }
}
