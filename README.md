<p align="center">
<img
    src="https://neo-cdn.azureedge.net/images/neo_logo.svg"
    width="250px">
</p>

<p align="center">
  <a href="https://travis-ci.org/neo-project/neo">
    <img src="https://travis-ci.org/neo-project/neo.svg?branch=master" alt="Current TravisCI build status.">
  </a>
  <a href="https://github.com/neo-project/neo/releases">
    <img src="https://badge.fury.io/gh/neo-project%2Fneo.svg" alt="Current neo version.">
  </a>
  <a href="https://codecov.io/github/neo-project/neo/branch/master/graph/badge.svg">
    <img src="https://codecov.io/github/neo-project/neo/branch/master/graph/badge.svg" alt="Current Coverage Status." />
  </a>
  <a href="https://github.com/neo-project/neo">
    <img src="https://tokei.rs/b1/github/neo-project/neo?category=lines" alt="Current total lines.">
  </a>	
  <a href="https://github.com/neo-project/neo/blob/master/LICENSE">
    <img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="License.">
  </a>
</p>

NEO 3.0 (under development): A distributed network for the Smart Economy
================

NEO uses digital identity and blockchain technology to digitize assets and leverages smart contracts for autonomously managed digital assets to create a "smart economy" within a decentralized network.

To learn more about NEO, please read the [White Paper](https://docs.neo.org/en-us/whitepaper.html)|[白皮书](https://docs.neo.org/zh-cn/whitepaper.html).

NEO 2.x: Reference template  
================

NEO adopts a model of continuous improvement and evolution.
We believe that the research and development of blockchain technology will be kept in continuous transformation for the next years.
From 2017 until the middle of 2020 (estimated), NEO 2.x will still be supported and the compatibility of new packages will be maintained.

A link to the essential C# reference implementation can be seen below:

* [neo](https://github.com/neo-project/neo/tree/master-2.x): core implementation (branch `master-2.x`)
* [neo-vm](https://github.com/neo-project/neo-vm/tree/master-2.x): virtual machine (branch `master-2.x`)
* [neo-cli](https://github.com/neo-project/neo-cli/tree/master-2.x): command-line interface (branch `master-2.x`)
* [neo-plugins](https://github.com/neo-project/neo-plugins/tree/master-2.x): plugins (branch `master-2.x`)
* [neo-devpack-dotnet](https://github.com/neo-project/neo-devpack-dotnet/tree/master-2.x): NeoContract compiler and development pack for .NET (branch `master-2.x`)

NEO 1.x was known as Antshares and the roots of the code can be found historically in this current repository.

Supported Platforms
--------

We already support the following platforms:

* CentOS 7
* Docker
* macOS 10 +
* Red Hat Enterprise Linux 7.0 +
* Ubuntu 14.04, Ubuntu 14.10, Ubuntu 15.04, Ubuntu 15.10, Ubuntu 16.04, Ubuntu 16.10
* Windows 7 SP1 +, Windows Server 2008 R2 +

We will support the following platforms in the future:

* Debian
* Fedora
* FreeBSD
* Linux Mint
* OpenSUSE
* Oracle Linux

Development
--------

To start building peer applications for NEO on Windows, you need to download [Visual Studio 2017](https://www.visualstudio.com/products/visual-studio-community-vs), install the [.NET Framework 4.7 Developer Pack](https://www.microsoft.com/en-us/download/details.aspx?id=55168) and the [.NET Core SDK](https://www.microsoft.com/net/core).

If you need to develop on Linux or macOS, just install the [.NET Core SDK](https://www.microsoft.com/net/core).

To install Neo SDK to your project, run the following command in the [Package Manager Console](https://docs.nuget.org/ndocs/tools/package-manager-console):

```
PM> Install-Package Neo
```

For more information about how to build DAPPs for NEO, please read the [documentation](http://docs.neo.org/en-us/sc/introduction.html)|[文档](http://docs.neo.org/zh-cn/sc/introduction.html).

Daily builds
--------

If you want to use the [latest daily build](https://www.myget.org/feed/neo/package/nuget/Neo) then you need to add a NuGet.Config to your app with the following content:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
    <packageSources>
        <clear />
        <add key="MyGet-neo" value="https://www.myget.org/F/neo/api/v3/index.json" />
        <add key="NuGet.org" value="https://api.nuget.org/v3/index.json" />
    </packageSources>
</configuration>
```

*NOTE: This NuGet.Config should be with your application unless you want nightly packages to potentially start being restored for other apps on the machine.*

How to Contribute
--------

You can contribute to NEO with [issues](https://github.com/neo-project/neo/issues) and [PRs](https://github.com/neo-project/neo/pulls). Simply filing issues for problems you encounter is a great way to contribute. Contributing implementations is greatly appreciated.

We use and recommend the following workflow:

1. Create an issue for your work.
    * You can skip this step for trivial changes.
	* Reuse an existing issue on the topic, if there is one.
	* Clearly state that you are going to take on implementing it, if that's the case. You can request that the issue be assigned to you. Note: The issue filer and the implementer don't have to be the same person.
1. Create a personal fork of the repository on GitHub (if you don't already have one).
1. Create a branch off of master(`git checkout -b mybranch`).
    * Name the branch so that it clearly communicates your intentions, such as issue-123 or githubhandle-issue.
	* Branches are useful since they isolate your changes from incoming changes from upstream. They also enable you to create multiple PRs from the same fork.
1. Make and commit your changes.
1. Add new tests corresponding to your change, if applicable.
1. Build the repository with your changes.
    * Make sure that the builds are clean.
	* Make sure that the tests are all passing, including your new tests.
1. Create a pull request (PR) against the upstream repository's master branch.
    * Push your changes to your fork on GitHub.

Note: It is OK for your PR to include a large number of commits. Once your change is accepted, you will be asked to squash your commits into one or some appropriately small number of commits before your PR is merged.

License
------

The NEO project is licensed under the [MIT license](LICENSE).
