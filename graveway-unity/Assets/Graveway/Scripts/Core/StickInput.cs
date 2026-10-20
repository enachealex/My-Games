// Movement input, ported from graveway/src/input.js. On a phone you press
// anywhere on the play area and drag: a joystick appears under your thumb.
// Any number of fingers can be down. The first one on the play area steers;
// if it lifts while another is still down, that one takes over the stick
// where it is. Touches that start on a button don't steer.
// In the editor, the mouse acts as one finger and WASD / arrows also work.
//
// All input goes through here and GameKeys, on the Input System package
// (the project's Active Input Handling is "Input System Package (New)", so
// UnityEngine.Input must not be used anywhere).

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace Graveway
{
    public sealed class StickInput
    {
        const float Deadzone = 0.12f;
        const int MouseId = -100;

        public bool enabled;
        public Func<Vector2, bool> overUi = _ => false;   // set by the HUD: is this screen point on a button?

        // Set to steer without touching anything (tests, demos). x across the
        // road, z up the road, length up to 1.
        public Vector3? autopilot;

        public bool Active => pointer != int.MinValue;
        public Vector2 Origin { get; private set; }       // screen pixels
        public Vector2 Knob { get; private set; }         // offset from Origin, clamped to Radius
        public float Radius => Mathf.Max(48, (Screen.dpi > 0 ? Screen.dpi : 320) * 0.36f);

        Vector2 stick;
        int pointer = int.MinValue;
        readonly List<int> order = new List<int>();                    // fingers on the play area, oldest first
        readonly Dictionary<int, Vector2> fingers = new Dictionary<int, Vector2>();

        public StickInput()
        {
            if (!EnhancedTouchSupport.enabled) EnhancedTouchSupport.Enable();
        }

        public void Update()
        {
            if (!enabled) { Release(); return; }
            var touches = Touch.activeTouches;
            for (int i = 0; i < touches.Count; i++)
            {
                var t = touches[i];
                Handle(t.touchId, t.phase, t.screenPosition);
            }
            var mouse = Mouse.current;
            if (touches.Count == 0 && mouse != null)
            {
                // Mouse as a finger (editor and desktop).
                var pos = mouse.position.ReadValue();
                if (mouse.leftButton.wasPressedThisFrame) Handle(MouseId, TouchPhase.Began, pos);
                else if (mouse.leftButton.wasReleasedThisFrame) Handle(MouseId, TouchPhase.Ended, pos);
                else if (mouse.leftButton.isPressed) Handle(MouseId, TouchPhase.Moved, pos);
            }
        }

        void Handle(int id, TouchPhase phase, Vector2 pos)
        {
            switch (phase)
            {
                case TouchPhase.Began:
                    if (overUi(pos)) return;                      // a button press, not steering
                    fingers[id] = pos;
                    order.Remove(id); order.Add(id);
                    if (!Active) Grab(id, pos);
                    break;
                case TouchPhase.Moved:
                case TouchPhase.Stationary:
                    if (!fingers.ContainsKey(id)) return;
                    fingers[id] = pos;
                    if (id == pointer) Steer(pos);
                    break;
                case TouchPhase.Ended:
                case TouchPhase.Canceled:
                    if (!fingers.Remove(id)) return;
                    order.Remove(id);
                    if (id != pointer) return;
                    pointer = int.MinValue;
                    stick = Vector2.zero;
                    // Hand the stick to the finger that went down most recently.
                    if (order.Count > 0) { int next = order[order.Count - 1]; Grab(next, fingers[next]); }
                    break;
            }
        }

        void Grab(int id, Vector2 pos)
        {
            pointer = id;
            Origin = pos;
            Knob = Vector2.zero;
            stick = Vector2.zero;
        }

        void Steer(Vector2 pos)
        {
            var d = pos - Origin;
            float r = Radius, len = d.magnitude;
            // Past the rim the base follows the thumb, so changing direction
            // never means dragging all the way back across the circle.
            if (len > r * 1.35f)
            {
                Origin += d * ((len - r * 1.35f) / len);
                d = pos - Origin;
                len = d.magnitude;
            }
            var n = len > 0 ? d / len : Vector2.zero;
            float clamped = Mathf.Min(len, r);
            Knob = n * clamped;
            float mag = clamped / r;
            float m = mag < Deadzone ? 0 : (mag - Deadzone) / (1 - Deadzone);
            stick = n * m;
        }

        public void Release()
        {
            pointer = int.MinValue;
            fingers.Clear();
            order.Clear();
            stick = Vector2.zero;
            Knob = Vector2.zero;
        }

        // World-space direction: x across the road, z up the road.
        public Vector3 Move
        {
            get
            {
                if (!enabled) return Vector3.zero;
                if (autopilot.HasValue) return Vector3.ClampMagnitude(autopilot.Value, 1);
                float kx = (GameKeys.Held(Key.D) || GameKeys.Held(Key.RightArrow) ? 1 : 0) - (GameKeys.Held(Key.A) || GameKeys.Held(Key.LeftArrow) ? 1 : 0);
                float kz = (GameKeys.Held(Key.W) || GameKeys.Held(Key.UpArrow) ? 1 : 0) - (GameKeys.Held(Key.S) || GameKeys.Held(Key.DownArrow) ? 1 : 0);
                if (kx != 0 || kz != 0) return new Vector3(kx, 0, kz).normalized;
                return new Vector3(stick.x, 0, stick.y);
            }
        }
    }

    // Keyboard shortcuts (editor and desktop), and the Android back button,
    // which the Input System reports as Escape.
    public static class GameKeys
    {
        public static bool Down(Key k)
        {
            var kb = Keyboard.current;
            return kb != null && kb[k].wasPressedThisFrame;
        }

        public static bool Held(Key k)
        {
            var kb = Keyboard.current;
            return kb != null && kb[k].isPressed;
        }

        public static bool Back => Down(Key.Escape);
    }
}
