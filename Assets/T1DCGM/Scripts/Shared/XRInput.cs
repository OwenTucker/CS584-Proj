using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using InputDevice = UnityEngine.XR.InputDevice;

namespace T1DCGM
{
    /// <summary>Small helpers for reading controller buttons and sending haptics without extra input assets.</summary>
    public static class XRInput
    {
        /// <summary>Button action with any number of bindings, e.g. "&lt;XRController&gt;{RightHand}/primaryButton".</summary>
        public static InputAction Button(string name, params string[] bindings)
        {
            var action = new InputAction(name, InputActionType.Button);
            foreach (var b in bindings)
                action.AddBinding(b);
            return action;
        }

        public static void Haptic(XRNode node, float amplitude, float seconds)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            if (device.isValid)
                device.SendHapticImpulse(0u, Mathf.Clamp01(amplitude), seconds);
        }

        /// <summary>Finds a transform anywhere in the loaded scenes by exact name.</summary>
        public static Transform FindByName(string name)
        {
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (t.name == name)
                    return t;
            return null;
        }
    }
}
