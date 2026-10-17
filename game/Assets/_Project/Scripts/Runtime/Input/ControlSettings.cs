using System;
using System.Collections.Generic;
using UnityEngine;

namespace NightWorld.Input
{
    /// <summary>What kind of control a <see cref="ControlBinding"/> describes.</summary>
    public enum ControlBindingKind
    {
        /// <summary>One control path, e.g. &lt;Keyboard&gt;/i or &lt;Gamepad&gt;/leftStick.</summary>
        Path = 0,

        /// <summary>A 2D-vector composite built from four buttons (up, down, left, right).</summary>
        Vector2Composite = 1,
    }

    /// <summary>One default binding for an action. Plain bindings use <see cref="path"/>;
    /// 2D-vector composites use <see cref="up"/>, <see cref="down"/>, <see cref="left"/> and <see cref="right"/>.</summary>
    [Serializable]
    public class ControlBinding
    {
        public ControlBindingKind kind = ControlBindingKind.Path;

        [Tooltip("Input System control path for a Path binding, e.g. the I key on the keyboard. Unused by composites.")]
        public string path = "";

        [Tooltip("Composite parts. Unused by Path bindings.")]
        public string up = "";
        public string down = "";
        public string left = "";
        public string right = "";
    }

    /// <summary>
    /// The default keys and buttons for every action the game listens to. One asset lives at
    /// Data/control-settings.asset and game-config points at it. <see cref="GameControls"/> builds the
    /// "player" action map from it; a settings menu can list these bindings.
    /// </summary>
    [CreateAssetMenu(menuName = "NightWorld/Control Settings", fileName = "control-settings")]
    public class ControlSettings : ScriptableObject
    {
        [Tooltip("Walk. Vector2.")]
        public List<ControlBinding> move = new List<ControlBinding>();

        [Tooltip("Turn the camera. Vector2.")]
        public List<ControlBinding> look = new List<ControlBinding>();

        [Tooltip("Open the inventory. Button.")]
        public List<ControlBinding> inventory = new List<ControlBinding>();

        [Tooltip("Open the menu / free the cursor. Button.")]
        public List<ControlBinding> menu = new List<ControlBinding>();
    }
}
