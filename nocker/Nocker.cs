﻿// Copyright (c) Weihan Li. All rights reserved.
// Licensed under the MIT license.

using System.ComponentModel;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Nodes;

public sealed class Nocker
{
    internal static readonly MethodInfo[] Methods =
        typeof(Nocker).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    private const string BtrfsPath = "/var/nocker", CGroups = "cpu,cpuacct,memory", TmpPath = "/tmp/nocker";

    internal static void EnsureDirectoryCreated()
    {
        if (!Directory.Exists(BtrfsPath))
        {
            Directory.CreateDirectory(BtrfsPath);
        }
        if (!Directory.Exists(TmpPath))
        {
            Directory.CreateDirectory(TmpPath);
        }
    }

    private static readonly HttpClient HttpClient = new(new HttpClientHandler()
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All
    });

    public void Help()
    {
        Console.WriteLine("nocker:");
        Console.WriteLine("Commands");
        foreach (var method in Methods)
        {
            var commandName = method.Name.ToLower();
            var description = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? commandName;
            var arguments = method.GetParameters()
                .Select(p => new { ParameterName = p.Name!.ToLower(), p.ParameterType, })
                .ToArray();
            Console.WriteLine($"{commandName}\t\t{description}");
            if (arguments.Length > 0)
            {
                foreach (var argument in arguments)
                {
                    Console.WriteLine($"\t{argument.ParameterName}  {argument.ParameterType}");
                }
            }
        }
    }

    public void Version()
    {
        var libraryInfo = ApplicationHelper.GetLibraryInfo(typeof(Nocker));
        Console.WriteLine($"nocker: {libraryInfo.LibraryVersion}+{libraryInfo.LibraryHash}");
    }

    private bool Check(string path)
    {
        return Path.Exists(Path.Combine(BtrfsPath, path));
    }

    private string Init(string dir, string repo, string tag)
    {
        if (!Directory.Exists(dir))
            throw new ArgumentException($"directory named {dir} not exits");

        var uuid = $"img_{Random.Shared.Next(30000, 65536)}";
        while (!Check(uuid))
        {
            Directory.CreateDirectory(Path.Combine(BtrfsPath, uuid));
            //     CommandExecutor.ExecuteCommand($"btrfs subvolume create {BtrfsPath}/{uuid}");
            //     CommandExecutor.ExecuteCommand($"cp -rf --reflink=auto {dir}/* {BtrfsPath}/{uuid}");   
        }

        var imgSourcePath = $"{BtrfsPath}/{uuid}/img.source";
        if (!File.Exists(imgSourcePath))
        {
            File.WriteAllText(imgSourcePath, dir);
        }

        Console.WriteLine($"Created: {uuid}");

        return uuid;
    }

    /// <summary>
    /// Pull an image from Docker Hub: nocker pull name:tag
    /// </summary>
    /// <param name="image">image id, for example: <c>weihanli/mdnice:latest</c> </param>
    public async Task Pull(string image)
    {
        // https://docs.docker.com/registry/spec/api/#pulling-an-image
        Guard.NotNullOrEmpty(image);
        Console.WriteLine($"Pulling image {image}");
        var splits = image.Split(':', 2);
        var (repo, tag) = (splits[0], splits.Length > 1 ? splits[1] : "latest");
        if (repo.IndexOf('/') <= 0)
        {
            repo = $"library/{repo}";
        }
        var getTokenUrl =
            $"https://auth.docker.io/token?service=registry.docker.io&scope=repository:{repo}:pull";
        var getTokenResponseObject = await HttpClient.GetFromJsonAsync<JsonObject>(getTokenUrl);
        ArgumentNullException.ThrowIfNull(getTokenResponseObject);
        var token = getTokenResponseObject["token"]!.GetValue<string>();

        Console.WriteLine("Trying to get manifests");
        using var getManifestsRequest = new HttpRequestMessage(HttpMethod.Get, $"https://registry-1.docker.io/v2/{repo}/manifests/{tag}");
        getManifestsRequest.SetBearerToken(token);
        using var getManifestResponse = await HttpClient.SendAsync(getManifestsRequest);
        var getManifestResponseObject = await getManifestResponse.Content.ReadFromJsonAsync<JsonObject>();
        ArgumentNullException.ThrowIfNull(getManifestResponseObject);
        if (getManifestResponse.Headers.TryGetValues("Docker-Content-Digest", out var digestValues))
        {
            Console.WriteLine($"docker-content-digest: {digestValues.StringJoin(", ")}");
        }
        
        var layers = getManifestResponseObject!["fsLayers"]!.AsArray()
            .Select(l => l!["blobSum"]!.GetValue<string>())
            .ToArray();
        var tmpDirPath = Path.Combine(TmpPath, Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows())
        {
            tmpDirPath = tmpDirPath.Replace('\\', '/');
        }
        Directory.CreateDirectory(tmpDirPath);
        
        foreach (var layer in layers)
        {
            Console.WriteLine($"Trying to get layer {layer}");
            // https://registry-1.docker.io/v2/weihanli/mdnice/blobs/sha256:xxx
            var blobUrl = $"https://registry-1.docker.io/v2/{repo}/blobs/{layer}";
            using var getBlobRequest = new HttpRequestMessage(HttpMethod.Get, blobUrl);
            getBlobRequest.SetBearerToken(token);
            using var getBlobResponse = await HttpClient.SendAsync(getBlobRequest);
            await using var blobStream = await getBlobResponse.Content.ReadAsStreamAsync();
            {
                await using var decompressStream = new GZipStream(blobStream, CompressionMode.Decompress);
                await TarFile.ExtractToDirectoryAsync(decompressStream, tmpDirPath, true);
            }
        }
        
        await File.WriteAllTextAsync(Path.Combine(tmpDirPath, "img.source"), $"{repo}:{tag}");
        Console.WriteLine("image downloaded");
        Console.WriteLine(tmpDirPath);

        Init(tmpDirPath, repo, tag);
    }

    /// <summary>
    /// Delete an image: nocker rmi image_id
    /// </summary>
    /// <param name="imageId">image to remove</param>
    public void Rmi(string imageId)
    {
        if (!Check(imageId))
            Console.WriteLine($"No image named {imageId} exists");            

        // CommandExecutor.ExecuteCommand($"btrfs subvolume delete {BtrfsPath}/{containerId}");        
        // CommandExecutor.ExecuteCommand($"cgdelete -g {CGroups}:/{containerId}");

        var storagePath = File.ReadAllText(Path.Combine(BtrfsPath, imageId, "img.source"));
        Directory.Delete(storagePath, true);
        Directory.Delete(Path.Combine(BtrfsPath, imageId));
        Console.WriteLine($"Removed: {imageId}");
    }

    /// <summary>
    /// List images: nocker images
    /// </summary>
    public void Images()
    {
        Console.WriteLine($"{"ImageId", -10} {"Repo", -24} Source");
        foreach (var img in Directory.GetDirectories(BtrfsPath))
        {
            var uuid = Path.GetFileName(img);
            if (!uuid.StartsWith("img_"))
            {
                continue;
            }
            
            var imageSource = File.ReadAllText($"{BtrfsPath}/{uuid}/img.source");
            var sourceRepoPath = Path.Combine(imageSource, "img.source"); 
            if (!File.Exists(sourceRepoPath))
            {
                continue;
            }
            
            var imageSourceRepo = File.ReadAllText(sourceRepoPath);
            Console.WriteLine($"{uuid[4..], -10} {imageSourceRepo, -24} {imageSource}");
        }
    }

    /// <summary>
    /// List containers: nocker ps
    /// </summary>
    public void Ps()
    {
        Console.WriteLine("ContainerId\t\tCommand");
        foreach (var ps in Directory.GetDirectories(BtrfsPath).Where(f => f.StartsWith("ps_")))
        {
            var id = Path.GetDirectoryName(ps);
            var command = File.ReadAllText($"{BtrfsPath}/{id}/${id}.cmd");
            Console.WriteLine($"{id}\t\t{command}");
        }
    }

    /// <summary>
    /// Create a container: nocker run image_id command
    /// </summary>
    /// <param name="image">image</param>
    /// <param name="command">command</param>
    public async Task Run(string image, string? command)
    {
        if (!Check(image))
        {
            await Pull(image);
        }

        string uuid;
        do
        {
            uuid = $"ps_${Random.Shared.Next(42002, 42254)}";
        } while (Check(uuid));

        var ip = uuid[^3..].Replace("0", "");
        var mac = $"{uuid[^3..^2]}:{uuid[^2..]}";

        await CommandExecutor.ExecuteCommandAsync($"ip link add dev veth0_{uuid} type veth peer name veth1_{uuid}");
        await CommandExecutor.ExecuteCommandAsync($"ip link set dev veth0_{uuid} up");
        await CommandExecutor.ExecuteCommandAsync($"ip link set veth0_{uuid} master bridge0");
        await CommandExecutor.ExecuteCommandAsync($"ip netns add netns_{uuid}");
        await CommandExecutor.ExecuteCommandAsync($"ip link set veth1_{uuid} netns netns_{uuid}");

        await CommandExecutor.ExecuteCommandAsync($"ip netns exec netns_{uuid} ip link set dev lo up");
        await CommandExecutor.ExecuteCommandAsync(
            $"ip netns exec netns_{uuid} ip link set veth1_{uuid} address 02:42:ac:11:00{mac}");
        await CommandExecutor.ExecuteCommandAsync($"ip netns exec netns_{uuid} ip addr add 10.0.0.{ip}/24 dev veth1_{uuid}");
        await CommandExecutor.ExecuteCommandAsync($"ip netns exec netns_{uuid} ip link set dev veth1_{uuid} up");
        await CommandExecutor.ExecuteCommandAsync($"ip netns exec netns_{uuid} ip route add default via 10.0.0.1");
        await CommandExecutor.ExecuteCommandAsync($"btrfs subvolume snapshot {BtrfsPath}/{image} {BtrfsPath}/{uuid}");

        await File.WriteAllTextAsync($"{BtrfsPath}/{uuid}/etc/resolv.conf", "nameserver 8.8.8.8");
        await File.WriteAllTextAsync($"{BtrfsPath}/{uuid}/{uuid}.cmd", command ?? string.Empty);

        await CommandExecutor.ExecuteCommandAsync($"cgcreate -g {CGroups}:/{uuid}");
        await CommandExecutor.ExecuteCommandAsync($"cgset -r cpu.shares=512 {uuid}");
        await CommandExecutor.ExecuteCommandAsync($"cgset -r memory.limit_in_bytes={512 * 1024 * 1024} {uuid}");


        await CommandExecutor.ExecuteCommandAsync($@"cgexec -g {CGroups}:{uuid}
ip netns exec netns_{uuid}
unshare -fmuip --mount-proc
chroot {BtrfsPath}/{uuid}
/bin/sh -c ""/bin/mount -t proc proc /proc && {command}""
2>&1 | tee ""{BtrfsPath}/{uuid}/{uuid}.log"" || true
");

        await CommandExecutor.ExecuteCommandAsync($"ip link del dev veth0_{uuid}");
        await CommandExecutor.ExecuteCommandAsync($"ip netns del netns_{uuid}");
    }

    /// <summary>
    /// Delete an image or container: nocker rm container_id
    /// </summary>
    /// <param name="containerId">container to remove</param>
    public void Rm(string containerId)
    {
        if (!Check(containerId))
            Console.WriteLine($"No container named {containerId} exists");

        CommandExecutor.ExecuteCommand($"btrfs subvolume delete {BtrfsPath}/{containerId}");
        CommandExecutor.ExecuteCommand($"cgdelete -g {CGroups}:/{containerId}");
        Console.WriteLine($"Removed: {containerId}");
    }

    /// <summary>
    /// Build image from Dockerfile/Containerfile
    /// </summary>
    /// <param name="containerFile"></param>
    /// <param name="tag"></param>
    /// <param name="context"></param>
    public void Build(string containerFile, string tag, string? context = null)
    {
    }
    
    /// <summary>
    /// create new tag
    /// </summary>
    /// <param name="originalTag">originalTag</param>
    /// <param name="newTag">newTag</param>
    public void Tag(string originalTag, string newTag)
    {
    }
    
    /// <summary>
    /// Push image
    /// </summary>
    /// <param name="imageTag">image tag to push</param>
    public void Push(string imageTag)
    {
    }

    public void Exec()
    {
    }

    /// <summary>
    /// logs
    /// </summary>
    /// <param name="container">container</param>
    public void Logs(string container)
    {
        if (!Check(container))
            Console.WriteLine($"No container named {container} exists");

        var logFilePath = $"{BtrfsPath}/{container}/{container}.log";
        var logs = File.ReadAllLines(logFilePath);
        foreach (var log in logs)
        {
            Console.WriteLine(log);
        }
    }
}

