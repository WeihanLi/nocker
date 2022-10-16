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
    Console.WriteLine($"Not supported command {commandName}");
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
    Console.WriteLine($"Exception when invoke command {e}");
    return -2;
}
