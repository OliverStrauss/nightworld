using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine.InputSystem;

namespace NightWorld.Input
{
    /// <summary>
    /// The one place that defines every input action. Builds the "player" InputActionMap from a
    /// <see cref="ControlSettings"/> asset. Each binding gets a stable id (see <see cref="StableBindingId"/>),
    /// so saved binding overrides (InputActionMap.LoadBindingOverridesFromJson) still match after a restart.
    /// Plain C# object: whoever creates it calls Enable() and, when done, Dispose().
    /// </summary>
    public sealed class GameControls : IDisposable
    {
        public const string MapName = "player";
        public const string MoveName = "Move";
        public const string LookName = "Look";
        public const string InventoryName = "Inventory";
        public const string MenuName = "Menu";

        public InputActionMap Map { get; }
        public InputAction Move { get; }
        public InputAction Look { get; }
        public InputAction Inventory { get; }
        public InputAction Menu { get; }

        public GameControls(ControlSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            Map = new InputActionMap(MapName);
            Move = AddAction(MoveName, InputActionType.Value, "Vector2", settings.move);
            Look = AddAction(LookName, InputActionType.Value, "Vector2", settings.look);
            Inventory = AddAction(InventoryName, InputActionType.Button, "Button", settings.inventory);
            Menu = AddAction(MenuName, InputActionType.Button, "Button", settings.menu);
        }

        public void Enable() => Map.Enable();

        public void Disable() => Map.Disable();

        public void Dispose() => Map.Dispose();

        /// <summary>The id of the binding at <paramref name="bindingIndex"/> in the action's bindings
        /// (composite parts count as bindings). Same inputs always give the same Guid.</summary>
        public static Guid StableBindingId(string actionName, int bindingIndex)
        {
            byte[] key = Encoding.UTF8.GetBytes(MapName + "/" + actionName + "/" + bindingIndex);
            using (var md5 = MD5.Create())
            {
                return new Guid(md5.ComputeHash(key));
            }
        }

        private InputAction AddAction(string name, InputActionType type, string layout, List<ControlBinding> bindings)
        {
            var action = Map.AddAction(name, type, expectedControlLayout: layout);

            if (bindings != null)
            {
                foreach (var binding in bindings)
                {
                    if (binding == null) continue;
                    if (binding.kind == ControlBindingKind.Vector2Composite)
                    {
                        action.AddCompositeBinding("2DVector")
                            .With("Up", binding.up)
                            .With("Down", binding.down)
                            .With("Left", binding.left)
                            .With("Right", binding.right);
                    }
                    else if (!string.IsNullOrEmpty(binding.path))
                    {
                        action.AddBinding(binding.path);
                    }
                }
            }

            // Bindings made from code get random ids every run; swap them for stable ones.
            for (int i = 0; i < action.bindings.Count; i++)
            {
                InputBinding withId = action.bindings[i];
                withId.id = StableBindingId(name, i);
                action.ChangeBinding(i).To(withId);
            }

            return action;
        }
    }
}
