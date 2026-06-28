# 🧠 NarcoNet  
Server-Client sync Mod for SPT

> "One Connection. Every Mod. Every Setting. More Reliable than your dealer."

## Description
NarcoNet is a synchronization framework for SPT that ensures that every client has the same mod setup as the server, no mismatched configurations, missing dependencies, or version chaos.
When a player connects, NarcoNet will check the servers Mod List and compare it to the client's installation and pushes or removes files auto-magically to keep everything clean and consistent.

## Features
- Automatic mod/config sync between server and client.
- Version and integrity checking.
- Optional dependency enforcement.
- Lightweight, no-nonsense,  p2p protocol.
- Zero tolerance for desyncs.

## `config.yaml` usage
NarcoNet loads `config.yaml` from the NarcoNet server mod directory under `user/mods/*narconet*`. Use `ignoredProfiles` to skip NarcoNet sync for specific SPT profiles, such as test or admin profiles:

```yaml
syncPaths:
  - ../BepInEx/plugins
  - path: user/mods
    name: Server mods
    enabled: true
    enforced: true
    silent: false
    restartRequired: true

ignoredProfiles:
  - profile-one
  - user/profiles/profile-two.json

exclusions:
  - user/mods/**/config.json
```

`ignoredProfiles` entries can be bare profile IDs, profile JSON file names, or profile paths; NarcoNet normalizes them to the active profile ID before deciding whether to bypass sync.

## Philosophy
> You run your own operation. NarcoNet just keeps your crew supplied.