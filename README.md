# DigitalTwin-futurotec

A real-time Unity Digital Twin application built on ViitorCloud's shared Unity base project template. This module provides a robust, decoupled MQTT client integration for bidirectional communication between Unity digital twin scenes and physical IoT hardware, sensors, and control systems.

---

## Table of Contents

- [Overview](#overview)
- [Key Features](#key-features)
- [Architecture & Folder Structure](#architecture--folder-structure)
- [Prerequisites & Dependencies](#prerequisites--dependencies)
- [Configuration](#configuration)
  - [1. ScriptableObject (`MqttBrokerSettings.asset`)](#1-scriptableobject-mqttbrokersettingsasset)
  - [2. Runtime JSON (`MqttConfig.json`)](#2-runtime-json-mqttconfigjson)
  - [3. Latest-Edit-Wins Priority](#3-latest-edit-wins-priority)
- [Getting Started](#getting-started)
- [MQTT Topic Reference & Payloads](#mqtt-topic-reference--payloads)
- [Code Example: Subscribing to Topics](#code-example-subscribing-to-topics)
- [Documentation Index](#documentation-index)

---

## Overview

`DigitalTwin-futurotec` resides at `Assets/Games/DigitalTwin-futurotec/` inside the parent base project workspace, operating as its own independent git repository. It is engineered to:

1. Connect to enterprise MQTT brokers over standard TCP or secure WebSockets (`wss://`).
2. Dispatch network events onto Unity's main thread safely and boundedly.
3. Drive live 3D digital twin states (such as lighting arrays, telemetry monitors, and visual alerts).
4. Provide diagnostic tools, manual connection overrides, and real-time on-screen telemetry.

---

## Key Features

- **Decoupled Architecture**: `IMqttClientService` abstracts the underlying network transport. The default implementation is powered by **MQTTnet 4.3.x**, guarded cleanly with `#if MQTTNET_ENABLED`.
- **Multi-Transport Support**: Auto-detects TCP (`hostname:port`) vs. WebSocket / Secure WebSocket (`ws://` / `wss://` URI).
- **TLS & Certificate Pinning**: Supports enterprise TLS as well as self-signed certificate pinning by thumbprint via `TrustedCertificatePath`, with an optional insecure bypass for local testing.
- **Resilient Auto-Reconnect**: Configurable exponential backoff reconnection policy on accidental disconnects.
- **Main Thread Dispatcher**: Thread-safe message marshalling through `ConcurrentQueue<Action>` with configurable per-frame dispatch limits to protect frame rate.
- **Non-Destructive Runtime Config**: Clones configuration at runtime (`effectiveSettings`) so external JSON configs or Play Mode tweaks never corrupt committed `.asset` files on disk.
- **Modular Game Logic**: Includes feature modules like `MqttLightController` (driving numbered lights 1–4 via MQTT index commands).
- **Unified Logging Bridge**: Integrates seamlessly with ViitorCloud's `Modules.Utility.Utility` colored logger via `MqttUtilityLogBridge`, preserving caller file tags.
- **In-Engine Diagnostics**: Built-in `MqttDebugUI` and `MqttConnectionPanelUI` providing live connection status, latency/timestamps, topic history, and manual endpoint controls.

---

## Architecture & Folder Structure

```
Assets/Games/DigitalTwin-futurotec/
├── README.md                              # This file
├── Scenes/
│   └── Boot.unity                         # Main bootstrap scene with MqttManager and debug canvas
├── Scripts/
│   ├── Lights/
│   │   └── MqttLightController.cs         # Maps MQTT topics (on-light/off-light) to scene Light GameObjects
│   ├── Mqtt/                              # Core MQTT Module (DigitalTwinFuturotec.Mqtt.asmdef)
│   │   ├── DigitalTwinFuturotec.Mqtt.asmdef
│   │   ├── IMqttClientService.cs          # Transport-agnostic MQTT service interface
│   │   ├── MqttBrokerSettings.cs          # ScriptableObject schema for broker & topic settings
│   │   ├── MqttClientService.cs           # MQTTnet 4.x implementation (TCP/WSS/TLS/Reconnect)
│   │   ├── MqttConnectionPanelUI.cs       # Connect/disconnect buttons & manual settings form
│   │   ├── MqttConnectionState.cs         # Connection state enum
│   │   ├── MqttDebugUI.cs                 # Live on-screen telemetry and message stream display
│   │   ├── MqttLog.cs                     # Log routing abstraction
│   │   ├── MqttManager.cs                 # Main thread coordinator, singleton MonoBehaviour
│   │   ├── MqttMessage.cs                 # Immutable message DTO (topic, payload, QoS, retain)
│   │   ├── MqttQualityOfService.cs        # QoS enum (AtMostOnce, AtLeastOnce, ExactlyOnce)
│   │   ├── MqttRuntimeConfig.cs           # JSON serialization schema for runtime overrides
│   │   ├── MqttRuntimeConfigLoader.cs     # Cross-platform UnityWebRequest JSON loader
│   │   ├── MqttTopicMatcher.cs            # MQTT wildcard (+, #) matching utility
│   │   └── MqttTopicSubscription.cs       # Configurable topic subscription definition
│   └── MqttUtilityLogBridge.cs            # Bridges MqttLog to Assembly-CSharp Modules.Utility.Utility
├── Settings/
│   └── MqttBrokerSettings.asset           # Default ScriptableObject configuration asset
└── docs/                                  # Project documentation
    ├── ai_handoff.md                      # Session handoff notes and architectural decisions
    ├── architecture.md                    # Deep-dive architecture and design details
    ├── decisions.md                       # Architectural decision records (ADRs)
    ├── project-overview.md                # High-level overview
    ├── roadmap.md                         # Milestones and upcoming roadmap
    └── tasks.md                           # Progress and task tracking
```

---

## Prerequisites & Dependencies

1. **Unity Editor**: 2021.3+ / 2022.3+ (Desktop Standalone, Android, or iOS; WebGL is not supported due to raw socket constraints).
2. **MQTTnet**: Version `4.3.7.1207` precompiled DLL placed in project plugins.
3. **Newtonsoft.Json**: `com.unity.nuget.newtonsoft-json` (Package Manager).
4. **TextMesh Pro**: `com.unity.ugui` with TMP Essential Resources imported.
5. **Scripting Define Symbols**: Ensure `MQTTNET_ENABLED` is added to Player Settings (`Standalone`, `Android`, and `iOS`).

---

## Configuration

The project uses a hybrid configuration model:

### 1. ScriptableObject (`MqttBrokerSettings.asset`)
Located at `Settings/MqttBrokerSettings.asset`. Holds baseline defaults (broker host/URI, port, TLS toggle, reconnect delays, keep-alive interval, and default subscription topics). Holds **no hardcoded credentials**.

### 2. Runtime JSON (`MqttConfig.json`)
Located at `Assets/StreamingAssets/DigitalTwinFuturotec/MqttConfig.json` (at the Unity project root). Overrides ScriptableObject properties dynamically at startup and supplies authentication credentials:

```json
{
  "BrokerHost": "192.168.1.28",
  "BrokerPort": 8883,
  "UseTls": true,
  "AllowUntrustedCertificates": true,
  "TrustedCertificatePath": "certs/server.crt",
  "ClientIdPrefix": "DigitalTwin_Unity",
  "KeepAliveSeconds": 60,
  "AutoReconnect": true,
  "ReconnectInitialDelaySeconds": 2.0,
  "ReconnectMaxDelaySeconds": 30.0,
  "Username": "tester",
  "Password": "test-pass-123",
  "Topics": [
    {
      "Topic": "on-light",
      "QualityOfService": "AtLeastOnce",
      "SubscribeOnConnect": true
    },
    {
      "Topic": "off-light",
      "QualityOfService": "AtLeastOnce",
      "SubscribeOnConnect": true
    }
  ]
}
```

### 3. Latest-Edit-Wins Priority
In the Unity Editor, `MqttManager` checks the last-modified timestamp between `MqttBrokerSettings.asset` and `MqttConfig.json`. If you make edits directly in the Inspector and save the asset, Inspector values take precedence over stale JSON files.

---

## Getting Started

1. Open the project in Unity.
2. Ensure the `MQTTNET_ENABLED` define symbol is active in **Project Settings > Player > Scripting Define Symbols**.
3. Open `Scenes/Boot.unity`.
4. Adjust your broker settings in `Assets/StreamingAssets/DigitalTwinFuturotec/MqttConfig.json` or through the `MqttBrokerSettings.asset` Inspector.
5. Press **Play**:
   - `MqttManager` initializes, loads the configuration, and connects automatically.
   - The on-screen `MqttDebugCanvas` displays real-time connection state and topic feeds.
   - When connected, `LightsRoot` activates in the hierarchy to begin accepting light commands.

---

## MQTT Topic Reference & Payloads

| Topic | Payload Format | Description |
|---|---|---|
| `on-light` | `"1"`, `"2"`, `"3"`, or `"4"` | Turns ON the scene light at the specified 1-based index (`Light1`..`Light4`). |
| `off-light` | `"1"`, `"2"`, `"3"`, or `"4"` | Turns OFF the scene light at the specified 1-based index (`Light1`..`Light4`). |
| `qrScanned-biocon-house` | JSON / Text payload | Telemetry event triggered when a visitor QR code is scanned. |
| `visitorCreated-biocon-house`| JSON / Text payload | Event triggered when a new visitor record is created in the twin. |

---

## Code Example: Subscribing to Topics

To consume MQTT messages in your own game components:

```csharp
using UnityEngine;
using DigitalTwinFuturotec.Mqtt;

public class MySensorListener : MonoBehaviour
{
    private void Start()
    {
        // Subscribe to a topic or wildcard filter
        MqttManager.Instance.Subscribe("sensors/temperature/+", OnTemperatureReceived);
    }

    private void OnDestroy()
    {
        if (MqttManager.Instance != null)
        {
            MqttManager.Instance.Unsubscribe("sensors/temperature/+", OnTemperatureReceived);
        }
    }

    private void OnTemperatureReceived(MqttMessage message)
    {
        string payload = message.PayloadString;
        Debug.Log($"Received on {message.Topic}: {payload}");
    }
}
```

---

## Documentation Index

For detailed design specifications, architectural decisions, and setup logs, refer to the `docs/` directory:

- [Project Overview](file:///d:/Unity/DigitalTwin-futurotec-base-project/Assets/Games/DigitalTwin-futurotec/docs/project-overview.md) - High-level goals and template integration.
- [Architecture](file:///d:/Unity/DigitalTwin-futurotec-base-project/Assets/Games/DigitalTwin-futurotec/docs/architecture.md) - Deep dive into MQTT networking, main-thread synchronization, TLS, and UI layers.
- [Decisions (ADRs)](file:///d:/Unity/DigitalTwin-futurotec-base-project/Assets/Games/DigitalTwin-futurotec/docs/decisions.md) - Decision logs (MQTTnet choice, non-destructive runtime cloning, symbol definitions).
- [Roadmap](file:///d:/Unity/DigitalTwin-futurotec-base-project/Assets/Games/DigitalTwin-futurotec/docs/roadmap.md) - Project roadmap and upcoming milestones.
- [Tasks](file:///d:/Unity/DigitalTwin-futurotec-base-project/Assets/Games/DigitalTwin-futurotec/docs/tasks.md) - Completed and pending task breakdown.
- [AI Handoff](file:///d:/Unity/DigitalTwin-futurotec-base-project/Assets/Games/DigitalTwin-futurotec/docs/ai_handoff.md) - Complete developer handoff briefing.
