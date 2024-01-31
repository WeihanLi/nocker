// Copyright (c) Weihan Li. All rights reserved.
// Licensed under the MIT license.

Console.WriteLine("Hello Nocker!");

var nocker = new Nocker();
if (args.IsNullOrEmpty() 
    || args is ["-h"] or ["--help"]
    )
{
    nocker.Help();
    return 0;
}

var commandName = args[0];
var method = Nocker.Methods.FirstOrDefault(x => x.Name.EqualsIgnoreCase(commandName));
if (method is null)
{
    ConsoleHelper.WriteLineWithColor($"Not supported command {commandName}", ConsoleColor.Red);
    return -1;
}

try
{
    Nocker.EnsureDirectoryCreated();
    var result = method.Invoke(nocker, args[1..].Select(x => (object?)x).ToArray());
    await TaskHelper.ToTask(result);
    return 0;
}
catch (Exception e)
{
    ConsoleHelper.WriteLineWithColor($"Exception when invoke command {e}", ConsoleColor.DarkRed);
    return -2;
}

