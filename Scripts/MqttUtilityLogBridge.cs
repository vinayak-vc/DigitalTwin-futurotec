using UnityEngine;

using ViitorCloud.Games.DigitalTwinFuturotec.Mqtt;

namespace ViitorCloud.Games.DigitalTwinFuturotec {
    /// <summary>
    /// Wires MqttLog (inside DigitalTwinFuturotec.Mqtt.asmdef, which cannot
    /// reference Assembly-CSharp) to Modules.Utility.Utility's colored logger.
    /// Deliberately placed outside that asmdef's folder so this file itself
    /// compiles into Assembly-CSharp, where both namespaces are visible.
    /// RuntimeInitializeOnLoadMethod guarantees this runs before any scene's
    /// Awake, so it is wired before MqttManager ever logs anything.
    ///
    /// Modules.Utility.Utility is referenced by its fully-qualified name
    /// (not a "using Modules.Utility;" + bare "Utility") because this file's
    /// own namespace nests under ViitorCloud, and a sibling ViitorCloud.Utility
    /// namespace also exists elsewhere in the base project - C# resolves an
    /// unqualified "Utility" against an enclosing namespace before it ever
    /// consults a using-directive, so a bare reference would silently resolve
    /// to the wrong one.
    /// </summary>
    internal static class MqttUtilityLogBridge {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install() {
            // callerFilePath is forwarded explicitly (not left to Utility.Log's own
            // [CallerFilePath] default) so the resulting log tag names the file that
            // actually called MqttLog, not this bridge file.
            MqttLog.InfoHandler = (message, context, callerFilePath) => Modules.Utility.Utility.Log(message, context, callerFilePath);
            MqttLog.WarningHandler = (message, context, callerFilePath) => Modules.Utility.Utility.LogWarning(message, context, callerFilePath);
            MqttLog.ErrorHandler = (message, context, callerFilePath) => Modules.Utility.Utility.LogError(message, context, callerFilePath);
        }
    }
}
