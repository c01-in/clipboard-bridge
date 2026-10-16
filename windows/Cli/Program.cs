using ClipboardBridge;
using System.IO.Pipes;
using System.Text;
if (args.Length == 0) { Console.WriteLine("status | pairing-status | account <json路径> | android-config <json路径> | endpoints <地址> | send-text <文字> | send-image <PNG路径>"); return; }
try
{
    var command = args[0] switch
    {
        "status" or "pairing-status" => new Command(args[0]),
        "account" or "android-config" => new Command(args[0], Value: await File.ReadAllTextAsync(args[1])),
        "endpoints" => new Command("endpoints", Value: args[1]),
        "send-text" => new Command("send", "text/plain", Convert.ToBase64String(Encoding.UTF8.GetBytes(args[1]))),
        "send-image" => new Command("send", "image/png", Convert.ToBase64String(await File.ReadAllBytesAsync(args[1]))),
        _ => throw new ArgumentException("未知命令")
    };
    using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(40));
    using var pipe = new NamedPipeClientStream(".", Bridge.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    await pipe.ConnectAsync(3000, ct.Token); await Bridge.WriteFrame(pipe, Bridge.Encode(command), ct.Token);
    var reply = Bridge.Decode<Reply>(await Bridge.ReadFrame(pipe, ct.Token));
    Console.WriteLine(UiText.Translate(reply.Message));
    if (reply.Value is not null) Console.WriteLine(reply.Value);
    if (!reply.Ok) Environment.ExitCode = 1;
}
catch (Exception e) { Console.Error.WriteLine(e.Message); Environment.ExitCode = 1; }
