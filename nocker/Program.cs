// Copyright (c) Weihan Li. All rights reserved.
// Licensed under the MIT license.

Console.WriteLine("Hello Nocker!");

var nocker = new Nocker();
if (args.IsNullOrEmpty())
{
    nocker.Help();
    return 0;
}

var commandName = args[0];
var method = Nocker.Methods.FirstOrDefault(x => x.Name.EqualsIgnoreCase(commandName));
if (method is null)
{
    WriteLineWithColor($"Not supported command {commandName}", ConsoleColor.Red);
    return -1;
}

try
{
    var result = method.Invoke(nocker, args[1..].Select(x => (object?)x).ToArray());
    var task = result switch
    {
        Task t => new ValueTask(t),
        ValueTask vt => vt,
        _ => ValueTask.CompletedTask
    };
    await task;
    return 0;
}
catch (Exception e)
{
    WriteLineWithColor($"Exception when invoke command {e}", ConsoleColor.DarkRed);
    return -2;
}


static void WriteLineWithColor(string text, ConsoleColor consoleColor)
{
    var originalColor = Console.ForegroundColor;
    Console.ForegroundColor = ConsoleColor.Red;
    try
    {
        Console.WriteLine(text);
    }
    finally
    {
        Console.ForegroundColor = originalColor;
    }        
}