# XDS - A Bitcoin-derived minable cryptocurrency with concurrent PoS

*This is to announce/report the start of a new free and open-source decentralized cryptocurrency, owned and maintained only and fully by its hopefully emerging and growing world-wide community of contributors. This is not a project claimed by a company, founders, VIP's, lead developers or any people that claim a special role. There are no brands or already-reserved accounts. Not even a name has been chosen for this cryptocurrency, it's up to the community to agree on one. For practical purposes, a preliminary coin symbol was chosen (XDS) which may be kept or changed.*

*The reason why the initiator(s) do not want to claim any further role in the life of this cryptocurrency and will remain pseudonymous, anonymous or even silent forever, is that they think this will make adoption and contributions easier, because there will be no hierarchy or 'leaders' then, but freedom instead. It means everyone can contribute, create a website, or coffee mugs for the coin. It shall be a productive anarchy.*

*If there was a White Paper for the coin, it would be Satoshi's white paper, since the success of Bitcoin proves he got it right. The only addition would be a chapter on the proof-of-stake consensus, which runs in parallel to proof-of-work, so that blocks are (quite randomly) produced either by proof-of-work or proof-of-stake. Proof-of-stake might have benefits for the climate, so it seems worthwhile trying this.*

## Overview
------------
Start date: 2nd Jan 2020 23:56:00 UTC (Genesis block time)
Genesis hash: 0x0000000e13c5bf36c155c7cb1681053d607c191fc44b863d0c5aef6d27b8eb8f
Block height as of 19th Jan 2020: 0 (all blocks that were mined for testing have been deleted)
Units mined as of 19th Jan 2020: 0 XDS
Max supply: 21,000,000 XDS
Block reward: 50 XDS (halves every 210,000 blocks)
Consensus: PoW + PoS
Transaction protocol: Only P2WPKH and P2WSH transactions (and burns) are allowed, addresses must be in bech32 format only, base58 addresses are not supported.
PoW Hash: Double-SHA512 (truncated to 256 bits)
Block spacing: 256 seconds
Json/rpc port: 48333 (you need to enable this in xds.conf)

### Connecting and syncing
--------------------------
There are node seed nodes in the source code, community forks are encouraged to change that.
Therefore, to bootstrap the network, as many people as possible should run nodes and publish their IP addresses here.
The default protocol port is 38333. Make sure you open the firewall on your OS and router, so that you can get incoming connections.

### Creating a wallet, mining, staking, receive addresses, transactions
--------------
1.) Run the node
2.) Browse to http://localhost:48334/swagger to access the API help page
3.) Follow the instructions there to 
     a) create a wallet
     b) load the wallet
     c) start mining
     d) start staking
     e) get a receive address
     f) send coins

In addition to this, a Bitcoin-standard json/rpc interface is available at port 48333.

### Forks
Note that the maximum reorg length is set to 125 blocks. That means, if you happen to be on a fork that 'the majority' is not following, and if you want to re-join the 'majority chain', this will not happen automatically. If you want follow the chain that your connected peers are using, and you have been more then 125 blocks on a fork, you need to delete the following directories in [OS Application DATA]/FullNodeRoot/xds/MainNet:
/blocks
/chain
/coinview
/common
/provenheaders

You need NOT delete your wallet(s) (*.wallet.json), but all transactions, including mined/staked blocks that are not on the main chain will be lost if you delete the fork where they happened.

## Build and run on Linux, Windows, MacOS
The .NET Core SDK is required for the build:
https://dotnet.microsoft.com/download

Clone the repository:
git clone https://github.com/sonofsatoshi2020/xds.git

Change to the daemon directory:
cd /src/daemon

Build and start:
dotnet run -c release addnode=[ip address] addnode=[another ip address] ...

Build an executable file:
https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-publish










