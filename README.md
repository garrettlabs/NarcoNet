# 🧠 NarcoNet

Server-Client sync Mod for SPT

  > "One Connection. Every Mod. Every Setting. More Reliable than your dealer."

## Description

NarcoNet is a synchronization framework for SPT that ensures that every client
has the same mod setup as the server, no mismatched configurations,
missing dependencies, or version chaos.

When a player connects, NarcoNet will check the servers Mod List and compare
it to the client's installation and pushes or removes files auto-magically
to keep everything clean and consistent.

## Features

  - Automatic mod/config sync between server and client.
  - Enforce paths the client cannot opt-out
  - Optional paths the client can choose to ignore (NarcoNet_Data/Exclusions.json)
  - Per-profile sync bypass (`ignoredProfiles`) for test/admin profiles.
  - Zero tolerance for desyncs.

## Ignored profiles

Use `ignoredProfiles` in the server's `config.yaml` to skip NarcoNet sync entirely for
specific SPT profiles — for example test or admin profiles that should not receive synced
mods. The client short-circuits before any hashing or downloads when the active profile
matches, and notifies the server so the bypass is recorded in the server log.

```yaml
syncPaths:
  - BepInEx/plugins
  - SPT/user/mods

ignoredProfiles:
  - 64f1a2b3c4d5e6f7a8b9c0d1
  - user/profiles/admin.json

exclusions:
  - "**/*.log"
```

`ignoredProfiles` entries can be bare profile IDs, profile JSON file names, or profile
paths; NarcoNet normalizes them to the profile file stem before matching the active
profile ID. An empty list (the default) leaves sync behaviour unchanged.

## Philosophy

  > You run your own operation. NarcoNet just keeps your crew supplied.

  Narconet allows you to enforce all clients to have the MANDATORY files they
  will need to be able to connect on your server and play (enforced paths)

  It also allows you to try and distribute optional files that SHOULD be present
  to fully enjoy the game, but the client can refuse if he chooses. (optional paths)

  What it does NOT do: We cannot control that a client does not install some
  certain mods/files that you consider cheating... The reason ? The client can
  always rename, recompile with a small difference to get another hash...

  If you don't trust your client, do not allow him to connect and play with you.

