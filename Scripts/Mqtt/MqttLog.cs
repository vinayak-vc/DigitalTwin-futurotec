using System;
using System.Runtime.CompilerServices;

using UnityEngine;

namespace ViitorCloud.Games.DigitalTwinFuturotec.Mqtt {
    /// <summary>
    /// Logging indirection for this assembly. DigitalTwinFuturotec.Mqtt.asmdef
    /// compiles before Unity's default Assembly-CSharp, so it can never directly
    /// reference Modules.Utility.Utility (which has no asmdef of its own and
    /// therefore lives in Assembly-CSharp) - that's a one-way Unity compilation
    /// order rule. MqttUtilityLogBridge (Scripts/MqttUtilityLogBridge.cs, one
    /// folder up, outside this asmdef, so it *does* compile into Assembly-CSharp)
    /// wires the handlers below to Utility.Log/LogWarning/LogError on startup.
    /// Falls back to Debug.Log/LogWarning/LogError if that bridge never runs, so
    /// logging never silently disappears.
    ///
    /// callerFilePath is captured here (via [CallerFilePath], defaulted so every
    /// call site in this assembly gets it for free) rather than left to
    /// Utility.Log's own [CallerFilePath] parameter - that attribute resolves at
    /// its *own* call site, which would always be MqttUtilityLogBridge.cs if left
    /// to default, mislabeling every MQTT log under one file instead of the
    /// actual caller (MqttManager.cs, MqttClientService.cs, ...).
    /// </summary>
    public static class MqttLog {
        public static Action<string, UnityEngine.Object, string> InfoHandler;
        public static Action<string, UnityEngine.Object, string> WarningHandler;
        public static Action<object, UnityEngine.Object, string> ErrorHandler;

        public static void Info(string message, UnityEngine.Object context = null, [CallerFilePath] string callerFilePath = "") {
            if (InfoHandler != null) {
                InfoHandler(message, context, callerFilePath);
            } else {
                Debug.Log(message, context);
            }
        }

        public static void Warning(string message, UnityEngine.Object context = null, [CallerFilePath] string callerFilePath = "") {
            if (WarningHandler != null) {
                WarningHandler(message, context, callerFilePath);
            } else {
                Debug.LogWarning(message, context);
            }
        }

        public static void Error(object message, UnityEngine.Object context = null, [CallerFilePath] string callerFilePath = "") {
            if (ErrorHandler != null) {
                ErrorHandler(message, context, callerFilePath);
            } else {
                Debug.LogError(message, context);
            }
        }
    }
}
