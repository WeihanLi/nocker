# nocker

## Intro

Simplified docker implemented by C# && .NET

## Prerequisites

The following packages are needed to run nocker.

* btrfs-progs
* curl
* iproute2
* iptables
* libcgroup-tools
* util-linux >= 2.25.2
* coreutils >= 7.5

Because most distributions do not ship a new enough version of util-linux you will probably need to grab the sources from [here](https://www.kernel.org/pub/linux/utils/util-linux/v2.25/) and compile it yourself.

Additionally your system will need to be configured with the following:

* A btrfs filesystem mounted under `/var/nocker`
* A network bridge called `bridge0` and an IP of 10.0.0.1/24
* IP forwarding enabled in `/proc/sys/net/ipv4/ip_forward`
* A firewall routing traffic from `bridge0` to a physical interface.

``` sh
mkdir /var/nocker
fallocate -l 4G /home/nocker.btrfs
mkfs.btrfs /home/nocker.btrfs
mount -o loop /home/nocker.btrfs /var/nocker
```
