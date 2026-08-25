# Project Overview

## What this is

`DigitalTwin-futurotec` - a game/app built on ViitorCloud's shared Unity base
project template. Lives at `Assets/Games/DigitalTwin-futurotec/` inside the
base project, but is its own git repository and remote
(`https://github.com/vinayak-vc/DigitalTwin-futurotec.git`) - the base project
around it is read-only from this repo's perspective; only files under this
folder are edited here.

This repo was empty prior to the MQTT work below (initial commit only).

## In progress - MQTT integration

Added a provider-style MQTT client module so the app can publish/subscribe to
a broker (device telemetry, control commands, live twin state, etc). See
architecture.md and roadmap.md.

## Key decisions made so far

- Library: **MQTTnet** (not M2Mqtt) - see decisions.md.
- Target platforms: Desktop/Mobile (Standalone, Android, iOS) only. No WebGL
  support - MQTTnet's TCP transport does not work in WebGL builds (raw
  sockets are unavailable there); revisit if WebGL is ever required.
- Broker host/port/credentials are **not** hardcoded - `MqttBrokerSettings` is
  a non-secret config asset, credentials are injected at runtime.
- The base project's `Packages/manifest.json` and `ProjectSettings/` are
  read-only from this repo - the MQTTnet dependency is brought in as a
  manually-installed package/DLL, not a manifest edit. See decisions.md.
