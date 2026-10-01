using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EscapeAnalytics.Gameplay
{
    /// <summary>
    /// Lectura mínima de teclado que funciona tanto con el Input System nuevo
    /// (default en Unity 6) como con el Input Manager clásico.
    /// Cuando integres el movimiento del jugador puedes reemplazar esto por tus Input Actions.
    /// </summary>
    public static class InputCompat
    {
        public const int HotbarKeys = 8;

#if ENABLE_INPUT_SYSTEM
        static readonly Key[] DigitKeys =
        {
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4,
            Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8
        };
#endif

        /// <summary>Tecla E (recoger / interactuar).</summary>
        public static bool InteractPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.E);
#else
            return false;
#endif
        }

        /// <summary>Devuelve 0..7 si se presionó una tecla 1..8 este frame; si no, -1.</summary>
        public static int PressedHotbarIndex()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return -1;
            for (int i = 0; i < DigitKeys.Length; i++)
                if (kb[DigitKeys[i]].wasPressedThisFrame) return i;
            return -1;
#elif ENABLE_LEGACY_INPUT_MANAGER
            for (int i = 0; i < HotbarKeys; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) return i;
            return -1;
#else
            return -1;
#endif
        }
    }
}
